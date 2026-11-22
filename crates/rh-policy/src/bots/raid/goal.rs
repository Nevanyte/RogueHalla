//! Goals and executors (P-RAID-0): `RaidGoal`, the `Executor` trait every goal kind implements, and what an executor
//! returns each think (`Step`: a status, intents for the Humanizer, acts for the paced queue).
//!
//! A goal names its target by a STABLE key (a camp index, a point, a chest's public key, an actor's rendered key, a
//! quest's definition id), never by a frame handle: handles live one frame and are resolved at emit time by `refs.rs`.
//!
//! What an executor may do: read the percept (`Cx::p`), ask the room path service within its per-tick budget, draw from
//! the arbiter stream it is lent, keep its own memory between thinks (stable keys only), and return intents and acts.
//! What it may not do: emit `PublicCommand`s itself, hold a handle past its frame, read anything but the percept and the
//! match's public statics, use a clock or a hash-ordered collection, or reach into another executor's state.
use super::acts::Act;
use super::arbiter::Prices;
use super::percept::RaidPercept;
use crate::bots::knowledge::Knowledge;
use crate::bots::paths::RoomPathService;
use crate::bots::planning::PlanMap;
use crate::bots::random::Rng;
use crate::bots::types::{Intent, PersonaId, PersonaParams, TierId, TierParams};
use rh_sim::Vec2;

/// Every goal kind the plan of record names (`docs/backlog/BALANCE_LAB_PATCH.md` section 1), declared up front so a
/// later packet adds its executor and candidates without editing this file. The declaration order is the arbiter's
/// tie-break order (an earlier kind wins an exact tie).
#[derive(Clone, Copy, Debug, PartialEq, Eq, PartialOrd, Ord)]
pub enum GoalKind {
    /// Heal up, drink, disengage (P-RAID-C).
    Sustain,
    /// Kill an elite and take its chest (P-RAID-E).
    Elite,
    /// Clear a normal camp (this packet: `exec/farm.rs`).
    Farm,
    /// Accept a quest, walk to its objective, send the offered interaction (P-RAID-Q1, `quest/walker.rs`).
    QuestWalk,
    /// Hold a place until the quest completes (P-RAID-Q2, `quest/dwell.rs`).
    QuestDwell,
    /// Answer a quest decision (P-RAID-Q3, `quest/decide.rs`).
    QuestDecide,
    /// Hunt a kill quest's target (P-RAID-Q4, `quest/hunt.rs`).
    QuestHunt,
    /// A PvP or event quest step (P-RAID-P, `quest/pvp.rs`).
    QuestPvp,
    /// Search for tokens (P-RAID-W, `quest/search.rs`).
    QuestSearch,
    /// Open a reward chest (this packet: `exec/chest.rs`).
    Chest,
    /// A loot detour to a container (later).
    Loot,
    /// Fight a player (P-RAID-C / P-RAID-P). This packet proposes no player fight: bots that hunt each other end matches
    /// in minutes, and the lab needs matches that reach minute 5 to 12.
    Fight,
    /// Go to the coliseum (P-RAID-P).
    Coliseum,
    /// Walk somewhere new when nothing else pays (this packet: `exec/travel.rs`).
    Roam,
}

impl GoalKind {
    /// The persona's appetite for this kind of goal: a multiplier on the candidate's score. Read from the legacy persona
    /// table (`bots/types.rs` PERSONAS), so a Farmer and a Hunter rank the same candidates differently.
    pub fn persona_mult(self, p: &PersonaParams) -> f64 {
        match self {
            Self::Farm => 0.5 + 0.5 * p.pve,
            Self::Elite => 0.5 + 0.5 * p.elite_appetite,
            Self::Chest | Self::Loot => 0.5 + 0.5 * p.loot,
            Self::Fight | Self::QuestPvp => 0.5 + 0.5 * p.pvp,
            Self::Coliseum => 0.5 + 0.5 * p.coliseum,
            Self::Sustain
            | Self::QuestWalk
            | Self::QuestDwell
            | Self::QuestDecide
            | Self::QuestHunt
            | Self::QuestSearch
            | Self::Roam => 1.,
        }
    }
    /// The tier's execution quality for this kind of goal: a multiplier on the candidate's score. Kinds whose outcome
    /// rides on fighting well scale with the tier; walking and picking do not.
    pub fn tier_mult(self, t: &TierParams) -> f64 {
        let skill = match t.id {
            TierId::Easy => 0.85,
            TierId::Normal => 1.,
            TierId::Hard => 1.15,
        };
        match self {
            Self::Elite | Self::Fight | Self::QuestHunt | Self::QuestPvp => skill,
            _ => 1.,
        }
    }
}

/// What a goal aims at, by stable key (never a frame handle).
#[derive(Clone, Debug, PartialEq)]
pub enum Target {
    None,
    /// A camp by its index (`WireCamp[i]` is `PlanMap.camps[i]`).
    Camp(u32),
    /// A point on the map.
    Point(Vec2),
    /// A reward chest or container by its public key (`perception::ChestRef::key`, `PropRef::key`).
    Key(String),
    /// An actor by its rendered key (`p<id>` a player, `n<id>` an npc).
    Actor(String),
    /// A quest row by its definition id (`WireQuestRow::def`), with the stage-specific index the executor wants.
    Quest { def: String, stage: u8 },
}

