use crate::database::*;
use crate::first_playable::*;
use crate::flags::*;
use crate::settings::*;
use rh_server::api::ApiConfig;
use rh_server::edge::OriginPolicy;
use std::net::SocketAddr;
use std::time::Duration;

fn args(values: &[&str]) -> Vec<String> {
    values.iter().map(|s| s.to_string()).collect()
}

#[test]
fn full_wish_mode_is_a_single_explicit_flag_and_defaults_off() {
    assert!(!wish_full_enabled(&args(&["rh-server"])).unwrap());
    assert!(wish_full_enabled(&args(&["rh-server", "--wish-full"])).unwrap());
    for bad in [
        vec!["--wish-full", "--wish-full"],
        vec!["--wish-full", "true"],
        vec!["--wish-full=true"],
        vec!["--wish-full-extra"],
    ] {
        assert!(wish_full_enabled(&args(&bad)).is_err(), "{bad:?}");
    }
}

#[test]
fn first_playable_profile_is_opt_in_and_limits_seat_overrides() {
    assert!(first_playable_settings(&args(&["rh-server"]))
        .unwrap()
        .is_none());
    let profile = first_playable_settings(&args(&[
        "rh-server",
        "--first-playable",
        "--min-players",
        "1",
        "--fill-bots",
        "8",
    ]))
    .unwrap()
    .unwrap();
    assert_eq!(profile.min_players, 1);
    assert_eq!(profile.fill_bots, 8);
    for bad in [
        vec!["--first-playable", "--map", "x"],
        vec!["--first-playable", "--map=x"],
        vec!["--first-playable", "--first-playable"],
        vec!["--first-playable", "true"],
        vec!["--first-playable-lifetime", "99999"],
        vec!["--first-playable", "--fill-bots", "9"],
        vec!["--first-playable", "--fill-bots", "1.5"],
        vec!["--first-playable", "--min-players", "0"],
        vec!["--first-playable", "--min-players", "9"],
        vec!["--first-playable", "--min-players=2"],
        vec!["--first-playable", "--lobby-seconds", "NaN"],
        vec!["--first-playable", "--fill-seconds", "-1"],
        vec!["--first-playable", "--fill-bots", "1", "--fill-bots", "2"],
    ] {
        assert!(first_playable_settings(&args(&bad)).is_err(), "{bad:?}");
    }
}
#[test]
fn embedded_map_is_admitted_and_fits_the_process_budget() {
    let arena = first_playable_arena().unwrap();
    rh_server::wire::arena_fits(&arena).unwrap();
    let data = arena.map.layers().unwrap().data();
    assert_eq!(arena.spawns.len(), 8);
    assert_eq!(data.crate_spots.len(), 8);
    assert_eq!(data.ground_spots.len(), 48);
    assert_eq!(data.camps.iter().map(|c| c.spots.len()).sum::<usize>(), 4);
    assert_eq!(rh_sim::constants::MAX_PLAYERS, 8);
    assert_eq!(rh_sim::constants::CAMP_RESPAWN_MS, 180000.);
    assert_eq!(
        (FIRST_PLAYABLE_LIFETIME + FIRST_PLAYABLE_GRACE).as_secs(),
        14580
    );
    let waves = 1 + 14580 / 180;
    let units = 4 * waves + 48 + 16;
    assert_eq!(8 + 48 + 8 + units, 456);
    assert!(8 + 48 + 8 + units <= 512);
}
#[tokio::test(start_paused = true)]
async fn service_deadlines_are_absolute_and_disabled_has_no_timer() {
    let started = tokio::time::Instant::now();
    let auto = tokio::spawn(service_deadline(Some(started + FIRST_PLAYABLE_LIFETIME)));
    let hard = tokio::spawn(service_deadline(Some(
        started + FIRST_PLAYABLE_LIFETIME + FIRST_PLAYABLE_GRACE,
    )));
    let off = tokio::spawn(service_deadline(None));
    tokio::task::yield_now().await;
    tokio::time::advance(FIRST_PLAYABLE_LIFETIME - Duration::from_secs(1)).await;
    assert!(!auto.is_finished());
    assert!(!hard.is_finished());
    tokio::time::advance(Duration::from_secs(1)).await;
    tokio::task::yield_now().await;
    assert!(auto.is_finished());
    tokio::time::advance(FIRST_PLAYABLE_GRACE).await;
    tokio::task::yield_now().await;
    assert!(hard.is_finished());
    assert!(!off.is_finished());
    off.abort();
}

