//! Authoritative Top25 spell effects. The inventory pays only after effect admission succeeds.
pub(super) fn visit_rules(word: &mut dyn FnMut(u64)) {
    for value in [
        STASIS_MS,
        SHIELD_MS,
        WARHORN_MS,
        TORNADO_WINDUP,
        TORNADO_RADIUS,
        PULL_TICK_DISTANCE,
        SMITE_RANGE,
        SWAP_RANGE,
    ] {
        word(value.to_bits());
    }
    terrain::visit_rules(word);
}

mod checkpoint;
mod terrain;
pub(crate) use checkpoint::validate;
#[cfg(test)]
mod tests;

use super::{inventory, Award};
use crate::{
    fighter::FighterIx,
    geometry::{has_line_of_sight, Footprint},
    map::{MASK_MOVE, MASK_SIGHT},
    math::{cos, hypot, sin},
    state::{Packet, World},
    status_runtime::{self as rt, Selector, StatusApply, StatusKind},
    systems::{displace, ledger, status, Cx},
    PlayerId, Vec2,
};
use serde::{Deserialize, Serialize};

pub(crate) use terrain::RADIUS as DEPLOYMENT_RADIUS;
pub(crate) use terrain::{damage_deployment, deployments, is_cage, reveals, visible_deployments};
pub use terrain::{DeploymentKind, DeploymentView};

const STASIS_MS: f64 = 2500.;
const SHIELD_MS: f64 = 1500.;
const WARHORN_MS: f64 = 2500.;
const TORNADO_WINDUP: f64 = 600.;
const TORNADO_RADIUS: f64 = 240.;
const PULL_TICK_DISTANCE: f64 = 80.;
const SMITE_RANGE: f64 = 450.;
const SWAP_RANGE: f64 = 400.;

#[derive(Clone, Copy, Debug, PartialEq, Serialize, Deserialize)]
enum Buff {
    Stasis,
    Shield { refund_at: f64 },
    BlindRage,
    StillMind,
    Warhorn { count: u8 },
    Cleansed,
}

#[derive(Clone, Copy, Debug, PartialEq, Serialize, Deserialize)]
enum Control {
    Pull { to: Vec2 },
    Taunt { source: PlayerId },
}

#[derive(Clone, Copy, Debug, PartialEq, Serialize, Deserialize)]
#[serde(deny_unknown_fields)]
struct Timed<T> {
    id: PlayerId,
    until: f64,
    effect: T,
}

#[derive(Clone, Copy, Debug, PartialEq, Serialize, Deserialize)]
#[serde(deny_unknown_fields)]
struct Tornado {
    owner: PlayerId,
    at: Vec2,
    max_hp: f64,
    lands: f64,
}

#[derive(Clone, Copy, Debug, PartialEq, Serialize, Deserialize)]
enum ShotKind {
    Hook,
    Bolas,
}
impl ShotKind {
    fn parameters(self) -> (f64, f64, u8) {
        match self {
            Self::Hook => (700., 14., MASK_MOVE),
            Self::Bolas => (550., 12., MASK_SIGHT),
        }
    }
}

#[derive(Clone, Copy, Debug, PartialEq, Serialize, Deserialize)]
#[serde(deny_unknown_fields)]
struct Shot {
    owner: PlayerId,
    at: Vec2,
    end: Vec2,
    origin: Vec2,
    kind: ShotKind,
}

#[derive(Clone, Copy, Debug, PartialEq, Serialize, Deserialize)]
pub enum SpellCueKind {
    Hook,
    Bolas,
    Tornado,
    ForgetMeNotSeed,
}

#[derive(Clone, Copy, Debug, PartialEq, Serialize, Deserialize)]
pub struct SpellCue {
    pub kind: SpellCueKind,
    pub at: Vec2,
    pub lands_at: Option<f64>,
}

#[derive(Clone, Copy, Debug, PartialEq, Serialize, Deserialize)]
#[serde(deny_unknown_fields)]
struct Swap {
    a: PlayerId,
    b: PlayerId,
    slot_a: u8,
    slot_b: u8,
    until: f64,
}

