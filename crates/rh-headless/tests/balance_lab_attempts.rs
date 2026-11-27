//! Oracle for packet P-BAL-LAB-1-fix (docs/backlog/packets/P-BAL-LAB-1-fix.md): the balance lab reads quest outcomes from
//! `Sim::quest_attempts`. Loose proof (owner direction 2026-11-10, new tooling, no legacy counterpart): the scripted tests
//! pin the bookkeeping rules on hand-written probe rows; one short real run with RaidBots pins that the report counts a
//! completion, that its funnel adds up and that `complete_secs.samples` agrees with `sum` and `n`.
use rh_headless::balance_lab::attempts::{tally_seat, AttemptTally, ProbeRow, StepRead};
use rh_headless::balance_lab::{credit_window_open, run, Options, SeatPolicy};
use serde_json::Value;
use std::collections::BTreeMap;

const QUEST: &str = "watchtowers";

/// An accepted attempt that is still open: timed with `deadline` (0 = untimed).
fn open(offer: u32, quest: &str, deadline: f64) -> ProbeRow {
    ProbeRow {
        offer,
        quest: quest.to_string(),
        accepted: true,
        ready: false,
        turned_in: false,
        completed: false,
        deadline,
        progress: 0,
    }
}
/// Objective met at the endpoint, reward not paid (`accepted` stays true, the deadline is 0).
fn turned_in(offer: u32, quest: &str) -> ProbeRow {
    ProbeRow {
        ready: true,
        turned_in: true,
        ..open(offer, quest, 0.)
    }
}
/// Paid: the sim keeps the row for the rest of the match with `accepted` false.
fn paid(offer: u32, quest: &str) -> ProbeRow {
    ProbeRow {
        accepted: false,
        completed: true,
        ..open(offer, quest, 0.)
    }
}
fn step(tick: u32, now_ms: f64, rows: Vec<ProbeRow>) -> StepRead {
    StepRead {
        tick,
        now_ms,
        locked: false,
        died: false,
        rows,
    }
}

fn only(t: &BTreeMap<String, AttemptTally>, quest: &str) -> AttemptTally {
    assert_eq!(
        t.keys().map(String::as_str).collect::<Vec<_>>(),
        vec![quest],
        "expected exactly one quest in {t:?}"
    );
    t[quest].clone()
}

/// Every attempt is in exactly one bucket.
fn assert_partition(t: &AttemptTally) {
    assert_eq!(
        t.attempts,
        t.completed + t.ended_timer + t.ended_death + t.unknown + t.open + t.turned_in_unpaid,
        "the buckets must add up to the attempts: {t:?}"
    );
    assert_eq!(t.complete_secs.len() as u64, t.completed, "{t:?}");
}

#[test]
fn a_completion_is_timed_from_the_accept_to_the_turn_in_and_counted_once() {
    let steps = vec![
        step(5, 10_000., vec![open(3, QUEST, 70_000.)]),
        step(
            6,
            10_050.,
            vec![ProbeRow {
                progress: 1,
                ready: true,
                ..open(3, QUEST, 70_000.)
            }],
        ),
        // Turned in at the endpoint: objective met, the reward waits for the seat's choice (full spell slots).
        step(900, 40_000., vec![turned_in(3, QUEST)]),
        step(920, 41_000., vec![paid(3, QUEST)]),
        // The paid row persists for the whole match and is never a second attempt.
        step(921, 41_050., vec![paid(3, QUEST)]),
    ];
    let t = only(&tally_seat(&steps), QUEST);
    assert_partition(&t);
    assert_eq!((t.attempts, t.accepted, t.completed), (1, 1, 1), "{t:?}");
    assert_eq!(t.first_accept_ticks, vec![5]);
    assert_eq!(
        t.complete_secs,
        vec![30.0],
        "the time ends at the step the seat finished the objective (turned in), not at the later payment"
    );
    assert_eq!((t.open, t.turned_in_unpaid, t.unknown), (0, 0, 0));
}

