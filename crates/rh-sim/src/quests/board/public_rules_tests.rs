//! P-RAID-TOP25-READ-1 oracle (rh-sim half): every field of the public quest rules equals the `TOP25` field it is built
//! from, so a retune moves both and no copy can drift.
use super::{public_rules, QuestPublicRules, TOP25};

#[test]
fn public_rules_equal_the_table_fields_they_come_from() {
    // No `..`: a field added to the struct without a line here does not compile.
    let QuestPublicRules {
        fort_ms,
        meditate_ms,
        contact_range,
        fort_range,
        bargain_cost,
        bargain_stacks,
        route_pace,
        route_slack_ms,
    } = public_rules();
    assert_eq!(fort_ms, TOP25.fort_ms);
    assert_eq!(meditate_ms, TOP25.meditate_ms);
    assert_eq!(contact_range, TOP25.contact_range);
    assert_eq!(fort_range, TOP25.fort_range);
    assert_eq!(bargain_cost, TOP25.bargain_cost);
    assert_eq!(bargain_stacks, TOP25.bargain_stacks);
    assert_eq!(route_pace, TOP25.route_pace);
    assert_eq!(route_slack_ms, TOP25.route_slack_ms);
}

#[test]
fn public_rules_hold_todays_numbers() {
    // The numbers the bots carried by hand before this packet: this packet moves none of them.
    let r = public_rules();
    assert_eq!((r.fort_ms, r.meditate_ms), (35_000., 30_000.));
    assert_eq!((r.contact_range, r.fort_range), (64., 72.));
    assert_eq!((r.bargain_cost, r.bargain_stacks), (20, 30));
    assert_eq!((r.route_pace, r.route_slack_ms), (1500., 15_000.));
}
