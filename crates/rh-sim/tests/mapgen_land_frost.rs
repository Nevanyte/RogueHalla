//! Oracle of P-LAND-FROST-1 (brief `docs/backlog/packets/P-LAND-FROST-1.md`): with `pipeline::Options::signature` on,
//! the Frost styler builds its lake in front of the cave's mouth first, on ground softly reserved for it, and the parts
//! of the owner's lake rule are targets, never a refusal. No legacy counterpart (DEV-020: the signature landmarks are the
//! owner's, not legacy's); option off stays byte for byte (`pins::pinned_off`, guarded by
//! `mapgen_signature_frost.rs::g8_option_off_maps_are_todays` and `mapgen_signature_frost_lake.rs`, not repeated here).
//!
//! The owner's direction (`docs/coordination/LAND_GENERATION_REQUIREMENTS.md` section 3, Frost): "The lake is built in
//! front of the mouth first, not grown and checked afterwards: about 400 tiles (give or take 15%), on the line from the
//! coliseum to the mouth, 3 dry tiles between mouth and shore, and always one dry way round it to the mouth at least 3
//! wide. One smaller second lake elsewhere." And "Every zone. About today's share of the zone's free ground as fence,
//! bog and ice, within about a fifth, measured after the site is taken out."
//!
//! **The owner's decision of 2026-11-14** ("Yes to both", to: "build it first in front of the cave mouth, with the
//! softer ground reservation, as targets that never cost a map?"). Measured as hard guarantees the rule cannot hold, so:
//! * hard (asserted on every map, seeds 1-128): every seed keeps a map, within the stack's attempts; every map has a lake
//!   at the mouth and passes `lake-at-mouth`; no map has stray ice; Frost's guarantee list stays `["lake-at-mouth"]` and
//!   breaking a target refuses nothing; the cave's room is a soft fan (24 deep, the inner rect's width) given up only
//!   when the round's camps cannot be placed with it; the volcano's fan stays hard;
//! * targets (counted over seeds 1-128, each a measured-gap pin in the style of `mapgen_land_fire.rs`
//!   `known_gap_about_one_fire_camp_in_twenty_has_no_crater`): the size window, the gap, the line, the dry way, the
//!   second lake, all at once, the median size. Each floor is set on the lane's judgment a little under the value the
//!   decided prototype measured and above today's game; it is the last value so set: a later packet whose maps fall
//!   under one stops, and the measurement goes to the owner;
//! * the owner's own number, kept exactly: the ice share against option off, median over seeds 1-32, within 0.8-1.2.
//!
//! Definitions (tile units; a tile's point is its centre, x + 0.5; "floor" is tile value 0, walkable 4-connected):
//! * cave: the one feature of kind `Cave`; mouth: the Frost anchor's `entrance` tiles (the frame's mark `D`).
//! * mouth point M: the mean of the mouth tiles' points. Coliseum point C: the point of the tile of `coliseum.centre`.
//!   The line: from M toward C. Along (s) and off (o) the line, for a point p: s = (p - M) . u and o = |(p - M) x u|,
//!   with u the unit vector from M to C.
//! * ice: floor whose terrain is the cave's ice (`signature::DEFAULT_DEFINITION.ice`, 6). Dry: floor that is not ice.
//!   Unowned Frost ice: ice in the Frost region that the cave does not own.
//! * the mouth lake: the largest 4-connected body of unowned Frost ice (first in row order on a tie of size). Its size:
//!   its tile count. A map has a lake at the mouth when its mouth lake is not empty.
//! * the other lakes: the rest of the unowned Frost ice, grouped so that two pieces at most `spacing` apart (Chebyshev)
//!   are one lake. Stray ice: more than one other lake.
//! * Chebyshev distance between two tile sets: the least max(|dx|, |dy|) over their tiles; d means d - 1 tiles between.
//! * the size target: the mouth lake holds `size` (340-460) tiles.
//! * the line target (counted): the mouth lake's centroid lies at most `axis_offset` (4) tiles off the line. The
//!   mouth-lake target (below) also asks that the centroid lie strictly between M and C along the line (0 < s < |C - M|)
//!   and that the line cross the lake (some lake tile with s > 0 has its point within 0.5 of the line).
//! * the gap target: the mouth lake's Chebyshev distance from the mouth is exactly `mouth_gap` + 1 (3 tiles between),
//!   the fewest 4-connected floor steps from a mouth tile (step 0) to the lake are exactly `mouth_gap` + 1, and no
//!   unowned Frost ice of any lake lies within Chebyshev `mouth_gap` of a mouth tile.
//! * the mouth-lake target: size, line (all three parts) and gap together (`style_rime::mouth_lake_holds`).
//! * the dry way, `way_width` = 3 wide (`style_rime::dry_way_holds`): a 4-connected walk of block centres, each block the
//!   3 x 3 square round its centre wholly dry floor; it starts at any centre beyond the lake's far side (s of the centre
//!   at least the largest s of a lake tile's point plus 2) and ends at a centre whose block holds a mouth tile or a tile
//!   4-adjacent to one. None without a mouth lake.
//! * the second lake (`style_rime::second_lake_holds`): exactly one other lake, of `second` (60-200) tiles and fewer than
//!   the mouth lake's, more than `spacing` (Chebyshev) from it.
//! * the whole rule: the mouth-lake target, the dry way and the second lake together.
//! * free ground of the zone: Frost floor outside the cave's footprint and apron (the core band of `signature::band`;
//!   the site taken out); option off there is no cave: all Frost floor. Ice share: unowned Frost ice outside the core
//!   band (option off: all Frost ice) over free ground.
//! * the cave's room: the fan of the cave's table entry (`spec::Spec::fan`, `FanHold::Soft`): in front of the Frost
//!   site's inner rect (the site less `bands[0]`), along the front axis read at the site's centre tile, `cave_fan_depth`
//!   deep and the inner rect's width plus `cave_fan_side` on each side, clipped to the map (ADR 0028 decision 10's
//!   geometry, `signature::fan`). Kept for an attempt: recorded in `AttemptState::fans`; given up: not recorded.
use rh_sim::mapdata::{FeatureKind, GameMapData, TileRect, Zone};
use rh_sim::mapgen::coliseum::{CX, CY, H, W};
use rh_sim::mapgen::landmarks::recipe_support::DIR16;
use rh_sim::mapgen::landmarks::signature::frame::LandmarkFrame;
use rh_sim::mapgen::landmarks::signature::spec::{specs, Fan, FanHold, Spec};
use rh_sim::mapgen::landmarks::signature::{
    band, Band, Definition, ReservedSite, SignatureContext, DEFAULT_DEFINITION,
};
use rh_sim::mapgen::landmarks::{recipes, stage};
use rh_sim::mapgen::style_rime::{
    dry_way_holds, lake_rule, mouth_lake_holds, second_lake_holds, LakeRule,
};
use rh_sim::mapgen::style_zones::guarantees;
use rh_sim::mapgen::zones::{camps::CampContext, ZoneContext};
use rh_sim::mapgen::{attempt, coliseum, pipeline};
use std::collections::{BTreeSet, VecDeque};
use std::sync::OnceLock;

