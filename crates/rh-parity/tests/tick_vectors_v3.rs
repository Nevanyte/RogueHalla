//! P-TICK-4 oracle (ADR 0029): the prediction vectors at both step lengths. `protocol/fixtures/server_walk_v3.json` and
//! `ranged_pause_v3.json` each carry `runs: [{tickMs: 50, cases}, {tickMs: 25, cases}]`. The 50 ms run is the v2 file's
//! `cases` byte for byte (nothing at 20 Hz moves); the 25 ms run is the same scenarios on a 25 ms sim, the same wall time
//! (twice the rows). The generators (`hitbox_goldens.rs`, `ranged_pause_golden.rs`) write them; this file judges them
//! without the generators: the 25 ms walk is re-simulated here, the pause runs are checked by shape and by a loose
//! wall-clock proof against the 50 ms run (owner direction 2026-11-10: not drastically off).
//!
//! The C# tests (`TickRateVectorTests.cs`) replay the same files through `LocalMotion`.

use rh_parity::load_paths;
use rh_sim::archetype::{ArchetypeId, HitboxMode};
use rh_sim::{
    validate, Arena, FactionId, Placement, PlayerId, RawCommand, Secrets, Sim, SimConfig, Vec2,
};
use serde_json::Value;
use std::path::{Path, PathBuf};

fn repo(relative: &str) -> PathBuf {
    Path::new(env!("CARGO_MANIFEST_DIR"))
        .join("../..")
        .join(relative)
}

fn read(name: &str) -> Value {
    let path = repo(&format!("protocol/fixtures/{name}"));
    assert!(
        path.exists(),
        "{name} is missing: the generators write it (cargo run -p rh-parity --bin hitbox-fixtures; \
         RH_WRITE_FIXTURES=1 cargo test -p rh-parity --test ranged_pause_golden)"
    );
    serde_json::from_str(&std::fs::read_to_string(&path).unwrap()).unwrap()
}

fn runs(v: &Value) -> &Vec<Value> {
    v["runs"].as_array().expect("runs")
}

fn run_at(v: &Value, tick_ms: f64) -> &Value {
    runs(v)
        .iter()
        .find(|r| r["tickMs"].as_f64() == Some(tick_ms))
        .unwrap_or_else(|| panic!("no run at {tick_ms} ms"))
}

fn cases(run: &Value) -> &Vec<Value> {
    run["cases"].as_array().expect("cases")
}

fn tick_ms_list(v: &Value) -> Vec<f64> {
    runs(v)
        .iter()
        .map(|r| r["tickMs"].as_f64().expect("tickMs"))
        .collect()
}

// ---------------------------------------------------------------------------------------------------------------------
// server_walk_v3

#[test]
fn walk_v3_has_a_fifty_and_a_twenty_five_millisecond_run() {
    let v = read("server_walk_v3.json");
    assert_eq!(v["format"], "rh-server-walk/3");
    assert_eq!(tick_ms_list(&v), vec![50.0, 25.0]);
    for key in ["map", "playerSpeed", "archetype", "footprint"] {
        assert!(!v[key].is_null(), "{key} is carried like in v2");
    }
}

#[test]
fn walk_v3_fifty_run_is_the_v2_file() {
    let v3 = read("server_walk_v3.json");
    let v2 = read("server_walk_v2.json");
    assert_eq!(
        run_at(&v3, 50.0)["cases"],
        v2["cases"],
        "the 20 Hz run reproduces v2 exactly"
    );
}

#[test]
fn walk_v3_rows_sit_on_the_step_grid_for_twelve_seconds() {
    let v3 = read("server_walk_v3.json");
    for (tick_ms, rows) in [(50.0, 241usize), (25.0, 481)] {
        let list = cases(run_at(&v3, tick_ms));
        assert!(list.len() >= 3, "the corner walks");
        for c in list {
            let ticks = c["ticks"].as_array().unwrap();
            assert_eq!(ticks.len(), rows, "{} at {tick_ms} ms", c["name"]);
            assert_eq!(ticks[0]["tick"], 1, "the reset state is tick 1");
            for (k, row) in ticks.iter().enumerate() {
                let tick = row["tick"].as_u64().unwrap();
                assert_eq!(tick, 1 + k as u64);
                assert_eq!(
                    row["t"].as_f64().unwrap(),
                    tick as f64 * tick_ms,
                    "t = tick * tickMs ({} row {k} at {tick_ms} ms)",
                    c["name"]
                );
            }
        }
    }
}

