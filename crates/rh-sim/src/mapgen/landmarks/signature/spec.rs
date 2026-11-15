//! P-MAPGEN-SIG-BLUEPRINT-1: the per-landmark table (architecture review 2026-11-13, finding 2). One `Spec` per
//! signature landmark, in the order `apply` places them (`reserved_zones`): what it is, its drawing and legend, the
//! features it writes and in which order, how it is placed and what its entrance is. The generic stamp reads only this
//! table; nothing per landmark is written by hand. A function, not a const: `tests/content.rs` reads consts as
//! tunables.
//!
//! Each legend is today's reading of its drawing (the legend lines of the `*.txt` files say the same in prose): what
//! each character writes, which feature owns it and which `Site` test it must pass. Where the landmarks differ (the
//! tree's tiles keep out of earlier aprons, the cave's ice is floor, the church's fence is LOW), the difference is a
//! glyph here, not code.
use super::blueprint::{EntranceRule, Glyph, Legality, Legend, Placement, Write};
use super::{catalog_codes, Definition};
use crate::mapdata::{FeatureJob, FeatureKind, Zone};
use crate::mapgen::certify::rules::R;

/// Where a feature's anchor point (`MapFeature::ax`, `ay`) lies.
#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub enum FeatureAnchor {
    /// The landmark anchor's centre: the pinned tile (`Placement::Pin`) or the site's centre tile (`Placement::Centre`).
    Centre,
    /// The middle tile of the entrance as drawn (grid order, index `len / 2`), landed: the graveyard's (the main gate's
    /// middle tile).
    EntranceMiddle,
}

/// One feature a landmark writes: its kind, job and anchor point. The tiles it owns are the drawing's tiles whose
/// glyph names this kind.
#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub struct FeatureSpec {
    pub kind: FeatureKind,
    pub job: FeatureJob,
    pub anchor: FeatureAnchor,
}

/// P-MAPGEN-SIG-HOLY-1-fix: the camps a reserved site carries, fixed with the site in the Prefix before camp placement.
/// With `Placement::Pin(glyph)` the site's one camp is the pinned tile (`site_depth` from the site's far edge, on its
/// centre line); with `Placement::Centre` the site's camps are the landed tiles of `glyph` (grid order: the first is
/// `ReservedSite::camp`, the rest `ReservedSite::extra`) where the drawing lands centred, unmirrored, in the site's inner
/// rect. `elite`: whether the first camp is the zone's first elite (`ReservedSite::elite`); when false the zone's elites
/// follow legacy's rule among all its camps.
#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub struct SiteCamps {
    /// The camp glyph. Read for a centred entry (its landed tiles are the camps). Unused for a `Placement::Pin` entry,
    /// whose camp is the pinned tile; there it must equal the pin glyph (`SignatureContext::from_table` checks it).
    pub glyph: u8,
    pub elite: bool,
}

/// P-MAPGEN-SIG-HOLY-1-fix: which of a landmark's tiles the spawn pool may use (what `SignatureContext::write` marks in
/// `LandmarkStage::masks`: `boxes` the keep mask, `near1` the tiles closed to the spawn pool). Applies to the entry's
/// box-shaped features (not its `by_tile` kinds, whose masks follow their own tiles).
#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub enum SpawnRule {
    /// The whole box and its apron (`bands[0]` tiles round it) are closed to the spawn pool; the box is the keep mask.
    Box,
    /// The box is the keep mask, and its open floor is allowed: closed are the tiles the feature writes, the ring of
    /// `bands[0]` tiles round the box, and every tile within `by` tiles (Chebyshev) of a tile of glyph `clear_of` (found
    /// by its glyph in the drawing, not by what it writes).
    OpenFloor { clear_of: u8, by: i32 },
}

/// P-MAPGEN-SIG-HOLY-1-fix: a fan reserved in front of a site's inner rect (toward the coliseum, along the front axis
/// read at the site's centre tile), `depth` tiles deep and as wide as the inner rect plus `side` tiles on each side,
/// clipped to the map (`SignatureContext::fan`). Camp halos and the legacy landmark stage's buildings keep out of it.
#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub struct Fan {
    pub depth: i32,
    pub side: i32,
}

