//! Oracle of P-MAPGEN-SIG-HOLY-1: in signature mode the Holy hall IS the owner-approved blueprint
//! (`docs/landmarks/blueprints/hall.txt`; owner decisions 2026-11-12, 2026-11-13 and 2026-11-14, ADR 0028 rule G9),
//! placed in the Landmarks phase as the fifth signature landmark: one drawing, 37 x 37, filling the inner rect of a
//! reserved Holy site of 41 a side, in one of eight orientations with its front toward the coliseum; two Holy camps on
//! its `E` tiles; the zone's elites chosen by legacy's rule among all ten Holy camps; spawn points allowed on its open
//! floor but kept off its walls and its light and 3 tiles clear of the shrine's light (owner decision 2026-11-14, 2);
//! the certifier's "every hall wall piece is a 3 x 3 pier" rule not applied to the shrine's walls (owner decision
//! 2026-11-14, 3) and still applied to every pier. Guarantees are hard (no tolerance); the attempt bounds are the
//! stack's (measured on the prototype, with headroom), not the owner's. No legacy counterpart (DEV-020). Seeds 1-32,
//! option on (no decor), `certified_styled`, generated once per test binary; the hard rules also on seeds 33-128.
//! Brief: `docs/backlog/packets/P-MAPGEN-SIG-HOLY-1.md`.
//!
//! Definitions (tile units; x to the right, y down, the map's tile index `y * w + x`):
//! * the drawing: the lines of `hall.txt` that do not start with `;` and are not empty; grid tile (gx, gy) is character
//!   gx of line gy. `#` a wall (a pier or the shrine's wall); `L` the shrine's healing light (floor, terrain `light`, the
//!   terrain catalog's kind keyed "light"); `.`, `*`, `D`, `E` plain floor. Its front is the bottom, +gy. It is mirror
//!   symmetric (every row reads the same reversed).
//! * the nave's front run: grid (15..=21, 36), the seven open tiles of the front row through the `E` column (18).
//! * the Holy site: the Holy `ReservedSite` of the accepted attempt's prefix, 41 x 41; its inner rect: the site shrunk by
//!   the core band (`DEFAULT_DEFINITION.bands[0]` = 2) on every side, 37 x 37: the box. The centre tile: box corner +
//!   (18, 18), the drawing's centre tile (18, 18) under every orientation.
//! * an orientation: a front direction u (one of (1, 0), (-1, 0), (0, 1), (0, -1)) and a mirror flag; r = (u.1, -u.0)
//!   unmirrored, -r mirrored. The drawing turned is g -> gx * r + gy * u; its box, translated so that its corner nearest
//!   the map origin is the box's corner, is the box.
//! * the hall: the one `Hall` feature of the map (index k); it equals the drawing under an orientation when every grid
//!   tile's map tile is: for `#` a wall owned by the hall with terrain 0; for `L` floor with the light terrain owned by
//!   the hall; for every other glyph floor not owned by the hall and not of the light terrain; and the hall owns no
//!   other tile. Because the drawing is mirror symmetric, both mirror states of one front land the same tiles: every
//!   orientation the hall equals must land the same tiles, and the one the generator used is the Holy frame's landing.
//! * a wall group: an 8-connected piece of the wall tiles the hall owns (the certifier's `feature-whole` reading). A
//!   pier: a wall group of exactly 9 tiles whose box is 3 x 3. The shrine: the one wall group that is not a pier. Its
//!   door: the tiles of its box's border that are not its walls.
//! * front toward the coliseum: f = the DIR16 index of the largest dot product with (CX - t.x, CY - t.y), first on a tie;
//!   u is the dominant axis of `DIR16[f]` (x on a tie).
//! * a spawn point's tile: (floor(x / R.tile), floor(y / R.tile)), for every point of `spawn_pool` and of `spawns`.
//!   Shrine-clear: the Chebyshev distance from every light tile is more than 3.
//! * a camp's tile: (floor(x / R.tile), floor(y / R.tile)); an elite camp: `elite_spot >= 0`.
use rh_sim::map::{TILE_FLOOR, TILE_WALL};
use rh_sim::mapdata::{FeatureJob, FeatureKind, GameMapData, TileRect, Zone};
use rh_sim::mapgen::certify::rules::R;
use rh_sim::mapgen::coliseum::{CX, CY};
use rh_sim::mapgen::landmarks::recipe_support::DIR16;
use rh_sim::mapgen::landmarks::signature::blueprint::{
    EntranceRule, Glyph, Legality, Placement, Write,
};
use rh_sim::mapgen::landmarks::signature::spec::{specs, FeatureAnchor, FeatureSpec};
use rh_sim::mapgen::landmarks::signature::{
    relabel_sites, Placed, ReservedSite, SignatureContext, DEFAULT_DEFINITION,
};
use rh_sim::mapgen::landmarks::{recipes, stage};
use rh_sim::mapgen::zones::camps::{self, CampSite};
use rh_sim::mapgen::{attempt, certify, coliseum, map_hash, pipeline, terrain_gate};
use rh_sim::rng::Mulberry32;
use std::collections::{BTreeMap, BTreeSet};
use std::sync::OnceLock;

#[path = "support/signature_pins.rs"]
mod pins;

type Cell = (i32, i32);

// ---------------------------------------------------------------------------------------------------------------------
// The numbers (functions, not consts: `tests/content.rs` reads consts as unregistered tunables).
// ---------------------------------------------------------------------------------------------------------------------

/// The approved drawing, read from the docs (the owner's copy; the generator carries a byte-equal copy in the crate).
fn approved() -> &'static str {
    include_str!("../../../docs/landmarks/blueprints/hall.txt")
}

/// The drawing's side (owner decision 2026-11-13: "37 x 37").
fn side() -> i32 {
    37
}

/// The Holy site's side (owner decision 2026-11-13: "a 41-tile site: the hall plus a 2-tile apron each side").
fn site_side() -> i32 {
    41
}

/// The two camp tiles of the drawing (`E`), grid order: the back one (row 15), then the front one (row 27).
fn camp_grid() -> [Cell; 2] {
    [(18, 15), (18, 27)]
}

/// The nave's front run (header definition): the anchor's entrance.
fn nave() -> Vec<Cell> {
    (15..=21).map(|gx| (gx, 36)).collect()
}

/// The drawing's centre tile, the anchor's centre.
fn centre_grid() -> Cell {
    (18, 18)
}

/// Owner decision 2026-11-14 (2): spawn points stay "3 tiles clear of the shrine": no spawn point within this
/// Chebyshev distance of a light tile.
fn shrine_clear() -> i32 {
    3
}

/// The attempts `certified_styled` tries per seed.
fn attempt_limit() -> u64 {
    16
}

/// The stack's bound, not the owner's (measured on the prototype `agent/opus/P-MAPGEN-SIG-HOLY-1-measure` @ 471dd92b9,
/// the decided lever: worst attempt index 11 over seeds 1-32, 1-64 and 1-128): the worst attempt index over seeds 1-32.
/// 12 is also every other signature oracle's bound on this base (P-MAPGEN-SIG-SCALE-4).
fn worst_attempt_allowed() -> u64 {
    12
}

/// The stack's bound, not the owner's: the attempts seeds 1-32 use in all. The prototype used 93 over seeds 1-32 (189
/// over 1-64; today 89 and 192); 93 plus about 30%: 121.
fn attempts_allowed() -> u64 {
    121
}

/// The least number of spawn-pool points on the hall's open floor over seeds 1-32, and of maps with one. Owner
/// decision 2026-11-14 (2) allows them "as today"; the prototype measured 4.34 per map inside the hall's box (278 over
/// seeds 1-64; today's styler hall 2.44). Floors well under the measurement: the test says the permission is used.
fn open_floor_spawns_min() -> (usize, usize) {
    (32, 8)
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

// ---------------------------------------------------------------------------------------------------------------------
// The drawing and the orientations.
// ---------------------------------------------------------------------------------------------------------------------

fn grid() -> Vec<&'static [u8]> {
    approved()
        .lines()
        .filter(|l| !l.starts_with(';') && !l.is_empty())
        .map(str::as_bytes)
        .collect()
}