#[test]
fn lease_cli_accepts_defaults_and_a_valid_override() {
    assert_eq!(lease_policy(&[]).unwrap().ttl, Duration::from_secs(120));
    let policy = lease_policy(&args(&[
        "--lease-ttl-secs",
        "15",
        "--lease-renew-secs",
        "4",
        "--lease-db-timeout-secs",
        "2",
    ]))
    .unwrap();
    assert_eq!(policy.ttl, Duration::from_secs(15));
    assert_eq!(policy.renew_interval, Duration::from_secs(4));
    assert_eq!(policy.db_timeout, Duration::from_secs(2));
    assert_eq!(
        lease_policy(&args(&["--lease-ttl-secs", "3600"]))
            .unwrap()
            .ttl,
        Duration::from_secs(3600)
    );
}

#[cfg(feature = "postgres")]
#[test]
fn spool_dead_bound_is_a_flag_and_retry_secs_is_only_an_alert_threshold() {
    let (_, cfg) = spool_settings(&args(&["--result-spool", "d", "--result-dead-max", "7"]))
        .unwrap()
        .unwrap();
    assert_eq!(cfg.max_dead, 7);
    assert_eq!(
        cfg.max_records, 4096,
        "dead letters no longer share the pending bound"
    );
    assert!(spool_settings(&args(&["--result-dead-max", "7"])).is_err());
    assert!(spool_settings(&args(&["--result-spool", "d", "--result-dead-max", "0"])).is_err());
}

#[test]
fn hosted_flags_parse_and_a_misspelt_one_is_refused() {
    let off = hosted_settings(&args(&["rh-server", "--bind", "x"])).unwrap();
    assert!(!off.require_admit && off.admit_ttl.is_none() && off.token.is_none());
    let on = hosted_settings(&args(&[
        "rh-server",
        "--control",
        "127.0.0.1:1",
        "--control-insecure-loopback",
        "--require-admit",
        "--admit-ttl-secs",
        "30",
    ]))
    .unwrap();
    assert!(on.require_admit);
    assert_eq!(on.admit_ttl, Some(Duration::from_secs(30)));
    for bad in [
        vec!["rh-server", "--control", "x", "--require-admt"],
        vec!["rh-server", "--control", "x", "--require-admit=true"],
        vec!["rh-server", "--control", "x", "--admit-ttl", "5"],
        vec!["rh-server", "--control-tokenfile", "f", "--control", "x"],
        vec!["rh-server", "--require-admit"],
        vec!["rh-server", "--control", "x", "--admit-ttl-secs", "0"],
        vec!["rh-server", "--control", "x", "--admit-ttl-secs", "301"],
        vec!["rh-server", "--control", "x", "--control-token-file"],
        vec![
            "rh-server",
            "--control",
            "x",
            "--control-token-file",
            "/nonexistent/token",
        ],
    ] {
        assert!(hosted_settings(&args(&bad)).is_err(), "{bad:?}");
    }
}

#[test]
fn a_control_listener_needs_a_token_or_an_explicit_insecure_loopback_flag() {
    let refused = hosted_settings(&args(&["rh-server", "--control", "127.0.0.1:1"]))
        .err()
        .expect("refused");
    assert!(
        refused.contains("--control-token-file") && refused.contains("--control-insecure-loopback"),
        "{refused}"
    );
    let dir = std::env::temp_dir().join(format!("rh-ctl-token-unit-{}", std::process::id()));
    std::fs::write(&dir, "tok\n").unwrap();
    let token = hosted_settings(&args(&[
        "rh-server",
        "--control",
        "127.0.0.1:1",
        "--control-token-file",
        dir.to_str().unwrap(),
    ]))
    .unwrap();
    assert_eq!(token.token.as_deref(), Some("tok"));
    let _ = std::fs::remove_file(&dir);
    assert!(hosted_settings(&args(&[
        "rh-server",
        "--control",
        "127.0.0.1:1",
        "--control-insecure-loopback"
    ]))
    .unwrap()
    .token
    .is_none());
    // The opt-out means nothing without a control listener, and no control listener means no requirement.
    assert!(hosted_settings(&args(&["rh-server", "--control-insecure-loopback"])).is_err());
    assert!(hosted_settings(&args(&["rh-server", "--bind", "x"])).is_ok());
}

