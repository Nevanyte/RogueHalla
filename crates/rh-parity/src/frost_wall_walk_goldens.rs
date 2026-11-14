//! P-FROST-WALL-PREDICT-1 Rust-native goldens for the Unity walk mirror round Frost Walls (not legacy parity): per-tick
//! rows of a walker in a live-config sim (`frost_wish = Wall`, `HitboxMode::Archetypes`, a Fire hero walker, a Frost
//! caster) while walls rise, stand, are walked into, cut a gap and fall, plus the same walk in a `Freeze` world. Each row
//! carries the walker's position, its `move_seq` and `displace_seq`, and the standing walls exactly as the walker's own
//! observation lists them (`observe(..).fields`, kind `Wall`: start, end, tiles), i.e. what the client holds for that
//! tick. `cargo run -p rh-parity --bin frost-wall-walk-fixtures` writes `protocol/fixtures/frost_wall_walk_v1.json`;
//! `--check` (and `tests/frost_wall_walk_golden.rs`) fails when the committed file differs from what the current core
//! produces.
//!
//! Timing (the C# replay depends on it): an order or cast with `tick: k` is submitted before the sim's k-th step, so the
//! server applies it in tick k's inputs phase; the client issues it at local time `50 * (k - 1)`, right after it applied
//! row k - 1. Row k is the state after step k (row 0 is the reset state). Order goals are rounded to `f32` (what the
//! client's `LocalMotion.Order` keeps). A `keyboard` order's goal is the legacy `keyboardMoveGoal` (C#
//! `KeyboardSteer.TryGoal`) for the held key, from the walker's row k - 1 position rounded to `f32` (what the client
//! sees), on the Welcome map with row k - 1's walls LOW: the goal the mirror's steering must choose.
use rh_sim::abilities::{Mode, Slot};
use rh_sim::archetype::HitboxMode;
use rh_sim::geometry::Footprint;
use rh_sim::map::{MASK_MOVE, TILE_FLOOR, TILE_LOW, TILE_WALL};
use rh_sim::status_runtime::FieldKind;
use rh_sim::{
    observe, validate, Arena, FactionId, FrostWish, Placement, PlayerId, RawCommand, Secrets, Sim,
    SimConfig, TileMap, Vec2,
};
use serde_json::{json, Value};
use std::path::{Path, PathBuf};

pub const FROST_WALL_WALK_FILE: &str = "protocol/fixtures/frost_wall_walk_v1.json";

const W: u32 = 40;
const H: u32 = 24;
/// The Frost caster stands at tile (5, 12) and always aims at tile (12, 12): the wall stands across column 12.
const CASTER: (u32, u32) = (5, 12);
const AIM: (u32, u32) = (12, 12);

fn repo(relative: &str) -> PathBuf {
    Path::new(env!("CARGO_MANIFEST_DIR"))
        .join("../..")
        .join(relative)
}

/// Tile centre in world units.
fn tc(t: u32) -> f64 {
    f64::from(t) * 32. + 16.
}

fn f32r(v: f64) -> f64 {
    f64::from(v as f32)
}

/// A 40 x 24 open room with a wall border.
fn room() -> Vec<u8> {
    let mut tiles = vec![TILE_FLOOR; (W * H) as usize];
    for y in 0..H {
        for x in 0..W {
            if x == 0 || y == 0 || x == W - 1 || y == H - 1 {
                tiles[(y * W + x) as usize] = TILE_WALL;
            }
        }
    }
    tiles
}

/// The room with rock in columns 9..=11 and 13..=15, rows 8..=16: column 12 is a one-tile corridor there, so a wall
/// across it that lands on a fighter leaves it no room on either side (the gap, `barriers::keep_gap`).
fn corridor() -> Vec<u8> {
    let mut tiles = room();
    for y in 8..=16 {
        for x in (9..=11).chain(13..=15) {
            tiles[(y * W + x) as usize] = TILE_WALL;
        }
    }
    tiles
}

