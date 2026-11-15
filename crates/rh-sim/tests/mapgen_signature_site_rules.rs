//! P-MAPGEN-SIG-HOLY-1-fix oracle: the per-landmark site, camp, spawn and fan rules are table data (`spec::Spec`
//! `site_side`, `site_camps`, `spawn`, `fan`), and the generator reads them (the review note of P-MAPGEN-SIG-HOLY-1,
//! 2026-11-14; ADR 0028: the table is the per-landmark contract). A refactor: no generated map moves. That half is the
//! digest guards (`tests/support/signature_pins.rs` and the `rh-mapdress` goldens, named in the packet); this file pins
//! the other half, that the code reads the table, by running the reservation, the stamp and write, and the fans on a
//! doctored entry (`SignatureContext::from_table`, `AttemptContext::with_signature_table`). The default table cannot
//! show that: on it the hard-coded zone branches and the table agree.
//!
//! Every test here is red on the stub (`stub:` commit) on an assertion: the stub's table holds placeholders, and its
//! `reserve`, `write` and `fan` still branch on `Zone::Holy`, `FeatureKind::Hall` and `Zone::Fire`.
use rh_sim::map::TILE_FLOOR;
use rh_sim::mapdata::{FeatureKind, TileRect, Zone};
use rh_sim::mapgen::coliseum::{CX, CY, H, W};
use rh_sim::mapgen::landmarks::recipe_support::DIR16;
use rh_sim::mapgen::landmarks::signature::blueprint::{Glyph, Legality, Orientation, Write};
use rh_sim::mapgen::landmarks::signature::spec::{specs, Fan, SiteCamps, SpawnRule, Spec};
use rh_sim::mapgen::landmarks::signature::{
    reserved_zones, Definition, Placed, ReservedSite, SignatureContext, DEFAULT_DEFINITION,
};
use rh_sim::mapgen::landmarks::{recipes, stage};
use rh_sim::mapgen::{attempt, coliseum, terrain_gate};

type Cell = (i32, i32);

// ---------------------------------------------------------------------------------------------------------------------
// Helpers.
// ---------------------------------------------------------------------------------------------------------------------

/// The default table with `edit` applied.
fn doctored(d: &Definition, edit: impl FnOnce(&mut Vec<Spec>)) -> Vec<Spec> {
    let mut t = specs(d);
    edit(&mut t);
    t
}

/// The entry of `zone`.
fn entry(t: &mut [Spec], zone: Zone) -> &mut Spec {
    let e = t.iter_mut().find(|s| s.zone == zone);
    assert!(e.is_some(), "no {zone:?} entry in specs()");
    e.unwrap()
}

/// The first option-on prefix of each seed in `seeds` (attempts 0-7) with the default context.
fn prefixes(seeds: std::ops::RangeInclusive<i64>) -> Vec<attempt::AttemptFrame> {
    let fixed = coliseum::fixed_layout();
    let context = attempt::AttemptContext::new(&fixed);
    let out: Vec<attempt::AttemptFrame> = seeds
        .clone()
        .filter_map(|seed| {
            (0..8).find_map(|n| {
                attempt::begin_attempt_signature(&context, seed, n, true).expect("prefix input")
            })
        })
        .collect();
    assert!(
        out.len() * 2 >= seeds.count(),
        "only {} default prefixes",
        out.len()
    );
    out
}

/// One reservation by `ctx` on `frame`'s partition and faction order, from fresh signature streams: the sites, and the
/// streams after the reservation's draws.
fn reserve(
    ctx: &SignatureContext<'_>,
    frame: &attempt::AttemptFrame,
) -> Option<(Vec<ReservedSite>, Vec<rh_sim::rng::Mulberry32>)> {
    let s = &frame.state;
    let mut streams = ctx.streams(s.seed, s.attempt, &s.order).expect("streams");
    let sites = ctx.reserve(&s.partition.zone, &s.order, &mut streams)?;
    Some((sites, streams))
}

