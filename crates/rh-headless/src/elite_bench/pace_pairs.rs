//! Matched solo encounters. Each camp keeps its full natural roster. A pair shares the
//! fighter, farm kit, weapon, raid age, zone and (when available) the normal mob type.
use super::*;
use serde_json::json;

pub(super) fn run(seed: u64) -> Result<Output, String> {
    let arena = Arc::new(rh_headless::balance_lab::generated_arena(seed)?);
    let probe = Sim::reset_active(
        config(),
        arena.clone(),
        Secrets::from_seed(seed),
        &[Placement {
            profile: "probe".into(),
            faction: FactionId::Fire,
            bot: false,
            at: arena.centre(),
            hp: None,
            weapon0: None,
            weapon1: None,
        }],
    );
    let rosters = probe.camp_rosters();
    let mut rows = Vec::new();
    for (index, elite) in rosters
        .iter()
        .enumerate()
        .filter(|(_, r)| r.elite.is_some())
    {
        let boss = elite.elite.expect("selected an elite camp");
        let Some(elite_scene) = scene_at(&probe, &arena, index as u32) else {
            continue;
        };
        let zone = rh_sim::mob_catalog::mob_type_by_id(boss.as_str())
            .expect("catalog boss")
            .zone;
        // Some generated zones use several elite sites. Sample one site of each boss per seed.
        if rosters[..index].iter().any(|r| r.elite == Some(boss)) {
            continue;
        }
        let normal = rosters
            .iter()
            .enumerate()
            .filter(|(_, r)| r.elite.is_none() && normal_types_of(zone).any(|t| t.id == r.mob_type))
            .filter_map(|(i, r)| scene_at(&probe, &arena, i as u32).map(|s| (r, s)))
            .min_by_key(|(r, s)| (r.mob_type != elite.mob_type, s.camp))
            .ok_or("missing same-zone normal control")?;
        let faction = FactionId::ALL[(index + seed as usize) % FactionId::ALL.len()];
        for (minute, camps) in [(0, 6), (6, 9), (10, 15), (15, 20), (20, 25)] {
            let mut build = banked_camp_build(&probe, seed, camps);
            if minute >= 15 {
                build.cards = vec![(CardId::MachineGun, 1), (CardId::Committed, 1)];
            }
            for weapon in [WeaponId::Sword, WeaponId::Bow] {
                for (label, scene) in [("normal", &normal.1), ("elite", &elite_scene)] {
                    let m = fight(
                        &arena,
                        seed,
                        faction,
                        weapon,
                        scene,
                        Some(&build),
                        7_200,
                        Who::Duelist,
                        Some(minute),
                    )?;
                    rows.push(json!({"seed":seed, "minute":minute,"camp_type":label,"camp":scene.camp,
                        "boss":boss.as_str(),"elite_base":elite.mob_type.as_str(),"normal_base":normal.0.mob_type.as_str(),
                        "zone":zone.as_str(),"faction":faction.as_str(),"weapon":weapon.as_str(),"camps":camps,
                        "outcome":m.outcome,"first_any_hit_tick":m.first_hit,"first_hit_tick":m.first_camp_hit,"end_tick":m.ticks,
                        "clear_s":m.first_camp_hit.filter(|_| m.outcome=="cleared").map(|t| f64::from(m.ticks-t)*0.025)}));
                }
            }
        }
    }
    Ok(Output {
        json: serde_json::to_string(&rows).map_err(|e| e.to_string())?,
        table: String::new(),
    })
}
