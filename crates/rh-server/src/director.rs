//! The match director: admission (protocol, content, credentials, profile ownership, capacity), room placement, session
//! resume, and the off-tick handling of what rooms report (results to durable storage with retries, freed profiles).
//! Several matches run in one process (one tokio task each); the hosting layer decides how many processes run per host.

mod admission;
mod control_api;
mod info;
mod reports;
mod results;
mod spawn;

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
    /// Private quest-board match secrets for deterministic in-process test rigs. The server binary leaves this unset.
    pub fixed_board_secrets: Option<rh_sim::Secrets>,
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
            fixed_board_secrets: None,
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
