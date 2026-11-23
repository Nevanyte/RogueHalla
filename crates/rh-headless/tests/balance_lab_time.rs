//! Oracle for packet P-BAL-LAB-TIME-1 (docs/backlog/packets/P-BAL-LAB-TIME-1.md): the balance lab says where each seat's
//! time goes (nine buckets, first match wins) and has one row per seed and seat. Scripted tests pin the pure
//! `TimeTally` on hand-written steps; real-run tests (release only) pin the identities the report must hold. Loose proof
//! (owner direction 2026-11-10, new tooling): no exact legacy numbers, only that the accounting adds up.
use rh_headless::balance_lab::time_budget::{
    tally_time, Bucket, Buckets, SeatRow, SeatStep, TimeStep, FIGHT_WINDOW_STEPS, MOVED_DIST,
};
use rh_headless::balance_lab::{run, Options, SeatPolicy};
use serde_json::Value;

// ---- builders ----

fn alive() -> SeatStep {
    SeatStep {
        alive: true,
        hp_full: true,
        ..SeatStep::default()
    }
}
fn dead() -> SeatStep {
    SeatStep::default()
}
fn walking(mut s: SeatStep) -> SeatStep {
    s.moved = 5.;
    s
}
fn hurt(mut s: SeatStep) -> SeatStep {
    s.hp_full = false;
    s
}
fn quest(mut s: SeatStep) -> SeatStep {
    s.quest_open = true;
    s
}
fn elite(mut s: SeatStep) -> SeatStep {
    s.hit_elite = true;
    s
}
fn camp(mut s: SeatStep) -> SeatStep {
    s.hit_camp = true;
    s
}
fn player(mut s: SeatStep) -> SeatStep {
    s.hit_player = true;
    s
}

/// One seat's steps as a match: ticks 1, 2, 3, ...
fn one_seat(steps: &[SeatStep]) -> Vec<TimeStep> {
    steps
        .iter()
        .enumerate()
        .map(|(i, s)| TimeStep {
            tick: i as u32 + 1,
            seats: vec![s.clone()],
        })
        .collect()
}

fn diff(a: &Buckets, b: &Buckets) -> Vec<(Bucket, u64)> {
    Bucket::ALL
        .into_iter()
        .map(|k| (k, b.get(k).saturating_sub(a.get(k))))
        .filter(|(_, d)| *d > 0)
        .collect()
}

/// The bucket each step of one seat landed in: tally the prefixes and read what each new step added.
fn trail(steps: &[SeatStep]) -> Vec<Bucket> {
    let all = one_seat(steps);
    let mut before = Buckets::default();
    let mut out = Vec::new();
    for k in 0..all.len() {
        let rows = tally_time(&all[..=k], 1);
        let now = rows[0].time.clone();
        let d = diff(&before, &now);
        assert_eq!(
            d.len(),
            1,
            "step {k} must land in exactly one bucket once, got {d:?} (total {})",
            now.total()
        );
        assert_eq!(d[0].1, 1, "step {k}: one seat-step, one count: {d:?}");
        out.push(d[0].0);
        before = now;
    }
    out
}

fn repeat(s: SeatStep, n: usize) -> Vec<SeatStep> {
    vec![s; n]
}

// ---- the priority ----

#[test]
fn without_a_fight_or_a_quest_a_step_is_moving_resting_or_idle() {
    let near = SeatStep {
        moved: MOVED_DIST - 0.001,
        ..alive()
    };
    let edge = SeatStep {
        moved: MOVED_DIST,
        ..alive()
    };
    let t = trail(&[
        alive(),
        hurt(alive()),
        walking(alive()),
        walking(hurt(alive())),
        near,
        edge,
    ]);
    assert_eq!(
        t,
        [
            Bucket::Idle,
            Bucket::Resting,
            Bucket::Moving,
            Bucket::Moving,
            Bucket::Idle,
            Bucket::Moving
        ],
        "full hp standing is idle, low hp standing is resting, any step moved at least MOVED_DIST is moving at any hp"
    );
}

