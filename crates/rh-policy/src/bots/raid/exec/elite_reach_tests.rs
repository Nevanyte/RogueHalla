//! P-RAID-REACH-1 oracle (docs/backlog/packets/P-RAID-REACH-1.md), in-crate on hand-built frames. Read-only for the
//! implementer.
//!
//! Part 1, the reach rule: a ranged seat within `lock_range` of its target but outside the leash band of the target's
//! camp steps toward it (Elite and Farm both, through the one helper `combat::may_fight_from`); a seat inside the band
//! locks and stands; a melee seat is unchanged; the helper never allows a place the sim's own leash (`leash_ok`,
//! `MOB_LEASH_MARGIN`) refuses.
//! Part 2, the fight record: a fight's start names the camp it belongs to, and a lock counts only on a mob that is this
//! camp's (P-RAID-E-leave-fix2's arm read too wide).
//!
//! Hand-built facts: camp rows for every camp of the map (`CampRoster` is public to the bot through `CampSeen`), the mob
//! rows of the camp under test, a seat holding a weapon at full hp. Part 1 uses the generated map of lab seed 7 (the
//! lab's own: `mapgen` + board admission), whose camp 50 (a Gaoler camp: radius 96, 11 territory cells, home
//! (4304, 752)) is the traced trap and whose camp 51 is a small normal camp; Part 2 uses the seed1 fixture map as
//! `elite_leave_fix2_tests.rs` does.
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

// ---- Part 1: the reach rule --------------------------------------------------------------------------------------------

fn spot(arena: &Arena, camp: u32, i: usize) -> Vec2 {
    let s = &arena.map.layers().expect("layers").data().camps[camp as usize].spots[i];
    Vec2::new(s.x, s.y)
}

