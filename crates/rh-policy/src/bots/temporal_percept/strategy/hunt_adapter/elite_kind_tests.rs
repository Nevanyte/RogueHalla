//! P-BAL-ELITE-BOTS-1 oracle: a per-kind elite row moves the remembered elite's max-hp estimate of that kind only.
use super::*;
use rh_sim::mob_catalog::MobTypeId::Wyrmling;
use rh_sim::mob_runtime::{EliteKindRow, EliteTuning};
use rh_sim::EliteRule::Rework;

static ROWS: [EliteKindRow; 1] = [EliteKindRow {
    ty: Wyrmling,
    hp: 1.25,
    dmg: 0.8,
}];

fn name_of(id: &str) -> &'static str {
    mob_catalog::mob_type_by_id(id).expect("catalog kind").name
}

#[test]
fn a_row_moves_the_hp_estimate_of_its_kind_only() {
    let real = EliteTuning::of(Rework);
    assert!(real.per_kind.is_empty(), "the real Rework table is empty");
    let doctored = EliteTuning {
        per_kind: &ROWS,
        ..*real
    };
    let zone = FactionId::Fire;
    let (wyrmling, drake) = (name_of("wyrmling"), name_of("ashDrake"));
    let est = |t: &EliteTuning, elite: bool, name: Option<&str>| {
        mob_max_hp_under(Some(zone), elite, name, t)
    };

    let (a, b) = (
        est(real, true, Some(wyrmling)),
        est(&doctored, true, Some(wyrmling)),
    );
    assert!(
        (b / a - 1.25).abs() < 1e-9,
        "Wyrmling: doctored estimate {b} is not the real {a} x row 1.25"
    );
    for (what, name) in [("AshDrake", Some(drake)), ("an unnamed elite", None)] {
        assert_eq!(
            est(real, true, name).to_bits(),
            est(&doctored, true, name).to_bits(),
            "{what} must not move with the Wyrmling row"
        );
    }
    assert_eq!(
        est(real, false, Some(wyrmling)).to_bits(),
        est(&doctored, false, Some(wyrmling)).to_bits(),
        "a regional mob ignores the elite table"
    );
    // The real table gives today's rule-based estimate.
    assert_eq!(a, mob_max_hp(Some(zone), true, Some(wyrmling), Rework));
}