/// Coordinator's oracle-amend (2026-11-23): the three fight kinds are tried in trails of their own. The first form put a
/// camp hit, an elite hit and a player hit on consecutive steps and expected `Player` on the last, against the brief's
/// rule (the highest-ranked OPEN window wins) and against `a_new_hit_restarts_the_window_and_each_kind_has_its_own`.
#[test]
fn an_open_quest_attempt_splits_into_moving_and_still_and_loses_to_any_fight() {
    let t = trail(&[
        quest(alive()),
        quest(walking(alive())),
        quest(hurt(alive())),
    ]);
    assert_eq!(
        t,
        [Bucket::QuestStill, Bucket::QuestMoving, Bucket::QuestStill]
    );
    assert_eq!(
        trail(&[quest(alive()), camp(quest(alive()))]),
        [Bucket::QuestStill, Bucket::Camp]
    );
    assert_eq!(
        trail(&[quest(walking(alive())), elite(quest(walking(alive())))]),
        [Bucket::QuestMoving, Bucket::Elite]
    );
    assert_eq!(
        trail(&[quest(alive()), player(quest(alive()))]),
        [Bucket::QuestStill, Bucket::Player]
    );
    // With several windows open the highest-ranked one wins, whatever the newest hit was.
    assert_eq!(
        trail(&[
            camp(quest(alive())),
            elite(quest(walking(alive()))),
            player(quest(alive())),
        ]),
        [Bucket::Camp, Bucket::Elite, Bucket::Elite]
    );
}

/// Coordinator's oracle-amend (2026-11-23): step 3 (a camp hit one step after an elite hit) is `Elite`, not `Camp`:
/// the elite window opened at step 2 is still open and outranks the camp's. Movement is tried in trails of its own.
#[test]
fn the_fight_kinds_rank_elite_then_camp_then_player_and_movement_does_not_matter() {
    let t = trail(&[
        player(alive()),
        camp(player(alive())),
        elite(camp(player(alive()))),
        walking(camp(alive())),
        walking(hurt(elite(alive()))),
    ]);
    assert_eq!(
        t,
        [
            Bucket::Player,
            Bucket::Camp,
            Bucket::Elite,
            Bucket::Elite,
            Bucket::Elite
        ]
    );
    assert_eq!(trail(&[walking(camp(alive()))]), [Bucket::Camp]);
    assert_eq!(trail(&[walking(hurt(elite(alive())))]), [Bucket::Elite]);
    assert_eq!(trail(&[walking(hurt(player(alive())))]), [Bucket::Player]);
}

#[test]
fn dead_beats_everything_and_a_dead_step_is_never_counted_elsewhere() {
    let all_flags = SeatStep {
        alive: false,
        moved: 9.,
        hp_full: false,
        quest_open: true,
        hit_elite: true,
        hit_camp: true,
        hit_player: true,
        ..SeatStep::default()
    };
    assert_eq!(
        trail(&[dead(), all_flags, alive()]),
        [Bucket::Dead, Bucket::Dead, Bucket::Idle]
    );
}

// ---- the fight window ----

#[test]
fn a_hit_holds_its_fight_for_exactly_the_window_counted_from_the_hit_step() {
    assert_eq!(
        FIGHT_WINDOW_STEPS, 60,
        "3 s at 20 Hz (the brief's decision 1)"
    );
    let w = FIGHT_WINDOW_STEPS as usize;
    for (kind, mark) in [
        (Bucket::Elite, elite as fn(SeatStep) -> SeatStep),
        (Bucket::Camp, camp),
        (Bucket::Player, player),
    ] {
        let mut steps = vec![mark(alive())];
        steps.extend(repeat(alive(), w + 1));
        let t = trail(&steps);
        assert!(
            t[..w].iter().all(|b| *b == kind),
            "{kind:?}: the hit step and the {} after it are in the window: {t:?}",
            w - 1
        );
        assert_eq!(t[w], Bucket::Idle, "{kind:?}: step {w} is out: {t:?}");
        assert_eq!(t[w + 1], Bucket::Idle);
    }
}

