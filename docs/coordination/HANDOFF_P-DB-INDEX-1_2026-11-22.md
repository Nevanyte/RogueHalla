# Hand-off: P-DB-INDEX-1 (2026-11-22, backend-efficiency lane)

```
PACKET:          P-DB-INDEX-1 — serves REQUIREMENTS R-OPT-1/R-OPT-2, ORDER_OF_OPERATIONS phase 4 item 18; owner
                 direction 2026-11-22 (backend audit finding 9), dispatched by the coordinator session. New migration:
                 the owner said yes in the lane's chat on 2026-11-22 ("Yes, write it"), for this one migration only
BRANCH:          agent/opus/P-DB-INDEX-1 @ 541e4e8c9 (this hand-off is the next commit; oracle packet/P-DB-INDEX-1 @
                 13cf5279f; base feature/unity-rework @ c92d606e9, integration 82; integration 83 changed nothing in
                 rh-persist)
GATE:            PASS (quick, --oracle packet/P-DB-INDEX-1, DATABASE_URL set: purchases_index and schema ran and passed)
                 [full gate not run by the lane: workspace build with the new build script, rh-server's postgres
                 suites, CI and the Docker build are the coordinator's]
CHANGED:         crates/rh-persist/migrations/0003_purchases_profile_index.sql (new),
                 crates/rh-persist/build.rs (new: cargo:rerun-if-changed=migrations)
D-RULES:         D1 n/a; D2 n/a; D5/D6/D9 none
DEVIATIONS:      none (legacy keeps profiles in a JSON store)
SHARED RECORDS:  proposed: docs/backlog/README.md, the R-OPT row for P-DB-INDEX-1 "Done"
LEGACY DOUBTS:   none
NOT ESTABLISHED: the index build time on a production-sized purchases table (measured sizes: 300 000 rows, 4.8 MB
                 index, well under a second); that the planner picks the index for discard_unclaimed_profile's
                 anti-join at real statistics (the oracle proves it is usable; the brief's pgbench run measured
                 70 ms -> 1.5 ms); a real version-2 binary against a version-3 database (schema.rs fakes the step one
                 version up)
RISKS:           a plain CREATE INDEX at startup blocks writes to purchases (shop buys, the claim-undo delete) while it
                 builds; reads continue. Rolling-deploy purchase inserts on the old binary wait that long
```

## What it does

- Migration 0003 creates `purchases_profile ON purchases (profile_id)`. `purchases` had only its primary key, so
  `discard_unclaimed_profile`'s `NOT EXISTS` and PostgreSQL's foreign-key check on a `profiles` delete scanned the whole
  table. Additive: `schema_compat.min_binary` stays 1, no query result changes.
- `known_schema_version()` becomes 3. A version-2 binary keeps running on a version-3 database (`ignore_missing`, the
  compat floor is unchanged); a version-3 binary applies 0003 at startup under sqlx's advisory lock, in one transaction
  with its `_sqlx_migrations` row.
- Not `CONCURRENTLY`: sqlx would allow it with `-- no-transaction`, but a failed concurrent build leaves an INVALID index
  and no version row, so the retry fails. The migration's comment says so.
- The ledger index the audit also proposed was measured and not taken (131 MB for 0.1-9 ms on a cold path); the brief
  has the table.

## Found on the way: a cached build ignored a new migration

sqlx 0.8.6's `migrate!` embeds each migration file with `include_str!` but watches the folder only under an unstable cfg
(`sqlx-macros-core-0.8.6/src/migrate.rs:63,110`). Measured on this branch: adding a file to `migrations/` left
`rh-persist` "Fresh", so a cached target would have shipped a binary that knows two migrations. With
`crates/rh-persist/build.rs` (`cargo:rerun-if-changed=migrations`, what `sqlx migrate build-script` writes) adding or
removing a file marks it "Dirty" and rebuilds. The lane added it after the implementer's report and amended the write
scope (`brief-amend` 13cf5279f). The Dockerfile builds fresh and was never affected; local and gate worktrees with a
cached rh-persist were. Until this merges, `cargo clean -p rh-persist` after adding a migration.

## Review

`reviewer-opus`: **LAND**, no notes. It traced both compatibility directions against `crates/rh-persist/src/lib.rs` and
sqlx's migrator, confirmed the build-script cause in the sqlx source, and checked that the oracle's EXPLAIN needs the new
index (without it the plan is a sequential scan).

Minor, not notes (no packet):
1. The migration comment said sqlx refuses CONCURRENTLY; sqlx allows it with `-- no-transaction`. Reworded by the lane
   before hand-off (541e4e8c9), because the checksum freezes the file once merged.
2. No `SET LOCAL lock_timeout`: an open writer transaction on `purchases` would delay the build. Left out: a timeout
   turns a short wait into a startup exit.
3. The oracle's EXPLAIN uses a simplified lookup with sequential scans off: it proves the index is available, not
   chosen at real statistics. Fine for a loose proof.
4. `crates/rh-persist/README.md:6` says "Schema (v1, …)" and was already stale before 0002; a later docs pass.

## What changes for a player, on the wire, in a pinned value

Nothing on the wire and no pinned value. Undoing an undelivered account claim stops scanning every purchase.
