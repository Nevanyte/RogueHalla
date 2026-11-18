//! The source-arena profile on the wire: lobby, planning and active phases, owner plan state, Wish fx and arena events.

use super::*;

fn arena_env() -> Env {
    let arena = source_arena();
    Env::new(
        EnvConfig {
            arena,
            sim: SimConfig {
                lifecycle: rh_sim::ArenaMode::Source(rh_sim::ArenaRules::default()),
                min_players: 2,
                lobby_seconds: 0.,
                fill_seconds: 0.,
                ..Default::default()
            },
            max_ticks: 100,
            record: false,
        },
        (0..2)
            .map(|i| AgentSpec {
                profile: format!("p{i}"),
                faction: FactionId::Holy,
                bot: false,
                agent: Agent::External,
            })
            .collect(),
        12,
    )
}

fn arena_plan_command(ready: bool) -> rh_sim::PublicCommand {
    rh_sim::PublicCommand::Plan {
        spawn: Some(0),
        boon: None,
        ready: Some(ready),
    }
}

fn state_items(frames: &[u8]) -> Vec<StateItem> {
    let ServerMsg::State { items, .. } = ServerMsg::decode(frames).expect("state frame") else {
        panic!("not a state frame")
    };
    items
}

fn plan_of(items: &[StateItem]) -> Option<WirePlan> {
    items.iter().find_map(|item| match item {
        StateItem::Plan(plan) => plan.clone(),
        _ => None,
    })
}

fn arena_of(items: &[StateItem]) -> Option<WireArena> {
    items.iter().find_map(|item| match item {
        StateItem::Arena(arena) => *arena,
        _ => None,
    })
}

/// The source lifecycle's Lobby -> Planning -> Active phases and the owner-plan/public-arena transport encode; the
/// teleport event is the source's room-wide fx, never a refused frame.
#[test]
fn arena_profile_transports_lobby_planning_and_active_with_owner_plan_state() {
    let mut env = arena_env();
    let viewer = env.observations()[0].me.as_ref().unwrap().id;
    let mut publisher = Publisher::default();
    let mut observer = rh_sim::ItemObserver::new(viewer);
    let mut collections = crate::wire_state::CollectionPublisher::default();
    for expected in [Phase::Lobby, Phase::Planning, Phase::Active] {
        assert_eq!(env.sim.phase(), expected);
        let mut earlier = vec![SeenEvent::Teleported];
        let frames = publisher.frames(&env.sim, viewer, 0, &[], &mut earlier, &[]);
        assert!(frames.refused.is_none());
        assert!(frames.snapshot.is_ok());
        // The source teleport presentation effect maps to the wire's Teleport fx.
        let ServerMsg::Fx { fx, .. } =
            ServerMsg::decode(frames.fx.as_ref().expect("teleport fx")).unwrap()
        else {
            panic!("not an fx frame")
        };
        assert_eq!(fx, vec![WireFx::Teleport]);
        assert!(publisher
            .frames_prepared(&mut observer, None, &env.sim, 0, &[], &mut earlier, &[])
            .is_ok());
        let obs = rh_sim::observe(&env.sim, viewer, &[]);
        let snapshot = checked_snapshot(&obs, 0).expect("phase is wire-encodable");
        let ServerMsg::Snapshot { phase, .. } = &snapshot else {
            panic!("not a snapshot")
        };
        assert_eq!(*phase, checked_phase(expected).unwrap());
        assert!(snapshot.try_encode().is_ok());
        let pending = collections
            .prepare(publisher.observation(), false, false, false)
            .expect("plan/arena state encodes");
        let items = state_items(pending.frame.as_ref().expect("full state after welcome"));
        let plan = plan_of(&items);
        let arena = arena_of(&items).expect("source arena view");
        assert_eq!(arena.lobby_ends_at, 0.);
        match expected {
            Phase::Lobby => {
                assert!(plan.is_none());
                assert_eq!(arena.started_with, 0);
                assert_eq!(arena.raid_started_at, 0.);
            }
            Phase::Planning => {
                let plan = plan.expect("the owner's private plan");
                assert!(plan.ends_at >= 0.);
                // The admitted source map deals a real ordered pool; the server resolves both option facts.
                assert!(plan.spawns.iter().all(|s| s.is_some()));
                assert!(plan.options.iter().all(|o| o.is_some()));
                for option in plan.options.iter().flatten() {
                    assert!((1..=5).contains(&option.zone));
                    assert!(option.walk_seconds.is_some());
                }
                assert!(!plan.ready);
                assert_eq!(arena.plan_ends_at, plan.ends_at);
            }
            Phase::Active => {
                assert!(plan.is_none(), "the private plan clears when planning ends");
                assert!(arena.raid_started_at > 0.);
                assert!(arena.started_with >= 2);
            }
            Phase::Ended => unreachable!(),
        }
        pending.commit(&mut collections);
        if expected == Phase::Lobby {
            env.step(&[]);
        } else if expected == Phase::Planning {
            env.step(&[
                vec![arena_plan_command(true)],
                vec![arena_plan_command(true)],
            ]);
        }
    }
}

