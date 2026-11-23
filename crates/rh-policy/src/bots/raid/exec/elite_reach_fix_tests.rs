//! P-RAID-REACH-1-fix oracle, in-crate on hand-built frames (docs/backlog/packets/P-RAID-REACH-1-fix.md). Read-only for
//! the implementer.
//!
//! The sim's rule: a chasing mob gives up when its own position fails `leash_ok` at margin 0 (or 20 s after its last
//! contact); a chase point is not leash-tested (`mob_ai.rs` `fight`, `combat_policy::decide`). So a seat may fight from
//! where the camp's mob, standing inside its own leash, can reach it. These tests drive the executors think by think on
//! lab maps (seeds 1, 5, 7, 11) with a seat that walks 12 u per think toward each `Move` it is told to make.
use super::*;
use crate::bots::paths::{PathBudget, RoomPathService};
use crate::bots::perception::LootFacts;
use crate::bots::planning::PlanMap;
use crate::bots::raid::exec::combat::may_fight_from;
use crate::bots::raid::exec::farm::{Farm, MELEE_LOCK_U};
use crate::bots::raid::percept::{Me, RaidPercept};
use crate::bots::random::Rng;
use crate::bots::types::{HudView, Percept, PersonaId, SelfView, TierId};
use rh_sim::mapdata::patience::leash_ok;
use rh_sim::{Arena, FactionId, Secrets};
use std::sync::Arc;

fn seed1_arena() -> Arena {
    let path = concat!(
        env!("CARGO_MANIFEST_DIR"),
        "/../rh-sim/src/mapdata/fixtures/seed1.map.json"
    );
    let text = std::fs::read_to_string(path).expect("the seed1 fixture map");
    let file: rh_sim::mapdata::MapExport = serde_json::from_str(&text).expect("rh-map/2 json");
    file.validate()
        .expect("an admitted map")
        .into_arena()
        .expect("an arena")
}