type Cell = (i32, i32);
type Rect = (usize, usize, usize, usize);

// --------------------------------------------------------------------------------------------------------------------
// The numbers (functions, not consts: `tests/content.rs` reads consts as unregistered tunables).
// --------------------------------------------------------------------------------------------------------------------

/// The owner's numbers (lake 400 give or take 15%, 3 dry tiles, a way at least 3 wide) and the lane's (the centroid at
/// most 4 off the line, the second lake 60-200 tiles, 3 dry tiles between the lakes), as `style_rime::lake_rule` holds
/// them.
fn rule() -> LakeRule {
    LakeRule {
        size: (340, 460),
        mouth_gap: 3,
        way_width: 3,
        axis_offset: 4,
        second: (60, 200),
        spacing: 3,
    }
}

/// The seeds every map assertion and count runs on.
fn seeds() -> std::ops::RangeInclusive<i64> {
    1..=128
}

/// The seeds the ice share is taken on (the owner's number was measured there).
fn share_seeds() -> std::ops::RangeInclusive<i64> {
    1..=32
}

/// The attempts `certified_styled` may use (`pipeline` rules, `certified_attempts`).
fn attempt_limit() -> u64 {
    16
}

/// The stack's bound on the worst kept attempt index (P-MAPGEN-SIG-SCALE-4's oracles; today's game 11 on seed 11, the
/// decided prototype 10 on seed 16, seeds 1-128).
fn worst_attempt_allowed() -> u64 {
    12
}

/// The owner's amounts rule: the median over seeds 1-32 of (ice share option on) / (ice share option off) lies in this
/// window ("about today's share, within about a fifth"). Today's game 0.639; the decided prototype 0.810.
fn share_window() -> (f64, f64) {
    (0.8, 1.2)
}

/// Spires and headlands keep today's rules: their totals over seeds 1-32 keep at least these (the first oracle's 80% of
/// today's option-on totals, 32 and 91).
fn spire_headland_floor() -> (usize, usize) {
    (25, 72)
}

/// The cave's room (owner decision 2026-11-14): 24 tiles deep, the inner rect's width (0 to the side).
fn cave_room() -> (i32, i32) {
    (24, 0)
}

// The counted floors, seeds 1-128 (kept maps). Each: the decided prototype's value, today's game's value, the floor.
// Set on the lane's judgment; the last value so set: a packet whose maps fall under one stops and goes to the owner.

/// Mouth lakes of 340-460 tiles: prototype 46, today 30.
fn size_floor() -> usize {
    40
}

/// Median mouth lake size (tiles): prototype 305.5, today 254.
fn median_floor() -> f64 {
    280.0
}

/// Exactly 3 dry tiles from the mouth to the shore (the gap target): prototype 111, today 0.
fn gap_floor() -> usize {
    97
}

/// The centroid within 4 tiles of the line: prototype 90, today 81. The floor must lie above today's: 85.
fn line_floor() -> usize {
    85
}

/// A dry way 3 wide: prototype 124, today 48.
fn way_floor() -> usize {
    110
}

/// One smaller second lake apart: prototype 104, today 37.
fn second_floor() -> usize {
    92
}

/// The whole rule at once: prototype 25, today 0.
fn whole_floor() -> usize {
    21
}

/// The default table's room kept for the attempt on at least this many kept maps, and given up on at least one:
/// prototype 114 kept, 14 given up.
fn room_kept_floor() -> usize {
    100
}

fn ice() -> u8 {
    DEFAULT_DEFINITION.ice
}

fn tile_size() -> f64 {
    32.0
}

fn on_options() -> pipeline::Options {
    pipeline::Options {
        decor: false,
        signature: true,
        ..pipeline::Options::default()
    }
}

fn off_options() -> pipeline::Options {
    pipeline::Options {
        decor: false,
        ..pipeline::Options::default()
    }
}

// --------------------------------------------------------------------------------------------------------------------
// The maps.
// --------------------------------------------------------------------------------------------------------------------

/// One seed with the option on: the attempt that gave the map, the refusals before it, the map, its Frost frame, the
/// fans its Prefix recorded and the cave's room as the definition draws it for its Frost site.
struct Landed {
    seed: i64,
    attempt: u64,
    refusals: Vec<String>,
    map: GameMapData,
    frame: LandmarkFrame,
    fans: Vec<Rect>,
    room: Rect,
}

impl Landed {
    fn room_kept(&self) -> bool {
        self.fans.contains(&self.room)
    }
}

struct Run {
    kept: Vec<Landed>,
    lost: Vec<(i64, Vec<String>)>,
}

/// The Prefix and Landmarks phase of an accepted attempt, option on (its frames are the ones the pipeline hands the
/// Frost styler and guarantees): the Frost frame, the recorded fans, the Frost site.
fn phases(seed: i64, n: u64) -> (LandmarkFrame, Vec<Rect>, ReservedSite) {
    let fixed = coliseum::fixed_layout();
    let context = attempt::AttemptContext::new(&fixed);
    let prefix = attempt::begin_attempt_signature(&context, seed, n, true)
        .expect("prefix input")
        .expect("the accepted attempt passed the Prefix");
    let mut st = stage::StageContext::new(&fixed)
        .apply(&prefix, &recipes::Recipes::default())
        .expect("stage input")
        .expect("the accepted attempt passed the landmark stage");
    let frame = SignatureContext::new(&fixed)
        .apply(&prefix, &mut st)
        .expect("signature input")
        .expect("the accepted attempt placed its landmarks")
        .frames
        .into_iter()
        .find(|f| f.zone() == Zone::Frost)
        .unwrap_or_else(|| panic!("seed {seed}: no Frost frame"));
    let site = site(&prefix.state.reserved, Zone::Frost).clone();
    (frame, prefix.state.fans.clone(), site)
}

fn land(generator: &pipeline::Pipeline<'_>, seed: i64) -> Result<Landed, (i64, Vec<String>)> {
    let mut refusals = Vec::new();
    for n in 0..attempt_limit() {
        let (report, _) = generator
            .styled_attempt_with_trace(seed, n, on_options())
            .expect("generator error");
        match report.map {
            Some(map) => {
                let (frame, fans, site) = phases(seed, n);
                let d = DEFAULT_DEFINITION;
                let room = expected_fan(&site, room_fan(&d), &d);
                return Ok(Landed {
                    seed,
                    attempt: n,
                    refusals,
                    map,
                    frame,
                    fans,
                    room,
                });
            }
            None => refusals.push(format!(
                "{:?}",
                report.refusal.expect("a refused attempt says why")
            )),
        }
    }
    Err((seed, refusals))
}

