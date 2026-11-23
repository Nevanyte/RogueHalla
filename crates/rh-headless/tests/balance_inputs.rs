//! Oracle for packet P-BAL-INPUTS-1 (docs/backlog/packets/P-BAL-INPUTS-1.md): the shipped
//! `tools/balance-lab/inputs.json` carries a reward descriptor for every Top 25 quest and an entry for every native augment.
//!
//! Loose proof (owner direction 2026-11-10): the evaluator, fed the shipped inputs and a hand-built report with the
//! workbook's own calibration (c_eff 3, kappa 0.6, mu 0.06, coliseum at minute 12), reproduces the workbook's value cells
//! within 10%. Sources: `docs/quests/FUTURE_ONLY_Quest_Design_Discussion.xlsx` sheet `Top_25` (column AF, "Value V (CE)")
//! and sheet `Augment_Tuning` (column S, "New value (CE)"; its columns H to N are the transcribed numbers).
//! Spell rewards have no value until P-BAL-LAB-2 (a duel equivalence), so they must stay "not measured".
use rh_headless::balance_model::{evaluate_json, Evaluation};
use rh_sim::cards::CardId;
use rh_sim::quests::board::catalog::{RewardKind, DEFINITIONS};
use serde_json::{json, Value};

const TOL: f64 = 0.10;

fn shipped() -> (String, Value) {
    let path = concat!(
        env!("CARGO_MANIFEST_DIR"),
        "/../../tools/balance-lab/inputs.json"
    );
    let text = std::fs::read_to_string(path).expect("tools/balance-lab/inputs.json exists");
    let v: Value = serde_json::from_str(&text).expect("the shipped inputs are json");
    (text, v)
}

fn object<'a>(v: &'a Value, key: &str) -> &'a serde_json::Map<String, Value> {
    v[key]
        .as_object()
        .unwrap_or_else(|| panic!("inputs.json has no {key} object"))
}

/// The lab report of the workbook's calibration: 300 camps in 100 player-minutes (c_eff 3), kill 12 s, heal 4 s, walk 4 s
/// (kappa 0.6), 6 deaths (mu 0.06), 12 camp-equivalents banked at minute 5. `quests` and `accepted` are as given.
fn report(quests: Value, accepted: Value) -> String {
    let secs = |s: f64| json!({ "sum": s * 100., "n": 100 });
    json!({
        "schema": "balance-lab/1",
        "world": {
            "camps_cleared": 300, "camps_credited": 300,
            "kill_secs": secs(12.), "heal_secs": secs(4.), "walk_secs": secs(4.),
            "player_deaths": { "total": 6,
                "by_minute": { "0": { "player_ticks": 100 * 1200, "total": 6 } } },
            "minutes": { "5": { "samples": 8, "farm_stacks": { "ember_imp": 8 * 144 } } }
        },
        "quests": { "by_quest": quests, "accepted_by_minute": accepted },
        "augments": { "by_card": [] }
    })
    .to_string()
}

/// Ten attempts, ten completions, each `tau_secs` long.
fn completed(id: &str, tau_secs: f64) -> Value {
    json!({ "quest": id, "dealt": 10, "accepted": 10, "attempts": 10, "completed": 10,
            "ended_timer": 0, "ended_death": 0, "abandoned": 0,
            "complete_secs": { "sum": tau_secs * 10., "n": 10 } })
}

fn evaluate(report: &str, inputs_text: &str) -> Evaluation {
    evaluate_json(report, inputs_text).expect("the shipped inputs evaluate")
}

fn close(got: Option<f64>, want: f64, what: &str) {
    let got = got.unwrap_or_else(|| panic!("{what}: expected about {want}, got not-measured"));
    assert!(
        (got - want).abs() <= want.abs() * TOL,
        "{what}: expected {want} within 10%, got {got}"
    );
}