#[test]
fn a_new_hit_restarts_the_window_and_each_kind_has_its_own() {
    let w = FIGHT_WINDOW_STEPS as usize;
    // A camp hit at 0 and an elite hit at 40: the elite wins while both hold (40..w), then the camp's own window (to
    // w - 1) is over but the elite's (to 40 + w - 1) is not; a second elite hit is not needed.
    let mut steps = repeat(alive(), 40 + w + 2);
    steps[0] = camp(alive());
    steps[40] = elite(alive());
    let t = trail(&steps);
    assert!(t[..40].iter().all(|b| *b == Bucket::Camp), "{t:?}");
    assert!(t[40..40 + w].iter().all(|b| *b == Bucket::Elite), "{t:?}");
    assert_eq!(t[40 + w], Bucket::Idle, "{t:?}");

    // An elite hit at 0 and a camp hit at 40: the camp shows once the elite window is over, until its own ends.
    let mut steps = repeat(alive(), 40 + w + 2);
    steps[0] = elite(alive());
    steps[40] = camp(alive());
    let t = trail(&steps);
    assert!(t[..w].iter().all(|b| *b == Bucket::Elite), "{t:?}");
    assert!(t[w..40 + w].iter().all(|b| *b == Bucket::Camp), "{t:?}");
    assert_eq!(t[40 + w], Bucket::Idle, "{t:?}");

    // The same kind again at 59 restarts it: 59 + 60 = 119 is the first step out.
    let mut steps = repeat(alive(), 125);
    steps[0] = player(alive());
    steps[w - 1] = player(alive());
    let t = trail(&steps);
    assert!(t[..2 * w - 1].iter().all(|b| *b == Bucket::Player), "{t:?}");
    assert_eq!(t[2 * w - 1], Bucket::Idle, "{t:?}");
}

#[test]
fn after_the_window_a_seat_with_a_quest_goes_back_to_it_and_one_that_walked_off_is_moving() {
    let w = FIGHT_WINDOW_STEPS as usize;
    let mut steps = repeat(quest(alive()), w + 2);
    steps[0] = camp(quest(alive()));
    steps[w + 1] = quest(walking(alive()));
    let t = trail(&steps);
    assert!(t[..w].iter().all(|b| *b == Bucket::Camp), "{t:?}");
    assert_eq!(t[w], Bucket::QuestStill, "{t:?}");
    assert_eq!(t[w + 1], Bucket::QuestMoving, "{t:?}");

    // Fleeing: the fight holds while the seat runs; once the window passes the running is `moving`.
    let mut steps = repeat(walking(alive()), w + 1);
    steps[0] = walking(camp(alive()));
    let t = trail(&steps);
    assert!(t[..w].iter().all(|b| *b == Bucket::Camp), "{t:?}");
    assert_eq!(t[w], Bucket::Moving, "{t:?}");
}

#[test]
fn death_ends_the_windows_and_a_hit_on_a_dead_step_opens_none() {
    // Hit, die, come back: the respawned seat is not still in the old fight.
    let t = trail(&[camp(alive()), dead(), alive(), alive()]);
    assert_eq!(t, [Bucket::Camp, Bucket::Dead, Bucket::Idle, Bucket::Idle]);
    // The killing blow lands on a step the seat is already dead on: it opens no window either.
    let t = trail(&[elite(dead()), alive(), alive()]);
    assert_eq!(t, [Bucket::Dead, Bucket::Idle, Bucket::Idle]);
}

// ---- sums, bands and rows ----

/// A small deterministic generator (no clock, no entropy).
struct Lcg(u64);
impl Lcg {
    fn next(&mut self, n: u64) -> u64 {
        self.0 = self
            .0
            .wrapping_mul(6364136223846793005)
            .wrapping_add(1442695040888963407);
        (self.0 >> 33) % n
    }
}

fn random_step(g: &mut Lcg) -> SeatStep {
    let alive = g.next(10) != 0;
    SeatStep {
        alive,
        moved: if g.next(2) == 0 {
            0.
        } else {
            g.next(12) as f64
        },
        hp_full: g.next(3) != 0,
        quest_open: g.next(4) == 0,
        hit_elite: g.next(40) == 0,
        hit_camp: g.next(25) == 0,
        hit_player: g.next(60) == 0,
        camps_normal: g.next(50).saturating_sub(48) as u32,
        camps_elite: g.next(80).saturating_sub(78) as u32,
        deaths: (g.next(200) == 0) as u32,
        quests_completed: (g.next(150) == 0) as u32,
        elite_kills: (g.next(300) == 0) as u32,
        picks: (0..g.next(100).saturating_sub(97))
            .map(|k| format!("pick{}", (g.next(3) + k) % 3))
            .collect(),
        farm_stacks: None,
        weapon: (g.next(500) == 0).then(|| ["sword", "bow", "axe"][g.next(3) as usize].to_string()),
    }
}

fn generated(seed: u64, seats: usize, steps: u32, tick0: u32) -> Vec<TimeStep> {
    let mut g = Lcg(seed);
    (0..steps)
        .map(|i| TimeStep {
            tick: tick0 + i,
            seats: (0..seats).map(|_| random_step(&mut g)).collect(),
        })
        .collect()
}