/// Seeds 1-128 with the option on, once per test binary.
fn run() -> &'static Run {
    static RUN: OnceLock<Run> = OnceLock::new();
    RUN.get_or_init(|| {
        let fixed = coliseum::fixed_layout();
        let generator = pipeline::Pipeline::new(&fixed);
        let mut run = Run {
            kept: Vec::new(),
            lost: Vec::new(),
        };
        for seed in seeds() {
            match land(&generator, seed) {
                Ok(l) => run.kept.push(l),
                Err(lost) => run.lost.push(lost),
            }
        }
        run
    })
}

fn maps() -> &'static Vec<Landed> {
    &run().kept
}

/// Seeds 1-32 with the option off (today's generator), once per test binary.
fn off_maps() -> &'static Vec<(i64, GameMapData)> {
    static MAPS: OnceLock<Vec<(i64, GameMapData)>> = OnceLock::new();
    MAPS.get_or_init(|| {
        let fixed = coliseum::fixed_layout();
        let generator = pipeline::Pipeline::new(&fixed);
        share_seeds()
            .map(|seed| {
                let m = generator
                    .certified_styled(seed, off_options())
                    .expect("generator error")
                    .unwrap_or_else(|| panic!("seed {seed}: no option-off map"));
                (seed, m)
            })
            .collect()
    })
}

// --------------------------------------------------------------------------------------------------------------------
// Geometry.
// --------------------------------------------------------------------------------------------------------------------

fn at(m: &GameMapData, c: Cell) -> Option<usize> {
    (c.0 >= 0 && c.1 >= 0 && c.0 < m.w as i32 && c.1 < m.h as i32)
        .then(|| (c.1 * m.w as i32 + c.0) as usize)
}

fn cell(m: &GameMapData, i: usize) -> Cell {
    ((i as i32) % m.w as i32, (i as i32) / m.w as i32)
}

fn point(c: Cell) -> (f64, f64) {
    (f64::from(c.0) + 0.5, f64::from(c.1) + 0.5)
}

fn cheb(a: Cell, b: Cell) -> i32 {
    (a.0 - b.0).abs().max((a.1 - b.1).abs())
}

/// Chebyshev distance between two tile sets (`i32::MAX` when either is empty).
fn cheb_sets(a: &[Cell], b: &[Cell]) -> i32 {
    a.iter()
        .flat_map(|&p| b.iter().map(move |&q| cheb(p, q)))
        .min()
        .unwrap_or(i32::MAX)
}

fn is_floor(m: &GameMapData, c: Cell) -> bool {
    at(m, c).is_some_and(|i| m.tiles[i] == 0)
}

fn is_dry(m: &GameMapData, c: Cell) -> bool {
    at(m, c).is_some_and(|i| m.tiles[i] == 0 && m.terrain[i] != ice())
}

fn cave(m: &GameMapData) -> Option<usize> {
    m.features.iter().position(|f| f.kind == FeatureKind::Cave)
}

/// Unowned Frost ice (file comment).
fn unowned_ice(m: &GameMapData) -> BTreeSet<Cell> {
    let cave = cave(m);
    (0..m.tiles.len())
        .filter(|&i| {
            m.region_grid[i] == Zone::Frost.code()
                && m.tiles[i] == 0
                && m.terrain[i] == ice()
                && cave.is_none_or(|k| usize::from(m.feature_grid[i]) != k + 1)
        })
        .map(|i| cell(m, i))
        .collect()
}

/// Groups of `cells` whose members link when at most `reach` apart (Chebyshev), in order of their first cell (row
/// order); `reach` 1 with `four` set gives 4-connected bodies.
fn groups(cells: &BTreeSet<Cell>, reach: i32, four: bool) -> Vec<Vec<Cell>> {
    let mut ordered: Vec<Cell> = cells.iter().copied().collect();
    ordered.sort_by_key(|c| (c.1, c.0));
    let mut seen = BTreeSet::new();
    let mut out = Vec::new();
    for start in ordered {
        if !seen.insert(start) {
            continue;
        }
        let mut group = vec![start];
        let mut stack = vec![start];
        while let Some((x, y)) = stack.pop() {
            for dy in -reach..=reach {
                for dx in -reach..=reach {
                    if (dx, dy) == (0, 0) || (four && dx != 0 && dy != 0) {
                        continue;
                    }
                    let n = (x + dx, y + dy);
                    if cells.contains(&n) && seen.insert(n) {
                        group.push(n);
                        stack.push(n);
                    }
                }
            }
        }
        out.push(group);
    }
    out
}

/// A map's lakes as the file comment defines them.
struct Lakes {
    mouth: Vec<Cell>,
    /// M, the unit vector toward C, and |C - M|.
    origin: (f64, f64),
    unit: (f64, f64),
    reach: f64,
    lake: Vec<Cell>,
    others: Vec<Vec<Cell>>,
    all_ice: Vec<Cell>,
}

impl Lakes {
    fn along(&self, c: Cell) -> f64 {
        self.along_pt(point(c))
    }
    fn along_pt(&self, p: (f64, f64)) -> f64 {
        (p.0 - self.origin.0) * self.unit.0 + (p.1 - self.origin.1) * self.unit.1
    }
    fn off(&self, p: (f64, f64)) -> f64 {
        ((p.0 - self.origin.0) * self.unit.1 - (p.1 - self.origin.1) * self.unit.0).abs()
    }
    fn centroid(&self) -> Option<(f64, f64)> {
        if self.lake.is_empty() {
            return None;
        }
        let n = self.lake.len() as f64;
        Some((
            self.lake.iter().map(|&c| point(c).0).sum::<f64>() / n,
            self.lake.iter().map(|&c| point(c).1).sum::<f64>() / n,
        ))
    }
}

fn lakes(m: &GameMapData, f: &LandmarkFrame) -> Lakes {
    let mouth: Vec<Cell> = f.anchor.entrance.iter().map(|p| (p.x, p.y)).collect();
    assert!(!mouth.is_empty(), "the Frost frame has no mouth");
    let n = mouth.len() as f64;
    let origin = (
        mouth.iter().map(|&c| point(c).0).sum::<f64>() / n,
        mouth.iter().map(|&c| point(c).1).sum::<f64>() / n,
    );
    let col = point((
        (m.coliseum.centre.x / tile_size()).floor() as i32,
        (m.coliseum.centre.y / tile_size()).floor() as i32,
    ));
    let (dx, dy) = (col.0 - origin.0, col.1 - origin.1);
    let reach = (dx * dx + dy * dy).sqrt();
    let unit = (dx / reach, dy / reach);
    let ice = unowned_ice(m);
    let bodies = groups(&ice, 1, true);
    let lake = bodies
        .iter()
        .enumerate()
        .max_by_key(|(k, b)| (b.len(), std::cmp::Reverse(*k)))
        .map(|(_, b)| b.clone())
        .unwrap_or_default();
    let in_lake: BTreeSet<Cell> = lake.iter().copied().collect();
    let rest: BTreeSet<Cell> = ice.difference(&in_lake).copied().collect();
    let others = groups(&rest, rule().spacing, false);
    Lakes {
        mouth,
        origin,
        unit,
        reach,
        lake,
        others,
        all_ice: ice.into_iter().collect(),
    }
}

