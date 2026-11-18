//! P-LAND-FIRE-1: the Fire styler's end-of-attempt guarantees (`style_zones::guarantees(Zone::Fire)`), run by the
//! pipeline on the finished map of a signature attempt with the Fire frame (`pipeline::Refusal::Guarantee`).
//!
//! Owner direction (`docs/coordination/LAND_GENERATION_REQUIREMENTS.md` section 3) as decided 2026-11-14: "Craters keep
//! clear of all three streams", with the gentler rule for the camps: a camp's crater takes its band's option, then each
//! smaller allowed one (`craters.rs` `place_craters`, down to `radius_min4`), and a camp no size fits goes without (a
//! measured gap of about 1 camp in 20). So "every Fire camp has a crater" is NOT a guarantee and no attempt is refused
//! for a missing crater. The two rules, in this order:
//! - `"crater-clear-of-lava"`: no stream or pool lava (`stream_lava`: the frame's lava outside the cone's box) lies
//!   inside a Fire crater's disc, and no tile a Fire crater owns lies within `lava_clearance()` tiles (Chebyshev) of it;
//! - `"crater-on-camp"`: every Fire crater (a `Crater` feature of zone Fire) is anchored on a Fire camp's tile; a camp
//!   without a crater passes.
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
            rule: "crater-clear-of-lava",
            check: crater_clear_of_lava,
        },
        Guarantee {
            rule: "crater-on-camp",
            check: crater_on_camp,
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
pub(super) fn stream_lava(
    frame: &LandmarkFrame,
    w: usize,
) -> impl Iterator<Item = (i32, i32)> + '_ {
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

/// `"crater-on-camp"`: every Fire crater's anchor is the tile (world position over `TILE`, floored) of a Fire camp. A
/// Fire camp with no crater passes (the measured gap, owner decision 2026-11-14).
fn crater_on_camp(m: &GameMapData, _: &LandmarkFrame) -> bool {
    let camps: Vec<(i32, i32)> = m
        .camps
        .iter()
        .filter(|c| c.zone == Zone::Fire)
        .map(|c| ((c.x / TILE).floor() as i32, (c.y / TILE).floor() as i32))
        .collect();
    (0..m.features.len())
        .filter(|&k| is_fire_crater(m, k))
        .all(|k| camps.contains(&(m.features[k].ax, m.features[k].ay)))
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

/// Both rules on small hand-built maps: the boundaries no generated seed is sure to reach. The map is 40 x 40 floor,
/// all Fire; the frame's anchor footprint is the 3 x 3 box at (1, 1) (the cone's box); lava is written by the frame.
#[cfg(test)]
mod tests {
    use super::*;
    use crate::mapdata::{
        CampDef, ColiseumDef, FeatureJob, MapFeature, TilePt, TileRect, WorldPt, ZoneAnchor,
    };

    fn side() -> usize {
        40
    }

    fn map() -> GameMapData {
        let n = side() * side();
        let rect = TileRect {
            x: 0,
            y: 0,
            w: 1,
            h: 1,
        };
        GameMapData {
            tiles: vec![0; n],
            seed: 0,
            w: side() as u32,
            h: side() as u32,
            terrain: vec![0; n],
            props: Vec::new(),
            region_grid: vec![Zone::Fire.code(); n],
            attempt: 0,
            territory_grid: vec![0; n],
            coliseum: ColiseumDef {
                rect,
                centre: WorldPt { x: 0.0, y: 0.0 },
                gates: Vec::new(),
                inner_spawns: Vec::new(),
            },
            regions: Vec::new(),
            camps: Vec::new(),
            spawn_pool: Vec::new(),
            buildings: Vec::new(),
            spawns: Vec::new(),
            crate_spots: Vec::new(),
            ground_spots: Vec::new(),
            features: Vec::new(),
            feature_grid: vec![0; n],
            anchors: Vec::new(),
        }
    }

    /// A camp of `zone` whose world position lies inside tile (x, y).
    fn camp(m: &mut GameMapData, zone: Zone, x: i32, y: i32) {
        let at = |v: i32| f64::from(v) * TILE + TILE / 2.0;
        m.camps.push(CampDef {
            id: format!("camp{}", m.camps.len()),
            zone,
            x: at(x),
            y: at(y),
            r: 0.0,
            spots: Vec::new(),
            elite_spot: -1,
            chest: WorldPt { x: 0.0, y: 0.0 },
            area: 0,
            radius: 0.0,
        });
    }

    /// A feature of `kind` and `zone` anchored on (x, y) with radius `r4`; its index.
    fn feature(
        m: &mut GameMapData,
        kind: FeatureKind,
        zone: Zone,
        at: (i32, i32),
        r4: i32,
    ) -> usize {
        m.features.push(MapFeature {
            kind,
            variant: String::new(),
            zone,
            jobs: vec![FeatureJob::Pen],
            ax: at.0,
            ay: at.1,
            p: vec![0, r4, 0],
            bbox: TileRect {
                x: at.0,
                y: at.1,
                w: 1,
                h: 1,
            },
            tiles: 1,
        });
        m.features.len() - 1
    }

    fn own(m: &mut GameMapData, k: usize, x: i32, y: i32) {
        m.feature_grid[y as usize * side() + x as usize] = u8::try_from(k + 1).unwrap();
    }

    /// A frame with the cone's box at (1, 1), 3 x 3, writing `cells` (tile, class, terrain).
    fn frame(cells: &[((i32, i32), u8, u8)]) -> LandmarkFrame {
        let footprint = TileRect {
            x: 1,
            y: 1,
            w: 3,
            h: 3,
        };
        let mut cells: Vec<(u32, u8, u8)> = cells
            .iter()
            .map(|&((x, y), t, k)| ((y as usize * side() + x as usize) as u32, t, k))
            .collect();
        cells.sort_unstable();
        LandmarkFrame {
            anchor: ZoneAnchor {
                zone: Zone::Fire,
                kind: FeatureKind::Volcano,
                centre: TilePt { x: 2, y: 2 },
                footprint,
                front: 0,
                entrance: Vec::new(),
            },
            landing: None,
            marks: Vec::new(),
            features: Vec::new(),
            cells,
        }
    }

    fn check(rule: &str, m: &GameMapData, f: &LandmarkFrame) -> bool {
        let g = guarantees()
            .iter()
            .find(|g| g.rule == rule)
            .expect("registered");
        (g.check)(m, f)
    }

    fn lava() -> u8 {
        DEFAULT_DEFINITION.lava
    }

    #[test]
    fn the_rules_are_registered_in_order_and_the_clearance_is_the_apron() {
        let names: Vec<_> = guarantees().iter().map(|g| g.rule).collect();
        assert_eq!(names, ["crater-clear-of-lava", "crater-on-camp"]);
        assert_eq!(lava_clearance(), 2);
    }

    #[test]
    fn every_fire_crater_needs_a_camp_but_a_camp_needs_no_crater() {
        let f = frame(&[]);
        let mut m = map();
        assert!(check("crater-on-camp", &m, &f), "no camps, no craters");
        camp(&mut m, Zone::Fire, 10, 12);
        camp(&mut m, Zone::Frost, 30, 30);
        assert!(
            check("crater-on-camp", &m, &f),
            "the Fire camp has no crater: not refused"
        );
        feature(&mut m, FeatureKind::Crater, Zone::Frost, (10, 11), 20);
        feature(&mut m, FeatureKind::Barrow, Zone::Fire, (10, 11), 20);
        assert!(
            check("crater-on-camp", &m, &f),
            "a Frost crater or a Fire barrow is not a Fire crater: ignored"
        );
        let mut stray = m.clone();
        feature(&mut stray, FeatureKind::Crater, Zone::Fire, (10, 11), 20);
        assert!(
            !check("crater-on-camp", &stray, &f),
            "one tile off the camp"
        );
        feature(&mut stray, FeatureKind::Crater, Zone::Fire, (10, 12), 20);
        assert!(
            !check("crater-on-camp", &stray, &f),
            "the stray crater stands on no Fire camp"
        );
        feature(&mut m, FeatureKind::Crater, Zone::Fire, (10, 12), 20);
        assert!(
            check("crater-on-camp", &m, &f),
            "the Fire camp has its crater"
        );
        let mut frost = map();
        camp(&mut frost, Zone::Frost, 10, 12);
        feature(&mut frost, FeatureKind::Crater, Zone::Fire, (10, 12), 20);
        assert!(
            !check("crater-on-camp", &frost, &f),
            "a Frost camp is not a Fire camp"
        );
    }

    #[test]
    fn a_crater_tile_keeps_the_clearance_from_stream_lava() {
        let f = frame(&[((20, 10), TILE_LOW, lava())]);
        let mut m = map();
        let k = feature(&mut m, FeatureKind::Crater, Zone::Fire, (10, 10), 20);
        own(&mut m, k, 17, 10);
        own(&mut m, k, 17, 13);
        assert!(
            check("crater-clear-of-lava", &m, &f),
            "3 tiles (Chebyshev) from the lava"
        );
        let mut near = m.clone();
        own(&mut near, k, 18, 12);
        assert!(
            !check("crater-clear-of-lava", &near, &f),
            "2 tiles (Chebyshev) from the lava"
        );
        // A tile at 2 owned by anything but a Fire crater does not count.
        let mut other = m.clone();
        let p = feature(&mut other, FeatureKind::Crater, Zone::Frost, (30, 30), 20);
        own(&mut other, p, 18, 12);
        assert!(
            check("crater-clear-of-lava", &other, &f),
            "a Frost crater's tile"
        );
    }

    #[test]
    fn a_craters_disc_holds_no_stream_lava() {
        // Lava 10 tiles east of the crater: its disc holds it once 16 * 10^2 < r4^2, from r4 = 41.
        let f = frame(&[((20, 10), TILE_LOW, lava())]);
        let mut m = map();
        let k = feature(&mut m, FeatureKind::Crater, Zone::Fire, (10, 10), 40);
        assert!(
            check("crater-clear-of-lava", &m, &f),
            "the lava on the disc's edge"
        );
        m.features[k].p[1] = 41;
        assert!(
            !check("crater-clear-of-lava", &m, &f),
            "the lava inside the disc"
        );
    }

    #[test]
    fn only_low_lava_outside_the_cones_box_is_stream_lava() {
        let mut m = map();
        feature(&mut m, FeatureKind::Crater, Zone::Fire, (6, 2), 64);
        // Inside the cone's box (the cone's own lake), a lava-terrain wall, a LOW tile of another terrain: none counts.
        let f = frame(&[
            ((2, 2), TILE_LOW, lava()),
            ((8, 2), crate::map::TILE_WALL, lava()),
            ((9, 2), TILE_LOW, lava().wrapping_add(1)),
        ]);
        assert!(check("crater-clear-of-lava", &m, &f));
        assert_eq!(stream_lava(&f, side()).count(), 0);
        let f = frame(&[((4, 2), TILE_LOW, lava())]);
        assert_eq!(stream_lava(&f, side()).collect::<Vec<_>>(), [(4, 2)]);
        assert!(
            !check("crater-clear-of-lava", &m, &f),
            "lava one tile outside the box"
        );
    }
}