#[test]
fn full_wish_owner_and_barrier_snapshot_encode_with_arena_off() {
    let mut sim = rh_sim::Sim::new(
        SimConfig {
            quests: false,
            abilities: rh_sim::abilities::Mode::Full,
            lifecycle: rh_sim::ArenaMode::Off,
            min_players: 2,
            lobby_seconds: 0.,
            fill_seconds: 0.,
            ..Default::default()
        },
        rh_sim::arenas::practice_arena(),
        rh_sim::Secrets::from_seed(12),
    );
    let viewer = sim.add_player("p0", Some(FactionId::Holy), false).unwrap();
    sim.add_player("p1", Some(FactionId::Holy), false).unwrap();
    let mut publisher = Publisher::default();
    let mut observer = rh_sim::ItemObserver::new(viewer);
    for expected in [Phase::Lobby, Phase::Active] {
        assert_eq!(sim.phase(), expected);
        assert!(profile_supported(sim.config()).is_ok());
        let mut earlier = vec![SeenEvent::Stagger { id: viewer }];
        let frames = publisher.frames(&sim, viewer, 0, &[], &mut earlier, &[]);
        assert!(frames.snapshot.is_ok());
        assert!(frames.fx.is_some());
        assert!(publisher
            .frames_prepared(&mut observer, None, &sim, 0, &[], &mut earlier, &[])
            .is_ok());
        let obs = rh_sim::observe(&sim, viewer, &[]);
        assert!(obs.me.as_ref().unwrap().wish.is_some());
        assert!(obs.arena.is_none() && obs.plan.is_none());
        let direct = checked_snapshot(&obs, 0).expect("full Wish owner fact is supported");
        assert!(!direct.try_encode().unwrap().is_empty());
        assert!(matches!(
            direct,
            ServerMsg::Snapshot { server_time, .. } if server_time == obs.server_time
        ));
        if expected == Phase::Lobby {
            sim.step();
        }
    }
}

#[test]
fn a_prerequisite_refusal_uses_the_existing_owner_toast_without_poisoning_frames() {
    let env = seed1_match(1);
    let viewer = env.sim.players()[0].id;
    let event = SeenEvent::ChestRefused {
        reason: rh_sim::reward_chests::Refusal::Needs(rh_sim::cards::BaseStatId::Ad),
    };
    assert!(
        matches!(to_wire(&event), Wire::Event(WireEvent::Toast { text }) if text == "Needs Offense")
    );
    let mut publisher = Publisher::default();
    let mut earlier = vec![event];
    let frames = publisher.frames(&env.sim, viewer, 0, &[], &mut earlier, &[]);
    assert!(frames.snapshot.is_ok() && frames.refused.is_none());
    let ServerMsg::Events { events, .. } = ServerMsg::decode(&frames.important.unwrap()).unwrap()
    else {
        panic!()
    };
    assert_eq!(
        events,
        vec![WireEvent::Toast {
            text: "Needs Offense".into()
        }]
    );
    assert!(earlier.is_empty());
    assert!(publisher
        .frames(&env.sim, viewer, 0, &[], &mut earlier, &[])
        .snapshot
        .is_ok());
}