/// Fewest 4-connected floor steps from any of `from` (step 0) to any tile of `to`.
fn floor_steps(m: &GameMapData, from: &[Cell], to: &[Cell]) -> Option<usize> {
    let to: BTreeSet<Cell> = to.iter().copied().collect();
    let mut seen = vec![false; m.tiles.len()];
    let mut queue = VecDeque::new();
    for &s in from {
        if let Some(i) = at(m, s).filter(|&i| m.tiles[i] == 0) {
            seen[i] = true;
            queue.push_back((s, 0));
        }
    }
    while let Some((c, d)) = queue.pop_front() {
        if to.contains(&c) {
            return Some(d);
        }
        for (dx, dy) in [(1, 0), (-1, 0), (0, 1), (0, -1)] {
            let n = (c.0 + dx, c.1 + dy);
            if let Some(i) = at(m, n) {
                if !seen[i] && m.tiles[i] == 0 {
                    seen[i] = true;
                    queue.push_back((n, d + 1));
                }
            }
        }
    }
    None
}

// --------------------------------------------------------------------------------------------------------------------
// The targets, each as a finding (`Err`) or none.
// --------------------------------------------------------------------------------------------------------------------

fn size_rule(l: &Lakes) -> Result<(), String> {
    let (lo, hi) = rule().size;
    if (lo..=hi).contains(&l.lake.len()) {
        Ok(())
    } else {
        Err(format!(
            "mouth lake {} tiles (want {lo}-{hi})",
            l.lake.len()
        ))
    }
}

/// The counted line target: the centroid at most `axis_offset` off the line.
fn near_line(l: &Lakes) -> Result<(), String> {
    let Some(c) = l.centroid() else {
        return Err("no mouth lake".into());
    };
    let off = l.off(c);
    if off <= f64::from(rule().axis_offset) {
        Ok(())
    } else {
        Err(format!(
            "mouth lake centroid {off:.1} tiles off the line (allowed {})",
            rule().axis_offset
        ))
    }
}

/// The whole line part of the mouth-lake target: near the line, between M and C along it, crossed by it.
fn line_rule(l: &Lakes) -> Result<(), String> {
    near_line(l)?;
    let c = l.centroid().expect("near_line found a lake");
    let along = l.along_pt(c);
    if !(along > 0.0 && along < l.reach) {
        return Err(format!(
            "mouth lake centroid {along:.1} along the line, not between the mouth and the coliseum ({:.1})",
            l.reach
        ));
    }
    let crossed = l
        .lake
        .iter()
        .any(|&t| l.along(t) > 0.0 && l.off(point(t)) <= 0.5);
    if !crossed {
        return Err("the line from the mouth to the coliseum does not cross the mouth lake".into());
    }
    Ok(())
}

fn gap_rule(m: &GameMapData, l: &Lakes) -> Result<(), String> {
    let want = rule().mouth_gap + 1;
    let c = cheb_sets(&l.mouth, &l.lake);
    let steps = floor_steps(m, &l.mouth, &l.lake);
    let any = cheb_sets(&l.mouth, &l.all_ice);
    if c != want || steps != Some(want as usize) || any < want {
        return Err(format!(
            "mouth to lake: Chebyshev {c}, floor steps {steps:?}, nearest ice of any lake {any} (want {want}, {want}, \
             at least {want})"
        ));
    }
    Ok(())
}

fn mouth_lake_rule(m: &GameMapData, l: &Lakes) -> Result<(), String> {
    size_rule(l)?;
    line_rule(l)?;
    gap_rule(m, l)
}

/// The dry way (file comment), `width` wide (odd).
fn has_dry_way(m: &GameMapData, l: &Lakes, width: i32) -> bool {
    if l.lake.is_empty() {
        return false;
    }
    let h = (width - 1) / 2;
    let far = l.lake.iter().map(|&c| l.along(c)).fold(f64::MIN, f64::max);
    let mouth: BTreeSet<Cell> = l.mouth.iter().copied().collect();
    let block_dry = |c: Cell| (-h..=h).all(|dy| (-h..=h).all(|dx| is_dry(m, (c.0 + dx, c.1 + dy))));
    let ends = |c: Cell| {
        (-h..=h).any(|dy| {
            (-h..=h).any(|dx| {
                let b = (c.0 + dx, c.1 + dy);
                mouth.contains(&b)
                    || [(1, 0), (-1, 0), (0, 1), (0, -1)]
                        .iter()
                        .any(|(ex, ey)| mouth.contains(&(b.0 + ex, b.1 + ey)))
            })
        })
    };
    let mut seen = vec![false; m.tiles.len()];
    let mut queue = VecDeque::new();
    for (i, s) in seen.iter_mut().enumerate() {
        let c = cell(m, i);
        if l.along(c) >= far + 2.0 && block_dry(c) {
            *s = true;
            queue.push_back(c);
        }
    }
    while let Some(c) = queue.pop_front() {
        if ends(c) {
            return true;
        }
        for (dx, dy) in [(1, 0), (-1, 0), (0, 1), (0, -1)] {
            let n = (c.0 + dx, c.1 + dy);
            if let Some(i) = at(m, n) {
                if !seen[i] && block_dry(n) {
                    seen[i] = true;
                    queue.push_back(n);
                }
            }
        }
    }
    false
}

fn way_rule(m: &GameMapData, l: &Lakes) -> Result<(), String> {
    if has_dry_way(m, l, rule().way_width) {
        Ok(())
    } else {
        Err(format!(
            "no dry way {} wide from beyond the lake to the mouth",
            rule().way_width
        ))
    }
}

fn second_rule(l: &Lakes) -> Result<(), String> {
    let sizes: Vec<usize> = l.others.iter().map(Vec::len).collect();
    if sizes.len() != 1 {
        return Err(format!(
            "{} lakes besides the mouth lake (sizes {sizes:?})",
            sizes.len()
        ));
    }
    let (lo, hi) = rule().second;
    let two = &l.others[0];
    if !(lo..=hi).contains(&two.len()) || two.len() >= l.lake.len() {
        return Err(format!(
            "second lake {} tiles (want {lo}-{hi} and fewer than the mouth lake's {})",
            two.len(),
            l.lake.len()
        ));
    }
    let apart = cheb_sets(two, &l.lake);
    if apart <= rule().spacing {
        return Err(format!(
            "second lake {apart} (Chebyshev) from the mouth lake (want more than {})",
            rule().spacing
        ));
    }
    Ok(())
}

