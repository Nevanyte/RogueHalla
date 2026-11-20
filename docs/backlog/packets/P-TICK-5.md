# P-TICK-5: the server runs each room at its configured step

- **Serves:** not in the plan: owner approval 2026-11-19 (ADR 0029); supports ORDER_OF_OPERATIONS phase 3, item 16a
- **Implementer tier:** implementer-opus (coordinator 2026-11-20: now also lifts the wire refusal and wires the encoder rate)
- **Reviewer:** reviewer-opus (the rewind window is lag compensation)
- **Integration base:** feature/unity-rework
- **D-rules touched:** D1 n/a (no sim rule) · D2 n/a · D5 none · D6 disconnect classification untouched · D9 unchanged

## 1. ID and outcome
The server picks a room's step from a setting (`--tick-ms 50|25` and its environment variable, default 50), builds the
sim with it, paces the room on that period (`director.rs` builds the pacer from the constant today), sends the matching
`tick_rate` in Welcome (`room.rs`), and uses it wherever the room adds one step (`room/commands.rs` `now_apply`,
`room/bots.rs` `step_t`) and in the lag-compensation window (`rtt.rs` `allowed_rewind_ms`, floored at one step).
`densitybench` reports match seconds from the room's rate. Live rooms stay at 50 until P-TICK-6.

## 2. Dependencies
- P-TICK-3 merged (Welcome and snapshot time follow the rate)

## 3. Relevant legacy paths
- `packages/shared/src/constants.ts:11-13`: the legacy step and patch rate (`TICK_RATE = 20`, `TICK_MS = 1000 / TICK_RATE`,
  `PATCH_MS = TICK_MS`). Legacy has no 25 ms mode; the 25 ms behaviour is ADR 0029's, proven loosely (same wall-clock
  behaviour per second, not tick-exact).

