//! Match-long quest rewards and the physical crown's provisional farm bonus.
use super::{inventory, Award};
use crate::content::DamageShares;
use crate::fighter::{FighterIx, Signature};
use crate::mob_catalog::MobTypeId::{self, *};
use crate::modifiers::{self, farm};
use crate::quests::board::attempts::{self, PendingReward};
use crate::quests::board::tuning::TOP25;
use crate::stat_fold::{EffectKind, EffectRow};
use crate::state::{Player, PlayerId, World};
use crate::systems::{ledger::Entry, Cx};
use crate::WeaponId;
use serde::{Deserialize, Serialize};

pub(crate) fn visit_rules(word: &mut dyn FnMut(u64)) {
    for count in TOP25.trade_up_stacks {
        word(count);
    }
    word(SCORCH_STAT as u64);
    for stat in CHEF_STATS {
        word(stat as u64);
    }
    word(TOP25.bargain_stacks);
    word(TOP25.feed_damage_stacks);
    for value in [
        TOP25.chef_bonus_share,
        TOP25.chef_bonus_cap,
        TOP25.chef_move_share,
        TOP25.chef_move_cap,
        TOP25.helm_bypass,
    ] {
        word(value.to_bits());
    }
}

const SCORCH_STAT: MobTypeId = EmberImp;

// Farm attributes with a positive starting value. Roster order breaks equal growth.
const CHEF_STATS: [MobTypeId; 6] = [
    SCORCH_STAT,
    CinderHound,
    MagmaBrute,
    Bailiff,
    ChainWarden,
    PlagueRat,
];

#[derive(Clone, Copy, Debug, PartialEq, Serialize, Deserialize)]
#[serde(deny_unknown_fields)]
pub(super) struct ChefBonus {
    stat: MobTypeId,
    amount: f64,
}

impl ChefBonus {
    pub(super) fn valid(self) -> bool {
        CHEF_STATS.contains(&self.stat) && self.amount.is_finite() && self.amount >= 0.
    }

    fn row(self) -> Option<EffectRow<'static>> {
        let kind = match farm::Farm::reward_of(self.stat)?.stat {
            farm::RewardStat::Stat(stat) => EffectKind::Stat {
                stat,
                v: self.amount,
            },
            farm::RewardStat::Flat(stat) => EffectKind::Flat {
                stat,
                v: self.amount,
            },
        };
        Some(EffectRow::new(kind))
    }
}

pub(crate) fn chef_row(w: &World, owner: PlayerId) -> Option<EffectRow<'static>> {
    inventory::owner(w, owner)?.chef_bonus?.row()
}

fn chef_bonus(p: &Player) -> ChefBonus {
    let sheet = crate::content::faction(p.faction());
    let (damage_rows, mirrors) = p.modifiers.permanent_growth_inputs();
    let growths = [
        damage_rows,
        mirrors.speed_mult - 1.,
        (p.vitals.max_hp - sheet.hp) / sheet.hp,
        mirrors.armor_bonus / sheet.armor,
        mirrors.shield_bonus / sheet.shield,
        mirrors.as_mult - 1.,
    ];
    let (index, growth) = growths
        .into_iter()
        .enumerate()
        .min_by(|(_, a), (_, b)| a.total_cmp(b))
        .expect("at least one stat is eligible");
    let stat = CHEF_STATS[index];
    let unit = match stat {
        Bailiff => sheet.armor,
        ChainWarden => sheet.shield,
        PlagueRat => 1. / p.modifiers.mult_of(crate::stat_fold::MultKey::AsPost),
        // Damage, movement and max HP rows are already in starting-stat units.
        _ => 1.,
    };
    // Snapshot the gain at claim so future farming cannot compound it. Movement
    // uses a smaller gain because each farm stack represents half a percent.
    let (share, cap) = if stat == CinderHound {
        (TOP25.chef_move_share, TOP25.chef_move_cap)
    } else {
        (TOP25.chef_bonus_share, TOP25.chef_bonus_cap)
    };
    ChefBonus {
        stat,
        amount: unit * ((1. + growth).max(0.) * share).min(cap),
    }
}

fn stack_room(p: &Player, stat: MobTypeId) -> u64 {
    farm::DEFINITION.max_count - p.farm.count(stat)
}

fn grant_stacks(cx: &mut Cx, owner: PlayerId, stat: MobTypeId, stacks: u64) {
    let amount = stacks.min(stack_room(cx.w.player(owner).unwrap(), stat));
    let granted = modifiers::grant_mob_stat(cx.k, cx.w, owner, stat, amount);
    if granted > 0 {
        crate::quests::notices::owner(
            cx.ev,
            owner,
            modifiers::farm_text::grant_notice(
                stat,
                granted,
                cx.w.player(owner).unwrap().farm.count(stat),
            ),
        );
    }
}

