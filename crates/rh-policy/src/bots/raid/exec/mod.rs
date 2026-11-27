//! RaidBot executors: one file per goal kind, each implementing `goal::Executor`, and `registry`, the one list a later
//! packet adds its executor to.
pub mod chest;
pub mod combat;
pub mod elite;
pub mod farm;

#[cfg(feature = "strategy-route-lab")]
pub fn registry_with_lab_route(route: farm::LabRoute) -> Vec<Box<dyn super::goal::Executor>> {
    registry_with_farm(farm::Farm::with_lab_route(route))
}
pub mod held;
pub mod spell;
pub mod sustain;
pub mod travel;

use super::goal::Executor;

/// Every executor of the seat, in call order. Add one line per new goal module (quest executors included). The seat
/// dispatches a goal to the first executor whose `kinds()` holds it, and calls every executor's `observe`, `propose`
/// and `ride_along` in this order: the order is behaviour (it fixes the arbiter stream's draw order and which
/// ride-along intent wins a channel).
pub fn registry() -> Vec<Box<dyn Executor>> {
    registry_with_farm(farm::Farm::default())
}

fn registry_with_farm(farm: farm::Farm) -> Vec<Box<dyn Executor>> {
    vec![
        Box::new(sustain::Sustain::default()),
        Box::new(farm),
        Box::new(chest::Chest::default()),
        Box::new(elite::Elite::default()),
        Box::new(super::quest::walker::Walker::default()),
        Box::new(super::quest::dwell::Dwell::default()),
        Box::new(travel::Roam::default()),
        Box::new(super::quest::decide::Decide::default()),
        Box::new(spell::Spell::default()),
    ]
}