fn tiles_of(cs: &[u8]) -> Vec<Cell> {
    let mut out = Vec::new();
    for (gy, row) in grid().iter().enumerate() {
        for (gx, ch) in row.iter().enumerate() {
            if cs.contains(ch) {
                out.push((gx as i32, gy as i32));
            }
        }
    }
    out
}

/// One orientation: front direction and mirror flag.
type Orientation = (Cell, bool);

fn orientations() -> [Orientation; 8] {
    let mut out = [((0, 0), false); 8];
    for (k, u) in [(1, 0), (-1, 0), (0, 1), (0, -1)].into_iter().enumerate() {
        out[2 * k] = (u, false);
        out[2 * k + 1] = (u, true);
    }
    out
}

/// The drawing's turn of grid tile `g` (header definition), about grid (0, 0).
fn turn(o: Orientation, g: Cell) -> Cell {
    let (u, mirrored) = o;
    let s = if mirrored { -1 } else { 1 };
    let r = (s * u.1, -s * u.0);
    (g.0 * r.0 + g.1 * u.0, g.0 * r.1 + g.1 * u.1)
}

/// Where grid tile `g` lands when the turned box's corner nearest the map origin is `corner`.
fn place(o: Orientation, corner: Cell, g: Cell) -> Cell {
    let far = side() - 1;
    let ts = [(0, 0), (far, 0), (0, far), (far, far)].map(|g| turn(o, g));
    let lo = ts.iter().fold(ts[0], |m, c| (m.0.min(c.0), m.1.min(c.1)));
    let d = turn(o, g);
    (corner.0 - lo.0 + d.0, corner.1 - lo.1 + d.1)
}

fn row_major(mut cells: Vec<Cell>) -> Vec<Cell> {
    cells.sort_by_key(|c| (c.1, c.0));
    cells
}

// ---------------------------------------------------------------------------------------------------------------------
// Maps.
// ---------------------------------------------------------------------------------------------------------------------

fn on_options() -> pipeline::Options {
    pipeline::Options {
        decor: false,
        signature: true,
        ..pipeline::Options::default()
    }
}

/// One seed: the finished map, the accepted attempt's prefix, and its Landmarks phase's `Placed`.
struct Landed {
    seed: i64,
    map: GameMapData,
    prefix: attempt::AttemptState,
    placed: Placed,
}

fn landmarks_phase(seed: i64, n: u64) -> Option<(attempt::AttemptState, Placed)> {
    let fixed = coliseum::fixed_layout();
    let context = attempt::AttemptContext::new(&fixed);
    let frame = attempt::begin_attempt_signature(&context, seed, n, true).expect("prefix input")?;
    let mut st = stage::StageContext::new(&fixed)
        .apply(&frame, &recipes::Recipes::default())
        .expect("stage input")?;
    let placed = SignatureContext::new(&fixed)
        .apply(&frame, &mut st)
        .expect("signature input")?;
    Some((frame.state, placed))
}

fn on_maps() -> &'static Vec<(i64, Option<Landed>)> {
    static MAPS: OnceLock<Vec<(i64, Option<Landed>)>> = OnceLock::new();
    MAPS.get_or_init(|| {
        let fixed = coliseum::fixed_layout();
        let generator = pipeline::Pipeline::new(&fixed);
        (1..=32)
            .map(|seed| {
                let landed = generator
                    .certified_styled(seed, on_options())
                    .expect("generator error")
                    .map(|map| {
                        let (prefix, placed) =
                            landmarks_phase(seed, map.attempt).unwrap_or_else(|| {
                                panic!("seed {seed}: the accepted attempt's Landmarks phase")
                            });
                        Landed {
                            seed,
                            map,
                            prefix,
                            placed,
                        }
                    });
                (seed, landed)
            })
            .collect()
    })
}

/// Every seed 1-32 gives a map (G2, G5: a landmark is never a reason to lose a seed).
fn maps() -> impl Iterator<Item = &'static Landed> {
    on_maps().iter().map(|(seed, l)| {
        l.as_ref()
            .unwrap_or_else(|| panic!("seed {seed}: no map with the option on"))
    })
}

/// One wide seed: the seed, and its map with the accepted attempt's prefix (`None`: no map).
type Wide = (i64, Option<(GameMapData, attempt::AttemptState)>);

/// Seeds 33-128, option on: (seed, the map and the accepted attempt's prefix), generated once per test binary.
fn wide_maps() -> &'static Vec<Wide> {
    static MAPS: OnceLock<Vec<Wide>> = OnceLock::new();
    MAPS.get_or_init(|| {
        let fixed = coliseum::fixed_layout();
        let generator = pipeline::Pipeline::new(&fixed);
        let context = attempt::AttemptContext::new(&fixed);
        (33..=128)
            .map(|seed| {
                let kept = generator
                    .certified_styled(seed, on_options())
                    .expect("generator error")
                    .map(|map| {
                        let frame =
                            attempt::begin_attempt_signature(&context, seed, map.attempt, true)
                                .expect("prefix input")
                                .unwrap_or_else(|| {
                                    panic!("seed {seed}: the accepted attempt's prefix")
                                });
                        (map, frame.state)
                    });
                (seed, kept)
            })
            .collect()
    })
}

// ---------------------------------------------------------------------------------------------------------------------
// Helpers.
// ---------------------------------------------------------------------------------------------------------------------

fn at(m: &GameMapData, c: Cell) -> Option<usize> {
    (c.0 >= 0 && c.1 >= 0 && c.0 < m.w as i32 && c.1 < m.h as i32)
        .then(|| (c.1 * m.w as i32 + c.0) as usize)
}

fn cell(m: &GameMapData, i: usize) -> Cell {
    ((i as i32) % m.w as i32, (i as i32) / m.w as i32)
}

fn tile_cell(i: usize) -> Cell {
    ((i % coliseum::W) as i32, (i / coliseum::W) as i32)
}

fn world_cell(x: f64, y: f64) -> Cell {
    ((x / R.tile).floor() as i32, (y / R.tile).floor() as i32)
}

fn chebyshev(a: Cell, b: Cell) -> i32 {
    (a.0 - b.0).abs().max((a.1 - b.1).abs())
}

fn of_kind(m: &GameMapData, kind: FeatureKind) -> Vec<usize> {
    (0..m.features.len())
        .filter(|&k| m.features[k].kind == kind)
        .collect()
}

fn single(seed: i64, m: &GameMapData, kind: FeatureKind, zone: Zone) -> usize {
    let ks = of_kind(m, kind);
    assert_eq!(ks.len(), 1, "seed {seed}: {} {kind:?} features", ks.len());
    assert_eq!(m.features[ks[0]].zone, zone, "seed {seed}: {kind:?} zone");
    ks[0]
}

/// (x0, y0, x1, y1) of `r` grown by `by` (negative shrinks).
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

fn axis(f: u8) -> Cell {
    let v = DIR16[usize::from(f)];
    if v[0].abs() >= v[1].abs() {
        (v[0].signum(), 0)
    } else {
        (0, v[1].signum())
    }
}

/// The Holy site of a prefix; a failed assertion (the red of this oracle on the stub) when it has none.
fn holy_site(seed: i64, prefix: &attempt::AttemptState) -> &ReservedSite {
    let r = prefix.reserved.iter().find(|r| r.zone == Zone::Holy);
    assert!(
        r.is_some(),
        "seed {seed}: no Holy site in the prefix (sites {:?})",
        prefix.reserved.iter().map(|r| r.zone).collect::<Vec<_>>()
    );
    r.unwrap()
}

