//! Admission against dead lobbies, leaving profiles, lease timeouts, cancellation and mailboxes.

use super::*;

/// A burst of real credential checks against a cold Source arena fits in two board rooms.
#[tokio::test(flavor = "multi_thread", worker_threads = 4)]
async fn concurrent_ordinary_joins_fill_two_cold_board_rooms() {
    let args = [
        "--runtime",
        "source-arena",
        "--map",
        concat!(
            env!("CARGO_MANIFEST_DIR"),
            "/../../maps/first-playable.json"
        ),
    ]
    .map(str::to_owned);
    let settings = crate::runtime::Settings::from_args(&args).unwrap().unwrap();
    let cfg = DirectorConfig {
        max_rooms: 2,
        fixed_board_secrets: Some(rh_sim::Secrets::from_seed(2)),
        ..DirectorConfig::new(settings.sim(), settings.arena().unwrap())
    };
    let seats = cfg.max_rooms * rh_sim::constants::MAX_PLAYERS;
    let profiles: Vec<_> = (0..=seats).map(|i| format!("player{i}")).collect();
    let auth = Auth::Static(
        profiles
            .iter()
            .map(|profile| (profile.clone(), format!("{profile}-ticket")))
            .collect(),
    );
    let d = Director::new(
        cfg,
        Arc::new(Persistence::Memory(Box::new(MemoryStore::strict()))),
        Arc::new(auth),
        Arc::new(Lifecycle::default()),
    );
    d.lifecycle.ready();
    assert_eq!(d.room_count(), 0);
    let credentialed_hello = |profile: &str| Hello {
        ticket: format!("{profile}-ticket").into_bytes(),
        ..hello(profile)
    };
    let start = Arc::new(tokio::sync::Barrier::new(seats + 1));
    let mut joins = tokio::task::JoinSet::new();
    for (i, profile) in profiles[..seats].iter().enumerate() {
        let (d, start, h) = (d.clone(), start.clone(), credentialed_hello(profile));
        joins.spawn(async move {
            start.wait().await;
            let result = d
                .admit(h, Arc::new(Outbox::default()), (i + 1) as u64)
                .await;
            (i, result)
        });
    }
    let (rooms, refused) = tokio::time::timeout(Duration::from_secs(40), async {
        start.wait().await;
        let mut rooms = HashMap::<MatchId, usize>::new();
        let mut refused = Vec::new();
        while let Some(outcome) = joins.join_next().await {
            let (i, result) = outcome.unwrap();
            match result {
                Ok((_tx, ok)) => {
                    let rh_protocol::ServerMsg::Welcome { match_id, .. } =
                        rh_protocol::ServerMsg::decode(&ok.welcome).unwrap()
                    else {
                        panic!("expected a Welcome for {}", profiles[i]);
                    };
                    *rooms.entry(match_id).or_default() += 1;
                }
                Err(error) => refused.push((&profiles[i], error)),
            }
        }
        (rooms, refused)
    })
    .await
    .expect("all first-join attempts completed");
    assert!(refused.is_empty(), "first-join refusals: {refused:?}");
    assert_eq!(rooms.len(), 2);
    assert!(rooms.values().all(|&n| n == rh_sim::constants::MAX_PLAYERS));
    let refused = d
        .admit(
            credentialed_hello(&profiles[seats]),
            Arc::new(Outbox::default()),
            (seats + 1) as u64,
        )
        .await
        .map(|_| ())
        .unwrap_err();
    assert_eq!(refused, (RejectCode::MatchFull, "no capacity".into()));
}

/// A lobby whose room task already ended (its last player left) is still in the map until its `Closed` report is
/// handled: an admission that picks it is moved to another room instead of being refused.
#[tokio::test]
async fn an_admission_that_picks_a_room_that_just_closed_lands_in_another() {
    let d = director();
    let dead = id(9);
    dead_lobby(&d, dead, 1);
    let (_tx, ok) = d
        .admit(hello("papa"), Arc::new(Outbox::default()), 1)
        .await
        .expect("admitted into a fresh lobby");
    assert_ne!(ok.session, [0; 16]);
    let inner = d.inner.lock().unwrap();
    assert_eq!(
        inner.rooms[&dead].phase,
        Phase::Active,
        "never offered again"
    );
    assert_eq!(inner.rooms.len(), 2);
}

