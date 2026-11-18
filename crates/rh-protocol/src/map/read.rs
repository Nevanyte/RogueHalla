//! Map decoding: the preflight scan, run and plane validation, hashing and the decode of each section.

use super::*;

#[derive(Clone, Copy)]
pub(super) struct PlanePlan<'a> {
    runs: &'a [u8],
    count: usize,
}
#[derive(Clone, Copy)]
pub(super) struct MapPlan<'a> {
    w: u16,
    h: u16,
    collision_hash: u32,
    collision_runs: &'a [u8],
    collision_count: usize,
    semantic_hash: u32,
    terrain: Option<PlanePlan<'a>>,
    regions: Option<PlanePlan<'a>>,
    /// List sections keep their exact wire bytes (count included): the semantic hash covers them as sent.
    camps: Option<&'a [u8]>,
    coliseum: Option<&'a [u8]>,
    bushes: Option<PlanePlan<'a>>,
    props: Option<&'a [u8]>,
    ground_variants: Option<PlanePlan<'a>>,
    /// Section version 3's patience plane.
    patience: Option<PlanePlan<'a>>,
    /// Section version 3's anchors section (wire bytes, count included).
    anchors: Option<&'a [u8]>,
    mode: GroundMode,
    context_hash: u32,
}

pub(super) fn preflight<'a>(r: &mut Reader<'a>) -> Result<MapPlan<'a>, DecodeError> {
    let w = r.u16()?;
    let h = r.u16()?;
    let collision_hash = r.u32()?;
    let collision_count = r.u32()? as usize;
    let total = usize::from(w) * usize::from(h);
    if w == 0 || h == 0 || total > MAX_MAP_TILES {
        return Err(DecodeError::BadMap("size"));
    }
    if collision_count == 0 || collision_count > total || collision_count > MAX_CUMULATIVE_RUNS {
        return Err(DecodeError::BadMap("run"));
    }
    let collision_bytes = collision_count
        .checked_mul(RUN_LEN)
        .ok_or(DecodeError::BadMap("run"))?;
    let collision_runs = r.bytes(collision_bytes)?;
    validate_runs(collision_runs, collision_count, total, 2, "run")?;
    let actual_collision_hash = hash_collision_runs(w, h, collision_runs, collision_count);
    if actual_collision_hash != collision_hash {
        return Err(DecodeError::BadMap("hash"));
    }

    let ext_len = r.u32()? as usize;
    if ext_len < EXTENSION_STATIC_HEADER_LEN + EXTENSION_TAIL_LEN {
        return Err(DecodeError::BadMap("extension length"));
    }
    let ext = r.bytes(ext_len)?;
    let mut er = Reader::new(ext);
    let version = er.u8()?;
    if version != SECTION_VERSION && version != SECTION_VERSION_PRESENCE2 {
        return Err(DecodeError::BadMap("section version"));
    }
    let flags = er.u8()?;
    if flags & !PRESENCE_KNOWN != 0 {
        return Err(DecodeError::BadMap("presence"));
    }
    // Version 3 carries a second presence byte; without any of its bits the map would have a second encoding.
    let presence2 = if version == SECTION_VERSION_PRESENCE2 {
        let presence2 = er.u8()?;
        if presence2 == 0 || presence2 & !PRESENCE2_KNOWN != 0 {
            return Err(DecodeError::BadMap("presence"));
        }
        presence2
    } else {
        0
    };
    let semantic_hash = er.u32()?;
    let mut cumulative_runs = collision_count;
    let terrain = if flags & PRESENCE_TERRAIN != 0 {
        Some(read_plane_plan(&mut er, total, &mut cumulative_runs)?)
    } else {
        None
    };
    let regions = if flags & PRESENCE_REGIONS != 0 {
        Some(read_plane_plan(&mut er, total, &mut cumulative_runs)?)
    } else {
        None
    };
    let camps = if flags & PRESENCE_CAMPS != 0 {
        Some(section(ext, &mut er, read_camps)?)
    } else {
        None
    };
    let coliseum = if flags & PRESENCE_COLISEUM != 0 {
        Some(section(ext, &mut er, |r| read_coliseum(r, w, h))?)
    } else {
        None
    };
    let bushes = if flags & PRESENCE_BUSHES != 0 {
        Some(read_plane_plan(&mut er, total, &mut cumulative_runs)?)
    } else {
        None
    };
    let props = if flags & PRESENCE_PROPS != 0 {
        Some(section(ext, &mut er, |r| read_props(r, w, h))?)
    } else {
        None
    };
    let ground_variants = if flags & PRESENCE_GROUND_VARIANTS != 0 {
        let plan = read_plane_plan(&mut er, total, &mut cumulative_runs)?;
        if plan
            .runs
            .chunks_exact(RUN_LEN)
            .any(|run| run[0] > MAX_GROUND_VARIANT)
        {
            return Err(DecodeError::BadMap("ground variant"));
        }
        Some(plan)
    } else {
        None
    };
    let patience = if presence2 & PRESENCE2_PATIENCE != 0 {
        let plan = read_patience_plan(&mut er, total, &mut cumulative_runs)?;
        // Cells name a camp of this map's camps section (1-based), or 0; no camps section means only 0.
        let camps_count = camps.map_or(0, |c| c[0]);
        if plan
            .runs
            .chunks_exact(RUN_LEN)
            .any(|run| run[0] > camps_count)
        {
            return Err(DecodeError::BadMap("patience camp"));
        }
        Some(plan)
    } else {
        None
    };
    let anchors = if presence2 & PRESENCE2_ANCHORS != 0 {
        Some(section(ext, &mut er, |r| read_anchors(r, w, h))?)
    } else {
        None
    };
    if cumulative_runs > MAX_CUMULATIVE_RUNS {
        return Err(DecodeError::BadMap("run"));
    }
    let mode = GroundMode::from_code(er.u8()?)?;
    let context_hash = er.u32()?;
    if er.remaining() != 0 {
        return Err(DecodeError::BadMap("extension trailing"));
    }
    if decoded_bytes(total, flags, presence2)? > MAX_DECODED_PLANES {
        return Err(DecodeError::BadMap("decoded size"));
    }
    validate_plane_plan(terrain, regions)?;
    if let Some(terrain) = terrain {
        validate_terrain_compatibility(collision_runs, collision_count, terrain)?;
    }
    let actual_semantic_hash = hash_semantic(
        w,
        h,
        flags,
        presence2,
        [
            Part::Plane(terrain),
            Part::Plane(regions),
            Part::Bytes(camps),
            Part::Bytes(coliseum),
            Part::Plane(bushes),
            Part::Bytes(props),
            Part::Plane(ground_variants),
            Part::Plane(patience),
            Part::Bytes(anchors),
        ],
    );
    if semantic_hash != actual_semantic_hash {
        return Err(DecodeError::BadMap("semantic hash"));
    }
    if context_hash != context_hash_fn(collision_hash, semantic_hash, mode) {
        return Err(DecodeError::BadMap("context hash"));
    }
    Ok(MapPlan {
        w,
        h,
        collision_hash,
        collision_runs,
        collision_count,
        semantic_hash,
        terrain,
        regions,
        camps,
        coliseum,
        bushes,
        props,
        ground_variants,
        patience,
        anchors,
        mode,
        context_hash,
    })
}

