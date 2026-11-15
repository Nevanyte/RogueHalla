//! P-LAND-FROST-1 (owner direction, `docs/coordination/LAND_GENERATION_REQUIREMENTS.md` section 3; owner decision
//! 2026-11-14: targets, never a refusal): with the signature option on, the Frost styler builds its first lake in front
//! of the cave's mouth from the landmark's frame, then one smaller second lake elsewhere, then today's remaining Frost
//! work. It builds for `targets::lake_rule` (the rule's numbers); `build_rule` holds the build's own figures.
//!
//! Functions returning literals, no `const` item: `tests/content.rs` reads every upper-case `const` or `static` as a
//! tunable. Read only in signature mode (the option off never builds a mouth lake), so nothing here is folded into the
//! content hash.
//!
//! The build (`build`), in order, all on the Frost styler's own noise (no RNG draw; the shelves after it draw as today):
//! 1. The line from the mouth point M toward the coliseum's centre tile C; the near shore tile `p0`, the allowed tile at
//!    Chebyshev `mouth_gap + 1` from the mouth that is exactly that many floor steps from it, nearest the line; when no
//!    such tile is free, the free tile nearest the mouth (then nearest the line, then row order).
//! 2. The mouth lake, grown best-first from `p0` to `lake_target` tiles on the allowed ground (off the route bands,
//!    farther than `mouth_gap` from the mouth, off the ring road) under a value that falls off an ellipse centred on the
//!    line (`BuildRule`) plus today's lake noise: one 4-connected body by construction.
//! 3. The dry way: a walk of `way_width` squares of dry floor from beyond the lake's far side to the mouth. When the lake
//!    left none, the cheapest walk round it is planned (a lake tile in a square costs `way_lake_cost`), its squares are
//!    taken out of the lake and the lake is regrown to size elsewhere, never beyond its far side. The walk's tiles stay
//!    dry and free of walls (`Sig::keep`).
//! 4. The second lake, best-first from today's second seed (`rime::second_seed`) on the ground left (`spacing` dry tiles
//!    from the mouth lake), to `second_target` tiles or three quarters of the mouth lake, whichever is fewer.
//! 5. The spawn routes (`RouteBook::ensure_wet`) with both lakes as walls, so they go round them; a band that still
//!    clips a lake is taken out of it and the lake regrown elsewhere.
use super::lakes::Lake;
use super::routes::RouteBook;
use super::targets::{lake_rule, Line};
use super::*;
use crate::mapgen::coliseum::{CX, CY};
use crate::mapgen::landmarks::signature::{band, Band};
use std::cmp::Reverse;
use std::collections::{BinaryHeap, VecDeque};

/// How the styler builds to `lake_rule` (the implementer's figures, not rules: the guarantees read `lake_rule` alone).
#[derive(Clone, Copy, Debug, PartialEq)]
pub struct BuildRule {
    /// The mouth lake's tile count as built (the middle of `lake_rule().size`).
    pub lake_target: usize,
    /// The second lake's tile count as built (inside `lake_rule().second`; the ice share's figure, brief section 4).
    pub second_target: usize,
    /// The second lake as built holds at most this percentage of the mouth lake's tiles (so it stays the smaller).
    pub second_most_percent: usize,
    /// The ellipse the mouth lake's value falls off: half-length along the line over half-width across it.
    pub aspect: f64,
    /// The weight of today's lake noise (`fbm_q`, salt `salt_one`) against the ellipse (1 is the ellipse's rim).
    pub noise: f64,
    /// A dry way's square costs this much more per mouth-lake tile it would take when the way is planned.
    pub way_lake_cost: u32,
    /// Rounds of routes and regrowth (step 5).
    pub route_rounds: usize,
}

/// The build's figures (see `BuildRule`).
pub fn build_rule() -> BuildRule {
    BuildRule {
        lake_target: 400,
        second_target: 100,
        second_most_percent: 75,
        aspect: 1.25,
        noise: 0.45,
        way_lake_cost: 8,
        route_rounds: 3,
    }
}
/// Chebyshev distance from (x, y) to the nearest of `mouth` (`i32::MAX` when empty).
pub(super) fn cheb_mouth(mouth: &[(i32, i32)], x: i32, y: i32) -> i32 {
    mouth
        .iter()
        .map(|m| (m.0 - x).abs().max((m.1 - y).abs()))
        .min()
        .unwrap_or(i32::MAX)
}

