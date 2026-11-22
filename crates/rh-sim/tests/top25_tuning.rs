//! P-BAL-Q25-SEAM-1 oracle: every Top 25 quest number lives in `TOP25` (`src/quests/board/tuning.rs`). A source scan of
//! the non-test files of `src/quests/board/**`: no numeric `const`/`static` and no tuning float literal outside the
//! table, each exception with its reason, and no dead-code allowance left on the table (so every field is read by the
//! game). The content hash staying put is `tests/content.rs` `GOLDEN_CONTENT_HASH`.

use std::path::{Path, PathBuf};

/// Numeric consts that are not tuning, as (file under `quests/board/`, name, reason). Each must still be declared.
const CONST_ALLOWED: &[(&str, &str, &str)] = &[
    (
        "mod.rs",
        "FIRST_BOARD_DEF",
        "provenance: board definitions occupy giver ids from 256, an identifier range",
    ),
    (
        "attempts/signals.rs",
        "BARGAIN_STAT_COUNT",
        "length of BARGAIN_STAT_NAMES and the action 5/7 target encoding (give * 9 + receive), read by src/input.rs",
    ),
    (
        "rewards/effects/terrain.rs",
        "RADIUS",
        "deployment hitbox, read as DEPLOYMENT_RADIUS by systems/melee.rs and systems/projectiles.rs: geometry",
    ),
    (
        "special/egg.rs",
        "RADIUS",
        "egg hitbox, read by systems/melee.rs and systems/projectiles.rs: geometry",
    ),
    (
        "special/wall.rs",
        "FENCE",
        "the low-tile kind code of a fence on the map export: a map code",
    ),
    (
        "special/wall.rs",
        "MAX_REMOVALS",
        "wire capacity of the reliable cell collection (300 cells per wire seat)",
    ),
];

/// Float literals that are not tuning, in any scanned file: neutral values, halves, unit conversion, tolerance.
const FLOAT_ALLOWED_ANYWHERE: &[(&str, &str)] = &[
    ("0.", "zero"),
    ("0.0", "zero"),
    ("1.", "one (neutral factor, minimum move)"),
    ("1.0", "one"),
    ("0.5", "half: midpoints and the minimum step"),
    ("2.", "halves: TILE / 2., a tile pair"),
    ("1000.", "ms per second"),
    ("1e-6", "float tolerance of restore checks"),
];

/// Float literals that are not tuning, per file: (file, literal, reason).
const FLOAT_ALLOWED_IN: &[(&str, &str, &str)] = &[
    (
        "rewards/effects.rs",
        "1e6",
        "threat sentinel: a provoked mob targets the caster first",
    ),
    (
        "rewards/effects.rs",
        "16.",
        "fallback body box of a fighter without a hull: geometry",
    ),
    (
        "rewards/effects/terrain.rs",
        "0.65",
        "cage ring thickness in tiles (which tiles form the ring): geometry",
    ),
    (
        "rewards/effects/terrain.rs",
        "4.",
        "shove-out search step (24 steps of 4 units): collision resolution",
    ),
    (
        "attempts/interactions.rs",
        "3000.",
        "Trade Up trader patrol shape: quarter-period phase offset",
    ),
    (
        "attempts/interactions.rs",
        "12_000.",
        "Trade Up trader patrol shape: period",
    ),
    (
        "attempts/interactions.rs",
        "6000.",
        "Trade Up trader patrol shape: half period",
    ),
    (
        "attempts/interactions.rs",
        "72.",
        "Trade Up trader patrol arm length",
    ),
    (
        "attempts/targets.rs",
        "3.",
        "encounter camp minimum radius in tiles: map admission geometry",
    ),
];

/// Files out of the float-literal scan: the catalog's `def!` rows (route timers and counts stay there by design) and
/// the board's placement geometry (where givers and objectives are dealt), which is not quest tuning.
const FLOAT_SCAN_SKIPS: &[&str] = &["catalog.rs", "placement.rs", "map_admission.rs"];

const TABLE: &str = "tuning.rs";

fn board_dir() -> PathBuf {
    Path::new(env!("CARGO_MANIFEST_DIR")).join("src/quests/board")
}

