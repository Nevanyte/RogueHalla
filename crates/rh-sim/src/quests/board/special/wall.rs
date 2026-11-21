//! WallBuster opens ordinary terrain and breaks the cells of temporary spell barriers.
use crate::map::{tile_to_world_center, world_to_tile, TILE_FLOOR, TILE_LOW, TILE_WALL};
use crate::{state::World, statics::MatchStatic, systems::Cx, Phase, PlayerId, Vec2};

pub(crate) fn visit_rules(word: &mut dyn FnMut(u64)) {
    word(RADIUS.to_bits());
    word(ANCHOR_MARGIN.to_bits());
    word(MAX_REMOVALS as u64);
    word(u64::from(FENCE));
}

const RADIUS: f64 = 300.;
const RADIUS_SQ: f64 = RADIUS * RADIUS;
const FENCE: u8 = 2;
const ANCHOR_MARGIN: f64 = crate::constants::TILE;
// The reliable wire collection admits up to 300 cells for each possible wire seat.
const MAX_REMOVALS: usize = 9_600;

pub(crate) fn wall_buster(cx: &mut Cx, owner: PlayerId) -> bool {
    let Some(aim) = cx.s.quest_spell_aim.take() else {
        return false;
    };
    let Some(ix) = cx.w.fighter_idx(owner) else {
        return false;
    };
    if cx.w.phase != Phase::Active
        || !cx.w.is_player(ix)
        || !cx.w.standing(ix)
        || cx.w.players[ix.slot()].seat.bot
    {
        return false;
    }
    let from = cx.w.core(ix).pos();
    let has_range = |pt: Vec2| distance_sq(from, pt) <= RADIUS_SQ;
    if !has_range(aim) {
        return false;
    }

    let (k, w) = (cx.k, &mut *cx.w);
    let map = &k.arena.map;
    let (aim_x, aim_y) = (world_to_tile(aim.x), world_to_tile(aim.y));
    if map.tile(aim_x, aim_y).is_none() {
        return false;
    }
    let aim_i = aim_y as u32 * map.w + aim_x as u32;
    let eligible = |i| has_range(center(k, i)) && !protected(k, w, i);
    let destroys_ground =
        |i| eligible(i) && ordinary(k, i) && w.destroyed_terrain.cells.binary_search(&i).is_err();
    let aims_at_barrier = w.abilities.as_ref().is_some_and(|a| {
        eligible(aim_i)
            && a.barriers
                .0
                .iter()
                .any(|b| b.cells.binary_search(&aim_i).is_ok())
    });
    if !destroys_ground(aim_i) && !aims_at_barrier {
        return false;
    }

    let min_y = world_to_tile(from.y - RADIUS).max(0);
    let max_y = world_to_tile(from.y + RADIUS).min(i64::from(map.h) - 1);
    let min_x = world_to_tile(from.x - RADIUS).max(0);
    let max_x = world_to_tile(from.x + RADIUS).min(i64::from(map.w) - 1);
    let mut opened = Vec::new();
    for y in min_y..=max_y {
        for x in min_x..=max_x {
            let i = y as u32 * map.w + x as u32;
            if destroys_ground(i) {
                opened.push(i);
            }
        }
    }
    // Protection depends on the admitted map and the quest points. Compute the affected
    // barrier cells before taking the mutable borrow of the field runtime.
    let broken: Vec<u32> = w
        .abilities
        .as_ref()
        .into_iter()
        .flat_map(|a| &a.barriers.0)
        .flat_map(|b| b.cells.iter().copied())
        .filter(|&i| eligible(i))
        .collect();
    if !opened.is_empty() {
        w.destroyed_terrain.cells.append(&mut opened);
        w.destroyed_terrain.cells.sort_unstable();
        w.destroyed_terrain.rebuild(k);
    }
    if let Some(a) = w.abilities.as_mut() {
        for b in &mut a.barriers.0 {
            b.cells.retain(|i| !broken.contains(i));
        }
        a.fields.retain(|f| {
            f.kind != crate::status_runtime::FieldKind::Wall
                || a.barriers
                    .0
                    .iter()
                    .any(|b| b.field == f.id && !b.cells.is_empty())
        });
        a.barriers.0.retain(|b| !b.cells.is_empty());
        a.overlay.0 = None;
    }
    crate::barriers::sync(k, w);
    true
}

