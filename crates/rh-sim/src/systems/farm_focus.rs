//! Optional, common player/bot route incentive for normal themed camps.
use crate::mob_catalog::MobTypeId;
use crate::modifiers::farm::{Farm, RewardStat};
use crate::stat_fold::{EffectStat, FlatStat};
use crate::state::{RouteOrigin, World};
use crate::{CampBuffRule, FactionId, PlayerId};

/// First-ranked stat shared by both authored build goals for each faction.
/// These are actual authoritative reward kinds, not zone labels.
fn focus_stat(faction: FactionId) -> RewardStat {
    match faction {
        FactionId::Fire => RewardStat::Stat(EffectStat::Damage),
        FactionId::Sentenced | FactionId::Frost | FactionId::Holy => {
            RewardStat::Stat(EffectStat::AttackSpeed)
        }
        FactionId::Plague => RewardStat::Flat(FlatStat::Ap),
    }
}

/// A real normal camp (elite locations cannot start or complete a focus route).
pub(crate) fn normal_camp(w: &World, camp: u32) -> bool {
    let Some(rt) = w.mob_runtime.as_ref() else {
        return false;
    };
    let Some(c) = rt.camps.get(camp as usize) else {
        return false;
    };
    c.elite.is_none()
}

/// A normal camp whose actual kill reward pays the faction's focus stat.
pub(crate) fn eligible_camp(w: &World, camp: u32, faction: FactionId) -> bool {
    if !normal_camp(w, camp) {
        return false;
    }
    let rt = w.mob_runtime.as_ref().expect("normal camp has runtime");
    let c = &rt.camps[camp as usize];
    let paid = rt
        .buffs
        .0
        .get(camp as usize)
        .copied()
        .flatten()
        .unwrap_or(c.mob_type);
    Farm::reward_of(paid).is_some_and(|r| r.stat == focus_stat(faction))
}

/// Any eligible normal clear arms the route. The next focus-stat camp must be a different site.
pub(super) fn record_clear(w: &mut World, id: PlayerId, camp: u32) {
    let Some(i) = w.idx(id) else { return };
    if normal_camp(w, camp) {
        w.players[i].last_route_camp = Some(RouteOrigin(camp));
    } else {
        w.players[i].last_route_camp = None;
    }
}

/// Only normal kills in a different eligible camp earn the focus increment.
pub(super) fn bonus(w: &World, id: PlayerId, camp: u32, paid: MobTypeId) -> u64 {
    let Some(i) = w.idx(id) else { return 0 };
    if w.players[i]
        .last_route_camp
        .is_some_and(|last| last.0 != camp)
        && eligible_camp(w, camp, w.players[i].faction())
        && Farm::reward_of(paid).is_some_and(|r| r.stat == focus_stat(w.players[i].faction()))
    {
        3
    } else {
        0
    }
}

pub(super) fn enabled(rule: CampBuffRule) -> bool {
    rule == CampBuffRule::ThemedFocus
}
