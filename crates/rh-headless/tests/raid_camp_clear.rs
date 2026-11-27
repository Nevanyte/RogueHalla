//! Local paired camp pacing measurement. Run with cargo test --release -p rh-headless
//! --features elite-bench --test raid_camp_clear -- --nocapture.
#![cfg(all(feature = "elite-bench", not(debug_assertions)))]

use rh_sim::events::Event;
use rh_sim::geometry::{has_line_of_sight, segment_clear};
use rh_sim::map::{MASK_MELEE, MASK_MOVE};
use rh_sim::mob_catalog::MobTypeId;
use rh_sim::mob_runtime::MobKind;
use rh_sim::trusted_build::{self, TrustedBuild};
use rh_sim::{
    Arena, ArenaMode, CombatPace, FactionId, Placement, PlayerId, RawCommand, Secrets, Sim,
    SimConfig, Status, Vec2, WeaponId,
};
use std::sync::Arc;

const SEED: u64 = 7;
const LIMIT_MS: f64 = 90_000.;

#[derive(Clone, Copy)]
struct Scene {
    camp: u32,
    start: Vec2,
}

fn config(pace: CombatPace) -> SimConfig {
    SimConfig {
        lifecycle: ArenaMode::Off,
        combat_pace: pace,
        abilities: rh_sim::abilities::Mode::Off,
        hazards: rh_sim::terrain::Mode::Off,
        quests: false,
        regen: false,
        ..SimConfig::live_content()
    }
}

fn room(arena: Arc<Arena>, pace: CombatPace, faction: FactionId, start: Vec2) -> Sim {
    Sim::reset_active(
        config(pace),
        arena,
        Secrets::from_seed(SEED),
        &[Placement {
            profile: "camp-bench-seat".into(),
            faction,
            bot: false,
            at: start,
            hp: None,
            weapon0: Some(WeaponId::Sword),
            weapon1: None,
        }],
    )
}

fn distance(a: Vec2, b: Vec2) -> f64 {
    (a.x - b.x).hypot(a.y - b.y)
}

fn scenes(arena: &Arc<Arena>) -> Vec<Scene> {
    let probe = room(
        arena.clone(),
        CombatPace::Legacy,
        FactionId::Holy,
        arena.spawns[0],
    );
    [FactionId::Fire, FactionId::Frost, FactionId::Holy]
        .into_iter()
        .map(|zone| {
            probe
                .mobs()
                .iter()
                .filter(|m| {
                    m.kind == MobKind::Regional
                        && !m.elite
                        && m.core.faction_id() == Some(zone)
                        && probe.camp_rosters()[m.camp as usize].elite.is_none()
                        && probe.camp_rosters()[m.camp as usize].alive >= 2
                })
                .find_map(|m| {
                    let home = m.home;
                    let start = (0..16).find_map(|n| {
                        let a = f64::from(n) * std::f64::consts::TAU / 16.;
                        let at = Vec2::new(home.x + a.cos() * 96., home.y + a.sin() * 96.);
                        segment_clear(&arena.map, home, at, 12., MASK_MOVE).then_some(at)
                    })?;
                    Some(Scene {
                        camp: m.camp,
                        start,
                    })
                })
                .expect("a normal camp in each zone with a walkable approach")
        })
        .collect()
}

fn submit(sim: &mut Sim, id: PlayerId, cmd: RawCommand) {
    sim.submit(id, rh_sim::validate(cmd).expect("script command validates"))
        .expect("script command submits");
}

#[derive(Debug)]
struct Fight {
    outcome: &'static str,
    contact_s: Option<f64>,
    clear_s: Option<f64>,
    first_step_deaths: usize,
    members: usize,
}

