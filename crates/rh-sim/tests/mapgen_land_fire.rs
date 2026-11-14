//! Oracle of P-LAND-FIRE-1 (brief `docs/backlog/packets/P-LAND-FIRE-1.md`): with `pipeline::Options::signature` on,
//! Fire craters keep clear of the volcano's three lava streams, every Fire crater stands on a Fire camp, a camp's crater
//! shrinks (never grows) to fit, about today's share of the zone's free ground is crater, and two end-of-attempt
//! guarantees are registered for Fire (`style_scorch::guarantees`). No legacy counterpart (DEV-020): legacy
//! (`packages/shared/src/zoneStyle/scorch.ts`) has no volcano; option off keeps it byte for byte (guarded by
//! `mapgen_signature_fire.rs::g8_option_off_maps_are_todays` and the option-off pins, not repeated here).
//!
//! The owner's direction (`docs/coordination/LAND_GENERATION_REQUIREMENTS.md` section 3): "Craters keep clear of all
//! three streams. Every Fire camp keeps its crater; size falls with distance and none is dropped."
//!
//! **The owner's decision of 2026-11-14** (decision 1 of three, answered "Yea to all 3"): "land the gentler rule now,
//! with craters guaranteed clear of lava, and record 'about 1 camp in 20 without a crater' as a measured gap." So:
//! * hard (an attempt that breaks one is refused): `crater-clear-of-lava` and `crater-on-camp`;
//! * NOT a guarantee: "every Fire camp keeps its crater". A camp's crater takes its band's option, then each smaller
//!   allowed size, skipping a size whose disc holds stream or pool lava and retrying a short rim as a bare ring; a camp
//!   goes without only when no allowed size fits; no attempt is refused for a missing crater and no seed loses its map.
//!   The owner's direction "every Fire camp keeps its crater" is therefore NOT delivered: the test that stood for it
//!   is replaced, by that decision, with a measured-gap pin (`known_gap_about_one_fire_camp_in_twenty_has_no_crater`).
//!
//! Every check is a property measured from the map and the Fire landmark frame of the accepted attempt (seeds 1-32,
//! option on, the server's options: no decor; `wide_*` and the gap pin also on seeds 33-128). Nothing pins an option-on
//! map, hash, attempt number or tile position.
//!
//! Definitions (tile units):
//! * Fire camp: a `map.camps` entry with zone Fire; its tile is (floor(x / 32), floor(y / 32)).
//! * its crater (as `style_scorch/craters.rs` makes it: one pen centred on the camp): the `Crater` feature of zone Fire
//!   whose anchor (`ax`, `ay`) is the camp's tile. Its radius is `p[1]` (quarter tiles); its tiles are the tiles the
//!   feature owns (`feature_grid`: rim walls and bowl floor); its disc is every tile (x, y) with
//!   16 * ((x - ax)^2 + (y - ay)^2) < p[1]^2 (strictly inside the ring's nominal radius).
//! * stream or pool lava: the tiles of the Fire frame's `cells` written LOW with the lava terrain
//!   (`DEFAULT_DEFINITION.lava`) that lie outside the frame anchor's footprint (the cone's box): the three streams and
//!   their pools, as the volcano wrote them (the cone's own lava lake is inside its box).
//! * clearance: `style_scorch::lava_clearance()` = 2, today's stream and pool apron (`craters.rs` `lava_apron`, the
//!   core band `bands[0]`; `SignatureContext::intact` refuses any styler tile within it). A crater keeps clear of a
//!   stream when no stream or pool lava lies in its disc and no crater tile lies within the clearance (Chebyshev) of it.
//! * a camp's radius options and band (as `mapgen_signature_fire_bands.rs`): R = clamp(floor(2 * distance to the nearest
//!   other Fire camp - radius_subtract), radius_min4, radius_max4 or radius_elite_max4), options R, R - 4, R - 8 keeping
//!   those >= radius_min4 (20); band of dc - ceil(R / 4), dc the Chebyshev distance from the camp tile to the anchor's
//!   footprint. The band's option: near the largest, mid the second (the smallest when there is one only), far the
//!   smallest; core any.
//! * free ground of the zone: Fire-region tiles that are floor or owned by a Fire crater, outside the site: the
//!   anchor's core band (footprint and apron) and the tiles within the clearance of stream or pool lava (option off
//!   there is no site: all of it). Crater share = Fire crater tiles / free ground.
use rh_sim::map::TILE_LOW;
use rh_sim::mapdata::{FeatureKind, GameMapData, Zone, ZoneAnchor};
use rh_sim::mapgen::landmarks::recipes::search_family::scorch;
use rh_sim::mapgen::landmarks::signature::frame::LandmarkFrame;
use rh_sim::mapgen::landmarks::signature::{band, Band, SignatureContext, DEFAULT_DEFINITION};
use rh_sim::mapgen::landmarks::{recipes, stage};
use rh_sim::mapgen::style_zones::{guarantees, Guarantee};
use rh_sim::mapgen::{attempt, coliseum, pipeline, style_scorch};
use std::sync::OnceLock;

type Cell = (i32, i32);

// ---------------------------------------------------------------------------------------------------------------------
// The numbers (functions, not consts: `tests/content.rs` reads consts as unregistered tunables).
// ---------------------------------------------------------------------------------------------------------------------

