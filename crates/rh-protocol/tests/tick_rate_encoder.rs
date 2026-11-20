//! P-TICK-3-fix (ADR 0029, protocol 21): the Rust ENCODER writes snapshot time and its delta limits at the connection's rate,
//! as the three decoders read them since P-TICK-3. A 25 ms snapshot carries no time override (its time is `tick * 25`), a
//! 40 Hz connection forces a Full frame at most once per 1000 ms (40 ticks) and accepts a delta base up to 3200 ms (128
//! ticks) old, and a snapshot decoded at 40 Hz re-encodes at 40 Hz to its own bytes. At 20 Hz nothing moves (20 and 64
//! ticks, the v21 vectors unchanged).
use rh_protocol::delta::{max_delta_age, min_forced_full_ticks};
use rh_protocol::*;

fn crowd(tick: u32, shift: f32) -> impl ExactSizeIterator<Item = WireActor> {
    (1..=12u32).map(move |id| WireActor {
        id,
        x: 100.0 + id as f32 * 40.0 + if id == 3 { shift } else { 0.0 },
        y: 200.0 + id as f32 * 16.0,
        hp_pct: 100,
        status: 1,
        ..Default::default()
    })
}

/// A snapshot of `tick` on the `tick_ms` grid (its time is `tick * tick_ms`), id 3 moved by `shift`.
fn snap_at(tick: u32, tick_ms: u32, shift: f32) -> SnapshotData {
    let mut s = SnapshotData::build_with_server_time(
        tick,
        snapshot_server_ms_at(tick, tick_ms),
        1,
        tick,
        None,
        crowd(tick, shift),
    )
    .expect("valid");
    s.set_tick_ms(tick_ms).expect("a hosted rate");
    s
}

fn time_of(msg: &ServerMsg) -> (u32, f64) {
    match msg {
        ServerMsg::Snapshot {
            tick, server_time, ..
        } => (*tick, *server_time),
        ServerMsg::SnapshotDelta(d) => (d.tick, d.server_time),
        other => panic!("a snapshot frame decodes to a snapshot: {other:?}"),
    }
}

#[test]
fn the_limits_at_both_rates() {
    assert_eq!((max_delta_age(50), min_forced_full_ticks(50)), (64, 20));
    assert_eq!((max_delta_age(25), min_forced_full_ticks(25)), (128, 40));
}

#[test]
fn a_25_ms_snapshot_encodes_without_a_time_override_and_reads_tick_times_25_at_40_hz() {
    for tick in [1u32, 7, 101, 16_384, 1_000_000] {
        let snap = snap_at(tick, 25, 0.0);
        assert_eq!(snap.tick_ms(), 25);
        let mut full = Vec::new();
        snap.encode_full_into(&mut full);
        assert_eq!(full.len(), snap.full_len(), "full_len counts no override");
        assert!(snap.min_len() <= full.len(), "min_len stays a lower bound");
        // The same snapshot at 20 Hz needs the 8-byte override (tick * 25 is not tick * 50).
        let mut at50 = SnapshotData::build_with_server_time(
            tick,
            snapshot_server_ms_at(tick, 25),
            1,
            tick,
            None,
            crowd(tick, 0.0),
        )
        .unwrap();
        assert_eq!(at50.tick_ms(), TICK_MS, "a snapshot stands at 20 Hz unless told");
        let mut full50 = Vec::new();
        at50.encode_full_into(&mut full50);
        assert_eq!(full50.len(), full.len() + 8, "tick {tick}: no override at 25 ms");
        at50.set_tick_ms(25).unwrap();
        assert_eq!(at50, snap);
        let msg = ServerMsg::decode_at(&full, 40).expect("a 40 Hz decoder accepts it");
        assert_eq!(time_of(&msg), (tick, tick as f64 * 25.0));
        assert_eq!(msg, snap.decoded());
        // Through a 40 Hz encoder too, and its delta.
        let mut enc = ConnEncoder::at(25).unwrap();
        let (mut a, mut b) = (Vec::new(), Vec::new());
        assert_eq!(enc.encode(snap_at(tick, 25, 0.0), &mut a), FrameKind::Full);
        assert_eq!(a, full);
        assert_eq!(enc.encode(snap_at(tick + 1, 25, 8.0), &mut b), FrameKind::Delta);
        let mut t = SnapshotTracker::awaiting_welcome();
        t.decode(&welcome_at(40)).unwrap();
        t.decode(&a).unwrap();
        assert_eq!(
            time_of(&t.decode(&b).expect("the 40 Hz tracker applies it")),
            (tick + 1, (tick + 1) as f64 * 25.0)
        );
    }
}

