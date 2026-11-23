//! P-BAL-LAB-1-fix: the quest funnel of one seat as a pure function of its per-step `Sim::quest_attempts` reads. The
//! observer builds one [`StepRead`] per step for every seat and calls [`tally_seat`] at the end of the match; tests call
//! it with hand-written rows. Rules: docs/backlog/packets/P-BAL-LAB-1-fix.md.
use std::collections::{BTreeMap, BTreeSet};

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

/// A row that vanished is a timer end only if its deadline passed no more than this long before the step that shows it
/// gone (two 50 ms ticks: the sim drops an expired attempt in the pass of the tick the deadline falls in). A row gone
/// long after its deadline was ended by something else.
const TIMER_SLACK_MS: f64 = 100.;

/// The attempt of one offer being followed.
struct Live {
    first_ms: f64,
    /// When the row was first `turned_in || completed`.
    done_ms: Option<f64>,
    last: ProbeRow,
    /// Paid: the row stays for the rest of the match and is no longer an attempt in flight.
    paid: bool,
}

/// Incremental form of [`tally_seat`]: feed it one [`StepRead`] per step, in order.
#[derive(Default)]
pub struct SeatTally {
    live: BTreeMap<(String, u32), Live>,
    seen_offers: BTreeSet<(String, u32)>,
    out: BTreeMap<String, AttemptTally>,
    prev_died: bool,
}

impl SeatTally {
    pub fn push(&mut self, s: &StepRead) {
        for row in &s.rows {
            let key = (row.quest.clone(), row.offer);
            let t = self.out.entry(row.quest.clone()).or_default();
            let live = self.live.entry(key.clone()).or_insert_with(|| {
                t.attempts += 1;
                if self.seen_offers.insert(key.clone()) {
                    t.accepted += 1;
                    t.first_accept_ticks.push(s.tick);
                }
                Live {
                    first_ms: s.now_ms,
                    done_ms: None,
                    last: row.clone(),
                    paid: false,
                }
            });
            if (row.turned_in || row.completed) && live.done_ms.is_none() {
                live.done_ms = Some(s.now_ms);
            }
            if row.completed && !live.paid {
                live.paid = true;
                t.completed += 1;
                let done = live.done_ms.unwrap_or(s.now_ms);
                t.complete_secs.push((done - live.first_ms) / 1000.);
            }
            live.last = row.clone();
        }
        let gone: Vec<(String, u32)> = self
            .live
            .keys()
            .filter(|(q, o)| !s.rows.iter().any(|r| r.offer == *o && r.quest == *q))
            .cloned()
            .collect();
        for key in gone {
            let Some(live) = self.live.remove(&key) else {
                continue;
            };
            if live.paid {
                continue;
            }
            let t = self.out.entry(key.0).or_default();
            let d = live.last.deadline;
            if !s.locked && d > 0. && d <= s.now_ms && s.now_ms - d <= TIMER_SLACK_MS {
                t.ended_timer += 1;
            } else if s.died || self.prev_died {
                t.ended_death += 1;
            } else {
                t.unknown += 1;
            }
        }
        self.prev_died = s.died;
    }

    /// The books at the last read: what is still standing is open or turned in and unpaid.
    pub fn finish(mut self) -> BTreeMap<String, AttemptTally> {
        for ((quest, _), live) in self.live {
            if live.paid {
                continue;
            }
            let t = self.out.entry(quest).or_default();
            if live.last.turned_in {
                t.turned_in_unpaid += 1;
            } else {
                t.open += 1;
            }
        }
        self.out
    }
}

/// The tally of one seat over all its per-step reads (in step order).
pub fn tally_seat(steps: &[StepRead]) -> BTreeMap<String, AttemptTally> {
    let mut t = SeatTally::default();
    for s in steps {
        t.push(s);
    }
    t.finish()
}
