//! P-COLISEUM-GRAND-1 oracle (owner request 2026-11-19, with a drawing; 8 sides confirmed): the grand coliseum, an
//! octagon about twice the legacy size across, with four L-shaped walls whose corners point at the centre, leaving an
//! open cross through the middle and a corridor between the L arms' ends and the outer wall. No legacy counterpart: the
//! geometry is the owner's drawing as read by the lane (proposed DEVIATIONS row in the packet brief). The legacy layout
//! (`fixed_layout`, `DEFAULT_DEFINITION`) stays byte-identical, pinned by a digest.
use super::coliseum::{
    fixed_layout, grand_layout, FixedLayout, GateSide, TileRect, CX, CY, GRAND_DEFINITION, H, W,
};
use super::pipeline;
use crate::constants::{MASK_MOVE, PLAYER_RADIUS, TILE};
use crate::map::{TILE_FLOOR, TILE_WALL};

fn ix(x: i32, y: i32) -> usize {
    y as usize * W + x as usize
}

/// The four Ls in map tiles: the vertical arm through the corner, then the horizontal arm (corner counted once).
fn ls() -> Vec<Vec<(i32, i32)>> {
    let l =
        |cx: i32, cy: i32, vy: std::ops::RangeInclusive<i32>, hx: std::ops::RangeInclusive<i32>| {
            let mut t: Vec<(i32, i32)> = vy.map(|y| (cx, y)).collect();
            t.extend(hx.map(|x| (x, cy)).filter(|&p| p != (cx, cy)));
            t
        };
    vec![
        l(73, 73, 67..=73, 67..=73), // top-left: arms run up and left
        l(86, 73, 67..=73, 86..=92), // top-right: up and right
        l(86, 86, 86..=92, 86..=92), // bottom-right: down and right
        l(73, 86, 86..=92, 67..=73), // bottom-left: down and left
    ]
}

fn l_tiles() -> Vec<u8> {
    let mut m = vec![0u8; W * H];
    for l in ls() {
        for (x, y) in l {
            m[ix(x, y)] = 1;
        }
    }
    m
}

/// The walls the attempt builds from the layout (`attempt/mod.rs` `assemble_with`): off-footprint, the wall ring and
/// the drawn pillars of `variant` are wall; the gate tiles are opened.
fn layout_walls(f: &FixedLayout, variant: usize) -> Vec<u8> {
    let mut walls = vec![0u8; W * H];
    for (i, wall) in walls.iter_mut().enumerate() {
        if f.wall_ring[i] != 0 || f.pillars[variant][i] != 0 || f.fp[i] == 0 {
            *wall = 1;
        }
    }
    for g in &f.gates {
        for t in &g.tiles {
            walls[ix(t.x, t.y)] = 0;
        }
    }
    walls
}

/// 4-connected flood over open tiles of the footprint from `from`.
fn reach(walls: &[u8], fp: &[u8], from: (i32, i32)) -> Vec<u8> {
    let mut seen = vec![0u8; W * H];
    let mut q = vec![from];
    seen[ix(from.0, from.1)] = 1;
    while let Some((x, y)) = q.pop() {
        for (dx, dy) in [(1, 0), (-1, 0), (0, 1), (0, -1)] {
            let (nx, ny) = (x + dx, y + dy);
            if nx < 0 || ny < 0 || nx >= W as i32 || ny >= H as i32 {
                continue;
            }
            let j = ix(nx, ny);
            if seen[j] == 0 && fp[j] != 0 && walls[j] == 0 {
                seen[j] = 1;
                q.push((nx, ny));
            }
        }
    }
    seen
}

/// Radius 13, phase 0 (the brief: radius 14 put the four arm-end spawns inside the gate lanes).
fn grand_spawns() -> [(i32, i32); 8] {
    [
        (93, 80),
        (89, 89),
        (80, 93),
        (70, 89),
        (66, 80),
        (70, 70),
        (80, 66),
        (89, 70),
    ]
}

