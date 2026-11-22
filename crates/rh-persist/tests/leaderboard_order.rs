//! P-DB-LEADERBOARD-1 oracle: one tie order for every leaderboard read, whatever the database's default collation.
//! The legacy comparison (`byGvThenName`) is GV descending, then the callsign by code unit, which is PostgreSQL's
//! `COLLATE "C"` for the callsign alphabet `[A-Za-z0-9_-]`. A production database is usually created with a natural
//! language collation (en-US), where "Zed" sorts after "abe" and `_` before `-`; the test cluster's default is "C", so
//! these tests make their own database with an ICU en-US default to see the difference.
//!   eval "$(tools/pgtest/start.sh)"; cargo test -p rh-persist --test leaderboard_order

use rh_persist::Store;
use sqlx::postgres::{PgConnectOptions, PgPoolOptions};
use sqlx::{Connection, PgConnection};
use std::str::FromStr;
use std::time::Duration;
use uuid::Uuid;

const MONTH: &str = "2026-11";

struct IcuDb {
    store: Store,
    name: String,
    admin: PgConnectOptions,
}

/// A throwaway database whose DEFAULT collation is ICU en-US (not "C"), migrated. `None` only with RH_SKIP_PG_TESTS=1.
async fn icu_db() -> Option<IcuDb> {
    let Ok(url) = std::env::var("DATABASE_URL") else {
        if std::env::var("RH_SKIP_PG_TESTS").as_deref() == Ok("1") {
            eprintln!("SKIPPED: RH_SKIP_PG_TESTS=1 and no DATABASE_URL");
            return None;
        }
        panic!("DATABASE_URL is not set (eval \"$(tools/pgtest/start.sh)\")");
    };
    let admin = PgConnectOptions::from_str(&url).expect("DATABASE_URL parses");
    let name = format!("rh_lb_{}", Uuid::new_v4().simple());
    let mut conn = PgConnection::connect_with(&admin).await.unwrap();
    sqlx::query(&format!(
        "CREATE DATABASE {name} TEMPLATE template0 LOCALE_PROVIDER icu ICU_LOCALE 'en-US'"
    ))
    .execute(&mut conn)
    .await
    .expect("create an ICU en-US database (PostgreSQL built with ICU)");
    let pool = PgPoolOptions::new()
        .max_connections(4)
        .acquire_timeout(Duration::from_secs(60))
        .connect_with(admin.clone().database(&name))
        .await
        .unwrap();
    let store = Store::from_pool(pool);
    store.migrate().await.expect("migrate");
    // The premise: this database's default order is NOT byte order.
    let natural: Vec<String> =
        sqlx::query_scalar("SELECT n FROM unnest(ARRAY['abe', 'Zed']) n ORDER BY n")
            .fetch_all(store.pool())
            .await
            .unwrap();
    assert_eq!(
        natural,
        ["abe", "Zed"],
        "the test database must sort naturally"
    );
    Some(IcuDb { store, name, admin })
}

impl IcuDb {
    /// A profile with `gv` banked in `month` (the two tables the leaderboard reads; the result path is tested elsewhere).
    async fn banked(&self, callsign: &str, month: &str, gv: i64) -> i64 {
        self.store
            .create_profile(callsign, &format!("tok-{callsign}"))
            .await
            .unwrap();
        let id = self.store.profile_id(callsign).await.unwrap().unwrap();
        sqlx::query("INSERT INTO gv_by_month (profile_id, month, gv) VALUES ($1, $2, $3)")
            .bind(id)
            .bind(month)
            .bind(gv)
            .execute(self.store.pool())
            .await
            .unwrap();
        id
    }

    async fn teardown(self) {
        self.store.pool().close().await;
        if let Ok(mut conn) = PgConnection::connect_with(&self.admin).await {
            let _ = sqlx::query(&format!(
                "DROP DATABASE IF EXISTS {} WITH (FORCE)",
                self.name
            ))
            .execute(&mut conn)
            .await;
        }
    }
}

#[tokio::test]
async fn every_leaderboard_read_breaks_ties_by_byte_order_on_a_natural_collation_database() {
    let Some(db) = icu_db().await else {
        return;
    };
    db.banked("top", MONTH, 900).await;
    for name in ["abe", "Zed", "a_b", "a-b", "Mid", "aa"] {
        db.banked(name, MONTH, 100).await;
    }
    // GV descending, then code-unit order: uppercase before lowercase, '-' (0x2D) before '_' (0x5F) before letters.
    let expected = ["top", "Mid", "Zed", "a-b", "a_b", "aa", "abe"];

    let legacy: Vec<String> = db
        .store
        .leaderboard(MONTH, 10)
        .await
        .unwrap()
        .into_iter()
        .map(|(n, _)| n)
        .collect();
    let rows: Vec<String> = db
        .store
        .leaderboard_rows(MONTH, 10)
        .await
        .unwrap()
        .into_iter()
        .map(|r| r.name)
        .collect();
    assert_eq!(rows, expected, "leaderboard_rows");
    assert_eq!(
        legacy, expected,
        "Store::leaderboard breaks ties by the database's default collation, not byte order like leaderboard_rows"
    );
    for (i, name) in expected.iter().enumerate() {
        let id = db.store.profile_id(name).await.unwrap().unwrap();
        let (rank, _) = db.store.leaderboard_rank(MONTH, id).await.unwrap().unwrap();
        assert_eq!(rank, i as i64 + 1, "rank of {name}");
    }
    db.teardown().await;
}

#[tokio::test]
async fn the_rank_is_the_listed_position_through_heavy_ties() {
    let Some(db) = icu_db().await else {
        return;
    };
    // 240 profiles over five GV values; the six name stems cycle independently, so every tie group mixes case and
    // punctuation.
    let mut ids = Vec::new();
    for i in 0..240u32 {
        let stem = ["a", "A", "b-", "b_", "Z", "z"][(i % 6) as usize];
        let name = format!("{stem}{:03}", (i * 37) % 240);
        let gv = 1 + i64::from(i % 5) * 50;
        ids.push((db.banked(&name, MONTH, gv).await, name));
    }
    // Zero GV, another month and a bad month are unranked.
    let idle = db.banked("idle", MONTH, 0).await;
    let other = db.banked("other", "2026-10", 5000).await;

    let rows = db.store.leaderboard_rows(MONTH, 1000).await.unwrap();
    assert_eq!(rows.len(), 240);
    for (i, row) in rows.iter().enumerate() {
        let id = ids.iter().find(|(_, n)| *n == row.name).unwrap().0;
        assert_eq!(
            db.store.leaderboard_rank(MONTH, id).await.unwrap(),
            Some((i as i64 + 1, row.gv)),
            "rank of {}",
            row.name
        );
    }
    assert_eq!(db.store.leaderboard_rank(MONTH, idle).await.unwrap(), None);
    assert_eq!(db.store.leaderboard_rank(MONTH, other).await.unwrap(), None);
    assert_eq!(
        db.store.leaderboard_rank("2026-10", other).await.unwrap(),
        Some((1, 5000))
    );
    assert_eq!(
        db.store.leaderboard_rank("garbage", other).await.unwrap(),
        None
    );
    db.teardown().await;
}