/// Every quest of the sim's Top 25 catalog has a reward descriptor of the right kind and a source; every native augment
/// has an entry; nothing else is named.
#[test]
fn every_quest_and_native_augment_has_an_entry() {
    let (_, v) = shipped();
    let quests = object(&v, "quests");
    let want: Vec<&str> = DEFINITIONS.iter().map(|d| d.id).collect();
    assert_eq!(want.len(), 25);
    let mut have: Vec<&str> = quests.keys().map(String::as_str).collect();
    let mut sorted = want.clone();
    sorted.sort_unstable();
    have.sort_unstable();
    assert_eq!(
        have, sorted,
        "inputs.json quests are exactly the 25 catalog ids"
    );

    for d in DEFINITIONS.iter() {
        let q = &quests[d.id];
        let r = &q["reward"];
        let kind = r["kind"].as_str().unwrap_or("");
        let spell_type = r["spell_type"].as_str();
        match d.reward.kind() {
            RewardKind::Permanent => {
                assert_eq!(kind, "permanent_buff", "{}: a permanent reward", d.id);
                assert!(
                    r["direct_stacks"].as_f64().is_some_and(|x| x > 0.),
                    "{}",
                    d.id
                );
                assert!(r["damage_fraction"].as_f64().is_some(), "{}", d.id);
            }
            RewardKind::SingleUse | RewardKind::Reusable => {
                assert_eq!(kind, "spell", "{}: a spell reward", d.id);
                assert_eq!(
                    spell_type,
                    Some(if d.reward.kind() == RewardKind::SingleUse {
                        "single_use"
                    } else {
                        "reusable"
                    }),
                    "{}: spell_type follows the catalog",
                    d.id
                );
                assert!(
                    r.get("duel_equivalent_ce").is_none(),
                    "{}: no duel equivalence exists yet; none may be invented",
                    d.id
                );
            }
        }
        assert!(
            r["source"].as_str().is_some_and(|s| !s.trim().is_empty()),
            "{}: every reward names its workbook source",
            d.id
        );
        // The timer verdict applies to route quests only.
        assert_eq!(
            q.get("difficulty").is_some(),
            d.route_timer_ms.is_some(),
            "{}: a difficulty class exactly when the quest has a route timer",
            d.id
        );
    }

    let augments = object(&v, "augments");
    let mut want: Vec<&str> = CardId::NATIVE.iter().map(|c| c.as_str()).collect();
    assert_eq!(want.len(), 14);
    let mut have: Vec<&str> = augments.keys().map(String::as_str).collect();
    want.sort_unstable();
    have.sort_unstable();
    assert_eq!(
        have, want,
        "inputs.json augments are exactly the 14 native cards"
    );
    for (id, a) in augments {
        assert_eq!(
            a["growth"], "none",
            "{id}: design stacks, no measured growth (brief decision 3)"
        );
        assert!(
            a["source"].as_str().is_some_and(|s| !s.trim().is_empty()),
            "{id}: every augment names its workbook source"
        );
    }
}

/// With completions for every quest, each non-spell quest has a value and a ratio; each spell quest says why it has none.
#[test]
fn every_non_spell_quest_gets_a_value() {
    let (text, v) = shipped();
    let quests: Vec<Value> = DEFINITIONS.iter().map(|d| completed(d.id, 45.)).collect();
    let r = report(json!(quests), json!({ "4": 250 }));
    let e = evaluate(&r, &text);
    assert_eq!(e.quests.len(), 25, "one verdict per catalog quest");
    for q in &e.quests {
        let kind = v["quests"][&q.quest]["reward"]["kind"]
            .as_str()
            .unwrap_or("");
        if kind == "spell" {
            assert!(
                q.value.is_none() && q.ratio.is_none() && q.verdict.is_none(),
                "{}: a spell has no value without a duel equivalence: {q:?}",
                q.quest
            );
            assert!(
                q.not_measured.iter().any(|w| w.contains("duel")),
                "{}: the reason names the duel equivalence: {:?}",
                q.quest,
                q.not_measured
            );
        } else {
            assert_eq!(kind, "permanent_buff", "{}", q.quest);
            assert!(
                q.value.is_some_and(|x| x.is_finite() && x > 0.),
                "{}: {q:?}",
                q.quest
            );
            assert!(
                q.ratio.is_some_and(|x| x.is_finite() && x > 0.),
                "{}: {q:?}",
                q.quest
            );
            assert!(q.verdict.is_some(), "{}", q.quest);
        }
    }
}

/// Workbook `Top_25`: (quest id, pick-up minute N, tau minutes O, direct stacks = b S x 12, damage d U, value V AF).
/// Rows 6, 15, 16, 19, 27, 28, 29. c_eff 3, kappa 0.6 and T = 12 are the workbook's `Inputs` C14, C12, C16.
const PERMANENT: [(&str, u32, f64, f64, f64, f64); 7] = [
    ("the_burning_crown", 4, 0.8, 48., 0.10, 5.246),
    ("the_bargain", 4, 0.1, 24., 0.05, 2.697),
    ("trade_up", 3, 1.25, 54., 0.08, 5.581),
    ("let_him_cook", 3, 1.25, 36., 0.05, 3.684),
    ("feed_or_hunt", 2, 0.5, 6., 0.06, 1.502),
    ("even_skeletons_get_cold", 3, 0.42, 18., 0.0, 1.5),
    ("tuber_eats", 4, 0.6, 18., 0.05, 2.153),
];

