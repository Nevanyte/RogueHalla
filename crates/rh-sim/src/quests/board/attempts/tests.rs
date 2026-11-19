use super::*;

#[test]
fn each_owner_can_claim_the_same_offer_once() {
    let mut board = BoardState::default();
    let a = PlayerId(1);
    let b = PlayerId(2);
    for id in [a, b] {
        assert!(board.start(id, 17, 50_000., 0));
        let attempt = board.get_mut(id, 17).unwrap();
        attempt.ready = true;
        attempt.pending = Some(PendingReward::default());
    }
    assert!(board.finish(a, 17));
    assert!(!board.finish(a, 17));
    assert!(!board.start(a, 17, 50_000., 0));
    assert!(board.finish(b, 17));
}

#[test]
fn abandoning_and_retrying_resets_only_that_owner() {
    let mut board = BoardState::default();
    let a = PlayerId(1);
    let b = PlayerId(2);
    assert!(board.start(a, 17, 50_000., 0));
    assert!(board.start(b, 17, 50_050., 0));
    board.get_mut(a, 17).unwrap().claim_anchor(0);
    board.get_mut(b, 17).unwrap().claim_anchor(2);
    assert!(board.abandon(a, 17));
    assert!(board.start(a, 17, 50_100., 0));
    assert_eq!(board.get(a, 17).unwrap().bits, 0);
    assert_eq!(board.get(b, 17).unwrap().bits, 4);
}

#[test]
fn repeated_interaction_with_one_token_does_not_count_twice() {
    let mut a = Attempt::new(0., 0);
    assert!(a.claim_anchor(2));
    assert!(!a.claim_anchor(2));
    assert!(a.claim_anchor(0));
    assert_eq!(a.progress, 2);
}

#[test]
fn pending_reward_round_trips_and_claim_is_idempotent() {
    let mut board = BoardState::default();
    let owner = PlayerId(1);
    assert!(board.start(owner, 17, 0., 0));
    let a = board.get_mut(owner, 17).unwrap();
    a.ready = true;
    a.pending = Some(PendingReward {
        points: 2,
        ..Default::default()
    });
    let mut restored: BoardState =
        serde_json::from_slice(&serde_json::to_vec(&board).unwrap()).unwrap();
    assert_eq!(restored, board);
    assert!(restored.finish(owner, 17));
    assert!(!restored.finish(owner, 17));
}

fn scenario<const N: usize>(ids: &[QuestId], bots: [bool; N]) -> crate::Sim {
    let at = Vec2::new(292., 284.);
    let seats: [_; N] = std::array::from_fn(|i| crate::Placement {
        profile: format!("player-{i}"),
        faction: crate::FactionId::Holy,
        bot: bots[i],
        at,
        hp: None,
        weapon0: None,
        weapon1: None,
    });
    let mut sim = crate::Sim::reset_active(
        crate::SimConfig::default(),
        crate::arenas::practice_arena(),
        crate::Secrets::from_seed(11),
        &seats,
    );
    let offers = ids
        .iter()
        .enumerate()
        .map(|(i, &id)| Offer {
            id: 900 + i as u32,
            def: 256 + id as u16,
            known: true,
            at,
            tokens: vec![at],
            parcel: None,
            status: super::super::super::live::Status::Open,
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
        })
        .collect();
    sim.w.quests = Some(super::super::super::live::Quests {
        stream: crate::private_rng::Sfc32::from_words([1, 2, 3, 4]),
        offers,
        carried: Vec::new(),
        recheck: false,
        board: Some(BoardState::default()),
        rewards: Some(super::super::rewards::RewardState::default()),
    });
    sim
}

fn run(sim: &mut crate::Sim, run: impl FnOnce(&mut Cx)) {
    let mut events = Vec::new();
    run(&mut Cx {
        stats: crate::status_runtime::stats::Inputs::default(),
        #[cfg(feature = "trusted-status-probes")]
        probe: None,
        k: &sim.k,
        w: &mut sim.w,
        s: &mut sim.s,
        ev: &mut events,
        pf: &mut sim.pf,
    });
}

