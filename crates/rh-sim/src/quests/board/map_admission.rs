//! Map admission for a Top 25 raid. The public seed and geometry can change
//! across candidate attempts. The match's private quest draw cannot.
use super::attempts::targets;
use super::catalog::QuestId;
use super::placement;
use crate::map::world_to_tile;
use crate::mapdata::{GameMapData, MapExport};
use crate::mapgen::{map_hash, pipeline};
use crate::private_rng::Sfc32;
use crate::statics::Arena;
use crate::Secrets;
use std::sync::Arc;

/// Cold admission for direct simulation and headless callers. Reuse an
/// already-admitted allocation. Repair the supplied geometry when possible
/// and only try another attempt when the selected quests cannot fit it.
pub fn ensure(arena: Arc<Arena>, secrets: Secrets) -> Result<Arc<Arena>, String> {
    if needs_repair(&arena, secrets) == Ok(false) {
        return Ok(arena);
    }
    let layers = arena
        .map
        .layers()
        .ok_or("quest board requires admitted map layers")?;
    let data = layers.data();
    let source = layers
        .to_export(&arena.map.tiles)
        .map_err(|e| e.to_string())?;
    if let Ok(export) = prepare(source, secrets) {
        let mut repaired = into_arena(export)?;
        if repaired.spawns != arena.spawns {
            let canonical = std::mem::replace(&mut repaired.spawns, arena.spawns.clone());
            if plan(&repaired, secrets).is_err() {
                repaired.spawns = canonical;
            }
        }
        return Ok(Arc::new(repaired));
    }
    let layout = crate::mapgen::coliseum::fixed_layout();
    let generator = pipeline::Pipeline::new(&layout);
    let options = pipeline::Options {
        decor: false,
        signature: data
            .features
            .iter()
            .any(|f| f.kind == crate::mapdata::FeatureKind::Volcano),
        ..Default::default()
    };
    let export = generate(&generator, data.seed, 0..24, options, secrets)?;
    Ok(Arc::new(into_arena(export)?))
}

fn into_arena(export: MapExport) -> Result<Arena, String> {
    export
        .validate()
        .map_err(|e| e.to_string())?
        .into_arena()
        .map_err(|e| e.to_string())
}

/// Return whether a validated map needs a terrain repair for the selected board.
/// An unsuitable landmark layout returns the placement error so the caller can
/// request another geometry using the same secrets.
pub fn needs_repair(arena: &Arena, secrets: Secrets) -> Result<bool, String> {
    let sites = plan(arena, secrets)?;
    let data = arena
        .map
        .layers()
        .ok_or("admitted board map lacks layers")?
        .data();
    Ok(!encounters_match(data, &sites) || !unlit_bays(data, &sites).is_empty())
}

fn encounters_match(data: &crate::mapdata::LayerData, sites: &[placement::BoardSite]) -> bool {
    let mut requirements = sites.iter().filter_map(|site| {
        let (count, elite_spot) = match site.quest {
            QuestId::TheWarden => (1, 0),
            QuestId::RimeHunt => (3, -1),
            _ => return None,
        };
        Some((site, count, elite_spot))
    });
    let camps = data
        .camps
        .iter()
        .filter(|camp| targets::is_target_camp(&camp.id));
    if camps.clone().count() != requirements.clone().count() {
        return false;
    }
    requirements.all(|(site, count, elite_spot)| {
        camps.clone().any(|camp| {
            targets::tagged(&camp.id) == Some(site.quest)
                && camp.zone.code() == site.quest.home().code() + 1
                && camp.spots.len() == count
                && camp.elite_spot == elite_spot
                && camp
                    .spots
                    .iter()
                    .zip(&site.objectives)
                    .all(|(spot, at)| spot.x == at.x && spot.y == at.y)
        })
    })
}

fn plan(arena: &Arena, secrets: Secrets) -> Result<Vec<placement::BoardSite>, String> {
    let mut random = Sfc32::from_words(crate::quests::live::quest_salt(secrets.reward_salt));
    if arena.spawns.is_empty() {
        return Err("quest board requires a fallback spawn".into());
    }
    placement::plan_with_fallback(&arena.map, &arena.spawns, &mut || random.next_f64())
}

fn unlit_bays(data: &crate::mapdata::LayerData, sites: &[placement::BoardSite]) -> Vec<usize> {
    let Some(pilgrim) = sites
        .iter()
        .find(|site| site.quest == QuestId::PilgrimsPath)
    else {
        return Vec::new();
    };
    pilgrim
        .objectives
        .iter()
        .skip(2)
        .filter_map(|at| {
            let x = world_to_tile(at.x) as usize;
            let y = world_to_tile(at.y) as usize;
            let i = y * data.w as usize + x;
            (data.terrain[i] == 0).then_some(i)
        })
        .collect()
}

