//! The signature step's tunables and the zone-side rules: `Definition`, the reserved sites, the camp relabel, the fan
//! rectangle and the G4 bands. Re-exported by `signature`.

use super::*;

/// Every signature tunable (G4's band table included) in one definition. Not folded into `content_hash` while the
/// option is off by default (listed in `tests/content.rs` NON_GAMEPLAY; the owner decides when it is promoted).
#[derive(Clone, Copy, Debug, PartialEq)]
pub struct Definition {
    /// The `hash_seed` stream id of the signature draws (0-2 are the stylers', 3 the landmark recipes', 4 the bushes').
    pub stream: u32,
    /// G4: the outer edge, in tiles of Chebyshev distance from the anchor's footprint rectangle, of the core, near and
    /// mid bands (inclusive); beyond the last is far. The core band is also the apron kept clear round the landmark.
    pub bands: [i32; 3],
    /// The terrain codes of the bog (the tree's marsh), the lava (LOW), the ember (floor) and the ice (floor):
    /// documented mirrors of the terrain catalog's codes of the kinds keyed `bog`, `lava`, `ember` and `ice`
    /// (`terrain_gate::DEFAULT_DEFINITION.kinds`; homes Plague, Fire, Fire, Frost), for the readers outside the step (the
    /// stylers, the Plague pools, the tests). The step itself writes and checks the catalog's codes
    /// (`SignatureContext::terrain_codes`, resolved by key when the context is built; a debug build asserts they agree).
    pub bog: u8,
    pub lava: u8,
    pub ember: u8,
    pub ice: u8,
    /// Plague site: the tiles its centre must stand beyond the start of the Plague zone's outer third (slack for later
    /// rezoning; `SignatureContext::reserve`).
    pub outer_slack: f64,
    /// Fire, Sentenced and Frost sites: the tiles a site's centre stands beyond the zone centroid's distance from the map
    /// centre (the back edge, with slack for later rezoning; `SignatureContext::reserve`).
    pub back_slack: f64,
    /// Volcano streams (P-MAPGEN-SIG-SCALE-4: laid from the drawing's outlet runs, `volcano::streams`): the shortest
    /// path tried (tiles), how many heading steps beyond its outlet run's centre the path's first tile lies, how far a
    /// path may bend from its own heading and from the drawing's front (DIR16 steps), the Chebyshev gap kept from every
    /// other stream and pool, and the squared distance from its outlet run within which that gap is not asked.
    pub stream_min: usize,
    pub stream_start: i32,
    pub stream_bend: i32,
    pub front_max: i32,
    pub stream_gap: i32,
    pub start_d2: i32,
    /// Pools: the squared radii tried, largest first.
    pub pool_d2: &'static [i32],
    /// Volcano: the zone's total lava (the cone's crater lake, channels and outlets, the streams and the pools;
    /// inclusive). PROVISIONAL (P-MAPGEN-SIG-SCALE-4): the window round the prototype's measured 221-244; the coordinator
    /// finalises it after measurement on the build.
    pub lava_budget: (usize, usize),
    /// The tiles beyond each entrance tile, along the front, that must be `Site::open` for an entry whose table row says
    /// `doorway` (the church's main gate, the cave's mouth; `stamp`).
    pub doorway: i32,
    /// Frost styler in signature mode: the mere's seed is an allowed tile at most `lake_reach` floor steps from the
    /// cave's mouth; the first of `lake_tries` candidates that grows a lake is kept. It is regrown off the spawn routes
    /// at most `lake_rounds` times. The second lake grows to at most `second_share` percent of the mere's area.
    pub lake_reach: usize,
    pub lake_tries: usize,
    pub lake_rounds: usize,
    pub second_share: usize,
    /// The end-of-attempt check (`SignatureContext::intact`): the Frost zone's largest ice body outside the cave lies
    /// within `lake_reach + lake_slack` floor steps of the cave's mouth, else the map is discarded (P-MAPGEN-SIG-FROST-2).
    pub lake_slack: usize,
    /// P-MAPGEN-SIG-RESERVE-1: the side (tiles) of the square site each styled zone reserves in the Prefix before its
    /// camps (the landmark's target footprint plus the apron, `bands[0]`, on each side), and the depth (tiles) of the
    /// church's or the cave's camp from the site's far edge, on the site's centre line (its halo and the landmark's back
    /// wall stay inside the site's inner rect).
    pub site_side: i32,
    pub site_depth: i32,
    /// P-MAPGEN-SIG-SCALE-1: the Frost cave's approved blueprint (owner decision 2026-11-13; ADR 0028 G9), the text of
    /// `cave.txt` beside this file, a byte-equal copy of `docs/landmarks/blueprints/cave.txt` (the docs are not in the
    /// server image's build context, so the crate carries its own copy). Lines starting `;` are its legend; the rest is
    /// the 29 x 29 grid, front (toward the coliseum) at the bottom. Parsed once per context by `blueprint::Blueprint::parse`
    /// with the table's legend (`spec::specs`); a grid that does not parse refuses every option-on attempt at the cave
    /// (`Refusal::Signature`).
    pub cave_grid: &'static str,
    /// P-MAPGEN-SIG-SCALE-3: the Plague bog tree's approved blueprint (owner decision 2026-11-13; ADR 0028 G9), the text
    /// of `bog.txt` beside this file, a byte-equal copy of `docs/landmarks/blueprints/bog.txt` (the docs are not in the
    /// server image's build context). Lines starting `;` are its legend; the rest is the 29 x 29 grid, front (toward the
    /// coliseum) at the bottom. Parsed as `cave_grid`; a grid that does not parse refuses every option-on attempt at the
    /// tree (`Refusal::Signature`).
    pub bog_grid: &'static str,
    /// P-MAPGEN-SIG-SCALE-2: the Sentenced church's approved blueprint with its graveyard and churchyard (owner decision
    /// 2026-11-13; ADR 0028 G9), the text of `church.txt` beside this file, a byte-equal copy of
    /// `docs/landmarks/blueprints/church.txt` (as `cave_grid`). Lines starting `;` are its legend; the rest is the 29 x 29
    /// grid, front (toward the coliseum) at the bottom. Parsed as `cave_grid`; a grid that does not parse refuses every
    /// option-on attempt at the church (`Refusal::Signature`).
    pub church_grid: &'static str,
    /// P-MAPGEN-SIG-SCALE-4: the Fire volcano's approved blueprint (owner decisions 2026-11-13, requirements L1 and L4,
    /// and 2026-11-14, "22 across"; ADR 0028 G9), the text of `volcano.txt` beside this file, a byte-equal copy of
    /// `docs/landmarks/blueprints/volcano.txt` (as `cave_grid`). Lines starting `;` are its legend; the rest is the 22 x 22
    /// grid, front (toward the coliseum) at the bottom, its six outlet tiles `O` in three runs of two on the front row.
    /// It lands centred in the Fire site's unchanged 30 x 30 inner rect (4 tiles spare on every side).
    /// Parsed as `cave_grid`; a grid that does not parse refuses every option-on attempt at the volcano.
    pub volcano_grid: &'static str,
    /// P-MAPGEN-SIG-SCALE-4: per outlet run of the volcano drawing, in grid order (left, middle, right: ascending `gx`),
    /// the stream's heading as a grid direction (front = `(0, 1)`; turned with the drawing by `Orientation::turn`) and
    /// its path length range in tiles (inclusive; two lava tiles per path tile; measured as the oracle measures it,
    /// `volcano::length`). The owner's numbers (requirement L4; lengths by the owner decision of 2026-11-14): the side
    /// streams 45 degrees off straight ahead, 7-10 tiles; the middle one straight ahead, 10-14, strictly the longest.
    pub outlet_heading: [(i32, i32); 3],
    pub outlet_len: [(i32, i32); 3],
    /// P-MAPGEN-SIG-SCALE-4 (owner decision 2026-11-14): the Fire fan, a rectangle in front of the Fire site's inner
    /// rect (toward the coliseum, along the front axis of the site's centre tile), `fan_depth` tiles deep and as wide
    /// as the inner rect plus `fan_side` tiles on each side, clipped to the map. Camp halos (the Prefix's camp
    /// placement) and the legacy landmark stage's buildings (its protection) keep out of it; the streams and their
    /// pools run in it (`fan`).
    pub fan_depth: i32,
    pub fan_side: i32,
    /// P-MAPGEN-SIG-HOLY-1: the Holy hall's approved blueprint (owner decisions 2026-11-13: one drawing, 37 x 37, two
    /// Holy camps inside on the nave's centre line; ADR 0028 G9), the text of `hall.txt` beside this file, a byte-equal
    /// copy of `docs/landmarks/blueprints/hall.txt` (as `cave_grid`). Lines starting `;` are its legend; the rest is the
    /// 37 x 37 grid, front (toward the coliseum) at the bottom, mirror symmetric about column 18. Parsed as `cave_grid`;
    /// a grid that does not parse refuses every option-on attempt at the hall.
    pub hall_grid: &'static str,
    /// P-MAPGEN-SIG-HOLY-1 (owner decision 2026-11-13): the side (tiles) of the Holy zone's reserved site: the 37-tile
    /// hall plus the apron (`bands[0]`) on each side. The other four zones keep `site_side`.
    pub hall_site_side: i32,
    /// P-MAPGEN-SIG-HOLY-1 (owner decision 2026-11-14, 2): spawn points may stand on the hall's open floor, but no
    /// spawn-pool tile lies within this Chebyshev distance of a tile of the shrine's light ("3 tiles clear of the
    /// shrine"); the hall's walls and light are kept from the pool too (`SignatureContext::write`).
    pub shrine_clear: i32,
    /// P-LAND-FROST-1 (owner decision 2026-11-14, "the softer reservation"): the Frost lake's room, the cave's fan
    /// (`spec::Spec::fan` of the cave's entry, `spec::FanHold::Soft`): `cave_fan_depth` tiles deep in front of the Frost
    /// site's inner rect and as wide as the inner rect plus `cave_fan_side` tiles on each side. Measured as decided: 24
    /// and 0 (prototype `agent/opus/P-LAND-FROST-1-stack-measure`, seeds 1-128). The Prefix gives the room up for a
    /// round whose camps cannot be placed with it, and then does not record it (`attempt`, the soft retry).
    pub cave_fan_depth: i32,
    pub cave_fan_side: i32,
}

