//! P-AUG-NATIVE-1b: the zone and generalist batch table: Pathfinder, Hazard Walker, Wish Engine and Overflow
//! (P-AUG-NATIVE-3), then Marked Man (P-AUG-NATIVE-5). Each card's hook handlers, readout rows and pool membership live
//! here. Membership is not read by any pool until native ids are on the wire (P-WIRE19-A).
use crate::cards::CardId;
use crate::reward_chests::Offer;

/// The zone batch's chest pool membership and offer rule (empty until its batch packet; read by no pool yet).
pub(super) static POOL: &[(CardId, Offer)] = &[];