#[derive(Clone, Copy)]
enum When {
    /// Before step k.
    Tick(u32),
    /// Before the step of the first row with the wall (L), plus d.
    Landing(i32),
    /// Before the step of the first row after L without the wall (E), plus d.
    Expiry(i32),
}

#[derive(Clone, Copy)]
enum Goal {
    At(f64, f64),
    /// A held key (server axes, -1/0/1).
    Keyboard(i32, i32),
}

struct Case {
    name: &'static str,
    description: &'static str,
    map: &'static str,
    wish: FrostWish,
    walker: Vec2,
    cast: Option<u32>,
    orders: Vec<(When, Goal)>,
    steps: u32,
}

fn cases() -> Vec<Case> {
    let west = Goal::At(tc(6), tc(12));
    vec![
        Case {
            name: "freeze_world_no_wall",
            description: "Freeze world: the same Wish cast is refused (Freeze needs a unit), no wall ever stands, the walk is straight: the mirror must plan on the Welcome map exactly as today.",
            map: "room",
            wish: FrostWish::Freeze,
            walker: Vec2::new(tc(18), tc(12)),
            cast: Some(1),
            orders: vec![(When::Tick(1), west)],
            steps: 60,
        },
        Case {
            name: "click_past_standing_wall",
            description: "A click past a standing wall: the server plans round it; the mirror's path must be the same.",
            map: "room",
            wish: FrostWish::Wall,
            walker: Vec2::new(tc(18), tc(12)),
            cast: Some(1),
            orders: vec![(When::Landing(4), west)],
            steps: 70,
        },
        Case {
            name: "click_into_standing_wall",
            description: "A click on the wall's middle tile: the server snaps the goal off the ice and stops where it plans to.",
            map: "room",
            wish: FrostWish::Wall,
            walker: Vec2::new(tc(18), tc(12)),
            cast: Some(1),
            orders: vec![(When::Landing(4), Goal::At(tc(12), tc(12)))],
            steps: 60,
        },
        Case {
            name: "keyboard_into_standing_wall",
            description: "The west key held (an order every 2 ticks, the 100 ms repeat) into a standing wall: each goal is the steering goal on the map with the wall LOW, and the walker stops against the ice.",
            map: "room",
            wish: FrostWish::Wall,
            walker: Vec2::new(tc(16), tc(12)),
            cast: Some(1),
            orders: (0..20)
                .map(|i| (When::Landing(2 + 2 * i), Goal::Keyboard(-1, 0)))
                .collect(),
            steps: 60,
        },
        Case {
            name: "wall_rises_on_walk",
            description: "A walk under way when a wall lands across it: in the landing tick the server re-plans the blocked walk from where it stood to its last waypoint, and walks the new path that same tick.",
            map: "room",
            wish: FrostWish::Wall,
            walker: Vec2::new(tc(18), tc(12)),
            cast: Some(1),
            orders: vec![(When::Tick(1), west)],
            steps: 70,
        },
        Case {
            name: "wall_falls_mid_detour",
            description: "A detour planned round a wall that falls while it is walked: the server re-plans nothing when a wall falls, so the detour is walked to its end.",
            map: "room",
            wish: FrostWish::Wall,
            walker: Vec2::new(tc(18), tc(12)),
            cast: Some(1),
            orders: vec![(When::Expiry(-12), west)],
            steps: 0,
        },
        Case {
            name: "orders_across_expiry",
            description: "Orders applied on the tick before the expiry tick E, on E itself (inputs run before the wall falls: still planned round it) and on E + 1 (planned through the open tiles).",
            map: "room",
            wish: FrostWish::Wall,
            walker: Vec2::new(tc(18), tc(12)),
            cast: Some(1),
            orders: vec![
                (When::Expiry(-1), west),
                (When::Expiry(0), west),
                (When::Expiry(1), west),
            ],
            steps: 0,
        },
        Case {
            name: "gap_walker",
            description: "A walker standing in a one-tile corridor when a wall lands on it, with no room on either side: the wall keeps a gap round it (rows 11..=13 are not ice). An order north is unreachable (the server stays); one south inside the gap is walked.",
            map: "corridor",
            wish: FrostWish::Wall,
            walker: Vec2::new(tc(12), tc(12)),
            cast: Some(1),
            orders: vec![
                (When::Landing(2), Goal::At(tc(12), tc(3))),
                (When::Landing(10), Goal::At(tc(12), 440.)),
            ],
            steps: 50,
        },
        Case {
            name: "pushed_walker",
            description: "A walker on the wall's line when it lands: the server sets it down east of the ice (displace_seq + 1) and re-plans its walk; the mirror follows the correction and pushes nothing itself.",
            map: "room",
            wish: FrostWish::Wall,
            walker: Vec2::new(tc(12), tc(17)),
            cast: Some(1),
            orders: vec![(When::Tick(1), Goal::At(tc(12), tc(2)))],
            // The walk ends near tick 58; the rest lets the smoothing settle on the server.
            steps: 90,
        },
    ]
}

