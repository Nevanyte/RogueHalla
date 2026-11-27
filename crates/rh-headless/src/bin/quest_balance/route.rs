//! Offline observer of the focal stock policy's goal including its walk to the giver.
use rh_policy::bots::{assign_seats, raid::goal::Target, RaidBot};
use rh_policy::{
    InputKind, InputPreview, InputRefusal, InputRequirements, MatchStart, Policy, PolicyInput,
    PolicySpec,
};
use rh_sim::{Observation, PublicCommand};
use serde::Serialize;
use std::sync::{Arc, Mutex};

pub type Goal = Arc<Mutex<Option<String>>>;

pub struct ObserveGoal {
    bot: RaidBot,
    goal: Goal,
}

impl ObserveGoal {
    pub fn new(seed: u64, players: usize, goal: Goal) -> Self {
        let (tier, persona) = assign_seats(seed, players)[0];
        Self {
            bot: RaidBot::new(tier, persona),
            goal,
        }
    }
}

impl Policy for ObserveGoal {
    fn input_requirements(&self) -> InputRequirements {
        self.bot.input_requirements()
    }
    fn input_kind(&self) -> InputKind {
        self.bot.input_kind()
    }
    fn check_input(&self, input: InputPreview<'_>) -> Result<(), InputRefusal> {
        self.bot.check_input(input)
    }
    fn spec(&self) -> PolicySpec {
        self.bot.spec()
    }
    fn reset(&mut self, start: &MatchStart) {
        self.bot.reset(start);
    }
    fn decide(&mut self, observation: &Observation) -> Vec<PublicCommand> {
        self.bot.decide(observation)
    }
    fn decide_input_into(
        &mut self,
        input: PolicyInput<'_>,
        out: &mut Vec<PublicCommand>,
    ) -> Result<(), InputRefusal> {
        self.bot.decide_input_into(input, out)?;
        let mut goal = self.goal.lock().unwrap();
        if let Target::Quest { def, .. } = &self.bot.goal().0.target {
            if goal.as_deref() != Some(def) {
                *goal = Some(def.clone());
            }
        } else {
            *goal = None;
        }
        Ok(())
    }
}

#[derive(Serialize)]
pub struct Leg {
    pub quest: String,
    pub start_s: f64,
    pub end_s: f64,
    pub standing_s: f64,
    pub normal_clears: u64,
}
