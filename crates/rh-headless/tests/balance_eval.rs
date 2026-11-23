//! Oracle for packet P-BAL-EVAL-1 (docs/backlog/packets/P-BAL-EVAL-1.md): `balance_model`, the evaluator.
//!
//! Loose proof (owner direction 2026-11-10): the code reproduces the numbers the value model's own document and workbook
//! state for their worked examples, within 10%, from hand-built reports, plus the rule that what a report does not
//! measure is "not measured" and never a zero or a verdict. Sources: `docs/QUEST_VALUE_MODEL.md` sections 8 and 11.4,
//! `docs/quests/FUTURE_ONLY_Quest_Design_Discussion.xlsx` sheet `Augment_Value`. No lab run is needed.
use rh_headless::balance_model::{evaluate_json, parse_inputs, Evaluation, QuestVerdict, Verdict};
use serde_json::{json, Value};

const TOL: f64 = 0.10;

fn close(got: Option<f64>, want: f64, tol: f64, what: &str) {
    let got =
        got.unwrap_or_else(|| panic!("{what}: expected about {want}, got not-measured (None)"));
    assert!(
        (got - want).abs() <= want.abs() * tol,
        "{what}: expected {want} within {:.0}%, got {got}",
        tol * 100.
    );
}

fn quest<'a>(e: &'a Evaluation, id: &str) -> &'a QuestVerdict {
    e.quests.iter().find(|q| q.quest == id).unwrap_or_else(|| {
        panic!(
            "quest {id} is missing from the evaluation ({} quests)",
            e.quests.len()
        )
    })
}

/// A lab report in the shape of `balance-lab/1`, with the measured side the workbook's inputs imply:
/// `camps_credited` camps in `player_minutes` player-minutes, kill 12 s, heal 4 s, walk 4 s (kappa 0.6), `deaths` deaths,
/// and at minute 5 eight samples holding 144 stacks each (12 camp-equivalents banked).
fn report(
    camps_credited: u64,
    player_minutes: u64,
    deaths: u64,
    quests: Value,
    accepted: Value,
) -> String {
    let secs = |s: f64| json!({ "sum": s * 100., "n": 100 });
    json!({
        "schema": "balance-lab/1",
        "world": {
            "camps_cleared": camps_credited, "camps_credited": camps_credited,
            "kill_secs": secs(12.), "heal_secs": secs(4.), "walk_secs": secs(4.),
            "player_deaths": { "total": deaths,
                "by_minute": { "0": { "player_ticks": player_minutes * 1200, "total": deaths } } },
            "minutes": { "5": { "samples": 8, "farm_stacks": { "ember_imp": 8 * 144 } } }
        },
        "quests": { "by_quest": quests, "accepted_by_minute": accepted },
        "augments": { "by_card": [] }
    })
    .to_string()
}

fn inputs(quests: Value, augments: Value) -> String {
    json!({
        "schema": "balance-inputs/1",
        "band": { "low": 0.85, "high": 1.25 },
        "premium": 1.15,
        "stacks_per_camp": 12,
        "coliseum_start_minute": 12,
        "difficulty_targets": { "easy": 0.95, "medium": 0.85, "hard": 0.70 },
        "failure": { "downtime_secs": 15, "failed_attempt_share": 1.0 },
        "elite_assumptions": { "pick_minute": 5,
            "time_ce": { "solo": 2.2, "duo": 1.6, "trio": 1.4 },
            "death_chance": { "solo": 0.15, "duo": 0.05, "trio": 0.02 } },
        "quests": quests,
        "augments": augments
    })
    .to_string()
}

fn counts(id: &str, attempts: u64, completed: u64, complete: Value) -> Value {
    json!({ "quest": id, "dealt": attempts, "accepted": attempts, "attempts": attempts, "completed": completed,
            "ended_timer": 0, "ended_death": 0, "abandoned": 0, "complete_secs": complete })
}

/// The +10% damage reward of QUEST_VALUE_MODEL section 8 (10 stacks of 1%).
fn buff10() -> Value {
    json!({ "kind": "permanent_buff", "direct_stacks": 10, "damage_fraction": 0.10, "move_fraction": 0 })
}

