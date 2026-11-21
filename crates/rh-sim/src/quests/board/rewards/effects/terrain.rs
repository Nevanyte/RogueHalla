//! Collision-backed cage and temporary scouting/combat deployments.
use super::*;
use crate::{
    barriers::{self, Barrier},
    constants::TILE,
    map::{tile_to_world_center, world_to_tile, TILE_FLOOR},
    statics::MatchStatic,
    TileMap,
};

pub(super) const CAGE_RADIUS: f64 = 160.;
const CAGE_MS: f64 = 6000.;
const GAP_HALF_ANGLE: f64 = 0.48;
pub(super) fn visit_rules(word: &mut dyn FnMut(u64)) {
    for v in [
        CAGE_RADIUS,
        CAGE_MS,
        FALCON_SPEED_PER_MS,
        RADIUS,
        GAP_HALF_ANGLE,
        TURRET_MS,
        TURRET_INTERVAL,
        TURRET_RANGE,
        TURRET_REWARD_RANGE,
        WARD_MS,
        FALCON_MS,
    ] {
        word(v.to_bits());
    }
}

const TURRET_MS: f64 = 25_000.;
const TURRET_INTERVAL: f64 = 850.;
const TURRET_RANGE: f64 = 340.;
const TURRET_REWARD_RANGE: f64 = 400.;
const WARD_MS: f64 = 75_000.;
const FALCON_MS: f64 = 4000.;
const FALCON_SPEED_PER_MS: f64 = 6.;
pub(crate) const RADIUS: f64 = 16.;

#[derive(Clone, Copy, Debug, PartialEq, Serialize, Deserialize)]
pub enum DeploymentKind {
    Turret,
    LanternWard,
    ScoutFalcon,
}

#[derive(Clone, Copy, Debug, PartialEq, Serialize, Deserialize)]
#[serde(deny_unknown_fields)]
pub(crate) struct Deployment {
    id: u32,
    pub(crate) owner: PlayerId,
    pub(crate) at: Vec2,
    end: Option<Vec2>,
    kind: DeploymentKind,
    hp: f64,
    until: f64,
    next_shot: f64,
}

#[derive(Clone, Copy, Debug, PartialEq, Serialize, Deserialize)]
pub struct DeploymentView {
    pub id: u32,
    pub owner: Option<PlayerId>,
    pub kind: DeploymentKind,
    pub at: Vec2,
    pub end: Option<Vec2>,
    pub hp: f64,
    pub until: f64,
}

pub(super) fn body_map<'a>(k: &'a MatchStatic, w: &'a World) -> &'a TileMap {
    w.abilities
        .as_ref()
        .and_then(|a| a.overlay.0.as_deref())
        .map_or_else(
            || crate::collision::map(k, w),
            |o| {
                if w.rt.gates_locked {
                    o.locked.as_ref().unwrap_or(&o.open)
                } else {
                    &o.open
                }
            },
        )
}

