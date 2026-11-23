//! Oracle for packet P-BAL-LAB-ELITE-1-fix (docs/backlog/packets/P-BAL-LAB-ELITE-1-fix.md): the balance lab counts a chest
//! pick exactly once, whatever handle the sim shows the chest under. Scripted tests pin the pure `tally_chests` on
//! hand-written steps; real-run tests (release only) pin that the report adds up. Loose proof (owner direction
//! 2026-11-10, new tooling).
use rh_headless::balance_lab::chest_picks::{
    tally_chests, ChestKind, ChestRow, ChestStep, ChestTally, OptionRow,
};
use rh_headless::balance_lab::{run, Options, SeatPolicy};
use serde_json::Value;

fn opt(name: &str, kind: &str, taken: bool) -> OptionRow {
    OptionRow {
        name: name.into(),
        kind: kind.into(),
        taken_by_seat: taken,
    }
}
fn card(name: &str, taken: bool) -> OptionRow {
    opt(name, "card", taken)
}
/// A chest at (x, 0) expiring at `exp`, as `seat` sees it.
fn row(seat: usize, exp: f64, x: f64, kind: ChestKind, options: Vec<OptionRow>) -> ChestRow {
    ChestRow {
        seat,
        x,
        y: 0.,
        expires_at: exp,
        can_choose: true,
        kind,
        seat_is_killer: false,
        options,
    }
}
fn step(taken_events: u64, rows: Vec<ChestRow>) -> ChestStep {
    ChestStep { taken_events, rows }
}
fn sum(m: &std::collections::BTreeMap<String, u64>) -> u64 {
    m.values().sum()
}
fn picked(t: &ChestTally, name: &str) -> u64 {
    t.cards.get(name).map_or(0, |c| c.picked)
}
fn offered(t: &ChestTally, name: &str) -> u64 {
    t.cards.get(name).map_or(0, |c| c.offered)
}
/// What holds for every tally: named plus unnamed is the events, the kinds split the same picks, a pick is an offer.
fn check(t: &ChestTally, events: u64) {
    assert_eq!(t.taken_events, events, "{t:?}");
    assert_eq!(sum(&t.picks_by_kind) + t.picks_unnamed, events, "{t:?}");
    assert_eq!(sum(&t.picks_by_chest_kind), sum(&t.picks_by_kind), "{t:?}");
    for (n, c) in &t.cards {
        assert!(c.picked <= c.offered, "{n}: {c:?}");
        assert!(
            c.picked_by_killer + c.picked_by_bystander <= c.picked,
            "{n}: {c:?}"
        );
    }
}

#[test]
fn a_pick_by_one_seat_then_another_with_a_reissued_handle_counts_each_card_once() {
    // Two seats see one chest with cards a and b. Seat 0 takes a (step 2); seat 1 takes b (step 3). In step 3 seat 0's
    // view is the same chest again (the sim reissued its handle; the lab no longer sees a handle at all).
    let (e, x) = (30_000., 10.);
    let both = |seat, a, b| {
        row(
            seat,
            e,
            x,
            ChestKind::Ground,
            vec![card("a", a), card("b", b)],
        )
    };
    let steps = [
        step(0, vec![both(0, false, false), both(1, false, false)]),
        step(1, vec![both(0, true, false), both(1, false, false)]),
        step(1, vec![both(0, true, false), both(1, false, true)]),
        step(0, vec![both(0, true, false), both(1, false, true)]),
    ];
    let t = tally_chests(&steps);
    check(&t, 2);
    assert_eq!((picked(&t, "a"), picked(&t, "b")), (1, 1), "{t:?}");
    assert_eq!(
        (offered(&t, "a"), offered(&t, "b")),
        (2, 2),
        "one offer per seat: {t:?}"
    );
    assert_eq!(t.chests_offered, 2, "{t:?}");
    assert_eq!(t.picks_by_kind.get("card"), Some(&2), "{t:?}");
    assert_eq!(t.picks_by_chest_kind.get("ground"), Some(&2), "{t:?}");
    assert_eq!(t.picks_unnamed, 0);
}

