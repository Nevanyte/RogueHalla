//! Lease policy, renewals across generations and ownership of profile entries.

use super::*;

#[test]
fn lease_policy_reserves_time_for_a_renewal() {
    let default = LeasePolicy::default();
    assert!(default.validate().is_ok());
    assert!(LeasePolicy {
        ttl: Duration::from_secs(3600),
        ..default
    }
    .validate()
    .is_ok());
    for bad in [
        LeasePolicy {
            ttl: Duration::ZERO,
            ..default
        },
        LeasePolicy {
            renew_interval: Duration::ZERO,
            ..default
        },
        LeasePolicy {
            db_timeout: Duration::ZERO,
            ..default
        },
        LeasePolicy {
            ttl: default.renew_interval + default.db_timeout,
            ..default
        },
        LeasePolicy {
            ttl: Duration::from_secs(3601),
            ..default
        },
        LeasePolicy {
            ttl: Duration::from_secs(10_000_000_000_000),
            ..default
        },
        LeasePolicy {
            ttl: Duration::MAX,
            ..default
        },
    ] {
        assert!(bad.validate().is_err());
    }
}

#[tokio::test(start_paused = true)]
async fn a_late_renewal_completion_cannot_change_a_replacement_generation() {
    let d = director_with(MemoryStore {
        renew_delay: Duration::from_secs(2),
        ..MemoryStore::strict()
    });
    let policy = d.cfg.leases;
    let old = d
        .persistence
        .acquire_for("archer", id(1), policy.ttl)
        .await
        .unwrap();
    d.inner.lock().unwrap().profiles.insert(
        "archer".into(),
        seat(1, id(1), old.fence, SeatState::Seated),
    );
    let start = tokio::time::Instant::now();
    let (tx, old_clock) = tokio::sync::watch::channel(start + policy.ttl);
    d.start_renewals("archer".into(), 1, id(1), old, start, tx);
    settle().await;
    tokio::time::advance(policy.renew_interval).await;
    settle().await;
    // The old query is outstanding. A new owner has acquired a fresh fence for this name.
    assert!(d.persistence.release("archer", old.fence).await.unwrap());
    let new = d
        .persistence
        .acquire_for("archer", id(2), policy.ttl)
        .await
        .unwrap();
    d.inner.lock().unwrap().profiles.insert(
        "archer".into(),
        seat(2, id(2), new.fence, SeatState::Seated),
    );
    let expiry = store(&d).lease("archer").unwrap().expires_at;
    tokio::time::advance(Duration::from_secs(2)).await;
    settle().await;
    assert_eq!(
        *old_clock.borrow(),
        start + policy.ttl,
        "obsolete false result must be ignored"
    );
    assert_eq!(store(&d).lease("archer").unwrap().expires_at, expiry);
    assert_eq!(entry_of(&d, "archer").unwrap().gen, 2);
}

#[tokio::test(start_paused = true)]
async fn a_seat_that_left_keeps_renewing_until_a_slow_result_commits() {
    let d = director_with(MemoryStore {
        record_delay: Duration::from_secs(250),
        ..MemoryStore::strict()
    });
    let m = id(1);
    let lease = d
        .persistence
        .acquire_for("archer", m, d.cfg.leases.ttl)
        .await
        .unwrap();
    let start = tokio::time::Instant::now();
    let (tx, _clock) = tokio::sync::watch::channel(start + d.cfg.leases.ttl);
    d.inner
        .lock()
        .unwrap()
        .profiles
        .insert("archer".into(), seat(7, m, lease.fence, SeatState::Seated));
    d.start_renewals("archer".into(), 7, m, lease, start, tx);
    let worker = entry_of(&d, "archer").unwrap().renewal.unwrap();
    d.reports
        .send(RoomReport::Left {
            admission: None,
            match_id: m,
            profile: "archer".into(),
            fence: lease.fence,
            result_pending: true,
        })
        .unwrap();
    settle().await;
    // A pending room report also keeps the lease through an entire initial TTL.
    for _ in 0..5 {
        tokio::time::advance(d.cfg.leases.renew_interval).await;
        settle().await;
    }
    assert!(d.persistence.acquire("archer", id(2)).await.is_err());
    d.reports
        .send(RoomReport::Result {
            bot_choices: Vec::new(),
            admission: None,
            match_id: m,
            profile: "archer".into(),
            fence: Some(lease.fence),
            bot: false,
            bot_spec: None,
            result: some_result("archer"),
            roster: Roster::default(),
        })
        .unwrap();
    settle().await;
    for _ in 0..7 {
        tokio::time::advance(d.cfg.leases.renew_interval).await;
        settle().await;
    }
    assert_eq!(entry_of(&d, "archer").unwrap().writes, 1);
    assert!(d.persistence.acquire("archer", id(2)).await.is_err());
    tokio::time::advance(Duration::from_secs(40)).await;
    settle().await;
    assert_eq!(store(&d).results.lock().unwrap().len(), 1);
    assert!(entry_of(&d, "archer").is_none());
    assert!(
        worker.is_finished(),
        "retired ownership cancels its sleeping worker immediately"
    );
}