## 3a. Established facts (oracle-author, 2026-11-20, base `fa25285b`)
Where the server still assumes 50 ms (every line is the implementer's to change):
- `director.rs:123` `DirectorConfig::new` sets `tick` from `rh_sim::constants::TICK_MS`, not from `sim.tick_ms`;
  `director/spawn.rs:115` copies `self.cfg.tick` into `RoomConfig::tick`; `room.rs:592` builds the `Pacer` from
  `cfg.tick`. So a 25 ms sim under the default director config is stepped every 50 ms (20 steps of 25 ms per second:
  sim time runs at half speed).
- `room.rs:555` and `director.rs:525` call `wire::profile_supported`, which refuses `tick_ms != 50` (`wire.rs:114-119`):
  today a 25 ms room or director panics at construction.
- `room.rs:667` `welcome()` sends `tick_rate: TICK_RATE as u8` (20); join and resume both use it
  (`room/admission.rs`, `room/commands.rs:93`).
- Nothing calls `Outbox::set_tick_ms` (`outbox.rs:230`) or `CollectionPublisher::set_tick_ms` yet: every connection
  encodes at 20 Hz, so a 25 ms snapshot (built with the sim's server time, `wire.rs:451`) carries a time override.
- `room/commands.rs:123` `now_apply = server_time + TICK_MS`; `room/bots.rs:114` `step_t = server_time + TICK_MS`;
  `rtt.rs:132` `allowed_rewind_ms` adds and floors at `TICK_MS` (50). `RttEstimator` is created per seat at
  `room/admission.rs:61` and `room/commands.rs:59`.
- `main.rs:196` builds the core sim with `tick_ms: 50`; `--runtime` profiles take `runtime.sim()` (default 50). There
  is no `--tick-ms` flag or environment variable yet. `bin/densitybench/engine.rs:249,326` use the constants.
- `public_ground.rs:312` and `room/ground_tests.rs:26` build test Welcome headers with `TICK_RATE`: test-only, outside
  scope, unaffected.

Existing tests that pin what this packet changes:
- `wire/tests/profiles.rs` `a_sim_stepping_other_than_50_ms_is_not_hosted`: asserts 25, 0 and 100 are refused with the
  message "only a 50 ms (20 Hz) tick is hosted until the room announces its rate". Conflicts with this packet (25 must be
  hosted): amended in its own `oracle-amend:` commit (25 accepted; 0, 20, 30, 40, 100 refused, message free). **Owner
  approval needed for the amendment** (it is the P-TICK-3 refusal this packet was written to lift).
- `rtt.rs` tests `the_floor_stops_a_lucky_sample_from_collapsing_the_window` (window 240 at RTT floor, floor `TICK_MS`)
  and `the_window_by_rtt_floor_and_ceiling` (`allowed_rewind_ms(-1000) == 50`, `TICK_MS == 50`): stay true at 50 ms if
  the one-argument `allowed_rewind_ms` keeps meaning 50 ms. No conflict.
- `pacer.rs` unit tests (C-TICK evidence) take the period as an argument: unaffected.
- `outbox.rs` `a_40_hz_outbox_writes_at_its_rate` and `wire/tests/tick_rate.rs`
  `forced_replacements_are_throttled_to_1000_ms_at_the_connection_rate` (P-TICK-3-fix): the encoder side; this packet
  only calls the setters. No conflict.
- `room/tests.rs` `cfg(tick)` builds every room test at `tick_ms: 50` with the given `tick`: unaffected.

## 4. New paths
- tests beside the code they cover

## 5. Exclusive write scope
- `crates/rh-server/src/director.rs`, `director/spawn.rs`, `pacer.rs` (doc comment only), `room.rs`, `room/**` (except
  the oracle file below), `rtt.rs`, `main.rs` (the flags), `runtime.rs` (`tick_ms_setting`, stubbed by the oracle; applying it to `Settings::sim`),
  `src/bin/densitybench/**`,
  `quest_bridge_tests.rs` (reads the rate)
- added by the coordinator 2026-11-20 (P-TICK-3/3-fix notes): `crates/rh-server/src/wire.rs` (`profile_supported`
  accepts 25), `crates/rh-server/src/outbox.rs`, `crates/rh-server/src/conn.rs` (comment), the Unity client lines named
  below only if P-TICK-4 does not take them

## 6. Contracts and references
- C-TICK as amended (pacer unit tests are its evidence), C-INPUT, ADR 0019, ADR 0029

## 7. Behaviour to preserve
- At 50 ms every server test, the density test and the browser proof pass unchanged.

## 8. Acceptance checks
- `scripts/gate.sh --oracle packet/P-TICK-5` passes (full).
- On a paused tokio clock a room at 25 ms runs 40 steps per second and sends `tick_rate` 40; `allowed_rewind_ms` of a
  negative RTT is 25 at 25 ms and 50 at 50 ms; `--tick-ms 30` is refused at start.

## Oracle files (read-only for implementers)
- `crates/rh-server/src/director/tests/tick_rate.rs`: `a_director_room_runs_and_announces_its_configured_step`. For
  50 ms then 25 ms on a paused tokio clock: `profile_supported` accepts the sim; `DirectorConfig::new(sim).tick` is the
  sim's step; a human admitted through `Director::admit` gets a Welcome of `tick_rate` 20 / 40; over one second of
  paused time the room's snapshots advance 20 / 40 ticks and its clock (`JoinOk::clock`) 1000 ms; the first Full
  snapshot carries no time override (decoded at its own rate its time is `tick * tick_ms`, decoded at the other rate
  `tick * other_ms`).
- `crates/rh-server/src/room/tests/tick_rate.rs`: `a_25_ms_room_announces_40_hz_on_join_and_resume`. A room built at
  25 ms (`RoomConfig::tick` 25 ms, `sim.tick_ms` 25) sends `tick_rate` 40 on join and on resume, and the snapshot after
  a resume carries no time override at 40 Hz. Same checks at 50 ms / 20 Hz first.
- `crates/rh-server/src/rtt_tick_tests.rs`: `the_rewind_window_adds_and_floors_at_one_step_of_the_room`.
  `allowed_rewind_ms_at(rtt, tick_ms)` and `RttEstimator::allowed_rewind_ms_at(tick_ms)`: negative RTT 25 at 25 ms and
  50 at 50 ms; RTT floor 20: 215 at 25 ms (20 + 150 + 25 + 20), 240 at 50 ms; cap 300 at both.
- `crates/rh-server/src/runtime_tick_tests.rs`: `the_tick_ms_setting_takes_50_or_25_from_the_flag_or_the_environment`.
  `rh_server::runtime::tick_ms_setting(args, env)`: default 50; `--tick-ms 25|50` and `RH_TICK_MS=25|50` accepted; the flag wins over the
  environment; `--tick-ms 30` (and 0, 20, 40, 100, `abc`, `25.0`, a missing value) and `RH_TICK_MS=30` (and `abc`, empty)
  are errors (the binary exits 2 on them, like every other invalid setting).
- `crates/rh-server/src/wire/tests/profiles.rs` (`oracle-amend:`): `a_sim_stepping_other_than_50_or_25_ms_is_not_hosted`.

Stubs (the `stub:` commit, the implementer replaces them): `rtt.rs` `allowed_rewind_ms_at` and
`RttEstimator::allowed_rewind_ms_at` (ignore the rate), `runtime.rs` `tick_ms_setting` (always 50) and `TICK_MS_ENV`,
and the `mod` lines in `rtt.rs`, `runtime.rs`, `room/tests.rs`, `director/tests.rs`. The setting lives in the library, beside
`core_hitboxes`, because `scripts/gate.sh --oracle-check` reads only `--lib` and `--test` targets (a first try under
`main.rs`'s `startup/` tests was moved; its file was removed on this branch).

Not covered by an oracle (review it): `room/commands.rs` `now_apply` and `room/bots.rs` `step_t` adding the room's step;
the room's rewind clamp using `allowed_rewind_ms_at(.., sim.tick_ms())`; `CollectionPublisher::set_tick_ms` on join and
resume; `main.rs` applying `tick_ms_setting` to the core sim and to `--runtime` sims (exit 2 on error); densitybench.

## 9. Validation commands
- `cargo test -p rh-server`, `cargo run --release -p rh-server --bin densitybench`

## 10. Risk and suggested model tier
Medium: one crate, clear oracle. Sonnet, Opus review for the rewind window.

## 11. Known traps
- The environment variable is `RH_TICK_MS` (chosen by the oracle: the brief named none; `TICK_MS_ENV` in
  `runtime.rs`). The flag wins over it, as `--fill-bots` wins over `BOTS_FILL_TO`.
- `RoomConfig::tick` and `sim.tick_ms` are two sources of one fact. The director must derive the first from the second;
  whether `Room::new` also refuses or ignores a mismatch is the implementer's choice (the oracle builds both consistent).
- Snapshots are coalesced per connection: a delta follows the first Full, so the oracle reads ticks through a
  `SnapshotTracker` fed the Welcome.
- `MAX_CATCHUP` (3 steps) halves in wall time at 25 ms; keep it in steps unless the measurements in P-TICK-6 say
  otherwise.

## Notes from previous attempts

## Carried from P-TICK-3 (implementer-opus hand-off, 2026-11-20)
- The Rust encoder still writes 20 Hz: `ConnEncoder`, `SnapshotData::build` and `ServerMsg::encode` give default time
  `tick * 50`, and at 40 Hz that becomes an override, which a 40 Hz decoder refuses when it equals `tick * 25`. Make
  the encoder rate-aware (the connection's Welcome rate).
- `ConnEncoder`, `rh-policy` `CollectionPublisher::resync_due` (`delivery/collections.rs`) and `outbox.rs` still use
  `MAX_DELTA_AGE` 64 / `MIN_FORCED_FULL_TICKS` 20: switch to `max_delta_age(tick_ms)` / `min_forced_full_ticks(tick_ms)`.
- `rh-server/src/wire.rs` `profile_supported` refuses `tick_ms != 50` (P-TICK-3): lift it here. `room.rs:667` sends
  `TICK_RATE as u8`: send the room's rate.
- Unity (outside the library): `UnityProject/Assets/Scripts/Net/MatchClient.cs:290` multiplies fx due time by
  `Wire.TickMs`; `RemoteDraw.cs:245` uses `Max(TickMs, Wire.TickMs)`. Both assume 50 (or hand to P-TICK-4).
- From P-TICK-3-fix (implementer-opus, 2026-11-20): the encoder side is rate-aware, but nothing sets the rate yet. Where
  the Welcome is built (`room/admission.rs`, `commands.rs`), call `outbox.set_tick_ms(sim.tick_ms())` and
  `h.collections.set_tick_ms(..)`; `room.rs` `welcome()` sends the sim's rate; `conn.rs:370` comment is stale.
- From the P-TICK-3-fix review: `SnapshotData::build`/`build_with` still default the time to `tick*50`; a 25 ms room
  builds with `build_with_server_time` (or builds at the connection's rate) so frames carry no override and skip
  `refresh_min_len`. The rate lives in two places (`Outbox::set_tick_ms`, `CollectionPublisher::set_tick_ms`): set both.
