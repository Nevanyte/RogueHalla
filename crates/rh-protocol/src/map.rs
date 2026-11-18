//! Canonical collision map and optional public ground planes.

use crate::codec::{DecodeError, EncodeError, Reader, Writer};
use crate::ground::{
    context_hash, section_header, semantic_hash, semantic_hash_parts2, validate_plane_values,
    GroundMode, PRESENCE2_ANCHORS, PRESENCE2_KNOWN, PRESENCE2_PATIENCE, PRESENCE_BUSHES,
    PRESENCE_CAMPS, PRESENCE_COLISEUM, PRESENCE_GROUND_VARIANTS, PRESENCE_KNOWN, PRESENCE_PROPS,
    PRESENCE_REGIONS, PRESENCE_TERRAIN, SECTION_VERSION, SECTION_VERSION_PRESENCE2,
};

mod encoded;
mod read;
mod write;

pub use self::encoded::EncodedMap;
use self::{read::*, write::*};

/// Maximum cells in any map plane.
pub const MAX_MAP_TILES: usize = 1024 * 1024;
const MAP_HEAD_LEN: usize = 12;
const RUN_LEN: usize = 3;
const EXTENSION_STATIC_HEADER_LEN: usize = 6;
const EXTENSION_TAIL_LEN: usize = 5;
/// Six run-length planes may be present: collision, terrain, regions, bushes, ground variants and (section version 3)
/// the camp patience plane.
const MAX_CUMULATIVE_RUNS: usize = 6 * MAX_MAP_TILES;
const MAX_DECODED_PLANES: usize = 6 * MAX_MAP_TILES;
/// Wire bytes of one camp row (`x u16, y u16, r u16, zone u8`) and one prop row (`kind u8, x u16, y u16, variant u8,
/// flags u8`).
const CAMP_LEN: usize = 7;
const PROP_LEN: usize = 7;
/// `Zone::code` runs 1..=5; `GateSide` codes 0..=3 (n, e, s, w).
const MAX_CAMP_ZONE: u8 = 5;
const MAX_GATE_SIDE: u8 = 3;
/// Wire bytes of an anchor row before its entrance tiles: `zone u8, kind u8, centre x/y u16, footprint x/y/w/h u16,
/// front u8, entrance count u8` (P-WIRE17-MAP-3).
const ANCHOR_FIXED_LEN: usize = 16;
/// Wire bytes of one entrance tile (`x u16, y u16`).
const ANCHOR_TILE_LEN: usize = 4;
/// DIR16: an anchor's `front` is below this.
const ANCHOR_DIRS: u8 = 16;

/// Most camp markers a map may carry (legacy: 5 zones x 10 camps).
pub const MAX_MAP_CAMPS: usize = 64;
/// Most coliseum gates (legacy: four, one per side).
pub const MAX_COLISEUM_GATES: usize = 8;
/// Most tiles in one gate (legacy: GATE_W = 4).
pub const MAX_GATE_TILES: usize = 16;
/// Most cosmetic props (`rh_mapdress::MAX_PROPS`).
pub const MAX_MAP_PROPS: usize = 8192;

// P-DRESS-BAKE-1 (stub: the values are the brief's; the checks that use them are the implementer's).
/// Prop flag: drawn tall (`rh_mapdress::FLAG_TALL`).
pub const PROP_FLAG_TALL: u8 = 1;
/// Prop flag: a generator `GameMapData.props` entry; `kind` is a legacy prop-catalogue index (`rh_mapdress::FLAG_LEGACY`).
pub const PROP_FLAG_LEGACY: u8 = 2;
/// Highest rule-file kind code: five zones x ten codes x two blocks (`crates/rh-mapdress/src/rules.rs` module doc).
/// Rule props carry `1..=MAX_PROP_KIND`.
pub const MAX_PROP_KIND: u8 = 100;
/// Legacy props carry a kind below this: the generator's prop catalogue
/// (`rh_sim::mapgen::terrain_gate::DEFAULT_DEFINITION.props`).
pub const LEGACY_PROP_KINDS: u8 = 34;
/// Highest sprite variant of a rule prop (`rh_mapdress` draws `0..=3`); a legacy prop's variant is its generator `v`.
pub const MAX_PROP_VARIANT: u8 = 3;
/// Highest ground-variant id in the ground-variant plane.
pub const MAX_GROUND_VARIANT: u8 = 15;
/// World units per tile: a prop at `(x, y)` stands on tile `(x / 32, y / 32)`, which must be inside the map.
const PROP_TILE: u16 = 32;