/// P-MAPGEN-SIG-HOLY-1-fix: how the params of a landmark's own feature (`MapFeature::p`) are derived from what the step
/// wrote.
#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub enum FeatureParams {
    /// No params.
    None,
    /// The legacy `Hall` row the certifier (`feature-params`) and the client read: [axis, pitch, radius, piers, bays]
    /// (`hall_params`).
    Hall,
}

/// One signature landmark.
#[derive(Clone, Debug, PartialEq, Eq)]
pub struct Spec {
    pub zone: Zone,
    /// The anchor's kind (`ZoneAnchor::kind`).
    pub kind: FeatureKind,
    /// The drawing's text (a `Definition` grid field); `None` refuses every option-on attempt at the landmark (every
    /// entry has one since P-MAPGEN-SIG-SCALE-4).
    pub drawing: Option<&'static str>,
    pub legend: Legend,
    /// The features written, in write order (the first is the anchor's feature).
    pub features: Vec<FeatureSpec>,
    pub placement: Placement,
    pub entrance: EntranceRule,
    /// Whether the `Definition::doorway` tiles beyond each entrance tile, along the front, must be `Site::open`.
    pub doorway: bool,
    /// P-MAPGEN-SIG-BLUEPRINT-2: every feature kind that belongs to this landmark (its own features and, for the volcano,
    /// its streams and pools): what `SignatureContext::is_signature`, `aprons_apart` and the owned-tile part of `intact`
    /// read. A kind belongs to one entry only.
    pub members: Vec<FeatureKind>,
    /// P-MAPGEN-SIG-BLUEPRINT-2: the member kinds whose apron and keep mask follow their own tiles rather than their box
    /// (the volcano's lava streams and pools): what `SignatureContext::by_tile` reads.
    pub by_tile: Vec<FeatureKind>,
    /// P-MAPGEN-SIG-BLUEPRINT-2: the member kinds whose owned tiles `intact` checks by tile only, any terrain: the church
    /// building, whose end-of-attempt check has always read its wall tiles only (the legend writes terrain 0). Kept
    /// exactly: a stricter check would discard more maps and move attempts; tightening it is a behaviour change.
    pub any_terrain: Vec<FeatureKind>,
    /// P-MAPGEN-SIG-HOLY-1-fix: the side (tiles) of the square site the zone reserves in the Prefix (the drawing plus
    /// the apron, `bands[0]`, on each side): `Definition::site_side` for the four, `Definition::hall_site_side` for the
    /// hall. `SignatureContext::reserve` reads it.
    pub site_side: i32,
    /// P-MAPGEN-SIG-HOLY-1-fix: the camps the site carries (`SiteCamps`); `None` for a site without a camp (the volcano,
    /// the tree). `SignatureContext::reserve` reads it.
    pub site_camps: Option<SiteCamps>,
    /// P-MAPGEN-SIG-HOLY-1-fix: what the spawn pool may use of the landmark (`SpawnRule`). `SignatureContext::write`
    /// reads it.
    pub spawn: SpawnRule,
    /// P-MAPGEN-SIG-HOLY-1-fix: the fan reserved in front of the site (`Fan`); `None` without one. `SignatureContext::fan`
    /// reads it.
    pub fan: Option<Fan>,
    /// P-MAPGEN-SIG-HOLY-1-fix: the params the entry's own feature (the one of kind `Spec::kind`) is written with
    /// (`MapFeature::p`, `FeatureParams`). `SignatureContext::write` reads it.
    pub params: FeatureParams,
}

/// A glyph that writes `write`, owned by `feature`, on a tile passing `legality`.
fn solid(ch: u8, write: Write, feature: FeatureKind, legality: Legality) -> Glyph {
    Glyph {
        ch,
        write,
        feature: Some(feature),
        legality,
        mark: false,
    }
}

/// Plain floor, or a decoration the generator does not name (the church's lantern posts `*`, the tree's causeways
/// `:`): writes nothing; the tile must be `Site::floor`.
fn plain(ch: u8) -> Glyph {
    Glyph {
        ch,
        write: Write::Nothing,
        feature: None,
        legality: Legality::Floor,
        mark: false,
    }
}

