//! Oracle for packet P-BAL-LAB-1 (docs/backlog/packets/P-BAL-LAB-1.md): `balance_lab`, the headless measurement tool.
//!
//! Loose proof (owner direction 2026-11-10): nothing here pins a balance number. What is pinned is that the observer
//! changes nothing (the watched match ends in the state hash of an unwatched one), that the report is byte-identical for
//! any `--jobs`, that its counters are consistent, that it is not empty, and that the seat flag really changes what a
//! seat can quest. Debug-build sized: seeds 1..4 end after roughly 900 to 1400 ticks with human seats.
use rh_headless::balance_lab::{
    match_setup, parse_args, play_seed, run, MapChoice, Options, SeatPolicy, SCHEMA, SEATS,
};
use rh_headless::Env;
use serde_json::Value;
use std::sync::OnceLock;

/// Never reached on seeds 1..4 with human seats; keeps a stray long match inside the debug budget.
const MAX_TICKS: u32 = 2400;

fn opts(jobs: usize, human_seats: bool) -> Options {
    Options {
        seeds: 1..4,
        jobs,
        max_ticks: MAX_TICKS,
        human_seats,
        ..Options::default()
    }
}

/// The match a plain `Env` plays for `seed`: the lab's own setup, no observer. Returns (ticks, state hash).
fn plain(opts: &Options, seed: u64) -> (u32, u64) {
    let (cfg, agents) = match_setup(opts, seed).expect("setup");
    let mut env = Env::new(cfg, agents, seed);
    let mut ticks = 0;
    loop {
        let p = env.advance(&[]);
        ticks += 1;
        if p.terminated || p.truncated {
            return (ticks, env.sim.state_hash());
        }
    }
}

fn report(o: &Options) -> String {
    run(o).expect("lab run").json
}

/// Seeds 1..4, human seats, one thread (cached: several tests read it).
fn human_jobs1() -> &'static String {
    static R: OnceLock<String> = OnceLock::new();
    R.get_or_init(|| report(&opts(1, true)))
}
fn human_jobs3() -> &'static String {
    static R: OnceLock<String> = OnceLock::new();
    R.get_or_init(|| report(&opts(3, true)))
}
/// Seed 1 only, bot seats.
fn bot_seed1() -> &'static String {
    static R: OnceLock<String> = OnceLock::new();
    R.get_or_init(|| {
        report(&Options {
            seeds: 1..2,
            ..opts(1, false)
        })
    })
}

fn json(text: &str) -> Value {
    serde_json::from_str(text).expect("the report is one JSON object")
}
fn arr<'a>(v: &'a Value, k: &str) -> &'a Vec<Value> {
    v[k].as_array()
        .unwrap_or_else(|| panic!("`{k}` is missing or not an array in {v}"))
}
fn n(v: &Value, k: &str) -> u64 {
    v[k].as_u64()
        .unwrap_or_else(|| panic!("`{k}` is missing or not a number in {v}"))
}

#[test]
fn the_observer_changes_nothing_the_watched_match_ends_in_the_unwatched_state_hash() {
    for (human_seats, seeds) in [(true, 1..4u64), (false, 1..2u64)] {
        let o = opts(1, human_seats);
        for seed in seeds {
            let (ticks, hash) = plain(&o, seed);
            let watched = play_seed(&o, seed).expect("observed play");
            assert_eq!(watched.seed, seed);
            assert_eq!(
                watched.state_hash, hash,
                "seed {seed} human_seats {human_seats}: the observer moved the final state hash"
            );
            assert_eq!(
                watched.ticks, ticks,
                "seed {seed} human_seats {human_seats}: the observed match ran a different length"
            );
            assert!(
                watched.terminated || watched.truncated,
                "seed {seed}: the observed play must run to the match end"
            );
        }
    }
}

#[test]
fn the_report_bytes_do_not_depend_on_jobs() {
    let (one, three) = (human_jobs1(), human_jobs3());
    assert!(
        one.contains(SCHEMA) && one.contains("\"quests\":") && one.contains("\"world\":"),
        "an empty report is not a report: {one}"
    );
    assert_eq!(one, three, "--jobs 1 and --jobs 3 gave different bytes");
    // The report holds no wall clock: a second run in this process is byte-identical too.
    assert_eq!(one, &report(&opts(2, true)), "--jobs 2 differs");
}

