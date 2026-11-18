//! The cached, pre-encoded map prefix ([`EncodedMap`]) and the run helpers that compare it with a map.

use super::*;

/// Cached immutable prefix. The final mode/context tail is emitted by [`EncodedMap::write_with_mode`].
#[derive(Clone, Debug, PartialEq, Eq)]
pub struct EncodedMap {
    pub(super) w: u16,
    pub(super) h: u16,
    pub(super) layer_hash: u32,
    pub(super) semantic_hash: u32,
    pub(super) body: Box<[u8]>,
}
impl EncodedMap {
    /// A map without protocol 17 public sections.
    pub fn from_slices(
        w: u16,
        h: u16,
        tiles: &[u8],
        terrain: Option<&[u8]>,
        regions: Option<&[u8]>,
    ) -> Result<Self, EncodeError> {
        Self::from_public(w, h, tiles, terrain, regions, &MapPublic::default())
    }

    /// Server path for protocol 17: the same bytes as [`MapBlob::pre_encode`] for a map carrying `public`.
    pub fn from_public(
        w: u16,
        h: u16,
        tiles: &[u8],
        terrain: Option<&[u8]>,
        regions: Option<&[u8]>,
        public: &MapPublic,
    ) -> Result<Self, EncodeError> {
        let total = usize::from(w) * usize::from(h);
        if w == 0 || h == 0 || total > MAX_MAP_TILES {
            return Err(EncodeError::BadMap("size"));
        }
        if tiles.len() != total {
            return Err(EncodeError::BadMap("length"));
        }
        if tiles.iter().any(|&v| v > 2) {
            return Err(EncodeError::BadMap("run"));
        }
        validate_plane_values(terrain, regions)?;
        for plane in [terrain, regions].into_iter().flatten() {
            if plane.len() != total {
                return Err(EncodeError::BadMap("plane length"));
            }
        }
        if terrain.is_some_and(|plane| {
            plane
                .iter()
                .zip(tiles)
                .any(|(&t, &c)| !terrain_collision_compatible(t, c))
        }) {
            return Err(EncodeError::BadMap("terrain collision"));
        }
        check_public(w, h, public)?;
        let collision_hash = layer_hash(w as u32, h as u32, tiles);
        let flags = presence(terrain.is_some(), regions.is_some()) | public_presence(public);
        let semantic_hash = public_semantic_hash(w, h, terrain, regions, public);
        let collision_count = Runs { values: tiles }.count();
        let ext_static_len = extension_header_len(public)
            + terrain.map_or(0, rle_len)
            + regions.map_or(0, rle_len)
            + public_len(public);
        let static_len = MAP_HEAD_LEN + RUN_LEN * collision_count + 4 + ext_static_len;
        let full_len = crate::encode::WELCOME_HEADER_LEN + static_len + EXTENSION_TAIL_LEN;
        if full_len > crate::MAX_SERVER_FRAME {
            return Err(EncodeError::TooLarge {
                len: full_len,
                max: crate::MAX_SERVER_FRAME,
            });
        }
        let mut out = Writer::with_capacity(static_len);
        out.u16(w);
        out.u16(h);
        out.u32(collision_hash);
        out.u32(collision_count as u32);
        write_runs(&mut out, tiles);
        out.u32((ext_static_len + EXTENSION_TAIL_LEN) as u32);
        let (header, header_len) = section_header(flags, public_presence2(public));
        out.bytes(&header[..header_len]);
        out.u32(semantic_hash);
        write_plane(&mut out, terrain);
        write_plane(&mut out, regions);
        write_public(&mut out, public);
        Ok(Self {
            w,
            h,
            layer_hash: collision_hash,
            semantic_hash,
            body: out.finish().into_boxed_slice(),
        })
    }

    pub fn static_bytes(&self) -> &[u8] {
        &self.body
    }
    pub fn len(&self) -> usize {
        self.body.len() + 5
    }
    pub fn is_empty(&self) -> bool {
        self.body.is_empty()
    }
    pub fn w(&self) -> u16 {
        self.w
    }
    pub fn h(&self) -> u16 {
        self.h
    }
    pub fn layer_hash(&self) -> u32 {
        self.layer_hash
    }
    pub fn semantic_hash(&self) -> u32 {
        self.semantic_hash
    }
    pub fn encoded_len_for_mode(&self, _mode: GroundMode) -> usize {
        self.len()
    }
    pub fn write_with_mode(&self, w: &mut Writer, mode: GroundMode) {
        w.bytes(&self.body);
        w.u8(mode as u8);
        w.u32(context_hash(self.layer_hash, self.semantic_hash, mode));
    }

