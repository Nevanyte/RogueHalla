//! P-RAID-LOS-1: a ranged seat locks, and picks its fight point, only with a clear shot to the target. Scene: lab seed 9,
//! camp 44, a Wand seat in a pocket from which a member of the camp is in range and in the band but the shot meets a wall
//! (the live oracle `tests/raid_los.rs` watched the seat fire 84 bolts into it).
use super::*;
use crate::bots::paths::{PathBudget, RoomPathService};
use crate::bots::perception::LootFacts;
use crate::bots::planning::PlanMap;
use crate::bots::raid::exec::farm::lock_range;
use crate::bots::raid::goal::{GoalKind, RaidGoal, Status, Target};
use crate::bots::raid::percept::{CampSeen, Me, MobClass, RaidPercept};
use crate::bots::raid::values::Prices;
use crate::bots::random::Rng;
use crate::bots::types::{HudView, Percept, PersonaId, SelfView, TierId};
use rh_sim::{Arena, FactionId, Secrets};
use std::sync::Arc;

const CAMP: u32 = 44;
const SEAT: (f64, f64) = (1674.5, 1848.4);
const MOB: (f64, f64) = (1430.875, 1558.875);

fn lab_arena(seed: u64) -> Arc<Arena> {
    use rh_sim::mapgen::{coliseum, pipeline};
    let layout = coliseum::fixed_layout();
    let options = pipeline::Options {
        decor: false,
        ..Default::default()
    };
    let map = pipeline::Pipeline::new(&layout)
        .certified_styled(seed as i64, options)
        .expect("generated")
        .expect("accepted");
    let export = rh_sim::mapdata::MapExport {
        format: "rh-map/2".into(),
        legacy_map_hash_u32: u32::from_str_radix(&rh_sim::mapgen::map_hash::map_hash(&map), 16)
            .expect("a hash"),
        map,
    };
    let arena = export
        .validate()
        .expect("admitted")
        .into_arena()
        .expect("an arena");
    rh_sim::quests::board::map_admission::ensure(Arc::new(arena), Secrets::from_seed(seed))
        .expect("a board-admitted map")
}

fn member(at: Vec2) -> MobSeen {
    MobSeen {
        key: "m1".into(),
        at,
        hp_share: 1.,
        alive: true,
        class: MobClass::Regional,
        mob_type: None,
        weapon: None,
        faction: None,
        camp: Some(CAMP),
        engaged: false,
    }
}

struct Scene {
    plan: PlanMap,
    paths: RoomPathService,
    p: RaidPercept,
}

fn scene(arena: &Arena, at: Vec2, weapon: WeaponId, mob: &MobSeen) -> Scene {
    let plan = PlanMap::from_arena(arena);
    let paths = RoomPathService::new(&arena.map, PathBudget::default());
    let camps = plan
        .camps
        .iter()
        .enumerate()
        .map(|(i, c)| CampSeen {
            index: i as u32,
            at: Vec2::new(c.x, c.y),
            zone: c.zone,
            elite_spot: c.elite_spot,
            mob_type: 0,
            reward_stat: None,
            elite: None,
            alive: u32::from(i as u32 == CAMP),
            state: 0,
            cleared_at: 0.,
            respawn_at: 0.,
            ward: None,
        })
        .collect();
    let base = Percept {
        now: 5_000.,
        hud: HudView {
            phase: "active".into(),
            gates_locked: false,
        },
        self_view: SelfView {
            x: at.x,
            y: at.y,
            status: "alive".into(),
            hp: 100.,
            max_hp: 100.,
            ..SelfView::default()
        },
        ..Percept::default()
    };
    let p = RaidPercept {
        now: 5_000.,
        tick: 100,
        base,
        facts: LootFacts::default(),
        toasts: Vec::new(),
        self_key: "p1".into(),
        me: Me {
            at,
            hp: 100.,
            max_hp: 100.,
            alive: true,
            faction: Some(FactionId::Holy),
            weapons: [Some(weapon), None],
            slot: 0,
            effects: 0,
            ready_at: 0.,
            potion_small: 0,
            potion_large: 0,
            potions_healing: 0,
            healing_until: None,
        },
        active: true,
        camps,
        mobs: vec![mob.clone()],
        givers: Vec::new(),
        players: Vec::new(),
        quests: None,
        trade: None,
        fields: Vec::new(),
        build: None,
        summary: None,
        arena: None,
        hit_at: None,
    };
    Scene { plan, paths, p }
}

