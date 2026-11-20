//! One match: a task that owns its [`Sim`], its seats and its pacer. Everything that touches the simulation happens on this
//! task, in arrival order, between steps (the legacy rule: handlers validate and enqueue, the sim consumes at the next step).
//! Nothing here blocks on I/O: frames go to per-connection [`Outbox`]es, results to a channel drained off-tick.

// The file the packet names as `src/rtt.rs`; declared here because `lib.rs` is outside this phase's write scope.
#[path = "rtt.rs"]
pub mod rtt;

mod admission;
mod bots;
mod commands;
mod finish;
mod step;
mod task;

pub use self::task::run_room;

use crate::bot_fill::{BotFillConfig, BotFillState, BotSeatSpec, FillView};
use crate::metrics::{self, Histogram, Metrics};
use crate::outbox::{Outbox, OutboxCounters};
use crate::pacer::Pacer;
use crate::wire;
use rh_policy::bots::delay::DelayLine;
use rh_policy::bots::random::Rng;
use rh_policy::bots::{BrainBot, HumanBot};
use rh_policy::{drive_seats_queued, scripted, MatchStart, Policy, Seat};
use rh_protocol::map::EncodedMap;
use rh_protocol::{encode_welcome, RejectCode, ServerMsg, WelcomeHeader, WireEvent, WireResult};
use rh_sim::replay::{BotPolicy, Recorder, Replay, ReplayInput, ReplayStart};
use rh_sim::{
    Arena, Event, FactionId, Phase, PlayerId, RunResult, Secrets, SeenEvent, Sim, SimConfig,
};
use rtt::{clamp_render_time_noted_at, RewindClampStats, RttEstimator};
use std::sync::atomic::{AtomicU64, Ordering};
use std::sync::Arc;
use std::time::Duration;
use tokio::sync::{mpsc, oneshot};
use tokio::time::Instant;

/// Seconds a dropped player's seat is held for a reconnect (legacy reconnect grace).
pub const RECONNECT_GRACE: Duration = Duration::from_secs(10);
/// A finished match stays up this long so clients receive the end and their results.
pub const LINGER: Duration = Duration::from_secs(5);
pub const INBOUND_CAP: usize = 2048;
/// A room nobody has joined yet waits this long for its first player (admission may still be acquiring a durable lease)
/// before closing itself.
pub const UNCLAIMED_TIMEOUT: Duration = Duration::from_secs(30);
/// A training room whose seat accepted no input for this long closes itself (legacy trainingIdle: the practice client gave
/// up; the room has no run to settle and nothing to bank, so it just closes).
pub const TRAINING_IDLE_SECONDS: u64 = 300;

pub type MatchId = [u8; 16];

/// The canonical UUID text of a match id (the form the durable store and replay headers use).
pub fn match_id_string(id: MatchId) -> String {
    let h: String = id.iter().map(|b| format!("{b:02x}")).collect();
    format!(
        "{}-{}-{}-{}-{}",
        &h[0..8],
        &h[8..12],
        &h[12..16],
        &h[16..20],
        &h[20..32]
    )
}

/// Who was in a match, for its durable record (`matches.humans` / `matches.bots`): humans that ever took a seat, bots
/// that were placed.
#[derive(Clone, Copy, Debug, Default, PartialEq, Eq)]
pub struct Roster {
    pub humans: u32,
    pub bots: u32,
}

pub struct JoinOk {
    pub player: PlayerId,
    pub session: [u8; 16],
    pub welcome: Vec<u8>,
    /// The room's latest simulation time (f64 bits), for Pong.
    pub clock: Arc<AtomicU64>,
}

/// The admission that produced a seat. Durable identity and generation survive
/// resume, departure, and deferred reports; callsigns are only display names.
#[derive(Clone, Copy, Debug)]
pub struct Admission {
    pub gen: u64,
    pub profile_id: Option<i64>,
}

