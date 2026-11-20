//! P-AUG-NATIVE-1a: the native augments (`CardId::NATIVE`, ids 80..=93): their hand-written definitions
//! (`table.rs`), the generic capped per-player state (`NativeCards`, the `native` field of `Build`) and the
//! `check_world` rules for it. Live world only (`SimConfig::augments = Rework`): a `Legacy` world holds none.
//!
//! No behaviour yet: no hook reads or writes a slot (P-AUG-NATIVE-1b and the batch packets do) and no pool offers a
//! native card. Since P-WIRE19-A (protocol 19) a reward option carries native ids 80..=93, so a Rework chest may hold one.
//!
//! Serialized form (brief section 1.5): only the held cards, in native order, as
//! `[{"card","count","counter","meters","marks"}]`; `Build` writes it first and only when one is held, so a world holding
//! none writes and hashes exactly as before the field existed (`hash.rs`, fourth World exception).
use super::{CardDef, CardId, CardRow};
use crate::reward_chests::OptionId;
use crate::state::World;
use crate::statics::MatchStatic;

mod table;
use table::{NATIVE_DEFINITIONS, NATIVE_RULES};

// P-AUG-NATIVE-1b: the hook points, the own-only readouts and the three batch tables.
mod faction;
pub(crate) mod hooks;
mod readout;
mod weapon;
mod zone;
pub use readout::{readouts, Readout, ReadoutKind, Readouts, MAX_READOUTS};

#[cfg(test)]
mod exemplars_tests;
#[cfg(test)]
mod hooks_tests;
#[cfg(test)]
mod weapon_batch_tests;

/// P-AUG-NATIVE-1b: every native card's chest pool membership and offer rule, from the three batch tables (weapon,
/// zone, faction). No pool reads it until native ids are on the wire (P-WIRE19-A).
pub fn pool_members() -> impl Iterator<Item = (CardId, crate::reward_chests::Offer)> {
    weapon::POOL
        .iter()
        .chain(zone::POOL)
        .chain(faction::POOL)
        .copied()
}

/// The readout sources of a native card, from the batch table that holds it (`None` when no table declares rows).
fn readout_sources(card: CardId) -> Option<&'static [readout::Source]> {
    weapon::readouts(card)
        .or_else(|| zone::readouts(card))
        .or_else(|| faction::readouts(card))
}

/// The native cards' share of a holder's outgoing damage sum (`modifiers::outgoing_damage`, under the same G1 ceiling as
/// the card rows): Marksman's Tally's counter. 0 for a build holding none (every build in a non-`Rework` world).
pub(crate) fn outgoing_share(build: &super::Build) -> f64 {
    if build.native.is_empty() {
        return 0.;
    }
    weapon::outgoing_share(&build.native)
}

/// The bursts the ledger added to one victim's entries (`landed_burst`): at most one per holder, at most one holder per
/// seat. Fixed size, so the pass allocates nothing; a burst that does not fit is not added (and not spent).
#[derive(Clone, Copy, Debug)]
pub(crate) struct Bursts {
    rows: [(crate::PlayerId, usize, f64); crate::constants::MAX_PLAYERS],
    len: usize,
}

impl Default for Bursts {
    fn default() -> Self {
        Self {
            rows: [(crate::PlayerId(0), 0, 0.); crate::constants::MAX_PLAYERS],
            len: 0,
        }
    }
}

impl Bursts {
    /// Has `by` already a burst on this victim?
    pub(crate) fn has_holder(&self, by: crate::PlayerId) -> bool {
        self.rows[..self.len].iter().any(|r| r.0 == by)
    }
    /// Records `by`'s burst on entry `j`; false when full (the burst is then not added).
    pub(crate) fn push(&mut self, by: crate::PlayerId, j: usize, extra: f64) -> bool {
        if self.len == self.rows.len() {
            return false;
        }
        self.rows[self.len] = (by, j, extra);
        self.len += 1;
        true
    }
    /// The burst added to entry `j` (0 for none).
    pub(crate) fn of(&self, j: usize) -> f64 {
        self.rows[..self.len]
            .iter()
            .find(|r| r.1 == j)
            .map_or(0., |r| r.2)
    }
}

/// What the native cards of `by` add to its next landed weapon hit after the victim's mitigation (Bastion's burst), read
/// by the ledger; the hit that carries it reports it in `NativeEvent::HitCount { burst }`, which spends it. 0 for a
/// non-holder (one guard).
pub(crate) fn landed_burst(k: &MatchStatic, w: &World, by: crate::PlayerId) -> f64 {
    if !hooks::holds_native(k, w, by) {
        return 0.;
    }
    w.player(by).map_or(0., weapon::landed_burst)
}

/// Working meters per native card (stored damage, per-hazard seconds, a shield, a per-cast record).
pub const NATIVE_METERS: usize = 3;

