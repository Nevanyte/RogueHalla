//! The RaidBot policy seat (P-RAID-0). STUB: it accepts delivered frames and proposes nothing; the implementer replaces
//! this file with the seat that checks its input as HumanBot does, answers the planning screen through `PlanModule`,
//! builds the percept, runs the arbiter and the executors, and emits through the Humanizer and the act queue.
use crate::bots::types::{PersonaId, TierId};
use crate::{
    InputKind, InputPreview, InputRefusal, MatchStart, Policy, PolicyInput, PolicySpec,
    POLICY_CONTRACT_VERSION,
};
use rh_sim::{Observation, PlayerId, PublicCommand, OBSERVATION_VERSION};

/// The balance lab's test player on one seat.
pub struct RaidBot {
    tier: TierId,
    persona: PersonaId,
    me: Option<PlayerId>,
}

impl RaidBot {
    pub fn new(tier: TierId, persona: PersonaId) -> Self {
        Self {
            tier,
            persona,
            me: None,
        }
    }
    pub fn tier(&self) -> TierId {
        self.tier
    }
    pub fn persona(&self) -> PersonaId {
        self.persona
    }
}

impl Policy for RaidBot {
    fn input_requirements(&self) -> crate::InputRequirements {
        crate::InputRequirements {
            inventory: true,
            owner_combat: true,
        }
    }
    fn input_kind(&self) -> InputKind {
        InputKind::Delivered14
    }
    fn spec(&self) -> PolicySpec {
        PolicySpec {
            name: format!("raid-{}-{}", self.tier.as_str(), self.persona.as_str()),
            version: "0".into(),
            contract: POLICY_CONTRACT_VERSION,
            observation_version: OBSERVATION_VERSION,
            decision_interval_ticks: 1,
        }
    }
    fn reset(&mut self, m: &MatchStart) {
        self.me = Some(m.me);
    }
    fn check_input(&self, input: InputPreview<'_>) -> Result<(), InputRefusal> {
        match input {
            InputPreview::Delivered14(_) => Ok(()),
            _ => Err(InputRefusal::RequiresDelivered14),
        }
    }
    fn decide_input_into(
        &mut self,
        input: PolicyInput<'_>,
        _out: &mut Vec<PublicCommand>,
    ) -> Result<(), InputRefusal> {
        self.check_input(input.preview())
    }
    fn decide(&mut self, _: &Observation) -> Vec<PublicCommand> {
        Vec::new()
    }
    fn decide_into(&mut self, _: &Observation, _: &mut Vec<PublicCommand>) {}
}
