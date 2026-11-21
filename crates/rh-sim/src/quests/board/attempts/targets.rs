//! Quest encounter bindings over the regional mob lifecycle.
use super::*;
use crate::constants::{CAMP_RESPAWN_MS, TILE};
use crate::map::{tile_to_world_center, world_to_tile};
use crate::mapdata::{CampDef, MapExport, MobCampDef, RegionWeapon, WorldPt, Zone};
use crate::mob_catalog::MobTypeId;
use crate::mob_runtime::{ActorOrigin, Mob};
use crate::quests::board::placement::BoardSite;

const WARDEN_CAMP: &str = "quest-target-the-warden";
const RIME_CAMP: &str = "quest-target-rime-hunt";
const QUEST_RESPAWN_MS: f64 = 25_000.;
const WARDEN_HP_MULTIPLIER: f64 = 3.;

pub(super) fn visit_rules(word: &mut dyn FnMut(u64)) {
    word(QUEST_RESPAWN_MS.to_bits());
    word(WARDEN_HP_MULTIPLIER.to_bits());
}

pub(crate) fn tagged(id: &str) -> Option<QuestId> {
    match id {
        WARDEN_CAMP => Some(QuestId::TheWarden),
        RIME_CAMP => Some(QuestId::RimeHunt),
        _ => None,
    }
}

pub(crate) fn is_target_camp(id: &str) -> bool {
    tagged(id).is_some()
}

fn tile_index(p: Vec2, w: usize, h: usize) -> Option<usize> {
    let x = usize::try_from(world_to_tile(p.x)).ok()?;
    let y = usize::try_from(world_to_tile(p.y)).ok()?;
    (x < w && y < h).then_some(y * w + x)
}

pub(crate) fn append_target_camps(
    export: &mut MapExport,
    sites: &[BoardSite],
) -> Result<(), String> {
    let map = &mut export.map;
    let (w, h) = (map.w as usize, map.h as usize);
    for s in sites {
        let (tag, zone, count, elite_spot, weapon) = match s.quest {
            QuestId::TheWarden => (WARDEN_CAMP, Zone::Sentenced, 1, 0, RegionWeapon::Sword),
            QuestId::RimeHunt => (RIME_CAMP, Zone::Frost, 3, -1, RegionWeapon::Bow),
            _ => continue,
        };
        if map.camps.iter().any(|c| c.id == tag) {
            continue;
        }
        let pads = s
            .objectives
            .get(..count)
            .ok_or("missing quest target pads")?;
        let owned =
            u8::try_from(map.camps.len() + 1).map_err(|_| "quest camp index exceeds map limit")?;
        let mut occupied = vec![false; w * h];
        for at in map
            .camps
            .iter()
            .flat_map(|c| c.spots.iter().copied().chain(Some(c.chest)))
            .chain(map.spawn_pool.iter().map(|s| WorldPt { x: s.x, y: s.y }))
        {
            if let Some(i) = tile_index(Vec2::new(at.x, at.y), w, h) {
                occupied[i] = true;
            }
        }
        let free = |i: usize| {
            let feature = map.feature_grid[i]
                .checked_sub(1)
                .and_then(|i| map.features.get(i as usize));
            map.tiles[i] == 0
                && !occupied[i]
                && map.region_grid[i] == zone.code()
                && feature.is_none_or(|f| f.kind != crate::mapdata::FeatureKind::Bush)
        };
        let mut pads_i = [0; 3];
        for (i, &at) in pads.iter().enumerate() {
            pads_i[i] = tile_index(at, w, h)
                .filter(|&p| free(p))
                .ok_or("quest target pad is occupied")?;
        }
        let point = |i: usize| {
            Vec2::new(
                tile_to_world_center((i % w) as i64),
                tile_to_world_center((i / w) as i64),
            )
        };
        let chest_i = (0..w * h)
            .filter(|&i| {
                let at = point(i);
                free(i)
                    && !pads_i[..count].contains(&i)
                    && map.feature_grid[i] == 0
                    && !sites
                        .iter()
                        .any(|s| s.giver == at || s.objectives.contains(&at))
            })
            .min_by(|&a, &b| distance(pads[0], point(a)).total_cmp(&distance(pads[0], point(b))))
            .ok_or("no spare tile for the quest encounter chest")?;
        let center = point(chest_i);
        let radius = (pads.iter().map(|&p| distance(center, p)).fold(0., f64::max) + 2. * TILE)
            .max(3. * TILE)
            .ceil();
        // Preserve native camp pads and bush ownership. Transfer any other
        // available floor within the encounter radius and keep both areas exact.
        let claimed: Vec<_> = (0..w * h)
            .filter(|&i| {
                free(i)
                    && (pads_i[..count].contains(&i)
                        || i == chest_i
                        || (map.feature_grid[i] == 0 && distance(center, point(i)) <= radius))
            })
            .collect();
        let area = claimed.len() as u32;
        for i in claimed {
            if let Some(previous) = map.territory_grid[i].checked_sub(1) {
                map.camps[previous as usize].area -= 1;
            }
            map.territory_grid[i] = owned;
        }
        let spots: Vec<_> = pads.iter().map(|p| WorldPt { x: p.x, y: p.y }).collect();
        let chest = WorldPt {
            x: center.x,
            y: center.y,
        };
        map.camps.push(CampDef {
            id: tag.into(),
            zone,
            x: center.x,
            y: center.y,
            r: radius,
            spots: spots.clone(),
            elite_spot,
            chest,
            area,
            radius,
        });
        let region = map
            .regions
            .iter_mut()
            .find(|r| r.id == zone)
            .ok_or("missing quest encounter region")?;
        let weapon = region.mob_camps.first().map_or(weapon, |c| c.weapon);
        region.mob_camps.push(MobCampDef {
            id: tag.into(),
            region: zone,
            x: center.x,
            y: center.y,
            r: radius,
            spots,
            elite: elite_spot >= 0,
            weapon,
        });
    }
    Ok(())
}

