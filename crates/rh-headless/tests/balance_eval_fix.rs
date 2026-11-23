//! Oracle for packet P-BAL-EVAL-1-fix (docs/backlog/packets/P-BAL-EVAL-1-fix.md): the evaluator invents no model number
//! and prints no non-finite ratio beside a verdict. Invariants over hand-built reports and inputs; no lab run.
use rh_headless::balance_model::{evaluate_json, parse_inputs, to_json};
use serde_json::{json, Value};

/// A `balance-lab/1` report: `camps` credited camps in `pm` player-minutes, mean kill/heal/walk seconds as given,
/// and at minute 5 eight samples holding 144 stacks each.
fn report_with(camps: u64, pm: u64, secs: (f64, f64, f64), quests: Value) -> String {
    let s = |x: f64| json!({ "sum": x * 100., "n": 100 });
    json!({
        "schema": "balance-lab/1",
        "world": {
            "camps_credited": camps,
            "kill_secs": s(secs.0), "heal_secs": s(secs.1), "walk_secs": s(secs.2),
            "player_deaths": { "total": 6,
                "by_minute": { "0": { "player_ticks": pm * 1200, "total": 6 } } },
            "minutes": { "5": { "samples": 8, "farm_stacks": { "ember_imp": 8 * 144 } } }
        },
        "quests": { "by_quest": quests, "accepted_by_minute": { "4": 100 } },
        "augments": { "by_card": [] }
    })
    .to_string()
}

fn report(camps: u64, quests: Value) -> String {
    report_with(camps, 100, (12., 4., 4.), quests)
}

fn inputs_value(quests: Value, augments: Value) -> Value {
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
}

fn counts(id: &str, n: u64) -> Value {
    json!({ "quest": id, "attempts": n, "completed": n, "complete_secs": { "sum": 45. * n as f64, "n": n } })
}

/// `v` with the member at `path` removed.
fn without(mut v: Value, path: &[&str]) -> String {
    let (last, parents) = path.split_last().unwrap();
    let mut at = &mut v;
    for p in parents {
        at = &mut at[*p];
    }
    assert!(
        at.as_object_mut().unwrap().remove(*last).is_some(),
        "{path:?} is not in the fixture"
    );
    v.to_string()
}

fn augment(growth: &str) -> Value {
    json!({ "fight_stacks_now": 28, "one_shot_stacks": 0, "farm_speed_pct": 38, "income_mult": 1,
            "hazard_mult": 1, "cap_stacks": 0, "growth": growth })
}

/// No number of the model comes from code: every block and number the inputs file owns must be in it.
#[test]
fn a_missing_design_number_is_an_error_naming_it() {
    let base = || inputs_value(json!({}), json!({}));
    let paths: &[&[&str]] = &[
        &["band"],
        &["premium"],
        &["stacks_per_camp"],
        &["coliseum_start_minute"],
        &["difficulty_targets"],
        &["failure"],
        &["elite_assumptions"],
        &["band", "low"],
        &["band", "high"],
        &["difficulty_targets", "easy"],
        &["difficulty_targets", "medium"],
        &["difficulty_targets", "hard"],
        &["failure", "downtime_secs"],
        &["failure", "failed_attempt_share"],
        &["elite_assumptions", "pick_minute"],
        &["elite_assumptions", "time_ce"],
        &["elite_assumptions", "death_chance"],
        &["elite_assumptions", "time_ce", "solo"],
        &["elite_assumptions", "death_chance", "trio"],
    ];
    for path in paths {
        let text = without(base(), path);
        let field = path.last().unwrap();
        for result in [
            parse_inputs(&text).map(|_| ()),
            evaluate_json(&report(290, json!([])), &text).map(|_| ()),
        ] {
            let err = result.expect_err(&format!("inputs without {path:?} must be refused"));
            assert!(
                err.contains(field),
                "error for {path:?} must name {field}: {err}"
            );
        }
    }
}

/// An unknown `growth` is a typo, not "no growth". `none` and `measured` stay valid; an omitted multiplier is the identity.
#[test]
fn an_unknown_growth_is_an_error() {
    let bad = inputs_value(json!({}), json!({ "c": augment("mesured") })).to_string();
    let err = evaluate_json(&report(290, json!([])), &bad).expect_err("typo in growth");
    assert!(err.contains("growth"), "{err}");
    for g in ["none", "measured"] {
        let ok = inputs_value(json!({}), json!({ "c": augment(g) })).to_string();
        evaluate_json(&report(290, json!([])), &ok).unwrap_or_else(|e| panic!("growth {g}: {e}"));
    }
    let mut a = augment("none");
    a.as_object_mut().unwrap().remove("income_mult");
    a.as_object_mut().unwrap().remove("hazard_mult");
    let ok = inputs_value(json!({}), json!({ "c": a })).to_string();
    evaluate_json(&report(290, json!([])), &ok)
        .expect("a multiplier of 1 is arithmetic, not a design number");
}

