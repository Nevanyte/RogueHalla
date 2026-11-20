//! P-AUG-NATIVE-1b: the own-only native readout producer (C-OBS). A viewer's rows come from the viewer's own native
//! slots only, held cards in native order, each card's rows as its batch table declares them; fixed size, no
//! allocation. Nothing observes them yet: P-WIRE19-B puts them on the wire beside `observe.rs` `out.me`.
use crate::cards::CardId;
use crate::{PlayerId, Sim};

/// The most readout rows one viewer can have (two per native card).
pub const MAX_READOUTS: usize = 28;

/// What a readout row shows.
#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub enum ReadoutKind {
    /// The slot's `counter` (stacks in the card's own units).
    Counter,
    /// One of the slot's `meters`.
    Meter,
    /// The number of the slot's `marks` set.
    Marks,
}

/// One readout row: a card, what it shows, its value and its maximum.
#[derive(Clone, Copy, Debug, PartialEq)]
pub struct Readout {
    pub card: CardId,
    pub kind: ReadoutKind,
    pub value: f64,
    pub max: f64,
}

/// A viewer's readout rows (fixed size).
#[derive(Clone, Copy, Debug, PartialEq)]
pub struct Readouts {
    rows: [Readout; MAX_READOUTS],
    len: usize,
}

impl Default for Readouts {
    fn default() -> Self {
        Self {
            rows: [Readout {
                card: CardId::MarksmansTally,
                kind: ReadoutKind::Counter,
                value: 0.,
                max: 0.,
            }; MAX_READOUTS],
            len: 0,
        }
    }
}

impl Readouts {
    /// The rows, in native order.
    pub fn rows(&self) -> &[Readout] {
        &self.rows[..self.len]
    }
}

/// The readout rows of `viewer`'s own native cards (none for a non-holder, a non-player or a `Legacy` world).
pub fn readouts(sim: &Sim, viewer: PlayerId) -> Readouts {
    // Stub (P-AUG-NATIVE-1b): the implementer reads the viewer's slots through the batch tables' readout sources.
    let _ = (sim, viewer);
    Readouts::default()
}