fn camp(k: &MatchStatic, ci: usize) -> Option<&CampDef> {
    k.arena.map.layers()?.data().camps.get(ci)
}

fn camp_target(k: &MatchStatic, ci: usize, spot: usize) -> Option<QuestId> {
    let camp = camp(k, ci)?;
    let deal = k.quest_deal.as_ref()?.as_ref().ok()?;
    let selected = |id| deal.placements.iter().any(|p| quest(p.def) == Some(id));
    if let Some(id) = tagged(&camp.id) {
        return selected(id).then_some(id);
    }
    let zone = crate::FactionId::parse(camp.zone.as_str())?;
    for p in &deal.placements {
        let id = quest(p.def)?;
        if id.home() != zone {
            continue;
        }
        let Some(&at) = p.tokens.first() else {
            continue;
        };
        let matches_tile = |p: WorldPt| {
            world_to_tile(p.x) == world_to_tile(at.x) && world_to_tile(p.y) == world_to_tile(at.y)
        };
        if match id {
            QuestId::EliteSlayer | QuestId::ABoneToPick => {
                camp.elite_spot == spot as i32
                    && camp.spots.get(spot).copied().is_some_and(matches_tile)
            }
            QuestId::FeedOrHunt => {
                camp.spots
                    .iter()
                    .enumerate()
                    .find(|(i, _)| camp.elite_spot != *i as i32)
                    .is_some_and(|(i, _)| i == spot)
                    && matches_tile(WorldPt {
                        x: camp.x,
                        y: camp.y,
                    })
            }
            _ => false,
        } {
            return Some(id);
        }
    }
    None
}

pub(crate) fn roster(
    k: &MatchStatic,
    ci: usize,
    normal: &mut MobTypeId,
    elite: &mut Option<MobTypeId>,
) {
    let Some(c) = camp(k, ci) else { return };
    if c.id == RIME_CAMP && k.quest_deal.as_ref().is_some_and(Result::is_ok) {
        *normal = MobTypeId::FrostbowSentry;
        return;
    }
    let Some(id) = usize::try_from(c.elite_spot)
        .ok()
        .and_then(|s| camp_target(k, ci, s))
    else {
        return;
    };
    match id {
        QuestId::ABoneToPick => *elite = Some(MobTypeId::RatOgre),
        QuestId::TheWarden => *elite = Some(MobTypeId::Gaoler),
        _ => {}
    }
}

pub(crate) fn respawn_ms(k: &MatchStatic, ci: usize) -> f64 {
    if k.quest_deal.as_ref().is_some_and(Result::is_ok)
        && camp(k, ci).is_some_and(|c| {
            tagged(&c.id).is_some() || (0..c.spots.len()).any(|s| camp_target(k, ci, s).is_some())
        })
    {
        QUEST_RESPAWN_MS
    } else {
        CAMP_RESPAWN_MS
    }
}

pub(crate) fn configure(k: &MatchStatic, ci: usize, si: usize, mob: &mut Mob) {
    match camp_target(k, ci, si) {
        Some(QuestId::TheWarden) => {
            mob.display_name = Some("The Warden".into());
            mob.core.vitals.max_hp *= WARDEN_HP_MULTIPLIER;
            mob.core.vitals.hp = mob.core.vitals.max_hp;
        }
        Some(QuestId::ABoneToPick) => mob.display_name = Some("The Barrow Archer".into()),
        Some(QuestId::FeedOrHunt) => mob.display_name = Some("Warren Forager".into()),
        _ => {}
    }
}

pub(super) fn refresh(k: &MatchStatic, w: &mut World) {
    let next = w.next_id;
    let Some(b) = board_mut(w) else { return };
    let seen = b.seen_actor;
    if next == seen {
        return;
    }
    b.seen_actor = next;
    let n = w.mobs.len();
    for i in 0..n {
        let mob = &w.mobs[i];
        if mob.status != crate::state::Status::Alive || mob.zombie.is_some() {
            continue;
        }
        let actor = mob.core.id;
        if actor.0 < seen {
            continue;
        }
        let Some(ActorOrigin::Mob { camp, spot, .. }) = w.actor_origin(actor) else {
            continue;
        };
        let Some(id) = camp_target(k, *camp as usize, *spot as usize) else {
            continue;
        };
        let offer = w
            .quests
            .as_ref()
            .and_then(|q| q.offers.iter().find(|o| kind(o) == Some(id)))
            .map(|o| o.id);
        if let Some(offer) = offer {
            bind_target(w, offer, actor);
        }
    }
}

pub(super) fn matches_actor(k: &MatchStatic, w: &World, actor: PlayerId, quest: QuestId) -> bool {
    matches!(w.actor_origin(actor), Some(ActorOrigin::Mob { camp, spot, .. })
        if camp_target(k, *camp as usize, *spot as usize) == Some(quest))
}

pub(super) fn is_ice_camp(k: &MatchStatic, ci: usize) -> bool {
    camp(k, ci).is_some_and(|c| tagged(&c.id) == Some(QuestId::RimeHunt))
}
