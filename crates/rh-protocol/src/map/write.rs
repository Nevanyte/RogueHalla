//! Map encoding: section writers, lengths and the pre-encode checks.

use super::*;

pub(super) fn presence(terrain: bool, regions: bool) -> u8 {
    u8::from(terrain) | (u8::from(regions) << 1)
}
pub(super) fn rle_len(plane: &[u8]) -> usize {
    RUN_LEN * Runs { values: plane }.count() + 4
}
pub(super) fn write_plane(w: &mut Writer, plane: Option<&[u8]>) {
    if let Some(values) = plane {
        let run_count = Runs { values }.count();
        w.u32(run_count as u32);
        for (value, count) in (Runs { values }) {
            w.u8(value);
            w.u16(count);
        }
    }
}
/// Presence bits of the protocol 17 sections: a list is present when non-empty, a plane or the coliseum when `Some`.
pub(super) fn public_presence(p: &MapPublic) -> u8 {
    let mut flags = 0;
    if !p.camps.is_empty() {
        flags |= PRESENCE_CAMPS;
    }
    if p.coliseum.is_some() {
        flags |= PRESENCE_COLISEUM;
    }
    if p.bushes.is_some() {
        flags |= PRESENCE_BUSHES;
    }
    if !p.props.is_empty() {
        flags |= PRESENCE_PROPS;
    }
    if p.ground_variants.is_some() {
        flags |= PRESENCE_GROUND_VARIANTS;
    }
    flags
}
/// Second presence byte (section version 3): 0 when no second-presence section is present (the map is version 2).
pub(super) fn public_presence2(p: &MapPublic) -> u8 {
    let mut flags = 0;
    if p.patience.is_some() {
        flags |= PRESENCE2_PATIENCE;
    }
    if !p.anchors.is_empty() {
        flags |= PRESENCE2_ANCHORS;
    }
    flags
}
/// Extension bytes before the first section: version, presence, (version 3) presence2, semantic hash.
pub(super) fn extension_header_len(p: &MapPublic) -> usize {
    EXTENSION_STATIC_HEADER_LEN + usize::from(public_presence2(p) != 0)
}

pub(super) fn camps_len(camps: &[MapCamp]) -> usize {
    if camps.is_empty() {
        0
    } else {
        1 + CAMP_LEN * camps.len()
    }
}
pub(super) fn coliseum_len(coliseum: Option<&MapColiseum>) -> usize {
    coliseum.map_or(0, |c| {
        9 + c.gates.iter().map(|g| 2 + 4 * g.tiles.len()).sum::<usize>()
    })
}
pub(super) fn anchors_len(anchors: &[MapAnchor]) -> usize {
    if anchors.is_empty() {
        0
    } else {
        1 + anchors
            .iter()
            .map(|a| ANCHOR_FIXED_LEN + ANCHOR_TILE_LEN * a.entrance.len())
            .sum::<usize>()
    }
}
pub(super) fn props_len(props: &[MapProp]) -> usize {
    if props.is_empty() {
        0
    } else {
        2 + PROP_LEN * props.len()
    }
}
/// Wire bytes of every present public section (only meaningful once [`check_public`] passed).
pub(super) fn public_len(p: &MapPublic) -> usize {
    camps_len(&p.camps)
        + coliseum_len(p.coliseum.as_ref())
        + p.bushes.as_deref().map_or(0, rle_len)
        + props_len(&p.props)
        + p.ground_variants.as_deref().map_or(0, rle_len)
        + p.patience.as_deref().map_or(0, rle_len)
        + anchors_len(&p.anchors)
}

