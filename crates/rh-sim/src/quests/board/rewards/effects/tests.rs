use super::*;
use crate::{Arena, FactionId, Placement, RawCommand, Secrets, Sim, SimConfig, TileMap, WeaponId};

fn room() -> Sim {
    let seats =
        [(320., 320., WeaponId::Sword), (455., 320., WeaponId::Axe)].map(|(x, y, weapon)| {
            Placement {
                profile: format!("seat-{x}"),
                faction: FactionId::Holy,
                bot: false,
                at: Vec2::new(x, y),
                hp: None,
                weapon0: Some(weapon),
                weapon1: None,
            }
        });
    let config = SimConfig {
        abilities: crate::abilities::Mode::Full,
        ..SimConfig::default()
    };
    let arena = Arena {
        map: TileMap::new(72, 30, vec![0; 72 * 30]).unwrap(),
        spawns: vec![seats[0].at, seats[1].at],
    };
    let mut sim = Sim::reset_active(config, arena, Secrets::from_seed(11), &seats);
    let stream = crate::private_rng::Sfc32::from_words([1, 2, 3, 4]);
    sim.k.quest_deal = Some(Ok(crate::quests::board::Deal {
        placements: Vec::new(),
        stream: stream.clone(),
    }));
    sim.w.quests = Some(crate::quests::live::Quests {
        stream,
        offers: Vec::new(),
        carried: Vec::new(),
        recheck: false,
        board: Some(Default::default()),
        rewards: Some(Default::default()),
    });
    sim
}

fn issue(sim: &mut Sim, id: PlayerId, cmd: RawCommand) {
    sim.submit_unbudgeted(id, crate::validate(cmd).unwrap())
        .unwrap();
}

fn players(sim: &Sim) -> (PlayerId, PlayerId) {
    (sim.players()[0].id, sim.players()[1].id)
}

fn position(sim: &Sim, id: PlayerId) -> Vec2 {
    sim.player(id).unwrap().pos()
}

fn use_slot(
    sim: &mut Sim,
    id: PlayerId,
    slot: crate::abilities::Slot,
    at: Vec2,
    target: Option<PlayerId>,
) {
    issue(
        sim,
        id,
        RawCommand::Cast {
            slot,
            x: at.x,
            y: at.y,
            id: target,
            t: None,
            seq: None,
        },
    );
    sim.step();
}

fn grant(sim: &mut Sim, id: PlayerId, award: Award) {
    assert!(inventory::grant(&mut sim.w, id, award));
}

fn cast(sim: &mut Sim, id: PlayerId, award: Award, at: Vec2, target: Option<PlayerId>) {
    grant(sim, id, award);
    use_slot(sim, id, crate::abilities::Slot::Q, at, target);
}

fn step(sim: &mut Sim, n: usize) {
    for _ in 0..n {
        sim.step();
    }
}

