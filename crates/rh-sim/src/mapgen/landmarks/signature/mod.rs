//! Signature landmarks inside the generator (P-MAPGEN-SIGNATURE-CORE-1; owner rules G1-G8, 2026-11-12). No legacy
//! counterpart (DEV-020): reached only through `pipeline::Options::signature` (default off), so default-mode maps,
//! parity fixtures and pinned maps never move (G8).
//!
//! The step runs in the Landmarks phase, right after the legacy landmark stage (`stage::StageContext::apply`) and
//! before the spawn preparation, so spawns, the route guard and every zone styler see the landmark as existing terrain
//! (G1, G2). It writes through the stage's own layers (`LandmarkStage::state`: tiles, features, feature owners) and
//! marks the footprint and its apron in `LandmarkStage::masks` (`boxes` and `near1`), which feed `spawns::PoolInput`,
//! `SpawnContext::style_protect` (the stylers' `protect` / `no_block`), repair's keep mask and the ground spots. It
//! draws only from its own per-zone stream, `hash_seed(seed, attempt, zone index, Definition::stream)`; the attempt's
//! main stream and the stylers' streams are untouched (G6).
//!
//! Every landmark stands in the site its zone reserved in the Prefix (P-MAPGEN-SIG-RESERVE-1): the volcano, the church,
//! the bog tree, the cave and the Holy hall (P-MAPGEN-SIG-HOLY-1) are their approved drawings, read and landed from the per-landmark table (`spec`,
//! `blueprint`, `stamp`); the volcano's three lava streams continue from its outlets (`volcano::streams`,
//! P-MAPGEN-SIG-SCALE-4). Integer geometry only.
//!
//! The table (`spec::Spec`) carries each landmark's site rules (P-MAPGEN-SIG-HOLY-1-fix): the drawing and its legend,
//! the site's side (`site_side`), the camps the site carries and whether the first is the zone's elite (`site_camps`),
//! what the spawn pool may use (`spawn`), the fan reserved in front of the site (`fan`) and the params of its own
//! feature (`params`). `reserve`, `write`, `fan`, `fan_holds_a_site_camp` and `relabel_sites` read the entry; a new
//! landmark fact of these kinds is one table line, not a zone or kind branch. Still decided by zone, older than the
//! table: where a site may stand (`reserve`: Plague's outer-third line and its deepest-square fallback) and the
//! volcano's streams (`place`, Fire only).
pub mod blueprint;
mod bog_tree;
mod cave;
mod church;
mod definition;
pub mod frame;
mod site;
pub mod spec;
#[cfg(test)]
mod tests;
mod volcano;

use crate::content::FactionId;
use crate::map::{TILE_FLOOR, TILE_LOW, TILE_WALL};
use crate::mapdata::{
    FeatureJob, FeatureKind, GameMapData, MapFeature, TilePt, TileRect, Zone, ZoneAnchor,
};
use crate::mapgen::attempt::{AttemptFrame, AttemptInputError};
use crate::mapgen::certify::rules::R;
use crate::mapgen::coliseum::{FixedLayout, CX, CY, H, W};
use crate::mapgen::spawns;
use crate::mapgen::terrain_gate;
use crate::mapgen::zones::camps::CampSite;
use crate::rng::{hash_seed, Mulberry32};

use super::recipe_support::DIR16;
use super::stage::LandmarkStage;
use blueprint::{Blueprint, Legality, Orientation, Placement, Target, Write};

use self::definition::{axis, fan_rect, slot_of};
pub use self::definition::{
    band, landmark_kind, relabel_sites, relabelled_zones, reserved_zones, t5_exempt, Band,
    Definition, ReservedSite,
};
pub(crate) use self::site::{at, front, Built, Piece, Site};
use self::site::{
    bounds, camp_halo, centred_camps, entrance_door, hall_params, mark_halo, pick, stamp, written,
};

// Stays in this file: `crates/rh-sim/tests/content.rs` reads the tunables from `signature/mod.rs` by path.
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

/// What the step placed in one attempt: the terrain layer the attempt starts its styler pass from (all zero except the
/// landmarks' own terrain, such as the bog tree's bands) and (P-MAPGEN-LANDMARK-FRAME-1) one `LandmarkFrame` per zone it
/// built, in placement order (`reserved_zones`): the one record of what was placed. The zones' anchors and the drawn
/// landmarks' landings are read through `Placed::anchors()` and `Placed::landed()`.
#[derive(Clone, Debug, PartialEq)]
pub struct Placed {
    pub terrain: Vec<u8>,
    pub frames: Vec<frame::LandmarkFrame>,
}

/// P-MAPGEN-SIG-BLUEPRINT-1: one landmark placed from its drawing: the zone, the landing (orientation, origin, box) and
/// every mark of its legend (ascending by character) with the map tiles it landed on, row-major. Generator working
/// state, derived from the frames (`Placed::landed()`); never exported.
#[derive(Clone, Debug, PartialEq, Eq)]
pub struct Landed {
    pub zone: Zone,
    pub landing: blueprint::Landing,
    pub marks: Vec<(u8, Vec<TilePt>)>,
}

/// P-MAPGEN-SIG-BLUEPRINT-2: the terrain codes (indices into the terrain catalog, `terrain_gate::DEFAULT_DEFINITION.kinds`)
/// of the catalog kinds `bog`, `lava`, `ember` and `ice`, as the context uses them (`SignatureContext::terrain_codes`).
#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub struct TerrainCodes {
    pub bog: u8,
    pub lava: u8,
    pub ember: u8,
    pub ice: u8,
}

