//! P-RAID-LOS-1 oracle, live scenes (docs/backlog/packets/P-RAID-LOS-1.md). Read-only for the implementer.
//!
//! A ranged RaidBot seat (a human seat, `bot: false`) must not go on shooting at a camp member it has no line of sight
//! to. The sim stops a projectile at the first `MASK_SIGHT` wall tile on its centre line (`systems/projectiles.rs`
//! `raycast`, origin = the shooter's position), the same mask as `RoomPathService::los`; a seat whose lock stands on a
//! target behind a wall sends arrows or bolts into that wall for as long as the fight lasts.
//!
//! The effect is read off the sim: a projectile of the seat that ends on `ProjectileEnd::Wall`, counted in a streak that
//! a hit of the seat on anything resets. The seat is a Farm seat priced to farm (camp price 50, elite goals off, rewards
//! and chests at nothing), a banked build as `raid_reach_fix.rs`, started at the arena centre (and once at the named
//! scene), 4 800 ticks, every command through `validate` + `Sim::submit`. Release: `cargo test --release -p rh-policy
//! --test raid_los`.
use rh_policy::bots::raid::values::Prices;
use rh_policy::bots::{PersonaId, RaidBot, TierId};
use rh_policy::{drive_seats_with, feed_seats, scripted, MatchStart, Policy, Seat};
use rh_sim::mob_catalog::MobTypeId;
use rh_sim::trusted_build::TrustedBuild;
use rh_sim::{
    Arena, ArenaMode, Event, FactionId, Placement, ProjectileEnd, Secrets, SightCache, Sim,
    SimConfig, Vec2, WeaponId,
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

#[derive(Debug, Default)]
struct Out {
    /// Projectiles of the seat that ended on a wall.
    wall_ends: u32,
    /// The most wall ends of the seat with no hit of the seat on anything between them.
    worst_wall_streak: u32,
    hits: u32,
    fault: bool,
}

const TICKS: u32 = 4_800;
/// The most wall ends in a row (no hit between) a seat may have: a mob that steps behind a corner as the arrow flies
/// costs one or two; the defect costs 18 to 84 at the base.
const MAX_STREAK: u32 = 6;
/// Lab seeds of the sweep, each with a Bow and a Wand from the arena centre; the named scene is run besides.
const SEEDS: std::ops::RangeInclusive<u64> = 1..=10;

fn run(seed: u64, start: Option<(f64, f64)>, weapon: WeaponId) -> Out {
    let arena = lab_arena(seed);
    let secrets = Secrets::from_seed(seed);
    let probe = Sim::reset_active(
        config(),
        arena.clone(),
        secrets,
        &placement(arena.centre(), weapon),
    );
    let build = banked(&probe);
    let at = start.map_or(arena.centre(), |(x, y)| Vec2::new(x, y));
    let mut sim = Sim::reset_active(config(), arena.clone(), secrets, &placement(at, weapon));
    let me = sim.players()[0].id;
    rh_sim::trusted_build::apply(&mut sim, me, &build).expect("the declared stacks");
    let mut cp = serde_json::to_value(&sim).expect("a checkpoint");
    cp["world"]["players"][0]["potion_large"] = serde_json::json!(2);
    sim = Sim::restore(cp).expect("the declared stock is accepted");
    let mut prices = Prices {
        reward_default: 0.,
        reward_rows: &[],
        camp: 50.,
        chest: 0.,
        ..Prices::LOCAL
    };
    prices.elite.on = false;
    let policy: Box<dyn Policy> =
        Box::new(RaidBot::new(TierId::Normal, PersonaId::Farmer).with_prices(prices));
    let ms = MatchStart::of(&sim, me, seed);
    let mut seats = vec![Seat::new(&ms, policy, Box::new(scripted::Idle))];
    seats[0].bind_fx(&sim).expect("a new seat binds");
    let mut sight = SightCache::new();
    let mut events: Vec<Event> = Vec::new();
    let mut o = Out::default();
    let mut streak = 0u32;
    for _ in 0..TICKS {
        drive_seats_with(&mut sim, &mut seats, &events, &mut sight, |_, _| {});
        events.clear();
        sim.step_into(&mut events);
        feed_seats(&sim, &mut seats);
        if sim.player(me).filter(|p| p.standing()).is_none() {
            break;
        }
        for e in &events {
            match e {
                Event::ProjectileEnded { launch, reason, .. }
                    if launch.owner == me && *reason == ProjectileEnd::Wall =>
                {
                    o.wall_ends += 1;
                    streak += 1;
                    o.worst_wall_streak = o.worst_wall_streak.max(streak);
                }
                Event::Hit { by: Some(b), .. } if *b == me => {
                    o.hits += 1;
                    streak = 0;
                }
                _ => {}
            }
        }
    }
    o.fault = seats[0].fault.is_some();
    println!("RUN seed {seed} {weapon:?} from {start:?}: {o:?}");
    o
}

/// One run of the sweep: seed, weapon, start (`None`: the arena centre), result.
type RunOf = (u64, WeaponId, Option<(f64, f64)>, Out);

fn all_runs() -> Vec<RunOf> {
    let mut runs = Vec::new();
    for seed in SEEDS {
        for weapon in [WeaponId::Bow, WeaponId::Wand] {
            runs.push((seed, weapon, None));
        }
    }
    runs.push((1, WeaponId::Bow, Some((3700., 3500.))));
    runs.into_iter()
        .map(|(seed, weapon, start)| (seed, weapon, start, run(seed, start, weapon)))
        .collect()
}

/// Measured at the base `coord/int-89` @ `7bc23f775` (`-- --ignored --nocapture measure`), worst streak Bow / Wand by
/// seed: 1: 26 / 0, 2: 18 / 33, 3: 0 / 1, 4: 0 / 0, 5: 0 / 0, 6: 1 / 0, 7: 1 / 0, 8: 0 / 0, 9: 0 / 84, 10: 3 / 0; the
/// named scene (seed 1, Bow, from (3700, 3500)): 59. Four sweep runs and the scene exceed 6. Hits over the 20 sweep
/// runs: 4 330; 18 of 20 have at least 10; the scene 173.
#[test]
fn a_ranged_seat_does_not_go_on_shooting_at_a_target_behind_a_wall() {
    let outs = all_runs();
    assert!(outs.iter().all(|r| !r.3.fault), "no seat fault");
    let bad: Vec<String> = outs
        .iter()
        .filter(|r| r.3.worst_wall_streak > MAX_STREAK)
        .map(|(s, w, st, o)| format!("seed {s} {w:?} {st:?}: {o:?}"))
        .collect();
    assert!(
        bad.is_empty(),
        "a seat kept shooting into a wall, over {MAX_STREAK} in a row: {bad:#?}"
    );
    // The seat still fights: the sweep does not pass by never shooting.
    let sweep = &outs[..outs.len() - 1];
    let hits: u32 = sweep.iter().map(|r| r.3.hits).sum();
    let fighting = sweep.iter().filter(|r| r.3.hits >= 10).count();
    assert!(
        hits >= 3_000,
        "{hits} hits over the sweep: the seats stopped fighting"
    );
    assert!(fighting >= 15, "only {fighting} of 20 runs landed 10 hits");
    assert!(
        outs.last().is_some_and(|r| r.3.hits >= 20),
        "the named scene's seat landed fewer than 20 hits"
    );
}

/// Measurement only (`-- --ignored --nocapture`): the runs above.
#[test]
#[ignore = "measurement"]
fn measure() {
    all_runs();
}