#[test]
fn a_rate_other_than_50_or_25_ms_is_refused() {
    let mut s = snap_at(3, 50, 0.0);
    for tick_ms in [0u32, 1, 20, 33, 40, 100] {
        assert!(s.set_tick_ms(tick_ms).is_err(), "{tick_ms} ms");
        assert!(ConnEncoder::at(tick_ms).is_err(), "{tick_ms} ms");
    }
    assert_eq!(s.tick_ms(), 50, "a refused rate changes nothing");
    assert_eq!(ConnEncoder::new().tick_ms(), TICK_MS);
    assert_eq!(ConnEncoder::default().tick_ms(), TICK_MS);
}

#[test]
fn an_encoder_writes_its_own_rate_whatever_the_snapshot_was_built_at() {
    // A 50 ms snapshot on a 40 Hz connection: tick * 50 is not the 40 Hz default, so it is an override there.
    let mut enc = ConnEncoder::at(25).unwrap();
    let mut out = Vec::new();
    enc.encode(snap_at(20, 50, 0.0), &mut out);
    assert_eq!(
        time_of(&ServerMsg::decode_at(&out, 40).unwrap()),
        (20, 1000.0),
        "the 40 Hz reader sees the time the snapshot holds"
    );
    // And a 25 ms snapshot on a 20 Hz connection.
    let mut enc = ConnEncoder::new();
    let mut out = Vec::new();
    enc.encode(snap_at(20, 25, 0.0), &mut out);
    assert_eq!(time_of(&ServerMsg::decode(&out).unwrap()), (20, 500.0));
}

/// Forces a resync at `first`, then asks again at once: returns the first tick after `first` that is written Full.
fn next_forced_full(tick_ms: u32, first: u32) -> u32 {
    let mut enc = ConnEncoder::at(tick_ms).unwrap();
    let mut out = Vec::new();
    enc.encode(snap_at(first - 1, tick_ms, 0.0), &mut out);
    enc.request_full();
    out.clear();
    assert_eq!(enc.encode(snap_at(first, tick_ms, 8.0), &mut out), FrameKind::Full);
    enc.request_full();
    for tick in first + 1..first + 200 {
        out.clear();
        let shift = if tick % 2 == 0 { 0.0 } else { 8.0 };
        if enc.encode(snap_at(tick, tick_ms, shift), &mut out) == FrameKind::Full {
            return tick;
        }
    }
    panic!("the pending request was answered");
}

#[test]
fn a_40_hz_encoder_forces_a_full_after_1000_ms_and_20_hz_after_20_ticks() {
    assert_eq!(next_forced_full(25, 100) - 100, 40, "40 ticks at 40 Hz");
    assert_eq!(next_forced_full(50, 100) - 100, 20, "20 ticks at 20 Hz");
}

/// The kind a fresh encoder at `tick_ms` writes for a snapshot `age` ticks after its base.
fn kind_after(tick_ms: u32, age: u32) -> (FrameKind, Vec<u8>) {
    let mut enc = ConnEncoder::at(tick_ms).unwrap();
    let mut out = Vec::new();
    enc.encode(snap_at(500, tick_ms, 0.0), &mut out);
    out.clear();
    let kind = enc.encode(snap_at(500 + age, tick_ms, 8.0), &mut out);
    (kind, out)
}