/// QUEST_VALUE_MODEL section 8 (lines 147-165): c_eff 2.9, kappa 0.6, s 12, pi 1.15, R = 8, tau = 0.75 min, p = 1.
/// "V_P at R = 8 is 2.04", "needs V >= 2.50", "overpriced for its reward even at R = 8", "about a 37 s quest".
#[test]
fn quest_a_worked_example() {
    let quests = json!([counts(
        "worked_example_a",
        100,
        100,
        json!({ "sum": 4500., "n": 100 })
    )]);
    let r = report(290, 100, 6, quests, json!({ "4": 100 }));
    let i = inputs(
        json!({ "worked_example_a": { "difficulty": "medium", "reward": buff10() } }),
        json!({}),
    );
    let e = evaluate_json(&r, &i).expect("evaluates");
    let q = quest(&e, "worked_example_a");
    close(q.value, 2.04, TOL, "value of +10% damage at R = 8");
    close(q.price, 2.50, TOL, "price = pi * E[C] = 1.15 * 2.9 * 0.75");
    close(q.ratio, 2.04 / 2.50, TOL, "ratio");
    assert_eq!(
        q.verdict,
        Some(Verdict::BelowBand),
        "0.82 is under the 0.85 band"
    );
    close(
        q.reward_value_at_parity,
        2.50,
        TOL,
        "reward value at ratio 1.0",
    );
    close(
        q.duration_secs_at_parity,
        37.2,
        TOL,
        "quest duration at ratio 1.0 (tau = 0.62 min)",
    );
}

/// QUEST_VALUE_MODEL 11.4 (lines 247-253): the timer that meets the tier's chance to beat it. The samples are the doc's own
/// mixture, tau + k * 20 s with k ~ Poisson(mu): Watchtowers easy mu 0.2 -> 50 s; Rime Hunt medium mu 0.3 -> 65 s;
/// Smuggler's Run hard mu 1.2 -> 100 s.
#[test]
fn timers_meet_the_difficulty_target() {
    let mix = |base: f64, per: &[u64]| -> Value {
        let mut s = Vec::new();
        for (k, n) in per.iter().enumerate() {
            s.extend(vec![base + 20. * k as f64; *n as usize]);
        }
        json!({ "sum": s.iter().sum::<f64>(), "n": s.len(), "samples": s })
    };
    let w = mix(30., &[82, 16, 2]);
    let rh = mix(45., &[741, 222, 33, 4]);
    let sm = mix(60., &[301, 361, 217, 87, 34]);
    let quests = json!([
        counts("watchtowers", 100, 100, w),
        counts("rime_hunt", 1000, 1000, rh),
        counts("smugglers_run", 1000, 1000, sm)
    ]);
    let r = report(290, 100, 6, quests, json!({ "4": 100 }));
    let i = inputs(
        json!({ "watchtowers": { "difficulty": "easy" }, "rime_hunt": { "difficulty": "medium" },
                "smugglers_run": { "difficulty": "hard" } }),
        json!({}),
    );
    let e = evaluate_json(&r, &i).expect("evaluates");
    close(
        quest(&e, "watchtowers").timer_secs,
        50.,
        0.01,
        "easy timer (0.95)",
    );
    close(
        quest(&e, "rime_hunt").timer_secs,
        65.,
        0.01,
        "medium timer (0.85)",
    );
    close(
        quest(&e, "smugglers_run").timer_secs,
        100.,
        0.01,
        "hard timer (0.70)",
    );
}

/// Workbook `Augment_Value`: mu 0.06, pick at minute 5 with the coliseum at 12, banked 12, c_eff 3, kappa 0.6, elite
/// time 2.2 / 1.6 / 1.4 and death chance 0.15 / 0.05 / 0.02: "Needed solo 7.558, duo 3.516, trio 2.280". Row `Committed`
/// (28 fight stacks, 38% farm speed, income 1, hazard 1, no growth): V = 4.927, V / needed = 0.652 / 1.401 / 2.161.
#[test]
fn augment_prices_and_a_value_match_the_workbook() {
    let r = report(300, 100, 6, json!([]), json!({}));
    let i = inputs(
        json!({}),
        json!({ "committed": { "fight_stacks_now": 28, "one_shot_stacks": 0, "farm_speed_pct": 38, "income_mult": 1,
                               "hazard_mult": 1, "cap_stacks": 0, "growth": "none" } }),
    );
    let e = evaluate_json(&r, &i).expect("evaluates");
    close(e.needed.solo, 7.558, TOL, "needed solo");
    close(e.needed.duo, 3.516, TOL, "needed with one helper");
    close(e.needed.trio, 2.280, TOL, "needed with two helpers");
    let a = e
        .augments
        .iter()
        .find(|a| a.card == "committed")
        .expect("committed is missing from the evaluation");
    close(a.value, 4.927, TOL, "Committed value");
    close(a.ratio_solo, 0.652, TOL, "Committed / needed solo");
    close(a.ratio_duo, 1.401, TOL, "Committed / needed duo");
    close(a.ratio_trio, 2.161, TOL, "Committed / needed trio");
    assert_eq!(a.meets_rule, Some(true), "duo >= 0.85 and solo <= 1.25");
}

