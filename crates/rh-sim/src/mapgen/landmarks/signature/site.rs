//! Site geometry and the landing of one drawing: `Piece`, `Built`, `Site`, `stamp` and the small helpers they use.
//! Re-exported into `signature`.

use super::*;

/// P-MAPGEN-SIG-HOLY-1: the drawn hall's feature params, the legacy `Hall` row the certifier (`feature-params`) and
/// the client read: [axis, pitch, radius, piers, bays]. Axis 1 when the nave runs along y (the front read at the anchor
/// is vertical), else 0; pitch `R.hall_pitch`; radius half the box's side less one (18 for 37); piers the wall pieces
/// (8-connected) that are exact `R.square_side` squares (32); no bays.
pub(super) fn hall_params(p: &Piece, bbox: TileRect) -> Vec<i32> {
    let vertical = axis(front(p.anchor.0, p.anchor.1)).0 == 0;
    let walls: Vec<usize> = p
        .cells
        .iter()
        .filter(|c| c.1 == TILE_WALL)
        .map(|c| c.0)
        .collect();
    let piers = wall_pieces(&walls)
        .iter()
        .filter(|g| exact_square(g, R.square_side))
        .count();
    vec![
        i32::from(vertical),
        R.hall_pitch,
        (bbox.w.min(bbox.h) as i32 - 1) / 2,
        i32::try_from(piers).unwrap_or(i32::MAX),
        0,
    ]
}

/// The 8-connected pieces of tiles `walls` (sorted ascending), each in discovery order.
pub(super) fn wall_pieces(walls: &[usize]) -> Vec<Vec<usize>> {
    let mut seen = vec![false; walls.len()];
    let mut out = Vec::new();
    for s in 0..walls.len() {
        if seen[s] {
            continue;
        }
        seen[s] = true;
        let (mut piece, mut stack) = (Vec::new(), vec![s]);
        while let Some(k) = stack.pop() {
            piece.push(walls[k]);
            let (x, y) = ((walls[k] % W) as i32, (walls[k] / W) as i32);
            for dy in -1..=1 {
                for dx in -1..=1 {
                    let Some(j) = at(x + dx, y + dy) else {
                        continue;
                    };
                    if let Ok(n) = walls.binary_search(&j) {
                        if !seen[n] {
                            seen[n] = true;
                            stack.push(n);
                        }
                    }
                }
            }
        }
        out.push(piece);
    }
    out
}

/// Whether tiles `g` are exactly a full `side` x `side` square.
pub(super) fn exact_square(g: &[usize], side: usize) -> bool {
    let b = bounds(g.iter().copied());
    g.len() == side * side && b.w as usize == side && b.h as usize == side
}

/// P-MAPGEN-SIG-HOLY-1, P-MAPGEN-SIG-HOLY-1-fix: a centred entry's site camps for the square of side `side` with top-left
/// (x, y): the camp glyph's grid tiles `marks` (`SiteCamps::glyph`, grid order) where the drawing lands centred in the
/// site's inner rect (the site less `bands[0]`), its front `axis(front(t))` at the inner rect's centre tile `t`, as
/// `stamp` reads it. Unmirrored: the hall's `E` tiles stand on its mirror axis, so the stamp's later mirror draw cannot
/// move them (a glyph off the axis would land mirrored half the time: the hall is the only such entry today). `None` (not
/// a candidate) when the drawing does not land there or a camp tile is off the map; the camps' halo legality is camp
/// placement's (`zones::camps::place_camps_reserving`).
pub(super) fn centred_camps(
    bp: &Blueprint,
    marks: &[(i32, i32)],
    x: i32,
    y: i32,
    side: i32,
    d: &Definition,
) -> Option<Vec<usize>> {
    let b = d.bands[0];
    let inner = TileRect {
        x: x + b,
        y: y + b,
        w: u32::try_from(side - 2 * b).ok()?,
        h: u32::try_from(side - 2 * b).ok()?,
    };
    let t = (inner.x + inner.w as i32 / 2, inner.y + inner.h as i32 / 2);
    let o = Orientation {
        front: axis(front(t.0, t.1)),
        mirrored: false,
    };
    let landing = bp.land(Placement::Centre, o, &Target { inner, camp: None })?;
    marks
        .iter()
        .map(|&g| {
            let p = landing.tile(g);
            at(p.0, p.1)
        })
        .collect()
}

