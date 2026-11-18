//! Settings parsers for the spool, hosted mode, drain, leases, the account API and the public edge.

use crate::flags::{arg, flag_value};
use rh_server::api::{ApiConfig, CorsPolicy};
use rh_server::director::LeasePolicy;
use rh_server::edge::{EdgeConfig, HttpLimits, OriginPolicy};
use std::net::SocketAddr;
use std::time::Duration;

/// `--result-spool <dir>` with `--result-spool-max` (pending records, default 4096), `--result-dead-max` (dead letters kept,
/// default 1024; at the bound a refused record stays pending and is counted and logged) and
/// `--result-retry-secs` (ALERT threshold, default 900: a result still uncommitted after this long is logged and counted as
/// stuck, repeatedly; it is never dead-lettered for time, a transient error retries forever).
#[cfg(feature = "postgres")]
pub(crate) fn spool_settings(
    args: &[String],
) -> Result<Option<(std::path::PathBuf, rh_server::spool::SpoolConfig)>, String> {
    let Some(dir) = arg(args, "--result-spool") else {
        if args.iter().any(|a| a == "--result-spool") {
            return Err("--result-spool requires a directory".into());
        }
        for k in [
            "--result-spool-max",
            "--result-dead-max",
            "--result-retry-secs",
        ] {
            if args.iter().any(|a| a == k) {
                return Err(format!("{k} needs --result-spool"));
            }
        }
        return Ok(None);
    };
    if dir.starts_with("--") || dir.is_empty() {
        return Err("--result-spool requires a directory".into());
    }
    let whole = |name: &str| -> Result<Option<u64>, String> {
        match args.iter().position(|a| a == name) {
            None => Ok(None),
            Some(i) => args
                .get(i + 1)
                .and_then(|s| s.parse::<u64>().ok())
                .filter(|n| *n > 0)
                .map(Some)
                .ok_or_else(|| format!("{name} requires a positive whole number")),
        }
    };
    let mut cfg = rh_server::spool::SpoolConfig::default();
    if let Some(n) = whole("--result-spool-max")? {
        cfg.max_records = usize::try_from(n).map_err(|_| "--result-spool-max too large")?;
    }
    if let Some(n) = whole("--result-dead-max")? {
        cfg.max_dead = usize::try_from(n).map_err(|_| "--result-dead-max too large")?;
    }
    if let Some(n) = whole("--result-retry-secs")? {
        cfg.retry_deadline = Duration::from_secs(n);
    }
    Ok(Some((dir.into(), cfg)))
}

/// Hosted-mode settings: `--require-admit`, `--admit-ttl-secs`, `--control-token-file`. Unknown flags in these families are
/// refused (a typo must not silently turn a hosted server into an open one).
pub(crate) struct HostedSettings {
    pub(crate) require_admit: bool,
    pub(crate) admit_ttl: Option<Duration>,
    pub(crate) token: Option<String>,
}

pub(crate) fn hosted_settings(args: &[String]) -> Result<HostedSettings, String> {
    const KNOWN: [&str; 5] = [
        "--require-admit",
        "--admit-ttl-secs",
        "--control-token-file",
        "--control-insecure-loopback",
        "--control",
    ];
    for a in args.iter().skip(1) {
        let family =
            a.starts_with("--require") || a.starts_with("--admit") || a.starts_with("--control");
        if family && !KNOWN.contains(&a.as_str()) {
            return Err(format!("unknown flag {a} (did you mean one of {KNOWN:?}?)"));
        }
    }
    let admit_ttl = match args.iter().position(|a| a == "--admit-ttl-secs") {
        None => None,
        Some(i) => Some(
            args.get(i + 1)
                .and_then(|s| s.parse::<u64>().ok())
                .filter(|n| (1..=300).contains(n))
                .map(Duration::from_secs)
                .ok_or("--admit-ttl-secs requires a whole number of seconds, 1..=300")?,
        ),
    };
    let token = match args.iter().position(|a| a == "--control-token-file") {
        None => None,
        Some(i) => {
            let path = args
                .get(i + 1)
                .filter(|p| !p.starts_with("--"))
                .ok_or("--control-token-file requires a file")?;
            let text = std::fs::read_to_string(path)
                .map_err(|e| format!("cannot read --control-token-file {path}: {e}"))?;
            let t = text.trim().to_string();
            if t.is_empty() || t.len() > 4096 || t.chars().any(char::is_control) {
                return Err(
                    "--control-token-file must hold one non-empty token (at most 4096 bytes)"
                        .into(),
                );
            }
            Some(t)
        }
    };
    let require_admit = args.iter().any(|a| a == "--require-admit");
    if (require_admit || token.is_some()) && !args.iter().any(|a| a == "--control") {
        return Err(
            "--require-admit and --control-token-file need --control (the control listener)".into(),
        );
    }
    // The control listener can create and end rooms, kick players and drain the process. Loopback is not an authentication
    // boundary on a shared host (any local process, or an SSRF in a sibling service, reaches it), so a token is required
    // unless the operator says, explicitly, that loopback alone is acceptable (local development, load tests).
    let insecure = args.iter().any(|a| a == "--control-insecure-loopback");
    if args.iter().any(|a| a == "--control") && token.is_none() && !insecure {
        return Err(
            "--control needs --control-token-file <file> (control requests then carry `Authorization: Bearer <token>`); \
             pass --control-insecure-loopback to run the control API unauthenticated on loopback (development only)"
                .into(),
        );
    }
    if insecure && !args.iter().any(|a| a == "--control") {
        return Err("--control-insecure-loopback needs --control".into());
    }
    Ok(HostedSettings {
        require_admit,
        admit_ttl,
        token,
    })
}