fn owners(w: &World) -> [PlayerId; 2] {
    [w.players[0].id, w.players[1].id]
}

#[test]
fn two_deliveries_are_pending_on_the_same_tick_and_finish_independently() {
    let mut sim = scenario(&[QuestId::TuberEats], [false; 2]);
    let [a, b] = owners(&sim.w);
    run(&mut sim, |cx| {
        for owner in [a, b] {
            accept(cx, 0, owner);
            interact(cx, owner, 900, 3, 0);
        }
        system(cx);
    });
    assert!(pending(&sim.w, a, 900).is_some());
    assert!(pending(&sim.w, b, 900).is_some());
    assert!(finish(&mut sim.w, a, 900));
    assert!(!finish(&mut sim.w, a, 900));
    assert!(is_completed(&sim.w, a, 900));
    assert!(is_accepted(&sim.w, b, 900));
    assert!(finish(&mut sim.w, b, 900));
}

#[test]
fn opposing_banners_pay_only_at_the_destination_of_each_delivery() {
    let mut sim = scenario(&[QuestId::TwoBanners], [false; 2]);
    let [outbound, returning] = owners(&sim.w);
    let home = sim.w.players[0].pos();
    let outpost = Vec2::new(home.x + 256., home.y);
    sim.w.quests.as_mut().unwrap().offers[0].tokens[0] = outpost;
    run(&mut sim, |cx| {
        for (owner, branch) in [(outbound, 0), (returning, 1)] {
            accept(cx, 0, owner);
            interact(cx, owner, 900, 2, branch);
            assert_eq!(objective_for_owner(cx.w, owner, 900, 0), Some(outpost));
            cx.w.players.get_mut(owner).unwrap().body.x = outpost.x;
        }
        interact(cx, returning, 900, 3, 0);
        system(cx);
        assert!(pending(cx.w, outbound, 900).is_some());
        assert!(
            pending(cx.w, returning, 900).is_none(),
            "taking the rival banner starts the return trip"
        );
        assert_eq!(objective_for_owner(cx.w, returning, 900, 0), Some(home));
        cx.w.players.get_mut(returning).unwrap().body.x = home.x;
        system(cx);
        assert!(pending(cx.w, returning, 900).is_some());
    });
}

#[test]
fn crown_drops_with_power_and_freshly_returns_after_turn_in() {
    let mut sim = scenario(&[QuestId::TheBurningCrown], [false; 2]);
    let [a, b] = owners(&sim.w);
    run(&mut sim, |cx| {
        accept(cx, 0, a);
        accept(cx, 0, b);
        interact(cx, a, 900, 3, 0);
        interact(cx, b, 900, 3, 0);
    });
    assert_eq!(public_markers(&sim.w, 900).next().map(|p| p.0), Some(a));
    board_mut(&mut sim.w).unwrap().crown.elapsed_ms = 7000.;
    on_drop_body(
        &mut Hx {
            stats: crate::status_runtime::stats::Inputs::default(),
            fx: None,
            k: &sim.k,
            w: &mut sim.w,
            ev: &mut Vec::new(),
        },
        &crate::hooks::HookArgs::DropBody {
            victim: a,
            between_ticks: false,
        },
    );
    assert!(public_markers(&sim.w, 900).next().is_none());
    run(&mut sim, |cx| {
        interact(cx, b, 900, 3, 0);
        system(cx);
    });
    assert_eq!(pending(&sim.w, b, 900).unwrap().points, 7);
    assert!(finish(&mut sim.w, b, 900));
    assert_eq!(board(&sim.w).unwrap().crown, Crown::default());
    run(&mut sim, |cx| {
        accept(cx, 0, a);
        interact(cx, a, 900, 3, 0);
    });
    assert_eq!(public_markers(&sim.w, 900).next().map(|p| p.0), Some(a));
}