pub enum RoomCmd {
    /// `conn` identifies the connection (unique per process); `fence` is the profile's session-lease fence.
    Join {
        profile: String,
        faction: Option<FactionId>,
        outbox: Arc<Outbox>,
        conn: u64,
        fence: i64,
        reply: oneshot::Sender<Result<JoinOk, RejectCode>>,
    },
    /// A durable admission. This deadline belongs to this exact seat, including after any resume.
    JoinLeased {
        admission: Admission,
        profile: String,
        faction: Option<FactionId>,
        outbox: Arc<Outbox>,
        conn: u64,
        fence: i64,
        deadline: tokio::sync::watch::Receiver<Instant>,
        reply: oneshot::Sender<Result<JoinOk, RejectCode>>,
    },
    Resume {
        session: [u8; 16],
        outbox: Arc<Outbox>,
        conn: u64,
        reply: oneshot::Sender<Result<JoinOk, RejectCode>>,
    },
    /// Commands from a connection that is no longer the seat's (it was replaced by a resume) are ignored.
    Input {
        player: PlayerId,
        conn: u64,
        seq: u32,
        commands: Vec<rh_protocol::WireCommand>,
        /// When the connection task decoded the frame (telemetry only: input age at the step that applies it).
        received: std::time::Instant,
    },
    /// One server-measured RTT sample (a Pong matching a Ping this connection was sent), for the seat's rewind window.
    /// Samples from a connection that no longer owns the seat are ignored.
    RttSample {
        player: PlayerId,
        conn: u64,
        rtt_ms: f64,
    },
    Disconnected {
        player: PlayerId,
        conn: u64,
    },
    Leave {
        player: PlayerId,
        conn: u64,
    },
    Drain,
    /// The hosting layer revoked this seat (control API `leave` mode `kick`): the same path as a disconnect past the grace.
    /// The profile is matched ASCII case-insensitively; an unknown profile is ignored.
    Kick {
        profile: String,
    },
    /// The hosting layer ends the room now (control API `end` mode `abort`): seated players are closed out and their seats
    /// and leases freed, and no result is invented for an interrupted match.
    Abort,
    /// The process drain reached its maximum duration (`--drain-max-secs`): every seated human is closed out as if it had
    /// left (a player still standing in a run is MIA, so the result it earned is reported and written under its fence),
    /// then the room closes. Unlike `Abort`, results are kept.
    DrainEnd,
    /// Tests: the room task panics (the crash path).
    #[cfg(test)]
    Crash,
}

/// What the room reports to its owner (the director): free a profile, a result to persist, the room closed.
#[derive(Debug)]
pub enum RoomReport {
    /// A human left its seat. `result_pending`: it left a run it was still standing in, so its (MIA) result follows in a
    /// `Result` report; the director keeps the profile reserved until that result is written.
    Left {
        admission: Option<Admission>,
        match_id: MatchId,
        profile: String,
        fence: i64,
        result_pending: bool,
    },
    /// `fence`: the session-lease fence the result was earned under (None for a bot).
    Result {
        admission: Option<Admission>,
        match_id: MatchId,
        profile: String,
        bot: bool,
        /// A bot's fill spec when it is a managed fill bot (the learning record caller's tier/persona/faction); None
        /// for a human or an unmanaged bot.
        bot_spec: Option<BotSeatSpec>,
        /// A bot seat's own choice keys for the learning record (`Policy::finish`); empty for a human or a policy that
        /// keeps none.
        bot_choices: Vec<String>,
        fence: Option<i64>,
        result: RunResult,
        roster: Roster,
    },
    Closed {
        match_id: MatchId,
        stats: RoomStats,
        final_hash: u64,
        replay: Option<Box<Replay>>,
    },
    /// A human took a seat (its Join reply follows this report): `humans` is the room's seated count. The director counts
    /// the admission as seated from here on; until then it was only a reservation (an admission that may still be
    /// acquiring its durable lease).
    Seated {
        match_id: MatchId,
        phase: Phase,
        humans: usize,
    },
    /// The room's phase changed (a lobby that closed into a run must stop receiving joins).
    Phase { match_id: MatchId, phase: Phase },
    /// One human's seat changed state, for the control API's player list (docs/hosting/CONTROL_API.md section 3.4). Sent
    /// in the same channel as `Left`, so it is ordered with the seat's other reports. It carries no lease logic.
    Seat {
        match_id: MatchId,
        profile: String,
        event: SeatEvent,
    },
    /// A bot took (`seated`) or gave up its seat, by callsign: only the public lobby listing reads it (no lease logic).
    BotSeat {
        match_id: MatchId,
        name: String,
        seated: bool,
    },
}