fn seeds() -> std::ops::RangeInclusive<i64> {
    1..=32
}

fn wide_seeds() -> std::ops::RangeInclusive<i64> {
    33..=128
}

/// The seeds the owner's measured gap was taken on (decision of 2026-11-14).
fn gap_seeds() -> std::ops::RangeInclusive<i64> {
    1..=64
}

/// The attempts `certified_styled` may use (`pipeline` rules, `certified_attempts`).
fn attempt_limit() -> u64 {
    16
}

/// The stack's attempt bounds over seeds 1-32 (P-MAPGEN-SIG-SCALE-4's provisional figures; measured 9 / 89 on the
/// stub and 9 / 94 on the decided rule).
fn worst_attempt_allowed() -> u64 {
    12
}

fn attempts_allowed() -> u64 {
    137
}

/// The most attempts over seeds 1-32 a Fire guarantee may refuse. Measured 0 on the decided rule (prototype V1): the
/// styler fits each crater (shrinking, skipping lava, bare ring) and never draws one off its camp or over lava, so the
/// guarantees are a net that should not catch; 2 leaves room for a rare tile the runtime moves.
fn fire_refusals_allowed() -> usize {
    2
}

/// The clearance between crater tiles and stream or pool lava: today's apron (header definition).
fn clearance() -> i32 {
    2
}

/// The smallest crater: today's `radius_min4` (quarter tiles; a ring 5 tiles out). No crater is drawn smaller; a camp
/// that cannot hold one this size goes without (owner decision 2026-11-14).
fn smallest_radius4() -> i32 {
    20
}

/// PROVISIONAL (oracle author's, for the owner): the least share of Fire craters over seeds 1-32 that take exactly
/// their band's option (stub 1.0; the decided rule: 3 of about 600 craters below it on seeds 1-64).
fn at_band_option_min() -> f64 {
    0.9
}

/// Size falls with distance: the near band's (core and near camps) mean crater tiles at least this many times the far
/// band's, pooled over seeds 1-32 (`mapgen_signature_fire_bands.rs` `NEAR_OVER_FAR`; decided rule 1.338 on 1-64).
fn near_over_far() -> f64 {
    1.15
}

/// Owner direction: about today's share, "within about a fifth" (median over seeds 1-32 of on / off).
fn share_window() -> (f64, f64) {
    (0.8, 1.2)
}

/// The two Fire guarantees, in run order (owner decision 2026-11-14).
fn fire_rules() -> [&'static str; 2] {
    ["crater-clear-of-lava", "crater-on-camp"]
}

/// The measured gap the owner accepted on 2026-11-14: Fire camps without a crater over seeds 1-64, option on.
/// Today's game (the stub) leaves 36 of 640; the decided rule (prototype V1) 32. The ceiling sits a few camps above
/// today's figure, so the build may not leave more camps bare than today's game does.
fn gap_ceiling() -> usize {
    40
}

/// The floor of the measured gap: at least one camp without a crater on seeds 1-64. Not a wish: reaching zero means the
/// owner's direction is delivered here and the rule should become a guarantee (see the test's failure message).
fn gap_floor() -> usize {
    1
}

/// The most Fire camps without a crater on one map (decided rule: 2 on seeds 1-64, 3 on seed 114; stub 3 on seed 50).
fn worst_map_allowed() -> usize {
    3
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

fn off_options() -> pipeline::Options {
    pipeline::Options {
        decor: false,
        ..pipeline::Options::default()
    }
}

/// One seed with the option on: the attempt that gave the map, the refusals before it, the map, the Fire frame.
struct Landed {
    seed: i64,
    attempt: u64,
    refusals: Vec<String>,
    map: GameMapData,
    frame: LandmarkFrame,
}

/// A range of seeds with the option on: the maps kept, and the seeds with no map in `attempt_limit` attempts (with
/// their refusals).
struct Run {
    kept: Vec<Landed>,
    lost: Vec<(i64, Vec<String>)>,
}

/// The Fire frame of attempt `n`'s Landmarks phase (the frame the pipeline hands the Fire styler and guarantees).
fn fire_frame(seed: i64, n: u64) -> LandmarkFrame {
    let fixed = coliseum::fixed_layout();
    let context = attempt::AttemptContext::new(&fixed);
    let frame = attempt::begin_attempt_signature(&context, seed, n, true)
        .expect("prefix input")
        .expect("the accepted attempt passed the Prefix");
    let mut st = stage::StageContext::new(&fixed)
        .apply(&frame, &recipes::Recipes::default())
        .expect("stage input")
        .expect("the accepted attempt passed the landmark stage");
    let placed = SignatureContext::new(&fixed)
        .apply(&frame, &mut st)
        .expect("signature input")
        .expect("the accepted attempt placed its landmarks");
    placed
        .frames
        .iter()
        .find(|f| f.zone() == Zone::Fire)
        .unwrap_or_else(|| panic!("seed {seed}: no Fire frame"))
        .clone()
}

fn land(generator: &pipeline::Pipeline<'_>, seed: i64) -> Result<Landed, (i64, Vec<String>)> {
    let mut refusals = Vec::new();
    for n in 0..attempt_limit() {
        let (report, _) = generator
            .styled_attempt_with_trace(seed, n, on_options())
            .expect("generator error");
        match report.map {
            Some(map) => {
                return Ok(Landed {
                    seed,
                    attempt: n,
                    refusals,
                    map,
                    frame: fire_frame(seed, n),
                })
            }
            None => refusals.push(format!(
                "{:?}",
                report.refusal.expect("a refused attempt says why")
            )),
        }
    }
    Err((seed, refusals))
}

fn land_range(range: std::ops::RangeInclusive<i64>) -> Run {
    let fixed = coliseum::fixed_layout();
    let generator = pipeline::Pipeline::new(&fixed);
    let mut run = Run {
        kept: Vec::new(),
        lost: Vec::new(),
    };
    for seed in range {
        match land(&generator, seed) {
            Ok(l) => run.kept.push(l),
            Err(lost) => run.lost.push(lost),
        }
    }
    run
}

/// Seeds 1-32 with the option on, once per test binary.
fn first() -> &'static Run {
    static RUN: OnceLock<Run> = OnceLock::new();
    RUN.get_or_init(|| land_range(seeds()))
}