#[test]
fn permanent_rewards_match_the_workbook_values() {
    let (text, v) = shipped();
    for (id, pickup, tau, stacks, d, want) in PERMANENT {
        let r = &v["quests"][id]["reward"];
        assert_eq!(
            r["direct_stacks"].as_f64(),
            Some(stacks),
            "{id}: direct_stacks (b x 12)"
        );
        assert_eq!(
            r["damage_fraction"].as_f64(),
            Some(d),
            "{id}: damage_fraction"
        );
        assert_eq!(
            r["move_fraction"].as_f64().unwrap_or(0.),
            0.,
            "{id}: no move bonus"
        );
        let rep = report(
            json!([completed(id, tau * 60.)]),
            json!({ pickup.to_string(): 10 }),
        );
        let e = evaluate(&rep, &text);
        let q = e
            .quests
            .iter()
            .find(|q| q.quest == id)
            .expect("quest evaluated");
        close(q.value, want, &format!("{id}: value V (Top_25 AF)"));
    }
}

/// Workbook `Augment_Tuning` rows (the numbers the game ships): (card id, fight stacks now or stacks by coliseum O,
/// cap J, one-shot K, farm speed % L, income M, hazard N, new value S).
type AugRow = (&'static str, f64, f64, f64, f64, f64, f64, f64);
const AUGMENTS: [AugRow; 14] = [
    ("markedMan", 0., 0., 0., 0., 1.5, 1.5, 6.2228),
    ("marksmansTally", 30., 30., 0., 6., 1., 1., 2.2455),
    ("eighthStrike", 12.5, 0., 0., 12.5, 1., 1., 1.9093),
    ("calluses", 23.8, 24., 0., 0., 1., 1., 1.3031),
    ("bastion", 14., 0., 0., 4., 1., 1., 1.1716),
    ("hazardWalker", 15., 15., 0., 2., 1., 1., 1.0254),
    ("glacier", 16.8, 24., 0., 0., 1., 1., 0.9199),
    ("stoked", 14., 30., 0., 1., 1., 1., 0.8690),
    ("corpseHoarder", 12.6, 24., 0., 0., 1., 1., 0.6899),
    ("overflow", 6., 0., 0., 3., 1., 1., 0.6335),
    ("wishEngine", 8., 8., 0., 1., 1., 1., 0.5405),
    ("executionersLedger", 8.4, 30., 0., 0., 1., 1., 0.4599),
    ("trophyHunter", 7., 15., 0., 0., 1., 1., 0.3833),
    ("pathfinder", 48., 48., 0., 6., 1., 1., 3.2310),
];

#[test]
fn native_augments_match_the_workbook_values() {
    let (text, v) = shipped();
    let e = evaluate(&report(json!([]), json!({})), &text);
    for (id, fight, cap, one_shot, farm, income, hazard, want) in AUGMENTS {
        let a = &v["augments"][id];
        let num = |k: &str, default: f64| a[k].as_f64().unwrap_or(default);
        assert_eq!(
            num("fight_stacks_now", f64::NAN),
            fight,
            "{id}: fight_stacks_now"
        );
        assert_eq!(num("cap_stacks", f64::NAN), cap, "{id}: cap_stacks");
        assert_eq!(
            num("one_shot_stacks", 0.),
            one_shot,
            "{id}: one_shot_stacks"
        );
        assert_eq!(
            num("farm_speed_pct", f64::NAN),
            farm,
            "{id}: farm_speed_pct"
        );
        assert_eq!(num("income_mult", 1.), income, "{id}: income_mult");
        assert_eq!(num("hazard_mult", 1.), hazard, "{id}: hazard_mult");
        let got = e.augments.iter().find(|x| x.card == id);
        let got = got.unwrap_or_else(|| panic!("{id} is missing from the evaluation"));
        close(got.value, want, &format!("{id}: value (Augment_Tuning S)"));
        assert!(
            got.ratio_solo.is_some() && got.ratio_duo.is_some() && got.ratio_trio.is_some(),
            "{id}: priced against the elite: {got:?}"
        );
    }
    // Augment_Tuning V5: after the nerf Marked Man is still "Worth it with one helper" (value 6.22, needed duo 3.52).
    let mm = e.augments.iter().find(|x| x.card == "markedMan").unwrap();
    assert!(
        mm.ratio_duo.is_some_and(|r| r >= 0.85),
        "Marked Man is worth it with one helper: {mm:?}"
    );
}
