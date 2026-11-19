//! P-AUG-RULES-1: the Rework override rows, hand-written (the catalog, `catalog_data.rs`, is generated and pinned to the
//! legacy capture). `cards::Tuning` reads this table under `SimConfig::augments = Rework`; every row passes
//! `cards::check_override` (only row numbers and `text` may differ from the catalog row).
//!
//! Layout (fixed: the Unity mirror test reads `title` and `text` from it with the `catalog_data.rs` regex,
//! `BuildDrawerEditModeTests.cs:28`): one `CardDef { ... }` per card, every field on its own line in the
//! `catalog_data.rs` order `id`, `kind`, `tier`, `base`, `requires`, `unique`, `ready`, `title`, `text`, `tags`, `rows`,
//! `selections`, in `CardId` order. How to add a row: `docs/agent/AUGMENT_REWORK.md`.
use super::CardDef;

#[allow(dead_code)] // STUB (P-AUG-RULES-1): read by `Tuning::of` once implemented.
pub(crate) static REWORK_OVERRIDES: &[CardDef] = &[];