/// What happened to a human's seat (see [`RoomReport::Seat`]).
#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub enum SeatEvent {
    Seated,
    /// The connection dropped at `at`; the seat is held for the reconnect grace.
    Away {
        at: Instant,
    },
    /// A resume (the 16-byte token) re-attached a connection inside the grace.
    Back,
    /// Final: the seat is gone.
    Gone(GoneReason),
}

/// Why a seat is gone (the control API's `reason` of a `left` player).
#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub enum GoneReason {
    /// The connection stayed away longer than the reconnect grace.
    GraceExpired,
    /// A clean leave.
    Left,
    /// Revoked by the hosting layer.
    Kicked,
    /// The room closed with the player still seated (after its results were delivered).
    MatchOver,
    /// The durable lease the seat ran under lapsed (the seat is no longer authorised).
    LeaseExpired,
    /// The hosting layer aborted the room.
    Aborted,
}

impl GoneReason {
    pub fn as_str(self) -> &'static str {
        match self {
            GoneReason::GraceExpired => "grace_expired",
            GoneReason::Left => "left",
            GoneReason::Kicked => "kicked",
            GoneReason::MatchOver => "match_over",
            GoneReason::LeaseExpired => "lease_expired",
            GoneReason::Aborted => "aborted",
        }
    }
}

#[derive(Clone, Debug, Default, serde::Serialize)]
#[serde(rename_all = "camelCase")]
pub struct RoomStats {
    pub ticks: u64,
    /// Step wall-time quantiles in microseconds, from a log-linear histogram: p50 and p99 are bucket upper bounds (never
    /// below the true value, at most 25% above it); the max is exact.
    pub step_us_p50: u64,
    pub step_us_p99: u64,
    pub step_us_max: u64,
    pub late_wakes: u64,
    pub dropped_steps: u64,
    /// Bytes the room handed to its humans' outboxes: events and fx frames, and each snapshot at a lower bound of the size of
    /// its Full frame (the connection's writer encodes it, as a delta when it can: `snapshot_bytes_sent` has the real bytes).
    pub bytes_out: u64,
    /// Bytes of the frames the outboxes accepted (at most `bytes_out`: a closed outbox takes nothing).
    pub bytes_enqueued_total: u64,
    /// Fx frames evicted unsent from slow clients' outboxes, over every connection the match had.
    pub fx_dropped: u64,
    /// Snapshots a slow client skipped because a newer one replaced them.
    pub snapshots_skipped: u64,
    /// Snapshot frames the connections' writers encoded as Full frames and as deltas (protocol v3), and their bytes: what the
    /// snapshots cost on the wire.
    pub snapshots_full: u64,
    pub snapshots_delta: u64,
    pub snapshot_bytes_sent: u64,
    /// Snapshots the encoder refused to send (never a repaired frame): counted here, logged once per room.
    pub snapshot_refusals: u64,
    /// Refusal log lines printed: at most one per room, however many refusals there were.
    pub snapshot_refusals_logged: u64,
    /// Events and Fx frames the encoder refused (never a clamped or repaired frame): counted, logged once per room.
    pub frame_refusals: u64,
    pub frame_refusals_logged: u64,
    /// The room task panicked: these are the stats it had, reported by its unwinding guard.
    pub crashed: bool,
    /// Server-measured RTT samples the room accepted over every seat it had, and the last and highest smoothed estimate
    /// (milliseconds) the estimator produced (P-NET-1 Phase 3, for P-OPS-2).
    ///
    /// `skip`: the `/info` closed-room key set is pinned by a director test and is outside this phase's write scope, so
    /// the JSON contract stays byte-for-byte the same; the counters live on the Rust `RoomStats` for P-OPS-2 to expose.
    #[serde(skip)]
    pub rtt_samples: u64,
    #[serde(skip)]
    pub rtt_last_ms: f64,
    #[serde(skip)]
    pub rtt_max_ms: f64,
    /// Attack/release render times the server bounded by its measured RTT, over every seat (P-OPS-2's cheat counter).
    #[serde(skip)]
    pub rewind_clamps: u64,
}

