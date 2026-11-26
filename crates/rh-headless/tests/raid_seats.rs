//! Oracle for packet P-RAID-H1 (docs/backlog/packets/P-RAID-H1.md): `RaidBot` seated by `rh-headless` in human seats, and
//! run by `balance_lab`.
//!
//! Loose proof (owner direction 2026-11-10): nothing here pins a number of the bot's play beyond a floor far under what
//! was measured. What is pinned: `raid` parses; `run_bot_match_kind` still seats `bot: true`; eight RaidBots in human seats
//! on the lab's setup reach `Active` without fault, fallback or rejected command, clear camps, and repeat exactly; the
//! seats really are `RaidBot` with `Human`'s tier/persona draw; the lab accepts and reports `raid`.
//! Debug build: about a minute (every run is capped by `max_ticks`).
use rh_headless::balance_lab::{match_setup, parse_args, run, Options, SeatPolicy, SEATS};
use rh_headless::{
    load_arena, run_bot_match_kind, run_bot_match_seated, Agent, AgentSpec, BotKind, Env, EnvConfig,
};
use rh_policy::SeatStats;
use rh_sim::{Event, FactionId, Phase, SimConfig};

const MAX_TICKS: u32 = 2400;
/// A tick after the lobby and planning (Active from tick ~145-162 on seeds 1-3) with room for real play.
const SHORT_TICKS: u32 = 900;

fn raid_lab_options(seeds: std::ops::Range<u64>, max_ticks: u32) -> Options {
    Options {
        seeds,
        max_ticks,
        human_seats: true,
        seats: (0..SEATS).map(|i| (i, SeatPolicy::Raid)).collect(),
        ..Options::default()
    }
}

/// What one lab-setup match of eight RaidBots showed.
struct Run {
    ticks: u32,
    state_hash: u64,
    stats: Vec<SeatStats>,
    faults: usize,
    all_human_seats: bool,
    reached_active: bool,
    camps_cleared: usize,
}

fn lab_run(seed: u64, max_ticks: u32) -> Run {
    let (cfg, agents) =
        match_setup(&raid_lab_options(seed..seed + 1, max_ticks), seed).expect("setup");
    drive(Env::new(cfg, agents, seed))
}

fn drive(mut env: Env) -> Run {
    let (mut ticks, mut camps_cleared, mut reached_active) = (0, 0, false);
    let all_human_seats = env.sim.players().iter().all(|p| !p.seat.bot);
    loop {
        let p = env.advance(&[]);
        ticks += 1;
        reached_active |= env.sim.phase() == Phase::Active;
        camps_cleared += env
            .last_events()
            .iter()
            .filter(|e| matches!(e, Event::CampCleared { .. }))
            .count();
        if p.terminated || p.truncated {
            break;
        }
    }
    let seats = env.seat_stats();
    Run {
        ticks,
        state_hash: env.sim.state_hash(),
        faults: seats.iter().filter(|(_, _, f)| f.is_some()).count(),
        stats: seats.into_iter().map(|(_, s, _)| s).collect(),
        all_human_seats,
        reached_active,
        camps_cleared,
    }
}

#[test]
fn raid_parses_and_the_other_kinds_still_do() {
    assert_eq!(BotKind::parse("raid"), Some(BotKind::Raid));
    assert_eq!(BotKind::parse("brawler"), Some(BotKind::Brawler));
    assert_eq!(BotKind::parse("human"), Some(BotKind::Human));
    assert_eq!(BotKind::parse("brain"), Some(BotKind::Brain));
    assert_eq!(BotKind::parse("nope"), None);
}

/// Control, green on the stub: `run_bot_match_kind` seats `bot: true` for every kind, and the new entry point with
/// `bot_seats: true` is the same match.
#[test]
fn run_bot_match_kind_still_seats_bot_seats() {
    let arena = load_arena(concat!(
        env!("CARGO_MANIFEST_DIR"),
        "/../../parity/maps/seed1.map.json"
    ))
    .expect("seed-1 map");
    for kind in [BotKind::Brawler, BotKind::Human, BotKind::Raid] {
        let (env, summary) =
            run_bot_match_kind(&arena, SimConfig::default(), 8, 1, 200, false, false, kind);
        assert_eq!(env.sim.players().len(), 8);
        assert!(
            env.sim.players().iter().all(|p| p.seat.bot),
            "{kind:?}: every seat of run_bot_match_kind is bot: true"
        );
        let (_, seated) = run_bot_match_seated(
            &arena,
            SimConfig::default(),
            8,
            1,
            200,
            false,
            false,
            kind,
            true,
        );
        assert_eq!(
            summary.state_hash, seated.state_hash,
            "{kind:?}: bot_seats: true is run_bot_match_kind"
        );
    }
}