/// The box (header definition): the Holy site's inner rect, asserted 37 x 37 in a 41 x 41 site.
fn hall_box(seed: i64, prefix: &attempt::AttemptState) -> (i32, i32, i32, i32) {
    let r = holy_site(seed, prefix);
    assert_eq!(
        (r.rect.w as i32, r.rect.h as i32),
        (site_side(), site_side()),
        "seed {seed}: the Holy site {:?} is not {} a side",
        r.rect,
        site_side()
    );
    let b = grow(r.rect, -DEFAULT_DEFINITION.bands[0]);
    assert_eq!(
        (b.2 - b.0 + 1, b.3 - b.1 + 1),
        (side(), side()),
        "seed {seed}: the Holy site's inner rect {b:?} is not {} a side",
        side()
    );
    b
}

/// The hall on one map: its feature, the box's corner, the orientation it was drawn under.
struct Hall {
    k: usize,
    corner: Cell,
    o: Orientation,
}

impl Hall {
    fn tile(&self, g: Cell) -> Cell {
        place(self.o, self.corner, g)
    }
    fn tiles(&self, cs: &[u8]) -> Vec<Cell> {
        tiles_of(cs).into_iter().map(|g| self.tile(g)).collect()
    }
}

/// Grid tiles that differ from the drawing under `o` (first one described), and the hall's tiles off its drawn ones.
fn mismatches(
    m: &GameMapData,
    k: usize,
    corner: Cell,
    o: Orientation,
) -> (usize, Option<String>, usize) {
    let lit = light();
    let (mut bad, mut first, mut drawn) = (0, None, BTreeSet::new());
    for (gy, row) in grid().iter().enumerate() {
        for (gx, &ch) in row.iter().enumerate() {
            let g = (gx as i32, gy as i32);
            let p = place(o, corner, g);
            let Some(i) = at(m, p) else {
                bad += 1;
                first.get_or_insert_with(|| format!("grid {g:?} lands off the map at {p:?}"));
                continue;
            };
            let (tile, terrain, own) = (
                m.tiles[i],
                m.terrain[i],
                usize::from(m.feature_grid[i]) == k + 1,
            );
            let ok = match ch {
                b'#' => tile == TILE_WALL && own && terrain == 0,
                b'L' => tile == TILE_FLOOR && own && terrain == lit,
                _ => tile == TILE_FLOOR && !own && terrain != lit,
            };
            if matches!(ch, b'#' | b'L') {
                drawn.insert(i);
            }
            if !ok {
                bad += 1;
                first.get_or_insert_with(|| {
                    format!(
                        "grid {g:?} '{}' at {p:?}: tile {tile}, terrain {terrain}, owned by the hall {own}",
                        ch as char
                    )
                });
            }
        }
    }
    let extra = (0..m.feature_grid.len())
        .filter(|&i| usize::from(m.feature_grid[i]) == k + 1 && !drawn.contains(&i))
        .count();
    (bad, first, extra)
}

/// The map tile and glyph class of every wall and light grid tile under `o`, sorted.
fn landed_tiles(corner: Cell, o: Orientation) -> Vec<(Cell, u8)> {
    let mut out: Vec<(Cell, u8)> = tiles_of(b"#L")
        .into_iter()
        .map(|g| {
            let ch = grid()[g.1 as usize][g.0 as usize];
            (place(o, corner, g), ch)
        })
        .collect();
    out.sort();
    out
}

/// The hall of one map built on `prefix` (header definitions): the map's only `Hall` feature, in Holy (the Holy styler
/// places no hall of its own in signature mode), equal to the drawing in the Holy site's box under at least one
/// orientation; every orientation it equals lands the same tiles (the mirror tie of a symmetric drawing); the one
/// returned is `used` (the Holy frame's landing), which must be one of them, or else the first.
fn hall(
    seed: i64,
    m: &GameMapData,
    prefix: &attempt::AttemptState,
    used: Option<Orientation>,
) -> Hall {
    let b = hall_box(seed, prefix);
    let corner = (b.0, b.1);
    let halls = of_kind(m, FeatureKind::Hall);
    assert_eq!(
        halls.len(),
        1,
        "seed {seed}: {} Hall features (zones {:?}); want the drawn hall alone",
        halls.len(),
        halls
            .iter()
            .map(|&k| m.features[k].zone)
            .collect::<Vec<_>>()
    );
    let k = halls[0];
    assert_eq!(
        m.features[k].zone,
        Zone::Holy,
        "seed {seed}: the hall's zone"
    );
    let mut exact = Vec::new();
    let mut closest: Option<(usize, Orientation, Option<String>, usize)> = None;
    for o in orientations() {
        let (bad, first, extra) = mismatches(m, k, corner, o);
        if bad == 0 && extra == 0 {
            exact.push(o);
        }
        if closest.as_ref().is_none_or(|c| bad + extra < c.0 + c.3) {
            closest = Some((bad, o, first, extra));
        }
    }
    let (bad, o, first, extra) = closest.unwrap();
    assert!(
        !exact.is_empty(),
        "seed {seed} (attempt {}): the hall equals the drawing under no orientation; closest {o:?}: {bad} of {} tiles \
         differ (first: {first:?}), {extra} hall tiles outside the drawing",
        m.attempt,
        side() * side()
    );
    let tiles = landed_tiles(corner, exact[0]);
    for &o in &exact[1..] {
        assert!(
            landed_tiles(corner, o) == tiles,
            "seed {seed}: the hall equals the drawing under {:?} and {o:?}, which land different tiles",
            exact[0]
        );
    }
    let o = match used {
        Some(u) => {
            assert!(
                exact.contains(&u),
                "seed {seed}: the Holy frame landed under {u:?}, not one of the orientations the hall equals {exact:?}"
            );
            u
        }
        None => exact[0],
    };
    Hall { k, corner, o }
}

/// The orientation of the Holy frame's landing, if any.
fn frame_orientation(placed: &Placed) -> Option<Orientation> {
    placed
        .frames
        .iter()
        .find(|f| f.anchor.zone == Zone::Holy)
        .and_then(|f| f.landing)
        .map(|l| (l.orientation.front, l.orientation.mirrored))
}

fn hall_of(l: &Landed) -> Hall {
    hall(l.seed, &l.map, &l.prefix, frame_orientation(&l.placed))
}

/// The 8-connected pieces of `cells`, each sorted.
fn groups8(cells: &BTreeSet<Cell>) -> Vec<Vec<Cell>> {
    let mut seen = BTreeSet::new();
    let mut out = Vec::new();
    for &s in cells {
        if !seen.insert(s) {
            continue;
        }
        let (mut piece, mut stack) = (vec![s], vec![s]);
        while let Some(c) = stack.pop() {
            for dy in -1..=1 {
                for dx in -1..=1 {
                    let n = (c.0 + dx, c.1 + dy);
                    if cells.contains(&n) && seen.insert(n) {
                        piece.push(n);
                        stack.push(n);
                    }
                }
            }
        }
        piece.sort();
        out.push(piece);
    }
    out
}

fn bounds(cells: &[Cell]) -> (i32, i32, i32, i32) {
    cells
        .iter()
        .fold((i32::MAX, i32::MAX, i32::MIN, i32::MIN), |b, c| {
            (b.0.min(c.0), b.1.min(c.1), b.2.max(c.0), b.3.max(c.1))
        })
}

