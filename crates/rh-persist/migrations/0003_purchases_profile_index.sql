-- P-DB-INDEX-1: purchases looked up by profile (discard_unclaimed_profile's NOT EXISTS, and PostgreSQL's foreign-key check
-- when a profiles row is deleted) scanned the whole table; 0001 indexes only purchase_id. Additive migration: no query
-- result changes, old binaries keep running (schema_compat.min_binary stays 1).
--
-- Not CONCURRENTLY: sqlx runs each migration in a transaction, where CONCURRENTLY is refused. The plain build holds a
-- write lock on purchases while it runs, which at the measured sizes (300 000 rows, a 4.8 MB index) is well under a second.
CREATE INDEX purchases_profile ON purchases (profile_id);
