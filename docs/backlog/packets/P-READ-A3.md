# P-READ-A3: Rust module headers

- **Serves:** owner direction 2026-11-16, the readability sweep ("Yes, both"); standard `docs/agent/READABILITY.md`.
- **Tier:** `implementer-sonnet`. Reviewer: reviewer (Sonnet); comments only.
- **Write scope:** the files named below, `//!` lines only.
- **Base:** `coord/int-56` @ `a8d46a9f5`.

## What

Add a `//!` header to each file named below, in the standard's shape. Read each file (and its `mod` parent) before writing; say what it is for and who uses it, and name the legacy source when the file ports one (the existing headers in sibling files show how this crate cites `packages/`). The diff must consist of added `//!` lines only (and a blank line after them where rustfmt wants one).

## How (all P-READ packets)

Follow `docs/agent/READABILITY.md` exactly: what to write, the move-only rules, frozen files, the comparator's
constants, and the proof. This packet changes no behaviour. Its oracle is `scripts/readability-allowlist.txt` with the
lines listed below removed (committed on `packet/P-READ-A3`); the gate's readability step is red until the work is done.
Never edit the allowlist (it is the oracle file) or add lines to it.

- Branch: `agent/sonnet/P-READ-A3` from `packet/P-READ-A3`; first command in your worktree:
  `git checkout -B agent/sonnet/P-READ-A3 packet/P-READ-A3`.
- Iterate with `scripts/gate.sh --quick --base coord/int-56`; finish with
  `scripts/gate.sh --quick --base coord/int-56 --oracle packet/P-READ-A3`. Prefix cargo and gate commands with
  `export PATH="$HOME/.cargo/bin:$PATH"`.
- A red parity, golden, hash, digest, pin or test-expectation step means a change altered behaviour: undo it. Never
  refresh a fixture, pin or expectation in a P-READ packet.
- Hand-off: the AGENTS.md report, plus the `readability: OK (...)` line, and for a split the output of
  `git diff --stat coord/int-56` and every changed line that is not a move (expected: `mod`, `use`, `pub use`, visibility,
  `#[cfg(test)] mod tests;`, comments).

## Allowlist lines this packet removes (its oracle)

```
header crates/rh-headless/src/bin/determinism_sweep.rs
header crates/rh-headless/src/bin/determinism_sweep_support/mod.rs
header crates/rh-headless/src/bin/determinism_sweep_support/sha256.rs
header crates/rh-parity/src/canon.rs
header crates/rh-parity/src/mapping.rs
header crates/rh-policy/src/bots/temporal_percept/tactics/source_memory_encoder.rs
header crates/rh-server/src/outbox/scene_candidate/client14_fixture.rs
header crates/rh-sim/src/abilities/source_tests/compare.rs
header crates/rh-sim/src/abilities/source_tests/runner.rs
header crates/rh-sim/src/abilities/source_tests/setup.rs
header crates/rh-sim/src/initial_world/rules.rs
header crates/rh-sim/src/mapdata/validate.rs
header crates/rh-sim/src/mapgen/certify/diagnostics.rs
header crates/rh-sim/src/mapgen/certify/features.rs
header crates/rh-sim/src/mapgen/certify/mod.rs
header crates/rh-sim/src/mapgen/certify/terrain.rs
header crates/rh-sim/src/mapgen/landmarks/recipes/search_family/gallows.rs
header crates/rh-sim/src/mapgen/landmarks/recipes/search_family/scorch.rs
header crates/rh-sim/src/mapgen/landmarks/runtime/geometry.rs
header crates/rh-sim/src/mapgen/landmarks/search/tables.rs
header crates/rh-sim/src/mapgen/records/rules.rs
header crates/rh-sim/src/mapgen/style_features/fields.rs
header crates/rh-sim/src/mapgen/style_features/geometry.rs
header crates/rh-sim/src/mapgen/style_features/graphs.rs
header crates/rh-sim/src/mapgen/style_features/rules.rs
header crates/rh-sim/src/mapgen/style_features/search.rs
header crates/rh-sim/src/mapgen/style_gallows/rows.rs
header crates/rh-sim/src/mapgen/style_gallows/shape.rs
header crates/rh-sim/src/mapgen/style_rime/lakes.rs
header crates/rh-sim/src/mapgen/style_rime/routes.rs
header crates/rh-sim/src/mapgen/style_rime/shape.rs
header crates/rh-sim/src/mapgen/style_runtime/connectivity.rs
header crates/rh-sim/src/mapgen/style_runtime/route.rs
header crates/rh-sim/src/mapgen/style_runtime/transactions.rs
header crates/rh-sim/src/mapgen/style_scorch/craters.rs
header crates/rh-sim/src/mapgen/style_scorch/raster.rs
header crates/rh-sim/src/mapgen/style_scorch/river.rs
header crates/rh-sim/src/mapgen/style_warren/planning.rs
header crates/rh-sim/src/mapgen/style_warren/shape.rs
header crates/rh-sim/src/mapgen/zones/growth.rs
header crates/rh-sim/src/mapgen/zones/metrics.rs
header crates/rh-sim/src/stat_fold/boost.rs
header crates/rh-sim/src/stat_fold/definition.rs
header crates/rh-sim/src/stat_fold/fold.rs
header crates/rh-sim/src/stat_fold/js.rs
header crates/rh-sim/src/stat_fold/keys.rs
header crates/rh-sim/src/stat_fold/mirror.rs
header crates/rh-sim/src/stat_fold/rows.rs
header crates/rh-sim/src/status_runtime/mutation.rs
header crates/rh-sim/src/status_runtime/readers.rs
header crates/rh-sim/src/status_runtime/rules.rs
```
