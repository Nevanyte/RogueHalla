//! P-RAID-V oracle (loose proof, docs/backlog/packets/P-RAID-V.md): the value table has a row per board quest def and per
//! Rework-pool augment (catalog and native), the Rust slices equal `data/raid_values.json`, an unknown key takes the
//! flat default, `farm_only` clears every row, and a doctored table changes the accessors. The numbers are the design
//! workbook's estimates of 2026-11-22 (camp-equivalents); a later number packet amends `REWARDS` and `AUGMENTS` here.
use super::values::Prices;
use rh_sim::cards::CardId;
use rh_sim::quests::board::catalog::DEFINITIONS;
use rh_sim::reward_chests::REWORK_POOL;
use serde_json::Value;

/// Quest rewards by board def id: workbook Top_25 "Value V (CE)", rounded to 0.1.
const REWARDS: [(&str, f64); 25] = [
    ("the_warden", 2.0),
    ("hold_the_fort", 3.6),
    ("bounty_hunter", 3.5),
    ("smugglers_run", 2.5),
    ("two_banners", 3.0),
    ("ember_walk", 2.0),
    ("the_burning_crown", 5.2),
    ("let_him_cook", 3.7),
    ("elite_slayer", 2.1),
    ("tuber_eats", 2.2),
    ("barrow_run", 1.5),
    ("the_bargain", 2.7),
    ("a_cruel_joke", 2.0),
    ("a_bone_to_pick", 1.8),
    ("feed_or_hunt", 1.5),
    ("rime_hunt", 3.5),
    ("watchtowers", 0.9),
    ("whispers_in_the_brush", 0.8),
    ("even_skeletons_get_cold", 1.5),
    ("lost_brother", 2.5),
    ("pilgrims_path", 2.0),
    ("gatecrasher", 2.5),
    ("letters", 2.0),
    ("stay_a_while", 2.0),
    ("trade_up", 3.5),
];

/// Augments by `CardId::as_str`: workbook Augment_Value "Total value V (CE)", scaled for the live Augment_Tuning.
const AUGMENTS: [(CardId, f64); 33] = [
    (CardId::MarkedMan, 7.0),
    (CardId::Pathfinder, 3.2),
    (CardId::MarksmansTally, 2.4),
    (CardId::EighthStrike, 1.9),
    (CardId::Calluses, 1.3),
    (CardId::Bastion, 1.2),
    (CardId::HazardWalker, 1.1),
    (CardId::Glacier, 0.9),
    (CardId::Stoked, 0.9),
    (CardId::Overflow, 0.8),
    (CardId::CorpseHoarder, 0.6),
    (CardId::WishEngine, 0.5),
    (CardId::TrophyHunter, 0.5),
    (CardId::ExecutionersLedger, 0.45),
    (CardId::Committed, 5.4),
    (CardId::MachineGun, 2.5),
    (CardId::PlantYourFeet, 2.3),
    (CardId::FistsOfFury, 1.9),
    (CardId::TankEngine, 1.9),
    (CardId::Gambler, 1.9),
    (CardId::HereComesTheBoom, 1.7),
    (CardId::Hemorrhage, 1.5),
    (CardId::Overkill, 1.4),
    (CardId::ThirdTimesTheCharm, 0.9),
    (CardId::SpikedShell, 0.9),
    (CardId::SecondWind, 0.8),
    (CardId::SlowBurn, 0.6),
    (CardId::BestOffense, 0.6),
    (CardId::EyeForAnEye, 0.5),
    (CardId::Siphon, 0.5),
    (CardId::WishfulThinking, 0.4),
    (CardId::PocketSand, 0.3),
    (CardId::RaceToZero, 0.0),
];

fn augments() -> &'static [(CardId, f64)] {
    &AUGMENTS
}