fn tiles_for(map: &str) -> Vec<u8> {
    match map {
        "room" => room(),
        "corridor" => corridor(),
        other => panic!("unknown map {other}"),
    }
}

fn config(wish: FrostWish) -> SimConfig {
    SimConfig {
        abilities: Mode::Full,
        wish_lock_ms: 0.,
        frost_wish: wish,
        quests: false,
        consumables: false,
        hitboxes: HitboxMode::Archetypes,
        ..Default::default()
    }
}

struct Run {
    rows: Vec<Value>,
    orders: Vec<Value>,
    landing: Option<u32>,
    expiry: Option<u32>,
}

/// The walls in an observation: (started_at, ends_at, tiles).
type Walls = Vec<(f64, f64, Vec<u32>)>;

fn walls_of(sim: &Sim, viewer: PlayerId, events: &[rh_sim::Event]) -> Walls {
    observe(sim, viewer, events)
        .fields
        .iter()
        .filter(|f| f.kind == FieldKind::Wall)
        .map(|f| (f.started_at, f.ends_at, f.tiles.clone()))
        .collect()
}

/// Legacy `keyboardMoveGoal` as the C# `KeyboardSteer.TryGoal` ports it: the held heading (and on a diagonal each single
/// axis), 64 u down to 8 u in 8 u steps, then for a box foot 7 u down to 1 u; the first goal the foot slides to clear.
fn keyboard_goal(map: &TileMap, origin: Vec2, kx: i32, ky: i32, foot: Footprint) -> Option<Vec2> {
    let (kx, ky) = (f64::from(kx.signum()), f64::from(ky.signum()));
    let len = (kx * kx + ky * ky).sqrt();
    let headings = if kx != 0. && ky != 0. { 3 } else { 1 };
    for h in 0..headings {
        let (hx, hy) = match h {
            0 => (kx / len, ky / len),
            1 => (kx, 0.),
            _ => (0., ky),
        };
        let mut d = 64.;
        while d >= 8. {
            let g = Vec2::new(origin.x + hx * d, origin.y + hy * d);
            if foot.segment_clear(map, origin, g, MASK_MOVE) {
                return Some(g);
            }
            d -= 8.;
        }
        if matches!(foot, Footprint::Box { .. }) {
            let mut d = 7.;
            while d >= 1. {
                let g = Vec2::new(origin.x + hx * d, origin.y + hy * d);
                if foot.segment_clear(map, origin, g, MASK_MOVE) {
                    return Some(g);
                }
                d -= 1.;
            }
        }
    }
    None
}

fn with_walls(base: &[u8], walls: &Walls) -> TileMap {
    let mut tiles = base.to_vec();
    for (_, _, cells) in walls {
        for &i in cells {
            if tiles[i as usize] == TILE_FLOOR {
                tiles[i as usize] = TILE_LOW;
            }
        }
    }
    TileMap::new(W, H, tiles).expect("map")
}

/// The step an order is submitted before; `None` while the landing or expiry it is placed by is not known yet (the first
/// pass).
fn order_tick(when: When, landing: Option<u32>, expiry: Option<u32>) -> Option<u32> {
    let at = |base: Option<u32>, d: i32| {
        base.map(|b| u32::try_from(i64::from(b) + i64::from(d)).expect("tick"))
    };
    match when {
        When::Tick(k) => Some(k),
        When::Landing(d) => at(landing, d),
        When::Expiry(d) => at(expiry, d),
    }
}

