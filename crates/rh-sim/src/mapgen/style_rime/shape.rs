use super::*;
use crate::mapgen::{
    landmarks::{
        recipe_support::js_round,
        signature::{self, band, Band},
    },
    style_features::dir_index16,
};
pub(super) const NORMALS: [[i32; 2]; 8] = [
    [1, 0],
    [-1, 0],
    [0, 1],
    [0, -1],
    [1, 1],
    [-1, -1],
    [1, -1],
    [-1, 1],
];
pub(super) fn facet(rng: &mut crate::rng::Mulberry32, rad: f64, d: &Definition) -> Vec<[i32; 2]> {
    let mut picks = vec![];
    for k in 0..8 {
        if rng.next_f64() < d.facet_diagonal_chance || k < 4 {
            picks.push(k);
        }
    }
    let ds: Vec<_> = picks
        .iter()
        .map(|&k| {
            if k < 4 {
                rad * (d.facet_axis_base + rng.next_f64() * d.facet_axis_range)
            } else {
                rad * (d.facet_diag_base + rng.next_f64() * d.facet_diag_range)
            }
        })
        .collect();
    let reach = (rad * d.facet_reach).ceil() as i32 + 1;
    let mut cells = vec![];
    for oy in -reach..=reach {
        for ox in -reach..=reach {
            if picks
                .iter()
                .zip(&ds)
                .all(|(&k, &v)| f64::from(NORMALS[k][0] * ox + NORMALS[k][1] * oy) <= v)
            {
                cells.push([ox, oy]);
            }
        }
    }
    cells.sort_by_key(|&[x, y]| (x * x + y * y, y, x));
    cells
}
fn shelf_tiles(
    w: usize,
    h: usize,
    rng: &mut crate::rng::Mulberry32,
    centre: i32,
    rad: f64,
    d: &Definition,
) -> Vec<i32> {
    let (cx, cy) = (centre % w as i32, centre / w as i32);
    facet(rng, rad, d)
        .into_iter()
        .filter_map(|[dx, dy]| {
            let (x, y) = (cx + dx, cy + dy);
            (x >= 0 && y >= 0 && x < w as i32 && y < h as i32).then_some(y * w as i32 + x)
        })
        .collect()
}
fn core_free(c: &ShapeContext<'_, '_>, band: &[u8], centre: i32) -> bool {
    let b = c.base();
    let (w, h) = (b.w as i32, b.h as i32);
    let (x, y) = (centre % w, centre / w);
    x >= 1
        && y >= 1
        && x < w - 1
        && y < h - 1
        && (-1..=1).all(|dy| {
            (-1..=1).all(|dx| {
                let j = centre + dy * w + dx;
                c.open(j) && band[j as usize] == 0
            })
        })
}
fn feature(
    kind: FeatureKind,
    job: FeatureJob,
    anchor: [i32; 2],
    params: Vec<i32>,
    cells: Vec<i32>,
    floor: Option<Vec<i32>>,
) -> FeatureSpec {
    FeatureSpec {
        kind,
        variant: None,
        jobs: vec![job],
        anchor,
        params: Some(params),
        blocks: cells.into_iter().map(|i| (i, BlockKind::Wall)).collect(),
        groups: None,
        floor,
        mode: Mode::Whole,
        min_piece: None,
        min_share_permille: None,
        min_groups: None,
    }
}
pub(super) fn run(cb: &Callbacks<'_>, c: &mut ShapeContext<'_, '_>) -> Result<Plan, Error> {
    cb.admit_geometry(&c.base())?;
    let d = &cb.definition;
    let mut plan = Plan::default();
    let corridor = c.route_guard().corridor().to_vec();
    let b = c.base();
    let (w, h) = (b.w, b.h);
    let input = field_input(&b, cb.fixed);
    let clr = rime::clearance_field(
        &input,
        &cb.shared,
        rime::ClearanceOptions {
            spawns: b.spawns,
            corridor: Some(&corridor),
            margin_spawn: d.margin_spawn,
            margin_corridor: d.margin_corridor,
        },
        |i| b.is_owned(i),
    );
    let mut allow = rime::allow_field(&input, &clr, &cb.shared, |i| b.is_owned(i));
    // Signature mode (P-MAPGEN-SIG-FROST-1): `signature_anchor` is `Some` only when the Landmarks phase handed this zone
    // the cave's frame (P-MAPGEN-LANDMARK-FRAME-1). No lake tile in the cave's core band (its apron). Option off this is
    // `None` and the styler draws and places exactly what it did before (G8).
    //
    // P-LAND-FROST-1 (owner decision 2026-11-14): in signature mode the lakes are built, not grown and repaired: the
    // mouth lake in front of the mouth, the dry way round it and the second lake (`mouth_lake::build`, which also draws
    // the spawn routes round them). The fallback: when the build gives no mouth lake the plan can keep (no line, no
    // ground, or a mouth lake under `lake_min`, which the plan drops, and the second lake with it: no ice at the mouth),
    // the styler takes this map back to today's signature growth on a fresh route book, exactly as before this packet
    // (the mere seeded at the mouth, `grow_lakes_at`, else `grow_lakes`; `fill`; `keep_off_routes` after a seeded
    // mere), and `lake-at-mouth` decides the map as it did. No draw: the growth reads the noise only.
    let anchor = signature_anchor(c.base());
    let mut book = routes::RouteBook::new(&b, &corridor, cb.fixed, d);
    let mut sig = None;
    let mut lakes = match anchor.as_ref() {
        Some(a) => {
            for (i, v) in allow.iter_mut().enumerate() {
                if *v == 1 && band(a, (i % w) as i32, (i / w) as i32) == Band::Core {
                    *v = 0;
                }
            }
            match mouth_lake::build(cb, &b, &c.streams.noise, &clr, &allow, &mut book, a)
                .filter(|(l, _)| l.first().is_some_and(|m| m.area >= d.lake_min))
            {
                Some((l, s)) => {
                    sig = Some(s);
                    l
                }
                None => {
                    book = routes::RouteBook::new(&b, &corridor, cb.fixed, d);
                    let free: Vec<u8> = allow
                        .iter()
                        .zip(&book.band)
                        .map(|(&v, &r)| u8::from(v == 1 && r == 0))
                        .collect();
                    let mouth: Vec<usize> = a
                        .entrance
                        .iter()
                        .map(|p| p.y as usize * w + p.x as usize)
                        .collect();
                    let sd = &signature::DEFAULT_DEFINITION;
                    let seeded = lakes::grow_lakes_at(
                        &b,
                        &c.streams.noise,
                        &clr,
                        &free,
                        d,
                        &cb.shared,
                        &mouth,
                        sd,
                    );
                    let at_mouth = seeded.is_some();
                    let mut lakes = seeded.unwrap_or_else(|| {
                        lakes::grow_lakes(&b, &c.streams.noise, &clr, &allow, d, &cb.shared)
                    });
                    if !lakes.is_empty() {
                        book.ensure(&b, &lakes, d, cb.fixed);
                        lakes::fill(&b, &allow, &mut lakes, &mut book, d, cb.fixed);
                        if at_mouth {
                            lakes::keep_off_routes(
                                &b,
                                &allow,
                                &mut lakes,
                                &mut book,
                                d,
                                cb.fixed,
                                sd.lake_rounds,
                            );
                        }
                    }
                    lakes
                }
            }
        }
        None => {
            let mut lakes = lakes::grow_lakes(&b, &c.streams.noise, &clr, &allow, d, &cb.shared);
            if !lakes.is_empty() {
                book.ensure(&b, &lakes, d, cb.fixed);
                lakes::fill(&b, &allow, &mut lakes, &mut book, d, cb.fixed);
            }
            lakes
        }
    };
    if lakes.is_empty() {
        return Ok(plan);
    }
    plan.routed = book.routed;
    let kept = |j: i32| sig.as_ref().is_some_and(|s| s.keep[j as usize] == 1);
    let mut chosen: Vec<i32> = b
        .camps
        .iter()
        .filter_map(|camp| {
            let i = camp.y * w as i32 + camp.x;
            (b.mask[i as usize] == 1).then_some(i)
        })
        .collect();
    for k in 0..lakes.len() {
        let lake = lakes[k].clone();
        // The plan's index of this lake: `k`, but for a signature lake too small to keep (never with the option off).
        let pk = plan.lakes.len();
        if sig.is_some() && lake.area < d.lake_min {
            continue;
        }
        // The lake's size as grown, before its shelves and (in signature mode) the regrowth that follows them: what
        // the plan's `grown` records and `admit` checks against `lake_min`. In signature mode it is at least `lake_min`
        // by the test above. It is not the size the lake ends at: a regrowth that comes out smaller is not refused here
        // (`lake-at-mouth` and the oracle's counts read the finished map).
        let grown = lake.area;
        let set = &lake.set;
        let mut shore = vec![];
        {
            let b = c.base();
            for y in lake.y0..=lake.y1 {
                for x in lake.x0..=lake.x1 {
                    let i = y * w + x;
                    if set[i] != 1 || b.edge_dist[i] < d.head_edge {
                        continue;
                    }
                    let free = |j| set[j] == 0 && b.mask[j] == 1 && b.tiles[j] == TILE_FLOOR;
                    if (x > 0 && free(i - 1))
                        || (x < w - 1 && free(i + 1))
                        || (y > 0 && free(i - w))
                        || (y < h - 1 && free(i + w))
                    {
                        shore.push(i);
                    }
                }
            }
        }
        let (lcx, lcy) = (
            lake.sum_x as f64 / lake.area as f64,
            lake.sum_y as f64 / lake.area as f64,
        );
        for hd in 0..d.headlands {
            let b = c.base();
            let mut candidates = vec![];
            for &i in &shore {
                if b.tiles[i] != TILE_FLOOR {
                    continue;
                }
                let (x, y) = ((i % w) as i32, (i / w) as i32);
                let mut dist = f64::INFINITY;
                for &j in &chosen {
                    let (dx, dy) = (x - j % w as i32, y - j / w as i32);
                    let e = f64::from(dx * dx + dy * dy);
                    if e < dist {
                        dist = e;
                    }
                }
                if dist >= f64::from(d.head_sep2) {
                    candidates.push((dist, i));
                }
            }
            candidates.sort_by(|a, b| b.0.total_cmp(&a.0).then(a.1.cmp(&b.1)));
            let mut failed = 0;
            for &(_, i) in candidates.iter().take(d.head_look) {
                if failed >= d.head_tries {
                    break;
                }
                let (x, y) = ((i % w) as i32, (i / w) as i32);
                let (ox, oy) = (x as f64 - lcx, y as f64 - lcy);
                let len = crate::math::sqrt(ox * ox + oy * oy);
                let len = if len == 0.0 { 1.0 } else { len };
                let centre = (y + js_round((oy / len) * d.head_outset) as i32) * w as i32
                    + x
                    + js_round((ox / len) * d.head_outset) as i32;
                if !core_free(c, &book.band, centre) {
                    continue;
                }
                let cells = shelf_tiles(
                    w,
                    h,
                    &mut c.streams.rng,
                    centre,
                    if hd == 0 {
                        d.head_radius_first
                    } else {
                        d.head_radius_later
                    },
                    d,
                );
                if cells.is_empty()
                    || !cells
                        .iter()
                        .all(|&j| c.open(j) && book.band[j as usize] == 0 && !kept(j))
                {
                    continue;
                }
                let dir = dir_index16(
                    x as f64 * lake.area as f64 - lake.sum_x as f64,
                    y as f64 * lake.area as f64 - lake.sum_y as f64,
                ) as u8;
                let placed = c.place_feature(&feature(
                    FeatureKind::Headland,
                    FeatureJob::Lee,
                    [centre % w as i32, centre / w as i32],
                    vec![i32::from(dir)],
                    cells,
                    None,
                ))?;
                let Some(placed) = placed else {
                    failed += 1;
                    continue;
                };
                plan.shelves.push(ShelfPlan {
                    lake: pk,
                    centre,
                    dir,
                    spire: false,
                    walls: placed.written,
                });
                chosen.push(i as i32);
                break;
            }
        }
        let mut spots = vec![];
        {
            let b = c.base();
            for y in lake.y0..=lake.y1 {
                for x in lake.x0..=lake.x1 {
                    let i = y * w + x;
                    if set[i] == 1
                        && b.tiles[i] == TILE_FLOOR
                        && clr[i] >= d.spire_clearance
                        && book.band[i] == 0
                        && !kept(i as i32)
                    {
                        spots.push(i);
                    }
                }
            }
        }
        spots.sort_by(|&a, &b| clr[b].cmp(&clr[a]).then(a.cmp(&b)));
        let mut tried: Vec<usize> = vec![];
        for at in spots {
            if tried.len() >= d.spire_tries {
                break;
            }
            if tried.iter().any(|&t| {
                ((t % w) as i32 - (at % w) as i32).abs() < d.spire_separation
                    && ((t / w) as i32 - (at / w) as i32).abs() < d.spire_separation
            }) {
                continue;
            }
            tried.push(at);
            let mut cells = vec![];
            for rad in d.spire_radii {
                cells = shelf_tiles(w, h, &mut c.streams.rng, at as i32, rad, d);
                if cells.len() >= d.spire_min_cells {
                    break;
                }
            }
            if cells.len() < d.spire_min_cells
                || !cells
                    .iter()
                    .all(|&j| c.open(j) && book.band[j as usize] == 0 && !kept(j))
            {
                continue;
            }
            if let Some(placed) = c.place_feature(&feature(
                FeatureKind::Spire,
                FeatureJob::Landmark,
                [(at % w) as i32, (at / w) as i32],
                vec![],
                cells,
                None,
            ))? {
                plan.shelves.push(ShelfPlan {
                    lake: pk,
                    centre: at as i32,
                    dir: 0,
                    spire: true,
                    walls: placed.written,
                });
                break;
            }
        }
        // P-LAND-FROST-1: in signature mode the lake is brought back to its size after its shelves took tiles, on its
        // own ground (floor, unowned, off the bands, `spacing` from the other lake), and kept to one body.
        let lake = match sig.as_ref() {
            Some(s) => {
                let b = c.base();
                let mut reg = s.region[k].clone();
                for (i, g) in reg.iter_mut().enumerate() {
                    if !mouth_lake::iceable(w, h, b.tiles, i)
                        || b.is_owned(i as i32)
                        || book.band[i] == 1
                    {
                        *g = 0;
                    }
                }
                if lakes.len() == 2 {
                    reg =
                        lakes::off_limits(w, h, &reg, &lakes[1 - k], targets::lake_rule().spacing);
                }
                let mut set: Vec<u8> = (0..lake.set.len())
                    .map(|i| u8::from(lake.set[i] == 1 && reg[i] == 1))
                    .collect();
                mouth_lake::keep_component(w, h, &mut set, Some(lake.seed));
                mouth_lake::best_first(w, h, &reg, &s.val[k], &[lake.seed], s.target[k], &mut set);
                lakes[k] = lakes::Lake::from_set(w, h, lake.seed, set);
                lakes[k].clone()
            }
            None => lake,
        };
        let set = &lake.set;
        let (mut ice, mut plain) = (vec![], vec![]);
        {
            let b = c.base();
            for y in lake.y0..=lake.y1 {
                for x in lake.x0..=lake.x1 {
                    let i = y * w + x;
                    if set[i] != 1 || b.tiles[i] != TILE_FLOOR {
                        continue;
                    }
                    if book.band[i] == 1 {
                        plain.push(i as i32);
                    } else {
                        ice.push(i as i32);
                    }
                }
            }
        }
        let anchor = [(lake.seed % w) as i32, (lake.seed / w) as i32];
        let mut all = ice.clone();
        if plain.len() >= d.causeway_min {
            if c.place_feature(&feature(
                FeatureKind::Causeway,
                FeatureJob::Crossing,
                anchor,
                vec![],
                vec![],
                Some(plain.clone()),
            ))?
            .is_some()
            {
                plan.causeway.extend(plain);
            }
        } else if !plain.is_empty() {
            all.extend(plain);
            all.sort_unstable();
        }
        c.place_feature(&feature(
            FeatureKind::Mere,
            FeatureJob::Crossing,
            anchor,
            vec![pk as i32, 0],
            vec![],
            Some(all),
        ))?;
        plan.lakes.push(LakePlan {
            index: pk,
            seed: lake.seed as i32,
            clr: clr[lake.seed],
            tiles: ice,
            grown,
            ice: 0,
        });
    }
    Ok(plan)
}