pub const DEFAULT_DEFINITION: Definition = Definition {
    stream: 5,
    bands: [2, 6, 14],
    bog: 5,
    lava: 1,
    ember: 4,
    ice: 6,
    outer_slack: 2.0,
    back_slack: 2.0,
    stream_min: 4,
    stream_start: 1,
    stream_bend: 2,
    front_max: 3,
    stream_gap: 3,
    start_d2: 20,
    pool_d2: &[4, 2],
    lava_budget: (200, 270),
    doorway: 2,
    lake_reach: 8,
    lake_tries: 8,
    lake_rounds: 3,
    second_share: 75,
    lake_slack: 2,
    site_side: 34,
    site_depth: 9,
    cave_grid: include_str!("cave.txt"),
    bog_grid: include_str!("bog.txt"),
    church_grid: include_str!("church.txt"),
    volcano_grid: include_str!("volcano.txt"),
    outlet_heading: [(-1, 1), (0, 1), (1, 1)],
    outlet_len: [(7, 10), (10, 14), (7, 10)],
    fan_depth: 14,
    fan_side: 14,
    hall_grid: include_str!("hall.txt"),
    hall_site_side: 41,
    shrine_clear: 3,
    cave_fan_depth: 24,
    cave_fan_side: 0,
};

/// One zone's reserved landmark site (P-MAPGEN-SIG-RESERVE-1): chosen in the Prefix, after the partition and before
/// the camps, with the option on only (`SignatureContext::reserve`). `rect` is the whole site (the landmark's footprint
/// plus its apron); `camp` is the tile of the camp placed inside it (the church's and the cave's elite camp, the Holy
/// hall's back camp), `None` for the volcano and the tree. `stream` is the zone's signature stream after the
/// reservation's draws: the Landmarks phase continues it, never re-using a draw. Generator working state only: nothing
/// reads it after the Landmarks phase and it is never exported.
#[derive(Clone, Debug, PartialEq, Eq)]
pub struct ReservedSite {
    pub zone: Zone,
    pub rect: TileRect,
    pub camp: Option<usize>,
    /// P-MAPGEN-SIG-HOLY-1: the further camps placed inside the site after `camp`, in the drawing's grid order (the
    /// Holy hall's second `E`, the front one); empty for the other four sites.
    pub extra: Vec<usize>,
    /// P-MAPGEN-SIG-HOLY-1: whether `camp` is the zone's first elite (the church's and the cave's camp), or the zone's
    /// elites follow legacy's rule among all its camps (the Holy hall's two camps; owner decision 2026-11-13). False for
    /// a site without a camp. Read by the Prefix's camp placement (`zones::camps::Reserve::elite`).
    pub elite: bool,
    pub stream: Mulberry32,
}

