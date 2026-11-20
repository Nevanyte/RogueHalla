//! P-TICK-1b (ADR 0029): a burn (a damage-over-time status) deals the same damage per second at 25 ms as at 50 ms.
//! The public walk, bow, regen, walk-hint and checkpoint checks are `tests/tick_rate.rs`; this one applies the status
//! directly (`status::operations::apply`), which the public API cannot do on its own.
//!
//! Legacy: a DoT deals `value * dtMs / 1000` per step while `until > now` (Rust `systems/status/dot.rs`), and the PvP
//! DoT cap is `PVP_DOT_CAP_SHARE * maxHp * dtMs / 1000` per step (`systems/ledger.rs`): both are per-second rates.

use super::status::operations;
use crate::{
    content::DamageShares,
    status_runtime::{self as rt, StatusKind},
    FactionId, Placement, PlayerId, Secrets, Sim, SimConfig, Vec2,
};

fn sim(tick_ms: u32) -> Sim {
    let ps: Vec<_> = (0..2)
        .map(|i| Placement {
            profile: format!("p{i}"),
            faction: FactionId::Holy,
            bot: false,
            at: Vec2::new(400. + 300. * i as f64, 400.),
            hp: Some(500.),
            weapon0: None,
            weapon1: None,
        })
        .collect();
    Sim::reset_active(
        SimConfig {
            tick_ms,
            ..SimConfig::default()
        },
        crate::arenas::practice_arena(),
        Secrets::from_seed(4),
        &ps,
    )
}

/// A true-damage burn of `dps` for `ms`, from player 1 (a Blaze row: no owner multiplier, no crit roll).
fn burn(s: &Sim, dps: f64, ms: f64) -> rt::StatusApply {
    rt::StatusApply {
        kind: StatusKind::Dot,
        source: rt::StatusSource {
            id: Some(PlayerId(1)),
            name: s.w.roster.label(PlayerId(1)).unwrap().clone(),
            owner_id: None,
        },
        ms,
        ability: Some(rt::AbilityId::Blaze),
        tag: None,
        value: Some(dps),
        add: None,
        data: Some(rt::Payload {
            shares: Some(DamageShares {
                phys: 0.,
                magic: 0.,
                true_: 1.,
            }),
            ..Default::default()
        }),
    }
}

/// Burns player 2 with `dps` for `ms`, applied between steps at time t0. Returns (damage over its whole duration,
/// damage from t0 + 500 to t0 + 1500).
fn burned(tick_ms: u32, dps: f64, ms: f64) -> (f64, f64) {
    let mut s = sim(tick_ms);
    assert_eq!(s.tick_ms(), tick_ms);
    let t0 = s.server_time();
    let a = burn(&s, dps, ms);
    let mut ev = Vec::new();
    operations::apply(&s.k, &mut s.w, &mut ev, PlayerId(2), a, false);
    let hp = |s: &Sim| s.w.players[1].vitals.hp;
    let start = hp(&s);
    let (mut at500, mut at1500) = (None, None);
    while s.server_time() < t0 + ms + 1000. {
        s.step();
        if s.server_time() == t0 + 500. {
            at500 = Some(hp(&s));
        }
        if s.server_time() == t0 + 1500. {
            at1500 = Some(hp(&s));
        }
    }
    (
        start - hp(&s),
        at500.expect("t0 + 500 is on the grid") - at1500.expect("t0 + 1500 is on the grid"),
    )
}

#[test]
fn a_burn_deals_the_same_damage_per_second_at_25_ms_as_at_50() {
    let (dps, ms) = (30., 2000.);
    let (total50, second50) = burned(50, dps, ms);
    let (total25, second25) = burned(25, dps, ms);
    // One second inside the burn deals `dps` at both rates.
    for (tick_ms, got) in [(50, second50), (25, second25)] {
        assert!(
            (got - dps).abs() <= 1e-6 * dps,
            "{tick_ms} ms: {got} damage in one second of a {dps}/s burn"
        );
    }
    // Over its whole duration, a burn applied between steps first bites on the next step (until = t0 + ms, damage while
    // until > now): `dps * (ms - tick_ms) / 1000`. The two rates differ by at most one 25 ms step of burn.
    for (tick_ms, got) in [(50., total50), (25., total25)] {
        let want = dps * (ms - tick_ms) / 1000.;
        assert!(
            (got - want).abs() <= 1e-6 * want,
            "{tick_ms} ms: the whole burn dealt {got}, want {want}"
        );
    }
    assert!((total25 - total50).abs() <= dps * 25. / 1000. + 1e-9);
}

#[test]
fn the_pvp_dot_cap_is_the_same_per_second_at_25_ms_as_at_50() {
    // A burn far over the cap deals `PVP_DOT_CAP_SHARE * max_hp` a second at both rates.
    let mut per_second = Vec::new();
    for tick_ms in [50, 25] {
        let (_, second) = burned(tick_ms, 10_000., 2000.);
        let max_hp = sim(tick_ms).w.players[1].vitals.max_hp;
        let want = crate::constants::PVP_DOT_CAP_SHARE * max_hp;
        assert!(
            (second - want).abs() <= 1e-6 * want,
            "{tick_ms} ms: {second} capped damage in one second, want {want}"
        );
        per_second.push(second);
    }
    assert!((per_second[0] - per_second[1]).abs() <= 1e-6 * per_second[0]);
}
