//! Result reports: retained metadata, in-flight writes, refusals, duplicates and seat counts.

use super::*;

#[tokio::test(start_paused = true)]
async fn ending_metadata_is_retained_for_humans_and_never_banks_a_bot() {
    let d = director_with(MemoryStore::strict());
    let m = id(1);
    let fence = d.persistence.acquire("subject", m).await.unwrap();
    d.inner
        .lock()
        .unwrap()
        .profiles
        .insert("subject".into(), seat(1, m, fence, SeatState::Seated));
    let mut result = some_result("subject");
    result.faction = Some(FactionId::Frost);
    result.weapon = Some(rh_sim::WeaponId::Wand);
    for bot in [true, false] {
        d.reports
            .send(RoomReport::Result {
                bot_choices: Vec::new(),
                admission: None,
                match_id: m,
                profile: "subject".into(),
                fence: Some(fence),
                bot,
                bot_spec: None,
                result: result.clone(),
                roster: Roster::default(),
            })
            .unwrap();
        settle().await;
        let stored = store(&d).results.lock().unwrap();
        if bot {
            assert!(stored.is_empty());
        } else {
            assert_eq!(stored.len(), 1);
            assert_eq!(stored[0].2, result);
        }
    }
}

/// A seat whose result was reported but not yet written leaves (it died, then left, or the match ended and lingered
/// out): its lease is not given back in parallel with the write, which would make the write fail `NoLease` and lose
/// the result. The write releases the lease itself, and the profile is free once it lands.
#[tokio::test(start_paused = true)]
async fn a_seat_with_a_result_in_flight_keeps_its_lease_until_the_write_lands() {
    let d = director_with(MemoryStore {
        record_delay: Duration::from_millis(500),
        ..MemoryStore::strict()
    });
    let m = id(1);
    let fence = d.persistence.acquire("papa", m).await.unwrap();
    d.inner
        .lock()
        .unwrap()
        .profiles
        .insert("papa".into(), seat(7, m, fence, SeatState::Seated));
    d.reports
        .send(RoomReport::Result {
            bot_choices: Vec::new(),
            admission: None,
            match_id: m,
            profile: "papa".into(),
            bot: false,
            bot_spec: None,
            fence: Some(fence),
            result: some_result("papa"),
            roster: Roster::default(),
        })
        .unwrap();
    d.reports
        .send(RoomReport::Left {
            admission: None,
            match_id: m,
            profile: "papa".into(),
            fence,
            result_pending: false,
        })
        .unwrap();
    settle().await;
    let e = entry_of(&d, "papa").expect("held while the write is in flight");
    assert_eq!(
        (e.state, e.writes, e.lease),
        (SeatState::Leaving, 1, LeaseState::Held)
    );
    assert!(
        !store(&d).lease("papa").unwrap().released,
        "not given back early"
    );
    tokio::time::sleep(Duration::from_millis(500)).await;
    settle().await;
    assert_eq!(
        store(&d).results.lock().unwrap().len(),
        1,
        "the result landed"
    );
    let inner = d.inner.lock().unwrap();
    assert_eq!(
        (
            inner.results_ok,
            inner.results_refused,
            inner.results_failed
        ),
        (1, 0, 0)
    );
    assert!(
        !inner.profiles.contains_key(&"papa".into()),
        "free once written"
    );
    drop(inner);
    assert!(
        store(&d).lease("papa").unwrap().released,
        "released by the write"
    );
}

/// Even if a result report arrived after its seat's give-back started (no room sends that order, but the bookkeeping
/// must not depend on it), the entry is not freed while the write is in flight.
#[tokio::test(start_paused = true)]
async fn a_give_back_never_frees_an_entry_with_a_write_in_flight() {
    let d = director_with(MemoryStore {
        record_delay: Duration::from_millis(300),
        ..MemoryStore::strict()
    });
    let m = id(1);
    d.inner.lock().unwrap().profiles.insert(
        "papa".into(),
        ProfileSeat {
            writes: 1,
            lease: LeaseState::Releasing,
            ..seat(6, m, 1, SeatState::Leaving)
        },
    );
    d.free(&"papa".into(), 6);
    let e = entry_of(&d, "papa").expect("kept while the write is in flight");
    assert_eq!((e.writes, e.lease), (1, LeaseState::Gone));
    d.inner
        .lock()
        .unwrap()
        .profiles
        .get_mut(&ProfileKey::from("papa"))
        .unwrap()
        .writes = 0;
    d.free(&"papa".into(), 6);
    assert!(
        entry_of(&d, "papa").is_none(),
        "free once nothing is pending"
    );
}

