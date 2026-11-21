# P-TICK-1b-fix: two reviewer notes from P-TICK-1b (25 ms scenario racks, handle grace windows)

- **Serves:** not in the plan: owner approval 2026-11-19 (ADR 0029); supports ORDER_OF_OPERATIONS phase 3, item 16a
- **Implementer tier:** implementer-opus (same tier as P-TICK-1b)
- **Reviewer:** reviewer-opus
- **Integration base:** claude/project-thread-baoxuk (into feature/unity-rework through PR #72)
- **D-rules touched:** D1 byte-exact at 50 ms · D2 n/a · D5/D6/D9 none

## 1. ID and outcome
1. After P-TICK-1b, `Sim::reset_active` steps to 50 ms of sim time at every rate, so a 25 ms sim is at tick 2 before any
   input, and `systems/items.rs:210` (`self.w.tick > 1`) refuses every scenario weapon rack. When this lands the check
   compares sim time (or `steps_of_legacy_ticks(1)`), and a 25 ms sim seeds a rack after `reset_active`.
2. The handle grace windows count steps: `item_view.rs:13` `GRACE: u32 = 20` (uses :495, :685, :785; retired-ring bound
   :1187) and `reward_chests/observer.rs:214,324` literal `20`. At 25 ms they halve to 500 ms. When this lands they keep
   1000 ms (ADR 0029 decision 3) via `steps_of_legacy_ticks(20)`.

## 2. Dependencies
- P-TICK-1b merged (7e0e35dc)

## 5. Exclusive write scope
- `crates/rh-sim/src/systems/items.rs`, `crates/rh-sim/src/item_view.rs`, `crates/rh-sim/src/reward_chests/observer.rs`,
  `crates/rh-sim/src/actor_refs.rs` (added by the coordinator 2026-11-20: it shares `item_view::GRACE`),
  tests beside them

## 7. Behaviour to preserve
- At 50 ms: every parity fixture and state hash (digest guard `crates/rh-headless/tests/tick_ms_digest_guard.rs`).

## 8. Acceptance checks
- Implementer writes the failing tests first (small fix from review): a 25 ms sim seeds a scenario rack after
  `reset_active`; at 25 ms a handle retired 30 steps (750 ms) earlier still resolves, and one retired 41 steps earlier
  does not. Both red before the fix, green after; quick gate with the oracle passes.

## 10. Risk
Low to medium; two small edits in rh-sim. Opus because it is the same tick family.