pub(super) fn write_camps(w: &mut Writer, camps: &[MapCamp]) {
    if camps.is_empty() {
        return;
    }
    w.u8(camps.len() as u8);
    for c in camps {
        w.u16(c.x);
        w.u16(c.y);
        w.u16(c.r);
        w.u8(c.zone);
    }
}
pub(super) fn write_coliseum(w: &mut Writer, coliseum: Option<&MapColiseum>) {
    let Some(c) = coliseum else {
        return;
    };
    w.u16(c.x);
    w.u16(c.y);
    w.u16(c.w);
    w.u16(c.h);
    w.u8(c.gates.len() as u8);
    for g in &c.gates {
        w.u8(g.side);
        w.u8(g.tiles.len() as u8);
        for t in &g.tiles {
            w.u16(t.x);
            w.u16(t.y);
        }
    }
}
pub(super) fn write_props(w: &mut Writer, props: &[MapProp]) {
    if props.is_empty() {
        return;
    }
    w.u16(props.len() as u16);
    for p in props {
        w.u8(p.kind);
        w.u16(p.x);
        w.u16(p.y);
        w.u8(p.variant);
        w.u8(p.flags);
    }
}
pub(super) fn write_anchors(w: &mut Writer, anchors: &[MapAnchor]) {
    if anchors.is_empty() {
        return;
    }
    w.u8(anchors.len() as u8);
    for a in anchors {
        w.u8(a.zone);
        w.u8(a.kind);
        w.u16(a.centre.x);
        w.u16(a.centre.y);
        w.u16(a.footprint.x);
        w.u16(a.footprint.y);
        w.u16(a.footprint.w);
        w.u16(a.footprint.h);
        w.u8(a.front);
        w.u8(a.entrance.len() as u8);
        for t in &a.entrance {
            w.u16(t.x);
            w.u16(t.y);
        }
    }
}
/// The public sections in wire (presence bit) order.
pub(super) fn write_public(w: &mut Writer, p: &MapPublic) {
    write_camps(w, &p.camps);
    write_coliseum(w, p.coliseum.as_ref());
    write_plane(w, p.bushes.as_deref());
    write_props(w, &p.props);
    write_plane(w, p.ground_variants.as_deref());
    write_plane(w, p.patience.as_deref());
    write_anchors(w, &p.anchors);
}

pub(super) fn section_bytes(len: usize, f: impl FnOnce(&mut Writer)) -> Vec<u8> {
    let mut w = Writer::with_capacity(len);
    f(&mut w);
    w.finish()
}

/// Version 2 semantic hash: planes hash their decoded cells, list sections their wire bytes (count included). Version 3
/// hashes `[3, presence, presence2]` instead of `[2, presence]`, the patience cells after the ground-variant cells, then
/// the anchors section's wire bytes.
pub(super) fn public_semantic_hash(
    w: u16,
    h: u16,
    terrain: Option<&[u8]>,
    regions: Option<&[u8]>,
    p: &MapPublic,
) -> u32 {
    let flags = presence(terrain.is_some(), regions.is_some()) | public_presence(p);
    let camps = section_bytes(camps_len(&p.camps), |w| write_camps(w, &p.camps));
    let coliseum = section_bytes(coliseum_len(p.coliseum.as_ref()), |w| {
        write_coliseum(w, p.coliseum.as_ref())
    });
    let props = section_bytes(props_len(&p.props), |w| write_props(w, &p.props));
    let anchors = section_bytes(anchors_len(&p.anchors), |w| write_anchors(w, &p.anchors));
    semantic_hash_parts2(
        w,
        h,
        flags,
        public_presence2(p),
        [
            terrain.unwrap_or_default(),
            regions.unwrap_or_default(),
            &camps,
            &coliseum,
            p.bushes.as_deref().unwrap_or_default(),
            &props,
            p.ground_variants.as_deref().unwrap_or_default(),
            p.patience.as_deref().unwrap_or_default(),
            &anchors,
        ],
    )
}

