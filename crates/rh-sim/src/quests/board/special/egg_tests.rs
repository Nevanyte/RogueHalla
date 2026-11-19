use super::plant_egg;
use crate::{
    input::validate, ArenaMode, Event, FactionId, Outcome, Placement, PlayerId, RawCommand,
    Secrets, Sim, SimConfig, Status, Vec2, WeaponId,
};

const OWNER: PlayerId = PlayerId(2);
const RIVAL: PlayerId = PlayerId(1);
const PLANTED: Vec2 = Vec2::new(100., 100.);
const FALLEN: Vec2 = Vec2::new(240., 100.);

fn raid(n: usize) -> Sim {
    raid_with_abilities(n, crate::abilities::Mode::Off)
}

fn raid_with_abilities(n: usize, abilities: crate::abilities::Mode) -> Sim {
    let placements: Vec<_> = (0..n)
        .map(|i| Placement {
            profile: format!("p{i}"),
            faction: FactionId::Holy,
            bot: false,
            at: match i {
                0 => Vec2::new(200., 100.),
                1 => PLANTED,
                _ => Vec2::new(450. + 100. * i as f64, 140.),
            },
            hp: (i == 1).then_some(1.),
            weapon0: Some(WeaponId::Sword),
            weapon1: None,
        })
        .collect();
    let mut secrets = Secrets::from_seed(11);
    secrets.order_salt = crate::rng::Mulberry32::new(secrets.spawn_seed).next_u32();
    Sim::reset_active(
        SimConfig {
            abilities,
            lifecycle: ArenaMode::Source(crate::ArenaRules {
                plan_seconds: 0.,
                ..Default::default()
            }),
            faction_mode: crate::FactionMode::EngagementHolyPlagueFireFrostSentenced,
            ..SimConfig::default()
        },
        crate::arenas::practice_arena(),
        secrets,
        &placements,
    )
}

fn place(sim: &mut Sim, id: PlayerId, at: Vec2) {
    let p = sim.w.players.get_mut(id).unwrap();
    p.body.x = at.x;
    p.body.y = at.y;
    p.body.history.reset(crate::state::Sample {
        t: sim.w.server_time,
        x: at.x,
        y: at.y,
    });
}

fn kill(sim: &mut Sim) -> Vec<Event> {
    kill_from(sim, true)
}

fn kill_from(sim: &mut Sim, lock: bool) -> Vec<Event> {
    let p = sim.w.players.get_mut(OWNER).unwrap();
    p.vitals.immune_until = 0.;
    p.credit.bounty = 7;
    if lock {
        sim.w.rt.gates_locked = true;
        sim.w.rt.in_coliseum = true;
        sim.w.rt.sudden_death_at = sim.w.server_time + 60_000.;
    }
    sim.submit(
        RIVAL,
        validate(RawCommand::Attack { a: 0., t: None }).unwrap(),
    )
    .unwrap();
    let mut events = Vec::new();
    for _ in 0..20 {
        sim.step_into(&mut events);
        if events.iter().any(
            |e| matches!(e, Event::Death { id, by: Some(by), .. } if *id == OWNER && *by == RIVAL),
        ) {
            return events;
        }
    }
    panic!("a validated sword attack must really kill the owner");
}

fn results(events: &[Event]) -> usize {
    events.iter().filter(|e| matches!(e, Event::Resolved { id, result, .. } if *id == OWNER && result.outcome == Outcome::Died)).count()
}

#[test]
fn rebirth_egg_real_death_opens_gates_and_hatches_once_after_three_seconds() {
    let mut sim = raid(5);
    let planted = sim.with_cx(|cx| plant_egg(cx, OWNER, PLANTED));
    place(&mut sim, OWNER, FALLEN);
    let events = kill(&mut sim);
    assert_eq!(sim.player(OWNER).unwrap().status, Status::Dead);
    assert_eq!(sim.player(RIVAL).unwrap().credit.kills, 1);
    assert_eq!(sim.player(RIVAL).unwrap().credit.gv_parts.bounty, 7);
    assert!(sim
        .w
        .plague_corpses
        .iter()
        .any(|c| c.player == OWNER && c.x == FALLEN.x));
    assert!(!sim.w.rt.gates_locked);
    assert!(sim.w.rt.patience_ends_at.is_some());
    assert_eq!(results(&events), 0, "a real death awaits the planted egg");
    assert!(planted);
    for _ in 0..59 {
        assert_eq!(results(&sim.step()), 0);
        assert_eq!(sim.player(OWNER).unwrap().status, Status::Dead);
    }
    assert_eq!(results(&sim.step()), 0);
    let owner = sim.player(OWNER).unwrap();
    assert!(owner.standing());
    assert_eq!(owner.pos(), PLANTED);
    assert_eq!(owner.vitals.hp, owner.vitals.max_hp * 0.4);
    assert_eq!(sim.player(RIVAL).unwrap().credit.kills, 1);
}