/// The hitbox archetype table every Welcome carries (protocol 16, ADR0024): built once from the sim's table.
pub fn archetype_table() -> &'static [rh_protocol::WireArchetype] {
    static TABLE: std::sync::OnceLock<Vec<rh_protocol::WireArchetype>> = std::sync::OnceLock::new();
    TABLE.get_or_init(rh_policy::delivery::projection::wire_archetypes)
}

/// The weapon table every Welcome carries (protocol 16): built once from the sim content's weapon shapes.
pub fn weapon_table() -> &'static [rh_protocol::WireWeapon] {
    static TABLE: std::sync::OnceLock<Vec<rh_protocol::WireWeapon>> = std::sync::OnceLock::new();
    TABLE.get_or_init(rh_policy::delivery::projection::wire_weapons)
}

/// The HUD content block of a room with the default Frost Wish (Freeze): [`hud_content_for`]`(FrostWish::Freeze)`.
pub fn hud_content() -> &'static rh_protocol::WireHudContent {
    hud_content_for(rh_sim::FrostWish::Freeze)
}

/// The HUD content block a Welcome carries (protocol 17, P-WIRE17-HUD-2b), for a room whose Frost Wish is `frost_wish`
/// (P-FROST-WALL-2: HUD term 23 names it): built once per value from the sim content.
pub fn hud_content_for(frost_wish: rh_sim::FrostWish) -> &'static rh_protocol::WireHudContent {
    use rh_policy::delivery::projection::wire_hud_for;
    use rh_sim::FrostWish;
    static FREEZE: std::sync::OnceLock<rh_protocol::WireHudContent> = std::sync::OnceLock::new();
    static WALL: std::sync::OnceLock<rh_protocol::WireHudContent> = std::sync::OnceLock::new();
    match frost_wish {
        FrostWish::Freeze => FREEZE.get_or_init(|| wire_hud_for(FrostWish::Freeze)),
        FrostWish::Wall => WALL.get_or_init(|| wire_hud_for(FrostWish::Wall)),
    }
}

/// What every room of one map shares: the immutable arena and its Welcome map blob, RLE-encoded once. The director builds
/// one per map; a room only clones the `Arc`s.
#[derive(Clone)]
pub struct MapAssets {
    pub arena: Arc<Arena>,
    pub welcome_map: Arc<EncodedMap>,
}

impl MapAssets {
    /// Fails (with the protocol's reason) for a map a client would refuse to decode. The blob carries the map's
    /// decoration (P-DRESS-BAKE-1), dressed here from the arena with the shipped rules (an arena keeps no landmark
    /// anchors: the pool dresses its maps from the generator's map, [`Self::dressed`]); a map without layers is undressed.
    /// Runs where the assets are built (start-up, the pool), never on a room's tick.
    pub fn new(arena: impl Into<Arc<Arena>>) -> Result<Self, rh_protocol::EncodeError> {
        let arena = arena.into();
        // The reason is the rules' whole message ("dressing rules: ..."), so a caller's panic names what is wrong.
        let rules =
            crate::runtime::shipped_dress_rules().map_err(rh_protocol::EncodeError::BadMap)?;
        let dressing = crate::public_ground::dress_map(&arena.map, rules)?;
        Self::dressed(arena, dressing.as_ref())
    }