fn rs_files(dir: &Path, out: &mut Vec<PathBuf>) {
    let mut entries: Vec<PathBuf> = std::fs::read_dir(dir)
        .unwrap()
        .map(|e| e.unwrap().path())
        .collect();
    entries.sort();
    for p in entries {
        if p.is_dir() {
            rs_files(&p, out);
        } else if p.extension().is_some_and(|e| e == "rs") {
            out.push(p);
        }
    }
}

fn is_test_file(rel: &str) -> bool {
    let name = rel.rsplit('/').next().unwrap();
    name == "tests.rs" || name.ends_with("_tests.rs") || rel.starts_with("attempts/scenarios")
}

/// The non-test board sources outside the table, as (path relative to `quests/board/`, cleaned code). Cleaning blanks
/// comments and string/char literals (keeping line breaks) and removes `#[cfg(test)] mod … { … }` blocks.
fn sources() -> Vec<(String, String)> {
    let root = board_dir();
    let mut files = Vec::new();
    rs_files(&root, &mut files);
    let out: Vec<(String, String)> = files
        .into_iter()
        .filter_map(|p| {
            let rel = p
                .strip_prefix(&root)
                .unwrap()
                .to_string_lossy()
                .replace('\\', "/");
            if rel == TABLE || is_test_file(&rel) {
                return None;
            }
            let text = std::fs::read_to_string(&p).unwrap();
            Some((rel, drop_test_modules(&blank_comments_and_strings(&text))))
        })
        .collect();
    assert!(out.len() >= 15, "the scan found too few board files");
    out
}

fn blank_comments_and_strings(text: &str) -> String {
    let c: Vec<char> = text.chars().collect();
    let mut out = String::with_capacity(text.len());
    let mut i = 0;
    while i < c.len() {
        if c[i] == '/' && c.get(i + 1) == Some(&'/') {
            while i < c.len() && c[i] != '\n' {
                i += 1;
            }
        } else if c[i] == '/' && c.get(i + 1) == Some(&'*') {
            i += 2;
            while i + 1 < c.len() && !(c[i] == '*' && c[i + 1] == '/') {
                if c[i] == '\n' {
                    out.push('\n');
                }
                i += 1;
            }
            i += 2;
        } else if c[i] == '"' {
            out.push('"');
            i += 1;
            while i < c.len() && c[i] != '"' {
                if c[i] == '\\' {
                    i += 1;
                }
                if c.get(i) == Some(&'\n') {
                    out.push('\n');
                }
                i += 1;
            }
            out.push('"');
            i += 1;
        } else if c[i] == '\''
            && (c.get(i + 2) == Some(&'\'')
                || (c.get(i + 1) == Some(&'\\') && c.get(i + 3) == Some(&'\'')))
        {
            out.push_str("' '");
            i += if c[i + 1] == '\\' { 4 } else { 3 };
        } else {
            out.push(c[i]);
            i += 1;
        }
    }
    out
}

/// Removes `#[cfg(test)]` followed by an inline `mod name { … }`, keeping line breaks so line numbers stay true.
fn drop_test_modules(code: &str) -> String {
    let mut out = String::with_capacity(code.len());
    let mut rest = code;
    while let Some(at) = rest.find("#[cfg(test)]") {
        let after = &rest[at + "#[cfg(test)]".len()..];
        let trimmed = after.trim_start();
        let inline_mod = trimmed.starts_with("mod ")
            && trimmed
                .find(['{', ';'])
                .is_some_and(|k| trimmed.as_bytes()[k] == b'{');
        if !inline_mod {
            out.push_str(&rest[..at + "#[cfg(test)]".len()]);
            rest = after;
            continue;
        }
        out.push_str(&rest[..at]);
        let open =
            at + "#[cfg(test)]".len() + (after.len() - trimmed.len()) + trimmed.find('{').unwrap();
        let mut depth = 0i32;
        let mut end = rest.len();
        for (k, ch) in rest[open..].char_indices() {
            match ch {
                '{' => depth += 1,
                '}' => {
                    depth -= 1;
                    if depth == 0 {
                        end = open + k + 1;
                        break;
                    }
                }
                _ => {}
            }
        }
        out.extend(rest[at..end].chars().filter(|&ch| ch == '\n'));
        rest = &rest[end..];
    }
    out.push_str(rest);
    out
}

