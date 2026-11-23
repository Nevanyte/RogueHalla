//! Quest goals (P-RAID-Q1..Q4, -P, -W): the executors that walk, dwell, decide, hunt, fight and search for quests.
pub mod decide;
#[cfg(test)]
mod decide_fix_tests;
#[cfg(test)]
mod decide_tests;
pub mod dwell;
#[cfg(test)]
mod dwell_tests;
pub mod hunt;
pub mod pvp;
pub mod search;
pub mod walker;
#[cfg(test)]
mod walker_tests;

use crate::bots::raid::goal::Cx;
use rh_sim::quests::board::catalog::{RewardKind, DEFINITIONS};

/// The seat holds this many quest spells at most (a third reward waits in `pending`).
pub(crate) const SPELL_SLOTS: usize = 2;

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