#[test]
fn stasis_stops_real_movement_attacks_casting_and_damage_then_releases() {
    let mut sim = room();
    let (owner, rival) = players(&sim);
    let from = position(&sim, owner);
    let there = Vec2::new(from.x, from.y + 110.);
    let go = RawCommand::Move {
        x: there.x,
        y: there.y,
        seq: None,
    };
    issue(&mut sim, owner, go);
    sim.step();
    let from = position(&sim, owner);
    assert_ne!(from.y, 320., "the queued move actually started");
    let mirrors = sim.player(owner).unwrap().modifiers.mirrors();
    cast(&mut sim, owner, Award::Stasis, from, None);
    assert_eq!(position(&sim, owner), from, "the existing path stopped");
    let end = sim.w.server_time + crate::quests::board::tuning::TOP25.stasis_ms;
    let hp = sim.player(owner).unwrap().vitals.hp;
    let rival_hp = sim.player(rival).unwrap().vitals.hp;
    grant(&mut sim, owner, Award::BlindRage);
    use_slot(&mut sim, owner, crate::abilities::Slot::Q, from, None);
    let wish_ready_at = sim.player(owner).unwrap().stocks.wish_ready_at;
    issue(
        &mut sim,
        owner,
        RawCommand::Cast {
            slot: crate::abilities::Slot::Wish,
            x: from.x,
            y: from.y,
            id: None,
            t: None,
            seq: None,
        },
    );
    while sim.w.server_time + crate::constants::TICK_MS < end {
        if sim.tick() % 5 == 0 {
            issue(&mut sim, owner, go);
            issue(&mut sim, owner, RawCommand::Attack { a: 0., t: None });
        }
        sim.with_cx(|cx| {
            hit(cx, rival, owner, 50., None, true);
            ledger::apply(cx);
        });
        sim.step();
        let p = sim.player(owner).unwrap();
        assert_eq!(p.pos(), from);
        assert_eq!(p.vitals.hp, hp);
        assert_eq!(p.modifiers.mirrors(), mirrors);
        assert!(p.weapon.action.is_none());
        assert!(p.statuses.cast.is_none());
        assert_eq!(p.stocks.wish_ready_at, wish_ready_at);
        assert_eq!(sim.player(rival).unwrap().vitals.hp, rival_hp);
        assert_eq!(
            inventory::owned_spells(&sim.w, owner)[0].unwrap().award,
            Award::BlindRage
        );
    }
    issue(&mut sim, owner, go);
    sim.step();
    assert!(position(&sim, owner).y > from.y);
    assert!(!stasis(&sim.w, owner));
    sim.with_cx(|cx| {
        hit(cx, rival, owner, 50., None, true);
        ledger::apply(cx);
    });
    assert!(sim.player(owner).unwrap().vitals.hp < hp);
}

#[test]
fn cage_blocks_melee_and_motion_but_keeps_sight_and_its_directed_gap() {
    let mut sim = room();
    let (owner, _) = players(&sim);
    let from = position(&sim, owner);
    let center = Vec2::new(from.x + 280., from.y);
    cast(&mut sim, owner, Award::WardensCage, center, None);
    let looking = Vec2::new(center.x + 220., center.y);
    let west = Vec2::new(center.x - 220., center.y);
    let map = terrain::body_map(&sim.k, &sim.w);
    assert!(has_line_of_sight(map, center, looking, MASK_MOVE));
    assert!(!has_line_of_sight(
        map,
        center,
        west,
        crate::map::MASK_MELEE
    ));
    assert!(has_line_of_sight(map, center, west, MASK_SIGHT));
    assert_eq!(crate::observe(&sim, owner, &[]).fields.len(), 1);
    assert!(crate::barriers::check(&sim.k, &sim.w).is_ok());
    step(&mut sim, 120);
    assert!(has_line_of_sight(
        terrain::body_map(&sim.k, &sim.w),
        center,
        west,
        MASK_MOVE
    ));
}

#[test]
fn shield_blocks_one_bolas_and_refunds_then_cleanse_removes_second_root() {
    let mut sim = room();
    let (owner, rival) = players(&sim);
    let from = position(&sim, owner);
    cast(&mut sim, owner, Award::SpellShield, from, None);
    cast(&mut sim, rival, Award::Bolas, from, None);
    step(&mut sim, 3);
    let root = |sim: &Sim| {
        rt::has(
            &sim.player(owner).unwrap().statuses.instances,
            StatusKind::Root,
            sim.w.server_time,
        )
    };
    assert!(!root(&sim));
    let slot = inventory::owned_spells(&sim.w, owner)[0].unwrap();
    assert_eq!(slot.charges, 1);
    assert!(!shielding(&sim.w, owner));
    grant(&mut sim, rival, Award::Bolas);
    use_slot(&mut sim, rival, crate::abilities::Slot::E, from, None);
    step(&mut sim, 3);
    assert!(root(&sim));
    grant(&mut sim, owner, Award::Cleanse);
    use_slot(&mut sim, owner, crate::abilities::Slot::E, from, None);
    assert!(!root(&sim));
}