fn site(sites: &[ReservedSite], zone: Zone) -> &ReservedSite {
    let r = sites.iter().find(|r| r.zone == zone);
    assert!(r.is_some(), "no {zone:?} site");
    r.unwrap()
}

/// A site's camps: `camp`, then `extra`.
fn camps_of(r: &ReservedSite) -> Vec<usize> {
    r.camp.iter().chain(&r.extra).copied().collect()
}

fn cell(i: usize) -> Cell {
    ((i % W) as i32, (i / W) as i32)
}

fn chebyshev(a: Cell, b: Cell) -> i32 {
    (a.0 - b.0).abs().max((a.1 - b.1).abs())
}

/// Inclusive (x0, y0, x1, y1) of `r` grown by `by`.
fn grow(r: TileRect, by: i32) -> (i32, i32, i32, i32) {
    (
        r.x - by,
        r.y - by,
        r.x + r.w as i32 - 1 + by,
        r.y + r.h as i32 - 1 + by,
    )
}

fn inside(r: (i32, i32, i32, i32), c: Cell) -> bool {
    c.0 >= r.0 && c.0 <= r.2 && c.1 >= r.1 && c.1 <= r.3
}

/// The DIR16 index from tile `t` toward the coliseum centre tile (the rule `signature::front` uses).
fn front(t: Cell) -> u8 {
    let (dx, dy) = (i64::from(CX - t.0), i64::from(CY - t.1));
    let mut best = (0usize, i64::MIN);
    for (k, v) in DIR16.iter().enumerate() {
        let dot = dx * i64::from(v[0]) + dy * i64::from(v[1]);
        if dot > best.1 {
            best = (k, dot);
        }
    }
    best.0 as u8
}

/// The dominant axis of DIR16 `f` (x on a tie), as a unit vector.
fn axis(f: u8) -> Cell {
    let v = DIR16[usize::from(f)];
    if v[0].abs() >= v[1].abs() {
        (v[0].signum(), 0)
    } else {
        (0, v[1].signum())
    }
}

/// The fan the packet defines for site `r` and fan `f` (ADR 0028 decision 10's geometry, today's `signature::fan` with
/// the entry's numbers): in front of the inner rect (the site less `bands[0]`), along the front axis read at the site's
/// centre tile, `depth` deep, the inner rect's width plus `side` on each side, clipped to the map.
fn expected_fan(r: &ReservedSite, f: Fan, d: &Definition) -> (usize, usize, usize, usize) {
    let b = d.bands[0];
    let (w, h) = (r.rect.w as i32, r.rect.h as i32);
    let (x0, y0, x1, y1) = (
        r.rect.x + b,
        r.rect.y + b,
        r.rect.x + w - 1 - b,
        r.rect.y + h - 1 - b,
    );
    let (a0, b0, a1, b1) = match axis(front((r.rect.x + w / 2, r.rect.y + h / 2))) {
        (0, 1) => (x0 - f.side, y1 + 1, x1 + f.side, y1 + f.depth),
        (0, -1) => (x0 - f.side, y0 - f.depth, x1 + f.side, y0 - 1),
        (1, 0) => (x1 + 1, y0 - f.side, x1 + f.depth, y1 + f.side),
        _ => (x0 - f.depth, y0 - f.side, x0 - 1, y1 + f.side),
    };
    let clip = |v: i32, n: usize| v.clamp(0, n as i32 - 1) as usize;
    (clip(a0, W), clip(b0, H), clip(a1, W), clip(b1, H))
}

/// The terrain catalog's code of the kind keyed "light" (the shrine's healing light).
fn light() -> u8 {
    let k = terrain_gate::DEFAULT_DEFINITION
        .kinds
        .iter()
        .position(|k| k.key == "light");
    assert!(k.is_some(), "no terrain kind \"light\" in the catalog");
    u8::try_from(k.unwrap()).unwrap()
}

/// One zone's landmark placed alone (`apply_only`) by the context of `table` on the default prefixes of seeds 1-6:
/// per placed map, the stage's `near1` and `boxes` before and after, and the `Placed`. At least 3 maps.
struct Written {
    near_before: Vec<u8>,
    near: Vec<u8>,
    boxes_before: Vec<u8>,
    boxes: Vec<u8>,
    placed: Placed,
}

