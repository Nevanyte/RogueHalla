//! P-AUG-NATIVE-1b: the weapon batch table (workbook alignment "Weapon"): Marksman's Tally and Bastion (this packet),
//! then Calluses, Executioner's Ledger and Eighth Strike (P-AUG-NATIVE-2). Each card's hook handlers, readout rows and
//! pool membership live here. Membership is not read by any pool until native ids are on the wire (P-WIRE19-A).
use crate::cards::CardId;
use crate::reward_chests::Offer;

/// The weapon batch's chest pool membership and offer rule (read by no pool yet; see the module doc).
pub(super) static POOL: &[(CardId, Offer)] = &[];
