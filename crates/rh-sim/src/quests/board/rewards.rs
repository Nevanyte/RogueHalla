//! Personal inventory and turn-in runtime for named Top25 awards.
mod awards;
mod casts;
pub(crate) mod effects;
mod inventory;
pub(crate) mod permanent;
#[cfg(test)]
mod tests;

use crate::state::{PlayerId, World};
use crate::systems::Cx;
pub(crate) use awards::Award;
#[allow(unused_imports)]
// The dedicated special lane constructs saved quest inventory entries.
pub(crate) use inventory::Owned;
pub(crate) use inventory::RewardState;
#[allow(unused_imports)]
// Public projection API; available loot is wired by the quest-view packet.
pub(crate) use inventory::{available_loot, owned_spells, LootView, SpellView};

pub(crate) fn visit_rules(word: &mut dyn FnMut(u64)) {
    for quest in super::catalog::QuestId::ALL {
        let award = Award::from(quest.definition().reward);
        word(award as u64);
        word(award.cooldown_ms().to_bits());
        word(u64::from(inventory::Owned::new(award, 0.).max_charges));
    }
    permanent::visit_rules(word);
    effects::visit_rules(word);
}

/// Pay pending awards after attempts have reached their giver. Empty slots are filled at once; a full inventory keeps
/// the choice serialized until action 8 resolves it.
pub(crate) fn system(cx: &mut Cx) {
    inventory::pay_pending(cx);
    casts::update(cx);
}

/// Resolves a full-slot reward choice (action 8) or claims visible ground loot (action 9).
pub(crate) fn interact(cx: &mut Cx, id: PlayerId, offer: u32, action: u8, target: u8) {
    match action {
        8 => inventory::resolve_choice(cx, id, offer, target),
        9 => inventory::pickup(cx, id, offer),
        _ => {}
    }
}

/// Attempts to use the equipped quest reward in slot 1 or 2.
pub(crate) fn cast(
    cx: &mut Cx,
    ix: crate::fighter::FighterIx,
    slot: u8,
    x: f64,
    y: f64,
    id: Option<PlayerId>,
) -> bool {
    casts::cast(cx, ix, slot, x, y, id)
}

pub(crate) fn validate(w: &World) -> Result<(), String> {
    inventory::validate(w)
}

/// Queries used by narrow combat, movement and status hooks. Defaults are neutral when the reward lane is absent.
pub(crate) fn has(w: &World, owner: PlayerId, award: Award) -> bool {
    inventory::has(w, owner, award)
}
#[cfg(test)]
pub(crate) use permanent::armor_bypass_fraction;

/// P-BAL-PROBE-1: why a trusted quest-reward grant (`crate::trusted_build`) is refused. Nothing is written then.
#[cfg(feature = "trusted-build-setup")]
#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub(crate) enum GrantRefusal {
    /// The world has no Top 25 board (no quest deal).
    NoBoard,
    /// A bot seat owns no quest reward.
    BotSeat,
    /// Crown Power, Trade Up and The Bargain take their size from how the quest was played.
    NeedsQuest(super::catalog::RewardId),
    /// A permanent already held, or asked twice in one request.
    Held(super::catalog::RewardId),
    /// Fewer empty spell slots than this spell needs (two for the Blind Rage bundle).
    SlotsFull(super::catalog::RewardId),
}

/// P-BAL-PROBE-1: checks, without writing anything, that [`pay_checked`] can pay every reward of `rewards` to the
/// seated player `owner` in request order. The caller has already checked that `owner` is seated and standing.
#[cfg(feature = "trusted-build-setup")]
pub(crate) fn check_grant(
    w: &World,
    owner: PlayerId,
    rewards: &[super::catalog::RewardId],
) -> Result<(), GrantRefusal> {
    if w
        .quests
        .as_ref()
        .is_none_or(|q| q.board.is_none() || q.rewards.is_none())
    {
        return Err(GrantRefusal::NoBoard);
    }
    if w.player(owner).is_none_or(|p| p.seat.bot) {
        return Err(GrantRefusal::BotSeat);
    }
    let slots = inventory::owner(w, owner).map_or(inventory::Owner::default().slots, |o| o.slots);
    let mut free = slots.iter().filter(|s| s.is_none()).count();
    for (i, &reward) in rewards.iter().enumerate() {
        let award = Award::from(reward);
        if !award.is_permanent() {
            free = free
                .checked_sub(awards::bundle(award).len())
                .ok_or(GrantRefusal::SlotsFull(reward))?;
        } else if matches!(
            award,
            Award::CrownPower | Award::TradeUp | Award::TheBargain
        ) {
            return Err(GrantRefusal::NeedsQuest(reward));
        } else if inventory::has(w, owner, award) || rewards[..i].contains(&reward) {
            return Err(GrantRefusal::Held(reward));
        }
    }
    Ok(())
}

/// P-BAL-PROBE-1: pays `rewards`, which passed [`check_grant`], to `owner` through the turn-in's own code: a spell
/// through `inventory::grant` (an empty slot, full charges, ready now), a permanent through `permanent::apply` (its
/// ordinary effect, then ownership). Between steps only. Emits no event: the owner notices a payment would raise are
/// dropped, so the next step's events are what they would have been.
#[cfg(feature = "trusted-build-setup")]
pub(crate) fn pay_checked(cx: &mut Cx, owner: PlayerId, rewards: &[super::catalog::RewardId]) {
    let events = cx.ev.len();
    for &reward in rewards {
        for award in awards::bundle(Award::from(reward)) {
            let paid = if award.is_permanent() {
                permanent::apply(
                    cx,
                    owner,
                    award,
                    super::attempts::PendingReward::default(),
                )
            } else {
                inventory::grant(cx.w, owner, award)
            };
            assert!(paid, "check_grant passed {reward:?}, yet {award:?} was not paid");
        }
    }
    cx.ev.truncate(events);
}
