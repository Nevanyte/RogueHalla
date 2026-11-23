//! P-BAL-ELITE-BOTS-1 oracle: a per-kind elite row moves the planning sheet of that kind only.
use super::*;
use rh_sim::mob_catalog::MobTypeId::{AshDrake, Wyrmling};
use rh_sim::mob_runtime::{EliteKindRow, EliteTuning};
use rh_sim::EliteRule::Rework;

static ROWS: [EliteKindRow; 1] = [EliteKindRow {
    ty: Wyrmling,
    hp: 1.25,
    dmg: 0.8,
}];

#[test]
fn a_row_moves_the_planning_sheet_of_its_kind_only() {
    let real = EliteTuning::of(Rework);
    assert!(real.per_kind.is_empty(), "the real Rework table is empty");
    let doctored = EliteTuning {
        per_kind: &ROWS,
        ..*real
    };
    let f = FactionId::Fire;
    let (a, b) = (
        mob_faction_under(f, true, Some(Wyrmling), real),
        mob_faction_under(f, true, Some(Wyrmling), &doctored),
    );
    assert!(
        (b.hp / a.hp - 1.25).abs() < 1e-9,
        "Wyrmling hp: doctored {} is not the real {} x row 1.25",
        b.hp,
        a.hp
    );
    assert!(
        (b.hit / a.hit - 0.8).abs() < 1e-9,
        "Wyrmling hit: doctored {} is not the real {} x row 0.8",
        b.hit,
        a.hit
    );
    for (what, ty, elite) in [
        ("AshDrake", Some(AshDrake), true),
        ("an elite of unknown kind", None, true),
        ("a regional mob", Some(Wyrmling), false),
    ] {
        let (x, y) = (
            mob_faction_under(f, elite, ty, real),
            mob_faction_under(f, elite, ty, &doctored),
        );
        assert_eq!(
            (x.hp.to_bits(), x.hit.to_bits()),
            (y.hp.to_bits(), y.hit.to_bits()),
            "{what} must not move with the Wyrmling row"
        );
    }
    // With the real (empty) table the sheet is today's: the rule-based entry point.
    let today = mob_faction(f, true, Rework);
    assert_eq!((a.hp, a.hit), (today.hp, today.hit));
}
