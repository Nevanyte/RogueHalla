//! Spawning a room: bot learning and fill policy hooks, the room task itself, and the lease renewal tasks that outlive an admission.

use super::*;

impl Director {
    /// Installs the P-BOTS-5 record caller (the live learning store). The server calls it once at startup; tests install
    /// a probe sink. Learning stays off (the NO_BIAS zeros) when nothing is installed.
    pub fn install_learning(&self, sink: Arc<dyn LearningSink>) {
        *self.learning.lock_recover() = Some(sink);
    }

    /// Installs the read-only profile-name set a new lobby's fill policy consults (nobody profile-owned is ever drawn).
    pub fn set_bot_names(&self, names: Arc<std::collections::HashSet<String>>) {
        *self
            .bot_names
            .write()
            .unwrap_or_else(std::sync::PoisonError::into_inner) = names;
    }

    pub(super) fn bot_names(&self) -> Arc<std::collections::HashSet<String>> {
        self.bot_names
            .read()
            .unwrap_or_else(std::sync::PoisonError::into_inner)
            .clone()
    }

    /// The bias snapshot the next lobby's fill policy reads (refreshed after every recorded run).
    pub(super) fn learning_snapshot(&self) -> Option<rh_policy::bots::learning::LearnedBias> {
        self.learning
            .lock_recover()
            .as_ref()
            .map(|sink| sink.snapshot())
    }

    /// The record caller (source BotManager.onResult): a resolved fill bot's run goes to the learning store with the
    /// facts the room has, the seat policy's own choice keys (a Brain seat's opener/second/spawn/boon/camp; none for
    /// HumanBot) and the manager's faction key.
    pub(super) fn record_bot_result(
        &self,
        spec: &crate::bot_fill::BotSeatSpec,
        result: &rh_sim::RunResult,
        choices: &[String],
    ) {
        let Some(sink) = self.learning.lock_recover().clone() else {
            return;
        };
        sink.record_choices(
            BotRunFacts {
                tier: crate::bot_learning::learning_tier(spec.tier),
                persona: spec.persona,
                faction: spec.faction,
                outcome: BotRunFacts::outcome(result.outcome),
                place: result.place,
                players: result.players,
            },
            choices,
        );
    }

    /// The fill policy a new room gets, with the room's fill count (the config's own fill_to is the default for
    /// direct room construction): None leaves the room on its immediate placeholder fill. This initializes its snapshot;
    /// the live sink installed on the fill state refreshes it again before every actual managed arrival.
    pub(super) fn fill_policy(
        &self,
        fill_bots_to: usize,
    ) -> Option<crate::bot_fill::BotFillConfig> {
        self.cfg.bot_fill.as_ref().map(|fill| {
            let mut config = crate::bot_fill::BotFillConfig {
                fill_to: fill_bots_to,
                ..fill.clone()
            };
            if let Some(learned) = self.learning_snapshot() {
                config.learned = learned;
            }
            config
        })
    }