#[derive(Clone, Debug, Default, PartialEq, Serialize, Deserialize)]
#[serde(deny_unknown_fields)]
pub(crate) struct EffectState {
    buffs: Vec<Timed<Buff>>,
    blocked_trails: Vec<Timed<u32>>,
    control: Vec<Timed<Control>>,
    tornadoes: Vec<Tornado>,
    shots: Vec<Shot>,
    swaps: Vec<Swap>,
    seeds: Vec<(PlayerId, Vec2)>,
    deployments: Vec<terrain::Deployment>,
}

impl EffectState {
    pub(super) fn is_empty(&self) -> bool {
        self.buffs.is_empty()
            && self.blocked_trails.is_empty()
            && self.control.is_empty()
            && self.tornadoes.is_empty()
            && self.shots.is_empty()
            && self.swaps.is_empty()
            && self.seeds.is_empty()
            && self.deployments.is_empty()
    }
}

fn state(w: &World) -> Option<&EffectState> {
    Some(&inventory::state(w)?.spell_effects)
}

fn state_mut(w: &mut World) -> &mut EffectState {
    &mut inventory::state_mut(w)
        .expect("active Top25 award has inventory")
        .spell_effects
}

fn buff(w: &World, id: PlayerId, kind: impl Fn(Buff) -> bool) -> bool {
    state(w).is_some_and(|s| {
        s.buffs
            .iter()
            .any(|r| r.id == id && r.until > w.server_time && kind(r.effect))
    })
}

fn set_buff(w: &mut World, id: PlayerId, ms: f64, effect: Buff) {
    let until = w.server_time + ms;
    let buffs = &mut state_mut(w).buffs;
    if let Some(row) = buffs.iter_mut().find(|r| {
        r.id == id && std::mem::discriminant(&r.effect) == std::mem::discriminant(&effect)
    }) {
        *row = Timed { id, until, effect };
    } else {
        buffs.push(Timed { id, until, effect });
    }
}

pub(crate) fn stasis(w: &World, id: PlayerId) -> bool {
    buff(w, id, |b| b == Buff::Stasis)
}

pub(crate) fn blocks_status(w: &World, id: PlayerId, kind: StatusKind) -> bool {
    stasis(w, id)
        || (matches!(kind, StatusKind::Stun | StatusKind::Root)
            && buff(w, id, |b| b == Buff::StillMind))
}

pub(crate) fn attack_rate(w: &World, id: PlayerId) -> f64 {
    if buff(w, id, |b| b == Buff::BlindRage) {
        1.20
    } else {
        1.
    }
}

pub(crate) fn damage_factor(w: &World, id: PlayerId) -> f64 {
    state(w)
        .and_then(|s| {
            s.buffs.iter().find_map(|r| match r.effect {
                Buff::Warhorn { count } if r.id == id && r.until > w.server_time => Some(count),
                _ => None,
            })
        })
        .map_or(1., |n| 1. + 0.05 * f64::from(n))
}

pub(crate) fn shielding(w: &World, id: PlayerId) -> bool {
    buff(w, id, |b| matches!(b, Buff::Shield { .. }))
}

pub(crate) fn block_spell(w: &mut World, id: PlayerId) -> bool {
    let now = w.server_time;
    let Some(i) = state(w).and_then(|s| {
        s.buffs
            .iter()
            .position(|r| r.id == id && r.until > now && matches!(r.effect, Buff::Shield { .. }))
    }) else {
        return false;
    };
    let Buff::Shield { refund_at } = state_mut(w).buffs.remove(i).effect else {
        unreachable!();
    };
    let matches = |s: &inventory::Owned| s.award == Award::SpellShield && s.ready_at == refund_at;
    let refund = |spell: &mut inventory::Owned| {
        spell.ready_at = now;
        spell.charges = spell.max_charges;
    };
    let inventory = inventory::state_mut(w).expect("shield has reward state");
    let equipped = inventory
        .owners
        .iter_mut()
        .filter(|o| o.id == id)
        .flat_map(|o| o.slots.iter_mut().flatten())
        .find(|s| matches(s));
    if let Some(spell) = equipped {
        refund(spell);
    } else if let Some(loot) = inventory
        .loot
        .iter_mut()
        .find(|l| l.owner == id && matches(&l.spell))
    {
        refund(&mut loot.spell);
    }
    true
}