/// The terrain catalog's codes of the kinds keyed `bog`, `lava`, `ember` and `ice` (each its index in
/// `terrain_gate::DEFAULT_DEFINITION.kinds`). Panics when the catalog lacks one: the step cannot run without them.
pub(crate) fn catalog_codes() -> TerrainCodes {
    let code = |key: &str| {
        let k = terrain_gate::DEFAULT_DEFINITION
            .kinds
            .iter()
            .position(|k| k.key == key)
            .unwrap_or_else(|| panic!("signature: no terrain kind {key:?} in the catalog"));
        u8::try_from(k).expect("signature: terrain code")
    };
    TerrainCodes {
        bog: code("bog"),
        lava: code("lava"),
        ember: code("ember"),
        ice: code("ice"),
    }
}

/// What `intact` accepts on a tile owned by a feature of one member kind: (tile, terrain), `None` terrain for any.
type Owned = Vec<(u8, Option<u8>)>;

/// What `intact` accepts on an entrance tile of one drawn landmark: exactly what the glyph of its entrance writes there
/// (one glyph write per entry, `SignatureContext::from_definition`). `write`: the (tile, terrain) written, `None` for a
/// glyph that writes nothing (floor, any terrain, as before P-MAPGEN-SIG-SCALE-4). `owner`: the feature kind that owns
/// the tile (a feature of the landmark's zone), `None` for a glyph no feature owns (no feature of the landmark may own
/// it; any other owner is judged as before, by the apron rule).
#[derive(Clone, Copy, Debug, PartialEq, Eq)]
struct Door {
    write: Option<(u8, u8)>,
    owner: Option<FeatureKind>,
}

/// Immutable per-pipeline context, reused by every attempt.
pub struct SignatureContext<'f> {
    _fixed: core::marker::PhantomData<&'f FixedLayout>,
    rules: Definition,
    /// The catalog's terrain codes (`catalog_codes`), resolved once.
    codes: TerrainCodes,
    /// Tiles no landmark wall may take: the border, the coliseum, its road (with the legacy landmark margin) and lanes.
    hard: Vec<u8>,
    /// P-MAPGEN-SIG-BLUEPRINT-1: the per-landmark table (`spec::specs`), each entry with its drawing parsed once; `None`
    /// for an entry without a drawing or with one that does not parse (which refuses every option-on attempt at that
    /// landmark, after its mirror draw: `Refusal::Signature`).
    landmarks: Vec<(spec::Spec, Option<Blueprint>)>,
    /// P-MAPGEN-SIG-BLUEPRINT-2: per member kind of the table, what `intact` accepts on the tiles its features own: for a
    /// drawing, the (tile, terrain) its legend's glyphs of that kind write (any terrain for the entry's `any_terrain`
    /// kinds); for the volcano's streams and pools, which no drawing writes, `volcano::owned`.
    owned: Vec<(FeatureKind, Owned)>,
    /// Per table entry whose drawing has an entrance, by zone: what `intact` accepts on its entrance tiles (`Door`).
    doors: Vec<(Zone, Door)>,
}

impl<'f> SignatureContext<'f> {
    pub fn new(fixed: &'f FixedLayout) -> Self {
        Self::from_definition(fixed, &DEFAULT_DEFINITION)
    }

    pub fn from_definition(fixed: &'f FixedLayout, d: &Definition) -> Self {
        Self::from_table(fixed, d, spec::specs(d))
    }

    /// P-MAPGEN-SIG-HOLY-1-fix: the context of definition `d` with the per-landmark table `table` in place of
    /// `spec::specs(d)` (`from_definition` passes that): the seam through which a test runs the reservation, the stamp,
    /// the write and the fans on a doctored entry. `table` keeps `reserved_zones` order, one entry per zone.
    pub fn from_table(fixed: &'f FixedLayout, d: &Definition, table: Vec<spec::Spec>) -> Self {
        // A pinned entry's camp is the pinned tile (`inside_camp`), and its drawing is pinned on that tile by the same
        // glyph: the two must agree, or the stamp refuses every attempt for a reason the table does not show.
        debug_assert!(
            table.iter().all(|e| match e.placement {
                blueprint::Placement::Pin(g) => e.site_camps.is_some_and(|c| c.glyph == g),
                blueprint::Placement::Centre => true,
            }),
            "from_table: a pinned entry's `site_camps` names its pin glyph"
        );
        let sd = spawns::DEFAULT_DEFINITION;
        let road = u16::from(crate::mapgen::coliseum::default_definition().ring_road)
            + u16::from(sd.landmark_road_margin);
        let mut hard: Vec<u8> = (0..W * H)
            .map(|i| {
                u8::from(
                    fixed.border[i] != 0
                        || fixed.fp[i] != 0
                        || fixed.lane_in[i] != 0
                        || u16::from(fixed.dist_fp[i]) <= road,
                )
            })
            .collect();
        for t in &fixed.lane_out {
            let m = sd.landmark_lane_margin;
            for y in t.y - m..=t.y + m {
                for x in t.x - m..=t.x + m {
                    if let Some(i) = at(x, y) {
                        hard[i] = 1;
                    }
                }
            }
        }
        let codes = catalog_codes();
        debug_assert_eq!(
            codes,
            TerrainCodes {
                bog: d.bog,
                lava: d.lava,
                ember: d.ember,
                ice: d.ice,
            },
            "signature: the definition's terrain codes are not the catalog's"
        );
        let mut owned: Vec<(FeatureKind, Owned)> = volcano::owned(&codes);
        for s in table.iter().filter(|s| s.drawing.is_some()) {
            for g in &s.legend.0 {
                let (Some(kind), Some((tile, terrain))) = (g.feature, written(g.write)) else {
                    continue;
                };
                let pair = (tile, (!s.any_terrain.contains(&kind)).then_some(terrain));
                match owned.iter_mut().find(|(k, _)| *k == kind) {
                    Some((_, pairs)) if !pairs.contains(&pair) => pairs.push(pair),
                    Some(_) => {}
                    None => owned.push((kind, vec![pair])),
                }
            }
        }
        // A member kind without a rule would pass `intact`'s owned-tile check whatever its tiles hold.
        debug_assert!(
            table
                .iter()
                .flat_map(|s| &s.members)
                .all(|m| owned.iter().any(|(k, _)| k == m)),
            "signature: a member kind with no owned-tile rule"
        );
        let mut landmarks: Vec<(spec::Spec, Option<Blueprint>)> = table
            .into_iter()
            .map(|s| {
                let drawing = s.drawing.and_then(|t| Blueprint::parse(t, &s.legend).ok());
                (s, drawing)
            })
            .collect();
        // A frame records marks by their tiles (`LandmarkFrame::marks`): a legend mark no tile of its drawing carries
        // would drop out of `Placed::landed()`.
        debug_assert!(
            landmarks
                .iter()
                .filter_map(|(_, bp)| bp.as_ref())
                .all(|bp| bp.marks().iter().all(|(_, gs)| !gs.is_empty())),
            "signature: a legend mark with no tile in its drawing"
        );
        // The entrance tiles' glyph writes, read from each drawing's legend: the volcano's outlets `O` (LOW lava, owned
        // by the volcano); the church's gate, the tree's causeways and the cave's mouth write nothing. An anchor's
        // entrance is sorted by map tile, which loses the glyph of each tile: an entry's entrance glyphs must all write
        // one thing. An entry whose entrance breaks that keeps no drawing (in every build, P-MAPGEN-SIG-HOLY-1): it
        // refuses every option-on attempt at its landmark, as a drawing that does not parse does, rather than being
        // checked against the wrong door.
        let mut doors: Vec<(Zone, Door)> = Vec::new();
        for (s, bp) in &mut landmarks {
            let Some(drawing) = bp.as_ref() else {
                continue;
            };
            match entrance_door(drawing, s.entrance) {
                Ok(Some(door)) => doors.push((s.zone, door)),
                Ok(None) => {}
                Err(()) => *bp = None,
            }
        }
        Self {
            _fixed: core::marker::PhantomData,
            rules: *d,
            codes,
            hard,
            landmarks,
            owned,
            doors,
        }
    }