fn fight(mut sim: Sim, scene: Scene, build: &TrustedBuild) -> Fight {
    let owner = sim.players()[0].id;
    trusted_build::apply(&mut sim, owner, build).expect("bench build is legal");
    let begin = sim.server_time();
    let mut untouched: Vec<_> = sim
        .mobs()
        .iter()
        .filter(|m| m.camp == scene.camp && m.status == Status::Alive && m.owner.is_none())
        .map(|m| (m.core.id, m.core.vitals.hp))
        .collect();
    assert!(untouched.len() >= 2);
    assert_eq!(
        untouched.len(),
        sim.camp_rosters()[scene.camp as usize].alive as usize
    );
    let mut result = Fight {
        outcome: "timeout",
        contact_s: None,
        clear_s: None,
        first_step_deaths: 0,
        members: untouched.len(),
    };
    let mut events = Vec::new();
    while sim.server_time() - begin < LIMIT_MS {
        // The regular order budget is shared by move and attack. Keep the scripted cadence
        // below it and let the held trigger swing between decisions.
        if (sim.server_time() - begin) as u32 % 250 == 0 {
            let player = &sim.players()[0];
            let at = player.pos();
            let nearest = sim
                .mobs()
                .iter()
                .filter(|m| m.camp == scene.camp && m.status == Status::Alive && m.owner.is_none())
                .min_by(|a, b| {
                    distance(at, a.core.pos())
                        .total_cmp(&distance(at, b.core.pos()))
                        .then(a.core.id.cmp(&b.core.id))
                });
            let to = nearest
                .expect("a live camp member until full clear")
                .core
                .pos();
            let a = (to.y - at.y).atan2(to.x - at.x);
            if distance(at, to) <= 85. && has_line_of_sight(sim.collision_map(), at, to, MASK_MELEE)
            {
                submit(&mut sim, owner, RawCommand::Attack { a, t: None });
            } else {
                submit(
                    &mut sim,
                    owner,
                    RawCommand::Move {
                        x: to.x,
                        y: to.y,
                        seq: None,
                    },
                );
            }
        }
        sim.step_into(&mut events);
        let elapsed_s = (sim.server_time() - begin) / 1000.;
        untouched.retain(|&(id, hp)| {
            let mob = sim.mobs().iter().find(|mob| mob.core.id == id);
            let Some(mob) = mob.filter(|mob| mob.status == Status::Alive) else {
                result.contact_s.get_or_insert(elapsed_s);
                result.first_step_deaths += 1;
                return false;
            };
            if mob.core.vitals.hp < hp {
                result.contact_s.get_or_insert(elapsed_s);
                return false;
            }
            true
        });
        if sim.camp_rosters()[scene.camp as usize].alive == 0 {
            assert!(events
                .iter()
                .any(|e| matches!(e, Event::CampCleared { camp } if *camp == scene.camp)));
            result.outcome = "clear";
            result.clear_s = result.contact_s.map(|first| elapsed_s - first);
            break;
        }
        if !sim.players()[0].standing() {
            result.outcome = "died";
            break;
        }
    }
    result
}

fn build(damage: u64, speed: u64) -> TrustedBuild {
    use MobTypeId::{Bailiff, EmberImp, MagmaBrute, PlagueRat};
    TrustedBuild {
        stacks: vec![
            (EmberImp, damage),
            (PlagueRat, speed),
            (Bailiff, 30),
            (MagmaBrute, 35),
        ],
        ..TrustedBuild::default()
    }
}

#[test]
fn compare_normal_camp_clears_at_four_raid_ages() {
    let arena = Arc::new(
        rh_headless::load_arena(concat!(
            env!("CARGO_MANIFEST_DIR"),
            "/../rh-sim/src/mapdata/fixtures/seed1.map.json"
        ))
        .expect("regional arena"),
    );
    let scenes = scenes(&arena);
    let mut rows = Vec::new();
    std::thread::scope(|scope| {
        let mut jobs = Vec::new();
        for scene in scenes {
            for faction in [FactionId::Fire, FactionId::Frost, FactionId::Holy] {
                let arena = arena.clone();
                jobs.push(scope.spawn(move || {
                    let mut rows = Vec::new();
                    let mut controls = Vec::new();
                    for pace in [CombatPace::Legacy, CombatPace::Raid] {
                        let mut idle = room(arena.clone(), pace, faction, scene.start);
                        for (index, minute) in [0, 5, 10, 20].into_iter().enumerate() {
                            while idle.server_time() - idle.raid_started_at()
                                < f64::from(minute) * 60_000.
                            {
                                idle.step();
                            }
                            let seat = &idle.players()[0];
                            let members: Vec<_> = idle
                                .mobs()
                                .iter()
                                .filter(|m| m.camp == scene.camp)
                                .map(|m| (m.core.id, m.core.pos(), m.status, m.core.vitals.hp))
                                .collect();
                            assert!(seat.standing());
                            let start = (seat.pos(), seat.vitals.hp, members);
                            if pace == CombatPace::Legacy {
                                controls.push(start);
                            } else {
                                assert_eq!(start, controls[index], "paired starting state");
                            }
                            for (name, build) in
                                [("ordinary", build(20, 10)), ("offense", build(85, 30))]
                            {
                                let result = fight(idle.clone(), scene, &build);
                                rows.push((scene.camp, faction, pace, minute, name, result));
                            }
                        }
                    }
                    rows
                }));
            }
        }
        for job in jobs {
            rows.extend(job.join().expect("camp comparison completed"));
        }
    });
    println!("camp,faction,pace,minute,build,outcome,contact_s,contact_to_clear_s,first_damage_step_deaths,members");
    for (camp, faction, pace, minute, name, result) in &rows {
        let contact = result
            .contact_s
            .map_or(String::new(), |n| format!("{n:.2}"));
        let clear = result.clear_s.map_or(String::new(), |n| format!("{n:.2}"));
        println!(
            "{camp},{faction:?},{pace:?},{minute},{name},{},{contact},{clear},{},{}",
            result.outcome, result.first_step_deaths, result.members
        );
    }
    assert_eq!(rows.len(), 144);
    assert!(rows
        .iter()
        .all(|(_, _, _, _, _, result)| result.contact_s.is_some()));
    assert!(rows
        .iter()
        .any(|(_, _, _, _, _, result)| result.clear_s.is_some()));
}
