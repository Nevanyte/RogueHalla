//! P-LAND-FIRE-1 (STUB): the Fire styler's end-of-attempt guarantees (`style_zones::guarantees(Zone::Fire)`), run by
//! the pipeline on the finished map of a signature attempt with the Fire frame (`pipeline::Refusal::Guarantee`).
//!
//! The packet (brief `docs/backlog/packets/P-LAND-FIRE-1.md`) registers, in this order:
//! - `"crater-per-camp"`: every Fire camp of the map has its crater (a `Crater` feature of zone Fire anchored on the
//!   camp's tile); none is dropped;
//! - `"crater-clear-of-lava"`: no stream or pool lava (the frame's lava outside the cone's box) lies inside a Fire
//!   crater's disc, and no tile a Fire crater owns lies within `lava_clearance()` tiles (Chebyshev) of it.
//!
//! On the stub the list is empty and the styler is today's.
use crate::mapgen::landmarks::signature::DEFAULT_DEFINITION;
use crate::mapgen::style_zones::Guarantee;

/// The Fire styler's guarantees, in the order they run (a promoted literal: no `const` item, which
/// `tests/content.rs` would read as a tunable). STUB: empty.
pub fn guarantees() -> &'static [Guarantee] {
    &[]
}

/// The least Chebyshev distance, in tiles, kept between any tile a Fire crater owns and any stream or pool lava tile,
/// less one: no crater tile lies within this many tiles of that lava. Today's number: the stream and pool apron
/// (`craters.rs` `lava_apron`, and `SignatureContext::intact`'s apron rule), the core band `bands[0]` = 2.
pub fn lava_clearance() -> i32 {
    DEFAULT_DEFINITION.bands[0]
}
