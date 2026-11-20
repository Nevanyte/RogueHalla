//! P-AUG-NATIVE-1b: the faction batch table: Corpse Hoarder (Plague), Stoked (Fire), Glacier (Frost) and Trophy Hunter
//! (Sentenced), each offered only to a one-faction chest (`Offer::Faction`, plan decision 5), filled by
//! P-AUG-NATIVE-4. Each card's hook handlers, readout rows and pool membership live here. Membership is not read by any
//! pool until native ids are on the wire (P-WIRE19-A).
use crate::cards::CardId;
use crate::reward_chests::Offer;

/// The faction batch's chest pool membership and offer rule (empty until its batch packet; read by no pool yet).
pub(super) static POOL: &[(CardId, Offer)] = &[];