/// What `intact` accepts on the entrance tiles of drawing `bp` under entrance rule `rule` (`Door`): `Ok(None)` for a
/// drawing without an entrance, `Err` when its entrance tiles' glyphs do not all write one thing (or a tile has no
/// glyph): such an entry cannot be judged by one door and is refused (`SignatureContext::from_definition`).
pub(super) fn entrance_door(
    bp: &Blueprint,
    rule: blueprint::EntranceRule,
) -> Result<Option<Door>, ()> {
    let mut door: Option<Door> = None;
    for g in bp.entrance(rule) {
        let glyph = bp
            .char_at(g)
            .and_then(|ch| bp.legend().glyph(ch))
            .ok_or(())?;
        let d = Door {
            write: written(glyph.write),
            owner: glyph.feature,
        };
        if door.is_some_and(|first| first != d) {
            return Err(());
        }
        door = Some(d);
    }
    Ok(door)
}

/// The (tile, terrain) a glyph's write leaves on its map tile; `None` for `Write::Nothing`.
pub(super) fn written(w: Write) -> Option<(u8, u8)> {
    match w {
        Write::Nothing => None,
        Write::Wall => Some((TILE_WALL, 0)),
        Write::Low(k) => Some((TILE_LOW, k)),
        Write::Floor(k) => Some((TILE_FLOOR, k)),
    }
}

/// Every prefix camp's certifier halo (`mark_halo`): the tiles no landmark wall may take.
pub(super) fn camp_halo(frame: &AttemptFrame) -> Vec<u8> {
    let mut halo = vec![0u8; W * H];
    for c in &frame.state.layout.camps {
        mark_halo(&mut halo, c.site);
    }
    halo
}

