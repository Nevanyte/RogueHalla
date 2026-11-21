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