#[test]
fn the_report_has_its_schema_key_order_hash_and_per_seed_records() {
    let text = human_jobs1();
    // Declaration order of derived-Serialize structs; a `json!` map would sort the keys.
    let keys = [
        "schema",
        "content_hash",
        "seeds",
        "seating",
        "maps",
        "world",
        "elites",
        "quests",
        "rewards",
        "augments",
    ];
    let mut last = 0;
    for k in keys {
        let at = text
            .find(&format!("\"{k}\":"))
            .unwrap_or_else(|| panic!("top-level key {k} missing"));
        assert!(at >= last, "top-level key {k} is out of order");
        last = at;
    }
    let r = json(text);
    assert_eq!(r["schema"], SCHEMA);
    assert_eq!(
        r["content_hash"],
        format!("{:016x}", rh_sim::content::content_hash()),
        "the report names the content it measured"
    );
    // Seed order, one record each, and the report's hash is the unwatched hash.
    let seeds = arr(&r, "seeds");
    assert_eq!(seeds.len(), 3);
    let maps = arr(&r, "maps");
    assert_eq!(maps.len(), 3, "one admitted map per seed");
    for (i, seed) in (1..4u64).enumerate() {
        assert_eq!(n(&seeds[i], "seed"), seed, "seeds are folded in seed order");
        assert_eq!(n(&maps[i], "seed"), seed);
        let hash = maps[i]["map_hash"].as_str().unwrap_or_default();
        assert!(!hash.is_empty(), "seed {seed}: no admitted map_hash");
        let (ticks, state) = plain(&opts(1, true), seed);
        assert_eq!(n(&seeds[i], "ticks"), u64::from(ticks));
        assert_eq!(
            seeds[i]["state_hash"],
            format!("{state:016x}"),
            "seed {seed}: the report's state hash is the unwatched one"
        );
    }
    let seating = arr(&r, "seating");
    assert_eq!(seating.len(), SEATS);
    assert!(
        seating.iter().all(|s| s["bot"] == false),
        "--human-seats seats bot:false"
    );
}

#[test]
fn counters_are_consistent() {
    let r = json(human_jobs1());
    for q in arr(&r["quests"], "by_quest") {
        let name = q["quest"].as_str().expect("quest name");
        let attempts = n(q, "attempts");
        let ended =
            n(q, "completed") + n(q, "ended_timer") + n(q, "ended_death") + n(q, "abandoned");
        assert!(
            ended <= attempts,
            "{name}: {ended} ended attempts of {attempts}"
        );
        assert!(
            n(q, "accepted") <= n(q, "dealt"),
            "{name}: accepted more than dealt"
        );
        assert!(
            n(q, "accepted") <= attempts,
            "{name}: fewer attempts than acceptances"
        );
    }
    for c in arr(&r["augments"], "by_card") {
        let name = c["card"].as_str().expect("card name");
        assert!(
            n(c, "picked") <= n(c, "offered"),
            "{name}: picked more than offered"
        );
    }
    let deaths = &r["world"]["player_deaths"];
    let by_cause = &deaths["by_cause"];
    let sum: u64 = ["player", "elite", "mob", "hazard"]
        .iter()
        .map(|k| n(by_cause, k))
        .sum();
    assert_eq!(
        sum,
        n(deaths, "total"),
        "death causes must add up to the total"
    );
}

#[test]
fn the_world_block_is_not_empty() {
    let r = json(human_jobs1());
    let camps = n(&r["world"], "camps_cleared");
    let deaths = n(&r["world"]["player_deaths"], "total");
    assert!(
        camps + deaths >= 1,
        "three human-seat matches reported no cleared camp and no death"
    );
}

#[test]
fn human_seats_can_be_dealt_quests_and_bot_seats_cannot() {
    let human = json(human_jobs1());
    let dealt: u64 = arr(&human["quests"], "by_quest")
        .iter()
        .map(|q| n(q, "dealt"))
        .sum();
    // Measured: every human seat sees 10 rows from the start of the raid (quest_view/mod.rs:203 gives a bot seat none).
    assert!(
        dealt >= (3 * SEATS) as u64,
        "human seats were dealt only {dealt} quests over 3 seeds"
    );
    let bots = json(bot_seed1());
    assert!(
        arr(&bots, "seating").iter().all(|s| s["bot"] == true),
        "the default seats are bot:true"
    );
    let dealt_to_bots: u64 = arr(&bots["quests"], "by_quest")
        .iter()
        .map(|q| n(q, "dealt"))
        .sum();
    assert_eq!(
        dealt_to_bots, 0,
        "a bot seat has no quest view: nothing can be dealt"
    );
}

#[test]
fn the_command_line_parses_and_refuses() {
    fn a(s: &[&str]) -> Vec<String> {
        s.iter().map(|x| x.to_string()).collect()
    }
    let d = parse_args(&[]).expect("no arguments is the default run");
    assert_eq!(d.seeds, 1..2);
    assert_eq!(d.jobs, 1);
    assert!(!d.human_seats);
    assert_eq!(d.map, MapChoice::Generate);
    assert!(d.seats.is_empty());

    let o = parse_args(&a(&[
        "--seeds",
        "2..5",
        "--jobs",
        "4",
        "--max-ticks",
        "100",
        "--map",
        "some/map.json",
        "--seat",
        "3=brawler",
        "--seat",
        "5=human",
        "--human-seats",
    ]))
    .expect("a full command line");
    assert_eq!(o.seeds, 2..5);
    assert_eq!(o.jobs, 4);
    assert_eq!(o.max_ticks, 100);
    assert_eq!(o.map, MapChoice::File("some/map.json".to_string()));
    assert_eq!(
        o.seats,
        vec![(3, SeatPolicy::Brawler), (5, SeatPolicy::Human)]
    );
    assert!(o.human_seats);
    assert_eq!(
        parse_args(&a(&["--map", "generate"])).unwrap().map,
        MapChoice::Generate
    );

    for bad in [
        &["--jobs", "0"][..],
        &["--seeds", "5..2"],
        &["--seeds", "3"],
        &["--seat", "8=human"],
        &["--seat", "1=nope"],
        &["--max-ticks", "x"],
        &["--nonsense"],
    ] {
        assert!(
            parse_args(&a(bad)).is_err(),
            "{bad:?} must be a usage error"
        );
    }
}