/// P-MAPGEN-SIG-BLUEPRINT-1: one landmark drawn from its table entry `spec` and its parsed drawing in the zone's
/// reserved site `target`, with the landing it used and every mark tile of its legend in grid order (by character, then
/// the drawing's row-major order) on the map tile it landed on; `None` refuses the attempt (G5: the drawing is never served
/// smaller). In order, each step as the cave's, the church's and the tree's own code did before this packet:
/// 1. the zone's one draw on its signature stream, `pick(rng, 2) == 1` = mirrored, before anything can refuse;
/// 2. a drawing that did not parse refuses;
/// 3. the centre `t` is the camp tile (`Placement::Pin`) or the inner rect's centre tile (`Placement::Centre`); the
///    front row faces `axis(front(t))`; the drawing lands by the entry's placement rule (`Blueprint::land`);
/// 4. every tile passes its glyph's legality class;
/// 5. where the entry says `doorway`, the `Definition::doorway` tiles beyond each entrance tile, along the front, are
///    `Site::open`;
/// 6. the wall tiles form no 8-connected piece under `R.piece_min`, and the walls with the LOW tiles do not split the
///    zone's floor;
/// 7. each feature of the entry, in its order, owns the tiles its glyphs write, sorted by tile index.
///
/// The anchor: centre `t`, footprint the landing's box, front `front(t)`, entrance the entry's entrance rule landed,
/// row-major (by y, then x) without repeats. The volcano's centre is the site's centre tile: one of the drawing's four
/// middle crater-lake tiles (10|11, 10|11) of the 22 x 22 drawing centred in the 30 x 30 inner rect, which one depending
/// on the orientation, so `front == front(centre)` holds for it as for every anchor (P-MAPGEN-SIG-SCALE-4);
/// `FeatureAnchor::EntranceMiddle` reads the landed entrance in the drawing's grid order (index `len / 2`), as before the
/// sort. Pure but for the one draw.
pub(super) fn stamp(
    s: &Site<'_>,
    rng: &mut Mulberry32,
    spec: &spec::Spec,
    drawing: Option<&Blueprint>,
    target: &Target,
) -> Option<(Built, blueprint::Landing, Vec<frame::Mark>)> {
    let mirrored = pick(rng, 2) == 1;
    let bp = drawing?;
    let t = match spec.placement {
        Placement::Pin(_) => target.camp?,
        Placement::Centre => (
            target.inner.x + target.inner.w as i32 / 2,
            target.inner.y + target.inner.h as i32 / 2,
        ),
    };
    let f = front(t.0, t.1);
    let u = axis(f);
    let landing = bp.land(spec.placement, Orientation { front: u, mirrored }, target)?;
    let legal = |l: Legality, (x, y): (i32, i32)| match l {
        Legality::Wall => s.wall(x, y),
        Legality::WallGround => s.wall(x, y) && s.ground(x, y),
        Legality::Floor => s.floor(x, y),
    };
    let mut cells: Vec<Vec<(usize, u8, u8)>> = vec![Vec::new(); spec.features.len()];
    let (mut walls, mut solid) = (Vec::new(), Vec::new());
    for (g, glyph) in bp.glyphs() {
        let p = landing.tile(g);
        if !legal(glyph.legality, p) {
            return None;
        }
        let i = at(p.0, p.1)?;
        let Some((tile, terrain)) = written(glyph.write) else {
            continue;
        };
        let owner = spec
            .features
            .iter()
            .position(|fs| Some(fs.kind) == glyph.feature);
        debug_assert!(
            owner.is_some(),
            "stamp: glyph '{}' of the {:?} legend writes a tile no feature of the entry owns",
            glyph.ch as char,
            spec.zone
        );
        cells[owner?].push((i, tile, terrain));
        if matches!(glyph.write, Write::Wall | Write::Low(_)) {
            solid.push(i);
        }
        if glyph.write == Write::Wall {
            walls.push(i);
        }
    }
    let landed_tile = |g| {
        let (x, y) = landing.tile(g);
        TilePt { x, y }
    };
    let entrance: Vec<TilePt> = bp
        .entrance(spec.entrance)
        .into_iter()
        .map(landed_tile)
        .collect();
    let doorway = !spec.doorway
        || entrance
            .iter()
            .all(|p| (1..=s.ctx.rules.doorway).all(|a| s.open(p.x + a * u.0, p.y + a * u.1)));
    if !doorway
        || church::smallest_piece(&walls) < R.piece_min
        || s.floor_pieces(&solid) > s.floor_pieces(&[])
    {
        return None;
    }
    // The tiles the spawn rule keeps clear of: the landed tiles of its glyph (by the glyph, not by what it writes).
    let clear: Vec<usize> = match spec.spawn {
        spec::SpawnRule::OpenFloor { clear_of, .. } => bp
            .tiles_of(clear_of)
            .into_iter()
            .filter_map(|g| {
                let p = landing.tile(g);
                at(p.0, p.1)
            })
            .collect(),
        spec::SpawnRule::Box => Vec::new(),
    };
    let mut pieces = Vec::with_capacity(spec.features.len());
    for (fs, mut cells) in spec.features.iter().zip(cells) {
        cells.sort_unstable_by_key(|c| c.0);
        let anchor = match fs.anchor {
            spec::FeatureAnchor::Centre => t,
            spec::FeatureAnchor::EntranceMiddle => {
                let m = entrance.get(entrance.len() / 2)?;
                (m.x, m.y)
            }
        };
        pieces.push(Piece {
            kind: fs.kind,
            job: fs.job,
            anchor,
            cells,
            clear: clear.clone(),
        });
    }
    // Grid order: by character, then the drawing's row-major order (`Blueprint::marks`).
    let marks: Vec<frame::Mark> = bp
        .marks()
        .into_iter()
        .flat_map(|(ch, gs)| {
            gs.into_iter().map(move |grid| frame::Mark {
                ch,
                grid,
                tile: landed_tile(grid),
            })
        })
        .collect();
    // The anchor's entrance is row-major (P-MAPGEN-SIG-BLUEPRINT-2); the grid order above stays the feature anchors'.
    let mut entrance = entrance;
    entrance.sort_unstable_by_key(|p| (p.y, p.x));
    entrance.dedup();
    let anchor = ZoneAnchor {
        zone: spec.zone,
        kind: spec.kind,
        centre: TilePt { x: t.0, y: t.1 },
        footprint: landing.footprint,
        front: f,
        entrance,
    };
    Some((Built { pieces, anchor }, landing, marks))
}