/// A Blaze trail is one spell even though every overlapping tick refreshes its burn.
pub(crate) fn block_trail(w: &mut World, id: PlayerId, field: u32, until: f64) -> bool {
    if stasis(w, id)
        || state(w).is_some_and(|s| {
            s.blocked_trails
                .iter()
                .any(|r| r.id == id && r.effect == field && r.until > w.server_time)
        })
    {
        return true;
    }
    if !block_spell(w, id) {
        return false;
    }
    state_mut(w).blocked_trails.push(Timed {
        id,
        until,
        effect: field,
    });
    true
}

pub(crate) fn taunt(w: &World, id: PlayerId) -> Option<PlayerId> {
    let row = state(w)?
        .control
        .iter()
        .find(|r| r.id == id && r.until > w.server_time)?;
    match row.effect {
        Control::Taunt { source } => w.player(source).filter(|p| p.standing()).map(|_| source),
        Control::Pull { .. } => None,
    }
}

pub(crate) fn pulled(w: &World, id: PlayerId) -> bool {
    state(w).is_some_and(|s| {
        s.control.iter().any(|r| {
            r.id == id && r.until > w.server_time && matches!(r.effect, Control::Pull { .. })
        })
    })
}

fn control(w: &mut World, id: PlayerId, ms: f64, effect: Control) {
    let until = w.server_time + ms;
    let rows = &mut state_mut(w).control;
    rows.retain(|r| r.id != id);
    rows.push(Timed { id, until, effect });
}

pub(crate) fn locked_gear(w: &World, id: PlayerId) -> bool {
    state(w).is_some_and(|s| s.swaps.iter().any(|r| r.a == id || r.b == id))
}

pub(super) fn cast(
    cx: &mut Cx,
    ix: FighterIx,
    award: Award,
    aim: Vec2,
    target: Option<PlayerId>,
) -> bool {
    let from = cx.w.core(ix).pos();
    let id = cx.w.core(ix).id;
    match award {
        Award::WardensCage => return terrain::cage(cx, ix, aim),
        Award::Hook => launch(cx, ix, aim, ShotKind::Hook),
        Award::ShoulderCharge => return charge(cx, ix, aim),
        Award::Tornado => {
            if !placement(cx, from, aim, 500., 0.) {
                return false;
            }
            let row = Tornado {
                owner: id,
                at: aim,
                max_hp: cx.w.core(ix).vitals.max_hp,
                lands: cx.w.server_time + TORNADO_WINDUP,
            };
            state_mut(cx.w).tornadoes.push(row);
        }
        Award::Turret => return terrain::deploy(cx, id, from, aim, DeploymentKind::Turret),
        Award::Warhorn => {
            let mut count: u8 = 0;
            for i in 0..cx.w.fighter_count() {
                let j = FighterIx::of_slot(i);
                let t = cx.w.core(j);
                if enemy(cx.w, id, j)
                    && distance(from, t.pos()) <= 280.
                    && has_line_of_sight(
                        crate::collision::map(cx.k, cx.w),
                        from,
                        t.pos(),
                        MASK_SIGHT,
                    )
                {
                    let victim = t.id;
                    if !defended(cx.w, victim) {
                        stop_attacking(cx, victim);
                        control(cx.w, victim, WARHORN_MS, Control::Taunt { source: id });
                        count = count.saturating_add(1);
                    }
                }
            }
            if count > 0 {
                set_buff(cx.w, id, WARHORN_MS, Buff::Warhorn { count });
            }
        }
        Award::Smite => {
            let Some(victim) = target.filter(|t| focused(cx, ix, *t, SMITE_RANGE, false)) else {
                return false;
            };
            if !defended(cx.w, victim) {
                hit(cx, id, victim, 220., Some(SMITE_SOURCE), true);
            }
        }
        Award::SpellShield => {
            let refund_at = cx.w.server_time + award.cooldown_ms();
            set_buff(cx.w, id, SHIELD_MS, Buff::Shield { refund_at });
        }
        Award::ForgetMeNotSeed => {
            if let Some(i) = state(cx.w)
                .unwrap()
                .seeds
                .iter()
                .position(|(owner, _)| *owner == id)
            {
                let to = state(cx.w).unwrap().seeds[i].1;
                if !recall(cx, ix, from, to) {
                    return false;
                }
                state_mut(cx.w).seeds.remove(i);
            } else {
                state_mut(cx.w).seeds.push((id, from));
            }
        }
        Award::SwapStick => {
            let Some(victim) = target.filter(|t| focused(cx, ix, *t, SWAP_RANGE, true)) else {
                return false;
            };
            if locked_gear(cx.w, id) || locked_gear(cx.w, victim) {
                return false;
            }
            let a = cx.w.player(id).unwrap();
            let b = cx.w.player(victim).unwrap();
            if a.active_weapon() == b.active_weapon() {
                return false;
            }
            let swap = Swap {
                a: id,
                b: victim,
                slot_a: a.weapon.slot,
                slot_b: b.weapon.slot,
                until: cx.w.server_time + 10_000.,
            };
            if !defended(cx.w, victim) {
                swap_slots(cx.w, swap);
                state_mut(cx.w).swaps.push(swap);
            }
        }
        Award::LanternWard => {
            return terrain::deploy(cx, id, from, aim, DeploymentKind::LanternWard);
        }
        Award::Cleanse => cleanse(cx, id),
        Award::ScoutFalcon => return terrain::falcon(cx, ix, aim),
        Award::Stasis => {
            stop_attacking(cx, id);
            status::cancel_cast(cx.k, cx.w, cx.ev, id, rt::CastEndReason::Cancelled, false);
            let p = cx.w.core_mut(ix);
            p.motion.path.clear();
            p.motion.slip = None;
            p.weapon.targeting = Default::default();
            set_buff(cx.w, id, STASIS_MS, Buff::Stasis);
        }
        Award::Bolas => launch(cx, ix, aim, ShotKind::Bolas),
        Award::BlindRage => {
            set_buff(cx.w, id, 6000., Buff::BlindRage);
            crate::systems::items::refold_modifiers_with(cx.k, cx.w, ix.slot(), cx.stats);
        }
        Award::StillMind => {
            for kind in [StatusKind::Root, StatusKind::Stun] {
                status::operations::remove_discard_with(
                    cx.k,
                    cx.w,
                    id,
                    Selector::Kind(kind),
                    cx.stats,
                );
            }
            set_buff(cx.w, id, 3000., Buff::StillMind);
        }
        _ => return false,
    }
    true
}