/// One camp marker, index-aligned with the `WireCamp` rows in state. World units; `zone` is `Zone::code` (1..=5).
#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub struct MapCamp {
    pub x: u16,
    pub y: u16,
    /// Legacy `CampDef.r`: the clearing radius in world units.
    pub r: u16,
    pub zone: u8,
}

/// A tile coordinate inside the map.
#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub struct MapTile {
    pub x: u16,
    pub y: u16,
}

/// A coliseum gate: side 0 north, 1 east, 2 south, 3 west; its tiles in legacy order.
#[derive(Clone, Debug, PartialEq, Eq)]
pub struct MapGate {
    pub side: u8,
    pub tiles: Vec<MapTile>,
}

/// The coliseum rectangle in tiles and its gates in legacy order.
#[derive(Clone, Debug, PartialEq, Eq)]
pub struct MapColiseum {
    pub x: u16,
    pub y: u16,
    pub w: u16,
    pub h: u16,
    pub gates: Vec<MapGate>,
}

/// A cosmetic prop (the server's map dressing, P-DRESS-BAKE-1): x and y in world units, rows sorted by `(y, x, kind)`.
#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub struct MapProp {
    pub kind: u8,
    pub x: u16,
    pub y: u16,
    pub variant: u8,
    pub flags: u8,
}

/// A tile rectangle inside the map (`x + w <= map w`, `y + h <= map h`, `w` and `h` at least 1).
#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub struct MapRect {
    pub x: u16,
    pub y: u16,
    pub w: u16,
    pub h: u16,
}

/// P-WIRE17-MAP-3: most landmark anchors in a map (one per zone, five zones).
pub const MAX_MAP_ANCHORS: usize = 5;
/// P-WIRE17-MAP-3: most entrance tiles per anchor (the cave's mouth is 3; the bog tree's three causeways are 6; the
/// church's main gate is 5, the volcano's three outlets 6, the Holy hall's nave end 7).
pub const MAX_ANCHOR_ENTRANCE: usize = 16;
/// P-WIRE17-MAP-3: the wire's landmark kind codes (a table, not `rh_sim::mapdata::FeatureKind`'s ordinal, so a kind appended
/// in the sim never shifts a wire value). Every other code is refused (`"anchor kind"`).
pub const ANCHOR_KIND_VOLCANO: u8 = 1;
pub const ANCHOR_KIND_CHURCH: u8 = 2;
pub const ANCHOR_KIND_BOG_TREE: u8 = 3;
pub const ANCHOR_KIND_CAVE: u8 = 4;
pub const ANCHOR_KIND_HALL: u8 = 5;

/// P-WIRE17-MAP-3: one zone's landmark anchor (`rh_sim::mapdata::ZoneAnchor` as sent), tile units. Rows are in zone-code
/// order, one per zone; layout in docs/backlog/packets/P-WIRE17-MAP-3.md "Wire layout".
#[derive(Clone, Debug, PartialEq, Eq)]
pub struct MapAnchor {
    /// `Zone::code` (1..=5).
    pub zone: u8,
    /// One of the `ANCHOR_KIND_*` codes.
    pub kind: u8,
    /// The landmark's centre tile, inside `footprint`.
    pub centre: MapTile,
    /// Bounding rectangle of the landmark's owned tiles.
    pub footprint: MapRect,
    /// DIR16 index (0 = +x, 4 = +y), below 16.
    pub front: u8,
    /// The way in (cave mouth, church door), generator order; empty for a landmark without one.
    pub entrance: Vec<MapTile>,
}