fn is_ident(c: char) -> bool {
    c.is_ascii_alphanumeric() || c == '_'
}

const NUMERIC: &[&str] = &[
    "f32", "f64", "u8", "u16", "u32", "u64", "u128", "usize", "i8", "i16", "i32", "i64", "i128",
    "isize",
];

/// A numeric type: a primitive, or an array (any depth) of one.
fn numeric_type(ty: &str) -> bool {
    let mut t = ty;
    while let Some(inner) = t.strip_prefix('[') {
        t = inner.split(';').next().unwrap_or("");
    }
    NUMERIC.contains(&t)
}

/// `const NAME: TYPE` / `static NAME: TYPE` items with an upper-case name, as (line, name, type without spaces).
fn numeric_consts(code: &str) -> Vec<(usize, String, String)> {
    let mut out = Vec::new();
    for kw in ["const ", "static "] {
        let mut from = 0;
        while let Some(i) = code[from..].find(kw) {
            let at = from + i;
            from = at + kw.len();
            if code[..at].chars().next_back().is_some_and(is_ident) {
                continue;
            }
            let name: String = code[from..].chars().take_while(|c| is_ident(*c)).collect();
            if name.is_empty()
                || !name
                    .chars()
                    .all(|c| c.is_ascii_uppercase() || c.is_ascii_digit() || c == '_')
            {
                continue;
            }
            let rest = code[from + name.len()..].trim_start();
            let Some(rest) = rest.strip_prefix(':') else {
                continue;
            };
            let mut depth = 0i32;
            let ty: String = rest
                .chars()
                .take_while(|c| {
                    match c {
                        '[' | '<' | '(' => depth += 1,
                        ']' | '>' | ')' => depth -= 1,
                        _ => {}
                    }
                    !(depth == 0 && (*c == '=' || *c == ';'))
                })
                .filter(|c| !c.is_whitespace())
                .collect();
            if numeric_type(&ty) {
                let line = code[..at].matches('\n').count() + 1;
                out.push((line, name, ty));
            }
        }
    }
    out.sort();
    out
}

/// Float literal tokens (a `.` or an exponent or an `f32`/`f64` suffix), as (line, literal text).
fn float_literals(code: &str) -> Vec<(usize, String)> {
    let c: Vec<char> = code.chars().collect();
    let mut out = Vec::new();
    let mut line = 1;
    let mut i = 0;
    while i < c.len() {
        if c[i] == '\n' {
            line += 1;
            i += 1;
            continue;
        }
        let starts = c[i].is_ascii_digit() && (i == 0 || !(is_ident(c[i - 1]) || c[i - 1] == '.'));
        if !starts {
            i += 1;
            continue;
        }
        let begin = i;
        let mut float = false;
        while i < c.len() && (c[i].is_ascii_digit() || c[i] == '_') {
            i += 1;
        }
        if c.get(i) == Some(&'.')
            && !c
                .get(i + 1)
                .is_some_and(|&n| n == '.' || n.is_alphabetic() || n == '_')
        {
            float = true;
            i += 1;
            while i < c.len() && (c[i].is_ascii_digit() || c[i] == '_') {
                i += 1;
            }
        }
        if c.get(i) == Some(&'e')
            && (c.get(i + 1).is_some_and(|n| n.is_ascii_digit())
                || (matches!(c.get(i + 1), Some('-' | '+'))
                    && c.get(i + 2).is_some_and(|n| n.is_ascii_digit())))
        {
            float = true;
            i += 2;
            while i < c.len() && c[i].is_ascii_digit() {
                i += 1;
            }
        }
        let text: String = c[begin..i].iter().collect();
        let suffix: String = c[i..].iter().take_while(|&&ch| is_ident(ch)).collect();
        if suffix.trim_start_matches('_') == "f64" || suffix.trim_start_matches('_') == "f32" {
            float = true;
        }
        i += suffix.chars().count();
        if float {
            out.push((line, text));
        }
    }
    out
}