/// A native card's caps: its counter, each meter (0 = unused, the value must be 0) and the bits its marks may hold.
#[derive(Clone, Copy, Debug, PartialEq)]
pub struct NativeRule {
    pub counter_cap: f64,
    pub meter_caps: [f64; NATIVE_METERS],
    pub marks: u32,
}

/// One native card's per-player state. `counter` is its stacks in the card's own units, `meters` its working meters,
/// `marks` a bit set. A slot whose `count` is 0 is all zero (never written; whoever clears a card zeroes the slot).
#[derive(Clone, Copy, Debug, Default, PartialEq)]
pub struct NativeSlot {
    pub count: u8,
    pub counter: f64,
    pub meters: [f64; NATIVE_METERS],
    pub marks: u32,
}

/// Every native card's slot, in `CardId::NATIVE` order (fixed size: no allocation).
#[derive(Clone, Copy, Debug, Default, PartialEq)]
pub struct NativeCards([NativeSlot; 14]);

/// The position of a native id in `CardId::NATIVE` (`None` for the 80 catalog cards).
pub(crate) fn position(id: CardId) -> Option<usize> {
    id.index()
        .checked_sub(CardId::ALL.len())
        .filter(|i| *i < CardId::NATIVE.len())
}

/// Is this one of the native ids?
pub fn is_native(id: CardId) -> bool {
    position(id).is_some()
}

impl NativeCards {
    /// The slot of a native card (`None` for a catalog card).
    pub fn slot(&self, id: CardId) -> Option<&NativeSlot> {
        position(id).map(|i| &self.0[i])
    }
    pub(crate) fn slot_mut(&mut self, id: CardId) -> Option<&mut NativeSlot> {
        position(id).map(|i| &mut self.0[i])
    }
    /// The held native cards (count > 0), in native order.
    pub fn held(&self) -> impl Iterator<Item = (CardId, &NativeSlot)> + '_ {
        CardId::NATIVE
            .iter()
            .zip(&self.0)
            .filter(|(_, s)| s.count > 0)
            .map(|(id, s)| (*id, s))
    }
    /// Holds no native card (the serialized `Build` then has no `native` field).
    pub fn is_empty(&self) -> bool {
        self.0.iter().all(|s| s.count == 0)
    }
}

/// The serialized row of one held native card.
#[derive(serde::Serialize, serde::Deserialize)]
#[serde(deny_unknown_fields)]
struct Row {
    card: CardId,
    count: u8,
    counter: f64,
    meters: [f64; NATIVE_METERS],
    marks: u32,
}

impl serde::Serialize for NativeCards {
    fn serialize<S: serde::Serializer>(&self, s: S) -> Result<S::Ok, S::Error> {
        use serde::ser::SerializeSeq;
        let mut seq = s.serialize_seq(Some(self.held().count()))?;
        for (card, slot) in self.held() {
            seq.serialize_element(&Row {
                card,
                count: slot.count,
                counter: slot.counter,
                meters: slot.meters,
                marks: slot.marks,
            })?;
        }
        seq.end()
    }
}

impl<'de> serde::Deserialize<'de> for NativeCards {
    /// Refuses the second spellings of a state: an empty list (none held is no field), a row with `count` 0 (not held is
    /// not written), a catalog card, and a card repeated or out of native order.
    fn deserialize<D: serde::Deserializer<'de>>(d: D) -> Result<Self, D::Error> {
        use serde::de::Error;
        let rows = <Vec<Row>>::deserialize(d)?;
        if rows.is_empty() {
            return Err(D::Error::custom(
                "native rows: an empty list (no native card held is no field)",
            ));
        }
        let mut out = Self::default();
        let mut last: Option<usize> = None;
        for row in rows {
            let i = position(row.card).ok_or_else(|| {
                D::Error::custom(format!(
                    "native rows: {} is not a native card",
                    row.card.as_str()
                ))
            })?;
            if last.is_some_and(|l| i <= l) {
                return Err(D::Error::custom(
                    "native rows: a card repeated or out of native order",
                ));
            }
            if row.count == 0 {
                return Err(D::Error::custom(
                    "native rows: count 0 (a card not held is not written)",
                ));
            }
            last = Some(i);
            out.0[i] = NativeSlot {
                count: row.count,
                counter: row.counter,
                meters: row.meters,
                marks: row.marks,
            };
        }
        Ok(out)
    }
}

/// The caps of a native card (`None` for the 80 catalog cards).
pub fn rule(id: CardId) -> Option<&'static NativeRule> {
    position(id).map(|i| &NATIVE_RULES[i])
}

/// The definition of a native card (`CardId::definition` answers native ids from here).
pub(crate) fn definition(id: CardId) -> &'static CardDef {
    let i = position(id).expect("a native card id");
    &NATIVE_DEFINITIONS[i]
}

