//! Source-profile planning transport, catch-up from a visible step, resume after a result release and overload fairness.

use super::*;

/// The e2e transport on the admitted source profile: the lobby closes into planning, each owner receives its own
/// private deal as a complete-replacement State key (never another player's), picks and ready go through the ordinary
/// input frame and the run starts; the plan key clears when planning ends. Admission closes at planning (C-NET).
#[tokio::test(start_paused = true)]
async fn source_planning_transport_delivers_each_owner_plan_and_starts_the_run() {
    use rh_protocol::{WireCommand, WirePlan};
    let (mut room, _reports) = arena_room(0.25, 1.0);
    let start = Instant::now();
    let tick = Duration::from_millis(50);
    let a_out = Arc::new(Outbox::default());
    let b_out = Arc::new(Outbox::default());
    let a = room
        .join("a".into(), None, a_out.clone(), 1, 1, None)
        .unwrap();
    let b = room
        .join("b".into(), None, b_out.clone(), 2, 2, None)
        .unwrap();
    let mut at = start;
    for _ in 0..10 {
        at += tick;
        assert!(room.wake(at));
        if room.sim.phase() == Phase::Planning {
            break;
        }
    }
    assert_eq!(
        room.sim.phase(),
        Phase::Planning,
        "the source lobby closes into planning"
    );
    let a_plan: WirePlan = latest_plan_item(&drain(&a_out))
        .expect("the owner plan key was sent")
        .expect("the owner has a deal");
    let b_plan: WirePlan = latest_plan_item(&drain(&b_out))
        .expect("the other owner plan key was sent")
        .expect("the other owner has a deal");
    // Both options are resolved server facts: a pool index, a quantized position, a zone and a walk time.
    assert!(a_plan.spawns.iter().all(|s| s.is_some()));
    assert!(a_plan.options.iter().all(|o| o.is_some()));
    assert!(a_plan.ends_at > 0.);
    assert_ne!(
        a_plan.spawns, b_plan.spawns,
        "the two deals are private and disjoint"
    );
    // Admission closes at planning start.
    let late = Arc::new(Outbox::default());
    assert_eq!(
        room.join("late".into(), None, late, 3, 3, None).err(),
        Some(RejectCode::MatchFull)
    );
    // Picking and readying are ordinary validate+submit input commands (C-INPUT).
    at += tick;
    room.handle(
        RoomCmd::Input {
            received: std::time::Instant::now(),
            player: a.player,
            conn: 1,
            seq: 1,
            commands: vec![WireCommand::Plan {
                spawn: Some(0),
                boon: None,
                ready: None,
            }],
        },
        at,
    );
    let mut picked = None;
    for _ in 0..3 {
        at += tick;
        assert!(room.wake(at));
        if let Some(Some(plan)) = latest_plan_item(&drain(&a_out)) {
            if plan.pick.is_some() {
                picked = Some(plan.pick);
                break;
            }
        }
    }
    assert_eq!(picked, Some(Some(0)), "the owner's own pick is sent back");
    // Both seats ready: planning ends and the run starts.
    for (player, conn) in [(a.player, 1), (b.player, 2)] {
        at += tick;
        room.handle(
            RoomCmd::Input {
                received: std::time::Instant::now(),
                player,
                conn,
                seq: 2,
                commands: vec![WireCommand::Plan {
                    spawn: None,
                    boon: None,
                    ready: Some(true),
                }],
            },
            at,
        );
    }
    for _ in 0..3 {
        at += tick;
        assert!(room.wake(at));
        if room.sim.phase() == Phase::Active {
            break;
        }
    }
    assert_eq!(
        room.sim.phase(),
        Phase::Active,
        "every connected seat is ready"
    );
    let cleared = latest_plan_item(&drain(&a_out));
    assert!(
        matches!(cleared, Some(None)),
        "the private deal clears when planning ends"
    );
}