/// The art's reading of the hall (P-UNITY-LANDMARK-5) as failures, empty when it holds: 32 piers (wall groups that are
/// exact 3 x 3 boxes) and one larger wall group, the shrine, whose door (its box border's tiles that are not its walls)
/// is three tiles in one straight run on the box side that faces the anchor's front; the light is exactly the shrine's
/// 3 x 3 floor tiles with the light terrain (every tile of the hall's box with the light terrain), inside the shrine's
/// box.
fn art_faults(seed: i64, m: &GameMapData, h: &Hall) -> Vec<String> {
    let mut bad = Vec::new();
    let walls: BTreeSet<Cell> = (0..m.feature_grid.len())
        .filter(|&i| usize::from(m.feature_grid[i]) == h.k + 1 && m.tiles[i] == TILE_WALL)
        .map(|i| cell(m, i))
        .collect();
    let groups = groups8(&walls);
    let (piers, other): (Vec<&Vec<Cell>>, Vec<&Vec<Cell>>) = groups.iter().partition(|g| {
        let b = bounds(g);
        g.len() == 9 && b.2 - b.0 == 2 && b.3 - b.1 == 2
    });
    if piers.len() != 32 || other.len() != 1 {
        bad.push(format!(
            "seed {seed}: {} piers and {} other wall groups (sizes {:?}); want 32 and one shrine",
            piers.len(),
            other.len(),
            other.iter().map(|g| g.len()).collect::<Vec<_>>()
        ));
        return bad;
    }
    let shrine = other[0];
    let sb = bounds(shrine);
    let set: BTreeSet<Cell> = shrine.iter().copied().collect();
    let mut door = Vec::new();
    for y in sb.1..=sb.3 {
        for x in sb.0..=sb.2 {
            let edge = x == sb.0 || x == sb.2 || y == sb.1 || y == sb.3;
            if edge && !set.contains(&(x, y)) {
                door.push((x, y));
            }
        }
    }
    let straight = door.len() == 3
        && ((door.iter().all(|c| c.1 == door[0].1) && bounds(&door).2 - bounds(&door).0 == 2)
            || (door.iter().all(|c| c.0 == door[0].0) && bounds(&door).3 - bounds(&door).1 == 2));
    if !straight {
        bad.push(format!(
            "seed {seed}: the shrine's door {door:?} is not three tiles in one straight run"
        ));
    }
    let open = door
        .iter()
        .all(|&c| at(m, c).is_some_and(|i| m.tiles[i] == TILE_FLOOR));
    if !open {
        bad.push(format!(
            "seed {seed}: the shrine's door {door:?} is not floor"
        ));
    }
    let u = h.o.0;
    let facing = door.iter().all(|c| match u {
        (1, 0) => c.0 == sb.2,
        (-1, 0) => c.0 == sb.0,
        (0, 1) => c.1 == sb.3,
        _ => c.1 == sb.1,
    });
    if !facing {
        bad.push(format!(
            "seed {seed}: the shrine's door {door:?} is not on the side facing the front {u:?}"
        ));
    }
    let lit = light();
    let (b0, b1) = (h.corner, (h.corner.0 + side() - 1, h.corner.1 + side() - 1));
    let mut lights = Vec::new();
    for y in b0.1..=b1.1 {
        for x in b0.0..=b1.0 {
            if let Some(i) = at(m, (x, y)).filter(|&i| m.terrain[i] == lit) {
                lights.push((x, y));
                if m.tiles[i] != TILE_FLOOR {
                    bad.push(format!("seed {seed}: light tile {:?} is not floor", (x, y)));
                }
            }
        }
    }
    let lb = bounds(&lights);
    let block = lights.len() == 9 && lb.2 - lb.0 == 2 && lb.3 - lb.1 == 2;
    if !block || !inside(sb, (lb.0, lb.1)) || !inside(sb, (lb.2, lb.3)) {
        bad.push(format!(
            "seed {seed}: the hall box's light tiles {lights:?} are not one 3 x 3 block inside the shrine {sb:?}"
        ));
    }
    bad
}

/// Spawn points that break owner decision 2026-11-14 (2) (header definitions): on a hall wall or a light tile, or not
/// shrine-clear.
fn spawn_faults(seed: i64, m: &GameMapData, h: &Hall) -> Vec<String> {
    let walls: BTreeSet<Cell> = h.tiles(b"#").into_iter().collect();
    let lights = h.tiles(b"L");
    let points = m
        .spawn_pool
        .iter()
        .map(|p| ("pool", p.x, p.y))
        .chain(m.spawns.iter().map(|p| ("spawn", p.x, p.y)));
    let mut bad = Vec::new();
    for (what, x, y) in points {
        let c = world_cell(x, y);
        let near = lights
            .iter()
            .map(|&l| chebyshev(l, c))
            .min()
            .unwrap_or(i32::MAX);
        if walls.contains(&c) || near <= shrine_clear() {
            bad.push(format!(
                "seed {seed}: {what} point at tile {c:?} (on a hall wall: {}; {near} tiles from the shrine's light)",
                walls.contains(&c)
            ));
        }
    }
    bad
}

/// The certifier's failures on `m` (every zone strict), as `rule/subject`.
fn failures(seed: i64, m: &GameMapData) -> Vec<String> {
    certify::certify(
        m,
        &certify::Options {
            styled: true,
            zone: true,
            strict: certify::Strict::All,
        },
    )
    .unwrap_or_else(|e| panic!("seed {seed}: certifier refused the map: {e:?}"))
    .failures
    .iter()
    .map(|f| format!("{}/{}", f.rule, f.subject))
    .collect()
}

// ---------------------------------------------------------------------------------------------------------------------
// The table entry and the drawing.
// ---------------------------------------------------------------------------------------------------------------------

/// The Holy entry of the per-landmark table is the hall drawing: anchor kind `Hall`; drawing `Definition::hall_grid`
/// (the approved file); legend `#` a wall owned by the `Hall` on a `Site::wall` tile (as the church's and the cave's
/// walls: the hall's piers may stand in the legacy landmark protection, as the prototype measured), `L` floor of the
/// light terrain owned by the `Hall` on a `Site::floor` tile, `.` plain, `E`, `D` and `*` named marks on plain floor;
/// one feature, the `Hall`, job `Cover` (today's hall's first job), its anchor point the landmark's centre; placed
/// centred in the inner rect (37 in 37); entrance the front run through the `E` column (the nave's seven front tiles);
/// no doorway check (an open colonnade, walkable from every side); no by-tile or any-terrain kind. Its member kinds are
/// the implementer's (brief, design 6). Red on the stub: no Holy entry.
#[test]
fn the_holy_entry_is_the_hall_drawing_with_its_legend() {
    let d = DEFAULT_DEFINITION;
    let table = specs(&d);
    let holy = table.iter().find(|s| s.zone == Zone::Holy);
    assert!(holy.is_some(), "no Holy entry in specs()");
    let s = holy.unwrap();
    assert_eq!(s.kind, FeatureKind::Hall, "the anchor kind");
    assert!(
        s.drawing == Some(d.hall_grid) && d.hall_grid == approved(),
        "the Holy entry's drawing is {:?} bytes, want the approved hall.txt ({} bytes)",
        s.drawing.map(str::len),
        approved().len()
    );
    let glyph = |ch, write, feature, legality, mark| Glyph {
        ch,
        write,
        feature,
        legality,
        mark,
    };
    let hall = Some(FeatureKind::Hall);
    let mut want = vec![
        glyph(b'#', Write::Wall, hall, Legality::Wall, false),
        glyph(b'L', Write::Floor(light()), hall, Legality::Floor, false),
        glyph(b'.', Write::Nothing, None, Legality::Floor, false),
        glyph(b'*', Write::Nothing, None, Legality::Floor, true),
        glyph(b'D', Write::Nothing, None, Legality::Floor, true),
        glyph(b'E', Write::Nothing, None, Legality::Floor, true),
    ];
    want.sort_by_key(|g| g.ch);
    let mut got = s.legend.0.clone();
    got.sort_by_key(|g| g.ch);
    assert_eq!(got, want, "the Holy legend");
    assert_eq!(
        s.features,
        vec![FeatureSpec {
            kind: FeatureKind::Hall,
            job: FeatureJob::Cover,
            anchor: FeatureAnchor::Centre,
        }],
        "the Holy entry's features"
    );
    assert_eq!(s.placement, Placement::Centre, "the Holy placement");
    assert_eq!(
        s.entrance,
        EntranceRule::FrontRun(b'E'),
        "the Holy entrance"
    );
    assert!(!s.doorway, "the hall has no doorway check");
    assert!(s.by_tile.is_empty(), "no by-tile kind on Holy");
    assert!(s.any_terrain.is_empty(), "no any-terrain kind on Holy");
}