/// Public map dressing that legacy clients generated themselves (protocol 17). Empty lists and `None` encode as absent.
#[derive(Clone, Debug, Default, PartialEq, Eq)]
pub struct MapPublic {
    pub camps: Vec<MapCamp>,
    pub coliseum: Option<MapColiseum>,
    /// Row-major bush identity plane: 0 none, else the legacy bush id.
    pub bushes: Option<Vec<u8>>,
    pub props: Vec<MapProp>,
    /// Row-major ground-variant plane (the server's map dressing; ids `0..=MAX_GROUND_VARIANT`).
    pub ground_variants: Option<Vec<u8>>,
    /// P-WIRE17-MAP-2: row-major camp patience plane: 0, or the 1-based index (into `camps`) of the camp whose patience
    /// range covers the tile (legacy `campPatienceOutline`). Present only in a section-version-3 extension (second
    /// presence byte bit 0x01), after the version-2 sections; every cell is at most `camps.len()`.
    pub patience: Option<Vec<u8>>,
    /// P-WIRE17-MAP-3: each zone's landmark anchor, zone-code order (second presence byte bit 0x02, after the patience
    /// plane). Empty encodes as absent; the list section's wire bytes (count included) enter the semantic hash.
    pub anchors: Vec<MapAnchor>,
}

#[derive(Clone, Debug, PartialEq, Eq)]
pub struct MapBlob {
    pub w: u16,
    pub h: u16,
    /// Collision classes, row-major: 0 floor, 1 wall, 2 low.
    pub tiles: Vec<u8>,
    /// FNV-1a 32 over (w u32 LE, h u32 LE, collision cells).
    pub layer_hash: u32,
    /// Optional public semantic terrain plane, row-major. Present zeroes differ from absence.
    pub terrain: Option<Vec<u8>>,
    /// Optional public authored region plane, row-major. Present zeroes differ from absence.
    pub regions: Option<Vec<u8>>,
    pub terrain_mode: GroundMode,
    pub semantic_hash: u32,
    pub context_hash: u32,
    /// Protocol 17 public sections.
    pub public: MapPublic,
}

/// Same collision hash as `rh_sim::map::TileMap::layer_hash`.
pub fn layer_hash(w: u32, h: u32, tiles: &[u8]) -> u32 {
    let mut hsh: u32 = 0x811c_9dc5;
    for b in w
        .to_le_bytes()
        .iter()
        .chain(h.to_le_bytes().iter())
        .chain(tiles.iter())
    {
        hsh ^= *b as u32;
        hsh = hsh.wrapping_mul(0x0100_0193);
    }
    hsh
}

struct Runs<'a> {
    values: &'a [u8],
}
impl Iterator for Runs<'_> {
    type Item = (u8, u16);
    fn next(&mut self) -> Option<Self::Item> {
        let (&tile, rest) = self.values.split_first()?;
        let max = rest.len().min(u16::MAX as usize - 1);
        let same = rest[..max].iter().take_while(|&&v| v == tile).count();
        self.values = &rest[same..];
        Some((tile, same as u16 + 1))
    }
}

impl MapBlob {
    /// Collision-only compatibility constructor. New ground metadata is absent and the default mode is Off.
    pub fn new(w: u16, h: u16, tiles: Vec<u8>) -> Self {
        let layer_hash = layer_hash(w as u32, h as u32, &tiles);
        let semantic_hash = semantic_hash(w, h, 0, None, None);
        let terrain_mode = GroundMode::Off;
        let context_hash = context_hash(layer_hash, semantic_hash, terrain_mode);
        Self {
            w,
            h,
            tiles,
            layer_hash,
            terrain: None,
            regions: None,
            terrain_mode,
            semantic_hash,
            context_hash,
            public: MapPublic::default(),
        }
    }