    /// Spawns a room task and registers it (the lock is held). Used by admission (a lobby opened on demand) and by the
    /// control API (a room per game session); never blocks.
    pub(super) fn spawn_room(
        &self,
        inner: &mut Inner,
        fill_bots_to: usize,
    ) -> (MatchId, mpsc::Sender<RoomCmd>) {
        let id = random16();
        let (tx, rx) = mpsc::channel(INBOUND_CAP);
        let fill = self.fill_policy(fill_bots_to);
        let mut sim = self.cfg.sim.clone();
        if let Some(fill) = &fill {
            fill.apply_lobby_timing(&mut sim);
        }
        let public = PublicRoom {
            human_min: sim.min_players,
            fill_target: fill_bots_to,
            ..PublicRoom::default()
        };
        // A training room never draws from the pool (runtime.rs refuses the combination; this keeps library configs safe).
        let map = match &self.cfg.map_pool {
            Some(pool) if !sim.training => {
                let (map, served) = pool.take_served();
                eprintln!(
                    "[maps] match start: match={} {served}",
                    crate::control::room_id_hex(id)
                );
                map
            }
            _ => self.map.clone(),
        };
        let rc = RoomConfig {
            sim,
            arena: map.arena,
            welcome_map: map.welcome_map,
            fill_bots_to,
            tick: self.cfg.tick,
            record: self.cfg.record,
            fixed_secrets: None,
            fixed_bot_seed: None,
            reconnect_grace: self.cfg.reconnect_grace,
            linger: self.cfg.linger,
        };
        let mut room = Room::new(id, rc, self.reports.clone(), tokio::time::Instant::now());
        if let Some(fill) = fill {
            room = room.with_bot_fill(fill).with_bot_names(self.bot_names());
            if let Some(sink) = self.learning.lock_recover().clone() {
                room = room.with_learning(sink);
            }
        }
        // A panic in the room still reports `Closed` (room.rs `run_room`), so its seats are freed.
        tokio::spawn(run_room(room, rx));
        inner.rooms.insert(
            id,
            RoomEntry {
                tx: tx.clone(),
                phase: Phase::Lobby,
                seated: 0,
                reserved: 0,
                public,
            },
        );
        (id, tx)
    }

    /// There is at most one in-flight renewal per admitted seat. The worker holds neither
    /// the director nor its lock while sleeping or accessing durable storage. Result-pending
    /// and writing seats keep renewing even after their connection and room have gone.
    pub(super) fn start_renewals(
        &self,
        key: ProfileKey,
        gen: u64,
        match_id: MatchId,
        lease: HeldLease,
        started: tokio::time::Instant,
        validity: tokio::sync::watch::Sender<tokio::time::Instant>,
    ) {
        let weak = self.me.clone();
        let store = self.persistence.clone();
        let policy = self.cfg.leases;
        let mut inner = self.inner.lock_recover();
        let Some(owner) = inner.profiles.get_mut(&key).filter(|s| {
            s.gen == gen
                && s.match_id == match_id
                && s.fence == lease.fence
                && s.lease == LeaseState::Held
        }) else {
            return;
        };
        let worker = tokio::spawn(async move {
            use tokio::time::{sleep_until, timeout_at, Instant};
            let current = || {
                weak.upgrade().is_some_and(|d| {
                    d.inner.lock_recover().profiles.get(&key).is_some_and(|s| {
                        s.gen == gen
                            && s.match_id == match_id
                            && s.fence == lease.fence
                            && s.lease == LeaseState::Held
                    })
                })
            };
            let mut deadline = started + policy.ttl;
            let mut next = started + policy.renew_interval;
            loop {
                sleep_until(next.min(deadline)).await;
                let attempt = Instant::now();
                if attempt >= deadline || !current() {
                    break;
                }
                let outcome = timeout_at(
                    deadline.min(attempt + policy.db_timeout),
                    store.renew(&key, match_id, lease, policy.ttl),
                )
                .await;
                // Old-generation completions cannot change the new seat. A delayed success
                // must never revive one that crossed its confirmed local deadline.
                if !current() {
                    break;
                }
                let now = Instant::now();
                if now >= deadline {
                    break;
                }
                match outcome {
                    Ok(Ok(true)) => {
                        deadline = attempt + policy.ttl;
                        if !validity.send_if_modified(|confirmed| {
                            if *confirmed <= Instant::now() {
                                return false;
                            }
                            *confirmed = deadline;
                            true
                        }) {
                            break;
                        }
                        next = attempt + policy.renew_interval;
                    }
                    Ok(Ok(false)) => {
                        // A committed result may have released the row; the room keeps a
                        // resolved seat for reliable delivery. Only unresolved seats revoke.
                        validity.send_replace(now);
                        break;
                    }
                    Ok(Err(_)) | Err(_) => {
                        next = now + policy.renew_interval.min(policy.db_timeout)
                    }
                }
            }
        });
        owner.renewal = Some(worker.abort_handle());
    }
}
