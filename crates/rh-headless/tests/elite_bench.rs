//! Oracle for packet P-ELITE-BENCH-1 (docs/backlog/packets/P-ELITE-BENCH-1.md): the elite mode of `camp_bench` and the
//! minute-5 build sample it fights with.
//!
//! The elite code lives in `src/elite_bench.rs` and is compiled into this test through `#[path]` (rh-sim's
//! `trusted-build-setup` comes from this crate's dev-dependency features), so it runs in process under a plain
//! `cargo test -p rh-headless`. Nothing here asserts a balance number: the bench is evidence for the owner checkpoint
//! (target: an average minute-5 build dies 10-20% soloing an elite camp), not a gate on today's numbers. What is pinned
//! is that the report is complete, consistent, deterministic, honest about its caveats, and that the builds are real.
#[path = "../src/elite_bench.rs"]
mod elite_bench;

use elite_bench::{run, sample_builds, SampleFile, MINUTE5_TICK, SAMPLE_PATH};
use rh_headless::load_arena;
use rh_sim::mob_catalog::{elites_of, normal_types_of};
use rh_sim::trusted_build::{apply, TrustedBuild};
use rh_sim::{FactionId, Placement, Secrets, Sim, SimConfig, WeaponId};
use serde_json::{json, Value};
use std::collections::BTreeSet;
use std::path::PathBuf;
use std::process::Command;
use std::sync::{Arc, OnceLock};

const MAP_REL: &str = "crates/rh-sim/src/mapdata/fixtures/seed1.map.json";
/// Fight cap for the data tests: small enough for a debug build, large enough to walk 10 tiles and swing.
const TEST_TICKS: &str = "400";

fn root() -> PathBuf {
    PathBuf::from(env!("CARGO_MANIFEST_DIR")).join("../..")
}
fn args(a: &[&str]) -> Vec<String> {
    a.iter().map(|s| s.to_string()).collect()
}
fn s<'a>(v: &'a Value, k: &str) -> &'a str {
    v[k].as_str()
        .unwrap_or_else(|| panic!("field {k} missing or not a string in {v}"))
}
fn u(v: &Value, k: &str) -> u64 {
    v[k].as_u64()
        .unwrap_or_else(|| panic!("field {k} missing or not an integer in {v}"))
}
fn num(v: &Value, k: &str) -> f64 {
    let x = v[k]
        .as_f64()
        .unwrap_or_else(|| panic!("field {k} missing or not a number in {v}"));
    assert!(x.is_finite(), "field {k} is not finite in {v}");
    x
}

// ---- the sample -----------------------------------------------------------------------------------------------

fn sample() -> &'static SampleFile {
    static S: OnceLock<SampleFile> = OnceLock::new();
    S.get_or_init(|| {
        let text = std::fs::read_to_string(root().join(SAMPLE_PATH))
            .unwrap_or_else(|e| panic!("the checked-in sample {SAMPLE_PATH} must exist: {e}"));
        serde_json::from_str(&text).unwrap_or_else(|e| panic!("{SAMPLE_PATH} must parse: {e}"))
    })
}

#[test]
fn the_checked_in_sample_is_well_formed() {
    let f = sample();
    assert_eq!(f.version, 1);
    assert_eq!(f.tick, MINUTE5_TICK, "minute 5 at 20 Hz is tick 6000");
    assert_eq!(MINUTE5_TICK, 6000);
    assert_eq!(
        f.map, MAP_REL,
        "the map is the one source_soak and camp_bench use"
    );
    assert!(f.seeds[0] < f.seeds[1], "a non-empty seed range");
    // Every seed of the range either reached minute 5 and contributed builds, or is listed as ended early.
    let with_builds: BTreeSet<u64> = f.builds.iter().map(|b| b.seed).collect();
    let early: BTreeSet<u64> = f.ended_early.iter().copied().collect();
    for seed in f.seeds[0]..f.seeds[1] {
        assert!(
            with_builds.contains(&seed) != early.contains(&seed),
            "seed {seed} is either sampled or listed in ended_early, not both and not neither"
        );
    }
    assert!(with_builds
        .iter()
        .all(|s| (f.seeds[0]..f.seeds[1]).contains(s)));
    // Floor: enough builds for an average to mean something (8 bots per match; players dead by minute 5 may be absent).
    assert!(
        f.builds.len() >= 8,
        "at least 8 sampled builds, got {}",
        f.builds.len()
    );
    let keys: Vec<(u64, u32)> = f.builds.iter().map(|b| (b.seed, b.bot)).collect();
    let mut sorted = keys.clone();
    sorted.sort();
    sorted.dedup();
    assert_eq!(keys, sorted, "(seed, bot) order, each once");
    assert!(f.builds.iter().all(|b| b.bot < 8), "8 bots per soak match");
    let factions: BTreeSet<&str> = FactionId::ALL.iter().map(|f| f.as_str()).collect();
    assert!(f
        .builds
        .iter()
        .all(|b| factions.contains(b.faction.as_str())));
    // A minute-5 build is not empty: some build holds a card and some holds a stat stack.
    assert!(
        f.builds.iter().any(|b| !b.build.cards.is_empty()),
        "some sampled build holds cards"
    );
    assert!(
        f.builds.iter().any(|b| !b.build.stacks.is_empty()),
        "some sampled build holds normal-mob stat stacks"
    );
}