#[test]
fn drain_and_database_budget_flags_parse_with_safe_defaults() {
    let d = drain_settings(&args(&["rh-server"])).unwrap();
    assert_eq!(d.max, Some(Duration::from_secs(55)));
    assert_eq!(d.write_wait, Duration::from_secs(20));
    assert!(
        d.max.unwrap() + d.write_wait + HTTP_SHUTDOWN_GRACE <= Duration::from_secs(80),
        "inside systemd's default TimeoutStopSec"
    );
    let d = drain_settings(&args(&[
        "rh-server",
        "--drain-max-secs",
        "0",
        "--drain-write-wait-secs",
        "5",
    ]))
    .unwrap();
    assert_eq!((d.max, d.write_wait), (None, Duration::from_secs(5)));
    for bad in [
        vec!["rh-server", "--drain-max-secs", "x"],
        vec!["rh-server", "--drain-max-secs", "-1"],
        vec!["rh-server", "--drain-write-wait-secs", "0"],
        vec!["rh-server", "--drain-max-secs"],
        vec!["rh-server", "--drain-max-secs", "86401"],
    ] {
        assert!(drain_settings(&args(&bad)).is_err(), "{bad:?}");
    }
    assert_eq!(
        db_connect_budget(&args(&["rh-server"])).unwrap(),
        Duration::from_secs(60)
    );
    assert_eq!(
        db_connect_budget(&args(&["rh-server", "--db-connect-timeout-secs", "0"])).unwrap(),
        Duration::ZERO
    );
    assert!(db_connect_budget(&args(&["rh-server", "--db-connect-timeout-secs", "abc"])).is_err());
    assert!(db_connect_budget(&args(&["rh-server", "--db-connect-timeout-secs", "3601"])).is_err());
}

#[tokio::test(start_paused = true)]
async fn the_connect_retry_backs_off_then_succeeds_or_gives_up_within_its_budget() {
    let (ms, no_show) = (Duration::from_millis, |e: &String| e.clone());
    let not_fatal = |_: &String| false;
    // Two failures, then success: backoff 100, 200.
    let t0 = tokio::time::Instant::now();
    let tries = std::cell::Cell::new(0u32);
    let got = retry_with_backoff(
        Duration::from_secs(60),
        ms(100),
        ms(800),
        not_fatal,
        no_show,
        |n| {
            tries.set(n);
            async move {
                if n < 3 {
                    Err(format!("down {n}"))
                } else {
                    Ok(n)
                }
            }
        },
    )
    .await;
    assert_eq!(got, Ok(3));
    assert_eq!(t0.elapsed(), ms(300));
    // Never reachable: stops when the budget is spent, with the last error, and does not overshoot it.
    let t0 = tokio::time::Instant::now();
    let got: Result<(), String> = retry_with_backoff(
        Duration::from_secs(2),
        ms(500),
        ms(1000),
        not_fatal,
        no_show,
        |n| async move { Err(format!("down {n}")) },
    )
    .await;
    assert!(got.unwrap_err().starts_with("down "));
    assert_eq!(t0.elapsed(), Duration::from_secs(2));
    // Budget zero is one attempt; a fatal error is never retried.
    let tries = std::cell::Cell::new(0u32);
    let got: Result<(), String> =
        retry_with_backoff(Duration::ZERO, ms(100), ms(100), not_fatal, no_show, |n| {
            tries.set(n);
            async move { Err("x".to_string()) }
        })
        .await;
    assert!(got.is_err() && tries.get() == 1);
    let tries = std::cell::Cell::new(0u32);
    let got: Result<(), String> = retry_with_backoff(
        Duration::from_secs(60),
        ms(100),
        ms(100),
        |_| true,
        no_show,
        |n| {
            tries.set(n);
            async move { Err("schema".to_string()) }
        },
    )
    .await;
    assert!(got.is_err() && tries.get() == 1);
}

#[test]
fn logged_database_errors_never_carry_the_connection_string_or_password() {
    let url = "postgres://rh_admin:s3cr3t-pw@db.internal:5432/roguehalla";
    let msg = format!("error connecting to {url} (user rh_admin, password s3cr3t-pw): refused");
    let out = redact_secrets(&msg, &[url]);
    for leak in ["s3cr3t-pw", "rh_admin", "db.internal", "postgres://"] {
        assert!(!out.contains(leak), "{leak} leaked: {out}");
    }
    assert_eq!(
        redact_secrets("connection refused", &[url]),
        "connection refused"
    );
}

