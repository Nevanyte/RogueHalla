//! Oracle for packet P-BAL-LAB-ELITE-1 (docs/backlog/packets/P-BAL-LAB-ELITE-1.md): the balance lab's elite attempts end in
//! exactly one bucket, a kill has one chest outcome, and picks are split by chest kind. Loose proof (owner direction
//! 2026-11-10, new tooling): scripted tests pin the bookkeeping on hand-written steps; one real RaidBot run pins that the
//! report's numbers add up and are byte-identical across `--jobs`.
use rh_headless::balance_lab::elite_fights::{
    tally_elites, Contact, EliteDeath, EliteStep, EliteTally, SeatDeath, LEAVE_MS,
};
use rh_headless::balance_lab::{run, Options, SeatPolicy};
use serde_json::Value;
use std::collections::BTreeMap;

const KIND: &str = "Archon";
const WEAPON: &str = "sword";
const ELITE: u32 = 100;
const ADD: u32 = 101;
const CAMP: i32 = 5;
const OTHER_MOB: u32 = 200;
const OTHER_CAMP: i32 = 6;

fn contact(seat: usize, dealt: bool, on_elite: bool, elite: u32, camp: i32) -> Contact {
    Contact {
        seat,
        elite,
        camp,
        kind: KIND.into(),
        weapon: WEAPON.into(),
        dealt,
        on_elite,
    }
}
/// The seat hits the elite.
fn hit(seat: usize) -> Contact {
    contact(seat, true, true, ELITE, CAMP)
}
/// The seat is hit by the elite.
fn hurt(seat: usize) -> Contact {
    contact(seat, false, true, ELITE, CAMP)
}
fn at(now_ms: f64) -> EliteStep {
    EliteStep {
        now_ms,
        ..EliteStep::default()
    }
}
fn with(mut s: EliteStep, c: Contact) -> EliteStep {
    s.contacts.push(c);
    s
}
fn kill(mut s: EliteStep, killer: Option<usize>) -> EliteStep {
    s.elite_deaths.push(EliteDeath {
        elite: ELITE,
        killer,
    });
    s
}
fn seat_dies(mut s: EliteStep, seat: usize, mob: Option<u32>, camp: Option<i32>) -> EliteStep {
    s.seat_deaths.push(SeatDeath {
        seat,
        attacker_mob: mob,
        attacker_camp: camp,
    });
    s
}
fn pick(mut s: EliteStep, seat: usize) -> EliteStep {
    s.chest_picks.push((ELITE, seat));
    s
}
fn gone(mut s: EliteStep) -> EliteStep {
    s.chests_gone.push(ELITE);
    s
}

fn tally(steps: &[EliteStep]) -> BTreeMap<(String, String), EliteTally> {
    tally_elites(steps)
}
/// The one (KIND, WEAPON) row, with its partition checked.
fn row(steps: &[EliteStep]) -> EliteTally {
    let t = tally(steps);
    let r = t
        .get(&(KIND.to_string(), WEAPON.to_string()))
        .unwrap_or_else(|| panic!("no tally for ({KIND}, {WEAPON}): {t:?}"))
        .clone();
    assert_partition(&r);
    r
}
/// attempts = killed + taken_by_other + died + died_elsewhere + left + open; killed = the chest outcomes.
fn assert_partition(r: &EliteTally) {
    assert_eq!(
        r.attempts,
        r.killed + r.taken_by_other + r.died + r.died_elsewhere + r.left + r.open,
        "every attempt is in exactly one bucket: {r:?}"
    );
    assert_eq!(
        r.killed,
        r.chest_killer + r.chest_other + r.chest_none + r.chest_open,
        "every kill has exactly one chest outcome: {r:?}"
    );
    assert_eq!(r.kill_secs.len() as u64, r.killed, "{r:?}");
}

// ---- kill and chest ----

#[test]
fn a_kill_then_a_pick_by_the_killer() {
    let r = row(&[
        with(at(1_000.), hit(0)),
        kill(with(at(5_000.), hit(0)), Some(0)),
        pick(at(6_000.), 0),
        gone(at(7_000.)),
    ]);
    assert_eq!((r.attempts, r.killed), (1, 1));
    assert_eq!((r.chest_killer, r.chest_other, r.chest_none), (1, 0, 0));
    assert_eq!((r.chest_open, r.chest_bystander_picks), (0, 0));
    assert_eq!(r.kill_secs, vec![4.0]);
}

#[test]
fn a_kill_then_a_pick_by_a_bystander_only() {
    let r = row(&[
        with(at(1_000.), hit(0)),
        kill(with(at(5_000.), hit(0)), Some(0)),
        pick(at(6_000.), 3),
        gone(at(7_000.)),
    ]);
    assert_eq!(r.killed, 1);
    assert_eq!((r.chest_killer, r.chest_other, r.chest_none), (0, 1, 0));
    assert_eq!(r.chest_bystander_picks, 1);
}

