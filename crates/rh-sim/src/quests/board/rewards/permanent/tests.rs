use super::*;
use crate::constants::TICK_MS;
use crate::quests::{board::catalog::QuestId, live};
use crate::state::Packet;
use crate::systems::{frost, ledger, terrain};
use crate::{Arena, FactionId, Placement, Secrets, Sim, SimConfig, Vec2};

const OFFER: u32 = 900;

fn setup(quest: QuestId, arena: Arena, at: Vec2, mobs: bool) -> Sim {
    let seats = [FactionId::Holy, FactionId::Frost].map(|faction| Placement {
        profile: faction.as_str().into(),
        faction,
        bot: false,
        at,
        hp: None,
        weapon0: Some(WeaponId::Sword),
        weapon1: Some(WeaponId::Bow),
    });
    let mut sim = Sim::reset_active(
        SimConfig {
            hazards: crate::terrain::Mode::Map,
            initial_loot: mobs,
            pvp_rules: false,
            faction_mode: crate::statics::FactionMode::EngagementHolyPlagueFireFrostSentenced,
            mob_mode: if mobs {
                crate::mob_runtime::MobMode::Pve
            } else {
                Default::default()
            },
            ..Default::default()
        },
        arena,
        Secrets::from_seed(919),
        &seats,
    );
    sim.w.quests = Some(live::Quests {
        stream: crate::private_rng::Sfc32::from_words([1, 2, 3, 4]),
        offers: vec![live::Offer {
            id: OFFER,
            def: 256 + quest as u16,
            known: true,
            at,
            tokens: vec![at; 4],
            parcel: None,
            status: live::Status::Open,
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
        board: Some(attempts::BoardState::default()),
        rewards: Some(Default::default()),
    });
    sim
}

fn practice(quest: QuestId) -> Sim {
    setup(
        quest,
        crate::arenas::practice_arena(),
        Vec2::new(292., 284.),
        false,
    )
}

fn regional() -> Arena {
    let map: crate::mapdata::MapExport =
        serde_json::from_str(include_str!("../../../../mapdata/fixtures/seed1.map.json")).unwrap();
    map.validate().unwrap().into_arena().unwrap()
}

fn run(sim: &mut Sim, f: impl FnOnce(&mut Cx)) {
    f(&mut Cx {
        stats: Default::default(),
        #[cfg(feature = "trusted-status-probes")]
        probe: None,
        k: &sim.k,
        w: &mut sim.w,
        s: &mut sim.s,
        ev: &mut Vec::new(),
        pf: &mut sim.pf,
    });
}

fn claim(sim: &mut Sim, id: PlayerId) {
    let quest_gv = sim.w.player(id).unwrap().credit.gv_parts.quest;
    let slots = inventory::owned_spells(&sim.w, id);
    run(sim, |cx| {
        attempts::system(cx);
        assert!(attempts::pending(cx.w, id, OFFER).is_some());
        inventory::pay_pending(cx);
        inventory::pay_pending(cx);
    });
    assert!(attempts::is_completed(&sim.w, id, OFFER));
    assert_eq!(sim.w.player(id).unwrap().credit.gv_parts.quest, quest_gv);
    assert_eq!(inventory::owned_spells(&sim.w, id), slots);
}

fn close(actual: f64, expected: f64) {
    assert!((actual - expected).abs() < 1e-8, "{actual} != {expected}");
}

fn move_to(sim: &mut Sim, id: PlayerId, at: Vec2) {
    let i = sim.w.idx(id).unwrap();
    let p = &mut sim.w.players[i];
    p.body.x = at.x;
    p.body.y = at.y;
}

fn damage(
    sim: &mut Sim,
    by: PlayerId,
    victim: PlayerId,
    weapon: WeaponId,
    shares: [f64; 3],
) -> f64 {
    let hp = sim.w.player(victim).unwrap().vitals.hp;
    run(sim, |cx| {
        ledger::open(cx);
        let [phys, magic, true_] = shares;
        cx.s.ledger.push(Entry {
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
            packet: Packet {
                raw: 100.,
                phys,
                magic,
                true_,
            },
            weapon: Some(weapon),
            stagger: false,
            projectile: None,
            scale: 1.,
            derived: false,
            passive: false,
        });
        ledger::apply(cx);
    });
    let i = sim.w.idx(victim).unwrap();
    let victim = &mut sim.w.players[i];
    let dealt = hp - victim.vitals.hp;
    victim.vitals.hp = hp;
    dealt
}

#[test]
fn crown_damage_follows_the_crown_and_caps_at_48_without_double_payment() {
    let mut sim = practice(QuestId::TheBurningCrown);
    let [first, second] = [sim.w.players[0].id, sim.w.players[1].id];
    let giver = sim.w.players[0].pos();
    let ground = Vec2::new(giver.x + 200., giver.y);
    sim.w.quests.as_mut().unwrap().offers[0].tokens[0] = ground;
    move_to(&mut sim, first, ground);
    run(&mut sim, |cx| {
        attempts::accept(cx, 0, first);
        attempts::accept(cx, 0, second);
        attempts::interact(cx, first, OFFER, 3, 0);
    });
    assert_eq!(attempts::crown_power(&sim.w, first), Some(0));
    let hp = sim.w.player(first).unwrap().vitals.hp;
    for _ in 0..140 {
        sim.w.server_time += TICK_MS;
        run(&mut sim, |cx| {
            ledger::open(cx);
            ledger::apply(cx);
            attempts::system(cx);
        });
    }
    close(sim.w.player(first).unwrap().vitals.hp, hp * 0.94);
    close(
        damage(&mut sim, first, second, WeaponId::Sword, [0., 0., 1.]),
        107.,
    );
    assert_eq!(sim.w.player(first).unwrap().farm.count(EmberImp), 0);

    move_to(&mut sim, second, ground);
    run(&mut sim, |cx| {
        attempts::on_drop_body(
            &mut crate::hooks::Hx {
                stats: cx.stats,
                fx: None,
                k: cx.k,
                w: cx.w,
                ev: cx.ev,
            },
            &crate::hooks::HookArgs::DropBody {
                victim: first,
                between_ticks: false,
            },
        );
        attempts::interact(cx, second, OFFER, 3, 0);
    });
    close(
        damage(&mut sim, first, second, WeaponId::Sword, [0., 0., 1.]),
        100.,
    );
    close(
        damage(&mut sim, second, first, WeaponId::Sword, [0., 0., 1.]),
        107.,
    );
    for _ in 0..860 {
        sim.w.server_time += TICK_MS;
        run(&mut sim, |cx| {
            ledger::open(cx);
            ledger::apply(cx);
            attempts::system(cx);
        });
    }
    close(
        damage(&mut sim, second, first, WeaponId::Sword, [0., 0., 1.]),
        148.,
    );
    move_to(&mut sim, second, giver);
    claim(&mut sim, second);
    assert_eq!(attempts::crown_power(&sim.w, second), None);
    assert_eq!(sim.w.player(second).unwrap().farm.count(EmberImp), 48);
    close(
        damage(&mut sim, second, first, WeaponId::Sword, [0., 0., 1.]),
        148.,
    );
}

#[test]
fn trade_up_all_three_cash_outs_raise_max_hp() {
    for (stage, stacks) in [(1, 10), (2, 22), (3, 29)] {
        let mut sim = practice(QuestId::TradeUp);
        let id = sim.w.players[0].id;
        let base = sim.w.players[0].vitals.max_hp;
        run(&mut sim, |cx| {
            attempts::system(cx);
            attempts::accept(cx, 0, id);
        });
        for trader in 0..stage {
            let at = attempts::objective_for_owner(&sim.w, id, OFFER, trader).unwrap();
            move_to(&mut sim, id, at);
            run(&mut sim, |cx| {
                attempts::interact(cx, id, OFFER, 3, trader);
            });
        }
        if stage < 3 {
            run(&mut sim, |cx| {
                attempts::interact(cx, id, OFFER, 4, 0);
            });
        }
        claim(&mut sim, id);
        let p = sim.w.player(id).unwrap();
        assert_eq!(p.farm.count(Penitent), stacks);
        close(p.vitals.max_hp, base * (1. + stacks as f64 * 0.01));
    }
}

#[test]
fn chef_increases_the_lowest_percentage_growth_by_twenty_percent() {
    let mut sim = practice(QuestId::LetHimCook);
    let id = sim.w.players[0].id;
    run(&mut sim, |cx| {
        for (stat, stacks) in [
            (EmberImp, 40),
            (CinderHound, 70),
            (MagmaBrute, 33),
            (Bailiff, 40),
            (ChainWarden, 40),
            (PlagueRat, 10),
            (FrostbowSentry, 10),
        ] {
            assert_eq!(
                modifiers::grant_mob_stat(cx.k, cx.w, id, stat, stacks),
                stacks
            );
        }
        attempts::accept(cx, 0, id);
        for ingredient in 0..3 {
            attempts::interact(cx, id, OFFER, 3, ingredient);
        }
    });
    close(sim.w.players[0].modifiers.mirrors().as_mult, 1.20);
    let before_hp = sim.w.players[0].vitals.max_hp;
    claim(&mut sim, id);
    close(sim.w.players[0].modifiers.mirrors().as_mult, 1.44);
    close(sim.w.players[0].vitals.max_hp, before_hp);

    let saved = serde_json::to_vec(inventory::state(&sim.w).unwrap()).unwrap();
    let hash = crate::hash::hash_of(0, &sim.w);
    *inventory::state_mut(&mut sim.w).unwrap() = serde_json::from_slice(&saved).unwrap();
    assert_eq!(crate::hash::hash_of(0, &sim.w), hash);
    let bonus = inventory::owner_mut(&mut sim.w, id)
        .unwrap()
        .chef_bonus
        .as_mut()
        .unwrap();
    bonus.amount += 0.05;
    assert_ne!(crate::hash::hash_of(0, &sim.w), hash);
    *inventory::state_mut(&mut sim.w).unwrap() = serde_json::from_slice(&saved).unwrap();
    modifiers::recompute(&sim.k, &mut sim.w, FighterIx::of_slot(0));
    close(sim.w.players[0].modifiers.mirrors().as_mult, 1.44);
    inventory::owner_mut(&mut sim.w, id)
        .unwrap()
        .chef_bonus
        .as_mut()
        .unwrap()
        .stat = RotMonk;
    assert!(inventory::validate(&sim.w).is_err());
}

#[test]
fn feed_or_hunt_boosts_the_owners_landed_damage() {
    let mut sim = practice(QuestId::FeedOrHunt);
    let [id, rival] = [sim.w.players[0].id, sim.w.players[1].id];
    let before = damage(&mut sim, id, rival, WeaponId::Sword, [0., 0., 1.]);
    run(&mut sim, |cx| {
        assert!(apply(
            cx,
            id,
            Award::FeedOrHuntDamage,
            attempts::PendingReward::default()
        ));
    });
    close(before, 100.);
    assert_eq!(sim.w.player(id).unwrap().farm.count(EmberImp), 12);
    close(
        damage(&mut sim, id, rival, WeaponId::Sword, [0., 0., 1.]),
        112.,
    );
    close(
        damage(&mut sim, rival, id, WeaponId::Sword, [0., 0., 1.]),
        100.,
    );
}

#[test]
fn warm_coat_stops_bog_movement_slow_and_both_kinds_of_frost_chill() {
    let arena = regional();
    let index = arena
        .map
        .layers()
        .unwrap()
        .data()
        .terrain
        .iter()
        .position(|&kind| crate::terrain::surface(kind, None).speed < 1.)
        .unwrap();
    let at = Vec2::new(
        crate::map::tile_to_world_center((index % arena.map.w as usize) as i64),
        crate::map::tile_to_world_center((index / arena.map.w as usize) as i64),
    );
    let mut sim = setup(QuestId::EvenSkeletonsGetCold, arena, at, false);
    let [id, archer] = [sim.w.players[0].id, sim.w.players[1].id];
    let ix = FighterIx::of_slot(0);
    let now = sim.w.server_time;
    sim.w.players[1].signature.engaged = true;
    sim.w.players[1].signature.target = Some(id);
    let arrow = crate::systems::signatures::LandedHit {
        critical: false,
        cut: 0.,
        ability: None,
        dot: false,
        sig: None,
        weapon: Some(WeaponId::Bow),
        victim: id,
        by: Some(archer),
        amount: 10.,
        race: 1.,
        passive: false,
        derived: false,
    };
    let bog = |sim: &Sim| terrain::bog_value(&sim.k, &sim.w, ix, Default::default());
    let chill =
        |sim: &Sim| frost::movement_modifier(&sim.w, sim.w.core(ix), TICK_MS, Default::default());
    sim.w.players[0].weapon.ready_at = now + 250.;
    frost::ranged_hits(&mut sim.w, std::slice::from_ref(&arrow));
    frost::after_damage(&mut sim.w, sim.k.tick_ms());
    assert!(bog(&sim) < 1.);
    assert!(chill(&sim) < 1.);
    assert!(sim.w.players[0].weapon.ready_at > now + 250.);
    assert!(sim.w.players[0].signature.ranged_chill_until.is_some());

    run(&mut sim, |cx| {
        attempts::accept(cx, 0, id);
        attempts::interact(cx, id, OFFER, 3, 0);
    });
    claim(&mut sim, id);
    let ready = sim.w.players[0].weapon.ready_at;
    frost::ranged_hits(&mut sim.w, &[arrow]);
    frost::after_damage(&mut sim.w, sim.k.tick_ms());
    assert_eq!(bog(&sim), 1.);
    assert_eq!(chill(&sim), 1.);
    assert_eq!(sim.w.players[0].signature.chill, 1.);
    assert_eq!(sim.w.players[0].signature.ranged_chill_until, None);
    assert_eq!(sim.w.players[0].weapon.ready_at, ready);
}

#[test]
fn breakers_helm_bypasses_physical_armor_on_bow_and_axe_only() {
    let mut sim = practice(QuestId::TuberEats);
    let [id, victim] = [sim.w.players[0].id, sim.w.players[1].id];
    assert!(inventory::grant(&mut sim.w, id, Award::Hook));
    assert!(inventory::grant(&mut sim.w, id, Award::Bolas));
    let sheet = crate::content::faction(FactionId::Frost);
    let armor = 1. + sheet.armor / 100.;
    let shield = 1. + sheet.shield / 100.;
    close(
        damage(&mut sim, id, victim, WeaponId::Bow, [0.5, 0.4, 0.1]),
        50. / armor + 40. / shield + 10.,
    );
    run(&mut sim, |cx| {
        attempts::accept(cx, 0, id);
        attempts::interact(cx, id, OFFER, 3, 0);
    });
    claim(&mut sim, id);
    for weapon in [WeaponId::Bow, WeaponId::Axe] {
        close(
            damage(&mut sim, id, victim, weapon, [0.5, 0.4, 0.1]),
            40. / armor + 40. / shield + 20.,
        );
    }
    close(
        damage(&mut sim, id, victim, WeaponId::Sword, [0.5, 0.4, 0.1]),
        50. / armor + 40. / shield + 10.,
    );
}

#[test]
fn bargain_spends_equivalent_stacks_or_the_inactive_weapon_for_real_max_hp() {
    for weapon_trade in [false, true] {
        let mut sim = practice(QuestId::TheBargain);
        let id = sim.w.players[0].id;
        let base = sim.w.players[0].vitals.max_hp;
        let ready_at = sim.w.players[0].weapon.ready_at;
        sim.w.players[0].weapon.slot = 1;
        run(&mut sim, |cx| {
            modifiers::grant_mob_stat(cx.k, cx.w, id, EmberImp, 12);
            modifiers::grant_mob_stat(cx.k, cx.w, id, Inquisitor, 10);
            attempts::accept(cx, 0, id);
            attempts::interact(cx, id, OFFER, if weapon_trade { 7 } else { 5 }, 2);
        });
        claim(&mut sim, id);
        let p = sim.w.player(id).unwrap();
        assert_eq!(p.farm.count(MagmaBrute), 28);
        close(p.vitals.max_hp, base * 1.28);
        assert_eq!(p.active_weapon(), WeaponId::Bow);
        assert_eq!(p.weapon.ready_at, ready_at);
        if weapon_trade {
            assert_eq!(p.weapon.weapon0, None);
            assert_eq!(attempts::bargain_stacks(p, 0), 22);
        } else {
            assert_eq!(p.weapon.weapon0, Some(WeaponId::Sword));
            assert_eq!(p.farm.count(EmberImp), 0);
            assert_eq!(p.farm.count(Inquisitor), 2);
        }
    }
}