/// The zones that reserve a site, in the order of `AttemptState::reserved` and of placement (P-MAPGEN-SIG-HOLY-1: the
/// Holy hall last). A function, not a const: `tests/content.rs` reads consts as tunables.
pub fn reserved_zones() -> [Zone; 5] {
    [
        Zone::Fire,
        Zone::Sentenced,
        Zone::Plague,
        Zone::Frost,
        Zone::Holy,
    ]
}

/// P-MAPGEN-SIG-SCALE-1: the zones whose reserved site is relabelled to its one inside camp's territory
/// (`relabel_sites`): the Sentenced site, round which the blueprint church stands (P-MAPGEN-SIG-SCALE-2), and the Frost
/// site, round which the blueprint cave stands (public so that the church's oracle, `tests/mapgen_signature_church.rs`,
/// can read the list). `relabel_sites` does not read this list: it relabels every reserved site that carries a camp
/// (`spec::Spec::site_camps`), so the Holy site, with its two camps, is relabelled too (P-MAPGEN-SIG-HOLY-1) and a
/// later entry given site camps needs no line here. The list stays for the oracle that pins it.
pub fn relabelled_zones() -> [Zone; 2] {
    [Zone::Sentenced, Zone::Frost]
}

/// P-MAPGEN-SIG-SCALE-1 (coordinator decision 2026-11-13): before repair runs, no tile of a reserved site with an inside
/// camp belongs to another camp's territory. Every tile of such a site (the whole `rect`, apron included; any entry
/// whose table line gives it site camps) takes the inside camp's label, its index in `camps` (the prefix's) + 1. The reservation keeps
/// the other camps' halos out of the site, not their territories: without this the blueprint's walls cut those
/// territories and repair hands the pieces to camps beyond the territory radius (`Repair(RadiusPost)`). Called by the
/// pipeline on repair's copy of the territory grid on option-on attempts only (no sites otherwise: nothing changes). A
/// pure write: no draw. Public so that P-MAPGEN-SIG-SCALE-2's oracle (`tests/mapgen_signature_church.rs`) can call it.
///
/// P-MAPGEN-SIG-HOLY-1: the Holy site (the hall's, with its two camps `camp` and `extra`) is relabelled too: each tile
/// of its rect takes the label of the nearer of its camps (squared distance; the earlier, `camp`, on a tie). With one
/// inside camp that is the rule above.
pub fn relabel_sites(reserved: &[ReservedSite], camps: &[CampSite], territory: &mut [u8]) {
    for r in reserved.iter().filter(|r| r.camp.is_some()) {
        // The site's own camps (`camp`, then `extra`) found among the prefix's: (index, tile x, tile y, label).
        let own: Vec<(usize, i32, i32, u8)> = r
            .camp
            .iter()
            .chain(&r.extra)
            .filter_map(|&t| {
                let k = camps.iter().position(|c| c.site == t)?;
                let label = u8::try_from(k + 1).ok()?;
                Some((k, (t % W) as i32, (t / W) as i32, label))
            })
            .collect();
        if own.is_empty() {
            continue;
        }
        // `place_camps_reserving` keeps every other camp's halo, so its site tile, out of the rect; a camp left inside
        // would lose its own tile's territory here and fail far away (repair's safety nets admission).
        let inside: Vec<usize> = own.iter().map(|o| o.0).collect();
        debug_assert!(
            stray_camp(r, camps, &inside).is_none(),
            "relabel_sites: camp {:?} (zone {}) lies in the {:?} site {:?} of camps {inside:?}",
            stray_camp(r, camps, &inside),
            stray_camp(r, camps, &inside).map_or(0, |k| camps[k].zone),
            r.zone,
            r.rect
        );
        for y in r.rect.y..r.rect.y + r.rect.h as i32 {
            for x in r.rect.x..r.rect.x + r.rect.w as i32 {
                let Some(i) = at(x, y) else {
                    continue;
                };
                // The first of the nearest (`min_by_key` keeps the first minimum).
                if let Some(o) = own
                    .iter()
                    .min_by_key(|o| (o.1 - x).pow(2) + (o.2 - y).pow(2))
                {
                    territory[i] = o.3;
                }
            }
        }
    }
}