pub(super) fn seed_planted(w: &World, id: PlayerId) -> bool {
    state(w).is_some_and(|s| s.seeds.iter().any(|(owner, _)| *owner == id))
}

fn cleanse(cx: &mut Cx, id: PlayerId) {
    for kind in [StatusKind::Root, StatusKind::Stun] {
        status::operations::remove_discard_with(cx.k, cx.w, id, Selector::Kind(kind), cx.stats);
    }
    let now = cx.w.server_time;
    if let Some(ix) = cx.w.fighter_idx(id) {
        cx.w.core_mut(ix).weapon.root_until = now;
    }
    state_mut(cx.w).control.retain(|r| r.id != id);
    set_buff(cx.w, id, TORNADO_WINDUP, Buff::Cleansed);
}

fn stop_attacking(cx: &mut Cx, id: PlayerId) {
    if let Some(ix) = cx.w.fighter_idx(id) {
        let now = cx.w.server_time;
        let p = cx.w.core_mut(ix);
        crate::systems::weapons::cancel_action(p, now);
        p.weapon.held = false;
        p.weapon.buffered_until = 0.;
    }
}

fn apply_status(cx: &mut Cx, by: PlayerId, victim: PlayerId, kind: StatusKind, ms: f64) {
    let Some(name) = cx.w.roster.label(by).cloned() else {
        return;
    };
    status::operations::apply_with(
        cx.k,
        cx.w,
        cx.ev,
        victim,
        StatusApply {
            kind,
            ms,
            source: rt::StatusSource {
                id: Some(by),
                name,
                owner_id: None,
            },
            ability: None,
            tag: None,
            value: None,
            add: None,
            data: None,
        },
        false,
        cx.stats,
    );
}

fn defended(w: &mut World, id: PlayerId) -> bool {
    stasis(w, id) || block_spell(w, id)
}

