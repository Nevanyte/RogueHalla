//! Oracle for packet P-BAL-EVAL-2 (docs/backlog/packets/P-BAL-EVAL-2.md): the evaluator closes its known model gaps.
//!
//! Loose proof (owner direction 2026-11-10), as arithmetic checks over hand-built reports whose expected numbers are worked
//! in the comments from `docs/QUEST_VALUE_MODEL.md` (4.1, 4.2, 4.3), plus one run of the shipped `inputs.json` on a small
//! fixture reduced from the coordinator's dry run (`tests/fixtures/balance_pilot_dry_report.json`). No lab run is needed.
//!
//! The common report: `camps` credited camps in 100 player-minutes (so `c_eff = camps / 100`), kill 12 s, heal 4 s,
//! walk 4 s (kappa 0.6, walk share 0.2), 6 deaths (mu 0.06), 12 camp-equivalents banked at minute 5, quest acceptances at
//! minute 4 (pick-up minute 4, so the farm time left after pick-up is R = 12 - 4 = 8 minutes with the coliseum at 12).
use rh_headless::balance_model::{
    evaluate_json, parse_inputs, render_table, to_json, Evaluation, QuestVerdict, Verdict,
};
use serde_json::{json, Value};

const REF: f64 = 2.9;

fn near(got: Option<f64>, want: f64, what: &str) {
    let got = got.unwrap_or_else(|| panic!("{what}: expected {want}, got not-measured (None)"));
    assert!(
        (got - want).abs() <= want.abs() * 1e-9 + 1e-12,
        "{what}: expected {want}, got {got}"
    );
}

fn report_native(camps: u64, quests: Value, native: Value) -> String {
    let secs = |s: f64| json!({ "sum": s * 100., "n": 100 });
    let mut minutes = json!({ "5": { "samples": 8, "farm_stacks": { "ember_imp": 8 * 144 } } });
    if let Some(m) = native.as_object() {
        for (minute, cards) in m {
            minutes[minute.as_str()]["native"] = cards.clone();
        }
    }
    json!({
        "schema": "balance-lab/1",
        "world": {
            "camps_credited": camps,
            "kill_secs": secs(12.), "heal_secs": secs(4.), "walk_secs": secs(4.),
            "player_deaths": { "total": 6,
                "by_minute": { "0": { "player_ticks": 100 * 1200, "total": 6 } } },
            "minutes": minutes
        },
        "quests": { "by_quest": quests, "accepted_by_minute": { "4": 100 } },
        "augments": { "by_card": [] }
    })
    .to_string()
}

fn report(camps: u64, quests: Value) -> String {
    report_native(camps, quests, json!({}))
}

fn inputs_with(extra: Value, quests: Value, augments: Value) -> String {
    let mut v = json!({
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
    });
    for (k, x) in extra.as_object().unwrap() {
        v[k.as_str()] = x.clone();
    }
    v.to_string()
}

fn reference() -> Value {
    json!({ "reference_farm_rate": { "camps_per_minute": REF, "source": "QUEST_VALUE_MODEL.md:28" } })
}

/// Ten attempts, ten completions, each `secs` long: p = 1, so the price has no failure term.
fn done(id: &str, secs: f64) -> Value {
    json!({ "quest": id, "attempts": 10, "completed": 10,
            "complete_secs": { "sum": secs * 10., "n": 10, "samples": vec![secs; 10] } })
}

fn buff(direct: f64, damage: f64) -> Value {
    json!({ "kind": "permanent_buff", "direct_stacks": direct, "damage_fraction": damage, "move_fraction": 0 })
}

fn quest<'a>(e: &'a Evaluation, id: &str) -> &'a QuestVerdict {
    e.quests
        .iter()
        .find(|q| q.quest == id)
        .unwrap_or_else(|| panic!("quest {id} is missing"))
}

fn eval(r: &str, i: &str) -> Evaluation {
    evaluate_json(r, i).unwrap_or_else(|e| panic!("evaluates: {e}"))
}