/// Validate a candidate before admitting it, retaining its signature and dress
/// metadata for the subsequent Welcome map bake.
pub fn prepare(mut export: MapExport, secrets: Secrets) -> Result<MapExport, String> {
    let arena = into_arena(export.clone())?;
    let sites = plan(&arena, secrets)?;
    let data = arena
        .map
        .layers()
        .ok_or("admitted board map lacks layers")?
        .data();
    let bays = unlit_bays(data, &sites);
    // The signature Hall's decorative nave marks become healing light in the
    // selected quest. Legacy-styled bays already have healing terrain.
    for &i in &bays {
        export.map.terrain[i] = 7;
    }
    let prior_camp_count = export.map.camps.len();
    targets::append_target_camps(&mut export, &sites)?;
    if !bays.is_empty() || export.map.camps.len() != prior_camp_count {
        export.legacy_map_hash_u32 = computed_hash(&export.map);
        let repaired = into_arena(export.clone())?;
        if plan(&repaired, secrets)? != sites {
            return Err("quest map repair moved a reserved board site".into());
        }
        if !encounters_match(
            repaired
                .map
                .layers()
                .ok_or("repaired board map lacks layers")?
                .data(),
            &sites,
        ) {
            return Err("repaired map lacks its planned quest encounters".into());
        }
    } else if !encounters_match(data, &sites) {
        return Err("map contains encounters from a different quest board".into());
    }
    Ok(export)
}

/// Try consecutive attempts for this public seed up to a caller-owned bound.
/// Each geometry uses the same reward salt, even if the generator refuses
/// an attempt or a selected quest's landmark cannot be reached.
pub fn generate(
    generator: &pipeline::Pipeline<'_>,
    seed: i64,
    attempts: std::ops::Range<u64>,
    options: pipeline::Options,
    secrets: Secrets,
) -> Result<MapExport, String> {
    let mut reason = String::from("no attempted map passed generator certification");
    for attempt in attempts {
        if let Some(map) = generator
            .styled_attempt(seed, attempt, options)
            .map_err(|e| format!("{e:?}"))?
        {
            let result = prepare(
                MapExport {
                    format: "rh-map/2".into(),
                    legacy_map_hash_u32: computed_hash(&map),
                    map,
                },
                secrets,
            );
            match result {
                Ok(export) => return Ok(export),
                Err(error) => reason = error,
            }
        }
    }
    Err(format!(
        "quest board exhausted map attempts for seed {seed}: {reason}"
    ))
}

fn computed_hash(map: &GameMapData) -> u32 {
    u32::from_str_radix(&map_hash::map_hash(map), 16)
        .expect("map hash emits fixed-width hexadecimal")
}

#[cfg(test)]
mod tests {
    use super::*;
    use crate::mapdata::WorldPt;
    use crate::mapgen::coliseum;
    use std::collections::BTreeSet;

    #[test]
    fn admission_rejects_an_isolated_alternative_in_an_otherwise_connected_zone() {
        let fixed = coliseum::fixed_layout();
        let generator = pipeline::Pipeline::new(&fixed);
        let secrets = Secrets::from_seed(1);
        let mut export =
            generate(&generator, 1, 0..24, pipeline::Options::default(), secrets).unwrap();
        let map = &mut export.map;
        let width = map.w as usize;
        let index = |p: WorldPt| world_to_tile(p.y) as usize * width + world_to_tile(p.x) as usize;
        let protected = map
            .spawns
            .iter()
            .chain(&map.crate_spots)
            .chain(&map.ground_spots)
            .chain(&map.coliseum.inner_spawns)
            .chain(map.regions.iter().map(|r| &r.pole))
            .chain(
                map.camps
                    .iter()
                    .flat_map(|camp| std::iter::once(&camp.chest).chain(&camp.spots)),
            )
            .copied()
            .map(index)
            .collect::<BTreeSet<_>>();
        let ring = map
            .spawn_pool
            .iter()
            .skip(1)
            .find_map(|spawn| {
                let i = index(WorldPt {
                    x: spawn.x,
                    y: spawn.y,
                });
                let x = i % width;
                let y = i / width;
                if x < 2
                    || x + 2 >= width
                    || y < 2
                    || y + 2 >= map.h as usize
                    || map
                        .spawn_pool
                        .iter()
                        .filter(|other| other.zone == spawn.zone)
                        .count()
                        < 2
                {
                    return None;
                }
                let ring = [i - 1, i + 1, i - width, i + width];
                ring.iter()
                    .all(|&j| {
                        map.feature_grid[j] == 0 && map.terrain[j] == 0 && !protected.contains(&j)
                    })
                    .then_some(ring)
            })
            .expect("an alternate start with ordinary neighboring tiles");
        for i in ring {
            if map.tiles[i] == 0 {
                if let Some(camp) = map.territory_grid[i].checked_sub(1) {
                    map.camps[camp as usize].area -= 1;
                }
            }
            map.tiles[i] = crate::map::TILE_WALL;
        }
        export.legacy_map_hash_u32 = computed_hash(map);
        let arena =
            into_arena(export.clone()).expect("map format accepts the isolated floor spawn");
        let error =
            plan(&arena, secrets).expect_err("every planning candidate must reach the board");
        assert!(error.contains("disconnected"), "{error}");
        assert!(prepare(export, secrets).is_err());
    }