impl Target {
    /// The stable tie-break order inside one goal kind.
    pub fn order_key(&self) -> (u8, i64, i64, &str) {
        match self {
            Self::None => (0, 0, 0, ""),
            Self::Camp(i) => (1, i64::from(*i), 0, ""),
            Self::Point(p) => (2, p.x.round() as i64, p.y.round() as i64, ""),
            Self::Key(k) => (3, 0, 0, k),
            Self::Actor(k) => (4, 0, 0, k),
            Self::Quest { def, stage } => (5, i64::from(*stage), 0, def),
        }
    }
}

/// One goal: a kind and its target.
#[derive(Clone, Debug, PartialEq)]
pub struct RaidGoal {
    pub kind: GoalKind,
    pub target: Target,
}

impl RaidGoal {
    pub fn new(kind: GoalKind, target: Target) -> Self {
        Self { kind, target }
    }
    /// The same goal for hysteresis: same kind and target (two roam points in the same three-tile cell are the same).
    pub fn same(&self, other: &Self) -> bool {
        if self.kind != other.kind {
            return false;
        }
        match (&self.target, &other.target) {
            (Target::Point(a), Target::Point(b)) => {
                let cell = |x: f64| ((x / (3. * rh_sim::constants::TILE)) + 0.5).floor() as i64;
                cell(a.x) == cell(b.x) && cell(a.y) == cell(b.y)
            }
            (a, b) => a == b,
        }
    }
}

/// An executor's verdict on its goal after one think.
#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub enum Status {
    Running,
    Done,
    Failed,
}

/// What one think of an executor yields.
#[derive(Clone, Debug, PartialEq)]
pub struct Step {
    pub status: Status,
    /// Clicks for the Humanizer (moves, locks, picks): paced and humanized there.
    pub intents: Vec<Intent>,
    /// Public commands the shared `Command` enum does not carry (quest accept and interact, potions, trade): paced by
    /// the act queue (`acts.rs`).
    pub acts: Vec<Act>,
    pub why: &'static str,
}

impl Step {
    pub fn running(intents: Vec<Intent>, why: &'static str) -> Self {
        Self {
            status: Status::Running,
            intents,
            acts: Vec::new(),
            why,
        }
    }
    pub fn done(why: &'static str) -> Self {
        Self {
            status: Status::Done,
            intents: Vec::new(),
            acts: Vec::new(),
            why,
        }
    }
    pub fn failed(why: &'static str) -> Self {
        Self {
            status: Status::Failed,
            intents: Vec::new(),
            acts: Vec::new(),
            why,
        }
    }
    /// Nothing to add (a ride-along hook with nothing to say).
    pub fn none() -> Self {
        Self::running(Vec::new(), "")
    }
}

/// A candidate goal, priced in camp-equivalents (one normal camp cleared = 12 stat stacks = 1 camp-equivalent).
/// The arbiter scores it `(value x success - death x loss) / minutes`, times the kind's tier and persona multipliers.
#[derive(Clone, Debug, PartialEq)]
pub struct Candidate {
    pub goal: RaidGoal,
    /// Camp-equivalents gained when the goal succeeds.
    pub value: f64,
    /// Chance the attempt succeeds, 0..=1.
    pub success: f64,
    /// Chance the bot dies trying, 0..=1.
    pub death: f64,
    /// Camp-equivalents a death costs (what dying loses).
    pub loss: f64,
    /// Expected minutes to finish, travel included (floored by the arbiter so a goal underfoot is not infinite).
    pub minutes: f64,
}

/// What an executor reads and may use in one call. Built by the seat each active frame.
pub struct Cx<'a> {
    pub p: &'a RaidPercept,
    pub paths: &'a mut RoomPathService,
    pub plan: &'a PlanMap,
    pub knowledge: Option<&'a Knowledge>,
    pub tier: TierId,
    pub persona: PersonaId,
    pub prices: &'a Prices,
    /// When the owner last started standing still (Memory own motion).
    pub still_since: f64,
    /// The goal the seat holds now and its last status (the arbiter's incumbent).
    pub goal: &'a RaidGoal,
    pub status: Status,
    /// The arbiter's stream (tag 16), lent for this call. Executors are called in registry order, so the draw order is
    /// fixed.
    pub rng: &'a mut Rng,
}

impl Cx<'_> {
    pub fn tier_params(&self) -> &'static TierParams {
        TierParams::by_id(self.tier)
    }
    pub fn persona_params(&self) -> &'static PersonaParams {
        PersonaParams::by_id(self.persona)
    }
}

/// One goal kind's module: it proposes candidates for its kind, executes the goals of its kind, and may add intents or
/// acts on top of whatever goal is running (a chest pick, a potion, a spell). Registered once in `exec::registry`.
pub trait Executor: Send {
    /// The goal kinds this executor runs (empty for a module that only rides along).
    fn kinds(&self) -> &'static [GoalKind];
    /// Forget everything: a new match.
    fn reset(&mut self) {}
    /// Read one active frame (bookkeeping only), every frame, before any proposal or think.
    fn observe(&mut self, _cx: &mut Cx<'_>) {}
    /// Add this module's candidates for the arbiter (called on strategy ticks only).
    fn propose(&mut self, _cx: &mut Cx<'_>, _out: &mut Vec<Candidate>) {}
    /// The goal was just adopted (it was not the incumbent).
    fn start(&mut self, _goal: &RaidGoal, _cx: &mut Cx<'_>) {}
    /// One think for a goal of one of `kinds()`.
    fn act(&mut self, goal: &RaidGoal, cx: &mut Cx<'_>) -> Step;
    /// One think's additions while any goal runs (its status is ignored).
    fn ride_along(&mut self, _goal: &RaidGoal, _cx: &mut Cx<'_>) -> Step {
        Step::none()
    }
}
