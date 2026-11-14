//! P-LAND-FIRE-1: the Fire styler's end-of-attempt guarantees (`style_zones::guarantees(Zone::Fire)`), run by the
//! pipeline on the finished map of a signature attempt with the Fire frame (`pipeline::Refusal::Guarantee`).
//!
//! Owner direction (`docs/coordination/LAND_GENERATION_REQUIREMENTS.md` section 3): "Craters keep clear of all three
//! streams. Every Fire camp keeps its crater; size falls with distance and none is dropped." The styler fits each crater
//! (`craters.rs` `place_craters`: the band's option, then each smaller one, down to `radius_min4`); these two rules are
//! the net, in this order:
//! - `"crater-per-camp"`: every Fire camp of the map has its crater (a `Crater` feature of zone Fire anchored on the
//!   camp's tile), and every Fire crater stands on a Fire camp; none is dropped;
//! - `"crater-clear-of-lava"`: no stream or pool lava (`stream_lava`: the frame's lava outside the cone's box) lies
//!   inside a Fire crater's disc, and no tile a Fire crater owns lies within `lava_clearance()` tiles (Chebyshev) of it.
//!
//! Both read the finished map and the frame only. Pure: no draw, no hashing, nothing written.
use crate::constants::TILE;
use crate::map::TILE_LOW;
use crate::mapdata::{FeatureKind, GameMapData, Zone};
use crate::mapgen::landmarks::signature::frame::LandmarkFrame;
use crate::mapgen::landmarks::signature::DEFAULT_DEFINITION;
use crate::mapgen::style_zones::Guarantee;

/// The Fire styler's guarantees, in the order they run (a promoted literal: no `const` item, which
/// `tests/content.rs` would read as a tunable).
pub fn guarantees() -> &'static [Guarantee] {
    &[
        Guarantee {
            rule: "crater-per-camp",
            check: crater_per_camp,
        },
        Guarantee {
            rule: "crater-clear-of-lava",
            check: crater_clear_of_lava,
        },
    ]
}

/// The least Chebyshev distance, in tiles, kept between any tile a Fire crater owns and any stream or pool lava tile,
/// less one: no crater tile lies within this many tiles of that lava. Today's number: the stream and pool apron
/// (`craters.rs` `lava_apron`, and `SignatureContext::intact`'s apron rule), the core band `bands[0]` = 2.
pub fn lava_clearance() -> i32 {
    DEFAULT_DEFINITION.bands[0]
}

/// The volcano's stream and pool lava as the frame wrote it: every cell written LOW with the lava terrain
/// (`DEFAULT_DEFINITION.lava`) outside the anchor's footprint (the cone's box, which holds the cone's own lava lake), as
/// tile (x, y) on a map `w` tiles wide. The one definition the styler (`place_craters`) and the guarantees share.
pub(super) fn stream_lava(frame: &LandmarkFrame, w: usize) -> impl Iterator<Item = (i32, i32)> + '_ {
    let fp = frame.anchor.footprint;
    let w = w as u32;
    frame
        .cells
        .iter()
        .filter(|c| c.1 == TILE_LOW && c.2 == DEFAULT_DEFINITION.lava)
        .map(move |c| ((c.0 % w) as i32, (c.0 / w) as i32))
        .filter(move |&(x, y)| {
            !(x >= fp.x && y >= fp.y && x < fp.x + fp.w as i32 && y < fp.y + fp.h as i32)
        })
}

/// Whether a crater of radius `r4` (quarter tiles) centred on tile `(x, y)` holds any of `lava` in its disc: a tile
/// strictly inside the ring's nominal radius, 16 * d^2 < r4^2.
pub(super) fn disc_holds(lava: &[(i32, i32)], x: i32, y: i32, r4: i32) -> bool {
    lava.iter()
        .any(|&(lx, ly)| 16 * ((lx - x) * (lx - x) + (ly - y) * (ly - y)) < r4 * r4)
}

/// Whether feature `k` of `m` is a Fire crater.
fn is_fire_crater(m: &GameMapData, k: usize) -> bool {
    m.features
        .get(k)
        .is_some_and(|f| f.kind == FeatureKind::Crater && f.zone == Zone::Fire)
}

/// `"crater-per-camp"`: every Fire camp's tile (its world position over `TILE`, floored) anchors a Fire crater, and
/// every Fire crater's anchor is a Fire camp's tile.
fn crater_per_camp(m: &GameMapData, _: &LandmarkFrame) -> bool {
    let camps: Vec<(i32, i32)> = m
        .camps
        .iter()
        .filter(|c| c.zone == Zone::Fire)
        .map(|c| ((c.x / TILE).floor() as i32, (c.y / TILE).floor() as i32))
        .collect();
    let craters: Vec<(i32, i32)> = (0..m.features.len())
        .filter(|&k| is_fire_crater(m, k))
        .map(|k| (m.features[k].ax, m.features[k].ay))
        .collect();
    camps.iter().all(|t| craters.contains(t)) && craters.iter().all(|t| camps.contains(t))
}

/// `"crater-clear-of-lava"`: no stream or pool lava in a Fire crater's disc, and no Fire crater tile within
/// `lava_clearance()` (Chebyshev) of it. A map whose frame wrote no stream lava passes.
fn crater_clear_of_lava(m: &GameMapData, frame: &LandmarkFrame) -> bool {
    let (w, h) = (m.w as i32, m.h as i32);
    let lava: Vec<(i32, i32)> = stream_lava(frame, m.w as usize).collect();
    if (0..m.features.len())
        .filter(|&k| is_fire_crater(m, k))
        .any(|k| {
            let f = &m.features[k];
            disc_holds(&lava, f.ax, f.ay, f.p.get(1).copied().unwrap_or(0))
        })
    {
        return false;
    }
    let g = lava_clearance();
    let mut near = vec![false; m.feature_grid.len()];
    for &(x, y) in &lava {
        for ny in (y - g).max(0)..=(y + g).min(h - 1) {
            for nx in (x - g).max(0)..=(x + g).min(w - 1) {
                near[(ny * w + nx) as usize] = true;
            }
        }
    }
    !(0..m.feature_grid.len()).any(|i| {
        near[i]
            && usize::from(m.feature_grid[i])
                .checked_sub(1)
                .is_some_and(|k| is_fire_crater(m, k))
    })
}
