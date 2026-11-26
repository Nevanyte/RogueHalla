//! Stable serialized names for the 25 awards.
use super::super::catalog::{QuestId, RewardId};
use super::super::tuning::TOP25;
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
    pub(super) fn cooldown_ms(self) -> f64 {
        match self {
            Self::Hook => TOP25.hook_cooldown_ms,
            Self::ShoulderCharge => TOP25.shoulder_charge_cooldown_ms,
            Self::Tornado => TOP25.tornado_cooldown_ms,
            Self::Turret => TOP25.turret_cooldown_ms,
            Self::Smite => TOP25.smite_cooldown_ms,
            Self::SpellShield => TOP25.spell_shield_cooldown_ms,
            Self::LanternWard => TOP25.lantern_ward_cooldown_ms,
            Self::Cleanse => TOP25.cleanse_cooldown_ms,
            Self::ScoutFalcon => TOP25.scout_falcon_cooldown_ms,
            Self::Bolas => TOP25.bolas_cooldown_ms,
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
