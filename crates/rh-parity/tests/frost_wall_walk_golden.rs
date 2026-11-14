//! P-FROST-WALL-PREDICT-1: protocol/fixtures/frost_wall_walk_v1.json must be exactly what the current core's walk round
//! Frost Walls produces (`crates/rh-parity/src/frost_wall_walk_goldens.rs`, which also asserts each case still shows its
//! event: the wall's tiles, the gap, the push, the stop against the ice, the re-plan). Regenerate with
//! `cargo run -p rh-parity --bin frost-wall-walk-fixtures`.

use rh_parity::frost_wall_walk_goldens::all;

fn repo(relative: &str) -> std::path::PathBuf {
    std::path::Path::new(env!("CARGO_MANIFEST_DIR"))
        .join("../..")
        .join(relative)
}

#[test]
fn committed_frost_wall_walk_golden_is_the_current_core() {
    for (path, want) in all() {
        let got = std::fs::read_to_string(repo(path)).unwrap_or_else(|e| panic!("{path}: {e}"));
        assert!(
            got == want,
            "{path} differs from the current core: run cargo run -p rh-parity --bin frost-wall-walk-fixtures"
        );
    }
}
