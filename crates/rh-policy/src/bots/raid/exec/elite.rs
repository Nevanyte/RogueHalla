//! Elite executor (P-RAID-E): elite pricing, elite fight and the reward chest pick. Stub: it proposes nothing and
//! picks nothing yet; the pure pricing functions below are what `tests/raid_elite.rs` pins. Every number it prices
//! with is a field of `Prices::elite` (`values.rs`), none is written here.
use crate::bots::raid::goal::{Cx, Executor, GoalKind, RaidGoal, Step};
use crate::bots::raid::values::ElitePrices;
use rh_sim::mob_catalog::MobTypeId;
use rh_sim::WeaponId;

/// The Elite goal's executor: its memory between thinks (which camps' elites it saw alive or dead, the camps it gave
/// up on, the fight's start).
#[derive(Default)]
pub struct Elite {}

impl Executor for Elite {
    fn kinds(&self) -> &'static [GoalKind] {
        &[GoalKind::Elite]
    }
    fn reset(&mut self) {
        *self = Self::default();
    }
    fn act(&mut self, _goal: &RaidGoal, _cx: &mut Cx<'_>) -> Step {
        Step::failed("elite executor not built")
    }
}

/// The chance a fight with the elite of `kind` kills the bot, 0.02..=0.98: the bench table row of `kind`
/// (`ElitePrices::kind_death`) read at `stacks / stacks_per_camp` banked camps (linear between `camp_columns`, held at
/// the ends), times the weapon factor of the weapon in hand (1 when it has no row), divided by `held` (the hp share held
/// plus the potions held, as `Farm` counts it). A kind with no row falls back to the farm's death model.
pub fn death_prior(
    _elite: &ElitePrices,
    _kind: MobTypeId,
    _weapon: Option<WeaponId>,
    _stacks: f64,
    _stacks_per_camp: f64,
    _held: f64,
) -> f64 {
    0.5
}

/// The index of the offered card the price table ranks highest: the greatest `Prices::augment_value` among the options
/// not taken (`taken_by[i]` empty) and not held (`held(card)`), ties to the lowest index. `None` when the table prices
/// every pickable card at 0 (`farm_only`), when `prices.elite.on` is false, or when no card is pickable: the chest
/// module's own pick (and its skip) then stands.
pub fn best_card(
    _prices: &crate::bots::raid::values::Prices,
    _options: &[String],
    _taken_by: &[String],
    _held: &dyn Fn(&str) -> bool,
) -> Option<usize> {
    None
}
