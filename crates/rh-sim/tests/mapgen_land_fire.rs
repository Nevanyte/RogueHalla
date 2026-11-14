//! Oracle of P-LAND-FIRE-1 (brief `docs/backlog/packets/P-LAND-FIRE-1.md`): with `pipeline::Options::signature` on,
//! every Fire camp keeps its crater, and craters keep clear of the volcano's three lava streams (owner direction,
//! `docs/coordination/LAND_GENERATION_REQUIREMENTS.md` section 3: "Craters keep clear of all three streams. Every Fire
//! camp keeps its crater; size falls with distance and none is dropped."), with about today's share of the zone's free
//! ground as crater, and two end-of-attempt guarantees registered for Fire (`style_scorch::guarantees`). Guarantees are
//! hard: an attempt that cannot keep them is refused and the next one tried. No legacy counterpart (DEV-020): legacy
//! (`packages/shared/src/zoneStyle/scorch.ts`) has no volcano; option off keeps it byte for byte (guarded by
//! `mapgen_signature_fire.rs::g8_option_off_maps_are_todays` and the option-off pins, not repeated here).
//!
//! Every check is a property measured from the map and the Fire landmark frame of the accepted attempt (seeds 1-32,
//! option on, the server's options: no decor; `wide_*` on seeds 33-96). Nothing pins an option-on map, hash, attempt
//! number or tile position.
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
    33..=96
}

/// The attempts `certified_styled` may use (`pipeline` rules, `certified_attempts`).
fn attempt_limit() -> u64 {
    16
}

/// The stack's attempt bounds over seeds 1-32 (P-MAPGEN-SIG-SCALE-4's provisional figures; measured 9 / 89 on this
/// packet's base).
fn worst_attempt_allowed() -> u64 {
    12
}

fn attempts_allowed() -> u64 {
    137
}

/// PROVISIONAL (oracle author's, for the owner): the most attempts over seeds 1-32 the two Fire guarantees may refuse.
/// On the base 15 of 32 kept maps drop a camp's crater and 8 have stream lava inside a crater's disc; the styler is
/// meant to fit the crater, not to lean on refusals.
fn fire_refusals_allowed() -> usize {
    8
}

/// The clearance between crater tiles and stream or pool lava: today's apron (header definition).
fn clearance() -> i32 {
    2
}

/// The smallest crater: today's `radius_min4` (quarter tiles; a ring 5 tiles out). Owner figure to confirm: no crater
/// is drawn smaller; a camp that cannot hold one this size is a refused attempt.
fn smallest_radius4() -> i32 {
    20
}

/// PROVISIONAL (oracle author's, for the owner): the least share of Fire craters over seeds 1-32 that take exactly
/// their band's option (today 1.0; a crater shrinks only to keep clear of lava or to be kept at all).
fn at_band_option_min() -> f64 {
    0.9
}

/// Size falls with distance: the near band's (core and near camps) mean crater tiles at least this many times the far
/// band's, pooled over seeds 1-32 (`mapgen_signature_fire_bands.rs` `NEAR_OVER_FAR`; today 1.31).
fn near_over_far() -> f64 {
    1.15
}

/// Owner direction: about today's share, "within about a fifth" (median over seeds 1-32 of on / off).
fn share_window() -> (f64, f64) {
    (0.8, 1.2)
}

/// The two Fire guarantees, in run order.
fn fire_rules() -> [&'static str; 2] {
    ["crater-per-camp", "crater-clear-of-lava"]
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

fn land(generator: &pipeline::Pipeline<'_>, seed: i64) -> Landed {
    let mut refusals = Vec::new();
    for n in 0..attempt_limit() {
        let (report, _) = generator
            .styled_attempt_with_trace(seed, n, on_options())
            .expect("generator error");
        match report.map {
            Some(map) => {
                return Landed {
                    seed,
                    attempt: n,
                    refusals,
                    map,
                    frame: fire_frame(seed, n),
                }
            }
            None => refusals.push(format!(
                "{:?}",
                report.refusal.expect("a refused attempt says why")
            )),
        }
    }
    panic!(
        "seed {seed}: no map in {} attempts with the option on ({refusals:?})",
        attempt_limit()
    )
}

fn land_range(range: std::ops::RangeInclusive<i64>) -> Vec<Landed> {
    let fixed = coliseum::fixed_layout();
    let generator = pipeline::Pipeline::new(&fixed);
    range.map(|seed| land(&generator, seed)).collect()
}

/// Seeds 1-32 with the option on, once per test binary.
fn maps() -> &'static Vec<Landed> {
    static MAPS: OnceLock<Vec<Landed>> = OnceLock::new();
    MAPS.get_or_init(|| land_range(seeds()))
}

/// Seeds 33-96 with the option on, once per test binary.
fn wide_maps() -> &'static Vec<Landed> {
    static MAPS: OnceLock<Vec<Landed>> = OnceLock::new();
    MAPS.get_or_init(|| land_range(wide_seeds()))
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