#[test]
fn wish_start_and_landing_encode_as_checked_fx() {
    let env = seed1_match(1);
    let viewer = env.sim.players()[0].id;
    let mut publisher = Publisher::default();
    for event in [
        SeenEvent::CastStarted {
            event_time: 100.0,
            id: viewer,
            ability: rh_sim::status_runtime::AbilityId::Freeze,
            ms: 250.,
            target: None,
            at: Some(rh_sim::Vec2::new(2.0, 3.0)),
        },
        SeenEvent::CastLanded {
            event_time: 100.0,
            id: Some(viewer),
            ability: rh_sim::status_runtime::AbilityId::Freeze,
            target: None,
            at: None,
            to: None,
        },
    ] {
        assert!(matches!(to_wire(&event), Wire::Fx(_)));
        let frames = publisher.frames(&env.sim, viewer, 0, &[], &mut vec![event], &[]);
        assert!(frames.fx.is_some());
        assert!(frames.snapshot.is_ok());
        assert!(frames.refused.is_none());
    }
    assert!(matches!(
        to_wire(&SeenEvent::CastStarted {
            event_time: 100.0,
            id: viewer,
            ability: rh_sim::status_runtime::AbilityId::Freeze,
            ms: 250.0,
            target: None,
            at: Some(rh_sim::Vec2::new(-1.0, 3.0)),
        }),
        Wire::Refused("wish position")
    ));
}

#[test]
fn wish_points_check_source_f64_before_narrowing() {
    let max = f64::from(POS_MAX);
    assert_eq!(
        wire_point(Some(rh_sim::Vec2::new(max, max))),
        Ok(Some(WirePoint {
            x: POS_MAX,
            y: POS_MAX
        }))
    );
    let rounds_to_max = max + 0.0001;
    assert_eq!(
        rounds_to_max as f32, POS_MAX,
        "regression input must narrow back into range"
    );
    assert_eq!(
        wire_point(Some(rh_sim::Vec2::new(rounds_to_max, max))),
        Err("wish position")
    );
    let negative_underflow = -f64::MIN_POSITIVE;
    assert_eq!(
        negative_underflow as f32, -0.0,
        "regression input must underflow to negative zero"
    );
    assert_eq!(
        wire_point(Some(rh_sim::Vec2::new(negative_underflow, 1.0))),
        Err("wish position")
    );
    assert_eq!(
        wire_point(Some(rh_sim::Vec2::new(f64::NAN, 1.0))),
        Err("wish position")
    );
}

#[test]
fn arena_events_map_to_transport_and_off_profiles_still_encode() {
    let env = seed1_match(1);
    let viewer = env.sim.players()[0].id;
    let mut publisher = Publisher::default();
    let frames = publisher.frames(&env.sim, viewer, 7, &[], &mut Vec::new(), &[]);
    assert!(frames.snapshot.is_ok() && frames.refused.is_none());
    let obs = publisher.observation();
    assert!(checked_snapshot(obs, 7).unwrap().try_encode().is_ok());
    // Protocol 8 carries the planning phase and the source lifecycle profile (Wishes remain refused).
    assert_eq!(checked_phase(Phase::Planning), Ok(3));
    let mut planning = obs.clone();
    planning.phase = Phase::Planning;
    assert!(checked_snapshot(&planning, 7).unwrap().try_encode().is_ok());
    assert!(profile_supported(&SimConfig {
        lifecycle: rh_sim::ArenaMode::Source(Default::default()),
        ..Default::default()
    })
    .is_ok());
    // The source teleport is a room-wide presentation fx.
    assert!(matches!(
        to_wire(&SeenEvent::Teleported),
        Wire::Fx(WireFx::Teleport)
    ));
    let frames = publisher.frames(
        &env.sim,
        viewer,
        7,
        &[],
        &mut vec![SeenEvent::Teleported],
        &[],
    );
    assert!(frames.snapshot.is_ok() && frames.refused.is_none());
    let ServerMsg::Fx { fx, .. } =
        ServerMsg::decode(frames.fx.as_ref().expect("teleport fx")).unwrap()
    else {
        panic!("not an fx frame")
    };
    assert_eq!(fx, vec![WireFx::Teleport]);
    // The owner's deal is a complete-replacement State key, never a one-shot event.
    let plan = SeenEvent::Plan(rh_sim::PlanView {
        ends_at: 1.,
        spawns: [0, 1],
        taken: [false, false],
        pick: -1,
        boons: Vec::new(),
        boon: -1,
    });
    assert!(matches!(to_wire(&plan), Wire::Omitted));
    let frames = publisher.frames(&env.sim, viewer, 7, &[], &mut vec![plan], &[]);
    assert!(frames.snapshot.is_ok() && frames.refused.is_none());
    assert!(frames.important.is_none() && frames.fx.is_none());
    let mut observer = rh_sim::ItemObserver::new(viewer);
    let (frames, offer) = publisher
        .frames_prepared(&mut observer, None, &env.sim, 7, &[], &mut Vec::new(), &[])
        .unwrap();
    assert!(frames.snapshot.is_ok() && frames.refused.is_none());
    offer.commit();
}
