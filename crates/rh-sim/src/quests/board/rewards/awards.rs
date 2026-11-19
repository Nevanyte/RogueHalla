//! Stable serialized names for the 25 awards.
use super::super::catalog::{QuestId, RewardId};
use serde::{Deserialize, Serialize};

#[derive(Clone, Copy, Debug, PartialEq, Eq, Serialize, Deserialize)]
pub(crate) enum Award {
    WardensCage,
    CrownPower,
    Hook,
    ShoulderCharge,
    Tornado,
    Turret,
    RebirthEgg,
    Warhorn,
    Smite,
    SpellShield,
    TheBargain,
    TradeUp,
    ForgetMeNotSeed,
    SwapStick,
    ChefsKiss,
    LanternWard,
    Cleanse,
    ScoutFalcon,
    Stasis,
    Bolas,
    WallBuster,
    BlindRage,
    StillMind,
    FeedOrHuntDamage,
    WarmCoat,
    BreakersHelm,
}

impl Award {
    pub(crate) const fn is_permanent(self) -> bool {
        matches!(
            self,
            Self::CrownPower
                | Self::TheBargain
                | Self::TradeUp
                | Self::ChefsKiss
                | Self::FeedOrHuntDamage
                | Self::WarmCoat
                | Self::BreakersHelm
        )
    }
    pub(crate) const fn name(self) -> &'static str {
        match self {
            Self::WardensCage => "Warden's Cage",
            Self::CrownPower => "Crown Power",
            Self::Hook => "Hook",
            Self::ShoulderCharge => "Shoulder Charge",
            Self::Tornado => "Tornado",
            Self::Turret => "Turret",
            Self::RebirthEgg => "Rebirth Egg",
            Self::Warhorn => "Warhorn",
            Self::Smite => "Smite",
            Self::SpellShield => "Spell Shield",
            Self::TheBargain => "The Bargain",
            Self::TradeUp => "Trade Up",
            Self::ForgetMeNotSeed => "Forget-Me-Not Seed",
            Self::SwapStick => "Swap-Stick",
            Self::ChefsKiss => "Chef's Kiss",
            Self::LanternWard => "Lantern Ward",
            Self::Cleanse => "Cleanse",
            Self::ScoutFalcon => "Scout Falcon",
            Self::Stasis => "Stasis",
            Self::Bolas => "Bolas",
            Self::WallBuster => "WallBuster",
            Self::BlindRage => "BlindRage",
            Self::StillMind => "StillMind",
            Self::FeedOrHuntDamage => "Feed or Hunt",
            Self::WarmCoat => "Warm Coat",
            Self::BreakersHelm => "Breaker's Helm",
        }
    }
    pub(super) const fn cooldown_ms(self) -> f64 {
        match self {
            Self::Hook => 45_000.,
            Self::ShoulderCharge => 12_000.,
            Self::Tornado => 14_000.,
            Self::Turret => 30_000.,
            Self::Smite => 20_000.,
            Self::SpellShield => 25_000.,
            Self::LanternWard => 60_000.,
            Self::Cleanse => 30_000.,
            Self::ScoutFalcon => 35_000.,
            Self::Bolas => 18_000.,
            _ => 0.,
        }
    }
}

impl From<RewardId> for Award {
    fn from(v: RewardId) -> Self {
        match v {
            RewardId::WardensCage => Self::WardensCage,
            RewardId::CrownPower => Self::CrownPower,
            RewardId::Hook => Self::Hook,
            RewardId::ShoulderCharge => Self::ShoulderCharge,
            RewardId::Tornado => Self::Tornado,
            RewardId::Turret => Self::Turret,
            RewardId::RebirthEgg => Self::RebirthEgg,
            RewardId::Warhorn => Self::Warhorn,
            RewardId::Smite => Self::Smite,
            RewardId::SpellShield => Self::SpellShield,
            RewardId::TheBargain => Self::TheBargain,
            RewardId::TradeUp => Self::TradeUp,
            RewardId::ForgetMeNotSeed => Self::ForgetMeNotSeed,
            RewardId::SwapStick => Self::SwapStick,
            RewardId::ChefsKiss => Self::ChefsKiss,
            RewardId::LanternWard => Self::LanternWard,
            RewardId::Cleanse => Self::Cleanse,
            RewardId::ScoutFalcon => Self::ScoutFalcon,
            RewardId::Stasis => Self::Stasis,
            RewardId::Bolas => Self::Bolas,
            RewardId::WallBuster => Self::WallBuster,
            RewardId::BlindRageAndStillMind => Self::BlindRage,
            RewardId::FeedOrHuntDamage => Self::FeedOrHuntDamage,
            RewardId::WarmCoat => Self::WarmCoat,
            RewardId::BreakersHelm => Self::BreakersHelm,
        }
    }
}

pub(super) fn award_for_offer(w: &crate::state::World, offer: u32) -> Option<Award> {
    let o = w.quests.as_ref()?.offers.iter().find(|o| o.id == offer)?;
    let id = super::super::quest(o.def as usize)?;
    let reward = id.definition().reward;
    Some(match (id, reward) {
        (QuestId::LostBrother, RewardId::BlindRageAndStillMind) => Award::BlindRage,
        _ => Award::from(reward),
    })
}

pub(super) fn awards_for_offer(w: &crate::state::World, offer: u32) -> Option<Vec<Award>> {
    let first = award_for_offer(w, offer)?;
    Some(bundle(first))
}

pub(super) fn bundle(first: Award) -> Vec<Award> {
    if first == Award::BlindRage {
        vec![Award::BlindRage, Award::StillMind]
    } else {
        vec![first]
    }
}
