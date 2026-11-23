//! P-RAID-E-leave oracle (docs/backlog/packets/P-RAID-E-leave.md): the pure functions of the packet through their public
//! paths, so the implementer keeps them exported. The frames that pin the behaviour are in-crate
//! (`exec/elite_leave_tests.rs`). Read-only for the implementer.
use rh_policy::bots::raid::exec::elite::{ban_seconds_after, elite_fight_lost};
use rh_policy::bots::raid::exec::held::hp_held;
use rh_policy::bots::raid::values::Prices;
use rh_sim::mob_catalog::MobTypeId;
use rh_sim::WeaponId;

/// The ban doubles with each give-up at one camp, from `ban_seconds`, up to `ban_max_seconds` (`ElitePrices::LOCAL`).
#[test]
fn the_ban_table_grows_from_the_base_ban_to_its_cap() {
    let e = Prices::LOCAL.elite;
    let table: Vec<f64> = (1..=4).map(|n| ban_seconds_after(&e, n)).collect();
    assert_eq!(table, vec![20., 40., 80., 160.], "ban seconds by give-up");
    assert_eq!(ban_seconds_after(&e, 30), e.ban_max_seconds);
}

/// A drink moves a potion from the stock to a heal running and changes nothing the executors price with.
#[test]
fn the_hp_held_is_the_same_before_and_after_a_drink() {
    let s = Prices::LOCAL.sustain;
    let before = hp_held(&s, 0.47, 1, 0, 0, None, 10_000., &[]);
    let after = hp_held(
        &s,
        0.47,
        0,
        0,
        1,
        Some(10_000. + s.heal_seconds * 1_000.),
        10_000.,
        &[s.large_share],
    );
    assert!((before - after).abs() < 1e-9, "{before} against {after}");
    assert!((before - (0.47 + s.large_share)).abs() < 1e-9);
}

/// Seen from outside: the favourable and the unfavourable fight of the packet's frames.
#[test]
fn the_elite_fight_is_lost_for_the_troll_and_not_for_the_executioner() {
    let p = &Prices::LOCAL;
    let sword = Some(WeaponId::Sword);
    assert!(!elite_fight_lost(
        p,
        MobTypeId::Executioner,
        sword,
        36.,
        0.5,
        1.3,
        0.365
    ));
    assert!(elite_fight_lost(
        p,
        MobTypeId::AvalancheTroll,
        sword,
        24.,
        0.5,
        0.5,
        1.
    ));
}
