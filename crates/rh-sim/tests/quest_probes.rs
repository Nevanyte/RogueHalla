//! P-BAL-PROBE-1 oracle, public API: the read-only probes exist on `Sim` and are empty in a world without the Top 25
//! board; (feature `trusted-build-setup`) a quest-reward grant there is refused by name and changes nothing, a build
//! without rewards still applies, and the `rewards` field is optional in JSON both ways. The board world is covered by
//! `src/quests/board/attempts/scenarios/probe_tests.rs`.
use rh_sim::{
    ArenaMode, FactionId, Placement, PlayerId, QuestAttemptProbe, Secrets, Sim, SimConfig, WeaponId,
};

/// A board-less world (`quests: false`, no arena rules) on the checked-in seed-1 map, one human and one bot seat.
fn boardless() -> Sim {
    let export: rh_sim::mapdata::MapExport =
        serde_json::from_str(include_str!("../src/mapdata/fixtures/seed1.map.json")).unwrap();
    let arena = export.validate().unwrap().into_arena().unwrap();
    let seat = |i: usize, bot: bool| Placement {
        profile: format!("seat-{i}"),
        faction: FactionId::Holy,
        bot,
        at: arena.spawns[i],
        hp: None,
        weapon0: Some(WeaponId::Sword),
        weapon1: None,
    };
    let players = [seat(0, false), seat(1, true)];
    Sim::reset_active(
        SimConfig {
            quests: false,
            lifecycle: ArenaMode::Off,
            min_players: 1,
            ..SimConfig::default()
        },
        arena,
        Secrets::from_seed(1),
        &players,
    )
}

#[test]
fn a_world_without_the_board_has_no_attempts_and_open_gates() {
    let mut sim = boardless();
    for _ in 0..3 {
        sim.step();
    }
    for p in sim.players() {
        let rows: Vec<QuestAttemptProbe> = sim.quest_attempts(p.id);
        assert!(rows.is_empty(), "{:?}: {rows:?}", p.id);
    }
    assert!(sim.quest_attempts(PlayerId(60_000)).is_empty());
    assert!(!sim.gates_locked(), "no arena rules, no lock");
}

#[cfg(feature = "trusted-build-setup")]
#[test]
fn a_quest_reward_grant_without_the_board_is_refused_by_name() {
    use rh_sim::mob_catalog::MobTypeId;
    use rh_sim::quests::board::catalog::RewardId;
    use rh_sim::trusted_build::{apply, BuildRefusal, TrustedBuild};
    let mut sim = boardless();
    sim.step();
    let me = sim.players()[0].id;
    let before = sim.state_hash();
    let ask = TrustedBuild {
        stacks: vec![(MobTypeId::EmberImp, 3)],
        rewards: vec![RewardId::Hook],
        ..TrustedBuild::default()
    };
    assert_eq!(apply(&mut sim, me, &ask), Err(BuildRefusal::NoBoard));
    assert_eq!(sim.state_hash(), before, "a refused grant changes nothing");
    assert_eq!(sim.player(me).unwrap().farm.count(MobTypeId::EmberImp), 0);

    let without = TrustedBuild {
        rewards: Vec::new(),
        ..ask
    };
    apply(&mut sim, me, &without).expect("a build without rewards applies as before");
    assert_eq!(sim.player(me).unwrap().farm.count(MobTypeId::EmberImp), 3);
}

#[cfg(feature = "trusted-build-setup")]
#[test]
fn the_rewards_field_is_optional_in_json() {
    use rh_sim::quests::board::catalog::RewardId;
    use rh_sim::trusted_build::TrustedBuild;
    assert_eq!(
        serde_json::to_string(&TrustedBuild::default()).unwrap(),
        r#"{"cards":[],"stacks":[],"boosts":[]}"#,
        "a build without rewards serializes as before (bench reports keep their bytes)"
    );
    let parsed: TrustedBuild = serde_json::from_str(r#"{"rewards":["Hook","WarmCoat"]}"#).unwrap();
    assert_eq!(parsed.rewards, vec![RewardId::Hook, RewardId::WarmCoat]);
    let old: TrustedBuild =
        serde_json::from_str(r#"{"cards":[],"stacks":[],"boosts":[]}"#).unwrap();
    assert!(old.rewards.is_empty());
}