#[test]
fn schemas_match_exactly_and_quest_ids_are_unique() {
    let i = inputs_value(json!({}), json!({})).to_string();
    let r = report(290, json!([]));
    assert!(evaluate_json(&r, &i).is_ok());
    assert!(evaluate_json(&r.replace("balance-lab/1", "balance-lab/10"), &i).is_err());
    assert!(evaluate_json(&r, &i.replace("balance-inputs/1", "balance-inputs/10")).is_err());
    let dup = report(290, json!([counts("q", 10), counts("q", 20)]));
    assert!(
        evaluate_json(&dup, &i).is_err(),
        "a duplicate quest id is refused"
    );
}

/// Every object of the output: a verdict (or `meets_rule`) sits only beside numbers that are present.
fn walk(v: &Value, verdicts: &mut usize) {
    match v {
        Value::Object(m) => {
            let judged = ["verdict", "meets_rule"]
                .iter()
                .any(|k| m.get(*k).is_some_and(|x| !x.is_null()));
            if judged {
                *verdicts += 1;
                for k in [
                    "value",
                    "price",
                    "ratio",
                    "ratio_solo",
                    "ratio_duo",
                    "ratio_trio",
                ] {
                    if let Some(x) = m.get(k) {
                        assert!(!x.is_null(), "null {k} beside a verdict: {v}");
                    }
                }
            }
            m.values().for_each(|x| walk(x, verdicts));
        }
        Value::Array(a) => a.iter().for_each(|x| walk(x, verdicts)),
        _ => {}
    }
}

fn quest_inputs() -> Value {
    json!({
        // Value over price 0 is infinite today; the empty buff is 0 over 0, NaN today.
        "spell": { "difficulty": "easy", "reward": { "kind": "spell", "duel_equivalent_ce": 2.0 } },
        "empty_buff": { "difficulty": "easy", "reward": { "kind": "permanent_buff", "direct_stacks": 0 } }
    })
}

/// With no camps credited the price is 0: a ratio of value over 0 is neither a ratio nor a verdict.
#[test]
fn a_zero_price_gives_no_ratio_and_no_verdict() {
    let quests = json!([counts("spell", 10), counts("empty_buff", 10)]);
    let i = inputs_value(quest_inputs(), json!({})).to_string();

    // Control: with camps credited the spell row is judged, so the walk below is not vacuous.
    let e = evaluate_json(&report(290, quests.clone()), &i).expect("evaluates");
    let mut judged = 0;
    walk(&serde_json::from_str(&to_json(&e)).unwrap(), &mut judged);
    assert!(
        judged >= 1
            && e.quests
                .iter()
                .any(|q| q.ratio.is_some() && q.verdict.is_some())
    );

    let e = evaluate_json(&report(0, quests), &i).expect("evaluates");
    for q in &e.quests {
        assert!(q.ratio.is_none() && q.verdict.is_none(), "{q:?}");
        assert!(!q.not_measured.is_empty(), "the reason is stated: {q:?}");
    }
    walk(&serde_json::from_str(&to_json(&e)).unwrap(), &mut 0);
}

/// A farm-speed penalty that drives the farm rate to infinity gives an infinite augment value: absent, with a reason.
#[test]
fn a_non_finite_augment_value_is_not_measured() {
    // kappa = 10 / 20 = 0.5, so a farm speed of -200 % divides by zero: 1 - 0.5 * -200 / (100 - 200) = 0.
    let r = report_with(300, 100, (10., 5., 5.), json!([]));
    let mut a = augment("none");
    a["farm_speed_pct"] = json!(-200);
    let i = inputs_value(json!({}), json!({ "bad": a, "good": augment("none") })).to_string();
    let e = evaluate_json(&r, &i).expect("evaluates");
    let get = |c: &str| e.augments.iter().find(|a| a.card == c).unwrap();
    let bad = get("bad");
    assert!(
        bad.value.is_none()
            && bad.ratio_solo.is_none()
            && bad.ratio_duo.is_none()
            && bad.meets_rule.is_none(),
        "{bad:?}"
    );
    assert!(!bad.not_measured.is_empty());
    assert!(get("good").value.is_some() && get("good").meets_rule.is_some());
    let mut judged = 0;
    walk(&serde_json::from_str(&to_json(&e)).unwrap(), &mut judged);
    assert!(judged >= 1);
}

/// The shipped inputs file already carries every required block and still parses.
#[test]
fn shipped_inputs_still_parse() {
    let path = concat!(
        env!("CARGO_MANIFEST_DIR"),
        "/../../tools/balance-lab/inputs.json"
    );
    let text = std::fs::read_to_string(path).expect("inputs.json exists");
    parse_inputs(&text).expect("the shipped inputs parse with every block required");
}
