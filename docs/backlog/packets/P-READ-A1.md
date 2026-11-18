# P-READ-A1: READMEs for seven crates

- **Serves:** owner direction 2026-11-16, the readability sweep ("Yes, both"); standard `docs/agent/READABILITY.md`.
- **Tier:** `implementer-sonnet`. Reviewer: reviewer (Sonnet).
- **Write scope:** `crates/{rh-alloc-count,rh-headless,rh-mapdress,rh-parity,rh-policy,rh-protocol,rh-server}/README.md` (new).
- **Base:** `coord/int-56` @ `907ab240e`.

## What

Write one README per crate, in the standard's README shape. Read the crate's `lib.rs`/`main.rs`, its `AGENTS.md` if any, and `Cargo.toml` first; the crate map in the root `AGENTS.md` gives one line on each. Name real entry-point files and the exact test command (`cargo test -p <crate>` plus any feature or Postgres note the crate needs). The four crates that already have a README (`rh-sim`, `rh-persist`, `rh-placement`, `rh-load`) show the house tone; do not edit them.

## How (all P-READ packets)

Follow `docs/agent/READABILITY.md` exactly: what to write, the move-only rules, frozen files, the comparator's
constants, and the proof. This packet changes no behaviour. Its oracle is `scripts/readability-allowlist.txt` with the
lines listed below removed (committed on `packet/P-READ-A1`); the gate's readability step is red until the work is done.
Never edit the allowlist (it is the oracle file) or add lines to it.

- Branch: `agent/sonnet/P-READ-A1` from `packet/P-READ-A1`; first command in your worktree:
  `git checkout -B agent/sonnet/P-READ-A1 packet/P-READ-A1`.
- Iterate with `scripts/gate.sh --quick --base coord/int-56`; finish with
  `scripts/gate.sh --quick --base coord/int-56 --oracle packet/P-READ-A1`. Prefix cargo and gate commands with
  `export PATH="$HOME/.cargo/bin:$PATH"`.
- A red parity, golden, hash, digest, pin or test-expectation step means a change altered behaviour: undo it. Never
  refresh a fixture, pin or expectation in a P-READ packet.
- Hand-off: the AGENTS.md report, plus the `readability: OK (...)` line, and for a split the output of
  `git diff --stat coord/int-56` and every changed line that is not a move (expected: `mod`, `use`, `pub use`, visibility,
  `#[cfg(test)] mod tests;`, comments).

## Allowlist lines this packet removes (its oracle)

```
readme crates/rh-alloc-count
readme crates/rh-headless
readme crates/rh-mapdress
readme crates/rh-parity
readme crates/rh-policy
readme crates/rh-protocol
readme crates/rh-server
```