/// Drain bounds. The hosting stack stops the process with systemd's default `TimeoutStopSec` (90 s: `render.py` and
/// `bootstrap.py` set none for `rh-server`), then SIGKILL. From the signal, the process exit is bounded by ONE budget:
///
///   `--drain-max-secs` (everything up to the last room closing, including the forced end) +
///   `--drain-write-wait-secs` (result writes in flight) + `HTTP_SHUTDOWN_GRACE` (5 s) <= `TimeoutStopSec`
///
/// Defaults: 55 + 20 + 5 = 80 s, 10 s of margin under 90 s. A longer grace (container platforms often give 120 s+) can raise
/// both flags; keep the sum within it.
pub(crate) const DEFAULT_DRAIN_MAX: Duration = Duration::from_secs(55);
pub(crate) const DEFAULT_DRAIN_WRITE_WAIT: Duration = Duration::from_secs(20);
pub(crate) const HTTP_SHUTDOWN_GRACE: Duration = Duration::from_secs(5);

pub(crate) struct DrainSettings {
    /// After this long the remaining rooms are ended (results kept); `None` waits for the matches without a cap.
    pub(crate) max: Option<Duration>,
    /// How long the exit waits for result writes still in flight.
    pub(crate) write_wait: Duration,
}

pub(crate) fn drain_settings(args: &[String]) -> Result<DrainSettings, String> {
    let secs = |name: &str, default: Duration, zero_ok: bool| -> Result<Duration, String> {
        match flag_value(args, name)? {
            None => Ok(default),
            Some(v) => v
                .parse::<u64>()
                .ok()
                .filter(|n| (*n > 0 || zero_ok) && *n <= 86_400)
                .map(Duration::from_secs)
                .ok_or_else(|| {
                    format!(
                        "{name} requires whole seconds, {}..=86400",
                        u8::from(!zero_ok)
                    )
                }),
        }
    };
    let max = secs("--drain-max-secs", DEFAULT_DRAIN_MAX, true)?;
    Ok(DrainSettings {
        max: (!max.is_zero()).then_some(max),
        write_wait: secs("--drain-write-wait-secs", DEFAULT_DRAIN_WRITE_WAIT, false)?,
    })
}

pub(crate) fn lease_policy(args: &[String]) -> Result<LeasePolicy, String> {
    let defaults = LeasePolicy::default();
    let seconds = |name: &str, default: Duration| -> Result<Duration, String> {
        match args.iter().position(|a| a == name) {
            None => Ok(default),
            Some(i) => args
                .get(i + 1)
                .and_then(|s| s.parse::<u64>().ok())
                .map(Duration::from_secs)
                .ok_or_else(|| format!("{name} requires a whole number of seconds")),
        }
    };
    let policy = LeasePolicy {
        ttl: seconds("--lease-ttl-secs", defaults.ttl)?,
        renew_interval: seconds("--lease-renew-secs", defaults.renew_interval)?,
        db_timeout: seconds("--lease-db-timeout-secs", defaults.db_timeout)?,
    };
    policy.validate().map_err(str::to_owned)?;
    Ok(policy)
}

