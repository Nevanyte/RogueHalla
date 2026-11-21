//! P-TICK-5: the rewind window follows the room's step (ADR 0029).

use super::*;

/// The window adds one step of the room's phase and never goes below one step: at 50 ms it is exactly what
/// [`allowed_rewind_ms`] gives; at 25 ms the step term and the floor are 25.
#[test]
fn the_rewind_window_adds_and_floors_at_one_step_of_the_room() {
    // 50 ms: unchanged.
    for rtt in [-1_000.0, 0.0, 20.0, 60.0, 80.0, 2_000.0, f64::NAN] {
        assert_eq!(
            allowed_rewind_ms_at(rtt, 50).to_bits(),
            allowed_rewind_ms(rtt).to_bits(),
            "rtt {rtt} at 50 ms"
        );
    }
    assert_eq!(allowed_rewind_ms_at(-1_000.0, 50), 50.0, "floored at 50");
    assert_eq!(allowed_rewind_ms_at(20.0, 50), 240.0, "20 + 150 + 50 + 20");

    // 25 ms: one step is 25.
    assert_eq!(allowed_rewind_ms_at(-1_000.0, 25), 25.0, "floored at 25");
    assert_eq!(allowed_rewind_ms_at(20.0, 25), 215.0, "20 + 150 + 25 + 20");
    assert_eq!(allowed_rewind_ms_at(60.0, 25), 255.0, "60 + 150 + 25 + 20");
    assert_eq!(allowed_rewind_ms_at(2_000.0, 25), MAX_REWIND_MS, "capped");
    assert_eq!(
        allowed_rewind_ms_at(f64::NAN, 25),
        295.0,
        "the default RTT: 100 + 150 + 25 + 20"
    );

    // The estimator gives the same window at its room's step.
    let mut rtt = RttEstimator::new();
    rtt.sample(0.0);
    assert_eq!(rtt.allowed_rewind_ms_at(50), 240.0);
    assert_eq!(rtt.allowed_rewind_ms_at(25), 215.0);
}
