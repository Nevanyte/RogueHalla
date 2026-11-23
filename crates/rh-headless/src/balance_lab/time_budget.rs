//! P-BAL-LAB-TIME-1: where each seat's time goes. The observer builds one [`TimeStep`] per match step (a plain fact per
//! seat) and pushes it into a [`TimeTally`]; tests push hand-written steps. Rules: docs/backlog/packets/P-BAL-LAB-TIME-1.md
//! (decisions 1 to 4). The tally keeps counters and last-hit steps per seat, never a row per step.
use std::collections::BTreeMap;

/// A hit keeps its kind of fight for this many steps counted from the hit's own step (3 s at 20 Hz): steps `s` to
/// `s + 59` are in the window, step `s + 60` is out.
pub const FIGHT_WINDOW_STEPS: u32 = 60;
/// A seat moved this step when its position changed by at least this many world units.
pub const MOVED_DIST: f64 = 1.0;
/// Ticks in a match minute at the live 50 ms step (the lab's `MINUTE`).
pub const MINUTE_TICKS: u32 = 1200;
/// The minute bands of the pilot: minutes 0 to 5, 5 to 11, 11 and later.
pub const BAND_STARTS: [u32; 2] = [5, 11];
/// The minutes whose farm stacks a seat row carries.
pub const STACK_MINUTES: [u32; 3] = [5, 8, 12];

/// Where one seat-step went.
#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub enum Bucket {
    Dead,
    Elite,
    Camp,
    Player,
    QuestMoving,
    QuestStill,
    Moving,
    Resting,
    Idle,
}

impl Bucket {
    pub const ALL: [Bucket; 9] = [
        Bucket::Dead,
        Bucket::Elite,
        Bucket::Camp,
        Bucket::Player,
        Bucket::QuestMoving,
        Bucket::QuestStill,
        Bucket::Moving,
        Bucket::Resting,
        Bucket::Idle,
    ];

    /// The key the report uses.
    pub fn name(self) -> &'static str {
        match self {
            Bucket::Dead => "dead",
            Bucket::Elite => "elite",
            Bucket::Camp => "camp",
            Bucket::Player => "player",
            Bucket::QuestMoving => "quest_moving",
            Bucket::QuestStill => "quest_still",
            Bucket::Moving => "moving",
            Bucket::Resting => "resting",
            Bucket::Idle => "idle",
        }
    }
}

/// Seat-steps per bucket.
#[derive(Clone, Debug, Default, PartialEq, Eq)]
pub struct Buckets {
    pub dead: u64,
    pub elite: u64,
    pub camp: u64,
    pub player: u64,
    pub quest_moving: u64,
    pub quest_still: u64,
    pub moving: u64,
    pub resting: u64,
    pub idle: u64,
}

impl Buckets {
    pub fn get(&self, b: Bucket) -> u64 {
        match b {
            Bucket::Dead => self.dead,
            Bucket::Elite => self.elite,
            Bucket::Camp => self.camp,
            Bucket::Player => self.player,
            Bucket::QuestMoving => self.quest_moving,
            Bucket::QuestStill => self.quest_still,
            Bucket::Moving => self.moving,
            Bucket::Resting => self.resting,
            Bucket::Idle => self.idle,
        }
    }

    pub fn total(&self) -> u64 {
        Bucket::ALL.into_iter().map(|b| self.get(b)).sum()
    }
}

/// What the observer knows of one seat after one step.
#[derive(Clone, Debug, Default, PartialEq)]
pub struct SeatStep {
    /// `Player::standing()`.
    pub alive: bool,
    /// Distance from the seat's position at the previous step (0 on its first step and on a dead step).
    pub moved: f64,
    /// hp at max.
    pub hp_full: bool,
    /// The sim's attempt probe holds an open attempt of the seat.
    pub quest_open: bool,
    /// The seat dealt or took a hit this step against a mob of an elite camp / of a normal camp (or with no camp) /
    /// against another player.
    pub hit_elite: bool,
    pub hit_camp: bool,
    pub hit_player: bool,
    /// Credits booked to the seat this step.
    pub camps_normal: u32,
    pub camps_elite: u32,
    pub deaths: u32,
    pub quests_completed: u32,
    pub elite_kills: u32,
    /// Named chest picks of this step (option names).
    pub picks: Vec<String>,
    /// `(minute, stacks)` when the observer sampled the seat's farm stacks this step.
    pub farm_stacks: Option<(u32, u64)>,
    /// The weapon in hand, when read.
    pub weapon: Option<String>,
}

/// One match step: the sim's tick after it, and one [`SeatStep`] per seat.
#[derive(Clone, Debug, Default, PartialEq)]
pub struct TimeStep {
    pub tick: u32,
    pub seats: Vec<SeatStep>,
}

/// What one seat's match came to.
#[derive(Clone, Debug, Default, PartialEq)]
pub struct SeatRow {
    pub seat: usize,
    /// Seat-steps (every step of the match, alive or not).
    pub steps: u64,
    pub time: Buckets,
    /// The same steps split by minute band (see [`BAND_STARTS`]).
    pub bands: [Buckets; 3],
    pub camps_normal: u64,
    pub camps_elite: u64,
    pub deaths: u64,
    pub quests_completed: u64,
    pub elite_kills: u64,
    pub picks: BTreeMap<String, u64>,
    /// The farm stacks sampled at [`STACK_MINUTES`].
    pub farm_stacks: [Option<u64>; 3],
    /// The last weapon read.
    pub weapon: Option<String>,
}

/// Incremental tally of a match. STUB: counts nothing.
pub struct TimeTally {
    seats: usize,
}

impl TimeTally {
    pub fn new(seats: usize) -> Self {
        Self { seats }
    }

    pub fn push(&mut self, _step: &TimeStep) {}

    /// One row per seat, in seat order.
    pub fn finish(self) -> Vec<SeatRow> {
        (0..self.seats)
            .map(|seat| SeatRow {
                seat,
                ..SeatRow::default()
            })
            .collect()
    }
}

/// The whole match at once: pushes every step and finishes.
pub fn tally_time(steps: &[TimeStep], seats: usize) -> Vec<SeatRow> {
    let mut t = TimeTally::new(seats);
    for s in steps {
        t.push(s);
    }
    t.finish()
}