#[test]
fn killer_and_bystander_both_pick_the_chest_is_the_killers_and_the_bystander_is_still_counted() {
    let r = row(&[
        with(at(1_000.), hit(0)),
        kill(with(at(5_000.), hit(0)), Some(0)),
        pick(at(5_500.), 3),
        pick(at(6_000.), 0),
        gone(at(7_000.)),
    ]);
    assert_eq!((r.chest_killer, r.chest_other, r.chest_none), (1, 0, 0));
    assert_eq!(r.chest_bystander_picks, 1);
}

#[test]
fn a_kill_nobody_picks_from_is_a_chest_none_once_the_chest_is_gone() {
    let r = row(&[
        with(at(1_000.), hit(0)),
        kill(with(at(5_000.), hit(0)), Some(0)),
        gone(at(40_000.)),
    ]);
    assert_eq!(r.killed, 1);
    assert_eq!((r.chest_killer, r.chest_other, r.chest_none), (0, 0, 1));
}

#[test]
fn a_chest_still_standing_at_the_end_is_open_not_none() {
    let r = row(&[
        with(at(1_000.), hit(0)),
        kill(with(at(5_000.), hit(0)), Some(0)),
        at(6_000.),
    ]);
    assert_eq!((r.killed, r.chest_open, r.chest_none), (1, 1, 0));
}

// ---- how an attempt ends ----

#[test]
fn a_seat_that_gave_up_and_died_later_to_something_else_is_left_not_died() {
    // The finding of the integration 86 merge review: this used to add a death to the elite's `died`.
    let r = row(&[
        with(at(1_000.), hit(0)),
        with(at(3_000.), hurt(0)),
        at(60_000.),
        seat_dies(at(300_000.), 0, Some(OTHER_MOB), Some(OTHER_CAMP)),
    ]);
    assert_eq!(r.attempts, 1);
    assert_eq!(
        (r.left, r.died, r.died_elsewhere, r.open),
        (1, 0, 0, 0),
        "{r:?}"
    );
}

#[test]
fn a_seat_that_gave_up_and_died_later_to_the_elite_camp_is_still_left() {
    // Dying to the same camp five minutes later is a new fight, not this attempt's end.
    let r = row(&[
        with(at(1_000.), hit(0)),
        at(60_000.),
        seat_dies(at(300_000.), 0, Some(ADD), Some(CAMP)),
    ]);
    assert_eq!((r.left, r.died, r.died_elsewhere), (1, 0, 0), "{r:?}");
}

#[test]
fn a_seat_that_gave_up_and_came_back_and_killed_is_two_attempts_one_kill() {
    let r = row(&[
        with(at(1_000.), hit(0)),
        at(12_000.),
        with(at(40_000.), hit(0)),
        kill(with(at(60_000.), hit(0)), Some(0)),
        pick(at(61_000.), 0),
        gone(at(62_000.)),
    ]);
    assert_eq!((r.attempts, r.left, r.killed), (2, 1, 1), "{r:?}");
    assert_eq!(
        r.kill_secs,
        vec![20.0],
        "timed from the second streak, not the first hit"
    );
    assert_eq!(r.chest_killer, 1);
}

#[test]
fn a_death_to_the_elite_is_died() {
    let r = row(&[
        with(at(1_000.), hit(0)),
        seat_dies(with(at(3_000.), hurt(0)), 0, Some(ELITE), Some(CAMP)),
    ]);
    assert_eq!((r.attempts, r.died, r.died_elsewhere, r.left), (1, 1, 0, 0));
}

#[test]
fn a_death_to_the_elites_adds_is_died() {
    let r = row(&[
        with(at(1_000.), hit(0)),
        seat_dies(
            with(at(3_000.), contact(0, false, false, ELITE, CAMP)),
            0,
            Some(ADD),
            Some(CAMP),
        ),
    ]);
    assert_eq!((r.died, r.died_elsewhere), (1, 0), "{r:?}");
}

#[test]
fn a_death_to_another_camp_or_a_player_while_engaged_is_died_elsewhere() {
    let other_camp = row(&[
        with(at(1_000.), hit(0)),
        seat_dies(at(4_000.), 0, Some(OTHER_MOB), Some(OTHER_CAMP)),
    ]);
    assert_eq!((other_camp.died, other_camp.died_elsewhere), (0, 1));
    let hazard = row(&[
        with(at(1_000.), hit(0)),
        seat_dies(at(4_000.), 0, None, None),
    ]);
    assert_eq!((hazard.died, hazard.died_elsewhere), (0, 1));
}