/// Runs `read` and returns the exact extension bytes it consumed.
pub(super) fn section<'a>(
    ext: &'a [u8],
    r: &mut Reader<'a>,
    read: impl FnOnce(&mut Reader<'a>) -> Result<(), DecodeError>,
) -> Result<&'a [u8], DecodeError> {
    let start = r.position();
    read(r)?;
    Ok(&ext[start..r.position()])
}

pub(super) fn read_camps(r: &mut Reader<'_>) -> Result<(), DecodeError> {
    let count = usize::from(r.u8()?);
    if count == 0 || count > MAX_MAP_CAMPS {
        return Err(DecodeError::BadMap("camp count"));
    }
    let rows = r.bytes(count * CAMP_LEN)?;
    if rows
        .chunks_exact(CAMP_LEN)
        .any(|row| row[6] == 0 || row[6] > MAX_CAMP_ZONE)
    {
        return Err(DecodeError::BadMap("camp zone"));
    }
    Ok(())
}

pub(super) fn read_coliseum(r: &mut Reader<'_>, w: u16, h: u16) -> Result<(), DecodeError> {
    let (x, y, cw, ch) = (r.u16()?, r.u16()?, r.u16()?, r.u16()?);
    if !coliseum_rect_fits(x, y, cw, ch, w, h) {
        return Err(DecodeError::BadMap("coliseum rect"));
    }
    let gates = usize::from(r.u8()?);
    if gates > MAX_COLISEUM_GATES {
        return Err(DecodeError::BadMap("gate count"));
    }
    for _ in 0..gates {
        if r.u8()? > MAX_GATE_SIDE {
            return Err(DecodeError::BadMap("gate side"));
        }
        let tiles = usize::from(r.u8()?);
        if tiles == 0 || tiles > MAX_GATE_TILES {
            return Err(DecodeError::BadMap("gate tiles"));
        }
        for _ in 0..tiles {
            let (tx, ty) = (r.u16()?, r.u16()?);
            if tx >= w || ty >= h {
                return Err(DecodeError::BadMap("gate tile"));
            }
        }
    }
    Ok(())
}