#[test]
fn a_chest_seen_by_a_seat_in_many_steps_is_offered_once() {
    let one = || {
        row(
            0,
            30_000.,
            5.,
            ChestKind::Ground,
            vec![card("a", false), opt("fast", "boost", false)],
        )
    };
    let steps = [
        step(0, vec![one()]),
        step(0, vec![one()]),
        step(0, vec![one()]),
    ];
    let t = tally_chests(&steps);
    check(&t, 0);
    assert_eq!(t.chests_offered, 1, "{t:?}");
    assert_eq!((offered(&t, "a"), offered(&t, "fast")), (1, 1), "{t:?}");
    assert_eq!((picked(&t, "a"), picked(&t, "fast")), (0, 0), "{t:?}");
}

#[test]
fn an_option_first_seen_already_taken_by_the_seat_is_offered_once_and_picked_once() {
    // The seat's first sight of the chest is after its pick (it could not choose any more).
    let mut r = row(
        0,
        30_000.,
        5.,
        ChestKind::Trophy,
        vec![opt("kill:x", "kill", true), opt("kill:y", "kill", false)],
    );
    r.can_choose = false;
    let steps = [step(1, vec![r.clone()]), step(0, vec![r])];
    let t = tally_chests(&steps);
    check(&t, 1);
    assert_eq!(
        (offered(&t, "kill:x"), picked(&t, "kill:x")),
        (1, 1),
        "{t:?}"
    );
    assert_eq!(
        offered(&t, "kill:y"),
        0,
        "not choosable, never offered: {t:?}"
    );
    assert_eq!(t.picks_by_kind.get("kill"), Some(&1), "{t:?}");
    assert_eq!(t.picks_by_chest_kind.get("trophy"), Some(&1), "{t:?}");
}

#[test]
fn a_chest_taken_event_with_no_row_to_name_it_is_unnamed() {
    // A pick on the last step (no observations), and a skip (an event, no option taken anywhere).
    let one = || row(0, 30_000., 5., ChestKind::Ground, vec![card("a", false)]);
    let steps = [
        step(0, vec![one()]),
        step(1, vec![one()]), // a skip: the event, and the chest shows nothing taken
        step(1, vec![]),      // the last step: no rows
    ];
    let t = tally_chests(&steps);
    check(&t, 2);
    assert_eq!(t.picks_unnamed, 2, "{t:?}");
    assert_eq!(sum(&t.picks_by_kind), 0, "{t:?}");

    // Named plus unnamed is the events.
    let steps = [
        step(0, vec![one()]),
        step(
            2,
            vec![row(
                0,
                30_000.,
                5.,
                ChestKind::Ground,
                vec![card("a", true)],
            )],
        ),
    ];
    let t = tally_chests(&steps);
    check(&t, 2);
    assert_eq!((sum(&t.picks_by_kind), t.picks_unnamed), (1, 1), "{t:?}");
}

#[test]
fn two_chests_with_the_same_expiry_at_different_places_are_two_chests() {
    let at = |x, taken| row(0, 30_000., x, ChestKind::Ground, vec![card("a", taken)]);
    let steps = [
        step(0, vec![at(5., false), at(50., false)]),
        step(1, vec![at(5., true), at(50., false)]),
        step(1, vec![at(5., true), at(50., true)]),
    ];
    let t = tally_chests(&steps);
    check(&t, 2);
    assert_eq!(t.chests_offered, 2, "{t:?}");
    assert_eq!((offered(&t, "a"), picked(&t, "a")), (2, 2), "{t:?}");
}