/// c_eff 0.62 measured against a reference of 2.9. The price is linear in the rate, so the price at the reference is the
/// measured price times 2.9 / 0.62 exactly. `flat` (damage 0) has a value that does not depend on the rate, so its ratio
/// falls by exactly that factor; `dividend` (damage 0.05) also gains a farming dividend at the reference rate, so its ratio
/// falls by less. Without the reference number there is no reference field anywhere and the measured rows are unchanged.
#[test]
fn the_reference_rate_scales_the_price_and_the_ratio() {
    let quests = || json!({ "flat": { "reward": buff(18., 0.) }, "dividend": { "reward": buff(24., 0.05) } });
    let r = report(62, json!([done("flat", 25.8), done("dividend", 45.)]));
    let with = eval(&r, &inputs_with(reference(), quests(), json!({})));
    let without = eval(&r, &inputs_with(json!({}), quests(), json!({})));
    near(with.measured.c_eff, 0.62, "measured c_eff");
    near(
        with.reference_farm_rate,
        REF,
        "the reference rate is carried",
    );

    let k = REF / 0.62;
    let f = quest(&with, "flat");
    near(
        f.price_at_reference,
        f.price.unwrap() * k,
        "flat: price at the reference",
    );
    near(
        f.value_at_reference,
        f.value.unwrap(),
        "flat: no dividend, same value",
    );
    near(
        f.ratio_at_reference,
        f.ratio.unwrap() / k,
        "flat: ratio falls by 2.9 / 0.62",
    );

    let d = quest(&with, "dividend");
    near(
        d.price_at_reference,
        d.price.unwrap() * k,
        "dividend: price at the reference",
    );
    let (vr, v) = (
        d.value_at_reference.expect("value at reference"),
        d.value.unwrap(),
    );
    assert!(
        vr > v,
        "a faster farm makes the dividend worth more: {vr} vs {v}"
    );
    let ratio_r = d.ratio_at_reference.expect("ratio at reference");
    assert!(
        ratio_r < d.ratio.unwrap() && ratio_r > d.ratio.unwrap() / k,
        "ratio falls, by less than the price rises: {} -> {ratio_r}",
        d.ratio.unwrap()
    );

    // Without the number: no reference field in the Rust value, the JSON or the table; the measured rows are identical.
    assert!(without.reference_farm_rate.is_none() && without.needed_at_reference.is_none());
    for q in &without.quests {
        assert!(
            q.value_at_reference.is_none()
                && q.price_at_reference.is_none()
                && q.ratio_at_reference.is_none()
                && q.verdict_at_reference.is_none(),
            "{q:?}"
        );
        let m = quest(&with, &q.quest);
        assert_eq!((q.value, q.price, q.ratio), (m.value, m.price, m.ratio));
    }
    assert!(
        !to_json(&without).contains("reference"),
        "no reference key without the number"
    );
    assert!(!render_table(&without).contains("reference"));
    assert!(to_json(&with).contains("ratio_at_reference"));
}

/// The smallest report where the rate changes the verdict: `even_skeletons`' own numbers (18 stacks, 25.8 s, p = 1).
/// Value 18 / 12 = 1.5. Price at c 0.62: 1.15 * 0.62 * 0.43 = 0.3066, ratio 4.89, AboveBand. At 2.9:
/// 1.15 * 2.9 * 0.43 = 1.434, ratio 1.046, InBand (0.85 to 1.25).
#[test]
fn a_verdict_can_differ_between_the_two_rates() {
    let q = json!({ "even": { "reward": buff(18., 0.) } });
    let e = eval(
        &report(62, json!([done("even", 25.8)])),
        &inputs_with(reference(), q, json!({})),
    );
    let q = quest(&e, "even");
    assert_eq!(q.verdict, Some(Verdict::AboveBand));
    assert_eq!(q.verdict_at_reference, Some(Verdict::InBand));
    near(q.ratio, 1.5 / (1.15 * 0.62 * 0.43), "measured ratio");
    near(
        q.ratio_at_reference,
        1.5 / (1.15 * REF * 0.43),
        "ratio at the reference",
    );
}