fn swap_slots(w: &mut World, swap: Swap) {
    let (Some(a), Some(b)) = (w.idx(swap.a), w.idx(swap.b)) else {
        return;
    };
    let get = |p: &mut crate::state::Player, slot| {
        if slot == 0 {
            p.weapon.weapon0.take()
        } else {
            p.weapon.weapon1.take()
        }
    };
    let left = get(&mut w.players[a], swap.slot_a);
    let right = get(&mut w.players[b], swap.slot_b);
    for (i, s, weapon) in [(a, swap.slot_a, right), (b, swap.slot_b, left)] {
        let p = &mut w.players[i];
        if s == 0 {
            p.weapon.weapon0 = weapon;
        } else {
            p.weapon.weapon1 = weapon;
        }
        crate::systems::items::weapon_changed(&mut p.core, w.server_time);
    }
}

pub(crate) fn restore_gear(w: &mut World, id: PlayerId) {
    if let Some(i) = state(w).and_then(|s| s.swaps.iter().position(|r| r.a == id || r.b == id)) {
        let row = state_mut(w).swaps.remove(i);
        swap_slots(w, row);
    }
}

fn recall(cx: &mut Cx, ix: FighterIx, from: Vec2, to: Vec2) -> bool {
    let map = terrain::body_map(cx.k, cx.w);
    let p = cx.w.core(ix);
    if !p.foot().clear(map, to, MASK_MOVE) {
        return false;
    }
    if cx.w.rt.gates_locked {
        let component = |p: Vec2| {
            use crate::map::world_to_tile;
            let (x, y) = (world_to_tile(p.x), world_to_tile(p.y));
            map.tile(x, y)
                .map(|_| map.comp(y as usize * map.w as usize + x as usize))
        };
        if component(from) != component(to) {
            return false;
        }
    }
    let id = p.id;
    cx.w.core_mut(ix).motion.path.clear();
    displace::apply(cx.k, cx.w, cx.pf, id, to);
    true
}

fn charge(cx: &mut Cx, ix: FighterIx, aim: Vec2) -> bool {
    let p = cx.w.core(ix);
    let (id, from) = (p.id, p.pos());
    let end = aim_end(cx, ix, aim, 220.);
    let end = clipped(cx, p.foot(), from, end);
    if distance(from, end) < 1. {
        return false;
    }
    let contact = first_hit(cx, id, from, end, 21., MASK_MOVE);
    let to = if let Some((victim, u)) = contact {
        let at = cx.w.core(cx.w.fighter_idx(victim).unwrap()).pos();
        if !defended(cx.w, victim) {
            let d = distance(from, end);
            let away = Vec2::new(
                at.x + (end.x - from.x) * 120. / d,
                at.y + (end.y - from.y) * 120. / d,
            );
            push(cx, victim, away);
            apply_status(cx, id, victim, StatusKind::Stun, 500.);
        }
        between(from, end, u)
    } else {
        end
    };
    cx.w.core_mut(ix).motion.path.clear();
    displace::apply(cx.k, cx.w, cx.pf, id, to);
    true
}

fn enemy(w: &World, id: PlayerId, j: FighterIx) -> bool {
    w.standing(j) && crate::systems::plague_zombies::hostile(w, id, w.core(j).id)
}

fn focused(cx: &Cx, ix: FighterIx, target: PlayerId, range: f64, player: bool) -> bool {
    cx.w.fighter_idx(target).is_some_and(|j| {
        cx.w.is_player(j) == player && crate::systems::abilities::unit_valid(cx, ix, target, range)
    })
}

fn first_hit(
    cx: &Cx,
    owner: PlayerId,
    from: Vec2,
    to: Vec2,
    width: f64,
    mask: u8,
) -> Option<(PlayerId, f64)> {
    let map = terrain::body_map(cx.k, cx.w);
    let mut first: Option<(PlayerId, f64)> = None;
    for i in 0..cx.w.fighter_count() {
        let j = FighterIx::of_slot(i);
        if !enemy(cx.w, owner, j) {
            continue;
        }
        let t = cx.w.core(j);
        let Some(u) = crate::geometry::segment_box_entry(
            from,
            to,
            t.hull()
                .body_box(t.pos())
                .unwrap_or_else(|| crate::geometry::Aabb::centred(t.pos(), 16., 16.))
                .grown(width),
        ) else {
            continue;
        };
        if first.is_none_or(|(id, f)| (u, t.id) < (f, id))
            && has_line_of_sight(map, from, between(from, to, u), mask)
            && has_line_of_sight(map, from, t.pos(), mask)
        {
            first = Some((t.id, u));
        }
    }
    first
}

