//! `check_world` rules for the match's step length (`SimConfig::tick_ms`, ADR 0029, P-TICK-1a). The public round trips
//! and refusals are `tests/tick_ms.rs`; these check the decoded state itself, which another serde format or a future
//! constructor would reach without the JSON deserializer's refusal.

use crate::arenas::practice_arena;
use crate::constants::TICK_MS;
use crate::sim::{Secrets, Sim, SimConfig};

fn lobby() -> Sim {
    Sim::new(
        SimConfig::default(),
        practice_arena(),
        Secrets::from_seed(1),
    )
}

#[test]
fn an_unsupported_step_length_is_refused_by_check_world() {
    for bad in [0, 1, 24, 33, 40, 75, 100, u32::MAX] {
        let mut a = lobby();
        a.k.config.tick_ms = bad;
        assert!(super::check_world(&a.k, &a.w).is_err(), "{bad} ms");
    }
    for ok in [25, 50] {
        let mut a = lobby();
        a.k.config.tick_ms = ok;
        assert!(super::check_world(&a.k, &a.w).is_ok(), "{ok} ms");
    }
}

#[test]
fn the_clock_bound_follows_the_match_step() {
    // At 25 ms the tick counter reaches half the clock it reaches at 50.
    let mut a = lobby();
    a.k.config.tick_ms = 25;
    a.w.server_time = u32::MAX as f64 * 25.0;
    assert!(super::check_world(&a.k, &a.w).is_ok());
    a.w.server_time = u32::MAX as f64 * TICK_MS;
    assert!(super::check_world(&a.k, &a.w).is_err());
}