fn sum_rows(rows: &[SeatRow], f: impl Fn(&SeatRow) -> u64) -> u64 {
    rows.iter().map(f).sum()
}

#[test]
fn whatever_the_sequence_every_step_is_in_one_bucket_and_the_bands_split_the_same_steps() {
    for seed in [1, 2, 3, 99] {
        // Ticks 5800 to 13400 cross both band edges.
        let steps = generated(seed, 4, 7600, 5800);
        let rows = tally_time(&steps, 4);
        assert_eq!(rows.len(), 4);
        for (i, r) in rows.iter().enumerate() {
            assert_eq!(r.seat, i, "rows are in seat order");
            assert_eq!(r.steps, 7600, "seed {seed} seat {i}: a seat has every step");
            assert_eq!(
                r.time.total(),
                r.steps,
                "seed {seed} seat {i}: buckets must sum to steps: {:?}",
                r.time
            );
            for b in Bucket::ALL {
                let by_band: u64 = r.bands.iter().map(|x| x.get(b)).sum();
                assert_eq!(by_band, r.time.get(b), "{b:?}: bands sum to the bucket");
            }
            assert!(
                r.time.dead > 0 && r.time.camp > 0 && r.time.moving > 0,
                "{r:?}"
            );
        }
    }
}

#[test]
fn the_minute_bands_split_at_ticks_6000_and_13200() {
    let ticks = [0u32, 1, 5999, 6000, 6001, 13_199, 13_200, 24_000];
    let steps: Vec<TimeStep> = ticks
        .iter()
        .map(|&tick| TimeStep {
            tick,
            seats: vec![alive()],
        })
        .collect();
    let r = &tally_time(&steps, 1)[0];
    assert_eq!(r.steps, 8);
    assert_eq!(
        [r.bands[0].idle, r.bands[1].idle, r.bands[2].idle],
        [3, 3, 2],
        "minutes 0 to 5 are ticks below 6000, 5 to 11 below 13200, then the rest"
    );
    assert_eq!(r.time.idle, 8);
}

#[test]
fn a_row_adds_up_to_what_was_fed_in_and_seats_do_not_leak_into_each_other() {
    let steps = generated(7, 3, 4000, 1);
    let rows = tally_time(&steps, 3);
    #[allow(clippy::needless_range_loop)]
    for seat in 0..3 {
        let fed =
            |f: fn(&SeatStep) -> u64| -> u64 { steps.iter().map(|s| f(&s.seats[seat])).sum() };
        let r = &rows[seat];
        assert_eq!(
            r.camps_normal,
            fed(|s| s.camps_normal.into()),
            "seat {seat}"
        );
        assert_eq!(r.camps_elite, fed(|s| s.camps_elite.into()), "seat {seat}");
        assert_eq!(r.deaths, fed(|s| s.deaths.into()), "seat {seat}");
        assert_eq!(
            r.quests_completed,
            fed(|s| s.quests_completed.into()),
            "seat {seat}"
        );
        assert_eq!(r.elite_kills, fed(|s| s.elite_kills.into()), "seat {seat}");
        assert_eq!(
            r.picks.values().sum::<u64>(),
            fed(|s| s.picks.len() as u64),
            "seat {seat}: every named pick is counted once under its name"
        );
    }
    let fed_all: u64 = steps
        .iter()
        .flat_map(|s| &s.seats)
        .map(|s| u64::from(s.camps_normal + s.camps_elite))
        .sum();
    assert!(fed_all > 0, "the generator must feed some camps");
    assert_eq!(
        sum_rows(&rows, |r| r.camps_normal + r.camps_elite),
        fed_all,
        "seat rows sum to the totals fed in"
    );
    // One seat fights, the other never does.
    let steps: Vec<TimeStep> = (0..100u32)
        .map(|i| TimeStep {
            tick: i + 1,
            seats: vec![if i == 0 { camp(alive()) } else { alive() }, alive()],
        })
        .collect();
    let rows = tally_time(&steps, 2);
    assert_eq!(rows[0].time.camp, 60, "{:?}", rows[0].time);
    assert_eq!(rows[1].time.camp, 0, "{:?}", rows[1].time);
    assert_eq!(rows[1].time.idle, 100);
}