/// The map `balance_lab` plays for `seed` (generated, board-admitted as `Env::new` does).
fn lab_arena(seed: u64) -> Arc<Arena> {
    use rh_sim::mapgen::{coliseum, pipeline};
    let layout = coliseum::fixed_layout();
    let generator = pipeline::Pipeline::new(&layout);
    let options = pipeline::Options {
        decor: false,
        ..Default::default()
    };
    let map = generator
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

struct Frame {
    plan: PlanMap,
    paths: RoomPathService,
    p: RaidPercept,
}

fn mob(
    key: &str,
    at: Vec2,
    class: MobClass,
    kind: Option<MobTypeId>,
    camp: Option<u32>,
) -> MobSeen {
    MobSeen {
        key: key.into(),
        at,
        hp_share: 1.,
        alive: true,
        class,
        mob_type: kind,
        weapon: None,
        faction: None,
        camp,
        engaged: false,
    }
}

/// One frame: a row for every camp of the map (the executors index `camps` by camp index), `rows` of them alive with
/// the given elite kind and member count; a seat at `at` holding `weapon` at full hp, its lock `lock`, `now` 5 000 ms.
fn frame(
    arena: &Arena,
    at: Vec2,
    weapon: WeaponId,
    lock: Option<&str>,
    rows: &[(u32, Option<MobTypeId>, u32)],
    mobs: Vec<MobSeen>,
) -> Frame {
    let plan = PlanMap::from_arena(arena);
    let paths = RoomPathService::new(&arena.map, PathBudget::default());
    let camps = plan
        .camps
        .iter()
        .enumerate()
        .map(|(i, c)| {
            let row = rows.iter().find(|r| r.0 == i as u32);
            CampSeen {
                index: i as u32,
                at: Vec2::new(c.x, c.y),
                zone: c.zone,
                elite_spot: c.elite_spot,
                mob_type: 0,
                elite: row.and_then(|r| r.1),
                alive: row.map_or(0, |r| r.2),
                state: 0,
                cleared_at: 0.,
                respawn_at: 0.,
                ward: None,
            }
        })
        .collect();
    let owner_lock = match lock {
        Some(k) => OwnerLock::Held {
            target: Some(k.into()),
        },
        None => OwnerLock::Clear,
    };
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
            owner_lock: Some(owner_lock),
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
        mobs,
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
    Frame { plan, paths, p }
}

fn cx<'a>(f: &'a mut Frame, goal: &'a RaidGoal, rng: &'a mut Rng, prices: &'a Prices) -> Cx<'a> {
    Cx {
        p: &f.p,
        paths: &mut f.paths,
        plan: &f.plan,
        knowledge: None,
        tier: TierId::Normal,
        persona: PersonaId::Farmer,
        prices,
        still_since: 0.,
        goal,
        status: Status::Running,
        rng,
    }
}

fn elite_goal(camp: u32) -> RaidGoal {
    RaidGoal::new(GoalKind::Elite, Target::Camp(camp))
}

fn farm_goal(camp: u32) -> RaidGoal {
    RaidGoal::new(GoalKind::Farm, Target::Camp(camp))
}

/// Elite's frame: `observe`, then `act`.
fn elite_frame(el: &mut Elite, f: &mut Frame, goal: &RaidGoal) -> Step {
    let prices = &Prices::LOCAL;
    let mut rng = Rng::new(1);
    el.observe(&mut cx(f, goal, &mut rng, prices));
    el.act(goal, &mut cx(f, goal, &mut rng, prices))
}

fn farm_frame(farm: &mut Farm, f: &mut Frame, goal: &RaidGoal) -> Step {
    let prices = &Prices::LOCAL;
    let mut rng = Rng::new(1);
    farm.observe(&mut cx(f, goal, &mut rng, prices));
    farm.act(goal, &mut cx(f, goal, &mut rng, prices))
}

fn giveups_at(el: &Elite, camp: u32) -> Option<u32> {
    el.giveups.iter().find(|g| g.camp == camp).map(|g| g.n)
}

fn move_to(step: &Step) -> Option<Vec2> {
    step.intents.iter().find_map(|i| match i.body {
        IntentBody::Move { to } => Some(to),
        _ => None,
    })
}

fn has_lock(step: &Step) -> bool {
    step.intents
        .iter()
        .any(|i| matches!(i.body, IntentBody::Lock { .. }))
}

// ---- helpers -----------------------------------------------------------------------------------------------------------

fn spot(arena: &Arena, camp: u32, i: usize) -> Vec2 {
    let s = &arena.map.layers().expect("layers").data().camps[camp as usize].spots[i];
    Vec2::new(s.x, s.y)
}

fn floor_line(arena: &Arena, a: Vec2, b: Vec2) -> bool {
    (0..=40).all(|i| {
        let t = f64::from(i) / 40.;
        arena.map.is_floor(
            rh_sim::map::world_to_tile(a.x + (b.x - a.x) * t),
            rh_sim::map::world_to_tile(a.y + (b.y - a.y) * t),
        )
    })
}

fn assert_way(f: &mut Frame, to: Vec2) {
    let at = f.p.me.at;
    assert!(
        travel::step_toward(&mut f.paths, at, to).is_some(),
        "premise: no way from {at:?} to {to:?}"
    );
}

fn centre(arena: &Arena, camp: u32) -> Vec2 {
    let plan = PlanMap::from_arena(arena);
    Vec2::new(plan.camps[camp as usize].x, plan.camps[camp as usize].y)
}

/// Walking pace per think of the driver below.
const WALK_U: f64 = 12.;

/// One camp of `arena` as the executors see it: an elite camp (the elite resting at its elite spot, kind `kind`) or a
/// normal camp (one member at each spot); a seat at `at` with `weapon`. Returns the frame, the goal and the homes.
fn camp_frame(
    arena: &Arena,
    camp: u32,
    at: Vec2,
    weapon: WeaponId,
    kind: MobTypeId,
) -> (Frame, RaidGoal, Vec<Vec2>) {
    let data = arena.map.layers().expect("layers").data();
    let def = &data.camps[camp as usize];
    if def.elite_spot >= 0 {
        let home = spot(arena, camp, def.elite_spot as usize);
        let f = frame(
            arena,
            at,
            weapon,
            None,
            &[(camp, Some(kind), 1)],
            vec![mob("elite", home, MobClass::Elite, Some(kind), Some(camp))],
        );
        (f, elite_goal(camp), vec![home])
    } else {
        let homes: Vec<Vec2> = (0..def.spots.len()).map(|i| spot(arena, camp, i)).collect();
        let mobs = homes
            .iter()
            .enumerate()
            .map(|(i, h)| mob(&format!("m{i}"), *h, MobClass::Regional, None, Some(camp)))
            .collect();
        let rows = [(camp, None, homes.len() as u32)];
        let f = frame(arena, at, weapon, None, &rows, mobs);
        (f, farm_goal(camp), homes)
    }
}

/// One think of the right executor for `goal`.
fn think(el: &mut Elite, farm: &mut Farm, f: &mut Frame, goal: &RaidGoal) -> Step {
    match goal.kind {
        GoalKind::Elite => elite_frame(el, f, goal),
        _ => farm_frame(farm, f, goal),
    }
}

fn place(f: &mut Frame, at: Vec2) {
    f.p.me.at = at;
    f.p.base.self_view.x = at.x;
    f.p.base.self_view.y = at.y;
}

/// Think after think (50 ms apart), the seat walking `WALK_U` toward each move it is told to make, the mobs resting.
/// The number of thinks until the executor first emits a lock, or `None` within `max`.
fn thinks_until_lock(f: &mut Frame, goal: &RaidGoal, max: usize) -> Option<usize> {
    let (mut el, mut farm) = (Elite::default(), Farm::default());
    for n in 0..max {
        let step = think(&mut el, &mut farm, f, goal);
        if has_lock(&step) {
            return Some(n);
        }
        if let Some(to) = move_to(&step) {
            let at = f.p.me.at;
            let d = dist(at, to);
            let k = if d <= WALK_U { 1. } else { WALK_U / d };
            place(
                f,
                Vec2::new(at.x + (to.x - at.x) * k, at.y + (to.y - at.y) * k),
            );
        }
        f.p.now += 50.;
        f.p.base.now += 50.;
    }
    None
}

/// The camps of the lab seeds 1 to 12 whose resting spot lies outside its own band at margin 24 and inside it at margin
/// 0 (packet census): seed 1 camp 50, seed 5 camps 30 (one spot of three) and 51, seed 7 camps 50 (elite) and 51, seed
/// 11 camp 50 (elite). These five are the ones the tests use.
const SMALL_CAMPS: [(u64, u32); 5] = [(1, 50), (5, 51), (7, 51), (7, 50), (11, 50)];

// ---- the stall, hand-built ------------------------------------------------------------------------------------------------

/// RED at the head. A ranged seat standing 5 u from an idle member of seed 7 camp 51 (all three resting spots are
/// outside their own band at margin 24, inside it at margin 0) locks within 100 thinks (5 s): at once (the mob can bring
/// the fight to a seat that close to home) or after a few steps to a place it may fight from. At the head it walks onto
/// the member and sends a move every think.
#[test]
fn a_ranged_seat_beside_an_idle_member_of_a_small_camp_locks() {
    let arena = lab_arena(7);
    for weapon in [WeaponId::Wand, WeaponId::Bow] {
        let home = spot(&arena, 51, 0);
        let at = Vec2::new(home.x + 5., home.y);
        let (mut f, goal, _) = camp_frame(&arena, 51, at, weapon, MobTypeId::Gaoler);
        let n = thinks_until_lock(&mut f, &goal, 100);
        assert!(
            n.is_some(),
            "{weapon:?}: no lock in 100 thinks beside an idle member; the seat ended at {:?}",
            f.p.me.at
        );
    }
}

/// RED at the head: the same for an elite camp, a Wand seat beside the idle Gaoler of seed 7 camp 50 (radius 96, whose
/// one spot is outside its band at margin 24).
#[test]
fn a_ranged_seat_beside_an_idle_elite_of_a_small_camp_locks() {
    let arena = lab_arena(7);
    let home = spot(&arena, 50, 0);
    let at = Vec2::new(home.x + 30., home.y);
    let (mut f, goal, _) = camp_frame(&arena, 50, at, WeaponId::Wand, MobTypeId::Gaoler);
    assert!(
        thinks_until_lock(&mut f, &goal, 100).is_some(),
        "no lock in 100 thinks beside the idle Gaoler; the seat ended at {:?}",
        f.p.me.at
    );
}

/// RED at the head, and the guard against an approach that never ends: on each of the five small camps, a Wand seat
/// starting at any of 48 bearings 300 u from the camp's first home (bearings off the floor or without a way to the home
/// are skipped; at least 16 are tried per camp) ends in a lock within 600 thinks (30 s). Hand-built, not live. A seat
/// that walks to a place it may fight from and locks there passes; one that stands, or walks on, fails.
#[test]
fn from_every_bearing_a_ranged_seat_on_a_small_camp_ends_in_a_lock() {
    for (seed, camp) in SMALL_CAMPS {
        let arena = lab_arena(seed);
        let kind = MobTypeId::Executioner;
        let home0 = camp_frame(&arena, camp, spot(&arena, camp, 0), WeaponId::Wand, kind).2[0];
        let mut tried = 0;
        for k in 0..48 {
            let a = f64::from(k) * std::f64::consts::TAU / 48.;
            let at = Vec2::new(
                home0.x + rh_sim::math::cos(a) * 300.,
                home0.y + rh_sim::math::sin(a) * 300.,
            );
            let tile = |v: f64| rh_sim::map::world_to_tile(v);
            if !arena.map.is_floor(tile(at.x), tile(at.y)) {
                continue;
            }
            let (mut f, goal, _) = camp_frame(&arena, camp, at, WeaponId::Wand, kind);
            if travel::step_toward(&mut f.paths, at, home0).is_none() {
                continue;
            }
            tried += 1;
            let n = thinks_until_lock(&mut f, &goal, 600);
            assert!(
                n.is_some(),
                "seed {seed} camp {camp}: from bearing {k} ({at:?}) no lock in 600 thinks; the seat ended at {:?}",
                f.p.me.at
            );
        }
        assert!(
            tried >= 16,
            "seed {seed} camp {camp}: only {tried} bearings tried"
        );
    }
}

// ---- the reach rule is the sim's -------------------------------------------------------------------------------------

/// The longest melee attack a regional or elite mob has (`combat_policy::spacing`: `reach + PLAYER_RADIUS -
/// NPC_MELEE_EDGE`, the Axe and Sword reach 112), plus 23 u for the cell-centre sampling below (a mob stands anywhere in
/// a 32 u cell; its centre is at most 16 * sqrt 2 away).
const MELEE_BOUND_U: f64 = 112. + 12. - 6. + 23.;

fn homes_of(arena: &Arena, camp: u32) -> (Vec<Vec2>, bool) {
    let data = arena.map.layers().expect("layers").data();
    let c = &data.camps[camp as usize];
    if c.elite_spot >= 0 {
        (vec![spot(arena, camp, c.elite_spot as usize)], true)
    } else {
        (
            (0..c.spots.len()).map(|i| spot(arena, camp, i)).collect(),
            false,
        )
    }
}

/// RED at the head, both directions of the sim's rule, over seeds 1, 2, 3 and 7, every camp, a 33 x 33 grid at 64 u
/// around the camp's first home (the grid of `the_helpers_reach_never_exceeds_the_sims_leash`):
/// (a) the helper says yes at every point where a mob resting at one of the camp's homes, standing there, stays inside
/// its leash at margin 0 (`leash_ok`, the sim's give-up test `mob_ai.rs` `fight`);
/// (b) it never says yes where no point of the camp's band (a 32 u territory cell centre inside `leash_ok` at margin 0 of
/// a home) lies within `MELEE_BOUND_U` (141 u): a melee mob that stays inside its leash cannot reach that far. The helper
/// has no weapon argument, so a ranged mob's longer reach is not used here; a rule that wants it goes back to the
/// oracle author.
#[test]
fn the_helper_says_yes_exactly_where_a_mob_inside_its_leash_can_reach() {
    for seed in [1u64, 2, 3, 7] {
        let arena = lab_arena(seed);
        let paths = RoomPathService::new(&arena.map, PathBudget::default());
        let data = arena.map.layers().expect("layers").data();
        let (mut yes, mut inside_total) = (0u32, 0u32);
        for (ci, c) in data.camps.iter().enumerate() {
            if c.spots.is_empty() {
                continue;
            }
            let camp = ci as u32;
            let (homes, elite) = homes_of(&arena, camp);
            let mut band: Vec<Vec2> = Vec::new();
            for ty in 0..data.h {
                for tx in 0..data.w {
                    let q = Vec2::new((f64::from(tx) + 0.5) * 32., (f64::from(ty) + 0.5) * 32.);
                    if dist(q, homes[0]) < 1_400.
                        && homes.iter().any(|h| leash_ok(data, camp, *h, q, 0.))
                    {
                        band.push(q);
                    }
                }
            }
            for gx in -16i32..=16 {
                for gy in -16i32..=16 {
                    let p = Vec2::new(
                        homes[0].x + f64::from(gx) * 64.,
                        homes[0].y + f64::from(gy) * 64.,
                    );
                    let says = may_fight_from(&paths, camp, elite, p);
                    let inside = homes.iter().any(|h| leash_ok(data, camp, *h, p, 0.));
                    inside_total += u32::from(inside);
                    if inside {
                        assert!(
                            says,
                            "seed {seed} camp {camp} elite={elite}: the helper refuses {p:?}, inside the leash the sim gives up on (margin 0)"
                        );
                    }
                    if says {
                        yes += 1;
                        assert!(
                            band.iter().any(|q| dist(*q, p) <= MELEE_BOUND_U),
                            "seed {seed} camp {camp} elite={elite}: the helper allows {p:?}, farther than {MELEE_BOUND_U} u from every point of the band"
                        );
                    }
                }
            }
        }
        assert!(yes > 0 && inside_total > 0, "seed {seed}: nothing allowed");
    }
}

// ---- melee unchanged ----------------------------------------------------------------------------------------------------

/// Guard (green at the head, must stay green): a Sword seat's approach on seed 7 camp 51 is as before: 200 u from the
/// nearest member it moves toward it and does not lock; 30 u from one it locks and does not move.
#[test]
fn a_melee_seat_on_a_small_camp_walks_in_and_locks_as_before() {
    let arena = lab_arena(7);
    let first = spot(&arena, 51, 0);
    let far = (0..32)
        .map(|k| {
            let a = f64::from(k) * std::f64::consts::TAU / 32.;
            Vec2::new(
                first.x + rh_sim::math::cos(a) * 200.,
                first.y + rh_sim::math::sin(a) * 200.,
            )
        })
        .find(|p| floor_line(&arena, *p, first))
        .expect("a clear bearing");
    let (mut f, goal, homes) = camp_frame(&arena, 51, far, WeaponId::Sword, MobTypeId::Gaoler);
    assert_way(&mut f, first);
    let step = farm_frame(&mut Farm::default(), &mut f, &goal);
    let nearest = *homes
        .iter()
        .min_by(|a, b| dist(**a, far).total_cmp(&dist(**b, far)))
        .expect("homes");
    let to = move_to(&step).unwrap_or_else(|| panic!("a Sword seat 200 u away stands: {step:?}"));
    assert!(dist(to, nearest) < dist(far, nearest) - 1., "{to:?}");
    assert!(!has_lock(&step), "{step:?}");
    let near = Vec2::new(first.x + 30., first.y);
    let (mut f, goal, _) = camp_frame(&arena, 51, near, WeaponId::Sword, MobTypeId::Gaoler);
    let step = farm_frame(&mut Farm::default(), &mut f, &goal);
    assert!(has_lock(&step), "{step:?}");
    assert!(move_to(&step).is_none(), "{step:?}");
}

// ---- a farm fight is bounded -------------------------------------------------------------------------------------------

/// Farm thinks one second apart for `seconds`, the seat at `at` (it stays), the members resting. With `contact: Some(n)`
/// every `n` seconds a member loses 2 % of its hp (a hit dealt) and the seat's `hit_at` is refreshed (a hit taken);
/// `None`: no contact at all. Returns the second of the first `Failed` think, if any, with the farm, frame and goal.
fn farm_run(
    arena: &Arena,
    at: Vec2,
    seconds: u32,
    contact: Option<u32>,
) -> (Option<u32>, Farm, Frame, RaidGoal) {
    let (mut f, goal, _) = camp_frame(arena, 51, at, WeaponId::Wand, MobTypeId::Gaoler);
    let mut farm = Farm::default();
    for s in 0..seconds {
        if let Some(every) = contact {
            if s > 0 && s % every == 0 {
                f.p.mobs[0].hp_share = (f.p.mobs[0].hp_share - 0.02).max(0.1);
                f.p.hit_at = Some(f.p.now);
            }
        }
        let step = farm_frame(&mut farm, &mut f, &goal);
        if step.status == Status::Failed {
            return (Some(s), farm, f, goal);
        }
        f.p.now += 1_000.;
        f.p.base.now += 1_000.;
    }
    (None, farm, f, goal)
}

/// RED at the head (no bound): a Wand seat 30 u from an idle member (it locks it; nothing happens: no hit dealt, none
/// taken) gives the camp up after a time with no contact, which this oracle bounds between 10 s (a fight is not cut
/// short) and 45 s (the sim's own mob gives up 20 s after its last contact).
#[test]
fn a_farm_seat_with_no_contact_gives_the_camp_up_in_time() {
    let arena = lab_arena(7);
    let home = spot(&arena, 51, 0);
    let at = Vec2::new(home.x + 30., home.y);
    let (failed, ..) = farm_run(&arena, at, 90, None);
    let s = failed.expect("the farm never gave the idle camp up in 90 s");
    assert!((10..=45).contains(&s), "gave up after {s} s");
}

/// Guard (green at the head, must stay green): a fight with contact every 5 s is not cut short in 120 s.
#[test]
fn a_farm_fight_with_contact_is_not_cut_short() {
    let arena = lab_arena(7);
    let home = spot(&arena, 51, 0);
    let at = Vec2::new(home.x + 30., home.y);
    let (failed, ..) = farm_run(&arena, at, 120, Some(5));
    assert!(
        failed.is_none(),
        "a fight with contact gave up at {failed:?}"
    );
}

/// RED at the head: the camp given up is not proposed again for a while (10 s on) and is again 300 s on.
#[test]
fn a_camp_the_farm_gave_up_is_banned_for_a_while_not_for_good() {
    let arena = lab_arena(7);
    let home = spot(&arena, 51, 0);
    let at = Vec2::new(home.x + 30., home.y);
    let (failed, mut farm, mut f, goal) = farm_run(&arena, at, 90, None);
    assert!(failed.is_some(), "premise: the farm gave the camp up");
    let proposed = |farm: &mut Farm, f: &mut Frame| {
        let prices = &Prices::LOCAL;
        let mut rng = Rng::new(1);
        let mut out = Vec::new();
        farm.propose(&mut cx(f, &goal, &mut rng, prices), &mut out);
        out.iter()
            .any(|c| matches!(c.goal.target, Target::Camp(51)))
    };
    f.p.now += 10_000.;
    assert!(
        !proposed(&mut farm, &mut f),
        "the camp is proposed 10 s after the give-up"
    );
    f.p.now += 290_000.;
    assert!(proposed(&mut farm, &mut f), "the camp is banned for good");
}

// ---- note 2: the elite lock is bounded by the camp's leash, not a fixed radius ----------------------------------------------

/// A point of `camp`'s band (inside `leash_ok` at margin 0 from its elite spot) at least 500 u from the camp's centre, the
/// farthest from it: where the elite may stand while it chases a seat out to the band's edge; and the centre.
fn far_band_point(arena: &Arena, camp: u32) -> (Vec2, Vec2) {
    let data = arena.map.layers().expect("layers").data();
    let home = homes_of(arena, camp).0[0];
    let c = centre(arena, camp);
    let mut best: Option<Vec2> = None;
    for ty in 0..data.h {
        for tx in 0..data.w {
            let q = Vec2::new((f64::from(tx) + 0.5) * 32., (f64::from(ty) + 0.5) * 32.);
            if dist(q, c) > 500.
                && leash_ok(data, camp, home, q, 0.)
                && best.is_none_or(|b| dist(q, c) > dist(b, c))
            {
                best = Some(q);
            }
        }
    }
    (best.expect("a band point beyond 500 u of the centre"), c)
}

/// A Wand seat 300 u from `elite_at` (toward `toward`) holding a lock on the camp's elite.
fn elite_lock_frame(arena: &Arena, camp: u32, elite_at: Vec2, toward: Vec2) -> Frame {
    let d = dist(elite_at, toward);
    let at = Vec2::new(
        elite_at.x + (toward.x - elite_at.x) / d * 300.,
        elite_at.y + (toward.y - elite_at.y) / d * 300.,
    );
    frame(
        arena,
        at,
        WeaponId::Wand,
        Some("elite"),
        &[(camp, Some(MobTypeId::Executioner), 1)],
        vec![mob(
            "elite",
            elite_at,
            MobClass::Elite,
            Some(MobTypeId::Executioner),
            Some(camp),
        )],
    )
}

/// RED at the head: a second ranged seat locks this camp's elite from 300 u (beyond `FIGHT_U`) while the elite stands
/// inside the camp's band, more than 500 u from the camp's centre (as it does when it chases a seat at the band's edge:
/// 92 of the 124 elite camps of seeds 1 to 12 have a band reaching past 480 u from the centre). The fight is on record.
#[test]
fn a_lock_on_this_camps_elite_inside_its_leash_but_far_from_the_centre_starts_the_fight() {
    let arena = lab_arena(1);
    let camp = 7;
    let (p, c) = far_band_point(&arena, camp);
    let mut f = elite_lock_frame(&arena, camp, p, c);
    let mut el = Elite::default();
    elite_frame(&mut el, &mut f, &elite_goal(camp));
    assert!(
        el.fight_since.is_some(),
        "a lock on the camp's elite at {p:?} (inside its leash, {:.0} u from the centre) left no fight on record",
        dist(p, c)
    );
}

/// Guard (green at the head, must stay green): the same elite standing 1 200 u east of its home, outside every leash
/// and far from the centre: a lock on it is not this camp's fight.
#[test]
fn a_lock_on_an_elite_outside_the_camps_leash_does_not_start_the_fight() {
    let arena = lab_arena(1);
    let camp = 7;
    let home = homes_of(&arena, camp).0[0];
    let data = arena.map.layers().expect("layers").data();
    let out = Vec2::new(home.x + 1_200., home.y);
    assert!(!leash_ok(data, camp, home, out, 0.), "premise");
    let mut f = elite_lock_frame(&arena, camp, out, home);
    let mut el = Elite::default();
    elite_frame(&mut el, &mut f, &elite_goal(camp));
    assert!(
        el.fight_since.is_none(),
        "a lock on an elite outside the leash started the fight"
    );
}
