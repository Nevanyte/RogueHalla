//! Live pickup handles and resume offers, and the failed-publish barrier on a real room.

use super::*;

#[tokio::test(start_paused = true)]
async fn live_pickup_resolves_delivered_handles_and_resume_replaces_the_offer() {
    use rh_protocol::{PickupHandle, ServerMsg, StateItem, WireCommand, WirePropKind};
    use rh_sim::{ContainerKind, Placement, Vec2, WeaponId};
    fn drain(out: &Outbox) -> Vec<ServerMsg> {
        let mut frames = Vec::new();
        assert_eq!(out.drain_into(&mut frames).close, None);
        frames
            .iter()
            .map(|f| ServerMsg::decode(f).unwrap())
            .collect()
    }
    fn props(messages: &[ServerMsg]) -> rh_protocol::WireProps {
        messages
            .iter()
            .filter_map(|m| match m {
                ServerMsg::State { items, .. } => Some(items),
                _ => None,
            })
            .flatten()
            .find_map(|s| match s {
                StateItem::Props(p) => Some(p.clone()),
                _ => None,
            })
            .unwrap()
    }
    let start = Instant::now();
    let tick = Duration::from_millis(50);
    let (reports, _rx) = mpsc::unbounded_channel();
    let mut room = Room::new([8; 16], cfg(tick), reports, start);
    let out = Arc::new(Outbox::default());
    let joined = room
        .join("viewer".into(), None, out.clone(), 1, 1, None)
        .unwrap();
    room.join(
        "other".into(),
        None,
        Arc::new(Outbox::default()),
        2,
        1,
        None,
    )
    .unwrap();
    room.sim = Sim::reset_active(
        SimConfig::default(),
        rh_sim::arenas::practice_arena(),
        Secrets::from_seed(7),
        &[
            Placement {
                profile: "viewer".into(),
                faction: FactionId::Holy,
                bot: false,
                at: Vec2::new(100., 272.),
                hp: None,
                weapon0: None,
                weapon1: None,
            },
            Placement {
                profile: "other".into(),
                faction: FactionId::Fire,
                bot: false,
                at: Vec2::new(1400., 272.),
                hp: None,
                weapon0: None,
                weapon1: None,
            },
        ],
    );
    room.sim
        .seed_weapon_container(
            ContainerKind::Ground,
            Vec2::new(1300., 272.),
            &[WeaponId::Bow],
        )
        .unwrap();
    let sword_id = room
        .sim
        .seed_weapon_container(
            ContainerKind::Ground,
            Vec2::new(100., 272.),
            &[WeaponId::Sword],
        )
        .unwrap();
    room.sim
        .seed_weapon_container(
            ContainerKind::Ground,
            Vec2::new(116., 272.),
            &[WeaponId::Axe],
        )
        .unwrap();
    for h in &mut room.humans {
        h.fx.rebind(&room.sim, h.player);
        h.items = rh_sim::ItemObserver::new(h.player);
    }
    room.publish(&[]);
    let initial = drain(&out);
    let first = props(&initial);
    let sword = first
        .rows
        .iter()
        .find(|r| r.kind == WirePropKind::Ground(0))
        .unwrap()
        .handle;
    assert_ne!(
        sword.get(),
        sword_id,
        "wire input carries an observer reference"
    );
    room.handle(
        RoomCmd::Input {
            received: std::time::Instant::now(),
            player: joined.player,
            conn: 1,
            seq: 1,
            commands: vec![WireCommand::Pickup { handle: sword }],
        },
        start,
    );
    assert!(room.wake(start + tick));
    let picked = drain(&out);
    assert!(picked.iter().any(|m| matches!(m, ServerMsg::State {items,..} if items.iter().any(|i| matches!(i,StateItem::Inventory(Some(v)) if v.slots.contains(&Some(0)))))));
    assert!(!props(&picked)
        .rows
        .iter()
        .any(|r| r.kind == WirePropKind::Ground(0)));
    room.handle(
        RoomCmd::Input {
            received: std::time::Instant::now(),
            player: joined.player,
            conn: 1,
            seq: 2,
            commands: vec![WireCommand::Pickup { handle: sword }],
        },
        start + tick,
    );
    assert!(room.wake(start + tick * 2));
    assert!(drain(&out).iter().any(|m| matches!(m, ServerMsg::Events {events,..} if events.iter().any(|e| matches!(e,rh_protocol::WireEvent::PickupRefused {reason:2})))));

    let fresh = Arc::new(Outbox::default());
    let (reply, resumed) = oneshot::channel();
    room.handle(
        RoomCmd::Resume {
            session: joined.session,
            outbox: fresh.clone(),
            conn: 3,
            reply,
        },
        start + tick * 2,
    );
    assert_eq!(resumed.await.unwrap().unwrap().player, joined.player);
    room.publish(&[]);
    let resumed_props = props(&drain(&fresh));
    assert!(resumed_props
        .rows
        .iter()
        .all(|r| !first.rows.iter().any(|old| old.handle == r.handle)));
    let axe = resumed_props
        .rows
        .iter()
        .find(|r| r.kind == WirePropKind::Ground(4))
        .unwrap()
        .handle;
    room.handle(
        RoomCmd::Input {
            received: std::time::Instant::now(),
            player: joined.player,
            conn: 1,
            seq: 999,
            commands: vec![WireCommand::Pickup { handle: axe }],
        },
        start + tick * 2,
    );
    assert_eq!(
        room.humans[0].ack_input, 2,
        "replaced connection cannot act on the fresh offer"
    );
    room.handle(
        RoomCmd::Input {
            received: std::time::Instant::now(),
            player: joined.player,
            conn: 3,
            seq: 3,
            commands: vec![WireCommand::Pickup {
                handle: PickupHandle::new(sword.get()).unwrap(),
            }],
        },
        start + tick * 2,
    );
    assert!(room.wake(start + tick * 3));
    assert!(drain(&fresh).iter().any(|m| matches!(m, ServerMsg::Events {events,..} if events.iter().any(|e| matches!(e,rh_protocol::WireEvent::PickupRefused {reason:2})))));
    assert_eq!(room.frame_refusals, 0);
}

