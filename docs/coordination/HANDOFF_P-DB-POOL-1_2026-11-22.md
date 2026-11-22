# Hand-off: P-DB-POOL-1 (2026-11-22, backend-efficiency lane)

```
PACKET:          P-DB-POOL-1 — serves REQUIREMENTS R-OPT-1/R-OPT-2, ORDER_OF_OPERATIONS phase 4 item 18; owner
                 direction 2026-11-22 (backend audit finding 8), dispatched by the coordinator session
BRANCH:          agent/opus/P-DB-POOL-1 @ 19b5970d4 (implementation; this hand-off is the next commit; oracle
                 packet/P-DB-POOL-1 @ b33944fa2; base feature/unity-rework @ 83edb687f)
GATE:            PASS (quick, --oracle packet/P-DB-POOL-1, DATABASE_URL set: the rh-server Postgres suite ran; run by the
                 implementer on attempt 2 and re-run by the lane) [full gate not run by the lane]
CHANGED:         crates/rh-server/src/director/admission.rs (the credential check bounded by the lease db_timeout),
                 crates/rh-server/src/startup/database.rs (--db-pool, wired into Store::connect_with),
                 crates/rh-server/src/main.rs (module doc lists the flag)
D-RULES:         D1 n/a (no game behaviour); D2 n/a; D5/D6/D9 untouched
DEVIATIONS:      none (the legacy server has no PostgreSQL store)
SHARED RECORDS:  proposed: docs/backlog/README.md, an R-OPT/hosting row "Done" for P-DB-POOL-1
LEGACY DOUBTS:   none
NOT ESTABLISHED: a reconnect storm against a real deployment; the right pool size for the deployed instance (the
                 default stays 16; RDS's max_connections bounds it)
RISKS:           a credential check slower than db_timeout (5 s by default) on a healthy database is now refused
                 "try again shortly"; a primary-key SELECT is milliseconds
```

## What it does

- `rh-server --db-pool N` (default 16, 1..=1024; anything else is "invalid database settings", exit 2) sizes the durable
  store's pool through `rh_persist::Store::connect_with`.
- Admission's credential check (`persistence.authenticate`: a pool acquire plus one query) is bounded by
  `cfg.leases.db_timeout`, the bound the lease acquisition already had. On expiry the Hello is refused `RateLimited`
  ("server busy, storage unavailable; try again shortly"), never `BadCredentials`, so the edge throttle does not count
  it. The log line carries only the bound and goes through the once-per-second limiter of `api::log_internal`.
- Before: a saturated pool held every Hello, and its socket, for the pool's 30 s acquire timeout plus the query.
  The oracle shows it: a pool of one held connection with a 300 ms db_timeout. Before the change, admission was still
  waiting after 3 s. After, it is refused within the bound, and a wrong ticket on a free pool is still `BadCredentials`.

Why not a timeout around `director.admit` (lane decision, in the brief): every other wait in admission is already
bounded or owned by the `PendingAdmission` drop guard. The resume path has no guard, and a blanket timeout there could
leave a live seat bound to a connection that has gone.

## Gate gap found (for the coordinator; `scripts/gate.sh` is owner-protected)

`scripts/gate.sh --oracle-check` rejects any red `--bin` target ("red outside the oracle") even when the only red test
is the packet's own oracle in the binary's unit tests (`crates/rh-server/src/startup/tests.rs`, an oracle file here). It
handles `--test` and `--lib` targets only. The oracle check for this packet therefore printed GATE FAIL with the
oracle red for the right reasons. Also, the oracle check runs `cargo test -p rh-server` without `--features postgres`,
so a Postgres oracle is not exercised by it (the lane ran it by hand: red on the assertion).

## Review

`reviewer-opus`: **LAND**, no notes. Checked: dropping the credential check at the bound leaves nothing behind
(everything before it only reads; the reservation, admit and lease come after; `authenticate` is two read-only
SELECTs); the timeout is `RateLimited` and the edge throttle (`conn.rs:192`) counts only `BadCredentials`; both log arms
go through `api::log_internal`; `LeasePolicy::validate` keeps the bound positive; `--db-pool` parses and fails as the
brief says and reaches `Store::connect_with`; the diff stays inside the write scope and leaves both oracle files alone.

Minor, not notes (no packet):
- `--db-pool +4` parses as 4 (Rust's `u32::from_str`).
- When both database flags are bad, only the connect-budget error prints.
- `log_internal` shares one once-per-second limiter with the account API routes (the `[api]` prefix); in a storm an API
  failure in the same second can hide the timeout line, and suppressed lines are not counted. The storage-error arm
  already behaved so.
- The oracle does not admit the right ticket after the pool frees; that no reservation is left is structural, not
  tested.
- The one pool also serves the account API, lease renewals, the result spool and bot learning; the flag's doc does not
  say so. Sizing it against PostgreSQL's `max_connections` is the operator's.

## What changes for a player, on the wire, in a pinned value

Nothing on the wire (an existing reject code). A player whose Hello meets a stalled database is told to retry within
about 5 s instead of waiting up to 30 s. No pinned value moves.