/// The anchors section (P-WIRE17-MAP-3). Each row's fixed 16 bytes are read as one block and its entrance bytes are
/// tested against what the extension still holds, so a short section is `Truncated`, never a row read from the mode and
/// context-hash bytes after it.
pub(super) fn read_anchors(r: &mut Reader<'_>, w: u16, h: u16) -> Result<(), DecodeError> {
    let count = usize::from(r.u8()?);
    if count == 0 || count > MAX_MAP_ANCHORS {
        return Err(DecodeError::BadMap("anchor count"));
    }
    let mut previous = 0;
    for _ in 0..count {
        let row = r.bytes(ANCHOR_FIXED_LEN)?;
        let head = anchor_head(row);
        check_anchor_head(&head, previous, w, h).map_err(DecodeError::BadMap)?;
        let tiles = usize::from(row[ANCHOR_FIXED_LEN - 1]);
        if tiles > MAX_ANCHOR_ENTRANCE {
            return Err(DecodeError::BadMap("anchor entrance"));
        }
        let entrance = r.bytes(tiles * ANCHOR_TILE_LEN)?;
        if entrance
            .chunks_exact(ANCHOR_TILE_LEN)
            .any(|t| le16(t, 0) >= w || le16(t, 2) >= h)
        {
            return Err(DecodeError::BadMap("anchor entrance"));
        }
        previous = head.zone;
    }
    Ok(())
}

/// An anchor row's fixed part (its entrance left empty).
pub(super) fn anchor_head(row: &[u8]) -> MapAnchor {
    MapAnchor {
        zone: row[0],
        kind: row[1],
        centre: MapTile {
            x: le16(row, 2),
            y: le16(row, 4),
        },
        footprint: MapRect {
            x: le16(row, 6),
            y: le16(row, 8),
            w: le16(row, 10),
            h: le16(row, 12),
        },
        front: row[14],
        entrance: Vec::new(),
    }
}

pub(super) fn read_props(r: &mut Reader<'_>, w: u16, h: u16) -> Result<(), DecodeError> {
    let count = usize::from(r.u16()?);
    if count == 0 || count > MAX_MAP_PROPS {
        return Err(DecodeError::BadMap("prop count"));
    }
    let rows = r.bytes(count * PROP_LEN)?;
    let mut previous = None;
    for row in rows.chunks_exact(PROP_LEN) {
        let prop = prop_row(row);
        check_prop(&prop, previous.as_ref(), w, h).map_err(DecodeError::BadMap)?;
        previous = Some(prop);
    }
    Ok(())
}

/// The value checks of one prop row in the wire rules' order (flags, kind, variant, tile, order), shared by the encoder
/// and the decoder; `previous` is the row before it.
pub(super) fn check_prop(p: &MapProp, previous: Option<&MapProp>, w: u16, h: u16) -> Result<(), &'static str> {
    if p.flags & !(PROP_FLAG_TALL | PROP_FLAG_LEGACY) != 0 {
        return Err("prop flags");
    }
    if p.flags & PROP_FLAG_LEGACY != 0 {
        // A legacy prop's kind is a generator catalogue index and its variant the generator's `v` (not checked).
        if p.kind >= LEGACY_PROP_KINDS {
            return Err("prop kind");
        }
    } else {
        if p.kind == 0 || p.kind > MAX_PROP_KIND {
            return Err("prop kind");
        }
        if p.variant > MAX_PROP_VARIANT {
            return Err("prop variant");
        }
    }
    if p.x / PROP_TILE >= w || p.y / PROP_TILE >= h {
        return Err("prop tile");
    }
    if previous.is_some_and(|q| (q.y, q.x, q.kind) >= (p.y, p.x, p.kind)) {
        return Err("prop order");
    }
    Ok(())
}

