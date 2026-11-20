//! P-TICK-2 (ADR 0029): a 25 ms bot match makes the same number of decisions per bot per second as a 50 ms one. A loose
//! proof of the conversion (decision intervals are legacy 50 ms ticks, converted with the sim's step), not a parity run.
use rh_headless::{load_arena, run_bot_match_kind, BotKind};
use rh_sim::SimConfig;

const PLAYERS: usize = 8;
const SECONDS: u32 = 120;

/// Decisions per bot per second of sim time, and the step count, of a seed-1 match at `tick_ms`.
fn decisions_per_bot_second(kind: BotKind, tick_ms: u32) -> f64 {
    let arena = load_arena(concat!(
        env!("CARGO_MANIFEST_DIR"),
        "/../../parity/maps/seed1.map.json"
    ))
    .expect("seed-1 map");
    let (env, summary) = run_bot_match_kind(
        &arena,
        SimConfig {
            tick_ms,
            ..SimConfig::default()
        },
        PLAYERS,
        1,
        SECONDS * 1000 / tick_ms,
        false,
        false,
        kind,
    );
    assert_eq!(env.sim.tick_ms(), tick_ms);
    let seconds = f64::from(summary.ticks) * f64::from(tick_ms) / 1000.0;
    assert!(seconds >= 20.0, "the match ran only {seconds} s");
    let stats = env.seat_stats();
    assert_eq!(stats.len(), PLAYERS);
    assert!(
        stats.iter().all(|(_, _, fault)| fault.is_none()),
        "a seat faulted"
    );
    let total: u64 = stats.iter().map(|(_, s, _)| s.decisions).sum();
    total as f64 / PLAYERS as f64 / seconds
}

#[test]
fn bots_decide_the_same_number_of_times_per_second_at_25_and_50_ms() {
    for kind in [BotKind::Brawler, BotKind::Human] {
        let at50 = decisions_per_bot_second(kind, 50);
        let at25 = decisions_per_bot_second(kind, 25);
        assert!(at50 > 1.0, "{kind:?}: {at50} decisions per bot second");
        assert!(
            (at50 - at25).abs() <= 1.0,
            "{kind:?}: {at50} decisions per bot second at 50 ms, {at25} at 25 ms"
        );
    }
}
