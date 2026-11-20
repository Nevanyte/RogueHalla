# P-TICK-2: bots and the headless runner read the sim's step

- **Serves:** not in the plan: owner approval 2026-11-19 (ADR 0029); supports ORDER_OF_OPERATIONS phase 3, item 16a
- **Implementer tier:** implementer-sonnet
- **Reviewer:** reviewer-opus (policy timing is outcome-bearing)
- **Integration base:** feature/unity-rework
- **D-rules touched:** D1 byte-exact at 50 ms (bot fixtures unchanged) · D2 n/a · D5 none · D9 bots never use the walk
  predictor (unchanged)

## 1. ID and outcome
Bots act through the seat contract on a sim that may step at 25 ms. Their timings keep wall-clock meaning: the seat's
`tick_ms` (`crates/rh-policy/src/lib.rs`, today filled from the constant) comes from the sim; `decision_interval_ticks`
and `deadline_ticks` are converted from milliseconds with the sim's rate; module holds written as multiples of the step
(for example `chest_hunt.rs` `hold_ms: 3. * TICK_MS`) become fixed milliseconds equal to their value at 50 ms. The
headless runner and tournament read the sim's rate and accept a `--tick-ms 50|25` option (default 50).

## 2. Dependencies
- P-TICK-1b merged

## 3. Relevant legacy paths
- `packages/shared/src/constants.ts:11-12`: the legacy step the bot timings were tuned at

## 4. New paths
- tests beside the code they cover

## 5. Exclusive write scope
- `crates/rh-policy/src/**` (non-test files that read the step; tests that build a spec with the constant)
- `crates/rh-headless/src/lib.rs`, `crates/rh-headless/src/tournament.rs`, `crates/rh-headless/src/main.rs` (the option)

## 6. Contracts and references
- C-INPUT (validate + submit only), C-OBS, C-DET; `crates/rh-policy/AGENTS.md`

## 7. Behaviour to preserve
- At 50 ms: every bot fixture under `tools/bot-*-fixtures` and the headless determinism sweep, byte for byte.

## 8. Acceptance checks
- `scripts/gate.sh --oracle packet/P-TICK-2` passes (full).
- A 120 s seed-1 bot match at 25 ms makes the same number of decisions per bot per second as at 50 ms (within 1), and
  `rh-headless --tick-ms 25 --matches 4 --players 8 --map parity/maps/seed1.map.json --max-ticks 8000` finishes with
  no panic and a deterministic result over two runs.

## Oracle files (read-only for implementers)
- To be written by oracle-author on `packet/P-TICK-2`.

## 9. Validation commands
- `cargo test -p rh-policy`, `cargo test -p rh-headless`, `cargo run --release -p rh-headless -- --matches 4 --players 8
  --map parity/maps/seed1.map.json --max-ticks 4000`

## 10. Risk and suggested model tier
Medium: single crate pair, mostly conversion. Sonnet, with an Opus review because bot timing decides outcomes.

## 11. Known traps
- A decision interval of 1 tick at 50 ms becomes 2 ticks at 25 ms, not 1: bots must not react twice as fast.
- `tools/bot-*-fixtures` are oracles: never regenerate them here.

## Notes from previous attempts