fn fighter_sim(faction: FactionId) -> Sim {
    let arena = Arc::new(load_arena(root().join(MAP_REL).to_str().unwrap()).unwrap());
    let cfg = SimConfig {
        min_players: 1,
        lobby_seconds: 5.,
        fill_seconds: 3.,
        ..SimConfig::live_content()
    };
    let placement = [Placement {
        profile: "fighter".into(),
        faction,
        bot: false,
        at: arena.centre(),
        hp: None,
        weapon0: Some(WeaponId::Sword),
        weapon1: None,
    }];
    Sim::reset_active(cfg, arena, Secrets::from_seed(1), &placement)
}

#[test]
fn every_sampled_build_is_accepted_by_trusted_build_apply_for_every_faction() {
    let f = sample();
    for b in &f.builds {
        for faction in FactionId::ALL {
            let mut sim = fighter_sim(faction);
            let me = sim.players()[0].id;
            let before = sim.players()[0].build.counts();
            apply(&mut sim, me, &b.build).unwrap_or_else(|e| {
                panic!(
                    "seed {} bot {} refused for {}: {e:?}",
                    b.seed,
                    b.bot,
                    faction.as_str()
                )
            });
            // The fighter really holds what the sample says: the requested copies were added on top of what it had.
            let p = &sim.players()[0];
            for &(card, n) in &b.build.cards {
                assert_eq!(
                    p.build.counts()[card.index()],
                    before[card.index()] + n,
                    "card {card:?} of seed {} bot {}",
                    b.seed,
                    b.bot
                );
            }
            for &(mob, n) in &b.build.stacks {
                assert_eq!(p.farm.count(mob), n, "stacks of {mob:?}");
            }
        }
    }
}

#[test]
fn the_sample_regenerates_identically_for_its_first_sampled_seed() {
    // One 6000-tick live match (seconds): the sampler is a pure function of (map, seed, tick).
    let f = sample();
    let seed = f.builds[0].seed;
    let again = sample_builds(
        root().join(MAP_REL).to_str().unwrap(),
        seed..seed + 1,
        f.tick,
    )
    .expect("the sampler runs");
    let theirs: Vec<_> = f
        .builds
        .iter()
        .filter(|b| b.seed == seed)
        .cloned()
        .collect();
    assert!(!theirs.is_empty());
    assert_eq!(again.builds, theirs, "same seed, same minute-5 builds");
    assert_eq!(again.tick, f.tick);
    assert!(
        again.ended_early.is_empty(),
        "the first sampled seed reaches minute 5"
    );
}

#[test]
#[ignore = "slow: replays every match of the sample (cargo test -p rh-headless --test elite_bench -- --ignored)"]
fn the_whole_sample_regenerates_identically() {
    let f = sample();
    let again = sample_builds(
        root().join(MAP_REL).to_str().unwrap(),
        f.seeds[0]..f.seeds[1],
        f.tick,
    )
    .expect("the sampler runs");
    assert_eq!(
        &again, f,
        "the checked-in file is exactly what the sampler writes"
    );
}

// ---- the elite run --------------------------------------------------------------------------------------------

/// The first zone's elites, as the quick run uses them.
fn quick_kinds() -> Vec<String> {
    elites_of(FactionId::ALL[0])
        .map(|t| t.id.as_str().to_string())
        .collect()
}
fn all_elite_ids() -> BTreeSet<String> {
    FactionId::ALL
        .into_iter()
        .flat_map(|z| elites_of(z).map(|t| t.id.as_str().to_string()))
        .collect()
}

