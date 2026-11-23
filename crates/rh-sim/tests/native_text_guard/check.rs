//! P-BAL-NATIVE-TEXT-GUARD-1: the checker behind `tests/native_text_guard.rs`.
//!
//! Contract in `docs/backlog/packets/P-BAL-NATIVE-TEXT-GUARD-1.md` ("The checker's contract").

/// One native card's expected numbers: the C# `Entry` id and the numbers its description states, in reading order.
pub struct Expected {
    pub id: &'static str,
    pub numbers: Vec<f64>,
}

/// One disagreement between the catalog text and the table.
// The stub never builds one and nothing reads `detail` yet; the implementer removes the allow.
#[allow(dead_code)]
#[derive(Debug)]
pub struct Mismatch {
    pub card: String,
    pub detail: String,
}

/// Every disagreement between the numbers in `catalog_text` (the text of `CardDisplayCatalog.cs`) and `expected`.
pub fn check(catalog_text: &str, expected: &[Expected]) -> Vec<Mismatch> {
    let _ = (catalog_text, expected);
    Vec::new() // stub: the implementer replaces this body
}
