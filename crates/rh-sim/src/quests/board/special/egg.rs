//! Rebirth Egg, the planted Gatecrasher reward (Spell_Reworks). Death happens before a hatch can be scheduled.
use crate::{
    constants::{BUSH_REVEAL_RADIUS, MASK_MOVE, TILE},
    math::hypot,
    state::{Phase, Player, World},
    statics::MatchStatic,
    systems::Cx,
    Outcome, PlayerId, Status, TileMap, Vec2,
};
use serde::{Deserialize, Serialize, Serializer};

pub(crate) fn visit_rules(word: &mut dyn FnMut(u64)) {
    for value in [
        SAFE_DISTANCE,
        SIGHT_RADIUS,
        HATCH_MS,
        HATCH_HP_SHARE,
        RADIUS,
    ] {
        word(value.to_bits());
    }
    word(MIN_STANDING as u64);
}

const SAFE_DISTANCE: f64 = 600.;
const SIGHT_RADIUS: f64 = 200.;
const HATCH_MS: f64 = 3_000.;
const HATCH_HP_SHARE: f64 = 0.4;
const MIN_STANDING: usize = 4;
pub(crate) const RADIUS: f64 = 14.;

#[derive(Clone, Copy, Debug, PartialEq, Serialize, Deserialize)]
#[serde(deny_unknown_fields)]
pub(crate) struct Egg {
    pub(crate) owner: PlayerId,
    pub(crate) at: Vec2,
    pub(crate) death: Option<PendingDeath>,
    broken: bool,
}

#[derive(Clone, Copy, Debug, PartialEq, Serialize, Deserialize)]
#[serde(deny_unknown_fields)]
pub(crate) struct PendingDeath {
    pub(crate) at: f64,
    pub(crate) place: u32,
}

/// The checkpoint always writes the typed field. The state hasher receives a distinct tag
/// only when an egg exists, so worlds that never used the spell keep their legacy word stream.
pub(crate) fn serialize_shells<S: Serializer>(eggs: &[Egg], s: S) -> Result<S::Ok, S::Error> {
    if s.is_human_readable() {
        eggs.serialize(s)
    } else if eggs.is_empty() {
        s.serialize_unit()
    } else {
        ("rebirth-eggs", eggs).serialize(s)
    }
}

/// An ordinary corpse retains its legacy hash. A retained corpse whose owner hatched adds
/// its own tag, which distinguishes it from the same corpse before the owner returned.
pub(crate) fn serialize_hatched<S: Serializer>(hatched: &bool, s: S) -> Result<S::Ok, S::Error> {
    if s.is_human_readable() {
        hatched.serialize(s)
    } else if *hatched {
        s.serialize_str("rebirth-egg-corpse")
    } else {
        s.serialize_unit()
    }
}

/// The cast carries the usual cursor aim. The workbook plants at the caster's current authoritative feet.
pub(crate) fn plant_egg(cx: &mut Cx, owner: PlayerId, _at: Vec2) -> bool {
    let Some(p) = cx.w.player(owner) else {
        return false;
    };
    let at = p.pos();
    let walls = crate::barriers::held(cx.w);
    let map = crate::barriers::body_map(cx.k, &walls, cx.w.rt.gates_locked);
    let slot =
        cx.w.rebirth_eggs
            .binary_search_by_key(&owner, |egg| egg.owner);
    if cx.w.phase != Phase::Active
        || cx.k.config.training
        || cx.w.rt.voided
        || !p.standing()
        || p.seat.bot
        || slot.is_ok()
        || standing(cx.w) < MIN_STANDING
        || !outside_coliseum(cx.k, at)
        || !p.foot().clear(map, at, MASK_MOVE)
    {
        return false;
    }
    cx.w.rebirth_eggs.insert(
        slot.unwrap_err(),
        Egg {
            owner,
            at,
            death: None,
            broken: false,
        },
    );
    true
}

fn outside_coliseum(k: &MatchStatic, at: Vec2) -> bool {
    let map = &k.arena.map;
    let nearest = map.layers().map_or_else(
        || k.arena.centre(),
        |layers| {
            let rect = layers.coliseum().rect;
            let x = f64::from(rect.x);
            let y = f64::from(rect.y);
            Vec2::new(
                at.x.clamp(x * TILE, (x + f64::from(rect.w)) * TILE),
                at.y.clamp(y * TILE, (y + f64::from(rect.h)) * TILE),
            )
        },
    );
    at.x.is_finite()
        && at.y.is_finite()
        && hypot(at.x - nearest.x, at.y - nearest.y) >= SAFE_DISTANCE
}

fn standing(w: &World) -> usize {
    w.players.iter().filter(|p| p.standing()).count()
}

/// Keep the original result place and death time even when banking must wait.
pub(crate) fn delay(w: &mut World, owner: PlayerId, standing: usize) -> bool {
    if standing < MIN_STANDING {
        return false;
    }
    let Ok(index) = w.rebirth_eggs.binary_search_by_key(&owner, |egg| egg.owner) else {
        return false;
    };
    let egg = &mut w.rebirth_eggs[index];
    if egg.broken || egg.death.is_some() {
        return false;
    }
    egg.death = Some(PendingDeath {
        at: w.server_time,
        place: standing as u32 + 1,
    });
    true
}

/// A held result is already a death for credit and contact bookkeeping.
pub(crate) fn awaiting_hatch(w: &World, owner: PlayerId) -> bool {
    w.rebirth_eggs
        .binary_search_by_key(&owner, |egg| egg.owner)
        .is_ok_and(|i| w.rebirth_eggs[i].death.is_some())
}

pub(crate) fn remove(w: &mut World, owner: PlayerId) -> Option<PendingDeath> {
    let index = w
        .rebirth_eggs
        .binary_search_by_key(&owner, |egg| egg.owner)
        .ok()?;
    w.rebirth_eggs.remove(index).death
}

