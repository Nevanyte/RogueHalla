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

## 3a. Established facts (oracle author, 2026-11-20, base `claude/project-thread-baoxuk` @ 8331db79)
- `MatchStart::of` fills `tick_ms` from the constant (`rh-policy/src/lib.rs:97`), so a 25 ms sim's bots are told 50.
- `Seat::new` and `AsyncSeat::spawn` take `interval = spec.decision_interval_ticks.max(1)` as steps (`lib.rs:363`,
  `lib.rs:950`; due at `lib.rs:440`, `461`, `503`, `1098`, `1184`); `AsyncSeat` lateness is `tick > asked.tick +
  deadline_ticks` (`lib.rs:1074`). A spec is written in legacy 50 ms ticks (Brawler `2 + i % 3`, HumanBot 1), so at 25 ms
  the seat must scale by `50 / m.tick_ms` (2 at 25 ms): `decision_interval_ticks` and `deadline_ticks` keep their
  meaning as legacy ticks and the seat converts once with `m.tick_ms` (the sim's rate). Today a 25 ms bot decides twice
  per second what a 50 ms bot does.
- Other readers of the constant: `rh-headless/src/lib.rs:438` (command-delay drain `now + TICK_MS`),
  `tournament.rs:583` (tick cap from `max_sim_ms`), `tournament.rs:824` (`since_ms`), `chest_hunt.rs:38`
  (`hold_ms: 3. * TICK_MS` = 150 ms, becomes the literal 150.). `bots/bot.rs`, `brain_bot.rs`, `scripted.rs` and the
  policy test helpers build specs/starts with `TICK_MS`/`tick_ms: 50.`; at 50 ms they stay equal.
- `rh-headless/src/main.rs` ignores unknown options, so `--tick-ms 25` is silently a 50 ms run today. The output line
  must gain `"tickMs":<n>` (50 by default); an unsupported value (`rh_sim::statics::tick_ms_supported`) exits non-zero.
  `realTimeFactor` there multiplies ticks by `0.05`: it must use the step too.
- Legacy: the bot timings were tuned at `TICK_MS = 50` (`packages/shared/src/constants.ts:11-12`). No RNG draw changes
  at 50 ms. At 25 ms a bot that draws per decision draws at the same wall-clock rate once the interval is converted.

## 3b. Existing tests that pin what this packet touches (no conflict found)
- `rh-headless/tests/tick_ms_digest_guard.rs`: seed-1 state hash and outcome digest at 50 ms: must stay green.
- `rh-headless/tests/determinism_sweep.rs` and `tools/bot-*-fixtures`: 50 ms byte-exact.
- `rh-policy/src/tests.rs:87`: a 50 ms sim's probe sees `rh_sim::constants::TICK_MS` (50.) as `tick_ms`: stays equal.
- `rh-policy/src/tests.rs:39,124`, `async_session_tests.rs:10` build specs with intervals 1, 25 and 100 ticks and drive them
  at 50 ms: unchanged at 50 ms.

## 4. New paths
- tests beside the code they cover

## 5. Exclusive write scope
- `crates/rh-policy/src/**` (non-test files that read the step; tests that build a spec with the constant)
- `crates/rh-headless/src/lib.rs`, `crates/rh-headless/src/tournament.rs`, `crates/rh-headless/src/main.rs` (the option)
- NOT in scope: `crates/rh-policy/src/delivery/collections.rs`. Its forced-full rule is counted in steps and belongs to
  P-TICK-3; leave it untouched here (a `rg 'TICK_MS|tick_ms' crates/rh-policy/src` hit there is not this packet's).

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
- `crates/rh-policy/tests/tick_ms_seat.rs` (public API; red by assertion):
  - `a_match_start_carries_the_sims_step`: red, `MatchStart::of` on a 25 ms sim says 50 (want 25); a 50 ms sim says 50.
  - `a_seat_decides_per_second_at_the_same_rate_at_both_steps`: red, a Brawler(2) seat on a 25 ms sim decides every 2
    steps (40 per 2 s), twice the 50 ms seat (want within 1); also an `AsyncSeat` deadline of 2 legacy ticks.
- `crates/rh-headless/tests/tick_ms_bots.rs` (red by assertion):
  - `bots_decide_the_same_number_of_times_per_second_at_25_and_50_ms`: red, a 120 s seed-1 Brawler and HumanBot match
    decides twice as often per bot per second at 25 ms (want within 1 per second, totals within 10%).
- `crates/rh-headless/tests/tick_ms_cli.rs` (red by assertion; runs the built `rh-headless` binary):
  - `the_tick_ms_option_runs_a_25_ms_match_deterministically`: red, output has no `tickMs` key and a `--tick-ms 25`
    run equals the 50 ms run (want `"tickMs":25`, a different `stateHashFold`, identical over two runs).
  - `an_unsupported_tick_ms_is_refused`: red, `--tick-ms 30` exits 0 today (want non-zero).

## 9. Validation commands
- `cargo test -p rh-policy`, `cargo test -p rh-headless`, `cargo run --release -p rh-headless -- --matches 4 --players 8
  --map parity/maps/seed1.map.json --max-ticks 4000`

## 10. Risk and suggested model tier
Medium: single crate pair, mostly conversion. Sonnet, with an Opus review because bot timing decides outcomes.

## 11. Known traps
- A decision interval of 1 tick at 50 ms becomes 2 ticks at 25 ms, not 1: bots must not react twice as fast.
- `tools/bot-*-fixtures` are oracles: never regenerate them here.

## Notes from previous attempts