fn written(zone: Zone, table: Vec<Spec>) -> Vec<Written> {
    let fixed = coliseum::fixed_layout();
    let ctx = SignatureContext::from_table(&fixed, &DEFAULT_DEFINITION, table);
    let mut out = Vec::new();
    for frame in prefixes(1..=6) {
        let Some(mut st) = stage::StageContext::new(&fixed)
            .apply(&frame, &recipes::Recipes::default())
            .expect("stage input")
        else {
            continue;
        };
        let (near_before, boxes_before) = (st.masks.near1.clone(), st.masks.boxes.clone());
        let Some(placed) = ctx
            .apply_only(&frame, &mut st, zone)
            .expect("signature input")
        else {
            continue;
        };
        out.push(Written {
            near_before,
            near: st.masks.near1.clone(),
            boxes_before,
            boxes: st.masks.boxes.clone(),
            placed,
        });
    }
    assert!(
        out.len() >= 3,
        "only {} {zone:?} landmarks placed",
        out.len()
    );
    out
}

/// The landmark's anchor feature box and its written tiles (index, tile, terrain).
fn box_and_cells(w: &Written) -> (TileRect, Vec<(usize, u8, u8)>) {
    let f = &w.placed.frames[0];
    let cells = f
        .cells
        .iter()
        .map(|&(i, t, k)| (i as usize, t, k))
        .collect();
    (f.features[0].1, cells)
}

// ---------------------------------------------------------------------------------------------------------------------
// The table.
// ---------------------------------------------------------------------------------------------------------------------

/// `specs(d)` holds today's rules in the new fields, read from `d` (no number is written in the table): sites of
/// `site_side` (the hall's `hall_site_side`); the church's and the cave's site carries one elite camp at `E`, the hall's
/// its `E` camps without the elite pin, the volcano's and the tree's none; the four close their box and apron to the
/// spawn pool, the hall opens its floor but `shrine_clear` round its light `L`; only the volcano has a fan,
/// (`fan_depth`, `fan_side`). On the default definition and on one with every one of those numbers moved.
#[test]
fn the_table_holds_todays_site_camp_spawn_and_fan_rules() {
    let moved = Definition {
        site_side: 33,
        hall_site_side: 43,
        shrine_clear: 2,
        fan_depth: 9,
        fan_side: 5,
        ..DEFAULT_DEFINITION
    };
    for d in [DEFAULT_DEFINITION, moved] {
        let t = specs(&d);
        let zones: Vec<Zone> = t.iter().map(|s| s.zone).collect();
        assert_eq!(zones, reserved_zones().to_vec(), "table order");
        let elite = |elite| Some(SiteCamps { glyph: b'E', elite });
        let want = [
            (
                Zone::Fire,
                d.site_side,
                None,
                SpawnRule::Box,
                Some(Fan {
                    depth: d.fan_depth,
                    side: d.fan_side,
                }),
            ),
            (
                Zone::Sentenced,
                d.site_side,
                elite(true),
                SpawnRule::Box,
                None,
            ),
            (Zone::Plague, d.site_side, None, SpawnRule::Box, None),
            (Zone::Frost, d.site_side, elite(true), SpawnRule::Box, None),
            (
                Zone::Holy,
                d.hall_site_side,
                elite(false),
                SpawnRule::OpenFloor {
                    clear_of: b'L',
                    by: d.shrine_clear,
                },
                None,
            ),
        ];
        let got: Vec<_> = t
            .iter()
            .map(|s| (s.zone, s.site_side, s.site_camps, s.spawn, s.fan))
            .collect();
        assert_eq!(
            got,
            want.to_vec(),
            "(zone, site_side, site_camps, spawn, fan) with site_side {}",
            d.site_side
        );
    }
}

// ---------------------------------------------------------------------------------------------------------------------
// The reservation reads the entry.
// ---------------------------------------------------------------------------------------------------------------------

