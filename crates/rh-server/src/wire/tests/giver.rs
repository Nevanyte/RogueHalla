//! Quest giver views on the wire: refusals before publisher work, actor mapping, native alias, prepare, drop and renewal.

use super::*;

#[test]
fn giver_actor_mapping_preserves_source_fields_and_memo() {
    use rh_sim::{fighter::FighterAffiliation, observe::ActorKind};
    let giver = ActorView {
        kind: ActorKind::QuestGiver,
        faction: FighterAffiliation::Neutral,
        weapon: None,
        mob_type: None,
        hp_frac: 0.37,
        barrier_frac: 0.25,
        barrier_ring_step: 3,
        status: Status::Mia,
        effects: 0x1234,
        sig: 73,
        displace_seq: 9,
        bounty: 17,
        ..view(7)
    };
    let wire = wire_actor(&giver).unwrap();
    assert_eq!(
        (wire.kind, wire.faction, wire.weapon, wire.mob_type_code),
        (4, 15, 15, 0)
    );
    assert_eq!(
        (wire.hp_pct, wire.barrier_pct, wire.barrier_ring_step),
        (37, 25, 3)
    );
    assert_eq!(
        (
            wire.status,
            wire.effects,
            wire.sig,
            wire.displace_seq,
            wire.bounty
        ),
        (3, 0x1234, 73, 9, 17)
    );
    assert_eq!(
        (wire.x, wire.y, wire.facing),
        (giver.x as f32, giver.y as f32, giver.facing as f32)
    );
    let mut memo = ActorMemo::default();
    assert_eq!(memo.get(&giver), checked_actor(&giver));
    let mut changed = giver.clone();
    changed.hp_frac = 0.41;
    assert_ne!(memo.get(&changed), checked_actor(&giver));
    assert_eq!(memo.get(&changed), checked_actor(&changed));
    changed.kind = ActorKind::Player;
    changed.faction = FactionId::Holy.into();
    changed.weapon = Some(rh_sim::WeaponId::Fists);
    assert_eq!(memo.get(&changed), checked_actor(&changed));
    assert_ne!(memo.get(&changed).unwrap().kind, 4);
    for spare in [
        None,
        Some(SnapshotData::build(0, 1, 0, None, [wire].into_iter()).unwrap()),
    ] {
        let mut publisher = Publisher::default();
        publisher.obs.visible = vec![giver.clone()];
        let frames =
            publisher.build_observation_frames(spare, rh_sim::PlayerId(1), 0, &mut Vec::new(), &[]);
        assert!(frames.refused.is_none());
        let data = frames.snapshot.unwrap();
        let decoded = data.decoded();
        let bytes = decoded.try_encode().unwrap();
        assert_eq!(ServerMsg::decode(&bytes).unwrap(), decoded);
    }
}

#[test]
fn giver_native_alias_prepare_drop_renewal_and_target_resolution() {
    use rh_sim::{ItemObserver, Placement, PlayerId, PublicCommand, Secrets, Sim, Vec2};
    let arena = source_arena();
    let config = SimConfig {
        quests: true,
        pvp_rules: false,
        ..Default::default()
    };
    let at = arena.spawns[0];
    let peer = arena.spawns[1];
    let placements = |at: Vec2| {
        [
            Placement {
                profile: "giver-viewer".into(),
                faction: FactionId::Holy,
                bot: false,
                at,
                hp: None,
                weapon0: None,
                weapon1: None,
            },
            Placement {
                profile: "giver-peer".into(),
                faction: FactionId::Fire,
                bot: false,
                at: peer,
                hp: None,
                weapon0: None,
                weapon1: None,
            },
        ]
    };
    let genesis = Sim::reset_active(
        config.clone(),
        arena.clone(),
        Secrets::from_seed(7),
        &placements(at),
    );
    let mut scout = ItemObserver::new(PlayerId(1));
    let mut obs = Observation::default();
    scout.observe_into(&genesis, &[], &mut obs, None).unwrap();
    let giver_at = obs
        .quests
        .as_ref()
        .unwrap()
        .rows
        .iter()
        .find(|r| r.state == rh_sim::quest_view::RowState::Open)
        .unwrap()
        .giver;
    let sim = Sim::reset_active(config, arena, Secrets::from_seed(7), &placements(giver_at));
    let mut observer = ItemObserver::new(PlayerId(1));
    let mut publisher = Publisher::default();
    let (frames, offer) = publisher
        .frames_prepared(&mut observer, None, &sim, 0, &[], &mut Vec::new(), &[])
        .unwrap();
    assert!(frames.refused.is_none());
    let giver = publisher
        .observation()
        .visible
        .iter()
        .find(|a| a.kind == rh_sim::observe::ActorKind::QuestGiver)
        .expect("actual native giver in entitled scene")
        .clone();
    assert!(u64::from(giver.id.0) >= (1u64 << 31));
    let decoded =
        ServerMsg::decode(&frames.snapshot.unwrap().decoded().try_encode().unwrap()).unwrap();
    assert!(
        matches!(decoded,ServerMsg::Snapshot{actors,..} if actors.iter().any(|a|a.id==giver.id.0 && a.kind==4 && a.faction==15 && a.weapon==15 && a.mob_type_code==0))
    );
    drop(offer);
    let proposal = PublicCommand::Target { actor: giver.id };
    let missing = rh_sim::validate(rh_sim::RawCommand::MissingTarget).unwrap();
    assert_eq!(
        observer.resolve(&sim, proposal).unwrap(),
        missing,
        "prepared alias grants no capability"
    );
    let (_, offer) = publisher
        .frames_prepared(&mut observer, None, &sim, 0, &[], &mut Vec::new(), &[])
        .unwrap();
    let issued = publisher
        .observation()
        .visible
        .iter()
        .find(|a| {
            a.kind == rh_sim::observe::ActorKind::QuestGiver && a.x == giver.x && a.y == giver.y
        })
        .unwrap()
        .id;
    assert_eq!(issued, giver.id, "drop does not consume a namespace block");
    offer.commit();
    assert_ne!(
        observer.resolve(&sim, proposal).unwrap(),
        missing,
        "normal target resolve uses committed alias, not quest IDs"
    );
    let mut replacement = observer.renewed();
    let (_, offer) = publisher
        .frames_prepared(&mut replacement, None, &sim, 0, &[], &mut Vec::new(), &[])
        .unwrap();
    let next = publisher
        .observation()
        .visible
        .iter()
        .find(|a| {
            a.kind == rh_sim::observe::ActorKind::QuestGiver && a.x == giver.x && a.y == giver.y
        })
        .unwrap()
        .id;
    assert_ne!(next, issued);
    drop(offer);
    drop(replacement);
    assert_ne!(
        observer.resolve(&sim, proposal).unwrap(),
        missing,
        "failed replacement retains committed giver alias"
    );
    let mut replacement = observer.renewed();
    let (_, offer) = publisher
        .frames_prepared(&mut replacement, None, &sim, 0, &[], &mut Vec::new(), &[])
        .unwrap();
    offer.commit();
    observer = replacement;
    assert_eq!(
        observer.resolve(&sim, proposal).unwrap(),
        missing,
        "successful replacement revokes old NPC alias"
    );
    assert_ne!(
        observer
            .resolve(&sim, PublicCommand::Target { actor: next })
            .unwrap(),
        missing
    );
}
