//! `--database-url` startup: connect retry with backoff, secret redaction and the durable store handoff.

#[cfg(feature = "postgres")]
use crate::flags::arg;
#[cfg(any(feature = "postgres", test))]
use crate::flags::flag_value;
use rh_server::persist::{Auth, Persistence};
#[cfg(feature = "postgres")]
use std::sync::Arc;
#[cfg(any(feature = "postgres", test))]
use std::time::Duration;

/// Exit codes the supervisor can act on (besides 2 = bad command line, 78 = schema newer than this binary).
#[cfg(feature = "postgres")]
pub(crate) const EXIT_DB_UNREACHABLE: i32 = 69; // EX_UNAVAILABLE: PostgreSQL did not accept a connection within the connect budget
#[cfg(feature = "postgres")]
pub(crate) const EXIT_DB_MIGRATE: i32 = 70; // EX_SOFTWARE: connected, but the schema migration failed

/// `--db-connect-timeout-secs` (default 60; 0 = one attempt): how long startup keeps retrying an unreachable database.
#[cfg(any(feature = "postgres", test))]
pub(crate) fn db_connect_budget(args: &[String]) -> Result<Duration, String> {
    match flag_value(args, "--db-connect-timeout-secs")? {
        None => Ok(Duration::from_secs(60)),
        Some(v) => v
            .parse::<u64>()
            .ok()
            .filter(|n| *n <= 3600)
            .map(Duration::from_secs)
            .ok_or_else(|| "--db-connect-timeout-secs requires whole seconds, 0..=3600".into()),
    }
}

/// `--db-pool N` (default 16; 1..=1024): the durable store's connection pool size.
#[cfg(any(feature = "postgres", test))]
pub(crate) fn db_pool_size(_args: &[String]) -> Result<u32, String> {
    // stub: P-DB-POOL-1
    Ok(16)
}

/// Retries `op` with exponential backoff (`first_delay`, doubling, capped at `max_delay`) until it succeeds, `fatal` says the
/// error cannot be cured by waiting, or `budget` is spent (one attempt when it is zero). The last error is returned.
#[cfg(any(feature = "postgres", test))]
pub(crate) async fn retry_with_backoff<T, E, Fut>(
    budget: Duration,
    first_delay: Duration,
    max_delay: Duration,
    fatal: impl Fn(&E) -> bool,
    show: impl Fn(&E) -> String,
    mut op: impl FnMut(u32) -> Fut,
) -> Result<T, E>
where
    Fut: std::future::Future<Output = Result<T, E>>,
    E: std::fmt::Display,
{
    let started = tokio::time::Instant::now();
    let mut delay = first_delay;
    let mut attempt = 0u32;
    loop {
        attempt += 1;
        match op(attempt).await {
            Ok(v) => return Ok(v),
            Err(e) => {
                let left = budget.saturating_sub(started.elapsed());
                if fatal(&e) || left.is_zero() {
                    return Err(e);
                }
                let wait = delay.min(left);
                eprintln!(
                    "[server] database not reachable (attempt {attempt}): {}; retrying in {}ms ({}s of budget left)",
                    show(&e),
                    wait.as_millis(),
                    left.as_secs()
                );
                tokio::time::sleep(wait).await;
                delay = (delay * 2).min(max_delay);
            }
        }
    }
}

/// Removes the connection string, and its password and user, from text that is about to be logged. sqlx messages normally
/// carry none of them, but the log must never depend on that.
#[cfg(any(feature = "postgres", test))]
pub(crate) fn redact_secrets(text: &str, url: &[&str]) -> String {
    let mut out = text.to_string();
    for u in url {
        if u.is_empty() {
            continue;
        }
        out = out.replace(u, "<database-url>");
        if let Some((_, rest)) = u.split_once("://") {
            if let Some((cred, _)) = rest.split_once('@') {
                let mut parts = cred.splitn(2, ':');
                let user = parts.next().unwrap_or("");
                if let Some(pw) = parts.next().filter(|p| !p.is_empty()) {
                    out = out.replace(pw, "<redacted>");
                }
                if user.len() > 2 {
                    out = out.replace(user, "<user>");
                }
            }
        }
    }
    out
}