    pub fn definition(&self) -> &Definition {
        &self.rules
    }

    /// P-MAPGEN-SIG-HOLY-1-fix: the fan of reserved site `r` as its table entry says (`spec::Spec::fan`), inclusive
    /// (x0, y0, x1, y1) clipped to the map; `None` for an entry without a fan (on the default table the volcano's, hard,
    /// and the cave's room, soft: P-LAND-FROST-1). The Prefix records the fans it kept (`AttemptState::fans`).
    pub fn fan(&self, r: &ReservedSite) -> Option<(usize, usize, usize, usize)> {
        let f = self.entry(r.zone)?.fan?;
        Some(fan_rect(r, &self.rules, f))
    }

    /// P-LAND-FROST-1: how reserved site `r`'s fan holds (`spec::Spec::fan_hold`), `None` when its entry has no fan. The
    /// Prefix gives up a `Soft` fan for the round when the round's camps cannot be placed with it; never a `Hard` one.
    pub fn fan_hold(&self, r: &ReservedSite) -> Option<spec::FanHold> {
        self.entry(r.zone)
            .filter(|e| e.fan.is_some())
            .map(|e| e.fan_hold)
    }

    /// P-MAPGEN-SIG-HOLY-1-fix: whether the certifier's halo of a camp a reserved site carries (`camp`, `extra`) reaches
    /// into a fan (`fan`) of another site. Those camps are fixed with the sites, before camp placement can keep them out
    /// of a fan: the Prefix fails the round when one does (the partition and the sites are drawn again; with a soft fan
    /// reserved it first tries the round without the soft fans, `hard_fan_holds_a_site_camp`). A site's own camps are not
    /// tested against its own fan: the site's drawing and its camps are placed together, so its camps are where they are
    /// by design (the cave's camp stands inside its site, behind its own room).
    pub fn fan_holds_a_site_camp(&self, reserved: &[ReservedSite]) -> bool {
        self.fan_holds_with(reserved, false)
    }

    /// P-LAND-FROST-1: `fan_holds_a_site_camp` over the `Hard` fans only (the Prefix's retry without the soft fans).
    pub fn hard_fan_holds_a_site_camp(&self, reserved: &[ReservedSite]) -> bool {
        self.fan_holds_with(reserved, true)
    }

    fn fan_holds_with(&self, reserved: &[ReservedSite], hard_only: bool) -> bool {
        let fans: Vec<(usize, (usize, usize, usize, usize))> = reserved
            .iter()
            .enumerate()
            .filter(|(_, r)| !(hard_only && self.fan_hold(r) == Some(spec::FanHold::Soft)))
            .filter_map(|(k, r)| self.fan(r).map(|f| (k, f)))
            .collect();
        if fans.is_empty() {
            return false;
        }
        fans.iter().any(|&(owner, (x0, y0, x1, y1))| {
            let mut halo = vec![0u8; W * H];
            for &t in reserved
                .iter()
                .enumerate()
                .filter(|&(k, _)| k != owner)
                .flat_map(|(_, r)| r.camp.iter().chain(&r.extra))
            {
                mark_halo(&mut halo, t);
            }
            (y0..=y1).any(|y| (x0..=x1).any(|x| halo[y * W + x] != 0))
        })
    }

