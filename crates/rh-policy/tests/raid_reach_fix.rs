//! P-RAID-REACH-1-fix oracle, live scenes (docs/backlog/packets/P-RAID-REACH-1-fix.md). Read-only for the implementer.
//!
//! A ranged RaidBot seat (a human seat, `bot: false`) on a camp whose resting spots lie outside their own leash band
//! (`leash_ok` at `MOB_LEASH_MARGIN`) must not stall beside an idle member: it hits the camp, or leaves it. Scenes are
//! those of `tests/raid_reach.rs` (the lab's generated map and board admission, `live_content` with the lifecycle off, a
//! seat with 36 banked stacks and two large potions, `Prices::LOCAL` with the quest rewards zeroed and the Gaoler priced
//! by the farm fallback), repeated here because that file is read-only.
use rh_policy::bots::raid::values::Prices;
use rh_policy::bots::{PersonaId, RaidBot, TierId};
use rh_policy::{drive_seats_with, feed_seats, scripted, MatchStart, Policy, Seat};
use rh_sim::mob_catalog::MobTypeId;
use rh_sim::trusted_build::TrustedBuild;
use rh_sim::{
    Arena, ArenaMode, Event, FactionId, Placement, Secrets, SightCache, Sim, SimConfig, Vec2,
    WeaponId,
};
use std::sync::Arc;

fn config() -> SimConfig {
    SimConfig {
        lifecycle: ArenaMode::Off,
        min_players: 1,
        ..SimConfig::live_content()
    }
}

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

fn placement(start: Vec2, weapon: WeaponId) -> [Placement; 1] {
    [Placement {
        profile: "raider".into(),
        faction: FactionId::Holy,
        bot: false,
        at: start,
        hp: None,
        weapon0: Some(weapon),
        weapon1: None,
    }]
}

fn dist(a: Vec2, b: Vec2) -> f64 {
    rh_sim::math::hypot(a.x - b.x, a.y - b.y)
}

/// 36 stacks (three camps of 12) of the stats the normal camps pay, a fixed walk (as `raid_elite.rs`).
fn banked(probe: &Sim) -> TrustedBuild {
    let buffs = probe.camp_buffs();
    let pays: Vec<MobTypeId> = probe
        .camp_rosters()
        .iter()
        .enumerate()
        .filter(|(_, r)| r.elite.is_none())
        .map(|(i, r)| buffs.get(i).copied().flatten().unwrap_or(r.mob_type))
        .collect();
    let mut stacks: Vec<(MobTypeId, u64)> = Vec::new();
    for k in 0..3usize {
        let stat = pays[(k * 7 + 3) % pays.len()];
        match stacks.iter_mut().find(|(m, _)| *m == stat) {
            Some(e) => e.1 += 12,
            None => stacks.push((stat, 12)),
        }
    }
    TrustedBuild {
        stacks,
        ..Default::default()
    }
}

/// `Prices::LOCAL` with the quest rewards zeroed and the elite death table cut to the Executioner row: every other kind
/// (the Gaoler too) is then priced by the executor's fallback (the farm's death model, `death_prior`'s no-row path),
/// which is what routes this scene's seat to camp 50's Gaoler (with the full table it prices the Gaoler at a 0.73
/// death rate and walks away; measured: the same seat roams). It is a scene control, not a behaviour under test.
fn treated() -> Prices {
    let mut p = Prices {
        reward_default: 0.,
        reward_rows: &[],
        ..Prices::LOCAL
    };
    p.elite.kind_death = &[("executioner", [0.656, 0.089, 0.022, 0.011])];
    p
}

/// What routes the seat to a normal camp: farm value up, elite goals off, rewards and chests priced at nothing (a scene
/// control, not a behaviour under test; with the table's prices the seats of seeds 1 and 5 roam to other camps).
fn prices_of(s: Scene) -> Prices {
    if s.elite {
        return treated();
    }
    let mut p = Prices {
        reward_default: 0.,
        reward_rows: &[],
        camp: 50.,
        chest: 0.,
        ..Prices::LOCAL
    };
    p.elite.on = false;
    p
}

