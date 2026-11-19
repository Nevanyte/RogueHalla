//! The control surface's entry points on the director: create, admit, list, leave and end.

use super::*;

impl Director {
    /// `POST /control/session`: the room for one game session. Idempotent per `gameSessionId`.
    pub fn control_create(
        &self,
        gsid: &str,
        max_players: Option<usize>,
        fill_bots_to: Option<usize>,
    ) -> Result<serde_json::Value, CtlError> {
        let capacity = if self.cfg.sim.training {
            1
        } else {
            rh_sim::constants::MAX_PLAYERS
        };
        let max_players = max_players.unwrap_or(capacity);
        if self.cfg.sim.training && (max_players != 1 || fill_bots_to.is_some_and(|n| n != 0)) {
            return Err(CtlError::bad_request(
                "training requires maxPlayers 1 and fillBotsTo 0",
            ));
        }
        if !(1..=rh_sim::constants::MAX_PLAYERS).contains(&max_players) {
            return Err(CtlError::bad_request(format!(
                "maxPlayers must be 1..={}",
                rh_sim::constants::MAX_PLAYERS
            )));
        }
        let fill = fill_bots_to.unwrap_or(self.cfg.fill_bots_to.min(max_players));
        if fill > max_players {
            return Err(CtlError::bad_request("fillBotsTo must be 0..=maxPlayers"));
        }
        match self.lifecycle.state_name() {
            "starting" => return Err(CtlError::new(503, "not_ready", "the server is starting")),
            "ready" => {}
            _ => return Err(CtlError::conflict("draining", "the server is draining")),
        }
        let mut prepared = None;
        loop {
            let mut inner = self.inner.lock_recover();
            // Re-checked under the lock: `drain` flips the lifecycle first and only then reads the rooms under this same lock, so
            // a create that still sees `ready` here has its room in place before drain looks (drain then waits for it), and a
            // create that comes later is refused. Without this a session created during a drain could outlive `Terminated`.
            match self.lifecycle.state_name() {
                "ready" => {}
                "starting" => {
                    return Err(CtlError::new(503, "not_ready", "the server is starting"))
                }
                _ => return Err(CtlError::conflict("draining", "the server is draining")),
            }
            inner.control.sweep(tokio::time::Instant::now());
            match inner.control.check_create(gsid, max_players, fill)? {
                crate::control::CreateCheck::Existing {
                    room,
                    phase,
                    max_players,
                } => {
                    return Ok(serde_json::json!({
                        "v": crate::control::API_VERSION, "created": false, "gameSessionId": gsid,
                        "roomId": crate::control::room_id_hex(room), "phase": phase.as_str(), "maxPlayers": max_players,
                    }))
                }
                crate::control::CreateCheck::New => {
                    if inner.rooms.len() >= self.cfg.max_rooms {
                        return Err(CtlError::conflict("at_capacity", "no room capacity left"));
                    }
                    let (id, _tx) = if let Some(room) = prepared.take() {
                        self.activate_room(&mut inner, room)
                    } else if self.needs_board_room() {
                        drop(inner);
                        prepared = Some(self.prepare_board_room(fill).map_err(|error| {
                            crate::api::log_internal("quest map preparation", &error);
                            CtlError::new(
                                503,
                                "quest_map_unavailable",
                                "quest map unavailable; try again shortly",
                            )
                        })?);
                        continue;
                    } else {
                        self.spawn_room(&mut inner, fill)
                    };
                    inner.control.insert(gsid, id, max_players, fill);
                    return Ok(serde_json::json!({
                        "v": crate::control::API_VERSION, "created": true, "gameSessionId": gsid,
                        "roomId": crate::control::room_id_hex(id), "phase": "lobby", "maxPlayers": max_players,
                    }));
                }
            }
        }
    }

