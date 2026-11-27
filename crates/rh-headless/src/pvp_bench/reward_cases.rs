//! Load the real offer table and a valid quest-board arena for offline reward comparisons.
use rh_sim::cards::{Build, Tuning};
use rh_sim::mapgen::{coliseum, pipeline};
use rh_sim::quests::board::{catalog::DEFINITIONS, map_admission};
use rh_sim::reward_chests::Picker;
use rh_sim::{Arena, FactionId, Secrets, SimConfig, Vec2};
use std::collections::BTreeSet;
use std::sync::Arc;

pub(super) fn catalog() -> Result<(), String> {
    let tuning = Tuning::of(&SimConfig::live_content());
    let mut entries = Vec::new();
    for faction in FactionId::ALL {
        let mut seen = BTreeSet::new();
        for card in tuning.offer_pool(&[Picker {
            build: Build::default(),
            faction,
        }]) {
            if seen.insert(card as usize) {
                let def = tuning.def(card);
                entries.push(serde_json::json!({"faction": faction, "id":card, "name":def.title, "description":def.text}));
            }
        }
    }
    let quests: Vec<_> = DEFINITIONS
        .iter()
        .map(|q| {
            serde_json::json!({
                "id":q.id,"name":q.name,"reward":q.reward,"description":q.reward_text
            })
        })
        .collect();
    let out = serde_json::json!({"augments":entries,"quests":quests});
    println!(
        "{}",
        serde_json::to_string(&out).map_err(|e| e.to_string())?
    );
    Ok(())
}

pub(super) fn arena() -> Result<Arc<Arena>, String> {
    let fixed = coliseum::fixed_layout();
    let generator = pipeline::Pipeline::new(&fixed);
    let options = pipeline::Options {
        decor: false,
        ..Default::default()
    };
    let arena = map_admission::generate(&generator, 1, 0..32, options, Secrets::from_seed(1))
        .map_err(|e| format!("quest arena: {e:?}"))?
        .validate()
        .map_err(|e| e.to_string())?
        .into_arena()
        .map_err(|e| e.to_string())?;
    Ok(Arc::new(arena))
}

/// A long, open strip admits both weapons, placements and the Fire Wish displacement.
pub(super) fn open_origin(arena: &Arena) -> Result<Vec2, String> {
    use rh_sim::geometry::segment_clear;
    use rh_sim::map::{tile_to_world_center, MASK_MELEE, MASK_MOVE, MASK_SIGHT};
    let mask = MASK_MOVE | MASK_MELEE | MASK_SIGHT;
    let centre = arena.centre();
    let mut best: Option<(f64, Vec2)> = None;
    for y in 5..arena.map.h.saturating_sub(5) {
        for x in 7..arena.map.w.saturating_sub(8) {
            let pos = Vec2::new(
                tile_to_world_center(x as i64),
                tile_to_world_center(y as i64),
            );
            let dist = (pos.x - centre.x).abs() + (pos.y - centre.y).abs();
            if best.as_ref().is_some_and(|(d, _)| *d <= dist) {
                continue;
            }
            if [-48., 0., 48.].into_iter().all(|dy| {
                segment_clear(
                    &arena.map,
                    Vec2::new(pos.x - 170., pos.y + dy),
                    Vec2::new(pos.x + 210., pos.y + dy),
                    22.,
                    mask,
                )
            }) {
                best = Some((dist, pos));
            }
        }
    }
    best.map(|(_, pos)| pos)
        .ok_or_else(|| "no clear deployment and combat strip in the quest arena".into())
}
