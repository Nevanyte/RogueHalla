//! P-AUG-NATIVE-1a: the native augments (`CardId::NATIVE`, ids 80..=93): their hand-written definitions, the generic
//! capped per-player state (`NativeCards` in `Build`) and the `check_world` rules for it. Live world only
//! (`SimConfig::augments = Rework`). STUB: signatures only; the implementer replaces this file (packet brief
//! `docs/backlog/packets/P-AUG-NATIVE-1a.md`).
use super::{CardDef, CardId};

/// Working meters per native card (stored damage, per-hazard seconds, a shield, a per-cast record).
pub const NATIVE_METERS: usize = 3;

/// A native card's caps: its counter, each meter (0 = unused, the value must be 0) and the bits its marks may hold.
#[derive(Clone, Copy, Debug, PartialEq)]
pub struct NativeRule {
    pub counter_cap: f64,
    pub meter_caps: [f64; NATIVE_METERS],
    pub marks: u32,
}

/// The caps of a native card (`None` for the 80 catalog cards).
pub fn rule(_id: CardId) -> Option<&'static NativeRule> {
    None
}

/// The definition of a native card (STUB: a placeholder row; the native table answers it).
pub(crate) fn definition(_id: CardId) -> &'static CardDef {
    CardId::Ad1.definition()
}
