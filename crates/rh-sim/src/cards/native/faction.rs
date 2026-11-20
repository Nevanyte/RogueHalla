//! P-AUG-NATIVE-1b: the faction batch table: Corpse Hoarder (Plague), Stoked (Fire), Glacier (Frost) and Trophy Hunter
//! (Sentenced), each offered only to a one-faction chest (`Offer::Faction`, plan decision 5), filled by
//! P-AUG-NATIVE-4. Each card's hook handlers, readout rows and pool membership live here. Membership is not read by any
//! pool until native ids are on the wire (P-WIRE19-A).
use super::hooks::NativeEvent;
use super::readout::Source;
use crate::cards::CardId;
use crate::reward_chests::Offer;
use crate::state::World;

/// The faction batch's chest pool membership and offer rule (empty until its batch packet; read by no pool yet).
pub(super) static POOL: &[(CardId, Offer)] = &[];

/// The hook handlers of this table's cards (none until P-AUG-NATIVE-4); see `weapon::handle`.
pub(super) fn handle(w: &mut World, i: usize, card: CardId, event: &NativeEvent) {
    let _ = (w, i, card, event);
}

/// The readout rows of this table's cards (`None` for a card of another table, or one not filled yet).
pub(super) fn readouts(card: CardId) -> Option<&'static [Source]> {
    let _ = card;
    None
}