#[tokio::test(start_paused = true)]
async fn failed_publish_holds_wish_barrier_owner_combat_and_new_pickup_handles() {
    use rh_protocol::{ServerMsg, StateItem, WirePropKind};
    use rh_sim::{ContainerKind, FactionId, Placement, Vec2, WeaponId};

    let start = Instant::now();
    let tick = Duration::from_millis(50);
    let (reports, _rx) = mpsc::unbounded_channel();
    let mut config = cfg(tick);
    config.sim.abilities = rh_sim::abilities::Mode::Full;
    let mut room = Room::new([12; 16], config, reports, start);
    let stale = Arc::new(Outbox::default());
    let joined = room
        .join(
            "owner".into(),
            Some(FactionId::Fire),
            stale.clone(),
            1,
            1,
            None,
        )
        .unwrap();
    room.join(
        "peer".into(),
        Some(FactionId::Holy),
        Arc::new(Outbox::default()),
        2,
        1,
        None,
    )
    .unwrap();
    room.sim = Sim::reset_active(
        SimConfig {
            abilities: rh_sim::abilities::Mode::Full,
            ..Default::default()
        },
        rh_sim::arenas::practice_arena(),
        Secrets::from_seed(29),
        &[
            Placement {
                profile: "owner".into(),
                faction: FactionId::Fire,
                bot: false,
                at: Vec2::new(100., 272.),
                hp: None,
                weapon0: None,
                weapon1: None,
            },
            Placement {
                profile: "peer".into(),
                faction: FactionId::Holy,
                bot: false,
                at: Vec2::new(1400., 272.),
                hp: None,
                weapon0: None,
                weapon1: None,
            },
        ],
    );
    // Transport-only bootstrap of a checkpoint shape already admitted by rh-sim (timerless seeded barrier). This
    // deliberately does not claim a gameplay barrier producer; it gives the enqueue guard an unmistakably nonzero
    // source barrier/ring to retain across the failed and resumed snapshots.
    let mut admitted = serde_json::to_value(&room.sim).unwrap();
    admitted["world"]["players"][0]["core"]["vitals"]["barrier"] = serde_json::json!(30.0);
    admitted["world"]["players"][0]["barrier_until"] = serde_json::json!(0.0);
    admitted["world"]["players"][0]["barrier_decays"] = serde_json::json!(false);
    room.sim = Sim::restore(&admitted).expect("admitted timerless barrier checkpoint form");
    room.sim
        .seed_weapon_container(
            ContainerKind::Ground,
            Vec2::new(100., 272.),
            &[WeaponId::Sword],
        )
        .unwrap();
    room.sim
        .seed_weapon_container(
            ContainerKind::Ground,
            Vec2::new(116., 272.),
            &[WeaponId::Axe],
        )
        .unwrap();
    for seat in &mut room.humans {
        seat.fx.rebind(&room.sim, seat.player);
        seat.items = rh_sim::ItemObserver::new(seat.player);
    }

    // The first reliable offer commits normal handles and the complete Wish, barrier and combat owner fields.
    room.publish(&[]);
    let initial = decode(&stale);
    let initial_props = initial
        .iter()
        .find_map(|m| match m {
            ServerMsg::State { items, .. } => items.iter().find_map(|item| match item {
                StateItem::Props(p) => Some(p.clone()),
                _ => None,
            }),
            _ => None,
        })
        .expect("initial public props");
    let initial_combat = initial
        .iter()
        .find_map(|m| match m {
            ServerMsg::Snapshot {
                me: Some(me),
                owner_combat: Some(group),
                ..
            } if me.wish.is_some() && me.barrier == 30. && me.barrier_ring_step > 0 => {
                Some(*group)
            }
            _ => None,
        })
        .expect("the actual prepared owner group accompanies Wish and barrier");
    let max_initial = initial_props
        .rows
        .iter()
        .map(|row| row.handle.get())
        .max()
        .unwrap();

    // A newly visible row, owner data, and barrier pair are all staged for a real publish. A closed reliable queue
    // rejects the whole batch; no candidate action handle can resolve before the fresh connection accepts it.
    room.sim
        .seed_weapon_container(
            ContainerKind::Ground,
            Vec2::new(108., 272.),
            &[WeaponId::Bow],
        )
        .unwrap();
    stale.close(RejectCode::TooSlow);
    room.publish(&[]);
    let missing_pickup = rh_sim::validate(rh_sim::RawCommand::MissingPickup).unwrap();
    for raw in max_initial + 1..=max_initial + 3 {
        let proposal = rh_sim::PublicCommand::Pickup {
            handle: rh_sim::PickupHandle::new(raw).unwrap(),
        };
        assert!(
            matches!(room.humans[0].items.resolve(&room.sim, proposal), Ok(command) if command == missing_pickup),
            "uncommitted handle {raw}"
        );
    }

    let fresh = Arc::new(Outbox::default());
    let (reply, resumed) = oneshot::channel();
    room.handle(
        RoomCmd::Resume {
            session: joined.session,
            outbox: fresh.clone(),
            conn: 3,
            reply,
        },
        start,
    );
    assert_eq!(resumed.await.unwrap().unwrap().player, joined.player);
    room.publish(&[]);
    let accepted = decode(&fresh);
    assert!(accepted.iter().any(|m| matches!(m, ServerMsg::Snapshot { me: Some(me), owner_combat: Some(group), .. } if me.wish.is_some() && me.barrier == 30. && me.barrier_ring_step > 0 && *group == initial_combat)));
    let delivered = accepted
        .iter()
        .find_map(|m| match m {
            ServerMsg::State { items, .. } => items.iter().find_map(|item| match item {
                StateItem::Props(p) => Some(p),
                _ => None,
            }),
            _ => None,
        })
        .expect("fresh complete props offer");
    let bow = delivered
        .rows
        .iter()
        .find(|row| row.kind == WirePropKind::Ground(1))
        .expect("new Bow row")
        .handle;
    assert_ne!(
        room.humans[0]
            .items
            .resolve(
                &room.sim,
                rh_sim::PublicCommand::Pickup {
                    handle: rh_sim::PickupHandle::new(bow.get()).unwrap(),
                }
            )
            .unwrap(),
        missing_pickup
    );
    assert_eq!(room.frame_refusals, 0);

    fn decode(out: &Outbox) -> Vec<ServerMsg> {
        let mut bytes = Vec::new();
        out.drain_into(&mut bytes);
        bytes
            .iter()
            .map(|frame| ServerMsg::decode(frame).unwrap())
            .collect()
    }
}
