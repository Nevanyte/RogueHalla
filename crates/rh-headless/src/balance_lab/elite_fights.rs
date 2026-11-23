//! P-BAL-LAB-ELITE-1: the elite fights of a whole match as a pure function of per-step facts. The observer builds one
//! [`EliteStep`] per step and calls [`tally_elites`] at the end; tests call it with hand-written steps. Rules:
//! docs/backlog/packets/P-BAL-LAB-ELITE-1.md (decisions 3 to 8).
use std::collections::BTreeMap;

/// A seat idle (no hit dealt or taken against the elite or its camp) for more than this is no longer in the fight.
pub const LEAVE_MS: f64 = 10_000.;

/// One seat dealt a hit to, or took a hit from, an elite or a mob of its camp, in one step.
#[derive(Clone, Debug, Default, PartialEq)]
pub struct Contact {
    pub seat: usize,
    /// The elite mob the fight is about (the camp's elite, whichever mob of the camp was hit).
    pub elite: u32,
    /// The elite's camp; negative when it has none.
    pub camp: i32,
    pub kind: String,
    /// The weapon of the hit (read only when it opens the attempt).
    pub weapon: String,
    /// The seat dealt the hit (false: the seat took it).
    pub dealt: bool,
    /// The other party is the elite itself (false: one of its camp's adds).
    pub on_elite: bool,
}

/// A seat died; the attacker is as the lab resolves it (`Death.by`, else the last hit inside the kill-credit window).
#[derive(Clone, Debug, Default, PartialEq)]
pub struct SeatDeath {
    pub seat: usize,
    pub attacker_mob: Option<u32>,
    pub attacker_camp: Option<i32>,
}

/// An elite died; `killer` is the seat with the killing blow (`Death.by`).
#[derive(Clone, Debug, Default, PartialEq)]
pub struct EliteDeath {
    pub elite: u32,
    pub killer: Option<usize>,
}

/// One step of the match.
#[derive(Clone, Debug, Default, PartialEq)]
pub struct EliteStep {
    pub now_ms: f64,
    pub contacts: Vec<Contact>,
    pub seat_deaths: Vec<SeatDeath>,
    pub elite_deaths: Vec<EliteDeath>,
    /// A seat's first pick from the chest of an elite's death: (elite mob id, seat).
    pub chest_picks: Vec<(u32, usize)>,
    /// The chest of this elite's death is gone (expired, or in nobody's observation any more).
    pub chests_gone: Vec<u32>,
}

/// What the attempts of one (kind, weapon) came to.
#[derive(Clone, Debug, Default, PartialEq)]
pub struct EliteTally {
    pub attempts: u64,
    pub killed: u64,
    pub taken_by_other: u64,
    pub died: u64,
    pub died_elsewhere: u64,
    pub left: u64,
    pub open: u64,
    /// Seconds from first contact to the kill, one per `killed`, in kill order.
    pub kill_secs: Vec<f64>,
    pub chest_killer: u64,
    pub chest_other: u64,
    pub chest_none: u64,
    pub chest_open: u64,
    pub chest_bystander_picks: u64,
}

/// The tally of a match, keyed by (elite kind, weapon of the opening hit).
pub fn tally_elites(_steps: &[EliteStep]) -> BTreeMap<(String, String), EliteTally> {
    BTreeMap::new()
}
