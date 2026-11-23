# P-DB-INDEX-1: an index on `purchases (profile_id)` (additive migration 0003); the ledger index measured and not taken

- **Serves:** REQUIREMENTS R-OPT-1/R-OPT-2 and ORDER_OF_OPERATIONS phase 4, item 18; owner direction 2026-11-22 (the
  backend audit, finding 9 of `docs/backlog/BALANCE_LAB_PATCH.md`), dispatched by the coordinator session to the
  backend-efficiency lane.
- **Status:** Ready. The owner said yes in chat on 2026-11-22 to writing migration 0003 (`CLAUDE.md` "Approvals"); that
  yes covers this one additive migration and nothing else.
- **Implementer tier:** implementer-opus (migrations)
- **Reviewer:** reviewer-opus
- **Integration base:** `feature/unity-rework` @ `c92d606e9` (rebased from `83edb687f`; no change to `rh-persist` schema code between them)
- **D-rules touched:** D1 n/a · D2 n/a · D5/D6/D9 none

## 1. ID and outcome

`purchases` has no index on `profile_id` (`crates/rh-persist/migrations/0001_init.sql:100-107`). Two statements look
purchases up by profile: `discard_unclaimed_profile` (`crates/rh-persist/src/lib.rs:332-345`, `NOT EXISTS (SELECT 1 FROM
purchases WHERE profile_id = p.profile_id)`, run inside the account API when a claim's token could not be delivered) and
PostgreSQL's own foreign-key check when a `profiles` row is deleted. Both scan the whole table.

When this packet lands: migration `crates/rh-persist/migrations/0003_purchases_profile_index.sql` creates
`purchases_profile ON purchases (profile_id)`. Additive: `schema_compat.min_binary` stays 1, old binaries keep running.

**Not taken: `gv_ledger (profile_id, ref)`.** The audit proposed it for `settlement_by_id`
(`crates/rh-persist/src/settlement.rs:25-35`, filtered by `profile_id AND ref`, index on `profile_id` only,
`0001_init.sql:52`). Measured below: it saves about 0.1 ms per read for a player with 20 ledger rows and a few ms for one
with 20 000, on a read that runs once per receipt request, and the index is 7 times the size of the existing
`gv_ledger_profile` (a 36-character `ref` per row) on an append-only table that grows with every match. R-OPT-2:
complexity without demonstrated value is not added. Revisit if receipt reads become a hot path or ledgers reach
thousands of rows per player.

## 2. Dependencies

None (the owner's yes was given in chat on 2026-11-22).

## 3. Relevant legacy paths

None: the legacy server keeps profiles in a JSON store.

## 4. New paths

- `crates/rh-persist/migrations/0003_purchases_profile_index.sql`
- `crates/rh-persist/tests/purchases_index.rs` (oracle)

## 5. Exclusive write scope

- the migration file only. Merged migrations (`0001`, `0002`) are never edited.
- `crates/rh-persist/build.rs` (new, added by the lane after the implementer's report): `cargo:rerun-if-changed=migrations`.
  sqlx 0.8's `migrate!` tracks the migration files it found (`include_str!`), not the folder, on stable Rust, so a
  cached build never sees a new file: measured on this branch, adding a file to `migrations/` left `rh-persist` "Fresh"
  and its binary would have kept two migrations. sqlx's own advice (`sqlx migrate build-script`) is this line.

## 6. Contracts and references

- `0001_init.sql` header and `crates/rh-persist/src/lib.rs:36-45` (`MIGRATOR`, binary version = newest migration):
  additive migrations leave `min_binary` alone.
- `crates/rh-persist/tests/schema.rs` (the schema-compatibility tests): amended in this packet's oracle for a version-3
  binary.

## 7. Behaviour to preserve

- No query result changes. `min_binary` stays 1. Every existing Postgres test stays green.

## 8. Acceptance checks

- After `migrate()`, `pg_indexes` lists `purchases_profile` on `purchases (profile_id)`; `EXPLAIN` of the
  `discard_unclaimed_profile` lookup uses it (oracle).
- `scripts/gate.sh --quick --oracle packet/P-DB-INDEX-1` with `DATABASE_URL` set.

## Oracle files (read-only for implementers)

- `crates/rh-persist/tests/purchases_index.rs`
- `crates/rh-persist/tests/schema.rs` (oracle-amend: a version-3 binary; the "newer additive migration" it fakes is now
  version 4, since a real 0003 would collide with the fake one. Listed for the owner.)

## 9. Validation commands

- `eval "$(tools/pgtest/start.sh)"; cargo test -p rh-persist`

## 10. Risk and suggested model tier

Opus (migrations). `CREATE INDEX` without `CONCURRENTLY` locks `purchases` against writes while it builds: at today's
table sizes that is milliseconds; sqlx runs each migration in a transaction, where `CONCURRENTLY` is not allowed.

## 11. Known traps

- `sqlx::migrate!` embeds the files at compile time: a new file changes the binary's schema version to 3 (the amended
  `schema.rs` expects exactly that). Name the file `0003_<words>.sql` like the others.

## Measured (R-OPT-2)

Revision `83edb687f`; Apple M5 Pro (18 cores, 64 GB), macOS 26, native; PostgreSQL 16 (pgtest cluster, fsync off);
scratch database with migrations 0001-0002, 200 000 profiles, 2 020 000 ledger rows (20 per player of the month,
20 000 for one heavy player), 300 000 purchases; `pgbench -n -c 1 -T 5`; with and without the index, interleaved twice.
Machine shared (load average 100-114: another session's full gate, a game client); the ratios are the finding.

| Statement | Without | With | Index size |
|---|---|---|---|
| claim undo (`discard_unclaimed_profile` + the FK check of the delete), random fresh profile | 70.6 ms, 67.2 ms | 1.5 ms, 1.7 ms | `purchases_profile` 4.8 MB (table 22 MB) |
| settlement ledger lookup, random player (20 rows) | 0.42 ms, 0.19 ms | 0.25 ms, 0.17 ms | `(profile_id, ref)` 131 MB (table 210 MB; existing `gv_ledger_profile` 19 MB) |
| settlement ledger lookup, heavy player (20 000 rows) | 9.2 ms, 3.6 ms | 0.21 ms, 0.13 ms | same |

## Notes from previous attempts