    /// The table entry of `zone` (`spec::Spec`), `None` when the table has none.
    fn entry(&self, zone: Zone) -> Option<&spec::Spec> {
        self.landmarks
            .iter()
            .map(|(s, _)| s)
            .find(|s| s.zone == zone)
    }

    /// P-MAPGEN-SIG-BLUEPRINT-2 (review finding 6): the terrain codes this context writes and checks for the bog, the
    /// lava, the ember and the ice: the terrain catalog's (`catalog_codes`), looked up by key when the context is built.
    pub fn terrain_codes(&self) -> TerrainCodes {
        self.codes
    }

    /// The index of the table entry whose member kinds hold `k` (`spec::Spec::members`), `None` for a kind that is no
    /// signature landmark's.
    fn entry_of(&self, k: FeatureKind) -> Option<usize> {
        self.landmarks
            .iter()
            .position(|(s, _)| s.members.contains(&k))
    }

    /// Whether `k` is a member kind of a signature landmark.
    fn is_signature(&self, k: FeatureKind) -> bool {
        self.entry_of(k).is_some()
    }

    /// Whether `k`'s apron and keep mask follow its own tiles rather than its box (`spec::Spec::by_tile`: the lava
    /// streams and pools).
    fn by_tile(&self, k: FeatureKind) -> bool {
        self.landmarks.iter().any(|(s, _)| s.by_tile.contains(&k))
    }

    /// P-MAPGEN-SIG-RESERVE-1: each reserving zone's signature stream, `hash_seed(seed, attempt, zone slot, stream)`, in
    /// `reserved_zones` order: the streams the Landmarks phase draws from, begun in the Prefix.
    pub fn streams(&self, seed: i64, attempt: u64, order: &[FactionId]) -> Option<Vec<Mulberry32>> {
        reserved_zones()
            .iter()
            .map(|&z| {
                slot_of(order, z).map(|slot| {
                    Mulberry32::new(hash_seed(
                        seed as u32,
                        attempt as u32,
                        u32::from(slot),
                        self.rules.stream,
                    ))
                })
            })
            .collect()
    }

    /// P-MAPGEN-SIG-RESERVE-1: one prefix round's sites, in `reserved_zones` order, on the round's partition `zone`. A
    /// zone's candidates are the squares of its table entry's `site_side` (`spec::Spec::site_side`) wholly of its tiles,
    /// off `hard` (border, coliseum, road, lanes) and off the open map corners, whose centre point lies beyond the zone's
    /// line: Fire, Sentenced, Frost and Holy the zone centroid's distance from the map centre plus `back_slack`, Plague
    /// the start of its outer third plus `outer_slack`. An entry with `site_camps` also needs its camps: a pinned entry
    /// (`Placement::Pin`: the church, the cave) the one camp tile `inside_camp`, a centred entry (the hall) the landed
    /// tiles of the camp glyph (`centred_camps`: `camp` the first in grid order, `extra` the rest); the first is the
    /// zone's elite when `SiteCamps::elite` says so, else the zone's elites follow legacy's rule. A site's camp tile is
    /// not tested for legality here: when it is not a legal camp tile `place_camps_reserving` fails the round (not
    /// seen on the shipped table: the site is inside its zone and off the hard mask; nothing proves it cannot happen). One uniform
    /// draw among the candidates, the zone's one draw of the round on its stream; without a candidate, Plague takes the
    /// deepest square still beyond its centroid (largest centre distance, first in row order), drawing nothing. `None`
    /// when a zone has no square: the round fails as a camp failure does. Draws only from `streams` (`reserved_zones`
    /// order).
    pub fn reserve(
        &self,
        zone: &[u8],
        order: &[FactionId],
        streams: &mut [Mulberry32],
    ) -> Option<Vec<ReservedSite>> {
        let d = &self.rules;
        let co = spawns::DEFAULT_DEFINITION.corner_open;
        let corner = |x: usize, y: usize| x.min(W - 1 - x) <= co && y.min(H - 1 - y) <= co;
        let from_centre = |x: f64, y: f64| {
            let (dx, dy) = (x - W as f64 / 2.0, y - H as f64 / 2.0);
            crate::math::sqrt(dx * dx + dy * dy)
        };
        // Summed-area table of the tiles a site may not hold.
        let stride = W + 1;
        let mut bad = vec![0i32; stride * (H + 1)];
        let mut out = Vec::with_capacity(reserved_zones().len());
        for (k, &z) in reserved_zones().iter().enumerate() {
            let (entry, drawing) = self.landmarks.iter().find(|(s, _)| s.zone == z)?;
            let side = usize::try_from(entry.site_side)
                .ok()
                .filter(|&s| s > 0 && s <= W.min(H))?;
            let half = side as f64 / 2.0;
            let slot = slot_of(order, z)?;
            let (mut sx, mut sy, mut n) = (0i64, 0i64, 0i64);
            let (mut lo, mut hi) = (f64::MAX, 0.0f64);
            for y in 0..H {
                for x in 0..W {
                    let i = y * W + x;
                    let no = zone[i] != slot || self.hard[i] != 0 || corner(x, y);
                    bad[(y + 1) * stride + x + 1] =
                        i32::from(no) + bad[y * stride + x + 1] + bad[(y + 1) * stride + x]
                            - bad[y * stride + x];
                    if zone[i] == slot {
                        sx += 2 * x as i64 + 1;
                        sy += 2 * y as i64 + 1;
                        n += 1;
                        let r = from_centre(x as f64 + 0.5, y as f64 + 0.5);
                        lo = lo.min(r);
                        hi = hi.max(r);
                    }
                }
            }
            if n == 0 {
                return None;
            }
            let centroid = from_centre(sx as f64 / (2 * n) as f64, sy as f64 / (2 * n) as f64);
            let line = if z == Zone::Plague {
                lo + (hi - lo) * 2.0 / 3.0 + d.outer_slack
            } else {
                centroid + d.back_slack
            };
            // The site's camps (`spec::Spec::site_camps`): a pinned entry's one camp is `inside_camp`'s tile, a centred
            // entry's are the landed tiles of its camp glyph (the glyph's grid tiles in grid order, and the parsed
            // drawing they are read from: the reservation fails, as the stamp would, when the entry has none).
            let camp_marks = match (entry.site_camps, entry.placement) {
                (Some(sc), Placement::Centre) => {
                    let bp = drawing.as_ref()?;
                    Some((bp, bp.tiles_of(sc.glyph)))
                }
                _ => None,
            };
            let pinned = entry.site_camps.is_some() && matches!(entry.placement, Placement::Pin(_));
            let mut candidates: Vec<(usize, usize, Vec<usize>)> = Vec::new();
            let mut deepest: Option<(f64, usize, usize)> = None;
            for y in 0..=H - side {
                for x in 0..=W - side {
                    let held = bad[(y + side) * stride + x + side] + bad[y * stride + x]
                        - bad[y * stride + x + side]
                        - bad[(y + side) * stride + x];
                    if held != 0 {
                        continue;
                    }
                    let pin = if pinned {
                        match self.inside_camp(x as i32, y as i32, entry.site_side) {
                            Some(c) => Some(c),
                            None => continue,
                        }
                    } else {
                        None
                    };
                    let r = from_centre(x as f64 + half, y as f64 + half);
                    if r > line {
                        let camps = match &camp_marks {
                            Some((bp, marks)) => {
                                match centred_camps(bp, marks, x as i32, y as i32, side as i32, d) {
                                    Some(c) => c,
                                    None => continue,
                                }
                            }
                            None => pin.into_iter().collect(),
                        };
                        candidates.push((x, y, camps));
                    } else if z == Zone::Plague
                        && r >= centroid
                        && deepest.is_none_or(|(best, _, _)| r > best)
                    {
                        deepest = Some((r, x, y));
                    }
                }
            }
            let rng = &mut streams[k];
            let (x, y, camps) = if candidates.is_empty() {
                let (_, x, y) = deepest?;
                (x, y, Vec::new())
            } else {
                let n = pick(rng, candidates.len());
                candidates.swap_remove(n)
            };
            // `camp` the first, `extra` the rest; the first is the zone's first elite when the entry says so (otherwise
            // the zone's elites follow legacy's rule among all its camps: the hall, owner decision 2026-11-13).
            let (camp, extra) = match camps.split_first() {
                Some((&first, rest)) => (Some(first), rest.to_vec()),
                None => (None, Vec::new()),
            };
            let elite = camp.is_some() && entry.site_camps.is_some_and(|sc| sc.elite);
            out.push(ReservedSite {
                zone: z,
                rect: TileRect {
                    x: x as i32,
                    y: y as i32,
                    w: side as u32,
                    h: side as u32,
                },
                camp,
                extra,
                elite,
                stream: rng.clone(),
            });
        }
        Some(out)
    }

