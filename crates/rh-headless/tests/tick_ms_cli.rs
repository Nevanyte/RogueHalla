//! P-TICK-2 (ADR 0029): `rh-headless --tick-ms 50|25` (default 50) runs the match on a sim of that step, deterministically,
//! and refuses any other value. Runs the built binary.
use std::process::{Command, Output};

fn run(extra: &[&str]) -> Output {
    Command::new(env!("CARGO_BIN_EXE_rh-headless"))
        .args(["--matches", "1", "--players", "8", "--max-ticks", "600"])
        .args([
            "--map",
            concat!(
                env!("CARGO_MANIFEST_DIR"),
                "/../../parity/maps/seed1.map.json"
            ),
        ])
        .args(extra)
        .output()
        .expect("run rh-headless")
}

fn json(out: &Output) -> serde_json::Value {
    assert!(
        out.status.success(),
        "{}",
        String::from_utf8_lossy(&out.stderr)
    );
    serde_json::from_slice(&out.stdout).expect("one JSON line")
}

#[test]
fn the_tick_ms_option_runs_a_25_ms_match_deterministically() {
    let default = json(&run(&[]));
    assert_eq!(default["tickMs"], 50, "default step; output: {default}");
    assert_eq!(
        json(&run(&["--tick-ms", "50"]))["stateHashFold"],
        default["stateHashFold"]
    );

    let a = json(&run(&["--tick-ms", "25"]));
    let b = json(&run(&["--tick-ms", "25"]));
    assert_eq!(a["tickMs"], 25, "output: {a}");
    assert_eq!(a["ticks"], 600);
    assert_eq!(a["stateHashFold"], b["stateHashFold"], "two runs agree");
    assert_ne!(
        a["stateHashFold"], default["stateHashFold"],
        "a 25 ms match is not the 50 ms match"
    );
}

#[test]
fn an_unsupported_tick_ms_is_refused() {
    for bad in ["30", "0", "abc"] {
        assert!(
            !run(&["--tick-ms", bad]).status.success(),
            "--tick-ms {bad} must be refused"
        );
    }
}