fn placement(cx: &Cx, from: Vec2, at: Vec2, range: f64, radius: f64) -> bool {
    let map = terrain::body_map(cx.k, cx.w);
    distance(from, at) <= range
        && crate::geometry::circle_clear(map, at, radius, MASK_MOVE)
        && has_line_of_sight(map, from, at, MASK_SIGHT)
}

fn push(cx: &mut Cx, victim: PlayerId, to: Vec2) {
    let Some(ix) = cx.w.fighter_idx(victim) else {
        return;
    };
    let p = cx.w.core(ix);
    let from = p.pos();
    let to = clipped(cx, p.foot(), from, to);
    if distance(from, to) >= 0.5 {
        displace::apply(cx.k, cx.w, cx.pf, victim, to);
    }
}

fn clipped(cx: &Cx, foot: Footprint, from: Vec2, to: Vec2) -> Vec2 {
    let map = terrain::body_map(cx.k, cx.w);
    clip(from, to, |p| foot.segment_clear(map, from, p, MASK_MOVE))
}

fn clip(from: Vec2, to: Vec2, clear: impl Fn(Vec2) -> bool) -> Vec2 {
    if clear(to) {
        return to;
    }
    let (mut lo, mut hi) = (0., 1.);
    for _ in 0..12 {
        let mid = (lo + hi) * 0.5;
        if clear(between(from, to, mid)) {
            lo = mid;
        } else {
            hi = mid;
        }
    }
    between(from, to, lo)
}

fn distance(a: Vec2, b: Vec2) -> f64 {
    hypot(a.x - b.x, a.y - b.y)
}

fn between(a: Vec2, b: Vec2, u: f64) -> Vec2 {
    Vec2::new(a.x + (b.x - a.x) * u, a.y + (b.y - a.y) * u)
}

fn towards(from: Vec2, to: Vec2, limit: f64) -> Vec2 {
    let d = distance(from, to);
    if d <= limit {
        to
    } else {
        between(from, to, limit / d)
    }
}

fn aim_end(cx: &Cx, ix: FighterIx, aim: Vec2, range: f64) -> Vec2 {
    let p = cx.w.core(ix);
    let from = p.pos();
    let d = distance(from, aim);
    if d < 1. {
        Vec2::new(
            from.x + range * cos(p.body.facing),
            from.y + range * sin(p.body.facing),
        )
    } else {
        towards(from, aim, range)
    }
}

fn launch(cx: &mut Cx, ix: FighterIx, aim: Vec2, kind: ShotKind) {
    let (range, _, _) = kind.parameters();
    let p = cx.w.core(ix);
    let at = p.pos();
    let end = aim_end(cx, ix, aim, range);
    let row = Shot {
        owner: p.id,
        at,
        end,
        origin: at,
        kind,
    };
    state_mut(cx.w).shots.push(row);
}

fn shots(cx: &mut Cx) {
    let mut i = 0;
    while i < state(cx.w).unwrap().shots.len() {
        let shot = state(cx.w).unwrap().shots[i];
        let (_, width, mask) = shot.kind.parameters();
        let goal = towards(shot.at, shot.end, 175.);
        let map = terrain::body_map(cx.k, cx.w);
        let end = clip(shot.at, goal, |at| {
            has_line_of_sight(map, shot.at, at, mask)
        });
        if let Some((victim, _)) = first_hit(cx, shot.owner, shot.at, end, width, mask) {
            if !defended(cx.w, victim) {
                match shot.kind {
                    ShotKind::Hook => {
                        let at = cx.w.core(cx.w.fighter_idx(victim).unwrap()).pos();
                        let from = cx.w.player(shot.owner).map_or(shot.origin, |p| p.pos());
                        let to = towards(at, from, (distance(at, from) - 42.).max(0.));
                        control(cx.w, victim, 600., Control::Pull { to });
                    }
                    ShotKind::Bolas => {
                        apply_status(cx, shot.owner, victim, StatusKind::Root, 1500.)
                    }
                }
            }
            state_mut(cx.w).shots.remove(i);
        } else if end != goal || goal == shot.end {
            state_mut(cx.w).shots.remove(i);
        } else {
            state_mut(cx.w).shots[i].at = goal;
            i += 1;
        }
    }
}