/// One scene: lab `seed`, the seat's start and weapon, the camp watched and whether it is the camp's elite (or a normal
/// member) that counts.
#[derive(Clone, Copy)]
struct Scene {
    seed: u64,
    start: (f64, f64),
    weapon: WeaponId,
    camp: u32,
    elite: bool,
    ticks: u32,
}

#[derive(Debug, Default)]
struct Out {
    hits: u32,
    first_hit: Option<u32>,
    /// The longest run of ticks the seat stood within `NEAR_U` of a living full-hp mob of the watched camp and landed
    /// no hit on the camp.
    worst_stall: u32,
    /// Ticks within `NEAR_U` of a living full-hp mob of the watched camp.
    ticks_near: u32,
    seat_died_at: Option<u32>,
    final_pos: (f64, f64),
    fault: bool,
}

/// "Beside": the distance the brief's live traces use for a seat standing at a camp mob.
const NEAR_U: f64 = 60.;

fn run(s: Scene) -> Out {
    let arena = lab_arena(s.seed);
    let secrets = Secrets::from_seed(s.seed);
    let probe = Sim::reset_active(
        config(),
        arena.clone(),
        secrets,
        &placement(arena.centre(), s.weapon),
    );
    let build = banked(&probe);
    let start = Vec2::new(s.start.0, s.start.1);
    let mut sim = Sim::reset_active(
        config(),
        arena.clone(),
        secrets,
        &placement(start, s.weapon),
    );
    let me = sim.players()[0].id;
    rh_sim::trusted_build::apply(&mut sim, me, &build).expect("the declared stacks");
    let mut cp = serde_json::to_value(&sim).expect("a checkpoint");
    cp["world"]["players"][0]["potion_large"] = serde_json::json!(2);
    sim = Sim::restore(cp).expect("the declared stock is accepted");
    let policy: Box<dyn Policy> =
        Box::new(RaidBot::new(TierId::Normal, PersonaId::Farmer).with_prices(prices_of(s)));
    let ms = MatchStart::of(&sim, me, s.seed);
    let mut seats = vec![Seat::new(&ms, policy, Box::new(scripted::Idle))];
    seats[0].bind_fx(&sim).expect("a new seat binds");
    let mut sight = SightCache::new();
    let mut events: Vec<Event> = Vec::new();
    let mut o = Out::default();
    let mut streak = 0u32;
    for tick in 0..s.ticks {
        drive_seats_with(&mut sim, &mut seats, &events, &mut sight, |_, _| {});
        events.clear();
        sim.step_into(&mut events);
        feed_seats(&sim, &mut seats);
        let Some(p) = sim.player(me).filter(|p| p.standing()) else {
            o.seat_died_at = Some(tick);
            break;
        };
        o.final_pos = (p.pos().x, p.pos().y);
        let hit = events.iter().any(|e| match e {
            Event::Hit {
                victim,
                by: Some(b),
                ..
            } if *b == me => sim
                .mobs()
                .iter()
                .any(|m| m.core.id == *victim && m.camp == s.camp && m.elite == s.elite),
            _ => false,
        });
        if hit {
            o.hits += 1;
            o.first_hit.get_or_insert(tick);
        }
        let near = sim.mobs().iter().any(|m| {
            m.camp == s.camp
                && m.elite == s.elite
                && m.status == rh_sim::Status::Alive
                && m.core.vitals.hp >= m.core.vitals.max_hp * 0.999
                && dist(m.core.pos(), p.pos()) <= NEAR_U
        });
        if near {
            o.ticks_near += 1;
        }
        streak = if near && !hit { streak + 1 } else { 0 };
        o.worst_stall = o.worst_stall.max(streak);
    }
    o.fault = seats[0].fault.is_some();
    println!(
        "RUN seed {} camp {} {:?} from {:?}: {o:?}",
        s.seed, s.camp, s.weapon, s.start
    );
    o
}

