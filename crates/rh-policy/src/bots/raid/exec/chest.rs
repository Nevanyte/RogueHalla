//! Reward chests (P-RAID-0): the Chest goal and the pick that rides along every other goal, through the ported chests
//! module (`bots::loot`: `chest_goal`, `act_chest`, `intents`; source `packages/server/src/bots/brain/modules/chests.ts`).
//!
//! `Loot` speaks the shared Brain's `Goal`; this file is the one adapter between it and `RaidGoal` (a chest by its public
//! key, `Target::Key`). The chest's card choice is `Loot`'s (`choose_option`); P-RAID-E prices elite chests.
use crate::bots::brain::{Goal, Status as LootStatus};
use crate::bots::loot::Loot;
use crate::bots::raid::exec::travel::straight_minutes;
use crate::bots::raid::goal::{Candidate, Cx, Executor, GoalKind, RaidGoal, Status, Step, Target};

/// Seconds a pick takes once at the chest (the panel's birth hold and a click).
const PICK_SECONDS: f64 = 3.;
/// The chance a walk to a chest ends in a pick (it may expire or be taken).
const PICK_SUCCESS: f64 = 0.9;

/// The chests module, owned by this executor.
pub struct Chest {
    loot: Loot,
}

impl Default for Chest {
    fn default() -> Self {
        Self { loot: Loot::new() }
    }
}

/// The Brain goal `Loot` reads for a RaidGoal (only its chest and fight kinds matter to it).
fn loot_goal(goal: &RaidGoal) -> Goal {
    match (&goal.kind, &goal.target) {
        (GoalKind::Chest, Target::Key(chest)) => Goal::Chest {
            chest: chest.clone(),
        },
        (GoalKind::Fight, Target::Actor(target)) => Goal::Fight {
            target: target.clone(),
        },
        _ => Goal::Idle,
    }
}

fn loot_status(status: Status) -> LootStatus {
    match status {
        Status::Running => LootStatus::Running,
        Status::Done => LootStatus::Done,
        Status::Failed => LootStatus::Failed,
    }
}

fn step(out: crate::bots::brain::Output) -> Step {
    Step {
        status: match out.status {
            LootStatus::Running | LootStatus::Blocked => Status::Running,
            LootStatus::Done => Status::Done,
            LootStatus::Failed => Status::Failed,
        },
        intents: out.intents,
        acts: Vec::new(),
        why: out.why,
        committed: false,
    }
}

impl Executor for Chest {
    fn kinds(&self) -> &'static [GoalKind] {
        &[GoalKind::Chest]
    }
    fn reset(&mut self) {
        self.loot.reset();
    }
    fn observe(&mut self, cx: &mut Cx<'_>) {
        let v = cx.p.loot_view(cx.tier, cx.persona, cx.still_since);
        self.loot.observe(&v);
    }
    fn propose(&mut self, cx: &mut Cx<'_>, out: &mut Vec<Candidate>) {
        self.loot
            .settle(&loot_goal(cx.goal), loot_status(cx.status), cx.p.now);
        let p = cx.p;
        let v = p.loot_view(cx.tier, cx.persona, cx.still_since);
        let Some(Goal::Chest { chest }) = self.loot.chest_goal(&v, cx.paths) else {
            return;
        };
        let Some(at) = p.facts.chests.iter().find(|c| c.key == chest).map(|c| c.at) else {
            return;
        };
        out.push(Candidate {
            goal: RaidGoal::new(GoalKind::Chest, Target::Key(chest)),
            value: cx.prices.chest,
            success: PICK_SUCCESS,
            death: 0.,
            loss: cx.prices.death_loss,
            minutes: straight_minutes(cx.p.me.at, at) + PICK_SECONDS / 60.,
        });
    }
    fn act(&mut self, goal: &RaidGoal, cx: &mut Cx<'_>) -> Step {
        let Target::Key(chest) = &goal.target else {
            return Step::failed("chest without a key");
        };
        let p = cx.p;
        let v = p.loot_view(cx.tier, cx.persona, cx.still_since);
        step(self.loot.act_chest(chest, &v, cx.paths))
    }
    fn ride_along(&mut self, goal: &RaidGoal, cx: &mut Cx<'_>) -> Step {
        // The chests module goal takes no other module's intents (brain.ts act); its own pick is in `act`.
        if goal.kind == GoalKind::Chest {
            return Step::none();
        }
        let v = cx.p.loot_view(cx.tier, cx.persona, cx.still_since);
        Step::running(
            self.loot.intents(&loot_goal(goal), &v).into_iter().collect(),
            "chest pick rides along",
        )
    }
}
