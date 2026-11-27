//! A solo player's next farming decision from the same map position and build.
//! The destination is chosen before either fight. Award and elapsed time come from the simulator.
use super::*;
use rh_policy::bots::build_route::{type_weight, BUILD_GOALS, CLEAR_MS_PER_MOB};
use rh_sim::pathfind::{plan_path, Pathfinder};
use serde_json::json;

pub(super) fn run(seed: u64) -> Result<Output, String> {
    let arena = Arc::new(rh_headless::balance_lab::generated_arena(seed)?);
    let probe = super::pace_pairs::probe(&arena, seed);
    let inventory = banked_camp_build(&probe, seed, 15);
    let mut scenes: Vec<_> = probe
        .camp_rosters()
        .iter()
        .enumerate()
        .filter(|(_, r)| r.elite.is_none())
        .filter_map(|(i, r)| {
            scene_at(&probe, &arena, i as u32)
                .map(|s| (s, r.alive, probe.camp_buffs()[i].unwrap_or(r.mob_type)))
        })
        .collect();
    let weapons = [
        [WeaponId::Sword, WeaponId::Shield],
        [WeaponId::Sword, WeaponId::Bow],
        [WeaponId::Wand, WeaponId::Wand],
        [WeaponId::Shield, WeaponId::Bow],
        [WeaponId::Axe, WeaponId::Wand],
    ];
    let mut pathfinder = Pathfinder::default();
    let mut rows = Vec::new();
    for (fi, faction) in FactionId::ALL.into_iter().enumerate() {
        for (bi, goal) in BUILD_GOALS[fi].iter().enumerate() {
            let from = arena.spawns[(seed as usize + 2 * fi + bi) % arena.spawns.len()];
            let mut choices = Vec::new();
            for (i, (scene, alive, paid)) in scenes.iter().enumerate() {
                let path = plan_path(&mut pathfinder, &arena.map, from, scene.centre);
                if path.is_empty() {
                    continue;
                }
                let mut at = from;
                let mut length = 0.;
                for v in path {
                    length += (at.x - v.x).hypot(at.y - v.y);
                    at = v;
                }
                let walk = length / rh_sim::constants::PLAYER_SPEED;
                let score = 3. * f64::from(*alive) * type_weight(goal, Some(paid.as_str()))
                    / (walk + f64::from(*alive) * CLEAR_MS_PER_MOB / 1000.);
                choices.push((i, walk, score));
            }
            choices.sort_by(|a, b| a.1.total_cmp(&b.1).then(a.0.cmp(&b.0)));
            let nearest = choices
                .first()
                .copied()
                .ok_or("no reachable ordinary camp")?;
            // Cap the prospective detour at ten seconds and travel only for a forecast gain of at least 25%.
            let best = choices
                .iter()
                .copied()
                .filter(|c| c.1 <= nearest.1 + 10.)
                .max_by(|a, b| a.2.total_cmp(&b.2).then(b.0.cmp(&a.0)))
                .unwrap();
            let target = if best.2 >= nearest.2 * 1.25 {
                best
            } else {
                nearest
            };
            for (arm, choice) in [("nearest", nearest), ("target", target)] {
                let (scene, alive, paid) = &mut scenes[choice.0];
                scene.start = from;
                let result = fight(
                    &arena,
                    seed,
                    faction,
                    weapons[fi][bi],
                    scene,
                    Some(&inventory),
                    3600,
                    Who::Duelist,
                    Some(10),
                )?;
                let value: f64 = result
                    .earned
                    .iter()
                    .map(|(mob, n)| *n as f64 * type_weight(goal, Some(mob.as_str())))
                    .sum();
                rows.push(json!({"seed":seed,"faction":faction.as_str(),"build":goal.id,"arm":arm,"chosen_distinct":nearest.0!=target.0,
                    "camp":scene.camp,"reward":paid.as_str(),"mobs_at_start":alive,"walk_forecast_s":choice.1,"value_forecast_per_s":choice.2,
                    "elapsed_s":f64::from(result.ticks)*0.025,"first_camp_hit_s":result.first_camp_hit.map(|t|f64::from(t)*0.025),
                    "initial_stacks":inventory.stacks,"credited_clears":result.credited_clears,
                    "outcome":result.outcome,"earned":result.earned,"earned_value":value,"start":[from.x,from.y]}));
            }
        }
    }
    Ok(Output {
        json: serde_json::to_string(&rows).map_err(|e| e.to_string())?,
        table: String::new(),
    })
}
