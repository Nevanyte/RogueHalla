use crate::{
    observe::ActorKind, ArenaMode, FactionId, ItemObserver, Observation, Placement, PlayerId,
    Secrets, Sim, SimConfig, Status, Vec2, WeaponId,
};

/// The viewer (id 1) stands alone in a corner; ids 2..=4 stand together across the arena, out of its sight.
fn arena() -> Sim {
    let at = [
        Vec2::new(80., 80.),
        Vec2::new(1400., 900.),
        Vec2::new(1450., 940.),
        Vec2::new(1350., 940.),
    ];
    let placements: Vec<_> = at
        .iter()
        .enumerate()
        .map(|(i, &at)| Placement {
            profile: format!("p{i}"),
            faction: [
                FactionId::Fire,
                FactionId::Holy,
                FactionId::Frost,
                FactionId::Plague,
            ][i],
            bot: false,
            at,
            hp: None,
            weapon0: Some(WeaponId::Sword),
            weapon1: None,
        })
        .collect();
    let mut secrets = Secrets::from_seed(7);
    secrets.order_salt = crate::rng::Mulberry32::new(secrets.spawn_seed).next_u32();
    let mut sim = Sim::reset_active(
        SimConfig {
            lifecycle: ArenaMode::Source(crate::ArenaRules {
                plan_seconds: 0.,
                ..Default::default()
            }),
            ..SimConfig::default()
        },
        crate::arenas::practice_arena(),
        secrets,
        &placements,
    );
    sim.step();
    sim
}

fn ids(sim: &Sim) -> Vec<PlayerId> {
    sim.players().iter().map(|p| p.id).collect()
}

/// A final death: dead and resolved (what the settle step leaves once no Rebirth Egg is pending).
fn out_of_run(sim: &mut Sim, id: PlayerId) {
    let p = sim.w.players.get_mut(id).unwrap();
    p.status = Status::Dead;
    p.resolved = true;
}

fn seen_players(sim: &Sim, observer: &mut ItemObserver) -> Vec<PlayerId> {
    let mut out = Observation::default();
    let offer = observer.prepare_into(sim, &[], &mut out, None).unwrap();
    offer.commit();
    out.visible
        .iter()
        .filter(|a| a.kind == ActorKind::Player)
        .map(|a| a.id)
        .collect()
}

#[test]
fn only_a_final_death_may_spectate() {
    let mut sim = arena();
    let [me, a, _, _] = ids(&sim)[..] else {
        panic!()
    };
    assert_eq!(sim.spectate_eye(me, Some(a)), None, "alive: own view");
    sim.w.players.get_mut(me).unwrap().status = Status::Dead;
    assert_eq!(
        sim.spectate_eye(me, Some(a)),
        None,
        "dead but not resolved (a Rebirth Egg may still hatch): own view"
    );
    out_of_run(&mut sim, me);
    assert_eq!(sim.spectate_eye(me, None), None, "nothing asked");
    assert_eq!(sim.spectate_eye(me, Some(a)), Some(a));
    assert_eq!(
        sim.spectate_eye(me, Some(me)),
        Some(a),
        "never yourself: the next standing player"
    );
}

#[test]
fn a_fallen_target_moves_on_to_the_next_standing_player_in_id_order() {
    let mut sim = arena();
    let [me, a, b, c] = ids(&sim)[..] else {
        panic!()
    };
    out_of_run(&mut sim, me);
    out_of_run(&mut sim, b);
    assert_eq!(sim.spectate_eye(me, Some(b)), Some(c), "next after b");
    assert_eq!(sim.spectate_eye(me, Some(c)), Some(c));
    out_of_run(&mut sim, c);
    assert_eq!(sim.spectate_eye(me, Some(c)), Some(a), "wraps around");
    assert_eq!(
        sim.spectate_eye(me, Some(PlayerId(999))),
        Some(a),
        "an unknown id wraps too"
    );
    out_of_run(&mut sim, a);
    assert_eq!(sim.spectate_eye(me, Some(a)), None, "nobody standing");
}

#[test]
fn a_spectator_sees_what_the_watched_player_sees_and_gets_its_own_view_back_when_revived() {
    let mut sim = arena();
    let [me, a, _, _] = ids(&sim)[..] else {
        panic!()
    };
    let own_view = seen_players(&sim, &mut ItemObserver::new(me));
    out_of_run(&mut sim, me);
    let mut observer = ItemObserver::new(me);
    assert!(
        seen_players(&sim, &mut observer).is_empty(),
        "the guard: a dead viewer sees nobody until it asks to spectate"
    );
    observer.set_spectate(Some(a));
    let mut watched: Vec<PlayerId> = seen_players(&sim, &mut ItemObserver::new(a))
        .into_iter()
        .chain([a])
        .filter(|&id| id != me)
        .collect();
    watched.sort();
    let seen = seen_players(&sim, &mut observer);
    assert_eq!(seen, watched, "the watched player and exactly who it sees");
    assert!(seen.len() > 1, "the guard: the watched player sees someone");
    assert!(!seen.contains(&me), "the viewer is never its own actor row");
    let mut out = Observation::default();
    observer
        .prepare_into(&sim, &[], &mut out, None)
        .unwrap()
        .commit();
    assert_eq!(
        out.me.as_ref().map(|m| m.id),
        Some(me),
        "self stays the viewer's own"
    );
    // Revived (Rebirth Egg): alive again, so the spectate request no longer applies.
    let p = sim.w.players.get_mut(me).unwrap();
    p.status = Status::Alive;
    p.resolved = false;
    assert_eq!(
        seen_players(&sim, &mut observer),
        own_view,
        "own view again"
    );
}
