//! Quest spell cast rules (P-RAID-S): when to cast a held quest reward on the Q and E slots.
//! The rule table is data: one row per non-permanent award (`SPELL_RULES`), each with the situation it fires in, where it
//! aims and one phrase saying why. `decide_cast` is pure; `CastGate` keeps the seat from resending a refused cast.
//! STUB (P-RAID-S): the shapes are final, the bodies are the implementer's.
use super::acts::Act;
use rh_sim::Vec2;

/// How long a cast stays "asked" when the frame shows neither a spent charge nor a started cooldown (ms): the sim drops a
/// refused cast silently, so a seat waits this long before it asks again.
pub const CAST_RETRY_WAIT_MS: f64 = 2_000.;

/// What the running fight's foe is.
#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub enum FoeKind {
    Player,
    Elite,
    CampMob,
}

/// The foe of the running fight, by this frame's rendered key.
#[derive(Clone, Debug, PartialEq)]
pub struct Foe {
    pub key: String,
    pub kind: FoeKind,
    pub at: Vec2,
    pub distance: f64,
}

/// What one think reads of the percept, once.
#[derive(Clone, Debug, PartialEq)]
pub struct SpellScene {
    pub me: Vec2,
    /// Rooted or stunned (the frame shows no taunt or pull).
    pub impaired: bool,
    pub foe: Option<Foe>,
    /// Distance of each living hostile actor, any order.
    pub hostile_distances: Vec<f64>,
    /// While the running goal is a leave from a lost fight: the unit vector away from it.
    pub leave_away: Option<Vec2>,
    pub hp_share: f64,
}

/// One held quest spell as the frame shows it (`WireQuestSpell`): `slot` is the wire slot, 0 = Q, 1 = E.
#[derive(Clone, Copy, Debug, PartialEq)]
pub struct HeldSpell {
    pub slot: u8,
    pub reward: u8,
    pub charges: u8,
    pub ready_at: f64,
}

/// The situation a rule fires in.
#[derive(Clone, Copy, Debug, PartialEq)]
pub enum When {
    /// Not ruled yet.
    Never,
    Impaired,
    Foe {
        kinds: &'static [FoeKind],
        within: f64,
    },
    Crowd {
        at_least: u32,
        within: f64,
    },
    Leaving,
    HpBelow(f64),
}

/// Where a rule aims.
#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub enum RuleAim {
    OwnPosition,
    FoeActor,
    FoePosition,
    AwayAlongLeave,
}

/// One row of the table.
#[derive(Clone, Copy, Debug, PartialEq)]
pub struct SpellRule {
    /// The wire reward id (`Award as u8`).
    pub reward: u8,
    pub when: When,
    pub aim: RuleAim,
    pub why: &'static str,
}

/// One row per non-permanent award (19), in priority order.
pub const SPELL_RULES: &[SpellRule] = &[];

/// The cast to ask for this think: at most one, the first ready rule in table order.
pub fn decide_cast(_scene: &SpellScene, _held: &[HeldSpell], _now: f64) -> Option<Act> {
    None
}

/// The one piece of memory of the spell executor: the cast it last asked for and what the frame showed then.
#[derive(Clone, Debug, Default)]
pub struct CastGate {
    asked: Vec<(u8, u8, u8, f64, f64)>,
}

impl CastGate {
    /// Let `ask` through unless the same slot was asked and the frame shows neither a spent charge, a started cooldown, a
    /// changed slot, nor `CAST_RETRY_WAIT_MS` gone by.
    pub fn pass(&mut self, _ask: Option<Act>, _held: &[HeldSpell], _now: f64) -> Option<Act> {
        None
    }
    /// A new match.
    pub fn reset(&mut self) {
        self.asked.clear();
    }
}