/// Grows `set` best-first (the highest `val` on its 4-connected frontier first, the lower index on a tie) over
/// `region` until it holds `target` tiles or the frontier is empty; seeds `seeds` when `set` is empty. Returns the
/// area. Deterministic: integer keys, index tie-break.
pub(super) fn best_first(
    w: usize,
    h: usize,
    region: &[u8],
    val: &[i64],
    seeds: &[usize],
    target: usize,
    set: &mut [u8],
) -> usize {
    let mut area = set.iter().filter(|&&v| v == 1).count();
    let mut seen = set.to_vec();
    let mut heap = BinaryHeap::new();
    let push = |i: usize, seen: &mut Vec<u8>, heap: &mut BinaryHeap<(i64, Reverse<usize>)>| {
        let (x, y) = (i % w, i / w);
        for (ok, j) in [
            (x > 0, i.wrapping_sub(1)),
            (x + 1 < w, i + 1),
            (y > 0, i.wrapping_sub(w)),
            (y + 1 < h, i + w),
        ] {
            if ok && seen[j] == 0 && region[j] == 1 {
                seen[j] = 1;
                heap.push((val[j], Reverse(j)));
            }
        }
    };
    if area == 0 {
        for &s in seeds {
            if region[s] == 1 && seen[s] == 0 {
                seen[s] = 1;
                heap.push((val[s], Reverse(s)));
            }
        }
    } else {
        for (i, &v) in set.iter().enumerate() {
            if v == 1 {
                push(i, &mut seen, &mut heap);
            }
        }
    }
    while area < target {
        let Some((_, Reverse(i))) = heap.pop() else {
            break;
        };
        set[i] = 1;
        area += 1;
        push(i, &mut seen, &mut heap);
    }
    area
}

/// The 4-connected component of `set` holding `keep` (or, when `keep` is not in it, the largest, first in row order on
/// a tie); every other tile of `set` is cleared.
pub(super) fn keep_component(w: usize, h: usize, set: &mut [u8], keep: Option<usize>) {
    let mut label = vec![0u32; set.len()];
    let (mut best, mut best_size, mut next) = (0u32, 0usize, 0u32);
    let mut stack = Vec::new();
    for s in 0..set.len() {
        if set[s] != 1 || label[s] != 0 {
            continue;
        }
        next += 1;
        label[s] = next;
        stack.push(s);
        let mut size = 0;
        while let Some(i) = stack.pop() {
            size += 1;
            let (x, y) = (i % w, i / w);
            for (ok, j) in [
                (x > 0, i.wrapping_sub(1)),
                (x + 1 < w, i + 1),
                (y > 0, i.wrapping_sub(w)),
                (y + 1 < h, i + w),
            ] {
                if ok && set[j] == 1 && label[j] == 0 {
                    label[j] = next;
                    stack.push(j);
                }
            }
        }
        if size > best_size {
            (best, best_size) = (next, size);
        }
    }
    if let Some(k) = keep.filter(|&k| set[k] == 1) {
        best = label[k];
    }
    for (v, &l) in set.iter_mut().zip(&label) {
        if *v == 1 && l != best {
            *v = 0;
        }
    }
}

/// What the signature build hands the rest of the shape stage.
pub(super) struct Sig {
    /// Tiles no Frost wall may take: within `mouth_gap + 1` of the mouth (the gap and the near shore) and the dry way.
    pub keep: Vec<u8>,
    /// Each lake's ground and value for regrowth after the shelves, and its target.
    pub region: Vec<Vec<u8>>,
    pub val: Vec<Vec<i64>>,
    pub target: Vec<usize>,
}

/// Square `i` (centre) of half-width `r`: every tile in bounds.
fn square(w: usize, h: usize, i: usize, r: i32) -> Option<impl Iterator<Item = usize>> {
    let (x, y) = ((i % w) as i32, (i / w) as i32);
    (x - r >= 0 && y - r >= 0 && x + r < w as i32 && y + r < h as i32).then(move || {
        (-r..=r)
            .flat_map(move |dy| (-r..=r).map(move |dx| ((y + dy) as usize) * w + (x + dx) as usize))
    })
}