#[test]
fn hook_pulls_first_contact_and_charge_moves_and_staggers() {
    let mut sim = room();
    let (owner, rival) = players(&sim);
    let before = position(&sim, rival);
    cast(&mut sim, owner, Award::Hook, before, None);
    step(&mut sim, 4);
    let after = position(&sim, rival);
    assert!(after.x + 50. < before.x);
    grant(&mut sim, owner, Award::ShoulderCharge);
    use_slot(&mut sim, owner, crate::abilities::Slot::E, before, None);
    assert!(position(&sim, owner).x > 320.);
    assert!(position(&sim, rival).x >= after.x + 115.);
    assert!(rt::has(
        &sim.player(rival).unwrap().statuses.instances,
        StatusKind::Stun,
        sim.w.server_time
    ));
}

#[test]
fn tornado_waits_and_pulls_by_max_hp() {
    for (max_hp, moved) in [(200., true), (800., false)] {
        let mut sim = room();
        let (owner, rival) = players(&sim);
        sim.w.players[0].vitals.max_hp = 500.;
        sim.w.players[1].vitals.max_hp = max_hp;
        sim.w.players[1].vitals.hp = 100.;
        let at = position(&sim, owner);
        let before = position(&sim, rival);
        cast(&mut sim, owner, Award::Tornado, at, None);
        step(&mut sim, 11);
        assert_eq!(position(&sim, rival), before);
        step(&mut sim, 4);
        assert_eq!(position(&sim, rival).x < before.x, moved);
    }
}

#[test]
fn invalid_unit_casts_leave_equipped_charges_and_cooldown_unchanged() {
    let mut sim = room();
    let (owner, rival) = players(&sim);
    let aim = position(&sim, rival);
    grant(&mut sim, owner, Award::Smite);
    let before = inventory::owned_spells(&sim.w, owner);
    use_slot(&mut sim, owner, crate::abilities::Slot::Q, aim, Some(rival));
    assert_eq!(inventory::owned_spells(&sim.w, owner), before);
    grant(&mut sim, owner, Award::SwapStick);
    let before = inventory::owned_spells(&sim.w, owner);
    use_slot(
        &mut sim,
        owner,
        crate::abilities::Slot::E,
        aim,
        Some(PlayerId(u32::MAX)),
    );
    assert_eq!(inventory::owned_spells(&sim.w, owner), before);
}

#[test]
fn swap_returns_both_original_slots_and_seed_recalls_to_authoritative_feet() {
    let mut sim = room();
    let (owner, rival) = players(&sim);
    let aim = position(&sim, rival);
    cast(&mut sim, owner, Award::SwapStick, aim, Some(rival));
    assert_eq!(sim.player(owner).unwrap().active_weapon(), WeaponId::Axe);
    assert_eq!(sim.player(rival).unwrap().active_weapon(), WeaponId::Sword);
    step(&mut sim, 200);
    assert_eq!(sim.player(owner).unwrap().active_weapon(), WeaponId::Sword);
    assert_eq!(sim.player(rival).unwrap().active_weapon(), WeaponId::Axe);

    let anchor = position(&sim, owner);
    cast(&mut sim, owner, Award::ForgetMeNotSeed, aim, None);
    assert!(seed_planted(&sim.w, owner));
    issue(
        &mut sim,
        owner,
        RawCommand::Move {
            x: anchor.x,
            y: anchor.y + 180.,
            seq: None,
        },
    );
    step(&mut sim, 20);
    assert_ne!(position(&sim, owner), anchor);
    use_slot(&mut sim, owner, crate::abilities::Slot::Q, aim, None);
    assert_eq!(position(&sim, owner), anchor);
    assert!(inventory::owned_spells(&sim.w, owner)[0].is_none());
}

