# P-READ-B3: Split rh-protocol lib.rs, encode.rs and map.rs

- **Serves:** owner direction 2026-11-16, the readability sweep ("Yes, both"); standard `docs/agent/READABILITY.md`.
- **Tier:** `implementer-sonnet`. Reviewer: reviewer-opus.
- **Write scope:** the files named below, the new child files beside them, and their parent `mod` declarations; nothing outside their directories.
- **Base:** `coord/int-56` @ `a8d46a9f5`.

## What

Split each file named below by the standard's move-only rules, so each is under the limit (1,500 lines Rust, 800 C#) and every new file is under 800. Move inline tests out first. Split along the seams a reader looks for and say in the hand-off why you chose them. The wire format must not change: `protocol/fixtures/` stays byte-identical, and the Rust, C# and JS decoder steps of the gate stay green. `PROTOCOL_VERSION` stays in `lib.rs`. Runs before P-READ-B4 (same crate).

## How (all P-READ packets)

Follow `docs/agent/READABILITY.md` exactly: what to write, the move-only rules, frozen files, the comparator's
constants, and the proof. This packet changes no behaviour. Its oracle is `scripts/readability-allowlist.txt` with the
lines listed below removed (committed on `packet/P-READ-B3`); the gate's readability step is red until the work is done.
Never edit the allowlist (it is the oracle file) or add lines to it.

- Branch: `agent/sonnet/P-READ-B3` from `packet/P-READ-B3`; first command in your worktree:
  `git checkout -B agent/sonnet/P-READ-B3 packet/P-READ-B3`.
- Iterate with `scripts/gate.sh --quick --base coord/int-56`; finish with
  `scripts/gate.sh --quick --base coord/int-56 --oracle packet/P-READ-B3`. Prefix cargo and gate commands with
  `export PATH="$HOME/.cargo/bin:$PATH"`.
- A red parity, golden, hash, digest, pin or test-expectation step means a change altered behaviour: undo it. Never
  refresh a fixture, pin or expectation in a P-READ packet.
- Hand-off: the AGENTS.md report, plus the `readability: OK (...)` line, and for a split the output of
  `git diff --stat coord/int-56` and every changed line that is not a move (expected: `mod`, `use`, `pub use`, visibility,
  `#[cfg(test)] mod tests;`, comments).

## Allowlist lines this packet removes (its oracle)

```
size crates/rh-protocol/src/encode.rs 1958
size crates/rh-protocol/src/lib.rs 1894
size crates/rh-protocol/src/map.rs 1786
```
