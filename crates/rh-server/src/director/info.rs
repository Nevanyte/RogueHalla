//! Draining the director and its read-only views (`/info`, lobby view, control info, crash records).

use super::*;

impl Director {
    /// Stop admitting; active matches finish; the lifecycle reports Terminated when the last room closes.
    pub async fn drain(&self) {
        self.lifecycle.start_draining();
        let txs: Vec<_> = self
            .inner
            .lock_recover()
            .rooms
            .values()
            .map(|r| r.tx.clone())
            .collect();
        if txs.is_empty() {
            self.lifecycle.rooms_empty();
        }
        // One detached send per room: a wedged room whose mailbox is full must not hold up the others, nor the caller (and
        // with it the drain cap's clock). The send ends when the room takes the command or is gone.
        for tx in txs {
            tokio::spawn(async move {
                let _ = tx.send(RoomCmd::Drain).await;
            });
        }
    }

    /// The public `/info`: what a client, a deploy check or a probe needs (protocol, content hash, the coarse lifecycle state)
    /// and nothing about load. The full [`Director::info`] (rooms, players, results, closed rooms) is served on the loopback
    /// control listener only (`GET /control/info`).
    pub fn public_info(&self) -> serde_json::Value {
        serde_json::json!({
            "protocol": PROTOCOL_VERSION,
            "contentHash": format!("{:016x}", rh_sim::content::content_hash()),
            "state": self.lifecycle.state_name(),
        })
    }

    /// `drain`, then, when `max` is set and the rooms have not all closed in time, end the rest gracefully: each remaining room
    /// closes its seats out ([`RoomCmd::DrainEnd`]: results are kept, not invented or dropped) and the lifecycle terminates.
    /// Returns true when the cap, not the matches, ended the drain.
    ///
    /// `max` is ONE deadline measured from the call, covering everything here: the matches get `max` minus a reserve
    /// (`max / 6`, at most [`DRAIN_END_GRACE`]) to finish, the reserve is for the `DrainEnd` round, and at `max` the lifecycle is
    /// forced to terminated whether or not every room answered. Sends never wait (`try_send`): a room whose mailbox is full is
    /// wedged and is abandoned.
    pub async fn drain_within(&self, max: Option<Duration>) -> bool {
        let start = tokio::time::Instant::now();
        self.drain().await;
        let Some(max) = max else {
            return false;
        };
        let wait_until = |limit: tokio::time::Instant| async move {
            while !self.lifecycle.is_terminated() && tokio::time::Instant::now() < limit {
                tokio::time::sleep(Duration::from_millis(100)).await;
            }
        };
        let reserve = (max / 6).min(DRAIN_END_GRACE);
        wait_until(start + max - reserve).await;
        if self.lifecycle.is_terminated() {
            return false;
        }
        let txs: Vec<_> = self
            .inner
            .lock_recover()
            .rooms
            .values()
            .map(|r| r.tx.clone())
            .collect();
        eprintln!(
            "[server] WARN event=drain_cap_reached max_secs={} rooms_remaining={} action=ending_rooms_gracefully",
            max.as_secs(),
            txs.len()
        );
        for tx in &txs {
            let _ = tx.try_send(RoomCmd::DrainEnd);
        }
        wait_until(start + max).await;
        if !self.lifecycle.is_terminated() {
            eprintln!(
                "[server] ERROR event=drain_rooms_unresponsive action=forcing_termination rooms_remaining={}",
                self.room_count()
            );
            self.lifecycle.rooms_empty();
        }
        true
    }

    /// The operator view (`GET /control/info`, loopback): the public identity plus room, player and result counters.
    pub fn info(&self) -> serde_json::Value {
        let inner = self.inner.lock_recover();
        let lobbies = inner
            .rooms
            .values()
            .filter(|r| r.phase == Phase::Lobby)
            .count();
        serde_json::json!({
            "protocol": PROTOCOL_VERSION,
            "contentHash": format!("{:016x}", rh_sim::content::content_hash()),
            "rooms": inner.rooms.len(),
            "lobbies": lobbies,
            "maxRooms": self.cfg.max_rooms,
            "players": inner.profiles.len(),
            "state": self.lifecycle.state_name(),
            "resultsOk": inner.results_ok,
            "resultsFailed": inner.results_failed,
            "resultsRefused": inner.results_refused,
            "closedRooms": inner.closed.iter().rev().take(8).collect::<Vec<_>>(),
        })
    }

    /// The public lobby endpoint's copy of the rooms (P-LOBBY-ACTIVITY-1): plain data, copied under the lock. A training
    /// server's rooms are private runs (legacy lists only non-training rooms): their players only count as seated.
    ///
    /// Players are the seated humans (`Seat` reports), then the seated bots (`BotSeat` reports), each in arrival order.
    pub fn lobby_view(&self) -> LobbyView {
        let inner = self.inner.lock().unwrap_or_else(|p| p.into_inner());
        let training = self.cfg.sim.training;
        let mut ids: Vec<&MatchId> = inner.rooms.keys().collect();
        ids.sort();
        let mut view = LobbyView {
            room_limit: self.cfg.max_rooms,
            ..LobbyView::default()
        };
        for id in ids {
            let r = &inner.rooms[id];
            view.occupied
                .extend(r.public.humans.iter().map(|h| h.to_ascii_lowercase()));
            if training {
                continue;
            }
            view.rooms.push(LobbyRoomView {
                phase: r.phase,
                started_at_ms: r.public.started_at_ms,
                human_min: r.public.human_min,
                fill_target: r.public.fill_target,
                players: (r.public.humans.iter().map(|n| (n, false)))
                    .chain(r.public.bots.iter().map(|n| (n, true)))
                    .map(|(name, bot)| LobbyPlayerView {
                        name: name.clone(),
                        bot,
                    })
                    .collect(),
            });
        }
        view.room_count = view.rooms.len();
        view.joinable = !training
            && !self.lifecycle.draining()
            && (open_lobby(&inner.rooms, rh_sim::constants::MAX_PLAYERS).is_some()
                || inner.rooms.len() < self.cfg.max_rooms);
        view
    }

    // ---- the control API (docs/hosting/CONTROL_API.md); the HTTP layer is `control_http` in lib.rs ----

    /// The `control` object of `GET /control/health`: what the bridge checks before it reports the process ready.
    pub fn control_info(&self) -> serde_json::Value {
        serde_json::json!({
            "v": crate::control::API_VERSION,
            "requireAdmit": self.cfg.require_admit,
            "maxRooms": self.cfg.max_rooms,
            "admitTtlMs": self.cfg.admit_ttl.as_millis() as u64,
        })
    }

    /// Crashed rooms since this process started and the latest crash's summary: the `roomCrashes` object of
    /// `GET /control/health` (loopback only; deliberately not in the public `/info`). A non-zero `total` is the alert hook.
    pub fn crash_info(&self) -> serde_json::Value {
        let inner = self.inner.lock_recover();
        let last = inner.last_crash.as_ref().map(|c| {
            serde_json::json!({
                "roomId": c.room_id, "atUnixMs": c.at_unix_ms, "ticks": c.ticks, "seatsFreed": c.seats_freed,
            })
        });
        serde_json::json!({"total": inner.rooms_crashed, "last": last})
    }

    pub fn idle_timeout(&self) -> Duration {
        self.cfg.idle_timeout
    }

    pub fn room_count(&self) -> usize {
        self.inner.lock_recover().rooms.len()
    }
}