#[test]
fn every_board_quest_and_rework_augment_has_its_row_with_the_workbook_estimate() {
    let t = Prices::LOCAL;
    // The pinned set covers exactly the code's: 25 board defs, 14 natives plus the 19 of the Rework pool.
    assert_eq!(DEFINITIONS.len(), REWARDS.len(), "board defs vs pinned");
    for d in DEFINITIONS.iter() {
        let want = REWARDS
            .iter()
            .find(|(k, _)| *k == d.id)
            .unwrap_or_else(|| panic!("board def {} has no pinned reward", d.id));
        assert_eq!(
            t.reward_value(d.id),
            want.1,
            "reward_value({}): the workbook estimate of 2026-11-22; a later number packet amends this test",
            d.id
        );
        assert_ne!(
            t.reward_value(d.id),
            t.reward_default,
            "{} needs a row",
            d.id
        );
    }
    let mut code: Vec<CardId> = REWORK_POOL.to_vec();
    code.extend(CardId::NATIVE);
    assert_eq!(code.len(), 33);
    assert_eq!(augments().len(), 33);
    for id in code {
        let want = augments()
            .iter()
            .find(|(c, _)| *c == id)
            .unwrap_or_else(|| panic!("card {} has no pinned value", id.as_str()));
        assert_eq!(
            t.augment_value(id.as_str()),
            want.1,
            "augment_value({}): the workbook estimate of 2026-11-22; a later number packet amends this test",
            id.as_str()
        );
    }
    // Ordering the design cares about.
    assert!(t.reward_value("trade_up") > t.reward_value("watchtowers"));
    assert!(t.augment_value("pathfinder") > t.augment_value("pocketSand"));
    // A row of 0.0 is a row, not "no row".
    assert_eq!(t.augment_value("raceToZero"), 0.0);
    assert_eq!(t.reward_rows.len(), 25);
    assert_eq!(t.augment_rows.len(), 33);
}

fn json_rows(v: &Value, section: &str) -> Vec<(String, f64)> {
    assert!(
        v[section]["source"].as_str().is_some_and(|s| !s.is_empty()),
        "{section} needs a source string"
    );
    v[section]["rows"]
        .as_object()
        .unwrap_or_else(|| panic!("{section}.rows is an object"))
        .iter()
        .map(|(k, n)| (k.clone(), n.as_f64().expect("number")))
        .collect()
}

#[test]
fn the_rust_slices_equal_the_checked_in_json_row_for_row() {
    let text = std::fs::read_to_string(concat!(
        env!("CARGO_MANIFEST_DIR"),
        "/data/raid_values.json"
    ))
    .expect("data/raid_values.json");
    let v: Value = serde_json::from_str(&text).expect("json");
    assert_eq!(v["schema"], "raid-values/1");
    for (section, rows) in [
        ("rewards", Prices::LOCAL.reward_rows),
        ("augments", Prices::LOCAL.augment_rows),
    ] {
        let mut json = json_rows(&v, section);
        let mut rust: Vec<(String, f64)> = rows.iter().map(|(k, n)| (k.to_string(), *n)).collect();
        json.sort_by(|a, b| a.0.cmp(&b.0));
        rust.sort_by(|a, b| a.0.cmp(&b.0));
        assert!(!rust.is_empty(), "{section}: LOCAL has rows");
        assert_eq!(
            rust, json,
            "{section}: values.rs and raid_values.json differ"
        );
    }
}

#[test]
fn unknown_keys_take_the_flat_default_and_farm_only_zeroes_every_row() {
    let t = Prices::LOCAL;
    assert_eq!(t.reward_value("no_such_quest"), 1.0);
    assert_eq!(t.augment_value("noSuchCard"), 1.0);
    assert!(
        t.reward_value("trade_up") > 1.0,
        "LOCAL prices rewards by row"
    );
    let f = Prices::farm_only();
    assert_eq!(f.reward_value("no_such_quest"), 0.0);
    assert_eq!(f.augment_value("noSuchCard"), 0.0);
    for (k, _) in REWARDS {
        assert_eq!(f.reward_value(k), 0.0, "farm_only reward {k}");
    }
    for (c, _) in augments() {
        assert_eq!(
            f.augment_value(c.as_str()),
            0.0,
            "farm_only augment {}",
            c.as_str()
        );
    }
    assert!(f.reward_rows.is_empty() && f.augment_rows.is_empty());
    // Nothing else of LOCAL changes.
    assert_eq!(f.camp, t.camp);
    assert_eq!(f.chest, t.chest);
}

#[test]
fn a_doctored_row_changes_what_the_accessor_returns() {
    let d = Prices {
        reward_rows: &[("trade_up", 9.5)],
        augment_rows: &[("pathfinder", 4.25)],
        ..Prices::LOCAL
    };
    assert_eq!(d.reward_value("trade_up"), 9.5);
    assert_eq!(d.augment_value("pathfinder"), 4.25);
    assert_eq!(
        d.reward_value("watchtowers"),
        d.reward_default,
        "no row, flat default"
    );
    assert_eq!(d.augment_value("pocketSand"), d.augment_default);
}