/// Whether `c` lies in crater `k`'s disc (header definition).
fn in_disc(m: &GameMapData, k: usize, c: Cell) -> bool {
    let f = &m.features[k];
    let (dx, dy) = (c.0 - f.ax, c.1 - f.ay);
    16 * (dx * dx + dy * dy) < f.p[1] * f.p[1]
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

/// The camps of `l` with no crater.
fn craterless(l: &Landed) -> Vec<String> {
    camp_tiles(&l.map)
        .into_iter()
        .filter(|&(t, _)| crater_on(&l.map, t).is_none())
        .map(|(t, _)| format!("seed {}: Fire camp {t:?} has no crater", l.seed))
        .collect()
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
// 1. Every Fire camp keeps its crater.
// ---------------------------------------------------------------------------------------------------------------------

/// Every Fire camp of every map has its crater, and every Fire crater stands on a Fire camp (one each).
#[test]
fn every_fire_camp_has_its_crater() {
    let mut bad: Vec<String> = maps().iter().flat_map(craterless).collect();
    for l in maps() {
        let camps = camp_tiles(&l.map);
        for k in fire_craters(&l.map) {
            let f = &l.map.features[k];
            if !camps.iter().any(|&(t, _)| t == (f.ax, f.ay)) {
                bad.push(format!(
                    "seed {}: crater at ({}, {}) stands on no Fire camp",
                    l.seed, f.ax, f.ay
                ));
            }
        }
        let n = fire_craters(&l.map).len();
        if n != camps.len() && craterless(l).is_empty() {
            bad.push(format!(
                "seed {}: {n} Fire craters for {} Fire camps",
                l.seed,
                camps.len()
            ));
        }
    }
    verdict("Fire camps without their crater (seeds 1-32)", &bad);
}

#[test]
fn wide_every_fire_camp_has_its_crater() {
    let bad: Vec<String> = wide_maps().iter().flat_map(craterless).collect();
    verdict("Fire camps without their crater (seeds 33-96)", &bad);
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
    verdict("craters not clear of the streams (seeds 33-96)", &bad);
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

/// Fire registers `crater-per-camp` then `crater-clear-of-lava`.
#[test]
fn the_fire_guarantees_are_registered_by_name() {
    assert_eq!(
        rules(Zone::Fire),
        fire_rules().to_vec(),
        "Fire's guarantees, in run order"
    );
}

/// Both Fire guarantees pass on every kept map of seeds 1-32.
#[test]
fn the_fire_guarantees_hold_on_every_kept_map() {
    let gs = fire_rules().map(rule);
    for l in maps() {
        for g in &gs {
            assert!(
                (g.check)(&l.map, &l.frame),
                "seed {}: the kept map fails {:?}",
                l.seed,
                g.rule
            );
        }
    }
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

/// `crater-per-camp` fails on a kept map once one Fire crater is removed; `crater-clear-of-lava` still holds.
#[test]
fn crater_per_camp_fails_with_a_crater_removed() {
    let [per_camp, clear] = fire_rules().map(rule);
    let mut doctored = 0;
    for l in maps() {
        let Some(&k) = fire_craters(&l.map).first() else {
            continue;
        };
        let m = without_feature(&l.map, k);
        assert!(
            !(per_camp.check)(&m, &l.frame),
            "seed {}: the camp at ({}, {}) lost its crater",
            l.seed,
            l.map.features[k].ax,
            l.map.features[k].ay
        );
        assert!(
            (clear.check)(&m, &l.frame),
            "seed {}: fewer craters are no less clear",
            l.seed
        );
        doctored += 1;
    }
    assert!(
        doctored >= 16,
        "only {doctored} kept maps with a Fire crater"
    );
}

/// `crater-clear-of-lava` fails on a kept map once a tile beside stream lava (Chebyshev 1, not lava) is given to a
/// Fire crater; `crater-per-camp` still holds.
#[test]
fn crater_clear_of_lava_fails_with_a_crater_tile_beside_a_stream() {
    let [per_camp, clear] = fire_rules().map(rule);
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
            (per_camp.check)(&m, &l.frame),
            "seed {}: every camp still has its crater",
            l.seed
        );
        doctored += 1;
    }
    assert!(doctored >= 16, "only {doctored} kept maps doctored");
}

/// `crater-clear-of-lava` fails on a kept map once a Fire crater's radius is widened until its disc holds stream lava
/// (its tiles unchanged); `crater-per-camp` still holds.
#[test]
fn crater_clear_of_lava_fails_with_a_crater_disc_over_a_stream() {
    let [per_camp, clear] = fire_rules().map(rule);
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
            (per_camp.check)(&m, &l.frame),
            "seed {}: every camp still has its crater",
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
/// fifth of 1 (today 1.00).
#[test]
fn crater_share_of_free_ground_stays_within_a_fifth_of_todays() {
    let mut ratios: Vec<f64> = maps()
        .iter()
        .zip(off_maps())
        .map(|(l, (seed, off))| {
            assert_eq!(l.seed, *seed);
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
