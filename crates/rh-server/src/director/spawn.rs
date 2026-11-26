//! Spawning a room: bot learning and fill policy hooks, the room task itself, and the lease renewal tasks that outlive an admission.

use super::*;
use tokio::sync::OwnedMutexGuard;

/// CPU-heavy map preparation is bounded across simultaneous new rooms.
static QUEST_MAP_WORKERS: tokio::sync::Semaphore = tokio::sync::Semaphore::const_new(2);

pub(super) struct PreparedRoom {
    id: MatchId,
    room: Room,
    public: PublicRoom,
}

impl Director {
    pub fn install_alpha_capture(&self, capture: crate::alpha_capture::Capture) {
        let _ = self.alpha_capture.set(capture);
    }

    pub(crate) fn alpha_capture_health(&self) -> Option<serde_json::Value> {
        self.alpha_capture
            .get()
            .map(crate::alpha_capture::Capture::health)
    }

    pub async fn flush_alpha_capture(&self) {
        if let Some(capture) = self.alpha_capture.get() {
            capture.flush().await;
        }
    }

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

    pub(crate) fn needs_board_room(&self) -> bool {
        self.cfg.sim.top25_quests && self.cfg.sim.quests && !self.cfg.sim.training
    }

    /// New board room preparation may regenerate geography and must run
    /// outside the director lock, before a seat or session is reserved.
    pub(super) fn prepare_board_room(&self, fill_bots_to: usize) -> Result<PreparedRoom, String> {
        let _permit = QUEST_MAP_WORKERS
            .try_acquire()
            .map_err(|_| "quest map workers are busy")?;
        let secrets = self
            .cfg
            .fixed_board_secrets
            .unwrap_or_else(crate::room::random_secrets);
        self.build_room(fill_bots_to, Some(secrets))
    }

    pub(super) async fn prepare_board_room_async(
        &self,
        fill_bots_to: usize,
        opening_guard: OwnedMutexGuard<()>,
    ) -> Result<(PreparedRoom, OwnedMutexGuard<()>), String> {
        let director = self.me.upgrade().ok_or("director is closed")?;
        // A canceled admission cannot stop a running blocking worker. Keep the guard with that worker until it exits.
        tokio::task::spawn_blocking(move || {
            let room = director.prepare_board_room(fill_bots_to)?;
            Ok((room, opening_guard))
        })
        .await
        .map_err(|_| "quest map worker failed".to_owned())?
    }

    /// Legacy rooms retain the existing shared-map fast path.
    pub(super) fn spawn_room(
        &self,
        inner: &mut Inner,
        fill_bots_to: usize,
    ) -> (MatchId, mpsc::Sender<RoomCmd>) {
        let room = self
            .build_room(fill_bots_to, None)
            .expect("configured legacy room");
        self.activate_room(inner, room)
    }

    fn build_room(
        &self,
        fill_bots_to: usize,
        board_secrets: Option<rh_sim::Secrets>,
    ) -> Result<PreparedRoom, String> {
        let id = random16();
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
        // A training room never draws from the pool (runtime.rs refuses the combination).
        let map = match &self.cfg.map_pool {
            Some(pool) if !sim.training => {
                let (map, served) = pool.take_served();
                eprintln!(
                    "[maps] {}: match={} {served}",
                    if board_secrets.is_some() {
                        "selected candidate"
                    } else {
                        "match start"
                    },
                    crate::control::room_id_hex(id)
                );
                map
            }
            _ => self.map.clone(),
        };
        let map = if let Some(secrets) = board_secrets {
            let style = self
                .cfg
                .map_pool
                .as_ref()
                .map(|pool| pool.quest_map_style())
                .unwrap_or_default();
            crate::map_pool::for_quest_board(map, secrets, style)?
        } else {
            map
        };
        let rc = RoomConfig {
            sim,
            arena: map.arena,
            welcome_map: map.welcome_map,
            fill_bots_to,
            tick: self.cfg.tick,
            record: self.cfg.record,
            fixed_secrets: board_secrets,
            fixed_bot_seed: None,
            reconnect_grace: self.cfg.reconnect_grace,
            linger: self.cfg.linger,
        };
        let mut room = Room::try_new(id, rc, self.reports.clone(), tokio::time::Instant::now())?;
        if let Some(capture) = self.alpha_capture.get() {
            room = room.with_alpha_capture(capture);
        }
        if let Some(fill) = fill {
            room = room.with_bot_fill(fill).with_bot_names(self.bot_names());
            if let Some(sink) = self.learning.lock_recover().clone() {
                room = room.with_learning(sink);
            }
        }
        Ok(PreparedRoom { id, room, public })
    }

    pub(super) fn activate_room(
        &self,
        inner: &mut Inner,
        prepared: PreparedRoom,
    ) -> (MatchId, mpsc::Sender<RoomCmd>) {
        let PreparedRoom { id, room, public } = prepared;
        let (tx, rx) = mpsc::channel(INBOUND_CAP);
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