    /// Exact static-content match for a map without public sections; hashes alone are not used as cache identity.
    pub fn matches_slices(
        &self,
        w: u16,
        h: u16,
        tiles: &[u8],
        terrain: Option<&[u8]>,
        regions: Option<&[u8]>,
    ) -> bool {
        self.matches_public(w, h, tiles, terrain, regions, &MapPublic::default())
    }

    /// Exact static-content match including the protocol 17 public sections, so a cache never reuses a stale blob.
    pub fn matches_public(
        &self,
        w: u16,
        h: u16,
        tiles: &[u8],
        terrain: Option<&[u8]>,
        regions: Option<&[u8]>,
        public: &MapPublic,
    ) -> bool {
        if self.w != w || self.h != h || tiles.len() != usize::from(w) * usize::from(h) {
            return false;
        }
        let Some((collision_runs, mut offset)) = encoded_runs(&self.body, 8) else {
            return false;
        };
        if !runs_match(collision_runs, tiles) || offset + 10 > self.body.len() {
            return false;
        }
        let ext_len =
            u32::from_le_bytes(self.body[offset..offset + 4].try_into().unwrap()) as usize;
        offset += 4;
        let header_len = extension_header_len(public);
        if ext_len < header_len + EXTENSION_TAIL_LEN
            || offset + ext_len - EXTENSION_TAIL_LEN != self.body.len()
        {
            return false;
        }
        let (header, len) = section_header(
            presence(terrain.is_some(), regions.is_some()) | public_presence(public),
            public_presence2(public),
        );
        if self.body[offset..offset + len] != header[..len] {
            return false;
        }
        offset += header_len;
        for values in [terrain, regions].into_iter().flatten() {
            let Some((runs, next)) = encoded_runs(&self.body, offset) else {
                return false;
            };
            if !runs_match(runs, values) {
                return false;
            }
            offset = next;
        }
        if check_public(w, h, public).is_err() || offset + public_len(public) != self.body.len() {
            return false;
        }
        let mut expected = Writer::with_capacity(public_len(public));
        write_public(&mut expected, public);
        self.body[offset..] == expected.finish()[..]
    }

    /// [`Self::matches_public`] for a blob that may also carry dressing (props, ground variants) and landmark anchors
    /// (P-WIRE17-MAP-3: the generator's, which an arena does not keep): every other section must match `public` exactly,
    /// and the dressing and anchors the blob carries must pass the decoder's checks. `public`'s own dressing and anchors
    /// are ignored. A cold path (room start, pool tests), never per tick: it decodes the blob once.
    pub fn matches_public_any_dressing(
        &self,
        w: u16,
        h: u16,
        tiles: &[u8],
        terrain: Option<&[u8]>,
        regions: Option<&[u8]>,
        public: &MapPublic,
    ) -> bool {
        let mut out = Writer::with_capacity(self.len());
        self.write_with_mode(&mut out, GroundMode::Off);
        let bytes = out.finish();
        let Ok(decoded) = MapBlob::read(&mut Reader::new(&bytes)) else {
            return false;
        };
        let dressed = MapPublic {
            props: decoded.public.props,
            ground_variants: decoded.public.ground_variants,
            anchors: decoded.public.anchors,
            ..public.clone()
        };
        self.matches_public(w, h, tiles, terrain, regions, &dressed)
    }
}

pub(super) fn write_runs(w: &mut Writer, values: &[u8]) {
    for (value, count) in (Runs { values }) {
        w.u8(value);
        w.u16(count);
    }
}

pub(super) fn encoded_runs(bytes: &[u8], count_offset: usize) -> Option<(&[u8], usize)> {
    if count_offset.checked_add(4)? > bytes.len() {
        return None;
    }
    let count = u32::from_le_bytes(bytes[count_offset..count_offset + 4].try_into().ok()?) as usize;
    let start = count_offset + 4;
    let end = start.checked_add(count.checked_mul(RUN_LEN)?)?;
    Some((bytes.get(start..end)?, end))
}

pub(super) fn runs_match(runs: &[u8], values: &[u8]) -> bool {
    let mut expected = Runs { values };
    let mut actual = runs.chunks_exact(RUN_LEN);
    loop {
        match (expected.next(), actual.next()) {
            (None, None) => return true,
            (Some((v, n)), Some(bytes))
                if v == bytes[0] && n == u16::from_le_bytes([bytes[1], bytes[2]]) => {}
            _ => return false,
        }
    }
}
