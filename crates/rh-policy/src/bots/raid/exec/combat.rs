//! Combat adapter (P-RAID-C): fight, orbwalk and retreat for the current target.
//!
//! P-RAID-REACH-1: the one rule both fight executors (`farm.rs`, `elite.rs`) ask before a ranged seat fights from where
//! it stands.
use crate::bots::paths::RoomPathService;
use rh_sim::Vec2;

/// May a seat standing at `from` fight a target of camp `camp` (a 0-based index into the map's camps) from there? A
/// target camp cannot bring the fight to a seat that stands outside its leash band (`rh_sim::mapdata::patience::leash_ok`
/// over the public map layers, `MOB_LEASH_MARGIN`): the mob gives up and heals. `elite` selects the target's home: the
/// camp's elite spot (`CampDef::elite_spot`) for an elite, any spot of the camp for a normal member. A map without
/// layers, a camp without spots or an unknown camp answers `true` (nothing known, nothing changed).
///
/// STUB (P-RAID-REACH-1): always `true`, the behaviour before the packet. The implementer replaces the body.
pub fn may_fight_from(paths: &RoomPathService, camp: u32, elite: bool, from: Vec2) -> bool {
    let _ = (paths, camp, elite, from);
    true
}
