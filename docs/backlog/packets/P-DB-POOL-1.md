# P-DB-POOL-1: a sized database pool, and an admission that never waits on it longer than a lease does

- **Serves:** REQUIREMENTS R-OPT-1/R-OPT-2 and ORDER_OF_OPERATIONS phase 4, item 18 (cheap always-on host); owner
  direction 2026-11-22 (the backend audit he asked for, finding 8 of `docs/backlog/BALANCE_LAB_PATCH.md`), dispatched by
  the coordinator session to the backend-efficiency lane.
- **Implementer tier:** implementer-opus (admission is security work, `crates/rh-server/AGENTS.md` "Routing")
- **Reviewer:** reviewer-opus
- **Integration base:** `feature/unity-rework` @ `83edb687f`
- **D-rules touched:** D1 n/a (no game behaviour) · D2 n/a · D5/D6/D9 none

## 1. ID and outcome

The durable store's pool is fixed at 16 connections with a 30 s acquire timeout (`crates/rh-persist/src/lib.rs:221-229`,
`Store::connect` -> `connect_with(url, 16)`; the server's only call is `crates/rh-server/src/startup/database.rs:151`).
Admission (`crates/rh-server/src/director/admission.rs`, called with no timeout from `conn.rs:190`) bounds its lease
acquisition by `LeasePolicy::db_timeout` (5 s, `admission.rs:243`) but **not its credential check**
(`persistence.authenticate`, `admission.rs:89-92` -> `persist.rs:399-434`: `verify_token_id` or `profile_id`, one pool
acquire and one query each). In a reconnect storm the pool saturates and every Hello holds its socket for up to the
30 s acquire timeout, plus an unbounded query time.

When this packet lands:
1. `rh-server --db-pool N` sizes the pool (default 16, the current value; accepts whole numbers 1..=1024; anything else is
   a startup error, exit 2, like `--db-connect-timeout-secs`). The value reaches `rh_persist::Store::connect_with`.
2. The credential check inside admission is bounded by the same `cfg.leases.db_timeout` that bounds the lease
   acquisition. On expiry the Hello is refused `RejectCode::RateLimited` with the existing fixed reason
   `"server busy, storage unavailable; try again shortly"` and a warning line in the log; it is not counted as a bad
   credential by the edge throttle (only `BadCredentials` is).

**Why the credential check and not a blanket timeout around `director.admit`** (lane decision): every other await in
admission is already bounded (`wait_while_leaving` by `LEAVING_WAIT`, the lease by `db_timeout`, the Join send by the
lease deadline) or owned by the `PendingAdmission` drop guard (`director.rs:301-345`). The resume path
(`admission.rs:53-77`) has no guard: a blanket timeout that fired after the room attached the new outbox and before the
reply arrived would leave a live seat bound to a connection that has gone. Bounding the one unbounded database wait
removes the 30 s hold without opening that window.

## 2. Dependencies

None.

## 3. Relevant legacy paths

None: the legacy server (`packages/server`) has no PostgreSQL store; this is hosting robustness.

## 4. New paths

- `crates/rh-server/tests/admission_db_budget.rs` (oracle).

## 5. Exclusive write scope

- `crates/rh-server/src/startup/database.rs` (the `db_pool_size` stub is the implementer's to replace)
- `crates/rh-server/src/director/admission.rs`
- `crates/rh-server/src/main.rs` (module doc: list the flag next to `--db-connect-timeout-secs`)
- `crates/rh-server/README.md` (only if it lists the database flags)

## 6. Contracts and references

- `crates/rh-server/AGENTS.md`: request-path code returns errors; persistence stays off the tick.
- Admission refusal codes are wire values (`rh_protocol::RejectCode`): no new code (a new one is a protocol change).
- `LeasePolicy::validate` (`director.rs:67-80`) already guarantees `db_timeout` is positive.

## 7. Behaviour to preserve

- With a healthy database every refusal and admission is unchanged: a wrong ticket is still `BadCredentials` (and still
  counted by the edge throttle), a right one is admitted.
- The pool size and acquire timeout without the flag stay 16 and 30 s.
- `Store::connect` and `Store::connect_with` keep their signatures (other crates and tests call them).

## 8. Acceptance checks

- `scripts/gate.sh --quick --oracle packet/P-DB-POOL-1` passes with `DATABASE_URL` set (the rh-server Postgres suite runs).
- The oracle: with a pool of one connection held by the test and `db_timeout` 300 ms, `Director::admit` answers
  `RateLimited` within 3 s (today it waits for the 30 s acquire timeout); after the connection is released a wrong
  ticket is `BadCredentials`.
- `db_pool_size`: default 16, `--db-pool 4` is 4, `0`, `1025`, `abc` and a missing value are errors.

## Oracle files (read-only for implementers)

- `crates/rh-server/tests/admission_db_budget.rs`
- `crates/rh-server/src/startup/tests.rs`

## 9. Validation commands

- `eval "$(tools/pgtest/start.sh)"; cargo test -p rh-server --features postgres --test admission_db_budget`
- `cargo test -p rh-server --bin rh-server startup::tests::db_pool_flag_sizes_the_pool`
- `scripts/gate.sh --quick --oracle packet/P-DB-POOL-1`

## 10. Risk and suggested model tier

Opus: the admission path is security work. The change is small; the risk is a timeout that refuses a healthy but slow
credential check (5 s is far above a pooled `SELECT` by primary key) or a refusal counted as a bad credential.

## 11. Known traps

- `authenticate` returns `Result<Option<ProfileKey>, String>`; a timeout is neither `Ok(None)` (bad credentials) nor a
  silent pass. Map it to the same arm as the storage error.
- Do not wrap `director.admit` in `conn.rs`: see "Why the credential check" above.
- `startup/database.rs` items are `cfg(feature = "postgres")` or `cfg(any(feature = "postgres", test))`; the flag parser
  must build in the default (no-postgres) test build, as `db_connect_budget` does.

## Notes from previous attempts
