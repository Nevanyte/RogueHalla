//! Combat adapter (P-RAID-C): fight, orbwalk and retreat for the current target.
//!
//! P-RAID-REACH-1: the one rule both fight executors (`farm.rs`, `elite.rs`) ask before a ranged seat fights from where
//! it stands.
use crate::bots::paths::RoomPathService;
use crate::bots::raid::exec::farm::{lock_range, DEFEND_U};
use crate::bots::raid::goal::Cx;
use crate::bots::raid::percept::MobSeen;
use rh_sim::constants::MOB_LEASH_MARGIN;
use rh_sim::mapdata::patience::leash_ok;
use rh_sim::{Vec2, WeaponId};

/// May a seat standing at `from` fight a target of camp `camp` (a 0-based index into the map's camps) from there? A
/// target camp cannot bring the fight to a seat that stands outside its leash band (`rh_sim::mapdata::patience::leash_ok`
/// over the public map layers, `MOB_LEASH_MARGIN`): the mob gives up and heals. `elite` selects the target's home: the
/// camp's elite spot (`CampDef::elite_spot`) for an elite, any spot of the camp for a normal member. A map without
/// layers, a camp without spots or an unknown camp answers `true` (nothing known, nothing changed).
///
/// An elite whose camp has no valid elite spot reads the union over the camp's spots, as a normal member does.
pub fn may_fight_from(paths: &RoomPathService, camp: u32, elite: bool, from: Vec2) -> bool {
    let Some(layers) = paths.collision_map().layers() else {
        return true;
    };
    let data = layers.data();
    let Some(def) = data.camps.get(camp as usize) else {
        return true;
    };
    if def.spots.is_empty() {
        return true;
    }
    let ok = |s: &rh_sim::mapdata::WorldPt| {
        leash_ok(data, camp, Vec2::new(s.x, s.y), from, MOB_LEASH_MARGIN)
    };
    let elite_home = usize::try_from(def.elite_spot)
        .ok()
        .filter(|_| elite)
        .and_then(|i| def.spots.get(i));
    match elite_home {
        Some(home) => ok(home),
        None => def.spots.iter().any(ok),
    }
}

/// The approach rule both fight executors share (P-RAID-REACH-1): does a seat holding `in_hand` lock `mob` (a target of
/// camp `camp`) from where it stands, rather than step toward it? Within `lock_range`, and, for a ranged weapon, either
/// allowed to fight from here (`may_fight_from`: the target's camp can bring the fight to the seat) or the mob is
/// already fighting at the seat's side (engaged within `DEFEND_U`: it has reached the seat). A melee lock (40 u) is
/// unchanged. `elite` as in `may_fight_from`.
pub(crate) fn locks_from_here(
    cx: &Cx<'_>,
    camp: u32,
    elite: bool,
    mob: &MobSeen,
    in_hand: Option<WeaponId>,
) -> bool {
    let d = cx.p.dist(mob.at);
    if d > lock_range(in_hand) {
        return false;
    }
    let ranged = matches!(
        in_hand.map(|w| rh_sim::content::weapon(w).shape),
        Some(rh_sim::content::Shape::Projectile { .. })
    );
    !ranged || (mob.engaged && d <= DEFEND_U) || may_fight_from(&*cx.paths, camp, elite, cx.p.me.at)
}
