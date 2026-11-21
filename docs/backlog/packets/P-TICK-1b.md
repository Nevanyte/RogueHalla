# P-TICK-1b: every sim system reads the sim's step; tick-counted rules keep their milliseconds

- **Serves:** not in the plan: owner approval 2026-11-19 (ADR 0029); supports ORDER_OF_OPERATIONS phase 3, item 16a
- **Implementer tier:** implementer-opus (`rh-sim` tick and timing)
- **Reviewer:** reviewer-opus
- **Integration base:** claude/project-thread-baoxuk (P-TICK-1a merged; PR #74 merged at 7abe714b)
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
- `packages/shared/src/constants.ts:11-12,33,235`: `TICK_RATE = 20`, `TICK_MS = 1000 / TICK_RATE`,
  `WALK_HINT_MAX_TICKS = 3`, `PVP_DOT_CAP_SHARE = 0.15` (a per-second share, applied per step as `* TICK_MS / 1000`)
- `packages/server/src/sim/movement.ts:101-105`: `recordHistory` trims with `cutoff = now - MAX_REWIND_MS - TICK_MS`,
  keeping two; at 25 ms the cutoff reads the sim's step

## 3a. Established facts (oracle author, 2026-11-20, base `claude/project-thread-baoxuk` @ 7abe714b)
- Today a 25 ms sim advances its clock by 25 per step (P-TICK-1a) but every system still steps 50 ms of game: a
  walker covers 340 u/s instead of `PLAYER_SPEED` 170 (`systems/movement.rs:48`, `walk_one(.., TICK_MS, ..)`), passive
  regen heals twice its share (`systems/regen.rs:41`), a burn deals twice its dps (`systems/status/dot.rs:10`) and the
  PvP DoT cap is twice its share (`systems/ledger.rs:707`).
- Bow timing and arrow flight are already right at 25 ms on this base (green in the oracle, kept as pins): the first
  release, the next `ready_at` and the release-to-release gap land within 25 ms of the 50 ms run, and an arrow's
  `traveled` 400 ms after launch is equal at both rates (flight is time-based; `systems/projectiles.rs:554-556` substeps
  `TICK_MS` back from now, which overlaps the previous step at 25 ms but does not change the travel). The
  `ready_at.max(now - TICK_MS)` buffered-press rule (`systems/weapons.rs:169`) is not exercised by a held trigger.
- Walk hint: `systems/walk_hint.rs:92,100,105,115` step by `TICK_MS` and cap at `WALK_HINT_MAX_TICKS` (3). At 25 ms the
  cap must be 6 steps (150 ms). The checkpoint refuses `walk_ticks > WALK_HINT_MAX_TICKS` (`checkpoint.rs:706`): that
  bound must follow the sim's step too, or a 25 ms checkpoint with a 6-step hint is refused. Since PR #74 (merged into
  the base at 7abe714b) the hint reaches the wire (`observe.rs` `ActorView.walk`, `OBSERVATION_VERSION` 31, protocol 19)
  as a count of steps: at 25 ms the client reads up to 6 (P-TICK-4/5 read the rate; not this packet).
- Rewind history: `systems/movement.rs:162` calls `History::record` (legacy step 50, `state.rs:143`); a 25 ms walker
  keeps 15 samples, the oldest `now - 350`; with the sim's step the oldest is `now - MAX_REWIND_MS - 25 = now - 325`.
  The density check (`checkpoint.rs:811-825`) already reads `k.tick_ms()`: a 25 ms history denser than 25 ms is refused
  today (green pin for the carried review item).
- Odd-step checkpoint: a Frost Wish cast accepted at a 25 ms step that is not on the 50 ms grid (server time 325) makes
  every later checkpoint refuse with `invalid card Wish spend` (`abilities/checkpoint.rs:76`, `spend.at % TICK_MS`).
  The same `% TICK_MS` / `+ TICK_MS` rules are at `abilities/checkpoint.rs:9,12,29,54,63,101,161,165,182,236` and
  `cards/fight_clock.rs:82-83`; only the Wish spend is reached by the oracle's public setup.
- No RNG draw is added or moved by the oracle setups at 50 ms. One per-step draw to watch: the DoT crit roll
  (`status/dot.rs` `roll_dot_crit(.., tick, ..)`) rolls once per step, so at 25 ms an owner with crit chance rolls
  twice as often per second (legacy does the same per step; not pinned here, flagged for the implementer).
- Brief wording: section 1 says the `TICK_MS`/`TICK_RATE` aliases are removed, but the coordinator confirmed (carried
  note 4) that the registry rows keep those names for the content hash and `LEGACY_*` are the aliases. The oracle
  needs neither; the implementer keeps the registry rows (`tests/content.rs` `hash_golden`).