#[test]
fn a_completion_in_one_step_and_a_row_already_paid_at_the_first_read() {
    let steps = vec![
        step(1, 5_000., vec![open(1, QUEST, 0.)]),
        step(2, 5_050., vec![paid(1, QUEST)]),
        step(1, 5_000., vec![paid(2, "letters")]),
    ];
    let t = tally_seat(&steps);
    assert_eq!(t[QUEST].completed, 1);
    assert_eq!(t[QUEST].complete_secs, vec![0.05]);
    assert_eq!(
        t["letters"].completed, 1,
        "a row first seen paid is one attempt"
    );
    assert_eq!(t["letters"].complete_secs, vec![0.0]);
    t.values().for_each(assert_partition);
}

#[test]
fn a_turn_in_that_is_never_paid_has_its_own_bucket() {
    let steps = vec![
        step(1, 1_000., vec![open(4, QUEST, 90_000.)]),
        step(2, 21_000., vec![turned_in(4, QUEST)]),
        step(3, 21_050., vec![turned_in(4, QUEST)]),
    ];
    let t = only(&tally_seat(&steps), QUEST);
    assert_partition(&t);
    assert_eq!((t.attempts, t.completed, t.turned_in_unpaid), (1, 0, 1));
    assert_eq!(t.open, 0, "a turned-in attempt is not open");
    assert!(
        t.complete_secs.is_empty(),
        "an unpaid turn-in is no completion"
    );
}

#[test]
fn an_attempt_whose_row_vanished_after_its_deadline_is_a_timer_end() {
    for (gone_at, died) in [(30_050., false), (30_000., false), (30_050., true)] {
        let mut gone = step(10, gone_at, Vec::new());
        gone.died = died;
        let steps = vec![step(9, 29_950., vec![open(2, QUEST, 30_000.)]), gone];
        let t = only(&tally_seat(&steps), QUEST);
        assert_partition(&t);
        assert_eq!(
            (t.ended_timer, t.ended_death, t.unknown),
            (1, 0, 0),
            "last deadline 30000 against now {gone_at} (died {died}): the deadline passed, so the timer; the lab keeps its \
             old order, timer before death"
        );
    }
}

#[test]
fn a_seat_that_died_before_the_deadline_is_a_death_end() {
    let mut gone = step(10, 12_000., Vec::new());
    gone.died = true;
    let steps = vec![step(9, 11_950., vec![open(2, QUEST, 90_000.)]), gone];
    let t = only(&tally_seat(&steps), QUEST);
    assert_partition(&t);
    assert_eq!((t.ended_death, t.ended_timer, t.unknown), (1, 0, 0));
}

#[test]
fn a_death_one_step_before_the_row_vanishes_still_ends_it_by_death() {
    // The board drops a dead seat's attempt in its own pass (quests/board/attempts.rs:493-499), which can run in the step
    // after the death event; and a turned-in attempt is never dropped (:488), dead or not.
    let mut died = step(10, 12_000., vec![open(2, QUEST, 90_000.)]);
    died.died = true;
    let steps = vec![died, step(11, 12_050., Vec::new())];
    let t = only(&tally_seat(&steps), QUEST);
    assert_partition(&t);
    assert_eq!((t.ended_death, t.unknown), (1, 0), "{t:?}");

    // Two steps later is no longer that death.
    let mut died = step(10, 12_000., vec![open(2, QUEST, 90_000.)]);
    died.died = true;
    let steps = vec![
        died,
        step(11, 12_050., vec![open(2, QUEST, 90_000.)]),
        step(12, 12_100., Vec::new()),
    ];
    let t = only(&tally_seat(&steps), QUEST);
    assert_eq!((t.ended_death, t.unknown), (0, 1), "{t:?}");

    // A turned-in attempt of a seat that died stays, and is counted in its own bucket.
    let mut died = step(10, 12_000., vec![turned_in(2, QUEST)]);
    died.died = true;
    let t = only(
        &tally_seat(&[died, step(11, 12_050., vec![turned_in(2, QUEST)])]),
        QUEST,
    );
    assert_eq!((t.turned_in_unpaid, t.ended_death), (1, 0));
}

#[test]
fn a_vanished_row_nothing_explains_is_unknown_and_an_untimed_row_never_expires() {
    let steps = vec![
        step(
            1,
            1_000.,
            vec![open(1, QUEST, 90_000.), open(2, "letters", 0.)],
        ),
        // Both gone, nobody died; the first deadline is still in the future, the second quest has no timer.
        step(2, 5_000_000., vec![]),
    ];
    let t = tally_seat(&steps);
    assert_eq!(t[QUEST].unknown, 1);
    assert_eq!(
        (t["letters"].unknown, t["letters"].ended_timer),
        (1, 0),
        "deadline 0 means untimed: it is never a timer end, however late it is"
    );
    t.values().for_each(assert_partition);
}