#[test]
fn letters_fails_at_lock_while_routes_pause() {
    let mut sim = scenario(&[QuestId::Letters, QuestId::TuberEats], [false; 2]);
    let [a, _] = owners(&sim.w);
    run(&mut sim, |cx| {
        accept(cx, 0, a);
        accept(cx, 1, a);
        let before = deadline(cx.w, a, 901);
        cx.w.rt.gates_locked = true;
        system(cx);
        assert!(!is_accepted(cx.w, a, 900));
        assert_eq!(deadline(cx.w, a, 901), before + TICK_MS);
        cx.w.rt.gates_locked = false;
        system(cx);
        accept(cx, 0, a);
        assert!(is_accepted(cx.w, a, 900));
    });
}

#[test]
fn solo_bot_bounty_is_private_from_bounty_payout_and_can_be_replaced() {
    let mut sim = scenario(&[QuestId::BountyHunter], [false, true, true]);
    let [a, b] = owners(&sim.w);
    let c = sim.w.players[2].id;
    run(&mut sim, |cx| {
        accept(cx, 0, a);
        accept(cx, 0, b);
        system(cx);
        assert!(!is_accepted(cx.w, b, 900));
        assert_eq!(public_markers(cx.w, 900).next().map(|p| p.0), Some(b));
        on_player_killed(
            &mut Hx {
                stats: crate::status_runtime::stats::Inputs::default(),
                fx: None,
                k: cx.k,
                w: cx.w,
                ev: cx.ev,
            },
            &crate::hooks::HookArgs::PlayerKilled {
                by: a,
                victim: b,
                outcome: crate::results::Outcome::Died,
            },
        );
        assert_eq!(cx.w.player(a).unwrap().credit.gv_parts.bounty, 0);
        assert!(ready(cx.w, a, 900));
        cx.w.players[1].status = crate::state::Status::Dead;
        system(cx);
        assert_eq!(public_markers(cx.w, 900).next().map(|p| p.0), Some(c));
    });
}