/// Runs a case for `steps` steps. `known` is the (landing, expiry) of a first pass, so orders placed relative to them
/// can be submitted; the second pass must find the same ticks.
fn run(case: &Case, steps: u32, known: (Option<u32>, Option<u32>)) -> Run {
    let base = tiles_for(case.map);
    let placements = [
        Placement {
            profile: "walker".into(),
            faction: FactionId::Fire,
            bot: false,
            at: case.walker,
            hp: Some(1e9),
            weapon0: None,
            weapon1: None,
        },
        Placement {
            profile: "caster".into(),
            faction: FactionId::Frost,
            bot: false,
            at: Vec2::new(tc(CASTER.0), tc(CASTER.1)),
            hp: Some(1e9),
            weapon0: None,
            weapon1: None,
        },
    ];
    let mut sim = Sim::reset_active(
        config(case.wish),
        Arena {
            map: TileMap::new(W, H, base.clone()).expect("map"),
            spawns: placements.iter().map(|p| p.at).collect(),
        },
        Secrets::from_seed(11),
        &placements,
    );
    let walker = sim.players()[0].id;
    let caster = sim.players()[1].id;
    assert_eq!(
        sim.player(walker).expect("walker").archetype,
        rh_sim::archetype::ArchetypeId::HERO
    );
    let foot = sim.player(walker).expect("walker").foot();
    let row = |sim: &Sim, walls: &Walls| {
        let p = sim.player(walker).expect("walker");
        let walls: Vec<Value> = walls
            .iter()
            .map(|(s, e, t)| json!({"startedAt": s, "endsAt": e, "tiles": t}))
            .collect();
        let path: Vec<Value> = p
            .motion
            .path
            .as_slice()
            .iter()
            .map(|q| json!([q.x, q.y]))
            .collect();
        json!({
            "tick": sim.tick(),
            "t": sim.server_time(),
            "x": p.body.x,
            "y": p.body.y,
            "moveSeq": p.motion.move_seq,
            "displaceSeq": p.motion.displace_seq,
            "path": path,
            "walls": walls,
        })
    };
    let mut walls: Walls = walls_of(&sim, walker, &[]);
    let mut rows = vec![row(&sim, &walls)];
    let mut orders = Vec::new();
    let (mut landing, mut expiry) = (None, None);
    let mut seq: u8 = 0;
    for k in 1..=steps {
        if case.cast == Some(k) {
            let cmd = validate(RawCommand::Cast {
                slot: Slot::Wish,
                x: tc(AIM.0),
                y: tc(AIM.1),
                id: None,
                t: None,
                seq: None,
            })
            .expect("a finite cast");
            // Under Freeze the point cast is refused (Freeze needs a unit): that is the Freeze case.
            let _ = sim.submit(caster, cmd);
        }
        for &(when, kind) in &case.orders {
            if order_tick(when, known.0, known.1) != Some(k) {
                continue;
            }
            let goal = match kind {
                Goal::At(x, y) => Some(Vec2::new(x, y)),
                Goal::Keyboard(kx, ky) => {
                    let p = sim.player(walker).expect("walker");
                    let origin = Vec2::new(f32r(p.body.x), f32r(p.body.y));
                    keyboard_goal(&with_walls(&base, &walls), origin, kx, ky, foot)
                }
            };
            let Some(goal) = goal else {
                continue;
            };
            let goal = Vec2::new(f32r(goal.x), f32r(goal.y));
            seq += 1;
            let cmd = validate(RawCommand::Move {
                x: goal.x,
                y: goal.y,
                seq: Some(seq),
            })
            .expect("a finite move");
            sim.submit(walker, cmd).expect("one move order");
            let mut o = json!({"tick": k, "seq": seq, "goal": [goal.x, goal.y]});
            if let Goal::Keyboard(kx, ky) = kind {
                o["keyboard"] = json!([kx, ky]);
            }
            orders.push(o);
        }
        let events = sim.step();
        walls = walls_of(&sim, walker, &events);
        if landing.is_none() && !walls.is_empty() {
            landing = Some(k);
        } else if landing.is_some() && expiry.is_none() && walls.is_empty() {
            expiry = Some(k);
        }
        rows.push(row(&sim, &walls));
    }
    Run {
        rows,
        orders,
        landing,
        expiry,
    }
}

