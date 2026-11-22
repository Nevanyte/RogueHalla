//! `--first-playable`: the bounded opt-in service profile and its embedded map.

use std::time::Duration;

/// Bounded opt-in service profile, not new simulation rules. Default startup is unchanged.
pub(crate) const FIRST_PLAYABLE_LIFETIME: Duration = Duration::from_secs(4 * 60 * 60);
pub(crate) const FIRST_PLAYABLE_GRACE: Duration = Duration::from_secs(180);
pub(crate) struct FirstPlayable {
    pub(crate) min_players: usize,
    pub(crate) fill_bots: usize,
    pub(crate) lobby_seconds: f64,
    pub(crate) fill_seconds: f64,
}
pub(crate) fn first_playable_settings(args: &[String]) -> Result<Option<FirstPlayable>, String> {
    let count = args
        .iter()
        .filter(|a| a.as_str() == "--first-playable")
        .count();
    if args
        .iter()
        .any(|a| a.starts_with("--first-play") && a != "--first-playable")
    {
        return Err("unknown --first-playable option; use the flag without a value".into());
    }
    if count == 0 {
        return Ok(None);
    }
    if count != 1 {
        return Err("--first-playable must occur once".into());
    }
    let index = args.iter().position(|a| a == "--first-playable").unwrap();
    if args.get(index + 1).is_some_and(|a| !a.starts_with("--")) {
        return Err("--first-playable takes no value".into());
    }
    for flag in [
        "--min-players",
        "--fill-bots",
        "--lobby-seconds",
        "--fill-seconds",
        "--lobby-timing",
        "--bot-wait-seconds",
    ] {
        if args.iter().any(|a| a.starts_with(&format!("{flag}="))) {
            return Err(format!("use {flag} with a separate value"));
        }
    }
    if args.iter().any(|a| a == "--map" || a.starts_with("--map=")) {
        return Err("--first-playable uses the embedded map; --map cannot override it".into());
    }
    let whole = |flag: &str, default: usize, minimum: usize| -> Result<usize, String> {
        let mut found = args.iter().enumerate().filter(|(_, a)| a.as_str() == flag);
        let value = match found.next() {
            None => return Ok(default),
            Some((i, _)) => args
                .get(i + 1)
                .and_then(|v| v.parse::<usize>().ok())
                .ok_or_else(|| format!("{flag} requires a whole seat count"))?,
        };
        if found.next().is_some() || !(minimum..=rh_sim::constants::MAX_PLAYERS).contains(&value) {
            return Err(format!("{flag} must occur once with {minimum}..=8 seats"));
        }
        Ok(value)
    };
    let seconds = |flag: &str, default: f64| -> Result<f64, String> {
        let mut found = args.iter().enumerate().filter(|(_, a)| a.as_str() == flag);
        let value = match found.next() {
            None => return Ok(default),
            Some((i, _)) => args
                .get(i + 1)
                .and_then(|v| v.parse::<f64>().ok())
                .ok_or_else(|| format!("{flag} requires seconds"))?,
        };
        if found.next().is_some()
            || !value.is_finite()
            || value < 0.
            || !(value * 1000.).is_finite()
        {
            return Err(format!("{flag} requires finite nonnegative seconds"));
        }
        Ok(value)
    };
    Ok(Some(FirstPlayable {
        min_players: whole("--min-players", 2, 1)?,
        fill_bots: whole("--fill-bots", 0, 0)?,
        lobby_seconds: seconds("--lobby-seconds", 30.)?,
        fill_seconds: seconds("--fill-seconds", 5.)?,
    }))
}
pub(crate) fn first_playable_arena() -> Result<rh_sim::Arena, String> {
    if rh_sim::constants::MAX_PLAYERS != 8 {
        return Err(
            "first-playable capacity requires the existing eight-seat admission cap".into(),
        );
    }
    let map: rh_sim::mapdata::MapExport =
        serde_json::from_slice(include_bytes!("../../../../maps/first-playable.json"))
            .map_err(|e| e.to_string())?;
    if map.legacy_map_hash_u32 != 0x0252_d18b
        || rh_sim::mapgen::map_hash::map_hash(&map.map) != "0252d18b"
    {
        return Err(
            "embedded first-playable map identity changed; review map capacity before enabling"
                .into(),
        );
    }
    map.validate()
        .map_err(|e| e.to_string())?
        .into_arena()
        .map_err(|e| e.to_string())
}
pub(crate) async fn service_deadline(deadline: Option<tokio::time::Instant>) {
    match deadline {
        Some(at) => tokio::time::sleep_until(at).await,
        None => std::future::pending::<()>().await,
    }
}