## 3b. Existing tests that pin what this packet touches (no conflict found)
- `crates/rh-headless/tests/tick_ms_digest_guard.rs`: seed-1 state hash `0x2b20_ce9e_ab4e_809a` and outcome digest at
  50 ms, 4000 ticks. Must stay green (section 7).
- `crates/rh-sim/src/checkpoint/walk_hint_state_tests.rs:146-190`: at 50 ms `walk_ticks` 1..=3 restore and 4, 255 are
  refused. Still true when the bound becomes 150 ms / step (3 at 50 ms).
- `crates/rh-sim/src/checkpoint.rs:1618` `a_too_dense_rewind_history_is_refused` (50 ms, gaps 0, 1, 49).
- `crates/rh-sim/src/state.rs:1301-1420` ring tests (`record` at 50 ms, `record_at_step` at 25/50).
- `crates/rh-sim/src/systems/status/tests.rs:234` `generic_dot_uses_shared_cap_...`: the cap is
  `PVP_DOT_CAP_SHARE * max_hp * TICK_MS / 1000` per step at 50 ms.
- `crates/rh-sim/src/cards/fight_clock_tests.rs`, `systems/walk_hint/tests.rs`, `parity/walk-hint/repair` corpus: 50 ms.
- About 40 test files read `TICK_MS` directly (the registry constant stays, so they keep building).

## 4. New paths
- tests beside the code they cover

## 5. Exclusive write scope
- `crates/rh-sim/src/systems/**`, `crates/rh-sim/src/abilities/**`, `crates/rh-sim/src/cards/**`,
  `crates/rh-sim/src/constants.rs` (the tick-counted rows only, and removing the aliases), and any other `rh-sim` file
  whose only change is reading the sim's `tick_ms` instead of the constant (list them in the hand-off). The oracle
  needs at least: `crates/rh-sim/src/checkpoint.rs` (the walk-hint bound at :706 in steps of the sim) and
  `crates/rh-sim/src/state.rs` (`History::record`, or its removal in favour of `record_at_step`)
- `crates/rh-sim/src/systems/mod.rs` (the `mod tick_rate_tests;` line is a stub; keep it)

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
- `crates/rh-sim/tests/tick_rate.rs` (public API; 8 tests, 5 red by assertion on the base, 3 green pins):
  - `a_walker_on_open_floor_covers_player_speed_per_second_at_both_rates`: red, 340 u/s at 25 ms; a 25 ms and a 50 ms
    walker stand within 25 ms of walk of each other after 3 s.
  - `a_held_bow_releases_and_is_ready_within_one_step_of_the_50_ms_run`: green pin (first launch, `ready_at`, next
    launch within 25 ms).
  - `an_arrow_flies_the_same_distance_per_second_at_both_rates`: green pin (`traveled` 400 ms after launch, 1e-6).
  - `passive_regen_heals_the_same_per_second_at_both_rates`: red, twice `PLAYER_REGEN_SHARE_OUT * max_hp` per s.
  - `the_walk_hint_commits_for_150_ms_at_both_rates`: red, 3 steps at 25 ms (want 6); the 25 ms checkpoint with the
    hint restores with the same hash.
  - `a_25_ms_rewind_history_is_spaced_and_trimmed_by_the_sim_step`: red, oldest sample `now - 350` (want `now - 325`).
  - `a_too_dense_25_ms_rewind_history_is_refused`: green pin (gaps 0, 1, 12.5, 24 ms refused; a real one restores).
  - `a_25_ms_checkpoint_with_a_wish_cast_at_an_odd_step_restores`: red, `invalid card Wish spend` at 325 ms.
- `crates/rh-sim/src/systems/tick_rate_tests.rs` (beside the code; applies a status directly): 2 tests, both red.
  - `a_burn_deals_the_same_damage_per_second_at_25_ms_as_at_50`: one second inside a 30/s burn deals 30 (1e-6) at both
    rates (60 at 25 ms today); the whole 2000 ms burn, applied between steps, deals `30 * (2000 - tick_ms) / 1000`.
  - `the_pvp_dot_cap_is_the_same_per_second_at_25_ms_as_at_50`: `PVP_DOT_CAP_SHARE * max_hp` per second (1e-6).
- `docs/backlog/packets/P-TICK-1b.md` (this file).

Section 8's "a burn deals the same total damage over its duration within 1e-6" holds only for a burn applied inside the
DoT pass (it bites on its own step); one applied between steps first bites on the next step, so its total is
`dps * (ms - tick_ms) / 1000` and the two rates differ by one 25 ms step of burn. The oracle pins the per-second rate at
1e-6 and the whole-duration total by that formula at each rate.

## Stub (implementer keeps)
- `crates/rh-sim/src/systems/mod.rs`: `#[cfg(test)] mod tick_rate_tests;`. No production code is stubbed: every API the
  oracle uses exists on the base.

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