/// The dry way: from a square whose centre lies at least 2 beyond `far` along the line to a square that holds a mouth
/// tile or a tile 4-adjacent to one, through squares (half-width `r`) of `dry` tiles; a square costs 1 plus `cost(tile)`
/// summed over its tiles. Returns the walk's square centres (cheapest, then fewest steps), or `None`.
#[allow(clippy::too_many_arguments)]
fn way_walk(
    w: usize,
    h: usize,
    r: i32,
    dry: &[u8],
    line: &Line,
    far: f64,
    mouth: &[(i32, i32)],
    cost: impl Fn(usize) -> u32,
) -> Option<Vec<usize>> {
    let n = w * h;
    let near_mouth = |t: usize| {
        let (x, y) = ((t % w) as i32, (t / w) as i32);
        mouth.iter().any(|m| (m.0 - x).abs() + (m.1 - y).abs() <= 1)
    };
    let mut ok = vec![u32::MAX; n];
    let mut end = vec![false; n];
    for (i, v) in ok.iter_mut().enumerate() {
        let Some(sq) = square(w, h, i, r) else {
            continue;
        };
        let mut c = 1u32;
        let mut touch = false;
        let mut good = true;
        for t in sq {
            if dry[t] != 1 {
                good = false;
                break;
            }
            c = c.saturating_add(cost(t));
            touch |= near_mouth(t);
        }
        if good {
            *v = c;
            end[i] = touch;
        }
    }
    let mut dist = vec![u64::MAX; n];
    let mut prev = vec![usize::MAX; n];
    let mut heap = BinaryHeap::new();
    for i in 0..n {
        if ok[i] != u32::MAX && line.along((i % w) as i32, (i / w) as i32) >= far + 2.0 {
            dist[i] = u64::from(ok[i]);
            heap.push((Reverse(dist[i]), Reverse(i)));
        }
    }
    while let Some((Reverse(d), Reverse(i))) = heap.pop() {
        if d > dist[i] {
            continue;
        }
        if end[i] {
            let mut walk = vec![i];
            let mut k = i;
            while prev[k] != usize::MAX {
                k = prev[k];
                walk.push(k);
            }
            return Some(walk);
        }
        let (x, y) = (i % w, i / w);
        for (good, j) in [
            (x > 0, i.wrapping_sub(1)),
            (x + 1 < w, i + 1),
            (y > 0, i.wrapping_sub(w)),
            (y + 1 < h, i + w),
        ] {
            if good && ok[j] != u32::MAX {
                let e = d + u64::from(ok[j]);
                if e < dist[j] {
                    dist[j] = e;
                    prev[j] = i;
                    heap.push((Reverse(e), Reverse(j)));
                }
            }
        }
    }
    None
}

/// The largest along-line value of a set's tiles.
fn far_side(w: usize, set: &[u8], line: &Line) -> f64 {
    set.iter()
        .enumerate()
        .filter(|(_, &v)| v == 1)
        .map(|(i, _)| line.along((i % w) as i32, (i / w) as i32))
        .fold(f64::MIN, f64::max)
}

