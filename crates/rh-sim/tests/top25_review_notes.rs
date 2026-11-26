//! P-BAL-PROBE-1 oracle: the quest-table review notes (reviewer-opus on P-BAL-Q25-SEAM-1), read from the source because
//! `TOP25` is an immutable static with no test hook.
//! - `at_turn_in` checks the same range as the turn-in menu and the tick's endpoint check: `TOP25.contact_range`, not the
//!   raw `QUEST_TURN_IN_RANGE` (a retune of `contact_range` must not offer a turn-in the server then refuses).
//! - `tuning.rs` says which fields the content hash does not fold, and documents the fields whose unit or second role
//!   is not obvious from the name.
use std::path::Path;

fn source(rel: &str) -> String {
    std::fs::read_to_string(Path::new(env!("CARGO_MANIFEST_DIR")).join(rel)).unwrap()
}

/// The text of `fn <name>` up to the first line that is a lone `}` at column 0.
fn function_body(text: &str, name: &str) -> String {
    let start = text
        .find(&format!("fn {name}("))
        .unwrap_or_else(|| panic!("fn {name} not found"));
    let rest = &text[start..];
    let end = rest.find("\n}\n").expect("function end");
    rest[..end].to_string()
}

#[test]
fn at_turn_in_reads_the_tables_contact_range() {
    let choices = source("src/quests/board/attempts/choices.rs");
    let menu = function_body(&choices, "choices");
    assert!(
        menu.contains("TOP25.contact_range"),
        "the objective menu reads TOP25.contact_range (scan sanity)"
    );
    let body = function_body(&choices, "at_turn_in");
    assert!(
        body.contains("near(") && body.contains("TOP25.contact_range"),
        "at_turn_in must check its endpoint with TOP25.contact_range:\n{body}"
    );
    assert!(
        !body.contains("QUEST_TURN_IN_RANGE"),
        "at_turn_in still reads QUEST_TURN_IN_RANGE directly:\n{body}"
    );
    let ticks = source("src/quests/board/attempts/interactions.rs");
    assert!(
        function_body(&ticks, "tick")
            .contains("near(cx.k, cx.w, pos, destination, TOP25.contact_range)"),
        "the tick's endpoint check reads TOP25.contact_range (scan sanity)"
    );
}

/// The `///` lines directly above `pub(crate) <field>:` in the table struct, joined.
fn field_doc(table: &str, field: &str) -> String {
    let lines: Vec<&str> = table.lines().collect();
    let at = lines
        .iter()
        .position(|l| l.trim_start().starts_with(&format!("pub(crate) {field}:")))
        .unwrap_or_else(|| panic!("field {field} not in Top25Tuning"));
    let mut doc = Vec::new();
    for line in lines[..at].iter().rev() {
        let t = line.trim_start();
        if let Some(text) = t.strip_prefix("///") {
            doc.push(text.trim());
        } else {
            break;
        }
    }
    doc.reverse();
    doc.join(" ")
}

#[test]
fn the_tuning_table_documents_its_unfolded_fields_and_units() {
    let table = source("src/quests/board/tuning.rs");
    let module: String = table
        .lines()
        .filter_map(|l| l.strip_prefix("//!"))
        .collect::<Vec<_>>()
        .join(" ");
    assert!(
        module.contains("39") && module.contains("91") && module.contains("content hash"),
        "the module doc says that 39 of the 91 fields are not folded into the content hash today: {module}"
    );
    for field in [
        "crown_burn_rate",
        "crown_power_cap",
        "pull_tick_distance",
        "tornado_pull",
        "turret_hp",
        "turret_damage_share",
        "route_pace",
        "tornado_windup_ms",
        "falcon_cap",
    ] {
        let doc = field_doc(&table, field);
        assert!(
            doc.split_whitespace().count() >= 4,
            "TOP25.{field} needs a doc comment with its unit and meaning (has: {doc:?})"
        );
    }
    assert!(
        field_doc(&table, "tornado_windup_ms").contains("Cleansed"),
        "tornado_windup_ms also sets how long the Cleansed buff lasts"
    );
    assert!(
        field_doc(&table, "falcon_cap")
            .to_lowercase()
            .contains("restore"),
        "falcon_cap is a restore bound (the cast path keeps one falcon per owner)"
    );
}
