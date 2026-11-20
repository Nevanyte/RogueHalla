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
- `packages/shared/src/constants.ts:11-13`: the legacy step and patch rate

## 4. New paths
- tests beside the code they cover

## 5. Exclusive write scope
- `crates/rh-server/src/director.rs`, `pacer.rs` (doc comment only), `room.rs`, `room/**`, `rtt.rs`, `main.rs` (the
  flags), `src/bin/densitybench/**`,
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
- To be written by oracle-author on `packet/P-TICK-5`.

## 9. Validation commands
- `cargo test -p rh-server`, `cargo run --release -p rh-server --bin densitybench`

## 10. Risk and suggested model tier
Medium: one crate, clear oracle. Sonnet, Opus review for the rewind window.

## 11. Known traps
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
