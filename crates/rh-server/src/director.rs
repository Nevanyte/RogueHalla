//! The match director: admission (protocol, content, credentials, profile ownership, capacity), room placement, session
//! resume, and the off-tick handling of what rooms report (results to durable storage with retries, freed profiles).
//! Several matches run in one process (one tokio task each); the hosting layer decides how many processes run per host.

use crate::bot_learning::{BotRunFacts, LearningSink};
use crate::control::{ControlBook, CtlError, EndMode, LeaveMode};
use crate::hosting::Lifecycle;
use crate::outbox::Outbox;
use crate::persist::{Auth, HeldLease, LeaseError, Persistence, ProfileKey, RecordError, Recorded};
use crate::room::{
    random16, run_room, Admission, JoinOk, MapAssets, MatchId, Room, RoomCmd, RoomConfig,
    RoomReport, RoomStats, INBOUND_CAP,
};
use rh_protocol::{RejectCode, PROTOCOL_VERSION};
use rh_sim::{Arena, FactionId, Phase, SimConfig};
use std::collections::HashMap;
use std::sync::{Arc, Mutex};
use std::time::Duration;
use tokio::sync::{mpsc, oneshot, Notify};

/// Poison-tolerant locking for the director's maps. A panic while the lock was held used to make every later
/// `lock().expect(..)` panic too, so one bad critical section bricked admission, `/info` and the control API for the whole
/// process. Recovery is safe here because every critical section is a short, synchronous map/counter update that never
/// awaits and never calls out: the worst a panic can leave behind is one half-updated entry (for example a reserved seat
/// whose session row was not yet written), and each such entry is already reclaimed by the paths that own it (the seat's
/// `unwind`, the room's `Closed` sweep, lease expiry). That is strictly better than refusing all service. The poison flag
/// is cleared after the first recovery so the line below is logged once per incident, not once per lock.
pub(crate) trait LockRecover<T> {
    fn lock_recover(&self) -> std::sync::MutexGuard<'_, T>;
}
impl<T> LockRecover<T> for Mutex<T> {
    fn lock_recover(&self) -> std::sync::MutexGuard<'_, T> {
        self.lock().unwrap_or_else(|poisoned| {
            eprintln!(
                "[server] ERROR event=director_lock_poisoned detail=\"a thread panicked holding a director lock; recovered and continuing\""
            );
            self.clear_poison();
            poisoned.into_inner()
        })
    }
}

/// The director renews one fenced row at a time per seat. Local validity is measured from
/// the start of the corresponding acquire/renew, never from the database response.
#[derive(Clone, Copy, Debug)]
pub struct LeasePolicy {
    pub ttl: Duration,
    pub renew_interval: Duration,
    pub db_timeout: Duration,
}
impl Default for LeasePolicy {
    fn default() -> Self {
        Self {
            ttl: Duration::from_secs(120),
            renew_interval: Duration::from_secs(30),
            db_timeout: Duration::from_secs(5),
        }
    }
}
impl LeasePolicy {
    pub fn validate(self) -> Result<(), &'static str> {
        if self.ttl > Duration::from_secs(3600) {
            return Err("lease TTL must be at most one hour");
        }
        if self.renew_interval.is_zero()
            || self.db_timeout.is_zero()
            || self
                .renew_interval
                .checked_add(self.db_timeout)
                .is_none_or(|d| d >= self.ttl)
            || tokio::time::Instant::now().checked_add(self.ttl).is_none()
        {
            return Err("renew interval and database timeout must be positive and together shorter than a representable lease TTL");
        }
        Ok(())
    }
}
#[derive(Clone)]
pub struct DirectorConfig {
    pub leases: LeasePolicy,
    pub sim: SimConfig,
    /// The map every room plays on, shared (never cloned per room).
    pub arena: Arc<Arena>,
    pub max_rooms: usize,
    pub fill_bots_to: usize,
    /// The legacy lobby fill (BotManager): arrivals over the countdown, closing top-up and the yield seat. None keeps
    /// the rooms' immediate placeholder fill (default for tests and benches; the server binary enables it).
    pub bot_fill: Option<crate::bot_fill::BotFillConfig>,
    pub tick: Duration,
    /// Record a private replay of every match (kept in memory for the last few rooms; tests and diagnostics).
    pub record: bool,
    pub reconnect_grace: Duration,
    pub linger: Duration,
    /// A connection that sends nothing for this long is treated as dropped (clients ping at least every second).
    pub idle_timeout: Duration,
    /// Hosted mode (`--require-admit`, docs/hosting/CONTROL_API.md section 4): a Hello is admitted only with a matching,
    /// unexpired, unconsumed admit, checked after the profile credential check and before profile ownership. Rooms then
    /// exist only through `POST /control/session`. Off: admission is exactly as it always was.
    pub require_admit: bool,
    /// Default lifetime of an admit that names no `ttlMs`.
    pub admit_ttl: Duration,
    /// `--map-pool` (P-MAP-3): each new arena room takes its own certified map from this pool; `arena` is then the pool's
    /// last resort. None (the default): every room plays `arena`.
    pub map_pool: Option<Arc<crate::map_pool::MapPool>>,
}

impl DirectorConfig {
    pub fn new(sim: SimConfig, arena: impl Into<Arc<Arena>>) -> Self {
        Self {
            leases: LeasePolicy::default(),
            sim,
            arena: arena.into(),
            max_rooms: 16,
            fill_bots_to: 0,
            bot_fill: None,
            tick: Duration::from_millis(rh_sim::constants::TICK_MS as u64),
            record: false,
            reconnect_grace: crate::room::RECONNECT_GRACE,
            linger: crate::room::LINGER,
            idle_timeout: crate::conn::IDLE_TIMEOUT,
            require_admit: false,
            admit_ttl: Duration::from_millis(crate::control::DEFAULT_TTL_MS),
            map_pool: None,
        }
    }
}

struct RoomEntry {
    tx: mpsc::Sender<RoomCmd>,
    phase: Phase,
    /// Humans the room reported seated (its `Seated` reports, less its `Left` ones).
    seated: usize,
    /// Admissions that picked this room and have not been seated yet (their durable lease may still be pending). Kept
    /// apart from `seated` so a report of the seated count can never erase them.
    reserved: usize,
    /// What the public lobby endpoint may show of this room.
    public: PublicRoom,
}

/// The public lobby facts of one room, kept from its reports (P-LOBBY-ACTIVITY-1).
#[derive(Clone, Debug, Default)]
struct PublicRoom {
    /// Callsigns seated (a `Seat` report `Seated` until its `Gone`), in arrival order.
    humans: Vec<String>,
    /// Bot callsigns seated (`BotSeat` reports), in arrival order.
    bots: Vec<String>,
    started_at_ms: Option<u64>,
    human_min: usize,
    fill_target: usize,
}

impl PublicRoom {
    fn on_seat(&mut self, profile: &str, event: crate::room::SeatEvent) {
        use crate::room::SeatEvent;
        match event {
            SeatEvent::Seated => {
                if !self.humans.iter().any(|h| h.eq_ignore_ascii_case(profile)) {
                    self.humans.push(profile.to_owned());
                }
            }
            SeatEvent::Gone(_) => self.humans.retain(|h| !h.eq_ignore_ascii_case(profile)),
            SeatEvent::Away { .. } | SeatEvent::Back => {}
        }
    }

    /// Legacy `raidStartedAtEpoch`: the wall time the room first reported its raid running.
    fn on_phase(&mut self, phase: Phase) {
        if phase == Phase::Active && self.started_at_ms.is_none() {
            self.started_at_ms = Some(
                std::time::SystemTime::now()
                    .duration_since(std::time::UNIX_EPOCH)
                    .map_or(0, |d| d.as_millis() as u64),
            );
        }
    }
}

impl RoomEntry {
    /// Humans the room has or is about to have.
    fn committed(&self) -> usize {
        self.seated + self.reserved
    }
}

/// One player of a room as the public lobby endpoint may show it (`lobby.rs`): a display name and the bot flag, nothing else.
#[derive(Clone, Debug, PartialEq, Eq)]
pub struct LobbyPlayerView {
    pub name: String,
    pub bot: bool,
}