pub(super) fn cage(cx: &mut Cx, ix: FighterIx, at: Vec2) -> bool {
    let p = cx.w.core(ix);
    let (from, owner) = (p.pos(), p.id);
    if !placement(cx, from, at, 400., 0.) || cx.w.entity_counter == u32::MAX {
        return false;
    }
    let direction = p.body.facing;
    let map = body_map(cx.k, cx.w);
    let reach = CAGE_RADIUS + TILE;
    let min_x = world_to_tile(at.x - reach).max(1);
    let min_y = world_to_tile(at.y - reach).max(1);
    let max_x = world_to_tile(at.x + reach).min(i64::from(map.w) - 2);
    let max_y = world_to_tile(at.y + reach).min(i64::from(map.h) - 2);
    let mut cells = Vec::new();
    for ty in min_y..=max_y {
        for tx in min_x..=max_x {
            let pt = Vec2::new(tile_to_world_center(tx), tile_to_world_center(ty));
            let gap =
                crate::math::wrap_angle(crate::math::atan2(pt.y - at.y, pt.x - at.x) - direction)
                    .abs();
            if (distance(pt, at) - CAGE_RADIUS).abs() <= TILE * 0.65
                && gap >= GAP_HALF_ANGLE
                && map.tile(tx, ty) == Some(TILE_FLOOR)
                && cx.k.locked_map.as_ref().is_none_or(|m| {
                    m.tile(tx, ty) == Some(TILE_FLOOR)
                        || cx
                            .w
                            .destroyed_terrain
                            .cells
                            .binary_search(&(ty as u32 * map.w + tx as u32))
                            .is_ok()
                })
            {
                cells.push(ty as u32 * map.w + tx as u32);
            }
        }
    }
    if cells.is_empty() {
        return false;
    }
    let mut moves = Vec::new();
    for i in 0..cx.w.fighter_count() {
        let j = FighterIx::of_slot(i);
        let fighter = cx.w.core(j);
        let from = fighter.pos();
        let footprint = fighter.foot();
        let overlaps = |pos: Vec2| {
            cells.iter().any(|&i| {
                let x = tile_to_world_center(i64::from(i % map.w));
                let y = tile_to_world_center(i64::from(i / map.w));
                (pos.x - x).abs() < TILE / 2. + footprint.half_x()
                    && (pos.y - y).abs() < TILE / 2. + footprint.half_y()
            })
        };
        if !cx.w.standing(j) || !overlaps(from) {
            continue;
        }
        if stasis(cx.w, fighter.id) {
            return false;
        }
        let d = distance(at, from);
        let sign = if d < CAGE_RADIUS { -1. } else { 1. };
        let candidate = [sign, -sign].into_iter().find_map(|side| {
            (1..=24)
                .map(|step| between(at, from, (d + side * f64::from(step) * 4.) / d))
                .find(|&to| !overlaps(to) && fighter.foot().segment_clear(map, from, to, MASK_MOVE))
        });
        let Some(to) = candidate else { return false };
        moves.push((fighter.id, to));
    }
    let now = cx.w.server_time;
    let id = crate::systems::fields::add(
        cx.w,
        rt::Field {
            id: 0,
            kind: rt::FieldKind::Wall,
            ability: rt::AbilityId::Freeze,
            owner: rt::StatusSource {
                id: Some(owner),
                name: cx.w.roster.label(owner).unwrap().clone(),
                owner_id: None,
            },
            subject: None,
            x: at.x,
            y: at.y,
            x2: at.x,
            y2: at.y,
            r: CAGE_RADIUS,
            vx: 0.,
            vy: 0.,
            started_at: now,
            ends_at: now + CAGE_MS,
            exit_stun_ms: None,
        },
    );
    cx.w.abilities
        .as_mut()
        .unwrap()
        .barriers
        .0
        .push(Barrier { field: id, cells });
    barriers::sync(cx.k, cx.w);
    for (id, to) in moves {
        displace::apply(cx.k, cx.w, cx.pf, id, to);
    }
    let held = barriers::held(cx.w);
    let map = barriers::body_map(cx.k, &held, cx.w.rt.gates_locked);
    for i in 0..cx.w.fighter_count() {
        let j = FighterIx::of_slot(i);
        let c = cx.w.core_mut(j);
        let path = c.motion.path.as_slice();
        if let Some(&goal) = path.last() {
            if barriers::walk_blocked(map, c.pos(), path) {
                let (from, foot) = (c.pos(), c.foot());
                c.motion.path.replan(cx.pf, map, from, goal, foot);
            }
        }
    }
    true
}

pub(crate) fn is_cage(w: &World, f: &rt::Field) -> bool {
    state(w).is_some()
        && f.kind == rt::FieldKind::Wall
        && f.ability == rt::AbilityId::Freeze
        && f.r == CAGE_RADIUS
        && f.x == f.x2
        && f.y == f.y2
        && f.subject.is_none()
        && f.ends_at == f.started_at + CAGE_MS
}

pub(super) fn deploy(
    cx: &mut Cx,
    owner: PlayerId,
    from: Vec2,
    at: Vec2,
    kind: DeploymentKind,
) -> bool {
    if !placement(cx, from, at, 400., RADIUS) || cx.w.entity_counter == u32::MAX {
        return false;
    }
    let (hp, life) = if kind == DeploymentKind::Turret {
        (100., TURRET_MS)
    } else {
        (35., WARD_MS)
    };
    let id = cx.w.next_entity_id();
    let now = cx.w.server_time;
    let rows = &mut state_mut(cx.w).deployments;
    let cap = if kind == DeploymentKind::Turret { 1 } else { 2 };
    let same = |r: &&Deployment| r.owner == owner && r.kind == kind;
    if rows.iter().filter(same).count() >= cap {
        let first = rows.iter().position(|r| same(&r)).unwrap();
        rows.remove(first);
    }
    rows.push(Deployment {
        id,
        owner,
        at,
        end: None,
        kind,
        hp,
        until: now + life,
        next_shot: now + TURRET_INTERVAL,
    });
    true
}