/// Is `p` inside the leash band of camp `camp` for a mob resting at any of the camp's spots (the sim's own predicate)?
fn in_band_any(arena: &Arena, camp: u32, p: Vec2) -> bool {
    let data = arena.map.layers().expect("layers").data();
    data.camps[camp as usize].spots.iter().any(|s| {
        leash_ok(
            data,
            camp,
            Vec2::new(s.x, s.y),
            p,
            rh_sim::constants::MOB_LEASH_MARGIN,
        )
    })
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

/// The first of 32 bearings at `d` u from `home` where the seat stands on a clear floor line to `home` and
/// `want_inside` matches the camp's band; deterministic.
fn seat_point(arena: &Arena, camp: u32, home: Vec2, d: f64, want_inside: bool) -> Vec2 {
    for k in 0..32 {
        let a = f64::from(k) * std::f64::consts::TAU / 32.;
        let at = Vec2::new(
            home.x + rh_sim::math::cos(a) * d,
            home.y + rh_sim::math::sin(a) * d,
        );
        if floor_line(arena, at, home) && in_band_any(arena, camp, at) == want_inside {
            return at;
        }
    }
    panic!("no seat point {d} u from {home:?} with inside={want_inside} at camp {camp}");
}

const TRAP_CAMP: u32 = 50;
const NORMAL_CAMP: u32 = 51;

/// Camp 50 of lab seed 7 as a Gaoler camp with its elite resting at its home, a seat at `at` with `weapon`.
fn trap_frame(arena: &Arena, at: Vec2, weapon: WeaponId) -> Frame {
    let home = spot(arena, TRAP_CAMP, 0);
    frame(
        arena,
        at,
        weapon,
        None,
        &[(TRAP_CAMP, Some(MobTypeId::Gaoler), 1)],
        vec![mob(
            "elite",
            home,
            MobClass::Elite,
            Some(MobTypeId::Gaoler),
            Some(TRAP_CAMP),
        )],
    )
}

/// Camp 51 of lab seed 7 as a normal three-member camp, the members resting at its spots.
fn normal_frame(arena: &Arena, at: Vec2, weapon: WeaponId) -> Frame {
    let mobs = (0..3)
        .map(|i| {
            mob(
                &format!("m{i}"),
                spot(arena, NORMAL_CAMP, i),
                MobClass::Regional,
                None,
                Some(NORMAL_CAMP),
            )
        })
        .collect();
    frame(arena, at, weapon, None, &[(NORMAL_CAMP, None, 3)], mobs)
}

fn assert_way(f: &mut Frame, to: Vec2) {
    let at = f.p.me.at;
    assert!(
        travel::step_toward(&mut f.paths, at, to).is_some(),
        "premise: no way from {at:?} to {to:?}"
    );
}

/// Premise of the whole packet (green on the base): the bot's own public statics carry the map layers the sim's leash
/// predicate reads, through `RoomPathService::collision_map` (built from the match's arena), so no new field, wire or
/// observation is needed (decision 2a).
#[test]
fn the_bots_public_statics_carry_the_layers_the_leash_predicate_reads() {
    let arena = lab_arena(7);
    let paths = RoomPathService::new(&arena.map, PathBudget::default());
    let layers = paths
        .collision_map()
        .layers()
        .expect("the path service keeps the map's layers");
    assert!(layers.data().camps.len() > TRAP_CAMP as usize);
}

/// RED on the base (the stub says yes everywhere): a Wand seat 300 u from camp 50's resting Gaoler, inside its
/// `lock_range` 512 but outside the camp's leash band (the band is 72 u across at most: radius 96 less the margin), is
/// not fighting from where the Gaoler cannot come: it steps toward the elite. This is lab seed 7's traced seat.
#[test]
fn a_wand_seat_in_lock_range_but_outside_the_elites_band_steps_toward_it() {
    let arena = lab_arena(7);
    let home = spot(&arena, TRAP_CAMP, 0);
    let at = seat_point(&arena, TRAP_CAMP, home, 300., false);
    let mut f = trap_frame(&arena, at, WeaponId::Wand);
    assert!(
        f.p.dist(home) <= crate::bots::raid::exec::farm::lock_range(Some(WeaponId::Wand)),
        "premise: inside the wand's lock range"
    );
    assert_way(&mut f, home);
    let mut el = Elite::default();
    let step = elite_frame(&mut el, &mut f, &elite_goal(TRAP_CAMP));
    let to = move_to(&step)
        .unwrap_or_else(|| panic!("a Wand seat outside the band stands (no move): {step:?}"));
    assert!(
        dist(to, home) < dist(at, home) - 1.,
        "the move {to:?} is not toward the elite at {home:?} (seat {at:?})"
    );
}

/// RED on the base: the same for a Bow seat (lock range 0.8 of its range).
#[test]
fn a_bow_seat_in_lock_range_but_outside_the_elites_band_steps_toward_it() {
    let arena = lab_arena(7);
    let home = spot(&arena, TRAP_CAMP, 0);
    let at = seat_point(&arena, TRAP_CAMP, home, 300., false);
    let mut f = trap_frame(&arena, at, WeaponId::Bow);
    assert!(f.p.dist(home) <= crate::bots::raid::exec::farm::lock_range(Some(WeaponId::Bow)));
    assert_way(&mut f, home);
    let mut el = Elite::default();
    let step = elite_frame(&mut el, &mut f, &elite_goal(TRAP_CAMP));
    let to = move_to(&step)
        .unwrap_or_else(|| panic!("a Bow seat outside the band stands (no move): {step:?}"));
    assert!(dist(to, home) < dist(at, home) - 1., "{to:?}");
}

/// RED on the base: Farm's approach is the same rule (one helper). Camp 51 of seed 7 is a small normal camp (radius 166,
/// 54 cells): a Wand seat 300 u from its first member, outside the band of every spot, steps toward the member. Measured
/// (docs/backlog/packets/P-RAID-REACH-1.md): normal camps show no trap in the lab, so this pins the shared rule, not a
/// farm speed-up.
#[test]
fn a_wand_seat_in_lock_range_but_outside_a_normal_camps_band_steps_toward_the_member() {
    let arena = lab_arena(7);
    let first = spot(&arena, NORMAL_CAMP, 0);
    let at = (0..32)
        .map(|k| {
            let a = f64::from(k) * std::f64::consts::TAU / 32.;
            Vec2::new(
                first.x + rh_sim::math::cos(a) * 300.,
                first.y + rh_sim::math::sin(a) * 300.,
            )
        })
        .find(|p| floor_line(&arena, *p, first) && !in_band_any(&arena, NORMAL_CAMP, *p))
        .expect("a seat point outside the band of every spot");
    let mut f = normal_frame(&arena, at, WeaponId::Wand);
    assert_way(&mut f, first);
    let mut farm = Farm::default();
    let step = farm_frame(&mut farm, &mut f, &farm_goal(NORMAL_CAMP));
    let nearest = (0..3)
        .map(|i| spot(&arena, NORMAL_CAMP, i))
        .min_by(|a, b| dist(*a, at).total_cmp(&dist(*b, at)))
        .expect("members");
    let to = move_to(&step)
        .unwrap_or_else(|| panic!("a Wand seat outside the band stands (no move): {step:?}"));
    assert!(
        dist(to, nearest) < dist(at, nearest) - 1.,
        "the move {to:?} is not toward the member at {nearest:?} (seat {at:?})"
    );
}

/// Guard (green on the base, must stay green): a Wand seat already inside the band locks the Gaoler and does not move.
#[test]
fn a_seat_inside_the_band_locks_and_stands() {
    let arena = lab_arena(7);
    let home = spot(&arena, TRAP_CAMP, 0);
    let at = seat_point(&arena, TRAP_CAMP, home, 40., true);
    let mut f = trap_frame(&arena, at, WeaponId::Wand);
    let mut el = Elite::default();
    let step = elite_frame(&mut el, &mut f, &elite_goal(TRAP_CAMP));
    assert!(has_lock(&step), "no lock from inside the band: {step:?}");
    assert!(
        move_to(&step).is_none(),
        "a move from inside the band: {step:?}"
    );
}

/// Guard: the same through Farm on the normal camp, a seat inside the band of a spot.
#[test]
fn a_farm_seat_inside_the_band_locks_and_stands() {
    let arena = lab_arena(7);
    let first = spot(&arena, NORMAL_CAMP, 0);
    let at = seat_point(&arena, NORMAL_CAMP, first, 60., true);
    let mut f = normal_frame(&arena, at, WeaponId::Wand);
    let mut farm = Farm::default();
    let step = farm_frame(&mut farm, &mut f, &farm_goal(NORMAL_CAMP));
    assert!(has_lock(&step), "no lock from inside the band: {step:?}");
    assert!(
        move_to(&step).is_none(),
        "a move from inside the band: {step:?}"
    );
}

/// Guard (melee unchanged): a Sword seat 30 u from the resting Gaoler locks it (its `lock_range` is `MELEE_LOCK_U`)
/// even where the seat stands outside the camp's band (the mob has been drawn out of its band, hand-built), and one 200 u
/// away walks to it exactly as before. Both green on the base.
#[test]
fn a_melee_seats_approach_is_unchanged() {
    let arena = lab_arena(7);
    let home = spot(&arena, TRAP_CAMP, 0);
    // 30 u from the elite, which stands at the seat's side (outside the band).
    let at = seat_point(&arena, TRAP_CAMP, home, 200., false);
    let mut f = trap_frame(&arena, at, WeaponId::Sword);
    f.p.mobs[0].at = Vec2::new(at.x + MELEE_LOCK_U - 10., at.y);
    let mut el = Elite::default();
    let step = elite_frame(&mut el, &mut f, &elite_goal(TRAP_CAMP));
    assert!(
        has_lock(&step),
        "a melee seat in reach does not lock: {step:?}"
    );
    assert!(move_to(&step).is_none(), "{step:?}");
    // 200 u away, elite at home: it walks.
    let mut f = trap_frame(&arena, at, WeaponId::Sword);
    assert_way(&mut f, home);
    let mut el = Elite::default();
    let step = elite_frame(&mut el, &mut f, &elite_goal(TRAP_CAMP));
    let to = move_to(&step).unwrap_or_else(|| panic!("a Sword seat 200 u away stands: {step:?}"));
    assert!(dist(to, home) < dist(at, home) - 1., "{to:?}");
    assert!(!has_lock(&step), "a lock from 200 u with a sword: {step:?}");
}

/// Guard (the helper over several maps): at the resting spot of every camp the helper allows the fight (a mob at home
/// reaches a seat standing on it), and nowhere does it allow a place the sim's leash refuses: for a point `p` it says
/// yes to, `leash_ok` holds for the camp's elite spot (an elite target) or for some spot (a normal target). A later
/// change of the sim's leash numbers breaks this test, not the bot silently. RED on the stub (it says yes everywhere).
#[test]
fn the_helpers_reach_never_exceeds_the_sims_leash() {
    for seed in [1u64, 2, 3, 7] {
        let arena = lab_arena(seed);
        let paths = RoomPathService::new(&arena.map, PathBudget::default());
        let data = arena.map.layers().expect("layers").data();
        let margin = rh_sim::constants::MOB_LEASH_MARGIN;
        let mut denied = 0u32;
        for (ci, c) in data.camps.iter().enumerate() {
            let camp = ci as u32;
            if c.spots.is_empty() {
                continue;
            }
            let elite = c.elite_spot >= 0;
            let homes: Vec<Vec2> = if elite {
                vec![spot(&arena, camp, c.elite_spot as usize)]
            } else {
                (0..c.spots.len()).map(|i| spot(&arena, camp, i)).collect()
            };
            for h in &homes {
                assert!(
                    may_fight_from(&paths, camp, elite, *h),
                    "seed {seed} camp {camp}: the helper refuses the camp's own resting spot {h:?}"
                );
            }
            for gx in -16i32..=16 {
                for gy in -16i32..=16 {
                    let p = Vec2::new(
                        homes[0].x + f64::from(gx) * 64.,
                        homes[0].y + f64::from(gy) * 64.,
                    );
                    if may_fight_from(&paths, camp, elite, p) {
                        let sim_ok = homes.iter().any(|h| leash_ok(data, camp, *h, p, margin));
                        assert!(
                            sim_ok,
                            "seed {seed} camp {camp} elite={elite}: the helper allows {p:?}, the sim's leash does not"
                        );
                    } else {
                        denied += 1;
                    }
                }
            }
        }
        assert!(denied > 0, "seed {seed}: the helper never refused anything");
    }
}

// ---- Part 2: the fight record names its camp; a lock counts only on this camp's mob ----------------------------------

/// Two Executioner camps of the seed1 fixture map at least 1 500 u apart (`a`, `b`), found by a fixed walk.
fn two_camps(arena: &Arena) -> (u32, u32) {
    let plan = PlanMap::from_arena(arena);
    for a in 0..plan.camps.len() {
        for b in a + 1..plan.camps.len() {
            let d = dist(
                Vec2::new(plan.camps[a].x, plan.camps[a].y),
                Vec2::new(plan.camps[b].x, plan.camps[b].y),
            );
            if d >= 1_500. {
                return (a as u32, b as u32);
            }
        }
    }
    panic!("the fixture map has no two camps 1 500 u apart");
}

fn centre(arena: &Arena, camp: u32) -> Vec2 {
    let plan = PlanMap::from_arena(arena);
    Vec2::new(plan.camps[camp as usize].x, plan.camps[camp as usize].y)
}

/// Both camps rolled Executioner: each with its elite at the centre and one add 8 u beside it; the seat `off` from the
/// centre of camp `near` with a Wand and its lock `lock`; `extra` mobs appended.
fn two_camp_frame(
    arena: &Arena,
    a: u32,
    b: u32,
    near: u32,
    off: Vec2,
    lock: Option<&str>,
    extra: Vec<MobSeen>,
) -> Frame {
    let mut mobs = Vec::new();
    for (tag, c) in [("a", a), ("b", b)] {
        let at = centre(arena, c);
        mobs.push(mob(
            &format!("add_{tag}"),
            Vec2::new(at.x + 8., at.y + 8.),
            MobClass::Regional,
            None,
            Some(c),
        ));
        mobs.push(mob(
            &format!("elite_{tag}"),
            at,
            MobClass::Elite,
            Some(MobTypeId::Executioner),
            Some(c),
        ));
    }
    mobs.extend(extra);
    let n = centre(arena, near);
    frame(
        arena,
        Vec2::new(n.x + off.x, n.y + off.y),
        WeaponId::Wand,
        lock,
        &[
            (a, Some(MobTypeId::Executioner), 2),
            (b, Some(MobTypeId::Executioner), 2),
        ],
        mobs,
    )
}

/// RED on the base. `give_up(A)` (the seat never reached A in 120 s), then the arbiter picks Elite B and B's first act
/// runs while the seat's lock is still on a chaser of A that has left every camp radius (no label). The chaser must not
/// make B's fight: after the next observe (the goal switch A to B is seen) A's give-up count is still 1.
#[test]
fn a_give_up_is_not_counted_again_when_the_next_elite_goal_holds_an_unlabelled_lock() {
    let arena = seed1_arena();
    let (a, b) = two_camps(&arena);
    let chaser_at = |f: &Frame| Vec2::new(f.p.me.at.x + 100., f.p.me.at.y);
    let mut f = two_camp_frame(&arena, a, b, a, Vec2::new(400., 0.), None, Vec::new());
    let mut el = Elite::default();
    // t0: the first think of A (never reached); t1, 121 s on: the walk is out of time.
    elite_frame(&mut el, &mut f, &elite_goal(a));
    f.p.now += 121_000.;
    let step = elite_frame(&mut el, &mut f, &elite_goal(a));
    assert_eq!(step.status, Status::Failed, "premise: {step:?}");
    assert_eq!(giveups_at(&el, a), Some(1), "premise: one give-up of A");
    // t2: the seat's goal is still A's in `observe` (the arbiter picks after it); then Elite B starts and acts with the
    // lock on the chaser, which stands 100 u from the seat and carries no camp label.
    f.p.now += 50.;
    let prices = &Prices::LOCAL;
    let mut rng = Rng::new(1);
    el.observe(&mut cx(&mut f, &elite_goal(a), &mut rng, prices));
    let chaser = mob("chaser", chaser_at(&f), MobClass::Regional, None, None);
    f.p.mobs.push(chaser);
    f.p.base.self_view.owner_lock = Some(OwnerLock::Held {
        target: Some("chaser".into()),
    });
    let goal_b = elite_goal(b);
    el.start(&goal_b, &mut cx(&mut f, &goal_b, &mut rng, prices));
    el.act(&goal_b, &mut cx(&mut f, &goal_b, &mut rng, prices));
    // t3: observe sees the goal switch A to B.
    f.p.now += 50.;
    el.observe(&mut cx(&mut f, &goal_b, &mut rng, prices));
    assert_eq!(
        giveups_at(&el, a),
        Some(1),
        "A was banned twice for one time-out: the lock on an unlabelled chaser made B's fight"
    );
}

/// RED on the base: a lock on a regional mob that is not engaged and carries no camp label, 450 u from the seat, does
/// not start the camp's fight (`fight_since` stays unset).
#[test]
fn a_lock_on_an_unlabelled_regional_mob_does_not_start_a_fight() {
    let arena = seed1_arena();
    let (a, b) = two_camps(&arena);
    let mut f = two_camp_frame(&arena, a, b, a, Vec2::new(400., 0.), None, Vec::new());
    let at = f.p.me.at;
    f.p.mobs.push(mob(
        "chaser",
        Vec2::new(at.x, at.y + 450.),
        MobClass::Regional,
        None,
        None,
    ));
    f.p.base.self_view.owner_lock = Some(OwnerLock::Held {
        target: Some("chaser".into()),
    });
    let mut el = Elite::default();
    elite_frame(&mut el, &mut f, &elite_goal(a));
    assert!(
        el.fight_since.is_none(),
        "a lock on a mob of no camp, 450 u away, started the fight"
    );
}

/// RED on the base: camp B's goal, B's own elite out of sight (not in the frame), the seat's lock on camp A's elite
/// (the same kind, 1 500 u or more from B): that is not B's fight.
#[test]
fn a_lock_on_another_camps_elite_of_the_same_kind_does_not_start_this_camps_fight() {
    let arena = seed1_arena();
    let (a, b) = two_camps(&arena);
    let mut f = two_camp_frame(
        &arena,
        a,
        b,
        a,
        Vec2::new(400., 0.),
        Some("elite_a"),
        Vec::new(),
    );
    f.p.mobs.retain(|m| m.key != "elite_b" && m.key != "add_b");
    let mut el = Elite::default();
    elite_frame(&mut el, &mut f, &elite_goal(b));
    assert!(
        el.fight_since.is_none(),
        "a lock on camp A's elite started camp B's fight"
    );
}

/// Guard (green on the base, must stay green): on camp B's goal, a lock on B's own elite from 400 u starts the fight...
#[test]
fn a_lock_on_this_camps_own_elite_from_400_u_starts_the_fight() {
    let arena = seed1_arena();
    let (a, b) = two_camps(&arena);
    let mut f = two_camp_frame(
        &arena,
        a,
        b,
        b,
        Vec2::new(400., 0.),
        Some("elite_b"),
        Vec::new(),
    );
    let mut el = Elite::default();
    elite_frame(&mut el, &mut f, &elite_goal(b));
    assert!(el.fight_since.is_some(), "the fight is not recorded");
}

/// ...and so does a lock on its labelled add from 400 u.
#[test]
fn a_lock_on_this_camps_labelled_add_from_400_u_starts_the_fight() {
    let arena = seed1_arena();
    let (a, b) = two_camps(&arena);
    let mut f = two_camp_frame(
        &arena,
        a,
        b,
        b,
        Vec2::new(400., 0.),
        Some("add_b"),
        Vec::new(),
    );
    let mut el = Elite::default();
    elite_frame(&mut el, &mut f, &elite_goal(b));
    assert!(el.fight_since.is_some(), "the fight is not recorded");
}