#[test]
fn catch_up_carries_a_real_wish_start_from_its_visible_step() {
    use rh_protocol::{WireCommand, WireFx};
    use rh_sim::{FactionId, Placement, Vec2};

    let now = Instant::now();
    let tick = Duration::from_millis(50);
    let (tx, _) = mpsc::unbounded_channel();
    let mut config = cfg(tick);
    config.sim.abilities = rh_sim::abilities::Mode::Full;
    let mut room = Room::new([9; 16], config, tx, now);
    let viewer_out = Arc::new(Outbox::default());
    let caster_out = Arc::new(Outbox::default());
    let viewer = room
        .join(
            "viewer".into(),
            Some(FactionId::Fire),
            viewer_out.clone(),
            1,
            1,
            None,
        )
        .unwrap();
    let caster = room
        .join(
            "caster".into(),
            Some(FactionId::Plague),
            caster_out.clone(),
            2,
            1,
            None,
        )
        .unwrap();
    room.sim = Sim::reset_active(
        SimConfig {
            abilities: rh_sim::abilities::Mode::Full,
            wish_lock_ms: 0.,
            ..Default::default()
        },
        rh_sim::arenas::practice_arena(),
        Secrets::from_seed(17),
        &[
            Placement {
                profile: "viewer".into(),
                faction: FactionId::Fire,
                bot: false,
                at: Vec2::new(400., 272.),
                hp: None,
                weapon0: None,
                weapon1: None,
            },
            Placement {
                profile: "caster".into(),
                faction: FactionId::Plague,
                bot: false,
                at: Vec2::new(1290., 272.),
                hp: None,
                weapon0: None,
                weapon1: None,
            },
        ],
    );
    for human in &mut room.humans {
        human.fx.rebind(&room.sim, human.player);
    }

    let mut tracker = rh_protocol::SnapshotTracker::new();
    assert!(room.wake(now + tick));
    let mut initial_frames = Vec::new();
    viewer_out.drain_into(&mut initial_frames);
    for frame in &initial_frames {
        tracker.decode(frame).unwrap();
    }
    let _ = drain(&caster_out);
    // The viewer is 890 units from the caster: visible at the cast's source step, then walks outside the 900-unit
    // radius during this single three-step wake. The cast is validated/submitted as a native command, then the Room's
    // normal per-step projector and publisher carry the genuine producer event to the connection.
    room.handle(
        RoomCmd::Input {
            received: std::time::Instant::now(),
            player: viewer.player,
            conn: 1,
            seq: 1,
            commands: vec![WireCommand::Move {
                x: 100.,
                y: 272.,
                seq: None,
            }],
        },
        now + tick,
    );
    room.sim
        .submit(
            caster.player,
            rh_sim::validate(rh_sim::RawCommand::Cast {
                slot: rh_sim::abilities::Slot::Wish,
                x: 1290.,
                y: 272.,
                id: None,
                t: None,
                seq: None,
            })
            .unwrap(),
        )
        .unwrap();
    assert!(room.wake(now + tick * 4));

    let mut catchup_frames = Vec::new();
    viewer_out.drain_into(&mut catchup_frames);
    let messages = catchup_frames
        .iter()
        .map(|frame| tracker.decode(frame).unwrap())
        .collect::<Vec<_>>();
    let mut saw_start = false;
    let mut final_actors = None;
    for message in messages {
        match message {
            ServerMsg::Fx { fx, .. } => {
                saw_start |= fx
                    .iter()
                    .any(|event| matches!(event, WireFx::WishStarted { ability: 1, .. }));
            }
            ServerMsg::Snapshot {
                phase: 1, actors, ..
            } => final_actors = Some(actors),
            _ => {}
        }
    }
    assert!(
        saw_start,
        "the cast was visible at its own step and carried to the wake frame"
    );
    assert!(final_actors.expect("final active snapshot").iter().all(|actor| actor.id != caster.player.0),
        "the caster is outside final visibility, so a final-world re-filter would drop the earlier cue");
    assert_eq!(room.frame_refusals, 0);
}