    /// Constructs a complete ground-aware map and computes both public hashes.
    pub fn with_ground(
        w: u16,
        h: u16,
        tiles: Vec<u8>,
        terrain: Option<Vec<u8>>,
        regions: Option<Vec<u8>>,
        terrain_mode: GroundMode,
    ) -> Result<Self, EncodeError> {
        let layer_hash = layer_hash(w as u32, h as u32, &tiles);
        let presence = presence(terrain.is_some(), regions.is_some());
        let semantic_hash = semantic_hash(w, h, presence, terrain.as_deref(), regions.as_deref());
        let context_hash = context_hash(layer_hash, semantic_hash, terrain_mode);
        let map = Self {
            w,
            h,
            tiles,
            layer_hash,
            terrain,
            regions,
            terrain_mode,
            semantic_hash,
            context_hash,
            public: MapPublic::default(),
        };
        map.check()?;
        Ok(map)
    }

    /// Alias emphasizing that all semantic planes and hashes are checked before the model is returned.
    pub fn new_with_ground(
        w: u16,
        h: u16,
        tiles: Vec<u8>,
        terrain: Option<Vec<u8>>,
        regions: Option<Vec<u8>>,
        mode: GroundMode,
    ) -> Result<Self, EncodeError> {
        Self::with_ground(w, h, tiles, terrain, regions, mode)
    }

    /// Attaches the protocol 17 public sections, recomputes the hashes and checks the whole map. Empty lists and `None`
    /// are absent sections; nothing is clamped or dropped: a section a decoder would refuse is an error.
    pub fn with_public(mut self, public: MapPublic) -> Result<Self, EncodeError> {
        check_public(self.w, self.h, &public)?;
        self.public = public;
        self.semantic_hash = self.computed_semantic_hash();
        self.context_hash = context_hash(self.layer_hash, self.semantic_hash, self.terrain_mode);
        self.check()?;
        Ok(self)
    }

    fn computed_semantic_hash(&self) -> u32 {
        public_semantic_hash(
            self.w,
            self.h,
            self.terrain.as_deref(),
            self.regions.as_deref(),
            &self.public,
        )
    }

