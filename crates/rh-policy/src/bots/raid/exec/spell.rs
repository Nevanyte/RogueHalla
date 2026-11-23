//! The spell executor (P-RAID-S): no goal kinds; it rides along any goal and asks for the cast the rule table names.
//! STUB: the implementer replaces the body.
use crate::bots::raid::goal::{Cx, Executor, GoalKind, RaidGoal, Step};

/// Casts held quest spells by `spells::SPELL_RULES`.
#[derive(Default)]
pub struct Spell {}

impl Executor for Spell {
    fn kinds(&self) -> &'static [GoalKind] {
        &[]
    }
    fn act(&mut self, _goal: &RaidGoal, _cx: &mut Cx<'_>) -> Step {
        Step::none()
    }
}