#[cfg(feature = "postgres")]
#[test]
fn spool_cli_is_optional_validated_and_bounded() {
    assert!(spool_settings(&[]).unwrap().is_none());
    let (dir, cfg) = spool_settings(&args(&[
        "--result-spool",
        "/var/rh/spool",
        "--result-spool-max",
        "10",
        "--result-retry-secs",
        "60",
    ]))
    .unwrap()
    .unwrap();
    assert_eq!(dir, std::path::PathBuf::from("/var/rh/spool"));
    assert_eq!(cfg.max_records, 10);
    assert_eq!(cfg.retry_deadline, Duration::from_secs(60));
    for bad in [
        vec!["--result-spool"],
        vec!["--result-spool", "--bind"],
        vec!["--result-spool-max", "5"],
        vec!["--result-spool", "d", "--result-spool-max", "0"],
        vec!["--result-spool", "d", "--result-retry-secs", "x"],
    ] {
        assert!(spool_settings(&args(&bad)).is_err(), "{bad:?}");
    }
}

fn bind(s: &str) -> SocketAddr {
    s.parse().unwrap()
}

#[test]
fn dev_auth_is_refused_on_a_public_listener_unless_explicitly_accepted() {
    let lo = bind("127.0.0.1:7777");
    let any = bind("0.0.0.0:7777");
    let v6 = bind("[::]:7777");
    // Local development and every test harness: loopback, no proxy header.
    let e = exposure_check(&args(&["rh-server"]), lo).unwrap();
    assert!(!e.public && e.warnings.is_empty());
    assert!(exposure_check(&args(&["rh-server", "--auth", "dev"]), bind("[::1]:1")).is_ok());
    for public in [any, v6, bind("10.0.0.5:7777")] {
        for dev in [vec!["rh-server"], vec!["rh-server", "--auth", "dev"]] {
            let err = exposure_check(&args(&dev), public).unwrap_err();
            assert!(err.contains("--insecure-dev-auth"), "{err}");
        }
    }
    // Behind a reverse proxy (forwarded-address header) a loopback bind is public too.
    assert!(exposure_check(
        &args(&["rh-server", "--client-ip-header", "x-forwarded-for"]),
        lo
    )
    .is_err());
    // Real credentials on a public bind are fine.
    for ok in [
        vec!["rh-server", "--database-url", "postgres://x"],
        vec!["rh-server", "--auth", "static:/etc/tokens"],
        vec![
            "rh-server",
            "--auth",
            "dev",
            "--database-url",
            "postgres://x",
        ],
    ] {
        let e = exposure_check(&args(&ok), any).unwrap();
        assert!(e.public && e.warnings.is_empty(), "{ok:?}");
    }
    let e = exposure_check(&args(&["rh-server", "--insecure-dev-auth"]), any).unwrap();
    assert!(e.warnings[0].starts_with("INSECURE"), "{:?}", e.warnings);
    for typo in ["--insecure-devauth", "--insecure", "--insecure-dev-auth=1"] {
        assert!(
            exposure_check(&args(&["rh-server", typo]), lo).is_err(),
            "{typo}"
        );
    }
    let unused = exposure_check(&args(&["rh-server", "--insecure-dev-auth"]), lo).unwrap();
    assert_eq!(
        unused.warnings.len(),
        1,
        "an unneeded --insecure flag is called out"
    );
}

#[test]
fn self_signed_tls_is_refused_on_a_public_listener_and_tls_flags_must_pair() {
    let lo = bind("127.0.0.1:7443");
    let any = bind("0.0.0.0:7443");
    let db = ["--database-url", "postgres://x"];
    let with = |extra: &[&str]| {
        let mut v = vec!["rh-server"];
        v.extend_from_slice(&db);
        v.extend_from_slice(extra);
        args(&v)
    };
    assert!(exposure_check(&with(&["--self-signed"]), lo).is_ok());
    let err = exposure_check(&with(&["--self-signed"]), any).unwrap_err();
    assert!(err.contains("--insecure-self-signed"), "{err}");
    let e = exposure_check(&with(&["--self-signed", "--insecure-self-signed"]), any).unwrap();
    assert!(e.warnings[0].starts_with("INSECURE"));
    assert!(exposure_check(&with(&["--tls-cert", "c.pem", "--tls-key", "k.pem"]), any).is_ok());
    for bad in [
        vec!["--tls-cert", "c.pem"],
        vec!["--tls-key", "k.pem"],
        vec!["--tls-cert"],
        vec!["--self-signed", "--tls-cert", "c.pem", "--tls-key", "k.pem"],
    ] {
        assert!(exposure_check(&with(&bad), lo).is_err(), "{bad:?}");
    }
}