/// A site's side is its entry's `site_side`, for any zone: the tree's entry given 33 reserves 33-tile Plague sites and
/// the hall's given 43 reserves 43-tile Holy sites, the other three keep `site_side`. On the default prefixes of seeds
/// 1-12, at least 6 reservations.
#[test]
fn a_sites_side_is_its_entrys_site_side() {
    let fixed = coliseum::fixed_layout();
    let d = DEFAULT_DEFINITION;
    let table = doctored(&d, |t| {
        entry(t, Zone::Plague).site_side = d.site_side - 1;
        entry(t, Zone::Holy).site_side = d.hall_site_side + 2;
    });
    let ctx = SignatureContext::from_table(&fixed, &d, table);
    let mut checked = 0;
    for frame in prefixes(1..=12) {
        let Some((sites, _)) = reserve(&ctx, &frame) else {
            continue;
        };
        let got: Vec<(Zone, u32, u32)> =
            sites.iter().map(|r| (r.zone, r.rect.w, r.rect.h)).collect();
        let side = |s: i32| s as u32;
        let want = vec![
            (Zone::Fire, side(d.site_side), side(d.site_side)),
            (Zone::Sentenced, side(d.site_side), side(d.site_side)),
            (Zone::Plague, side(d.site_side - 1), side(d.site_side - 1)),
            (Zone::Frost, side(d.site_side), side(d.site_side)),
            (
                Zone::Holy,
                side(d.hall_site_side + 2),
                side(d.hall_site_side + 2),
            ),
        ];
        assert_eq!(
            got, want,
            "seed {}: (zone, w, h) of the reserved sites",
            frame.state.seed
        );
        checked += 1;
    }
    assert!(checked >= 6, "only {checked} reservations");
}

/// The elite pin is the entry's: the cave's entry with `elite: false` leaves the Frost elites to the zone's rule
/// (`ReservedSite::elite` false, its camp unchanged) and the hall's with `elite: true` pins its first camp. Nothing else
/// of the reservation moves: the same sites, camps and draws (the streams after it) as the default table's.
#[test]
fn a_sites_elite_pin_is_its_entrys() {
    let fixed = coliseum::fixed_layout();
    let d = DEFAULT_DEFINITION;
    let plain = SignatureContext::new(&fixed);
    let table = doctored(&d, |t| {
        entry(t, Zone::Frost).site_camps = Some(SiteCamps {
            glyph: b'E',
            elite: false,
        });
        entry(t, Zone::Holy).site_camps = Some(SiteCamps {
            glyph: b'E',
            elite: true,
        });
    });
    let ctx = SignatureContext::from_table(&fixed, &d, table);
    let mut checked = 0;
    for frame in prefixes(1..=8) {
        let Some((want, want_streams)) = reserve(&plain, &frame) else {
            continue;
        };
        let (got, streams) =
            reserve(&ctx, &frame).expect("the doctored table reserves where the default does");
        let seed = frame.state.seed;
        assert!(
            site(&got, Zone::Frost).camp.is_some(),
            "seed {seed}: the Frost site keeps its camp"
        );
        assert_eq!(
            (site(&got, Zone::Frost).elite, site(&got, Zone::Holy).elite),
            (false, true),
            "seed {seed}: (Frost, Holy) elite pins"
        );
        let unpinned: Vec<ReservedSite> = got
            .iter()
            .map(|r| ReservedSite {
                elite: want.iter().find(|w| w.zone == r.zone).unwrap().elite,
                ..r.clone()
            })
            .collect();
        assert_eq!(unpinned, want, "seed {seed}: the sites but the pins");
        assert_eq!(streams, want_streams, "seed {seed}: the draws");
        checked += 1;
    }
    assert!(checked >= 4, "only {checked} reservations");
}

