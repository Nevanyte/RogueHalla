//! The live fire feed against the core, and trophy-room enqueue rejection.

use super::*;

#[test]
fn live_fire_feed_matches_core() {
    let now = Instant::now();
    let tick = Duration::from_millis(50);
    let (tx, _rx) = mpsc::unbounded_channel();
    let mut room = Room::new([9; 16], cfg(tick), tx, now);
    for (i, faction) in [FactionId::Fire, FactionId::Holy].into_iter().enumerate() {
        room.join(
            format!("p{i}"),
            Some(faction),
            Arc::new(Outbox::default()),
            i as u64 + 1,
            1,
            None,
        )
        .unwrap();
    }
    let placements = [FactionId::Fire, FactionId::Holy]
        .into_iter()
        .enumerate()
        .map(|(i, faction)| rh_sim::Placement {
            profile: format!("p{i}"),
            faction,
            bot: false,
            at: rh_sim::Vec2::new(400. + 56. * i as f64, 320.),
            hp: None,
            weapon0: None,
            weapon1: None,
        })
        .collect::<Vec<_>>();
    room.sim = Sim::reset_active(
        SimConfig {
            consumables: true,
            faction_mode: rh_sim::FactionMode::EngagementHolyPlagueFire,
            // Off until the coordinator-owned quest transport packet lands; the lifecycle has its own config switch.
            quests: false,
            ..Default::default()
        },
        rh_sim::arenas::practice_arena(),
        Secrets::from_seed(7),
        &placements,
    );
    for h in &mut room.humans {
        h.fx.rebind(&room.sim, h.player);
    }
    let mut reference = room.sim.clone();
    let mut projector = rh_sim::FxProjector::attach(&reference, PlayerId(1));
    let mut signatures = 0;
    for n in 1..=40 {
        if n == 2 {
            for (id, a) in [(PlayerId(1), 0.), (PlayerId(2), std::f64::consts::PI)] {
                let cmd = rh_sim::validate(rh_sim::RawCommand::Attack { a, t: None }).unwrap();
                room.sim.submit(id, cmd).unwrap();
                reference.submit(id, cmd).unwrap();
            }
        }
        reference.step();
        projector.feed(reference.fx_report()).unwrap();
        assert!(room.wake(now + tick * n));
        assert_eq!(room.sim.state_hash(), reference.state_hash());
        assert_eq!(room.humans[0].fx.events(), projector.events());
        signatures += projector
            .events()
            .iter()
            .filter(|e| matches!(e, SeenEvent::SignatureHit { .. }))
            .count();
    }
    assert!(signatures > 2);
    assert_eq!(room.frame_refusals, 0);
}

