# P-READ-B5: Split rh-sim content.rs and observe.rs

- **Serves:** owner direction 2026-11-16, the readability sweep ("Yes, both"); standard `docs/agent/READABILITY.md`.
- **Tier:** `implementer-sonnet`. Reviewer: reviewer-opus.
- **Write scope:** the files named below, the new child files beside them, and their parent `mod` declarations; nothing outside their directories.
- **Base:** `coord/int-56` @ `a8d46a9f5`.

## What

Split each file named below by the standard's move-only rules, so each is under the limit (1,500 lines Rust, 800 C#) and every new file is under 800. Move inline tests out first. Split along the seams a reader looks for and say in the hand-off why you chose them. Shared files (merge one at a time). The constants and enums `tools/determinism/compare.py` reads by regex from these two files stay in them (grep `compare.py` for the names). The content hash and the state hash must not move: the gate's rh-sim suite and parity report are the proof. The owner asked for Sonnet sub-agents for this sweep; a move-only split changes no state, tick, RNG or hash behaviour, so it is Sonnet work with an Opus review, escalating to `implementer-opus` per the ladder if it fails twice.

## How (all P-READ packets)

Follow `docs/agent/READABILITY.md` exactly: what to write, the move-only rules, frozen files, the comparator's
constants, and the proof. This packet changes no behaviour. Its oracle is `scripts/readability-allowlist.txt` with the
lines listed below removed (committed on `packet/P-READ-B5`); the gate's readability step is red until the work is done.
Never edit the allowlist (it is the oracle file) or add lines to it.

- Branch: `agent/sonnet/P-READ-B5` from `packet/P-READ-B5`; first command in your worktree:
  `git checkout -B agent/sonnet/P-READ-B5 packet/P-READ-B5`.
- Iterate with `scripts/gate.sh --quick --base coord/int-56`; finish with
  `scripts/gate.sh --quick --base coord/int-56 --oracle packet/P-READ-B5`. Prefix cargo and gate commands with
  `export PATH="$HOME/.cargo/bin:$PATH"`.
- A red parity, golden, hash, digest, pin or test-expectation step means a change altered behaviour: undo it. Never
  refresh a fixture, pin or expectation in a P-READ packet.
- Hand-off: the AGENTS.md report, plus the `readability: OK (...)` line, and for a split the output of
  `git diff --stat coord/int-56` and every changed line that is not a move (expected: `mod`, `use`, `pub use`, visibility,
  `#[cfg(test)] mod tests;`, comments).

## Allowlist lines this packet removes (its oracle)

```
size crates/rh-sim/src/content.rs 1684
size crates/rh-sim/src/observe.rs 1592
```