pub(super) fn falcon(cx: &mut Cx, ix: FighterIx, aim: Vec2) -> bool {
    if cx.w.entity_counter == u32::MAX {
        return false;
    }
    let p = cx.w.core(ix);
    let (owner, at) = (p.id, p.pos());
    let end = aim_end(cx, ix, aim, 1200.);
    let map = crate::collision::map(cx.k, cx.w);
    let end = clip(at, end, |p| has_line_of_sight(map, at, p, MASK_SIGHT));
    if distance(at, end) < 1. {
        return false;
    }
    let id = cx.w.next_entity_id();
    let until = cx.w.server_time + FALCON_MS;
    let rows = &mut state_mut(cx.w).deployments;
    rows.retain(|d| d.owner != owner || d.kind != DeploymentKind::ScoutFalcon);
    rows.push(Deployment {
        id,
        owner,
        at,
        end: Some(end),
        kind: DeploymentKind::ScoutFalcon,
        hp: 0.,
        until,
        next_shot: 0.,
    });
    true
}

pub(crate) fn deployments(w: &World) -> &[Deployment] {
    state(w).map_or(&[], |s| &s.deployments)
}

impl Deployment {
    pub(crate) fn strikeable(self, w: &World, attacker: PlayerId) -> bool {
        self.hp > 0.
            && self.until > w.server_time
            && crate::systems::plague_zombies::hostile(w, attacker, self.owner)
    }
}

pub(crate) fn damage_deployment(w: &mut World, i: usize, amount: f64) {
    let rows = &mut state_mut(w).deployments;
    if let Some(row) = rows.get_mut(i) {
        row.hp -= amount;
        if row.hp <= 0. {
            rows.remove(i);
        }
    }
}

pub(crate) fn reveals(w: &World, map: &TileMap, viewer: PlayerId, at: Vec2, tick_ms: f64) -> bool {
    deployments(w).iter().any(|d| {
        if d.owner != viewer || d.until <= w.server_time {
            return false;
        }
        let (origin, range) = match d.kind {
            DeploymentKind::Turret => (d.at, 220.),
            DeploymentKind::LanternWard => (d.at, 365.),
            DeploymentKind::ScoutFalcon => {
                let reached = falcon_head(*d, w.server_time, tick_ms);
                (nearest(d.at, reached, at), 165.)
            }
        };
        distance(origin, at) <= range
            && !crate::vision::scene_line_blocked(map, origin.x, origin.y, at.x, at.y)
    })
}

fn falcon_head(d: Deployment, now: f64, tick_ms: f64) -> Vec2 {
    let elapsed = (FALCON_MS + now - d.until).max(0.) + tick_ms;
    towards(
        d.at,
        d.end.expect("falcon has a flight segment"),
        FALCON_SPEED_PER_MS * elapsed,
    )
}

fn nearest(a: Vec2, b: Vec2, at: Vec2) -> Vec2 {
    if a == b {
        return a;
    }
    let (x, y) = (b.x - a.x, b.y - a.y);
    let u = ((x * (at.x - a.x) + y * (at.y - a.y)) / (x * x + y * y)).clamp(0., 1.);
    between(a, b, u)
}

pub(crate) fn visible_deployments(
    sim: &crate::Sim,
    viewer: PlayerId,
    out: &mut Vec<DeploymentView>,
) {
    out.clear();
    for d in deployments(&sim.w) {
        let owner = d.owner == viewer;
        if d.until > sim.w.server_time
            && (owner || (d.end.is_none() && sim.can_see_point(viewer, d.at)))
        {
            out.push(DeploymentView {
                id: d.id,
                owner: owner.then_some(d.owner),
                kind: d.kind,
                at: d.at,
                end: d
                    .end
                    .map(|_| falcon_head(*d, sim.w.server_time, sim.k.tick_ms())),
                hp: d.hp,
                until: d.until,
            });
        }
    }
}