/// The first camp not in `inside` (indices into `camps`) whose site tile lies in site `r`'s rect; `None` when there is
/// none, as `place_camps_reserving` guarantees (`relabel_sites`'s precondition).
pub(super) fn stray_camp(r: &ReservedSite, camps: &[CampSite], inside: &[usize]) -> Option<usize> {
    let (x0, y0) = (r.rect.x, r.rect.y);
    let (x1, y1) = (x0 + r.rect.w as i32, y0 + r.rect.h as i32);
    camps.iter().enumerate().position(|(k, c)| {
        let (x, y) = ((c.site % W) as i32, (c.site / W) as i32);
        !inside.contains(&k) && x >= x0 && x < x1 && y >= y0 && y < y1
    })
}

/// The zone's one-based slot in the attempt's faction order.
pub(super) fn slot_of(order: &[FactionId], zone: Zone) -> Option<u8> {
    order
        .iter()
        .position(|f| f.as_str() == zone.as_str())
        .map(|s| (s + 1) as u8)
}

/// The front axis of a landmark drawn from the table (`stamp`) and of a reserved site's camp (`inside_camp`): the
/// dominant axis of DIR16 `f` (x on a tie), as a unit vector.
pub(super) fn axis(f: u8) -> (i32, i32) {
    let v = DIR16[usize::from(f)];
    if v[0].abs() >= v[1].abs() {
        (v[0].signum(), 0)
    } else {
        (0, v[1].signum())
    }
}