/// A centred entry's site camps are the landed tiles of its camp glyph, whoever the entry is: the hall's entry given
/// `*` (the nave's six light marks) carries six camps, exactly the `*` tiles where the drawing lands (grid order: by
/// row, then column), on the same site as with `E`; the volcano's given its outlets `O` carries six, inside its inner
/// rect, its first pinned elite. On the default prefixes of seeds 1-8, at least 4 reservations.
#[test]
fn a_centred_sites_camps_are_its_camp_glyphs_landed_tiles() {
    let fixed = coliseum::fixed_layout();
    let d = DEFAULT_DEFINITION;
    let plain = SignatureContext::new(&fixed);
    let table = doctored(&d, |t| {
        entry(t, Zone::Holy).site_camps = Some(SiteCamps {
            glyph: b'*',
            elite: false,
        });
        entry(t, Zone::Fire).site_camps = Some(SiteCamps {
            glyph: b'O',
            elite: true,
        });
    });
    let ctx = SignatureContext::from_table(&fixed, &d, table);
    // The hall's `E` (back, front) and `*` tiles in grid order (`hall.txt`).
    let e_grid: [Cell; 2] = [(18, 15), (18, 27)];
    let stars: [Cell; 6] = [(16, 10), (20, 10), (16, 21), (20, 21), (16, 33), (20, 33)];
    let mut checked = 0;
    for frame in prefixes(1..=8) {
        let Some((want, _)) = reserve(&plain, &frame) else {
            continue;
        };
        let Some((got, _)) = reserve(&ctx, &frame) else {
            continue;
        };
        let seed = frame.state.seed;
        // The hall: the default `E` camps give the landing (unmirrored; front from the back camp to the front one).
        let (holy, holy_e) = (site(&got, Zone::Holy), site(&want, Zone::Holy));
        assert_eq!(holy.rect, holy_e.rect, "seed {seed}: the Holy site");
        let e = camps_of(holy_e);
        assert_eq!(e.len(), 2, "seed {seed}: the default Holy camps");
        let (back, fwd) = (cell(e[0]), cell(e[1]));
        let span = e_grid[1].1 - e_grid[0].1;
        let u = ((fwd.0 - back.0) / span, (fwd.1 - back.1) / span);
        let o = Orientation {
            front: u,
            mirrored: false,
        };
        assert_eq!(o.turn((0, span)), (fwd.0 - back.0, fwd.1 - back.1));
        let landed: Vec<usize> = stars
            .iter()
            .map(|&g| {
                let t = o.turn((g.0 - e_grid[0].0, g.1 - e_grid[0].1));
                ((back.1 + t.1) as usize) * W + (back.0 + t.0) as usize
            })
            .collect();
        assert_eq!(
            camps_of(holy),
            landed,
            "seed {seed}: the Holy site's camps are the landed `*` tiles"
        );
        assert!(!holy.elite, "seed {seed}: the Holy pin");
        // The volcano: six camps in the inner rect, distinct, the first pinned elite.
        let fire = site(&got, Zone::Fire);
        let fc = camps_of(fire);
        let inner = grow(fire.rect, -d.bands[0]);
        let mut distinct = fc.clone();
        distinct.sort_unstable();
        distinct.dedup();
        assert_eq!(
            (fc.len(), distinct.len(), fire.elite),
            (6, 6, true),
            "seed {seed}: the Fire site's (camps, distinct, elite)"
        );
        assert!(
            fc.iter().all(|&i| inside(inner, cell(i))),
            "seed {seed}: a Fire camp outside the inner rect"
        );
        checked += 1;
    }
    assert!(checked >= 4, "only {checked} reservations");
}

// ---------------------------------------------------------------------------------------------------------------------
// The write reads the entry's spawn rule.
// ---------------------------------------------------------------------------------------------------------------------

/// The hall's entry given `SpawnRule::Box` closes its whole box and the ring round it to the spawn pool (`near1`) and
/// keeps its box (`boxes`), as the other four do: the hall's open floor is the entry's rule, not the hall's kind.
#[test]
fn the_hall_given_the_box_rule_closes_its_whole_box() {
    let d = DEFAULT_DEFINITION;
    let table = doctored(&d, |t| entry(t, Zone::Holy).spawn = SpawnRule::Box);
    for w in written(Zone::Holy, table) {
        let (bbox, _) = box_and_cells(&w);
        let (core, apron) = (grow(bbox, 0), grow(bbox, d.bands[0]));
        let mut open = Vec::new();
        for i in 0..W * H {
            let c = cell(i);
            if inside(apron, c) && w.near[i] == 0 {
                open.push(c);
            }
            if inside(core, c) {
                assert_eq!(w.boxes[i], 1, "boxes at {c:?}");
            }
        }
        assert!(
            open.is_empty(),
            "{} tiles of the hall's box or apron open to the spawn pool, e.g. {:?}",
            open.len(),
            open.first()
        );
    }
}

