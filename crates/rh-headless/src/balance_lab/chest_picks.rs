//! P-BAL-LAB-ELITE-1-fix: the chest offers and picks of a whole match as a pure function of per-step facts. STUB: the
//! implementer replaces `tally_chests` (rules: docs/backlog/packets/P-BAL-LAB-ELITE-1-fix.md, decisions 1 to 5).
use std::collections::{BTreeMap, BTreeSet};

/// Which kind of chest a row is (as the observer classifies it).
#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub enum ChestKind {
    Elite,
    Trophy,
    Ground,
}

/// One option of a chest as one seat sees it.
#[derive(Clone, Debug, PartialEq)]
pub struct OptionRow {
    /// The option's name (`OptionId::as_str`).
    pub name: String,
    /// `card`, `boost` or `kill`.
    pub kind: String,
    /// The option is taken, by this very seat.
    pub taken_by_seat: bool,
}

/// One chest in one seat's observation in one step. No handle: a handle is reissued when the chest's public facts
/// change; the chest is named by `expires_at` and its position (compared by bits).
#[derive(Clone, Debug, PartialEq)]
pub struct ChestRow {
    pub seat: usize,
    pub x: f64,
    pub y: f64,
    pub expires_at: f64,
    pub can_choose: bool,
    pub kind: ChestKind,
    /// For an elite chest: the seat is the kill's killing-blow seat.
    pub seat_is_killer: bool,
    pub options: Vec<OptionRow>,
}

/// One step: its `Event::ChestTaken` count and every seat's chest rows (empty on the step that ended the match).
#[derive(Clone, Debug, Default, PartialEq)]
pub struct ChestStep {
    pub taken_events: u64,
    pub rows: Vec<ChestRow>,
}

#[derive(Clone, Debug, Default, PartialEq)]
pub struct CardPicks {
    pub offered: u64,
    pub picked: u64,
    pub picked_by_killer: u64,
    pub picked_by_bystander: u64,
}

#[derive(Clone, Debug, Default, PartialEq)]
pub struct ChestTally {
    /// (seat, chest) pairs offered.
    pub chests_offered: u64,
    pub cards: BTreeMap<String, CardPicks>,
    /// Named picks by option kind (`card`, `boost`, `kill`).
    pub picks_by_kind: BTreeMap<String, u64>,
    /// The same picks by chest kind (`elite`, `trophy`, `ground`).
    pub picks_by_chest_kind: BTreeMap<String, u64>,
    /// `ChestTaken` events whose option the rows could not name.
    pub picks_unnamed: u64,
    pub taken_events: u64,
}

/// A chest named by facts that never change over its life (never by handle: the sim reissues those, and a viewer's
/// handles restart at 1 on every `observe` call): seat, `expires_at` and position, by bits.
type ChestKey = (usize, u64, u64, u64);

/// The tally of a match: one offer per (seat, chest, option), one pick per (seat, chest, option) the seat holds, and
/// every `ChestTaken` event that no row could name (a skip, a pick on the last step, an expired chest) as unnamed.
pub fn tally_chests(steps: &[ChestStep]) -> ChestTally {
    let mut t = ChestTally::default();
    let mut chests: BTreeSet<ChestKey> = BTreeSet::new();
    let mut offered: BTreeSet<(ChestKey, usize)> = BTreeSet::new();
    let mut picked: BTreeSet<(ChestKey, usize)> = BTreeSet::new();
    let mut named = 0u64;
    for step in steps {
        t.taken_events += step.taken_events;
        for r in &step.rows {
            let key = (r.seat, r.expires_at.to_bits(), r.x.to_bits(), r.y.to_bits());
            if r.can_choose {
                if chests.insert(key) {
                    t.chests_offered += 1;
                }
                for (i, o) in r.options.iter().enumerate() {
                    if offered.insert((key, i)) {
                        t.cards.entry(o.name.clone()).or_default().offered += 1;
                    }
                }
            }
            for (i, o) in r.options.iter().enumerate() {
                if !o.taken_by_seat || !picked.insert((key, i)) {
                    continue;
                }
                let c = t.cards.entry(o.name.clone()).or_default();
                if offered.insert((key, i)) {
                    c.offered += 1;
                }
                c.picked += 1;
                if r.kind == ChestKind::Elite {
                    if r.seat_is_killer {
                        c.picked_by_killer += 1;
                    } else {
                        c.picked_by_bystander += 1;
                    }
                }
                let chest_kind = match r.kind {
                    ChestKind::Elite => "elite",
                    ChestKind::Trophy => "trophy",
                    ChestKind::Ground => "ground",
                };
                *t.picks_by_chest_kind.entry(chest_kind.into()).or_default() += 1;
                *t.picks_by_kind.entry(o.kind.clone()).or_default() += 1;
                named += 1;
            }
        }
    }
    t.picks_unnamed = t.taken_events.saturating_sub(named);
    t
}
