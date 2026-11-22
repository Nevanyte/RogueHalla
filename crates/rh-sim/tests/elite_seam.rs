//! P-BAL-ELITE-SEAM-1 oracle (source half; docs/backlog/packets/P-BAL-ELITE-SEAM-1.md): every rh-sim reader of an elite
//! multiplier goes through `EliteTuning::hp_mult_of(ty)` / `dmg_mult_of(ty)`, so a per-kind row reaches all of them.
//!
//! The scan walks `crates/rh-sim/src`. Outside `mob_runtime.rs` (and its child modules under `mob_runtime/`) no code may
//! read the field `.hp_mult` or `.dmg_mult` of an elite table, nor build or destructure `EliteTuning { .. }`. The mob
//! role's own `hp_mult` / `dmg_mult` share the field names: the receiver `role_def()` (any file) and the role row `row`
//! of the mob-catalog content fold (`content/fold.rs`) are the only reads allowed. `.hp_mult_of(` and `max_hp_mult` are
//! other names and never match. The in-file reader `spawn_hp` (`mob_runtime.rs`) is pinned by the behaviour half
//! (`src/mob_runtime/elite_kind_tests.rs`).
use std::path::{Path, PathBuf};

fn sources(dir: &Path, out: &mut Vec<PathBuf>) {
    let mut entries: Vec<PathBuf> = std::fs::read_dir(dir)
        .unwrap()
        .map(|e| e.unwrap().path())
        .collect();
    entries.sort();
    for p in entries {
        if p.is_dir() {
            sources(&p, out);
        } else if p.extension().is_some_and(|e| e == "rs") {
            out.push(p);
        }
    }
}

fn is_ident(c: char) -> bool {
    c.is_ascii_alphanumeric() || c == '_'
}

/// The code of a line without its `//` comment (a `//` inside a string literal is rare enough here to ignore: it can
/// only hide code, never invent a read).
fn code_of(line: &str) -> &str {
    line.find("//").map_or(line, |i| &line[..i])
}

/// The receiver text just before a `.` at byte `dot`: the identifier, or `name()` for a call with empty parentheses.
fn receiver(code: &str, dot: usize) -> String {
    let before = &code[..dot];
    let (base, call) = match before.strip_suffix("()") {
        Some(b) => (b, "()"),
        None => (before, ""),
    };
    let start = base
        .char_indices()
        .rev()
        .take_while(|(_, c)| is_ident(*c))
        .last()
        .map_or(base.len(), |(i, _)| i);
    format!("{}{call}", &base[start..])
}

#[test]
fn no_file_outside_mob_runtime_reads_an_elite_multiplier_field() {
    let root = Path::new(env!("CARGO_MANIFEST_DIR")).join("src");
    let mut files = vec![];
    sources(&root, &mut files);
    assert!(files.len() > 100, "the scan found the source tree");
    let mut offending = vec![];
    let mut allowed_role_reads = 0;
    for path in &files {
        let rel = path
            .strip_prefix(&root)
            .unwrap()
            .to_string_lossy()
            .replace('\\', "/");
        if rel == "mob_runtime.rs" || rel.starts_with("mob_runtime/") {
            continue;
        }
        let text = std::fs::read_to_string(path).unwrap();
        for (n, line) in text.lines().enumerate() {
            let code = code_of(line);
            if code.contains("EliteTuning {") || code.contains("EliteTuning{") {
                offending.push(format!(
                    "{rel}:{}: builds or destructures EliteTuning: {}",
                    n + 1,
                    line.trim()
                ));
            }
            for field in [".hp_mult", ".dmg_mult"] {
                let mut from = 0;
                while let Some(i) = code[from..].find(field) {
                    let at = from + i;
                    from = at + field.len();
                    if code[from..].chars().next().is_some_and(is_ident) {
                        continue; // `.hp_mult_of(`, `.hp_multiplier`, ...: another name
                    }
                    let recv = receiver(code, at);
                    let role =
                        recv.ends_with("role_def()") || (rel == "content/fold.rs" && recv == "row");
                    if role {
                        allowed_role_reads += 1;
                    } else {
                        offending.push(format!(
                            "{rel}:{}: reads `{recv}{field}`: {}",
                            n + 1,
                            line.trim()
                        ));
                    }
                }
            }
        }
    }
    assert!(
        allowed_role_reads >= 4,
        "the scan still sees the mob role's own multipliers (state.rs, sentenced.rs, content/fold.rs): {allowed_role_reads}"
    );
    assert!(
        offending.is_empty(),
        "elite multiplier fields read outside mob_runtime.rs; read EliteTuning::hp_mult_of(ty) / dmg_mult_of(ty) instead:\n{}",
        offending.join("\n")
    );
}