    /// The assets of `arena` with `dressing` (already computed, e.g. from the generator's map) in its Welcome blob.
    pub fn dressed(
        arena: impl Into<Arc<Arena>>,
        dressing: Option<&rh_mapdress::Dressing>,
    ) -> Result<Self, rh_protocol::EncodeError> {
        Self::landmarked(arena, dressing, &[])
    }

    /// [`Self::dressed`] also carrying the generator's landmark anchors (P-WIRE17-MAP-3), which the arena does not keep:
    /// the pool reads them from the generator's map before the arena conversion (`map_pool::MadeMap::anchors`).
    pub fn landmarked(
        arena: impl Into<Arc<Arena>>,
        dressing: Option<&rh_mapdress::Dressing>,
        anchors: &[rh_sim::mapdata::ZoneAnchor],
    ) -> Result<Self, rh_protocol::EncodeError> {
        let arena = arena.into();
        let welcome_map = crate::public_ground::encode_landmark_map(&arena.map, dressing, anchors)?;
        Ok(Self {
            arena,
            welcome_map: Arc::new(welcome_map),
        })
    }
}

pub struct RoomConfig {
    pub sim: SimConfig,
    /// Shared by every room of the map (never cloned per room).
    pub arena: Arc<Arena>,
    /// The map's Welcome blob (see [`MapAssets`]); every join and resume copies these bytes.
    pub welcome_map: Arc<EncodedMap>,
    /// Fill the match with scripted bots up to this many players (0: none).
    pub fill_bots_to: usize,
    /// The pacer's period. Hosted rooms take it from `sim.tick_ms` (the director builds both from one setting and refuses
    /// a mismatch, P-TICK-5); a test may pace faster or slower than the sim steps. Sim time, the Welcome rate and every
    /// "one step" the room adds follow `sim.tick_ms`, never this.
    pub tick: Duration,
    /// Record a private replay of the match (every input, every step's state hash).
    pub record: bool,
    /// Tests only: fixed match secrets and bot seeds (live rooms draw both from the OS CSPRNG).
    pub fixed_secrets: Option<Secrets>,
    pub fixed_bot_seed: Option<u64>,
    pub reconnect_grace: Duration,
    pub linger: Duration,
}

/// Retained after leave so a delayed result still names its exact admitted owner.
struct SeatRecord {
    player: PlayerId,
    fence: Option<i64>,
    admission: Option<Admission>,
}

struct HumanSeat {
    fx: rh_sim::FxProjector,
    admission: Option<Admission>,
    deadline: Option<tokio::sync::watch::Receiver<Instant>>,
    player: PlayerId,
    conn: u64,
    fence: i64,
    profile: String,
    session: [u8; 16],
    outbox: Option<Arc<Outbox>>,
    dropped_at: Option<Instant>,
    ack_input: u32,
    /// When the oldest Input message not yet applied by a step was decoded on its connection (telemetry: input age).
    input_received: Option<std::time::Instant>,
    /// The server-measured RTT of this seat's current connection: it bounds the attack render times this seat may claim.
    rtt: RttEstimator,
    /// What this human could know of the wake's steps before the last, judged step by step as they ran (empty between
    /// wakes and on every one-step wake).
    carry: Vec<SeenEvent>,
    items: rh_sim::ItemObserver,
    collections: crate::wire_state::CollectionPublisher,
    /// A snapshot the connection's outbox handed back, to build the next one in (no allocation per tick).
    spare: Option<rh_protocol::SnapshotData>,
    /// This player's own resolved run, kept on the seat so a client that was away when it resolved (the reconnect grace, or a
    /// TooSlow close that dropped its queued events) still receives it: it is re-sent after a resume's Welcome (protocol v4,
    /// docs/adr/0011-protocol-v4.md).
    result: Option<WireResult>,
}