fn egg_view(sim: &Sim, viewer: PlayerId) -> Option<bool> {
    let mut observer = crate::ItemObserver::new(viewer);
    let mut view = crate::Observation::default();
    observer.observe_into(sim, &[], &mut view, None).unwrap();
    view.props.iter().find_map(|row| match row.cue.kind {
        crate::PropKind::RebirthEgg { hatching } => Some(hatching),
        _ => None,
    })
}

fn equip_counter(sim: &mut Sim, weapon: WeaponId) {
    place(sim, RIVAL, Vec2::new(150., 100.));
    let rival = sim.w.players.get_mut(RIVAL).unwrap();
    rival.weapon.weapon0 = Some(weapon);
    rival.weapon.ready_at = sim.w.server_time;
    crate::systems::weapons::release_trigger(rival, sim.w.server_time);
    sim.submit(
        RIVAL,
        validate(RawCommand::Attack {
            a: crate::math::PI,
            t: None,
        })
        .unwrap(),
    )
    .unwrap();
}

#[test]
fn rebirth_egg_is_local_before_death_and_counterable_while_hatching() {
    let mut sim = raid(5);
    let far = PlayerId(3);
    place(&mut sim, RIVAL, Vec2::new(300., 100.));
    assert!(sim.with_cx(|cx| plant_egg(cx, OWNER, FALLEN)));
    assert_eq!(
        sim.w.rebirth_eggs[0].at, PLANTED,
        "cast aim does not move the plant"
    );
    assert!(!sim.with_cx(|cx| plant_egg(cx, OWNER, PLANTED)));
    assert_eq!(egg_view(&sim, RIVAL), Some(false), "200 u is visible");
    assert_eq!(egg_view(&sim, far), None);
    place(&mut sim, RIVAL, Vec2::new(200., 100.));
    place(&mut sim, OWNER, FALLEN);
    assert_eq!(results(&kill(&mut sim)), 0);
    assert_eq!(
        egg_view(&sim, far),
        Some(true),
        "the public hatch overrides fog"
    );
    equip_counter(&mut sim, WeaponId::Sword);
    let mut all = Vec::new();
    for _ in 0..60 {
        sim.step_into(&mut all);
    }
    assert_eq!(results(&all), 1);
    assert!(sim.player(OWNER).unwrap().resolved);
    assert_eq!(sim.player(RIVAL).unwrap().credit.kills, 1);
    assert_eq!(egg_view(&sim, far), None);
}

#[test]
fn rebirth_egg_low_alive_and_departures_resolve_without_hatching() {
    let mut sim = raid(3);
    assert!(!sim.with_cx(|cx| plant_egg(cx, OWNER, PLANTED)));
    let mut sim = raid(4);
    assert!(sim.with_cx(|cx| plant_egg(cx, OWNER, PLANTED)));
    place(&mut sim, OWNER, FALLEN);
    assert_eq!(results(&kill(&mut sim)), 1, "only three remain after death");
    assert!(sim.w.rebirth_eggs.is_empty());

    for leaving in [OWNER, PlayerId(3)] {
        let mut sim = raid(5);
        assert!(sim.with_cx(|cx| plant_egg(cx, OWNER, PLANTED)));
        place(&mut sim, OWNER, FALLEN);
        assert_eq!(results(&kill(&mut sim)), 0);
        sim.remove_player(leaving);
        let mut all = Vec::new();
        for _ in 0..61 {
            sim.step_into(&mut all);
        }
        assert_eq!(results(&all), 1);
        assert!(sim.w.rebirth_eggs.is_empty());
    }
}

#[test]
fn rebirth_egg_checkpoint_preserves_pending_and_hatched_corpse() {
    let mut sim = raid(5);
    assert!(sim.with_cx(|cx| plant_egg(cx, OWNER, PLANTED)));
    place(&mut sim, OWNER, FALLEN);
    assert_eq!(results(&kill_from(&mut sim, false)), 0);

    fn restore(sim: &Sim) -> Sim {
        let checkpoint = serde_json::to_string(sim).unwrap();
        Sim::restore(&mut serde_json::Deserializer::from_str(&checkpoint)).unwrap()
    }

    let mut restored = restore(&sim);
    assert_eq!(sim.state_hash(), restored.state_hash());
    let mut shell_removed = sim.clone();
    shell_removed.w.rebirth_eggs.clear();
    assert_ne!(sim.state_hash(), shell_removed.state_hash());

    let mut bad = serde_json::to_value(&sim).unwrap();
    bad["world"]["rebirth_eggs"][0]["death"]["place"] = serde_json::json!(1);
    let bad = bad.to_string();
    assert!(Sim::restore(&mut serde_json::Deserializer::from_str(&bad)).is_err());

    for _ in 0..60 {
        assert_eq!(sim.step(), restored.step());
        assert_eq!(sim.state_hash(), restored.state_hash());
    }
    assert!(sim.player(OWNER).unwrap().standing());
    assert!(sim
        .w
        .plague_corpses
        .iter()
        .any(|c| c.player == OWNER && c.x == FALLEN.x));
    assert_eq!(sim.state_hash(), restore(&sim).state_hash());
}