/// Guard (green on the stub). The drawing and the site are the owner's: `hall_grid` is the approved file; 37 x 37;
/// glyph counts `#` 315 (32 piers of 9 and the shrine's 27), `L` 9, `D` 3, `E` 2, `*` 6, `.` 1034; mirror symmetric;
/// the two `E` on column 18 at rows 15 and 27; the light a 3 x 3 block (17..=19, 3..=5); the nave's front run open and
/// closed by walls; the Holy site 41 a side with the 2-tile core band as its apron (37 + 2 x 2).
#[test]
fn guard_the_drawing_and_the_site_are_the_owners() {
    let d = DEFAULT_DEFINITION;
    assert!(
        d.hall_grid == approved(),
        "hall_grid is not the approved file"
    );
    let g = grid();
    assert_eq!(g.len() as i32, side(), "rows");
    assert!(g.iter().all(|r| r.len() as i32 == side()), "columns");
    let count = |ch: u8| tiles_of(&[ch]).len();
    assert_eq!(
        b"#LDE*.".map(count),
        [315, 9, 3, 2, 6, 1034],
        "glyph counts"
    );
    for (gy, row) in g.iter().enumerate() {
        let mut rev = row.to_vec();
        rev.reverse();
        assert_eq!(&rev[..], *row, "row {gy} is not mirror symmetric");
    }
    assert_eq!(tiles_of(b"E"), camp_grid().to_vec(), "the camp tiles");
    let block: Vec<Cell> = (3..=5)
        .flat_map(|y| (17..=19).map(move |x| (x, y)))
        .collect();
    assert_eq!(tiles_of(b"L"), block, "the light");
    let front = g[36];
    assert!(
        nave().iter().all(|&(gx, _)| front[gx as usize] == b'.')
            && front[14] == b'#'
            && front[22] == b'#',
        "the nave's front run"
    );
    assert_eq!(d.hall_site_side, site_side(), "Definition::hall_site_side");
    assert_eq!(
        site_side() - 2 * d.bands[0],
        side(),
        "the site less its apron is the drawing"
    );
}

// ---------------------------------------------------------------------------------------------------------------------
// The site and its camps (the Prefix).
// ---------------------------------------------------------------------------------------------------------------------

/// Seeds 1-32, the accepted attempt's prefix: the Holy site is 41 x 41 and carries the hall's two camps, `camp` the
/// drawing's back `E` and `extra` its front `E`, landed in the inner rect with the front read at the site's centre
/// tile (either mirror state lands them there: the `E` stand on the mirror axis); its `elite` is false (legacy's elite
/// rule, owner decision 2026-11-13). The church's and the cave's sites keep one camp, `elite` true; the volcano's and
/// the tree's carry none. Red on the stub: no Holy site.
#[test]
fn the_holy_site_carries_the_two_hall_camps_and_pins_no_elite() {
    for l in maps() {
        let seed = l.seed;
        let b = hall_box(seed, &l.prefix);
        let corner = (b.0, b.1);
        let t = (b.0 + centre_grid().0, b.1 + centre_grid().1);
        let o = (axis(front(t)), false);
        let want: Vec<usize> = camp_grid()
            .iter()
            .map(|&g| {
                let c = place(o, corner, g);
                (c.1 as usize) * coliseum::W + c.0 as usize
            })
            .collect();
        for r in &l.prefix.reserved {
            let got = (r.camp, r.extra.clone(), r.elite);
            let expect = match r.zone {
                Zone::Holy => (Some(want[0]), vec![want[1]], false),
                Zone::Sentenced | Zone::Frost => (r.camp, Vec::new(), true),
                _ => (None, Vec::new(), false),
            };
            assert_eq!(
                got, expect,
                "seed {seed}: the {:?} site's (camp, extra, elite)",
                r.zone
            );
            if matches!(r.zone, Zone::Sentenced | Zone::Frost) {
                assert!(
                    r.camp.is_some(),
                    "seed {seed}: the {:?} site has no camp",
                    r.zone
                );
            }
        }
    }
}

/// `relabel_sites` gives every tile of a Holy site (its whole rect) the label of the nearer of its two camps (squared
/// distance; `camp` on a tie), and no other tile changes. A pure write. Red on the stub: the Holy site is not relabelled.
#[test]
fn relabel_sites_gives_each_holy_site_tile_its_nearer_hall_camp() {
    let w = coliseum::W;
    let camp = |zone: u8, x: usize, y: usize| CampSite {
        zone,
        site: y * w + x,
        elite: false,
        spots: Vec::new(),
    };
    // A 41 site at (40, 36): inner rect from (42, 38); the E tiles (front +y) at (60, 53) and (60, 65).
    let camps = vec![camp(1, 5, 5), camp(5, 60, 53), camp(5, 60, 65)];
    let rect = TileRect {
        x: 40,
        y: 36,
        w: 41,
        h: 41,
    };
    let site = ReservedSite {
        zone: Zone::Holy,
        rect,
        camp: Some(camps[1].site),
        extra: vec![camps[2].site],
        elite: false,
        stream: Mulberry32::new(1),
    };
    let before = vec![7u8; w * coliseum::H];
    let mut grid = before.clone();
    relabel_sites(&[site], &camps, &mut grid);
    let d2 = |a: Cell, b: Cell| (a.0 - b.0).pow(2) + (a.1 - b.1).pow(2);
    let mut bad = Vec::new();
    for i in 0..grid.len() {
        let c = tile_cell(i);
        let want = if inside(grow(rect, 0), c) {
            if d2(c, (60, 53)) <= d2(c, (60, 65)) {
                2
            } else {
                3
            }
        } else {
            before[i]
        };
        if grid[i] != want {
            bad.push((c, grid[i], want));
        }
    }
    assert!(
        bad.is_empty(),
        "{} tiles wrong (tile, label, want), first {:?}",
        bad.len(),
        bad.first()
    );
}

// ---------------------------------------------------------------------------------------------------------------------
// The hall is the drawing.
// ---------------------------------------------------------------------------------------------------------------------

/// Seeds 1-32: the map's one `Hall` feature is the Holy hall and equals the drawing in the Holy site's 37 x 37 inner
/// rect under one of the eight orientations (the mirror tie: header definition); every front direction the Holy sites
/// produce occurs. Red on the stub: no Holy site; the styler's hall is a pier field of 38-39 a side.
#[test]
fn the_hall_is_the_blueprint_filling_the_holy_sites_inner_rect() {
    let (mut dirs, mut fronts) = (BTreeSet::new(), BTreeSet::new());
    for l in maps() {
        let h = hall_of(l);
        dirs.insert(h.o.0);
        let b = hall_box(l.seed, &l.prefix);
        fronts.insert(axis(front((b.0 + 18, b.1 + 18))));
    }
    assert_eq!(
        dirs, fronts,
        "front directions used vs the Holy sites' fronts"
    );
}

