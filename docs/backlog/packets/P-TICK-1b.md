# P-TICK-1b: every sim system reads the sim's step; tick-counted rules keep their milliseconds

- **Serves:** not in the plan: owner approval 2026-11-19 (ADR 0029); supports ORDER_OF_OPERATIONS phase 3, item 16a
- **Implementer tier:** implementer-opus (`rh-sim` tick and timing)
- **Reviewer:** reviewer-opus
- **Integration base:** feature/unity-rework
- **D-rules touched:** D1 byte-exact at 50 ms; loose proof at 25 ms (owner direction 2026-11-10) · D2 n/a · D5/D6/D9 none

## 1. ID and outcome
Every use of the step constant in `rh-sim` systems (about 215; `rg 'TICK_MS|TICK_RATE' crates/rh-sim/src`) reads the
sim's own `tick_ms` from P-TICK-1a: movement, weapons, projectiles, DoTs (fire, plague), regen, frost stretch, targeting,
walk hint, ledger caps, arena rules, ability and card checkpoints, mobs. The tick-counted constants become milliseconds
(ADR 0029 decision 3): `WALK_HINT_MAX_TICKS` 3 → 150 ms, `MOB_SPACING_LOOK_TICKS` 4 → 200 ms, converted with the sim's
rate. `MAX_ACTIONS_PER_TICK` stays per step. The deprecated `TICK_MS`/`TICK_RATE` aliases are removed from `rh-sim`.
When this lands a 25 ms sim plays the same game per second as a 50 ms one, within one step.

## 2. Dependencies
- P-TICK-1a merged

## 3. Relevant legacy paths
- `packages/shared/src/walk.ts:231-253`: walking in substeps of at most `TICK_MS` (the server calls it with one tick)
- `packages/shared/src/constants.ts:281-283`: Festering counts its own 1000 ms period (unchanged)

## 4. New paths
- tests beside the code they cover

## 5. Exclusive write scope
- `crates/rh-sim/src/systems/**`, `crates/rh-sim/src/abilities/**`, `crates/rh-sim/src/cards/**`,
  `crates/rh-sim/src/constants.rs` (the tick-counted rows only, and removing the aliases), and any other `rh-sim` file
  whose only change is reading the sim's `tick_ms` instead of the constant (list them in the hand-off)

## 6. Contracts and references
- C-TICK as amended, C-DET, C-PARITY, C-INPUT (order budget stays on sim time)

## 7. Behaviour to preserve
- At 50 ms: every parity fixture and state hash, byte for byte. RNG draw order. The schedule.

## 8. Acceptance checks
- `scripts/gate.sh --oracle packet/P-TICK-1b` passes (full).
- The P-TICK-1a digest guard still holds at 50 ms.
- At 25 ms: a walker on an open floor covers `PLAYER_SPEED` per second within 1 u; a bow's first release and its next
  ready time land within one step (25 ms) of the 50 ms run; a burn deals the same total damage over its duration
  within 1e-6 relative; the walk hint commits for 150 ms (6 steps).

## Oracle files (read-only for implementers)
- To be written by oracle-author on `packet/P-TICK-1b` (a `tick_rate_tests.rs` in `rh-sim/src/systems`).

## 9. Validation commands
- `cargo test -p rh-sim`, `cargo test -p rh-parity`, `scripts/gate.sh --quick --oracle packet/P-TICK-1b`

## 10. Risk and suggested model tier
High: wide, timing-sensitive edits in the tick. Opus. If the diff passes about 600 lines, the coordinator splits it by
system family (movement and weapons; status and DoTs; abilities and cards) with the same oracle.

## 11. Known traps
- Per-step rates hidden as plain numbers (a factor applied once per tick without `tick_ms`): grep each system for
  multiplications by a constant inside the step and convert those too, or the 25 ms run doubles them.
- `% tick_ms` checks in `abilities/checkpoint.rs` and `cards/fight_clock.rs` assume every recorded time is on the grid;
  at 25 ms a time recorded at 50 ms is still on it, not the other way round.
- `plague_zombies.rs` `now % PLAGUE_ZOMBIE_REGEN_STEP_MS >= TICK_MS` fires once per window only if the window is a
  multiple of the step: check it at 25.

## Carried from the P-TICK-1a review (reviewer-opus LAND, 2026-11-20)
- `History::record()` still trims with the legacy step; move `movement.rs` to `record_at_step` with the sim's step, and
  consider `pub(crate)` for `record_at_step` (its `tick_ms` is checked only by a `debug_assert`).
- No test refuses a too-dense 25 ms rewind history (the existing dense test runs at 50 only).
- The `% TICK_MS` checks in `abilities/checkpoint.rs` and `cards/fight_clock.rs` must read the sim's step, or 25 ms
  checkpoints at some ticks are refused.
- Registry rows keep the names `TICK_RATE`/`TICK_MS` (content hash), `LEGACY_*` are aliases: confirmed by the
  coordinator.

## Notes from previous attempts