fn whole_rule(m: &GameMapData, l: &Lakes) -> Result<(), String> {
    mouth_lake_rule(m, l)?;
    way_rule(m, l)?;
    second_rule(l)
}

fn median(v: &mut [f64]) -> f64 {
    v.sort_by(f64::total_cmp);
    let n = v.len();
    if n % 2 == 1 {
        v[n / 2]
    } else {
        (v[n / 2 - 1] + v[n / 2]) / 2.0
    }
}

/// The kept maps of seeds 1-128 that meet `target`, and the first failures of the others.
fn count(target: impl Fn(&Landed, &Lakes) -> Result<(), String>) -> (usize, Vec<String>) {
    let mut met = 0;
    let mut misses = Vec::new();
    for r in maps() {
        match target(r, &lakes(&r.map, &r.frame)) {
            Ok(()) => met += 1,
            Err(e) => misses.push(format!("seed {} (attempt {}): {e}", r.seed, r.attempt)),
        }
    }
    (met, misses)
}

/// A counted floor (file comment): `met` maps of `n` against `floor`, with the owner's and the measured values named.
fn floor_pin(what: &str, met: usize, floor: usize, measured: &str, misses: &[String]) {
    let n = maps().len();
    eprintln!("{what}: {met} of {n} kept maps of seeds 1-128 ({measured})");
    assert!(
        met >= floor,
        "{what}: {met} of {n} kept maps of seeds 1-128, under the floor {floor} ({measured}). The parts of the owner's \
         Frost lake rule are targets by his decision of 2026-11-14 (built for on every map, measured, never a refusal); \
         this floor was set on the lane's judgment, a little under the decided prototype's value and above today's \
         game. It is the last value so set: a packet whose maps fall under it stops, and the measurement goes to the \
         owner. Do not lower it. First misses: {:#?}",
        &misses[..misses.len().min(12)]
    );
}

// --------------------------------------------------------------------------------------------------------------------
// The room: table and Prefix helpers.
// --------------------------------------------------------------------------------------------------------------------

/// The cave's room as the definition gives it.
fn room_fan(d: &Definition) -> Fan {
    Fan {
        depth: d.cave_fan_depth,
        side: d.cave_fan_side,
    }
}

fn site(sites: &[ReservedSite], zone: Zone) -> &ReservedSite {
    let r = sites.iter().find(|r| r.zone == zone);
    assert!(r.is_some(), "no {zone:?} site");
    r.unwrap()
}