#[test]
fn native_encounters_bind_to_physical_actors_and_pay_only_credited_killers() {
    use crate::mapgen::{coliseum, pipeline};
    use crate::quests::board::{map_admission, placement};
    let secrets = (1..=128)
        .map(crate::Secrets::from_seed)
        .find(|secrets| {
            let mut stream = crate::private_rng::Sfc32::from_words(
                super::super::super::live::quest_salt(secrets.reward_salt),
            );
            let draw = placement::draw(&mut || stream.next_f64());
            [QuestId::TheWarden, QuestId::RimeHunt, QuestId::EliteSlayer]
                .iter()
                .all(|quest| draw.contains(quest))
        })
        .expect("draw both encounter quests");
    let fixed = coliseum::fixed_layout();
    let generator = pipeline::Pipeline::new(&fixed);
    let arena = map_admission::generate(
        &generator,
        1,
        0..32,
        pipeline::Options {
            decor: false,
            ..Default::default()
        },
        secrets,
    )
    .unwrap()
    .validate()
    .unwrap()
    .into_arena()
    .unwrap();
    let positions = [arena.spawns[0], arena.spawns[1]];
    let seats = [0, 1].map(|i| crate::Placement {
        profile: format!("player-{i}"),
        faction: crate::FactionId::Holy,
        bot: false,
        at: positions[i],
        hp: None,
        weapon0: None,
        weapon1: None,
    });
    let mut sim = crate::Sim::reset_active(
        crate::SimConfig {
            lifecycle: crate::ArenaMode::Source(crate::ArenaRules {
                plan_seconds: 0.,
                ..Default::default()
            }),
            ..crate::SimConfig::live_content()
        },
        arena,
        secrets,
        &seats,
    );
    let [a, b] = owners(&sim.w);
    let offers = &sim.w.quests.as_ref().unwrap().offers;
    let warden = offers
        .iter()
        .position(|o| kind(o) == Some(QuestId::TheWarden))
        .unwrap();
    let rime = offers
        .iter()
        .position(|o| kind(o) == Some(QuestId::RimeHunt))
        .unwrap();
    let warden_id = offers[warden].id;
    let rime_id = offers[rime].id;
    let elite = offers
        .iter()
        .position(|o| kind(o) == Some(QuestId::EliteSlayer))
        .unwrap();
    let elite_id = offers[elite].id;
    run(&mut sim, |cx| {
        accept(cx, warden, a);
        accept(cx, warden, b);
        accept(cx, rime, a);
        accept(cx, elite, a);
        accept(cx, elite, b);
        system(cx);
    });
    let warden_target = board(&sim.w)
        .unwrap()
        .targets
        .iter()
        .find(|t| t.offer == warden_id)
        .unwrap()
        .actor;
    let boss = sim.w.mobs.get(warden_target).unwrap();
    assert_eq!(boss.display_name.as_deref(), Some("The Warden"));
    let ice: Vec<_> = board(&sim.w)
        .unwrap()
        .targets
        .iter()
        .filter(|t| t.offer == rime_id)
        .map(|t| t.actor)
        .collect();
    assert_eq!(ice.len(), 3);
    for &actor in &ice {
        let mob = sim.w.mobs.get(actor).unwrap();
        assert_eq!(mob.core.active_weapon(), crate::WeaponId::Bow);
        assert_eq!(
            mob.mob_type,
            Some(crate::mob_catalog::MobTypeId::FrostbowSentry)
        );
    }
    // Submit damage to the actual ledger. The kill consumer receives the physical source actor's ID.
    let queue = |w: &mut World, victim, by| {
        w.deferred.defer_hit(crate::systems::ledger::Entry {
            terrain: false,
            dot: false,
            ability: None,
            arena: false,
            source: None,
            critical: false,
            critical_mult: None,
            sig: None,
            execute: false,
            victim,
            by: Some(by),
            packet: crate::state::Packet {
                raw: 100_000.,
                phys: 0.,
                magic: 0.,
                true_: 1.,
            },
            weapon: None,
            stagger: false,
            projectile: None,
            scale: 1.,
            derived: false,
            passive: false,
        });
    };
    queue(&mut sim.w, warden_target, b);
    queue(&mut sim.w, ice[0], a);
    sim.step();
    assert!(ready(&sim.w, b, warden_id));
    assert!(!ready(&sim.w, a, warden_id));
    assert!(!ready(&sim.w, a, rime_id));
    let mut bad = sim.clone();
    board_mut(&mut bad.w)
        .unwrap()
        .targets
        .iter_mut()
        .find(|t| t.actor == ice[0])
        .unwrap()
        .offer = warden_id;
    assert_eq!(validate(&bad.k, &bad.w), Err("invalid quest target".into()));
    let serialized = serde_json::to_vec(&sim).unwrap();
    let mut sim =
        crate::Sim::restore(&mut serde_json::Deserializer::from_slice(&serialized)).unwrap();
    assert!(ready(&sim.w, b, warden_id));
    assert!(!ready(&sim.w, a, warden_id));
    for &actor in &ice[1..] {
        queue(&mut sim.w, actor, b);
    }
    sim.step();
    assert!(ready(&sim.w, a, rime_id));
    assert!(!is_accepted(&sim.w, b, rime_id));

    let elites: Vec<_> = sim
        .w
        .mobs
        .iter()
        .filter(|m| {
            m.elite
                && m.status == crate::state::Status::Alive
                && m.core.faction_id() == Some(crate::FactionId::Fire)
        })
        .take(2)
        .map(|m| m.core.id)
        .collect();
    assert_eq!(elites.len(), 2);
    queue(&mut sim.w, elites[0], a);
    queue(&mut sim.w, elites[1], b);
    sim.step();
    assert!(ready(&sim.w, a, elite_id) || is_completed(&sim.w, a, elite_id));
    assert!(ready(&sim.w, b, elite_id) || is_completed(&sim.w, b, elite_id));
}

