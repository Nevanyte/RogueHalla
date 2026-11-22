//! Structured lifecycle log lines (P-ALPHA-SERVER-1): one `eprintln!` line per join, resume, drop, grace expiry, leave,
//! admission reject and match end, so a tester's report can be traced. Pure formatting; the call sites print.
//! Format: `[server] INFO event=<name> key=value ...`; a value that is empty or holds whitespace, `"` or `=` is quoted
//! with `{:?}` (as `detail={:?}` is elsewhere in the crate). Never called per tick.

use std::net::IpAddr;
use std::time::Duration;

/// Why a connection was lost while its seat lives on (`event=drop reason=...`).
#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub enum DropReason {
    /// `reason=idle_timeout`: nothing received for `conn::IDLE_TIMEOUT`.
    IdleTimeout,
    /// `reason=closed`: the socket closed or errored.
    Closed,
    /// `reason=replaced`: the same seat resumed on a newer connection.
    Replaced,
}

/// The generic formatter: `[server] INFO event=<event>` then each field as ` key=value`, in the order given.
pub fn line(event: &str, fields: &[(&str, &str)]) -> String {
    let mut out = format!("[server] INFO event={event}");
    for (k, v) in fields {
        if v.is_empty() || v.chars().any(|c| c.is_whitespace() || c == '"' || c == '=') {
            out.push_str(&format!(" {k}={v:?}"));
        } else {
            out.push_str(&format!(" {k}={v}"));
        }
    }
    out
}

/// `event=join room=<room> callsign=<callsign> humans=<n>`
pub fn join(room: &str, callsign: &str, humans: usize) -> String {
    line(
        "join",
        &[
            ("room", room),
            ("callsign", callsign),
            ("humans", &humans.to_string()),
        ],
    )
}

/// `event=resume room=<room> callsign=<callsign> humans=<n>`
pub fn resume(room: &str, callsign: &str, humans: usize) -> String {
    line(
        "resume",
        &[
            ("room", room),
            ("callsign", callsign),
            ("humans", &humans.to_string()),
        ],
    )
}

/// `event=drop room=<room> callsign=<callsign> reason=<idle_timeout|closed|replaced>`
pub fn dropped(room: &str, callsign: &str, reason: DropReason) -> String {
    let reason = match reason {
        DropReason::IdleTimeout => "idle_timeout",
        DropReason::Closed => "closed",
        DropReason::Replaced => "replaced",
    };
    line(
        "drop",
        &[("room", room), ("callsign", callsign), ("reason", reason)],
    )
}

/// `event=grace_expired room=<room> callsign=<callsign>`
pub fn grace_expired(room: &str, callsign: &str) -> String {
    line("grace_expired", &[("room", room), ("callsign", callsign)])
}

/// `event=leave room=<room> callsign=<callsign> humans=<n>` (humans left after this one)
pub fn leave(room: &str, callsign: &str, humans: usize) -> String {
    line(
        "leave",
        &[
            ("room", room),
            ("callsign", callsign),
            ("humans", &humans.to_string()),
        ],
    )
}

/// `event=reject code=<RejectCode Debug name> reason=<text> peer=<ip>`
pub fn reject(code: rh_protocol::RejectCode, reason: &str, peer: IpAddr) -> String {
    line(
        "reject",
        &[
            ("code", &format!("{code:?}")),
            ("reason", reason),
            ("peer", &peer.to_string()),
        ],
    )
}

/// `event=match_end room=<room> duration_ms=<ms> humans=<n>`
pub fn match_end(room: &str, duration: Duration, humans: usize) -> String {
    line(
        "match_end",
        &[
            ("room", room),
            ("duration_ms", &duration.as_millis().to_string()),
            ("humans", &humans.to_string()),
        ],
    )
}
