//! P-LAND-FROST-1 (owner decision 2026-11-14, "Yes to both"): the Frost lake rule's parts as **targets**. In signature
//! mode the Frost styler builds for them on every map (`mouth_lake::build`); they are measured on the finished map and
//! reported, and never refuse an attempt (they are not registered guarantees: Frost's list stays `lake-at-mouth`).
//! Definitions in `tests/mapgen_land_frost.rs` (its head comment); the numbers in `lake_rule`.
//!
//! Functions returning literals, no `const` item: `tests/content.rs` reads every upper-case `const` or `static` as a
//! tunable. Read only in signature mode, so nothing here is folded into the content hash. Every check is a pure read of
//! the finished map and the Frost frame: no draw.
use crate::constants::TILE;
use crate::map::TILE_FLOOR;
use crate::mapdata::{FeatureKind, GameMapData, Zone};
use crate::mapgen::coliseum::{H, W};
use crate::mapgen::landmarks::signature::frame::LandmarkFrame;
use crate::mapgen::landmarks::signature::{at, catalog_codes};
use std::collections::VecDeque;

/// The Frost lake rule's numbers (the owner's: size, gap, way width; the lane's, reported to the owner with the
/// measurement: the axis offset, the second lake's window, the spacing).
#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub struct LakeRule {
    /// Owner: about 400 tiles, give or take 15%: the mouth lake's ice tiles, inclusive bounds.
    pub size: (usize, usize),
    /// Owner: dry tiles between the mouth and the lake's nearest shore (exactly).
    pub mouth_gap: i32,
    /// Owner: the dry way round the lake to the mouth is at least this many tiles wide.
    pub way_width: i32,
    /// The lane's: the most the mouth lake's centroid may lie off the line from the mouth to the coliseum, in tiles.
    pub axis_offset: i32,
    /// The lane's: the second lake's ice tiles, inclusive bounds (and fewer than the mouth lake's).
    pub second: (usize, usize),
    /// The lane's: the fewest dry tiles between the second lake and the mouth lake; also the gap below which two pieces
    /// of ice count as one lake.
    pub spacing: i32,
}

/// The Frost lake rule's numbers (see `LakeRule`).
pub fn lake_rule() -> LakeRule {
    LakeRule {
        size: (340, 460),
        mouth_gap: 3,
        way_width: 3,
        axis_offset: 4,
        second: (60, 200),
        spacing: 3,
    }
}

/// The line from the mouth point M (the mean of the mouth tiles' points) toward the coliseum's centre tile's point C
/// (tile points at x + 0.5). Shared by the build and the checks.
#[derive(Clone, Copy, Debug)]
pub(super) struct Line {
    pub origin: (f64, f64),
    pub unit: (f64, f64),
    pub reach: f64,
}

impl Line {
    /// `None` without a mouth or when the mouth point is the coliseum's.
    pub fn new(mouth: &[(i32, i32)], col: (i32, i32)) -> Option<Self> {
        if mouth.is_empty() {
            return None;
        }
        let n = mouth.len() as f64;
        let origin = (
            mouth.iter().map(|c| f64::from(c.0) + 0.5).sum::<f64>() / n,
            mouth.iter().map(|c| f64::from(c.1) + 0.5).sum::<f64>() / n,
        );
        let (dx, dy) = (
            f64::from(col.0) + 0.5 - origin.0,
            f64::from(col.1) + 0.5 - origin.1,
        );
        let reach = crate::math::sqrt(dx * dx + dy * dy);
        (reach > 0.0).then(|| Self {
            origin,
            unit: (dx / reach, dy / reach),
            reach,
        })
    }
    /// Along the line (s) of a point.
    pub fn along_pt(&self, p: (f64, f64)) -> f64 {
        (p.0 - self.origin.0) * self.unit.0 + (p.1 - self.origin.1) * self.unit.1
    }
    /// Off the line (o, signed: positive to the left of M to C in map coordinates) of a point.
    pub fn side_pt(&self, p: (f64, f64)) -> f64 {
        (p.0 - self.origin.0) * self.unit.1 - (p.1 - self.origin.1) * self.unit.0
    }
    /// Along the line of a tile's point.
    pub fn along(&self, x: i32, y: i32) -> f64 {
        self.along_pt((f64::from(x) + 0.5, f64::from(y) + 0.5))
    }
    /// Off the line (signed) of a tile's point.
    pub fn side(&self, x: i32, y: i32) -> f64 {
        self.side_pt((f64::from(x) + 0.5, f64::from(y) + 0.5))
    }
}

/// A finished map's lakes as `tests/mapgen_land_frost.rs` defines them: unowned Frost ice (floor of the Frost region
/// carrying the ice terrain that the cave does not own); the mouth lake, its largest 4-connected body (first in row
/// order on a size tie); the other lakes, the rest grouped so that pieces at most `spacing` apart (Chebyshev) are one.
struct Lakes {
    mouth: Vec<(i32, i32)>,
    line: Line,
    lake: Vec<usize>,
    others: Vec<Vec<usize>>,
    ice: Vec<usize>,
}

