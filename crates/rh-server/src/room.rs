//! One match: a task that owns its [`Sim`], its seats and its pacer. Everything that touches the simulation happens on this
//! task, in arrival order, between steps (the legacy rule: handlers validate and enqueue, the sim consumes at the next step).
//! Nothing here blocks on I/O: frames go to per-connection [`Outbox`]es, results to a channel drained off-tick.

// The file the packet names as `src/rtt.rs`; declared here because `lib.rs` is outside this phase's write scope.
#[path = "rtt.rs"]
pub mod rtt;

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
use rh_sim::constants::TICK_MS;
use rh_sim::replay::{BotPolicy, Recorder, Replay, ReplayInput, ReplayStart};
use rh_sim::{
    Arena, Event, FactionId, Phase, PlayerId, RunResult, Secrets, SeenEvent, Sim, SimConfig,
};
use rtt::{clamp_render_time_noted, RewindClampStats, RttEstimator};
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

    fn welcome(&self, player: PlayerId, session: [u8; 16]) -> Vec<u8> {
        // The map is copied from its once-encoded blob, not run-length encoded per join and resume.
        encode_welcome(
            &WelcomeHeader {
                protocol: rh_protocol::PROTOCOL_VERSION,
                content_hash: rh_sim::content::content_hash(),
                match_id: self.id,
                player_id: player.0,
                session,
                tick_rate: rh_sim::constants::TICK_RATE as u8,
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

    pub fn handle(&mut self, cmd: RoomCmd, now: Instant) {
        let now = now.max(Instant::now());
        self.expire_leases(now);
        match cmd {
            RoomCmd::Join {
                profile,
                faction,
                outbox,
                conn,
                fence,
                reply,
            } => {
                let r = self.join(profile, faction, outbox, conn, fence, None);
                let _ = reply.send(r);
            }
            RoomCmd::JoinLeased {
                admission,
                profile,
                faction,
                outbox,
                conn,
                fence,
                deadline,
                reply,
            } => {
                let result = if *deadline.borrow() <= now {
                    Err(RejectCode::ProfileInUse)
                } else {
                    self.join(
                        profile,
                        faction,
                        outbox,
                        conn,
                        fence,
                        Some((admission, deadline)),
                    )
                };
                let _ = reply.send(result);
            }
            RoomCmd::Resume {
                session,
                outbox,
                conn,
                reply,
            } => {
                let r = match self.humans.iter_mut().find(|h| h.session == session) {
                    None => Err(RejectCode::NoSuchSession),
                    Some(h) => {
                        let old = h.outbox.replace(outbox.clone());
                        // From now on only the new connection speaks for this seat.
                        h.conn = conn;
                        h.dropped_at = None;
                        // A new connection is a new path: its first Pong measures it, the old estimate is not inherited.
                        h.rtt = RttEstimator::new();
                        h.fx.rebind(&self.sim, h.player);
                        h.carry.clear();
                        h.items.reconnect();
                        h.collections.reconnect();
                        let (player, session) = (h.player, h.session);
                        let seat_profile = h.profile.clone();
                        let resolved = h.result.clone();
                        if let Some(old) = old {
                            old.close(RejectCode::Kicked);
                            self.retire_outbox(&old);
                        }
                        self.record(ReplayInput::Dropped {
                            player,
                            dropped: false,
                        });
                        self.sim.mark_dropped(player, false);
                        let _ = self.reports.send(RoomReport::Seat {
                            match_id: self.id,
                            profile: seat_profile,
                            event: SeatEvent::Back,
                        });
                        // The run this player already resolved: the one reliable event that is not state the client can read
                        // back, so it is queued behind the Welcome (which the connection writes first).
                        if let Some(r) = resolved {
                            let frame = ServerMsg::Events {
                                tick: self.sim.tick(),
                                events: vec![WireEvent::Result(r)],
                            }
                            .try_encode();
                            if let Ok(frame) = frame {
                                outbox.important(frame);
                            }
                        }
                        // A fresh Welcome is the resync: full map and the current tick; the next snapshot follows.
                        Ok(JoinOk {
                            player,
                            session,
                            welcome: self.welcome(player, session),
                            clock: self.clock.clone(),
                        })
                    }
                };
                let _ = reply.send(r);
            }
            RoomCmd::Input {
                player,
                conn,
                seq,
                commands,
                received,
            } => {
                if let Some(index) = self
                    .humans
                    .iter()
                    .position(|h| h.player == player && h.conn == conn)
                {
                    self.humans[index].ack_input = seq;
                    self.humans[index].input_received.get_or_insert(received);
                    // The clamp is written for the sim time this batch will be applied at: the room applies commands
                    // between steps and the next step adds one tick first, so `note_lag` in the sim will see
                    // `now_apply` as its `now`. P-NET-1 Phase 3: bound an attack render time by the RTT the server
                    // measured for this connection (never by the client's claim), before `Sim::submit`. Cast render-time
                    // metadata receives the same bound, but Wish effects are not rewound by it.
                    let now_apply = self.sim.server_time() + TICK_MS;
                    let mut accepted = 0u32;
                    let mut clamps = RewindClampStats::default();
                    for c in commands {
                        let (proposal, clamped) = clamp_render_time_noted(
                            wire::command(c),
                            now_apply,
                            &self.humans[index].rtt,
                            &mut clamps,
                        );
                        if clamped {
                            self.rewind_clamps += 1;
                        }
                        // Resolve only within this seat's delivered offer, then use the shared
                        // validator/budget/submit path. Target is an observer-issued reference,
                        // never a private-ID fallback. Misses are recorded ordinary commands.
                        let resolved = self.humans[index].items.resolve(&self.sim, proposal);
                        if let Ok(cmd) = resolved {
                            self.record(ReplayInput::Command { player, cmd });
                            if self.sim.submit(player, cmd).is_ok() {
                                accepted += 1;
                            }
                        }
                    }
                    self.metrics.note_rewind_clamps(&clamps);
                    // The training idle clock runs on ACCEPTED inputs (a rejected/no-op command does not keep the room up).
                    if accepted > 0 {
                        self.last_input_at = Some(now);
                    }
                }
            }
            RoomCmd::RttSample {
                player,
                conn,
                rtt_ms,
            } => {
                // Only the connection that owns the seat may move its window; a replaced connection's late Pongs are
                // ignored, so a resume starts measuring the new path from scratch.
                if let Some(h) = self
                    .humans
                    .iter_mut()
                    .find(|h| h.player == player && h.conn == conn)
                {
                    h.rtt.sample(rtt_ms);
                    let estimate = h.rtt.estimate_ms();
                    rtt::note_estimate(self.id, player, estimate);
                    self.rtt_samples += 1;
                    self.rtt_last_ms = estimate;
                    self.rtt_max_ms = self.rtt_max_ms.max(estimate);
                }
            }
            RoomCmd::Disconnected { player, conn } => {
                // A replaced connection's late disconnect must not detach the seat its resume took over.
                if let Some(h) = self
                    .humans
                    .iter_mut()
                    .find(|h| h.player == player && h.conn == conn)
                {
                    let old = h.outbox.take();
                    h.dropped_at = Some(now);
                    self.record(ReplayInput::Dropped {
                        player,
                        dropped: true,
                    });
                    self.sim.mark_dropped(player, true);
                    if let Some(old) = old {
                        self.retire_outbox(&old);
                    }
                    if let Some(h) = self.humans.iter().find(|h| h.player == player) {
                        let _ = self.reports.send(RoomReport::Seat {
                            match_id: self.id,
                            profile: h.profile.clone(),
                            event: SeatEvent::Away { at: now },
                        });
                    }
                }
            }
            RoomCmd::Leave { player, conn } => {
                if self
                    .humans
                    .iter()
                    .any(|h| h.player == player && h.conn == conn)
                {
                    self.leave(player, GoneReason::Left);
                }
            }
            RoomCmd::Kick { profile } => {
                if let Some(player) = self
                    .humans
                    .iter()
                    .find(|h| h.profile.eq_ignore_ascii_case(&profile))
                    .map(|h| h.player)
                {
                    self.leave(player, GoneReason::Kicked);
                }
            }
            RoomCmd::Abort => {
                self.finish(true);
            }
            RoomCmd::Drain => self.draining = true,
            RoomCmd::DrainEnd => {
                self.draining = true;
                let seated: Vec<PlayerId> = self.humans.iter().map(|h| h.player).collect();
                for player in seated {
                    self.leave(player, GoneReason::Aborted);
                }
                self.finish(false);
            }
            #[cfg(test)]
            RoomCmd::Crash => panic!("test: the room task panics"),
        }
    }

    fn join(
        &mut self,
        profile: String,
        faction: Option<FactionId>,
        outbox: Arc<Outbox>,
        conn: u64,
        fence: i64,
        lease: Option<(Admission, tokio::sync::watch::Receiver<Instant>)>,
    ) -> Result<JoinOk, RejectCode> {
        if self.draining {
            return Err(RejectCode::ServerDraining);
        }
        // A training room seats exactly one human (the practice console is a solo room; there is no second seat to fill).
        if self.training && !self.humans.is_empty() {
            return Err(RejectCode::MatchFull);
        }
        if self.sim.phase() != Phase::Lobby {
            return Err(RejectCode::MatchFull);
        }
        // A human takes a bot's seat when the lobby is full (legacy: the newest bot yields).
        if self.sim.players().len() >= rh_sim::constants::MAX_PLAYERS {
            self.yield_newest_bot();
        }
        let player = self
            .sim
            .add_player(&profile, faction, false)
            .ok_or(RejectCode::MatchFull)?;
        self.record(ReplayInput::Join {
            profile: profile.clone(),
            faction,
            bot: false,
            policy: None,
        });
        let session = random16();
        let admission = lease.as_ref().map(|(identity, _)| *identity);
        self.fences.push(SeatRecord {
            player,
            fence: Some(fence),
            admission,
        });
        // A room that has had a human closes the moment it has none again (see `wake`).
        self.had_human = true;
        self.humans.push(HumanSeat {
            fx: rh_sim::FxProjector::attach(&self.sim, player),
            admission,
            deadline: lease.map(|(_, deadline)| deadline),
            player,
            conn,
            fence,
            profile,
            session,
            outbox: Some(outbox),
            dropped_at: None,
            ack_input: 0,
            input_received: None,
            rtt: RttEstimator::new(),
            carry: Vec::new(),
            items: rh_sim::ItemObserver::new(player),
            collections: Default::default(),
            spare: None,
            result: None,
        });
        if self.fill.is_none() {
            self.fill_bots();
        }
        let _ = self.reports.send(RoomReport::Seated {
            match_id: self.id,
            phase: self.sim.phase(),
            humans: self.humans_present(),
        });
        if let Some(h) = self.humans.last() {
            let _ = self.reports.send(RoomReport::Seat {
                match_id: self.id,
                profile: h.profile.clone(),
                event: SeatEvent::Seated,
            });
        }
        Ok(JoinOk {
            player,
            session,
            welcome: self.welcome(player, session),
            clock: self.clock.clone(),
        })
    }

    /// Scripted bots on the same seat contract as future policies (rh-policy). Bots never close a lobby: only humans count.
    fn fill_bots(&mut self) {
        while self.humans_present() > 0 && self.sim.players().len() < self.fill_to {
            self.bot_seq += 1;
            let name = format!("bot{}", self.bot_seq);
            let Some(id) = self.sim.add_player(&name, None, true) else {
                break;
            };
            self.report_bot(&name, true);
            let seed = match self.fixed_bot_seed {
                Some(s) => s.wrapping_add(self.bot_seq),
                None => u64::from(random_u32()) << 32 | u64::from(random_u32()),
            };
            let interval = 2 + (self.bot_seq % 3) as u32;
            self.record(ReplayInput::Join {
                profile: name,
                faction: None,
                bot: true,
                policy: Some(BotPolicy {
                    name: "brawler".into(),
                    interval,
                    seed,
                }),
            });
            self.bots.push(Seat::new(
                &MatchStart::of(&self.sim, id, seed),
                Box::new(scripted::Brawler::new(interval)),
                Box::new(scripted::Idle),
            ));
            self.bots
                .last_mut()
                .expect("new bot")
                .bind_fx(&self.sim)
                .expect("new bot is unbound");
        }
    }

    /// Opt into the legacy manager's lobby fill (BotManager.ts): bots arrive on a seeded schedule through the countdown,
    /// the closing step seats whoever has not arrived, and a joining human takes the newest bot's seat. Without it the room
    /// keeps its immediate placeholder fill above (the default for tests, benches and rooms that do not configure bots).
    ///
    /// The policy's seed comes from the room's fixed bot seed when set (tests, replay determinism), else OS entropy: the
    /// fill seed is not a match secret (docs/CONTRACTS.md C-RNG does not cover bot identity).
    pub fn with_bot_fill(mut self, cfg: BotFillConfig) -> Self {
        // Source BOTS_SEED seeds every manager stream; the rooms' fixed bot seed (tests, replays) wins over it.
        let seed = self
            .fixed_bot_seed
            .or_else(|| cfg.seed.map(u64::from))
            .unwrap_or_else(|| (u64::from(random_u32()) << 32) | u64::from(random_u32()));
        self.line_rng = DelayLine::<rh_sim::PublicCommand>::rng(seed as u32);
        self.fill = Some(BotFillState::new(cfg, seed));
        self
    }

    pub fn with_learning(mut self, sink: Arc<dyn crate::bot_learning::LearningSink>) -> Self {
        if let Some(fill) = &mut self.fill {
            fill.set_learning_sink(sink);
        }
        self
    }

    /// The manager's read-only profile-name seam (source isNameTaken): callsigns real profiles own, lower-cased. The set
    /// is a snapshot the director refreshes per lobby; bots never read or write a profile.
    pub fn with_bot_names(mut self, names: Arc<std::collections::HashSet<String>>) -> Self {
        self.taken_names = names;
        self
    }

    /// The delay line's send half (source BotManager.deliver): a fill bot's proposal gets its arrival from the manager's
    /// line stream; a seat with no fill spec (the immediate placeholder fill) submits now, as before.
    fn enqueue_bot_commands(&mut self, proposed: Vec<(PlayerId, rh_sim::PublicCommand)>) {
        if proposed.is_empty() {
            return;
        }
        let (latency, jitter) = self.fill.as_ref().map_or((0., 0.), |fill| {
            (fill.config().latency_ms, fill.config().jitter_ms)
        });
        let at = self.sim.server_time();
        for (player, cmd) in proposed {
            if let Some(pos) = self.bot_lines.iter().position(|(p, _)| *p == player) {
                let accepted = self.bot_lines[pos]
                    .1
                    .enqueue(&mut self.line_rng, at, latency, jitter, cmd)
                    .is_some();
                if accepted {
                    self.bot_sent += 1;
                } else {
                    // The line is at its 256-command cap (source MAX_QUEUED): a delivery-stage rejection, not an intake
                    // refusal. Kept apart from bot_refused so the source's delivery-time accounting stays legible.
                    self.bot_line_dropped += 1;
                }
            } else {
                self.submit_bot_now(player, cmd);
            }
        }
    }

    /// The delay line's delivery half (source BotManager.beforeStep, called with the upcoming step's time): a command
    /// arrives, is resolved against the state at that step and goes through validate + submit, the intake a human's
    /// message takes.
    fn deliver_bot_commands(&mut self) {
        if self.bot_lines.is_empty() {
            return;
        }
        let step_t = self.sim.server_time() + rh_sim::constants::TICK_MS;
        let mut due: Vec<(PlayerId, rh_sim::PublicCommand)> = Vec::new();
        for (player, line) in &mut self.bot_lines {
            let mut out = Vec::new();
            line.pop_due(step_t, &mut out);
            due.extend(out.into_iter().map(|cmd| (*player, cmd)));
        }
        for (player, cmd) in due {
            if self.sim.player(player).is_none() {
                self.bot_lost += 1;
                continue;
            }
            self.submit_bot_now(player, cmd);
        }
    }

    fn submit_bot_now(&mut self, player: PlayerId, cmd: rh_sim::PublicCommand) {
        let Some(index) = self.bots.iter().position(|seat| seat.player() == player) else {
            self.bot_lost += 1;
            return;
        };
        let Some(command) = self.bots[index].resolve(&self.sim, cmd) else {
            self.bot_refused += 1;
            self.bots[index].note_delivery(false);
            return;
        };
        if let Some(r) = self.recorder.as_mut() {
            r.record(ReplayInput::Command {
                player,
                cmd: command,
            });
        }
        match self.sim.submit(player, command) {
            Ok(()) => {
                self.bot_applied += 1;
                self.bots[index].note_delivery(true);
            }
            Err(_) => {
                self.bot_refused += 1;
                self.bots[index].note_delivery(false);
            }
        }
    }

    /// The delay-line counters (source manager stats' delivery half: sent, applied, refused, lost), for telemetry.
    /// refused is delivery-time intake refusal only (see the field docs); line-cap drops are reported by
    /// bot_line_dropped and are not part of these four.
    pub fn bot_delivery(&self) -> (u64, u64, u64, u64) {
        (
            self.bot_sent,
            self.bot_applied,
            self.bot_refused,
            self.bot_lost,
        )
    }

    /// Commands the delay line dropped because it already held its 256-command cap (source MAX_QUEUED). The source reports
    /// no such counter at all; it is kept here so the delivery-time refused count stays the source's meaning.
    pub fn bot_line_dropped(&self) -> u64 {
        self.bot_line_dropped
    }

    /// The seated fill bots' telemetry tags (id, callsign, tier, persona, faction, seed). The lobby HUD's head count and
    /// its presentation stay with the client/coordinator; this is the native per-bot telemetry surface.
    /// The `[bots]` lines this room printed (tests only).
    #[cfg(test)]
    fn log_lines(&self) -> &[String] {
        &self.log_sink
    }

    pub fn bot_specs(&self) -> &[(PlayerId, BotSeatSpec)] {
        &self.bot_specs
    }

    /// One lobby step of the fill policy, before the step it applies to (the manager's lobbyTick and, on the closing
    /// step, lobbyClosing): draws the arrivals that are due and seats them, then seats whoever is still missing when the
    /// lobby is about to close into the raid.
    fn fill_step(&mut self) {
        if self.fill.is_none() || self.sim.phase() != Phase::Lobby {
            return;
        }
        let now = self.sim.server_time();
        let ends = self.sim.lobby_ends_at();
        let mut factions = [0u32; 5];
        let mut names_lower = Vec::with_capacity(self.sim.players().len());
        let mut humans = 0usize;
        for player in self.sim.players() {
            names_lower.push(player.seat.name.to_lowercase());
            if !player.seat.bot {
                humans += 1;
            }
            let faction = player.faction();
            if let Some(index) = FactionId::ALL.iter().position(|known| *known == faction) {
                factions[index] += 1;
            }
        }
        let players = self.sim.players().len();
        let taken_names = self.taken_names.clone();
        let taken = |name: &str| taken_names.contains(&name.to_ascii_lowercase());
        let view = FillView {
            players,
            humans,
            factions,
            names_lower,
            taken: &taken,
        };
        // The step closes the lobby when the countdown is out, when the lobby is full, or when enough humans are in and
        // the fill time is zero (the sim's updateLobby rule). The manager seats its remaining arrivals on that step.
        let closing = humans >= rh_sim::constants::MAX_PLAYERS
            || now >= ends
            || (humans >= self.min_players && self.fill_seconds <= 0.);
        let mut specs: Vec<BotSeatSpec> = self
            .fill
            .as_mut()
            .map(|fill| fill.lobby_tick(now, ends, &view))
            .unwrap_or_default();
        if closing {
            if let Some(fill) = self.fill.as_mut() {
                specs.extend(fill.lobby_closing(&view));
            }
        }
        for spec in specs {
            self.seat_fill_bot(spec);
        }
    }

    /// Seats one bot the fill policy drew: its callsign, faction, tier and persona go on the normal seat contract, so it
    /// perceives only through observations and acts only through validate + submit; it never touches a profile or quest.
    fn seat_fill_bot(&mut self, spec: BotSeatSpec) {
        let name = spec.name.clone();
        let Some(id) = self.sim.add_player(&name, Some(spec.faction), true) else {
            return;
        };
        self.report_bot(&name, true);
        let brain = self
            .fill
            .as_ref()
            .filter(|fill| fill.config().brain == crate::bot_fill::BotBrain::Full)
            .map(|fill| fill.config().learned.clone());
        let budgeted = brain.is_some();
        let (policy, fallback, replay_name): (Box<dyn Policy>, Box<dyn Policy>, &str) = match brain
        {
            // P-BOT-BRAIN-LIVE-1: the Brain plays the seat; a faulted Brain hands it to HumanBot, not to Idle.
            Some(learned) => (
                Box::new(
                    BrainBot::new(
                        spec.tier,
                        spec.persona,
                        rh_policy::bots::brain_features(self.sim.config(), self.sim.shared_arena()),
                    )
                    .with_learned(learned),
                ),
                Box::new(HumanBot::new(spec.tier, spec.persona)),
                "brain",
            ),
            None => (
                Box::new(HumanBot::new(spec.tier, spec.persona)),
                Box::new(scripted::Idle),
                "human",
            ),
        };
        let interval = policy.spec().decision_interval_ticks;
        self.record(ReplayInput::Join {
            profile: name,
            faction: Some(spec.faction),
            bot: true,
            policy: Some(BotPolicy {
                name: replay_name.into(),
                interval,
                seed: u64::from(spec.seed),
            }),
        });
        let mut seat = Seat::new(
            &MatchStart::of(&self.sim, id, u64::from(spec.seed)),
            policy,
            fallback,
        );
        if budgeted {
            seat.budget = Some(crate::bot_fill::BRAIN_DECISION_BUDGET);
        }
        seat.bind_fx(&self.sim).expect("new bot is unbound");
        self.bots.push(seat);
        self.bot_specs.push((id, spec));
        self.bot_lines.push((id, DelayLine::new()));
    }

    /// Prints one `[bots]` line (and keeps it for tests).
    fn bots_log(&mut self, line: String) {
        eprintln!("{line}");
        #[cfg(test)]
        self.log_sink.push(line);
    }

    /// P-BOT-BRAIN-FAULT-LOG-1: a Brain seat's first fault logs one line (and counts); the steady path scans a handful of
    /// seats and allocates nothing. Also the once-per-match line saying how many Brain seats the match started with.
    fn log_brain_fallbacks(&mut self) {
        if !self.brain_start_logged && self.sim.phase() == Phase::Active {
            self.brain_start_logged = true;
            let k = self.bots.iter().filter(|s| s.budget.is_some()).count();
            if k > 0 {
                let line = format!(
                    "[bots] match={} fill bots: {k} brain",
                    crate::control::room_id_hex(self.id)
                );
                self.bots_log(line);
            }
        }
        for i in 0..self.bots.len() {
            let seat = &self.bots[i];
            let Some(fault) = seat.fault.as_ref().filter(|_| seat.budget.is_some()) else {
                continue;
            };
            let player = seat.player();
            if self.fallback_logged.contains(&player) {
                continue;
            }
            let reason = fault_kind(fault);
            self.fallback_logged.insert(player);
            self.metrics.brain_fallbacks.fetch_add(1, Ordering::Relaxed);
            let number = self
                .bot_specs
                .iter()
                .find(|(id, _)| *id == player)
                .map_or(0, |(_, spec)| spec.number);
            let line = format!(
                "[bots] match={} seat={number} brain fell back to the fallback bot: {reason}",
                crate::control::room_id_hex(self.id)
            );
            self.bots_log(line);
        }
    }

    /// A bot took (`seated`) or gave up its seat: the director's public lobby listing (P-LOBBY-ACTIVITY-1) shows bot seats.
    fn report_bot(&self, name: &str, seated: bool) {
        let _ = self.reports.send(RoomReport::BotSeat {
            match_id: self.id,
            name: name.to_owned(),
            seated,
        });
    }

    /// The newest bot gives its seat up (legacy BotManager.yieldSeat): the fill policy forgets it and the player leaves.
    fn yield_newest_bot(&mut self) -> bool {
        let Some((id, name)) = self
            .sim
            .players()
            .iter()
            .rev()
            .find(|player| player.seat.bot)
            .map(|player| (player.id, player.seat.name.clone()))
        else {
            return false;
        };
        if let Some(fill) = self.fill.as_mut() {
            if let Some(spec) = fill.yield_seat() {
                debug_assert_eq!(spec.name, name, "the fill policy seeded the newest bot");
            }
        }
        self.record(ReplayInput::Leave { player: id });
        self.sim.remove_player(id);
        self.report_bot(&name, false);
        self.bots.retain(|seat| seat.player() != id);
        self.bot_specs.retain(|(player, _)| *player != id);
        self.bot_lines.retain(|(player, _)| *player != id);
        true
    }

    /// Expire before processing commands and before stepping queued inputs. A committed result
    /// already ended this human's gameplay; its reliable delivery survives release of the lease.
    fn expire_leases(&mut self, now: Instant) {
        let mut i = 0;
        while i < self.humans.len() {
            let h = &self.humans[i];
            if h.deadline.as_ref().is_some_and(|d| *d.borrow() <= now)
                && h.result.is_none()
                && self.sim.player(h.player).is_some_and(|p| !p.resolved)
            {
                let id = h.player;
                if let Some(seat) = self.fences.iter_mut().find(|s| s.player == id) {
                    seat.fence = None; // This forced MIA is not authorized to bank a result.
                }
                self.leave(id, GoneReason::LeaseExpired);
            } else {
                i += 1;
            }
        }
    }

    fn leave(&mut self, player: PlayerId, why: GoneReason) {
        if let Some(i) = self.humans.iter().position(|h| h.player == player) {
            let h = self.humans.remove(i);
            if let Some(o) = h.outbox {
                o.close(RejectCode::Kicked);
                self.retire_outbox(&o);
            }
            // Leaving a run one still stands in is MIA: the sim resolves it now and its result is reported with the next
            // step's events; the director holds the profile until it is written.
            let result_pending = self
                .fences
                .iter()
                .any(|seat| seat.player == player && seat.fence.is_some())
                && self.sim.phase() == Phase::Active
                && self.sim.player(player).is_some_and(|p| !p.resolved);
            self.record(ReplayInput::Leave { player });
            self.sim.remove_player(player);
            let _ = self.reports.send(RoomReport::Seat {
                match_id: self.id,
                profile: h.profile.clone(),
                event: SeatEvent::Gone(why),
            });
            let _ = self.reports.send(RoomReport::Left {
                admission: h.admission,
                match_id: self.id,
                profile: h.profile,
                fence: h.fence,
                result_pending,
            });
        }
    }

    pub fn next_deadline(&self) -> Instant {
        self.pacer.next_deadline()
    }

    /// Runs the steps the grid owes and publishes once. Returns false when the room should close.
    pub fn wake(&mut self, now: Instant) -> bool {
        self.expire_leases(now);
        // Reconnect grace: a seat whose client did not come back is a leave (MIA in a run).
        let expired: Vec<PlayerId> = self
            .humans
            .iter()
            .filter(|h| {
                h.dropped_at
                    .is_some_and(|t| now.duration_since(t) >= self.reconnect_grace)
            })
            .map(|h| h.player)
            .collect();
        for p in expired {
            self.leave(p, GoneReason::GraceExpired);
        }
        // A training room whose seat stopped sending accepted inputs closes: there is no run to settle and nothing to bank.
        if self.training {
            let since = self.last_input_at.unwrap_or(self.opened_at);
            if now.duration_since(since) >= self.training_idle {
                return self.close();
            }
        }
        if self.humans.is_empty()
            && self.sim.phase() != Phase::Ended
            && (self.had_human
                || self.draining
                || now.duration_since(self.opened_at) >= UNCLAIMED_TIMEOUT)
        {
            // Nobody left to play for: a lobby closes; a run's remaining players are all bots or gone. A room nobody has
            // joined yet waits for its first player (its admission may be acquiring a durable lease off the tick), unless
            // the server is draining: no admission can seat after that, so an empty room has nothing left to wait for.
            return self.close();
        }
        let slot = self.pacer.next_deadline();
        let dropped_before = self.pacer.dropped;
        let steps = self.pacer.due(now);
        if steps == 0 {
            return true;
        }
        #[cfg(test)]
        if !self.stall.is_zero() {
            std::thread::sleep(self.stall);
        }
        // One add per shared counter for this wake (the histograms add one bucket per value).
        self.metrics.wakes.fetch_add(1, Ordering::Relaxed);
        self.metrics
            .steps
            .fetch_add(u64::from(steps), Ordering::Relaxed);
        let dropped = self.pacer.dropped - dropped_before;
        if dropped > 0 {
            self.metrics
                .dropped_steps
                .fetch_add(dropped, Ordering::Relaxed);
        }
        self.metrics.note_wake(
            now.saturating_duration_since(slot),
            steps,
            self.pacer.period(),
        );
        self.wake_events.clear();
        for _ in 0..steps {
            self.fill_step();
            let t = std::time::Instant::now();
            // Input age: every Input message the room took since the last step is applied by this one.
            for h in &mut self.humans {
                if let Some(at) = h.input_received.take() {
                    self.metrics.input_age_us.record(
                        t.saturating_duration_since(at)
                            .as_micros()
                            .min(u128::from(u32::MAX)) as u32,
                    );
                }
            }
            self.last_step_start = Some(t);
            let mut proposed: Vec<(PlayerId, rh_sim::PublicCommand)> = Vec::new();
            drive_seats_queued(
                &mut self.sim,
                &mut self.bots,
                &self.last_events,
                &mut self.bot_sight,
                |player, cmd| proposed.push((player, cmd)),
            );
            self.enqueue_bot_commands(proposed);
            self.deliver_bot_commands();
            self.expire_leases(Instant::now().max(now));
            self.step_events.clear();
            self.sim.step_into(&mut self.step_events);
            let us = t.elapsed().as_micros().min(u32::MAX as u128) as u32;
            if let Some(r) = self.recorder.as_mut() {
                r.stepped(&self.sim);
            }
            self.step_us.record(us);
            self.metrics.step_us.record(us);
            rh_policy::feed_seats(&self.sim, &mut self.bots);
            self.log_brain_fallbacks();
            for h in &mut self.humans {
                if h.outbox.is_none() {
                    h.fx.rebind(&self.sim, h.player);
                    h.carry.clear();
                    continue;
                }
                match h.fx.feed(self.sim.fx_report()).and_then(|status| {
                    if status != rh_sim::FeedStatus::Duplicate {
                        h.fx.append_into(&self.sim, h.player, &mut h.carry)?;
                    }
                    Ok(status)
                }) {
                    Ok(rh_sim::FeedStatus::Duplicate) => {}
                    Ok(_) => {}
                    Err(e) => {
                        h.carry.clear();
                        self.frame_refusals += 1;
                        if self.frame_refusals_logged == 0 {
                            self.frame_refusals_logged += 1;
                            eprintln!("[room] temporal projection refused: {e:?}");
                        }
                    }
                }
            }
            self.wake_events.extend_from_slice(&self.step_events);
            std::mem::swap(&mut self.last_events, &mut self.step_events);
        }
        self.clock
            .store(self.sim.server_time().to_bits(), Ordering::Relaxed);
        let t = std::time::Instant::now();
        // The buffer leaves the room for the call (publish borrows the room mutably) and comes back.
        let all_events = std::mem::take(&mut self.wake_events);
        // A training room never resolves a run: a between-tick down event is not a result let alone banked, and no result
        // frame is ever sent to its seat.
        let all_events = if self.training {
            let mut kept = all_events;
            kept.retain(|e| !matches!(e, Event::Resolved { .. }));
            kept
        } else {
            all_events
        };
        self.keep_results(&all_events);
        self.publish(&all_events);
        self.metrics
            .publish_us
            .record(t.elapsed().as_micros().min(u128::from(u32::MAX)) as u32);
        self.report_results(&all_events);
        self.wake_events = all_events;
        if self.sim.phase() != self.reported_phase {
            // Every phase change reaches the director (a lobby that closed into a run must stop receiving joins).
            self.reported_phase = self.sim.phase();
            let _ = self.reports.send(RoomReport::Phase {
                match_id: self.id,
                phase: self.sim.phase(),
            });
        }
        if self.sim.phase() == Phase::Ended && self.ended_at.is_none() {
            self.ended_at = Some(now);
        }
        if let Some(t) = self.ended_at {
            if now.duration_since(t) >= self.linger {
                return self.close();
            }
        }
        true
    }

    /// One tick's frames for every connected human. `events`: every event of the wake; the observation's own events are
    /// those of the last step (`last_events`) plus what each human's `carry` gathered from the earlier steps.
    fn publish(&mut self, events: &[Event]) {
        let (mut bytes_total, mut pushes) = (0u64, 0u64);
        for h in &mut self.humans {
            let Some(out) = &h.outbox else {
                h.carry.clear();
                continue;
            };
            // A forced replacement renews all capability namespaces off to the side. Failed
            // encoding or enqueue preserves the live epoch; successful delivery installs both.
            let resync_requested = out.state_resync_requested();
            let mut renewal = h
                .collections
                .resync_due(self.sim.tick(), resync_requested)
                .then(|| h.items.renewed());
            let observer = match renewal.as_mut() {
                Some(observer) => observer,
                None => &mut h.items,
            };
            let (frames, offer) = match self.publisher.frames_prepared(
                observer,
                h.spare.take(),
                &self.sim,
                h.ack_input,
                &[],
                &mut h.carry,
                events,
            ) {
                Ok(prepared) => prepared,
                Err(error) => {
                    if self.frame_refusals == 0 {
                        self.frame_refusals_logged += 1;
                        eprintln!("[room] unsupported observation: {error:?}");
                    }
                    self.frame_refusals += 1;
                    out.close(RejectCode::Malformed);
                    continue;
                }
            };
            if let Some(error) = frames.refused {
                if self.frame_refusals == 0 {
                    self.frame_refusals_logged += 1;
                    eprintln!("[room] frame not sent: {error}");
                }
                self.frame_refusals += 1;
                // A refused part of this tick cannot publish a new capability epoch or baseline.
                out.close(RejectCode::Malformed);
                continue;
            }
            let snap = match frames.snapshot {
                Ok(snap) => snap,
                Err(error) => {
                    if self.snapshot_refusals == 0 {
                        self.snapshot_refusals_logged += 1;
                        eprintln!("[room] snapshot not sent: {error}");
                    }
                    self.snapshot_refusals += 1;
                    out.close(RejectCode::Malformed);
                    continue;
                }
            };
            let mut state = match h.collections.prepare(
                self.publisher.observation(),
                offer.props_changed(),
                offer.rewards_changed(),
                resync_requested,
            ) {
                Ok(pending) => pending,
                Err(error) => {
                    if self.frame_refusals == 0 {
                        self.frame_refusals_logged += 1;
                        eprintln!("[room] reliable state not sent: {error}");
                    }
                    self.frame_refusals += 1;
                    out.close(RejectCode::Malformed);
                    continue;
                }
            };
            let bytes = [&state.frame, &frames.important, &frames.fx]
                .into_iter()
                .flatten()
                .map(Vec::len)
                .sum::<usize>()
                + snap.min_len();
            let (spare, accepted) = out.push_tick_stamped(
                self.last_step_start,
                state.frame.take(),
                frames.important,
                frames.fx,
                Some(snap),
            );
            h.spare = spare;
            if accepted {
                let forced = state.commit(&mut h.collections);
                offer.commit();
                if let Some(observer) = renewal {
                    h.items = observer;
                }
                if forced {
                    out.state_resync_answered();
                }
                self.bytes_out += bytes as u64;
                bytes_total += bytes as u64;
                pushes += 1;
            }
            // Failed reliable enqueue drops both prepared guards: no handles or baseline advance.
        }
        if pushes > 0 {
            self.metrics
                .bytes_out
                .fetch_add(bytes_total, Ordering::Relaxed);
            self.metrics
                .outbox_pushes
                .fetch_add(pushes, Ordering::Relaxed);
        }
    }

    /// Keeps each human's own resolved result on its seat (see [`HumanSeat::result`]).
    fn keep_results(&mut self, events: &[Event]) {
        if self.training {
            return;
        }
        for e in events {
            if let Event::Resolved {
                id,
                result,
                bot: false,
            } = e
            {
                if let Some(h) = self.humans.iter_mut().find(|h| h.player == *id) {
                    h.result = Some(wire::result(result));
                }
            }
        }
    }

    /// Every resolved result goes to the director (durable storage), with the fence it was earned under.
    fn report_results(&mut self, events: &[Event]) {
        // A training room never settles or banks a run: no result reaches the director (and so no durable match).
        if self.training {
            return;
        }
        for e in events {
            if let Event::Resolved { id, result, bot } = e {
                let (fence, admission) = if *bot {
                    (None, None)
                } else {
                    let Some(seat) = self
                        .fences
                        .iter()
                        .find(|seat| seat.player == *id && seat.fence.is_some())
                    else {
                        continue;
                    };
                    (seat.fence, seat.admission)
                };
                let bot_spec = if *bot {
                    self.bot_specs
                        .iter()
                        .find(|(player, _)| player == id)
                        .map(|(_, spec)| spec.clone())
                } else {
                    None
                };
                if let Some(spec) = &bot_spec {
                    if !self.reported_bots.insert(spec.number) {
                        continue;
                    }
                }
                // Source BotManager.onResult: the seat's `runner.end(result)` summary, asked once.
                let bot_choices = if *bot {
                    self.bots
                        .iter_mut()
                        .find(|seat| seat.player() == *id)
                        .and_then(|seat| seat.finish(Some(result)))
                        .map(|finish| finish.choices)
                        .unwrap_or_default()
                } else {
                    Vec::new()
                };
                let _ = self.reports.send(RoomReport::Result {
                    admission,
                    match_id: self.id,
                    profile: result.profile.clone(),
                    bot: *bot,
                    bot_spec,
                    bot_choices,
                    fence,
                    result: result.clone(),
                    roster: Roster {
                        humans: self.fences.len() as u32,
                        bots: self.bots.len() as u32,
                    },
                });
            }
        }
    }

    fn close(&mut self) -> bool {
        self.finish(false)
    }

    /// Closes the room. `interrupted` (the hosting layer's abort): events not yet reported are discarded, so no result is
    /// invented for a match that did not finish; seats and leases are freed exactly as for a normal close.
    fn finish(&mut self, interrupted: bool) -> bool {
        if self.closed {
            return false;
        }
        self.closed = true;
        // Results emitted between steps (the last human's MIA leave) would otherwise die with the room.
        let pending = self.sim.take_events();
        if !interrupted {
            self.report_results(&pending);
        }
        let why = if interrupted {
            GoneReason::Aborted
        } else {
            GoneReason::MatchOver
        };
        for h in std::mem::take(&mut self.humans) {
            let _ = self.reports.send(RoomReport::Seat {
                match_id: self.id,
                profile: h.profile.clone(),
                event: SeatEvent::Gone(why),
            });
            if let Some(o) = &h.outbox {
                o.close(RejectCode::Kicked);
                self.retire_outbox(o);
            }
            // A result this seat earned was reported above or earlier; the director keeps the profile until its write
            // lands (the write releases the lease), so `result_pending` stays about results still to come.
            let _ = self.reports.send(RoomReport::Left {
                admission: h.admission,
                match_id: self.id,
                profile: h.profile,
                fence: h.fence,
                result_pending: false,
            });
        }
        let replay = self.recorder.take().map(|r| Box::new(r.replay));
        let _ = self.reports.send(RoomReport::Closed {
            match_id: self.id,
            stats: self.stats(),
            final_hash: self.sim.state_hash(),
            replay,
        });
        false
    }

    /// The room reported `Closed`; its task must end.
    pub fn is_closed(&self) -> bool {
        self.closed
    }

    pub fn stats(&self) -> RoomStats {
        // Connections the room still holds count live; the ones it let go were folded in when it did.
        let mut outboxes = self.retired_outboxes;
        for o in self.humans.iter().filter_map(|h| h.outbox.as_deref()) {
            outboxes.add(o.counters());
        }
        RoomStats {
            ticks: self.step_us.count(),
            step_us_p50: self.step_us.quantile(0.5),
            step_us_p99: self.step_us.quantile(0.99),
            step_us_max: self.step_us.max(),
            late_wakes: self.pacer.late_wakes,
            dropped_steps: self.pacer.dropped,
            bytes_out: self.bytes_out,
            bytes_enqueued_total: outboxes.bytes_enqueued_total,
            fx_dropped: outboxes.fx_dropped,
            snapshots_skipped: outboxes.snapshots_skipped,
            snapshots_full: outboxes.snapshots_full,
            snapshots_delta: outboxes.snapshots_delta,
            snapshot_bytes_sent: outboxes.snapshot_bytes_sent,
            snapshot_refusals: self.snapshot_refusals,
            snapshot_refusals_logged: self.snapshot_refusals_logged,
            frame_refusals: self.frame_refusals,
            frame_refusals_logged: self.frame_refusals_logged,
            crashed: false,
            rtt_samples: self.rtt_samples,
            rtt_last_ms: self.rtt_last_ms,
            rtt_max_ms: self.rtt_max_ms,
            rewind_clamps: self.rewind_clamps,
        }
    }

    /// A call into the room panicked (see `guarded`): its connections are closed and `Closed` is reported (marked `crashed`),
    /// so the director frees every seat and gives the leases back instead of holding them until the process restarts.
    /// The simulation is not touched (its state is suspect: no results, no final hash). Never panics itself.
    fn crashed(&mut self) {
        eprintln!(
            "[server] ERROR event=room_task_panicked room={} ticks={} humans={} action=reporting_closed",
            crate::control::room_id_hex(self.id),
            self.stats().ticks,
            self.humans.len()
        );
        for h in &self.humans {
            if let Some(o) = &h.outbox {
                o.close(RejectCode::Kicked);
            }
        }
        let stats = RoomStats {
            crashed: true,
            ..self.stats()
        };
        let _ = self.reports.send(RoomReport::Closed {
            match_id: self.id,
            stats,
            final_hash: 0,
            replay: None,
        });
    }
}

/// Runs one call into the room for [`run_room`]. A panic inside it (a simulation assert, a bug) is caught, the room is
/// reported closed ([`Room::crashed`]) and `None` is returned: the task then ends. Only the room's own code is guarded,
/// so a task cancelled while its thread unwinds from someone else's panic reports nothing.
fn guarded<T>(room: &mut Room, f: impl FnOnce(&mut Room) -> T) -> Option<T> {
    match std::panic::catch_unwind(std::panic::AssertUnwindSafe(|| f(room))) {
        Ok(v) => Some(v),
        Err(_) => {
            room.crashed();
            None
        }
    }
}

/// The short kind of a seat fault, for the log (never the payload of an error).
fn fault_kind(fault: &rh_policy::Fault) -> &'static str {
    use rh_policy::Fault;
    match fault {
        Fault::Input(_) => "input refused",
        Fault::Delivery(_) => "delivery error",
        Fault::FxProjection(_) => "fx projection error",
        Fault::ItemObservation(_) => "item observation error",
        Fault::WorkerStopped => "worker stopped",
        Fault::Panicked => "panicked",
        Fault::OverBudget { .. } => "over budget",
        Fault::Late { .. } => "late",
        Fault::IncompatibleContract { .. } => "incompatible contract",
        Fault::IncompatibleObservation { .. } => "incompatible observation",
    }
}

/// The room's task: commands in arrival order between steps; one wake per grid slot.
///
/// C-TICK note: the select is `biased` with the step deadline first. When a step is due and commands are queued at the same
/// poll, the step runs first and the commands are applied (and acknowledged) after it, so they take effect in the next
/// step's input window. Before, either order was possible and a paused-clock run varied with the runtime's random branch
/// choice (about 1.4% in instructions). Only already-allowed orderings remain: a command arriving a moment after the wake
/// would have met the same order, and replays record the order actually applied. The deadline is one pinned [`Sleep`]
/// that is reset only when the grid's next slot changes, not a new timer per loop turn.
///
/// Overload: under sustained overload (a wake that takes longer than its slot), the next deadline is already past when the
/// select runs again, and the biased select alone would step forever without polling the inbox (joins, leaves and inputs
/// would starve until every client is closed `TooSlow`). So when a wake ends with its next step already due, the commands
/// already queued (at most [`INBOUND_CAP`], one inbox) are applied before that step: at most one wake runs between two
/// looks at the inbox. That is the same order a command arriving during the wake meets in the normal case (it is applied
/// after the step that was running, before the next one), so no new ordering is introduced; in the normal case (the next
/// deadline is still ahead) nothing changes and a due step still runs before a queued command.
///
/// A panic in the room is caught at the call (`guarded`) and reported as `Closed`, marked `crashed`.
///
/// [`Sleep`]: tokio::time::Sleep
pub async fn run_room(mut room: Room, mut rx: mpsc::Receiver<RoomCmd>) {
    let room = &mut room;
    let mut armed = room.next_deadline();
    let sleep = tokio::time::sleep_until(armed);
    tokio::pin!(sleep);
    loop {
        tokio::select! {
            biased;
            _ = &mut sleep => {
                if guarded(room, |r| r.wake(Instant::now())) != Some(true) { return; }
                // Overloaded (the next step is already due): what is queued now is applied before it.
                if room.next_deadline() <= Instant::now() {
                    for _ in 0..INBOUND_CAP {
                        match rx.try_recv() {
                            Ok(cmd) => {
                                if guarded(room, |r| r.handle(cmd, Instant::now())).is_none() || room.is_closed() { return; }
                            }
                            Err(mpsc::error::TryRecvError::Empty) => break,
                            Err(mpsc::error::TryRecvError::Disconnected) => {
                                guarded(room, Room::close);
                                return;
                            }
                        }
                    }
                }
                // The sleep has fired: arm it for the next slot (a wake that ran no step leaves the slot where it was,
                // and re-arming it then behaves as a fresh `sleep_until`, as before).
                armed = room.next_deadline();
                sleep.as_mut().reset(armed);
            }
            cmd = rx.recv() => match cmd {
                Some(cmd) => {
                    if guarded(room, |r| r.handle(cmd, Instant::now())).is_none() || room.is_closed() { return; }
                    let next = room.next_deadline();
                    if next != armed {
                        armed = next;
                        sleep.as_mut().reset(armed);
                    }
                }
                None => { guarded(room, Room::close); return; }
            },
        }
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