const QUICK_SEEDS: u64 = 2;

fn quick_args(extra: &[&str]) -> Vec<String> {
    let mut a = args(&[
        "--quick",
        "--seed",
        "1",
        "--seeds",
        "2",
        "--max-ticks",
        TEST_TICKS,
    ]);
    a.extend(args(extra));
    a
}

/// The shared quick run: (json text, parsed json, table).
fn quick() -> &'static (String, Value, String) {
    static Q: OnceLock<(String, Value, String)> = OnceLock::new();
    Q.get_or_init(|| {
        let out =
            run(&quick_args(&[])).unwrap_or_else(|e| panic!("the quick elite run reports: {e}"));
        let v = serde_json::from_str(&out.json).expect("the report is one JSON document");
        (out.json, v, out.table)
    })
}

#[test]
fn the_quick_run_reports_a_complete_elite_inventory() {
    let (_, r, _) = quick();
    assert_eq!(r["version"], json!(1));
    assert_eq!(r["mode"], json!("elite"));
    assert_eq!(r["quick"], json!(true));
    assert_eq!(u(r, "max_ticks"), 400);
    // Quick = Frost,Holy x Sword,Wand x the first zone's 2 elites x 2 seeds; a seed that rolls no such camp is listed
    // in `skipped`, never silently dropped.
    let kinds = quick_kinds();
    assert_eq!(kinds.len(), 2, "catalog: 2 elites per zone");
    let expected = 2 * 2 * kinds.len() as u64 * QUICK_SEEDS;
    let cells = r["cells"].as_array().expect("cells");
    let skipped = r["skipped"].as_array().expect("skipped");
    assert_eq!(
        cells.len() as u64 + skipped.len() as u64,
        expected,
        "every faction x weapon x elite x seed is a cell or a skip"
    );
    assert!(!cells.is_empty(), "floor: at least one fight ran");
    // Order: faction, weapon, kind, seed (seed innermost), each exactly once.
    let key = |c: &Value| {
        (
            s(c, "faction").to_string(),
            s(c, "weapon").to_string(),
            s(c, "kind").to_string(),
            u(c, "seed"),
        )
    };
    let keys: Vec<_> = cells.iter().chain(skipped.iter()).map(key).collect();
    let mut sorted = keys.clone();
    sorted.sort();
    sorted.dedup();
    assert_eq!(sorted.len(), keys.len(), "each cell exactly once");
    let all = all_elite_ids();
    for c in cells {
        assert!(
            all.contains(s(c, "kind")),
            "{} is a catalog elite",
            s(c, "kind")
        );
        assert!(
            kinds.contains(&s(c, "kind").to_string()),
            "quick kinds only"
        );
        assert_eq!(c["elite"], json!(true));
        assert!(["died", "cleared", "truncated"].contains(&s(c, "outcome")));
        assert!(u(c, "ticks") <= 400);
        assert!(num(c, "hp_start") > 0.);
        let died = s(c, "outcome") == "died";
        assert_eq!(num(c, "hp_remaining") == 0., died, "hp 0 exactly when died");
        // A normal camp kind is never reported here.
        assert!(normal_types_of(FactionId::ALL[0]).all(|t| t.id.as_str() != s(c, "kind")));
    }
}

#[test]
fn the_report_states_the_rules_the_build_source_and_the_brawler_caveat() {
    let (_, r, table) = quick();
    assert_eq!(
        r["config"],
        json!("live_content"),
        "SimConfig::live_content()"
    );
    assert_eq!(r["rules"]["elites"], json!("rework"));
    assert_eq!(r["rules"]["augments"], json!("rework"));
    assert_eq!(
        r["builds"]["source"],
        json!(SAMPLE_PATH),
        "the default build source is the checked-in minute-5 sample"
    );
    assert_eq!(u(&r["builds"], "tick"), 6000);
    assert!(u(&r["builds"], "count") >= 8);
    let caveat = s(r, "caveat");
    assert!(
        caveat.contains("Brawler"),
        "plan decision 8: the report notes its fighter is the scripted Brawler, which may skew death rates"
    );
    assert!(
        table.contains("Brawler"),
        "the readable table carries the same note"
    );
    // The elite bench builds its world from live_content (the file is the proof of which config it asks for).
    let src = include_str!("../src/elite_bench.rs");
    assert!(
        src.contains("SimConfig::live_content()"),
        "elite mode starts from SimConfig::live_content()"
    );
}

