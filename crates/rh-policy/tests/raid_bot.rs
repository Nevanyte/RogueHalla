//! P-RAID-0 oracle (loose proof, docs/backlog/packets/P-RAID-0.md): a `RaidBot` seated as a HUMAN (`bot: false`) on the
//! live content world with a quest board, started a short walk from a normal camp, clears that camp within 90 s of sim
//! time, sends only commands the sim accepts, and replays a seed exactly.
//!
//! The harness is the seat path `Env::advance` uses (`drive_seats_with` -> `Sim::step_into` -> `feed_seats`), so the
//! policy receives accepted `Frame14` deliveries, at a start chosen here (rh-headless seats only at the spawn list).
//! A `HumanBot` in the same seat is the control: it shows each scene is winnable for a fresh fighter.
use rh_policy::bots::{HumanBot, PersonaId, RaidBot, TierId};
use rh_policy::{drive_seats_with, feed_seats, scripted, MatchStart, Policy, Seat, SeatStats};
use rh_sim::mob_runtime::MobKind;
use rh_sim::{
    Arena, ArenaMode, Event, FactionId, ItemObserver, Observation, Placement, PlayerId, Secrets,
    SightCache, Sim, SimConfig, Vec2, WeaponId,
};
use std::sync::Arc;

/// 90 s of sim time at the 20 Hz step.
const MAX_TICKS: u32 = 1_800;
const SEEDS: std::ops::RangeInclusive<u64> = 1..=8;
/// A camp clear is a fight a fresh fighter can lose; at least this many of the 8 seeds must clear (the control clears
/// 8 of 8 on these scenes).
const CLEAR_FLOOR: usize = 7;
/// How far from the camp's members the fighter starts: a few seconds' walk.
const START_DIST: f64 = 256.;