fn xy(i: usize) -> (i32, i32) {
    ((i % W) as i32, (i / W) as i32)
}

fn pt(i: usize) -> (f64, f64) {
    let (x, y) = xy(i);
    (f64::from(x) + 0.5, f64::from(y) + 0.5)
}

/// Chebyshev distance between two tile sets (`i32::MAX` when either is empty).
fn cheb_sets(a: &[usize], b: &[usize]) -> i32 {
    let mut best = i32::MAX;
    for &p in a {
        let (px, py) = xy(p);
        for &q in b {
            let (qx, qy) = xy(q);
            best = best.min((px - qx).abs().max((py - qy).abs()));
        }
    }
    best
}

/// Groups of `cells` (ascending tile indices) linked when at most `reach` apart (Chebyshev; 4-neighbours only when
/// `four`), in row order of their first tile.
fn groups(cells: &[usize], reach: i32, four: bool) -> Vec<Vec<usize>> {
    let n = W * H;
    let mut member = vec![false; n];
    for &i in cells {
        member[i] = true;
    }
    let mut seen = vec![false; n];
    let mut out = Vec::new();
    for &start in cells {
        if seen[start] {
            continue;
        }
        seen[start] = true;
        let mut group = vec![start];
        let mut stack = vec![start];
        while let Some(i) = stack.pop() {
            let (x, y) = xy(i);
            for dy in -reach..=reach {
                for dx in -reach..=reach {
                    if (dx, dy) == (0, 0) || (four && dx != 0 && dy != 0) {
                        continue;
                    }
                    if let Some(j) = at(x + dx, y + dy).filter(|&j| member[j] && !seen[j]) {
                        seen[j] = true;
                        group.push(j);
                        stack.push(j);
                    }
                }
            }
        }
        group.sort_unstable();
        out.push(group);
    }
    out
}

/// The map's lakes, or `None` when the frame gives no line (no mouth, or a mouth on the coliseum's centre).
fn lakes(map: &GameMapData, frame: &LandmarkFrame) -> Option<Lakes> {
    let ice_code = catalog_codes().ice;
    let mouth: Vec<(i32, i32)> = frame.anchor.entrance.iter().map(|p| (p.x, p.y)).collect();
    let col = (
        (map.coliseum.centre.x / TILE).floor() as i32,
        (map.coliseum.centre.y / TILE).floor() as i32,
    );
    let line = Line::new(&mouth, col)?;
    let cave = map
        .features
        .iter()
        .position(|f| f.kind == FeatureKind::Cave);
    let frost = Zone::Frost.code();
    let ice: Vec<usize> = (0..map.tiles.len().min(W * H))
        .filter(|&i| {
            map.region_grid[i] == frost
                && map.tiles[i] == TILE_FLOOR
                && map.terrain[i] == ice_code
                && cave.is_none_or(|k| usize::from(map.feature_grid[i]) != k + 1)
        })
        .collect();
    let bodies = groups(&ice, 1, true);
    let lake = bodies
        .iter()
        .enumerate()
        .max_by_key(|(k, b)| (b.len(), core::cmp::Reverse(*k)))
        .map(|(_, b)| b.clone())
        .unwrap_or_default();
    let mut in_lake = vec![false; W * H];
    for &i in &lake {
        in_lake[i] = true;
    }
    let rest: Vec<usize> = ice.iter().copied().filter(|&i| !in_lake[i]).collect();
    let others = groups(&rest, lake_rule().spacing, false);
    Some(Lakes {
        mouth,
        line,
        lake,
        others,
        ice,
    })
}

/// The mouth lake's size and the other lakes' sizes, for the report (`report.rs`).
#[cfg(test)]
pub(super) fn lake_sizes(map: &GameMapData, frame: &LandmarkFrame) -> Option<(usize, Vec<usize>)> {
    let l = lakes(map, frame)?;
    Some((l.lake.len(), l.others.iter().map(Vec::len).collect()))
}

/// Fewest 4-connected floor steps from any mouth tile (step 0) to a tile of `to`.
fn floor_steps(map: &GameMapData, from: &[(i32, i32)], to: &[usize]) -> Option<usize> {
    let mut target = vec![false; W * H];
    for &i in to {
        target[i] = true;
    }
    let mut dist = vec![usize::MAX; W * H];
    let mut queue = VecDeque::new();
    for &(x, y) in from {
        if let Some(i) = at(x, y).filter(|&i| map.tiles[i] == TILE_FLOOR && dist[i] != 0) {
            dist[i] = 0;
            queue.push_back(i);
        }
    }
    while let Some(i) = queue.pop_front() {
        if target[i] {
            return Some(dist[i]);
        }
        let (x, y) = xy(i);
        for (dx, dy) in [(1, 0), (-1, 0), (0, 1), (0, -1)] {
            if let Some(j) = at(x + dx, y + dy) {
                if dist[j] == usize::MAX && map.tiles[j] == TILE_FLOOR {
                    dist[j] = dist[i] + 1;
                    queue.push_back(j);
                }
            }
        }
    }
    None
}

