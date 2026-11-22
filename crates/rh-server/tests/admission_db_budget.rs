//! P-DB-POOL-1 oracle: admission never waits on a saturated database pool longer than it waits for a lease.
//! The credential check used to have no bound but the pool's 30 s acquire timeout, so a reconnect storm held every socket
//! for half a minute. A real PostgreSQL from DATABASE_URL (feature `postgres`):
//!   eval "$(tools/pgtest/start.sh)"; cargo test -p rh-server --features postgres --test admission_db_budget
#![cfg(feature = "postgres")]

use rh_protocol::{RejectCode, PROTOCOL_VERSION};
use rh_server::director::{Director, DirectorConfig, Hello, LeasePolicy};
use rh_server::hosting::Lifecycle;
use rh_server::outbox::Outbox;
use rh_server::persist::{Auth, Persistence};
use rh_sim::map::tile_to_world_center;
use rh_sim::{Arena, SimConfig, TileMap, Vec2};
use sqlx::postgres::PgPoolOptions;
use std::sync::Arc;
use std::time::Duration;

fn arena() -> Arena {
    let rows: Vec<String> = (0..8)
        .map(|y| {
            (0..12)
                .map(|x| {
                    if x == 0 || y == 0 || x == 11 || y == 7 {
                        '#'
                    } else {
                        '.'
                    }
                })
                .collect()
        })
        .collect();
    Arena {
        map: TileMap::from_rows(&rows).unwrap(),
        spawns: vec![
            Vec2::new(tile_to_world_center(5), tile_to_world_center(4)),
            Vec2::new(tile_to_world_center(6), tile_to_world_center(4)),
        ],
    }
}

fn hello(profile: &str, ticket: &str) -> Hello {
    Hello {
        protocol: PROTOCOL_VERSION,
        content_hash: rh_sim::content::content_hash(),
        profile: profile.into(),
        ticket: ticket.as_bytes().to_vec(),
        faction: None,
        resume: None,
    }
}

#[tokio::test(flavor = "multi_thread", worker_threads = 2)]
async fn a_saturated_pool_refuses_the_hello_within_the_lease_db_budget() {
    let Ok(url) = std::env::var("DATABASE_URL") else {
        if std::env::var("RH_SKIP_PG_TESTS").as_deref() == Ok("1") {
            eprintln!("SKIPPED: RH_SKIP_PG_TESTS=1");
            return;
        }
        panic!("DATABASE_URL is not set (eval \"$(tools/pgtest/start.sh)\")");
    };
    let admin = rh_persist::Store::connect(&url).await.unwrap();
    let db = format!("rh_admit_budget_{}", std::process::id());
    let _ = sqlx::query(&format!("DROP DATABASE IF EXISTS {db}"))
        .execute(admin.pool())
        .await;
    sqlx::query(&format!("CREATE DATABASE {db}"))
        .execute(admin.pool())
        .await
        .unwrap();
    let db_url = match url.rfind('/') {
        Some(i) => format!("{}/{db}", &url[..i]),
        None => panic!("DATABASE_URL has no database name"),
    };
    {
        let setup = rh_persist::Store::connect(&db_url).await.unwrap();
        setup.migrate().await.unwrap();
        setup.create_profile("romeo", "tok-romeo").await.unwrap();
        setup.pool().close().await;
    }
    // A pool of ONE connection with the store's own 30 s acquire timeout: the shape of a saturated pool.
    let pool = PgPoolOptions::new()
        .max_connections(1)
        .acquire_timeout(Duration::from_secs(30))
        .connect(&db_url)
        .await
        .unwrap();
    let store = Arc::new(rh_persist::Store::from_pool(pool.clone()));

    let db_timeout = Duration::from_millis(300);
    let cfg = DirectorConfig {
        leases: LeasePolicy {
            db_timeout,
            ..LeasePolicy::default()
        },
        ..DirectorConfig::new(SimConfig::default(), arena())
    };
    let lifecycle = Arc::new(Lifecycle::default());
    let director = Director::new(
        cfg,
        Arc::new(Persistence::postgres(store.clone(), "test-host".into())),
        Arc::new(Auth::Postgres(store.clone())),
        lifecycle.clone(),
    );
    lifecycle.ready();

    // Every connection of the pool is busy (a reconnect storm's credential checks, say).
    let held = pool.acquire().await.unwrap();
    let started = tokio::time::Instant::now();
    let answer = tokio::time::timeout(
        Duration::from_secs(3),
        director.admit(hello("romeo", "tok-romeo"), Arc::new(Outbox::default()), 1),
    )
    .await;
    let waited = started.elapsed();
    let answer = answer.unwrap_or_else(|_| {
        panic!(
            "admission still waiting on the saturated pool after {waited:?}: the credential check is not bounded by \
             the lease db_timeout ({db_timeout:?})"
        )
    });
    match answer {
        Err((code, reason)) => {
            assert_eq!(
                code,
                RejectCode::RateLimited,
                "a storage stall is 'try again shortly', never a credential verdict: {reason}"
            );
            assert!(reason.contains("storage unavailable"), "{reason}");
        }
        Ok(_) => panic!("admitted while every pool connection was held"),
    }
    assert!(
        waited < db_timeout * 5,
        "refused after {waited:?}; the bound is the lease db_timeout ({db_timeout:?})"
    );

    // With the pool free again the credential check answers on its merits: a wrong ticket is still a bad credential.
    drop(held);
    let wrong = tokio::time::timeout(
        Duration::from_secs(10),
        director.admit(hello("romeo", "wrong"), Arc::new(Outbox::default()), 2),
    )
    .await
    .expect("a healthy credential check answers");
    assert!(
        matches!(wrong, Err((RejectCode::BadCredentials, _))),
        "a wrong ticket on a healthy store is BadCredentials"
    );

    drop(director);
    pool.close().await;
    let _ = sqlx::query(&format!("DROP DATABASE IF EXISTS {db} WITH (FORCE)"))
        .execute(admin.pool())
        .await;
}
