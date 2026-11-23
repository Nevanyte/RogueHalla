//! P-BAL-NATIVE-TEXT-GUARD-1 oracle (docs/backlog/packets/P-BAL-NATIVE-TEXT-GUARD-1.md): a number in a native card's text
//! in `CardDisplayCatalog.cs` must be the number the Rust table gives. Numbers only, never phrasing.
//!
//! Text and table agree today, so the guard is proved on doctored input: one wrong digit in a card's description, one wrong
//! table value, a missing card and a changed count are each reported, by card name, and nothing else is.
#[path = "native_text_guard/check.rs"]
mod check;

use check::{check, Expected};
use rh_sim::constants::*;

const CATALOG: &str =
    include_str!("../../../client-unity/UnityProject/Assets/Scripts/UI/CardDisplayCatalog.cs");

fn ex(id: &'static str, numbers: &[f64]) -> Expected {
    Expected {
        id,
        numbers: numbers.to_vec(),
    }
}

/// The 14 natives in `CardId::NATIVE` order: the numbers each description states, in reading order, as the constants give
/// them (shares in percent). WishEngine's 50 is the Wishful floor, the only registered 0.5 (packet, Known traps).
fn table() -> Vec<Expected> {
    vec![
        ex(
            "MarksmansTally",
            &[
                REWORK_MARKSMANS_TALLY_PER * 100.,
                REWORK_MARKSMANS_TALLY_RANGE_SHARE * 100.,
                REWORK_MARKSMANS_TALLY_CAP * 100.,
            ],
        ),
        ex("Bastion", &[REWORK_BASTION_SHARE * 100.]),
        ex("Calluses", &[REWORK_CALLUSES_PER, REWORK_CALLUSES_CAP]),
        ex(
            "ExecutionersLedger",
            &[
                REWORK_EXECUTIONERS_LEDGER_PER,
                REWORK_EXECUTIONERS_LEDGER_CAP,
            ],
        ),
        ex("EighthStrike", &[REWORK_EIGHTH_STRIKE_EVERY as f64]),
        ex(
            "Pathfinder",
            &[REWORK_PATHFINDER_PER * 100., REWORK_PATHFINDER_CAP * 100.],
        ),
        ex(
            "HazardWalker",
            &[
                REWORK_HAZARD_WALKER_CAP,
                REWORK_HAZARD_WALKER_IMMUNE_MS / 1000.,
            ],
        ),
        ex(
            "WishEngine",
            &[REWORK_WISH_ENGINE_CAP * 100., WISHFUL_FLOOR_SHARE * 100.],
        ),
        ex("Overflow", &[REWORK_OVERFLOW_FADE_MS / 1000.]),
        ex(
            "CorpseHoarder",
            &[
                REWORK_CORPSE_HOARDER_PER * 100.,
                REWORK_CORPSE_HOARDER_CAP * 100.,
            ],
        ),
        ex("Stoked", &[REWORK_STOKED_PER, REWORK_STOKED_CAP]),
        ex("Glacier", &[REWORK_GLACIER_PER, REWORK_GLACIER_CAP]),
        ex(
            "TrophyHunter",
            &[
                REWORK_TROPHY_HUNTER_PER * 100.,
                REWORK_TROPHY_HUNTER_CAP * 100.,
            ],
        ),
        ex("MarkedMan", &[(REWORK_MARKED_MAN_STACK_MULT - 1.) * 100.]),
    ]
}

/// `CATALOG` with the first digit of card `id`'s description (the third string of its `new Entry("<id>", ...)` line)
/// changed: d becomes (d + 1) % 10.
fn doctored(id: &str) -> String {
    let start = CATALOG
        .find(&format!("new Entry(\"{id}\", "))
        .unwrap_or_else(|| panic!("no entry {id} in the catalog"));
    let line_end = start + CATALOG[start..].find('\n').unwrap();
    let desc_from = start
        + CATALOG[start..line_end]
            .match_indices("\", \"")
            .nth(1)
            .unwrap()
            .0
        + 4;
    let off = CATALOG[desc_from..line_end]
        .find(|c: char| c.is_ascii_digit())
        .unwrap_or_else(|| panic!("{id} states no number"));
    let at = desc_from + off;
    let d = CATALOG.as_bytes()[at] - b'0';
    let mut s = CATALOG.to_string();
    s.replace_range(at..at + 1, &((d + 1) % 10).to_string());
    s
}

#[test]
fn the_real_catalog_text_and_the_real_table_agree() {
    let t = table();
    assert_eq!(t.len(), 14, "the 14 natives");
    let found = check(CATALOG, &t);
    assert!(found.is_empty(), "text and table disagree: {found:?}");
}

#[test]
fn a_wrong_digit_in_any_native_card_is_reported_alone_and_by_name() {
    for e in table() {
        let found = check(&doctored(e.id), &table());
        assert_eq!(
            found.iter().map(|m| m.card.as_str()).collect::<Vec<_>>(),
            vec![e.id],
            "{}: one digit changed in its text must give exactly one mismatch, on that card",
            e.id
        );
    }
}

#[test]
fn a_changed_table_value_is_reported_against_the_unchanged_text() {
    let mut t = table();
    let calluses = t.iter_mut().find(|e| e.id == "Calluses").unwrap();
    calluses.numbers[1] = 31.;
    let found = check(CATALOG, &t);
    assert_eq!(found.len(), 1, "{found:?}");
    assert_eq!(found[0].card, "Calluses");
}

#[test]
fn a_later_number_of_a_longer_text_is_checked_too() {
    for (id, index, value) in [
        ("HazardWalker", 1, 21.),
        ("WishEngine", 1, 40.),
        ("MarksmansTally", 1, 75.),
    ] {
        let mut t = table();
        t.iter_mut().find(|e| e.id == id).unwrap().numbers[index] = value;
        let found = check(CATALOG, &t);
        assert_eq!(found.len(), 1, "{id}: {found:?}");
        assert_eq!(found[0].card, id);
    }
}

#[test]
fn a_different_count_of_numbers_is_reported() {
    let mut t = table();
    t.iter_mut()
        .find(|e| e.id == "Pathfinder")
        .unwrap()
        .numbers
        .push(5.);
    let found = check(CATALOG, &t);
    assert_eq!(found.len(), 1, "{found:?}");
    assert_eq!(found[0].card, "Pathfinder");
}

#[test]
fn a_card_missing_from_the_text_is_reported_and_so_is_an_empty_text() {
    let without = CATALOG.replace("new Entry(\"Overflow\", ", "new Entry(\"Overflowed\", ");
    let found = check(&without, &table());
    assert_eq!(found.len(), 1, "{found:?}");
    assert_eq!(found[0].card, "Overflow");
    let found = check("", &table());
    assert_eq!(found.len(), 14, "every card is missing from an empty text");
}