#[test]
fn nothing_expires_while_the_gates_are_locked() {
    // The sim pushes the deadline back every tick while locked, so a row can vanish at a locked step with a last deadline
    // that reads as passed; that is never a timer.
    let mut locked_gone = step(50, 9_000., Vec::new());
    locked_gone.locked = true;
    let mut locked_row = step(49, 8_950., vec![open(1, QUEST, 5_000.)]);
    locked_row.locked = true;
    let t = only(&tally_seat(&[locked_row, locked_gone]), QUEST);
    assert_eq!((t.ended_timer, t.unknown), (0, 1), "{t:?}");

    // Rows present across locked steps are still open at the end.
    let mut a = step(60, 10_000., vec![open(1, QUEST, 10_050.)]);
    let mut b = step(61, 10_050., vec![open(1, QUEST, 10_100.)]);
    a.locked = true;
    b.locked = true;
    let t = only(&tally_seat(&[a, b]), QUEST);
    assert_eq!((t.open, t.ended_timer), (1, 0));
    assert_partition(&t);
}

#[test]
fn a_retry_on_the_same_offer_is_a_second_attempt_and_one_acceptance() {
    let steps = vec![
        step(1, 1_000., vec![open(7, QUEST, 31_000.)]),
        step(2, 31_050., vec![]), // timer
        step(3, 32_000., vec![]),
        step(40, 50_000., vec![open(7, QUEST, 80_000.)]), // the same offer id again
        step(41, 60_000., vec![turned_in(7, QUEST)]),
        step(42, 60_050., vec![paid(7, QUEST)]),
    ];
    let t = only(&tally_seat(&steps), QUEST);
    assert_partition(&t);
    assert_eq!((t.attempts, t.accepted), (2, 1));
    assert_eq!(t.first_accept_ticks, vec![1], "the first acceptance only");
    assert_eq!((t.ended_timer, t.completed), (1, 1));
    assert_eq!(
        t.complete_secs,
        vec![10.0],
        "the retry is timed from its own first step, not from the first attempt"
    );
}

#[test]
fn attempts_still_accepted_at_the_end_are_open_and_a_ready_one_is_not_a_completion() {
    let ready = ProbeRow {
        ready: true,
        progress: 3,
        ..open(1, QUEST, 90_000.)
    };
    let steps = vec![
        step(
            1,
            1_000.,
            vec![open(1, QUEST, 90_000.), open(2, "letters", 0.)],
        ),
        step(2, 2_000., vec![ready, open(2, "letters", 0.)]),
    ];
    let t = tally_seat(&steps);
    assert_eq!((t[QUEST].open, t["letters"].open), (1, 1));
    assert_eq!(t[QUEST].completed, 0);
    t.values().for_each(assert_partition);
}

#[test]
fn a_seat_with_no_rows_has_no_tally() {
    assert!(tally_seat(&[]).is_empty());
    assert!(tally_seat(&[step(1, 50., vec![]), step(2, 100., vec![])]).is_empty());
}

#[test]
fn the_death_attribution_window_is_the_sims_kill_credit_window() {
    // systems/credit.rs:24: a hit is credited while `at - when <= KILL_CREDIT_SECONDS * 1000` (5 s).
    assert!(credit_window_open(10_000., 10_000.));
    assert!(credit_window_open(10_000., 12_000.));
    assert!(
        credit_window_open(10_000., 15_000.),
        "exactly 5 s is inside"
    );
    assert!(!credit_window_open(10_000., 15_050.));
    assert!(!credit_window_open(10_000., 60_000.));
}

// ---- one real, short lab run ----

/// Seeds and ticks measured at the stub: see the packet, "Measured premises".
const SEEDS: std::ops::Range<u64> = 1..3;
const MAX_TICKS: u32 = 2400;

