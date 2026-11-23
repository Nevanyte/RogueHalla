//! P-BAL-LAB-ELITE-1-fix: the chest offers and picks of a whole match as a pure function of per-step facts. STUB: the
//! implementer replaces `tally_chests` (rules: docs/backlog/packets/P-BAL-LAB-ELITE-1-fix.md, decisions 1 to 5).
use std::collections::BTreeMap;

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

/// The tally of a match.
pub fn tally_chests(steps: &[ChestStep]) -> ChestTally {
    let _ = steps;
    ChestTally::default()
}