/// Account API settings. The passcode comes from `PLAYTEST_PASSCODE` (the legacy name), never from argv, so it cannot show in a
/// process list. A limit of 0 turns that guard off (an edge limiter then owns it).
pub(crate) fn api_config(args: &[String]) -> Result<ApiConfig, String> {
    let mut cfg = ApiConfig {
        passcode: std::env::var("PLAYTEST_PASSCODE")
            .ok()
            .filter(|p| !p.is_empty()),
        ..ApiConfig::default()
    };
    let int = |name: &str, default: u32| -> Result<u32, String> {
        match arg(args, name) {
            None => Ok(default),
            Some(v) => v
                .parse()
                .map_err(|_| format!("{name} requires a non-negative whole number")),
        }
    };
    cfg.new_profiles_per_hour = int("--new-profiles-per-hour", cfg.new_profiles_per_hour)?;
    cfg.new_profiles_per_ip_per_hour = int(
        "--new-profiles-per-ip-per-hour",
        cfg.new_profiles_per_ip_per_hour,
    )?;
    cfg.passcode_fails_per_minute =
        int("--passcode-fails-per-minute", cfg.passcode_fails_per_minute)?;
    cfg.passcode_fails_per_minute_all = int(
        "--passcode-fails-per-minute-all",
        cfg.passcode_fails_per_minute_all,
    )?;
    cfg.shop_burst = f64::from(int("--shop-burst", cfg.shop_burst as u32)?);
    cfg.shop_per_second = f64::from(int("--shop-per-second", cfg.shop_per_second as u32)?);
    cfg.lookup_burst = f64::from(int("--lookup-burst", cfg.lookup_burst as u32)?);
    cfg.lookup_per_second = f64::from(int("--lookup-per-second", cfg.lookup_per_second as u32)?);
    cfg.db_concurrency = int("--api-db-concurrency", cfg.db_concurrency as u32)? as usize;
    cfg.request_timeout = Duration::from_secs(u64::from(int("--api-timeout-secs", 10)?.max(1)));
    cfg.leaderboard_ttl = Duration::from_secs(u64::from(int("--leaderboard-ttl-secs", 5)?));
    if let Some(list) = arg(args, "--api-cors-origins") {
        cfg.cors = if list.trim() == "*" {
            CorsPolicy::Any
        } else {
            CorsPolicy::Origins(
                list.split(',')
                    .map(|o| o.trim().trim_end_matches('/').to_ascii_lowercase())
                    .filter(|o| !o.is_empty())
                    .collect(),
            )
        };
    }
    if let Some(name) = arg(args, "--client-ip-header") {
        cfg.client_ip_header = Some(
            name.parse()
                .map_err(|_| format!("--client-ip-header: {name:?} is not a header name"))?,
        );
    }
    if let Some(list) = arg(args, "--trusted-proxies") {
        for p in list.split(',').filter(|p| !p.trim().is_empty()) {
            cfg.trusted_proxies.push(
                p.trim()
                    .parse()
                    .map_err(|_| format!("--trusted-proxies: {p:?} is not an IP address"))?,
            );
        }
    }
    Ok(cfg)
}

/// Whether this process serves public traffic: a non-loopback bind, or a forwarded-address header (a reverse proxy in front
/// of a loopback bind forwards public clients).
pub(crate) fn publicly_exposed(args: &[String], bind: SocketAddr) -> bool {
    !bind.ip().is_loopback() || args.iter().any(|a| a == "--client-ip-header")
}

/// What the startup checks of a publicly exposed process decided; `warnings` are printed before serving.
#[derive(Debug, Default)]
pub(crate) struct Exposure {
    pub(crate) public: bool,
    pub(crate) warnings: Vec<String>,
}

/// Refuses settings that are unsafe on a public listener unless explicitly accepted: dev auth (`--insecure-dev-auth`) and the
/// generated self-signed certificate (`--insecure-self-signed`). Also refuses ambiguous TLS flags and misspelt `--insecure-*`
/// flags (a typo must not silently keep an insecure default).
pub(crate) fn exposure_check(args: &[String], bind: SocketAddr) -> Result<Exposure, String> {
    const KNOWN: [&str; 2] = ["--insecure-dev-auth", "--insecure-self-signed"];
    for a in args.iter().skip(1) {
        if a.starts_with("--insecure") && !KNOWN.contains(&a.as_str()) {
            return Err(format!("unknown flag {a} (did you mean one of {KNOWN:?}?)"));
        }
    }
    let flag = |k: &str| args.iter().any(|a| a == k);
    let public = publicly_exposed(args, bind);
    let mut warnings = vec![];
    let dev_auth = arg(args, "--database-url").is_none()
        && matches!(arg(args, "--auth").as_deref(), None | Some("dev"));
    if dev_auth && public {
        if !flag("--insecure-dev-auth") {
            return Err(format!(
                "refusing to start: dev auth (any callsign is accepted with any ticket) on a publicly exposed listener \
                 ({bind}{}); pass --database-url or --auth static:<file>, or --insecure-dev-auth to accept this deliberately",
                if bind.ip().is_loopback() { ", behind --client-ip-header" } else { "" }
            ));
        }
        warnings.push(format!(
            "INSECURE: --insecure-dev-auth: {bind} accepts ANY callsign with ANY ticket; never use this for real players"
        ));
    }
    let self_signed = flag("--self-signed");
    let (cert, key) = (arg(args, "--tls-cert"), arg(args, "--tls-key"));
    if cert.is_some() != key.is_some()
        || (cert.is_none() && (flag("--tls-cert") || flag("--tls-key")))
    {
        return Err("--tls-cert and --tls-key must be given together, each with a file".into());
    }
    if self_signed && cert.is_some() {
        return Err("--self-signed and --tls-cert/--tls-key are exclusive".into());
    }
    if self_signed && public {
        if !flag("--insecure-self-signed") {
            return Err(format!(
                "refusing to start: --self-signed on a publicly exposed listener ({bind}) serves a throwaway certificate \
                 for localhost that no browser trusts; pass --tls-cert/--tls-key, or --insecure-self-signed to accept this"
            ));
        }
        warnings.push(format!(
            "INSECURE: --insecure-self-signed: {bind} serves a generated localhost certificate; clients cannot verify it"
        ));
    }
    if (flag("--insecure-dev-auth") && !(dev_auth && public))
        || (flag("--insecure-self-signed") && !(self_signed && public))
    {
        warnings
            .push("an --insecure-* flag was given but has no effect with these settings".into());
    }
    Ok(Exposure { public, warnings })
}