#[test]
fn grand_layout_is_a_36_tile_octagon_centred_on_the_map() {
    let f = grand_layout();
    assert_eq!((CX, CY), (80, 80));
    assert_eq!(
        f.co_rect,
        TileRect {
            x: 62,
            y: 62,
            w: 36,
            h: 36
        },
        "the grand footprint is 36x36 tiles, centred"
    );
    // Eight sides: each straight side 16 tiles, each chamfer widening the row by 2 per row for 10 rows.
    for k in 0..36 {
        let y = 62 + k;
        let xs: Vec<i32> = (0..W as i32).filter(|&x| f.fp[ix(x, y)] != 0).collect();
        let from_edge = k.min(35 - k);
        let want = (16 + 2 * from_edge).min(36);
        assert_eq!(xs.len() as i32, want, "row {y} footprint width");
        assert_eq!(
            xs[0] + xs[xs.len() - 1],
            159,
            "row {y} is symmetric about the centre"
        );
        assert!(
            xs.windows(2).all(|w| w[1] == w[0] + 1),
            "row {y} is one run"
        );
    }
    for (x, y) in [(62, 62), (97, 62), (62, 97), (97, 97)] {
        assert_eq!(f.fp[ix(x, y)], 0, "corner ({x},{y}) is cut by the chamfer");
    }
    let tiles = f.fp.iter().filter(|&&v| v != 0).count();
    assert_eq!(tiles, 36 * 36 - 4 * 55, "36x36 less four chamfered corners");
}

#[test]
fn grand_gates_open_the_four_straight_sides() {
    let f = grand_layout();
    let sides: Vec<GateSide> = f.gates.iter().map(|g| g.side).collect();
    assert_eq!(
        sides,
        [
            GateSide::North,
            GateSide::East,
            GateSide::South,
            GateSide::West
        ]
    );
    let want = [
        (78..=81).map(|x| (x, 62)).collect::<Vec<_>>(),
        (78..=81).map(|y| (97, y)).collect(),
        (78..=81).map(|x| (x, 97)).collect(),
        (78..=81).map(|y| (62, y)).collect(),
    ];
    for (g, want) in f.gates.iter().zip(want) {
        let got: Vec<(i32, i32)> = g.tiles.iter().map(|t| (t.x, t.y)).collect();
        assert_eq!(got, want, "{:?} gate tiles", g.side);
        for &(x, y) in &got {
            assert_eq!(
                f.wall_ring[ix(x, y)],
                1,
                "{:?} gate on the wall ring",
                g.side
            );
        }
    }
    let l = l_tiles();
    let lane: Vec<usize> = (0..W * H).filter(|&i| f.lane_in[i] != 0).collect();
    assert_eq!(lane.len(), 4 * 4 * 3, "four gates, 4 wide, 3 deep");
    for i in lane {
        assert_eq!(f.co_floor[i], 1, "lane tile {i} on the floor");
        assert_eq!(l[i], 0, "lane tile {i} clear of the Ls");
    }
}

