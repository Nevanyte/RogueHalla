//! Strict native intake and comparison for the sealed direct feature-aware hand source supplement
//! (`parity/policy/bot-private-brain/source.json`, format `rh-bot-private-brain-source/1`, `handSupplement`).
//!
//! Root source release: independent fresh pinned reproduction of source `6aaa4c6d` released the frozen corpus through
//! cherry-pick `90b191b3` (source bytes pinned by `docs/evidence/bot-private-brain-source/README.md`; independent review
//! `docs/evidence/bot-private-brain-source-review/README.md`). The supplement was produced by the actual unchanged
//! `createHumanizer` (`tools/bot-private-brain-fixtures/hand.mts`): six feature profiles, two real submit→later-release
//! operations each, twelve operations in total, with the raw `motor`/`lapse` draw sequence, released commands, misses,
//! stats and continued draw counts recorded from real source.
//!
//! This reader is strict native intake, not a reimplementation of the expected answers: it decodes the declared inputs
//! (feature profile, full hard tier, seeds, open map, percepts, intents), validates every consumed source fact and leaves
//! the hand to compute the results. The expected traces/commands/stats are compared exactly (continuous geometry and delay
//! leaves at the same 1e-9 relative tolerance the admitted bot-core hand parity uses; every discrete value and every draw
//! is exact). Nothing here is generated from native answers, and no source expected value is installed as hand state.
//!
//! Boundary: this is the narrow hand lease of `docs/backlog/packets/P-BOT-HAND-FEATURE-1.md`. The declared `memory`
//! snapshots are a side observation of the real Memory the source supplement also constructed; the hand itself never reads
//! them and the admitted Memory component owns its own witnesses, so the reader validates their declared shape and the
//! hand comparison does not compare them. No full Brain composition, fair observation binding or live activation is implied.

use super::{Context, Humanizer, HumanizerStats};
use crate::bots::clicks::BotMap;
use crate::bots::random::Rng;
use crate::bots::types::{
    Appraisal, Attention, BotWeapon, Cadence, Caution, ChooseMsg, ClickMiss, Command,
    ContainerView, ExGaussian, FighterView, Hesitation, HudView, Intent, IntentBody, IntentKind,
    IntentTiming, MobView, MoveScatter, Orbwalk, OwnerLock, Percept, PerceptExtras, PlanMsg, Route,
    SelfView, Stimulus, StimulusKind, TargetScatter, TierId, TierParams, TimedCommand,
    COMMAND_TYPES,
};
use crate::bots::view::Features;
use rh_sim::{FactionId, Vec2};
use serde_json::Value;

/// The sealed corpus is read from the worktree at test run time (22 MB: too large to embed in the test binary).
fn load_source() -> Value {
    let path = std::path::Path::new(env!("CARGO_MANIFEST_DIR"))
        .join("../../parity/policy/bot-private-brain/source.json");
    let text = std::fs::read_to_string(&path)
        .unwrap_or_else(|error| panic!("read sealed hand source {}: {error}", path.display()));
    serde_json::from_str(&text).expect("sealed hand source.json is valid JSON")
}

fn err(path: &str, message: impl Into<String>) -> String {
    format!("{path}: {}", message.into())
}

#[path = "source/mv.rs"]
mod mv;
use self::mv::*;

#[path = "source/model.rs"]
mod model;
use self::model::*;

#[path = "source/corpus.rs"]
mod corpus;
use self::corpus::*;

#[path = "source/tier.rs"]
mod tier;
use self::tier::*;

#[path = "source/percept.rs"]
mod percept;
use self::percept::*;

#[path = "source/intent.rs"]
mod intent;
use self::intent::*;

#[path = "source/compare.rs"]
mod compare;
use self::compare::*;

// --------------------------------------------------------------------------------------------------------------- tests

