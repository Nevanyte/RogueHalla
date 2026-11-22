//! Dwell quests (P-RAID-Q2): hold a place until the quest completes. STUB: registered, proposes nothing; the implementer
//! replaces this file (docs/backlog/packets/P-RAID-Q2.md).
use crate::bots::raid::goal::{Cx, Executor, GoalKind, RaidGoal, Step};

#[derive(Default)]
pub struct Dwell {
    /// Quests not to propose or work on until the time: (def, until ms). The implementer's state grows from here.
    banned: Vec<(String, f64)>,
}

impl Executor for Dwell {
    fn kinds(&self) -> &'static [GoalKind] {
        &[GoalKind::QuestDwell]
    }
    fn reset(&mut self) {
        self.banned.clear();
    }
    fn act(&mut self, _goal: &RaidGoal, _cx: &mut Cx<'_>) -> Step {
        Step::failed("dwell quests are not implemented")
    }
}