/// A refusal for good (no lease, stale fence) is not retried for two minutes: the profile is free at once.
#[tokio::test(start_paused = true)]
async fn a_result_the_store_refuses_for_good_is_not_retried() {
    let d = director_with(MemoryStore::strict());
    let m = id(1);
    let fence = d.persistence.acquire("papa", m).await.unwrap();
    // Someone else took over (a newer fence): this write can never land.
    assert!(d.persistence.release("papa", fence).await.unwrap());
    let newer = d.persistence.acquire("papa", id(2)).await.unwrap();
    assert_eq!(newer, fence + 1);
    d.inner
        .lock()
        .unwrap()
        .profiles
        .insert("papa".into(), seat(3, m, fence, SeatState::Seated));
    d.reports
        .send(RoomReport::Left {
            admission: None,
            match_id: m,
            profile: "papa".into(),
            fence,
            result_pending: true,
        })
        .unwrap();
    d.reports
        .send(RoomReport::Result {
            bot_choices: Vec::new(),
            admission: None,
            match_id: m,
            profile: "papa".into(),
            bot: false,
            bot_spec: None,
            fence: Some(fence),
            result: some_result("papa"),
            roster: Roster::default(),
        })
        .unwrap();
    let t0 = tokio::time::Instant::now();
    settle().await;
    assert_eq!(t0.elapsed(), Duration::ZERO, "no backoff");
    let inner = d.inner.lock().unwrap();
    assert_eq!(
        (
            inner.results_ok,
            inner.results_refused,
            inner.results_failed
        ),
        (0, 1, 0)
    );
    assert!(!inner.profiles.contains_key(&"papa".into()), "free at once");
    drop(inner);
    let lease = store(&d).lease("papa").unwrap();
    assert_eq!(
        (lease.fence, lease.released),
        (newer, false),
        "the newer lease is untouched"
    );
}

/// A duplicate result report is answered `AlreadyApplied` before the store looks at the lease: the live lease is still
/// given back (fenced), not left to expire.
#[tokio::test(start_paused = true)]
async fn a_duplicate_result_report_still_gives_its_live_lease_back() {
    let d = director_with(MemoryStore::strict());
    let m = id(1);
    let f1 = d.persistence.acquire("papa", m).await.unwrap();
    assert_eq!(
        d.persistence
            .record(m, "papa", f1, &some_result("papa"), Roster::default())
            .await,
        Ok(Recorded::Applied)
    );
    // The same match's profile holds a live lease again; a second report for the match arrives.
    let f2 = d.persistence.acquire("papa", m).await.unwrap();
    assert!(f2 > f1 && !store(&d).lease("papa").unwrap().released);
    d.inner
        .lock()
        .unwrap()
        .profiles
        .insert("papa".into(), seat(4, m, f2, SeatState::Seated));
    d.reports
        .send(RoomReport::Left {
            admission: None,
            match_id: m,
            profile: "papa".into(),
            fence: f2,
            result_pending: true,
        })
        .unwrap();
    d.reports
        .send(RoomReport::Result {
            bot_choices: Vec::new(),
            admission: None,
            match_id: m,
            profile: "papa".into(),
            bot: false,
            bot_spec: None,
            fence: Some(f2),
            result: some_result("papa"),
            roster: Roster::default(),
        })
        .unwrap();
    settle().await;
    settle().await;
    assert!(
        store(&d).lease("papa").unwrap().released,
        "lease given back"
    );
    assert!(entry_of(&d, "papa").is_none(), "profile free");
    assert_eq!(store(&d).results.lock().unwrap().len(), 1, "applied once");
    assert_eq!(d.inner.lock().unwrap().results_ok, 1);
}

#[tokio::test]
async fn a_report_of_the_seated_count_never_erases_admissions_still_acquiring_their_lease() {
    let d = director();
    let m = id(1);
    // Seven admissions picked this lobby; their leases are pending: reserved, none seated.
    d.inner
        .lock()
        .unwrap()
        .rooms
        .insert(m, entry(Phase::Lobby, 0, 7));
    let committed = |d: &Director| d.inner.lock().unwrap().rooms[&m].committed();
    assert_eq!(committed(&d), 7);
    // The first of them is seated: its reservation becomes a seat, the other six stay reserved.
    d.reports
        .send(RoomReport::Seated {
            match_id: m,
            phase: Phase::Lobby,
            humans: 1,
        })
        .unwrap();
    settle().await;
    assert_eq!(committed(&d), 7, "1 seated + 6 reserved");
    assert_eq!(d.inner.lock().unwrap().rooms[&m].reserved, 6);
    // A phase report carries no count at all and changes nothing else.
    d.reports
        .send(RoomReport::Phase {
            match_id: m,
            phase: Phase::Lobby,
        })
        .unwrap();
    settle().await;
    assert_eq!(committed(&d), 7);
    // So an eighth admission still fits and the ninth is not offered this lobby.
    let mut inner = d.inner.lock().unwrap();
    assert_eq!(
        open_lobby(&inner.rooms, rh_sim::constants::MAX_PLAYERS),
        Some(m)
    );
    inner.rooms.get_mut(&m).unwrap().reserved += 1;
    assert_eq!(
        open_lobby(&inner.rooms, rh_sim::constants::MAX_PLAYERS),
        None,
        "never over-offered"
    );
}