/// Ownership is tied to the admission that owns the entry: a room's `Closed` never marks or frees a reservation still
/// acquiring, a stale owner's unwind or free never removes a newer entry, and a `Left` with no entry of its own still
/// gives its fenced lease back.
#[tokio::test(start_paused = true)]
async fn profile_entries_are_changed_only_by_their_owner() {
    let d = director_with(MemoryStore::strict());
    let m = id(1);
    d.inner
        .lock()
        .unwrap()
        .rooms
        .insert(m, entry(Phase::Lobby, 0, 1));
    d.inner
        .lock()
        .unwrap()
        .profiles
        .insert("papa".into(), seat(5, m, 0, SeatState::Reserved));
    // The room closes while the admission is mid-acquire: the reservation is its admission's to unwind.
    d.reports
        .send(RoomReport::Closed {
            match_id: m,
            stats: RoomStats::default(),
            final_hash: 0,
            replay: None,
        })
        .unwrap();
    settle().await;
    let e = entry_of(&d, "papa").expect("kept");
    assert_eq!((e.gen, e.state), (5, SeatState::Reserved));
    // An older admission's late unwinding, or an older seat's free, never touches it.
    d.unwind(&"papa".into(), 4, m, None);
    d.unwind(&"papa".into(), 4, m, Some(3));
    d.free(&"papa".into(), 4);
    settle().await;
    assert_eq!(
        entry_of(&d, "papa").map(|e| (e.gen, e.state)),
        Some((5, SeatState::Reserved))
    );
    // Its own admission unwinds it: with a lease taken it stays `Leaving` until the give-back lands.
    let fence = d.persistence.acquire("papa", m).await.unwrap();
    d.unwind(&"papa".into(), 5, m, Some(fence));
    assert_eq!(
        entry_of(&d, "papa").map(|e| e.state),
        Some(SeatState::Leaving)
    );
    settle().await;
    assert!(entry_of(&d, "papa").is_none());
    assert!(store(&d).lease("papa").unwrap().released);
    // A Left for a seat this process has no entry for still gives its lease back (fenced: a newer lease is safe).
    let fq = d.persistence.acquire("quebec", m).await.unwrap();
    d.reports
        .send(RoomReport::Left {
            admission: None,
            match_id: m,
            profile: "Quebec".into(),
            fence: fq,
            result_pending: false,
        })
        .unwrap();
    settle().await;
    assert!(store(&d).lease("quebec").unwrap().released);
    let newer = d.persistence.acquire("quebec", id(2)).await.unwrap();
    d.reports
        .send(RoomReport::Left {
            admission: None,
            match_id: m,
            profile: "quebec".into(),
            fence: fq,
            result_pending: false,
        })
        .unwrap();
    settle().await;
    let l = store(&d).lease("quebec").unwrap();
    assert_eq!(
        (l.fence, l.released),
        (newer, false),
        "a stale fence releases nothing"
    );
}