/// Seeds 1-32: the zone keeps ten camps, and exactly two of them stand in the hall's box, on the landed `E` tiles.
/// Red on the stub.
#[test]
fn two_holy_camps_stand_on_the_landed_e_tiles() {
    for l in maps() {
        let (seed, m) = (l.seed, &l.map);
        let h = hall_of(l);
        let holy: Vec<Cell> = m
            .camps
            .iter()
            .filter(|c| c.zone == Zone::Holy)
            .map(|c| world_cell(c.x, c.y))
            .collect();
        assert_eq!(
            holy.len(),
            camps::DEFAULT_DEFINITION.camps_per_zone,
            "seed {seed}: Holy camps"
        );
        let b = (
            h.corner.0,
            h.corner.1,
            h.corner.0 + side() - 1,
            h.corner.1 + side() - 1,
        );
        let mut inside_box: Vec<Cell> = m
            .camps
            .iter()
            .map(|c| world_cell(c.x, c.y))
            .filter(|&c| inside(b, c))
            .collect();
        inside_box.sort();
        let mut want = h.tiles(b"E");
        want.sort();
        assert_eq!(inside_box, want, "seed {seed}: the camps in the hall's box");
        for c in &want {
            assert!(
                holy.contains(c),
                "seed {seed}: the camp at {c:?} is not a Holy camp"
            );
        }
    }
}

/// The Holy anchor is the drawing's (seeds 1-32): kind `Hall`; footprint the box (the Holy site's inner rect, the
/// landing's box); centre the drawing's centre tile (18, 18) landed, the hall feature's anchor point; front the
/// generator's rule from the centre, its axis the orientation's front; entrance the nave's seven front-row tiles
/// landed, row-major. It is placed in the Landmarks phase with the other four: `Placed::anchors()` holds all five zones,
/// Holy's equal to the finished map's; the Holy frame lands the drawing (landing box = footprint), writes one feature
/// (the `Hall`, its box the footprint) and lands the marks `E`, `D`, `*` in grid order. The hall feature's box is the
/// footprint on the finished map. Red on the stub.
#[test]
fn the_holy_anchor_is_the_box_the_centre_tile_and_the_nave() {
    let all: BTreeSet<String> = [
        Zone::Fire,
        Zone::Sentenced,
        Zone::Plague,
        Zone::Frost,
        Zone::Holy,
    ]
    .iter()
    .map(|z| format!("{z:?}"))
    .collect();
    for l in maps() {
        let (seed, m) = (l.seed, &l.map);
        let h = hall_of(l);
        let a = m.anchors.iter().find(|a| a.zone == Zone::Holy);
        assert!(a.is_some(), "seed {seed}: no Holy anchor");
        let a = a.unwrap();
        assert_eq!(a.kind, FeatureKind::Hall, "seed {seed}: anchor kind");
        let fp = TileRect {
            x: h.corner.0,
            y: h.corner.1,
            w: side() as u32,
            h: side() as u32,
        };
        assert_eq!(
            a.footprint, fp,
            "seed {seed}: the Holy footprint is not the box"
        );
        let t = h.tile(centre_grid());
        assert_eq!(
            (h.corner.0 + 18, h.corner.1 + 18),
            t,
            "seed {seed}: (18, 18) lands mid-box"
        );
        assert_eq!((a.centre.x, a.centre.y), t, "seed {seed}: the Holy centre");
        assert_eq!(
            (m.features[h.k].ax, m.features[h.k].ay),
            t,
            "seed {seed}: the hall feature's anchor point"
        );
        assert_eq!(
            m.features[h.k].bbox, fp,
            "seed {seed}: the hall feature's box"
        );
        assert_eq!(a.front, front(t), "seed {seed}: the Holy front");
        assert_eq!(
            h.o.0,
            axis(a.front),
            "seed {seed}: the front row does not face the coliseum"
        );
        let want = row_major(nave().into_iter().map(|g| h.tile(g)).collect());
        let got: Vec<Cell> = a.entrance.iter().map(|p| (p.x, p.y)).collect();
        assert_eq!(
            got, want,
            "seed {seed}: the Holy entrance (the nave's seven front tiles, row-major)"
        );
        // The Landmarks phase.
        let zones: BTreeSet<String> = l
            .placed
            .anchors()
            .map(|a| format!("{:?}", a.zone))
            .collect();
        assert_eq!(zones, all, "seed {seed}: the Landmarks phase's anchors");
        let early = l.placed.anchors().find(|x| x.zone == Zone::Holy);
        assert_eq!(
            early,
            Some(a),
            "seed {seed}: the Landmarks phase's Holy anchor is the map's"
        );
        let f = l
            .placed
            .frames
            .iter()
            .find(|f| f.anchor.zone == Zone::Holy)
            .unwrap();
        assert_eq!(
            f.landing.map(|x| x.footprint),
            Some(fp),
            "seed {seed}: the Holy frame's landing box"
        );
        assert_eq!(
            f.features,
            vec![(FeatureKind::Hall, fp)],
            "seed {seed}: the Holy frame's features"
        );
        for ch in *b"DE*" {
            let want: Vec<Cell> = tiles_of(&[ch]).into_iter().map(|g| h.tile(g)).collect();
            let got: Vec<Cell> = f.mark(ch).map(|p| (p.x, p.y)).collect();
            assert_eq!(
                got, want,
                "seed {seed}: the Holy frame's '{}' marks",
                ch as char
            );
        }
    }
}

/// What the Unity art reads (P-UNITY-LANDMARK-5), on seeds 1-32: `art_faults` (32 piers that are exact 3 x 3 boxes; one
/// larger wall group, the shrine, with a three-tile door in one straight run on its box border, on the side facing the
/// front; the light exactly the shrine's 3 x 3 floor tiles with the light terrain). And the hall feature reads as
/// today's hall to the certifier and the client: jobs `[Cover]`, params `[axis 0/1, pitch 6, radius, piers 32, bays]`.
/// Red on the stub.
#[test]
fn the_art_reads_32_piers_one_shrine_with_its_door_and_its_light() {
    for l in maps() {
        let (seed, m) = (l.seed, &l.map);
        let h = hall_of(l);
        let bad = art_faults(seed, m, &h);
        assert!(bad.is_empty(), "{bad:#?}");
        let f = &m.features[h.k];
        assert_eq!(
            f.jobs,
            vec![FeatureJob::Cover],
            "seed {seed}: the hall's jobs"
        );
        assert!(
            f.p.len() == 5 && (0..=1).contains(&f.p[0]) && f.p[1] == R.hall_pitch && f.p[3] == 32,
            "seed {seed}: the hall's params {:?}, want [axis 0/1, {}, radius, 32, bays]",
            f.p,
            R.hall_pitch
        );
    }
}

// ---------------------------------------------------------------------------------------------------------------------
// Spawn points (owner decision 2026-11-14, 2).
// ---------------------------------------------------------------------------------------------------------------------

/// Seeds 1-32: no spawn point (pool or spawn) stands on a hall wall or a light tile, or within 3 tiles (Chebyshev) of
/// the shrine's light. Red on the stub: no Holy site.
#[test]
fn spawn_points_keep_off_the_hall_walls_and_three_tiles_clear_of_the_light() {
    let mut bad = Vec::new();
    for l in maps() {
        let h = hall_of(l);
        bad.extend(spawn_faults(l.seed, &l.map, &h));
    }
    assert!(
        bad.is_empty(),
        "{} spawn points break the rule: {bad:#?}",
        bad.len()
    );
}

/// Seeds 1-32: spawn-pool points do stand on the hall's open floor (in its box, on neither a wall nor a light tile):
/// at least `open_floor_spawns_min()` (total, maps). The permission is used, as today's hall is. Red on the stub.
#[test]
fn spawn_points_stand_on_the_halls_open_floor() {
    let (mut total, mut maps_with) = (0, 0);
    for l in maps() {
        let h = hall_of(l);
        let solid: BTreeSet<Cell> = h.tiles(b"#L").into_iter().collect();
        let b = (
            h.corner.0,
            h.corner.1,
            h.corner.0 + side() - 1,
            h.corner.1 + side() - 1,
        );
        let n = l
            .map
            .spawn_pool
            .iter()
            .map(|p| world_cell(p.x, p.y))
            .filter(|&c| inside(b, c) && !solid.contains(&c))
            .count();
        total += n;
        maps_with += usize::from(n > 0);
    }
    let (want_total, want_maps) = open_floor_spawns_min();
    assert!(
        total >= want_total && maps_with >= want_maps,
        "{total} spawn-pool points on the hall's open floor over seeds 1-32 on {maps_with} maps (want at least \
         {want_total} on {want_maps})"
    );
}