fn entry(t: &mut [Spec], zone: Zone) -> &mut Spec {
    let e = t.iter_mut().find(|s| s.zone == zone);
    assert!(e.is_some(), "no {zone:?} entry in specs()");
    e.unwrap()
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

/// The fan `f` of site `r` (ADR 0028 decision 10's geometry, as `tests/mapgen_signature_site_rules.rs` draws it).
fn expected_fan(r: &ReservedSite, f: Fan, d: &Definition) -> Rect {
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

fn in_rect(r: Rect, i: usize) -> bool {
    let (x, y) = (i % W, i / W);
    x >= r.0 && x <= r.2 && y >= r.1 && y <= r.3
}

fn in_site(r: TileRect, i: usize) -> bool {
    let (x, y) = ((i % W) as i32, (i / W) as i32);
    x >= r.x && x < r.x + r.w as i32 && y >= r.y && y < r.y + r.h as i32
}

/// The first prefix of `seed` (attempts 0-7) with `context`.
fn first_prefix(context: &attempt::AttemptContext<'_>, seed: i64) -> Option<attempt::AttemptFrame> {
    (0..8).find_map(|n| {
        attempt::begin_attempt_signature(context, seed, n, true).expect("prefix input")
    })
}

/// Findings on one prefix: a camp halo of another site's or zone's camp, or a building of the legacy landmark stage,
/// inside a recorded fan.
fn fan_intrusions(fixed: &coliseum::FixedLayout, frame: &attempt::AttemptFrame) -> Vec<String> {
    let zones = ZoneContext::new(fixed);
    let halo = CampContext::new(&zones).halo_offsets().to_vec();
    let s = &frame.state;
    let mut out = Vec::new();
    for &fan in &s.fans {
        for c in &s.layout.camps {
            // A site's own camps are not tested against its own fan (`fan_holds_a_site_camp`); they stand inside their
            // site, behind it.
            if s.reserved.iter().any(|r| in_site(r.rect, c.site)) {
                continue;
            }
            let inside = halo
                .iter()
                .map(|&o| c.site as i64 + i64::from(o))
                .filter(|&t| t >= 0 && (t as usize) < W * H)
                .any(|t| in_rect(fan, t as usize));
            if inside {
                out.push(format!(
                    "seed {} attempt {}: the halo of the camp at {} reaches into the fan {fan:?}",
                    s.seed, s.attempt, c.site
                ));
            }
        }
    }
    if let Some(st) = stage::StageContext::new(fixed)
        .apply(frame, &recipes::Recipes::default())
        .expect("stage input")
    {
        for b in &st.buildings {
            let r = (b.x, b.y, b.x + b.w - 1, b.y + b.h - 1);
            for &(x0, y0, x1, y1) in &s.fans {
                let apart =
                    r.2 < x0 as i32 || r.0 > x1 as i32 || r.3 < y0 as i32 || r.1 > y1 as i32;
                if !apart {
                    out.push(format!(
                        "seed {} attempt {}: legacy building {b:?} in the fan {:?}",
                        s.seed,
                        s.attempt,
                        (x0, y0, x1, y1)
                    ));
                }
            }
        }
    }
    out
}

// --------------------------------------------------------------------------------------------------------------------
// Hard: every map.
// --------------------------------------------------------------------------------------------------------------------

/// Every seed of 1-128 keeps a map, the worst at attempt 12 or sooner (a landmark or zone rule never loses a seed); every
/// map has a lake at the mouth (the build falls back to today's growth when the built-first lake comes out empty: the
/// prototype left 4 maps without) and passes `lake-at-mouth`; no map has stray ice (0 in every measured variant; today's
/// game 56 of 128).
#[test]
fn every_seed_keeps_a_map_with_a_lake_at_the_mouth_and_no_stray_ice() {
    let r = run();
    assert!(
        r.lost.is_empty(),
        "seeds with no map in {} attempts with the option on: {:#?}",
        attempt_limit(),
        r.lost
    );
    let worst = maps().iter().max_by_key(|l| (l.attempt, l.seed)).unwrap();
    assert!(
        worst.attempt <= worst_attempt_allowed(),
        "seed {}: kept at attempt {} (allowed {}; the stack's bound); refused before: {:?}",
        worst.seed,
        worst.attempt,
        worst_attempt_allowed(),
        worst.refusals
    );
    let lake_at_mouth = guarantees(Zone::Frost)
        .iter()
        .find(|g| g.rule == "lake-at-mouth")
        .copied()
        .expect("lake-at-mouth registered");
    let mut findings = Vec::new();
    for l in maps() {
        let lk = lakes(&l.map, &l.frame);
        if lk.lake.is_empty() {
            findings.push(format!(
                "seed {} (attempt {}): no lake at the mouth",
                l.seed, l.attempt
            ));
        }
        if !(lake_at_mouth.check)(&l.map, &l.frame) {
            findings.push(format!(
                "seed {} (attempt {}): lake-at-mouth fails",
                l.seed, l.attempt
            ));
        }
        if lk.others.len() > 1 {
            findings.push(format!(
                "seed {} (attempt {}): stray ice: {} lakes besides the mouth lake (sizes {:?})",
                l.seed,
                l.attempt,
                lk.others.len(),
                lk.others.iter().map(Vec::len).collect::<Vec<_>>()
            ));
        }
    }
    assert!(
        findings.is_empty(),
        "{} findings on the kept maps of seeds 1-128:\n{}",
        findings.len(),
        findings
            .iter()
            .take(40)
            .cloned()
            .collect::<Vec<_>>()
            .join("\n")
    );
}

/// Frost's guarantee list stays today's one rule (`lake-at-mouth`; owner decision 2026-11-14: the parts of the lake rule
/// are targets, never a refusal), and `style_rime::lake_rule` carries this oracle's numbers. A kept map doctored to
/// break a target (the mouth lake dried from its far side to 200 tiles; one ice tile 2 tiles from the mouth; posts that
/// leave no 3 x 3 dry square beyond the mouth; the second lake dried) is refused by no Frost rule, while the target's
/// own check (`style_rime::*_holds`) held before and fails after. At least 8 maps for each target.
#[test]
fn breaking_a_target_refuses_no_map() {
    let names: Vec<&str> = guarantees(Zone::Frost).iter().map(|g| g.rule).collect();
    assert_eq!(
        names,
        vec!["lake-at-mouth"],
        "the Frost guarantees, in run order"
    );
    assert_eq!(lake_rule(), rule(), "style_rime::lake_rule");
    let refused_by = |m: &GameMapData, f: &LandmarkFrame| -> Vec<&'static str> {
        guarantees(Zone::Frost)
            .iter()
            .filter(|g| !(g.check)(m, f))
            .map(|g| g.rule)
            .collect()
    };
    let (mut small, mut wet_gap, mut posts, mut dried) = (0, 0, 0, 0);
    for r in maps() {
        let l = lakes(&r.map, &r.frame);
        let (m0, f) = (&r.map, &r.frame);
        let seed = r.seed;
        if mouth_lake_rule(m0, &l).is_ok() {
            assert!(
                mouth_lake_holds(m0, f),
                "seed {seed}: mouth_lake_holds says no on a map where the mouth-lake target holds"
            );
            // Too small: dry the lake's tiles farthest along the line until 200 remain.
            let mut m = m0.clone();
            let mut far = l.lake.clone();
            far.sort_by(|&a, &b| l.along(b).total_cmp(&l.along(a)).then(a.cmp(&b)));
            for &c in far.iter().take(l.lake.len().saturating_sub(200)) {
                let i = at(&m, c).unwrap();
                m.terrain[i] = 0;
            }
            assert!(
                !mouth_lake_holds(&m, f),
                "seed {seed}: a 200-tile lake and mouth_lake_holds says yes"
            );
            assert_eq!(
                refused_by(&m, f),
                Vec::<&str>::new(),
                "seed {seed}: a 200-tile lake is refused"
            );
            small += 1;
            // Ice in the gap: the first floor tile in row order at Chebyshev 2 from a mouth tile, Frost, not the cave's.
            let cave = cave(m0);
            let near = (0..m0.tiles.len()).map(|i| cell(m0, i)).find(|&c| {
                let i = at(m0, c).unwrap();
                is_floor(m0, c)
                    && cheb_sets(&[c], &l.mouth) == 2
                    && cave.is_none_or(|k| usize::from(m0.feature_grid[i]) != k + 1)
                    && m0.region_grid[i] == Zone::Frost.code()
            });
            if let Some(near) = near {
                let mut m = m0.clone();
                let i = at(&m, near).unwrap();
                m.terrain[i] = ice();
                assert!(
                    !mouth_lake_holds(&m, f),
                    "seed {seed}: ice at {near:?} and mouth_lake_holds says yes"
                );
                assert_eq!(
                    refused_by(&m, f),
                    Vec::<&str>::new(),
                    "seed {seed}: ice in the gap is refused"
                );
                wet_gap += 1;
            }
        }
        if way_rule(m0, &l).is_ok() {
            assert!(
                dry_way_holds(m0, f),
                "seed {seed}: dry_way_holds says no on a map where the dry way holds"
            );
            // Posts: wall on every dry floor tile with even x and even y more than mouth_gap + 1 from the mouth.
            let mut m = m0.clone();
            for i in 0..m.tiles.len() {
                let c = cell(&m, i);
                if c.0 % 2 == 0
                    && c.1 % 2 == 0
                    && m.tiles[i] == 0
                    && m.terrain[i] != ice()
                    && cheb_sets(&[c], &l.mouth) > rule().mouth_gap + 1
                {
                    m.tiles[i] = 1;
                }
            }
            assert!(
                !dry_way_holds(&m, f),
                "seed {seed}: posts everywhere and dry_way_holds says yes"
            );
            assert_eq!(
                refused_by(&m, f),
                Vec::<&str>::new(),
                "seed {seed}: posts are refused"
            );
            posts += 1;
        }
        if second_rule(&l).is_ok() {
            assert!(
                second_lake_holds(m0, f),
                "seed {seed}: second_lake_holds says no on a map where the second lake holds"
            );
            let mut m = m0.clone();
            for c in l.others.iter().flatten() {
                let i = at(&m, *c).unwrap();
                m.terrain[i] = 0;
            }
            assert!(
                !second_lake_holds(&m, f),
                "seed {seed}: no second lake and second_lake_holds says yes"
            );
            assert_eq!(
                refused_by(&m, f),
                Vec::<&str>::new(),
                "seed {seed}: a dried second lake is refused"
            );
            dried += 1;
        }
    }
    for (what, n) in [
        ("a 200-tile lake", small),
        ("ice in the gap", wet_gap),
        ("posts", posts),
        ("a dried second lake", dried),
    ] {
        assert!(n >= 8, "{what}: only {n} maps doctored (want at least 8)");
    }
}

/// The three target checks a report reads (`style_rime::mouth_lake_holds`, `dry_way_holds`, `second_lake_holds`) give
/// this oracle's verdict on every kept map of seeds 1-128 (definitions in the file comment).
#[test]
fn the_target_checks_agree_with_this_oracle() {
    let mut findings = Vec::new();
    for r in maps() {
        let l = lakes(&r.map, &r.frame);
        for (what, oracle, lib) in [
            (
                "mouth-lake",
                mouth_lake_rule(&r.map, &l),
                mouth_lake_holds(&r.map, &r.frame),
            ),
            (
                "dry-way",
                way_rule(&r.map, &l),
                dry_way_holds(&r.map, &r.frame),
            ),
            (
                "second-lake",
                second_rule(&l),
                second_lake_holds(&r.map, &r.frame),
            ),
        ] {
            if oracle.is_ok() != lib {
                findings.push(format!(
                    "seed {} (attempt {}): {what}: oracle {oracle:?}, style_rime says {lib}",
                    r.seed, r.attempt
                ));
            }
        }
    }
    assert!(
        findings.is_empty(),
        "{} disagreements:\n{}",
        findings.len(),
        findings
            .iter()
            .take(40)
            .cloned()
            .collect::<Vec<_>>()
            .join("\n")
    );
}

// --------------------------------------------------------------------------------------------------------------------
// Hard: the room.
// --------------------------------------------------------------------------------------------------------------------

/// The cave's table entry carries the room: a soft fan (`FanHold::Soft`) with the definition's numbers (24 deep, 0 to
/// the side: owner decision 2026-11-14), on the default definition and on one with those numbers moved; the volcano's
/// fan stays hard with its own numbers; no other entry has a fan.
#[test]
fn the_caves_entry_carries_a_soft_room_and_the_volcanos_fan_stays_hard() {
    assert_eq!(
        (
            DEFAULT_DEFINITION.cave_fan_depth,
            DEFAULT_DEFINITION.cave_fan_side
        ),
        cave_room(),
        "the definition's room"
    );
    let moved = Definition {
        cave_fan_depth: 17,
        cave_fan_side: 2,
        ..DEFAULT_DEFINITION
    };
    for d in [DEFAULT_DEFINITION, moved] {
        let t = specs(&d);
        let got: Vec<(Zone, Option<Fan>, Option<FanHold>)> = t
            .iter()
            .map(|s| (s.zone, s.fan, s.fan.map(|_| s.fan_hold)))
            .collect();
        let want = vec![
            (
                Zone::Fire,
                Some(Fan {
                    depth: d.fan_depth,
                    side: d.fan_side,
                }),
                Some(FanHold::Hard),
            ),
            (Zone::Sentenced, None, None),
            (Zone::Plague, None, None),
            (Zone::Frost, Some(room_fan(&d)), Some(FanHold::Soft)),
            (Zone::Holy, None, None),
        ];
        assert_eq!(
            got, want,
            "(zone, fan, fan_hold) with the room ({}, {})",
            d.cave_fan_depth, d.cave_fan_side
        );
    }
}

/// Only a soft fan is given up. The cave's room made as large as the map (a doctored soft entry, `with_signature_table`):
/// every round's camps fail with it, are placed once more without it, and the prefix records no Frost fan (only the
/// volcano's); seeds 1-8 keep at least 4 prefixes. The same fan marked hard is never given up: no prefix on seeds 1-8.
/// The volcano's fan made as large (hard, the default) is never given up either.
#[test]
fn only_a_soft_fan_is_given_up_and_then_it_is_not_recorded() {
    let fixed = coliseum::fixed_layout();
    let d = DEFAULT_DEFINITION;
    let huge = Fan {
        depth: 200,
        side: 200,
    };
    let with = |zone: Zone, hold: FanHold| {
        let mut t = specs(&d);
        let e = entry(&mut t, zone);
        e.fan = Some(huge);
        e.fan_hold = hold;
        attempt::AttemptContext::new(&fixed).with_signature_table(&d, t)
    };
    let soft = with(Zone::Frost, FanHold::Soft);
    let mut kept = 0;
    for seed in 1..=8 {
        let Some(frame) = first_prefix(&soft, seed) else {
            continue;
        };
        let s = &frame.state;
        let fire = expected_fan(
            site(&s.reserved, Zone::Fire),
            Fan {
                depth: d.fan_depth,
                side: d.fan_side,
            },
            &d,
        );
        assert_eq!(
            s.fans,
            vec![fire],
            "seed {seed} attempt {}: the recorded fans with the room given up (the volcano's only)",
            s.attempt
        );
        let intrusions = fan_intrusions(&fixed, &frame);
        assert!(intrusions.is_empty(), "{intrusions:#?}");
        kept += 1;
    }
    assert!(
        kept >= 4,
        "a soft room as large as the map: only {kept} prefixes on seeds 1-8 (want at least 4): the room was not given up \
         when the round's camps could not be placed with it"
    );
    for (what, context) in [
        ("the room marked hard", with(Zone::Frost, FanHold::Hard)),
        ("the volcano's fan", with(Zone::Fire, FanHold::Hard)),
    ] {
        for seed in 1..=8 {
            let got = first_prefix(&context, seed).map(|f| f.state.attempt);
            assert_eq!(
                got, None,
                "seed {seed}: {what} as large as the map was given up"
            );
        }
    }
}

/// On the default table, the cave's room is kept for the attempt (recorded after the volcano's fan) on most kept maps of
/// seeds 1-128 and given up on some; every recorded fan keeps every other camp's halo and every building of the legacy
/// landmark stage out (prototype: kept on 114, given up on 14).
#[test]
fn a_kept_room_keeps_camp_halos_and_legacy_buildings_out() {
    let fixed = coliseum::fixed_layout();
    let context = attempt::AttemptContext::new(&fixed);
    let d = DEFAULT_DEFINITION;
    let (mut kept, mut given_up) = (0, 0);
    let mut findings = Vec::new();
    for l in maps() {
        let frame = attempt::begin_attempt_signature(&context, l.seed, l.attempt, true)
            .expect("prefix input")
            .expect("the kept attempt passed the Prefix");
        let s = &frame.state;
        let fire = expected_fan(
            site(&s.reserved, Zone::Fire),
            Fan {
                depth: d.fan_depth,
                side: d.fan_side,
            },
            &d,
        );
        if s.fans == vec![fire, l.room] {
            kept += 1;
        } else if s.fans == vec![fire] {
            given_up += 1;
        } else {
            findings.push(format!(
                "seed {} attempt {}: fans {:?} (want the volcano's {fire:?}, then the room {:?} or nothing)",
                l.seed, l.attempt, s.fans, l.room
            ));
        }
        findings.extend(fan_intrusions(&fixed, &frame));
    }
    assert!(
        findings.is_empty(),
        "{} findings:\n{}",
        findings.len(),
        findings
            .iter()
            .take(40)
            .cloned()
            .collect::<Vec<_>>()
            .join("\n")
    );
    eprintln!("the room kept on {kept} kept maps, given up on {given_up}");
    assert!(
        kept >= room_kept_floor() && given_up >= 1,
        "the room kept on {kept} kept maps of seeds 1-128 (want at least {}), given up on {given_up} (want at least 1; \
         prototype 114 and 14)",
        room_kept_floor()
    );
    assert_eq!(
        maps().iter().filter(|l| l.room_kept()).count(),
        kept,
        "the kept maps' recorded rooms"
    );
}

// --------------------------------------------------------------------------------------------------------------------
// Targets: counted floors.
// --------------------------------------------------------------------------------------------------------------------

/// The mouth lake is built to about 400 tiles: 340-460 on at least `size_floor` maps, median at least `median_floor`.
#[test]
fn the_mouth_lake_holds_about_400_tiles_on_most_maps() {
    let (met, misses) = count(|_, l| size_rule(l));
    floor_pin(
        "mouth lakes of 340-460 tiles",
        met,
        size_floor(),
        "decided prototype 46, today's game 30",
        &misses,
    );
    let mut sizes: Vec<f64> = maps()
        .iter()
        .map(|r| lakes(&r.map, &r.frame).lake.len() as f64)
        .collect();
    let med = median(&mut sizes);
    eprintln!("median mouth lake {med}");
    assert!(
        med >= median_floor(),
        "median mouth lake {med} tiles on seeds 1-128, under the floor {} (decided prototype 305.5, today's game 254). \
         Set on the lane's judgment; the last value so set: a packet under it stops and goes to the owner.",
        median_floor()
    );
}

/// Exactly 3 dry tiles lie between the mouth and the shore, and no ice nearer, on at least `gap_floor` maps.
#[test]
fn three_dry_tiles_lie_between_mouth_and_shore_on_most_maps() {
    let (met, misses) = count(|r, l| gap_rule(&r.map, l));
    floor_pin(
        "exactly 3 dry tiles from the mouth to the shore",
        met,
        gap_floor(),
        "decided prototype 111, today's game 0",
        &misses,
    );
}

/// The mouth lake's centroid lies within 4 tiles of the line from the mouth to the coliseum on at least `line_floor`
/// maps.
#[test]
fn the_mouth_lake_stands_on_the_line_on_most_maps() {
    let (met, misses) = count(|_, l| near_line(l));
    floor_pin(
        "the mouth lake's centroid within 4 tiles of the line",
        met,
        line_floor(),
        "decided prototype 90, today's game 81",
        &misses,
    );
}

/// A dry way 3 wide leads from beyond the lake round it to the mouth on at least `way_floor` maps.
#[test]
fn a_dry_way_three_wide_leads_to_the_mouth_on_most_maps() {
    let (met, misses) = count(|r, l| way_rule(&r.map, l));
    floor_pin(
        "a dry way 3 wide",
        met,
        way_floor(),
        "decided prototype 124, today's game 48",
        &misses,
    );
}

/// One smaller second lake (60-200 tiles, apart from the mouth lake) stands elsewhere on at least `second_floor` maps.
#[test]
fn one_smaller_second_lake_stands_apart_on_most_maps() {
    let (met, misses) = count(|_, l| second_rule(l));
    floor_pin(
        "one smaller second lake apart",
        met,
        second_floor(),
        "decided prototype 104, today's game 37",
        &misses,
    );
}

/// Every part of the rule at once (the mouth-lake target, the dry way, the second lake) on at least `whole_floor` maps.
#[test]
fn the_whole_lake_rule_holds_on_some_maps() {
    let (met, misses) = count(|r, l| whole_rule(&r.map, l));
    floor_pin(
        "every part of the lake rule at once",
        met,
        whole_floor(),
        "decided prototype 25, today's game 0",
        &misses,
    );
}

// --------------------------------------------------------------------------------------------------------------------
// Every zone: the amounts.
// --------------------------------------------------------------------------------------------------------------------

/// The owner's amounts rule: about today's share of the zone's free ground as ice, within about a fifth, measured after
/// the site is taken out: the median over seeds 1-32 of (share option on) / (share option off) lies in 0.8-1.2. And
/// spires and headlands keep about today's numbers (at least 25 and 72 over seeds 1-32).
#[test]
fn the_ice_share_stays_within_a_fifth_of_option_off() {
    let mut ratios = Vec::new();
    let mut lines = Vec::new();
    let on: Vec<&Landed> = maps()
        .iter()
        .filter(|r| share_seeds().contains(&r.seed))
        .collect();
    assert_eq!(
        on.len(),
        off_maps().len(),
        "seeds 1-32 kept with the option on"
    );
    for (r, (seed, off)) in on.iter().zip(off_maps()) {
        assert_eq!(r.seed, *seed);
        let a = &r.frame.anchor;
        let (m, frost) = (&r.map, Zone::Frost.code());
        let outside = |c: Cell| band(a, c.0, c.1) != Band::Core;
        let free_on = (0..m.tiles.len())
            .filter(|&i| m.region_grid[i] == frost && m.tiles[i] == 0 && outside(cell(m, i)))
            .count();
        let ice_on = unowned_ice(m).into_iter().filter(|&c| outside(c)).count();
        let free_off = (0..off.tiles.len())
            .filter(|&i| off.region_grid[i] == frost && off.tiles[i] == 0)
            .count();
        let ice_off = (0..off.tiles.len())
            .filter(|&i| {
                off.region_grid[i] == frost && off.tiles[i] == 0 && off.terrain[i] == ice()
            })
            .count();
        let ratio = (ice_on as f64 / free_on as f64) / (ice_off as f64 / free_off as f64);
        lines.push(format!(
            "seed {seed}: on {ice_on}/{free_on}, off {ice_off}/{free_off}, ratio {ratio:.3}"
        ));
        ratios.push(ratio);
    }
    let med = median(&mut ratios);
    let (lo, hi) = share_window();
    eprintln!("median ice-share ratio {med:.3}");
    assert!(
        (lo..=hi).contains(&med),
        "median ice-share ratio {med:.3} over seeds 1-32 outside the owner's window [{lo}, {hi}] (\"about today's share, \
         within about a fifth\"; today's game 0.639, the decided prototype 0.810). This window is the owner's: a build \
         that lands outside it stops, and the measurement goes to the owner. Do not move it.\n{}",
        lines.join("\n")
    );
    let total = |k: FeatureKind| -> usize {
        on.iter()
            .map(|r| {
                r.map
                    .features
                    .iter()
                    .filter(|f| f.kind == k && f.zone == Zone::Frost)
                    .count()
            })
            .sum()
    };
    let (spires, headlands) = (total(FeatureKind::Spire), total(FeatureKind::Headland));
    let (s_min, h_min) = spire_headland_floor();
    eprintln!("spires {spires}, headlands {headlands} over seeds 1-32");
    assert!(
        spires >= s_min && headlands >= h_min,
        "over seeds 1-32: {spires} spires (at least {s_min}), {headlands} headlands (at least {h_min}); spires and \
         headlands keep today's rules (the owner's 'Every zone')"
    );
}