#[test]
fn picks_are_counted_by_name_stacks_by_sample_minute_and_the_weapon_is_the_last_one_read() {
    let mut steps: Vec<TimeStep> = (0..10u32)
        .map(|i| TimeStep {
            tick: i + 1,
            seats: vec![alive(), alive()],
        })
        .collect();
    steps[1].seats[0].picks = vec!["fast".into(), "sharp".into()];
    steps[4].seats[0].picks = vec!["fast".into()];
    steps[2].seats[1].picks = vec!["sharp".into()];
    steps[3].seats[0].farm_stacks = Some((5, 40));
    steps[6].seats[0].farm_stacks = Some((8, 80));
    steps[5].seats[1].farm_stacks = Some((12, 7));
    steps[0].seats[0].weapon = Some("sword".into());
    steps[7].seats[0].weapon = Some("axe".into());
    let rows = tally_time(&steps, 2);
    let get = |r: &SeatRow, k: &str| r.picks.get(k).copied().unwrap_or(0);
    assert_eq!((get(&rows[0], "fast"), get(&rows[0], "sharp")), (2, 1));
    assert_eq!((get(&rows[1], "fast"), get(&rows[1], "sharp")), (0, 1));
    assert_eq!(rows[0].farm_stacks, [Some(40), Some(80), None]);
    assert_eq!(rows[1].farm_stacks, [None, None, Some(7)]);
    assert_eq!(rows[0].weapon.as_deref(), Some("axe"));
    assert_eq!(rows[1].weapon, None);
}

// ---- real runs (release only) ----

/// Seeds 1, 2, 3 at 6000 ticks (five minutes), eight raid seats in human seats: the lab's other release-only files.
const SEEDS: std::ops::Range<u64> = 1..4;
const MAX_TICKS: u32 = 6000;
const SEATS: u64 = 8;
const BUCKETS: [&str; 9] = [
    "dead",
    "elite",
    "camp",
    "player",
    "quest_moving",
    "quest_still",
    "moving",
    "resting",
    "idle",
];

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

fn parsed() -> Value {
    serde_json::from_str(jobs1()).expect("json")
}

fn seat_buckets(row: &Value) -> Vec<u64> {
    BUCKETS.iter().map(|b| n(&row["buckets"], b)).collect()
}

#[test]
#[cfg_attr(
    debug_assertions,
    ignore = "release only: three 6000-tick seeds with map generation"
)]
fn the_real_report_has_one_row_per_seed_and_seat_and_the_buckets_add_up() {
    let r = parsed();
    let rows = r["seats"]
        .as_array()
        .unwrap_or_else(|| panic!("the report has no `seats` array: {:.300}", jobs1()));
    let want: Vec<(u64, u64)> = SEEDS
        .flat_map(|seed| (0..SEATS).map(move |seat| (seed, seat)))
        .collect();
    assert_eq!(rows.len(), want.len(), "one row per seed and seat");
    let got: Vec<(u64, u64)> = rows.iter().map(|x| (n(x, "seed"), n(x, "seat"))).collect();
    assert_eq!(got, want, "seed order, then seat order");
    for x in rows {
        assert!(x["faction"].is_string(), "{x}");
        assert!(x["picks"].is_object(), "{x}");
        let steps = n(x, "steps");
        assert_eq!(
            seat_buckets(x).iter().sum::<u64>(),
            steps,
            "the buckets of a seat sum to its steps: {x}"
        );
    }
}