#[test]
fn gatecrasher_counts_distinct_opponents_per_lock_and_requires_reopening() {
    let mut sim = scenario(&[QuestId::Gatecrasher], [false, false, true]);
    let [a, b] = owners(&sim.w);
    let c = sim.w.players[2].id;
    run(&mut sim, |cx| {
        accept(cx, 0, a);
        let landed = |cx: &mut Cx, victim| {
            on_hit_landed(
                &mut Hx {
                    stats: crate::status_runtime::stats::Inputs::default(),
                    fx: None,
                    k: cx.k,
                    w: cx.w,
                    ev: cx.ev,
                },
                &crate::hooks::HookArgs::HitLanded {
                    entry: crate::systems::ledger::Entry {
                        terrain: false,
                        dot: false,
                        ability: None,
                        arena: false,
                        source: None,
                        critical: false,
                        critical_mult: None,
                        sig: None,
                        execute: false,
                        victim,
                        by: Some(a),
                        packet: crate::state::Packet {
                            raw: 1.,
                            phys: 0.,
                            magic: 0.,
                            true_: 1.,
                        },
                        weapon: None,
                        stagger: false,
                        projectile: None,
                        scale: 1.,
                        derived: false,
                        passive: false,
                    },
                    amount: 1.,
                },
            );
        };
        landed(cx, b);
        assert_eq!(progress(cx.w, a, 900), 0);
        cx.w.rt.gates_locked = true;
        landed(cx, b);
        landed(cx, b);
        assert_eq!(progress(cx.w, a, 900), 1);
        cx.w.rt.gates_locked = false;
        system(cx);
        assert_eq!(progress(cx.w, a, 900), 0);
        cx.w.rt.gates_locked = true;
        landed(cx, b);
        landed(cx, c);
        assert_eq!(progress(cx.w, a, 900), 2);
        assert!(!ready(cx.w, a, 900));
        cx.w.rt.gates_locked = false;
        system(cx);
        assert!(ready(cx.w, a, 900));
    });
}

#[test]
fn contested_fort_and_interrupted_meditation_do_not_pay() {
    let mut sim = scenario(&[QuestId::HoldTheFort, QuestId::StayAWhile], [false, false]);
    let [a, b] = owners(&sim.w);
    run(&mut sim, |cx| {
        accept(cx, 0, a);
        accept(cx, 1, a);
        interact(cx, a, 901, 6, 0);
        for _ in 0..20 {
            system(cx);
        }
        assert_eq!(board(cx.w).unwrap().get(a, 900).unwrap().dwell_ms, 0.);
        assert_eq!(
            board(cx.w).unwrap().get(a, 901).unwrap().dwell_ms,
            20. * TICK_MS
        );
        let (a_slot, b_slot) = (cx.w.idx(a).unwrap(), cx.w.idx(b).unwrap());
        cx.w.players[a_slot].body.x += 1.;
        cx.w.players[b_slot].body.x += 500.;
        system(cx);
        assert_eq!(board(cx.w).unwrap().get(a, 901).unwrap().dwell_ms, 0.);
        for _ in 1..(FORT_MS / TICK_MS) as usize {
            system(cx);
        }
        assert!(ready(cx.w, a, 900));
        assert!(!ready(cx.w, a, 901));
        interact(cx, a, 901, 6, 0);
        for _ in 0..(MEDITATE_MS / TICK_MS) as usize {
            system(cx);
        }
        assert!(ready(cx.w, a, 901));
    });
}

#[test]
fn bargain_spends_equivalent_stat_stacks_in_roster_order() {
    let mut sim = scenario(&[QuestId::TheBargain], [false; 2]);
    let farm = &mut sim.w.players[0].farm;
    farm.set(crate::mob_catalog::MobTypeId::EmberImp, 12);
    farm.set(crate::mob_catalog::MobTypeId::Inquisitor, 10);
    let p = &mut sim.w.players[0];
    assert_eq!(bargain_stacks(p, 0), 22);
    assert!(!bargain_take(p, 0, 23));
    assert!(bargain_take(p, 0, 20));
    assert_eq!(p.farm.count(crate::mob_catalog::MobTypeId::Inquisitor), 2);
}
