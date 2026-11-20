//! P-TICK-1b-fix (ADR 0029 decision 3): a 25 ms sim keeps the scenario-rack window and the 1000 ms handle grace of a
//! 50 ms one. `Sim::reset_active` steps to 50 ms of sim time at every rate (tick 1 at 50 ms, tick 2 at 25 ms).
use super::*;
use crate::{arenas::practice_arena, FactionId, Placement, Secrets, SimConfig};

fn world(tick_ms: u32) -> Sim {
    let place = |name: &str, at, faction| Placement {
        profile: name.into(),
        at,
        faction,
        bot: false,
        hp: None,
        weapon0: None,
        weapon1: None,
    };
    let mut arena = practice_arena();
    arena.spawns = vec![Vec2::new(100., 272.)];
    let sim = Sim::reset_active(
        SimConfig {
            consumables: false,
            tick_ms,
            ..SimConfig::default()
        },
        arena,
        Secrets::from_seed(4),
        &[
            place("viewer", Vec2::new(100., 272.), FactionId::Holy),
            place("Ása", Vec2::new(1400.0, 272.0), FactionId::Fire),
        ],
    );
    assert_eq!(sim.tick_ms(), tick_ms);
    sim
}

#[test]
fn a_scenario_rack_is_seeded_after_reset_active_at_both_rates_and_refused_one_step_later() {
    for tick_ms in [50, 25] {
        let mut s = world(tick_ms);
        let rack = |s: &mut Sim| {
            s.seed_weapon_container(
                ContainerKind::Chest,
                Vec2::new(100., 272.),
                &[WeaponId::Sword],
            )
        };
        assert!(
            rack(&mut s).is_ok(),
            "{tick_ms} ms: a rack after reset_active"
        );
        s.step();
        assert!(
            rack(&mut s).is_err(),
            "{tick_ms} ms: a rack once play has run"
        );
    }
}

#[test]
fn a_retired_pickup_handle_resolves_for_1000_ms_at_both_rates() {
    for tick_ms in [50u32, 25] {
        let mut s = world(tick_ms);
        s.seed_weapon_container(
            ContainerKind::Chest,
            Vec2::new(100., 272.),
            &[WeaponId::Sword],
        )
        .unwrap();
        let mut seat = ItemObserver::new(PlayerId(1));
        let mut obs = Observation::default();
        seat.observe_into(&s, &[], &mut obs, None).unwrap();
        let h = obs.props[0].handle;
        let exact = seat
            .resolve(&s, PublicCommand::Pickup { handle: h })
            .unwrap();
        // The rack moves every step, so every sample issues a new batch and retires the previous one: `h` is retired on
        // the first step and is `age - 1` steps old in its retired ring at step `age`.
        for age in 1..=44u32 {
            s.step();
            s.w.containers[0].at.x += 0.25;
            seat.observe_into(&s, &[], &mut obs, None).unwrap();
            let retired_ms = f64::from(age - 1) * f64::from(tick_ms);
            assert_eq!(
                seat.resolve(&s, PublicCommand::Pickup { handle: h })
                    .unwrap()
                    == exact,
                retired_ms < 1000.,
                "{tick_ms} ms: handle retired {retired_ms} ms ago"
            );
        }
    }
}