#[test]
fn death_rates_are_consistent_per_camp_overall_and_in_the_table() {
    let (_, r, table) = quick();
    let cells = r["cells"].as_array().unwrap();
    let per_camp = r["per_camp"].as_array().expect("per_camp");
    let kinds = quick_kinds();
    assert_eq!(
        per_camp.len(),
        kinds.len(),
        "one row per elite camp kind, in catalog order"
    );
    let (mut samples, mut deaths) = (0u64, 0u64);
    for (row, kind) in per_camp.iter().zip(&kinds) {
        assert_eq!(s(row, "kind"), kind);
        let mine: Vec<&Value> = cells.iter().filter(|c| s(c, "kind") == kind).collect();
        let n = u(row, "samples");
        let d = u(row, "deaths");
        assert_eq!(n, mine.len() as u64, "samples counts the fights of {kind}");
        assert_eq!(
            d,
            mine.iter().filter(|c| s(c, "outcome") == "died").count() as u64
        );
        assert!(d <= n);
        if n > 0 {
            assert!((num(row, "death_rate") - d as f64 / n as f64).abs() < 1e-12);
        } else {
            assert!(row["death_rate"].is_null(), "no samples, no rate");
        }
        samples += n;
        deaths += d;
        // The table has a line for the camp with its sample count and its rate as a percentage.
        let line = table
            .lines()
            .find(|l| l.contains(kind.as_str()))
            .unwrap_or_else(|| panic!("the table names {kind}:\n{table}"));
        assert!(
            line.split_whitespace().any(|w| w == n.to_string()),
            "table line for {kind} carries its sample count {n}: {line}"
        );
        if n > 0 {
            let pct = format!("{:.1}%", 100. * d as f64 / n as f64);
            assert!(
                line.contains(&pct),
                "table line for {kind} carries {pct}: {line}"
            );
        }
    }
    let o = &r["overall"];
    assert_eq!(u(o, "samples"), samples);
    assert_eq!(u(o, "deaths"), deaths);
    assert_eq!(samples, cells.len() as u64);
    assert!(samples >= 1);
    let pct = format!("{:.1}%", 100. * deaths as f64 / samples as f64);
    let overall = table
        .lines()
        .find(|l| l.to_lowercase().contains("overall"))
        .unwrap_or_else(|| panic!("the table has an overall line:\n{table}"));
    assert!(
        overall.contains(&pct) && overall.contains(&samples.to_string()),
        "{overall}"
    );
    // Solo: one fighter per fight.
    assert_eq!(r["solo"], json!(true), "every sample is a solo fight");
}

#[test]
fn the_run_is_deterministic() {
    let again = run(&quick_args(&[])).expect("runs");
    let (json, _, table) = quick();
    assert_eq!(&again.json, json, "same seed and flags give identical JSON");
    assert_eq!(&again.table, table);
}

#[test]
fn builds_are_applied_before_the_first_step_and_the_fighter_walks_to_the_camp() {
    // Trap (coordinator, integration review): `trusted_build::apply` leaves Plant Your Feet's stillness clock
    // (`card_still_since`) at 0, so a holder granted the card right before a fight would plant its first swing at once.
    // The chosen design: apply at tick 0 and let the fighter walk the approach (the walk resets the clock every moving
    // tick, cards/mod.rs:167), so the opening swing is the same as in a played match. The report pins the pieces.
    let (_, r, _) = quick();
    for c in r["cells"].as_array().unwrap() {
        assert_eq!(u(c, "build_applied_tick"), 0, "applied before any step");
        let d = num(c, "start_dist");
        assert!(
            (128. ..=320.).contains(&d),
            "the fighter starts 4 to 10 tiles away and walks in (camp_bench START_DIST 320, fallbacks 256/192/128): {d}"
        );
        match c["first_hit_tick"].as_u64() {
            Some(t) => assert!(
                t >= 1,
                "no hit on the very first tick: the approach comes first"
            ),
            None => assert!(c["first_hit_tick"].is_null()),
        }
    }
}

