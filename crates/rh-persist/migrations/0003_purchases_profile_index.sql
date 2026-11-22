-- P-DB-INDEX-1: purchases looked up by profile (discard_unclaimed_profile's NOT EXISTS, and PostgreSQL's foreign-key check
-- when a profiles row is deleted) scanned the whole table; 0001 indexes only purchase_id. Additive migration: no query
-- result changes, old binaries keep running (schema_compat.min_binary stays 1).
--
-- Not CONCURRENTLY: sqlx would allow it after a `-- no-transaction` first line, but a failed concurrent build leaves an
-- INVALID index and no _sqlx_migrations row, so the retry fails. Inside the migration's transaction a failure leaves
-- nothing. The plain build blocks writes to purchases (reads continue) while it runs: well under a second at the
-- measured sizes (300 000 rows, a 4.8 MB index).
CREATE INDEX purchases_profile ON purchases (profile_id);