#[test]
fn sealed_source_hand_supplement_matches_all_profiles() {
    let source = load_source();
    let corpus = read_hand_corpus(&source)
        .unwrap_or_else(|error| panic!("strict sealed hand source reader: {error}"));
    assert_eq!(
        corpus.map_hash_u32, 696510049,
        "the declared open arena map hash pin"
    );
    assert_eq!(
        corpus.profiles.len(),
        PROFILE_ORDER.len(),
        "direct hand profile count"
    );
    let hard = TierParams::by_id(TierId::Hard);
    let mut operations = 0usize;
    for profile in &corpus.profiles {
        assert_eq!(
            profile.tier, *hard,
            "{}: declared full tier differs from the admitted TIERS table",
            profile.name
        );
        assert_ne!(
            profile.seeds.decide, profile.seeds.motor,
            "{}: distinct declared runtime streams",
            profile.name
        );
        let mut hand = match &profile.features {
            None => Humanizer::new(hard, corpus.map.clone()),
            Some(features) => Humanizer::with_features(hard, corpus.map.clone(), features),
        };
        let mut motor = Rng::new(profile.seeds.motor);
        let mut lapse = Rng::new(profile.seeds.lapse);
        let mut replay = [Rng::new(profile.seeds.motor), Rng::new(profile.seeds.lapse)];
        let mut drawn = Counts::default();
        for (index, operation) in profile.operations.iter().enumerate() {
            let label = format!("{} operation {index}", profile.name);
            {
                let mut ctx = Context {
                    p: &operation.percept,
                    engaged: false,
                    motor: &mut motor,
                    lapse: &mut lapse,
                };
                hand.submit(&operation.intents, &mut ctx);
            }
            check_phase(
                &label,
                "submit",
                &operation.submit_trace,
                &mut replay,
                &[motor.clone(), lapse.clone()],
                &mut drawn,
            )
            .unwrap_or_else(|error| panic!("{label}: {error}"));
            let released = {
                let mut ctx = Context {
                    p: &operation.release_percept,
                    engaged: false,
                    motor: &mut motor,
                    lapse: &mut lapse,
                };
                hand.release(&mut ctx)
            };
            check_phase(
                &label,
                "release",
                &operation.release_trace,
                &mut replay,
                &[motor.clone(), lapse.clone()],
                &mut drawn,
            )
            .unwrap_or_else(|error| panic!("{label}: {error}"));
            assert_eq!(
                drawn, operation.counts,
                "{label}: continued draw counts after the real release"
            );
            let misses = hand.drain_misses();
            compare_commands(&released, &operation.result, &label);
            compare_misses(&misses, &operation.misses, &label);
            compare_stats(hand.stats(), &operation.stats, &label);
            assert_eq!(
                operation.memory_keys,
                vec![
                    "fighters".to_string(),
                    "mobs".to_string(),
                    "camps".to_string(),
                    "containers".to_string(),
                    "self".to_string(),
                    "stimuli".to_string(),
                    "bounties".to_string(),
                    "choices".to_string(),
                ],
                "{label}: declared real-Memory side snapshot shape"
            );
            operations += 1;
            // The declared Knowledge stream belongs to the companion Brain witness; the direct hand never draws it,
            // but the strict reader still requires and preserves the declared seed.
            let _knowledge_seed = profile.seeds.knowledge;
        }
    }
    assert_eq!(
        operations, 12,
        "6 direct profiles x 2 real submit/release calls"
    );
}