/// One feature the step writes: kind, job, anchor point and its cells (index, tile, terrain), and (P-MAPGEN-SIG-HOLY-1-fix)
/// the map tiles of the entry's `SpawnRule::OpenFloor::clear_of` glyph where the drawing landed (`stamp`; empty for
/// `SpawnRule::Box` and for the volcano's streams and pools), ascending by grid order.
pub(crate) struct Piece {
    pub(super) kind: FeatureKind,
    pub(super) job: FeatureJob,
    pub(super) anchor: (i32, i32),
    pub(super) cells: Vec<(usize, u8, u8)>,
    pub(super) clear: Vec<usize>,
}

/// A zone's landmark: its features (the anchor feature first) and its anchor.
pub(crate) struct Built {
    pub(super) pieces: Vec<Piece>,
    pub(super) anchor: ZoneAnchor,
}

/// What the step may read while choosing one zone's site.
pub(crate) struct Site<'a> {
    pub(super) ctx: &'a SignatureContext<'a>,
    pub(super) zone: &'a [u8],
    pub(super) slot: u8,
    pub(super) tiles: &'a [u8],
    pub(super) owner: &'a [u16],
    /// The legacy landmark stage's protection (border, coliseum road, lanes, camp clearings with margin, corners).
    pub(super) protect: &'a [u8],
    /// The aprons marked so far (`LandmarkStage::masks.near1`: the legacy buildings grown by the spawn rules' margin and
    /// every signature landmark placed before this one).
    pub(super) apron: &'a [u8],
    /// The certifier's camp halo of every camp (no wall may stand there).
    pub(super) halo: &'a [u8],
    /// P-MAPGEN-SIG-RESERVE-1: the zone's reserved site less its apron (x0, y0, x1, y1 inclusive), which confines
    /// `floor` (and with it `wall` and `ground`) for every landmark drawn from the table; `None` for the volcano's
    /// streams and pools, which leave the site (`volcano::streams`).
    pub(super) inner: Option<(i32, i32, i32, i32)>,
}

