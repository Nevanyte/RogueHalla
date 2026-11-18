//! Hosted profiles: protocol 8 training frames, quest hosting and the mob-spacing refusal.

use super::*;

#[test]
fn protocol_8_training_frames_carry_the_training_set() {
    let config = SimConfig {
        min_players: 1,
        lobby_seconds: 0.,
        fill_seconds: 0.,
        training: true,
        dummies: true,
        ..Default::default()
    };
    let mut sim = rh_sim::Sim::new(
        config,
        rh_sim::arenas::practice_arena(),
        rh_sim::Secrets::from_seed(7),
    );
    sim.add_player("p0", Some(FactionId::Holy), false).unwrap();
    sim.step();
    let id = sim.players()[0].id;
    let obs = rh_sim::observe(&sim, id, &[]);
    assert!(obs.training, "a training observation says so");
    let ServerMsg::Snapshot { training, .. } = snapshot(&obs, 0) else {
        panic!("a snapshot")
    };
    assert!(training, "the training head bit reaches the wire");
    let public = command(WireCommand::Train {
        command: 4,
        id: 0,
        count: 0,
        options: [3, 7, 12],
        option_count: 3,
    });
    match public {
        rh_sim::PublicCommand::Train(raw) => assert_eq!(
            (
                raw.command,
                raw.id,
                raw.count,
                raw.options,
                raw.option_count
            ),
            (4, 0, 0, [3, 7, 12], 3)
        ),
        _ => panic!("a train command maps to the public console proposal"),
    }
    let Wire::Event(WireEvent::Toast { ref text }) = to_wire(&SeenEvent::Toast {
        text: "Slow down".into(),
    }) else {
        panic!("an owner-only toast maps to the protocol 8 event")
    };
    let frame = ServerMsg::Events {
        tick: 1,
        events: vec![WireEvent::Toast { text: text.clone() }],
    }
    .encode();
    assert!(matches!(
        ServerMsg::decode(&frame),
        Ok(ServerMsg::Events { .. })
    ));
}
#[test]
fn invalid_giver_and_unarmed_views_refuse_before_publisher_work() {
    use rh_sim::{fighter::FighterAffiliation, observe::ActorKind};
    let mut publisher = Publisher::default();
    let good = view(5);
    let expected = publisher.actors.get(&good);
    let before = publisher.actors.slots.clone();
    for actor in [
        ActorView {
            kind: ActorKind::QuestGiver,
            ..good.clone()
        },
        ActorView {
            faction: FighterAffiliation::Neutral,
            ..good.clone()
        },
        ActorView {
            weapon: None,
            ..good.clone()
        },
        ActorView {
            kind: ActorKind::QuestGiver,
            faction: FighterAffiliation::Neutral,
            weapon: None,
            mob_type: Some(rh_sim::mob_catalog::MobTypeId::Archon),
            ..good.clone()
        },
        ActorView {
            kind: ActorKind::QuestGiver,
            faction: FighterAffiliation::Neutral,
            weapon: Some(rh_sim::WeaponId::Fists),
            ..good.clone()
        },
    ] {
        assert!(checked_actor(&actor).is_err());
        publisher.obs.visible = vec![good.clone(), actor];
        assert!(checked_snapshot(&publisher.obs, 0).is_err());
        // Full, delta's spare buffer, and a new/resync publisher share this strict gate.
        for spare in [
            None,
            Some(
                SnapshotData::build(0, 1, 0, None, [wire_actor(&good).unwrap()].into_iter())
                    .unwrap(),
            ),
        ] {
            let frames =
                publisher.build_observation_frames(spare, good.id, 0, &mut Vec::new(), &[]);
            assert!(frames.snapshot.is_err() && frames.refused.is_some());
            assert!(frames.important.is_none() && frames.fx.is_none());
            assert_eq!(
                publisher.actors.slots, before,
                "refusal must not quantize/cache a prefix"
            );
        }
        let mut fresh = Publisher {
            obs: publisher.obs.clone(),
            ..Default::default()
        };
        assert!(fresh
            .build_observation_frames(None, good.id, 0, &mut Vec::new(), &[])
            .snapshot
            .is_err());
        assert!(fresh.actors.slots.is_empty());
    }
    assert_eq!(publisher.actors.get(&good), expected);
}

#[test]
fn quest_hosting_is_supported_and_incompatible_training_is_refused() {
    let config = rh_sim::SimConfig {
        quests: true,
        ..Default::default()
    };
    assert!(profile_supported(&config).is_ok());
    assert!(profile_supported(&rh_sim::SimConfig {
        training: true,
        ..config
    })
    .is_err());
    let obs = Observation {
        quests: Some(rh_sim::quest_view::QuestView::default()),
        ..Default::default()
    };
    assert!(observation_supported(&obs).is_ok());
    assert!(profile_supported(&rh_sim::SimConfig::default()).is_ok());
}

#[test]
fn mob_spacing_off_is_never_hosted() {
    let live = rh_sim::SimConfig {
        hitboxes: rh_sim::archetype::HitboxMode::Archetypes,
        ..Default::default()
    };
    assert!(profile_supported(&live).is_ok());
    assert!(profile_supported(&rh_sim::SimConfig {
        mob_spacing: rh_sim::archetype::MobSpacing::Off,
        ..live
    })
    .is_err());
}
