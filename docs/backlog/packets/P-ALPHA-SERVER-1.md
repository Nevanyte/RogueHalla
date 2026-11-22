# P-ALPHA-SERVER-1: disconnect tolerance and lifecycle logs for the open alpha

- **Serves:** not in the plan: owner request 2026-11-22 ("prepare the server for open alpha with 10+ people, multiplayer experience polish, not gameplay"); owner approval needed to treat as in plan
- **Implementer tier:** implementer-sonnet (constants plus log call sites in `rh-server` only; no wire, no `rh-sim`)
- **Reviewer:** reviewer
- **Integration base:** feature/unity-rework @ 37a2200d
- **D-rules touched:** D1 n/a (no sim behaviour) · D2 n/a · D5/D6/D9 none

## 1. ID and outcome
Browser testers hide a WebGL tab and the tab stops pinging; the server then drops them and loses the seat, and a quick
rejoin after a match end is refused `ProfileInUse`. After this packet: (a) `conn::IDLE_TIMEOUT` is 30 s (was 10 s),
`room::RECONNECT_GRACE` is 60 s (was 10 s), `director::LEAVING_WAIT` is 6 s (was 3 s), and `DirectorConfig::new` picks up the
first two (director.rs:132,134). (b) Every join, resume, drop, grace expiry, leave, admission reject and match end writes
one structured `eprintln!` line, built by a new pure module `lifecycle_log`, so a tester report can be traced. No per-tick logs.

## 2. Dependencies
none.

## 3. Relevant legacy paths
- `packages/shared/src/constants.ts:135`: `RECONNECT_SECONDS = 10`, used at `packages/server/src/rooms/RaidRoom.ts:479`
  (`allowReconnection`). The 60 s grace and 30 s idle are an intentional difference from legacy (proposed DEV row below).
- Rust sites: `crates/rh-server/src/conn.rs:21` (`IDLE_TIMEOUT`), `room.rs:41` (`RECONNECT_GRACE`), `director.rs:449`
  (`LEAVING_WAIT`), `director.rs:132-134` (config defaults), `control.rs:252` (`ControlBook::default` uses
  `RECONNECT_GRACE`), `room/step.rs:19` (grace expiry check), `conn.rs:293` (`Wake::Closed | Wake::Idle` breaks the loop),
  `conn.rs:95-182` (rejects at admission), `room/finish.rs:180` and `director/reports.rs:259` (existing `event=` style).
- Owner decisions: searched `docs/OWNER_DECISIONS.md` for idle/grace/leaving: no row on these values.

## 4. New paths
- `crates/rh-server/src/lifecycle_log.rs` (stubbed on the branch: signatures, `DropReason`, bodies return an empty `String`).
- `pub mod lifecycle_log;` in `crates/rh-server/src/lib.rs` (stub commit).

## 5. Exclusive write scope
- `crates/rh-server/src/conn.rs`, `room.rs` (and `room/*.rs` as needed), `director.rs` (and `director/*.rs`),
  `lifecycle_log.rs` (implement the bodies), `lib.rs` (no change beyond the stub).
- Existing tests listed in section 7 that must change (not oracle files).
- `docs/DEVIATIONS.md` row is proposed in the hand-off, not edited (owner signs).

## 6. Contracts and references
- C-INPUT, C-OBS unaffected. `crates/rh-server/AGENTS.md`: new shared state, if any, poison-tolerant; request-path code returns errors.
- Log lines carry a callsign (public) and a peer ip at reject only; never a ticket, token, seed or hash (C-RNG).

## 7. Behaviour to preserve, and existing tests the implementer must update
Preserve: wire bytes, tick order, admission outcomes, reject codes, existing `event=` lines.
Tests that pin or depend on the old values (not oracle files; the implementer updates them as needed, minimal edits):
- `src/director/tests/admission.rs:58-82` `leaving_wait_is_one_deadline_for_the_whole_admission`: asserts elapsed ==
  `LEAVING_WAIT` with 2 s give-back delays sized for a 3 s bound (doc comment says "3 s"). With 6 s two 2 s waits no longer
  hit the bound: scale the delays (e.g. 4 s) and the comment.
- `src/director/tests/admission.rs:96-140` `an_admission_waits_for_a_leaving_profile_but_not_for_a_seated_one`: uses the
  constant symbolically; should stay green, re-check.
- `src/room/tests.rs:149`, `src/room/tests/training.rs:44`, `tests/density.rs:66`, `src/bin/densitybench/engine.rs:258`:
  use `RECONNECT_GRACE` symbolically; any test that advances a paused clock by a literal ~10 s to expire a grace must use
  the constant. Run the whole `cargo test -p rh-server` to find them.
- `src/control.rs:1178` and `conn.rs:808`: literal 10 s fixtures independent of the constants; leave.
- Comments naming 3 s / 10 s: `director.rs:449` doc, `conn.rs:19-21` ("legacy 4.5 s beat timeout"), `tests/pg_e2e.rs:313` (comment).

## 8. Acceptance checks
- `scripts/gate.sh --oracle packet/P-ALPHA-SERVER-1` passes (full).
- `cargo test -p rh-server --test alpha_server_tolerance` green.
- Call sites exist (reviewer greps `lifecycle_log::` in `conn.rs`, `room*`, `director*`): join and resume (seat admitted,
  `humans` = humans seated after), drop (connection lost, reason `idle_timeout` on `Wake::Idle`, `closed` on `Wake::Closed`,
  `replaced` when a resume takes the seat over), grace_expired (room/step.rs grace check), leave (explicit leave),
  reject (every admission refusal in conn.rs and director admission, with the peer ip), match_end (room finish: elapsed
  wall time and humans). Each printed with `eprintln!("{}", lifecycle_log::...)`, once per event, none per tick.

## Oracle files (read-only for implementers)
- `crates/rh-server/tests/alpha_server_tolerance.rs`

The stub commit adds `crates/rh-server/src/lifecycle_log.rs` and the `mod` line; the implementer replaces the bodies.

## 9. Validation commands
- `cargo test -p rh-server --test alpha_server_tolerance`
- `scripts/gate.sh --quick --oracle packet/P-ALPHA-SERVER-1`

## 10. Risk and suggested model tier
Low: three constants and logging. The risk is the tests that implicitly assume the old timings (section 7) and a log call
placed on a hot path. Line format is pinned by the oracle; where each line is called is checked by review only (the crate
has no seam to capture `eprintln!`, so the formatter is the tested unit).

## 11. Known traps
- Grace now outlasts a match end: a seat dropped near the end must still free at close, not wait 60 s (check `linger`/finish).
- `ControlBook::default` takes `RECONNECT_GRACE`: control-surface admit/session expiry follows it (60 s now).
- A longer `LEAVING_WAIT` lengthens a refused admission by up to 6 s; `ProfileInUse` for a live seat must still return at once.
- Drop reasons: one drop per lost connection; a replaced connection logs `replaced`, not a second `closed`.
- Proposed DEV row: reconnect grace 60 s and idle 30 s versus legacy `RECONNECT_SECONDS = 10`.
- Format: `[server] INFO event=<name> k=v ...`; values with space, `"` or `=` or empty are `{:?}`-quoted; room is the
  `control::room_id_hex` string; reject `code` is the `RejectCode` Debug name; `match_end` uses `duration_ms`.

## Notes from previous attempts