#[tokio::test(start_paused = true)]
async fn a_renewal_that_observes_the_result_release_preserves_reliable_resume() {
    let start = Instant::now();
    let tick = Duration::from_millis(50);
    let (reports, mut rx) = mpsc::unbounded_channel();
    let mut room = Room::new([7; 16], cfg(tick), reports, start);
    let (validity, deadline) = tokio::sync::watch::channel(start + Duration::from_secs(120));
    let (reply, joined) = oneshot::channel();
    room.handle(
        RoomCmd::JoinLeased {
            admission: Admission {
                gen: 1,
                profile_id: None,
            },
            profile: "Winner".into(),
            faction: None,
            outbox: Arc::new(Outbox::default()),
            conn: 1,
            fence: 4,
            deadline,
            reply,
        },
        start,
    );
    let winner = joined.await.unwrap().unwrap();
    let other = room
        .join(
            "Other".into(),
            None,
            Arc::new(Outbox::default()),
            2,
            1,
            None,
        )
        .unwrap();
    assert!(room.wake(start + tick));
    assert_eq!(room.sim.phase(), Phase::Active);
    room.leave(other.player, GoneReason::Left);
    assert!(room.wake(start + tick * 2));
    assert!(std::iter::from_fn(|| rx.try_recv().ok())
        .any(|r| matches!(r, RoomReport::Result{fence:Some(4),profile,..} if profile=="Winner")));
    // Durable record_result released this fence, but its director completion is still in
    // flight when renewal returns false. The room must still deliver the already earned result.
    validity.send_replace(start);
    let out = Arc::new(Outbox::default());
    let (reply, resumed) = oneshot::channel();
    room.handle(
        RoomCmd::Resume {
            session: winner.session,
            outbox: out.clone(),
            conn: 3,
            reply,
        },
        start + tick * 3,
    );
    assert_eq!(resumed.await.unwrap().unwrap().player, winner.player);
    let mut frames = Vec::new();
    let drained = out.drain_into(&mut frames);
    assert!(drained.close.is_none());
    assert!(frames.iter().any(|frame|matches!(rh_protocol::ServerMsg::decode(frame),
        Ok(rh_protocol::ServerMsg::Events{events,..}) if events.iter().any(|e|matches!(e,WireEvent::Result(_))))));
}

/// Under sustained overload (every wake takes 1.5 slots, so the next step is always already due when the loop looks
/// again), queued commands are still applied after each wake. The biased step-first select alone never polled the
/// inbox again: this join waited forever.
#[tokio::test(flavor = "multi_thread", worker_threads = 2)]
async fn an_overloaded_room_still_applies_queued_commands() {
    let tick = Duration::from_millis(20);
    let (reports, _keep) = mpsc::unbounded_channel();
    let metrics: &'static Metrics = Box::leak(Box::new(Metrics::new()));
    let mut room = Room::new([3; 16], cfg(tick), reports, Instant::now()).with_metrics(metrics);
    room.stall = tick * 3 / 2;
    let (tx, rx) = mpsc::channel(8);
    // Keeps the timer driver turning on the other worker while the room's worker is busy, so the room's re-armed
    // deadline is already elapsed for the timer wheel when the loop looks again (the starving case).
    let ticker = tokio::spawn(async {
        loop {
            tokio::time::sleep(Duration::from_millis(1)).await;
        }
    });
    let task = tokio::spawn(run_room(room, rx));
    let deadline = tokio::time::Instant::now() + Duration::from_secs(3);
    let result = async {
        while metrics.wakes.load(Ordering::Relaxed) < 3 {
            if tokio::time::Instant::now() >= deadline {
                return Err("room did not reach overload: fewer than 3 wakes before deadline");
            }
            tokio::time::sleep(Duration::from_millis(1)).await;
        }

        let (reply, got) = oneshot::channel();
        tx.send(RoomCmd::Join {
            profile: "alpha".into(),
            faction: None,
            outbox: Arc::new(Outbox::default()),
            conn: 1,
            fence: 1,
            reply,
        })
        .await
        .map_err(|_| "room command channel closed before the join was queued")?;
        let ok = tokio::time::timeout_at(deadline, got)
            .await
            .map_err(|_| "the join was not applied while the room was overloaded")?
            .map_err(|_| "room dropped the join reply")?
            .map_err(|_| "join was not seated")?;
        drop(ok);
        Ok::<(), &str>(())
    }
    .await;
    task.abort();
    ticker.abort();
    result.expect("overloaded room test condition");
}