#[test]
fn a_campless_elite_is_only_the_elite_itself() {
    let solo = |c: Contact| Contact { camp: -1, ..c };
    let by_elite = row(&[
        with(at(1_000.), solo(hit(0))),
        seat_dies(at(3_000.), 0, Some(ELITE), Some(-1)),
    ]);
    assert_eq!((by_elite.died, by_elite.died_elsewhere), (1, 0));
    // Another campless mob (camp -1 too) is not this elite's camp.
    let by_other = row(&[
        with(at(1_000.), solo(hit(0))),
        seat_dies(at(3_000.), 0, Some(300), Some(-1)),
    ]);
    assert_eq!((by_other.died, by_other.died_elsewhere), (0, 1));
}

#[test]
fn two_seats_on_one_elite_one_kills_the_other_is_taken_and_the_one_that_left_stays_left() {
    let mut s1 = with(at(1_000.), hit(0));
    s1.contacts.push(hit(1));
    s1.contacts.push(hit(2));
    let r = row(&[
        s1,
        with(with(at(8_000.), hit(0)), hit(1)),
        with(with(at(15_000.), hit(0)), hit(1)),
        kill(with(at(20_000.), hit(0)), Some(0)),
        pick(at(21_000.), 0),
        gone(at(22_000.)),
    ]);
    assert_eq!(r.attempts, 3);
    assert_eq!(
        (r.killed, r.taken_by_other, r.left, r.open),
        (1, 1, 1, 0),
        "{r:?}"
    );
}

#[test]
fn an_elite_killed_by_nobody_or_a_mob_is_taken_by_other_for_a_seat_still_in_the_fight() {
    let r = row(&[with(at(1_000.), hit(0)), kill(at(4_000.), None)]);
    assert_eq!((r.taken_by_other, r.killed, r.chest_none), (1, 0, 0));
}

#[test]
fn the_kill_seconds_start_at_a_hit_taken_before_the_first_hit_dealt() {
    let r = row(&[
        with(at(10_000.), hurt(0)),
        with(at(12_000.), hit(0)),
        kill(with(at(40_000.), hit(0)), Some(0)),
    ]);
    assert_eq!(r.attempts, 1);
    assert_eq!(r.kill_secs, vec![30.0]);
}

#[test]
fn a_hit_taken_alone_opens_no_attempt_and_a_hit_on_an_add_does_not_open_one() {
    // Seat 2 is the control: it hits the elite itself and opens the one attempt.
    let t = tally(&[
        with(at(1_000.), hurt(0)),
        with(at(2_000.), contact(1, true, false, ELITE, CAMP)),
        with(at(3_000.), hit(2)),
    ]);
    let attempts: u64 = t.values().map(|r| r.attempts).sum();
    assert_eq!(
        attempts, 1,
        "an attempt opens on a hit the seat deals to the elite itself: {t:?}"
    );
}

#[test]
fn the_bound_is_exact_and_the_match_end_splits_open_from_left() {
    assert_eq!(LEAVE_MS, 10_000.);
    let at_bound = row(&[with(at(1_000.), hit(0)), at(1_000. + LEAVE_MS)]);
    assert_eq!((at_bound.open, at_bound.left), (1, 0));
    let past = row(&[with(at(1_000.), hit(0)), at(1_050. + LEAVE_MS)]);
    assert_eq!((past.open, past.left), (0, 1));
}

#[test]
fn a_contact_inside_the_bound_continues_the_attempt() {
    let r = row(&[
        with(at(1_000.), hit(0)),
        with(at(10_000.), hurt(0)),
        with(at(19_000.), hit(0)),
        kill(with(at(25_000.), hit(0)), Some(0)),
    ]);
    assert_eq!((r.attempts, r.killed, r.left), (1, 1, 0));
    assert_eq!(r.kill_secs, vec![24.0]);
}

// ---- one real, short lab run (release only) ----

/// Seeds 1, 2, 3 at 6000 ticks, RaidBots in every seat, measured at the stub: 16 attempts, 7 kills (packet F3).
const SEEDS: std::ops::Range<u64> = 1..4;
const MAX_TICKS: u32 = 6000;
const MIN_ATTEMPTS: u64 = 10;
const MIN_KILLS: u64 = 4;

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

fn jobs1() -> &'static String {
    static R: std::sync::OnceLock<String> = std::sync::OnceLock::new();
    R.get_or_init(|| report(1))
}

fn n(v: &Value, k: &str) -> u64 {
    v[k].as_u64()
        .unwrap_or_else(|| panic!("`{k}` is missing or not a number in {v}"))
}

