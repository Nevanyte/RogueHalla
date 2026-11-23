//! P-RAID-TOP25-READ-1 oracle (rh-policy half): the quest executors read the quest numbers from the sim's public rules
//! (`rh_sim::quests::public_rules`) and keep no hand copy of them. The numbers do not change in this packet, so the
//! behaviour pins hold on the base too; the source scans are what is red there.
use super::dwell::dwell_seconds;
use super::walker::timer_spend_minutes;
use rh_sim::quests::public_rules;

/// Whether a file declares `name` as a `const` or `static` (any visibility).
fn declares(source: &str, name: &str) -> bool {
    source.lines().any(|l| {
        let l = l.trim_start();
        let l = l
            .strip_prefix("pub(super) ")
            .or_else(|| l.strip_prefix("pub(crate) "))
            .or_else(|| l.strip_prefix("pub "))
            .unwrap_or(l);
        (l.starts_with("const ") || l.starts_with("static ")) && l.contains(&format!("{name}:"))
    })
}

const WALKER: &str = include_str!("walker.rs");
const DWELL: &str = include_str!("dwell.rs");
const DECIDE: &str = include_str!("decide.rs");

#[test]
fn the_dwell_and_bargain_numbers_are_not_declared_by_hand() {
    for (file, source, names) in [
        ("dwell.rs", DWELL, &["HOLD_S", "STAY_S", "FORT_RANGE"][..]),
        ("decide.rs", DECIDE, &["COST_STACKS", "GAIN_STACKS"][..]),
    ] {
        for name in names {
            assert!(!declares(source, name), "{file} still declares `{name}`");
        }
    }
}

#[test]
fn the_route_timer_numbers_are_not_declared_by_hand() {
    // Separate from the test above: if the coordinator rules the route formula is not publishable, this test and the
    // route fields of the public struct go together.
    assert!(
        !declares(WALKER, "TIMER_BASE_S"),
        "walker.rs still declares `TIMER_BASE_S`"
    );
    let compact: String = WALKER.split_whitespace().collect();
    assert!(
        !compact.contains("TIMER_BASE_S)/1.5/60."),
        "walker.rs still carries the 1.5 pace inline"
    );
}

#[test]
fn the_stand_distance_is_derived_from_the_contact_range() {
    // `STAND_U = 32.` was a hand number, half of `contact_range` (64): it is now a local rule over the read number.
    for (file, source) in [
        ("walker.rs", WALKER),
        ("dwell.rs", DWELL),
        ("decide.rs", DECIDE),
    ] {
        assert!(
            !declares(source, "STAND_U"),
            "{file} still declares `STAND_U` as a constant"
        );
        assert!(
            !source.contains("= 32.;"),
            "{file} still carries a literal 32."
        );
    }
}

#[test]
fn the_executors_read_the_public_rules() {
    for (file, source) in [
        ("walker.rs", WALKER),
        ("dwell.rs", DWELL),
        ("decide.rs", DECIDE),
    ] {
        assert!(
            source.contains("public_rules()"),
            "{file} does not read `public_rules()`"
        );
    }
}

#[test]
fn the_walker_timer_estimate_follows_the_public_rules() {
    let r = public_rules();
    for ms in [20_000_u64, 75_000, 200_000, 600_000] {
        let want = ((ms as f64 / 1000. - r.route_slack_ms / 1000.) / (r.route_pace / 1000.) / 60.)
            .max(0.1);
        let got = timer_spend_minutes(ms);
        assert!((got - want).abs() < 1e-12, "{ms} ms: {got} vs {want}");
    }
    assert_eq!(timer_spend_minutes(1), 0.1, "the floor stays");
}

#[test]
fn the_dwell_seconds_follow_the_public_rules() {
    let r = public_rules();
    assert_eq!(dwell_seconds("hold_the_fort") * 1000., r.fort_ms);
    assert_eq!(dwell_seconds("stay_a_while") * 1000., r.meditate_ms);
}