pub(super) fn read_plane_plan<'a>(
    r: &mut Reader<'a>,
    total: usize,
    cumulative: &mut usize,
) -> Result<PlanePlan<'a>, DecodeError> {
    let count = r.u32()? as usize;
    read_plane_plan_counted(r, count, total, cumulative)
}

/// [`read_plane_plan`] after its run count was read.
pub(super) fn read_plane_plan_counted<'a>(
    r: &mut Reader<'a>,
    count: usize,
    total: usize,
    cumulative: &mut usize,
) -> Result<PlanePlan<'a>, DecodeError> {
    if count == 0 || count > total {
        return Err(DecodeError::BadMap("run"));
    }
    *cumulative = cumulative
        .checked_add(count)
        .ok_or(DecodeError::BadMap("run"))?;
    if *cumulative > MAX_CUMULATIVE_RUNS {
        return Err(DecodeError::BadMap("run"));
    }
    let bytes = count
        .checked_mul(RUN_LEN)
        .ok_or(DecodeError::BadMap("run"))?;
    let runs = r.bytes(bytes)?;
    validate_runs(runs, count, total, u8::MAX, "run")?;
    Ok(PlanePlan { runs, count })
}

/// The patience plane's runs (section version 3). Unlike the version-2 planes, a run count the extension cannot hold is
/// `Truncated` before its bounds are checked; the other refusals are the planes' own ("run", "length").
pub(super) fn read_patience_plan<'a>(
    r: &mut Reader<'a>,
    total: usize,
    cumulative: &mut usize,
) -> Result<PlanePlan<'a>, DecodeError> {
    let count = r.u32()? as usize;
    if count
        .checked_mul(RUN_LEN)
        .is_none_or(|bytes| bytes > r.remaining())
    {
        return Err(DecodeError::Truncated);
    }
    read_plane_plan_counted(r, count, total, cumulative)
}

pub(super) fn validate_runs(
    runs: &[u8],
    count: usize,
    total: usize,
    max_value: u8,
    reason: &'static str,
) -> Result<(), DecodeError> {
    if runs.len()
        != count
            .checked_mul(RUN_LEN)
            .ok_or(DecodeError::BadMap(reason))?
    {
        return Err(DecodeError::BadMap(reason));
    }
    let mut cells = 0usize;
    for chunk in runs.chunks_exact(RUN_LEN) {
        let value = chunk[0];
        let n = u16::from_le_bytes([chunk[1], chunk[2]]) as usize;
        if value > max_value || n == 0 {
            return Err(DecodeError::BadMap(reason));
        }
        cells = cells.checked_add(n).ok_or(DecodeError::BadMap(reason))?;
        if cells > total {
            return Err(DecodeError::BadMap(reason));
        }
    }
    if cells != total {
        return Err(DecodeError::BadMap("length"));
    }
    Ok(())
}

pub(super) fn validate_plane_plan(
    terrain: Option<PlanePlan<'_>>,
    regions: Option<PlanePlan<'_>>,
) -> Result<(), DecodeError> {
    if terrain.is_some_and(|p| p.runs.chunks_exact(3).any(|r| r[0] > 7)) {
        return Err(DecodeError::BadMap("terrain value"));
    }
    if regions.is_some_and(|p| p.runs.chunks_exact(3).any(|r| r[0] > 6)) {
        return Err(DecodeError::BadMap("region value"));
    }
    Ok(())
}

pub(super) fn validate_terrain_compatibility(
    collision: &[u8],
    collision_count: usize,
    terrain: PlanePlan<'_>,
) -> Result<(), DecodeError> {
    let mut ci = RunCursor::new(collision, collision_count);
    let mut ti = RunCursor::new(terrain.runs, terrain.count);
    while let (Some((c, cn)), Some((t, tn))) = (ci.current(), ti.current()) {
        if !terrain_collision_compatible(t, c) {
            return Err(DecodeError::BadMap("terrain collision"));
        }
        let step = cn.min(tn);
        ci.advance(step);
        ti.advance(step);
    }
    Ok(())
}