#[test]
#[cfg_attr(
    debug_assertions,
    ignore = "release only: three 6000-tick seeds with map generation"
)]
fn raid_bots_fight_elites_and_every_attempt_and_every_kill_is_in_one_bucket() {
    let r: Value = serde_json::from_str(jobs1()).expect("json");
    let rows = r["elites"]["by_kind_weapon"].as_array().expect("rows");
    let (mut attempts, mut kills) = (0, 0);
    let (mut died, mut died_elsewhere) = (0, 0);
    let (mut chest_killer, mut bystander) = (0, 0);
    for e in rows {
        let name = format!("{}/{}", e["kind"], e["weapon"]);
        let sum = n(e, "killed")
            + n(e, "taken_by_other")
            + n(e, "died")
            + n(e, "died_elsewhere")
            + n(e, "left")
            + n(e, "open");
        assert_eq!(n(e, "attempts"), sum, "{name}: {e}");
        assert_eq!(
            n(e, "killed"),
            n(e, "chest_killer") + n(e, "chest_other") + n(e, "chest_none") + n(e, "chest_open"),
            "{name}: one chest outcome per kill: {e}"
        );
        let secs = &e["kill_secs"];
        let samples = secs["samples"]
            .as_array()
            .unwrap_or_else(|| panic!("{name}: kill_secs has no samples: {e}"));
        assert_eq!(samples.len() as u64, n(secs, "n"), "{name}");
        assert_eq!(n(secs, "n"), n(e, "killed"), "{name}: one sample a kill");
        let total: f64 = samples.iter().map(|s| s.as_f64().expect("number")).sum();
        assert!((total - secs["sum"].as_f64().expect("sum")).abs() < 1e-6);
        assert!(samples.iter().all(|s| s.as_f64().is_some_and(|x| x > 0.)));
        attempts += n(e, "attempts");
        kills += n(e, "killed");
        died += n(e, "died");
        died_elsewhere += n(e, "died_elsewhere");
        chest_killer += n(e, "chest_killer");
        bystander += n(e, "chest_bystander_picks");
    }
    assert!(
        attempts >= MIN_ATTEMPTS && kills >= MIN_KILLS,
        "seeds {SEEDS:?} at {MAX_TICKS} ticks gave {attempts} attempts and {kills} kills; floors {MIN_ATTEMPTS} and {MIN_KILLS}"
    );
    // A seat death is booked to an elite fight only when the death's own cause was the elite or its camp (cause elite or mob).
    let deaths = &r["world"]["player_deaths"];
    let by = &deaths["by_cause"];
    assert!(
        died <= n(by, "elite") + n(by, "mob"),
        "died {died} exceeds the deaths caused by an elite or a mob: {by}"
    );
    assert!(died + died_elsewhere <= n(deaths, "total"));
    assert!(n(&r["world"], "elite_camps_cleared") <= n(&r["world"], "camps_cleared"));

    // Picks by chest kind.
    let by_kind: u64 = r["rewards"]["picks_by_kind"]
        .as_object()
        .expect("picks_by_kind")
        .values()
        .map(|v| v.as_u64().expect("n"))
        .sum();
    let by_chest = r["rewards"]["picks_by_chest_kind"]
        .as_object()
        .unwrap_or_else(|| panic!("rewards.picks_by_chest_kind is missing: {}", r["rewards"]));
    assert!(by_chest
        .keys()
        .all(|k| ["elite", "trophy", "ground"].contains(&k.as_str())));
    assert_eq!(
        by_chest
            .values()
            .map(|v| v.as_u64().expect("n"))
            .sum::<u64>(),
        by_kind,
        "the chest kinds split the same picks"
    );
    let elite_picks = by_chest.get("elite").and_then(Value::as_u64).unwrap_or(0);
    assert!(
        elite_picks >= chest_killer + bystander,
        "elite picks {elite_picks} < killer {chest_killer} + bystander {bystander}"
    );
    // Per card.
    let cards = r["augments"]["by_card"].as_array().expect("by_card");
    let (mut by_killer, mut by_bystander) = (0, 0);
    for c in cards {
        assert!(
            n(c, "picked_by_killer") + n(c, "picked_by_bystander") <= n(c, "picked"),
            "{c}"
        );
        by_killer += n(c, "picked_by_killer");
        by_bystander += n(c, "picked_by_bystander");
    }
    assert!(by_killer + by_bystander <= elite_picks);
    let notes = serde_json::to_string(&r["notes"]).expect("notes");
    assert!(
        notes.contains("left") && notes.contains("died_elsewhere"),
        "the notes must say how an elite attempt ends and that `died` is narrower: {notes}"
    );
}

#[test]
#[cfg_attr(
    debug_assertions,
    ignore = "release only: three 6000-tick seeds with map generation"
)]
fn the_elite_report_is_byte_identical_for_jobs_1_and_4() {
    let (one, four) = (jobs1(), &report(4));
    assert!(
        one.contains("\"chest_killer\""),
        "the report has no chest outcome fields: {one}"
    );
    assert_eq!(one, four, "--jobs 1 and --jobs 4 gave different bytes");
}