/// P-MAPGEN-SIG-SCALE-4 (owner decision 2026-11-14), P-MAPGEN-SIG-HOLY-1-fix: the fan `f` (its table entry's
/// `spec::Spec::fan`) of reserved site `r`, inclusive (x0, y0, x1, y1) clipped to the map. It lies in front of the site's
/// inner rect (the site less its apron, `bands[0]`), along the front axis read at the site's centre tile (`front`, then
/// `axis`: the tile and rule of the volcano's anchor, so the fan and the drawing's front row agree), `f.depth` tiles deep
/// and as wide as the inner rect plus `f.side` tiles on each side. The Prefix computes it once, with its own table
/// (`SignatureContext::fan`), and records it (`AttemptState::fans`); its two readers: the Prefix's camp placement keeps
/// every camp's halo out of it (`attempt`), the legacy landmark stage keeps its buildings out of the recorded fan
/// (`stage`). Nothing else is forbidden there: the volcano's streams and pools run in it.
pub(super) fn fan_rect(
    r: &ReservedSite,
    d: &Definition,
    f: spec::Fan,
) -> (usize, usize, usize, usize) {
    let b = d.bands[0];
    let (w, h) = (r.rect.w as i32, r.rect.h as i32);
    let (x0, y0, x1, y1) = (
        r.rect.x + b,
        r.rect.y + b,
        r.rect.x + w - 1 - b,
        r.rect.y + h - 1 - b,
    );
    let (depth, side) = (f.depth, f.side);
    let (a0, b0, a1, b1) = match axis(front(r.rect.x + w / 2, r.rect.y + h / 2)) {
        (0, 1) => (x0 - side, y1 + 1, x1 + side, y1 + depth),
        (0, -1) => (x0 - side, y0 - depth, x1 + side, y0 - 1),
        (1, 0) => (x1 + 1, y0 - side, x1 + depth, y1 + side),
        _ => (x0 - depth, y0 - side, x0 - 1, y1 + side),
    };
    let clip = |v: i32, n: usize| v.clamp(0, n as i32 - 1) as usize;
    (clip(a0, W), clip(b0, H), clip(a1, W), clip(b1, H))
}

