//! Combat adapter (P-RAID-C): fight, orbwalk and retreat for the current target.
//!
//! P-RAID-REACH-1: the one rule both fight executors (`farm.rs`, `elite.rs`) ask before a ranged seat fights from where
//! it stands. P-RAID-REACH-1-fix: the rule is the sim's own give-up test (margin 0), and a seat that may not fight from
//! where it stands walks to the nearest place it may fight from (`Reach`), not onto the mob.
use crate::bots::paths::RoomPathService;
use crate::bots::raid::exec::farm::{dist, lock_range, DEFEND_U};
use crate::bots::raid::exec::travel::step_toward;
use crate::bots::raid::goal::Cx;
use crate::bots::raid::percept::MobSeen;
use rh_sim::constants::{PLAYER_RADIUS, TILE};
use rh_sim::mapdata::patience::leash_ok;
use rh_sim::mapdata::{CampDef, LayerData};
use rh_sim::{Vec2, WeaponId};

/// How far beyond its band a mob that stays inside its leash reaches a seat, for `may_fight_from`. 0: the seat must
/// stand inside the band itself (see `may_fight_from`).
const MOB_REACH_U: f64 = 0.;
/// A seat walking to its fight point that has not come this much nearer to the band...
const PROGRESS_U: f64 = 8.;
/// ...for this long can walk no further: it fights from where it stands.
const STUCK_MS: f64 = 1_500.;
/// A fight point is chosen this much inside the weapon's lock range of the mob, so the seat still locks on arrival.
const LOCK_SLACK_U: f64 = TILE / 2.;

/// The homes the target's leash is measured from: the camp's elite spot for an elite (`elite`, when the camp has a valid
/// one), else every spot of the camp (the union, as `patience::patience_plane`).
fn homes(def: &CampDef, elite: bool) -> impl Iterator<Item = Vec2> + '_ {
    let elite_home = usize::try_from(def.elite_spot)
        .ok()
        .filter(|_| elite)
        .and_then(|i| def.spots.get(i));
    let all = elite_home.is_none().then_some(def.spots.iter());
    elite_home
        .into_iter()
        .chain(all.into_iter().flatten())
        .map(|s| Vec2::new(s.x, s.y))
}

/// The camp's layer data and definition, when the map has layers, the camp exists and has spots.
fn camp_of(paths: &RoomPathService, camp: u32) -> Option<(&LayerData, &CampDef)> {
    let data = paths.collision_map().layers()?.data();
    let def = data.camps.get(camp as usize)?;
    (!def.spots.is_empty()).then_some((data, def))
}

/// Is `at` inside the leash of a target of camp `camp` resting at its home (`elite` as in `may_fight_from`), by the
/// sim's give-up test (`leash_ok`, margin 0: a chasing mob gives up when its own position fails it, `mob_ai.rs` `fight`)?
/// `None` when nothing is known (no layers, an unknown camp, a camp without spots).
pub(crate) fn in_leash(paths: &RoomPathService, camp: u32, elite: bool, at: Vec2) -> Option<bool> {
    let (data, def) = camp_of(paths, camp)?;
    Some(homes(def, elite).any(|h| leash_ok(data, camp, h, at, 0.)))
}

