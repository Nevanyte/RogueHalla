//! Drain caps, wedged rooms, a panicking room and the camelCase `/info` keys.

use super::*;

/// The drain cap: a match that would never finish is ended, its seat closed out, and the lifecycle terminates.
#[tokio::test(start_paused = true)]
async fn a_drain_past_its_cap_ends_the_remaining_rooms() {
    let d = director_with(MemoryStore::strict());
    let (_tx, _ok) = d
        .admit(hello("papa"), Arc::new(Outbox::default()), 1)
        .await
        .expect("admitted");
    // A lobby waiting for a second player never finishes by itself.
    let capped = d.drain_within(Some(Duration::from_secs(30))).await;
    assert!(capped, "the cap, not the match, ended the drain");
    assert!(d.lifecycle.is_terminated());
    assert!(
        entry_of(&d, "papa").is_none() || {
            settle().await;
            entry_of(&d, "papa").is_none()
        }
    );
    assert_eq!(d.room_count(), 0);
}

/// A room that never answers (its mailbox is full and nobody reads it) must not stop the drain cap from starting or from
/// ending the drain on time.
#[tokio::test(start_paused = true)]
async fn a_wedged_room_with_a_full_mailbox_cannot_hold_the_drain_past_its_cap() {
    let d = director_with(MemoryStore::strict());
    let (tx, rx) = mpsc::channel::<RoomCmd>(1);
    tx.send(RoomCmd::Drain).await.unwrap();
    d.inner.lock().unwrap().rooms.insert(
        id(7),
        RoomEntry {
            tx,
            phase: Phase::Active,
            seated: 1,
            reserved: 0,
            public: PublicRoom::default(),
        },
    );
    let started = tokio::time::Instant::now();
    let capped = d.drain_within(Some(Duration::from_secs(30))).await;
    assert!(capped && d.lifecycle.is_terminated());
    assert_eq!(
        started.elapsed(),
        Duration::from_secs(30),
        "exactly the cap, from the call"
    );
    drop(rx);
}

/// Without a cap (`--drain-max-secs 0`) the drain waits for the matches, as before; an empty server finishes at once.
#[tokio::test(start_paused = true)]
async fn an_uncapped_or_empty_drain_does_not_report_the_cap() {
    let d = director_with(MemoryStore::strict());
    assert!(!d.drain_within(Some(Duration::from_secs(30))).await);
    assert!(d.lifecycle.is_terminated());
    let d = director_with(MemoryStore::strict());
    assert!(!d.drain_within(None).await);
}

/// A room task that panics still reports `Closed`: its seats are freed and their leases given back, instead of the
/// profile being refused `ProfileInUse` until the process restarts.
#[tokio::test(start_paused = true)]
async fn a_room_that_panics_frees_its_seats_and_leases() {
    let d = director_with(MemoryStore::strict());
    let (tx, ok) = d
        .admit(hello("papa"), Arc::new(Outbox::default()), 1)
        .await
        .expect("admitted");
    assert_eq!(
        entry_of(&d, "papa").map(|e| e.state),
        Some(SeatState::Seated)
    );
    tx.send(RoomCmd::Crash).await.unwrap();
    for _ in 0..100 {
        if entry_of(&d, "papa").is_none() {
            break;
        }
        tokio::time::sleep(Duration::from_millis(1)).await;
    }
    assert!(entry_of(&d, "papa").is_none(), "the seat was freed");
    assert!(
        store(&d).lease("papa").unwrap().released,
        "its lease given back"
    );
    {
        let inner = d.inner.lock().unwrap();
        assert!(inner.rooms.is_empty());
        assert!(inner.closed.last().is_some_and(|s| s.crashed));
    }
    // The crash is on record for the loopback control health: a counter and the latest crash's summary.
    let info = d.crash_info();
    assert_eq!(info["total"], 1, "{info}");
    assert_eq!(info["last"]["seatsFreed"], 1, "{info}");
    assert!(info["last"]["roomId"]
        .as_str()
        .is_some_and(|r| r.len() == 32));
    assert!(info["last"]["atUnixMs"].as_u64().unwrap() > 0);
    drop(ok);
    // And the profile can play again.
    d.admit(hello("papa"), Arc::new(Outbox::default()), 2)
        .await
        .expect("rejoins");
}

/// `/info` and `/control/health` use camelCase keys throughout, `closedRooms` entries included (the contract text and the
/// room's own log line say `frameRefusals`).
#[tokio::test]
async fn info_and_closed_room_keys_are_exactly_these_camel_case_names() {
    let d = director();
    d.inner
        .lock()
        .unwrap()
        .closed
        .push(crate::room::RoomStats::default());
    let info = d.info();
    let sorted = |v: &serde_json::Value| {
        let mut k: Vec<String> = v.as_object().unwrap().keys().cloned().collect();
        k.sort_unstable();
        k
    };
    let want = |names: &[&str]| {
        let mut k: Vec<String> = names.iter().map(|s| s.to_string()).collect();
        k.sort_unstable();
        k
    };
    assert_eq!(
        sorted(&info),
        want(&[
            "closedRooms",
            "contentHash",
            "lobbies",
            "maxRooms",
            "players",
            "protocol",
            "resultsFailed",
            "resultsOk",
            "resultsRefused",
            "rooms",
            "state"
        ])
    );
    assert_eq!(
        sorted(&info["closedRooms"][0]),
        want(&[
            "bytesEnqueuedTotal",
            "bytesOut",
            "crashed",
            "droppedSteps",
            "frameRefusals",
            "frameRefusalsLogged",
            "fxDropped",
            "lateWakes",
            "snapshotBytesSent",
            "snapshotRefusals",
            "snapshotRefusalsLogged",
            "snapshotsDelta",
            "snapshotsFull",
            "snapshotsSkipped",
            "stepUsMax",
            "stepUsP50",
            "stepUsP99",
            "ticks",
        ])
    );
    let health = crate::metrics::process().json();
    for k in health.as_object().unwrap().keys() {
        assert!(!k.contains('_'), "{k}");
    }
}