// ---------------------------------------------------------------------------------------------------------------------
// The certifier (owner decision 2026-11-14, 3).
// ---------------------------------------------------------------------------------------------------------------------

/// Guard (green on the stub). Seeds 1-32: every map keeps the volcano, the church, the tree and the cave and a Holy
/// anchor, and certifies with zero failures (every zone strict): the drawn hall's shrine is not a `feature-whole`
/// failure.
#[test]
fn guard_every_map_keeps_its_landmarks_and_certifies_clean() {
    for l in maps() {
        let (seed, m) = (l.seed, &l.map);
        single(seed, m, FeatureKind::Volcano, Zone::Fire);
        single(seed, m, FeatureKind::Church, Zone::Sentenced);
        single(seed, m, FeatureKind::BogTree, Zone::Plague);
        single(seed, m, FeatureKind::Cave, Zone::Frost);
        assert!(
            m.anchors.iter().any(|a| a.zone == Zone::Holy),
            "seed {seed}: no Holy anchor"
        );
        let f = failures(seed, m);
        assert!(f.is_empty(), "seed {seed}: {f:?}");
    }
}

/// Seeds 1-8: the shrine's exemption covers the shrine only. A doctored hall whose top-left pier (grid (0..=2, 0..=2))
/// loses its middle tile (floor, unowned) or gains a wall owned by the hall beside it (grid (3, 1)) is not an exact
/// 3 x 3 pier, and the certifier reports `feature-whole`. Red on the stub: no Holy site.
#[test]
fn a_hall_pier_that_is_not_an_exact_three_by_three_is_refused() {
    for l in maps().filter(|l| l.seed <= 8) {
        let seed = l.seed;
        let h = hall_of(l);
        let mid = at(&l.map, h.tile((1, 1))).unwrap();
        let beside = at(&l.map, h.tile((3, 1))).unwrap();
        assert_eq!(
            l.map.tiles[beside], TILE_FLOOR,
            "seed {seed}: grid (3, 1) is floor"
        );
        let mut cut = l.map.clone();
        cut.tiles[mid] = TILE_FLOOR;
        cut.feature_grid[mid] = 0;
        cut.features[h.k].tiles -= 1;
        let mut bump = l.map.clone();
        bump.tiles[beside] = TILE_WALL;
        bump.terrain[beside] = 0;
        bump.feature_grid[beside] = u8::try_from(h.k + 1).unwrap();
        bump.features[h.k].tiles += 1;
        for (what, m) in [
            ("a pier without its middle", &cut),
            ("a pier with a wall beside it", &bump),
        ] {
            let f = failures(seed, m);
            assert!(
                f.iter().any(|x| x.starts_with("feature-whole/")),
                "seed {seed}: {what}: no feature-whole failure among {f:?}"
            );
        }
    }
}

// ---------------------------------------------------------------------------------------------------------------------
// The elites (owner decision 2026-11-13: legacy's rule among all ten camps).
// ---------------------------------------------------------------------------------------------------------------------

/// Guard (green on the stub; red if the hall's camps are pinned as elites). Seeds 1-32: the Holy elites are legacy's
/// choice among all ten Holy camps (`zones/camps/mod.rs`, `place_camps_reserving` without a pinned elite): the first the
/// camp farthest from the coliseum centre tile (CX, CY) (squared distance; the earlier camp on a tie), the second the
/// farthest of the rest at least `elite_min_sep` from the first, else the farthest of the rest.
#[test]
fn the_holy_elites_follow_legacys_rule_among_all_ten_camps() {
    let d = camps::DEFAULT_DEFINITION;
    for l in maps() {
        let seed = l.seed;
        let holy: Vec<(Cell, bool)> = l
            .map
            .camps
            .iter()
            .filter(|c| c.zone == Zone::Holy)
            .map(|c| (world_cell(c.x, c.y), c.elite_spot >= 0))
            .collect();
        let d2 = |a: Cell, b: Cell| (a.0 - b.0).pow(2) + (a.1 - b.1).pow(2);
        let mut by_far: Vec<usize> = (0..holy.len()).collect();
        by_far.sort_by(|&a, &b| {
            d2(holy[b].0, (CX, CY))
                .cmp(&d2(holy[a].0, (CX, CY)))
                .then(a.cmp(&b))
        });
        let first = by_far[0];
        let rest = &by_far[1..];
        let second = rest
            .iter()
            .copied()
            .find(|&c| f64::from(d2(holy[c].0, holy[first].0)) >= d.elite_min_sep * d.elite_min_sep)
            .unwrap_or(rest[0]);
        let want: BTreeSet<Cell> = [first, second]
            .iter()
            .take(d.elites_per_zone)
            .map(|&k| holy[k].0)
            .collect();
        let got: BTreeSet<Cell> = holy.iter().filter(|c| c.1).map(|c| c.0).collect();
        assert_eq!(got, want, "seed {seed}: the Holy elites");
    }
}

// ---------------------------------------------------------------------------------------------------------------------
// Every seed, the attempt bounds, option off.
// ---------------------------------------------------------------------------------------------------------------------

/// Guard (green on the stub). Seeds 33-128 each keep a map (seeds 1-32: every test above), and it certifies with zero
/// failures.
#[test]
fn wide_every_seed_33_to_128_keeps_a_map_that_certifies_clean() {
    let mut bad = Vec::new();
    for (seed, kept) in wide_maps() {
        match kept {
            None => bad.push(format!("seed {seed}: no map with the option on")),
            Some((m, _)) => {
                let f = failures(*seed, m);
                if !f.is_empty() {
                    bad.push(format!("seed {seed}: {f:?}"));
                }
            }
        }
    }
    assert!(bad.is_empty(), "{bad:#?}");
}

/// Seeds 33-128: the hard rules hold on every map: the hall is the drawing in the Holy site's inner rect, two Holy
/// camps on its `E` tiles, the art's reading, the spawn rule. Red on the stub: no Holy site.
#[test]
fn wide_the_hall_its_camps_the_art_and_the_spawn_rule_hold() {
    let mut bad = Vec::new();
    for (seed, kept) in wide_maps() {
        let Some((m, prefix)) = kept else { continue };
        let h = hall(*seed, m, prefix, None);
        let b = (
            h.corner.0,
            h.corner.1,
            h.corner.0 + side() - 1,
            h.corner.1 + side() - 1,
        );
        let mut camps_in: Vec<Cell> = m
            .camps
            .iter()
            .filter(|c| inside(b, world_cell(c.x, c.y)))
            .map(|c| world_cell(c.x, c.y))
            .collect();
        camps_in.sort();
        let mut want = h.tiles(b"E");
        want.sort();
        if camps_in != want
            || m.camps
                .iter()
                .any(|c| want.contains(&world_cell(c.x, c.y)) && c.zone != Zone::Holy)
        {
            bad.push(format!(
                "seed {seed}: camps in the hall {camps_in:?}, want Holy camps on {want:?}"
            ));
        }
        bad.extend(art_faults(*seed, m, &h));
        bad.extend(spawn_faults(*seed, m, &h));
    }
    assert!(bad.is_empty(), "{} failures: {bad:#?}", bad.len());
}

/// Guard (the stack's bound, `worst_attempt_allowed`, not the owner's). The worst attempt index over seeds 1-32.
#[test]
fn guard_the_worst_attempt_stays_within_the_bound() {
    let worst = maps()
        .map(|l| (l.map.attempt, l.seed))
        .max()
        .expect("seeds generated");
    assert!(
        worst.0 <= worst_attempt_allowed(),
        "seed {}: attempt {} (bound {})",
        worst.1,
        worst.0,
        worst_attempt_allowed()
    );
}