    /// The camp tile of a pinned entry's site (the church's or the cave's) of side `side` with top-left (x, y): on the
    /// site's centre line along the door axis (`axis` of DIR16 from the site's centre tile toward the coliseum),
    /// `site_depth` tiles from the far edge. Offsets from (x, y) at side 34 and depth 9: (9, 17) with the coliseum
    /// toward +x, (24, 17) toward -x, (17, 9) toward +y, (17, 24) toward -y.
    ///
    /// `None` when the camp tile's own door axis (the church's and the cave's rule) differs. A guard only, not a filter
    /// on where sites go: the camp moves deeper along the axis that already dominates, which cannot turn it, and at these
    /// numbers it refuses none of the 16,129 square positions on the 160x160 map (P-MAPGEN-SIG-RESERVE-1 review).
    fn inside_camp(&self, x: i32, y: i32, side: i32) -> Option<usize> {
        let depth = self.rules.site_depth;
        let (mx, my) = (x + side / 2, y + side / 2);
        let u = axis(front(mx, my));
        let along = |lo: i32, sign: i32| {
            if sign > 0 {
                lo + depth
            } else {
                lo + side - 1 - depth
            }
        };
        let (tx, ty) = if u.0 != 0 {
            (along(x, u.0), my)
        } else {
            (mx, along(y, u.1))
        };
        if axis(front(tx, ty)) != u {
            return None;
        }
        at(tx, ty)
    }

    /// Places each zone's signature landmark (Fire: the volcano with its streams and pools; Sentenced: the church and its
    /// graveyard; Plague: the bog tree; Frost: the cave; Holy: the hall) into the stage's layers and masks, in that order
    /// (`place`). Each
    /// zone's table entry (`spec::specs`) is drawn by the generic `stamp`, and its landing is recorded in
    /// `Placed::landed`; the volcano's streams continue from its outlets (`volcano::streams`). Only the volcano and the
    /// bog tree keep their tiles out of the aprons marked before them (`Site::ground`: their legends'
    /// `Legality::WallGround`, and the streams' own test); the church, the
    /// cave and the hall place their walls on any free wall tile (`Site::wall`). The rule that no landmark lies in another's apron is
    /// the end-of-attempt discard (`intact`). `Ok(None)` refuses the attempt (G5: a zone that cannot hold its landmark is
    /// never built without it; the pipeline tries the next attempt), and so does a prefix without a zone's reserved site
    /// (`begin_attempt`'s, the option-off prefix): every landmark is built inside its site.
    pub fn apply(
        &self,
        frame: &AttemptFrame,
        stage: &mut LandmarkStage,
    ) -> Result<Option<Placed>, AttemptInputError> {
        let halo = camp_halo(frame);
        let mut terrain = vec![0u8; W * H];
        let mut frames = Vec::with_capacity(reserved_zones().len());
        for zone in reserved_zones() {
            let Some(f) = self.place(frame, stage, &halo, &mut terrain, zone) else {
                return Ok(None);
            };
            frames.push(f);
        }
        Ok(Some(Placed { terrain, frames }))
    }