/// `upfront_stacks` takes `upfront / stacks_per_camp` off the value, at both rates, and nothing off the price.
/// 20 / 12 = 1.6667 CE. Default 0. A negative number is refused, naming the field.
#[test]
fn upfront_stacks_lower_the_value_by_stacks_over_stacks_per_camp() {
    let with_upfront = |u: Option<f64>| {
        let mut b = buff(24., 0.05);
        if let Some(u) = u {
            b["upfront_stacks"] = json!(u);
        }
        let q = json!({ "bargain": { "reward": b } });
        eval(
            &report(62, json!([done("bargain", 5.)])),
            &inputs_with(reference(), q, json!({})),
        )
    };
    let base = with_upfront(None);
    let zero = with_upfront(Some(0.));
    let cost = with_upfront(Some(20.));
    let (b, z, c) = (
        quest(&base, "bargain"),
        quest(&zero, "bargain"),
        quest(&cost, "bargain"),
    );
    near(z.value, b.value.unwrap(), "an upfront of 0 is the default");
    near(c.value, b.value.unwrap() - 20. / 12., "value less 20 / 12");
    near(
        c.value_at_reference,
        b.value_at_reference.expect("value at reference") - 20. / 12.,
        "the same stacks at the reference rate",
    );
    assert_eq!(c.price, b.price, "the cost is on the value side only");
    assert!(c.ratio.unwrap() < b.ratio.unwrap());

    let bad = {
        let mut b = buff(24., 0.05);
        b["upfront_stacks"] = json!(-1);
        inputs_with(json!({}), json!({ "bargain": { "reward": b } }), json!({}))
    };
    let err = parse_inputs(&bad)
        .map(|_| ())
        .expect_err("negative upfront");
    assert!(err.contains("upfront_stacks"), "{err}");
}

fn spell_inputs(spell: Value) -> String {
    inputs_with(reference(), json!({ "cd": { "reward": spell } }), json!({}))
}

fn reusable(delta: Option<f64>, gain: Option<f64>, cap: Option<f64>) -> Value {
    let mut s = json!({ "kind": "spell", "spell_type": "reusable" });
    for (k, v) in [
        ("duel_equivalent_ce", delta),
        ("farm_rate_gain", gain),
        ("cap_camps_per_minute", cap),
    ] {
        if let Some(v) = v {
            s[k] = json!(v);
        }
    }
    s
}

/// QUEST_VALUE_MODEL 4.3: V = Delta + (min(g * c, cap) - c_eff) * (R - tau). c_eff 3 (300 camps in 100 min), R = 8,
/// tau = 45 s = 0.75 min, so R - tau = 7.25. Delta 1, g 1.1:
///  - cap 3.2 binds: min(3.3, 3.2) = 3.2, V = 1 + 0.2 * 7.25 = 2.45;
///  - cap 10 does not: min(3.3, 10) = 3.3, V = 1 + 0.3 * 7.25 = 3.175.
///
/// At the reference rate 2.9 (c and c_eff both 2.9): min(3.19, 3.2) = 3.19, V = 1 + 0.29 * 7.25 = 3.1025.
#[test]
fn a_reusable_spell_with_every_number_gets_the_dividend_formula() {
    let r = report(300, json!([done("cd", 45.)]));
    let e = eval(&r, &spell_inputs(reusable(Some(1.), Some(1.1), Some(3.2))));
    let q = quest(&e, "cd");
    near(q.value, 2.45, "cap binds");
    near(q.value_at_reference, 3.1025, "at the reference rate");
    assert!(q.ratio.is_some() && q.verdict.is_some(), "{q:?}");
    let e = eval(&r, &spell_inputs(reusable(Some(1.), Some(1.1), Some(10.))));
    near(quest(&e, "cd").value, 3.175, "cap does not bind");

    // A single-use spell is its duel equivalent alone (4.2): flat in time and rate.
    let single = json!({ "kind": "spell", "spell_type": "single_use", "duel_equivalent_ce": 2.0 });
    let q = quest(&eval(&r, &spell_inputs(single)), "cd").clone();
    near(q.value, 2.0, "single-use value");
    near(
        q.value_at_reference,
        2.0,
        "single-use value at the reference rate",
    );
}