/// The feature kinds of the signature landmarks T5 exempts (`t5_exempt`): the volcano with its streams and pools, the
/// church with its graveyard, the bog tree and the cave; not the Holy hall (`FeatureKind::Hall`, which writes no LOW
/// tile, so T5 never reads it), not the stylers' features (the legacy `LavaRiver`, the craters) nor the legacy landmark
/// recipes' `Landmark`. Only T5 reads this list; `SignatureContext::is_signature` is the one for every member kind.
pub fn landmark_kind(kind: FeatureKind) -> bool {
    matches!(
        kind,
        FeatureKind::Volcano
            | FeatureKind::LavaStream
            | FeatureKind::LavaPool
            | FeatureKind::Church
            | FeatureKind::Graveyard
            | FeatureKind::BogTree
            | FeatureKind::Cave
    )
}

/// P-MAPGEN-SIG-SCALE-4 (owner decision 2026-11-14): terrain rule T5 (a LOW tile lies within the snap reach of floor)
/// does not count tile `i` of `m` when it is a signature landmark's own lava: LOW, of terrain `lava` (the lava code of
/// the reader's own terrain table), owned (the feature grid) by a `landmark_kind` feature: the volcano's walled-in
/// crater lake. Every other far LOW tile still fails. The one definition the terrain gate and the certifier both read.
/// Option-off maps have no signature feature, so no option-off verdict changes.
pub fn t5_exempt(m: &GameMapData, i: usize, lava: Option<u8>) -> bool {
    m.tiles.get(i) == Some(&TILE_LOW)
        && lava.is_some_and(|lava| m.terrain.get(i) == Some(&lava))
        && m.feature_grid
            .get(i)
            .and_then(|&f| usize::from(f).checked_sub(1))
            .and_then(|k| m.features.get(k))
            .is_some_and(|f| landmark_kind(f.kind))
}

/// G4: the band of a tile relative to a zone's anchor, nearest first.
#[derive(
    Clone, Copy, Debug, PartialEq, Eq, PartialOrd, Ord, serde::Serialize, serde::Deserialize,
)]
pub enum Band {
    Core,
    Near,
    Mid,
    Far,
}

/// G4: the band of tile (x, y): its Chebyshev distance from `anchor.footprint` (0 inside it) against
/// `DEFAULT_DEFINITION.bands`. Stylers and `rh-mapdress` call this; nothing else defines the bands.
pub fn band(anchor: &ZoneAnchor, x: i32, y: i32) -> Band {
    let f = anchor.footprint;
    let (x1, y1) = (f.x + f.w as i32 - 1, f.y + f.h as i32 - 1);
    let d = (f.x - x)
        .max(x - x1)
        .max(0)
        .max((f.y - y).max(y - y1).max(0));
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
