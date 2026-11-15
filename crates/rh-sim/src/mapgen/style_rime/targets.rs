//! P-LAND-FROST-1 (owner decision 2026-11-14, "Yes to both"): the Frost lake rule's parts as **targets**. In signature
//! mode the Frost styler builds for them on every map; they are measured on the finished map and reported, and never
//! refuse an attempt (they are not registered guarantees: Frost's list stays `lake-at-mouth`). Definitions in
//! `tests/mapgen_land_frost.rs` (its head comment); the numbers in `lake_rule`.
//!
//! Functions returning literals, no `const` item: `tests/content.rs` reads every upper-case `const` or `static` as a
//! tunable. Read only in signature mode, so nothing here is folded into the content hash.
//!
//! STUB (P-LAND-FROST-1 `stub:` commit): `lake_rule` carries the rule's numbers; the three checks answer `false`. The
//! implementer replaces their bodies with the oracle's definitions (pure reads of the finished map and the Frost frame,
//! no draw).
use crate::mapdata::GameMapData;
use crate::mapgen::landmarks::signature::frame::LandmarkFrame;

/// The Frost lake rule's numbers (the owner's: size, gap, way width; the lane's, reported to the owner with the
/// measurement: the axis offset, the second lake's window, the spacing).
#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub struct LakeRule {
    /// Owner: about 400 tiles, give or take 15%: the mouth lake's ice tiles, inclusive bounds.
    pub size: (usize, usize),
    /// Owner: dry tiles between the mouth and the lake's nearest shore (exactly).
    pub mouth_gap: i32,
    /// Owner: the dry way round the lake to the mouth is at least this many tiles wide.
    pub way_width: i32,
    /// The lane's: the most the mouth lake's centroid may lie off the line from the mouth to the coliseum, in tiles.
    pub axis_offset: i32,
    /// The lane's: the second lake's ice tiles, inclusive bounds (and fewer than the mouth lake's).
    pub second: (usize, usize),
    /// The lane's: the fewest dry tiles between the second lake and the mouth lake; also the gap below which two pieces
    /// of ice count as one lake.
    pub spacing: i32,
}

/// The Frost lake rule's numbers (see `LakeRule`).
pub fn lake_rule() -> LakeRule {
    LakeRule {
        size: (340, 460),
        mouth_gap: 3,
        way_width: 3,
        axis_offset: 4,
        second: (60, 200),
        spacing: 3,
    }
}

/// The mouth-lake target on a finished map: the mouth lake holds `lake_rule().size` tiles; its centroid lies at most
/// `axis_offset` off the line, strictly between the mouth and the coliseum along it, and the line crosses the lake; the
/// lake is exactly `mouth_gap + 1` from the mouth, Chebyshev and in floor steps, and no unowned Frost ice is nearer.
/// STUB: always `false`.
pub fn mouth_lake_holds(map: &GameMapData, frame: &LandmarkFrame) -> bool {
    let _ = (map, frame);
    false
}

/// The dry-way target on a finished map: a 4-connected walk of `way_width` squares of dry floor from beyond the mouth
/// lake's far side along the line to a square holding a mouth tile or a tile 4-adjacent to one. STUB: always `false`.
pub fn dry_way_holds(map: &GameMapData, frame: &LandmarkFrame) -> bool {
    let _ = (map, frame);
    false
}

/// The second-lake target on a finished map: besides the mouth lake exactly one lake (pieces at most `spacing` apart
/// are one), of `lake_rule().second` tiles and fewer than the mouth lake's, more than `spacing` (Chebyshev) from it.
/// STUB: always `false`.
pub fn second_lake_holds(map: &GameMapData, frame: &LandmarkFrame) -> bool {
    let _ = (map, frame);
    false
}