#[test]
fn warhorn_forces_walk_and_buffs_then_draughts_apply_and_expire() {
    let mut sim = room();
    let (owner, rival) = players(&sim);
    let at = position(&sim, owner);
    let from = position(&sim, rival);
    cast(&mut sim, owner, Award::Warhorn, at, None);
    assert!(position(&sim, rival).x < from.x);
    assert_eq!(damage_factor(&sim.w, owner), 1.2);
    assert_eq!(damage_factor(&sim.w, rival), 1.);
    step(&mut sim, 50);
    assert_eq!(damage_factor(&sim.w, owner), 1.);

    let base = sim.player(owner).unwrap().modifiers.mirrors().as_mult;
    cast(&mut sim, owner, Award::BlindRage, at, None);
    assert!(sim.player(owner).unwrap().modifiers.mirrors().as_mult > base * 1.19);
    cast(&mut sim, owner, Award::StillMind, at, None);
    sim.with_cx(|cx| apply_status(cx, rival, owner, StatusKind::Stun, 500.));
    assert!(!rt::has(
        &sim.player(owner).unwrap().statuses.instances,
        StatusKind::Stun,
        sim.w.server_time
    ));
    step(&mut sim, 120);
    assert_eq!(sim.player(owner).unwrap().modifiers.mirrors().as_mult, base);
    sim.with_cx(|cx| apply_status(cx, rival, owner, StatusKind::Stun, 500.));
    assert!(rt::has(
        &sim.player(owner).unwrap().statuses.instances,
        StatusKind::Stun,
        sim.w.server_time
    ));
}

#[test]
fn deployables_shoot_take_weapon_damage_and_publish_only_visible_effects() {
    let mut sim = room();
    let (owner, rival) = players(&sim);
    let at = Vec2::new(640., 320.);
    let before = sim.player(rival).unwrap().vitals.hp;
    cast(&mut sim, owner, Award::Turret, at, None);
    assert_eq!(
        crate::observe(&sim, owner, &[]).quest_deployments[0].kind,
        DeploymentKind::Turret
    );
    assert_eq!(
        crate::observe(&sim, rival, &[]).quest_deployments[0].owner,
        None
    );
    step(&mut sim, 17);
    assert!(sim.player(rival).unwrap().vitals.hp < before);
    let ix = sim.w.fighter_idx(rival).unwrap();
    sim.with_cx(|cx| displace::apply(cx.k, cx.w, cx.pf, rival, Vec2::new(at.x - 28., at.y)));
    assert!(sim.w.standing(ix));
    issue(&mut sim, rival, RawCommand::Attack { a: 0., t: None });
    step(&mut sim, 30);
    assert!(crate::observe(&sim, owner, &[])
        .quest_deployments
        .first()
        .is_none_or(|d| d.hp < 100.));

    let far = Vec2::new(1510., 320.);
    assert!(!sim.can_see_point(owner, far));
    grant(&mut sim, owner, Award::ScoutFalcon);
    use_slot(&mut sim, owner, crate::abilities::Slot::E, far, None);
    step(&mut sim, 3);
    assert!(sim.can_see_point(owner, far));
    assert!(!crate::observe(&sim, rival, &[])
        .quest_deployments
        .iter()
        .any(|d| d.kind == DeploymentKind::ScoutFalcon));
    step(&mut sim, 80);
    assert!(!sim.can_see_point(owner, far));
}

#[test]
fn effect_state_round_trips_hashes_and_rejects_invalid_rows() {
    let mut sim = room();
    let (owner, _) = players(&sim);
    let at = position(&sim, owner);
    cast(&mut sim, owner, Award::StillMind, at, None);
    let effect = state(&sim.w).unwrap().clone();
    assert!(!effect.is_empty());
    let copy: EffectState = serde_json::from_value(serde_json::to_value(&effect).unwrap()).unwrap();
    assert_eq!(copy, effect);
    assert!(validate(&sim.k, &sim.w).is_ok());
    let before = sim.state_hash();
    state_mut(&mut sim.w).buffs[0].until = f64::MAX;
    assert_ne!(sim.state_hash(), before);
    assert!(validate(&sim.k, &sim.w).is_err());
}