/// The mouth-lake target on a finished map: the mouth lake holds `lake_rule().size` tiles; its centroid lies at most
/// `axis_offset` off the line, strictly between the mouth and the coliseum along it, and the line crosses the lake (a
/// lake tile ahead of the mouth whose point lies within 0.5 of the line); the lake is exactly `mouth_gap + 1` from the
/// mouth, Chebyshev and in floor steps, and no unowned Frost ice is nearer.
pub fn mouth_lake_holds(map: &GameMapData, frame: &LandmarkFrame) -> bool {
    let rule = lake_rule();
    let Some(l) = lakes(map, frame) else {
        return false;
    };
    if !(rule.size.0..=rule.size.1).contains(&l.lake.len()) {
        return false;
    }
    let n = l.lake.len() as f64;
    let centroid = (
        l.lake.iter().map(|&i| pt(i).0).sum::<f64>() / n,
        l.lake.iter().map(|&i| pt(i).1).sum::<f64>() / n,
    );
    let along = l.line.along_pt(centroid);
    let crossed = l
        .lake
        .iter()
        .any(|&i| l.line.along_pt(pt(i)) > 0.0 && l.line.side_pt(pt(i)).abs() <= 0.5);
    if l.line.side_pt(centroid).abs() > f64::from(rule.axis_offset)
        || !(along > 0.0 && along < l.line.reach)
        || !crossed
    {
        return false;
    }
    let want = rule.mouth_gap + 1;
    let mouth: Vec<usize> = l.mouth.iter().filter_map(|&(x, y)| at(x, y)).collect();
    cheb_sets(&mouth, &l.lake) == want
        && floor_steps(map, &l.mouth, &l.lake) == Some(want as usize)
        && cheb_sets(&mouth, &l.ice) >= want
}

/// The dry-way target on a finished map: a 4-connected walk of `way_width` squares of dry floor (floor that is not ice,
/// any zone) from a square whose centre lies at least 2 beyond the mouth lake's far side along the line to a square
/// holding a mouth tile or a tile 4-adjacent to one. Fails without a mouth lake.
pub fn dry_way_holds(map: &GameMapData, frame: &LandmarkFrame) -> bool {
    let Some(l) = lakes(map, frame) else {
        return false;
    };
    if l.lake.is_empty() {
        return false;
    }
    let ice_code = catalog_codes().ice;
    let r = (lake_rule().way_width - 1) / 2;
    let far = l
        .lake
        .iter()
        .map(|&i| l.line.along(xy(i).0, xy(i).1))
        .fold(f64::MIN, f64::max);
    let dry = |x: i32, y: i32| {
        at(x, y).is_some_and(|i| map.tiles[i] == TILE_FLOOR && map.terrain[i] != ice_code)
    };
    let square = |x: i32, y: i32| (-r..=r).all(|dy| (-r..=r).all(|dx| dry(x + dx, y + dy)));
    let ends = |x: i32, y: i32| {
        (-r..=r).any(|dy| {
            (-r..=r).any(|dx| {
                let (bx, by) = (x + dx, y + dy);
                l.mouth
                    .iter()
                    .any(|m| (m.0 - bx).abs() + (m.1 - by).abs() <= 1)
            })
        })
    };
    let mut seen = vec![false; W * H];
    let mut queue = VecDeque::new();
    for (i, s) in seen.iter_mut().enumerate() {
        let (x, y) = xy(i);
        if l.line.along(x, y) >= far + 2.0 && square(x, y) {
            *s = true;
            queue.push_back(i);
        }
    }
    while let Some(i) = queue.pop_front() {
        let (x, y) = xy(i);
        if ends(x, y) {
            return true;
        }
        for (dx, dy) in [(1, 0), (-1, 0), (0, 1), (0, -1)] {
            if let Some(j) = at(x + dx, y + dy) {
                if !seen[j] && square(x + dx, y + dy) {
                    seen[j] = true;
                    queue.push_back(j);
                }
            }
        }
    }
    false
}

/// The second-lake target on a finished map: besides the mouth lake exactly one lake (pieces at most `spacing` apart
/// are one), of `lake_rule().second` tiles and fewer than the mouth lake's, more than `spacing` (Chebyshev) from it.
pub fn second_lake_holds(map: &GameMapData, frame: &LandmarkFrame) -> bool {
    let rule = lake_rule();
    let Some(l) = lakes(map, frame) else {
        return false;
    };
    let [two] = l.others.as_slice() else {
        return false;
    };
    (rule.second.0..=rule.second.1).contains(&two.len())
        && two.len() < l.lake.len()
        && cheb_sets(two, &l.lake) > rule.spacing
}