fn map() -> Arena {
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

/// The live content world (quests and the Top 25 board on), with no match lifecycle so the run is active at once and
/// the fighter stands where it is placed.
fn config() -> SimConfig {
    SimConfig {
        lifecycle: ArenaMode::Off,
        min_players: 1,
        ..SimConfig::live_content()
    }
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

struct Scene {
    seed: u64,
    arena: Arc<Arena>,
    camp: u32,
    start: Vec2,
}

fn placement(start: Vec2) -> [Placement; 1] {
    [Placement {
        profile: "raider".into(),
        faction: FactionId::Holy,
        bot: false,
        at: start,
        hp: None,
        weapon0: Some(WeaponId::Sword),
        weapon1: None,
    }]
}

/// The first camp with no elite and exactly three regional members, and a start `START_DIST` from their centroid with
/// a straight floor line to it (the elite bench's way of picking a start).
fn scene(seed: u64) -> Scene {
    let secrets = Secrets::from_seed(seed);
    let arena = rh_sim::quests::board::map_admission::ensure(Arc::new(map()), secrets)
        .expect("a board-admitted map");
    let probe = Sim::reset_active(config(), arena.clone(), secrets, &placement(arena.centre()));
    for (ci, roster) in probe.camp_rosters().iter().enumerate() {
        if roster.elite.is_some() {
            continue;
        }
        let members: Vec<Vec2> = probe
            .mobs()
            .iter()
            .filter(|m| m.camp == ci as u32 && m.kind == MobKind::Regional && m.zombie.is_none())
            .map(|m| m.core.pos())
            .collect();
        if members.len() != 3 {
            continue;
        }
        let n = members.len() as f64;
        let centre = Vec2::new(
            members.iter().map(|p| p.x).sum::<f64>() / n,
            members.iter().map(|p| p.y).sum::<f64>() / n,
        );
        for k in 0..16 {
            let a = f64::from(k) * std::f64::consts::TAU / 16.;
            let start = Vec2::new(
                centre.x + rh_sim::math::cos(a) * START_DIST,
                centre.y + rh_sim::math::sin(a) * START_DIST,
            );
            if floor_line(&arena, start, centre) {
                return Scene {
                    seed,
                    arena,
                    camp: ci as u32,
                    start,
                };
            }
        }
    }
    panic!("seed {seed}: no three-member normal camp with a floor start");
}

fn world(s: &Scene) -> (Sim, PlayerId) {
    let sim = Sim::reset_active(
        config(),
        s.arena.clone(),
        Secrets::from_seed(s.seed),
        &placement(s.start),
    );
    let me = sim.players()[0].id;
    (sim, me)
}

#[derive(Debug)]
struct Run {
    /// The tick (from 0) whose step reported `CampCleared` for the scene camp.
    cleared: Option<u32>,
    stats: SeatStats,
    fault: Option<rh_policy::Fault>,
    hash: u64,
}

/// Seat `policy` for the scene's human fighter and step until the scene camp is cleared or `MAX_TICKS` have passed.
fn run(s: &Scene, policy: Box<dyn Policy>) -> Run {
    let (mut sim, me) = world(s);
    let start = MatchStart::of(&sim, me, s.seed);
    let mut seats = vec![Seat::new(&start, policy, Box::new(scripted::Idle))];
    seats[0].bind_fx(&sim).expect("a new seat binds");
    let mut sight = SightCache::new();
    let mut events: Vec<Event> = Vec::new();
    let mut cleared = None;
    for tick in 0..MAX_TICKS {
        drive_seats_with(&mut sim, &mut seats, &events, &mut sight, |_, _| {});
        events.clear();
        sim.step_into(&mut events);
        feed_seats(&sim, &mut seats);
        if events
            .iter()
            .any(|e| matches!(e, Event::CampCleared { camp } if *camp == s.camp))
        {
            cleared = Some(tick);
            break;
        }
    }
    Run {
        cleared,
        stats: seats[0].stats.clone(),
        fault: seats[0].fault.clone(),
        hash: sim.state_hash(),
    }
}

fn raid() -> Box<dyn Policy> {
    // This test isolates the farm executor: a farm-only price table (quest rewards and augments worth nothing) keeps
    // executors registered later from changing what it measures.
    Box::new(
        RaidBot::new(TierId::Normal, PersonaId::Farmer)
            .with_prices(rh_policy::bots::raid::values::Prices::farm_only()),
    )
}

fn human() -> Box<dyn Policy> {
    Box::new(HumanBot::new(TierId::Normal, PersonaId::Farmer))
}

#[test]
fn raid_bot_scene_is_a_human_seat_on_a_board_world_a_human_bot_can_clear() {
    let mut cleared = Vec::new();
    for seed in SEEDS {
        let s = scene(seed);
        let (sim, me) = world(&s);
        assert!(
            !sim.player(me).expect("seated").seat.bot,
            "seed {seed}: the fighter is a human seat"
        );
        let mut observer = ItemObserver::new(me);
        let mut obs = Observation::default();
        drop(
            observer
                .prepare_into(&sim, &[], &mut obs, None)
                .expect("an observation"),
        );
        assert!(
            obs.quests.as_ref().is_some_and(|q| !q.rows.is_empty()),
            "seed {seed}: a human seat on this world sees the quest board"
        );
        let control = run(&s, human());
        if control.cleared.is_some() {
            cleared.push(seed);
        }
    }
    assert!(
        cleared.len() >= CLEAR_FLOOR,
        "control: HumanBot cleared the scene camp on {cleared:?}, fewer than {CLEAR_FLOOR} of 8 seeds: the scene, not \
         the RaidBot, is wrong"
    );
}

#[test]
fn raid_bot_clears_a_nearby_normal_camp_within_90_s() {
    let mut cleared = Vec::new();
    let mut runs = Vec::new();
    for seed in SEEDS {
        let r = run(&scene(seed), raid());
        if r.cleared.is_some() {
            cleared.push(seed);
        }
        runs.push((seed, r));
    }
    assert!(
        cleared.len() >= CLEAR_FLOOR,
        "RaidBot cleared the scene camp within {MAX_TICKS} ticks on seeds {cleared:?}, fewer than {CLEAR_FLOOR} of 8: \
         {runs:#?}"
    );
}

#[test]
fn raid_bot_sends_only_commands_the_sim_accepts() {
    for seed in SEEDS {
        let r = run(&scene(seed), raid());
        assert!(r.fault.is_none(), "seed {seed}: no seat fault: {r:?}");
        assert_eq!(
            r.stats.fallback_decisions, 0,
            "seed {seed}: the policy decided every turn itself: {r:?}"
        );
        assert!(r.stats.proposed > 0, "seed {seed}: the bot acts: {r:?}");
        assert_eq!(
            r.stats.rejected, 0,
            "seed {seed}: every proposal resolved to a handle of its frame and Sim::submit took it (no stale handle, \
             no over-budget order): {r:?}"
        );
        assert_eq!(r.stats.accepted, r.stats.proposed, "seed {seed}: {r:?}");
    }
}

#[test]
fn raid_bot_replays_a_seed_exactly() {
    let s = scene(1);
    let a = run(&s, raid());
    let b = run(&s, raid());
    // A bot that does nothing replays trivially: the run must have acted (whether seed 1 clears is the floor's business).
    assert!(a.stats.proposed > 0, "seed 1: the replayed run acts: {a:?}");
    assert_eq!(a.cleared, b.cleared, "seed 1: the same clear tick");
    assert_eq!(a.stats, b.stats, "seed 1: the same proposals");
    assert_eq!(a.hash, b.hash, "seed 1: the same final state hash");
}