fn locks(s: &mut Scene, m: &MobSeen) -> bool {
    let goal = RaidGoal::new(GoalKind::Farm, Target::Camp(CAMP));
    let mut rng = Rng::new(1);
    let prices = Prices::LOCAL;
    let cx = Cx {
        p: &s.p,
        paths: &mut s.paths,
        plan: &s.plan,
        knowledge: None,
        tier: TierId::Normal,
        persona: PersonaId::Farmer,
        prices: &prices,
        still_since: 0.,
        goal: &goal,
        status: Status::Running,
        rng: &mut rng,
    };
    locks_from_here(&cx, CAMP, false, m, Some(WeaponId::Wand))
}

#[test]
fn a_mob_in_range_and_in_the_band_behind_a_wall_is_not_locked_from_here() {
    let arena = lab_arena(9);
    let m = member(Vec2::new(MOB.0, MOB.1));
    let mut s = scene(&arena, Vec2::new(SEAT.0, SEAT.1), WeaponId::Wand, &m);
    let at = Vec2::new(SEAT.0, SEAT.1);
    assert!(dist(at, m.at) <= lock_range(Some(WeaponId::Wand)));
    assert!(may_fight_from(&s.paths, CAMP, false, at), "in the band");
    assert!(!shot_clear(&s.paths, at, m.at), "the shot meets a wall");
    assert!(!locks(&mut s, &m));
}

#[test]
fn the_same_mob_with_a_clear_line_is_locked() {
    let arena = lab_arena(9);
    let m = member(Vec2::new(MOB.0, MOB.1));
    let mut s = scene(&arena, Vec2::new(SEAT.0, SEAT.1), WeaponId::Wand, &m);
    let range = lock_range(Some(WeaponId::Wand));
    let mut reach = Reach::default();
    let to = reach
        .nearest(
            &s.paths,
            CAMP,
            false,
            Vec2::new(SEAT.0, SEAT.1),
            m.at,
            range - 1.,
        )
        .expect("a fight point with a clear shot");
    s.p.me.at = to;
    assert!(shot_clear_around(&s.paths, to, m.at));
    assert!(locks(&mut s, &m));
}

#[test]
fn a_fight_point_without_a_clear_shot_is_skipped() {
    let arena = lab_arena(9);
    let m = member(Vec2::new(MOB.0, MOB.1));
    let s = scene(&arena, Vec2::new(SEAT.0, SEAT.1), WeaponId::Wand, &m);
    let at = Vec2::new(SEAT.0, SEAT.1);
    let range = lock_range(Some(WeaponId::Wand)) - 1.;
    let mut reach = Reach::default();
    let all = reach.points(&s.paths, CAMP, false).to_vec();
    let nearest_any = all
        .iter()
        .filter(|q| dist(**q, m.at) <= range)
        .min_by(|a, b| dist(**a, at).total_cmp(&dist(**b, at)))
        .copied()
        .expect("a point in range");
    assert!(
        !shot_clear_around(&s.paths, nearest_any, m.at),
        "the plain nearest point (the old pick) has no clear shot with a margin"
    );
    let to = reach
        .nearest(&s.paths, CAMP, false, at, m.at, range)
        .expect("a point with a clear shot");
    assert!(shot_clear_around(&s.paths, to, m.at));
}