pub(crate) fn visible_cues(sim: &crate::Sim, viewer: PlayerId, out: &mut Vec<SpellCue>) {
    out.clear();
    let Some(s) = state(&sim.w) else { return };
    if let Some(&(_, at)) = s.seeds.iter().find(|(owner, _)| *owner == viewer) {
        out.push(SpellCue {
            kind: SpellCueKind::ForgetMeNotSeed,
            at,
            lands_at: None,
        });
    }
    for row in &s.tornadoes {
        if row.owner == viewer || sim.can_see_point(viewer, row.at) {
            out.push(SpellCue {
                kind: SpellCueKind::Tornado,
                at: row.at,
                lands_at: Some(row.lands),
            });
        }
    }
    for row in &s.shots {
        if row.owner == viewer || sim.can_see_point(viewer, row.at) {
            let kind = match row.kind {
                ShotKind::Hook => SpellCueKind::Hook,
                ShotKind::Bolas => SpellCueKind::Bolas,
            };
            out.push(SpellCue {
                kind,
                at: row.at,
                lands_at: None,
            });
        }
    }
}

// These immutable source labels travel through the existing ledger's source snapshot.
const SMITE_SOURCE: &str = "Smite";
const REMOTE_TURRET_SOURCE: &str = "Remote Turret";

fn hit(
    cx: &mut Cx,
    owner: PlayerId,
    victim: PlayerId,
    raw: f64,
    label: Option<&str>,
    true_damage: bool,
) {
    let source = label.map(|name| rt::StatusSource {
        id: Some(owner),
        name: name.into(),
        owner_id: Some(owner),
    });
    ledger::record_hit(
        cx.w,
        &mut cx.s.ledger,
        ledger::Entry {
            terrain: false,
            dot: false,
            ability: None,
            arena: false,
            source,
            critical: false,
            critical_mult: None,
            sig: None,
            execute: false,
            victim,
            by: Some(owner),
            packet: Packet {
                raw,
                phys: 0.,
                magic: f64::from(u8::from(!true_damage)),
                true_: f64::from(u8::from(true_damage)),
            },
            weapon: None,
            stagger: false,
            projectile: None,
            scale: 1.,
            derived: false,
            passive: false,
        },
    );
}

pub(crate) fn mob_impact_label(entry: &ledger::Entry) -> Option<std::sync::Arc<str>> {
    let source = entry.source.as_ref()?;
    // Only this lane's direct spell hits carry an explicit self owner on a source snapshot.
    // Ordinary damage and status ticks can have the same text in a player's display name.
    (!entry.is_dot()
        && !entry.terrain
        && !entry.arena
        && entry.weapon.is_none()
        && entry.ability.is_none()
        && source.owner_id.is_some()
        && source.owner_id == entry.by)
        .then(|| source.name.clone())
}

pub(crate) fn smite_hit(hit: &ledger::Hit) -> bool {
    hit.name.as_deref() == Some(SMITE_SOURCE)
}

pub(crate) fn remote_turret_hit(hit: &ledger::Hit) -> bool {
    hit.name.as_deref() == Some(REMOTE_TURRET_SOURCE)
}

