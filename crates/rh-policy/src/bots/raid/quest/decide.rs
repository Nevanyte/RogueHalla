//! Quest decisions (P-RAID-Q3): the reward slot choice, the pick-up of a dropped quest spell and The Bargain. Two Banners and
//! Trade Up are P-RAID-Q3b's. STUB: `Decide` is registered and proposes nothing; the pure rules below return a wrong
//! answer for every case until the implementer replaces them (docs/backlog/packets/P-RAID-Q3.md section 8).
use crate::bots::raid::goal::{Cx, Executor, GoalKind, RaidGoal, Step};
use crate::bots::raid::percept::Me;
use crate::bots::raid::values::Prices;
use rh_protocol::state::{WireQuestDrop, WireQuestSpell};

/// The quest executor of `GoalKind::QuestDecide`.
#[derive(Default)]
pub struct Decide {}

impl Executor for Decide {
    fn kinds(&self) -> &'static [GoalKind] {
        &[GoalKind::QuestDecide]
    }
    fn reset(&mut self) {
        *self = Self::default();
    }
    fn act(&mut self, _goal: &RaidGoal, _cx: &mut Cx<'_>) -> Step {
        Step::failed("quest decisions are not built")
    }
}

/// The quest a spell's wire reward id came from, and the share of that quest's table row the spell stands for (1 for a
/// single reward; one half for each of `lost_brother`'s two). `None` for an id no spell has.
pub fn def_of_reward(_reward: u8) -> Option<(&'static str, f64)> {
    None
}

/// What a held or dropped spell is worth in camp-equivalents: `prices.reward_value(def)` times its share, halved while a
/// reusable spell has no charge left (it is recharging).
pub fn spell_worth(_prices: &Prices, _reward: u8, _charges: u8) -> f64 {
    0.
}

/// The answer to a pending reward slot choice (action 8, target): 0 or 1 drops that slot's spell and equips the new
/// reward (an empty slot is simply filled); 2 leaves the new reward on the ground. `new_def` is the pending quest's def.
pub fn slot_answer(_prices: &Prices, _held: &[WireQuestSpell], _new_def: &str) -> u8 {
    2
}

/// Whether to pick a dropped spell up (action 9): a slot is free (the server offers the pick-up only then) and the spell
/// is worth something.
pub fn pickup_wanted(_prices: &Prices, _held: &[WireQuestSpell], _drop: &WireQuestDrop) -> bool {
    false
}

/// What a stat of The Bargain (index 0..9: damage, move speed, max HP, ability power, armor, shield, attack damage,
/// attack speed, lifesteal) is worth per stack to this build, in the units of `Prices::chest` boosts (2.5 neutral).
pub fn stat_worth(_stat: u8, _me: &Me) -> f64 {
    0.
}

/// The trade to send at the hag, `(action, target)` copied from `offered` (the frame's actions of The Bargain's row: 5
/// with give * 9 + receive, 7 with the stat), or `None` when no offered trade is an improvement.
pub fn bargain_trade(_prices: &Prices, _me: &Me, _offered: &[(u8, u8)]) -> Option<(u8, u8)> {
    None
}