/// The kept maps of seeds 1-32.
fn maps() -> &'static Vec<Landed> {
    &first().kept
}

/// Seeds 33-128 with the option on, once per test binary.
fn wide() -> &'static Run {
    static RUN: OnceLock<Run> = OnceLock::new();
    RUN.get_or_init(|| land_range(wide_seeds()))
}

/// The kept maps of seeds 33-128.
fn wide_maps() -> &'static Vec<Landed> {
    &wide().kept
}

/// The kept maps of seeds 1-128.
fn all_maps() -> impl Iterator<Item = &'static Landed> {
    maps().iter().chain(wide_maps())
}

/// Seeds 1-32 with the option off (today's generator), once per test binary.
fn off_maps() -> &'static Vec<(i64, GameMapData)> {
    static MAPS: OnceLock<Vec<(i64, GameMapData)>> = OnceLock::new();
    MAPS.get_or_init(|| {
        let fixed = coliseum::fixed_layout();
        let generator = pipeline::Pipeline::new(&fixed);
        seeds()
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

// ---------------------------------------------------------------------------------------------------------------------
// Helpers.
// ---------------------------------------------------------------------------------------------------------------------

fn cell(m: &GameMapData, i: usize) -> Cell {
    ((i as i32) % m.w as i32, (i as i32) / m.w as i32)
}

fn chebyshev(a: Cell, b: Cell) -> i32 {
    (a.0 - b.0).abs().max((a.1 - b.1).abs())
}

fn owner(m: &GameMapData, i: usize) -> Option<usize> {
    usize::from(m.feature_grid[i]).checked_sub(1)
}

fn fire_craters(m: &GameMapData) -> Vec<usize> {
    (0..m.features.len())
        .filter(|&k| m.features[k].kind == FeatureKind::Crater && m.features[k].zone == Zone::Fire)
        .collect()
}

fn is_fire_crater_tile(m: &GameMapData, i: usize) -> bool {
    owner(m, i).is_some_and(|k| {
        m.features
            .get(k)
            .is_some_and(|f| f.kind == FeatureKind::Crater && f.zone == Zone::Fire)
    })
}

/// The tiles crater `k` owns.
fn crater_tiles(m: &GameMapData, k: usize) -> Vec<Cell> {
    (0..m.feature_grid.len())
        .filter(|&i| owner(m, i) == Some(k))
        .map(|i| cell(m, i))
        .collect()
}

/// Whether `c` lies in the disc of a crater of radius `r4` centred on `at` (header definition).
fn in_disc_at(at: Cell, r4: i32, c: Cell) -> bool {
    let (dx, dy) = (c.0 - at.0, c.1 - at.1);
    16 * (dx * dx + dy * dy) < r4 * r4
}

/// Whether `c` lies in crater `k`'s disc (header definition).
fn in_disc(m: &GameMapData, k: usize, c: Cell) -> bool {
    let f = &m.features[k];
    in_disc_at((f.ax, f.ay), f.p[1], c)
}

/// Stream or pool lava (header definition), from the frame.
fn stream_lava(frame: &LandmarkFrame) -> Vec<Cell> {
    let w = coliseum::W as u32;
    let fp = frame.anchor.footprint;
    frame
        .cells
        .iter()
        .filter(|c| c.1 == TILE_LOW && c.2 == DEFAULT_DEFINITION.lava)
        .map(|c| ((c.0 % w) as i32, (c.0 / w) as i32))
        .filter(|&(x, y)| {
            !(x >= fp.x && y >= fp.y && x < fp.x + fp.w as i32 && y < fp.y + fp.h as i32)
        })
        .collect()
}

/// The Fire camp tiles of `m`, in camp order, with their elite flag.
fn camp_tiles(m: &GameMapData) -> Vec<(Cell, bool)> {
    m.camps
        .iter()
        .filter(|c| c.zone == Zone::Fire)
        .map(|c| {
            (
                ((c.x / 32.0).floor() as i32, (c.y / 32.0).floor() as i32),
                c.elite_spot >= 0,
            )
        })
        .collect()
}

fn crater_on(m: &GameMapData, t: Cell) -> Option<usize> {
    fire_craters(m)
        .into_iter()
        .find(|&k| (m.features[k].ax, m.features[k].ay) == t)
}

fn footprint_distance(a: &ZoneAnchor, c: Cell) -> i32 {
    let f = a.footprint;
    let (x1, y1) = (f.x + f.w as i32 - 1, f.y + f.h as i32 - 1);
    (f.x - c.0)
        .max(c.0 - x1)
        .max(0)
        .max((f.y - c.1).max(c.1 - y1).max(0))
}

fn band_of_distance(d: i32) -> Band {
    let b = DEFAULT_DEFINITION.bands;
    if d <= b[0] {
        Band::Core
    } else if d <= b[1] {
        Band::Near
    } else if d <= b[2] {
        Band::Mid
    } else {
        Band::Far
    }
}

/// One Fire camp as the band rule sees it (header definition).
struct Camp {
    tile: Cell,
    options: Vec<i32>,
    band: Band,
    crater: Option<usize>,
}

impl Camp {
    /// The band's option (quarter tiles); `None` for the core band (any option).
    fn target(&self) -> Option<i32> {
        let o = &self.options;
        match self.band {
            Band::Core => None,
            Band::Near => Some(o[0]),
            Band::Mid => Some(o[1.min(o.len() - 1)]),
            Band::Far => Some(o[o.len() - 1]),
        }
    }
}

fn camps(m: &GameMapData, a: &ZoneAnchor) -> Vec<Camp> {
    let forge = scorch::default_definition();
    let shrink = style_scorch::default_definition().crater.radius_shrink4;
    let tiles = camp_tiles(m);
    tiles
        .iter()
        .enumerate()
        .map(|(q, &(t, elite))| {
            let hi = if elite {
                forge.radius_elite_max4
            } else {
                forge.radius_max4
            };
            let near2 = tiles
                .iter()
                .enumerate()
                .filter(|&(p, _)| p != q)
                .map(|(_, &(o, _))| f64::from((t.0 - o.0).pow(2) + (t.1 - o.1).pow(2)))
                .reduce(f64::min);
            let r = match near2 {
                None => hi,
                Some(v) => (2.0 * rh_sim::math::sqrt(v) - forge.radius_subtract)
                    .floor()
                    .max(forge.radius_min4)
                    .min(hi),
            } as i32;
            let mut options = vec![r];
            for s in shrink {
                if f64::from(r - s) >= forge.radius_min4 {
                    options.push(r - s);
                }
            }
            Camp {
                tile: t,
                band: band_of_distance(footprint_distance(a, t) - (r + 3) / 4),
                options,
                crater: crater_on(m, t),
            }
        })
        .collect()
}

/// The Fire camps of `l` with no crater.
fn craterless(l: &Landed) -> Vec<String> {
    camp_tiles(&l.map)
        .into_iter()
        .filter(|&(t, _)| crater_on(&l.map, t).is_none())
        .map(|(t, _)| format!("seed {}: Fire camp {t:?} has no crater", l.seed))
        .collect()
}

/// The Fire craters of `l` that stand on no Fire camp, or share their camp with another crater.
fn off_camp(l: &Landed) -> Vec<String> {
    let m = &l.map;
    let camps = camp_tiles(m);
    let mut out = Vec::new();
    let anchors: Vec<Cell> = fire_craters(m)
        .iter()
        .map(|&k| (m.features[k].ax, m.features[k].ay))
        .collect();
    for (q, &a) in anchors.iter().enumerate() {
        if !camps.iter().any(|&(t, _)| t == a) {
            out.push(format!(
                "seed {}: crater at {a:?} stands on no Fire camp",
                l.seed
            ));
        } else if anchors[..q].contains(&a) {
            out.push(format!(
                "seed {}: a second crater on the Fire camp at {a:?}",
                l.seed
            ));
        }
    }
    out
}

/// The craters of `l` that do not keep clear of the streams (header definition): lava in the disc, or a crater tile
/// within the clearance of lava.
fn not_clear(l: &Landed) -> Vec<String> {
    let lava = stream_lava(&l.frame);
    let mut out = Vec::new();
    for k in fire_craters(&l.map) {
        let f = &l.map.features[k];
        let inside = lava.iter().filter(|&&c| in_disc(&l.map, k, c)).count();
        if inside > 0 {
            out.push(format!(
                "seed {}: crater at ({}, {}) radius {} holds {inside} stream or pool lava tiles in its disc",
                l.seed, f.ax, f.ay, f.p[1]
            ));
        }
        let near = crater_tiles(&l.map, k)
            .iter()
            .flat_map(|&t| lava.iter().map(move |&c| chebyshev(t, c)))
            .min();
        if let Some(d) = near.filter(|&d| d <= clearance()) {
            out.push(format!(
                "seed {}: crater at ({}, {}) has a tile {d} from stream or pool lava (clearance {})",
                l.seed,
                f.ax,
                f.ay,
                clearance()
            ));
        }
    }
    out
}

fn verdict(what: &str, findings: &[String]) {
    assert!(
        findings.is_empty(),
        "{}: {} findings; first {:#?}",
        what,
        findings.len(),
        &findings[..findings.len().min(12)]
    );
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

fn rules(z: Zone) -> Vec<&'static str> {
    guarantees(z).iter().map(|g| g.rule).collect()
}

/// The Fire guarantee `name`.
fn rule(name: &str) -> Guarantee {
    let g = guarantees(Zone::Fire)
        .iter()
        .find(|g| g.rule == name)
        .copied();
    assert!(
        g.is_some(),
        "no guarantee {name:?} registered for Fire (registered: {:?})",
        rules(Zone::Fire)
    );
    g.unwrap()
}

// ---------------------------------------------------------------------------------------------------------------------
// 1. Every seed keeps its map; every crater stands on its camp; the measured gap.
// ---------------------------------------------------------------------------------------------------------------------

/// A zone rule never loses a seed (standing rule, restated in the owner's decision of 2026-11-14): every seed of 1-128
/// has a map within `attempt_limit` attempts with the option on. (The hard per-camp rule lost seeds 27, 30 and 70.)
#[test]
fn every_seed_of_1_to_128_keeps_its_map() {
    let lost: Vec<String> = first()
        .lost
        .iter()
        .chain(&wide().lost)
        .map(|(seed, r)| format!("seed {seed}: no map in {} attempts: {r:?}", attempt_limit()))
        .collect();
    verdict("seeds without a map (1-128, option on)", &lost);
}

/// Every Fire crater of every kept map stands on a Fire camp, one crater per camp at most (what `crater-on-camp`
/// guarantees, measured here without it).
#[test]
fn every_fire_crater_stands_on_its_own_fire_camp() {
    let bad: Vec<String> = all_maps().flat_map(off_camp).collect();
    verdict("Fire craters off their camp (seeds 1-128)", &bad);
}

/// KNOWN GAP, accepted by the owner on 2026-11-14 (decision 1: "land the gentler rule now, with craters guaranteed
/// clear of lava, and record 'about 1 camp in 20 without a crater' as a measured gap"). The owner's direction "every
/// Fire camp keeps its crater" (`LAND_GENERATION_REQUIREMENTS.md` section 3) is NOT delivered: this test replaced
/// `every_fire_camp_has_its_crater` by that decision.
///
/// Over seeds 1-64 (640 Fire camps) the count of Fire camps without a crater lies in [`gap_floor`, `gap_ceiling`]
/// (today's game 36, the decided rule 32); on seeds 1-64 and on seeds 65-128 no single map has more than
/// `worst_map_allowed` camps without a crater (decided rule: 2 and 3; today's game 3 on seeds 1-64).
#[test]
fn known_gap_about_one_fire_camp_in_twenty_has_no_crater() {
    let in_gap = |l: &&Landed| gap_seeds().contains(&l.seed);
    let missing: Vec<String> = all_maps().filter(in_gap).flat_map(craterless).collect();
    let camps: usize = all_maps()
        .filter(in_gap)
        .map(|l| camp_tiles(&l.map).len())
        .sum();
    eprintln!(
        "seeds 1-64: {} of {camps} Fire camps without a crater; {missing:#?}",
        missing.len()
    );
    assert!(
        missing.len() <= gap_ceiling(),
        "{} of {camps} Fire camps without a crater on seeds 1-64, above the ceiling {} (today's game 36, the rule the \
         owner accepted on 2026-11-14 measured 32): the build leaves more camps bare than today's game. Fix the \
         build; the ceiling moves only with the owner's approval. First {:#?}",
        missing.len(),
        gap_ceiling(),
        &missing[..missing.len().min(12)]
    );
    assert!(
        missing.len() >= gap_floor(),
        "no Fire camp without a crater on seeds 1-64: the owner's direction 'every Fire camp keeps its crater' is now \
         delivered on these seeds. Promote it to a guarantee (a per-camp Fire rule; restore the test \
         every_fire_camp_has_its_crater in place of this pin) in its own packet, and tell the owner the measured gap \
         of 2026-11-14 is closed."
    );
    for (name, range) in [("1-64", 1..=64), ("65-128", 65..=128)] {
        let worst = all_maps()
            .filter(|l| range.contains(&l.seed))
            .map(|l| (craterless(l).len(), l.seed))
            .max()
            .expect("kept maps in range");
        eprintln!(
            "seeds {name}: most Fire camps without a crater on one map {worst:?} (count, seed)"
        );
        assert!(
            worst.0 <= worst_map_allowed(),
            "seed {}: {} Fire camps without a crater on one map (seeds {name}), more than {} (the rule the owner \
             accepted on 2026-11-14 measured 2 on seeds 1-64 and 3 on seeds 65-128): {:#?}",
            worst.1,
            worst.0,
            worst_map_allowed(),
            all_maps()
                .find(|l| l.seed == worst.1)
                .map(craterless)
                .unwrap_or_default()
        );
    }
}

// ---------------------------------------------------------------------------------------------------------------------
// 2. Craters keep clear of the streams.
// ---------------------------------------------------------------------------------------------------------------------

/// The frame's stream and pool lava (header definition) is what the map's `LavaStream` and `LavaPool` features own as
/// lava: the definition reads the volcano as it was written, and the map still has it.
#[test]
fn the_frames_lava_outside_the_cone_is_the_streams_and_pools() {
    for l in maps() {
        let m = &l.map;
        let lava = stream_lava(&l.frame);
        assert!(!lava.is_empty(), "seed {}: no stream lava", l.seed);
        let mut owned: Vec<Cell> = (0..m.tiles.len())
            .filter(|&i| {
                owner(m, i).is_some_and(|k| {
                    matches!(
                        m.features[k].kind,
                        FeatureKind::LavaStream | FeatureKind::LavaPool
                    )
                }) && m.tiles[i] == TILE_LOW
                    && m.terrain[i] == DEFAULT_DEFINITION.lava
            })
            .map(|i| cell(m, i))
            .collect();
        let mut a = lava.clone();
        a.sort_unstable();
        owned.sort_unstable();
        assert_eq!(
            a, owned,
            "seed {}: the frame's lava outside the cone",
            l.seed
        );
    }
}

/// No stream or pool lava lies inside a Fire crater's disc, and no crater tile lies within the clearance of it.
#[test]
fn craters_keep_clear_of_the_streams_and_pools() {
    assert_eq!(
        style_scorch::lava_clearance(),
        clearance(),
        "the clearance is today's apron"
    );
    let bad: Vec<String> = maps().iter().flat_map(not_clear).collect();
    verdict("craters not clear of the streams (seeds 1-32)", &bad);
}

#[test]
fn wide_craters_keep_clear_of_the_streams_and_pools() {
    let bad: Vec<String> = wide_maps().iter().flat_map(not_clear).collect();
    verdict("craters not clear of the streams (seeds 33-128)", &bad);
}

// ---------------------------------------------------------------------------------------------------------------------
// 3. Size falls with distance.
// ---------------------------------------------------------------------------------------------------------------------

/// Every crater's radius is one of its camp's options, at least the smallest crater, and no larger than its band's
/// option (core: any option); at least `at_band_option_min` of them take exactly their band's option; the near band's
/// craters average at least `near_over_far` times the far band's tiles.
#[test]
fn crater_size_falls_with_distance_and_shrinks_only_below_its_bands_option() {
    assert_eq!(
        scorch::default_definition().radius_min4 as i32,
        smallest_radius4(),
        "the smallest crater is today's radius_min4"
    );
    let mut bad = Vec::new();
    let (mut total, mut at_option) = (0usize, 0usize);
    let (mut near, mut far) = (Vec::new(), Vec::new());
    for l in maps() {
        let a = &l.frame.anchor;
        for c in camps(&l.map, a) {
            let Some(k) = c.crater else { continue };
            let used = l.map.features[k].p[1];
            total += 1;
            if c.target().is_none_or(|t| used == t) {
                at_option += 1;
            }
            let ok = c.options.contains(&used)
                && used >= smallest_radius4()
                && c.target().is_none_or(|t| used <= t);
            if !ok {
                bad.push(format!(
                    "seed {} camp {:?} {:?} options {:?} band option {:?} radius {used}",
                    l.seed,
                    c.tile,
                    c.band,
                    c.options,
                    c.target()
                ));
            }
            let size = f64::from(l.map.features[k].tiles);
            match c.band {
                Band::Core | Band::Near => near.push(size),
                Band::Far => far.push(size),
                Band::Mid => {}
            }
        }
    }
    verdict("craters larger than their band allows", &bad);
    let share = at_option as f64 / total.max(1) as f64;
    assert!(
        share >= at_band_option_min(),
        "{at_option} of {total} craters take their band's option ({share:.3} < {})",
        at_band_option_min()
    );
    let mean = |v: &[f64]| v.iter().sum::<f64>() / v.len().max(1) as f64;
    assert!(
        !near.is_empty() && !far.is_empty() && mean(&near) >= near_over_far() * mean(&far),
        "near-band craters average {:.1} tiles over {}, far-band {:.1} over {}: less than {}x",
        mean(&near),
        near.len(),
        mean(&far),
        far.len(),
        near_over_far()
    );
}

// ---------------------------------------------------------------------------------------------------------------------
// 4. The guarantees.
// ---------------------------------------------------------------------------------------------------------------------

/// Fire registers `crater-clear-of-lava` then `crater-on-camp` (owner decision 2026-11-14), and nothing else: no rule
/// that every Fire camp has its crater.
#[test]
fn the_fire_guarantees_are_registered_by_name() {
    assert_eq!(
        rules(Zone::Fire),
        fire_rules().to_vec(),
        "Fire's guarantees, in run order"
    );
}

/// Both Fire guarantees pass on every kept map of seeds 1-128 (the maps with a camp left without a crater included).
#[test]
fn the_fire_guarantees_hold_on_every_kept_map() {
    let gs = fire_rules().map(rule);
    let mut with_gap = 0;
    for l in all_maps() {
        if !craterless(l).is_empty() {
            with_gap += 1;
        }
        for g in &gs {
            assert!(
                (g.check)(&l.map, &l.frame),
                "seed {}: the kept map fails {:?}",
                l.seed,
                g.rule
            );
        }
    }
    eprintln!("kept maps with a Fire camp left without a crater: {with_gap}");
}

/// `m` with feature `k` removed: its record gone, its tiles unowned, later owners renumbered.
fn without_feature(m: &GameMapData, k: usize) -> GameMapData {
    let mut out = m.clone();
    out.features.remove(k);
    for v in out.feature_grid.iter_mut() {
        let id = usize::from(*v);
        if id == k + 1 {
            *v = 0;
        } else if id > k + 1 {
            *v -= 1;
        }
    }
    out
}

/// A Fire camp without its crater is refused by no Fire rule (owner decision 2026-11-14): on every kept map of seeds
/// 1-32, with one Fire crater removed, every registered Fire guarantee still passes.
#[test]
fn a_camp_without_its_crater_is_refused_by_no_fire_rule() {
    let gs = fire_rules().map(rule);
    let mut doctored = 0;
    for l in maps() {
        let Some(&k) = fire_craters(&l.map).first() else {
            continue;
        };
        let m = without_feature(&l.map, k);
        for g in guarantees(Zone::Fire).iter().chain(&gs) {
            assert!(
                (g.check)(&m, &l.frame),
                "seed {}: the camp at ({}, {}) lost its crater and {:?} refuses the map",
                l.seed,
                l.map.features[k].ax,
                l.map.features[k].ay,
                g.rule
            );
        }
        doctored += 1;
    }
    assert!(
        doctored >= 16,
        "only {doctored} kept maps with a Fire crater"
    );
}

/// `crater-on-camp` fails on a kept map once a Fire crater's anchor is moved one tile off its camp (to a tile that is
/// no Fire camp, where its disc still holds no stream lava); `crater-clear-of-lava` still holds.
#[test]
fn crater_on_camp_fails_with_a_crater_moved_off_its_camp() {
    let [clear, on_camp] = fire_rules().map(rule);
    let mut doctored = 0;
    for l in maps() {
        let m0 = &l.map;
        let lava = stream_lava(&l.frame);
        let camps: Vec<Cell> = camp_tiles(m0).into_iter().map(|(t, _)| t).collect();
        let moved = fire_craters(m0).into_iter().find_map(|k| {
            let f = &m0.features[k];
            [(1, 0), (-1, 0), (0, 1), (0, -1)]
                .into_iter()
                .map(|(dx, dy)| (f.ax + dx, f.ay + dy))
                .find(|&a| !camps.contains(&a) && !lava.iter().any(|&c| in_disc_at(a, f.p[1], c)))
                .map(|a| (k, a))
        });
        let Some((k, a)) = moved else { continue };
        let mut m = m0.clone();
        m.features[k].ax = a.0;
        m.features[k].ay = a.1;
        assert!(
            !(on_camp.check)(&m, &l.frame),
            "seed {}: crater moved from ({}, {}) to {a:?}, off every Fire camp",
            l.seed,
            m0.features[k].ax,
            m0.features[k].ay
        );
        assert!(
            (clear.check)(&m, &l.frame),
            "seed {}: the moved crater is no less clear",
            l.seed
        );
        doctored += 1;
    }
    assert!(doctored >= 16, "only {doctored} kept maps doctored");
}

/// `crater-clear-of-lava` fails on a kept map once a tile beside stream lava (Chebyshev 1, not lava) is given to a
/// Fire crater; `crater-on-camp` still holds.
#[test]
fn crater_clear_of_lava_fails_with_a_crater_tile_beside_a_stream() {
    let [clear, on_camp] = fire_rules().map(rule);
    let mut doctored = 0;
    for l in maps() {
        let m0 = &l.map;
        let Some(&k) = fire_craters(m0).first() else {
            continue;
        };
        let lava = stream_lava(&l.frame);
        let w = m0.w as i32;
        let beside = lava.iter().find_map(|&(x, y)| {
            [(1, 0), (-1, 0), (0, 1), (0, -1)]
                .into_iter()
                .map(|(dx, dy)| (x + dx, y + dy))
                .find(|&c| !lava.contains(&c) && c.0 >= 0 && c.1 >= 0 && c.0 < w)
        });
        let Some(t) = beside else { continue };
        let mut m = m0.clone();
        m.feature_grid[(t.1 * w + t.0) as usize] =
            u8::try_from(k + 1).expect("feature id fits the grid");
        assert!(
            !(clear.check)(&m, &l.frame),
            "seed {}: crater tile {t:?} beside stream lava",
            l.seed
        );
        assert!(
            (on_camp.check)(&m, &l.frame),
            "seed {}: every crater still stands on its camp",
            l.seed
        );
        doctored += 1;
    }
    assert!(doctored >= 16, "only {doctored} kept maps doctored");
}

/// `crater-clear-of-lava` fails on a kept map once a Fire crater's radius is widened until its disc holds stream lava
/// (its tiles unchanged); `crater-on-camp` still holds.
#[test]
fn crater_clear_of_lava_fails_with_a_crater_disc_over_a_stream() {
    let [clear, on_camp] = fire_rules().map(rule);
    let mut doctored = 0;
    for l in maps() {
        let m0 = &l.map;
        let Some(&k) = fire_craters(m0).first() else {
            continue;
        };
        let f = &m0.features[k];
        let d2 = stream_lava(&l.frame)
            .iter()
            .map(|&(x, y)| (x - f.ax).pow(2) + (y - f.ay).pow(2))
            .min()
            .expect("stream lava");
        // 16 * d2 < r4^2 for the nearest lava tile.
        let r4 = (rh_sim::math::sqrt(f64::from(16 * d2)).floor() as i32) + 1;
        let mut m = m0.clone();
        m.features[k].p[1] = r4;
        assert!(
            !(clear.check)(&m, &l.frame),
            "seed {}: crater at ({}, {}) widened to radius {r4} over stream lava",
            l.seed,
            f.ax,
            f.ay
        );
        assert!(
            (on_camp.check)(&m, &l.frame),
            "seed {}: every crater still stands on its camp",
            l.seed
        );
        doctored += 1;
    }
    assert!(doctored >= 16, "only {doctored} kept maps doctored");
}

// ---------------------------------------------------------------------------------------------------------------------
// 5. Amounts, the volcano, attempts.
// ---------------------------------------------------------------------------------------------------------------------

/// (Fire crater tiles, free ground) of `m` (header definition); the site is taken out when `site` is given.
fn crater_share(m: &GameMapData, site: Option<&LandmarkFrame>) -> (usize, usize) {
    let lava = site.map(stream_lava).unwrap_or_default();
    let (mut craters, mut ground) = (0, 0);
    for i in 0..m.tiles.len() {
        if m.region_grid[i] != Zone::Fire.code() {
            continue;
        }
        let c = cell(m, i);
        if let Some(f) = site {
            if band(&f.anchor, c.0, c.1) == Band::Core
                || lava.iter().any(|&l| chebyshev(l, c) <= clearance())
            {
                continue;
            }
        }
        let crater = is_fire_crater_tile(m, i);
        if m.tiles[i] == 0 || crater {
            ground += 1;
        }
        if crater {
            craters += 1;
        }
    }
    (craters, ground)
}

/// The median over seeds 1-32 of (crater share, option on, site taken out) / (crater share, option off) lies within a
/// fifth of 1 (stub 0.960, the decided rule 0.952).
#[test]
fn crater_share_of_free_ground_stays_within_a_fifth_of_todays() {
    let mut ratios: Vec<f64> = maps()
        .iter()
        .map(|l| {
            let (_, off) = off_maps()
                .iter()
                .find(|(seed, _)| *seed == l.seed)
                .expect("an option-off map of the same seed");
            let (c_on, g_on) = crater_share(&l.map, Some(&l.frame));
            let (c_off, g_off) = crater_share(off, None);
            (c_on as f64 / g_on as f64) / (c_off as f64 / g_off as f64)
        })
        .collect();
    let per_seed = ratios.clone();
    let mid = median(&mut ratios);
    let (lo, hi) = share_window();
    assert!(
        (lo..=hi).contains(&mid),
        "median on/off crater share {mid:.3} outside [{lo}, {hi}]; per seed {per_seed:.3?}"
    );
}

/// The volcano is untouched: the Fire frame wrote the cone, then each stream and its pool, three times; the map keeps
/// exactly three Fire `LavaStream` and three `LavaPool` features (the full rules: `mapgen_signature_volcano.rs`).
#[test]
fn the_volcano_keeps_its_three_streams_and_pools() {
    for l in maps() {
        let kinds: Vec<FeatureKind> = l.frame.features.iter().map(|f| f.0).collect();
        assert_eq!(
            kinds,
            vec![
                FeatureKind::Volcano,
                FeatureKind::LavaStream,
                FeatureKind::LavaPool,
                FeatureKind::LavaStream,
                FeatureKind::LavaPool,
                FeatureKind::LavaStream,
                FeatureKind::LavaPool,
            ],
            "seed {}: the Fire frame's features",
            l.seed
        );
        for kind in [FeatureKind::LavaStream, FeatureKind::LavaPool] {
            let n = l
                .map
                .features
                .iter()
                .filter(|f| f.kind == kind && f.zone == Zone::Fire)
                .count();
            assert_eq!(n, 3, "seed {}: {kind:?} features", l.seed);
        }
    }
}

/// Over seeds 1-32: the worst attempt index at most `worst_attempt_allowed`, the attempts in all at most
/// `attempts_allowed`, and at most `fire_refusals_allowed` attempts refused by a Fire guarantee.
#[test]
fn the_attempt_bounds_hold_and_the_fire_guarantees_refuse_little() {
    let worst = maps().iter().map(|l| (l.attempt, l.seed)).max().unwrap();
    let all: u64 = maps().iter().map(|l| l.attempt + 1).sum();
    let fire: Vec<(i64, &String)> = maps()
        .iter()
        .flat_map(|l| l.refusals.iter().map(move |r| (l.seed, r)))
        .filter(|(_, r)| r.starts_with("Guarantee { zone: Fire"))
        .collect();
    eprintln!("worst attempt {worst:?}, attempts {all}, Fire guarantee refusals {fire:?}");
    assert!(
        worst.0 <= worst_attempt_allowed(),
        "seed {}: attempt {} > {}",
        worst.1,
        worst.0,
        worst_attempt_allowed()
    );
    assert!(
        all <= attempts_allowed(),
        "{all} attempts over seeds 1-32 > {}",
        attempts_allowed()
    );
    assert!(
        fire.len() <= fire_refusals_allowed(),
        "{} Fire guarantee refusals > {}: {fire:?}",
        fire.len(),
        fire_refusals_allowed()
    );
}
