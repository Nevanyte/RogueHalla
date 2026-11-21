# P-TICK-1b-perf: win back the instructions P-TICK-1b added at 50 ms

- **Serves:** not in the plan: owner approval 2026-11-19 (ADR 0029); supports ORDER_OF_OPERATIONS phase 3, item 16a
- **Implementer tier:** implementer-opus (rh-sim hot paths)
- **Reviewer:** reviewer-opus
- **Integration base:** claude/project-thread-baoxuk (PR #77)
- **D-rules touched:** D1 byte-exact at 50 ms · D2 n/a · D5/D6/D9 none

## 1. ID and outcome
CI perfproof on PR #77 (head b8911354, base feature/unity-rework) fails the instruction gate (`--max-ir-ratio 1.03`):
wire_seed1_8bots 1.119, headless_seed1_8bots 1.085, headless_seed1_recorded 1.074, verify_replay 1.070. PR #72
(P-TICK-1a) passed, so the cost is P-TICK-1b/1b-fix: reading the step through config (`k.tick_ms()`,
`steps_of_legacy_ticks`, `u32 -> f64` conversions, divisions) inside per-entity loops. When this lands every perfproof
case is at most 1.03 of the base, with nothing observable changed.

## 5. Exclusive write scope
- `crates/rh-sim/src/**` except oracle test files (`tests/tick_ms.rs`, `tests/tick_rate.rs`,
  `systems/tick_rate_tests.rs`, `item_view/tick_rate_tests.rs`, `actor_refs/tests.rs` 25 ms test)

## 7. Behaviour to preserve
- Every state hash, parity fixture and digest guard; all 25 ms tests.

## 8. Acceptance checks
- `python3 tools/perfproof/perfproof.py` (as `.github/workflows/perfproof.yml` runs it, base = merge-base with
  feature/unity-rework) reports every Ir ratio <= 1.03 and no allocation increase.
- `scripts/gate.sh --quick --keep-going --base claude/project-thread-baoxuk` passes.

## 11. Hints
- Measure first (callgrind A/B, `callgrind_annotate --diff` or per-function totals) before changing anything.
- Likely remedies: cache the step once per tick (`let step = k.tick_ms();` outside loops), store precomputed `f64` step
  and step counts in `MatchStatic` at construction, avoid recomputing `steps_of_legacy_ticks` per entity.