pub(super) fn update(cx: &mut Cx) {
    let now = cx.w.server_time;
    state_mut(cx.w).deployments.retain(|d| d.until > now);
    for i in 0..deployments(cx.w).len() {
        let d = deployments(cx.w)[i];
        if d.kind != DeploymentKind::Turret || now < d.next_shot {
            continue;
        }
        state_mut(cx.w).deployments[i].next_shot = now + TURRET_INTERVAL;
        let Some(owner) = cx.w.player(d.owner).filter(|p| p.standing()) else {
            continue;
        };
        let map = crate::collision::map(cx.k, cx.w);
        let mut target = None;
        for j in 0..cx.w.fighter_count() {
            let j = FighterIx::of_slot(j);
            let t = cx.w.core(j);
            let range = distance(d.at, t.pos());
            if range <= TURRET_RANGE
                && enemy(cx.w, d.owner, j)
                && !stasis(cx.w, t.id)
                && !rt::untargetable(&t.statuses.instances, now)
                && has_line_of_sight(map, d.at, t.pos(), MASK_SIGHT)
                && target.is_none_or(|(dist, id)| (range, t.id) < (dist, id))
            {
                target = Some((range, t.id));
            }
        }
        if let Some((_, target)) = target {
            let remote =
                distance(owner.pos(), d.at) > TURRET_REWARD_RANGE && cx.w.mobs.contains(target);
            let weapon = owner.active_weapon();
            let sheet = crate::content::paced(owner.faction());
            let interval =
                crate::content::attack_interval(&sheet, weapon, owner.modifiers.mirrors().as_mult);
            let raw = 0.3 * crate::modifiers::packet(owner, weapon).raw * TURRET_INTERVAL
                / (interval * 1000.);
            hit(
                cx,
                d.owner,
                target,
                raw,
                remote.then_some(REMOTE_TURRET_SOURCE),
                false,
            );
        }
    }
}

pub(super) fn pull_turrets(cx: &mut Cx, tornado: Tornado) {
    for i in 0..deployments(cx.w).len() {
        let d = deployments(cx.w)[i];
        if d.kind == DeploymentKind::Turret
            && d.until > cx.w.server_time
            && distance(d.at, tornado.at) <= TORNADO_RADIUS
        {
            let map = body_map(cx.k, cx.w);
            let at = clip(
                d.at,
                towards(
                    d.at,
                    tornado.at,
                    180. * (tornado.max_hp / 100. - 1.).clamp(0., 1.),
                ),
                |p| Footprint::Disc(RADIUS).segment_clear(map, d.at, p, MASK_MOVE),
            );
            state_mut(cx.w).deployments[i].at = at;
        }
    }
}

pub(super) fn validate(k: &MatchStatic, w: &World, rows: &[Deployment]) -> Result<(), String> {
    let inside = |p: Vec2| {
        (0. ..f64::from(k.arena.map.w) * TILE).contains(&p.x)
            && (0. ..f64::from(k.arena.map.h) * TILE).contains(&p.y)
    };
    for (i, d) in rows.iter().enumerate() {
        let (cap, life, max_hp) = match d.kind {
            DeploymentKind::Turret => (1, TURRET_MS, 100.),
            DeploymentKind::LanternWard => (2, WARD_MS, 35.),
            DeploymentKind::ScoutFalcon => (1, FALCON_MS, 0.),
        };
        if d.id == 0
            || d.id > w.entity_counter
            || !w.player(d.owner).is_some_and(|p| !p.seat.bot)
            || !inside(d.at)
            || !d.until.is_finite()
            || d.until <= 0.
            || d.until > w.server_time + life
            || !d.next_shot.is_finite()
            || d.next_shot < 0.
            || d.next_shot > w.server_time + TURRET_INTERVAL
            || !d.hp.is_finite()
            || d.hp > max_hp
            || (max_hp > 0. && d.hp <= 0.)
            || w.containers.iter().any(|c| c.id == d.id)
            || w.quests
                .as_ref()
                .is_some_and(|q| q.offers.iter().any(|o| o.id == d.id))
            || w.abilities
                .as_ref()
                .is_some_and(|a| a.fields.iter().any(|f| f.id == d.id))
            || w.projectiles.iter().any(|p| p.launch.id == d.id)
            || rows.get(i.wrapping_sub(1)).is_some_and(|p| p.id >= d.id)
            || rows[..i]
                .iter()
                .filter(|p| p.owner == d.owner && p.kind == d.kind)
                .count()
                >= cap
            || match d.end {
                Some(end) => {
                    d.kind != DeploymentKind::ScoutFalcon
                        || !inside(end)
                        || !(1. ..=1200. + 1e-6).contains(&distance(d.at, end))
                        || d.hp != 0.
                        || d.next_shot != 0.
                }
                None => d.kind == DeploymentKind::ScoutFalcon,
            }
        {
            return Err("invalid Top25 spell deployment".into());
        }
    }
    Ok(())
}