/// The stall scenes (all normal camps; each seat is a human seat started in lock range of a member and routed to the
/// camp by `prices_of`). Measured at `coord/int-88` with `locks_from_here` reduced to the pre-P-RAID-REACH-1 rule
/// (`14f1a628e`: a ranged seat locks from anywhere inside `lock_range`, so `may_fight_from` is never asked): first hit
/// at tick 21 / 14 (seed 7 Wand / Bow), 19 / 25 (seed 1 Bow / Wand), 15 (seed 5 Bow); no tick spent within 60 u of a
/// full-hp member without hitting. At the head (`055d3a22f`): seed 7 never hits (2 379 stalled ticks, both weapons),
/// seed 1 first hits at tick 1 220 after 1 197 stalled ticks (both weapons), seed 5 Bow at tick 779 after 748.
const SCENES: [(u64, (f64, f64), u32, WeaponId); 5] = [
    (7, (536., 3952.), 51, WeaponId::Wand),
    (7, (536., 3952.), 51, WeaponId::Bow),
    (1, (3700., 3500.), 50, WeaponId::Wand),
    (1, (3700., 3500.), 50, WeaponId::Bow),
    (5, (1584., 3768.), 51, WeaponId::Bow),
];

/// The floors: a first hit within 600 ticks (30 s; the pre-packet seats need under 30), and never more than 100
/// consecutive ticks (5 s) within 60 u of a full-hp member of the camp without a hit (the pre-packet seats: 0). A seat
/// that walks to the band edge first spends a few seconds crossing the camp's surroundings, not beside a member.
fn assert_not_stalled(i: usize) {
    let (seed, start, camp, weapon) = SCENES[i];
    let o = run(Scene {
        seed,
        start,
        weapon,
        camp,
        elite: false,
        ticks: 2_400,
    });
    assert!(!o.fault, "no seat fault");
    assert!(
        o.first_hit.is_some_and(|t| t <= 600),
        "seed {seed} camp {camp} {weapon:?}: no hit on the camp within 600 ticks (first hit {:?}, {:?})",
        o.first_hit,
        o
    );
    assert!(
        o.worst_stall <= 100,
        "seed {seed} camp {camp} {weapon:?}: {} consecutive ticks beside a full-hp member without a hit ({o:?})",
        o.worst_stall
    );
}

/// RED at the head: the seat of seed 7 camp 51 (a camp whose three resting spots lie outside their own band at margin
/// 24) walks up to the member and never fights.
#[test]
fn a_wand_seat_at_seed_7_camp_51_hits_the_camp_and_does_not_stall() {
    assert_not_stalled(0);
}

/// RED at the head: the same for a Bow.
#[test]
fn a_bow_seat_at_seed_7_camp_51_hits_the_camp_and_does_not_stall() {
    assert_not_stalled(1);
}

/// RED at the head: seed 1 camp 50 (a camp of radius 224, three spots outside their band at margin 24).
#[test]
fn a_wand_seat_at_seed_1_camp_50_hits_the_camp_and_does_not_stall() {
    assert_not_stalled(2);
}

/// RED at the head.
#[test]
fn a_bow_seat_at_seed_1_camp_50_hits_the_camp_and_does_not_stall() {
    assert_not_stalled(3);
}

/// RED at the head: seed 5 camp 51, a Bow (748 stalled ticks).
#[test]
fn a_bow_seat_at_seed_5_camp_51_hits_the_camp_and_does_not_stall() {
    assert_not_stalled(4);
}

/// Measurement only (`-- --ignored --nocapture`): the scenes above, for the floors.
#[test]
#[ignore = "measurement"]
fn measure_the_stall_scenes() {
    for i in 0..SCENES.len() {
        let (seed, start, camp, weapon) = SCENES[i];
        run(Scene {
            seed,
            start,
            weapon,
            camp,
            elite: false,
            ticks: 2_400,
        });
    }
}