#[test]
fn a_40_hz_encoder_accepts_a_delta_base_up_to_128_ticks_old_and_20_hz_up_to_64() {
    for age in [1u32, 64, 65, 100, 128] {
        let (kind, frame) = kind_after(25, age);
        assert_eq!(kind, FrameKind::Delta, "age {age} at 40 Hz");
        let msg = ServerMsg::decode_at(&frame, 40).expect("a 40 Hz decoder accepts it");
        assert_eq!(time_of(&msg), (500 + age, (500 + age) as f64 * 25.0));
        let base = snap_at(500, 25, 0.0);
        let cur = snap_at(500 + age, 25, 8.0);
        let d = delta::diff(&base, &cur).expect("the reference diff agrees");
        assert_eq!(d.base_tick, 500);
        assert_eq!(
            ServerMsg::SnapshotDelta(d).try_encode_at(40).as_deref(),
            Ok(frame.as_slice()),
            "the reference writer at 40 Hz writes the same bytes"
        );
    }
    assert_eq!(kind_after(25, 129).0, FrameKind::Full, "age 129 at 40 Hz");
    assert!(delta::diff(&snap_at(500, 25, 0.0), &snap_at(629, 25, 8.0)).is_none());
    assert_eq!(kind_after(50, 64).0, FrameKind::Delta, "age 64 at 20 Hz");
    assert_eq!(kind_after(50, 65).0, FrameKind::Full, "age 65 at 20 Hz");
    // The reference writer refuses a base age over its rate's limit.
    let d = delta::diff(&snap_at(500, 25, 0.0), &snap_at(600, 25, 8.0)).unwrap();
    assert!(ServerMsg::SnapshotDelta(d.clone()).try_encode_at(40).is_ok());
    assert!(
        ServerMsg::SnapshotDelta(d).try_encode().is_err(),
        "100 ticks is over the 20 Hz limit"
    );
}

/// The sample Welcome's frame with its rate byte replaced (byte 47, as in `tick_rate_wire.rs`).
fn welcome_at(rate: u8) -> Vec<u8> {
    let (_, welcome) = samples::server_samples()
        .into_iter()
        .find(|(name, _)| *name == "welcome")
        .expect("the welcome sample");
    let mut frame = welcome.encode();
    frame[47] = rate;
    frame
}

#[test]
fn a_snapshot_decoded_at_40_hz_re_encodes_at_40_hz_to_its_own_bytes() {
    let mut frames = Vec::new();
    for (tick, time) in [(7u32, 175.0), (101, 2525.0), (20, 888.75), (20, 1000.0)] {
        let s = SnapshotData::build_with_server_time(tick, time, 1, 2, None, crowd(tick, 0.0))
            .unwrap();
        let mut f = Vec::new();
        let mut enc = ConnEncoder::at(25).unwrap();
        enc.encode(s, &mut f);
        frames.push(f);
    }
    let (_, delta) = kind_after(25, 100);
    frames.push(delta);
    for frame in frames {
        let msg = ServerMsg::decode_at(&frame, 40).expect("decodes at 40");
        assert_eq!(msg.try_encode_at(40).as_deref(), Ok(frame.as_slice()), "{msg:?}");
        assert_eq!(msg.encoded_len_at(40), Ok(frame.len()));
    }
    // At 20 Hz the rate-aware writer is the plain one.
    for (_, msg) in samples::server_samples() {
        if matches!(msg, ServerMsg::Snapshot { .. } | ServerMsg::SnapshotDelta(_)) {
            assert_eq!(msg.try_encode_at(20), msg.try_encode());
        }
    }
    assert!(ServerMsg::Pong {
        nonce: 1,
        client_ms: 2.0,
        server_ms: 3.0
    }
    .try_encode_at(30)
    .is_err());
}
