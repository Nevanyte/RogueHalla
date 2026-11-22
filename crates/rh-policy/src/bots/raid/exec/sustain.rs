//! Sustain (P-RAID-C): potions, heal-up and disengage on low hp.
use crate::bots::raid::goal::{Cx, Executor, GoalKind, RaidGoal, Step};

/// The Sustain goal's executor. Stub: proposes nothing and is never urgent (P-RAID-C fills it in).
#[derive(Default)]
pub struct Sustain;

impl Executor for Sustain {
    fn kinds(&self) -> &'static [GoalKind] {
        &[GoalKind::Sustain]
    }
    fn act(&mut self, _goal: &RaidGoal, _cx: &mut Cx<'_>) -> Step {
        Step::failed("sustain is not implemented")
    }
}