fn distance_sq(a: Vec2, b: Vec2) -> f64 {
    let (dx, dy) = (a.x - b.x, a.y - b.y);
    dx * dx + dy * dy
}

fn center(k: &MatchStatic, i: u32) -> Vec2 {
    let w = k.arena.map.w;
    Vec2::new(
        tile_to_world_center(i64::from(i % w)),
        tile_to_world_center(i64::from(i / w)),
    )
}

fn ordinary(k: &MatchStatic, i: u32) -> bool {
    let map = &k.arena.map;
    let surface = map
        .layers()
        .map_or(0, |layers| layers.data().terrain[i as usize]);
    matches!(
        (map.tiles[i as usize], surface),
        (TILE_WALL, 0) | (TILE_LOW, 0 | FENCE)
    )
}

fn protected(k: &MatchStatic, w: &World, i: u32) -> bool {
    let map = &k.arena.map;
    let (x, y) = (i % map.w, i / map.w);
    if x == 0 || y == 0 || x + 1 == map.w || y + 1 == map.h {
        return true;
    }
    if let Some(layers) = map.layers() {
        let c = layers.coliseum();
        let (x, y) = (i64::from(x), i64::from(y));
        let (left, top) = (i64::from(c.rect.x), i64::from(c.rect.y));
        if (left..left + i64::from(c.rect.w)).contains(&x)
            && (top..top + i64::from(c.rect.h)).contains(&y)
            && map.tiles[i as usize] != TILE_FLOOR
        {
            return true;
        }
        if c.gates.iter().any(|g| {
            g.tiles
                .iter()
                .any(|p| i64::from(p.x) == x && i64::from(p.y) == y)
        }) {
            return true;
        }
    }
    let at = center(k, i);
    let nearby = |p| distance_sq(p, at) <= ANCHOR_MARGIN * ANCHOR_MARGIN;
    if let Some(Ok(deal)) = &k.quest_deal {
        return deal.placements.iter().any(|p| {
            nearby(p.at) || p.tokens.iter().copied().any(nearby) || p.parcel.is_some_and(nearby)
        });
    }
    w.quests.as_ref().is_some_and(|q| {
        q.offers.iter().any(|o| {
            nearby(o.at) || o.tokens.iter().copied().any(nearby) || o.parcel.is_some_and(nearby)
        })
    })
}

pub(crate) fn validate_terrain(k: &MatchStatic, w: &World) -> Result<(), String> {
    let cells = &w.destroyed_terrain.cells;
    if cells.len() > MAX_REMOVALS
        || cells.windows(2).any(|p| p[0] >= p[1])
        || cells.iter().any(|&i| {
            i as usize >= k.arena.map.tiles.len() || !ordinary(k, i) || protected(k, w, i)
        })
    {
        return Err("destroyed terrain contains invalid or protected cells".into());
    }
    Ok(())
}

#[cfg(test)]
mod tests {
    use super::*;
    use crate::geometry::has_line_of_sight;
    use crate::map::{MASK_MOVE, MASK_SIGHT, TILE_FLOOR, TILE_LOW, TILE_WALL};
    use crate::{Arena, FactionId, Placement, Secrets, Sim, SimConfig, TileMap};

    fn room() -> Sim {
        let (w, h) = (24, 24);
        let mut tiles = vec![TILE_FLOOR; (w * h) as usize];
        for y in 0..h {
            for x in 0..w {
                if x == 0 || x == w - 1 || y == 0 || y == h - 1 || x == 5 {
                    tiles[(y * w + x) as usize] = TILE_WALL;
                }
            }
        }
        tiles[(7 * w + 5) as usize] = TILE_LOW;
        let left = center(3, 5);
        let right = center(8, 5);
        let placement = |name: &str, at| Placement {
            profile: name.into(),
            faction: FactionId::Frost,
            bot: false,
            at,
            hp: None,
            weapon0: None,
            weapon1: None,
        };
        Sim::reset_active(
            SimConfig {
                abilities: crate::abilities::Mode::Full,
                frost_wish: crate::FrostWish::Wall,
                wish_lock_ms: 0.,
                ..Default::default()
            },
            Arena {
                map: TileMap::new(w, h, tiles).unwrap(),
                spawns: vec![left, right],
            },
            Secrets::from_seed(4),
            &[placement("caster", left), placement("opponent", right)],
        )
    }

    fn center(x: i64, y: i64) -> Vec2 {
        use crate::map::tile_to_world_center as tc;
        Vec2::new(tc(x), tc(y))
    }