/// With any of the three numbers missing the value is not measured and the reason names that number.
#[test]
fn a_reusable_spell_missing_a_number_is_not_measured_and_says_which() {
    let r = report(300, json!([done("cd", 45.)]));
    let (d, g, c) = (Some(1.), Some(1.1), Some(3.2));
    let cases = [
        (reusable(None, g, c), "duel_equivalent_ce"),
        (reusable(d, None, c), "farm_rate_gain"),
        (reusable(d, g, None), "cap_camps_per_minute"),
        (reusable(None, None, None), "duel_equivalent_ce"),
        (reusable(d, None, None), "farm_rate_gain"),
    ];
    for (spell, missing) in cases {
        let e = eval(&r, &spell_inputs(spell.clone()));
        let q = quest(&e, "cd");
        assert!(
            q.value.is_none()
                && q.ratio.is_none()
                && q.verdict.is_none()
                && q.value_at_reference.is_none()
                && q.ratio_at_reference.is_none()
                && q.verdict_at_reference.is_none(),
            "{spell}: {q:?}"
        );
        assert!(
            q.not_measured.iter().any(|w| w.contains(missing)),
            "{spell}: the reason names {missing}: {:?}",
            q.not_measured
        );
    }
    // The price is still measured: only the value is missing.
    let e = eval(&r, &spell_inputs(reusable(None, None, None)));
    assert!(quest(&e, "cd").price.is_some() && quest(&e, "cd").price_at_reference.is_some());
}

fn native_augment(readout: Option<f64>) -> Value {
    let mut a = json!({ "fight_stacks_now": 0, "one_shot_stacks": 0, "farm_speed_pct": 0, "income_mult": 1,
                        "hazard_mult": 1, "cap_stacks": 40, "growth": "measured" });
    if let Some(x) = readout {
        a["readout_per_stack"] = json!(x);
    }
    a
}

/// A `"measured"` augment needs `readout_per_stack`, and the measured growth is divided by it. The card's readout goes from
/// 0 per holder at minute 5 to 56 at minute 12: 8 readout units a minute. With 4 readout units to a stack that is 2 stacks
/// a minute; ra = 12 - 5 = 7, so min(cap 40, 2 * 7) = 14 stacks, n = 14 / 12 * exp(-mu * ra) with mu = 0.06, and no
/// farm term (income 1, hazard 1, farm speed 0): V = 14 / 12 * exp(-0.42). Undivided it would be min(40, 56) = 40 stacks.
#[test]
fn a_measured_native_growth_is_converted_to_stacks_by_its_readout() {
    let native = json!({
        "5": { "card": { "holders": 2, "value_sum": 0. } },
        "12": { "card": { "holders": 2, "value_sum": 112. } }
    });
    let r = report_native(300, json!([]), native);
    let bad = inputs_with(
        json!({}),
        json!({}),
        json!({ "card": native_augment(None) }),
    );
    let err = evaluate_json(&r, &bad)
        .map(|_| ())
        .expect_err("measured needs readout_per_stack");
    assert!(
        err.contains("card") && err.contains("readout_per_stack"),
        "{err}"
    );
    let err = parse_inputs(&bad)
        .map(|_| ())
        .expect_err("parse refuses it too");
    assert!(
        err.contains("card") && err.contains("readout_per_stack"),
        "{err}"
    );

    let ok = inputs_with(
        json!({}),
        json!({}),
        json!({ "card": native_augment(Some(4.)) }),
    );
    let e = eval(&r, &ok);
    let a = e.augments.iter().find(|a| a.card == "card").expect("card");
    near(
        a.value,
        14. / 12. * (-0.42f64).exp(),
        "value from 2 stacks a minute",
    );

    let one = inputs_with(
        json!({}),
        json!({}),
        json!({ "card": native_augment(Some(1.)) }),
    );
    let e = eval(&r, &one);
    let a = e.augments.iter().find(|a| a.card == "card").unwrap();
    near(
        a.value,
        40. / 12. * (-0.42f64).exp(),
        "a readout of 1 per stack: 8 stacks a minute, capped at 40",
    );

    let zero = inputs_with(
        json!({}),
        json!({}),
        json!({ "card": native_augment(Some(0.)) }),
    );
    assert!(
        parse_inputs(&zero).is_err(),
        "readout_per_stack must be above 0"
    );
}

