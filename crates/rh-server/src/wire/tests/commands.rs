//! Public commands and arena limits: the position-grid bound, PvE commands and notices, target conversion.

use super::*;

fn flat_arena(w: u32, h: u32) -> rh_sim::Arena {
    rh_sim::Arena {
        map: rh_sim::TileMap::new(w, h, vec![0; (w * h) as usize]).expect("map"),
        spawns: vec![rh_sim::Vec2::new(48.0, 48.0)],
    }
}

/// 255 tiles (8160 u) fits the position grid; 256 (8192 u) does not, on either axis, and the refusal names the limit.
#[test]
fn an_arena_larger_than_the_position_grid_is_refused() {
    assert!(arena_fits(&flat_arena(255, 255)).is_ok());
    assert!(arena_fits(&flat_arena(160, 160)).is_ok());
    for (w, h) in [(256, 4), (4, 256), (300, 300)] {
        let e = arena_fits(&flat_arena(w, h)).expect_err("too large");
        assert!(e.contains("8191.875") && e.contains("255 tiles"), "{e}");
    }
    let mut a = flat_arena(8, 8);
    a.spawns.push(rh_sim::Vec2::new(9000.0, 1.0));
    assert!(arena_fits(&a).expect_err("spawn").contains("spawn"));
    assert!(arena_fits(&rh_sim::arenas::practice_arena()).is_ok());
}
#[test]
fn pve_commands_and_notices_keep_normal_public_semantics() {
    assert_eq!(
        command(WireCommand::UseConsumable),
        rh_sim::PublicCommand::UseConsumable
    );
    assert_eq!(
        command(WireCommand::Target { actor: 0x8000_0201 }),
        rh_sim::PublicCommand::Target {
            actor: rh_sim::PlayerId(0x8000_0201)
        }
    );
    assert_eq!(
        command(WireCommand::HuntTarget { actor: 2 }),
        rh_sim::PublicCommand::HuntTarget {
            actor: rh_sim::PlayerId(2)
        }
    );
    assert_eq!(
        command(WireCommand::AttackMove {
            x: 12.5,
            y: 20.,
            seq: Some(9)
        }),
        rh_sim::PublicCommand::AttackMove {
            x: 12.5,
            y: 20.,
            seq: Some(9)
        }
    );
    assert_eq!(
        command(WireCommand::Cast {
            slot: 0,
            x: 12.5,
            y: 20.0,
            actor: Some(17),
            t: Some(777.0),
            seq: Some(9),
        }),
        rh_sim::PublicCommand::Cast {
            slot: rh_sim::abilities::Slot::Wish,
            x: 12.5,
            y: 20.0,
            id: Some(rh_sim::PlayerId(17)),
            t: Some(777.0),
            seq: Some(9),
        }
    );
    use rh_sim::potions::Notice::*;
    for (notice, code) in [Stored, BagFull, Used, Blocked, Busy, Full, Empty]
        .into_iter()
        .zip(1..=7)
    {
        let event = SeenEvent::PotionNotice { notice };
        assert_eq!(event.class(), Class::Important);
        assert!(
            matches!(to_wire(&event),Wire::Event(WireEvent::PotionNotice {notice:n}) if n==code)
        );
    }
    let env = seed1_match(2);
    let id = env.sim.players()[0].id;
    let mut own = rh_sim::observe(&env.sim, id, &[]).me.unwrap();
    own.barrier = 12.5;
    own.barrier_ring_step = 4;
    own.wish = Some(rh_sim::abilities::OwnerView {
        ability: rh_sim::status_runtime::AbilityId::Freeze,
        ready_at: 12.0,
        full_at: Some(42.0),
        max_charges: 1,
        recharge_ms: 45_000.0,
        charges: 1,
        next_charge_at: None,
        cast: Some(rh_sim::abilities::CastView {
            ability: rh_sim::status_runtime::AbilityId::Freeze,
            started_at: 10.0,
            lands_at: 260.0,
        }),
    });
    own.sig = 255;
    let owner_wire = wire_self(&own);
    assert_eq!(owner_wire.sig, 255);
    assert_eq!(
        (owner_wire.barrier, owner_wire.barrier_ring_step),
        (12.5, 4)
    );
    let wish = owner_wire.wish.unwrap();
    assert_eq!(wish.ability, 0, "Freeze keeps its protocol ability code");
    assert_eq!(
        (
            wish.ready_at,
            wish.full_at,
            wish.max_charges,
            wish.recharge_ms,
            wish.charges,
            wish.next_charge_at,
            wish.cast.unwrap().started_at,
            wish.cast.unwrap().lands_at,
        ),
        (12.0, Some(42.0), 1, 45_000.0, 1, None, 10.0, 260.0)
    );
    own.sig = 0;
    assert_eq!(wire_self(&own).sig, 0);
    let mut actor = view(9);
    actor.barrier_frac = 0.25;
    actor.barrier_ring_step = 3;
    actor.sig = 255;
    actor.mob_type = Some(rh_sim::mob_catalog::MobTypeId::Archon);
    let wire = wire_actor(&actor).unwrap();
    assert_eq!((wire.sig, wire.mob_type_code), (255, 30));
    assert_eq!((wire.barrier_pct, wire.barrier_ring_step), (25, 3));
    actor.sig = 0;
    actor.mob_type = None;
    let wire = wire_actor(&actor).unwrap();
    assert_eq!((wire.sig, wire.mob_type_code), (0, 0));
}

#[test]
fn target_conversion_never_bypasses_observer_delivery() {
    use rh_sim::{ItemObserver, Placement, PlayerId, Secrets, Sim, Vec2};
    let sim = Sim::reset_active(
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
                at: Vec2::new(120., 272.),
                hp: None,
                weapon0: None,
                weapon1: None,
            },
        ],
    );
    let mut observer = ItemObserver::new(PlayerId(1));
    let proposal = command(WireCommand::Target { actor: 2 });
    let missing = rh_sim::validate(rh_sim::RawCommand::MissingTarget).unwrap();
    assert_eq!(observer.resolve(&sim, proposal).unwrap(), missing);
    let mut obs = Observation::default();
    drop(observer.prepare_into(&sim, &[], &mut obs, None).unwrap());
    assert_eq!(observer.resolve(&sim, proposal).unwrap(), missing);
    observer
        .prepare_into(&sim, &[], &mut obs, None)
        .unwrap()
        .commit();
    assert_eq!(
        observer.resolve(&sim, proposal).unwrap(),
        rh_sim::validate(rh_sim::RawCommand::Target { id: PlayerId(2) }).unwrap()
    );
    assert_eq!(
        observer
            .resolve(&sim, command(WireCommand::Target { actor: 0x8000_0201 }))
            .unwrap(),
        missing
    );
    assert_eq!(
        observer
            .resolve(&sim, command(WireCommand::Target { actor: 999 }))
            .unwrap(),
        missing
    );
}
