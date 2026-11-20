//! P-TICK-5: a room announces its own step on every Welcome (join and resume) and writes its snapshots at that rate
//! (ADR 0029). At 50 ms everything stays as it was.

use super::*;

/// The rate whose step is `tick_ms` (20 Hz at 50 ms, 40 Hz at 25 ms).
fn rate_of(tick_ms: u32) -> u8 {
    (1000 / tick_ms) as u8
}

fn welcome_rate(welcome: &[u8]) -> u8 {
    match ServerMsg::decode(welcome).expect("a Welcome decodes on its own") {
        ServerMsg::Welcome { tick_rate, .. } => tick_rate,
        other => panic!("expected Welcome, got {other:?}"),
    }
}

/// The first Snapshot (Full) frame of `frames`, read at both rates: `(tick, time at 20 Hz, time at 40 Hz)`.
fn first_full_snapshot(frames: &[Vec<u8>]) -> (u32, f64, f64) {
    let frame = frames
        .iter()
        .find(|f| {
            matches!(ServerMsg::decode_at(f, 20), Ok(ServerMsg::Snapshot { .. }))
                || matches!(ServerMsg::decode_at(f, 40), Ok(ServerMsg::Snapshot { .. }))
        })
        .expect("a Full snapshot follows the Welcome");
    let read = |rate: u8| match ServerMsg::decode_at(frame, rate) {
        Ok(ServerMsg::Snapshot {
            tick, server_time, ..
        }) => (tick, server_time),
        other => panic!("the Full snapshot read at {rate} Hz: {other:?}"),
    };
    let (tick, at20) = read(20);
    let (tick40, at40) = read(40);
    assert_eq!(tick, tick40);
    (tick, at20, at40)
}

/// A room built at its step sends that rate on join and on resume, and the snapshot after a resume carries no time
/// override: read at its own rate its time is `tick * tick_ms`, read at the other rate `tick * other_ms`.
#[tokio::test(start_paused = true)]
async fn a_25_ms_room_announces_40_hz_on_join_and_resume() {
    for tick_ms in [50u32, 25] {
        let tick = Duration::from_millis(tick_ms as u64);
        let mut config = cfg(tick);
        config.sim.tick_ms = tick_ms;
        assert!(
            wire::profile_supported(&config.sim).is_ok(),
            "a {tick_ms} ms room is hosted"
        );
        let start = Instant::now();
        let (reports, _rx) = mpsc::unbounded_channel();
        let mut room = Room::new([5; 16], config, reports, start);
        assert_eq!(room.sim.tick_ms(), tick_ms);

        let first = Arc::new(Outbox::default());
        let joined = room
            .join("Steady".into(), None, first.clone(), 1, 1, None)
            .unwrap();
        assert_eq!(
            welcome_rate(&joined.welcome),
            rate_of(tick_ms),
            "the join Welcome of a {tick_ms} ms room"
        );
        let mut at = start;
        for _ in 0..4 {
            at += tick;
            assert!(room.wake(at));
        }

        let second = Arc::new(Outbox::default());
        let (reply, resumed) = oneshot::channel();
        room.handle(
            RoomCmd::Resume {
                session: joined.session,
                outbox: second.clone(),
                conn: 2,
                reply,
            },
            at,
        );
        let resumed = resumed.await.unwrap().expect("the seat resumes");
        assert_eq!(
            welcome_rate(&resumed.welcome),
            rate_of(tick_ms),
            "the resume Welcome of a {tick_ms} ms room"
        );
        for _ in 0..2 {
            at += tick;
            assert!(room.wake(at));
        }
        let mut frames = Vec::new();
        assert_eq!(second.drain_into(&mut frames).close, None);
        let (snap_tick, at20, at40) = first_full_snapshot(&frames);
        assert!(snap_tick > 0);
        assert_eq!(
            (at20, at40),
            (snap_tick as f64 * 50.0, snap_tick as f64 * 25.0),
            "a {tick_ms} ms snapshot carries no time override (tick {snap_tick})"
        );
        assert_eq!(
            room.sim.server_time(),
            room.sim.tick() as f64 * tick_ms as f64,
            "sim time is tick * {tick_ms}"
        );
    }
}