/// The distance kept from the light is the entry's `by`: the hall's entry given `by: 1` keeps closed the tiles within 1
/// of a light tile and leaves open (as before the write) the hall's unwritten floor 2 or more from every light tile,
/// off its ring.
#[test]
fn the_open_floor_distance_is_the_entrys() {
    let d = DEFAULT_DEFINITION;
    let table = doctored(&d, |t| {
        entry(t, Zone::Holy).spawn = SpawnRule::OpenFloor {
            clear_of: b'L',
            by: 1,
        }
    });
    let lit = light();
    let mut freed = 0;
    for w in written(Zone::Holy, table) {
        let (bbox, cells) = box_and_cells(&w);
        let lights: Vec<Cell> = cells
            .iter()
            .filter(|c| c.1 == TILE_FLOOR && c.2 == lit)
            .map(|c| cell(c.0))
            .collect();
        assert_eq!(lights.len(), 9, "the shrine's 3 x 3 light");
        let near = |c: Cell| lights.iter().map(|&l| chebyshev(l, c)).min().unwrap();
        for i in 0..W * H {
            let c = cell(i);
            if !inside(grow(bbox, 0), c) {
                continue;
            }
            if near(c) <= 1 {
                assert_eq!(
                    w.near[i], 1,
                    "a tile within 1 of the light at {c:?} is open"
                );
            } else if near(c) <= d.shrine_clear
                && w.near_before[i] == 0
                && !cells.iter().any(|x| x.0 == i)
            {
                assert_eq!(
                    w.near[i],
                    0,
                    "a floor tile {} from the light at {c:?} is closed with `by: 1`",
                    near(c)
                );
                freed += 1;
            }
        }
    }
    assert!(freed > 0, "no tile 2 to 3 from the light checked");
}

/// The light is found by its glyph: with the hall's nave marks `*` doctored to write the light's terrain too, the
/// tiles near a `*` but beyond `shrine_clear` of every `L` tile stay open (unwritten floor, off the ring, open before
/// the write). Today's code finds "any written floor with terrain" and closes them.
#[test]
fn the_light_is_found_by_its_glyph_not_by_its_terrain() {
    let d = DEFAULT_DEFINITION;
    let lit = light();
    let table = doctored(&d, |t| {
        let holy = entry(t, Zone::Holy);
        holy.spawn = SpawnRule::OpenFloor {
            clear_of: b'L',
            by: d.shrine_clear,
        };
        for g in &mut holy.legend.0 {
            if g.ch == b'*' {
                *g = Glyph {
                    ch: b'*',
                    write: Write::Floor(lit),
                    feature: Some(FeatureKind::Hall),
                    legality: Legality::Floor,
                    mark: true,
                };
            }
        }
    });
    let mut near_a_star = 0;
    for w in written(Zone::Holy, table) {
        let (bbox, cells) = box_and_cells(&w);
        let stars: Vec<Cell> = w.placed.frames[0].mark(b'*').map(|p| (p.x, p.y)).collect();
        assert_eq!(stars.len(), 6, "the six nave marks");
        let lights: Vec<Cell> = cells
            .iter()
            .filter(|c| c.1 == TILE_FLOOR && c.2 == lit)
            .map(|c| cell(c.0))
            .filter(|c| !stars.contains(c))
            .collect();
        assert_eq!(lights.len(), 9, "the shrine's 3 x 3 light");
        let ring_free = grow(bbox, 0);
        for i in 0..W * H {
            let c = cell(i);
            let far = lights.iter().all(|&l| chebyshev(l, c) > d.shrine_clear);
            if inside(ring_free, c)
                && far
                && w.near_before[i] == 0
                && !cells.iter().any(|x| x.0 == i)
            {
                assert_eq!(w.near[i], 0, "floor at {c:?} beyond the light closed");
                if stars.iter().any(|&s| chebyshev(s, c) <= d.shrine_clear) {
                    near_a_star += 1;
                }
            }
        }
    }
    assert!(near_a_star > 0, "no tile near a `*` checked");
}