fn bargain(cx: &mut Cx, owner: PlayerId, pending: PendingReward) -> bool {
    let Some(receive) = pending.receive else {
        return false;
    };
    let Some(stat) = attempts::bargain_stat(receive) else {
        return false;
    };
    let Some(i) = cx.w.idx(owner) else {
        return false;
    };
    if stack_room(&cx.w.players[i], stat) < TOP25.bargain_stacks
        || (pending.weapon_trade && super::effects::locked_gear(cx.w, owner))
    {
        return false;
    }
    let p = &mut cx.w.players[i];
    if pending.weapon_trade {
        let spare = if p.weapon.slot == 0 {
            &mut p.weapon.weapon1
        } else {
            &mut p.weapon.weapon0
        };
        if spare.take().is_none() {
            return false;
        }
    } else {
        let Some(give) = pending.give.filter(|&give| give != receive) else {
            return false;
        };
        if attempts::bargain_stacks(p, give) < TOP25.bargain_cost
            || !attempts::bargain_take(p, give, TOP25.bargain_cost)
        {
            return false;
        }
    }
    grant_stacks(cx, owner, stat, TOP25.bargain_stacks);
    true
}

/// Applies the current validated turn-in immediately before the attempt is finished.
pub(super) fn apply(cx: &mut Cx, owner: PlayerId, award: Award, pending: PendingReward) -> bool {
    if inventory::has(cx.w, owner, award) {
        return false;
    }
    let Some(ix) = cx.w.idx(owner).map(FighterIx::of_slot) else {
        return false;
    };
    match award {
        Award::CrownPower => {
            let Some(stacks) = attempts::crown_power(cx.w, owner) else {
                return false;
            };
            grant_stacks(cx, owner, SCORCH_STAT, u64::from(stacks));
        }
        Award::TradeUp => {
            let Some(stacks) = pending
                .points
                .checked_sub(1)
                .and_then(|stage| TOP25.trade_up_stacks.get(usize::from(stage)))
            else {
                return false;
            };
            grant_stacks(cx, owner, Penitent, *stacks);
        }
        Award::TheBargain => {
            if !bargain(cx, owner, pending) {
                return false;
            }
        }
        Award::ChefsKiss => {
            let bonus = chef_bonus(&cx.w.players[ix.slot()]);
            inventory::owner_mut(cx.w, owner).unwrap().chef_bonus = Some(bonus);
            modifiers::recompute_with(cx.k, cx.w, ix, cx.stats);
            modifiers::sync_max_hp(&mut cx.w.players[ix.slot()]);
        }
        Award::FeedOrHuntDamage => grant_stacks(cx, owner, SCORCH_STAT, TOP25.feed_damage_stacks),
        Award::WarmCoat => clear_chill(&mut cx.w.core_mut(ix).signature),
        Award::BreakersHelm => {} // The damage ledger reads permanent ownership on each weapon hit.
        _ => return false,
    }
    inventory::grant(cx.w, owner, award)
}

pub(crate) fn crown_damage(w: &World, owner: PlayerId) -> f64 {
    let Some(stacks) = attempts::crown_power(w, owner) else {
        return 0.;
    };
    let Some(player) = w.player(owner) else {
        return 0.;
    };
    let stacks = u64::from(stacks).min(stack_room(player, SCORCH_STAT));
    stacks as f64 * farm::Farm::reward_of(SCORCH_STAT).unwrap().value
}

pub(crate) fn clear_chill(sig: &mut Signature) {
    sig.chill = 1.;
    sig.chillers.clear();
    sig.frost_move_mult = 1.;
    sig.exposure_started_at = None;
    sig.last_near_at = None;
    sig.ranged_chill_until = None;
    sig.bits &= !crate::constants::SIG_CHILLED;
}

pub(crate) fn damage_shares(w: &World, hit: &Entry) -> DamageShares {
    let mut shares = DamageShares {
        phys: hit.packet.phys,
        magic: hit.packet.magic,
        true_: hit.packet.true_,
    };
    if hit.sig.is_none() && hit.ability.is_none() && !hit.dot && !hit.passive && !hit.derived {
        if let (Some(id), Some(weapon)) = (hit.by, hit.weapon) {
            let fraction = armor_bypass_fraction(w, id, weapon);
            if fraction > 0. {
                let bypass = shares.phys * fraction;
                shares.phys -= bypass;
                shares.true_ += bypass;
            }
        }
    }
    shares
}

pub(crate) fn armor_bypass_fraction(w: &World, owner: PlayerId, weapon: WeaponId) -> f64 {
    if matches!(weapon, WeaponId::Axe | WeaponId::Bow)
        && inventory::has(w, owner, Award::BreakersHelm)
    {
        TOP25.helm_bypass
    } else {
        0.
    }
}

#[cfg(test)]
mod tests;