/// One room as the public lobby endpoint may show it. No match id, session, address or position.
#[derive(Clone, Debug, PartialEq)]
pub struct LobbyRoomView {
    pub phase: Phase,
    /// Unix epoch milliseconds the raid started (the room first reported `Active`); None before.
    pub started_at_ms: Option<u64>,
    /// The room's human minimum (its sim's `min_players`) and the count bots top the lobby up to (0: no fill).
    pub human_min: usize,
    pub fill_target: usize,
    pub players: Vec<LobbyPlayerView>,
}

/// A read-only copy of what the public lobby endpoint shows, taken under the director's lock and released at once.
#[derive(Clone, Debug, Default, PartialEq)]
pub struct LobbyView {
    /// Public (non-training) rooms, in a stable order.
    pub rooms: Vec<LobbyRoomView>,
    /// Lower-cased callsigns seated in any room (training included): they are not "waiting".
    pub occupied: Vec<String>,
    pub room_count: usize,
    pub room_limit: usize,
    /// A lobby has a free seat, or another room may open.
    pub joinable: bool,
}

/// The lobby the next admission joins: among lobbies with a free seat (counting reservations), the fullest, so matches fill
/// before another opens; ties go to the lowest id. Deterministic (no HashMap order).
fn open_lobby(rooms: &HashMap<MatchId, RoomEntry>, capacity: usize) -> Option<MatchId> {
    rooms
        .iter()
        .filter(|(_, r)| r.phase == Phase::Lobby && r.committed() < capacity)
        .min_by_key(|(id, r)| (std::cmp::Reverse(r.committed()), **id))
        .map(|(id, _)| *id)
}

/// Where a profile's entry is in its life. Every entry has exactly one owner at a time, and only that owner moves it on.
#[derive(Clone, Copy, Debug, PartialEq, Eq)]
enum SeatState {
    /// An admission (the entry's `gen`) picked `match_id` and may still be acquiring the lease or waiting for the Join
    /// reply. Only that admission changes it: a seat makes it `Seated`, a failure `Leaving` (with its give-back). A
    /// room's `Closed` never touches it (the admission unwinds itself).
    Reserved,
    /// Seated in `match_id` under `fence`. Only the room's reports for that seat (`Left`, or `Closed` after a crash)
    /// change it.
    Seated,
    /// Gone from its seat. Removed once no result is due or in flight for it and its lease is given back (by the
    /// director, or by the result write's own transaction). An admission for the profile waits for that (bounded by
    /// `LEAVING_WAIT`) instead of racing the durable lease.
    Leaving,
}

/// What this process knows of the durable lease an entry holds.
#[derive(Clone, Copy, Debug, PartialEq, Eq)]
enum LeaseState {
    /// Held (or not taken yet: fence 0).
    Held,
    /// The director's give-back is in flight; its completion frees the entry.
    Releasing,
    /// Released by a result write's transaction (or refused as no longer ours): nothing to give back.
    Gone,
}

#[derive(Clone, Debug)]
struct ProfileSeat {
    renewal: Option<tokio::task::AbortHandle>,
    /// Owner token, unique per admission. Every deferred change (a lease give-back, a result write, the admission's own
    /// unwinding) names the entry by it, so it can never remove or mark an entry a newer admission created for the same
    /// profile.
    gen: u64,
    match_id: MatchId,
    /// The lease fence (0 until the admission's acquire returns).
    fence: i64,
    state: SeatState,
    /// The seat left a run it was still standing in: its (MIA) result report is still to come.
    result_due: bool,
    /// Result writes in flight for this seat. Each releases the lease in its own transaction, so the director never
    /// gives the lease back while one is in flight: a give-back that landed first would make the write fail `NoLease`.
    writes: u32,
    lease: LeaseState,
}

impl ProfileSeat {
    /// Retired seats must not leave heartbeat tasks sleeping until their next interval.
    fn stop_renewing(&mut self) {
        if let Some(worker) = self.renewal.take() {
            worker.abort();
        }
    }
}

/// Until admission delivers the room's reply, cancellation is owned by this guard.
/// After sending a Join the exact reply distinguishes a seat (Left/Closed owns it)
/// from a refused reservation (unwind owns it). Drop only schedules recovery.
struct PendingAdmission {
    director: std::sync::Weak<Director>,
    runtime: tokio::runtime::Handle,
    key: ProfileKey,
    gen: u64,
    match_id: MatchId,
    fence: Option<i64>,
    reply: Option<oneshot::Receiver<Result<JoinOk, RejectCode>>>,
    clock: Option<tokio::sync::watch::Sender<tokio::time::Instant>>,
    armed: bool,
}
impl Drop for PendingAdmission {
    fn drop(&mut self) {
        if !self.armed {
            return;
        }
        let Some(d) = self.director.upgrade() else {
            return;
        };
        let (key, gen, m, fence) = (self.key.clone(), self.gen, self.match_id, self.fence);
        let reply = self.reply.take();
        if let Some(clock) = self.clock.take() {
            // A worker never publishes a later deadline after this one expires.
            clock.send_replace(tokio::time::Instant::now());
        }
        self.runtime.spawn(async move {
            let seated = match reply {
                Some(rx) => matches!(rx.await, Ok(Ok(_))),
                None => false,
            };
            if !seated {
                d.unwind(&key, gen, m, fence);
                return;
            }
            let mut inner = d.inner.lock_recover();
            let room_exists = inner.rooms.contains_key(&m);
            if let Some(s) = inner
                .profiles
                .get_mut(&key)
                .filter(|s| s.gen == gen && s.match_id == m && Some(s.fence) == fence)
            {
                s.stop_renewing();
                if s.state == SeatState::Reserved {
                    s.state = if room_exists {
                        SeatState::Seated
                    } else {
                        SeatState::Leaving
                    };
                }
                d.advance(&mut inner, &key);
            }
        });
    }
}

/// How long rooms get to close after `DrainEnd` before the drain gives up on them.
const DRAIN_END_GRACE: Duration = Duration::from_secs(10);

/// What is kept about the latest crashed room: enough for an operator to find it in the logs and judge its blast radius.
#[derive(Clone, Debug)]
struct CrashRecord {
    room_id: String,
    at_unix_ms: u64,
    ticks: u64,
    seats_freed: usize,
}

/// C-PERSIST, in-process ownership (the durable lease fences across hosts):
///
/// * One entry per authenticated durable profile ID (a lower-cased callsign for memory seats). While it exists, an admission for that profile is refused `ProfileInUse`, or,
///   if the entry is `Leaving`, waits for it to go (at most `LEAVING_WAIT` for the whole admission, then refused). So a
///   profile never has two live seats in this process, and a new lease is never taken while the old one may still be
///   given back or written to.
/// * An entry is created only by an admission (`Reserved`, under a fresh `gen`) and removed only by its owner: the
///   admission's own unwinding, or the give-back / result write of the seat it became, each naming the `gen`.
/// * The lease of a seat that left is given back only once no result is due or in flight for it; a write that lands
///   releases the lease itself.
#[derive(Default)]
struct Inner {
    /// The control API's game sessions, admits and player statuses (control.rs). Plain data: no I/O under this lock.
    control: ControlBook,
    rooms: HashMap<MatchId, RoomEntry>,
    profiles: HashMap<ProfileKey, ProfileSeat>,
    /// The last `ProfileSeat::gen` handed out.
    gens: u64,
    sessions: HashMap<[u8; 16], MatchId>,
    closed: Vec<RoomStats>,
    /// Rooms whose task panicked since this process started, and a summary of the latest (loopback control health only).
    rooms_crashed: u64,
    last_crash: Option<CrashRecord>,
    results_ok: u64,
    results_failed: u64,
    /// Results the store refused for good (no lease, stale fence): not retried.
    results_refused: u64,
    /// Durable-spool accounting (see `spool.rs`): a result is first `spooled` (fsynced, survives a process restart on this
    /// host) and later `committed` (`results_ok`: PostgreSQL has it, the only host-loss-durable point).
    results_spooled: u64,
    /// Results whose append failed because the spool was full (they fall back to in-memory retry only).
    spool_full: u64,
    /// Results whose append failed on I/O (same fallback).
    spool_errors: u64,
    /// Spooled results committed (or found already applied) by the startup recovery pass.
    results_recovered: u64,
    /// Records moved to the dead-letter directory by this process (refused, permanently rejected, unreadable at startup).
    results_dead_lettered: u64,
    /// Alerts raised for results unresolved past `retry_deadline` (they keep retrying; nothing is dead-lettered for time).
    results_stuck_alerts: u64,
    replays: Vec<rh_sim::replay::Replay>,
}