/// Any entry may open its floor: the cave's entry given `OpenFloor { clear_of: '=', by: 1 }` keeps its box (`boxes`),
/// closes its written tiles, the ring round its box and every tile within 1 of an ice tile `=`, and leaves the rest of
/// its plain floor open (as before the write). Today's code gives the cave the box rule whatever the entry says.
#[test]
fn a_cave_given_the_open_floor_rule_opens_its_plain_floor() {
    let d = DEFAULT_DEFINITION;
    let fixed = coliseum::fixed_layout();
    let ice = SignatureContext::new(&fixed).terrain_codes().ice;
    let table = doctored(&d, |t| {
        entry(t, Zone::Frost).spawn = SpawnRule::OpenFloor {
            clear_of: b'=',
            by: 1,
        }
    });
    let mut open = 0;
    for w in written(Zone::Frost, table) {
        let (bbox, cells) = box_and_cells(&w);
        let ices: Vec<Cell> = cells
            .iter()
            .filter(|c| c.1 == TILE_FLOOR && c.2 == ice)
            .map(|c| cell(c.0))
            .collect();
        assert!(!ices.is_empty(), "the cave's ice");
        let (core, apron) = (grow(bbox, 0), grow(bbox, d.bands[0]));
        for i in 0..W * H {
            let c = cell(i);
            if !inside(apron, c) {
                continue;
            }
            let near_ice = ices.iter().any(|&l| chebyshev(l, c) <= 1);
            let wrote = cells.iter().any(|x| x.0 == i);
            if !inside(core, c) || wrote || near_ice {
                assert_eq!(w.near[i], 1, "closed tile at {c:?} open");
            } else if w.near_before[i] == 0 {
                assert_eq!(w.near[i], 0, "the cave's plain floor at {c:?} closed");
                open += 1;
            }
            if inside(core, c) {
                assert_eq!(w.boxes[i], 1, "boxes at {c:?}");
            } else {
                assert_eq!(
                    w.boxes[i], w.boxes_before[i],
                    "boxes outside the box at {c:?}"
                );
            }
        }
    }
    assert!(open > 0, "no open cave floor checked");
}

// ---------------------------------------------------------------------------------------------------------------------
// The fans read the entry.
// ---------------------------------------------------------------------------------------------------------------------

/// The Prefix records each entry's fan (`AttemptState::fans`, in `reserved_zones` order) with the entry's numbers, for
/// any zone: the volcano's given (9, 3) and the tree's given (4, 0) both get theirs; no camp stands in a fan, and no
/// building of the legacy landmark stage reaches into one. Seeds 1-8, at least 4 prefixes.
#[test]
fn an_entrys_fan_is_reserved_with_its_numbers() {
    let fixed = coliseum::fixed_layout();
    let d = DEFAULT_DEFINITION;
    let (fire, plague) = (Fan { depth: 9, side: 3 }, Fan { depth: 4, side: 0 });
    let table = doctored(&d, |t| {
        entry(t, Zone::Fire).fan = Some(fire);
        entry(t, Zone::Plague).fan = Some(plague);
    });
    let context = attempt::AttemptContext::new(&fixed).with_signature_table(&d, table);
    let mut checked = 0;
    for seed in 1..=8 {
        let Some(frame) = (0..8).find_map(|n| {
            attempt::begin_attempt_signature(&context, seed, n, true).expect("prefix input")
        }) else {
            continue;
        };
        let s = &frame.state;
        let want = vec![
            expected_fan(site(&s.reserved, Zone::Fire), fire, &d),
            expected_fan(site(&s.reserved, Zone::Plague), plague, &d),
        ];
        assert_eq!(
            s.fans, want,
            "seed {seed}: the recorded fans (Fire, Plague)"
        );
        for &(x0, y0, x1, y1) in &s.fans {
            let fan = (x0 as i32, y0 as i32, x1 as i32, y1 as i32);
            for c in &s.layout.camps {
                assert!(!inside(fan, cell(c.site)), "seed {seed}: a camp in a fan");
            }
        }
        let st = stage::StageContext::new(&fixed)
            .apply(&frame, &recipes::Recipes::default())
            .expect("stage input");
        if let Some(st) = st {
            for b in &st.buildings {
                let r = (b.x, b.y, b.x + b.w - 1, b.y + b.h - 1);
                for &(x0, y0, x1, y1) in &s.fans {
                    let apart =
                        r.2 < x0 as i32 || r.0 > x1 as i32 || r.3 < y0 as i32 || r.1 > y1 as i32;
                    assert!(apart, "seed {seed}: building {b:?} in a fan");
                }
            }
        }
        checked += 1;
    }
    assert!(checked >= 4, "only {checked} prefixes");
}