/// A named mark on plain floor (`E` the camp tile, `D` a door or mouth).
fn mark(ch: u8) -> Glyph {
    Glyph {
        mark: true,
        ..plain(ch)
    }
}

/// The landmark's own feature: job `Landmark`, anchor point the landmark's centre.
fn landmark(kind: FeatureKind) -> FeatureSpec {
    FeatureSpec {
        kind,
        job: FeatureJob::Landmark,
        anchor: FeatureAnchor::Centre,
    }
}

/// The table for definition `d`, one entry per landmark in `reserved_zones` order (Fire, Sentenced, Plague, Frost,
/// Holy). The terrain codes the legends write are the terrain catalog's (`super::catalog_codes`, `light_code`), not
/// `d`'s mirrors.
pub fn specs(d: &Definition) -> Vec<Spec> {
    use FeatureKind::{BogTree, Cave, Church, Graveyard, Hall, LavaPool, LavaStream, Volcano};
    let codes = catalog_codes();
    vec![
        // The volcano (P-MAPGEN-SIG-SCALE-4): the cone (`#` foot, `X` slope, `H` rim, all wall) round its crater lake and
        // channels (`~` lava), the six outlets `O` (lava, named) on the front row, `.` the cut corners; centred in its
        // site, off every apron marked before it (as today's cone and the bog tree). The entrance is the six outlets.
        // Its three streams and pools are not drawn: `volcano::streams` lays them from the outlet runs after the stamp
        // (their owned tiles are `volcano::owned`'s).
        Spec {
            zone: Zone::Fire,
            kind: Volcano,
            drawing: Some(d.volcano_grid),
            legend: Legend(vec![
                solid(b'#', Write::Wall, Volcano, Legality::WallGround),
                solid(b'X', Write::Wall, Volcano, Legality::WallGround),
                solid(b'H', Write::Wall, Volcano, Legality::WallGround),
                solid(b'~', Write::Low(codes.lava), Volcano, Legality::WallGround),
                Glyph {
                    mark: true,
                    ..solid(b'O', Write::Low(codes.lava), Volcano, Legality::WallGround)
                },
                plain(b'.'),
            ]),
            features: vec![landmark(Volcano)],
            placement: Placement::Centre,
            entrance: EntranceRule::Mark(b'O'),
            doorway: false,
            members: vec![Volcano, LavaStream, LavaPool],
            by_tile: vec![LavaStream, LavaPool],
            any_terrain: Vec::new(),
            site_side: d.site_side,
            site_camps: None,
            spawn: SpawnRule::Box,
            fan: Some(Fan {
                depth: d.fan_depth,
                side: d.fan_side,
            }),
            params: FeatureParams::None,
        },
        // The church (`#` walls, `P` pillars) and its graveyard (`G` headstones, `+` the LOW fence), `E` on the camp;
        // the entrance is the main gate, the open front row through `E`'s column.
        Spec {
            zone: Zone::Sentenced,
            kind: Church,
            drawing: Some(d.church_grid),
            legend: Legend(vec![
                solid(b'#', Write::Wall, Church, Legality::Wall),
                solid(b'P', Write::Wall, Church, Legality::Wall),
                solid(b'G', Write::Wall, Graveyard, Legality::Wall),
                solid(b'+', Write::Low(R.fence_kind), Graveyard, Legality::Wall),
                plain(b'.'),
                plain(b'*'),
                mark(b'E'),
                mark(b'D'),
            ]),
            features: vec![
                landmark(Church),
                FeatureSpec {
                    kind: Graveyard,
                    job: FeatureJob::Cover,
                    anchor: FeatureAnchor::EntranceMiddle,
                },
            ],
            placement: Placement::Pin(b'E'),
            entrance: EntranceRule::FrontRun(b'E'),
            doorway: true,
            members: vec![Church, Graveyard],
            by_tile: Vec::new(),
            any_terrain: vec![Church],
            site_side: d.site_side,
            site_camps: Some(SiteCamps {
                glyph: b'E',
                elite: true,
            }),
            spawn: SpawnRule::Box,
            fan: None,
            params: FeatureParams::None,
        },
        // The bog tree (`T` trunk, `r` roots, `%` marsh), off every apron marked before it, centred in its site; the
        // entrance is the causeways `:` on the box's edge.
        Spec {
            zone: Zone::Plague,
            kind: BogTree,
            drawing: Some(d.bog_grid),
            legend: Legend(vec![
                solid(b'T', Write::Wall, BogTree, Legality::WallGround),
                solid(b'r', Write::Wall, BogTree, Legality::WallGround),
                solid(b'%', Write::Floor(codes.bog), BogTree, Legality::WallGround),
                plain(b'.'),
                plain(b':'),
            ]),
            features: vec![landmark(BogTree)],
            placement: Placement::Centre,
            entrance: EntranceRule::Edge(b':'),
            doorway: false,
            members: vec![BogTree],
            by_tile: Vec::new(),
            any_terrain: Vec::new(),
            site_side: d.site_side,
            site_camps: None,
            spawn: SpawnRule::Box,
            fan: None,
            params: FeatureParams::None,
        },
        // The cave (`#` walls, `R` rock, `=` ice floor), `E` on the camp; the entrance is the mouth `D`.
        Spec {
            zone: Zone::Frost,
            kind: Cave,
            drawing: Some(d.cave_grid),
            legend: Legend(vec![
                solid(b'#', Write::Wall, Cave, Legality::Wall),
                solid(b'R', Write::Wall, Cave, Legality::Wall),
                solid(b'=', Write::Floor(codes.ice), Cave, Legality::Floor),
                plain(b'.'),
                mark(b'E'),
                mark(b'D'),
            ]),
            features: vec![landmark(Cave)],
            placement: Placement::Pin(b'E'),
            entrance: EntranceRule::Mark(b'D'),
            doorway: true,
            members: vec![Cave],
            by_tile: Vec::new(),
            any_terrain: Vec::new(),
            site_side: d.site_side,
            site_camps: Some(SiteCamps {
                glyph: b'E',
                elite: true,
            }),
            spawn: SpawnRule::Box,
            fan: None,
            params: FeatureParams::None,
        },
        // The Holy hall (P-MAPGEN-SIG-HOLY-1): an open colonnade of 3 x 3 piers and the shrine (`#` walls), the shrine's
        // healing light `L` (floor of the catalog's `light` terrain), `E` the two camps on the nave's centre line, `D` the
        // shrine's door, `*` the nave's light marks; centred in its site's inner rect (37 in 37). The entrance is the
        // nave's front run through the `E` column; no doorway check (walkable from every side).
        Spec {
            zone: Zone::Holy,
            kind: Hall,
            drawing: Some(d.hall_grid),
            legend: Legend(vec![
                solid(b'#', Write::Wall, Hall, Legality::Wall),
                solid(b'L', Write::Floor(light_code()), Hall, Legality::Floor),
                plain(b'.'),
                mark(b'*'),
                mark(b'D'),
                mark(b'E'),
            ]),
            features: vec![FeatureSpec {
                kind: Hall,
                job: FeatureJob::Cover,
                anchor: FeatureAnchor::Centre,
            }],
            placement: Placement::Centre,
            entrance: EntranceRule::FrontRun(b'E'),
            doorway: false,
            members: vec![Hall],
            by_tile: Vec::new(),
            any_terrain: Vec::new(),
            site_side: d.hall_site_side,
            site_camps: Some(SiteCamps {
                glyph: b'E',
                elite: false,
            }),
            spawn: SpawnRule::OpenFloor {
                clear_of: b'L',
                by: d.shrine_clear,
            },
            fan: None,
            params: FeatureParams::Hall,
        },
    ]
}

/// The terrain catalog's code of the kind keyed `light` (the shrine's healing light), looked up by key as
/// `catalog_codes` looks up the others (kept out of `TerrainCodes`, which callers build literally). Panics when the
/// catalog lacks it: the table cannot be built without it.
pub(super) fn light_code() -> u8 {
    let k = crate::mapgen::terrain_gate::DEFAULT_DEFINITION
        .kinds
        .iter()
        .position(|k| k.key == "light")
        .expect("signature: no terrain kind \"light\" in the catalog");
    u8::try_from(k).expect("signature: terrain code")
}