pub(super) struct RunCursor<'a> {
    bytes: &'a [u8],
    index: usize,
    run_left: usize,
}
impl<'a> RunCursor<'a> {
    fn new(bytes: &'a [u8], _count: usize) -> Self {
        let run_left = if bytes.is_empty() {
            0
        } else {
            usize::from(u16::from_le_bytes([bytes[1], bytes[2]]))
        };
        Self {
            bytes,
            index: 0,
            run_left,
        }
    }
    fn current(&self) -> Option<(u8, usize)> {
        if self.index < self.bytes.len() {
            Some((self.bytes[self.index], self.run_left))
        } else {
            None
        }
    }
    fn advance(&mut self, n: usize) {
        self.run_left -= n;
        if self.run_left == 0 {
            self.index += 3;
            if self.index < self.bytes.len() {
                self.run_left = usize::from(u16::from_le_bytes([
                    self.bytes[self.index + 1],
                    self.bytes[self.index + 2],
                ]));
            }
        }
    }
}

pub(super) fn decoded_bytes(total: usize, flags: u8, presence2: u8) -> Result<usize, DecodeError> {
    let planes = 1
        + [
            PRESENCE_TERRAIN,
            PRESENCE_REGIONS,
            PRESENCE_BUSHES,
            PRESENCE_GROUND_VARIANTS,
        ]
        .iter()
        .filter(|&&bit| flags & bit != 0)
        .count()
        + usize::from(presence2 & PRESENCE2_PATIENCE != 0);
    total
        .checked_mul(planes)
        .ok_or(DecodeError::BadMap("decoded size"))
}

pub(super) fn hash_collision_runs(w: u16, h: u16, runs: &[u8], count: usize) -> u32 {
    let mut state = 0x811c_9dc5u32;
    let update = |state: &mut u32, bytes: &[u8]| {
        for &b in bytes {
            *state = (*state ^ u32::from(b)).wrapping_mul(0x0100_0193);
        }
    };
    update(&mut state, &(w as u32).to_le_bytes());
    update(&mut state, &(h as u32).to_le_bytes());
    for chunk in runs.chunks_exact(RUN_LEN).take(count) {
        for _ in 0..u16::from_le_bytes([chunk[1], chunk[2]]) {
            update(&mut state, &[chunk[0]]);
        }
    }
    state
}