/// May a seat standing at `from` fight a target of camp `camp` (a 0-based index into the map's camps) from there? Yes
/// where the target, standing inside its leash, can reach the seat: the sim's chasing mob gives up when its OWN position
/// fails `rh_sim::mapdata::patience::leash_ok` at margin 0 (`MOB_LEASH_MARGIN` is for its retreat and spacing steps
/// only), so the seat stands inside that band (`in_leash`), or within `MOB_REACH_U` of a point of it. `elite` selects
/// the target's home: the camp's elite spot (`CampDef::elite_spot`) for an elite, any spot of the camp for a normal
/// member. A map without layers, a camp without spots or an unknown camp answers `true` (nothing known, nothing changed).
///
/// An elite whose camp has no valid elite spot reads the union over the camp's spots, as a normal member does.
///
/// `MOB_REACH_U` is 0: a melee mob stops chasing only within `reach * NPC_MELEE_FAR_SHARE` of the seat
/// (`combat_policy::decide`), so a mob at the band's edge facing a seat beyond it steps out of its leash and gives up;
/// and the live oracle (`tests/raid_reach.rs`) wants the seat's hits landed from inside the band.
pub fn may_fight_from(paths: &RoomPathService, camp: u32, elite: bool, from: Vec2) -> bool {
    let Some((data, def)) = camp_of(paths, camp) else {
        return true;
    };
    let inside = |p: Vec2| homes(def, elite).any(|h| leash_ok(data, camp, h, p, 0.));
    if inside(from) {
        return true;
    }
    if MOB_REACH_U <= 0. {
        return false;
    }
    // Within reach of a band cell's centre (the mob stands anywhere in its cell).
    let cells = (MOB_REACH_U / TILE).ceil() as i64;
    let (cx, cy) = (
        rh_sim::map::world_to_tile(from.x),
        rh_sim::map::world_to_tile(from.y),
    );
    (cy - cells..=cy + cells).any(|ty| {
        (cx - cells..=cx + cells).any(|tx| {
            let q = Vec2::new((tx as f64 + 0.5) * TILE, (ty as f64 + 0.5) * TILE);
            dist(q, from) <= MOB_REACH_U && inside(q)
        })
    })
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

/// What a fight executor does about its target this think (`Reach::engage`).
#[derive(Clone, Copy, Debug, PartialEq)]
pub(crate) enum Engage {
    /// Lock the target from where the seat stands.
    Lock,
    /// Click here (toward the target, or toward the nearest place the seat may fight it from).
    Move(Vec2),
    /// Nothing: out of lock range with no known way to the target.
    Stand,
}

/// The per-seat memory of the approach (P-RAID-REACH-1-fix): the points each camp's target may be fought from (cached
/// per camp and home kind: the layers are static for a match; the executor's `reset` clears it with the match), and the
/// progress of the walk to them.
#[derive(Default)]
pub(crate) struct Reach {
    /// `(camp, elite, points)`: the floor cells' centres the seat may fight that camp's target from
    /// (`may_fight_from`), within the leash cap of a home.
    points: Vec<(u32, bool, Vec<Vec2>)>,
    /// The walk to a fight point: the nearest the seat has come to one, the server ms it last came nearer, and the
    /// server ms of the last think of the walk (a walk not thought about for `STUCK_MS` starts again).
    progress: Option<(f64, f64, f64)>,
}

impl Reach {
    /// The cached fight points of camp `camp` (`elite` as in `may_fight_from`); empty when nothing is known.
    fn points(&mut self, paths: &RoomPathService, camp: u32, elite: bool) -> &[Vec2] {
        let i = match self
            .points
            .iter()
            .position(|(c, e, _)| *c == camp && *e == elite)
        {
            Some(i) => i,
            None => {
                let found = Self::scan(paths, camp, elite);
                self.points.push((camp, elite, found));
                self.points.len() - 1
            }
        };
        &self.points[i].2
    }

    /// Every floor cell centre within the leash cap (plus `MOB_REACH_U`) of one of the target's homes where
    /// `may_fight_from` says yes; once per camp and home kind.
    fn scan(paths: &RoomPathService, camp: u32, elite: bool) -> Vec<Vec2> {
        let Some((data, def)) = camp_of(paths, camp) else {
            return Vec::new();
        };
        let reach = rh_sim::constants::MOB_LEASH_CAP.max(rh_sim::constants::MOB_LEASH_RADIUS)
            + MOB_REACH_U
            + TILE;
        let map = paths.collision_map();
        let (mut lo_x, mut lo_y, mut hi_x, mut hi_y) = (i64::MAX, i64::MAX, i64::MIN, i64::MIN);
        for h in homes(def, elite) {
            lo_x = lo_x.min(rh_sim::map::world_to_tile(h.x - reach));
            lo_y = lo_y.min(rh_sim::map::world_to_tile(h.y - reach));
            hi_x = hi_x.max(rh_sim::map::world_to_tile(h.x + reach));
            hi_y = hi_y.max(rh_sim::map::world_to_tile(h.y + reach));
        }
        let (lo_x, lo_y) = (lo_x.max(0), lo_y.max(0));
        let (hi_x, hi_y) = (
            hi_x.min(i64::from(data.w) - 1),
            hi_y.min(i64::from(data.h) - 1),
        );
        let mut out = Vec::new();
        for ty in lo_y..=hi_y {
            for tx in lo_x..=hi_x {
                if !map.is_floor(tx, ty) {
                    continue;
                }
                let q = Vec2::new((tx as f64 + 0.5) * TILE, (ty as f64 + 0.5) * TILE);
                if may_fight_from(paths, camp, elite, q) {
                    out.push(q);
                }
            }
        }
        out
    }

    /// The fight point nearest `from` (ties by scan order) within `range` of the target at `mob_at`.
    fn nearest(
        &mut self,
        paths: &RoomPathService,
        camp: u32,
        elite: bool,
        from: Vec2,
        mob_at: Vec2,
        range: f64,
    ) -> Option<Vec2> {
        self.points(paths, camp, elite)
            .iter()
            .filter(|q| dist(**q, mob_at) <= range)
            .min_by(|a, b| dist(**a, from).total_cmp(&dist(**b, from)))
            .copied()
    }

    /// One think of the approach to `mob` (a target of camp `camp`, `elite` as in `may_fight_from`) with `in_hand`:
    /// beyond lock range, walk to the mob; in lock range, lock when `locks_from_here`; else walk to the nearest point the
    /// seat may fight from, and lock from where it stands when there is none, no way to it, or the walk stops coming
    /// nearer (it never stands in lock range of an idle target, able to walk no further, without locking).
    pub(crate) fn engage(
        &mut self,
        cx: &mut Cx<'_>,
        camp: u32,
        elite: bool,
        mob: &MobSeen,
        in_hand: Option<WeaponId>,
    ) -> Engage {
        let at = cx.p.me.at;
        let range = lock_range(in_hand);
        if cx.p.dist(mob.at) > range {
            self.progress = None;
            return step_toward(cx.paths, at, mob.at).map_or(Engage::Stand, Engage::Move);
        }
        if locks_from_here(cx, camp, elite, mob, in_hand) {
            self.progress = None;
            return Engage::Lock;
        }
        let Some(to) = self.nearest(
            cx.paths,
            camp,
            elite,
            at,
            mob.at,
            (range - LOCK_SLACK_U).max(0.),
        ) else {
            self.progress = None;
            return Engage::Lock;
        };
        let now = cx.p.now;
        let d = dist(at, to);
        let (best, since) = match self.progress {
            Some((best, since, last)) if now - last < STUCK_MS && d > best - PROGRESS_U => {
                (best, since)
            }
            _ => (d, now),
        };
        self.progress = Some((best, since, now));
        if d <= 2. * PLAYER_RADIUS || now - since >= STUCK_MS {
            return Engage::Lock;
        }
        step_toward(cx.paths, at, to).map_or(Engage::Lock, Engage::Move)
    }
}