/// Signature mode's lakes (module comment), or `None` when the frame gives no line or the zone has no ground a lake may
/// take: the caller then falls back to today's lake growth (`shape::run`).
#[allow(clippy::too_many_arguments)]
pub(super) fn build(
    cb: &Callbacks<'_>,
    b: &BaseContext<'_>,
    noise: &crate::mapgen::style_features::Noise,
    clr: &[i32],
    allow: &[u8],
    book: &mut RouteBook,
    anchor: &ZoneAnchor,
) -> Option<(Vec<Lake>, Sig)> {
    let d = &cb.definition;
    let (rule, br) = (lake_rule(), build_rule());
    let (w, h) = (b.w, b.h);
    let n = w * h;
    let mouth: Vec<(i32, i32)> = anchor.entrance.iter().map(|p| (p.x, p.y)).collect();
    let line = Line::new(&mouth, (CX, CY))?;
    let ring = (cb.fixed.dist_fp.len() == n).then_some(&cb.fixed.dist_fp);
    let near = rule.mouth_gap + 1;
    let xy = |i: usize| ((i % w) as i32, (i / w) as i32);
    let iceable = |i: usize| iceable(w, h, b.tiles, i);
    // Ground any lake may take: allowed, outside the core band (`allow` has it out already), off the route bands, off
    // the ring road's clearing (the surface would leave it plain), farther than the gap from the mouth.
    let mut ground = vec![0u8; n];
    for (i, g) in ground.iter_mut().enumerate() {
        let (x, y) = xy(i);
        if allow[i] == 1
            && book.band[i] == 0
            && iceable(i)
            && b.no_block[i] == 0
            && b.terrain[i] == 0
            && !ring.is_some_and(|r| i32::from(r[i]) <= cb.ring_clear)
            && cheb_mouth(&mouth, x, y) >= near
            && band(anchor, x, y) != Band::Core
        {
            *g = 1;
        }
    }
    // Walkable floor from the mouth, for the near shore's floor steps.
    let mut steps = vec![i32::MAX; n];
    let mut queue = VecDeque::new();
    for &(x, y) in &mouth {
        let i = y as usize * w + x as usize;
        if b.tiles[i] == TILE_FLOOR {
            steps[i] = 0;
            queue.push_back(i);
        }
    }
    while let Some(i) = queue.pop_front() {
        if steps[i] >= near {
            continue;
        }
        let (x, y) = (i % w, i / w);
        for (ok, j) in [
            (x > 0, i.wrapping_sub(1)),
            (x + 1 < w, i + 1),
            (y > 0, i.wrapping_sub(w)),
            (y + 1 < h, i + w),
        ] {
            if ok && steps[j] == i32::MAX && b.tiles[j] == TILE_FLOOR {
                steps[j] = steps[i] + 1;
                queue.push_back(j);
            }
        }
    }
    let p0 = (0..n)
        .filter(|&i| {
            let (x, y) = xy(i);
            ground[i] == 1
                && steps[i] == near
                && cheb_mouth(&mouth, x, y) == near
                && line.along(x, y) > 0.0
        })
        .min_by(|&p, &q| {
            let (px, py) = xy(p);
            let (qx, qy) = xy(q);
            line.side(px, py)
                .abs()
                .total_cmp(&line.side(qx, qy).abs())
                .then(p.cmp(&q))
        });
    // A blocked near shore (a landmark's ring, a camp's clearing, a route band): the free tile nearest the mouth. The
    // gap target then fails on this map; it is a target, not a refusal.
    let p0 = match p0 {
        Some(p) => p,
        None => (0..n).filter(|&i| ground[i] == 1).min_by(|&p, &q| {
            let (px, py) = xy(p);
            let (qx, qy) = xy(q);
            cheb_mouth(&mouth, px, py)
                .cmp(&cheb_mouth(&mouth, qx, qy))
                .then(line.side(px, py).abs().total_cmp(&line.side(qx, qy).abs()))
                .then(p.cmp(&q))
        })?,
    };
    // The mouth lake's value: an ellipse on the line, its near tip at the near shore, plus today's lake noise.
    let half_w = crate::math::sqrt(br.lake_target as f64 / (core::f64::consts::PI * br.aspect));
    let half_l = half_w * br.aspect;
    let centre = f64::from(near) + half_l;
    let bb = b.zone.bbox;
    let mut val = vec![i64::MIN; n];
    for y in bb.y..bb.y + bb.h as i32 {
        for x in bb.x..bb.x + bb.w as i32 {
            let i = y as usize * w + x as usize;
            let (s, o) = (
                (line.along(x, y) - centre) / half_l,
                line.side(x, y) / half_w,
            );
            let q = crate::mapgen::style_features::fbm_q(
                noise,
                f64::from(x),
                f64::from(y),
                d.noise_cell,
                d.noise_octaves,
                d.salt_one,
            );
            let v = -(s * s + o * o) + br.noise * (q - 512.0) / 512.0;
            val[i] = (v * 65536.0) as i64;
        }
    }
    // Step 2: the mouth lake.
    let mut one = vec![0u8; n];
    best_first(w, h, &ground, &val, &[p0], br.lake_target, &mut one);
    // Step 3: the dry way. `keep` (no Frost wall): within `near + 1` of the mouth, so the near shore's tiles keep
    // all eight floor neighbours, and the way; `way` (no lake): the way's squares.
    let mut keep = vec![0u8; n];
    for (i, k) in keep.iter_mut().enumerate() {
        let (x, y) = xy(i);
        if cheb_mouth(&mouth, x, y) <= near + 1 {
            *k = 1;
        }
    }
    let mut way = vec![0u8; n];
    let r = (rule.way_width - 1) / 2;
    let dry = |set: &[u8]| -> Vec<u8> {
        (0..n)
            .map(|i| {
                u8::from(
                    b.mask[i] == 1
                        && b.tiles[i] == TILE_FLOOR
                        && b.terrain[i] != cb.ice
                        && set[i] == 0,
                )
            })
            .collect()
    };
    let mut far = far_side(w, &one, &line);
    let mut walk = way_walk(w, h, r, &dry(&one), &line, far, &mouth, |_| 0);
    if walk.is_none() {
        let p0_square = |t: usize| t == p0;
        let all = dry(&vec![0u8; n]);
        let planned = way_walk(w, h, r, &all, &line, far, &mouth, |t| {
            if p0_square(t) {
                1 << 20
            } else if one[t] == 1 {
                br.way_lake_cost
            } else {
                0
            }
        });
        if let Some(planned) = planned {
            let mut taken = vec![0u8; n];
            for &c in &planned {
                for t in square(w, h, c, r).into_iter().flatten() {
                    taken[t] = 1;
                }
            }
            let cap = far;
            for i in 0..n {
                let (x, y) = xy(i);
                if taken[i] == 1 || line.along(x, y) > cap {
                    ground[i] = 0;
                }
                if taken[i] == 1 {
                    one[i] = 0;
                }
            }
            keep_component(w, h, &mut one, Some(p0));
            best_first(w, h, &ground, &val, &[p0], br.lake_target, &mut one);
            far = far_side(w, &one, &line);
            walk = way_walk(w, h, r, &dry(&one), &line, far, &mouth, |_| 0);
        }
    }
    // The way found holds: its squares stay dry and wall-free, and no lake grows beyond the far side it started from.
    if let Some(walk) = &walk {
        for &c in walk {
            for t in square(w, h, c, r).into_iter().flatten() {
                keep[t] = 1;
                way[t] = 1;
            }
        }
    }
    for i in 0..n {
        let (x, y) = xy(i);
        if way[i] == 1 || line.along(x, y) > far {
            ground[i] = 0;
        }
    }
    let lake_one = Lake::from_set(w, h, p0, one.clone());
    // Step 4: the second lake, `spacing` dry tiles from the mouth lake.
    let all_ground = second_ground(allow, &way, book, b, ring, cb, &mouth, near, anchor);
    let open = lakes::off_limits(w, h, &all_ground, &lake_one, rule.spacing);
    let first = centroid_tile(w, &lake_one);
    let second = rime::second_seed(w, clr, &open, first, &cb.shared)
        .or_else(|| crate::mapgen::style_features::argmax_in(clr, &open, 0, clr.len() as isize));
    let val_two = lakes::value_field(b, noise, clr, &open, d.salt_two, d)
        .into_iter()
        .map(i64::from)
        .collect::<Vec<_>>();
    let mut out = vec![lake_one];
    let mut region = vec![ground.clone()];
    let mut vals = vec![val.clone()];
    let mut target = vec![br.lake_target];
    if let Some(s) = second {
        let mut two = vec![0u8; n];
        // The second lake stays smaller than a small mouth lake.
        let second_target = br
            .second_target
            .min(out[0].area * br.second_most_percent / 100);
        best_first(w, h, &open, &val_two, &[s], second_target, &mut two);
        out.push(Lake::from_set(w, h, s, two));
        region.push(open);
        vals.push(val_two);
        target.push(second_target);
    }
    // Step 5: the routes go round the lakes; a band that still clips one is taken out and the lake regrown.
    for _ in 0..br.route_rounds {
        let mut wet = vec![0u8; n];
        for l in &out {
            for (i, &v) in l.set.iter().enumerate() {
                if v == 1 {
                    let (x, y) = xy(i);
                    for (dx, dy) in [(0, 0), (1, 0), (-1, 0), (0, 1), (0, -1)] {
                        let (nx, ny) = (x + dx, y + dy);
                        if nx >= 0 && ny >= 0 && nx < w as i32 && ny < h as i32 {
                            wet[ny as usize * w + nx as usize] = 1;
                        }
                    }
                }
            }
        }
        book.ensure_wet(b, &out, d, cb.fixed, &wet);
        let mut moved = false;
        for k in 0..out.len() {
            if !out[k]
                .set
                .iter()
                .zip(&book.band)
                .any(|(&v, &r)| v == 1 && r == 1)
            {
                continue;
            }
            moved = true;
            let mut set = out[k].set.clone();
            for (v, &r) in set.iter_mut().zip(&book.band) {
                if r == 1 {
                    *v = 0;
                }
            }
            let mut reg = region[k].clone();
            for (g, &r) in reg.iter_mut().zip(&book.band) {
                if r == 1 {
                    *g = 0;
                }
            }
            if let Some(other) = out.get(1 - k.min(1)).filter(|_| out.len() == 2) {
                reg = lakes::off_limits(w, h, &reg, other, rule.spacing);
            }
            let seed = out[k].seed;
            keep_component(w, h, &mut set, Some(seed));
            best_first(w, h, &reg, &vals[k], &[seed], target[k], &mut set);
            out[k] = Lake::from_set(w, h, seed, set);
        }
        if !moved {
            break;
        }
    }
    for reg in &mut region {
        for (g, &r) in reg.iter_mut().zip(&book.band) {
            if r == 1 {
                *g = 0;
            }
        }
    }
    Some((
        out,
        Sig {
            keep,
            region,
            val: vals,
            target,
        },
    ))
}