pub struct Room {
    pub id: MatchId,
    sim: Sim,
    humans: Vec<HumanSeat>,
    bots: Vec<Seat>,
    /// The fill policy's spec per seated bot (tier/persona/faction), for the P-BOTS-5 record caller.
    bot_specs: Vec<(PlayerId, BotSeatSpec)>,
    reported_bots: std::collections::BTreeSet<u32>,
    /// Brain seats whose fallback was logged (once each), and whether the match-start line was.
    fallback_logged: std::collections::BTreeSet<PlayerId>,
    brain_start_logged: bool,
    #[cfg(test)]
    log_sink: Vec<String>,
    fill_to: usize,
    /// The scheduled fill policy (legacy BotManager). None keeps the immediate placeholder fill.
    fill: Option<BotFillState>,
    /// The sim's lobby knobs the fill's closing step needs (the sim owns the countdown itself).
    min_players: usize,
    fill_seconds: f64,
    pacer: Pacer,
    /// The last step's events (the bots' input to their next decision, and the observation's events when published). Two
    /// buffers swap through `step_into` each step: the room allocates no event list per step.
    last_events: Vec<Event>,
    step_events: Vec<Event>,
    /// Every event of the current wake, in order (reused).
    wake_events: Vec<Event>,
    publisher: wire::Publisher,
    /// The bots' line-of-sight memo (derived, outside the sim; the publisher keeps its own for the humans).
    bot_sight: rh_sim::SightCache,
    /// The fill bots' source delay lines (P-BOTS-7): one per seated fill bot, seeded from the manager's line stream.
    bot_lines: Vec<(PlayerId, DelayLine<rh_sim::PublicCommand>)>,
    line_rng: Rng,
    /// The read-only profile callsign set (source isNameTaken): a fill bot never draws one. Lower-cased.
    taken_names: Arc<std::collections::HashSet<String>>,
    /// Delay-line delivery counters. The source manager counts a command as refused only at DELIVERY time, when the
    /// delivered message is refused by the intake (enqueueClientMessage false); its MAX_QUEUED / unsupported-type
    /// rejections in deliver are not part of that refused (deliver returns false before sent++). The port keeps those
    /// separate in bot_line_dropped, so refused here is the source's delivery-time intake refusal and the counters are
    /// deliberately not parity-identical to the source's stats as a whole. lost also covers a seat that left between
    /// queuing and delivery, which the source never observes (it reads the runtime at delivery).
    bot_sent: u64,
    bot_applied: u64,
    bot_refused: u64,
    bot_line_dropped: u64,
    bot_lost: u64,
    reports: mpsc::UnboundedSender<RoomReport>,
    clock: Arc<AtomicU64>,
    /// Step wall times: a fixed-size histogram (384 bytes at any uptime; was a Vec growing to 288 KB).
    step_us: Histogram,
    metrics: &'static Metrics,
    bytes_out: u64,
    snapshot_refusals: u64,
    snapshot_refusals_logged: u64,
    frame_refusals: u64,
    frame_refusals_logged: u64,
    ended_at: Option<Instant>,
    draining: bool,
    welcome_map: Arc<EncodedMap>,
    /// Counters of the outboxes this room no longer holds (a resumed, dropped or left connection's).
    retired_outboxes: OutboxCounters,
    bot_seq: u64,
    recorder: Option<Recorder>,
    fixed_bot_seed: Option<u64>,
    reconnect_grace: Duration,
    linger: Duration,
    opened_at: Instant,
    had_human: bool,
    /// Every human's lease fence, kept after it leaves: its result may be reported a step later.
    fences: Vec<SeatRecord>,
    reported_phase: Phase,
    /// The room reported `Closed` (the task must end).
    closed: bool,
    /// Tests: wall time every wake burns (an overloaded host).
    #[cfg(test)]
    stall: Duration,
    /// Server-measured RTT samples the room accepted, and the last and highest smoothed estimate any seat reported (for
    /// P-OPS-2 and diagnostics; the per-seat estimator is what the clamp reads).
    rtt_samples: u64,
    rtt_last_ms: f64,
    rtt_max_ms: f64,
    /// Attack/release render times the RTT bound rewrote (every clamp, for P-OPS-2).
    rewind_clamps: u64,
    /// Start of the last step run (stamped on each tick batch for the writer's step-to-write latency).
    last_step_start: Option<std::time::Instant>,
    /// A training room (a practice world: one human, no bots, no results or banking, an idle close and no replay).
    training: bool,
    /// When this room's seat last had a command accepted (the training idle clock); opened_at before the first one.
    last_input_at: Option<Instant>,
    /// The training idle limit (TRAINING_IDLE_SECONDS); tests shorten it.
    training_idle: Duration,
}

