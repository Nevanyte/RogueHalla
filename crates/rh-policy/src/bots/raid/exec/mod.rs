//! RaidBot executors: one file per goal kind, each implementing `goal::Executor`, and `registry`, the one list a later
//! packet adds its executor to.
pub mod chest;
pub mod combat;
pub mod elite;
pub mod farm;
pub mod sustain;
pub mod travel;

use super::goal::Executor;

/// Every executor of the seat, in call order. Add one line per new goal module (quest executors included). The seat
/// dispatches a goal to the first executor whose `kinds()` holds it, and calls every executor's `observe`, `propose`
/// and `ride_along` in this order: the order is behaviour (it fixes the arbiter stream's draw order and which
/// ride-along intent wins a channel).
pub fn registry() -> Vec<Box<dyn Executor>> {
    vec![
        Box::new(farm::Farm::default()),
        Box::new(chest::Chest::default()),
        Box::new(super::quest::walker::Walker::default()),
        Box::new(travel::Roam::default()),
    ]
}
