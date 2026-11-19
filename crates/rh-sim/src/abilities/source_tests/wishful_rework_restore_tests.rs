//! P-AUG-TUNE-1 oracle (restore half): the Wishful Thinking trim bound in `abilities/checkpoint.rs` follows the world's
//! rule. The base state is the frozen corpus case `arcane-overload-wishful-stock-trim` (Fire, Arcane Overload +
//! Wishful Thinking, the Wish cast twice), played tick by tick under Legacy and under Rework
//! (`config.augments: "rework"`, read by `setup.rs`); its trims are then rewritten in the checkpoint JSON.
//!
//! Legacy bound (today): each trim and the in-window sum at most `WISHFUL_BUDGET_MS` (30 000). Rework bound: the same
//! reading with the budget the Rework table gives, 20% of the cast's full cooldown.
use super::*;
use serde_json::json;

const CASE: &str = "arcane-overload-wishful-stock-trim";

/// The case played tick by tick (no capture comparison: the Rework numbers differ from the capture's) under `rework`,
/// as a checkpoint JSON, with the last cast's full cooldown.
fn base(rework: bool) -> (V, f64) {
    let corpus = corpus();
    let case = corpus["cases"]
        .as_array()
        .unwrap()
        .iter()
        .find(|c| c["name"] == CASE)
        .unwrap();
    let mut input = case["input"].clone();
    if rework {
        input["config"] = json!({"augments": "rework"});
    }
    let mut s = construct(&input);
    for a in input["actions"].as_array().unwrap() {
        for i in a["inputs"].as_array().into_iter().flatten() {
            enqueue(&mut s, i);
        }
        for _ in 0..a["count"].as_u64().unwrap_or(1) {
            s.step();
        }
    }
    let v = serde_json::to_value(&s).unwrap();
    assert!(Sim::restore(v.clone()).is_ok(), "the played state restores");
    let cd = v
        .pointer("/world/players/0/card_runtime/wish_cast/cooldown_ms")
        .and_then(V::as_f64)
        .expect("the Wish was cast");
    (v, cd)
}

/// The checkpoint with the player's trims replaced by `(at, ms)` pairs.
fn with(mut v: V, trims: &[(f64, f64)]) -> V {
    *v.pointer_mut("/world/players/0/card_trims").unwrap() = json!(trims
        .iter()
        .map(|(at, ms)| json!({"at": at, "ms": ms}))
        .collect::<Vec<_>>());
    v
}

fn restores(v: V) -> bool {
    match Sim::restore(v) {
        Ok(_) => true,
        Err(e) => {
            println!("REFUSED: {e:?}");
            false
        }
    }
}

fn trims_of(v: &V) -> Vec<f64> {
    v.pointer("/world/players/0/card_trims")
        .unwrap()
        .as_array()
        .unwrap()
        .iter()
        .map(|t| t["ms"].as_f64().unwrap())
        .collect()
}

/// Played for real under Rework: each hit's cut is 1.5% of the cast's full cooldown, and the state restores.
#[test]
fn a_played_rework_world_trims_a_share_of_the_full_cooldown_and_restores() {
    let (v, cd) = base(true);
    let trims = trims_of(&v);
    assert!(!trims.is_empty(), "the Wishful Thinking producer trimmed");
    for ms in trims {
        assert_eq!(ms, 0.015 * cd, "1.5% of {cd}");
    }
    let (lv, _) = base(false);
    let legacy = trims_of(&lv);
    assert!(!legacy.is_empty());
    for ms in legacy {
        assert_eq!(ms, 1000., "Legacy: 1 s a hit");
    }
}

/// Rework: trims at 20% of the full cooldown restore.
#[test]
fn rework_checkpoint_restores_trims_at_the_twenty_percent_cap() {
    let (v, cd) = base(true);
    let cap = 0.2 * cd;
    assert!(
        restores(with(v.clone(), &[(150., cap)])),
        "one trim at the cap"
    );
    assert!(
        restores(with(v, &[(100., cap / 2.), (150., cap / 2.)])),
        "two trims summing to the cap"
    );
}

/// Rework: one trim past 20% of the full cooldown is refused.
#[test]
fn rework_checkpoint_refuses_one_trim_past_the_cap() {
    let (v, cd) = base(true);
    assert!(!restores(with(v, &[(150., 0.2 * cd + 50.)])));
}

/// Rework: trims inside one window summing past 20% are refused.
#[test]
fn rework_checkpoint_refuses_a_window_sum_past_the_cap() {
    let (v, cd) = base(true);
    let half = 0.1 * cd + 50.;
    assert!(!restores(with(v, &[(100., half), (150., half)])));
}

/// The same numbers are inside Legacy's 30 000 ms budget: Legacy is bounded exactly as before.
#[test]
fn legacy_checkpoint_bound_is_unchanged() {
    let (v, cd) = base(false);
    let budget = crate::constants::WISHFUL_BUDGET_MS;
    assert!(restores(with(v.clone(), &[(150., 0.2 * cd + 50.)])));
    assert!(restores(with(v.clone(), &[(100., 6050.), (150., 6050.)])));
    assert!(restores(with(v.clone(), &[(150., budget)])));
    assert!(!restores(with(v.clone(), &[(150., budget + 50.)])));
    assert!(
        !restores(with(v, &[(100., 15_050.), (150., 15_000.)])),
        "a Legacy window sum past 30 000"
    );
}