#[test]
fn eight_raid_bots_in_human_seats_play_a_real_match() {
    let runs: Vec<(u64, Run)> = std::thread::scope(|scope| {
        let handles: Vec<_> = (1..=3u64)
            .map(|seed| (seed, scope.spawn(move || lab_run(seed, MAX_TICKS))))
            .collect();
        handles
            .into_iter()
            .map(|(seed, h)| (seed, h.join().expect("match thread")))
            .collect()
    });
    let mut camps = 0;
    for (seed, r) in &runs {
        assert!(r.all_human_seats, "seed {seed}: every seat is bot: false");
        assert!(
            r.reached_active,
            "seed {seed}: the lobby and planning end in Active"
        );
        assert!(
            r.ticks > 300,
            "seed {seed}: the match ran only {} ticks",
            r.ticks
        );
        assert_eq!(r.stats.len(), SEATS);
        assert_eq!(r.faults, 0, "seed {seed}: a seat faulted");
        for (i, s) in r.stats.iter().enumerate() {
            assert_eq!(s.fallback_decisions, 0, "seed {seed} seat {i}: fell back");
            assert_eq!(
                s.rejected, 0,
                "seed {seed} seat {i}: a command was rejected"
            );
            assert!(s.proposed > 0, "seed {seed} seat {i}: proposed nothing");
        }
        camps += r.camps_cleared;
    }
    // Measured 6, 9 and 9 camps by tick 2400; a quarter of 24 is the floor.
    assert!(camps >= 6, "only {camps} camps cleared over seeds 1-3");
}

#[test]
fn a_raid_match_repeats_exactly() {
    let (a, b) = std::thread::scope(|scope| {
        let a = scope.spawn(|| lab_run(1, SHORT_TICKS));
        let b = scope.spawn(|| lab_run(1, SHORT_TICKS));
        (a.join().expect("run a"), b.join().expect("run b"))
    });
    assert_eq!(a.ticks, b.ticks);
    assert_eq!(a.state_hash, b.state_hash);
    assert_eq!(a.stats, b.stats);
}

/// The seats are `RaidBot` with `Human`'s tier and persona draw, however they are reached: the lab's setup, the new
/// headless entry point, and eight `RaidBot::new(tier, persona)` built by hand all end in one state hash.
#[test]
fn the_seats_are_raid_bots_with_the_human_draw() {
    let seed = 1;
    let opts = raid_lab_options(seed..seed + 1, SHORT_TICKS);
    let lab = lab_run(seed, SHORT_TICKS);

    let (cfg, _) = match_setup(&opts, seed).expect("setup");
    let by_hand: Vec<AgentSpec> = (0..SEATS)
        .map(|i| {
            let (tier, persona) = rh_policy::bots::assign_seats(seed, SEATS)[i];
            AgentSpec {
                profile: format!("bot{i}"),
                faction: FactionId::ALL[(i + seed as usize) % 5],
                bot: false,
                agent: Agent::Policy {
                    policy: Box::new(rh_policy::bots::RaidBot::new(tier, persona)),
                },
            }
        })
        .collect();
    let hand = drive(Env::new(
        EnvConfig {
            arena: cfg.arena.clone(),
            sim: cfg.sim.clone(),
            max_ticks: SHORT_TICKS,
            record: false,
        },
        by_hand,
        seed,
    ));
    assert_eq!(
        lab.state_hash, hand.state_hash,
        "the lab seats RaidBot::new(tier, persona)"
    );

    let (env, summary) = run_bot_match_seated(
        &cfg.arena,
        cfg.sim.clone(),
        SEATS,
        seed,
        SHORT_TICKS,
        false,
        false,
        BotKind::Raid,
        false,
    );
    assert!(
        env.sim.players().iter().all(|p| !p.seat.bot),
        "run_bot_match_seated(.., bot_seats: false) seats bot: false"
    );
    assert_eq!(
        summary.state_hash, hand.state_hash,
        "the headless entry point plays the same match"
    );
}

#[test]
fn the_lab_runs_raid_seats() {
    let args = |extra: &[&str]| -> Vec<String> { extra.iter().map(|s| s.to_string()).collect() };
    // 2400 ticks, not 1200: since P-RAID-Q1 a raid seat takes a quest in the first minute, so seed 1 clears no camp
    // before tick 1200 (measured on integration 84: 0 by 1200, 3 by 2400, 5 by 3600). The expectation is unchanged.
    let mut line = vec!["--human-seats", "--seeds", "1..2", "--max-ticks", "2400"];
    let seats: Vec<String> = (0..SEATS).map(|i| format!("{i}=raid")).collect();
    for s in &seats {
        line.push("--seat");
        line.push(s);
    }
    let o = parse_args(&args(&line)).expect("--seat I=raid is accepted");
    assert_eq!(o.seats.len(), SEATS);
    assert!(o.seats.iter().all(|(_, p)| *p == SeatPolicy::Raid));
    assert!(parse_args(&args(&["--seat", "0=nope"])).is_err());

    let report: serde_json::Value =
        serde_json::from_str(&run(&o).expect("lab run").json).expect("json");
    let seating = report["seating"].as_array().expect("seating block");
    assert_eq!(seating.len(), SEATS);
    for seat in seating {
        assert_eq!(seat["policy"], "raid");
        assert_eq!(seat["bot"], false);
    }
    assert!(
        report["world"]["camps_cleared"].as_u64().unwrap_or(0) >= 1,
        "the world block is empty: {}",
        report["world"]
    );
}
