//! The paced act queue (P-RAID-0): QuestAccept, QuestInteract, UseConsumable and Trade sent as PublicCommand, and the
//! mirror of the seat's order budget that keeps every send inside it. The shared `Command` enum (`bots/types.rs`) is
//! not extended: it keys HumanBot's stats. Pauses draw from the queue's own stream (tag 17).
use super::refs::Refs;
use crate::bots::random::{uniform, Rng};
use rh_sim::constants::{ORDER_BURST, ORDER_PER_SECOND};
use rh_sim::PublicCommand;

/// The most acts waiting at once; a new one past it drops the oldest.
pub const MAX_QUEUED: usize = 8;
/// An act not sent within this long is stale and dropped.
pub const ACT_TTL_MS: f64 = 4_000.;
/// The pause before an act and between two sends (a person reading and clicking a menu).
pub const ACT_GAP_MS: (f64, f64) = (180., 420.);
/// Order-budget tokens the queue leaves for the Humanizer.
pub const ACT_RESERVE: f64 = 6.;

/// A public command the Humanizer does not carry, named by stable keys (resolved per frame by `Refs::act`).
#[derive(Clone, Debug, PartialEq)]
pub enum Act {
    QuestAccept {
        def: String,
    },
    QuestInteract {
        def: String,
        action: u8,
        target: u8,
    },
    /// Pick up a dropped quest spell (action 9). A drop has no row: it is named by the frame's drop `index` and its wire
    /// `reward` id (both checked against the same frame at send time, so an index that moved to another spell sends
    /// nothing).
    QuestPickup {
        index: u8,
        reward: u8,
    },
    UseConsumable,
    Trade {
        merchant: rh_sim::traders::Kind,
        pick: i8,
    },
    /// Cast the quest spell in wire slot `slot` (0 = Q, 1 = E), checked at send time to still hold `reward` with a charge.
    Cast {
        slot: u8,
        reward: u8,
        aim: CastAim,
    },
}

/// Where a cast aims.
#[derive(Clone, Debug, PartialEq)]
pub enum CastAim {
    /// The seat's own position.
    OwnPosition,
    /// A point on the map.
    Point(rh_sim::Vec2),
    /// An actor by its rendered key (`p<id>` / `n<id>`), resolved through the frame at send time.
    Actor(String),
}

/// The seat's order budget as the sim keeps it (`rh_sim::input::OrderBudget`), replayed from what this seat emits.
/// Every emitted command but `Aim` spends one token (the seat never sends `Aim`). A command the seat later fails to
/// resolve spent no real token, so the mirror only ever errs low.
#[derive(Clone, Debug, PartialEq)]
pub struct OrderBudgetMirror {
    tokens: f64,
    at_ms: f64,
}

impl Default for OrderBudgetMirror {
    fn default() -> Self {
        Self {
            tokens: ORDER_BURST,
            at_ms: 0.,
        }
    }
}

impl OrderBudgetMirror {
    fn refill(&mut self, now_ms: f64) {
        if now_ms > self.at_ms {
            self.tokens =
                ORDER_BURST.min(self.tokens + (now_ms - self.at_ms) / 1000. * ORDER_PER_SECOND);
            self.at_ms = now_ms;
        }
    }
    /// Tokens left at `now_ms`.
    pub fn available(&mut self, now_ms: f64) -> f64 {
        self.refill(now_ms);
        self.tokens
    }
    /// Spend one token if the sim would: the same rule as `OrderBudget::allow`.
    pub fn allow(&mut self, now_ms: f64) -> bool {
        self.refill(now_ms);
        if self.tokens >= 1. {
            self.tokens -= 1.;
            true
        } else {
            false
        }
    }
}

#[derive(Clone, Debug, PartialEq)]
struct Entry {
    act: Act,
    queued_at: f64,
    due: f64,
}

/// Counters for the lab.
#[derive(Clone, Copy, Debug, Default, PartialEq, Eq)]
pub struct ActStats {
    pub queued: u64,
    pub sent: u64,
    /// Dropped: overflow, stale, or its handle gone.
    pub dropped: u64,
}

/// The paced queue of acts. Executors return `Act`s beside their intents; the queue keeps at most `MAX_QUEUED` and paces
/// them like a person working a menu: an act waits a pause before it goes, then one send, then another pause. Every
/// frame it first drops an act whose quest row, offered interaction, merchant or potion the frame no longer shows, and
/// one older than `ACT_TTL_MS`. It sends only while `ACT_RESERVE` order-budget tokens stay for the Humanizer's clicks
/// (the budget is the seat's, shared: `Sim::submit` refuses past `ORDER_BURST` tokens refilled at `ORDER_PER_SECOND`).
pub struct ActQueue {
    entries: Vec<Entry>,
    rng: Rng,
    next_at: f64,
    stats: ActStats,
}

impl Default for ActQueue {
    fn default() -> Self {
        Self::new(Rng::new(17))
    }
}

impl ActQueue {
    /// A queue drawing its pauses from `rng` (the seat's tag-17 stream).
    pub fn new(rng: Rng) -> Self {
        Self {
            entries: Vec::new(),
            rng,
            next_at: 0.,
            stats: ActStats::default(),
        }
    }
    pub fn stats(&self) -> ActStats {
        self.stats
    }
    pub fn len(&self) -> usize {
        self.entries.len()
    }
    pub fn is_empty(&self) -> bool {
        self.entries.is_empty()
    }

    /// Queue `act` (an act already waiting is not queued twice).
    pub fn push(&mut self, act: Act, now: f64) {
        if self.entries.iter().any(|e| e.act == act) {
            return;
        }
        if self.entries.len() >= MAX_QUEUED {
            self.entries.remove(0);
            self.stats.dropped += 1;
        }
        let due = now + uniform(&mut self.rng, ACT_GAP_MS.0, ACT_GAP_MS.1);
        self.entries.push(Entry {
            act,
            queued_at: now,
            due,
        });
        self.stats.queued += 1;
    }

    /// Drop what this frame no longer offers, then send at most one due act while the budget keeps its reserve.
    pub fn release(
        &mut self,
        now: f64,
        refs: &Refs,
        budget: &mut OrderBudgetMirror,
        out: &mut Vec<PublicCommand>,
    ) {
        let before = self.entries.len();
        self.entries
            .retain(|e| now - e.queued_at <= ACT_TTL_MS && refs.act(&e.act).is_some());
        self.stats.dropped += (before - self.entries.len()) as u64;
        if now < self.next_at {
            return;
        }
        let Some(i) = self.entries.iter().position(|e| e.due <= now) else {
            return;
        };
        if budget.available(now) < 1. + ACT_RESERVE {
            return;
        }
        let Some(cmd) = refs.act(&self.entries[i].act) else {
            return;
        };
        if !budget.allow(now) {
            return;
        }
        self.entries.remove(i);
        out.push(cmd);
        self.stats.sent += 1;
        self.next_at = now + uniform(&mut self.rng, ACT_GAP_MS.0, ACT_GAP_MS.1);
    }
}
