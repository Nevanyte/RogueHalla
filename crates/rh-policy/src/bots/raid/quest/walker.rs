//! Quest walker (P-RAID-Q1): accept, walk to objectives, send the offered interaction, carry and turn in.
//! Stub: proposes nothing until P-RAID-Q1 is implemented (docs/backlog/packets/P-RAID-Q1.md).
use crate::bots::raid::goal::{Cx, Executor, GoalKind, RaidGoal, Step};

/// The `GoalKind::QuestWalk` executor.
#[derive(Default)]
pub struct Walker;

impl Executor for Walker {
    fn kinds(&self) -> &'static [GoalKind] {
        &[GoalKind::QuestWalk]
    }
    fn act(&mut self, _goal: &RaidGoal, _cx: &mut Cx<'_>) -> Step {
        Step::failed("quest walker not implemented")
    }
}