/// The dead-lobby retry takes its new lease only after the failed attempt's give-back landed. With durable lease rules
/// (a live lease of another match refuses the acquire) and a give-back that takes a round trip, the retry used to race
/// it and refuse a free player `ProfileInUse`; deterministic on this single-threaded runtime.
#[tokio::test(start_paused = true)]
async fn a_dead_lobby_retry_waits_for_its_own_lease_give_back() {
    for delay in [Duration::ZERO, Duration::from_millis(40)] {
        let d = director_with(MemoryStore {
            release_delay: delay,
            ..MemoryStore::strict()
        });
        let dead = id(9);
        dead_lobby(&d, dead, 1);
        let t0 = tokio::time::Instant::now();
        let (_tx, ok) = d
            .admit(hello("papa"), Arc::new(Outbox::default()), 1)
            .await
            .expect("a free player is admitted, never refused ProfileInUse");
        assert_eq!(t0.elapsed(), delay, "it waited exactly for the give-back");
        let lease = store(&d).lease("papa").expect("leased");
        assert_eq!(
            lease.fence, 2,
            "the dead lobby's lease (1) was given back first"
        );
        assert!(!lease.released);
        assert_ne!(lease.match_id, dead);
        let e = entry_of(&d, "papa").expect("seated");
        assert_eq!(e.state, SeatState::Seated);
        assert_eq!((e.fence, e.match_id), (2, lease.match_id));
        drop(ok);
    }
}

/// LEAVING_WAIT bounds the whole admission, not each retry: with give-backs that take 4 s and two dead lobbies, the
/// second wait ends at the shared deadline (6 s after the admission began), not 4 s later.
#[tokio::test(start_paused = true)]
async fn leaving_wait_is_one_deadline_for_the_whole_admission() {
    let d = director_with(MemoryStore {
        release_delay: Duration::from_secs(4),
        ..MemoryStore::strict()
    });
    dead_lobby(&d, id(9), 3);
    dead_lobby(&d, id(8), 2);
    dead_lobby(&d, id(7), 1);
    let t0 = tokio::time::Instant::now();
    let r = d.admit(hello("papa"), Arc::new(Outbox::default()), 1).await;
    assert!(
        matches!(r, Err((RejectCode::ProfileInUse, _))),
        "refused at the bound"
    );
    assert_eq!(
        t0.elapsed(),
        LEAVING_WAIT,
        "the real bound is the documented one"
    );
    // The second dead lobby's give-back still lands and frees the entry it owns.
    tokio::time::sleep(Duration::from_secs(4)).await;
    settle().await;
    assert!(entry_of(&d, "papa").is_none());
    assert!(store(&d).lease("papa").expect("row").released);
}

#[tokio::test(start_paused = true)]
async fn an_admission_waits_for_a_leaving_profile_but_not_for_a_seated_one() {
    let d = director();
    let m = id(1);
    d.inner
        .lock()
        .unwrap()
        .profiles
        .insert("papa".into(), seat(1, m, 1, SeatState::Leaving));
    d.inner
        .lock()
        .unwrap()
        .profiles
        .insert("quebec".into(), seat(2, m, 1, SeatState::Seated));
    d.inner
        .lock()
        .unwrap()
        .profiles
        .insert("romeo".into(), seat(3, m, 0, SeatState::Reserved));
    let soon = || tokio::time::Instant::now() + LEAVING_WAIT;
    // A live seat, or another admission in flight, is not waited for: the caller refuses it.
    for k in ["quebec", "romeo"] {
        tokio::time::timeout(
            Duration::from_millis(1),
            d.wait_while_leaving(&ProfileKey::from(k), soon()),
        )
        .await
        .expect("returns at once");
    }
    // A leaving profile is waited for until it is freed.
    let waiter = {
        let d = d.clone();
        let deadline = soon();
        tokio::spawn(async move { d.wait_while_leaving(&"papa".into(), deadline).await })
    };
    settle().await;
    assert!(!waiter.is_finished(), "still mid-release");
    d.free(&"papa".into(), 1);
    waiter.await.unwrap();
    assert!(entry_of(&d, "papa").is_none());
    // A release that never lands ends the wait at the bound (the caller then refuses as in use).
    d.inner
        .lock()
        .unwrap()
        .profiles
        .insert("papa".into(), seat(4, m, 1, SeatState::Leaving));
    let t0 = tokio::time::Instant::now();
    d.wait_while_leaving(&"papa".into(), soon()).await;
    assert_eq!(t0.elapsed(), LEAVING_WAIT);
    // Another owner's free does not free this entry.
    d.free(&"papa".into(), 1);
    assert!(entry_of(&d, "papa").is_some());
}