#[test]
#[cfg_attr(
    debug_assertions,
    ignore = "release only: three 6000-tick seeds with map generation"
)]
fn the_real_report_holds_the_identities_between_seats_and_the_world() {
    let r = parsed();
    let rows = r["seats"].as_array().expect("seats");
    let ticks: u64 = r["seeds"]
        .as_array()
        .expect("seeds")
        .iter()
        .map(|s| n(s, "ticks"))
        .sum();
    let sum = |k: &str| rows.iter().map(|x| n(x, k)).sum::<u64>();
    assert_eq!(
        sum("steps"),
        SEATS * ticks,
        "eight seats times each match's ticks"
    );

    let w = &r["world"];
    assert_eq!(
        sum("camps_normal") + sum("camps_elite"),
        n(w, "camps_credited"),
        "per-seat camps sum to the world's credited camps"
    );
    assert_eq!(sum("camps_normal"), n(w, "camps_credited_normal"));
    assert!(n(w, "camps_credited_normal") <= n(w, "camps_credited"));
    assert_eq!(sum("deaths"), n(&w["player_deaths"], "total"));
    let completed: u64 = r["quests"]["by_quest"]
        .as_array()
        .expect("by_quest")
        .iter()
        .map(|q| n(q, "completed"))
        .sum();
    assert_eq!(sum("quests_completed"), completed);
    let killed: u64 = r["elites"]["by_kind_weapon"]
        .as_array()
        .expect("elites")
        .iter()
        .map(|e| n(e, "killed"))
        .sum();
    // Coordinator's oracle-amend (2026-11-23): a seat's `elite_kills` is the sim's own credit (`Death.by` of an elite),
    // counted for every elite death; the elites block's `killed` counts only a kill that closed an attempt its killer
    // still had open, so it can be lower (77 against 76 on six full-length seeds). The world carries the sim's count,
    // so the kills the attempts miss are a number in the report and not a guess.
    assert_eq!(
        sum("elite_kills"),
        n(w, "elite_kills_credited"),
        "per-seat elite kills sum to the elite kills the sim credited to a seat"
    );
    assert!(
        killed <= n(w, "elite_kills_credited"),
        "the attempts' kills never exceed the kills the sim credited to a seat"
    );
    assert_eq!(
        n(&w["kill_secs_normal"], "n") + n(&w["kill_secs_elite"], "n"),
        n(&w["kill_secs"], "n"),
        "the camp fight seconds split into normal and elite without loss"
    );
    let split = w["kill_secs_normal"]["sum"].as_f64().expect("sum")
        + w["kill_secs_elite"]["sum"].as_f64().expect("sum");
    assert!((split - w["kill_secs"]["sum"].as_f64().expect("sum")).abs() < 1e-6);

    // The world's time block is the seats', bucket by bucket, and its bands split each bucket.
    let t = &w["time"];
    assert_eq!(n(t, "steps"), SEATS * ticks);
    let mut all_total = 0;
    for (i, b) in BUCKETS.iter().enumerate() {
        let cell = &t["buckets"][b];
        let all = n(cell, "all");
        all_total += all;
        assert_eq!(
            all,
            rows.iter().map(|x| seat_buckets(x)[i]).sum::<u64>(),
            "{b}: the world's seat-steps are the seats' sum"
        );
        assert_eq!(
            all,
            n(cell, "m0_5") + n(cell, "m5_11") + n(cell, "m11_plus"),
            "{b}: the bands split `all`"
        );
        assert_eq!(
            n(cell, "m11_plus"),
            0,
            "{b}: 6000 ticks never reach minute 11"
        );
    }
    assert_eq!(all_total, n(t, "steps"));
    let farm = rows.iter().map(|x| &x["farm_stacks"]).all(|f| {
        ["m5", "m8", "m12"]
            .iter()
            .all(|k| f.get(k).is_some_and(|v| v.is_null() || v.is_u64()))
    });
    assert!(
        farm,
        "farm_stacks has m5, m8 and m12, each a number or null"
    );
}

#[test]
#[cfg_attr(
    debug_assertions,
    ignore = "release only: three 6000-tick seeds with map generation"
)]
fn raid_bots_spend_time_fighting_walking_dying_and_questing() {
    let r = parsed();
    let t = &r["world"]["time"]["buckets"];
    let all = |b: &str| n(&t[b], "all");
    assert!(all("camp") > 0, "no seat-step in `camp`: {t}");
    assert!(all("moving") > 0, "no seat-step in `moving`: {t}");
    let deaths = n(&r["world"]["player_deaths"], "total");
    assert!(
        all("dead") >= deaths && all("dead") > 0,
        "{deaths} deaths must leave at least that many dead seat-steps and at least one: {t}"
    );
    assert!(
        all("quest_moving") + all("quest_still") > 0,
        "no seat-step with a quest open (human seats can take quests): {t}"
    );
    // The majority of a minute is alive and not dead.
    let total: u64 = BUCKETS.iter().map(|b| all(b)).sum();
    assert!(all("dead") * 2 < total, "dead is most of the match: {t}");
}

#[test]
#[cfg_attr(
    debug_assertions,
    ignore = "release only: three 6000-tick seeds with map generation"
)]
fn the_time_report_is_byte_identical_for_jobs_1_and_4() {
    let (one, four) = (jobs1(), &report(4));
    assert!(
        one.contains("\"seats\":[{") && one.contains("\"quest_still\""),
        "the report has no seat rows or time block: {:.300}",
        one
    );
    assert_eq!(one, four, "--jobs 1 and --jobs 4 gave different bytes");
}