/// The encoder's half of the decoder's section checks, with the same reasons; it never clamps or drops.
pub(super) fn check_public(w: u16, h: u16, p: &MapPublic) -> Result<(), EncodeError> {
    let total = usize::from(w) * usize::from(h);
    if p.camps.len() > MAX_MAP_CAMPS {
        return Err(EncodeError::BadMap("camp count"));
    }
    if p.camps
        .iter()
        .any(|c| c.zone == 0 || c.zone > MAX_CAMP_ZONE)
    {
        return Err(EncodeError::BadMap("camp zone"));
    }
    if let Some(c) = &p.coliseum {
        if !coliseum_rect_fits(c.x, c.y, c.w, c.h, w, h) {
            return Err(EncodeError::BadMap("coliseum rect"));
        }
        if c.gates.len() > MAX_COLISEUM_GATES {
            return Err(EncodeError::BadMap("gate count"));
        }
        for g in &c.gates {
            if g.side > MAX_GATE_SIDE {
                return Err(EncodeError::BadMap("gate side"));
            }
            if g.tiles.is_empty() || g.tiles.len() > MAX_GATE_TILES {
                return Err(EncodeError::BadMap("gate tiles"));
            }
            if g.tiles.iter().any(|t| t.x >= w || t.y >= h) {
                return Err(EncodeError::BadMap("gate tile"));
            }
        }
    }
    for plane in [
        p.bushes.as_deref(),
        p.ground_variants.as_deref(),
        p.patience.as_deref(),
    ]
    .into_iter()
    .flatten()
    {
        if plane.len() != total {
            return Err(EncodeError::BadMap("plane length"));
        }
    }
    if p.props.len() > MAX_MAP_PROPS {
        return Err(EncodeError::BadMap("prop count"));
    }
    let mut previous = None;
    for prop in &p.props {
        check_prop(prop, previous, w, h).map_err(EncodeError::BadMap)?;
        previous = Some(prop);
    }
    if p.ground_variants
        .as_deref()
        .is_some_and(|g| g.iter().any(|&v| v > MAX_GROUND_VARIANT))
    {
        return Err(EncodeError::BadMap("ground variant"));
    }
    if p.patience
        .as_deref()
        .is_some_and(|plane| plane.iter().any(|&v| usize::from(v) > p.camps.len()))
    {
        return Err(EncodeError::BadMap("patience camp"));
    }
    check_anchors(w, h, &p.anchors).map_err(EncodeError::BadMap)
}

/// The encoder's anchor checks, in the decoder's order (count, then per row: the fixed part, the entrance count, the
/// entrance tiles).
pub(super) fn check_anchors(w: u16, h: u16, anchors: &[MapAnchor]) -> Result<(), &'static str> {
    if anchors.len() > MAX_MAP_ANCHORS {
        return Err("anchor count");
    }
    let mut previous = 0;
    for a in anchors {
        check_anchor_head(a, previous, w, h)?;
        if a.entrance.len() > MAX_ANCHOR_ENTRANCE {
            return Err("anchor entrance");
        }
        if a.entrance.iter().any(|t| t.x >= w || t.y >= h) {
            return Err("anchor entrance");
        }
        previous = a.zone;
    }
    Ok(())
}

/// The checks on an anchor row's fixed part, shared by the encoder and the decoder, in the wire rules' order: zone, order
/// (`previous` is the zone of the row before, 0 for the first), kind, footprint, centre, front. The entrance is checked
/// by the caller.
pub(super) fn check_anchor_head(a: &MapAnchor, previous: u8, w: u16, h: u16) -> Result<(), &'static str> {
    if a.zone == 0 || a.zone > MAX_CAMP_ZONE {
        return Err("anchor zone");
    }
    if a.zone <= previous {
        return Err("anchor order");
    }
    if !(ANCHOR_KIND_VOLCANO..=ANCHOR_KIND_HALL).contains(&a.kind) {
        return Err("anchor kind");
    }
    let f = a.footprint;
    if !coliseum_rect_fits(f.x, f.y, f.w, f.h, w, h) {
        return Err("anchor footprint");
    }
    // Half-open: `x <= cx < x + w`, `y <= cy < y + h` (no u16 overflow).
    let inside = |c: u16, start: u16, len: u16| {
        c >= start && u32::from(c) < u32::from(start) + u32::from(len)
    };
    if !inside(a.centre.x, f.x, f.w) || !inside(a.centre.y, f.y, f.h) {
        return Err("anchor centre");
    }
    if a.front >= ANCHOR_DIRS {
        return Err("anchor front");
    }
    Ok(())
}

pub(super) fn coliseum_rect_fits(x: u16, y: u16, cw: u16, ch: u16, w: u16, h: u16) -> bool {
    cw >= 1
        && ch >= 1
        && u32::from(x) + u32::from(cw) <= u32::from(w)
        && u32::from(y) + u32::from(ch) <= u32::from(h)
}

pub(super) fn terrain_collision_compatible(terrain: u8, collision: u8) -> bool {
    match terrain {
        0 => collision <= 2,
        1..=3 => collision == 1 || collision == 2,
        4..=7 => collision == 0,
        _ => false,
    }
}