    fn cast(sim: &mut Sim, owner: PlayerId, aim: Vec2) -> bool {
        sim.with_cx(|cx| {
            cx.s.quest_spell_aim = Some(aim);
            wall_buster(cx, owner)
        })
    }

    #[test]
    fn special_wall_buster_opens_visibility_and_navigation_through_restore() {
        let mut sim = room();
        let owner = sim.players()[0].id;
        let (left, right) = (center(3, 5), center(8, 5));
        let map = sim.collision_map();
        assert!(map.blocked(5, 5, MASK_MOVE));
        assert_ne!(map.comp(5 * 24 + 3), map.comp(5 * 24 + 8));
        assert!(!has_line_of_sight(map, left, right, MASK_SIGHT));
        let mut pathfinder = crate::pathfind::Pathfinder::default();
        let walk = |pf: &mut crate::pathfind::Pathfinder, map: &TileMap| {
            pf.find_path(map, left, right, crate::constants::PLAYER_RADIUS)
        };
        assert!(walk(&mut pathfinder, map).is_empty());
        let before = sim.state_hash();

        assert!(
            cast(&mut sim, owner, center(5, 5)),
            "a nearby wall should break"
        );
        assert_ne!(sim.state_hash(), before);
        let restored = Sim::restore(serde_json::to_value(&sim).unwrap()).unwrap();
        assert_eq!(restored.state_hash(), sim.state_hash());
        for s in [&sim, &restored] {
            let map = s.collision_map();
            assert_eq!(map.tile(0, 5), Some(TILE_WALL));
            assert_eq!(map.tile(5, 7), Some(TILE_FLOOR));
            assert_eq!(map.tile(5, 20), Some(TILE_WALL));
            assert!(!map.blocked(5, 5, MASK_MOVE));
            assert_eq!(map.comp(5 * 24 + 3), map.comp(5 * 24 + 8));
            assert!(has_line_of_sight(map, left, right, MASK_SIGHT));
            assert_eq!(
                walk(&mut pathfinder, s.collision_map()).last(),
                Some(&right)
            );
        }
    }

    #[test]
    fn special_wall_buster_breaks_only_the_in_range_cells_of_a_real_cast_barrier() {
        let mut sim = room();
        let owner = sim.players()[0].id;
        let aim = center(12, 5);
        let cmd = crate::validate(crate::RawCommand::Cast {
            slot: crate::abilities::Slot::Wish,
            x: aim.x,
            y: aim.y,
            id: None,
            t: None,
            seq: None,
        })
        .unwrap();
        sim.submit(owner, cmd).unwrap();
        for _ in 0..16 {
            sim.step();
        }
        let row = |s: &Sim| s.w.abilities.as_ref().unwrap().barriers.0[0].cells.clone();
        let before = row(&sim);
        let origin = sim.player(owner).unwrap().pos();
        let expected: Vec<u32> = before
            .iter()
            .copied()
            .filter(|&i| distance_sq(origin, super::center(&sim.k, i)) > RADIUS_SQ)
            .collect();
        assert!(!expected.is_empty() && expected.len() < before.len());
        assert!(cast(&mut sim, owner, aim));
        assert_eq!(row(&sim), expected);
        let restored = Sim::restore(serde_json::to_value(&sim).unwrap()).unwrap();
        assert_eq!(row(&restored), expected);
        for s in [&sim, &restored] {
            let held = crate::barriers::held(&s.w);
            let body = crate::barriers::body_map(&s.k, &held, false);
            for &i in &before {
                assert_eq!(
                    body.tiles[i as usize],
                    if expected.contains(&i) {
                        TILE_LOW
                    } else {
                        TILE_FLOOR
                    }
                );
            }
        }
    }

