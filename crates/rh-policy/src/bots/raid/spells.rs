//! Quest spell cast rules (P-RAID-S): when to cast a held quest reward on the Q and E slots.
//! The rule table is data: one row per non-permanent award (`SPELL_RULES`), each with the situation it fires in, where it
//! aims and one phrase saying why. `decide_cast` is pure; `CastGate` keeps the seat from resending a refused cast.
use super::acts::{Act, CastAim};
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

// Hand copies of the sim's Top 25 ranges (`crates/rh-sim/src/quests/board/tuning.rs`; `TOP25` is crate-private, so a test
// below reads the source lines and keeps each copy equal to them).
/// `TOP25.smite_range` (tuning.rs:170).
pub const SMITE_RANGE: f64 = 450.;
/// `TOP25.charge_range` (tuning.rs:186).
pub const CHARGE_RANGE: f64 = 220.;
/// `TOP25.turret_range` (tuning.rs:203): what the turret reaches, not its 400 u deploy range.
pub const TURRET_RANGE: f64 = 340.;

const ELITE_ONLY: &[FoeKind] = &[FoeKind::Elite];

const fn never(reward: u8) -> SpellRule {
    SpellRule {
        reward,
        when: When::Never,
        aim: RuleAim::OwnPosition,
        why: "not ruled yet",
    }
}

/// One row per non-permanent award (19), in priority order. Rewards are wire ids (`Award as u8`).
pub const SPELL_RULES: &[SpellRule] = &[
    SpellRule {
        reward: 16, // Cleanse
        when: When::Impaired,
        aim: RuleAim::OwnPosition,
        why: "frees a rooted or stunned seat",
    },
    SpellRule {
        reward: 8, // Smite
        when: When::Foe {
            kinds: ELITE_ONLY,
            within: SMITE_RANGE,
        },
        aim: RuleAim::FoeActor,
        why: "220 damage to an elite in reach (the sim refuses a player target)",
    },
    SpellRule {
        reward: 5, // Turret
        when: When::Foe {
            kinds: ELITE_ONLY,
            within: TURRET_RANGE,
        },
        aim: RuleAim::OwnPosition,
        why: "a turret shoots the elite that is hurting us (340 u reach)",
    },
    SpellRule {
        reward: 3, // ShoulderCharge
        when: When::Leaving,
        aim: RuleAim::AwayAlongLeave,
        why: "a dash of up to 220 u away from a lost fight",
    },
    never(0),  // WardensCage
    never(2),  // Hook
    never(4),  // Tornado
    never(6),  // RebirthEgg
    never(7),  // Warhorn
    never(9),  // SpellShield
    never(12), // ForgetMeNotSeed
    never(13), // SwapStick
    never(15), // LanternWard
    never(17), // ScoutFalcon
    never(18), // Stasis
    never(19), // Bolas
    never(20), // WallBuster
    never(21), // BlindRage
    never(22), // StillMind
];

fn holds(when: &When, scene: &SpellScene) -> bool {
    match *when {
        When::Never => false,
        When::Impaired => scene.impaired,
        When::Foe { kinds, within } => scene
            .foe
            .as_ref()
            .is_some_and(|f| kinds.contains(&f.kind) && f.distance <= within),
        When::Crowd { at_least, within } => {
            scene
                .hostile_distances
                .iter()
                .filter(|d| **d <= within)
                .count() as u32
                >= at_least
        }
        When::Leaving => scene.leave_away.is_some(),
        When::HpBelow(share) => scene.hp_share < share,
    }
}

fn aim_of(aim: RuleAim, scene: &SpellScene) -> Option<CastAim> {
    match aim {
        RuleAim::OwnPosition => Some(CastAim::OwnPosition),
        RuleAim::FoeActor => scene.foe.as_ref().map(|f| CastAim::Actor(f.key.clone())),
        RuleAim::FoePosition => scene.foe.as_ref().map(|f| CastAim::Point(f.at)),
        RuleAim::AwayAlongLeave => scene.leave_away.map(|v| {
            CastAim::Point(Vec2::new(
                scene.me.x + v.x * CHARGE_RANGE,
                scene.me.y + v.y * CHARGE_RANGE,
            ))
        }),
    }
}

/// The cast to ask for this think: at most one, the first ready rule in table order. A spell is castable exactly when it
/// has a charge (the sim gates on `charges`, not on `ready_at`).
pub fn decide_cast(scene: &SpellScene, held: &[HeldSpell], _now: f64) -> Option<Act> {
    SPELL_RULES.iter().find_map(|rule| {
        let spell = held
            .iter()
            .find(|h| h.reward == rule.reward && h.charges >= 1)?;
        if !holds(&rule.when, scene) {
            return None;
        }
        Some(Act::Cast {
            slot: spell.slot,
            reward: spell.reward,
            aim: aim_of(rule.aim, scene)?,
        })
    })
}

/// The one piece of memory of the spell executor: per slot, the cast it last asked for and what the frame showed then
/// (slot, reward, charges, ready_at, asked at).
#[derive(Clone, Debug, Default)]
pub struct CastGate {
    asked: Vec<(u8, u8, u8, f64, f64)>,
}

impl CastGate {
    /// Let `ask` through unless the same slot was asked and the frame shows neither a spent charge, a started cooldown, a
    /// changed slot, nor `CAST_RETRY_WAIT_MS` gone by. Call it every think, with `None` when nothing is asked, so it sees
    /// the frame.
    pub fn pass(&mut self, ask: Option<Act>, held: &[HeldSpell], now: f64) -> Option<Act> {
        self.asked.retain(|&(slot, reward, charges, _, at)| {
            let Some(h) = held.iter().find(|h| h.slot == slot && h.reward == reward) else {
                return false;
            };
            let took = h.charges < charges || h.ready_at > at;
            !took && now - at < CAST_RETRY_WAIT_MS
        });
        let ask = ask?;
        if let Act::Cast { slot, reward, .. } = &ask {
            if self.asked.iter().any(|a| a.0 == *slot) {
                return None;
            }
            let (charges, ready_at) = held
                .iter()
                .find(|h| h.slot == *slot && h.reward == *reward)
                .map_or((1, 0.), |h| (h.charges, h.ready_at));
            self.asked.push((*slot, *reward, charges, ready_at, now));
        }
        Some(ask)
    }
    /// A new match.
    pub fn reset(&mut self) {
        self.asked.clear();
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    /// The value on the `field: <number>,` line of the sim's tuning table.
    fn sim_number(field: &str) -> f64 {
        const TUNING: &str = include_str!("../../../../rh-sim/src/quests/board/tuning.rs");
        let prefix = format!("    {field}: ");
        TUNING
            .lines()
            .find_map(|l| l.strip_prefix(prefix.as_str()))
            .and_then(|v| v.trim_end_matches(',').parse().ok())
            .unwrap_or_else(|| panic!("tuning.rs has no `{field}: <number>,` line"))
    }

    #[test]
    fn the_hand_copied_ranges_equal_the_sims_numbers() {
        assert_eq!(SMITE_RANGE, sim_number("smite_range"));
        assert_eq!(CHARGE_RANGE, sim_number("charge_range"));
        assert_eq!(TURRET_RANGE, sim_number("turret_range"));
    }
}