#[test]
fn grand_pillars_are_the_four_drawn_ls() {
    let f = grand_layout();
    assert_eq!(GRAND_DEFINITION.pillars.len(), 1, "one orbit");
    assert_eq!(f.pillars.len(), 2, "one orbit: plain and mirrored variant");
    let want = l_tiles();
    let expected: Vec<(i32, i32)> = (0..W * H)
        .filter(|&i| want[i] != 0)
        .map(|i| ((i % W) as i32, (i / W) as i32))
        .collect();
    assert_eq!(expected.len(), 52, "four Ls of 13 tiles");
    for (v, mask) in f.pillars.iter().enumerate() {
        let got: Vec<(i32, i32)> = (0..W * H)
            .filter(|&i| mask[i] != 0)
            .map(|i| ((i % W) as i32, (i / W) as i32))
            .collect();
        assert_eq!(got, expected, "variant {v} is exactly the four Ls");
    }
    for &(x, y) in &expected {
        assert_eq!(
            f.co_floor[ix(x, y)],
            1,
            "L tile ({x},{y}) stands on the floor"
        );
    }
    // The open cross: columns and rows 74..=85 carry no pillar across the whole footprint (12 tiles wide).
    for a in 74..=85 {
        for b in 62..=97 {
            assert_eq!(f.pillars[0][ix(a, b)], 0, "cross column tile ({a},{b})");
            assert_eq!(f.pillars[0][ix(b, a)], 0, "cross row tile ({b},{a})");
        }
    }
    // The corridor: the four rows between each arm end (row 67 / 92) and the outer wall are open floor on every side.
    let walls = layout_walls(&f, 0);
    for d in 1..=4 {
        for x in 62..=97 {
            for (tx, ty) in [(x, 62 + d), (x, 97 - d), (62 + d, x), (97 - d, x)] {
                if f.fp[ix(tx, ty)] != 0 && f.wall_ring[ix(tx, ty)] == 0 {
                    assert_eq!(walls[ix(tx, ty)], 0, "corridor tile ({tx},{ty}) is open");
                }
            }
        }
        for (tx, ty) in [(73, 62 + d), (86, 97 - d), (62 + d, 86), (97 - d, 73)] {
            assert_eq!(
                f.co_floor[ix(tx, ty)],
                1,
                "corridor floor ({tx},{ty}) beyond an arm end"
            );
        }
    }
}

#[test]
fn grand_inner_spawns_are_eight_open_reachable_floor_tiles() {
    let f = grand_layout();
    let got: Vec<(i32, i32)> = f.inner_tiles.iter().map(|t| (t.x, t.y)).collect();
    assert_eq!(
        got,
        grand_spawns(),
        "four at the cross arm ends, four in the pockets behind the L corners"
    );
    let l = l_tiles();
    let in_cross = |x: i32, y: i32| (74..=85).contains(&x) || (74..=85).contains(&y);
    let in_pocket =
        |x: i32, y: i32| !in_cross(x, y) && (67..=92).contains(&x) && (67..=92).contains(&y);
    assert_eq!(got.iter().filter(|&&(x, y)| in_cross(x, y)).count(), 4);
    assert_eq!(got.iter().filter(|&&(x, y)| in_pocket(x, y)).count(), 4);
    for &(x, y) in &got {
        let i = ix(x, y);
        assert_eq!(f.co_floor[i], 1, "spawn ({x},{y}) on the floor");
        assert_eq!(l[i], 0, "spawn ({x},{y}) not on an L");
        assert_eq!(f.lane_in[i], 0, "spawn ({x},{y}) not in a gate lane");
    }
    for v in 0..f.pillars.len() {
        let walls = layout_walls(&f, v);
        let seen = reach(&walls, &f.fp, got[0]);
        for &(x, y) in &got {
            assert_eq!(seen[ix(x, y)], 1, "variant {v}: spawn ({x},{y}) reachable");
        }
        for g in &f.gates {
            for t in &g.tiles {
                assert_eq!(
                    seen[ix(t.x, t.y)],
                    1,
                    "variant {v}: {:?} gate reachable",
                    g.side
                );
            }
        }
    }
}

fn fnv(bytes: &[u8]) -> u64 {
    let mut h = 0xcbf2_9ce4_8422_2325u64;
    for &b in bytes {
        h ^= u64::from(b);
        h = h.wrapping_mul(0x0100_0000_01b3);
    }
    h
}

/// The legacy layout is untouched: `fixed_layout()` (every existing test, fixture and parity pin) is byte-identical.
#[test]
fn legacy_fixed_layout_is_unchanged() {
    let legacy = fixed_layout();
    let digest = fnv(&serde_json::to_vec(&legacy).unwrap());
    assert_eq!(
        digest, 0x9c5f_d97d_3883_c908,
        "legacy layout digest is now {digest:016x}"
    );
    assert!(
        grand_layout() != legacy,
        "the grand layout is not the legacy one"
    );
}