fn case_json(case: &Case) -> Value {
    // First pass: where the wall lands and falls (no order moves it: the walker never stands where the wall lands
    // except in the gap and push cases, whose orders come after the landing or do not change where the walker is then).
    let probe = run(case, 140, (None, None));
    let known = (probe.landing, probe.expiry);
    let steps = if case.steps > 0 {
        case.steps
    } else {
        probe.expiry.expect("the wall fell") + 60
    };
    let out = run(case, steps, known);
    assert_eq!(
        (out.landing, out.expiry.filter(|&e| e <= steps)),
        (known.0, known.1.filter(|&e| e <= steps)),
        "{}: the orders moved the wall's landing or fall",
        case.name
    );
    check(case, &out);
    json!({
        "name": case.name,
        "description": case.description,
        "map": case.map,
        "frostWish": format!("{:?}", case.wish),
        "landingTick": out.landing,
        "expiryTick": out.expiry,
        "orders": out.orders,
        "ticks": out.rows,
    })
}

/// What each case is for, asserted on the server's own run (a fixture that no longer shows its event fails here).
fn check(case: &Case, out: &Run) {
    let rows = &out.rows;
    let x = |k: usize| rows[k]["x"].as_f64().unwrap();
    let y = |k: usize| rows[k]["y"].as_f64().unwrap();
    let displaced = |k: usize| rows[k]["displaceSeq"].as_u64().unwrap();
    let wall_rows = rows
        .iter()
        .filter(|r| !r["walls"].as_array().unwrap().is_empty())
        .count();
    if case.wish == FrostWish::Freeze {
        assert_eq!(wall_rows, 0, "{}: no wall in a Freeze world", case.name);
        return;
    }
    let landing = out.landing.expect("the wall landed") as usize;
    assert!(wall_rows > 0);
    let tiles: Vec<u64> = rows[landing]["walls"][0]["tiles"]
        .as_array()
        .unwrap()
        .iter()
        .map(|t| t.as_u64().unwrap())
        .collect();
    let column = |rows: std::ops::RangeInclusive<u64>| -> Vec<u64> {
        rows.map(|r| r * u64::from(W) + u64::from(AIM.0)).collect()
    };
    let crossed_wall = |k: usize| x(k) > 400. && x(k + 1) < 400. || x(k) < 400. && x(k + 1) > 400.;
    match case.name {
        "gap_walker" => {
            let want: Vec<u64> = column(9..=10).into_iter().chain(column(14..=15)).collect();
            assert_eq!(tiles, want, "the gap leaves rows 11..=13");
            assert_eq!(displaced(landing), displaced(0), "a gap, not a push");
            assert!(
                rows.iter().all(|r| r["y"].as_f64().unwrap() > 352.),
                "the order north is unreachable: the walker stays below row 11's top"
            );
            assert!(
                (y(rows.len() - 1) - 440.).abs() < 1e-6,
                "the order south inside the gap is walked"
            );
        }
        "pushed_walker" => {
            assert_eq!(tiles, column(9..=15));
            assert_eq!(
                displaced(landing),
                displaced(0) + 1,
                "the walker is set down"
            );
            assert_eq!(displaced(landing - 1), displaced(0));
            assert!(x(landing) > 416., "set down east of the ice");
            assert!(
                y(rows.len() - 1) < tc(3),
                "the walk north goes on after the push"
            );
        }
        _ => {
            assert_eq!(tiles, column(9..=15), "{}: the wall's tiles", case.name);
            for k in landing..rows.len() - 1 {
                if !rows[k + 1]["walls"].as_array().unwrap().is_empty() {
                    assert!(
                        !(crossed_wall(k) && (y(k) - 400.).abs() < 120.),
                        "{}: the server never walks through standing ice (tick {k})",
                        case.name
                    );
                }
            }
        }
    }
    let path_len = |k: usize| rows[k]["path"].as_array().unwrap().len();
    if case.name == "wall_falls_mid_detour" {
        let e = out.expiry.expect("the wall fell") as usize;
        assert!(
            path_len(e) > 1,
            "the fall re-plans nothing: the detour goes on"
        );
        assert!(
            (x(rows.len() - 1) - tc(6)).abs() < 1e-6,
            "and reaches the goal"
        );
    }
    if case.name == "orders_across_expiry" {
        let e = out.expiry.expect("the wall fell") as usize;
        assert!(path_len(e - 1) > 1, "E - 1: planned round the wall");
        assert!(path_len(e) > 1, "E: inputs run before the wall falls");
        assert_eq!(path_len(e + 1), 1, "E + 1: planned through the open tiles");
    }
    if case.name == "keyboard_into_standing_wall" {
        let last = rows.len() - 1;
        assert!(
            (426. ..=428.).contains(&x(last)),
            "the hero's foot box (hx 10) stops within 2 u of touching the ice's east face (416): {}",
            x(last)
        );
    }
    if case.name == "wall_rises_on_walk" {
        let ys: Vec<f64> = (landing..rows.len()).map(y).collect();
        assert!(
            ys.iter().any(|v| (v - tc(12)).abs() > 64.),
            "the re-planned walk leaves the straight line"
        );
        assert!(
            (x(rows.len() - 1) - tc(6)).abs() < 1e-6,
            "and reaches the goal"
        );
    }
}