    /// P-MAPGEN-SIG-BLUEPRINT-2 (review finding 5): `apply` for the one landmark of `zone` alone, as if the zones placed
    /// before it in `apply` had placed nothing: the same site, stream, draws and checks (`place`), its features written
    /// into `stage`, and a `Placed` holding only its terrain and its frame (built as `apply` builds it). A test seam: what
    /// each landmark's own oracle pins without depending on the others. `Ok(None)` where `apply` would refuse at this
    /// zone, and for a zone that reserves no site.
    pub fn apply_only(
        &self,
        frame: &AttemptFrame,
        stage: &mut LandmarkStage,
        zone: Zone,
    ) -> Result<Option<Placed>, AttemptInputError> {
        if !reserved_zones().contains(&zone) {
            return Ok(None);
        }
        let halo = camp_halo(frame);
        let mut terrain = vec![0u8; W * H];
        Ok(self
            .place(frame, stage, &halo, &mut terrain, zone)
            .map(|f| Placed {
                terrain,
                frames: vec![f],
            }))
    }

    /// One zone's landmark in its reserved site, continuing the zone's signature stream the Prefix began there
    /// (P-MAPGEN-SIG-RESERVE-1): drawn from its table entry by `stamp` (one draw, the mirror), for the volcano then its
    /// three streams and pools laid from its outlet runs (`volcano::streams`: one draw per run, in grid order) before
    /// anything is written; its features written into `stage` and their terrain into `terrain`. Its frame (P-MAPGEN-
    /// LANDMARK-FRAME-1): the anchor, the landing and the landed marks, each written feature's kind and box
    /// in write order, and every written tile ascending (the last writer's tile and terrain where pieces overlap, as the
    /// layers hold them); `None` refuses (no slot, no reserved site, or the site cannot hold the landmark). `halo` is
    /// every prefix camp's certifier halo (`camp_halo`).
    fn place(
        &self,
        frame: &AttemptFrame,
        stage: &mut LandmarkStage,
        halo: &[u8],
        terrain: &mut [u8],
        zone: Zone,
    ) -> Option<frame::LandmarkFrame> {
        let prefix = &frame.state;
        let slot = slot_of(&prefix.order, zone)?;
        let r = prefix.reserved.iter().find(|r| r.zone == zone)?;
        let (spec, drawing) = self.landmarks.iter().find(|(s, _)| s.zone == zone)?;
        let mut rng = r.stream.clone();
        // The reserved site less its apron.
        let b = self.rules.bands[0];
        let inner = TileRect {
            x: r.rect.x + b,
            y: r.rect.y + b,
            w: r.rect.w.saturating_sub(2 * b as u32),
            h: r.rect.h.saturating_sub(2 * b as u32),
        };
        let corners = (
            inner.x,
            inner.y,
            inner.x + inner.w as i32 - 1,
            inner.y + inner.h as i32 - 1,
        );
        let site = Site {
            ctx: self,
            zone: &prefix.partition.zone,
            slot,
            tiles: &stage.state.tiles,
            owner: &stage.state.feature_owner,
            protect: &stage.protect,
            apron: &stage.masks.near1,
            halo,
            inner: Some(corners),
        };
        let target = Target {
            inner,
            camp: r.camp.map(|t| ((t % W) as i32, (t / W) as i32)),
        };
        let (mut built, landing, marks) = stamp(&site, &mut rng, spec, drawing.as_ref(), &target)?;
        if zone == Zone::Fire {
            // P-MAPGEN-SIG-SCALE-4: the three streams continue from the cone's outlet runs, before anything is written
            // (after the write, `near1` holds the cone's own apron). They leave the site: their `Site` is unconfined.
            let open = Site {
                inner: None,
                ..site
            };
            let bp = drawing.as_ref()?;
            volcano::streams(&open, &mut rng, bp, spec, &landing, &mut built)?;
        }
        let landing = Some(landing);
        let mut features = Vec::with_capacity(built.pieces.len());
        let mut wrote: Vec<(u32, u8, u8)> = Vec::new();
        for piece in &built.pieces {
            features.push((piece.kind, self.write(stage, terrain, zone, piece)));
            wrote.extend(piece.cells.iter().map(|&(i, t, k)| (i as u32, t, k)));
        }
        // Ascending by tile, one entry per tile: the stable sort keeps write order among a tile's entries, and the last
        // one (the layers' value) replaces the earlier.
        wrote.sort_by_key(|c| c.0);
        let mut cells: Vec<(u32, u8, u8)> = Vec::with_capacity(wrote.len());
        for c in wrote {
            match cells.last_mut() {
                Some(last) if last.0 == c.0 => *last = c,
                _ => cells.push(c),
            }
        }
        Some(frame::LandmarkFrame {
            anchor: built.anchor,
            landing,
            marks,
            features,
            cells,
        })
    }