#[tokio::test(start_paused = true)]
async fn cancelling_after_join_was_seated_retires_the_exact_seat_and_reservation() {
    let d = director_with(MemoryStore::strict());
    let m = id(1);
    let (tx, mut rx) = mpsc::channel(4);
    d.inner.lock().unwrap().rooms.insert(
        m,
        RoomEntry {
            tx,
            phase: Phase::Lobby,
            seated: 0,
            reserved: 0,
            public: PublicRoom::default(),
        },
    );
    let now = tokio::time::Instant::now();
    let mut room = Room::new(
        m,
        RoomConfig {
            sim: d.cfg.sim.clone(),
            arena: d.map.arena.clone(),
            welcome_map: d.map.welcome_map.clone(),
            fill_bots_to: 0,
            tick: d.cfg.tick,
            record: false,
            fixed_secrets: None,
            fixed_bot_seed: None,
            reconnect_grace: d.cfg.reconnect_grace,
            linger: d.cfg.linger,
        },
        d.reports.clone(),
        now,
    );
    let cloned = d.clone();
    let out = Arc::new(Outbox::default());
    let connection = out.clone();
    let attempt = tokio::spawn(async move { cloned.admit(hello("Archer"), connection, 1).await });
    let mut command = rx.recv().await.expect("join");
    let RoomCmd::JoinLeased { reply, .. } = &mut command else {
        panic!("expected leased join")
    };
    let (intercept, joined) = oneshot::channel();
    let delayed = std::mem::replace(reply, intercept);
    room.handle(command, now);
    let granted = joined.await.unwrap().expect("real room seated the human");
    attempt.abort();
    assert!(attempt.await.is_err());
    let _ = delayed.send(Ok(granted));
    settle().await;
    room.wake(now + d.cfg.tick);
    settle().await;
    assert_eq!(out.drain_into(&mut vec![]).close, Some(RejectCode::Kicked));
    assert!(entry_of(&d, "archer").is_none());
    assert!(store(&d).lease("Archer").unwrap().released);
    assert!(store(&d).results.lock().unwrap().is_empty());
    assert!(d
        .admit(hello("Archer"), Arc::new(Outbox::default()), 2)
        .await
        .is_ok());
}

/// A join reply that arrives after the room's `Closed` was handled does not leave a session entry for the dead room.
#[tokio::test(start_paused = true)]
async fn a_join_reply_after_the_rooms_close_registers_no_session() {
    let d = director();
    let m = id(1);
    let (tx, mut rx) = mpsc::channel(4);
    d.inner.lock().unwrap().rooms.insert(
        m,
        RoomEntry {
            tx,
            phase: Phase::Lobby,
            seated: 0,
            reserved: 0,
            public: PublicRoom::default(),
        },
    );
    let d2 = d.clone();
    let admit = tokio::spawn(async move {
        d2.admit(hello("papa"), Arc::new(Outbox::default()), 1)
            .await
    });
    let Some(RoomCmd::JoinLeased { reply, .. }) = rx.recv().await else {
        panic!("a join");
    };
    // The room closed (crashed) before it replied.
    d.reports
        .send(RoomReport::Closed {
            match_id: m,
            stats: Default::default(),
            final_hash: 0,
            replay: None,
        })
        .unwrap();
    settle().await;
    assert!(d.inner.lock().unwrap().rooms.is_empty());
    let _ = reply.send(Ok(JoinOk {
        player: rh_sim::PlayerId(1),
        session: [9; 16],
        welcome: Vec::new(),
        clock: Arc::new(std::sync::atomic::AtomicU64::new(0)),
    }));
    assert!(admit.await.unwrap().is_ok());
    settle().await;
    assert!(
        d.inner.lock().unwrap().sessions.is_empty(),
        "no session for a gone room"
    );
}