    fn runs(&self) -> Runs<'_> {
        Runs {
            values: &self.tiles,
        }
    }
    fn presence(&self) -> u8 {
        presence(self.terrain.is_some(), self.regions.is_some()) | public_presence(&self.public)
    }

    fn collision_encoded_len(&self) -> usize {
        MAP_HEAD_LEN + RUN_LEN * self.runs().count()
    }
    fn extension_static_len(&self) -> usize {
        extension_header_len(&self.public)
            + self.terrain.as_deref().map_or(0, rle_len)
            + self.regions.as_deref().map_or(0, rle_len)
            + public_len(&self.public)
    }
    /// Exact size of the complete MapBlob, including its extension and context tail.
    pub fn encoded_len(&self) -> usize {
        self.collision_encoded_len() + 4 + self.extension_static_len() + EXTENSION_TAIL_LEN
    }

    /// Whether a decoder accepts this complete map.
    pub fn check(&self) -> Result<(), EncodeError> {
        let total = self.w as usize * self.h as usize;
        if self.w == 0 || self.h == 0 || total > MAX_MAP_TILES {
            return Err(EncodeError::BadMap("size"));
        }
        if self.tiles.len() != total {
            return Err(EncodeError::BadMap("length"));
        }
        if self.tiles.iter().any(|&v| v > 2) {
            return Err(EncodeError::BadMap("run"));
        }
        if layer_hash(self.w as u32, self.h as u32, &self.tiles) != self.layer_hash {
            return Err(EncodeError::BadMap("hash"));
        }
        validate_plane_values(self.terrain.as_deref(), self.regions.as_deref())?;
        for plane in [self.terrain.as_deref(), self.regions.as_deref()]
            .into_iter()
            .flatten()
        {
            if plane.len() != total {
                return Err(EncodeError::BadMap("plane length"));
            }
        }
        if let (Some(terrain), true) = (self.terrain.as_deref(), self.tiles.len() == total) {
            if terrain
                .iter()
                .zip(&self.tiles)
                .any(|(&t, &c)| !terrain_collision_compatible(t, c))
            {
                return Err(EncodeError::BadMap("terrain collision"));
            }
        }
        check_public(self.w, self.h, &self.public)?;
        if self.semantic_hash != self.computed_semantic_hash() {
            return Err(EncodeError::BadMap("semantic hash"));
        }
        if self.context_hash != context_hash(self.layer_hash, self.semantic_hash, self.terrain_mode)
        {
            return Err(EncodeError::BadMap("context hash"));
        }
        let full_len = crate::encode::WELCOME_HEADER_LEN + self.encoded_len();
        if full_len > crate::MAX_SERVER_FRAME {
            return Err(EncodeError::TooLarge {
                len: full_len,
                max: crate::MAX_SERVER_FRAME,
            });
        }
        Ok(())
    }

    pub fn write(&self, w: &mut Writer) {
        self.write_collision(w);
        self.write_extension_static(w);
        w.u8(self.terrain_mode as u8);
        w.u32(context_hash(
            self.layer_hash,
            self.semantic_hash,
            self.terrain_mode,
        ));
    }

    fn write_collision(&self, w: &mut Writer) {
        w.u16(self.w);
        w.u16(self.h);
        w.u32(self.layer_hash);
        w.u32(self.runs().count() as u32);
        for (v, n) in self.runs() {
            w.u8(v);
            w.u16(n);
        }
    }

    fn write_extension_static(&self, w: &mut Writer) {
        let static_len = self.extension_static_len();
        w.u32((static_len + EXTENSION_TAIL_LEN) as u32);
        let (header, len) = section_header(self.presence(), public_presence2(&self.public));
        w.bytes(&header[..len]);
        w.u32(self.semantic_hash);
        write_plane(w, self.terrain.as_deref());
        write_plane(w, self.regions.as_deref());
        write_public(w, &self.public);
    }

    /// RLE-encodes immutable collision and semantic data once. Mode and context are appended per Welcome.
    pub fn pre_encode(&self) -> Result<EncodedMap, EncodeError> {
        self.check()?;
        let static_len = self.collision_encoded_len() + 4 + self.extension_static_len();
        let mut w = Writer::with_capacity(static_len);
        self.write_collision(&mut w);
        self.write_extension_static(&mut w);
        Ok(EncodedMap {
            w: self.w,
            h: self.h,
            layer_hash: self.layer_hash,
            semantic_hash: self.semantic_hash,
            body: w.finish().into_boxed_slice(),
        })
    }

    /// Encodes borrowed source planes after validation, avoiding decoded-plane clones during asset admission.
    pub fn encode_slices(
        w: u16,
        h: u16,
        tiles: &[u8],
        terrain: Option<&[u8]>,
        regions: Option<&[u8]>,
    ) -> Result<EncodedMap, EncodeError> {
        EncodedMap::from_slices(w, h, tiles, terrain, regions)
    }

    /// Full two-pass validation: no decoded plane is allocated until all map and extension data are proven valid.
    pub fn read(r: &mut Reader) -> Result<Self, DecodeError> {
        let mut scan = r.clone();
        preflight(&mut scan)?;
        if scan.remaining() != 0 {
            return Err(DecodeError::Trailing(scan.remaining()));
        }
        let consumed = scan.position() - r.position();
        let frame_bytes = r.bytes(consumed)?;
        decode_plan(frame_bytes)
    }
}

// Reader support is intentionally narrow: the map preflight needs to compare the scan pass's exact consumed prefix.

#[cfg(test)]
mod tests;