/// The 25 ms walk, simulated here (not read back from the generator): the same Fire (hero) walker, one Move order.
fn walk_rows(
    map: &rh_sim::TileMap,
    spawns: &[Vec2],
    case: &Value,
    tick_ms: u32,
    steps: usize,
) -> Vec<(f64, f64, f64, u64)> {
    let pt = |k: &str| Vec2::new(case[k][0].as_f64().unwrap(), case[k][1].as_f64().unwrap());
    let (start, goal) = (pt("start"), pt("goal"));
    let mut sim = Sim::reset_active(
        SimConfig {
            hitboxes: HitboxMode::Archetypes,
            tick_ms,
            ..SimConfig::default()
        },
        Arena {
            map: map.clone(),
            spawns: spawns.to_vec(),
        },
        Secrets::from_seed(11),
        &[Placement {
            profile: "walker".into(),
            faction: FactionId::Fire,
            bot: false,
            at: start,
            hp: None,
            weapon0: None,
            weapon1: None,
        }],
    );
    assert_eq!(
        sim.player(PlayerId(1)).unwrap().archetype,
        ArchetypeId::HERO
    );
    sim.submit(
        PlayerId(1),
        validate(RawCommand::Move {
            x: goal.x,
            y: goal.y,
            seq: Some(1),
        })
        .unwrap(),
    )
    .unwrap();
    let row = |sim: &Sim| {
        let p = sim.player(PlayerId(1)).unwrap();
        (
            p.body.x,
            p.body.y,
            sim.server_time(),
            u64::from(p.motion.move_seq),
        )
    };
    let mut out = vec![row(&sim)];
    for _ in 0..steps {
        sim.step();
        out.push(row(&sim));
    }
    out
}

fn world(v3: &Value) -> (rh_sim::TileMap, Vec<Vec2>) {
    let (_, map) = load_paths(&repo("parity/fixtures/pathfinding_seed1.json"));
    let seed: Value =
        serde_json::from_str(&std::fs::read_to_string(repo(v3["map"].as_str().unwrap())).unwrap())
            .unwrap();
    let spawns = seed["spawns"]
        .as_array()
        .unwrap()
        .iter()
        .map(|p| Vec2::new(p[0].as_f64().unwrap(), p[1].as_f64().unwrap()))
        .collect();
    (map, spawns)
}

#[test]
fn walk_v3_twenty_five_run_is_what_a_25_ms_sim_walks() {
    let v3 = read("server_walk_v3.json");
    let (map, spawns) = world(&v3);
    for c in cases(run_at(&v3, 25.0)) {
        let want = walk_rows(&map, &spawns, c, 25, 480);
        let got = c["ticks"].as_array().unwrap();
        assert_eq!(got.len(), want.len());
        for (k, (g, w)) in got.iter().zip(&want).enumerate() {
            let name = &c["name"];
            assert!(
                (g["x"].as_f64().unwrap() - w.0).abs() < 1e-9,
                "{name} row {k} x"
            );
            assert!(
                (g["y"].as_f64().unwrap() - w.1).abs() < 1e-9,
                "{name} row {k} y"
            );
            assert_eq!(g["t"].as_f64().unwrap(), w.2, "{name} row {k} t");
            assert_eq!(
                g["moveSeq"].as_u64().unwrap(),
                w.3,
                "{name} row {k} moveSeq"
            );
        }
    }
}

/// Loose proof (ADR 0029 checklist): after the same wall time the walker is within one 50 ms step of walking
/// (PLAYER_SPEED 170 u/s x 0.05 s = 8.5 u) of where the 20 Hz walker is.
#[test]
fn walk_v3_distance_per_second_matches_across_rates() {
    let v3 = read("server_walk_v3.json");
    let one_step = 170.0 * 0.05;
    let (c50, c25) = (cases(run_at(&v3, 50.0)), cases(run_at(&v3, 25.0)));
    assert_eq!(c50.len(), c25.len());
    for (a, b) in c50.iter().zip(c25) {
        assert_eq!(a["name"], b["name"]);
        for seconds in [2usize, 6, 12] {
            let pa = &a["ticks"][seconds * 20];
            let pb = &b["ticks"][seconds * 40];
            assert_eq!(pa["t"].as_f64(), pb["t"].as_f64(), "the same wall time");
            let d = ((pa["x"].as_f64().unwrap() - pb["x"].as_f64().unwrap()).powi(2)
                + (pa["y"].as_f64().unwrap() - pb["y"].as_f64().unwrap()).powi(2))
            .sqrt();
            assert!(
                d <= one_step + 1e-6,
                "{} after {seconds} s: {d} u apart across rates",
                a["name"]
            );
        }
    }
}

// ---------------------------------------------------------------------------------------------------------------------
// ranged_pause_v3

