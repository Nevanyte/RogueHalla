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

    pub fn add(&mut self, b: Bucket) {
        match b {
            Bucket::Dead => self.dead += 1,
            Bucket::Elite => self.elite += 1,
            Bucket::Camp => self.camp += 1,
            Bucket::Player => self.player += 1,
            Bucket::QuestMoving => self.quest_moving += 1,
            Bucket::QuestStill => self.quest_still += 1,
            Bucket::Moving => self.moving += 1,
            Bucket::Resting => self.resting += 1,
            Bucket::Idle => self.idle += 1,
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

/// The minute band of a tick: 0 (minutes 0 to 4), 1 (5 to 10), 2 (11 on).
pub fn band_of(tick: u32) -> usize {
    let minute = tick / MINUTE_TICKS;
    BAND_STARTS.iter().filter(|&&m| minute >= m).count()
}

/// Per seat: the row so far and the step of the last hit of each kind (elite, camp, player).
struct SeatState {
    row: SeatRow,
    last_hit: [Option<u64>; 3],
}

/// Incremental tally of a match: counters and the last hit steps per seat, never a row per step.
pub struct TimeTally {
    seats: Vec<SeatState>,
}

impl TimeTally {
    pub fn new(seats: usize) -> Self {
        Self {
            seats: (0..seats)
                .map(|seat| SeatState {
                    row: SeatRow {
                        seat,
                        ..SeatRow::default()
                    },
                    last_hit: [None; 3],
                })
                .collect(),
        }
    }

    pub fn push(&mut self, step: &TimeStep) {
        let band = band_of(step.tick);
        for (st, s) in self.seats.iter_mut().zip(&step.seats) {
            let n = st.row.steps;
            st.row.steps += 1;
            let bucket = if !s.alive {
                st.last_hit = [None; 3];
                Bucket::Dead
            } else {
                for (last, hit) in
                    st.last_hit
                        .iter_mut()
                        .zip([s.hit_elite, s.hit_camp, s.hit_player])
                {
                    if hit {
                        *last = Some(n);
                    }
                }
                let held = |k: usize| {
                    st.last_hit[k].is_some_and(|h| n - h < u64::from(FIGHT_WINDOW_STEPS))
                };
                let moved = s.moved >= MOVED_DIST;
                if held(0) {
                    Bucket::Elite
                } else if held(1) {
                    Bucket::Camp
                } else if held(2) {
                    Bucket::Player
                } else if s.quest_open {
                    if moved {
                        Bucket::QuestMoving
                    } else {
                        Bucket::QuestStill
                    }
                } else if moved {
                    Bucket::Moving
                } else if !s.hp_full {
                    Bucket::Resting
                } else {
                    Bucket::Idle
                }
            };
            st.row.time.add(bucket);
            st.row.bands[band].add(bucket);
            let r = &mut st.row;
            r.camps_normal += u64::from(s.camps_normal);
            r.camps_elite += u64::from(s.camps_elite);
            r.deaths += u64::from(s.deaths);
            r.quests_completed += u64::from(s.quests_completed);
            r.elite_kills += u64::from(s.elite_kills);
            for p in &s.picks {
                *r.picks.entry(p.clone()).or_default() += 1;
            }
            if let Some((minute, stacks)) = s.farm_stacks {
                if let Some(i) = STACK_MINUTES.iter().position(|&m| m == minute) {
                    r.farm_stacks[i] = Some(stacks);
                }
            }
            if let Some(w) = &s.weapon {
                r.weapon = Some(w.clone());
            }
        }
    }

    /// One row per seat, in seat order.
    pub fn finish(self) -> Vec<SeatRow> {
        self.seats.into_iter().map(|s| s.row).collect()
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