/// The augment's value over the elite's cost at the reference rate: with income 1.5 and hazard 1.5 and no stacks the whole
/// value is the farm term, c * (1.5 * a_bar(1.5 mu) - a_bar(mu)), which is linear in c.
#[test]
fn an_augment_row_is_computed_at_both_rates() {
    let a = json!({ "fight_stacks_now": 0, "one_shot_stacks": 0, "farm_speed_pct": 0, "income_mult": 1.5,
                    "hazard_mult": 1.5, "cap_stacks": 0, "growth": "none" });
    let r = report(62, json!([]));
    let e = eval(
        &r,
        &inputs_with(reference(), json!({}), json!({ "marked": a })),
    );
    let m = e.augments.iter().find(|x| x.card == "marked").unwrap();
    near(
        m.value_at_reference,
        m.value.expect("value") * REF / 0.62,
        "a pure farm term scales with the rate",
    );
    assert!(m.ratio_solo_at_reference.is_some() && m.ratio_duo_at_reference.is_some());
    assert!(m.ratio_trio_at_reference.is_some() && m.meets_rule_at_reference.is_some());
    let needed = e
        .needed_at_reference
        .as_ref()
        .expect("the elite's cost at the reference rate");
    assert!(needed.solo.is_some() && needed.duo.is_some() && needed.trio.is_some());
}

/// A number a kind does not read cannot be supplied to it (the reward is an enum by kind). The controls keep the test from
/// passing on a parser that refuses everything.
#[test]
fn a_number_of_another_kind_is_an_error() {
    let parse = |reward: Value| {
        parse_inputs(&inputs_with(
            json!({}),
            json!({ "q": { "reward": reward } }),
            json!({}),
        ))
        .map(|_| ())
    };
    let mut ok = buff(10., 0.1);
    ok["source"] = json!("a source line");
    parse(ok).expect("a permanent buff with its source parses");
    parse(reusable(Some(1.), Some(1.1), Some(3.2))).expect("a complete reusable spell parses");
    parse(json!({ "kind": "spell", "spell_type": "single_use", "duel_equivalent_ce": 2.0 }))
        .expect("a single-use spell parses");

    let with = |mut base: Value, field: &str| {
        base[field] = json!(1.0);
        base
    };
    let cases = [
        (
            with(buff(10., 0.1), "duel_equivalent_ce"),
            "duel_equivalent_ce",
        ),
        (with(buff(10., 0.1), "farm_rate_gain"), "farm_rate_gain"),
        (
            with(buff(10., 0.1), "cap_camps_per_minute"),
            "cap_camps_per_minute",
        ),
        (
            with(reusable(Some(1.), Some(1.1), Some(3.2)), "direct_stacks"),
            "direct_stacks",
        ),
        (
            with(reusable(Some(1.), Some(1.1), Some(3.2)), "damage_fraction"),
            "damage_fraction",
        ),
        (
            with(reusable(Some(1.), Some(1.1), Some(3.2)), "upfront_stacks"),
            "upfront_stacks",
        ),
        (
            with(
                json!({ "kind": "spell", "spell_type": "single_use" }),
                "farm_rate_gain",
            ),
            "farm_rate_gain",
        ),
        (
            with(
                json!({ "kind": "spell", "spell_type": "single_use" }),
                "cap_camps_per_minute",
            ),
            "cap_camps_per_minute",
        ),
    ];
    for (reward, field) in cases {
        let err = parse(reward.clone()).expect_err(&format!("{reward} must be refused"));
        assert!(
            err.contains(field) && err.contains('q'),
            "{reward}: the error names quest and {field}: {err}"
        );
    }
}

fn dry_run() -> String {
    let path = concat!(
        env!("CARGO_MANIFEST_DIR"),
        "/tests/fixtures/balance_pilot_dry_report.json"
    );
    std::fs::read_to_string(path).expect("the dry-run fixture exists")
}