/// Attempts per result write (100 ms doubling to 5 s: about two minutes in all). Only transient errors are retried.
pub const RESULT_ATTEMPTS: u32 = 30;

/// How a spooled result ended in this process.
#[cfg(feature = "postgres")]
enum SpoolEnd {
    Committed { applied: bool },
    DeadLettered,
}

pub struct Director {
    /// Result writes in flight (the process waits for them before exiting).
    pub pending_writes: std::sync::atomic::AtomicUsize,
    cfg: DirectorConfig,
    /// The configured map and its Welcome blob, built once and lent to every room.
    map: MapAssets,
    inner: Mutex<Inner>,
    reports: mpsc::UnboundedSender<RoomReport>,
    pub persistence: Arc<Persistence>,
    pub auth: Arc<Auth>,
    pub lifecycle: Arc<Lifecycle>,
    /// Woken whenever a `leaving` profile is freed: an admission for it waits on this instead of being refused.
    freed: Notify,
    /// This director, for the tasks it spawns (lease give-backs) from `&self`.
    me: std::sync::Weak<Director>,
    /// The durable result spool (`--result-spool`); `None` keeps the in-memory-only retry.
    #[cfg(feature = "postgres")]
    spool: Option<Arc<crate::spool::ResultSpool>>,
    /// Bounds the spooled writes in flight at once, so a recovery pass cannot stampede the connection pool.
    #[cfg(feature = "postgres")]
    spool_permits: tokio::sync::Semaphore,
    /// The P-BOTS-5 live learning sink: the record caller for bot results and the bias view the next lobby reads.
    learning: Mutex<Option<Arc<dyn LearningSink>>>,
    /// The bot fill's read-only profile-name set (source isNameTaken). Refreshed by the server; a new lobby reads it.
    bot_names: std::sync::RwLock<Arc<std::collections::HashSet<String>>>,
}

/// How long an admission waits, in all (one deadline shared by its retries), for a profile that is mid-release (its lease
/// give-back or result write is in flight) before it is refused as in use. A release is milliseconds; a result write that
/// is being retried can take minutes, and that is a refusal, not a wait.
pub const LEAVING_WAIT: Duration = Duration::from_secs(3);

pub struct Hello {
    pub protocol: u16,
    pub content_hash: u64,
    pub profile: String,
    /// Opaque admission credential ([`rh_protocol::ClientMsg::Hello`]).
    pub ticket: Vec<u8>,
    pub faction: Option<u8>,
    pub resume: Option<[u8; 16]>,
}

/// Training has a public endpoint, with a dedicated solo room per admission. Refuse incompatible library configs too.
fn validate_runtime(cfg: &DirectorConfig) -> Result<(), &'static str> {
    if cfg.sim.quests && cfg.arena.map.layers().is_none() {
        return Err("quests require a validated rh-map/2 arena");
    }
    if cfg.sim.training
        && (!cfg.sim.dummies
            || cfg.sim.quests
            || cfg.sim.lifecycle != rh_sim::ArenaMode::Off
            || cfg.sim.initial_loot
            || cfg.sim.mob_mode != rh_sim::mob_runtime::MobMode::Off
            || cfg.sim.min_players != 1
            || cfg.sim.lobby_seconds != 0.
            || cfg.sim.fill_seconds != 0.
            || cfg.fill_bots_to != 0
            || cfg.bot_fill.as_ref().is_some_and(|f| f.fill_to != 0)
            || cfg.record)
    {
        return Err("training requires dummies, one human, immediate start, no quests, arena lifecycle, PvE, initial loot, bots or replay");
    }
    Ok(())
}

fn profile_ok(p: &str) -> bool {
    (2..=16).contains(&p.len())
        && p.bytes()
            .all(|b| b.is_ascii_alphanumeric() || b == b'_' || b == b'-')
}

impl Director {
    pub fn new(
        cfg: DirectorConfig,
        persistence: Arc<Persistence>,
        auth: Arc<Auth>,
        lifecycle: Arc<Lifecycle>,
    ) -> Arc<Self> {
        Self::build(
            cfg,
            persistence,
            auth,
            lifecycle,
            #[cfg(feature = "postgres")]
            None,
        )
    }

    /// As [`Director::new`], with the durable result spool. Panics if the persistence is not PostgreSQL: a spool record
    /// can only be replayed into the durable store.
    #[cfg(feature = "postgres")]
    pub fn new_with_spool(
        cfg: DirectorConfig,
        persistence: Arc<Persistence>,
        auth: Arc<Auth>,
        lifecycle: Arc<Lifecycle>,
        spool: Arc<crate::spool::ResultSpool>,
    ) -> Arc<Self> {
        assert!(
            persistence.is_durable(),
            "a result spool needs the PostgreSQL store"
        );
        Self::build(cfg, persistence, auth, lifecycle, Some(spool))
    }

    fn build(
        cfg: DirectorConfig,
        persistence: Arc<Persistence>,
        auth: Arc<Auth>,
        lifecycle: Arc<Lifecycle>,
        #[cfg(feature = "postgres")] spool: Option<Arc<crate::spool::ResultSpool>>,
    ) -> Arc<Self> {
        validate_runtime(&cfg).expect("invalid runtime admission configuration");
        crate::wire::profile_supported(&cfg.sim)
            .expect("refusing to host unsupported simulation profile");
        cfg.leases.validate().expect("invalid lease policy");
        if let Err(e) = crate::wire::arena_fits(&cfg.arena) {
            panic!("refusing to host this arena: {e}");
        }
        let (tx, rx) = mpsc::unbounded_channel();
        let map = MapAssets::new(cfg.arena.clone()).expect("the arena's map encodes as a Welcome");
        let inner = Inner {
            control: ControlBook::new(cfg.admit_ttl, cfg.reconnect_grace),
            ..Inner::default()
        };
        let d = Arc::new_cyclic(|me| Self {
            pending_writes: std::sync::atomic::AtomicUsize::new(0),
            cfg,
            map,
            inner: Mutex::new(inner),
            reports: tx,
            persistence,
            auth,
            lifecycle,
            freed: Notify::new(),
            me: me.clone(),
            learning: Mutex::new(None),
            bot_names: std::sync::RwLock::new(Arc::new(std::collections::HashSet::new())),
            #[cfg(feature = "postgres")]
            spool,
            #[cfg(feature = "postgres")]
            spool_permits: tokio::sync::Semaphore::new(8),
        });
        tokio::spawn(Self::report_loop(d.clone(), rx));
        d
    }

    /// Admits a connection. Every refusal is decided before a seat is taken.
    pub async fn admit(
        &self,
        h: Hello,
        outbox: Arc<Outbox>,
        conn: u64,
    ) -> Result<(mpsc::Sender<RoomCmd>, JoinOk), (RejectCode, String)> {
        // A lobby can close into its run, or its last player can leave and the room close, between the pick and the join
        // (the room then refuses MatchFull, or its inbox is gone): that room is marked unjoinable, try another. Every
        // attempt shares one deadline for waiting on a profile mid-release, so the documented bound is the real one.
        let deadline = tokio::time::Instant::now() + LEAVING_WAIT;
        let mut tries = 0;
        loop {
            match self.admit_once(&h, outbox.clone(), conn, deadline).await {
                Err((RejectCode::MatchFull, reason))
                    if (reason == "lobby closed" || reason == "match closed") && tries < 3 =>
                {
                    tries += 1
                }
                other => return other,
            }
        }
    }

