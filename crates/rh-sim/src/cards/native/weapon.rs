//! P-AUG-NATIVE-1b: the weapon batch table (workbook alignment "Weapon"): Marksman's Tally and Bastion (this packet),
//! then Calluses, Executioner's Ledger and Eighth Strike (P-AUG-NATIVE-2). Each card's hook handlers, readout rows and
//! pool membership live here. Membership is not read by any pool until native ids are on the wire (P-WIRE19-A).
use super::hooks::NativeEvent;
use super::readout::Source;
use super::NativeCards;
use crate::cards::CardId;
use crate::constants::{REWORK_BASTION_SHARE, REWORK_MARKSMANS_TALLY_PER};
use crate::reward_chests::Offer;
use crate::state::{Player, World};

/// The weapon batch's chest pool membership and offer rule (read by no pool yet; see the module doc).
pub(super) static POOL: &[(CardId, Offer)] = &[
    (CardId::MarksmansTally, Offer::Any),
    (CardId::Bastion, Offer::Any),
];

/// The hook handlers of this table's cards: player slot `i` holds `card` (the dispatcher checked it); a card of another
/// table, or a hook the card does not use, is ignored.
pub(super) fn handle(w: &mut World, i: usize, card: CardId, event: &NativeEvent) {
    match (card, *event) {
        // Marksman's Tally (workbook Augment_Tuning row 11, Augments row 37): +0.3% damage share per long-range hit,
        // capped at 30% (`min(cap, counter + per)`). The share joins `outgoing_damage` (see `outgoing_share`).
        (CardId::MarksmansTally, NativeEvent::LongRangeHit) => {
            let cap = super::rule(card).map_or(0., |r| r.counter_cap);
            if let Some(s) = w.players[i].build.native.slot_mut(card) {
                s.counter = cap.min(s.counter + REWORK_MARKSMANS_TALLY_PER);
            }
        }
        // Bastion (Augment_Tuning row 27, Augments row 53): a block fills `meters[0]` by what it kept, measured as Tank
        // Engine measures it (`cards::on_blocked`, legacy `cardHooks.ts:192`), up to `REWORK_BASTION_METER_CAP`.
        (CardId::Bastion, NativeEvent::Block { prevented }) => {
            let p = &w.players[i];
            let faction = crate::content::faction(p.faction());
            let mirrors = p.modifiers.mirrors();
            let shares = crate::content::DamageShares {
                phys: prevented.phys,
                magic: prevented.magic,
                true_: prevented.true_,
            };
            let armor = (faction.armor + mirrors.armor_bonus).max(0.);
            let shield = faction.shield + mirrors.shield_bonus;
            let kept = crate::content::mitigate_shares(prevented.raw, &shares, armor, shield);
            let cap = super::rule(card).map_or(0., |r| r.meter_caps[0]);
            if let Some(s) = w.players[i].build.native.slot_mut(card) {
                s.meters[0] = cap.min(s.meters[0] + kept);
            }
        }
        // Bastion: the hit that carried the burst (`landed_burst`, added by the ledger) empties the meter.
        (CardId::Bastion, NativeEvent::HitCount { burst, .. }) if burst > 0. => {
            if let Some(s) = w.players[i].build.native.slot_mut(card) {
                s.meters[0] = 0.;
            }
        }
        _ => {}
    }
}

/// The readout rows of this table's cards (`None` for a card of another table).
pub(super) fn readouts(card: CardId) -> Option<&'static [Source]> {
    match card {
        CardId::MarksmansTally => Some(&[Source::Counter]),
        CardId::Bastion => Some(&[Source::Meter(0)]),
        _ => None,
    }
}

/// This table's share of the holder's outgoing damage sum (`modifiers::outgoing_damage`): Marksman's Tally's counter.
pub(super) fn outgoing_share(native: &NativeCards) -> f64 {
    native
        .slot(CardId::MarksmansTally)
        .filter(|s| s.count > 0)
        .map_or(0., |s| s.counter)
}

/// The extra damage this table's cards add to the holder's next landed weapon hit, after mitigation: Bastion's
/// `REWORK_BASTION_SHARE` of its stored blocks.
pub(super) fn landed_burst(p: &Player) -> f64 {
    p.build
        .native
        .slot(CardId::Bastion)
        .filter(|s| s.count > 0 && s.meters[0] > 0.)
        .map_or(0., |s| REWORK_BASTION_SHARE * s.meters[0])
}