    /// `POST /control/admit`.
    pub fn control_admit(
        &self,
        gsid: &str,
        psid: &str,
        callsign: &str,
        ttl: Option<Duration>,
    ) -> Result<serde_json::Value, CtlError> {
        let draining = self.lifecycle.draining();
        let mut inner = self.inner.lock_recover();
        let ttl = ttl.unwrap_or(self.cfg.admit_ttl);
        let o = inner.control.admit(
            gsid,
            psid,
            callsign,
            ttl,
            tokio::time::Instant::now(),
            draining,
        )?;
        Ok(serde_json::json!({
            "v": crate::control::API_VERSION, "state": o.state, "gameSessionId": o.game_session_id,
            "playerSessionId": o.player_session_id, "callsign": o.callsign, "expiresInMs": o.expires_in_ms,
        }))
    }

    /// `GET /control/rooms`.
    pub fn control_rooms(&self) -> serde_json::Value {
        let mut inner = self.inner.lock_recover();
        let rooms = inner.control.rooms_json(tokio::time::Instant::now());
        serde_json::json!({
            "v": crate::control::API_VERSION,
            "state": self.lifecycle.state_name(),
            "draining": self.lifecycle.draining(),
            "maxRooms": self.cfg.max_rooms,
            "rooms": rooms,
        })
    }

    /// `POST /control/leave`. A kick marks the player `left` (reason `kicked`) at once and tells the room, which removes the
    /// seat through the same path as a disconnect past the grace.
    pub async fn control_leave(
        &self,
        gsid: &str,
        psid: &str,
        mode: LeaveMode,
    ) -> Result<serde_json::Value, CtlError> {
        // A kick must reach the room or not happen at all: room-mailbox space is reserved BEFORE the book is touched, so a
        // full mailbox answers 503 with the player still as it was (the bridge retries), never `left` with the room unaware.
        let tx = if mode == LeaveMode::Kick {
            let inner = self.inner.lock_recover();
            inner
                .control
                .room_of(gsid)
                .and_then(|room| inner.rooms.get(&room).map(|r| r.tx.clone()))
        } else {
            None
        };
        let permit = match &tx {
            Some(tx) => reserve_room_slot(tx, Duration::from_secs(2)).await?,
            None => None,
        };
        let out = {
            let mut inner = self.inner.lock_recover();
            inner
                .control
                .leave(gsid, psid, mode, tokio::time::Instant::now())?
        };
        if let (Some((_, profile)), Some(permit)) = (out.kick, permit) {
            permit.send(RoomCmd::Kick { profile });
        }
        Ok(serde_json::json!({"v": crate::control::API_VERSION, "state": out.state}))
    }

    /// `POST /control/end`. `abort` waits (bounded) for the room to report closed so the answer says so.
    pub async fn control_end(
        &self,
        gsid: &str,
        mode: EndMode,
    ) -> Result<serde_json::Value, CtlError> {
        // Same rule as `control_leave`: the room's mailbox slot is reserved before the book changes, so a room that cannot
        // be told is a 503 and the session is not marked ending.
        let tx = {
            let inner = self.inner.lock_recover();
            inner
                .control
                .room_of(gsid)
                .and_then(|room| inner.rooms.get(&room).map(|r| r.tx.clone()))
        };
        let permit = match &tx {
            Some(tx) => reserve_room_slot(tx, Duration::from_secs(2)).await?,
            None => None,
        };
        let out = {
            let mut inner = self.inner.lock_recover();
            inner.control.end(gsid, mode, tokio::time::Instant::now())?
        };
        if let (Some(m), Some(permit)) = (out.tell_room, permit) {
            permit.send(match m {
                EndMode::Graceful => RoomCmd::Drain,
                EndMode::Abort => RoomCmd::Abort,
            });
        }
        if out.tell_room == Some(EndMode::Abort) {
            let until = tokio::time::Instant::now() + Duration::from_secs(2);
            while tokio::time::Instant::now() < until {
                let closed = self
                    .inner
                    .lock_recover()
                    .control
                    .phase_of(gsid)
                    .is_none_or(|p| p == crate::control::CPhase::Closed);
                if closed {
                    break;
                }
                tokio::time::sleep(Duration::from_millis(5)).await;
            }
        }
        let phase = self
            .inner
            .lock_recover()
            .control
            .phase_of(gsid)
            .unwrap_or(out.phase);
        Ok(serde_json::json!({"v": crate::control::API_VERSION, "phase": phase.as_str()}))
    }
}