/// A storage failure while taking the lease is not "callsign already in a match": the reason is accurate and the code
/// is not `ProfileInUse`; the profile is free again immediately.
#[tokio::test(start_paused = true)]
async fn a_lease_acquisition_timeout_is_not_reported_as_profile_in_use() {
    let d = director_with(MemoryStore {
        acquire_delay: Duration::from_secs(3600),
        ..MemoryStore::strict()
    });
    let (code, why) = d
        .admit(hello("papa"), Arc::new(Outbox::default()), 1)
        .await
        .map(|_| ())
        .unwrap_err();
    assert_ne!(code, RejectCode::ProfileInUse);
    assert_eq!(code, RejectCode::RateLimited);
    assert!(
        why == "server busy, storage unavailable; try again shortly",
        "{why}"
    );
    assert!(
        entry_of(&d, "papa").is_none(),
        "the reservation was unwound"
    );
}

/// A real conflict (the profile holds a live lease for another match) is still `ProfileInUse`.
#[tokio::test(start_paused = true)]
async fn a_held_lease_is_still_profile_in_use() {
    let d = director_with(MemoryStore::strict());
    d.persistence.acquire("papa", id(9)).await.unwrap();
    let (code, _) = d
        .admit(hello("papa"), Arc::new(Outbox::default()), 1)
        .await
        .map(|_| ())
        .unwrap_err();
    assert_eq!(code, RejectCode::ProfileInUse);
}

/// One panic inside a director critical section must not brick admission, `/info` and the control API.
#[tokio::test(start_paused = true)]
async fn a_poisoned_director_lock_is_recovered_not_fatal() {
    let d = director_with(MemoryStore::strict());
    let d2 = d.clone();
    let panicked = std::thread::spawn(move || {
        let _guard = d2.inner.lock().unwrap();
        panic!("test: a panic while the director lock is held");
    })
    .join();
    assert!(panicked.is_err());
    assert!(d.inner.is_poisoned());
    assert_eq!(d.info()["rooms"], 0, "info still answers");
    assert!(
        !d.inner.is_poisoned(),
        "recovery clears the flag (one log line per incident)"
    );
    d.admit(hello("papa"), Arc::new(Outbox::default()), 1)
        .await
        .expect("admission still works");
    assert_eq!(d.crash_info()["total"], 0);
}

#[tokio::test(start_paused = true)]
async fn a_full_room_mailbox_is_a_503_and_a_closed_one_means_nobody_to_tell() {
    let (tx, rx) = mpsc::channel::<RoomCmd>(1);
    tx.send(RoomCmd::Drain).await.unwrap();
    let e = reserve_room_slot(&tx, Duration::from_secs(2))
        .await
        .map(|_| ())
        .unwrap_err();
    assert_eq!((e.status, e.code), (503, "room_busy"));
    // Space frees: the reservation succeeds and its send cannot fail.
    let mut rx = rx;
    rx.recv().await.unwrap();
    let permit = reserve_room_slot(&tx, Duration::from_secs(2))
        .await
        .unwrap()
        .expect("slot");
    permit.send(RoomCmd::Drain);
    assert!(matches!(rx.recv().await, Some(RoomCmd::Drain)));
    drop(rx);
    assert!(reserve_room_slot(&tx, Duration::from_secs(2))
        .await
        .unwrap()
        .is_none());
}
