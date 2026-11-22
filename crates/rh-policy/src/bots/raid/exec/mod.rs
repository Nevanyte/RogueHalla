//! RaidBot executors: one file per goal kind, each implementing `goal::Executor`.
//!
//! `registry` is the one list a later packet adds a line to: its executor proposes candidates for its kind, runs the
//! goals of its kind and may ride along any goal. The seat dispatches a goal to the first executor whose `kinds()` holds
//! it, and calls every executor's `observe`, `propose` and `ride_along` in this order (the order is behaviour: it fixes
//! the arbiter stream's draw order and which ride-along intent wins a channel).
pub mod chest;
pub mod combat;
pub mod elite;
pub mod farm;
pub mod sustain;
pub mod travel;

use super::goal::Executor;

/// Every executor of the seat, in call order. Add one line per new goal module (quest executors included).
pub fn registry() -> Vec<Box<dyn Executor>> {
    vec![
        Box::new(farm::Farm::default()),
        Box::new(chest::Chest::default()),
        Box::new(travel::Roam::default()),
    ]
}