const CASE_NAMES: [&str; 16] = [
    "wand-single-shot",
    "wand-held",
    "wand-rejected-cooldown",
    "wand-staggered",
    "wand-staggered-in-pause",
    "wand-weapon-swap",
    "bow-single-shot",
    "bow-held",
    "bow-holy-faction",
    "bow-plague-faction",
    "bow-frost-faction",
    "bow-rejected-cooldown",
    "bow-staggered-mid-draw",
    "bow-weapon-swap-mid-draw",
    "bow-weapon-swap-early-draw",
    "sword-swing",
];

fn paused_ms(case: &Value, tick_ms: f64) -> f64 {
    case["rows"]
        .as_array()
        .unwrap()
        .iter()
        .filter(|r| r["moveMult"] == serde_json::json!(0))
        .count() as f64
        * tick_ms
}

#[test]
fn pause_v3_has_both_runs_and_the_same_sixteen_cases() {
    let v = read("ranged_pause_v3.json");
    assert_eq!(v["format"], "rh-ranged-pause/3");
    assert_eq!(v["walkFixture"], "server_walk_v3.json");
    assert_eq!(tick_ms_list(&v), vec![50.0, 25.0]);
    for key in [
        "playerSpeed",
        "rangedReleasePauseMs",
        "bowWindupMaxS",
        "bowWindupIntervalShare",
        "tieEpsMs",
    ] {
        assert!(!v["constants"][key].is_null(), "constants.{key}");
    }
    for tick_ms in [50.0, 25.0] {
        let names: Vec<&str> = cases(run_at(&v, tick_ms))
            .iter()
            .map(|c| c["name"].as_str().unwrap())
            .collect();
        assert_eq!(names, CASE_NAMES, "at {tick_ms} ms");
    }
}

#[test]
fn pause_v3_fifty_run_is_the_v2_file() {
    let v3 = read("ranged_pause_v3.json");
    let v2 = read("ranged_pause_v2.json");
    assert_eq!(run_at(&v3, 50.0)["cases"], v2["cases"]);
}

#[test]
fn pause_v3_twenty_five_run_doubles_the_rows_and_the_script() {
    let v3 = read("ranged_pause_v3.json");
    let (c50, c25) = (cases(run_at(&v3, 50.0)), cases(run_at(&v3, 25.0)));
    for (a, b) in c50.iter().zip(c25) {
        let name = a["name"].as_str().unwrap();
        let (ra, rb) = (a["rows"].as_array().unwrap(), b["rows"].as_array().unwrap());
        assert_eq!(
            rb.len(),
            2 * (ra.len() - 1) + 1,
            "{name}: the same wall time"
        );
        for (k, row) in rb.iter().enumerate() {
            assert_eq!(row["row"].as_u64().unwrap(), k as u64, "{name}");
            let tick = row["tick"].as_u64().unwrap();
            assert_eq!(
                row["t"].as_f64().unwrap(),
                tick as f64 * 25.0,
                "{name} row {k}: t = tick * 25"
            );
        }
        let (ca, cb) = (
            a["commands"].as_array().unwrap(),
            b["commands"].as_array().unwrap(),
        );
        assert_eq!(ca.len(), cb.len(), "{name}: the same script");
        for (x, y) in ca.iter().zip(cb) {
            assert_eq!(x["kind"], y["kind"], "{name}");
            assert_eq!(
                y["afterRow"].as_u64().unwrap(),
                2 * x["afterRow"].as_u64().unwrap(),
                "{name}: a script row doubles"
            );
        }
        assert_eq!(a["weapon"], b["weapon"], "{name}");
        assert_eq!(a["goal"], b["goal"], "{name}");
    }
}

/// Loose proof: how long the server stops the walk is a wall-clock rule (wind-up, `RANGED_RELEASE_PAUSE_MS`), so at 25 ms
/// it is the 50 ms figure within a step or two and a fifth; a case that never paused at 50 ms does not start to.
#[test]
fn pause_v3_stops_the_walk_for_about_the_same_time_at_both_rates() {
    let v3 = read("ranged_pause_v3.json");
    let (c50, c25) = (cases(run_at(&v3, 50.0)), cases(run_at(&v3, 25.0)));
    let mut with_pause = 0;
    for (a, b) in c50.iter().zip(c25) {
        let name = a["name"].as_str().unwrap();
        let (p50, p25) = (paused_ms(a, 50.0), paused_ms(b, 25.0));
        if p50 > 0.0 {
            with_pause += 1;
            assert!(
                p25 > 0.0,
                "{name}: paused {p50} ms at 20 Hz, never at 40 Hz"
            );
        }
        assert!(
            (p50 - p25).abs() <= 100.0f64.max(0.4 * p50),
            "{name}: paused {p50} ms at 20 Hz but {p25} ms at 40 Hz"
        );
    }
    assert!(with_pause >= 10, "the cases exercise the pause");
}