/// Runs at settlement after the lethal ledger and the normal death side effects.
pub(crate) fn tick(cx: &mut Cx, standing: usize) {
    let mut index = 0;
    let enabled = standing >= MIN_STANDING && cx.w.phase == Phase::Active && !cx.w.rt.voided;
    while let Some(&egg) = cx.w.rebirth_eggs.get(index) {
        let due = egg
            .death
            .is_some_and(|death| cx.w.server_time >= death.at + HATCH_MS);
        if enabled && !egg.broken && !due {
            index += 1;
            continue;
        }
        cx.w.rebirth_eggs.remove(index);
        let Some(death) = egg.death else { continue };
        if enabled && !egg.broken {
            hatch(cx, egg);
        } else {
            crate::systems::settle::resolve_egg_death(cx.w, cx.ev, egg.owner, death);
        }
    }
}

fn hatch(cx: &mut Cx, egg: Egg) {
    let Some(p) = cx.w.players.get_mut(egg.owner) else {
        return;
    };
    if p.status != Status::Dead || p.down != Some(Outcome::Died) || p.resolved {
        return;
    }
    p.status = Status::Alive;
    p.down = None;
    p.fate = Default::default();
    p.credit.last_hit = None;
    p.inputs.clear();
    p.vitals.hp = p.vitals.max_hp * HATCH_HP_SHARE;
    p.vitals.barrier = 0.;
    p.vitals.immune_until = 0.;
    if let Some(corpse) =
        cx.w.plague_corpses
            .iter_mut()
            .find(|c| c.player == egg.owner)
    {
        corpse.owner_hatched = true;
    }
    crate::systems::displace::apply(cx.k, cx.w, cx.pf, egg.owner, egg.at);
    let tick_ms = cx.k.tick_ms();
    let tick = cx.w.tick;
    let locked = cx.w.rt.gates_locked;
    let p = cx.w.players.get_mut(egg.owner).expect("hatching player");
    crate::vision::refresh_player(p, tick, tick_ms, locked);
}

/// No owner or private reference is disclosed. During the hatch every observer can see the shell.
pub(crate) fn visible(k: &MatchStatic, w: &World, viewer: &Player, egg: Egg) -> bool {
    if egg.broken {
        return false;
    }
    if egg.death.is_some() {
        return true;
    }
    let from = viewer.pos();
    let distance = hypot(from.x - egg.at.x, from.y - egg.at.y);
    if !viewer.standing() || distance > SIGHT_RADIUS {
        return false;
    }
    let map = crate::collision::view(k, w);
    if crate::quests::board::rewards::effects::reveals(
        w,
        map.current(),
        viewer.id,
        egg.at,
        k.tick_ms(),
    ) {
        return true;
    }
    if crate::vision::WardSight::of(k, w).sees(map, viewer.id, egg.at, w.tick) {
        return true;
    }
    if crate::vision::scene_line_blocked(map.current(), from.x, from.y, egg.at.x, egg.at.y) {
        return false;
    }
    map.layers().is_none_or(|layers| {
        let bush = layers.bush_at_world(egg.at.x, egg.at.y);
        bush == 0 || layers.bush_at_world(from.x, from.y) == bush || distance <= BUSH_REVEAL_RADIUS
    })
}

/// Only a standing opponent who can see the egg can break it with a physical weapon contact.
pub(crate) fn strikeable(k: &MatchStatic, w: &World, viewer: &Player, egg: Egg) -> bool {
    viewer.id != egg.owner && viewer.standing() && visible(k, w, viewer, egg)
}

pub(crate) fn break_shell(w: &mut World, index: usize) {
    let egg = &mut w.rebirth_eggs[index];
    if egg.death.is_some() {
        egg.broken = true;
    } else {
        w.rebirth_eggs.remove(index);
    }
}

pub(crate) fn check_world(k: &MatchStatic, w: &World) -> Result<(), String> {
    if w.rebirth_eggs.is_empty() {
        return Ok(());
    }
    if w.phase != Phase::Active
        || w.rt.voided
        || k.config.training
        || standing(w) < MIN_STANDING
        || !w
            .rebirth_eggs
            .windows(2)
            .all(|pair| pair[0].owner < pair[1].owner)
    {
        return Err("invalid Rebirth Egg runtime".into());
    }
    for egg in &w.rebirth_eggs {
        let Some(p) = w.player(egg.owner) else {
            return Err("unknown Rebirth Egg owner".into());
        };
        if p.seat.bot || egg.broken || !outside_coliseum(k, egg.at) {
            return Err("invalid Rebirth Egg placement".into());
        }
        let valid = match egg.death {
            None => p.standing(),
            Some(death) => {
                p.status == Status::Dead
                    && p.down == Some(Outcome::Died)
                    && !p.resolved
                    && (w.raid_started_at..=w.server_time).contains(&death.at)
                    && w.server_time < death.at + HATCH_MS
                    && (MIN_STANDING as u32 + 1..=w.rt.started_with).contains(&death.place)
            }
        };
        if !valid {
            return Err("invalid Rebirth Egg death".into());
        }
    }
    Ok(())
}

/// The authoritative base geometry has been rebuilt by the time checkpoint restore calls this.
pub(crate) fn check_ground(map: &TileMap, w: &World) -> Result<(), String> {
    if w.rebirth_eggs.iter().any(|egg| {
        !w.player(egg.owner)
            .is_some_and(|p| p.foot().clear(map, egg.at, MASK_MOVE))
    }) {
        return Err("Rebirth Egg outside walkable ground".into());
    }
    Ok(())
}

#[cfg(test)]
#[path = "egg_tests.rs"]
mod tests;