/// Malformed declared intake is refused by the strict reader before any hand is constructed or any draw can happen; the
/// expected source traces are never used as setup or repaired to fit a native result.
#[test]
fn sealed_source_hand_reader_rejects_malformed_declarations() {
    let source = load_source();
    let envelope = hand_envelope(&source);
    assert!(
        read_hand_corpus(&envelope).is_ok(),
        "the unmodified hand envelope reads"
    );
    let mutations: Vec<CorpusMutation> = vec![
        (
            "wrong source format",
            Box::new(|value| value["format"] = Value::from("rh-bot-private-brain-source/2")),
        ),
        (
            "wrong method-value format",
            Box::new(|value| value["methodValue"] = Value::from("rh-method-value/2")),
        ),
        (
            "missing direct hand supplement",
            Box::new(|value| {
                value.as_object_mut().unwrap().remove("handSupplement");
            }),
        ),
        (
            "renamed profile",
            Box::new(|value| {
                value["handSupplement"][0]["profile"] = Value::from("absent-features");
            }),
        ),
        (
            "extra profile member",
            Box::new(|value| {
                value["handSupplement"][0]["unexpected"] = Value::from(true);
            }),
        ),
        (
            "nonfinite declared clock",
            Box::new(|value| {
                tagged_set(
                    &mut value["handSupplement"][0]["operations"][0]["percept"],
                    "now",
                    serde_json::json!({"$": "number", "value": "+Infinity"}),
                );
            }),
        ),
        (
            "unknown tier id",
            Box::new(|value| {
                tagged_set(
                    &mut value["handSupplement"][0]["tier"],
                    "id",
                    Value::from("nightmare"),
                );
            }),
        ),
        (
            "unknown rng lane",
            Box::new(|value| {
                tagged_set(
                    &mut value["handSupplement"][0]["operations"][0]["submitTrace"]["values"][0],
                    "lane",
                    Value::from("decide"),
                );
            }),
        ),
        (
            "skipped rng index",
            Box::new(|value| {
                tagged_set(
                    &mut value["handSupplement"][0]["operations"][0]["submitTrace"]["values"][0],
                    "index",
                    Value::from(7),
                );
            }),
        ),
        (
            "unknown intent kind",
            Box::new(|value| {
                tagged_set(
                    &mut value["handSupplement"][0]["operations"][0]["intents"]["values"][0],
                    "kind",
                    Value::from("teleport"),
                );
            }),
        ),
        (
            "missing operation member",
            Box::new(|value| {
                value["handSupplement"][0]["operations"][0]
                    .as_object_mut()
                    .unwrap()
                    .remove("stats");
            }),
        ),
        (
            "short profile lifetime",
            Box::new(|value| {
                value["handSupplement"][0]["operations"]
                    .as_array_mut()
                    .unwrap()
                    .pop();
            }),
        ),
        (
            "memory side snapshot loses a declared field",
            Box::new(|value| {
                value["handSupplement"][0]["operations"][0]["memory"]["entries"]
                    .as_array_mut()
                    .unwrap()
                    .remove(0);
            }),
        ),
    ];
    for (label, mutate) in mutations {
        let mut mutated = envelope.clone();
        mutate(&mut mutated);
        assert!(
            read_hand_corpus(&mutated).is_err(),
            "strict reader accepted malformed declaration: {label}"
        );
    }
}

/// One malformed-declaration control: a label and an in-memory corpus mutation.
type CorpusMutation = (&'static str, Box<dyn Fn(&mut Value)>);

/// Replace the tagged `key` member of a tagged object in place (test-only corpus mutation).
fn tagged_set(object: &mut Value, key: &str, replacement: Value) {
    let entries = object["entries"]
        .as_array_mut()
        .expect("tagged object entries");
    for pair in entries {
        if pair[0].as_str() == Some(key) {
            pair[1] = replacement;
            return;
        }
    }
    panic!("tagged object has no member {key}");
}

/// The tiny envelope the strict reader is handed: the two format tags, the open map and the direct hand profiles. The
/// rest of the 22 MB corpus (Brain histories, coverage, knowledge dependencies) is not the hand lease's input.
fn hand_envelope(source: &Value) -> Value {
    let open = source["maps"]
        .as_array()
        .expect("maps array")
        .iter()
        .find(|entry| entry.get("id").and_then(Value::as_str) == Some("open"))
        .expect("open map entry")
        .clone();
    serde_json::json!({
        "format": source["format"].clone(),
        "methodValue": source["methodValue"].clone(),
        "maps": [open],
        "handSupplement": source["handSupplement"].clone(),
    })
}