/// One failed connection attempt: the store's own error, or our per-attempt timeout (which is transient, never fatal).
#[cfg(feature = "postgres")]
pub(crate) enum ConnectFail {
    Store(rh_persist::Error),
    Timeout(u64),
}

#[cfg(feature = "postgres")]
impl std::fmt::Display for ConnectFail {
    fn fmt(&self, f: &mut std::fmt::Formatter<'_>) -> std::fmt::Result {
        match self {
            ConnectFail::Store(e) => write!(f, "{e}"),
            ConnectFail::Timeout(s) => write!(f, "no answer within {s}s"),
        }
    }
}

#[cfg(feature = "postgres")]
impl ConnectFail {
    /// Waiting cannot cure it: a schema too new for this binary, or a configuration/credential/database-name error.
    fn fatal(&self) -> bool {
        matches!(self, ConnectFail::Store(e)
            if matches!(e, rh_persist::Error::SchemaTooNew { .. }) || e.is_startup_fatal())
    }
}

/// `--database-url`: profiles, credentials, session leases and results in PostgreSQL (feature `postgres`).
#[cfg(feature = "postgres")]
pub(crate) async fn durable(url: &str, args: &[String]) -> (Persistence, Auth) {
    let budget = db_connect_budget(args).unwrap_or_else(|e| {
        eprintln!("[server] invalid database settings: {e}");
        std::process::exit(2);
    });
    // The startup schema gate: a database whose `schema_compat.min_binary` is above this binary (an older server meeting a
    // newer, incompatible schema during a rollout) is refused here, with a plain message and a non-zero exit, before the
    // server takes a single connection. An unreachable database is retried with backoff for the connect budget, then the
    // process exits with `EXIT_DB_UNREACHABLE` and a line that carries no connection string.
    // sqlx's own pool keeps retrying a refused connection for 30 s inside one `connect`; each attempt is cut at a third of the
    // budget (1..=10 s) so the retries and the budget are real. With a zero budget (one attempt) it gets a sane 10 s.
    let per_attempt = if budget.is_zero() {
        Duration::from_secs(10)
    } else {
        (budget / 3).clamp(Duration::from_secs(1), Duration::from_secs(10))
    };
    let store = match retry_with_backoff(
        budget,
        Duration::from_millis(500),
        Duration::from_secs(8),
        ConnectFail::fatal,
        |e: &ConnectFail| redact_secrets(&e.to_string(), &[url]),
        |_| async move {
            match tokio::time::timeout(per_attempt, rh_persist::Store::connect(url)).await {
                Ok(r) => r.map_err(ConnectFail::Store),
                Err(_) => Err(ConnectFail::Timeout(per_attempt.as_secs())),
            }
        },
    )
    .await
    {
        Ok(s) => Arc::new(s),
        Err(ConnectFail::Store(e @ rh_persist::Error::SchemaTooNew { .. })) => {
            eprintln!("rh-server: {e}");
            std::process::exit(78);
        }
        Err(e) => {
            // A fatal error (bad URL, rejected credentials, missing database) was not retried; the same exit code tells the
            // supervisor the database is not usable, the event name says why.
            let event = if e.fatal() {
                "database_connect_refused_permanently"
            } else {
                "database_unreachable"
            };
            eprintln!(
                "[server] ERROR event={event} budget_secs={} detail={:?} action=exit code={EXIT_DB_UNREACHABLE}",
                budget.as_secs(),
                redact_secrets(&e.to_string(), &[url])
            );
            std::process::exit(EXIT_DB_UNREACHABLE);
        }
    };
    if let Err(e) = store.migrate().await {
        eprintln!(
            "[server] ERROR event=database_migrate_failed detail={:?} action=exit code={EXIT_DB_MIGRATE}",
            redact_secrets(&e.to_string(), &[url])
        );
        std::process::exit(EXIT_DB_MIGRATE);
    }
    let owner =
        arg(args, "--instance-id").unwrap_or_else(|| format!("rh-server-{}", std::process::id()));
    (
        Persistence::postgres(store.clone(), owner),
        Auth::Postgres(store),
    )
}

#[cfg(not(feature = "postgres"))]
pub(crate) async fn durable(_: &str, _: &[String]) -> (Persistence, Auth) {
    panic!("--database-url needs a build with `--features postgres`");
}