#[test]
fn edge_flags_default_and_ws_origins_follow_the_api_cors_origins() {
    let api = api_config(&args(&[
        "rh-server",
        "--api-cors-origins",
        "https://play.rougehalla.com",
        "--client-ip-header",
        "x-forwarded-for",
        "--trusted-proxies",
        "127.0.0.1",
    ]))
    .unwrap();
    let (cfg, limits, warnings) = edge_settings(&args(&["rh-server"]), &api, true).unwrap();
    assert_eq!(
        cfg.origins,
        OriginPolicy::Only(vec!["https://play.rougehalla.com".into()])
    );
    assert_eq!(cfg.client_ip_header.as_ref().unwrap(), "x-forwarded-for");
    assert_eq!(
        cfg.trusted_proxies,
        vec!["127.0.0.1".parse::<std::net::IpAddr>().unwrap()]
    );
    assert_eq!(cfg.max_per_ip, rh_server::edge::DEFAULT_WS_MAX_PER_IP);
    assert_eq!(cfg.max_pending, rh_server::edge::DEFAULT_WS_MAX_PENDING);
    assert_eq!(cfg.auth_fails_per_minute, 10);
    assert_eq!(cfg.auth_fails_per_minute_all, 0);
    assert_eq!(limits.header_read_timeout, Duration::from_secs(10));
    assert!(warnings.is_empty());

    let (cfg, limits, _) = edge_settings(
        &args(&[
            "rh-server",
            "--ws-origins",
            "https://a.example,https://b.example/",
            "--ws-max-per-ip",
            "0",
            "--ws-connect-burst",
            "5",
            "--ws-connect-per-second",
            "1",
            "--ws-max-pending",
            "32",
            "--ws-auth-fails-per-minute",
            "3",
            "--ws-auth-fails-per-minute-all",
            "100",
            "--http-header-timeout-secs",
            "4",
        ]),
        &api,
        true,
    )
    .unwrap();
    assert_eq!(
        cfg.origins,
        OriginPolicy::Only(vec!["https://a.example".into(), "https://b.example".into()])
    );
    assert_eq!(
        (
            cfg.max_per_ip,
            cfg.connect_burst,
            cfg.connect_per_second,
            cfg.max_pending
        ),
        (0, 5.0, 1.0, 32)
    );
    assert_eq!(
        (cfg.auth_fails_per_minute, cfg.auth_fails_per_minute_all),
        (3, 100)
    );
    assert_eq!(limits.header_read_timeout, Duration::from_secs(4));
}

#[test]
fn an_empty_origin_list_is_permissive_only_when_not_publicly_exposed() {
    let api = ApiConfig::default();
    let (local, _, w) = edge_settings(&args(&["rh-server"]), &api, false).unwrap();
    assert_eq!(local.origins, OriginPolicy::Any);
    assert!(w.is_empty());
    let (public, _, w) = edge_settings(&args(&["rh-server"]), &api, true).unwrap();
    assert_eq!(public.origins, OriginPolicy::Only(vec![]));
    assert_eq!(w.len(), 1);
    let (star, _, w) =
        edge_settings(&args(&["rh-server", "--ws-origins", "*"]), &api, true).unwrap();
    assert_eq!(star.origins, OriginPolicy::Any);
    assert_eq!(w.len(), 1, "a public '*' is called out");
}

#[test]
fn edge_flags_refuse_typos_and_bad_values() {
    let api = ApiConfig::default();
    for bad in [
        vec!["rh-server", "--ws-max-per-address", "4"],
        vec!["rh-server", "--ws-origin", "https://a.example"],
        vec!["rh-server", "--http-header-timeout", "4"],
        vec!["rh-server", "--ws-max-per-ip", "-1"],
        vec!["rh-server", "--ws-max-pending", "x"],
        vec!["rh-server", "--ws-origins"],
        vec!["rh-server", "--ws-origins", "--bind"],
        vec!["rh-server", "--http-header-timeout-secs", "0"],
        vec!["rh-server", "--http-header-timeout-secs", "301"],
    ] {
        assert!(edge_settings(&args(&bad), &api, false).is_err(), "{bad:?}");
    }
}