/// `protocol/fixtures/frost_wall_walk_v1.json`.
pub fn frost_wall_walk_json() -> String {
    let rows: Vec<Value> = cases().iter().map(case_json).collect();
    let v = json!({
        "format": "rh-frost-wall-walk/1",
        "generator": "cargo run -p rh-parity --bin frost-wall-walk-fixtures (crates/rh-parity/src/frost_wall_walk_goldens.rs); checked by crates/rh-parity/tests/frost_wall_walk_golden.rs",
        "source": "crates/rh-sim Sim::reset_active, SimConfig { abilities: Full, wish_lock_ms: 0, frost_wish, quests: false, consumables: false, hitboxes: Archetypes }, a Fire (hero) walker and a Frost caster, 20 Hz",
        "timing": "an order or cast with tick k is submitted before step k (the client issues it at 50 * (k - 1), after applying row k - 1); row k is the state after step k; each row's walls are the walker's own observation of that tick (the State frame the client applies before the row's self sample)",
        "tickMs": 50.0,
        "playerRadius": rh_sim::constants::PLAYER_RADIUS,
        "footprint": {"kind": "box", "hx": 10.0, "hy": 6.0},
        "archetype": rh_sim::archetype::ArchetypeId::HERO.0,
        "caster": [tc(CASTER.0), tc(CASTER.1)],
        "aim": [tc(AIM.0), tc(AIM.1)],
        "maps": [
            {"name": "room", "w": W, "h": H, "tiles": room()},
            {"name": "corridor", "w": W, "h": H, "tiles": corridor()},
        ],
        "cases": rows,
    });
    let mut s = serde_json::to_string_pretty(&v).expect("json");
    s.push('\n');
    s
}

pub fn all() -> Vec<(&'static str, String)> {
    vec![(FROST_WALL_WALK_FILE, frost_wall_walk_json())]
}

/// Writes the golden (`check == false`) or lists the files that differ from the committed ones.
pub fn write_or_check(check: bool) -> Vec<&'static str> {
    let mut stale = Vec::new();
    for (path, text) in all() {
        let full = repo(path);
        if check {
            if std::fs::read_to_string(&full).ok().as_deref() != Some(text.as_str()) {
                stale.push(path);
            }
        } else {
            std::fs::write(&full, text).unwrap_or_else(|e| panic!("{}: {e}", full.display()));
        }
    }
    stale
}