impl Site<'_> {
    /// Free floor of this zone, owned by no feature, off the coliseum, road and lanes (and inside `inner`, if any).
    pub(super) fn floor(&self, x: i32, y: i32) -> bool {
        self.inner
            .is_none_or(|r| x >= r.0 && x <= r.2 && y >= r.1 && y <= r.3)
            && at(x, y).is_some_and(|i| {
                self.zone[i] == self.slot
                    && self.tiles[i] == TILE_FLOOR
                    && self.owner[i] == 0
                    && self.ctx.hard[i] == 0
            })
    }
    /// Floor of any zone, owned by no feature, off the coliseum, road and lanes: walkable ground beside a landmark.
    pub(super) fn open(&self, x: i32, y: i32) -> bool {
        at(x, y).is_some_and(|i| {
            self.zone[i] != 0
                && self.tiles[i] == TILE_FLOOR
                && self.owner[i] == 0
                && self.ctx.hard[i] == 0
        })
    }
    /// As `floor`, and outside every camp's halo: a landmark wall may stand here.
    pub(super) fn wall(&self, x: i32, y: i32) -> bool {
        self.floor(x, y) && self.halo[at(x, y).unwrap()] == 0
    }
    /// As `floor`, outside the legacy landmark protection and outside every apron marked so far: landmark terrain may
    /// lie here.
    pub(super) fn ground(&self, x: i32, y: i32) -> bool {
        self.floor(x, y) && {
            let i = at(x, y).unwrap();
            self.protect[i] == 0 && self.apron[i] == 0
        }
    }
    /// 4-connected pieces of this zone's floor with `walls` added.
    pub(super) fn floor_pieces(&self, walls: &[usize]) -> usize {
        let mut seen: Vec<bool> = (0..W * H)
            .map(|i| self.zone[i] != self.slot || self.tiles[i] != TILE_FLOOR)
            .collect();
        for &i in walls {
            seen[i] = true;
        }
        let mut pieces = 0;
        let mut stack = Vec::new();
        for s in 0..W * H {
            if seen[s] {
                continue;
            }
            pieces += 1;
            seen[s] = true;
            stack.push(s);
            while let Some(i) = stack.pop() {
                let (x, y) = ((i % W) as i32, (i / W) as i32);
                for (dx, dy) in [(1, 0), (-1, 0), (0, 1), (0, -1)] {
                    if let Some(j) = at(x + dx, y + dy).filter(|&j| !seen[j]) {
                        seen[j] = true;
                        stack.push(j);
                    }
                }
            }
        }
        pieces
    }
}

pub(crate) fn at(x: i32, y: i32) -> Option<usize> {
    (x >= 0 && y >= 0 && (x as usize) < W && (y as usize) < H).then(|| y as usize * W + x as usize)
}

pub(super) fn bounds(cells: impl Iterator<Item = usize>) -> TileRect {
    let (mut x0, mut y0, mut x1, mut y1) = (i32::MAX, i32::MAX, i32::MIN, i32::MIN);
    for i in cells {
        let (x, y) = ((i % W) as i32, (i / W) as i32);
        x0 = x0.min(x);
        y0 = y0.min(y);
        x1 = x1.max(x);
        y1 = y1.max(y);
    }
    TileRect {
        x: x0,
        y: y0,
        w: (x1 - x0 + 1) as u32,
        h: (y1 - y0 + 1) as u32,
    }
}

/// The DIR16 index from tile (x, y) toward the coliseum centre tile: the largest dot product, first on a tie.
pub(crate) fn front(x: i32, y: i32) -> u8 {
    let (dx, dy) = (i64::from(CX - x), i64::from(CY - y));
    let mut best = (0, i64::MIN);
    for (k, v) in DIR16.iter().enumerate() {
        let dot = dx * i64::from(v[0]) + dy * i64::from(v[1]);
        if dot > best.1 {
            best = (k, dot);
        }
    }
    best.0 as u8
}

/// The certifier's camp halo (`certify` `protected`): the clearing grown by the halo span.
pub(super) fn mark_halo(out: &mut [u8], site: usize) {
    let (cx, cy) = ((site % W) as i32, (site / W) as i32);
    let s = R.protection_span;
    for dy in -s..=s {
        for dx in -s..=s {
            let near = (-R.halo..=R.halo).any(|oy| {
                (-R.halo..=R.halo).any(|ox| {
                    f64::from((dx + ox).pow(2) + (dy + oy).pow(2)) <= R.clearing * R.clearing
                })
            });
            if let Some(i) = at(cx + dx, cy + dy).filter(|_| near) {
                out[i] = 1;
            }
        }
    }
}

/// A uniform pick from `n > 0` choices.
pub(super) fn pick(rng: &mut Mulberry32, n: usize) -> usize {
    (rng.next_u32() % n as u32) as usize
}
