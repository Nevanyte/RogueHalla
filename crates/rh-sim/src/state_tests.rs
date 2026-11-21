use super::*;
use crate::constants::TICK_MS;

/// The recorder as it was: push, then trim from the front while longer than two and the oldest is out of the window.
fn reference(h: &mut Vec<Sample>, now: f64, x: f64, y: f64) {
    h.push(Sample { t: now, x, y });
    let cutoff = now - MAX_REWIND_MS - TICK_MS;
    while h.len() > 2 && h[0].t < cutoff {
        h.remove(0);
    }
}

#[test]
fn the_history_ring_is_the_trimmed_vec_it_replaced() {
    let mut ring = History::default();
    let mut vec: Vec<Sample> = Vec::new();
    let mut now = 50.0;
    // Mostly one tick apart, with gaps (a fighter that did not stand records nothing for a while).
    for n in 0..2000u32 {
        now += TICK_MS
            * if n % 97 < 5 {
                1.0 + (n % 13) as f64
            } else {
                1.0
            };
        let (x, y) = (n as f64 * 0.37, 1000.0 - n as f64 * 0.11);
        ring.record_at_step(now, x, y, TICK_MS);
        reference(&mut vec, now, x, y);
        assert_eq!(ring.iter().collect::<Vec<_>>(), vec, "tick {n}");
        assert_eq!(
            serde_json::to_value(ring).unwrap(),
            serde_json::to_value(&vec).unwrap()
        );
        assert!(ring.len() <= history_cap());
    }
    // Restarting keeps one sample, and a checkpoint of the ring restores it.
    let s = Sample {
        t: now,
        x: 1.0,
        y: 2.0,
    };
    ring.reset(s);
    assert_eq!(ring.iter().collect::<Vec<_>>(), [s]);
    let back: History = serde_json::from_value(serde_json::to_value(ring).unwrap()).unwrap();
    assert_eq!(back, ring);
}

#[test]
fn the_ring_holds_the_whole_rewind_window() {
    // Samples one tick apart: the window plus the tick of slack, never more than the capacity, so nothing the rewind can
    // ask for is dropped.
    let mut ring = History::default();
    for n in 0..100u32 {
        ring.record_at_step(TICK_MS * n as f64, 0.0, 0.0, TICK_MS);
    }
    let want = ((MAX_REWIND_MS + TICK_MS) / TICK_MS) as usize + 1;
    assert_eq!(ring.len(), want);
    assert!(ring.len() < history_cap());
    assert_eq!(ring[ring.len() - 1].t, TICK_MS * 99.0);
    assert_eq!(ring[0].t, TICK_MS * (99 - want + 1) as f64);
    assert!(serde_json::from_value::<History>(
        serde_json::to_value(vec![
            Sample {
                t: 0.0,
                x: 0.0,
                y: 0.0
            };
            history_cap() + 1
        ])
        .unwrap()
    )
    .is_err());
}

/// The legacy recorder at any step: push, then trim while longer than two and the oldest is before the cutoff.
fn reference_at(h: &mut Vec<Sample>, now: f64, step: f64) {
    h.push(Sample {
        t: now,
        x: 0.0,
        y: 0.0,
    });
    let cutoff = now - MAX_REWIND_MS - step;
    while h.len() > 2 && h[0].t < cutoff {
        h.remove(0);
    }
}

#[test]
fn the_ring_holds_the_whole_rewind_window_at_every_allowed_step() {
    // P-TICK-1a (ADR 0029): sized for 25 ms, the ring holds the window at 25 ms with the match's own cutoff and with
    // the legacy one, and at 50 ms it keeps exactly the samples the legacy recorder keeps.
    for (spacing, cutoff_step) in [(25.0, 25.0), (25.0, TICK_MS), (TICK_MS, TICK_MS)] {
        let mut ring = History::default();
        let mut vec = Vec::new();
        for n in 0..400u32 {
            let now = spacing * n as f64;
            ring.record_at_step(now, 0.0, 0.0, cutoff_step);
            reference_at(&mut vec, now, cutoff_step);
            assert_eq!(ring.iter().collect::<Vec<_>>(), vec);
            assert!(ring.len() < history_cap(), "{spacing} ms spacing");
        }
        let want = ((MAX_REWIND_MS + cutoff_step) / spacing) as usize + 1;
        assert_eq!(
            ring.len(),
            want,
            "{spacing} ms spacing, {cutoff_step} ms cutoff"
        );
    }
}

#[test]
#[should_panic(expected = "ring is full")]
fn a_full_history_ring_refuses_a_push_in_every_build() {
    let mut ring = History::default();
    for n in 0..=history_cap() {
        ring.push(Sample {
            t: n as f64,
            x: 0.0,
            y: 0.0,
        });
    }
}

#[test]
fn a_path_serializes_only_what_is_left_and_compacts_on_replace() {
    let pts: Vec<Vec2> = (0..5).map(|i| Vec2::new(i as f64, -(i as f64))).collect();
    let mut p = Path::default();
    p.set_from(&pts);
    p.advance();
    p.advance();
    assert_eq!(p.first(), Some(pts[2]));
    assert_eq!(p.len(), 3);
    assert_eq!(
        serde_json::to_value(&p).unwrap(),
        serde_json::to_value((p.generation, &pts[2..])).unwrap(),
        "the consumed prefix is not serialized"
    );
    let mut fresh = Path::default();
    fresh.set_from(&pts[2..]);
    assert_eq!(
        p, fresh,
        "equality sees the logical path and replacement generation"
    );
    let back: Path = serde_json::from_value(serde_json::to_value(&p).unwrap()).unwrap();
    assert_eq!(back, p);
    p.set_from(&pts[..1]);
    assert_eq!((p.cursor, p.pts.len()), (0, 1), "replacing compacts");
    p.advance();
    assert!(p.is_empty() && p.first().is_none());
    assert_eq!(
        (p.cursor, p.pts.len()),
        (0, 0),
        "running out resets the cursor"
    );
    assert_eq!(
        serde_json::to_value(&p).unwrap(),
        serde_json::json!([2, []])
    );
}

#[test]
fn the_roster_names_leavers_too() {
    let mut r = Roster::default();
    r.push(PlayerId(1), "a");
    r.push(PlayerId(2), "b");
    r.push(PlayerId(5), "c");
    assert_eq!(r.name(PlayerId(2)), Some("b"));
    assert_eq!(r.name(PlayerId(5)), Some("c"));
    assert_eq!(r.name(PlayerId(3)), None);
}