#[test]
fn rebirth_egg_requires_clear_safe_ground_and_is_hidden_by_rock() {
    let mut sim = raid(5);
    let centre = sim.arena().centre();
    place(&mut sim, OWNER, centre);
    assert!(!sim.with_cx(|cx| plant_egg(cx, OWNER, PLANTED)));

    let behind_pillar = Vec2::new(155., 160.);
    place(&mut sim, OWNER, behind_pillar);
    assert!(sim.with_cx(|cx| plant_egg(cx, OWNER, PLANTED)));
    place(&mut sim, RIVAL, Vec2::new(240., 160.));
    assert_eq!(egg_view(&sim, RIVAL), None, "nearby but behind rock");

    place(&mut sim, RIVAL, Vec2::new(100., 160.));
    assert_eq!(egg_view(&sim, RIVAL), Some(false));
}

#[test]
fn rebirth_egg_breaks_to_a_validated_ranged_attack_before_death() {
    let mut sim = raid(5);
    assert!(sim.with_cx(|cx| plant_egg(cx, OWNER, PLANTED)));
    place(&mut sim, OWNER, FALLEN);
    equip_counter(&mut sim, WeaponId::Wand);
    for _ in 0..20 {
        sim.step();
        if sim.w.rebirth_eggs.is_empty() {
            break;
        }
    }
    assert!(sim.w.rebirth_eggs.is_empty());
    assert!(sim.player(OWNER).unwrap().standing());
    place(&mut sim, RIVAL, Vec2::new(200., 100.));
    let p = sim.w.players.get_mut(RIVAL).unwrap();
    p.weapon.weapon0 = Some(WeaponId::Sword);
    p.weapon.ready_at = sim.w.server_time;
    crate::systems::weapons::release_trigger(p, sim.w.server_time);
    assert_eq!(results(&kill(&mut sim)), 1);
}

#[test]
fn rebirth_egg_cast_and_real_death_drop_the_burning_crown_at_the_body() {
    use crate::quests::board::{attempts, catalog::QuestId, rewards, Deal};
    use crate::quests::live::{Offer, Quests, Status as OfferStatus};
    const CROWN: u32 = 900;
    let mut sim = raid_with_abilities(5, crate::abilities::Mode::Full);
    sim.k.config.quests = true;
    let stream = crate::private_rng::Sfc32::from_words([1, 2, 3, 4]);
    sim.k.quest_deal = Some(Ok(Deal {
        placements: Vec::new(),
        stream: stream.clone(),
    }));
    let mut inventory = rewards::RewardState::default();
    inventory.owners.push(Default::default());
    let owner = &mut inventory.owners[0];
    owner.id = OWNER;
    owner.slots[0] = Some(
        serde_json::from_value(serde_json::json!({
            "award": rewards::Award::RebirthEgg,
            "charges": 1,
            "max_charges": 1,
            "ready_at": 0.0,
        }))
        .unwrap(),
    );
    sim.w.quests = Some(Quests {
        stream,
        offers: vec![Offer {
            id: CROWN,
            def: 256 + QuestId::TheBurningCrown as u16,
            known: true,
            at: Vec2::new(520., 320.),
            tokens: vec![PLANTED],
            parcel: None,
            status: OfferStatus::Open,
            expires_at: 0.,
            accepted: Vec::new(),
            progress: Vec::new(),
            visits: Vec::new(),
            carrier: None,
            last_carrier: None,
            giver: None,
            published: true,
            publish_at: 0.,
            finder: None,
            closed_at: 0.,
            done_by: None,
            containers: Vec::new(),
        }],
        carried: Vec::new(),
        recheck: false,
        board: Some(Default::default()),
        rewards: Some(inventory),
    });
    sim.with_cx(|cx| {
        attempts::accept(cx, 0, OWNER);
        attempts::accept(cx, 0, RIVAL);
    });
    sim.submit(
        OWNER,
        validate(RawCommand::QuestInteract {
            quest_id: Some(CROWN),
            action: 3,
            target: 0,
        })
        .unwrap(),
    )
    .unwrap();
    sim.submit(
        OWNER,
        validate(RawCommand::Cast {
            slot: crate::abilities::Slot::Q,
            x: FALLEN.x,
            y: FALLEN.y,
            id: None,
            t: None,
            seq: None,
        })
        .unwrap(),
    )
    .unwrap();
    sim.step();
    assert_eq!(sim.w.rebirth_eggs[0].at, PLANTED);
    assert!(rewards::owned_spells(&sim.w, OWNER)[0].is_none());
    assert_eq!(
        attempts::public_markers(&sim.w, CROWN)
            .next()
            .map(|(id, _)| id),
        Some(OWNER)
    );
    place(&mut sim, OWNER, FALLEN);
    assert_eq!(results(&kill(&mut sim)), 0);
    assert_eq!(
        attempts::public_markers(&sim.w, CROWN)
            .next()
            .map(|(id, _)| id),
        None
    );
    assert_eq!(
        attempts::objective_for_owner(&sim.w, RIVAL, CROWN, 0),
        Some(FALLEN)
    );
    assert_eq!(sim.w.rebirth_eggs[0].at, PLANTED);
}