    async fn admit_once(
        &self,
        h: &Hello,
        outbox: Arc<Outbox>,
        conn: u64,
        deadline: tokio::time::Instant,
    ) -> Result<(mpsc::Sender<RoomCmd>, JoinOk), (RejectCode, String)> {
        if h.protocol != PROTOCOL_VERSION {
            return Err((
                RejectCode::ProtocolMismatch,
                format!(
                    "server speaks protocol {PROTOCOL_VERSION}, client {}",
                    h.protocol
                ),
            ));
        }
        let content = rh_sim::content::content_hash();
        if h.content_hash != content {
            return Err((
                RejectCode::ContentMismatch,
                format!("server content {content:016x}"),
            ));
        }
        if let Some(session) = h.resume {
            let tx = {
                let inner = self.inner.lock_recover();
                inner
                    .sessions
                    .get(&session)
                    .and_then(|m| inner.rooms.get(m))
                    .map(|r| r.tx.clone())
            };
            let Some(tx) = tx else {
                return Err((RejectCode::NoSuchSession, "session expired".into()));
            };
            let (reply, rx) = oneshot::channel();
            tx.send(RoomCmd::Resume {
                session,
                outbox,
                conn,
                reply,
            })
            .await
            .map_err(|_| (RejectCode::NoSuchSession, "match over".into()))?;
            let ok = rx
                .await
                .map_err(|_| (RejectCode::NoSuchSession, "match over".into()))?
                .map_err(|c| (c, "resume refused".into()))?;
            return Ok((tx, ok));
        }
        if !profile_ok(&h.profile) {
            return Err((
                RejectCode::BadCredentials,
                "callsign must be 2-16 of A-Z a-z 0-9 _ -".into(),
            ));
        }
        if self.lifecycle.draining() {
            return Err((RejectCode::ServerDraining, "server draining".into()));
        }
        let key = match self
            .persistence
            .authenticate(&self.auth, &h.profile, &h.ticket)
            .await
        {
            Ok(Some(key)) => key,
            Ok(None) => return Err((RejectCode::BadCredentials, "bad credentials".into())),
            // The credential store could not answer: that says nothing about the ticket, so it is neither reported nor
            // counted (the edge throttle counts only `BadCredentials`) as a bad credential. The wire has no "unavailable"
            // code; "try again shortly" is the honest existing one. The store's error stays in the (rate-limited) log.
            Err(e) => {
                crate::api::log_internal("hello credential check", &e);
                return Err((
                    RejectCode::RateLimited,
                    "server busy, storage unavailable; try again shortly".into(),
                ));
            }
        };
        // Hosted mode: an additional precondition after the credential check and before profile ownership and the fenced
        // lease (CONTROL_API.md section 4). It never replaces authentication and takes no lease. The admit is consumed
        // below, atomically with the seat reservation; this early look only refuses before any wait.
        if self.cfg.require_admit
            && !self
                .inner
                .lock_recover()
                .control
                .has_admit(&h.profile, tokio::time::Instant::now())
        {
            return Err((RejectCode::BadCredentials, "not admitted".into()));
        }
        // A player who just left (or whose match just closed, or whose previous attempt here just failed) has its lease
        // give-back in flight: wait for it instead of refusing, so a quick rejoin is deterministic. A profile with a live
        // seat, or with another admission in flight, is refused below as before.
        self.wait_while_leaving(&key, deadline).await;
        // Pick (or open) a lobby, reserving the profile in the same critical section so two connections cannot both pass.
        let (match_id, tx, gen) = {
            let mut inner = self.inner.lock_recover();
            if inner.profiles.contains_key(&key) {
                return Err((
                    RejectCode::ProfileInUse,
                    "callsign already in a match".into(),
                ));
            }
            let next_gen = inner.gens + 1;
            let (match_id, tx) = if self.cfg.require_admit {
                // Hosted mode: the room is the one the admit names (no automatic lobby placement, no room opened here).
                // The admit is consumed here, in the critical section that reserves the seat; any later failure of this
                // admission restores it (`unwind`).
                let Some(taken) =
                    inner
                        .control
                        .take_admit(&h.profile, tokio::time::Instant::now(), next_gen)
                else {
                    return Err((RejectCode::BadCredentials, "not admitted".into()));
                };
                let target = inner
                    .rooms
                    .get(&taken.room)
                    .map(|r| (r.phase == Phase::Lobby, r.tx.clone()));
                match target {
                    Some((true, tx)) => (taken.room, tx),
                    other => {
                        inner.control.restore_gen(next_gen);
                        let why = if other.is_none() {
                            "match closed"
                        } else {
                            "match in progress"
                        };
                        return Err((RejectCode::MatchFull, why.into()));
                    }
                }
            } else {
                let open = open_lobby(
                    &inner.rooms,
                    if self.cfg.sim.training {
                        1
                    } else {
                        rh_sim::constants::MAX_PLAYERS
                    },
                )
                .and_then(|id| inner.rooms.get(&id).map(|r| (id, r.tx.clone())));
                match open {
                    Some(x) => x,
                    None => {
                        if inner.rooms.len() >= self.cfg.max_rooms {
                            return Err((RejectCode::MatchFull, "no capacity".into()));
                        }
                        self.spawn_room(&mut inner, self.cfg.fill_bots_to)
                    }
                }
            };
            inner.gens += 1;
            let gen = inner.gens;
            inner.profiles.insert(
                key.clone(),
                ProfileSeat {
                    renewal: None,
                    gen,
                    match_id,
                    fence: 0,
                    state: SeatState::Reserved,
                    result_due: false,
                    writes: 0,
                    lease: LeaseState::Held,
                },
            );
            if let Some(r) = inner.rooms.get_mut(&match_id) {
                r.reserved += 1;
            }
            (match_id, tx, gen)
        };
        // Cross-host session ownership: a fenced lease in durable storage (off the tick; before the seat is taken).
        let mut pending = PendingAdmission {
            director: self.me.clone(),
            runtime: tokio::runtime::Handle::current(),
            key: key.clone(),
            gen,
            match_id,
            fence: None,
            reply: None,
            clock: None,
            armed: true,
        };
        let policy = self.cfg.leases;
        let started = tokio::time::Instant::now();
        let acquisition = self
            .persistence
            .acquire_live(&key, match_id, &h.profile, policy);
        let acquisition = tokio::time::timeout(policy.db_timeout, acquisition)
            .await
            .unwrap_or_else(|_| Err(LeaseError::timed_out()));
        let lease = match acquisition {
            Ok(lease) => lease,
            Err(e) => {
                pending.armed = false;
                self.unwind(&key, gen, match_id, None);
                return Err(match e {
                    LeaseError::Held(e) => (RejectCode::ProfileInUse, e),
                    // Storage trouble says nothing about the callsign; the wire has no "unavailable" code (appending one
                    // is a protocol change), so the closest honest one is "try again shortly" with an accurate reason.
                    LeaseError::Unavailable(e) => {
                        // The detail (sqlx/Postgres text) stays in the server log; the client gets a fixed string.
                        eprintln!("[server] WARN event=lease_unavailable detail={e:?}");
                        (
                            RejectCode::RateLimited,
                            "server busy, storage unavailable; try again shortly".to_string(),
                        )
                    }
                });
            }
        };
        let fence = lease.fence;
        pending.fence = Some(fence);
        if let Some(s) = self.inner.lock_recover().profiles.get_mut(&key) {
            if s.gen == gen {
                s.fence = fence;
            }
        }
        let (reply, rx) = oneshot::channel();
        let faction = h.faction.and_then(FactionId::from_code);
        let deadline = started + policy.ttl;
        let (validity, clock) = tokio::sync::watch::channel(deadline);
        let request = RoomCmd::JoinLeased {
            admission: Admission {
                gen,
                profile_id: key.id(),
            },
            profile: h.profile.clone(),
            faction,
            outbox,
            conn,
            fence,
            deadline: clock,
            reply,
        };
        match tokio::time::timeout_at(deadline, tx.send(request)).await {
            Ok(Ok(())) => {}
            failed => {
                pending.armed = false;
                self.unwind(&key, gen, match_id, Some(fence));
                if failed.is_ok() {
                    self.mark_unjoinable(match_id);
                    return Err((RejectCode::MatchFull, "match closed".into()));
                }
                return Err((
                    RejectCode::ProfileInUse,
                    "lease expired before seating".into(),
                ));
            }
        }
        pending.clock = Some(validity.clone());
        pending.reply = Some(rx);
        self.start_renewals(key.clone(), gen, match_id, lease, started, validity);
        let outcome = pending.reply.as_mut().expect("join sent").await;
        pending.armed = false;
        match outcome {
            Ok(Ok(ok)) => {
                let mut inner = self.inner.lock_recover();
                let room_gone = !inner.rooms.contains_key(&match_id);
                let mut orphaned = false;
                if let Some(s) = inner.profiles.get_mut(&key).filter(|s| s.gen == gen) {
                    if s.state == SeatState::Reserved {
                        // The room crashed after seating us and its `Closed` (which skips reservations) is already
                        // handled: nothing else would free this seat.
                        orphaned = room_gone;
                        s.state = if room_gone {
                            SeatState::Leaving
                        } else {
                            SeatState::Seated
                        };
                    }
                    // Otherwise the seat's `Left` was handled before this reply: the entry is `Leaving` and its own
                    // give-back frees it.
                }
                if orphaned {
                    self.advance(&mut inner, &key);
                }
                // A room that already closed has had its sessions swept: nothing would ever remove this one.
                if !room_gone {
                    inner.sessions.insert(ok.session, match_id);
                }
                Ok((tx, ok))
            }
            Ok(Err(code)) => {
                self.unwind(&key, gen, match_id, Some(fence));
                if code == RejectCode::MatchFull {
                    // The lobby closed into its run: never pick it again; admit() tries another room.
                    self.mark_unjoinable(match_id);
                    return Err((code, "lobby closed".into()));
                }
                Err((code, "join refused".into()))
            }
            Err(_) => {
                self.unwind(&key, gen, match_id, Some(fence));
                self.mark_unjoinable(match_id);
                Err((RejectCode::MatchFull, "match closed".into()))
            }
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

    fn bot_names(&self) -> Arc<std::collections::HashSet<String>> {
        self.bot_names
            .read()
            .unwrap_or_else(std::sync::PoisonError::into_inner)
            .clone()
    }

    /// The bias snapshot the next lobby's fill policy reads (refreshed after every recorded run).
    fn learning_snapshot(&self) -> Option<rh_policy::bots::learning::LearnedBias> {
        self.learning
            .lock_recover()
            .as_ref()
            .map(|sink| sink.snapshot())
    }

    /// The record caller (source BotManager.onResult): a resolved fill bot's run goes to the learning store with the
    /// facts the room has, the seat policy's own choice keys (a Brain seat's opener/second/spawn/boon/camp; none for
    /// HumanBot) and the manager's faction key.
    fn record_bot_result(
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
    fn fill_policy(&self, fill_bots_to: usize) -> Option<crate::bot_fill::BotFillConfig> {
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
    fn spawn_room(
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
    fn start_renewals(
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

    /// Unwinds an admission that took no seat: its room reservation goes, and its own entry (`gen`) is removed at once when
    /// no lease was taken, or stays `Leaving` until the lease give-back lands (so a retry, or another connection for the
    /// profile, waits for the give-back instead of racing it).
    fn unwind(&self, key: &ProfileKey, gen: u64, match_id: MatchId, fence: Option<i64>) {
        let mut inner = self.inner.lock_recover();
        // Hosted mode: an admission that took no seat gives the admit back (pending again, same deadline).
        inner.control.restore_gen(gen);
        let own = inner
            .profiles
            .get(key)
            .is_some_and(|s| s.gen == gen && s.match_id == match_id);
        if own {
            if let Some(r) = inner.rooms.get_mut(&match_id) {
                r.reserved = r.reserved.saturating_sub(1);
            }
        }
        match fence {
            None => {
                if own {
                    inner.profiles.remove(key);
                    drop(inner);
                    self.freed.notify_waiters();
                }
            }
            Some(fence) => {
                if let Some(s) = inner.profiles.get_mut(key).filter(|s| s.gen == gen) {
                    s.state = SeatState::Leaving;
                    s.stop_renewing();
                    s.lease = LeaseState::Releasing;
                }
                drop(inner);
                self.give_back(key, match_id, fence, own.then_some(gen));
            }
        }
    }

    /// Moves a `Leaving` entry on (the lock is held): nothing while a result is due or a write is in flight (the write
    /// releases the lease itself); then the lease is given back unless a write already released it, and the entry is
    /// freed when that lands.
    fn advance(&self, inner: &mut Inner, key: &ProfileKey) {
        let Some(s) = inner.profiles.get_mut(key) else {
            return;
        };
        if s.state != SeatState::Leaving || s.result_due || s.writes > 0 {
            return;
        }
        match s.lease {
            LeaseState::Held => {
                s.stop_renewing();
                s.lease = LeaseState::Releasing;
                let (fence, gen) = (s.fence, s.gen);
                self.give_back(key, s.match_id, fence, Some(gen));
            }
            LeaseState::Gone => {
                inner.profiles.remove(key);
                self.freed.notify_waiters();
            }
            LeaseState::Releasing => {}
        }
    }

    /// Gives a lease back off the asking task (no-op in memory), then tells `owner`'s entry (if it is still that entry).
    /// Best effort: an unreleased lease only expires later. Releasing by fence is harmless when the lease moved on.
    fn give_back(&self, key: &ProfileKey, match_id: MatchId, fence: i64, owner: Option<u64>) {
        let Some(d) = self.me.upgrade() else {
            return;
        };
        let key = key.clone();
        tokio::spawn(async move {
            let _ = d.persistence.release_live(&key, match_id, fence).await;
            if let Some(gen) = owner {
                d.free(&key, gen);
            }
        });
    }

    async fn report_loop(this: Arc<Self>, mut rx: mpsc::UnboundedReceiver<RoomReport>) {
        while let Some(r) = rx.recv().await {
            match r {
                RoomReport::Left {
                    admission,
                    match_id,
                    profile,
                    fence,
                    result_pending,
                } => {
                    let key =
                        ProfileKey::from_report(&profile, admission.and_then(|a| a.profile_id));
                    let mut inner = this.inner.lock_recover();
                    let seat = inner
                        .profiles
                        .get(&key)
                        .filter(|s| {
                            s.match_id == match_id
                                && s.fence == fence
                                && admission.is_none_or(|a| a.gen == s.gen)
                        })
                        .map(|s| s.state);
                    match seat {
                        // This seat is already being let go (cannot happen twice; ignored rather than released twice).
                        Some(SeatState::Leaving) => {}
                        Some(_) => {
                            if let Some(room) = inner.rooms.get_mut(&match_id) {
                                room.seated = room.seated.saturating_sub(1);
                            }
                            if let Some(s) = inner.profiles.get_mut(&key) {
                                // The profile stays reserved (`Leaving`) until its result is written or, with no result
                                // to come or in flight, until its lease is given back: a quick rejoin must not race the
                                // durable lease.
                                s.state = SeatState::Leaving;
                                s.result_due = result_pending;
                            }
                            this.advance(&mut inner, &key);
                        }
                        // No entry for this seat (never recorded, or another seat's): the fenced give-back is still owed,
                        // and it cannot touch a newer lease.
                        None => {
                            drop(inner);
                            this.give_back(&key, match_id, fence, None);
                        }
                    }
                }
                RoomReport::Result {
                    admission,
                    match_id,
                    profile,
                    bot,
                    bot_spec,
                    bot_choices,
                    fence,
                    result,
                    roster,
                } => {
                    if bot {
                        // Bots never touch profile persistence (legacy rule), but a resolved fill bot's run is the
                        // learning store's record (source BotManager.onResult), with the seat's own choices.
                        if let Some(spec) = &bot_spec {
                            this.record_bot_result(spec, &result, &bot_choices);
                        }
                        continue;
                    }
                    let key =
                        ProfileKey::from_report(&profile, admission.and_then(|a| a.profile_id));
                    // The seat it belongs to counts the write as in flight: its lease is not given back meanwhile.
                    let (fence, owner) = {
                        let mut inner = this.inner.lock_recover();
                        match inner.profiles.get_mut(&key).filter(|s| {
                            s.match_id == match_id
                                && fence.is_none_or(|f| f == s.fence)
                                && admission.is_none_or(|a| a.gen == s.gen)
                        }) {
                            Some(s) => {
                                s.writes += 1;
                                s.result_due = false;
                                (s.fence, Some(s.gen))
                            }
                            None => (fence.unwrap_or(0), None),
                        }
                    };
                    let d = this.clone();
                    // Durable I/O never runs on a room task: retried here with capped backoff for about two minutes
                    // (a database blip must not lose a result), idempotent by (match, profile); the process waits for
                    // in-flight writes before exiting (main.rs, `wait_for_writes`). A refusal for good is not retried.
                    d.pending_writes
                        .fetch_add(1, std::sync::atomic::Ordering::SeqCst);
                    tokio::spawn(async move {
                        // Whether the lease is gone after the write: released by its transaction, or no longer ours.
                        let lease_gone = d
                            .write_result(match_id, &profile, fence, &result, roster, key.id())
                            .await;
                        let mut inner = d.inner.lock_recover();
                        match owner {
                            Some(gen) => {
                                let mut mine = false;
                                if let Some(s) =
                                    inner.profiles.get_mut(&key).filter(|s| s.gen == gen)
                                {
                                    s.writes -= 1;
                                    if lease_gone && s.lease == LeaseState::Held {
                                        s.stop_renewing();
                                        s.lease = LeaseState::Gone;
                                    }
                                    mine = true;
                                }
                                // A profile that left is free once this lands (and gives its lease back first if the
                                // write gave up).
                                if mine {
                                    d.advance(&mut inner, &key);
                                }
                            }
                            None if !lease_gone => {
                                drop(inner);
                                d.give_back(&key, match_id, fence, None);
                            }
                            None => {}
                        }
                        d.pending_writes
                            .fetch_sub(1, std::sync::atomic::Ordering::SeqCst);
                    });
                }
                RoomReport::Closed {
                    match_id,
                    stats,
                    replay,
                    ..
                } => {
                    let mut inner = this.inner.lock_recover();
                    inner.rooms.remove(&match_id);
                    inner.sessions.retain(|_, m| *m != match_id);
                    // A room that closed normally reported every result and every seat's `Left` before this. A crashed
                    // one did not: its seats are let go here, and a result it still owed will never come. Reservations
                    // are their admissions' to unwind (an admission whose reply arrives after this finds the room gone).
                    let mut moved: Vec<ProfileKey> = Vec::new();
                    for (k, s) in inner.profiles.iter_mut() {
                        if s.match_id != match_id {
                            continue;
                        }
                        match s.state {
                            SeatState::Reserved => {}
                            SeatState::Seated => {
                                s.state = SeatState::Leaving;
                                moved.push(k.clone());
                            }
                            SeatState::Leaving if s.result_due => {
                                s.result_due = false;
                                moved.push(k.clone());
                            }
                            SeatState::Leaving => {}
                        }
                    }
                    let freed_seats = moved.len();
                    for k in moved {
                        this.advance(&mut inner, &k);
                    }
                    let stats_crashed = stats.crashed;
                    if stats.crashed {
                        let record = CrashRecord {
                            room_id: crate::control::room_id_hex(match_id),
                            at_unix_ms: std::time::SystemTime::now()
                                .duration_since(std::time::UNIX_EPOCH)
                                .map_or(0, |d| d.as_millis() as u64),
                            ticks: stats.ticks,
                            seats_freed: freed_seats,
                        };
                        inner.rooms_crashed += 1;
                        // One structured line at error level per crash; `rooms_crashed` in /control/health is the alert hook.
                        eprintln!(
                            "[server] ERROR event=room_crashed room={} ticks={} seats_freed={} crashed_total={}",
                            record.room_id, record.ticks, record.seats_freed, inner.rooms_crashed
                        );
                        inner.last_crash = Some(record);
                    }
                    inner.closed.push(stats);
                    if inner.closed.len() > 64 {
                        inner.closed.remove(0);
                    }
                    if let Some(r) = replay {
                        inner.replays.push(*r);
                        if inner.replays.len() > 4 {
                            inner.replays.remove(0);
                        }
                    }
                    let draining = this.lifecycle.draining();
                    inner.control.on_closed(
                        match_id,
                        stats_crashed,
                        draining,
                        tokio::time::Instant::now(),
                    );
                    let empty = inner.rooms.is_empty();
                    drop(inner);
                    if empty {
                        this.lifecycle.rooms_empty();
                    }
                }
                RoomReport::Seated {
                    match_id,
                    phase,
                    humans,
                } => {
                    let mut inner = this.inner.lock_recover();
                    if let Some(r) = inner.rooms.get_mut(&match_id) {
                        // The admission that took this seat stops being a reservation; the others still are.
                        r.reserved = r.reserved.saturating_sub(1);
                        r.seated = humans;
                        r.phase = phase;
                        r.public.on_phase(phase);
                    }
                    inner.control.on_phase(match_id, phase);
                }
                RoomReport::Seat {
                    match_id,
                    profile,
                    event,
                } => {
                    let took = {
                        let mut inner = this.inner.lock_recover();
                        if let Some(r) = inner.rooms.get_mut(&match_id) {
                            r.public.on_seat(&profile, event);
                        }
                        inner.control.on_seat(match_id, &profile, event)
                    };
                    if let Some(took) = took {
                        crate::metrics::process().note_admit_to_seat(took);
                    }
                }
                RoomReport::BotSeat {
                    match_id,
                    name,
                    seated,
                } => {
                    let mut inner = this.inner.lock_recover();
                    if let Some(r) = inner.rooms.get_mut(&match_id) {
                        let bots = &mut r.public.bots;
                        if seated {
                            bots.push(name);
                        } else if let Some(i) = bots.iter().position(|b| *b == name) {
                            bots.remove(i);
                        }
                    }
                }
                RoomReport::Phase { match_id, phase } => {
                    let mut inner = this.inner.lock_recover();
                    if let Some(r) = inner.rooms.get_mut(&match_id) {
                        r.phase = phase;
                        r.public.on_phase(phase);
                    }
                    inner.control.on_phase(match_id, phase);
                }
            }
        }
    }

    /// Never offer this room to an admission again (its lobby ended or its task is gone; its `Closed` report removes it).
    fn mark_unjoinable(&self, match_id: MatchId) {
        if let Some(r) = self.inner.lock_recover().rooms.get_mut(&match_id) {
            r.phase = Phase::Active;
        }
    }

    /// A give-back for `gen`'s entry landed: its lease is gone, and the entry is freed (and the admissions waiting for the
    /// profile woken) once nothing else is pending for it: never while a result write is still in flight. An entry a newer
    /// admission owns is not touched.
    fn free(&self, key: &ProfileKey, gen: u64) {
        let mut inner = self.inner.lock_recover();
        let mine = match inner.profiles.get_mut(key) {
            Some(s) if s.gen == gen => {
                s.stop_renewing();
                s.lease = LeaseState::Gone;
                true
            }
            _ => false,
        };
        if mine {
            self.advance(&mut inner, key);
        }
        drop(inner);
        self.freed.notify_waiters();
    }

    /// Waits, until `deadline` at the latest, while `key` is a `Leaving` profile. Returns at once for a free profile and
    /// for one with a live seat or an admission in flight (the caller refuses those: concurrent ownership is never
    /// weakened).
    async fn wait_while_leaving(&self, key: &ProfileKey, deadline: tokio::time::Instant) {
        loop {
            // Register for the wake-up before looking, so a free between the look and the wait is not missed.
            let notified = self.freed.notified();
            tokio::pin!(notified);
            notified.as_mut().enable();
            let leaving = self
                .inner
                .lock_recover()
                .profiles
                .get(key)
                .is_some_and(|s| s.state == SeatState::Leaving);
            if !leaving || tokio::time::timeout_at(deadline, notified).await.is_err() {
                return;
            }
        }
    }

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
        let mut inner = self.inner.lock_recover();
        // Re-checked under the lock: `drain` flips the lifecycle first and only then reads the rooms under this same lock, so
        // a create that still sees `ready` here has its room in place before drain looks (drain then waits for it), and a
        // create that comes later is refused. Without this a session created during a drain could outlive `Terminated`.
        match self.lifecycle.state_name() {
            "ready" => {}
            "starting" => return Err(CtlError::new(503, "not_ready", "the server is starting")),
            _ => return Err(CtlError::conflict("draining", "the server is draining")),
        }
        inner.control.sweep(tokio::time::Instant::now());
        match inner.control.check_create(gsid, max_players, fill)? {
            crate::control::CreateCheck::Existing {
                room,
                phase,
                max_players,
            } => Ok(serde_json::json!({
                "v": crate::control::API_VERSION, "created": false, "gameSessionId": gsid,
                "roomId": crate::control::room_id_hex(room), "phase": phase.as_str(), "maxPlayers": max_players,
            })),
            crate::control::CreateCheck::New => {
                if inner.rooms.len() >= self.cfg.max_rooms {
                    return Err(CtlError::conflict("at_capacity", "no room capacity left"));
                }
                let (id, _tx) = self.spawn_room(&mut inner, fill);
                inner.control.insert(gsid, id, max_players, fill);
                Ok(serde_json::json!({
                    "v": crate::control::API_VERSION, "created": true, "gameSessionId": gsid,
                    "roomId": crate::control::room_id_hex(id), "phase": "lobby", "maxPlayers": max_players,
                }))
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

    /// Recorded replays of the last closed rooms (private: they hold match secrets).
    pub fn take_replays(&self) -> Vec<rh_sim::replay::Replay> {
        std::mem::take(&mut self.inner.lock_recover().replays)
    }

    /// Writes one result off the room task and says whether the lease is gone afterwards (released by the write's own
    /// transaction, or no longer ours). With a spool the record is made durable first (see `spool.rs`); without one, or
    /// when the spool is full or unwritable, this is the in-memory retry that predates the spool.
    async fn write_result(
        self: &Arc<Self>,
        match_id: MatchId,
        profile: &str,
        fence: i64,
        result: &rh_sim::RunResult,
        roster: crate::room::Roster,
        durable_id: Option<i64>,
    ) -> bool {
        #[cfg(feature = "postgres")]
        if let Some((name, rec, spool)) = self
            .spool_result(match_id, profile, fence, result, roster, durable_id)
            .await
        {
            return match self.drive_spooled(&spool, &name, &rec, false).await {
                // The write released the lease itself.
                SpoolEnd::Committed { applied: true } => true,
                // A duplicate: the ordinary fenced give-back releases the lease (a no-op if it moved on).
                SpoolEnd::Committed { applied: false } => false,
                // Refused for good. The lease is deliberately NOT released: a release would make a
                // later operator replay of the dead letter impossible, and an unreleased lease only expires.
                SpoolEnd::DeadLettered => true,
            };
        }
        self.write_unspooled(match_id, profile, fence, result, roster, durable_id)
            .await
    }

    async fn write_unspooled(
        self: &Arc<Self>,
        match_id: MatchId,
        profile: &str,
        fence: i64,
        result: &rh_sim::RunResult,
        roster: crate::room::Roster,
        durable_id: Option<i64>,
    ) -> bool {
        let d = self;
        let key_id = durable_id;
        let mut delay = Duration::from_millis(100);
        let mut lease_gone = false;
        for attempt in 0..RESULT_ATTEMPTS {
            match d
                .persistence
                .record_with_id(match_id, profile, fence, result, roster, key_id)
                .await
            {
                Ok(Recorded::Applied) => {
                    d.inner.lock_recover().results_ok += 1;
                    lease_gone = true;
                    break;
                }
                // A duplicate report: the store answers before it looks at the lease, so the lease the
                // caller holds (if any) is still live. It is not marked gone: the seat's ordinary
                // fenced give-back releases it (a no-op when the lease already moved on).
                Ok(Recorded::AlreadyApplied) => {
                    d.inner.lock_recover().results_ok += 1;
                    break;
                }
                Err(RecordError::Refused(e) | RecordError::Permanent(e)) => {
                    eprintln!("[results] {profile}: refused by the store, not retried: {e}");
                    d.inner.lock_recover().results_refused += 1;
                    lease_gone = true;
                    break;
                }
                Err(e) if attempt == RESULT_ATTEMPTS - 1 => {
                    eprintln!("[results] giving up on {profile}: {e}");
                    d.inner.lock_recover().results_failed += 1;
                }
                Err(_) => {
                    tokio::time::sleep(delay).await;
                    delay = (delay * 2).min(Duration::from_secs(5));
                }
            }
        }
        lease_gone
    }

    /// Result-handoff counters for `/control/health` (loopback only; `/info` and its pinned key set are unchanged).
    /// `spooled`: fsynced locally (survives a process restart on this host). `committed`: in PostgreSQL (the only
    /// host-loss-durable point; the same count as `resultsOk` in `/info`).
    pub fn result_metrics(&self) -> serde_json::Value {
        let inner = self.inner.lock_recover();
        serde_json::json!({
            "spooled": inner.results_spooled,
            "committed": inner.results_ok,
            "recovered": inner.results_recovered,
            "refused": inner.results_refused,
            "failed": inner.results_failed,
            "deadLettered": inner.results_dead_lettered,
            "stuckAlerts": inner.results_stuck_alerts,
            "deadOverflow": self.spool_dead_overflow(),
            "spoolFull": inner.spool_full,
            "spoolErrors": inner.spool_errors,
            "spoolPending": self.spool_pending(),
            "spoolDead": self.spool_dead(),
        })
    }

    #[cfg(feature = "postgres")]
    fn spool_pending(&self) -> usize {
        self.spool.as_ref().map_or(0, |s| s.stats().pending)
    }
    #[cfg(feature = "postgres")]
    fn spool_dead(&self) -> usize {
        self.spool.as_ref().map_or(0, |s| s.stats().dead)
    }
    #[cfg(feature = "postgres")]
    fn spool_dead_overflow(&self) -> u64 {
        self.spool
            .as_ref()
            .map_or(0, |s| s.stats().dead_overflow_total)
    }
    #[cfg(not(feature = "postgres"))]
    fn spool_dead_overflow(&self) -> u64 {
        0
    }
    #[cfg(not(feature = "postgres"))]
    fn spool_pending(&self) -> usize {
        0
    }
    #[cfg(not(feature = "postgres"))]
    fn spool_dead(&self) -> usize {
        0
    }

    /// Makes the result durable locally before the first database attempt. `None` means no spool record exists (no
    /// spool, no durable identity, full, or unwritable) and the caller retries in memory only: the result is then in
    /// the wider loss window, which is counted and logged, never hidden.
    #[cfg(feature = "postgres")]
    async fn spool_result(
        &self,
        match_id: MatchId,
        profile: &str,
        fence: i64,
        result: &rh_sim::RunResult,
        roster: crate::room::Roster,
        durable_id: Option<i64>,
    ) -> Option<(
        String,
        crate::spool::SpoolRecord,
        Arc<crate::spool::ResultSpool>,
    )> {
        let spool = self.spool.clone()?;
        let id = durable_id?;
        let meta = self.persistence.spool_meta(roster)?;
        let rec = crate::spool::SpoolRecord::new(
            uuid::Uuid::from_bytes(match_id),
            id,
            profile,
            fence,
            meta,
            crate::persist::pg::to_persist(result),
        );
        let name = rec.file_name();
        let (s2, r2) = (spool.clone(), rec.clone());
        let appended = tokio::task::spawn_blocking(move || s2.append(&r2)).await;
        match appended {
            Ok(Ok(_)) => {
                self.inner.lock_recover().results_spooled += 1;
                eprintln!(
                    "[results] spooled match={} profile_id={id} fence={fence} (durable on this host only; not yet committed)",
                    rec.match_id
                );
                Some((name, rec, spool))
            }
            Ok(Err(crate::spool::SpoolError::Full)) => {
                self.inner.lock_recover().spool_full += 1;
                eprintln!(
                    "[results] spool FULL: match={} profile_id={id} is retried in memory only",
                    rec.match_id
                );
                None
            }
            other => {
                self.inner.lock_recover().spool_errors += 1;
                let why = match other {
                    Ok(Err(e)) => e.to_string(),
                    Err(e) => e.to_string(),
                    Ok(Ok(_)) => unreachable!(),
                };
                eprintln!("[results] spool append failed ({why}): match={} profile_id={id} is retried in memory only", rec.match_id);
                None
            }
        }
    }

    /// Replays one spooled record through the fenced store path until it commits or is refused for good. A TRANSIENT
    /// error is retried forever at `backoff_max` (a database outage of any length must not lose a result; the profile
    /// gate stays held, which costs nothing because admission needs the same database). `retry_deadline` is only an
    /// alert threshold: past it the record is logged and counted as stuck, repeatedly. The record is removed only after
    /// the commit (or a definitive `AlreadyApplied`); a definitive refusal ([`RecordError::Refused`] or
    /// [`RecordError::Permanent`]) is dead-lettered. Never acquires, renews or releases a lease.
    #[cfg(feature = "postgres")]
    async fn drive_spooled(
        &self,
        spool: &Arc<crate::spool::ResultSpool>,
        name: &str,
        rec: &crate::spool::SpoolRecord,
        recovered: bool,
    ) -> SpoolEnd {
        let started = tokio::time::Instant::now();
        let alert_every = spool.cfg.retry_deadline.max(Duration::from_millis(1));
        let mut next_alert = started + alert_every;
        let mut delay = spool.cfg.backoff_initial;
        loop {
            let attempt = {
                let _permit = self.spool_permits.acquire().await.expect("semaphore");
                self.persistence.record_spooled(rec).await
            };
            let dead = |reason: String| {
                let (s, n) = (spool.clone(), name.to_owned());
                async move {
                    let r = reason.clone();
                    match tokio::task::spawn_blocking(move || s.dead_letter(&n, &r)).await {
                        Ok(Ok(out)) => out,
                        Ok(Err(e)) => {
                            eprintln!("[results] could not dead-letter record: {e}");
                            crate::spool::DeadOutcome::Overflow
                        }
                        Err(_) => crate::spool::DeadOutcome::Overflow,
                    }
                }
            };
            match attempt {
                Ok(done) => {
                    let (s, n) = (spool.clone(), name.to_owned());
                    if let Ok(Err(e)) = tokio::task::spawn_blocking(move || s.remove(&n)).await {
                        // The result IS committed; a leftover record is replayed as AlreadyApplied and cleared.
                        eprintln!("[results] committed but could not remove its spool record: {e}");
                    }
                    {
                        let mut inner = self.inner.lock_recover();
                        inner.results_ok += 1;
                        if recovered {
                            inner.results_recovered += 1;
                        }
                    }
                    eprintln!(
                        "[results] committed match={} profile_id={} ({}{})",
                        rec.match_id,
                        rec.profile_id,
                        if done == Recorded::Applied {
                            "applied"
                        } else {
                            "already applied"
                        },
                        if recovered {
                            ", recovered from spool"
                        } else {
                            ""
                        }
                    );
                    return SpoolEnd::Committed {
                        applied: done == Recorded::Applied,
                    };
                }
                Err(e @ (RecordError::Refused(_) | RecordError::Permanent(_))) => {
                    let permanent = matches!(e, RecordError::Permanent(_));
                    let why = match &e {
                        RecordError::Refused(w) => format!("refused by the store: {w}"),
                        RecordError::Permanent(w) => format!("permanent store error: {w}"),
                        RecordError::Transient(_) => unreachable!(),
                    };
                    let out = dead(why.clone()).await;
                    {
                        let mut inner = self.inner.lock_recover();
                        if permanent {
                            inner.results_failed += 1;
                        } else {
                            inner.results_refused += 1;
                        }
                        if out == crate::spool::DeadOutcome::Moved {
                            inner.results_dead_lettered += 1;
                        }
                    }
                    eprintln!(
                        "[results] DEAD-LETTERED match={} profile_id={}: {why}; never retried{}",
                        rec.match_id,
                        rec.profile_id,
                        if out == crate::spool::DeadOutcome::Overflow {
                            " (dead-letter directory full or unwritable: the record stays pending)"
                        } else {
                            ""
                        }
                    );
                    return SpoolEnd::DeadLettered;
                }
                Err(RecordError::Transient(e)) => {
                    let now = tokio::time::Instant::now();
                    if now >= next_alert {
                        next_alert = now + alert_every;
                        self.inner.lock_recover().results_stuck_alerts += 1;
                        eprintln!(
                            "[results] STUCK match={} profile_id={}: still not committed after {}s ({e}); keeps retrying every {}s at most, never dead-lettered for time",
                            rec.match_id,
                            rec.profile_id,
                            started.elapsed().as_secs(),
                            spool.cfg.backoff_max.as_secs().max(1)
                        );
                    }
                    tokio::time::sleep(delay).await;
                    delay = (delay * 2).min(spool.cfg.backoff_max);
                }
            }
        }
    }

    /// Startup recovery: replays every spooled record left by an earlier process through the SAME fenced path with its
    /// original fence, identity and metadata. Call once, before the listener accepts admissions. Each profile with a
    /// pending record is held `Leaving` (no lease state) until its records resolve, so an admission in this process cannot
    /// race the replay; recovery itself takes, renews and releases no lease. Records the store refuses (a newer fence,
    /// a released lease) are dead-lettered, never retried forever. Returns how many records were found. Blocking scan,
    /// then background tasks: a database outage does not delay startup or any tick.
    #[cfg(feature = "postgres")]
    pub fn recover_spooled(self: &Arc<Self>) -> usize {
        let Some(spool) = self.spool.clone() else {
            return 0;
        };
        let records = spool.scan();
        let dead_on_scan = spool.stats().dead_total;
        if dead_on_scan > 0 {
            self.inner.lock_recover().results_dead_lettered += dead_on_scan;
        }
        if !records.is_empty() {
            eprintln!(
                "[results] recovering {} spooled result(s) from {}",
                records.len(),
                spool.dir().display()
            );
        }
        let n = records.len();
        for (name, rec) in records {
            let key = ProfileKey::Durable(rec.profile_id);
            let match_id = rec.match_uuid().map(|u| *u.as_bytes()).unwrap_or([0; 16]);
            let gen = {
                let mut inner = self.inner.lock_recover();
                inner.gens += 1;
                let fresh = inner.gens;
                let seat = inner.profiles.entry(key.clone()).or_insert(ProfileSeat {
                    renewal: None,
                    gen: fresh,
                    match_id,
                    fence: rec.fence,
                    state: SeatState::Leaving,
                    result_due: false,
                    writes: 0,
                    lease: LeaseState::Gone,
                });
                seat.writes += 1;
                seat.gen
            };
            let spool = spool.clone();
            let d = self.clone();
            d.pending_writes
                .fetch_add(1, std::sync::atomic::Ordering::SeqCst);
            tokio::spawn(async move {
                d.drive_spooled(&spool, &name, &rec, true).await;
                let mut inner = d.inner.lock_recover();
                let mut mine = false;
                if let Some(s) = inner.profiles.get_mut(&key).filter(|s| s.gen == gen) {
                    s.writes -= 1;
                    mine = true;
                }
                if mine {
                    d.advance(&mut inner, &key);
                }
                drop(inner);
                d.pending_writes
                    .fetch_sub(1, std::sync::atomic::Ordering::SeqCst);
            });
        }
        n
    }

    /// Waits (at most `max`) until no result write is in flight. Called before the process exits.
    pub async fn wait_for_writes(&self, max: Duration) -> bool {
        let t0 = tokio::time::Instant::now();
        while self
            .pending_writes
            .load(std::sync::atomic::Ordering::SeqCst)
            > 0
        {
            if t0.elapsed() >= max {
                return false;
            }
            tokio::time::sleep(Duration::from_millis(50)).await;
        }
        true
    }

    pub fn idle_timeout(&self) -> Duration {
        self.cfg.idle_timeout
    }

    pub fn room_count(&self) -> usize {
        self.inner.lock_recover().rooms.len()
    }
}

/// Reserves one slot in a room's command mailbox (bounded wait). `Ok(None)`: the room is already gone, so there is nobody to
/// tell and the book's own close handling covers the seats. `Err`: the mailbox stayed full, 503 (retry-safe: nothing has
/// changed yet).
pub(crate) async fn reserve_room_slot(
    tx: &tokio::sync::mpsc::Sender<RoomCmd>,
    wait: Duration,
) -> Result<Option<tokio::sync::mpsc::Permit<'_, RoomCmd>>, CtlError> {
    match tokio::time::timeout(wait, tx.reserve()).await {
        Ok(Ok(p)) => Ok(Some(p)),
        Ok(Err(_closed)) => Ok(None),
        Err(_) => Err(CtlError::new(
            503,
            "room_busy",
            "the room is not accepting commands right now; retry shortly",
        )),
    }
}

#[cfg(test)]
mod tests;
