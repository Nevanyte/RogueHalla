//! P-BAL-NATIVE-TEXT-GUARD-1: the checker behind `tests/native_text_guard.rs`.
//!
//! Contract in `docs/backlog/packets/P-BAL-NATIVE-TEXT-GUARD-1.md` ("The checker's contract").

/// One native card's expected numbers: the C# `Entry` id and the numbers its description states, in reading order.
pub struct Expected {
    pub id: &'static str,
    pub numbers: Vec<f64>,
}

/// One disagreement between the catalog text and the table.
#[derive(Debug)]
pub struct Mismatch {
    pub card: String,
    pub detail: String,
}

impl std::fmt::Display for Mismatch {
    fn fmt(&self, f: &mut std::fmt::Formatter<'_>) -> std::fmt::Result {
        write!(f, "{}: {}", self.card, self.detail)
    }
}

/// Reads one C# string literal starting at the opening quote of `s`; returns its text and the rest after the closing quote.
fn read_literal(s: &str) -> Option<(String, &str)> {
    let rest = s.strip_prefix('"')?;
    let mut out = String::new();
    let mut chars = rest.char_indices();
    while let Some((i, c)) = chars.next() {
        match c {
            '"' => return Some((out, &rest[i + 1..])),
            '\\' => out.push(chars.next()?.1),
            _ => out.push(c),
        }
    }
    None
}

/// The description (third string) of `new Entry("<id>", ...)`, if the entry exists and is well formed.
fn description(catalog_text: &str, id: &str) -> Option<String> {
    let needle = format!("new Entry(\"{id}\"");
    let at = catalog_text.find(&needle)? + needle.len();
    let mut rest = &catalog_text[at..];
    // The needle consumed the id; what follows is `, "<title>", "<description>"`.
    let q = rest.find('"')?;
    rest = read_literal(&rest[q..])?.1;
    let q = rest.find('"')?;
    read_literal(&rest[q..]).map(|(d, _)| d)
}

/// Numeric tokens (digits with an optional `.digits`) of `text`, in reading order.
fn numbers_in(text: &str) -> Vec<f64> {
    let b = text.as_bytes();
    let mut out = Vec::new();
    let mut i = 0;
    while i < b.len() {
        if b[i].is_ascii_digit() {
            let start = i;
            while i < b.len() && b[i].is_ascii_digit() {
                i += 1;
            }
            if i + 1 < b.len() && b[i] == b'.' && b[i + 1].is_ascii_digit() {
                i += 1;
                while i < b.len() && b[i].is_ascii_digit() {
                    i += 1;
                }
            }
            if let Ok(v) = text[start..i].parse() {
                out.push(v);
            }
        } else {
            i += 1;
        }
    }
    out
}

/// Every disagreement between the numbers in `catalog_text` (the text of `CardDisplayCatalog.cs`) and `expected`.
pub fn check(catalog_text: &str, expected: &[Expected]) -> Vec<Mismatch> {
    let mut out = Vec::new();
    for e in expected {
        let card = e.id.to_string();
        let Some(desc) = description(catalog_text, e.id) else {
            out.push(Mismatch {
                card,
                detail: "no `new Entry` with this id in the catalog text".to_string(),
            });
            continue;
        };
        let found = numbers_in(&desc);
        let same = found.len() == e.numbers.len()
            && found
                .iter()
                .zip(&e.numbers)
                .all(|(a, b)| (a - b).abs() <= 1e-9);
        if !same {
            out.push(Mismatch {
                card,
                detail: format!(
                    "text {desc:?} has numbers {found:?}, the table gives {:?}",
                    e.numbers
                ),
            });
        }
    }
    out
}