fn raid_opts(jobs: usize) -> Options {
    Options {
        seeds: SEEDS,
        jobs,
        max_ticks: MAX_TICKS,
        human_seats: true,
        seats: (0..8).map(|i| (i, SeatPolicy::Raid)).collect(),
        ..Options::default()
    }
}

fn report(jobs: usize) -> String {
    run(&raid_opts(jobs)).expect("lab run").json
}

/// Seeds 1..3 on one thread (cached: two tests read it).
fn jobs1() -> &'static String {
    static R: std::sync::OnceLock<String> = std::sync::OnceLock::new();
    R.get_or_init(|| report(1))
}

fn n(v: &Value, k: &str) -> u64 {
    v[k].as_u64()
        .unwrap_or_else(|| panic!("`{k}` is missing or not a number in {v}"))
}

#[test]
fn raid_bots_complete_quests_and_every_attempt_is_in_one_bucket() {
    let r: Value = serde_json::from_str(jobs1()).expect("json");
    let rows = r["quests"]["by_quest"].as_array().expect("by_quest");
    let (mut completed, mut samples_total) = (0, 0);
    for q in rows {
        let name = q["quest"].as_str().unwrap_or("?");
        let sum = n(q, "completed")
            + n(q, "ended_timer")
            + n(q, "ended_death")
            + n(q, "abandoned")
            + n(q, "open")
            + n(q, "turned_in_unpaid");
        assert_eq!(
            n(q, "attempts"),
            sum,
            "{name}: attempts = completed + timer + death + unknown (abandoned) + open + turned_in_unpaid: {q}"
        );
        assert!(n(q, "accepted") <= n(q, "attempts"), "{name}");
        assert!(n(q, "accepted") <= n(q, "dealt"), "{name}");
        completed += n(q, "completed");
        let secs = &q["complete_secs"];
        let samples = secs["samples"]
            .as_array()
            .unwrap_or_else(|| panic!("{name}: complete_secs has no `samples`: {secs}"));
        assert_eq!(samples.len() as u64, n(secs, "n"), "{name}");
        assert_eq!(
            n(secs, "n"),
            n(q, "completed"),
            "{name}: one sample a completion"
        );
        let total: f64 = samples.iter().map(|s| s.as_f64().expect("number")).sum();
        let sum = secs["sum"].as_f64().expect("sum");
        assert!(
            (total - sum).abs() < 1e-6,
            "{name}: samples add to {total}, sum says {sum}"
        );
        assert!(samples.iter().all(|s| s.as_f64().is_some_and(|x| x >= 0.)));
        samples_total += samples.len();
    }
    assert!(
        completed >= MIN_COMPLETED,
        "RaidBots completed {completed} quests over seeds {SEEDS:?}; the probe-read lab must see at least {MIN_COMPLETED}"
    );
    assert_eq!(samples_total as u64, completed);
    let notes = serde_json::to_string(&r["notes"]).expect("notes");
    assert!(
        notes.contains("quest_attempts"),
        "the notes must say the quest block is read from Sim::quest_attempts and what it cannot measure: {notes}"
    );
    assert!(
        !notes.contains("else abandoned"),
        "the old end-cause note (completions read from the seat's observation) must be replaced: {notes}"
    );
}

#[test]
fn each_viewing_seat_counts_all_ten_raid_offers_once() {
    let r: Value = serde_json::from_str(jobs1()).expect("json");
    let rows = r["quests"]["by_quest"].as_array().expect("by_quest");
    let dealt: u64 = rows.iter().map(|q| n(q, "dealt")).sum();
    let viewing_seats = (SEEDS.end - SEEDS.start) * 8;
    assert_eq!(dealt, viewing_seats * 10);
    assert!(
        rows.iter().all(|q| n(q, "dealt") <= viewing_seats),
        "a quest can be dealt at most once to each seat in each match"
    );
}

#[test]
fn the_report_with_samples_is_byte_identical_for_jobs_1_and_4() {
    let (one, four) = (jobs1(), &report(4));
    assert!(
        one.contains("\"samples\":["),
        "the report has no complete_secs samples: {one}"
    );
    assert_eq!(one, four, "--jobs 1 and --jobs 4 gave different bytes");
}

/// Measured at the stub with RaidBots in every seat: 26 completions on seeds 1..3 (12 and 14). A floor, not a target.
const MIN_COMPLETED: u64 = 10;