#[test]
fn the_killer_and_bystander_split_of_an_elite_chest_counts_each_pick_once() {
    // One elite chest; seat 0 is the killer and takes a, seat 1 takes a too. A trophy pick of a never counts as either.
    let view = |seat, killer, taken| {
        let mut r = row(
            seat,
            40_000.,
            7.,
            ChestKind::Elite,
            vec![card("a", taken), card("b", false)],
        );
        r.seat_is_killer = killer;
        r
    };
    let trophy = row(0, 41_000., 9., ChestKind::Trophy, vec![card("a", true)]);
    let steps = [
        step(0, vec![view(0, true, false), view(1, false, false)]),
        step(
            2,
            vec![view(0, true, true), view(1, false, false), trophy.clone()],
        ),
        step(
            1,
            vec![view(0, true, true), view(1, false, true), trophy.clone()],
        ),
        step(0, vec![view(0, true, true), view(1, false, true), trophy]),
    ];
    let t = tally_chests(&steps);
    check(&t, 3);
    let a = &t.cards["a"];
    assert_eq!(a.picked, 3, "{t:?}");
    assert_eq!((a.picked_by_killer, a.picked_by_bystander), (1, 1), "{t:?}");
    assert_eq!(t.picks_by_chest_kind.get("elite"), Some(&2), "{t:?}");
    assert_eq!(t.picks_by_chest_kind.get("trophy"), Some(&1), "{t:?}");
}

// ---- real runs (release only) ----

const SEEDS: std::ops::Range<u64> = 1..4;
const MAX_TICKS: u32 = 6000;
/// Seeds 1 to 3 at 6000 ticks show about nine offers and ten events (P-BAL-LAB-ELITE-1 F3); a floor well under that.
const MIN_EVENTS: u64 = 3;

fn report() -> &'static Value {
    static R: std::sync::OnceLock<Value> = std::sync::OnceLock::new();
    R.get_or_init(|| {
        let opts = Options {
            seeds: SEEDS,
            jobs: 1,
            max_ticks: MAX_TICKS,
            human_seats: true,
            seats: (0..8).map(|i| (i, SeatPolicy::Raid)).collect(),
            ..Options::default()
        };
        serde_json::from_str(&run(&opts).expect("lab run").json).expect("json")
    })
}
fn n(v: &Value, k: &str) -> u64 {
    v[k].as_u64()
        .unwrap_or_else(|| panic!("`{k}` is missing or not a number in {v}"))
}
fn sum_of(v: &Value) -> u64 {
    v.as_object()
        .unwrap_or_else(|| panic!("not an object: {v}"))
        .values()
        .map(|x| x.as_u64().expect("n"))
        .sum()
}

#[test]
#[cfg_attr(
    debug_assertions,
    ignore = "release only: three 6000-tick seeds with map generation"
)]
fn the_named_picks_and_the_unnamed_ones_are_the_chest_taken_events() {
    let rw = &report()["rewards"];
    let events = n(rw, "chest_taken_events");
    assert!(
        events >= MIN_EVENTS,
        "seeds {SEEDS:?} gave {events} ChestTaken events; floor {MIN_EVENTS}"
    );
    assert_eq!(
        sum_of(&rw["picks_by_kind"]) + n(rw, "picks_unnamed"),
        events,
        "named plus unnamed picks are the events: {rw}"
    );
}

#[test]
#[cfg_attr(
    debug_assertions,
    ignore = "release only: three 6000-tick seeds with map generation"
)]
fn no_card_is_picked_more_often_than_it_was_offered() {
    let cards = report()["augments"]["by_card"].as_array().expect("by_card");
    for c in cards {
        assert!(n(c, "picked") <= n(c, "offered"), "{c}");
        assert!(
            n(c, "picked_by_killer") + n(c, "picked_by_bystander") <= n(c, "picked"),
            "{c}"
        );
    }
}

#[test]
#[cfg_attr(
    debug_assertions,
    ignore = "release only: three 6000-tick seeds with map generation"
)]
fn the_chest_kinds_split_the_same_picks_as_the_option_kinds() {
    let rw = &report()["rewards"];
    assert_eq!(
        sum_of(&rw["picks_by_chest_kind"]),
        sum_of(&rw["picks_by_kind"]),
        "{rw}"
    );
    let picked: u64 = report()["augments"]["by_card"]
        .as_array()
        .expect("by_card")
        .iter()
        .map(|c| n(c, "picked"))
        .sum();
    assert_eq!(
        picked,
        sum_of(&rw["picks_by_kind"]),
        "the cards' picks are the named picks: {rw}"
    );
}