pub(crate) fn update(cx: &mut Cx) {
    let Some(s) = state(cx.w) else { return };
    if s.is_empty() {
        return;
    }
    let now = cx.w.server_time;
    for i in (0..state(cx.w).unwrap().buffs.len()).rev() {
        let r = state(cx.w).unwrap().buffs[i];
        if r.until <= now {
            state_mut(cx.w).buffs.remove(i);
            if r.effect == Buff::BlindRage {
                if let Some(ix) = cx.w.fighter_idx(r.id) {
                    crate::systems::items::refold_modifiers_with(cx.k, cx.w, ix.slot(), cx.stats);
                }
            }
        }
    }
    for i in (0..state(cx.w).unwrap().swaps.len()).rev() {
        let row = state(cx.w).unwrap().swaps[i];
        if row.until <= now {
            restore_gear(cx.w, row.a);
        }
    }
    state_mut(cx.w).blocked_trails.retain(|r| r.until > now);
    shots(cx);
    let mut i = 0;
    while i < state(cx.w).unwrap().tornadoes.len() {
        let row = state(cx.w).unwrap().tornadoes[i];
        if row.lands > now {
            i += 1;
            continue;
        }
        state_mut(cx.w).tornadoes.remove(i);
        tornado(cx, row);
    }
    let mut i = 0;
    while i < state(cx.w).unwrap().control.len() {
        let row = state(cx.w).unwrap().control[i];
        let standing = cx.w.fighter_idx(row.id).is_some_and(|j| cx.w.standing(j));
        if row.until <= now || !standing {
            state_mut(cx.w).control.remove(i);
            continue;
        }
        if let Control::Pull { to } = row.effect {
            let ix = cx.w.fighter_idx(row.id).unwrap();
            let from = cx.w.core(ix).pos();
            if !stasis(cx.w, row.id) {
                push(cx, row.id, towards(from, to, PULL_TICK_DISTANCE));
            }
            let after = cx.w.core(ix).pos();
            if distance(after, to) < 1. || distance(from, after) < 0.5 {
                state_mut(cx.w).control.remove(i);
                continue;
            }
        }
        i += 1;
    }
    terrain::update(cx);
}

fn tornado(cx: &mut Cx, row: Tornado) {
    for i in 0..cx.w.fighter_count() {
        let j = FighterIx::of_slot(i);
        let t = cx.w.core(j);
        let (victim, at) = (t.id, t.pos());
        let pull = 180. * (row.max_hp / t.vitals.max_hp - 1.).clamp(0., 1.);
        if pull > 0.
            && enemy(cx.w, row.owner, j)
            && distance(at, row.at) <= TORNADO_RADIUS
            && !buff(cx.w, victim, |b| b == Buff::Cleansed)
            && has_line_of_sight(terrain::body_map(cx.k, cx.w), at, row.at, MASK_MOVE)
            && !defended(cx.w, victim)
        {
            control(
                cx.w,
                victim,
                300.,
                Control::Pull {
                    to: towards(at, row.at, pull),
                },
            );
        }
    }
    terrain::pull_turrets(cx, row);
}

pub(crate) fn departed(w: &mut World, id: PlayerId) {
    if state(w).is_none() {
        return;
    }
    restore_gear(w, id);
    let s = state_mut(w);
    s.buffs.retain(|r| r.id != id);
    s.blocked_trails.retain(|r| r.id != id);
    s.control
        .retain(|r| r.id != id && r.effect != Control::Taunt { source: id });
    s.seeds.retain(|r| r.0 != id);
    s.tornadoes.retain(|r| r.owner != id);
    s.shots.retain(|r| r.owner != id);
    s.deployments.retain(|r| r.owner != id);
}

pub(crate) fn walk_taunted(
    k: &crate::statics::MatchStatic,
    w: &mut World,
    ix: FighterIx,
    source: PlayerId,
    dt: f64,
    mult: f64,
) {
    let p = w.core(ix);
    let from = p.pos();
    let target = w.player(source).expect("active taunt source").pos();
    let stop = (distance(from, target) - 42.).max(0.);
    let to = towards(
        from,
        target,
        (crate::constants::PLAYER_SPEED * dt * mult).min(stop),
    );
    let map = terrain::body_map(k, w);
    let to = clip(from, to, |pos| {
        p.foot().segment_clear(map, from, pos, MASK_MOVE)
    });
    if to != from {
        let p = w.core_mut(ix);
        p.body.x = to.x;
        p.body.y = to.y;
        p.body.facing = crate::math::atan2(to.y - from.y, to.x - from.x);
        p.motion.slip = None;
    }
}

pub(crate) fn forced_motion(w: &World, id: PlayerId) -> bool {
    stasis(w, id) || pulled(w, id) || taunt(w, id).is_some()
}