/// An entry without a fan gets none: with the volcano's fan removed the Prefix records no fan at all.
#[test]
fn no_fan_without_an_entrys_fan() {
    let fixed = coliseum::fixed_layout();
    let d = DEFAULT_DEFINITION;
    let table = doctored(&d, |t| {
        for s in t.iter_mut() {
            s.fan = None;
        }
    });
    let context = attempt::AttemptContext::new(&fixed).with_signature_table(&d, table);
    let mut checked = 0;
    for seed in 1..=4 {
        let Some(frame) = (0..8).find_map(|n| {
            attempt::begin_attempt_signature(&context, seed, n, true).expect("prefix input")
        }) else {
            continue;
        };
        assert_eq!(frame.state.fans, Vec::new(), "seed {seed}: fans");
        checked += 1;
    }
    assert!(checked >= 2, "only {checked} prefixes");
}

/// A site's own camps are not tested against its own fan; another site's are. A Fire site (a real one) given a camp in
/// the middle of its own fan does not fail the round; the same tile as the Sentenced site's camp does.
#[test]
fn a_sites_own_camp_in_its_own_fan_does_not_fail_the_round() {
    let fixed = coliseum::fixed_layout();
    let d = DEFAULT_DEFINITION;
    let table = doctored(&d, |t| {
        entry(t, Zone::Fire).fan = Some(Fan {
            depth: d.fan_depth,
            side: d.fan_side,
        });
    });
    let ctx = SignatureContext::from_table(&fixed, &d, table);
    let mut checked = 0;
    for frame in prefixes(1..=4) {
        let sites = &frame.state.reserved;
        let fire = site(sites, Zone::Fire);
        let fan = ctx.fan(fire);
        assert!(fan.is_some(), "the Fire entry's fan");
        let (x0, y0, x1, y1) = fan.unwrap();
        let mid = ((y0 + y1) / 2) * W + (x0 + x1) / 2;
        let own = ReservedSite {
            camp: Some(mid),
            ..fire.clone()
        };
        let mut sentenced = site(sites, Zone::Sentenced).clone();
        sentenced.camp = Some(mid);
        let fire_bare = ReservedSite {
            camp: None,
            ..fire.clone()
        };
        let seed = frame.state.seed;
        assert!(
            !ctx.fan_holds_a_site_camp(std::slice::from_ref(&own)),
            "seed {seed}: the Fire site's own camp in its own fan fails the round"
        );
        assert!(
            ctx.fan_holds_a_site_camp(&[fire_bare, sentenced.clone()]),
            "seed {seed}: the Sentenced camp in the Fire fan passes"
        );
        assert!(
            ctx.fan_holds_a_site_camp(&[own, sentenced]),
            "seed {seed}: both camps in the Fire fan pass"
        );
        checked += 1;
    }
    assert!(checked >= 2, "only {checked} prefixes");
}