#[tokio::test(start_paused = true)]
async fn real_trophy_room_enqueue_rejection_does_not_issue_or_spend_handles() {
    use rh_protocol::{
        ClientMsg, RewardChestHandle as WireRewardChestHandle, ServerMsg, StateItem,
        TrophyStatKey, WireCommand, WireRewardChest, WireRewardId,
    };
    use rh_sim::{kill_augments::KillStatKey, reward_chests::RewardChestView, Observation};

    fn rewards(out: &Outbox) -> Vec<WireRewardChest> {
        decoded(out)
            .into_iter()
            .find_map(|message| match message {
                ServerMsg::State { items, .. } => {
                    items.into_iter().find_map(|item| match item {
                        StateItem::RewardChests(rows) => Some(rows),
                        _ => None,
                    })
                }
                _ => None,
            })
            .expect("a full/replacement reward state")
    }

    fn assert_projection(expected: &[RewardChestView], actual: &[WireRewardChest]) {
        assert_eq!(actual.len(), expected.len(), "complete rows are preserved");
        for (view, wire) in expected.iter().zip(actual) {
            assert_eq!(wire.handle.get(), view.handle.wire());
            assert_eq!(wire.x, rh_protocol::quant::quantize_pos(view.at.x as f32));
            assert_eq!(wire.y, rh_protocol::quant::quantize_pos(view.at.y as f32));
            assert_eq!(wire.expires_at, view.expires_at);
            assert_eq!(wire.picked, view.picked);
            assert_eq!(wire.can_choose, view.can_choose);
            assert_eq!(
                wire.options.len(),
                view.options.len(),
                "option slots are not compacted"
            );
            for (source, encoded) in view.options.iter().zip(&wire.options) {
                assert_eq!(source.taken, encoded.taken);
                assert_eq!(source.taken_by.map(|id| id.0), encoded.taken_by);
                match (source.id, encoded.id) {
                    (
                        rh_sim::reward_chests::OptionId::Card(id),
                        WireRewardId::Card(wire_id),
                    ) => {
                        assert_eq!(wire_id, id.index() as u8);
                    }
                    (
                        rh_sim::reward_chests::OptionId::Boost(id),
                        WireRewardId::Boost(wire_id),
                    ) => {
                        assert_eq!(wire_id, id.index() as u8);
                    }
                    (
                        rh_sim::reward_chests::OptionId::Kill(augment),
                        WireRewardId::Kill { key, gain },
                    ) => {
                        let expected_key = match augment.key {
                            KillStatKey::Ad => TrophyStatKey::Ad,
                            KillStatKey::Ap => TrophyStatKey::Ap,
                            KillStatKey::Damage => TrophyStatKey::Damage,
                            KillStatKey::Health => TrophyStatKey::Health,
                            KillStatKey::AttackSpeed => TrophyStatKey::AttackSpeed,
                            KillStatKey::Armor => TrophyStatKey::Armor,
                            KillStatKey::Mr => TrophyStatKey::Mr,
                            KillStatKey::Speed => TrophyStatKey::Speed,
                            KillStatKey::Leech => TrophyStatKey::Leech,
                        };
                        assert_eq!(key, expected_key);
                        assert_eq!(gain.to_bits(), augment.gain.to_bits());
                    }
                    (source, encoded) => {
                        panic!("reward kind/order changed: {source:?} -> {encoded:?}")
                    }
                }
            }
        }
    }

    fn decoded_choose(handle: WireRewardChestHandle, pick: i8, seq: u32) -> WireCommand {
        let bytes = ClientMsg::Input {
            seq,
            commands: vec![WireCommand::Choose { handle, pick }],
        }
        .try_encode()
        .unwrap();
        let ClientMsg::Input {
            seq: decoded_seq,
            commands,
        } = ClientMsg::decode(&bytes).unwrap()
        else {
            panic!("encoded input did not decode as Input")
        };
        assert_eq!(decoded_seq, seq);
        assert_eq!(commands.len(), 1);
        commands.into_iter().next().unwrap()
    }

    async fn rejected_publish_then_resume_and_choose(fill_queue: bool) {
        let start = Instant::now();
        let tick = Duration::from_millis(50);
        let (mut sim, _) = trophy_fixture::four_seat_validated_pvp_kill();
        // Step through the genuine Trophy birth hold; do not alter/install the container or its outcome.
        let mut events = Vec::new();
        for _ in 0..3 {
            events = sim.step();
        }

        let assets = MapAssets::new(sim.arena().clone()).unwrap();
        let mut config = cfg(tick);
        config.sim = sim.config().clone();
        config.arena = assets.arena;
        config.welcome_map = assets.welcome_map;
        let (reports, _rx) = mpsc::unbounded_channel();
        let mut room = Room::new([13; 16], config, reports, start);
        let rejected = Arc::new(Outbox::default());
        let joined = room
            .join(
                "p0".into(),
                Some(FactionId::Fire),
                rejected.clone(),
                1,
                1,
                None,
            )
            .unwrap();
        let (welcome_protocol, welcome_content) =
            match ServerMsg::decode(&joined.welcome).unwrap() {
                ServerMsg::Welcome {
                    protocol,
                    content_hash,
                    ..
                } => (protocol, content_hash),
                other => panic!("join returned {other:?}, expected Welcome"),
            };
        assert_eq!(welcome_protocol, rh_protocol::PROTOCOL_VERSION);
        let mut peer_outboxes = Vec::new();
        for player in 1..4 {
            let out = Arc::new(Outbox::default());
            room.join(
                format!("p{player}"),
                Some(FactionId::Fire),
                out.clone(),
                10 + player as u64,
                1,
                None,
            )
            .unwrap();
            peer_outboxes.push(out);
        }
        room.sim = sim;
        for seat in &mut room.humans {
            seat.fx.rebind(&room.sim, seat.player);
            seat.items = rh_sim::ItemObserver::new(seat.player);
        }

        let mut owner_observer = rh_sim::ItemObserver::new(PlayerId(1));
        let mut owner_view = Observation::default();
        owner_observer
            .observe_into(&room.sim, &events, &mut owner_view, None)
            .unwrap();
        let candidate = owner_view
            .reward_chests
            .iter()
            .find(|row| {
                row.options
                    .iter()
                    .any(|option| matches!(option.id, rh_sim::reward_chests::OptionId::Kill(_)))
            })
            .expect("the actual credited kill produced an owner Trophy")
            .clone();
        assert!(
            candidate.can_choose,
            "the real Trophy is past its ordinary hold"
        );
        let peer = PlayerId(3);
        let mut stranger_observer = rh_sim::ItemObserver::new(peer);
        let mut stranger_view = Observation::default();
        stranger_observer
            .observe_into(&room.sim, &events, &mut stranger_view, None)
            .unwrap();
        assert!(
            stranger_view.reward_chests.iter().all(|row| row
                .options
                .iter()
                .all(|option| !matches!(option.id, rh_sim::reward_chests::OptionId::Kill(_)))),
            "the native owner filter hides Trophy options before Room publication"
        );

        if fill_queue {
            for _ in 0..crate::outbox::IMPORTANT_CAP {
                rejected.important(vec![0x99]);
            }
        } else {
            rejected.close(RejectCode::TooSlow);
        }
        let after_fill = rejected.pending();
        room.publish(&events);
        assert_eq!(
            rejected.pending(),
            after_fill,
            "refused Room batch adds no reliable state/snapshot"
        );
        let mut stale_frames = Vec::new();
        let refused = rejected.drain_into(&mut stale_frames);
        assert_eq!(refused.close, Some(RejectCode::TooSlow));
        assert_eq!(
            stale_frames.len(),
            if fill_queue {
                crate::outbox::IMPORTANT_CAP
            } else {
                0
            }
        );
        if fill_queue {
            assert!(
                stale_frames.iter().all(|frame| frame == &[0x99]),
                "refused Trophy frame was not queued"
            );
        }

        let candidate_handle =
            rh_protocol::RewardChestHandle::new(candidate.handle.wire()).unwrap();
        let absent = room.humans[0]
            .items
            .resolve(
                &room.sim,
                rh_sim::PublicCommand::Choose {
                    handle: candidate.handle,
                    pick: 0,
                },
            )
            .unwrap();
        assert_eq!(
            absent,
            rh_sim::validate(rh_sim::RawCommand::Choose {
                chest_id: None,
                pick: 0
            })
            .unwrap(),
            "a merely prepared candidate handle has no seat binding"
        );
        let chosen = match candidate.options.iter().find_map(|option| match option.id {
            rh_sim::reward_chests::OptionId::Kill(augment) => Some(augment),
            _ => None,
        }) {
            Some(augment) => augment,
            None => unreachable!(),
        };
        let before = room
            .sim
            .player(PlayerId(1))
            .unwrap()
            .kill_stats
            .get(chosen.key);
        room.handle(
            RoomCmd::Input {
                received: std::time::Instant::now(),
                player: PlayerId(1),
                conn: 1,
                seq: 1,
                commands: vec![decoded_choose(candidate_handle, 0, 1)],
            },
            start,
        );
        assert!(room.wake(start + tick));
        assert_eq!(
            room.sim
                .player(PlayerId(1))
                .unwrap()
                .kill_stats
                .get(chosen.key),
            before,
            "a Choose against the uncommitted candidate cannot grant or spend the Trophy"
        );

        let fresh = Arc::new(Outbox::default());
        let (reply, resumed) = oneshot::channel();
        room.handle(
            RoomCmd::Resume {
                session: joined.session,
                outbox: fresh.clone(),
                conn: 5,
                reply,
            },
            start + tick,
        );
        let resumed = resumed.await.unwrap().unwrap();
        match ServerMsg::decode(&resumed.welcome).unwrap() {
            ServerMsg::Welcome {
                protocol,
                content_hash,
                ..
            } => {
                assert_eq!(protocol, welcome_protocol);
                assert_eq!(
                    content_hash, welcome_content,
                    "resume uses the same content identity"
                );
            }
            other => panic!("resume returned {other:?}, expected Welcome"),
        }
        room.publish(&[]);
        let accepted_rows = rewards(&fresh);
        let mut final_observer = rh_sim::ItemObserver::new(PlayerId(1));
        let mut final_view = Observation::default();
        final_observer
            .observe_into(&room.sim, &[], &mut final_view, None)
            .unwrap();
        assert_projection(&final_view.reward_chests, &accepted_rows);
        let accepted_trophy = accepted_rows
            .iter()
            .find(|row| {
                row.options
                    .iter()
                    .any(|option| matches!(option.id, WireRewardId::Kill { .. }))
            })
            .expect("reliable Room queue carries Trophy");
        assert_eq!(
            accepted_trophy.handle.get(),
            candidate.handle.wire(),
            "failed enqueue did not consume/advance the unpublished handle"
        );
        assert!(!peer_outboxes.is_empty());
        assert!(
            peer_outboxes.iter().all(|out| {
                let rows = rewards(out);
                rows.iter().all(|row| {
                    row.options
                        .iter()
                        .all(|option| !matches!(option.id, WireRewardId::Kill { .. }))
                })
            }),
            "every stranger Room queue carries no private Trophy option"
        );

        // The normal wire Choose command reaches Room intake, seat binding, validation and Sim::submit.
        // Select the last actual option so the check follows its original array position through publication.
        let pick = i8::try_from(accepted_trophy.options.len() - 1).unwrap();
        let WireRewardId::Kill { key, gain } = accepted_trophy.options[pick as usize].id else {
            panic!("selected option is not the decoded Trophy choice")
        };
        let stat = match key {
            TrophyStatKey::Ad => KillStatKey::Ad,
            TrophyStatKey::Ap => KillStatKey::Ap,
            TrophyStatKey::Damage => KillStatKey::Damage,
            TrophyStatKey::Health => KillStatKey::Health,
            TrophyStatKey::AttackSpeed => KillStatKey::AttackSpeed,
            TrophyStatKey::Armor => KillStatKey::Armor,
            TrophyStatKey::Mr => KillStatKey::Mr,
            TrophyStatKey::Speed => KillStatKey::Speed,
            TrophyStatKey::Leech => KillStatKey::Leech,
        };
        let before = room.sim.player(PlayerId(1)).unwrap().kill_stats.get(stat);
        room.handle(
            RoomCmd::Input {
                received: std::time::Instant::now(),
                player: PlayerId(1),
                conn: 5,
                seq: 2,
                commands: vec![decoded_choose(accepted_trophy.handle, pick, 2)],
            },
            start + tick,
        );
        assert!(room.wake(start + tick * 2));
        let after = room.sim.player(PlayerId(1)).unwrap().kill_stats.get(stat);
        assert_eq!(
            after.to_bits(),
            (before + gain).to_bits(),
            "grant is exactly the delivered gain at the selected slot"
        );
    }

    rejected_publish_then_resume_and_choose(false).await;
    rejected_publish_then_resume_and_choose(true).await;
}