/// Guard (the stack's bound, `attempts_allowed`, not the owner's). The attempts seeds 1-32 use in all.
#[test]
fn guard_the_attempts_used_stay_within_the_bound() {
    let used: u64 = maps().map(|l| l.map.attempt + 1).sum();
    assert!(
        used <= attempts_allowed(),
        "{used} attempts over seeds 1-32 (bound {})",
        attempts_allowed()
    );
}

/// Guard. Option off: seeds 1-8 give today's maps byte for byte (G8).
#[test]
fn guard_option_off_is_todays_generator() {
    let fixed = coliseum::fixed_layout();
    let generator = pipeline::Pipeline::new(&fixed);
    for (seed, hash, json) in pins::pinned_off() {
        let m = generator
            .certified_styled(seed, pipeline::Options::default())
            .unwrap()
            .expect("certified");
        assert_eq!(map_hash::map_hash(&m), hash, "seed {seed}: map hash");
        assert_eq!(
            pins::fnv(&serde_json::to_vec(&m).unwrap()),
            json,
            "seed {seed}: map JSON digest"
        );
    }
}

// ---------------------------------------------------------------------------------------------------------------------
// Report (for the owner; brief, "What to measure and report").
// ---------------------------------------------------------------------------------------------------------------------

/// Holy's land: the share of the Holy zone's free ground (floor, or owned by a Holy styler feature) that the Holy
/// styler fills (tiles owned by a Holy feature other than the drawn hall, a legacy `Landmark`, `Infill` or `Bush`, or
/// of the light terrain and not the drawn hall's), with the tiles of `cut` (inclusive rect) taken out.
fn holy_share(
    m: &GameMapData,
    drawn: Option<usize>,
    cut: Option<(i32, i32, i32, i32)>,
) -> (usize, usize) {
    let lit = light();
    let (mut filled, mut free) = (0, 0);
    for i in 0..m.tiles.len() {
        if m.region_grid[i] != Zone::Holy.code() || cut.is_some_and(|r| inside(r, cell(m, i))) {
            continue;
        }
        let owner = usize::from(m.feature_grid[i]).checked_sub(1);
        let styler = owner.is_some_and(|k| {
            Some(k) != drawn
                && m.features[k].zone == Zone::Holy
                && !matches!(
                    m.features[k].kind,
                    FeatureKind::Landmark | FeatureKind::Infill | FeatureKind::Bush
                )
        });
        if m.tiles[i] == TILE_FLOOR || styler {
            free += 1;
            if styler || (m.terrain[i] == lit && (drawn.is_none() || owner != drawn)) {
                filled += 1;
            }
        }
    }
    (filled, free)
}

/// Seeds 1-64, option on and off: attempts and refusals, the hall's camps among the elites, spawn-pool points on the
/// hall's open floor, Holy's land share (option on: whole zone and with the Holy site taken out; option off: whole
/// zone and with the styler hall's box grown by the apron taken out). Run on the stub ("before") and on the build.
#[test]
#[ignore]
fn report_seeds_1_to_64() {
    let fixed = coliseum::fixed_layout();
    let generator = pipeline::Pipeline::new(&fixed);
    let context = attempt::AttemptContext::new(&fixed);
    let mut refusals = BTreeMap::<String, usize>::new();
    let (mut used, mut worst, mut lost) = ((0u64, 0u64), ((0u64, 0i64), (0u64, 0i64)), Vec::new());
    let (mut hall_elites, mut open_spawns) = (0usize, 0usize);
    let mut shares: Vec<(f64, f64, f64, f64)> = Vec::new();
    for seed in 1..=64i64 {
        let mut landed = None;
        for n in 0..attempt_limit() {
            let (report, _) = generator
                .styled_attempt_with_trace(seed, n, on_options())
                .expect("generator error");
            match report.map {
                Some(map) => {
                    landed = Some((n, map));
                    break;
                }
                None => {
                    *refusals
                        .entry(format!("{:?}", report.refusal.expect("refused attempt")))
                        .or_default() += 1;
                }
            }
        }
        let Some((n, m)) = landed else {
            eprintln!("seed {seed}: no map");
            lost.push(seed);
            continue;
        };
        used.1 += n + 1;
        worst.1 = worst.1.max((n, seed));
        if seed <= 32 {
            used.0 += n + 1;
            worst.0 = worst.0.max((n, seed));
        }
        let prefix = attempt::begin_attempt_signature(&context, seed, n, true)
            .expect("prefix input")
            .expect("the accepted attempt's prefix")
            .state;
        let site = prefix
            .reserved
            .iter()
            .find(|r| r.zone == Zone::Holy)
            .map(|r| grow(r.rect, 0));
        let drawn = site.map(|_| hall(seed, &m, &prefix, None));
        let mut line = format!("seed {seed}: attempt {n}");
        if let Some(h) = &drawn {
            let e = h.tiles(b"E");
            let elites = m
                .camps
                .iter()
                .filter(|c| c.elite_spot >= 0 && e.contains(&world_cell(c.x, c.y)))
                .count();
            hall_elites += elites;
            let solid: BTreeSet<Cell> = h.tiles(b"#L").into_iter().collect();
            let b = (
                h.corner.0,
                h.corner.1,
                h.corner.0 + side() - 1,
                h.corner.1 + side() - 1,
            );
            let k = m
                .spawn_pool
                .iter()
                .map(|p| world_cell(p.x, p.y))
                .filter(|&c| inside(b, c) && !solid.contains(&c))
                .count();
            open_spawns += k;
            line +=
                &format!("; hall camps elite {elites}; pool points on the hall's open floor {k}");
        }
        let (a, z) = holy_share(&m, drawn.as_ref().map(|h| h.k), None);
        let (b_, y) = holy_share(&m, drawn.as_ref().map(|h| h.k), site);
        let off = generator
            .certified_styled(
                seed,
                pipeline::Options {
                    decor: false,
                    ..pipeline::Options::default()
                },
            )
            .expect("generator error");
        let (c, x, d_, w) = match &off {
            Some(o) => {
                let styler_hall = of_kind(o, FeatureKind::Hall)
                    .into_iter()
                    .filter(|&k| o.features[k].zone == Zone::Holy)
                    .max_by_key(|&k| o.features[k].tiles)
                    .map(|k| grow(o.features[k].bbox, DEFAULT_DEFINITION.bands[0]));
                let (c, x) = holy_share(o, None, None);
                let (d_, w) = holy_share(o, None, styler_hall);
                (c, x, d_, w)
            }
            None => (0, 1, 0, 1),
        };
        let r = |p: usize, q: usize| p as f64 / q.max(1) as f64;
        shares.push((r(a, z), r(b_, y), r(c, x), r(d_, w)));
        eprintln!(
            "{line}; Holy share on {a}/{z} = {:.3}, on less the site {b_}/{y} = {:.3}; off {c}/{x} = {:.3}, off less \
             the styler hall {d_}/{w} = {:.3}",
            r(a, z),
            r(b_, y),
            r(c, x),
            r(d_, w)
        );
    }
    let median = |f: fn(&(f64, f64, f64, f64)) -> f64| {
        let mut v: Vec<f64> = shares.iter().map(f).collect();
        v.sort_by(f64::total_cmp);
        v.get(v.len() / 2).copied().unwrap_or(f64::NAN)
    };
    eprintln!(
        "attempts {} / {} (seeds 1-32 / 1-64); worst {:?} / {:?} (attempt, seed); no map {lost:?}",
        used.0, used.1, worst.0, worst.1
    );
    eprintln!("refusals {refusals:?}");
    eprintln!("hall camps that are elites {hall_elites}; pool points on the hall's open floor {open_spawns}");
    eprintln!(
        "Holy share medians: on {:.3}, on less the site {:.3}; off {:.3}, off less the styler hall {:.3}",
        median(|s| s.0),
        median(|s| s.1),
        median(|s| s.2),
        median(|s| s.3)
    );
}
