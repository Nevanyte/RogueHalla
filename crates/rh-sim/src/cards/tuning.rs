//! P-AUG-RULES-1: the card tuning table a world plays (`SimConfig::augments`), derived on `MatchStatic` as
//! `k.cards` like `abilities::definition_for` (`abilities/mod.rs:390-398`). Under `Legacy` it is the generated catalog
//! and the legacy constants; under `Rework` it reads the hand-written override table (`rework_overrides.rs`, empty in
//! this packet) and the rework numbers, so it equals legacy until a later packet fills them.
//!
//! STUB on `packet/P-AUG-RULES-1`: the implementer replaces every body below (the signatures are the oracle's).
use super::{Build, CardDef, CardId};
use crate::AugmentRule;

/// The named card numbers that are not catalog rows (legacy `constants.ts:237-288`; Rust `constants.rs:100-115` and the
/// stat fold's own `outgoing_row_cap` / `hp_race_cap`, `stat_fold/definition.rs:47-52`).
#[derive(Clone, Copy, Debug, PartialEq)]
pub struct CardNumbers {
    /// Plant Your Feet: the armed attack's extra share (legacy `PLANT_FEET_BONUS`).
    pub plant_feet_bonus: f64,
    /// Gambler: the chance an entry doubles (legacy `GAMBLER_CHANCE`).
    pub gambler_up_chance: f64,
    /// Gambler: the chance an entry halves (legacy: the same `GAMBLER_CHANCE`, read as `roll < 2 * GAMBLER_CHANCE`).
    pub gambler_down_chance: f64,
    /// Gambler: the up factor (legacy `GAMBLER_UP`).
    pub gambler_up: f64,
    /// Gambler: the down factor (legacy `GAMBLER_DOWN`).
    pub gambler_down: f64,
    /// Wishful Thinking: the most a window may cut (legacy `WISHFUL_BUDGET_MS`).
    pub wishful_budget_ms: f64,
    /// Wishful Thinking: the window (legacy `WISHFUL_WINDOW_MS`).
    pub wishful_window_ms: f64,
    /// Wishful Thinking: the share of a cast cooldown no cut goes below (legacy `WISHFUL_FLOOR_SHARE`).
    pub wishful_floor_share: f64,
    /// G1: the outgoing row ceiling AND the stat fold's `hit` clamp (legacy `OUTGOING_ROW_CAP`, both places).
    pub outgoing_row_cap: f64,
    /// G4: the Race to 0 ceiling AND the stat fold's `hpRace` clamp (legacy `HP_RACE_CAP`, both places).
    pub hp_race_cap: f64,
    /// Third Time's the Charm: every this many hits in a row (legacy `% 3`, `onHit.ts:39`).
    pub third_time_every: u16,
    /// Third Time's the Charm: the on-hit applications that hit adds (legacy `n++`: one, so effects apply twice).
    pub third_time_extra: f64,
}

/// A world's card table. `MatchStatic::cards` holds the one for its config.
#[derive(Debug)]
pub struct Tuning {
    rule: AugmentRule,
    numbers: CardNumbers,
    overrides: &'static [CardDef],
}

/// Why an override row is refused: it changes more than row numbers and text.
#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub struct ShapeError {
    pub id: CardId,
    pub what: &'static str,
}

// STUB: the implementer replaces these numbers with the legacy constants by name (`LEGACY`) and the Rework table
// (`REWORK`: the legacy constants until P-AUG-CONTENT-1 registers `REWORK_*` rows, and `REWORK_OVERRIDES`). The names
// `LEGACY` and `REWORK` are listed in `tests/content.rs` `NON_GAMEPLAY` (P-AUG-RULES-1 oracle-amend): keep them.
static LEGACY: Tuning = Tuning {
    rule: AugmentRule::Legacy,
    numbers: CardNumbers {
        plant_feet_bonus: f64::NAN,
        gambler_up_chance: f64::NAN,
        gambler_down_chance: f64::NAN,
        gambler_up: f64::NAN,
        gambler_down: f64::NAN,
        wishful_budget_ms: f64::NAN,
        wishful_window_ms: f64::NAN,
        wishful_floor_share: f64::NAN,
        outgoing_row_cap: f64::NAN,
        hp_race_cap: f64::NAN,
        third_time_every: 0,
        third_time_extra: f64::NAN,
    },
    overrides: &[],
};
#[allow(dead_code)] // STUB: returned by `Tuning::of` for `Rework` once implemented.
static REWORK: Tuning = Tuning {
    rule: AugmentRule::Legacy,
    numbers: CardNumbers {
        plant_feet_bonus: f64::NAN,
        gambler_up_chance: f64::NAN,
        gambler_down_chance: f64::NAN,
        gambler_up: f64::NAN,
        gambler_down: f64::NAN,
        wishful_budget_ms: f64::NAN,
        wishful_window_ms: f64::NAN,
        wishful_floor_share: f64::NAN,
        outgoing_row_cap: f64::NAN,
        hp_race_cap: f64::NAN,
        third_time_every: 0,
        third_time_extra: f64::NAN,
    },
    overrides: &[],
};

impl Tuning {
    /// The table a config plays (a static: no allocation, no per-call build).
    pub fn of(_config: &crate::SimConfig) -> &'static Tuning {
        &LEGACY
    }
    pub fn rule(&self) -> AugmentRule {
        self.rule
    }
    pub fn numbers(&self) -> &CardNumbers {
        &self.numbers
    }
    /// The definition this world plays for a card: the override row if the table has one, else the catalog's.
    pub fn def(&self, id: CardId) -> &'static CardDef {
        self.overrides
            .iter()
            .find(|d| d.id == id)
            .unwrap_or_else(|| id.definition())
    }
    /// The interactive augments a chest may offer in this world (legacy `INTERACTIVE_AUGMENT_IDS`).
    pub fn pool(&self) -> &'static [CardId] {
        &[]
    }
    /// `reward_chests::augment_pool` under this table.
    pub fn augment_pool(&self, _pickers: &[Build]) -> Vec<CardId> {
        Vec::new()
    }
    /// `reward_chests::conditional_pool` under this table.
    pub fn conditional_pool(&self, _pickers: &[Build]) -> Vec<CardId> {
        Vec::new()
    }
    /// A table for tests, built through the shape guard ([`check_override`] for every row, and one row per id).
    #[cfg(test)]
    pub(crate) fn for_test(
        rule: AugmentRule,
        numbers: CardNumbers,
        overrides: &'static [CardDef],
    ) -> Result<Tuning, ShapeError> {
        Ok(Tuning {
            rule,
            numbers,
            overrides,
        })
    }
}

/// The override-shape guard: `rework` may differ from `legacy` only in row numbers and its `text`.
pub fn check_override(_legacy: &CardDef, _rework: &CardDef) -> Result<(), ShapeError> {
    Ok(())
}

#[cfg(test)]
#[path = "tuning_tests.rs"]
mod tuning_tests;
