//! P-TICK-5: the server's step setting (`--tick-ms 50|25`, else `RH_TICK_MS`, else 50); anything else stops the start.

use crate::flags::{tick_ms_setting, TICK_MS_ENV};

fn args(values: &[&str]) -> Vec<String> {
    std::iter::once("rh-server")
        .chain(values.iter().copied())
        .map(str::to_string)
        .collect()
}

/// An environment holding `RH_TICK_MS = value` when given, and nothing else.
fn env(value: Option<&'static str>) -> impl Fn(&str) -> Option<String> {
    move |key: &str| {
        if key == TICK_MS_ENV {
            value.map(str::to_string)
        } else {
            None
        }
    }
}

#[test]
fn the_tick_ms_setting_takes_50_or_25_from_the_flag_or_the_environment() {
    assert_eq!(TICK_MS_ENV, "RH_TICK_MS");
    // Default: the legacy step.
    assert_eq!(tick_ms_setting(&args(&[]), env(None)), Ok(50));
    assert_eq!(
        tick_ms_setting(&args(&["--fill-bots", "8"]), env(None)),
        Ok(50)
    );
    // The flag.
    assert_eq!(
        tick_ms_setting(&args(&["--tick-ms", "25"]), env(None)),
        Ok(25)
    );
    assert_eq!(
        tick_ms_setting(&args(&["--tick-ms", "50"]), env(None)),
        Ok(50)
    );
    // The environment.
    assert_eq!(tick_ms_setting(&args(&[]), env(Some("25"))), Ok(25));
    assert_eq!(tick_ms_setting(&args(&[]), env(Some("50"))), Ok(50));
    // The flag wins over the environment.
    assert_eq!(
        tick_ms_setting(&args(&["--tick-ms", "50"]), env(Some("25"))),
        Ok(50)
    );
    assert_eq!(
        tick_ms_setting(&args(&["--tick-ms", "25"]), env(Some("50"))),
        Ok(25)
    );
    // Anything but 50 or 25 is refused at start.
    for bad in [
        vec!["--tick-ms", "30"],
        vec!["--tick-ms", "0"],
        vec!["--tick-ms", "20"],
        vec!["--tick-ms", "40"],
        vec!["--tick-ms", "100"],
        vec!["--tick-ms", "abc"],
        vec!["--tick-ms", "25.0"],
        vec!["--tick-ms", "-25"],
        vec!["--tick-ms"],
        vec!["--tick-ms", "--fill-bots", "8"],
    ] {
        assert!(
            tick_ms_setting(&args(&bad), env(None)).is_err(),
            "{bad:?} is refused"
        );
    }
    for bad in ["30", "0", "40", "abc", ""] {
        assert!(
            tick_ms_setting(&args(&[]), env(Some(bad))).is_err(),
            "RH_TICK_MS={bad:?} is refused"
        );
    }
}
