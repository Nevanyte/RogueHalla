# P-TICK-1a: the sim's step length is a setting (50 or 25 ms) in the core, save, restore and hash

- **Serves:** not in the plan: owner approval 2026-11-19 (ADR 0029); supports ORDER_OF_OPERATIONS phase 3, item 16a
- **Implementer tier:** implementer-opus (`rh-sim` state, tick, save/hash)
- **Reviewer:** reviewer-opus
- **Integration base:** feature/unity-rework
- **D-rules touched:** D1 byte-exact at 50 ms (every parity fixture and state hash unchanged) · D2 n/a · D5/D6/D9 none

## 1. ID and outcome
A sim can be built with `SimConfig::tick_ms` 50 (default) or 25. Sim time advances by the sim's own value each step, the
value is saved, restored and folded into the state hash, and a sim reports it (`Sim::tick_ms()`, and through the world
for systems). History rings that are sized from the tick (`state.rs` `history_cap`, the vision trail in `item_view.rs`)
are sized for the shortest allowed step. Systems still read the old constant after this packet; P-TICK-1b moves them.
With the default, nothing observable changes.

## 2. Dependencies
- P-TICK-0 (ADR 0029 accepted)

## 3. Relevant legacy paths
- `packages/shared/src/constants.ts:11-13`: the legacy step (20 Hz, 50 ms)

## 4. New paths
- tests beside the code they cover

## 5. Exclusive write scope
- `crates/rh-sim/src/statics.rs` (`SimConfig`), `crates/rh-sim/src/sim.rs` (the advance and accessor),
  `crates/rh-sim/src/state.rs` (`history_cap`, the ring trim), `crates/rh-sim/src/constants.rs` (rename to
  `LEGACY_TICK_MS`/`LEGACY_TICK_RATE` with `TICK_MS`/`TICK_RATE` kept as deprecated aliases until P-TICK-1b),
  `crates/rh-sim/src/hash.rs`, `crates/rh-sim/src/checkpoint.rs` and `checkpoint/`, `crates/rh-sim/src/replay.rs`,
  `crates/rh-sim/src/item_view.rs` (ring size only), `crates/rh-sim/src/systems/arena_validation.rs` (the
  `server_time == tick * tick_ms` check reads the sim's value)

## 6. Contracts and references
- C-TICK (as amended by ADR 0029), C-DET, C-PARITY; ADR 0007; `hash.rs` module docs on config fields that add no word

## 7. Behaviour to preserve
- At 50 ms: every parity fixture, every state hash, `CHECKPOINT_FORMAT` and `REPLAY_FORMAT` contents for existing files.
  The new field must add no word to the digest and no key to a checkpoint when it is 50 (the pattern `hash.rs` already
  uses for `mob_spacing`, `regen_in_combat`, `camp_buffs`).
- The schedule (system order) is untouched.

## 8. Acceptance checks
- `scripts/gate.sh --oracle packet/P-TICK-1a` passes (full).
- Digest guard: the seed-1 headless run's state hash at tick 4000 equals the value pinned on `packet/P-TICK-1a`.
- A 25 ms sim: `server_time == tick * 25` after 10,000 steps; save and restore round-trip with the same hash; the hash
  differs from the same world at 50 ms.
- Construction with any other `tick_ms` (for example 33) is refused.

## Oracle files (read-only for implementers)
- To be written by oracle-author on `packet/P-TICK-1a` (a test module beside `sim.rs` and a digest-guard test).

## 9. Validation commands
- `cargo test -p rh-sim`, `cargo test -p rh-parity`, `scripts/gate.sh --quick --oracle packet/P-TICK-1a`

## 10. Risk and suggested model tier
High: state, save and hash. Opus.

## 11. Known traps
- A serde default alone is not enough: a field that serializes at 50 changes every checkpoint and digest. Skip it at 50.
- `history_cap()` is a `const fn` with a `const` assert; sizing it for 25 ms grows the ring, which must not change which
  samples a rewind at 50 ms picks (the trim is by time, `now - MAX_REWIND_MS - tick_ms`).
- `server_time` stays `f64`; at 25 ms the sum stays exact (25 is a binary-exact value), so no move to integer time.

## Notes from previous attempts