    #[test]
    fn special_wall_buster_protects_the_admitted_gate_enclosure_and_lava() {
        let export: crate::mapdata::MapExport =
            serde_json::from_str(include_str!("../../../mapdata/fixtures/seed1.map.json")).unwrap();
        let arena = export.validate().unwrap().into_arena().unwrap();
        let sim = Sim::new(SimConfig::default(), arena, Secrets::from_seed(15));
        let map = &sim.k.arena.map;
        let layers = map.layers().unwrap();
        let coliseum = layers.coliseum();
        let gate = &coliseum.gates[0].tiles[0];
        assert!(protected(
            &sim.k,
            &sim.w,
            gate.y as u32 * map.w + gate.x as u32
        ));

        let rect = &coliseum.rect;
        let wall = (0..rect.h)
            .flat_map(|y| (0..rect.w).map(move |x| (rect.y as u32 + y) * map.w + rect.x as u32 + x))
            .find(|&i| ordinary(&sim.k, i))
            .expect("the coliseum contains permanent walls");
        assert!(protected(&sim.k, &sim.w, wall));
        let hazard = layers
            .data()
            .terrain
            .iter()
            .position(|&t| t == 1 || t == 3)
            .expect("a layered map has a lava or crevasse tile");
        assert!(!ordinary(&sim.k, hazard as u32));
    }

    #[test]
    fn special_wall_buster_refuses_a_quest_anchor_neighbor() {
        let mut sim = room();
        let owner = sim.players()[0].id;
        sim.k.quest_deal = Some(Ok(crate::quests::board::Deal {
            placements: vec![crate::quests::placement::Placement {
                def: 256,
                known: true,
                at: center(4, 5),
                tokens: vec![center(4, 7)],
                parcel: None,
            }],
            stream: crate::private_rng::Sfc32::from_words([1, 2, 3, 4]),
        }));
        assert!(!cast(&mut sim, owner, center(5, 5)));
        assert!(cast(&mut sim, owner, center(5, 9)));
        assert_eq!(sim.collision_map().tile(5, 5), Some(TILE_WALL));
        assert_eq!(sim.collision_map().tile(5, 7), Some(TILE_LOW));
    }

    #[test]
    fn special_wall_buster_slot_consumes_only_a_successful_validated_cast() {
        use crate::quests::board::{rewards, Deal};
        use crate::quests::live::Quests;
        let mut sim = room();
        let owner = sim.players()[0].id;
        let stream = crate::private_rng::Sfc32::from_words([1, 2, 3, 4]);
        sim.k.quest_deal = Some(Ok(Deal {
            placements: Vec::new(),
            stream: stream.clone(),
        }));
        let mut inventory = rewards::RewardState::default();
        inventory.owners.push(Default::default());
        inventory.owners[0].id = owner;
        inventory.owners[0].slots[0] = Some(rewards::Owned::new(rewards::Award::WallBuster, 0.));
        sim.w.quests = Some(Quests {
            stream,
            offers: Vec::new(),
            carried: Vec::new(),
            recheck: false,
            board: None,
            rewards: Some(inventory),
        });
        let equipped = |s: &Sim| {
            s.w.quests
                .as_ref()
                .unwrap()
                .rewards
                .as_ref()
                .unwrap()
                .owners[0]
                .slots[0]
                .is_some()
        };
        let submit_cast = |s: &mut Sim, aim: Vec2| {
            let cmd = crate::validate(crate::RawCommand::Cast {
                slot: crate::abilities::Slot::Q,
                x: aim.x,
                y: aim.y,
                id: None,
                t: None,
                seq: None,
            })
            .unwrap();
            s.submit(owner, cmd).unwrap();
            s.step();
        };
        submit_cast(&mut sim, center(3, 5));
        assert!(equipped(&sim));
        assert_eq!(sim.collision_map().tile(5, 5), Some(TILE_WALL));
        submit_cast(&mut sim, center(5, 5));
        assert!(!equipped(&sim));
        assert_eq!(sim.collision_map().tile(5, 5), Some(TILE_FLOOR));
    }

    #[test]
    fn special_wall_buster_refuses_checkpoint_with_removed_boundary() {
        let sim = room();
        let mut saved = serde_json::to_value(&sim).unwrap();
        saved["world"]["destroyed_terrain"] = serde_json::json!([24]);
        let err = Sim::restore(saved).unwrap_err();
        assert!(err.to_string().contains("invalid or protected cells"));
    }

    #[test]
    fn special_wall_buster_refuses_empty_protected_out_of_range_and_unknown_owner() {
        let mut sim = room();
        let owner = sim.players()[0].id;
        let before = sim.state_hash();
        for (caster, aim) in [
            (owner, center(3, 5)),
            (owner, center(0, 5)),
            (owner, center(5, 20)),
            (PlayerId(u32::MAX), center(5, 5)),
        ] {
            assert!(!cast(&mut sim, caster, aim));
            assert_eq!(sim.state_hash(), before);
        }
    }
}
