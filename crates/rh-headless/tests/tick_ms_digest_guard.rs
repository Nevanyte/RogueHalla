//! P-TICK-1a digest guard (ADR 0029): making the step length a per-sim setting must leave a 50 ms sim byte-for-byte as it
//! was. The values below were pinned on `packet/P-TICK-1a` from the code before the change (the stub adds an inert field
//! only); an implementation that moves any of them changed the 50 ms world, the state hash or the statics digest.
use rh_headless::{load_arena, run_bot_match_with};
use rh_sim::SimConfig;

const SEED: u64 = 1;
const PLAYERS: usize = 8;
const TICKS: u32 = 4000;

/// The seed-1 headless run (the README's `rh-headless --players 8 --map parity/maps/seed1.map.json`, seed 1) at tick 4000.
const PINNED_STATE_HASH: u64 = 0x2b20_ce9e_ab4e_809a;
/// The same run's `OutcomeDigest` (a behaviour fingerprint that does not read the state hash).
const PINNED_OUTCOME_DIGEST: u64 = 0x8c87_a634_c4ab_c2b3;

#[test]
fn the_seed_1_headless_run_at_50_ms_keeps_its_state_hash() {
    assert_eq!(
        SimConfig::default().tick_ms,
        50,
        "the default step stays 50 ms"
    );
    let arena = load_arena(concat!(
        env!("CARGO_MANIFEST_DIR"),
        "/../../parity/maps/seed1.map.json"
    ))
    .expect("seed-1 map");
    let (env, summary) = run_bot_match_with(&arena, PLAYERS, SEED, TICKS, false, true);
    assert_eq!(
        summary.ticks, TICKS,
        "the run is still going at tick {TICKS}"
    );
    assert!(!summary.terminated);
    assert_eq!(env.sim.tick_ms(), 50);
    // The env's first step is the sim's tick 1 after construction; the clock is still exactly tick * 50.
    assert_eq!(env.sim.tick(), TICKS + 1);
    assert_eq!(env.sim.server_time(), f64::from(env.sim.tick()) * 50.0);
    assert_eq!(
        summary.state_hash, PINNED_STATE_HASH,
        "state hash is now {:#018x}",
        summary.state_hash
    );
    assert_eq!(
        summary.outcome_digest,
        Some(PINNED_OUTCOME_DIGEST),
        "outcome digest is now {:#018x?}",
        summary.outcome_digest
    );
}
