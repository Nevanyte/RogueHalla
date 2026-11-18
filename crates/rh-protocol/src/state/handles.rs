//! Nonzero observer-issued handles: quest, roster, pickup and reward chest.

use super::*;

/// Quest row capability. Separate from pickup, reward, and actor identifiers.
#[derive(Clone, Copy, Debug, Eq, PartialEq, Ord, PartialOrd)]
pub struct QuestHandle(NonZeroU32);
impl QuestHandle {
    pub fn new(value: u32) -> Option<Self> {
        NonZeroU32::new(value).map(Self)
    }
    pub fn get(self) -> u32 {
        self.0.get()
    }
}

/// Quest-only roster identity. It is not a player/actor ID.
#[derive(Clone, Copy, Debug, Eq, PartialEq, Ord, PartialOrd)]
pub struct RosterId(NonZeroU32);
impl RosterId {
    pub fn new(value: u32) -> Option<Self> {
        NonZeroU32::new(value).map(Self)
    }
    pub fn get(self) -> u32 {
        self.0.get()
    }
}

/// An observer-issued pickup reference; never an actor or authoritative container identifier.
#[derive(Clone, Copy, Debug, Eq, PartialEq, Ord, PartialOrd)]
pub struct PickupHandle(NonZeroU32);
impl PickupHandle {
    pub fn new(value: u32) -> Option<Self> {
        NonZeroU32::new(value).map(Self)
    }
    pub fn get(self) -> u32 {
        self.0.get()
    }
}

/// A separately issued reward-choice handle. It cannot be used as a pickup or actor ID.
#[derive(Clone, Copy, Debug, Eq, PartialEq, Ord, PartialOrd)]
pub struct RewardChestHandle(NonZeroU32);
impl RewardChestHandle {
    pub fn new(value: u32) -> Option<Self> {
        NonZeroU32::new(value).map(Self)
    }
    pub fn get(self) -> u32 {
        self.0.get()
    }
}