/// Anything the report does not measure is "not measured": no number, no verdict, and never a zero.
#[test]
fn unmeasured_is_not_a_verdict() {
    let reward = json!({ "difficulty": "easy", "reward": buff10() });
    let i = inputs(
        json!({ "nobody_completes": reward, "no_samples": reward, "measured": reward }),
        json!({ "grower": { "fight_stacks_now": 0, "one_shot_stacks": 0, "farm_speed_pct": 0, "income_mult": 1,
                            "hazard_mult": 1, "cap_stacks": 40, "growth": "measured" } }),
    );
    let quests = json!([
        // The real shape of today's lab output: accepted, never completed, no completion times.
        counts("nobody_completes", 30, 0, json!({ "sum": 0., "n": 0 })),
        // Completions with a mean but no samples: a ratio, no timer.
        counts("no_samples", 50, 50, json!({ "sum": 2250., "n": 50 })),
        // Completions with samples but no row in the inputs.
        counts(
            "not_in_inputs",
            10,
            10,
            json!({ "sum": 450., "n": 10, "samples": vec![45.0; 10] })
        )
    ]);
    let r = report(290, 100, 6, quests, json!({ "4": 40 }));
    let e = evaluate_json(&r, &i).expect("evaluates");

    let q = quest(&e, "nobody_completes");
    assert!(
        q.value.is_none()
            && q.price.is_none()
            && q.ratio.is_none()
            && q.verdict.is_none()
            && q.timer_secs.is_none(),
        "zero completions give no value, price, ratio, verdict or timer: {q:?}"
    );
    assert!(
        q.reward_value_at_parity.is_none() && q.duration_secs_at_parity.is_none(),
        "{q:?}"
    );
    assert!(!q.not_measured.is_empty(), "the reason is stated");

    let q = quest(&e, "no_samples");
    assert!(
        q.ratio.is_some() && q.verdict.is_some(),
        "mean time and completions give a ratio: {q:?}"
    );
    assert!(
        q.timer_secs.is_none(),
        "a mean is not a quantile: no timer without samples"
    );
    assert!(!q.not_measured.is_empty());

    let q = quest(&e, "not_in_inputs");
    assert!(
        q.timer_secs.is_none(),
        "no difficulty class in the inputs: no timer"
    );
    assert!(q.verdict.is_none() && !q.not_measured.is_empty());

    let a = e
        .augments
        .iter()
        .find(|a| a.card == "grower")
        .expect("grower is missing");
    assert!(
        a.ratio_duo.is_none() && a.meets_rule.is_none(),
        "growth is measured and there is none: {a:?}"
    );
    assert!(!a.not_measured.is_empty());

    let bad = report(290, 100, 6, json!([]), json!({})).replace("balance-lab/1", "balance-lab/2");
    assert!(
        evaluate_json(&bad, &i).is_err(),
        "another schema is refused"
    );
}

/// The shipped design-intent file: the band and targets of the plan, and a class for every route-clock quest.
#[test]
fn shipped_inputs_hold_the_design_intent() {
    let path = concat!(
        env!("CARGO_MANIFEST_DIR"),
        "/../../tools/balance-lab/inputs.json"
    );
    let text = std::fs::read_to_string(path).expect("tools/balance-lab/inputs.json exists");
    let inputs = parse_inputs(&text).expect("the shipped inputs parse");
    assert_eq!((inputs.band.low, inputs.band.high), (0.85, 1.25));
    assert_eq!(
        (
            inputs.difficulty_targets.easy,
            inputs.difficulty_targets.medium,
            inputs.difficulty_targets.hard
        ),
        (0.95, 0.85, 0.70)
    );
    let v: Value = serde_json::from_str(&text).expect("json");
    for d in rh_sim::quests::board::catalog::DEFINITIONS.iter() {
        if d.route_timer_ms.is_none() {
            continue;
        }
        let class = v["quests"][d.id]["difficulty"].as_str().unwrap_or("");
        assert!(
            ["easy", "medium", "hard"].contains(&class),
            "route quest {} has no difficulty class in inputs.json (got {class:?})",
            d.id
        );
    }
}
