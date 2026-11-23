//! Quest goals (P-RAID-Q1..Q4, -P, -W): the executors that walk, dwell, decide, hunt, fight and search for quests.
#[cfg(test)]
mod death_tests;
pub mod decide;
#[cfg(test)]
mod decide_b_tests;
#[cfg(test)]
mod decide_fix2_tests;
#[cfg(test)]
mod decide_fix_tests;
#[cfg(test)]
mod decide_tests;
pub mod dwell;
#[cfg(test)]
mod dwell_tests;
pub mod hunt;
#[cfg(test)]
mod public_rules_tests;
pub mod pvp;
pub mod search;
pub mod walker;
#[cfg(test)]
mod walker_tests;

use crate::bots::planning::{matchup_share, zone_faction};
use crate::bots::raid::exec::farm::{death_chance, members_left};
use crate::bots::raid::goal::Cx;
use crate::bots::raid::percept::CAMP_JOIN_RADIUS;
use rh_sim::quests::board::catalog::{RewardKind, DEFINITIONS};
use rh_sim::Vec2;

/// The seat holds this many quest spells at most (a third reward waits in `pending`).
pub(crate) const SPELL_SLOTS: usize = 2;

/// Walk to within this of a point to accept, claim, deliver, pick up or stand (u). A bot rule over the published contact
/// range (`rh_sim::quests::public_rules().contact_range`, 64 u today): stand at half of it, well inside the server's reach.
pub(super) fn stand_u() -> f64 {
    rh_sim::quests::public_rules().contact_range / 2.
}

/// Spell slots spoken for: quest spells held, plus every accepted, unfinished quest on the board whose reward is a spell,
/// whichever executor owns it (the same count as the walker's private `slots_taken`). A later packet points the walker at
/// this function and deletes the walker's private copy.
pub(crate) fn spell_slots_taken(cx: &Cx<'_>) -> usize {
    cx.p.quests.as_ref().map_or(0, |q| {
        q.spells.len()
            + q.rows
                .iter()
                .filter(|r| r.owner_accepted && r.state == 1)
                .filter(|r| {
                    DEFINITIONS
                        .iter()
                        .find(|d| d.id == r.def)
                        .is_some_and(|d| d.reward.kind() != RewardKind::Permanent)
                })
                .count()
    })
}

/// The chance that the walk and stand of quest `def` through `points` ends in the seat's death. One helper for every quest
/// executor (P-RAID-Q3b-fix).
///
/// * What the quest does by design is `Prices::quest_death_share(def)` (a measured row, else `death_floor`), scaled up for a
///   hurt seat by 1 / hp share (as `death_chance` does), at most 0.98 and never below the row.
/// * A camp that is idle near a point adds nothing: the sim wakes a mob only on damage, a blocked hit or a camp mate's death,
///   and no quest step does any of them. A camp that is already awake counts (a living mob of it is `engaged` in the frame
///   and its camp label is the camp) with the per-camp `death_chance` of a fight, for every such camp whose centre lies
///   within `CAMP_JOIN_RADIUS` of any point. The two combine as `1 - (1 - quest)(1 - camp)...`.
pub(crate) fn walk_death(cx: &Cx<'_>, def: &str, points: &[Vec2]) -> f64 {
    let hp = cx.p.me.hp_share();
    let row = cx.prices.quest_death_share(def);
    let mut survive = 1. - (row / hp.max(0.05)).clamp(row, 0.98);
    if let Some(me) = cx.p.me.faction {
        for camp in &cx.p.camps {
            let near = points
                .iter()
                .any(|p| rh_sim::math::hypot(p.x - camp.at.x, p.y - camp.at.y) <= CAMP_JOIN_RADIUS);
            let awake =
                cx.p.mobs
                    .iter()
                    .any(|m| m.alive && m.engaged && m.camp == Some(camp.index));
            if camp.alive == 0 || !near || !awake {
                continue;
            }
            let share = matchup_share(me, zone_faction(camp.zone), false);
            survive *= 1. - death_chance(hp, share, members_left(cx.p, camp), &cx.prices.death);
        }
    }
    (1. - survive).max(row)
}