#[test]
fn lease_cli_refuses_invalid_values_and_expiry_without_renewal_headroom() {
    for values in [
        vec!["--lease-ttl-secs"],
        vec!["--lease-ttl-secs", "NaN"],
        vec!["--lease-ttl-secs", "-1"],
        vec!["--lease-ttl-secs", "0"],
        vec!["--lease-ttl-secs", "35"],
        vec!["--lease-ttl-secs", "3601"],
        vec!["--lease-renew-secs", "0"],
        vec!["--lease-db-timeout-secs", "0"],
        vec!["--lease-ttl-secs", "18446744073709551615"],
    ] {
        assert!(lease_policy(&args(&values)).is_err(), "{values:?}");
    }
}

/// Integration of fix/server-security and fix/server-resilience with the integration branch's runtime flags (map pool,
/// signature landmarks, lobby timing, hitboxes, bot learning; taken from integration/launch-fixes 3f5e68c8 with
/// fix/map-variety's `--generate-maps` replaced by `--map-pool`): each flag family's typo guard (`--insecure*`,
/// `--ws-*`/`--http-*`, `--require*`/`--admit*`/`--control*`, `--runtime*`) must accept every other family's real flags in
/// one command line.
#[test]
fn every_flag_parser_accepts_the_other_branches_flags_together() {
    let token_file =
        std::env::temp_dir().join(format!("rh-int-flags-token-{}", std::process::id()));
    std::fs::write(&token_file, "int-token\n").unwrap();
    let common = [
        "rh-server",
        "--bind",
        "127.0.0.1:7777",
        "--runtime",
        "source-arena",
        "--map",
        "map.json",
        "--map-pool",
        "--signature-landmarks",
        "--lobby-timing",
        "matchmaking",
        "--hitboxes",
        "archetypes",
        "--bot-learning",
        "single-writer",
        "--client-ip-header",
        "x-forwarded-for",
        "--trusted-proxies",
        "127.0.0.1",
        "--api-cors-origins",
        "https://play.example",
        "--ws-origins",
        "https://play.example",
        "--ws-max-per-ip",
        "16",
        "--ws-connect-burst",
        "20",
        "--ws-connect-per-second",
        "2",
        "--ws-max-pending",
        "256",
        "--ws-auth-fails-per-minute",
        "10",
        "--ws-auth-fails-per-minute-all",
        "0",
        "--http-header-timeout-secs",
        "10",
        "--insecure-dev-auth",
        "--db-connect-timeout-secs",
        "60",
        "--drain-max-secs",
        "55",
        "--drain-write-wait-secs",
        "20",
        "--control",
        "127.0.0.1:7778",
    ];
    let token = token_file.to_str().unwrap();
    for tail in [
        vec!["--control-insecure-loopback"],
        vec!["--control-token-file", token],
    ] {
        let a = args(&[&common[..], &tail[..]].concat());
        let bind: SocketAddr = "127.0.0.1:7777".parse().unwrap();
        let exposure = exposure_check(&a, bind).unwrap();
        assert!(
            exposure.public,
            "--client-ip-header makes the listener public"
        );
        hosted_settings(&a).unwrap();
        let d = drain_settings(&a).unwrap();
        assert_eq!(d.max, Some(Duration::from_secs(55)));
        assert_eq!(db_connect_budget(&a).unwrap(), Duration::from_secs(60));
        let api = api_config(&a).unwrap();
        edge_settings(&a, &api, exposure.public).unwrap();
        lease_policy(&a).unwrap();
        let rt = rh_server::runtime::Settings::from_args(&a)
            .unwrap()
            .unwrap();
        assert_eq!(rt.profile, rh_server::runtime::Profile::SourceArena);
        assert!(rt.map_pool && rt.signature_landmarks);
        assert_eq!(
            rh_server::bot_learning::LearningMode::from_args(&a).unwrap(),
            rh_server::bot_learning::LearningMode::SingleWriter
        );
    }
    let _ = std::fs::remove_file(&token_file);
}