/// Tile `i` can carry ice: it and its eight neighbours are floor (`style_runtime`'s ice rule).
pub(super) fn iceable(w: usize, h: usize, tiles: &[u8], i: usize) -> bool {
    let (x, y) = ((i % w) as i32, (i / w) as i32);
    (-1..=1).all(|dy| {
        (-1..=1).all(|dx| {
            let (nx, ny) = (x + dx, y + dy);
            nx >= 0
                && ny >= 0
                && nx < w as i32
                && ny < h as i32
                && tiles[ny as usize * w + nx as usize] == TILE_FLOOR
        })
    })
}
/// The ground the second lake may take: the mouth lake's ground without its cap along the line.
#[allow(clippy::too_many_arguments)]
fn second_ground(
    allow: &[u8],
    keep: &[u8],
    book: &RouteBook,
    b: &BaseContext<'_>,
    ring: Option<&Vec<u8>>,
    cb: &Callbacks<'_>,
    mouth: &[(i32, i32)],
    near: i32,
    anchor: &ZoneAnchor,
) -> Vec<u8> {
    let w = b.w;
    (0..allow.len())
        .map(|i| {
            let (x, y) = ((i % w) as i32, (i / w) as i32);
            u8::from(
                allow[i] == 1
                    && keep[i] == 0
                    && book.band[i] == 0
                    && iceable(b.w, b.h, b.tiles, i)
                    && b.no_block[i] == 0
                    && b.terrain[i] == 0
                    && !ring.is_some_and(|r| i32::from(r[i]) <= cb.ring_clear)
                    && cheb_mouth(mouth, x, y) >= near
                    && band(anchor, x, y) != Band::Core,
            )
        })
        .collect()
}

/// The lake tile nearest its centroid (lowest index on a tie).
fn centroid_tile(w: usize, l: &Lake) -> usize {
    let (cx, cy) = (
        l.sum_x as f64 / l.area.max(1) as f64,
        l.sum_y as f64 / l.area.max(1) as f64,
    );
    (0..l.set.len())
        .filter(|&i| l.set[i] == 1)
        .min_by(|&p, &q| {
            let e = |i: usize| {
                let (dx, dy) = ((i % w) as f64 - cx, (i / w) as f64 - cy);
                dx * dx + dy * dy
            };
            e(p).total_cmp(&e(q)).then(p.cmp(&q))
        })
        .unwrap_or(l.seed)
}