/// The largest Tank Engine (`TriggerAction::Fortify`) cap of a world's card table, every faction selection included:
/// the bound of `card_tank_hp` (legacy `cardHooks.ts:190-197`: `tankHp` only rises to `min(cap, ...)`).
fn tank_hp_cap(tuning: &crate::cards::Tuning) -> f64 {
    let mut cap = 0f64;
    for id in CardId::ALL {
        let def = tuning.def(id);
        for rows in std::iter::once(def.rows).chain(def.selections.iter().map(|s| s.rows)) {
            for row in rows {
                if let CardRow::Trigger(t) = row {
                    if let crate::cards::TriggerAction::Fortify { cap: c, .. } = t.action {
                        cap = cap.max(c);
                    }
                }
            }
        }
    }
    cap
}

/// `check_world` rules for the native state (brief section 1.6), and the `card_tank_hp` bound.
pub(crate) fn check(k: &MatchStatic, w: &World) -> Result<(), String> {
    let rework = k.cards.rule() == crate::AugmentRule::Rework;
    let tank_cap = tank_hp_cap(k.cards);
    for p in &w.players {
        let who = p.id.0;
        if !(0.0..=tank_cap).contains(&p.card_tank_hp) {
            return Err(format!(
                "player {who}: card_tank_hp {} outside 0..={tank_cap}",
                p.card_tank_hp
            ));
        }
        for (i, slot) in p.build.native.0.iter().enumerate() {
            let id = CardId::NATIVE[i];
            if slot.count == 0 {
                if slot.counter != 0. || slot.meters.iter().any(|m| *m != 0.) || slot.marks != 0 {
                    return Err(format!(
                        "player {who}: {} not held but its slot is not zero",
                        id.as_str()
                    ));
                }
                continue;
            }
            if !rework {
                return Err(format!(
                    "player {who}: native card {} outside the Rework card table",
                    id.as_str()
                ));
            }
            if slot.count > 1 {
                return Err(format!(
                    "player {who}: unique native card {} held {} times",
                    id.as_str(),
                    slot.count
                ));
            }
            let r = &NATIVE_RULES[i];
            if !(0.0..=r.counter_cap).contains(&slot.counter)
                || slot
                    .meters
                    .iter()
                    .zip(r.meter_caps)
                    .any(|(m, cap)| !(0.0..=cap).contains(m))
                || slot.marks & !r.marks != 0
            {
                return Err(format!(
                    "player {who}: native card {} state outside its caps",
                    id.as_str()
                ));
            }
        }
    }
    // P-WIRE19-A: native ids are on the wire (`WireRewardId::Card(80..=93)`), so only a Legacy world, whose card table
    // holds no native card, refuses a chest offering one.
    if !rework
        && w.reward_chests.iter().any(|c| {
            c.options
                .iter()
                .flatten()
                .any(|o| matches!(o, OptionId::Card(id) if is_native(*id)))
        })
    {
        return Err("a reward chest offers a native card outside the Rework card table".into());
    }
    Ok(())
}

/// The native ids, names, definitions and caps as content words (`content::visit_progression_content`, after the
/// catalog's `CardId::ALL`): per card its index, name length and bytes, then kind, tier, flags, rows (each row's number
/// count and numbers, `cards::map_numbers`), selections, and its rule's caps and marks.
pub(crate) fn visit_content(word: &mut impl FnMut(u64)) {
    fn rows(word: &mut impl FnMut(u64), rows: &[CardRow]) {
        word(rows.len() as u64);
        for row in rows {
            let mut count = 0u64;
            crate::cards::map_numbers(row, &mut |x| {
                count += 1;
                x
            });
            word(count);
            crate::cards::map_numbers(row, &mut |x| {
                word(x.to_bits());
                x
            });
        }
    }
    word(CardId::NATIVE.len() as u64);
    for (i, id) in CardId::NATIVE.into_iter().enumerate() {
        word(id.index() as u64);
        word(id.as_str().len() as u64);
        for b in id.as_str().bytes() {
            word(u64::from(b));
        }
        let def = &NATIVE_DEFINITIONS[i];
        word(def.kind as u64);
        word(u64::from(def.tier));
        word(u64::from(def.unique));
        word(u64::from(def.ready));
        word(def.base.map_or(0, |b| b as u64 + 1));
        word(def.requires.map_or(0, |b| b as u64 + 1));
        rows(word, def.rows);
        word(def.selections.len() as u64);
        for s in def.selections {
            word(u64::from(s.faction.code()));
            rows(word, s.rows);
        }
        let r = &NATIVE_RULES[i];
        word(r.counter_cap.to_bits());
        word(NATIVE_METERS as u64);
        for cap in r.meter_caps {
            word(cap.to_bits());
        }
        word(u64::from(r.marks));
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn the_tables_are_in_native_order() {
        for (i, id) in CardId::NATIVE.into_iter().enumerate() {
            assert_eq!(position(id), Some(i));
            assert_eq!(NATIVE_DEFINITIONS[i].id, id);
        }
        for id in CardId::ALL {
            assert_eq!(position(id), None);
        }
    }
}
