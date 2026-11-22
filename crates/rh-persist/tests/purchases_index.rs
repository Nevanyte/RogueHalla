//! P-DB-INDEX-1 oracle: purchases are found by profile through an index, not a scan of every purchase.
//! `discard_unclaimed_profile` (and PostgreSQL's foreign-key check when a profile row is deleted) look purchases up by
//! `profile_id`; migration 0001 indexes only `purchase_id`.
//!   eval "$(tools/pgtest/start.sh)"; cargo test -p rh-persist --test purchases_index
mod common;

use common::setup;

#[tokio::test]
async fn purchases_have_an_index_led_by_profile_id_and_the_lookup_uses_it() {
    let Some(db) = setup().await else { return };
    // Any index on `purchases` whose first key column is `profile_id` (its name is the migration's choice).
    let led_by_profile: i64 = sqlx::query_scalar(
        "SELECT count(*) FROM pg_index i
           JOIN pg_class t ON t.oid = i.indrelid
           JOIN pg_attribute a ON a.attrelid = t.oid AND a.attnum = i.indkey[0]
          WHERE t.relname = 'purchases' AND a.attname = 'profile_id'",
    )
    .fetch_one(db.store.pool())
    .await
    .unwrap();
    assert_eq!(
        led_by_profile, 1,
        "exactly one index on purchases leads with profile_id after migrate()"
    );

    // With sequential scans priced out, the planner reaches purchases by profile through that index.
    let mut tx = db.store.pool().begin().await.unwrap();
    sqlx::query("SET LOCAL enable_seqscan = off")
        .execute(&mut *tx)
        .await
        .unwrap();
    let plan: Vec<String> =
        sqlx::query_scalar("EXPLAIN SELECT 1 FROM purchases WHERE profile_id = 1 LIMIT 1")
            .fetch_all(&mut *tx)
            .await
            .unwrap();
    let plan = plan.join("\n");
    assert!(
        plan.contains("Index") && !plan.contains("Seq Scan on purchases"),
        "the by-profile lookup uses an index:\n{plan}"
    );
    tx.rollback().await.unwrap();
    db.teardown().await;
}
