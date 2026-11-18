//! The decoded hand corpus: lanes, draw counts, operations and profiles.

use super::*;

// ---------------------------------------------------------------------------------------------------- sealed hand model

#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub(super) enum Lane {
    Decide,
    Motor,
    Lapse,
}

impl Lane {
    pub(super) fn parse(text: &str) -> Option<Self> {
        match text {
            "decide" => Some(Lane::Decide),
            "motor" => Some(Lane::Motor),
            "lapse" => Some(Lane::Lapse),
            _ => None,
        }
    }
}

#[derive(Clone, Copy, Debug, PartialEq, Eq, Default)]
pub(super) struct Counts {
    pub(super) decide: u32,
    pub(super) motor: u32,
    pub(super) lapse: u32,
}

impl Counts {
    pub(super) fn get(&self, lane: Lane) -> u32 {
        match lane {
            Lane::Decide => self.decide,
            Lane::Motor => self.motor,
            Lane::Lapse => self.lapse,
        }
    }

    pub(super) fn add(&mut self, lane: Lane, drawn: u32) {
        match lane {
            Lane::Decide => self.decide += drawn,
            Lane::Motor => self.motor += drawn,
            Lane::Lapse => self.lapse += drawn,
        }
    }
}

#[derive(Clone, Copy, Debug)]
pub(super) struct Draw {
    pub(super) lane: Lane,
    pub(super) index: u32,
    pub(super) value: f64,
}

#[derive(Clone, Debug)]
pub(super) struct Seeds {
    pub(super) decide: u32,
    pub(super) motor: u32,
    pub(super) lapse: u32,
    pub(super) knowledge: u32,
}

#[derive(Clone, Debug)]
pub(super) struct HandOperation {
    pub(super) percept: Percept,
    pub(super) intents: Vec<Intent>,
    pub(super) submit_trace: Vec<Draw>,
    pub(super) release_percept: Percept,
    pub(super) result: Vec<TimedCommand>,
    pub(super) release_trace: Vec<Draw>,
    pub(super) misses: Vec<ClickMiss>,
    pub(super) stats: HumanizerStats,
    pub(super) counts: Counts,
    pub(super) memory_keys: Vec<String>,
}

#[derive(Clone, Debug)]
pub(super) struct HandProfile {
    pub(super) name: String,
    pub(super) features: Option<Features>,
    pub(super) tier: TierParams,
    pub(super) seeds: Seeds,
    pub(super) operations: Vec<HandOperation>,
}

pub(super) struct HandCorpus {
    pub(super) map: BotMap,
    pub(super) map_hash_u32: u32,
    pub(super) profiles: Vec<HandProfile>,
}
