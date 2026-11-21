//! P-TICK-3-fix: the reliable-state throttle of a connection is counted at its rate, as its snapshot writer's is
//! (`outbox.rs` `a_40_hz_outbox_writes_at_its_rate`).

use crate::wire_state::CollectionPublisher;
use rh_sim::Observation;

/// The forced-replacement throttle is 1000 ms at the connection's rate (20 ticks at 50 ms, 40 at 25 ms), and a reconnect
/// keeps the rate.
#[test]
fn forced_replacements_are_throttled_to_1000_ms_at_the_connection_rate() {
    for (tick_ms, ticks) in [(50u32, 20u32), (25, 40)] {
        let mut publisher = CollectionPublisher::default();
        assert_eq!(publisher.tick_ms(), rh_protocol::TICK_MS);
        publisher.set_tick_ms(tick_ms).unwrap();
        let mut obs = Observation::default();
        publisher
            .prepare(&obs, false, false, false)
            .unwrap()
            .commit(&mut publisher);
        obs.tick = 100;
        assert!(publisher
            .prepare(&obs, false, false, true)
            .unwrap()
            .commit(&mut publisher));
        assert!(!publisher.resync_due(100 + ticks - 1, true), "{tick_ms} ms");
        assert!(publisher.resync_due(100 + ticks, true), "{tick_ms} ms");
        publisher.reconnect();
        assert_eq!(publisher.tick_ms(), tick_ms, "a reconnect keeps the rate");
    }
    let mut publisher = CollectionPublisher::default();
    for tick_ms in [0u32, 20, 40, 100] {
        assert!(publisher.set_tick_ms(tick_ms).is_err(), "{tick_ms} ms");
    }
    assert_eq!(publisher.tick_ms(), rh_protocol::TICK_MS);
}