    /// Writes piece `p` of zone `zone`'s landmark into the stage (tiles, owners, masks, the feature) and its terrain
    /// into `terrain`; the written feature's box.
    fn write(
        &self,
        stage: &mut LandmarkStage,
        terrain: &mut [u8],
        zone: Zone,
        p: &Piece,
    ) -> TileRect {
        let id = u16::try_from(stage.state.features.len() + 1).expect("feature id");
        let bbox = bounds(p.cells.iter().map(|c| c.0));
        for &(i, tile, kind) in &p.cells {
            stage.state.tiles[i] = tile;
            terrain[i] = kind;
            stage.state.feature_owner[i] = id;
        }
        let b = self.rules.bands[0];
        let entry = self.entry(zone);
        let (x1, y1) = (bbox.x + bbox.w as i32 - 1, bbox.y + bbox.h as i32 - 1);
        if self.by_tile(p.kind) {
            // A stream's or pool's box is a long diagonal: its apron and keep mask follow its own tiles.
            for &(i, _, _) in &p.cells {
                stage.masks.boxes[i] = 1;
                let (cx, cy) = ((i % W) as i32, (i / W) as i32);
                for y in cy - b..=cy + b {
                    for x in cx - b..=cx + b {
                        if let Some(j) = at(x, y) {
                            stage.masks.near1[j] = 1;
                        }
                    }
                }
            }
        } else if let Some(spec::SpawnRule::OpenFloor { by, .. }) = entry.map(|s| s.spawn) {
            // P-MAPGEN-SIG-HOLY-1 (owner decision 2026-11-14, 2), P-MAPGEN-SIG-HOLY-1-fix: the entry opens its floor to
            // spawn points. Its box is the keep mask (`boxes`); the spawn pool and its halo (`near1`) keep off the ring
            // round the box, the piece's written tiles and every tile within `by` of a tile of the entry's `clear_of`
            // glyph (`Piece::clear`, found by its glyph, whatever it writes).
            for y in bbox.y - b..=y1 + b {
                for x in bbox.x - b..=x1 + b {
                    if let Some(i) = at(x, y) {
                        if x >= bbox.x && x <= x1 && y >= bbox.y && y <= y1 {
                            stage.masks.boxes[i] = 1;
                        } else {
                            stage.masks.near1[i] = 1;
                        }
                    }
                }
            }
            for &(i, _, _) in &p.cells {
                stage.masks.near1[i] = 1;
            }
            for &i in &p.clear {
                let (cx, cy) = ((i % W) as i32, (i / W) as i32);
                for y in cy - by..=cy + by {
                    for x in cx - by..=cx + by {
                        if let Some(j) = at(x, y) {
                            stage.masks.near1[j] = 1;
                        }
                    }
                }
            }
        } else {
            for y in bbox.y - b..=y1 + b {
                for x in bbox.x - b..=x1 + b {
                    if let Some(i) = at(x, y) {
                        stage.masks.near1[i] = 1;
                        if x >= bbox.x && x <= x1 && y >= bbox.y && y <= y1 {
                            stage.masks.boxes[i] = 1;
                        }
                    }
                }
            }
        }
        stage.state.features.push(MapFeature {
            kind: p.kind,
            variant: String::new(),
            zone,
            jobs: vec![p.job],
            ax: p.anchor.0,
            ay: p.anchor.1,
            p: match entry {
                Some(e) if e.kind == p.kind && e.params == spec::FeatureParams::Hall => {
                    hall_params(p, bbox)
                }
                _ => Vec::new(),
            },
            bbox,
            tiles: p.cells.len() as u32,
        });
        bbox
    }

    /// Writes the finished map's anchors (G3): each frame's anchor as the Landmarks phase placed it (P-MAPGEN-LANDMARK-
    /// FRAME-1: no footprint is re-read from the finished features; records keep every landmark feature's box), in
    /// zone-code order. Since P-MAPGEN-SIG-HOLY-1 the Holy hall is one of them (the Holy styler places no hall of its own
    /// in signature mode); no anchor is read from a styler's feature.
    pub fn finish(&self, map: &mut GameMapData, frames: &[frame::LandmarkFrame]) {
        let mut out: Vec<ZoneAnchor> = frames.iter().map(|f| f.anchor.clone()).collect();
        out.sort_by_key(|a| a.zone.code());
        map.anchors = out;
    }