fn certified(fixed: &FixedLayout, seed: i64) -> Option<crate::mapdata::GameMapData> {
    let options = pipeline::Options {
        decor: false,
        ..pipeline::Options::default()
    };
    pipeline::Pipeline::new(fixed)
        .certified_styled(seed, options)
        .expect("the generator runs")
}

/// The full styled pipeline (the server's options: decor off) certifies grand-coliseum maps. Legacy measured 32/32 on
/// seeds 1..=32 (packet brief); the floor allows one refused seed in sixteen. Every certified map carries the grand
/// coliseum exactly: rect, eight inner spawns, and inside the footprint floor everywhere except the 52 L tiles.
#[test]
fn styled_pipeline_certifies_grand_maps() {
    let f = grand_layout();
    let l = l_tiles();
    let mut ok = 0;
    for seed in 1..=16 {
        let Some(map) = certified(&f, seed) else {
            continue;
        };
        ok += 1;
        let r = map.coliseum.rect;
        assert_eq!((r.x, r.y, r.w, r.h), (62, 62, 36, 36), "seed {seed}");
        let spawns: Vec<(i32, i32)> = map
            .coliseum
            .inner_spawns
            .iter()
            .map(|p| ((p.x / TILE).floor() as i32, (p.y / TILE).floor() as i32))
            .collect();
        assert_eq!(spawns, grand_spawns(), "seed {seed}");
        for (i, &is_l) in l.iter().enumerate() {
            if f.co_floor[i] == 0 {
                continue;
            }
            let want = if is_l != 0 { TILE_WALL } else { TILE_FLOOR };
            assert_eq!(
                map.tiles[i],
                want,
                "seed {seed}: tile ({}, {})",
                i % W,
                i / W
            );
        }
    }
    assert!(ok >= 15, "grand maps certified on {ok}/16 seeds, floor 15");
}

/// Arena teleport (`systems::coliseum::spots`, RING_RADIUS 5 tiles): on a grand map, eight players land on the eight
/// distinct grand inner spawns, each clear for a player's body; two players land on two distinct ones.
#[test]
fn arena_spots_use_the_grand_inner_spawns() {
    let f = grand_layout();
    let native = (1..=4)
        .find_map(|seed| certified(&f, seed))
        .expect("a grand map certifies on seeds 1..=4");
    let hash = u32::from_str_radix(&super::map_hash::map_hash(&native), 16).unwrap();
    let v = crate::mapdata::MapExport {
        format: "rh-map/2".into(),
        legacy_map_hash_u32: hash,
        map: native,
    }
    .validate()
    .unwrap();
    let (w, h, t, l) = v.into_parts();
    let spawns = l
        .data()
        .spawns
        .iter()
        .map(|p| crate::Vec2::new(p.x, p.y))
        .collect();
    let arena = crate::Arena {
        map: crate::TileMap::new(w, h, t)
            .unwrap()
            .with_layers(std::sync::Arc::new(l))
            .unwrap(),
        spawns,
    };
    let inner: Vec<(i64, i64)> = grand_spawns()
        .iter()
        .map(|&(x, y)| (i64::from(x), i64::from(y)))
        .collect();
    for n in [8usize, 2] {
        let spots = crate::systems::coliseum::spots(&arena, n);
        assert_eq!(spots.len(), n);
        let mut tiles: Vec<(i64, i64)> = spots
            .iter()
            .map(|p| ((p.x / TILE).floor() as i64, (p.y / TILE).floor() as i64))
            .collect();
        for (k, &tile) in tiles.iter().enumerate() {
            assert!(
                inner.contains(&tile),
                "n={n}: spot {k} {tile:?} is a grand inner spawn"
            );
            assert!(
                crate::geometry::circle_clear(&arena.map, spots[k], PLAYER_RADIUS, MASK_MOVE),
                "n={n}: spot {k} {tile:?} clear for a player"
            );
        }
        tiles.sort_unstable();
        tiles.dedup();
        assert_eq!(tiles.len(), n, "n={n}: distinct spots");
    }
}
