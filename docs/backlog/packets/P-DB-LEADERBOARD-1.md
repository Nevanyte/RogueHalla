# P-DB-LEADERBOARD-1: the player's rank by counting, and one tie order for every leaderboard read

- **Serves:** REQUIREMENTS R-OPT-1/R-OPT-2 and ORDER_OF_OPERATIONS phase 4, item 18 (cheap always-on host); owner
  direction 2026-11-22 (the backend audit he asked for, finding 9 of `docs/backlog/BALANCE_LAB_PATCH.md`), dispatched by
  the coordinator session to the backend-efficiency lane.
- **Implementer tier:** implementer-sonnet (one crate, two SQL statements)
- **Reviewer:** reviewer
- **Integration base:** `feature/unity-rework` @ `83edb687f`
- **D-rules touched:** D1: legacy tie order `byGvThenName` (code-unit order) kept and made uniform · D2 n/a · D5/D6/D9 none

## 1. ID and outcome

`Store::leaderboard_rank` (`crates/rh-persist/src/account.rs:55-75`) numbers every row of the month with
`row_number() OVER (ORDER BY gv DESC, callsign COLLATE "C")` and keeps one: a sort of the whole month on every
authenticated `/api/leaderboard` request (`crates/rh-server/src/api/profile.rs:864`). And the month's rows are ordered
two ways: `leaderboard_rows` and `leaderboard_rank` break ties with `COLLATE "C"` (`account.rs:34,66`), while
`Store::leaderboard` (`crates/rh-persist/src/lib.rs:384-398`, legacy `leaderboard`) uses the database's default collation.
On a database created with a natural-language collation (en-US, the usual hosted default) the two disagree on
`"Zed"`/`"abe"` and on `"a_b"`/`"a-b"`.

When this packet lands:
1. The rank is `1 + (rows with more GV) + (rows with equal GV whose callsign sorts first, COLLATE "C")`, two counts
   the existing index `gv_by_month_month (month, gv DESC)` answers, instead of a window over the month. Same values:
   1-based, `None` for no GV that month or a malformed month.
2. `Store::leaderboard` returns exactly the `(name, gv)` of `leaderboard_rows` (one statement, one order); its signature
   is unchanged.

Measured candidate for (1) (the implementer may refine it; the oracle and the table below are what count):

```sql
SELECT 1 + (SELECT count(*) FROM gv_by_month a WHERE a.month = $1 AND a.gv > me.gv)
         + (SELECT count(*) FROM gv_by_month t JOIN profiles q USING (profile_id)
            WHERE t.month = $1 AND t.gv = me.gv AND q.callsign COLLATE "C" < me.callsign COLLATE "C"),
       me.gv
FROM (SELECT m.gv, p.callsign FROM gv_by_month m JOIN profiles p USING (profile_id)
      WHERE m.month = $1 AND m.profile_id = $2 AND m.gv > 0) me
```

## 2. Dependencies

None. The index migration the audit also proposed is a separate packet, P-DB-INDEX-1, held for the owner's yes.

## 3. Relevant legacy paths

- `packages/server/src/persistence/ProfileStore.ts:373` (`byGvThenName`, used at `:814`): GV descending, then name;
  JS string comparison is code-unit order, which `COLLATE "C"` reproduces for the callsign alphabet `[A-Za-z0-9_-]`.

## 4. New paths

- `crates/rh-persist/tests/leaderboard_order.rs` (oracle).

## 5. Exclusive write scope

- `crates/rh-persist/src/account.rs` (`leaderboard_rank` only)
- `crates/rh-persist/src/lib.rs` (`Store::leaderboard` only)

## 6. Contracts and references

- R-OPT-2: the before/after table below; the reviewer re-runs the agreement check.
- `account.rs` module doc: "the listing and the rank use the same ordering so a row's position and its rank agree".

## 7. Behaviour to preserve

- Every rank equals the row's 1-based position in `leaderboard_rows` (existing test
  `crates/rh-persist/tests/account.rs::leaderboard_rows_rank_months_and_epoch_agree` and the oracle).
- `leaderboard_rows` SQL and the `/api/leaderboard` JSON are unchanged.
- No migration, no new index, no schema change.

## 8. Acceptance checks

- `scripts/gate.sh --quick --oracle packet/P-DB-LEADERBOARD-1` passes with `DATABASE_URL` set.
- Both oracle tests pass: on an ICU en-US database every read breaks ties in byte order, and through 240 rows in five
  tie groups each rank is the listed position.

## Oracle files (read-only for implementers)

- `crates/rh-persist/tests/leaderboard_order.rs`

## 9. Validation commands

- `eval "$(tools/pgtest/start.sh)"; cargo test -p rh-persist --test leaderboard_order --test account`
- `scripts/gate.sh --quick --oracle packet/P-DB-LEADERBOARD-1`

## 10. Risk and suggested model tier

Sonnet: one crate, read-only SQL. The risk is an off-by-one or a tie compared in the default collation; the oracle pins
both on a database where the default collation is not "C".

## 11. Known traps

- Both sides of the callsign comparison need `COLLATE "C"`; otherwise the comparison uses the database default.
- `gv > 0` must stay on the player's own row (a 0-GV row is unranked), and is implied for the rows counted above it.
- The test cluster's default collation is "C": a test on the shared rig (`tests/common`) cannot see a collation bug.

## Measured (R-OPT-2)

Revision `83edb687f`; Apple M5 Pro (18 cores, 64 GB), macOS 26, native; PostgreSQL 16 (Homebrew, the pgtest cluster:
fsync off); a scratch database with migrations 0001-0002, 200 000 profiles, 100 000 of them banking GV in `2026-11`
(GV uniform in 1..=5000, so about 20 per tie), statistics fresh; `pgbench -n -c 1 -T 8`, one random player of the month
per transaction; two alternating runs each. The machine was shared (another session's full gate, a game client; load
average 60-70), so absolute times are high; the ratio is the finding.

| Query | Mean latency per call |
|---|---|
| `row_number()` over the month (today) | 297 ms, 322 ms |
| two counts (candidate above) | 10.8 ms, 10.2 ms |

Agreement: on 2 000 sampled players of the same month both queries return the same rank.

## Notes from previous attempts