/// The public edge settings (`--ws-*`, `--http-header-timeout-secs`). The client-address rules come from the account API's
/// `--client-ip-header`/`--trusted-proxies` (one parse, one trust rule). `--ws-origins` defaults to `--api-cors-origins`; an
/// empty list means any origin when not publicly exposed and no browser origin otherwise.
pub(crate) fn edge_settings(
    args: &[String],
    api: &ApiConfig,
    public: bool,
) -> Result<(EdgeConfig, HttpLimits, Vec<String>), String> {
    const KNOWN: [&str; 7] = [
        "--ws-origins",
        "--ws-max-per-ip",
        "--ws-connect-burst",
        "--ws-connect-per-second",
        "--ws-max-pending",
        "--ws-auth-fails-per-minute",
        "--ws-auth-fails-per-minute-all",
    ];
    for a in args.iter().skip(1) {
        if (a.starts_with("--ws-") && !KNOWN.contains(&a.as_str()))
            || (a.starts_with("--http-") && a != "--http-header-timeout-secs")
        {
            return Err(format!("unknown flag {a}"));
        }
    }
    let int = |name: &str, default: u32| -> Result<u32, String> {
        match args.iter().position(|a| a == name) {
            None => Ok(default),
            Some(i) => args
                .get(i + 1)
                .and_then(|v| v.parse().ok())
                .ok_or_else(|| format!("{name} requires a non-negative whole number")),
        }
    };
    let d = EdgeConfig::default();
    let mut warnings = vec![];
    let listed = match arg(args, "--ws-origins") {
        Some(list) if !list.starts_with("--") => OriginPolicy::parse(&list),
        Some(_) => return Err("--ws-origins requires '*' or a comma list of origins".into()),
        None if args.iter().any(|a| a == "--ws-origins") => {
            return Err("--ws-origins requires '*' or a comma list of origins".into())
        }
        None => match &api.cors {
            CorsPolicy::Any => OriginPolicy::Any,
            CorsPolicy::Origins(list) => OriginPolicy::Only(list.clone()),
        },
    };
    let origins = match listed {
        OriginPolicy::Only(list) if list.is_empty() && !public => OriginPolicy::Any,
        OriginPolicy::Only(list) if list.is_empty() => {
            warnings.push(
                "no --ws-origins/--api-cors-origins on a publicly exposed listener: browsers cannot open /ws \
                 (non-browser clients still can)"
                    .into(),
            );
            OriginPolicy::Only(list)
        }
        OriginPolicy::Any if public => {
            warnings
                .push("/ws accepts any browser origin on a publicly exposed listener ('*')".into());
            OriginPolicy::Any
        }
        other => other,
    };
    let cfg = EdgeConfig {
        origins,
        client_ip_header: api.client_ip_header.clone(),
        trusted_proxies: api.trusted_proxies.clone(),
        max_per_ip: int("--ws-max-per-ip", d.max_per_ip as u32)? as usize,
        connect_burst: f64::from(int("--ws-connect-burst", d.connect_burst as u32)?),
        connect_per_second: f64::from(int("--ws-connect-per-second", d.connect_per_second as u32)?),
        max_pending: int("--ws-max-pending", d.max_pending as u32)? as usize,
        auth_fails_per_minute: int("--ws-auth-fails-per-minute", d.auth_fails_per_minute)?,
        auth_fails_per_minute_all: int(
            "--ws-auth-fails-per-minute-all",
            d.auth_fails_per_minute_all,
        )?,
    };
    let default_secs = HttpLimits::default().header_read_timeout.as_secs() as u32;
    let secs = int("--http-header-timeout-secs", default_secs)?;
    if !(1..=300).contains(&secs) {
        return Err("--http-header-timeout-secs must be 1..=300".into());
    }
    let limits = HttpLimits {
        header_read_timeout: Duration::from_secs(u64::from(secs)),
    };
    Ok((cfg, limits, warnings))
}