/// One semantic-hash input: a plane hashes its decoded cells, a list section its wire bytes.
#[derive(Clone, Copy)]
pub(super) enum Part<'a> {
    Plane(Option<PlanePlan<'a>>),
    Bytes(Option<&'a [u8]>),
}

pub(super) fn hash_semantic<const N: usize>(
    w: u16,
    h: u16,
    flags: u8,
    presence2: u8,
    parts: [Part<'_>; N],
) -> u32 {
    let mut state = 0x811c_9dc5u32;
    let update = |state: &mut u32, bytes: &[u8]| {
        for &b in bytes {
            *state = (*state ^ u32::from(b)).wrapping_mul(0x0100_0193);
        }
    };
    update(&mut state, crate::ground::SEMANTIC_DOMAIN);
    update(&mut state, &(w as u32).to_le_bytes());
    update(&mut state, &(h as u32).to_le_bytes());
    let (header, len) = section_header(flags, presence2);
    update(&mut state, &header[..len]);
    for part in parts {
        match part {
            Part::Plane(Some(plan)) => {
                for chunk in plan.runs.chunks_exact(RUN_LEN).take(plan.count) {
                    for _ in 0..u16::from_le_bytes([chunk[1], chunk[2]]) {
                        update(&mut state, &[chunk[0]]);
                    }
                }
            }
            Part::Bytes(Some(bytes)) => update(&mut state, bytes),
            Part::Plane(None) | Part::Bytes(None) => {}
        }
    }
    state
}

pub(super) fn context_hash_fn(collision: u32, semantic: u32, mode: GroundMode) -> u32 {
    context_hash(collision, semantic, mode)
}

pub(super) fn decode_runs(bytes: &[u8], count: usize, total: usize) -> Vec<u8> {
    let mut out = Vec::with_capacity(total);
    for chunk in bytes.chunks_exact(RUN_LEN).take(count) {
        out.resize(
            out.len() + u16::from_le_bytes([chunk[1], chunk[2]]) as usize,
            chunk[0],
        );
    }
    out
}

pub(super) fn decode_plan(frame: &[u8]) -> Result<MapBlob, DecodeError> {
    let mut r = Reader::new(frame);
    let plan = preflight(&mut r)?;
    if r.remaining() != 0 {
        return Err(DecodeError::Trailing(r.remaining()));
    }
    let total = usize::from(plan.w) * usize::from(plan.h);
    let tiles = decode_runs(plan.collision_runs, plan.collision_count, total);
    let terrain = plan.terrain.map(|p| decode_runs(p.runs, p.count, total));
    let regions = plan.regions.map(|p| decode_runs(p.runs, p.count, total));
    let public = MapPublic {
        camps: plan.camps.map_or_else(Vec::new, decode_camps),
        coliseum: plan.coliseum.map(decode_coliseum).transpose()?,
        bushes: plan.bushes.map(|p| decode_runs(p.runs, p.count, total)),
        props: plan.props.map_or_else(Vec::new, decode_props),
        ground_variants: plan
            .ground_variants
            .map(|p| decode_runs(p.runs, p.count, total)),
        patience: plan.patience.map(|p| decode_runs(p.runs, p.count, total)),
        anchors: plan.anchors.map_or_else(Vec::new, decode_anchors),
    };
    Ok(MapBlob {
        w: plan.w,
        h: plan.h,
        layer_hash: plan.collision_hash,
        tiles,
        terrain,
        regions,
        terrain_mode: plan.mode,
        semantic_hash: plan.semantic_hash,
        context_hash: plan.context_hash,
        public,
    })
}

pub(super) fn le16(b: &[u8], at: usize) -> u16 {
    u16::from_le_bytes([b[at], b[at + 1]])
}

/// Camp rows from a section the preflight already checked (count byte first).
pub(super) fn decode_camps(section: &[u8]) -> Vec<MapCamp> {
    section[1..]
        .chunks_exact(CAMP_LEN)
        .map(|row| MapCamp {
            x: le16(row, 0),
            y: le16(row, 2),
            r: le16(row, 4),
            zone: row[6],
        })
        .collect()
}

/// Anchor rows from a section the preflight already checked (count byte first).
pub(super) fn decode_anchors(section: &[u8]) -> Vec<MapAnchor> {
    let count = usize::from(section[0]);
    let mut at = 1;
    let mut out = Vec::with_capacity(count);
    for _ in 0..count {
        let mut a = anchor_head(&section[at..at + ANCHOR_FIXED_LEN]);
        let tiles = usize::from(section[at + ANCHOR_FIXED_LEN - 1]);
        at += ANCHOR_FIXED_LEN;
        a.entrance = section[at..at + tiles * ANCHOR_TILE_LEN]
            .chunks_exact(ANCHOR_TILE_LEN)
            .map(|t| MapTile {
                x: le16(t, 0),
                y: le16(t, 2),
            })
            .collect();
        at += tiles * ANCHOR_TILE_LEN;
        out.push(a);
    }
    out
}

pub(super) fn prop_row(row: &[u8]) -> MapProp {
    MapProp {
        kind: row[0],
        x: le16(row, 1),
        y: le16(row, 3),
        variant: row[5],
        flags: row[6],
    }
}

pub(super) fn decode_props(section: &[u8]) -> Vec<MapProp> {
    section[2..].chunks_exact(PROP_LEN).map(prop_row).collect()
}

pub(super) fn decode_coliseum(section: &[u8]) -> Result<MapColiseum, DecodeError> {
    let mut r = Reader::new(section);
    let (x, y, w, h) = (r.u16()?, r.u16()?, r.u16()?, r.u16()?);
    let count = usize::from(r.u8()?);
    let mut gates = Vec::with_capacity(count);
    for _ in 0..count {
        let side = r.u8()?;
        let n = usize::from(r.u8()?);
        let mut tiles = Vec::with_capacity(n);
        for _ in 0..n {
            tiles.push(MapTile {
                x: r.u16()?,
                y: r.u16()?,
            });
        }
        gates.push(MapGate { side, tiles });
    }
    Ok(MapColiseum { x, y, w, h, gates })
}