    #[test]
    fn admission_replaces_a_body_blocked_fallback_without_changing_geometry() {
        let fixed = coliseum::fixed_layout();
        let generator = pipeline::Pipeline::new(&fixed);
        let secrets = Secrets::from_seed(1);
        let export = generate(&generator, 1, 0..24, pipeline::Options::default(), secrets).unwrap();
        let mut arena = into_arena(export).unwrap();
        let canonical = arena.spawns.clone();
        arena.spawns[0] = crate::Vec2::new(16., 16.);
        assert!(needs_repair(&arena, secrets).is_err());
        let original = Arc::new(arena);
        let repaired = ensure(original.clone(), secrets).unwrap();
        assert_eq!(repaired.spawns, canonical);
        assert_eq!(repaired.map.tiles, original.map.tiles);
        assert_eq!(needs_repair(&repaired, secrets), Ok(false));
    }

    #[test]
    fn admission_rejects_a_tagged_encounter_with_a_changed_roster() {
        let fixed = coliseum::fixed_layout();
        let generator = pipeline::Pipeline::new(&fixed);
        let (export, secrets) = (1..=16)
            .find_map(|seed| {
                let secrets = Secrets::from_seed(seed);
                let export = generate(
                    &generator,
                    seed as i64,
                    0..24,
                    pipeline::Options::default(),
                    secrets,
                )
                .unwrap();
                export
                    .map
                    .camps
                    .iter()
                    .any(|camp| targets::tagged(&camp.id) == Some(QuestId::TheWarden))
                    .then_some((export, secrets))
            })
            .expect("seed sweep includes The Warden");
        let mut poisoned = export;
        let camp = poisoned
            .map
            .camps
            .iter_mut()
            .find(|camp| targets::tagged(&camp.id) == Some(QuestId::TheWarden))
            .unwrap();
        camp.elite_spot = -1;
        let display = poisoned
            .map
            .regions
            .iter_mut()
            .flat_map(|region| &mut region.mob_camps)
            .find(|entry| entry.id == camp.id)
            .unwrap();
        display.elite = false;
        poisoned.legacy_map_hash_u32 = computed_hash(&poisoned.map);
        let arena = into_arena(poisoned.clone()).unwrap();
        assert_eq!(needs_repair(&arena, secrets), Ok(true));
        assert!(prepare(poisoned, secrets).is_err());
    }

    #[test]
    fn generated_board_sweep_preserves_the_private_draw_and_all_twenty_five_quests() {
        let fixed = coliseum::fixed_layout();
        let generator = pipeline::Pipeline::new(&fixed);
        for signature in [false, true] {
            let mut seen = BTreeSet::new();
            let mut regenerated = false;
            for seed in 1..=16 {
                let secrets = Secrets::from_seed(seed);
                let options = pipeline::Options {
                    decor: false,
                    signature,
                    ..Default::default()
                };
                let map = generate(&generator, seed as i64, 0..24, options, secrets).unwrap();
                regenerated |= map.map.attempt != 0;
                let arena = map.validate().unwrap().into_arena().unwrap();
                assert_eq!(needs_repair(&arena, secrets), Ok(false));
                let salt = crate::quests::live::quest_salt(secrets.reward_salt);
                let mut random = Sfc32::from_words(salt);
                let planned = placement::plan(&arena.map, &mut || random.next_f64()).unwrap();
                let mut random = Sfc32::from_words(salt);
                let expected = placement::draw(&mut || random.next_f64());
                assert!(planned.iter().map(|entry| entry.quest).eq(expected));
                let layers = arena.map.layers().unwrap();
                for entry in planned {
                    assert_eq!(
                        layers.region_at_world(entry.giver.x, entry.giver.y).code(),
                        entry.quest.home().code() + 1
                    );
                    seen.insert(entry.quest);
                }
            }
            assert!(regenerated, "the sweep should exercise regeneration");
            assert_eq!(seen.into_iter().collect::<Vec<_>>(), QuestId::ALL);
        }
    }
}
