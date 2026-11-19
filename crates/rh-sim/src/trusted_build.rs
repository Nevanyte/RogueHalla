//! Trusted build setup for headless benches (feature `trusted-build-setup`, never in a server build): gives a seated
//! fighter cards, normal-mob stat stacks and chest stat boosts between steps, with the grant rules of legacy
//! `grantCard` (`packages/server/src/sim/chests.ts:189`). Not an input path (C-INPUT): no command reaches it.
//! No RNG draw, no event. Packet P-SIM-BENCH-SEAM-1.
use crate::cards::CardId;
use crate::mob_catalog::MobTypeId;
use crate::reward_chests::BoostId;
use crate::{PlayerId, Sim};

/// What to add to one fighter's build, in request order.
#[derive(Clone, Debug, Default, PartialEq, serde::Serialize, serde::Deserialize)]
#[serde(default, deny_unknown_fields)]
pub struct TrustedBuild {
    /// Card copies to add (a unique card at most once in all).
    pub cards: Vec<(CardId, u8)>,
    /// Normal-mob stat stacks to add (`Player::farm`), by the mob type that pays them.
    pub stacks: Vec<(MobTypeId, u64)>,
    /// Chest stat boost picks to add (`Build::boosts`).
    pub boosts: Vec<(BoostId, u8)>,
}

/// Why a request was refused. A refused request changes nothing.
#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub enum BuildRefusal {
    /// No seated player has this id.
    NoSuchPlayer(PlayerId),
    /// The player is not standing (dead, down or resolved).
    NotStanding(PlayerId),
    /// A unique card already held, or asked for more than once.
    UniqueHeld(CardId),
    /// A conditional card without a base card of its `requires` held before it.
    NeedsBase(CardId),
    /// A card count past 255.
    CardCount(CardId),
    /// A mob type that pays no stat stacks (an elite, a quest giver).
    NotNormalMob(MobTypeId),
    /// A stack total past the farm's maximum count.
    StacksOverMax(MobTypeId),
    /// A boost total past the chest's pick cap.
    BoostOverCap(BoostId),
}

/// Adds `build` to the seated fighter `id`, between steps only: all or nothing, then one re-fold and max hp following.
pub fn apply(sim: &mut Sim, id: PlayerId, build: &TrustedBuild) -> Result<(), BuildRefusal> {
    // Stub (P-SIM-BENCH-SEAM-1): the implementer replaces this body.
    let _ = (sim, id, build);
    Ok(())
}