    /// The finished map still holds every signature landmark as it was placed (G5: later steps may have taken nothing
    /// of it): every tile a member feature owns lies in the feature's zone and holds what the table writes there
    /// (`owned`: the legend's walls, LOW and floor with their terrain; the streams' and pools' list, `volcano::owned`),
    /// every entrance tile holds exactly what its drawing wrote there (`doors_intact`: the volcano's outlets LOW lava owned
    /// by the volcano, the other entrances floor no feature of their landmark owns); no styler feature in an apron; no
    /// landmark's tiles in another landmark's apron, either way round
    /// (`aprons_apart`). A pure predicate: it draws nothing and changes nothing. The stylers' own end-of-attempt rules
    /// (the Frost lake, the Plague pools) are not judged here: the pipeline runs them after this and the certifier
    /// (`style_zones::guarantees`, P-MAPGEN-GUARANTEES-1).
    pub fn intact(&self, map: &GameMapData) -> bool {
        let rules: Vec<Option<&Owned>> = map
            .features
            .iter()
            .map(|f| {
                self.owned
                    .iter()
                    .find(|(kind, _)| *kind == f.kind)
                    .map(|(_, pairs)| pairs)
            })
            .collect();
        let owned_ok = (0..map.feature_grid.len()).all(|i| {
            let id = usize::from(map.feature_grid[i]);
            let Some(k) = id.checked_sub(1).filter(|&k| k < map.features.len()) else {
                return true;
            };
            let Some(pairs) = rules[k] else {
                return true;
            };
            let (t, terrain) = (map.tiles[i], map.terrain[i]);
            map.region_grid[i] == map.features[k].zone.code()
                && pairs
                    .iter()
                    .any(|&(tile, want)| tile == t && want.is_none_or(|w| w == terrain))
        });
        let door_ok = self.doors_intact(map);
        // Styler floor claims (`style_runtime` transactions) do not read `no_block`: a styler feature owning a tile of
        // a landmark's apron discards the map (G1: steps 3 and 4 build around the landmark, never into it).
        let b = self.rules.bands[0];
        let mut apron = vec![false; map.feature_grid.len()];
        let mut mark = |x0: i32, y0: i32, x1: i32, y1: i32| {
            for y in y0..=y1 {
                for x in x0..=x1 {
                    if let Some(i) = at(x, y) {
                        apron[i] = true;
                    }
                }
            }
        };
        let signature: Vec<bool> = map
            .features
            .iter()
            .map(|f| self.is_signature(f.kind))
            .collect();
        for (k, f) in map.features.iter().enumerate() {
            if !signature[k] {
                continue;
            }
            if self.by_tile(f.kind) {
                for i in (0..map.feature_grid.len())
                    .filter(|&i| usize::from(map.feature_grid[i]) == k + 1)
                {
                    let (x, y) = ((i % W) as i32, (i / W) as i32);
                    mark(x - b, y - b, x + b, y + b);
                }
            } else {
                let (x1, y1) = (
                    f.bbox.x + f.bbox.w as i32 - 1,
                    f.bbox.y + f.bbox.h as i32 - 1,
                );
                mark(f.bbox.x - b, f.bbox.y - b, x1 + b, y1 + b);
            }
        }
        let apron_ok = (0..apron.len()).filter(|&i| apron[i]).all(|i| {
            usize::from(map.feature_grid[i])
                .checked_sub(1)
                .and_then(|k| map.features.get(k).map(|g| (k, g)))
                .is_none_or(|(k, g)| {
                    signature[k]
                        || matches!(
                            g.kind,
                            FeatureKind::Landmark | FeatureKind::Infill | FeatureKind::Bush
                        )
                })
        });
        owned_ok && door_ok && apron_ok && self.aprons_apart(map)
    }

    /// `intact`'s entrance check: every entrance tile of every anchor holds exactly what its landmark's drawing wrote
    /// there (`Door`, from the entry's legend): the volcano's outlets LOW with the lava terrain, owned by a volcano
    /// feature of the anchor's zone; the church's gate, the tree's causeways and the cave's mouth floor that no feature
    /// of their landmark owns, and (P-MAPGEN-SIG-HOLY-1) the hall's nave floor that the hall does not own. An anchor
    /// without a drawn entrance keeps the rule from before P-MAPGEN-SIG-SCALE-4: floor.
    fn doors_intact(&self, map: &GameMapData) -> bool {
        map.anchors.iter().all(|a| {
            let members = self
                .landmarks
                .iter()
                .find(|(s, _)| s.zone == a.zone)
                .map(|(s, _)| &s.members[..])
                .unwrap_or(&[]);
            let door = self
                .doors
                .iter()
                .find(|(z, _)| *z == a.zone)
                .map(|&(_, d)| d);
            a.entrance.iter().all(|p| {
                at(p.x, p.y).is_some_and(|i| {
                    let Some(d) = door else {
                        return map.tiles[i] == TILE_FLOOR;
                    };
                    let owner = usize::from(map.feature_grid[i])
                        .checked_sub(1)
                        .and_then(|k| map.features.get(k));
                    let holds = match d.write {
                        Some((tile, terrain)) => map.tiles[i] == tile && map.terrain[i] == terrain,
                        None => map.tiles[i] == TILE_FLOOR,
                    };
                    let owned = match d.owner {
                        Some(kind) => owner.is_some_and(|f| f.kind == kind && f.zone == a.zone),
                        None => owner.is_none_or(|f| !members.contains(&f.kind)),
                    };
                    holds && owned
                })
            })
        })
    }

    /// G1, G5 (P-MAPGEN-SIG-FROST-2): no tile of one landmark lies in another landmark's apron, for every ordered pair
    /// of the five. At placement only the volcano and the bog tree keep out of the aprons marked before them
    /// (`Site::ground`); the church and the cave read no apron (`Site::wall` only), and no landmark keeps the earlier
    /// ones out of its own apron. This check is the rule for all five, and symmetric, so the placement order decides
    /// nothing. The landmarks: each table entry's member features (`spec::Spec::members`: the volcano with its streams
    /// and pools, whose by-tile aprons this check skips, so its apron is the cone's box; the church with its graveyard;
    /// the bog tree; the cave; the Holy hall, P-MAPGEN-SIG-HOLY-1). An apron is each of the landmark's boxes grown by the
    /// core band.
    fn aprons_apart(&self, map: &GameMapData) -> bool {
        // Each feature's landmark: its table entry (`spec::Spec::members`).
        let landmark: Vec<Option<usize>> =
            map.features.iter().map(|f| self.entry_of(f.kind)).collect();
        let b = self.rules.bands[0];
        map.features.iter().enumerate().all(|(k, f)| {
            let Some(host) = landmark[k] else {
                return true;
            };
            if self.by_tile(f.kind) {
                return true;
            }
            let (x1, y1) = (
                f.bbox.x + f.bbox.w as i32 - 1,
                f.bbox.y + f.bbox.h as i32 - 1,
            );
            (f.bbox.y - b..=y1 + b).all(|y| {
                (f.bbox.x - b..=x1 + b).all(|x| {
                    at(x, y)
                        .and_then(|i| usize::from(map.feature_grid[i]).checked_sub(1))
                        .and_then(|g| landmark.get(g).copied().flatten())
                        .is_none_or(|guest| guest == host)
                })
            })
        })
    }
}