fn shipped() -> String {
    let path = concat!(
        env!("CARGO_MANIFEST_DIR"),
        "/../../tools/balance-lab/inputs.json"
    );
    std::fs::read_to_string(path).expect("inputs.json exists")
}

/// The shipped inputs carry the reference rate (2.9, with its source) and 20 upfront stacks on `the_bargain`; on the dry run
/// (c_eff 0.618) the four permanent quests that had a verdict print both rates in the JSON and the table.
/// Dry-run values before this packet: even_skeletons 7.99, tuber_eats 7.39, let_him_cook 1.37, the_bargain 109.4.
/// `the_bargain` after the cost: value 2.230 - 20/12 = 0.563 over a price of 0.0204, about 27.6; at 2.9 about 14.7. Still
/// AboveBand: its 1.7 s completion time makes the price meaningless (a remaining gap named in the packet).
#[test]
fn the_shipped_inputs_print_both_rates_on_the_dry_run() {
    let text = shipped();
    parse_inputs(&text).expect("the shipped inputs parse");
    let v: Value = serde_json::from_str(&text).unwrap();
    assert_eq!(
        v["reference_farm_rate"]["camps_per_minute"].as_f64(),
        Some(2.9)
    );
    assert!(
        v["reference_farm_rate"]["source"]
            .as_str()
            .is_some_and(|s| s.contains("QUEST_VALUE_MODEL")),
        "the reference rate names its source line in docs/QUEST_VALUE_MODEL.md"
    );
    assert_eq!(
        v["quests"]["the_bargain"]["reward"]["upfront_stacks"].as_f64(),
        Some(20.)
    );

    let e = eval(&dry_run(), &text);
    let c = e.measured.c_eff.expect("c_eff");
    assert!(
        (c - 0.618).abs() < 0.001,
        "the fixture's measured rate is 0.618, got {c}"
    );
    near(e.reference_farm_rate, 2.9, "reference rate in the output");
    let table = render_table(&e);
    let header = table
        .lines()
        .find(|l| l.contains("reference") && l.contains("measured"))
        .unwrap_or_else(|| panic!("a header line names both rates:\n{table}"));
    assert!(
        header.contains("2.900") && header.contains(&format!("{c:.3}")),
        "{header}"
    );

    for id in [
        "even_skeletons_get_cold",
        "tuber_eats",
        "let_him_cook",
        "the_bargain",
    ] {
        let q = quest(&e, id);
        let (ratio, at_ref) = (q.ratio.expect(id), q.ratio_at_reference.expect(id));
        assert!(
            q.verdict.is_some() && q.verdict_at_reference.is_some(),
            "{id}: {q:?}"
        );
        assert!(
            at_ref < ratio,
            "{id}: the reference rate is higher, so the ratio falls: {ratio} -> {at_ref}"
        );
        near(
            q.price_at_reference,
            q.price.unwrap() * 2.9 / c,
            &format!("{id}: price at 2.9"),
        );
        let row = table
            .lines()
            .find(|l| l.starts_with(id))
            .unwrap_or_else(|| panic!("{id} has a row"));
        let (a, b) = (format!("{ratio:.3}"), format!("{at_ref:.3}"));
        assert!(
            row.contains(&a) && row.contains(&b),
            "{id}: the row prints {a} and {b}: {row}"
        );
    }
    near(
        quest(&e, "even_skeletons_get_cold").ratio_at_reference,
        quest(&e, "even_skeletons_get_cold").ratio.unwrap() * c / 2.9,
        "no dividend: the ratio falls by exactly c / 2.9",
    );
    let b = quest(&e, "the_bargain");
    let r = b.ratio.unwrap();
    assert!(
        r > 20. && r < 35.,
        "the_bargain net of its cost, about 27.6 (was 109.4): {r}"
    );
    let rr = b.ratio_at_reference.unwrap();
    assert!(
        rr > 10. && rr < 20.,
        "the_bargain at the reference rate, about 14.7: {rr}"
    );
}