#[test]
fn each_cell_uses_the_documented_build_and_a_bare_control_differs() {
    let (_, r, _) = quick();
    let n = u(&r["builds"], "count");
    let cells = r["cells"].as_array().unwrap();
    // Build choice is a pure function of the cell's place in the inventory (faction, weapon, kind, seed order, skips
    // included): ordinal % count. Rebuild the ordinal from the same listing.
    let mut all: Vec<&Value> = cells
        .iter()
        .chain(r["skipped"].as_array().unwrap().iter())
        .collect();
    let order = |v: &&Value| {
        let f = FactionId::ALL
            .iter()
            .position(|f| f.as_str() == s(v, "faction"))
            .unwrap();
        let w = WeaponId::ALL
            .iter()
            .position(|w| w.as_str() == s(v, "weapon"))
            .unwrap();
        let k = quick_kinds()
            .iter()
            .position(|k| k == s(v, "kind"))
            .unwrap();
        (f, w, k, u(v, "seed"))
    };
    all.sort_by_key(order);
    for (ordinal, c) in all.iter().enumerate() {
        if c.get("build_index").is_some() && !c["build_index"].is_null() {
            assert_eq!(
                u(c, "build_index"),
                ordinal as u64 % n,
                "build of cell {ordinal}"
            );
        }
    }
    // Negative control: the same fights with no build change what the fighter deals somewhere.
    let bare = run(&quick_args(&["--control-bare"])).expect("runs");
    let bare: Value = serde_json::from_str(&bare.json).unwrap();
    assert_eq!(bare["control"], json!("bare"));
    let sum = |v: &Value| -> f64 {
        v["cells"]
            .as_array()
            .unwrap()
            .iter()
            .map(|c| num(c, "damage_dealt"))
            .sum()
    };
    assert!(
        (sum(&bare) - sum(r)).abs() > 1e-9,
        "a minute-5 build changes the damage the fighter deals (the build is applied, not ignored)"
    );
}

#[test]
fn a_run_that_reports_exits_ok_even_when_nothing_finishes() {
    // One tick: every fight is cut off. The bench is evidence, not a gate: it still reports.
    let out = run(&args(&["--quick", "--seeds", "1", "--max-ticks", "1"]))
        .expect("Ok whenever it reported");
    let r: Value = serde_json::from_str(&out.json).unwrap();
    assert!(r["cells"]
        .as_array()
        .unwrap()
        .iter()
        .all(|c| s(c, "outcome") == "truncated"));
    assert_eq!(u(&r["overall"], "deaths"), 0);
    // Bad arguments are the one refusal.
    assert!(run(&args(&["--quick", "--seeds", "x"])).is_err());
    assert!(run(&args(&["--quick", "--builds", "/no/such/file.json"])).is_err());
}

#[test]
fn the_binary_exits_zero_with_the_feature_and_says_so_without_it() {
    let out = Command::new(env!("CARGO_BIN_EXE_camp_bench"))
        .args(["--elite", "--quick", "--seeds", "1", "--max-ticks", "50"])
        .current_dir(root())
        .output()
        .expect("camp_bench starts");
    if cfg!(feature = "elite-bench") {
        assert!(
            out.status.success(),
            "exit 0 once it reported: {}",
            String::from_utf8_lossy(&out.stderr)
        );
        let v: Value = serde_json::from_slice(&out.stdout).expect("stdout is one JSON report");
        assert_eq!(v["mode"], json!("elite"));
        assert!(!out.stderr.is_empty(), "the readable table goes to stderr");
    } else {
        assert_eq!(out.status.code(), Some(2));
        assert!(String::from_utf8_lossy(&out.stderr).contains("elite-bench"));
    }
}

// ---- measurement only: today's non-elite output is unchanged ---------------------------------------------------

#[test]
fn the_non_elite_camp_bench_output_is_byte_identical_to_before() {
    // Goldens produced by the camp_bench of the integration head ec6d2c2a9 (before this packet).
    let run_raw = |a: &[&str]| {
        let out = Command::new(env!("CARGO_BIN_EXE_camp_bench"))
            .args(a)
            .output()
            .expect("camp_bench starts");
        assert!(out.status.success());
        String::from_utf8(out.stdout).unwrap()
    };
    let fx = |n: &str| {
        std::fs::read_to_string(
            PathBuf::from(env!("CARGO_MANIFEST_DIR"))
                .join("tests/fixtures")
                .join(n),
        )
        .unwrap()
    };
    assert_eq!(
        run_raw(&["--list", "--quick", "--seed", "1"]),
        fx("camp_bench_quick_list.json")
    );
    assert_eq!(
        run_raw(&["--quick", "--seed", "1", "--max-ticks", "400"]),
        fx("camp_bench_quick_400.json"),
        "the quick fights (legacy and archetypes, no build) are unchanged by the elite mode"
    );
}