pub fn random16() -> [u8; 16] {
    let mut b = [0u8; 16];
    getrandom::getrandom(&mut b).expect("OS entropy");
    b
}

fn random_u32() -> u32 {
    let mut b = [0u8; 4];
    getrandom::getrandom(&mut b).expect("OS entropy");
    u32::from_le_bytes(b)
}

impl Room {
    pub fn new(
        id: MatchId,
        cfg: RoomConfig,
        reports: mpsc::UnboundedSender<RoomReport>,
        now: Instant,
    ) -> Self {
        wire::profile_supported(&cfg.sim).expect("refusing to host unsupported simulation profile");
        assert!(
            crate::public_ground::matches_static_map(&cfg.welcome_map, &cfg.arena.map),
            "refusing a Welcome cache that differs from the room's canonical public map"
        );
        let training = cfg.sim.training;
        // Match secrets come from the OS CSPRNG and never leave this task (docs/CONTRACTS.md C-RNG).
        let secrets = cfg.fixed_secrets.unwrap_or_else(|| Secrets {
            order_salt: random_u32(),
            spawn_seed: random_u32(),
            reward_salt: std::array::from_fn(|_| random_u32()),
        });
        let sim = Sim::new(cfg.sim.clone(), cfg.arena, secrets);
        let min_players = cfg.sim.min_players;
        let fill_seconds = cfg.sim.fill_seconds;
        // A training room has no run, result or replay to record: it never creates a recorder (no banking/telemetry of a
        // result and no private replay), whatever the hosting configuration asked for.
        let recorder = (cfg.record && !training).then(|| {
            let mut r = Recorder::new(&sim, cfg.sim, ReplayStart::Lobby);
            r.replay.match_id = Some(match_id_string(id));
            r
        });
        Self {
            id,
            sim,
            humans: Vec::new(),
            bots: Vec::new(),
            bot_specs: Vec::new(),
            reported_bots: Default::default(),
            fallback_logged: Default::default(),
            brain_start_logged: false,
            #[cfg(test)]
            log_sink: Vec::new(),
            fill_to: if training { 0 } else { cfg.fill_bots_to },
            fill: None,
            min_players,
            fill_seconds,
            pacer: Pacer::new(now, cfg.tick),
            last_events: Vec::new(),
            step_events: Vec::new(),
            wake_events: Vec::new(),
            publisher: wire::Publisher::default(),
            bot_sight: rh_sim::SightCache::new(),
            bot_lines: Vec::new(),
            line_rng: Rng::new(0),
            taken_names: Arc::new(std::collections::HashSet::new()),
            bot_sent: 0,
            bot_applied: 0,
            bot_refused: 0,
            bot_line_dropped: 0,
            bot_lost: 0,
            reports,
            clock: Arc::new(AtomicU64::new(0f64.to_bits())),
            step_us: Histogram::new(),
            metrics: metrics::process(),
            bytes_out: 0,
            snapshot_refusals: 0,
            snapshot_refusals_logged: 0,
            frame_refusals: 0,
            frame_refusals_logged: 0,
            ended_at: None,
            draining: false,
            welcome_map: cfg.welcome_map,
            retired_outboxes: OutboxCounters::default(),
            bot_seq: 0,
            recorder,
            fixed_bot_seed: cfg.fixed_bot_seed,
            reconnect_grace: cfg.reconnect_grace,
            linger: cfg.linger,
            opened_at: now,
            had_human: false,
            fences: Vec::new(),
            reported_phase: Phase::Lobby,
            closed: false,
            #[cfg(test)]
            stall: Duration::ZERO,
            rtt_samples: 0,
            rtt_last_ms: 0.0,
            rtt_max_ms: 0.0,
            rewind_clamps: 0,
            last_step_start: None,
            training,
            last_input_at: None,
            training_idle: Duration::from_secs(TRAINING_IDLE_SECONDS),
        }
    }

