//! The roster rows of State key 12.

use super::*;

/// One seated player (State key 12): its actor id (the id its actor row carries when delivered), the bot flag and its
/// display name (1..=24 UTF-8 bytes, no control character).
#[derive(Clone, Debug, PartialEq, Eq)]
pub struct WireRosterRow {
    pub id: u32,
    pub bot: bool,
    pub name: String,
}

/// A roster name a decoder accepts: 1..=[`MAX_ROSTER_NAME`] bytes with no control character.
pub fn roster_name_valid(name: &str) -> bool {
    (1..=MAX_ROSTER_NAME).contains(&name.len()) && !name.chars().any(char::is_control)
}

pub(super) fn roster_fault(rows: &[WireRosterRow]) -> Option<&'static str> {
    if rows.len() > MAX_ROSTER {
        return Some("roster count");
    }
    let mut previous = 0u32;
    for row in rows {
        if row.id == 0 || row.id <= previous {
            return Some("roster order");
        }
        previous = row.id;
        if !roster_name_valid(&row.name) {
            return Some("roster name");
        }
    }
    None
}
