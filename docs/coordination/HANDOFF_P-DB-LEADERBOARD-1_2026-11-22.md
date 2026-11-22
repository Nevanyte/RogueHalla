# Hand-off: P-DB-LEADERBOARD-1 (2026-11-22, backend-efficiency lane)

```
PACKET:          P-DB-LEADERBOARD-1 — serves REQUIREMENTS R-OPT-1/R-OPT-2, ORDER_OF_OPERATIONS phase 4 item 18; owner
                 direction 2026-11-22 (backend audit finding 9), dispatched by the coordinator session
BRANCH:          agent/sonnet/P-DB-LEADERBOARD-1 @ 8d43a43df (implementation; this hand-off is the next commit; oracle packet/P-DB-LEADERBOARD-1 @ 9f62253e0; base
                 feature/unity-rework @ 83edb687f)
GATE:            PASS (quick, --oracle packet/P-DB-LEADERBOARD-1, DATABASE_URL set: the rh-persist Postgres suite ran)
                 [full gate not run by the lane: the coordinator's merge gate]
CHANGED:         crates/rh-persist/src/account.rs (leaderboard_rank: two counts instead of a window over the month),
                 crates/rh-persist/src/lib.rs (Store::leaderboard delegates to leaderboard_rows; one unused import gone)
D-RULES:         D1: the legacy tie order byGvThenName (packages/server/src/persistence/ProfileStore.ts:373, code-unit
                 order) now holds for every leaderboard read; D2 n/a; D5/D6/D9 untouched
DEVIATIONS:      none (Store::leaderboard now matches legacy on a non-"C" database, where it did not)
SHARED RECORDS:  proposed: docs/backlog/README.md, a "Done" row for P-DB-LEADERBOARD-1 under R-OPT
LEGACY DOUBTS:   none
NOT ESTABLISHED: the timing was measured on a shared, heavily loaded machine (load average 60-70); the ratio
                 (about 30x on a 100 000-player month) is the finding, not the absolute times. At today's player counts
                 both queries are fast; the gain is headroom.
RISKS:           none known
```

## Evidence (R-OPT-2)

In the brief, `docs/backlog/packets/P-DB-LEADERBOARD-1.md`, section "Measured": `row_number()` over the month 297 and
322 ms per call; two counts 10.8 and 10.2 ms; same rank for 2 000 sampled players. Revision `83edb687f`, Apple M5 Pro,
native, PostgreSQL 16 pgtest cluster, `pgbench -c 1 -T 8`, a 100 000-player month with about 20 players per GV tie.

## Review

`reviewer` (Sonnet): **LAND**, no notes. The rank is exact because callsigns are unique case-insensitively (no two rows
tie on GV and name); `gv > 0` on the player's own row covers the counted rows; the malformed-month guard is kept; the
unused-import removal is inside the same file and needed for clippy.

Minor, not notes (no packet):
- `Store::leaderboard` now reads and discards `cosmetics` (at most 1 000 rows; test-only callers today).
- The `leaderboard_rank` doc comment is long.

## What changes for a player, on the wire, in a pinned value

Nothing on the wire. `/api/leaderboard` returns the same rows and rank. `Store::leaderboard` (callers: tests only) now
breaks ties in byte order on every database. No pinned value moves.