#[test]
fn no_numeric_const_outside_the_tuning_table() {
    let mut stray = Vec::new();
    let mut seen = Vec::new();
    for (file, code) in sources() {
        for (line, name, ty) in numeric_consts(&code) {
            if CONST_ALLOWED
                .iter()
                .any(|(f, n, _)| *f == file && *n == name)
            {
                seen.push((file.clone(), name));
            } else {
                stray.push(format!("quests/board/{file}:{line} {name}: {ty}"));
            }
        }
    }
    assert!(
        stray.is_empty(),
        "{} numeric consts outside quests/board/tuning.rs: move each number into TOP25 and read TOP25.<field> \
         (the visit_rules keep their word order); a const that is not tuning needs a reasoned CONST_ALLOWED row \
         (owner review):\n{}",
        stray.len(),
        stray.join("\n")
    );
    for (f, n, reason) in CONST_ALLOWED {
        assert!(!reason.is_empty());
        assert!(
            seen.iter().any(|(sf, sn)| sf == f && sn == n),
            "allowed const quests/board/{f} {n} is no longer declared: the allow-list is stale"
        );
    }
}

#[test]
fn no_tuning_float_literal_outside_the_tuning_table() {
    let mut stray = Vec::new();
    for (file, code) in sources() {
        if FLOAT_SCAN_SKIPS.contains(&file.as_str()) {
            continue;
        }
        let allowed_const_lines: Vec<usize> = numeric_consts(&code)
            .into_iter()
            .filter(|(_, n, _)| CONST_ALLOWED.iter().any(|(f, an, _)| *f == file && an == n))
            .map(|(line, _, _)| line)
            .collect();
        for (line, lit) in float_literals(&code) {
            let allowed = allowed_const_lines.contains(&line)
                || FLOAT_ALLOWED_ANYWHERE.iter().any(|(l, _)| *l == lit)
                || FLOAT_ALLOWED_IN
                    .iter()
                    .any(|(f, l, _)| *f == file && *l == lit);
            if !allowed {
                stray.push(format!("quests/board/{file}:{line} {lit}"));
            }
        }
    }
    assert!(
        stray.is_empty(),
        "{} tuning float literals outside quests/board/tuning.rs: each is a Top 25 number (packet F1) and reads \
         TOP25.<field>; a literal that is not tuning needs a reasoned FLOAT_ALLOWED row (owner review):\n{}",
        stray.len(),
        stray.join("\n")
    );
}

#[test]
fn the_table_carries_no_dead_code_allowance() {
    let text = std::fs::read_to_string(board_dir().join(TABLE)).unwrap();
    let code = blank_comments_and_strings(&text);
    assert!(
        code.contains("static TOP25: Top25Tuning"),
        "quests/board/tuning.rs declares `pub(crate) static TOP25: Top25Tuning`"
    );
    for allowance in [
        "allow(dead_code)",
        "allow(unused",
        "expect(dead_code)",
        "expect(unused",
    ] {
        assert!(
            !code.contains(allowance),
            "quests/board/tuning.rs still carries `{allowance}`: every TOP25 field must be read by the game (the \
             stub's allowance goes once each number in packet F1 reads its field)"
        );
    }
}

/// The scanner's own checks, so a green scan means something.
#[test]
fn the_scanner_sees_what_it_should() {
    let code = blank_comments_and_strings(
        "const A_B: f64 = 1.5; // const C: f64 = 2.5;\nstatic T: [u64; 3] = [1, 2, 3];\nconst S: &str = \"9.5\";\n\
         fn f() { let x = 45_000. + 3e-2 + 7f64; for i in 1..=24 {} let y = v.0; }\n\
         #[cfg(test)]\nmod tests {\n const Z: f64 = 4.25;\n}\nconst W: usize = 4;\n",
    );
    let code = drop_test_modules(&code);
    let consts: Vec<String> = numeric_consts(&code)
        .into_iter()
        .map(|(_, n, _)| n)
        .collect();
    assert_eq!(consts, ["A_B", "T", "W"]);
    let lits: Vec<String> = float_literals(&code).into_iter().map(|(_, l)| l).collect();
    assert_eq!(lits, ["1.5", "45_000.", "3e-2", "7"]);
    assert_eq!(
        numeric_consts(&code).last().unwrap().0,
        9,
        "line numbers survive the module cut"
    );
}