#[test]
fn smite_targets_a_mob_and_turret_shoots_mobs() {
    use crate::mob_runtime::{ActorOrigin, ActorProvenance, Mob};
    let mut sim = room();
    let (owner, rival) = players(&sim);
    let mob = PlayerId(sim.w.next_id);
    sim.w.next_id += 1;
    let at = Vec2::new(520., 320.);
    let provenance = ActorProvenance {
        id: mob,
        origin: ActorOrigin::Dummy {
            birth_serial: sim.w.next_entity_id(),
            born_tick: sim.w.tick,
            shield: false,
        },
    };
    sim.w.mobs.push(Mob::born(
        &provenance,
        at,
        0.,
        sim.w.server_time,
        sim.k.config.hitboxes,
    ));
    sim.w.actor_history.push(provenance);
    let hp = sim.w.mobs.get(mob).unwrap().core.vitals.hp;
    cast(&mut sim, owner, Award::Smite, at, Some(mob));
    let after = sim.w.mobs.get(mob).unwrap().core.vitals.hp;
    assert!(after < hp, "Smite reached a real mob through the ledger");

    let rival_hp = sim.player(rival).unwrap().vitals.hp;
    grant(&mut sim, owner, Award::Turret);
    use_slot(
        &mut sim,
        owner,
        crate::abilities::Slot::E,
        Vec2::new(640., 320.),
        None,
    );
    step(&mut sim, 17);
    assert!(sim.w.mobs.get(mob).unwrap().core.vitals.hp < after);
    assert_eq!(sim.player(rival).unwrap().vitals.hp, rival_hp);
}

#[test]
fn lantern_ward_has_two_independent_spends_and_private_sight() {
    let mut sim = room();
    let (owner, rival) = players(&sim);
    let at = Vec2::new(700., 320.);
    let reveal = Vec2::new(1020., 320.);
    cast(&mut sim, owner, Award::LanternWard, at, None);
    let first = inventory::owned_spells(&sim.w, owner)[0].unwrap();
    assert_eq!(first.charges, 1);
    assert!(reveals(
        &sim.w,
        sim.collision_map(),
        owner,
        reveal,
        sim.k.tick_ms()
    ));
    assert!(!reveals(
        &sim.w,
        sim.collision_map(),
        rival,
        reveal,
        sim.k.tick_ms()
    ));
    use_slot(
        &mut sim,
        owner,
        crate::abilities::Slot::Q,
        Vec2::new(280., 570.),
        None,
    );
    let second = inventory::owned_spells(&sim.w, owner)[0].unwrap();
    assert_eq!(second.charges, 0);
    assert_eq!(first.ready_at, second.ready_at);
    use_slot(&mut sim, owner, crate::abilities::Slot::Q, at, None);
    assert_eq!(inventory::owned_spells(&sim.w, owner)[0], Some(second));
    assert_eq!(deployments(&sim.w).len(), 2);
    assert!(validate(&sim.k, &sim.w).is_ok());
}

#[test]
#[ignore = "dhat runs alone: steady_top25_effects_allocate_nothing --ignored --test-threads=1"]
fn steady_top25_effects_allocate_nothing() {
    let mut sim = room();
    let (owner, _) = players(&sim);
    let at = position(&sim, owner);
    cast(&mut sim, owner, Award::ForgetMeNotSeed, at, None);
    let mut events = Vec::with_capacity(256);
    for _ in 0..40 {
        sim.step_into(&mut events);
    }
    assert!(seed_planted(&sim.w, owner));
    let _profiler = dhat::Profiler::builder().testing().build();
    let before = dhat::HeapStats::get().total_blocks;
    for _ in 0..40 {
        #[cfg(feature = "schedule-trace")]
        sim.clear_schedule_trace();
        sim.step_into(&mut events);
    }
    let after = dhat::HeapStats::get().total_blocks;
    dhat::assert_eq!(after, before);
}

#[test]
fn checkpoint_rejects_excess_pending_tornadoes() {
    let mut sim = room();
    let owner = players(&sim).0;
    let row = Tornado {
        owner,
        at: position(&sim, owner),
        lands: sim.w.server_time + crate::quests::board::tuning::TOP25.tornado_windup_ms,
        max_hp: 100.,
    };
    state_mut(&mut sim.w).tornadoes.push(row);
    assert!(super::checkpoint::validate(&sim.k, &sim.w).is_ok());
    state_mut(&mut sim.w)
        .tornadoes
        .resize(crate::constants::MAX_PLAYERS + 1, row);
    assert_eq!(
        super::checkpoint::validate(&sim.k, &sim.w),
        Err("too many pending Top25 tornadoes".into())
    );
}
