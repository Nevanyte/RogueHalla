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
pub fn line(_event: &str, _fields: &[(&str, &str)]) -> String {
    String::new()
}

/// `event=join room=<room> callsign=<callsign> humans=<n>`
pub fn join(_room: &str, _callsign: &str, _humans: usize) -> String {
    String::new()
}

/// `event=resume room=<room> callsign=<callsign> humans=<n>`
pub fn resume(_room: &str, _callsign: &str, _humans: usize) -> String {
    String::new()
}

/// `event=drop room=<room> callsign=<callsign> reason=<idle_timeout|closed|replaced>`
pub fn dropped(_room: &str, _callsign: &str, _reason: DropReason) -> String {
    String::new()
}

/// `event=grace_expired room=<room> callsign=<callsign>`
pub fn grace_expired(_room: &str, _callsign: &str) -> String {
    String::new()
}

/// `event=leave room=<room> callsign=<callsign> humans=<n>` (humans left after this one)
pub fn leave(_room: &str, _callsign: &str, _humans: usize) -> String {
    String::new()
}

/// `event=reject code=<RejectCode Debug name> reason=<text> peer=<ip>`
pub fn reject(_code: rh_protocol::RejectCode, _reason: &str, _peer: IpAddr) -> String {
    String::new()
}

/// `event=match_end room=<room> duration_ms=<ms> humans=<n>`
pub fn match_end(_room: &str, _duration: Duration, _humans: usize) -> String {
    String::new()
}
