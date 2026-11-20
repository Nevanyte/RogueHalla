# P-TICK-6: measure 25 ms on full maps, then make it the live default

- **Serves:** not in the plan: owner approval 2026-11-19 (ADR 0029); supports ORDER_OF_OPERATIONS phase 3, item 16a
- **Implementer tier:** coordinator (measurement and a one-line default)
- **Reviewer:** reviewer-opus (integration merge)
- **Integration base:** feature/unity-rework
- **D-rules touched:** D1 n/a · D2 n/a · D5 none · D7 the weak-hardware gate later measures the 40 Hz client

## 1. ID and outcome
With P-TICK-1a to 5 merged, measure a 25 ms room: full-map step cost and rooms per core
(`crates/rh-headless/examples/full_map_cost.rs`, whose budget already reads the step), bandwidth per client (typical
and heavy), the density bench, and click-to-confirmed latency in the browser proof (`tools/latency`). If the step p99
stays under 25 ms with room for wake lateness and the heavy case stays under 20 KB/s, flip the server's default to 25.
Record the numbers in `docs/evidence/forty-hertz/README.md` and the MIGRATION checkpoint.

## 2. Dependencies
- P-TICK-2, P-TICK-4, P-TICK-5 merged

## 3. Relevant legacy paths
- none

## 4. New paths
- `docs/evidence/forty-hertz/README.md`

## 5. Exclusive write scope
- the server's default `tick_ms`; `docs/evidence/forty-hertz/`; `docs/MIGRATION.md` (coordinator)

## 6. Contracts and references
- ADR 0029; R-OPT-2 (record cost, mechanism, risk, before and after)

## 7. Behaviour to preserve
- Parity and every fixture at 50 ms.

## 8. Acceptance checks
- `scripts/gate.sh --coordinator --keep-going` green on the merged tree at both rates.
- The evidence README holds before and after numbers for step mean and p99, rooms per core, bandwidth and latency.

## Oracle files (read-only for implementers)
- The measurements themselves; the flip is gated on them.

## 9. Validation commands
- `cargo run --release -p rh-headless --example full_map_cost`, `cargo run --release -p rh-server --bin densitybench`,
  the browser proof per `tools/latency/README.md`

## 10. Risk and suggested model tier
Low in code, high in consequence: a staging deploy needs the owner's approval per action.

## 11. Known traps
- Measure on a quiet machine; earlier runs on a loaded host read several times slower.

## Notes from previous attempts
