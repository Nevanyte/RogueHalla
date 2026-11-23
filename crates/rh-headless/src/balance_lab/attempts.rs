//! P-BAL-LAB-1-fix: the quest funnel of one seat as a pure function of its per-step `Sim::quest_attempts` reads. The
//! observer builds one [`StepRead`] per step for every seat and calls [`tally_seat`] at the end of the match; tests call
//! it with hand-written rows. Rules: docs/backlog/packets/P-BAL-LAB-1-fix.md.
use std::collections::BTreeMap;

/// One row of `Sim::quest_attempts`, with the quest as its def id (`QuestId::definition().id`).
#[derive(Clone, Debug, PartialEq)]
pub struct ProbeRow {
    pub offer: u32,
    pub quest: String,
    pub accepted: bool,
    pub ready: bool,
    pub turned_in: bool,
    pub completed: bool,
    pub deadline: f64,
    pub progress: u8,
}

/// One seat after one step.
#[derive(Clone, Debug, PartialEq)]
pub struct StepRead {
    pub tick: u32,
    /// `Sim::server_time()` in ms.
    pub now_ms: f64,
    /// `Sim::gates_locked()`.
    pub locked: bool,
    /// The seat died in this step (an `Event::Death` for it).
    pub died: bool,
    pub rows: Vec<ProbeRow>,
}

/// What one seat's attempts of one quest came to.
#[derive(Clone, Debug, Default, PartialEq)]
pub struct AttemptTally {
    pub attempts: u64,
    /// Distinct offers accepted at least once.
    pub accepted: u64,
    /// Tick of the first attempt of each accepted offer, in order.
    pub first_accept_ticks: Vec<u32>,
    pub completed: u64,
    pub ended_timer: u64,
    pub ended_death: u64,
    /// Ended otherwise: abandon, departure or anything the state cannot tell apart.
    pub unknown: u64,
    /// Still accepted and not turned in at the end of the probes.
    pub open: u64,
    /// Turned in at the end and never paid.
    pub turned_in_unpaid: u64,
    /// Seconds from the step the row first appeared to the step it was first `turned_in || completed`, one per completion.
    pub complete_secs: Vec<f64>,
}

/// Stub: returns no tallies.
pub fn tally_seat(steps: &[StepRead]) -> BTreeMap<String, AttemptTally> {
    let _ = steps;
    BTreeMap::new()
}