    /// Reports to `metrics` instead of the process-wide counters (tests and benches that need isolated numbers).
    pub fn with_metrics(mut self, metrics: &'static Metrics) -> Self {
        self.metrics = metrics;
        self
    }

    fn record(&mut self, input: ReplayInput) {
        if let Some(r) = self.recorder.as_mut() {
            r.record(input);
        }
    }

    pub fn sim(&self) -> &Sim {
        &self.sim
    }

    /// The room's step in milliseconds as sim time (`Sim::tick_ms`: 50, or 25 under ADR 0029): what the next step adds.
    fn step_ms(&self) -> f64 {
        f64::from(self.sim.tick_ms())
    }

    /// The rate the room's Welcome announces: 20 Hz at 50 ms, 40 Hz at 25 ms (`profile_supported` admits no other step).
    fn tick_rate(&self) -> u8 {
        (1000 / self.sim.tick_ms()) as u8
    }

    /// P-TICK-5: a connection that is about to get this room's Welcome writes its snapshots, and counts its delta and
    /// capability limits, at the room's step. Called where the Welcome is built (join and resume), before any snapshot.
    fn bind_rate(
        tick_ms: u32,
        outbox: &Outbox,
        collections: &mut crate::wire_state::CollectionPublisher,
    ) {
        // `profile_supported` refused every step a decoder does not accept when the room was built.
        outbox
            .set_tick_ms(tick_ms)
            .expect("the room's step is a hosted rate");
        collections
            .set_tick_ms(tick_ms)
            .expect("the room's step is a hosted rate");
    }

    fn welcome(&self, player: PlayerId, session: [u8; 16]) -> Vec<u8> {
        // The map is copied from its once-encoded blob, not run-length encoded per join and resume.
        encode_welcome(
            &WelcomeHeader {
                protocol: rh_protocol::PROTOCOL_VERSION,
                content_hash: rh_sim::content::content_hash(),
                match_id: self.id,
                player_id: player.0,
                session,
                tick_rate: self.tick_rate(),
                tick: self.sim.tick(),
                server_ms: self.sim.server_time(),
            },
            archetype_table(),
            &rh_policy::delivery::projection::wire_vision(&self.sim.arena().map),
            weapon_table(),
            hud_content_for(self.sim.config().frost_wish),
            &self.welcome_map,
            crate::public_ground::mode(self.sim.config().hazards),
        )
    }

    /// Takes a connection's counters into the room's totals once the room stops pushing to it.
    fn retire_outbox(&mut self, o: &Outbox) {
        self.retired_outboxes.add(o.counters());
    }

    fn humans_present(&self) -> usize {
        self.humans.len()
    }
}

#[cfg(test)]
#[path = "room/ground_tests.rs"]
mod public_ground_tests;

// Private native bridge component tests; these do not admit a hosted quest profile.
#[cfg(test)]
#[path = "quest_bridge_tests.rs"]
mod quest_bridge_tests;

#[cfg(test)]
#[path = "trade_bridge_tests.rs"]
mod trade_bridge_tests;

#[cfg(test)]
mod tests;
