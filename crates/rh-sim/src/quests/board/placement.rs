//! The board draws before it looks at a map. A failed reservation can reject that map,
//! but cannot remove a quest from the draw or give another quest a second chance.
use super::attempts::targets::is_target_camp;
use super::catalog::QuestId;
use crate::constants::{PLAYER_RADIUS, TILE};
use crate::geometry::segment_clear;
use crate::map::{tile_to_world_center, world_to_tile, MASK_MOVE, MASK_SIGHT, TILE_LOW};
use crate::mapdata::{FeatureJob, FeatureKind, LayerData, MapFeature, WorldPt};
use crate::{FactionId, TileMap, Vec2};
use std::collections::VecDeque;

#[derive(Clone, Debug, PartialEq, serde::Serialize, serde::Deserialize)]
pub struct BoardSite {
    pub quest: QuestId,
    pub giver: Vec2,
    /// World points in the quest's objective order. Actor/combat and dynamic trade stages
    /// use these as staging points, then bind the relevant runtime entities.
    pub objectives: Vec<Vec2>,
}

pub(super) fn draw(random: &mut impl FnMut() -> f64) -> Vec<QuestId> {
    let mut selected = Vec::with_capacity(10);
    for home in FactionId::ALL {
        let mut pool = QuestId::ALL
            .into_iter()
            .filter(|id| id.home() == home)
            .collect::<Vec<_>>();
        // The first two steps of a shuffle. Draws consume the same ten words
        // regardless of terrain, rejection, or the selected kinds.
        for _ in 0..2 {
            let index = (random() * pool.len() as f64).floor() as usize;
            selected.push(pool.swap_remove(index));
        }
    }
    selected
}

pub fn plan(map: &TileMap, random: &mut impl FnMut() -> f64) -> Result<Vec<BoardSite>, String> {
    plan_with_fallback(map, &[], random)
}

pub(super) fn plan_with_fallback(
    map: &TileMap,
    fallback: &[Vec2],
    random: &mut impl FnMut() -> f64,
) -> Result<Vec<BoardSite>, String> {
    let selected = draw(random);
    let mut sites = Sites::new(map)?;
    for (number, &at) in fallback.iter().enumerate() {
        sites
            .check_start(at)
            .map_err(|reason| format!("quest board fallback spawn {number} is {reason}"))?;
    }
    let mut board = Vec::with_capacity(selected.len());
    // Reserve quest geography first, then fit givers around it. The draw
    // fixes quest order. Within a map, distance ties stay row-major.
    for quest in selected {
        let objectives = sites
            .objectives(quest)
            .map_err(|why| format!("quest board {quest:?}: {why}"))?;
        board.push(BoardSite {
            quest,
            giver: Vec2::new(0., 0.),
            objectives,
        });
    }
    for entry in &mut board {
        entry.giver = sites
            .giver(entry.quest.home())
            .map_err(|why| format!("quest board {:?}: {why}", entry.quest))?;
    }
    Ok(board)
}

struct Sites<'a> {
    map: &'a TileMap,
    data: &'a LayerData,
    reachable: Vec<bool>,
    reserved: Vec<bool>,
    givers: Vec<Vec2>,
}

impl<'a> Sites<'a> {
    fn new(map: &'a TileMap) -> Result<Self, String> {
        let data = map
            .layers()
            .ok_or("quest board requires admitted map layers")?
            .data();
        if data.camps.is_empty() {
            return Err("quest board requires an arena with camps".into());
        }
        let mut this = Self {
            map,
            data,
            reachable: vec![false; map.tiles.len()],
            reserved: vec![false; map.tiles.len()],
            givers: Vec::with_capacity(10),
        };
        // Use the first zoned start as the root. The coliseum can belong
        // to a separate floor component and is not an ordinary raid start.
        let first = data
            .spawn_pool
            .first()
            .ok_or("quest board requires a starting spawn")?;
        let at = Vec2::new(first.x, first.y);
        let start = this
            .index(at)
            .filter(|&i| segment_clear(map, this.point(i), at, PLAYER_RADIUS, MASK_MOVE))
            .ok_or("quest board requires a body-clear first spawn")?;
        this.reachable[start] = true;
        let mut open = VecDeque::from([start]);
        let w = map.w as usize;
        while let Some(i) = open.pop_front() {
            let x = i % w;
            let adjacent = [
                (x > 0).then(|| i - 1),
                (x + 1 < w).then_some(i + 1),
                i.checked_sub(w),
                (i + w < map.tiles.len()).then_some(i + w),
            ];
            for j in adjacent.into_iter().flatten() {
                if !this.reachable[j]
                    && map.tiles[j] == 0
                    && segment_clear(map, this.point(i), this.point(j), PLAYER_RADIUS, MASK_MOVE)
                {
                    this.reachable[j] = true;
                    open.push_back(j);
                }
            }
        }
        let mut represented = [false; FactionId::ALL.len()];
        for (number, spawn) in data.spawn_pool.iter().enumerate() {
            this.check_start(Vec2::new(spawn.x, spawn.y))
                .map_err(|reason| {
                    format!("quest board {:?} spawn {number} is {reason}", spawn.zone)
                })?;
            represented[usize::from(spawn.zone.code() - 1)] = true;
        }
        for zone in FactionId::ALL {
            if !represented[usize::from(zone.code())] {
                return Err(format!("quest board has no connected {zone:?} spawn"));
            }
        }
        Ok(this)
    }

    fn check_start(&self, at: Vec2) -> Result<(), &'static str> {
        let i = self.index(at).ok_or("outside the map")?;
        if !segment_clear(self.map, self.point(i), at, PLAYER_RADIUS, MASK_MOVE) {
            return Err("body-blocked");
        }
        if !self.reachable[i] {
            return Err("disconnected from the quest board");
        }
        Ok(())
    }

    fn index(&self, point: Vec2) -> Option<usize> {
        let (x, y) = (world_to_tile(point.x), world_to_tile(point.y));
        self.map.tile(x, y)?;
        Some(y as usize * self.map.w as usize + x as usize)
    }

    fn point(&self, index: usize) -> Vec2 {
        let w = self.map.w as usize;
        Vec2::new(
            tile_to_world_center((index % w) as i64),
            tile_to_world_center((index / w) as i64),
        )
    }

    fn available_for_target(&self, i: usize, zone: FactionId) -> bool {
        let bush = self.data.feature_grid[i]
            .checked_sub(1)
            .and_then(|owner| self.data.features.get(owner as usize))
            .is_some_and(|feature| feature.kind == FeatureKind::Bush);
        if !self.available(i, zone) || bush {
            return false;
        }
        let at = self.point(i);
        self.data
            .camps
            .iter()
            .filter(|c| !is_target_camp(&c.id))
            .all(|c| {
                self.index(world(c.chest)) != Some(i)
                    && c.spots
                        .iter()
                        .all(|&spot| self.index(world(spot)) != Some(i))
            })
            && self
                .data
                .spawn_pool
                .iter()
                .all(|spawn| distance2(at, Vec2::new(spawn.x, spawn.y)) >= sq(2. * TILE))
    }

    fn available(&self, i: usize, zone: FactionId) -> bool {
        self.reachable[i] && !self.reserved[i] && self.data.region_grid[i] == zone.code() + 1
    }

    fn nearest(
        &self,
        zone: FactionId,
        target: Vec2,
        accepts: impl Fn(usize, Vec2) -> bool,
    ) -> Option<usize> {
        (0..self.reachable.len())
            .filter(|&i| self.available(i, zone))
            .filter_map(|i| {
                let at = self.point(i);
                accepts(i, at).then(|| (i, distance2(at, target)))
            })
            .min_by(|a, b| a.1.total_cmp(&b.1))
            .map(|(i, _)| i)
    }

    fn reserve(&mut self, index: usize) -> Vec2 {
        self.reserved[index] = true;
        self.point(index)
    }

    fn pole(&self, zone: FactionId) -> Result<Vec2, &'static str> {
        self.data
            .regions
            .iter()
            .find(|r| r.id.code() == zone.code() + 1)
            .map(|r| world(r.pole))
            .ok_or("missing region pole")
    }

    fn distant(&self, from: FactionId) -> Result<FactionId, &'static str> {
        let origin = self.pole(from)?;
        FactionId::ALL
            .into_iter()
            .filter(|&zone| zone != from)
            .filter_map(|zone| self.pole(zone).ok().map(|at| (zone, distance2(at, origin))))
            .max_by(|a, b| a.1.total_cmp(&b.1))
            .map(|(zone, _)| zone)
            .ok_or("missing destination region")
    }

    fn giver(&mut self, zone: FactionId) -> Result<Vec2, &'static str> {
        let pole = self.pole(zone)?;
        // Prefer established neutral giver clearance. On a crowded map,
        // move within the home region while retaining dry, reachable ground,
        // three tiles from the next giver and clear of actual camp/loot sites.
        let result = [8., 4., 2.]
            .into_iter()
            .find_map(|margin| {
                self.nearest(zone, pole, |i, at| {
                    self.data.terrain[i] == 0
                        && self
                            .data
                            .spawn_pool
                            .iter()
                            .all(|s| distance2(at, Vec2::new(s.x, s.y)) >= sq(margin * TILE))
                        && self
                            .givers
                            .iter()
                            .all(|&p| distance2(at, p) >= sq(3. * TILE))
                        && self
                            .data
                            .camps
                            .iter()
                            .filter(|c| !is_target_camp(&c.id))
                            .all(|c| {
                                distance2(at, Vec2::new(c.x, c.y)) >= sq(c.r + TILE)
                                    && distance2(at, world(c.chest)) >= sq(2. * TILE)
                                    && c.spots
                                        .iter()
                                        .all(|&p| distance2(at, world(p)) >= sq(2. * TILE))
                            })
                })
            })
            .ok_or("no reachable home giver site")?;
        let point = self.reserve(result);
        self.givers.push(point);
        Ok(point)
    }

    fn contact(&mut self, zone: FactionId) -> Result<Vec2, &'static str> {
        let target = self.pole(zone)?;
        let i = self
            .nearest(zone, target, |i, _| self.data.terrain[i] == 0)
            .ok_or("no reachable dry contact site")?;
        Ok(self.reserve(i))
    }

    fn camp(&mut self, zone: FactionId, elite: bool) -> Result<Vec2, &'static str> {
        let i = self
            .data
            .camps
            .iter()
            .filter(|c| c.zone.code() == zone.code() + 1 && !is_target_camp(&c.id))
            .filter_map(|c| {
                if elite {
                    usize::try_from(c.elite_spot)
                        .ok()
                        .and_then(|n| c.spots.get(n))
                        .copied()
                } else {
                    Some(WorldPt { x: c.x, y: c.y })
                }
            })
            .filter_map(|p| self.index(world(p)))
            .find(|&i| self.available(i, zone))
            .ok_or("no reachable matching camp")?;
        Ok(self.reserve(i))
    }

    fn terrain(
        &mut self,
        zone: FactionId,
        terrain: u8,
        count: usize,
        gap: f64,
    ) -> Result<Vec<Vec2>, &'static str> {
        let pole = self.pole(zone)?;
        let mut points = Vec::with_capacity(count);
        for _ in 0..count {
            let i = self
                .nearest(zone, pole, |i, at| {
                    self.data.terrain[i] == terrain
                        && points
                            .iter()
                            .all(|&other| distance2(at, other) >= sq(gap * TILE))
                })
                .ok_or("missing reachable tiles on the required terrain")?;
            points.push(self.reserve(i));
        }
        Ok(points)
    }

    fn ice_camp(&mut self) -> Result<Vec<Vec2>, &'static str> {
        // Camp stages later spawn three Frostbow Sentries on one group of
        // ice pads. Find a compact group rather than merging distant lakes.
        let pole = self.pole(FactionId::Frost)?;
        let mut ice: Vec<_> = (0..self.reachable.len())
            .filter(|&i| {
                self.available_for_target(i, FactionId::Frost) && self.data.terrain[i] == 6
            })
            .collect();
        ice.sort_by(|&a, &b| {
            distance2(self.point(a), pole).total_cmp(&distance2(self.point(b), pole))
        });
        for &centre in &ice {
            let start = self.point(centre);
            let mut pads = vec![centre];
            for &candidate in &ice {
                let at = self.point(candidate);
                if distance2(start, at) <= sq(6. * TILE)
                    && pads
                        .iter()
                        .all(|&pad| distance2(self.point(pad), at) >= sq(2. * TILE))
                    && segment_clear(self.map, start, at, PLAYER_RADIUS, MASK_MOVE)
                {
                    pads.push(candidate);
                    if pads.len() == 3 {
                        return Ok(pads.into_iter().map(|i| self.reserve(i)).collect());
                    }
                }
            }
        }
        Err("no reachable Frost camp-sized ice patch")
    }

    fn features(
        &mut self,
        zone: FactionId,
        filter: impl Fn(&MapFeature) -> bool,
        minimum: usize,
        maximum: usize,
        search_margin: i64,
    ) -> Result<Vec<Vec2>, &'static str> {
        let mut points = Vec::new();
        for (feature_index, feature) in self
            .data
            .features
            .iter()
            .enumerate()
            .filter(|(_, f)| f.zone.code() == zone.code() + 1 && filter(f))
        {
            let b = &feature.bbox;
            let target = Vec2::new(
                (f64::from(b.x) + f64::from(b.w) * 0.5) * TILE,
                (f64::from(b.y) + f64::from(b.h) * 0.5) * TILE,
            );
            if let Some(i) = self.nearest(zone, target, |i, at| {
                let x = world_to_tile(at.x);
                let y = world_to_tile(at.y);
                (feature.kind != FeatureKind::Bush
                    || usize::from(self.data.feature_grid[i]) == feature_index + 1)
                    && x >= i64::from(b.x) - search_margin
                    && y >= i64::from(b.y) - search_margin
                    && x < i64::from(b.x) + i64::from(b.w) + search_margin
                    && y < i64::from(b.y) + i64::from(b.h) + search_margin
            }) {
                points.push(self.reserve(i));
                if points.len() == maximum {
                    break;
                }
            }
        }
        if points.len() < minimum {
            return Err("missing reachable required landmark");
        }
        Ok(points)
    }

    fn brush(&mut self, home: FactionId) -> Result<Vec<Vec2>, &'static str> {
        let origin = self.pole(home)?;
        let mut zones = FactionId::ALL.to_vec();
        zones.sort_by(|&a, &b| {
            let distance = |zone| {
                self.pole(zone)
                    .map_or(f64::INFINITY, |at| distance2(origin, at))
            };
            distance(a).total_cmp(&distance(b))
        });
        // The workbook requires actual bushes, and does not restrict the
        // token destinations to Rime. Prefer its bushes, then the closest
        // other region when its signature geography has fewer than three.
        let mut points = Vec::with_capacity(3);
        for zone in zones {
            points.extend(self.features(
                zone,
                |f| f.kind == FeatureKind::Bush,
                0,
                3 - points.len(),
                0,
            )?);
            if points.len() == 3 {
                return Ok(points);
            }
        }
        Err("missing three reachable bushes")
    }

    fn pilgrims(&mut self) -> Result<Vec<Vec2>, &'static str> {
        let zone = FactionId::Holy;
        let mut points = self.features(zone, |f| f.variant == "shrine", 0, 2, 0)?;
        // The signature colonnade includes its own shrine, unlike the legacy
        // styler's hall. Its 3 x 3 patch of owned light identifies the shrine
        // and the direction of the nave in every cardinal orientation.
        let hall = self.data.features.iter().enumerate().find(|(_, feature)| {
            feature.kind == FeatureKind::Hall
                && feature.jobs == [FeatureJob::Cover]
                && feature.bbox.w == 37
                && feature.bbox.h == 37
        });
        let Some((owner, hall)) = hall else {
            if points.len() != 2 {
                return Err("missing two reachable Sanctum shrines");
            }
            points.extend(self.terrain(zone, 7, 2, 5.)?);
            return Ok(points);
        };
        let centre = Vec2::new(
            tile_to_world_center(i64::from(hall.bbox.x) + 18),
            tile_to_world_center(i64::from(hall.bbox.y) + 18),
        );
        let shrine = self
            .nearest(zone, centre, |i, _| {
                usize::from(self.data.feature_grid[i]) == owner + 1 && self.data.terrain[i] == 7
            })
            .ok_or("missing reachable shrine in the signature hall")?;
        let shrine_at = self.point(shrine);
        if points.len() == 1 {
            points.push(self.reserve(shrine));
        }
        if points.len() != 2 {
            return Err("missing detached shrine beside the signature hall");
        }
        let dx = centre.x - shrine_at.x;
        let dy = centre.y - shrine_at.y;
        let (front_x, front_y) = if dx.abs() > dy.abs() {
            (dx.signum(), 0.)
        } else {
            (0., dy.signum())
        };
        // Approved Hall drawing: the side marks at rows 10 and 32 of the
        // 37-tile nave (two symmetric columns, 2 tiles from its centre line).
        for row in [-8., 14.] {
            let mut bay = None;
            for side in [-2., 2.] {
                let at = Vec2::new(
                    centre.x + TILE * (row * front_x + side * front_y),
                    centre.y + TILE * (row * front_y - side * front_x),
                );
                if let Some(i) = self.index(at).filter(|&i| self.available(i, zone)) {
                    bay = Some(i);
                    break;
                }
            }
            points.push(
                self.reserve(bay.ok_or("no reachable marked light bay in the signature hall")?),
            );
        }
        Ok(points)
    }

    fn warden(&mut self) -> Result<Vec<Vec2>, &'static str> {
        let zone = FactionId::Sentenced;
        let w = self.map.w as usize;
        for (i, &owner) in self.data.feature_grid.iter().enumerate() {
            if owner == 0
                || self.data.features[owner as usize - 1].kind != FeatureKind::FenceRow
                || self.map.tiles[i] != TILE_LOW
            {
                continue;
            }
            let x = (i % w) as i64;
            let y = (i / w) as i64;
            for (dx, dy) in [(1, 0), (0, 1)] {
                let tiles = [(-2, -1), (-1, -2)];
                for (a, b) in tiles {
                    let at = |n: i64| {
                        Vec2::new(
                            tile_to_world_center(x + dx * n),
                            tile_to_world_center(y + dy * n),
                        )
                    };
                    let (Some(left), Some(right)) = (self.index(at(a)), self.index(at(-b))) else {
                        continue;
                    };
                    if self.available_for_target(left, zone)
                        && self.available(right, zone)
                        && segment_clear(
                            self.map,
                            self.point(left),
                            self.point(right),
                            0.,
                            MASK_SIGHT,
                        )
                    {
                        // Positions for the Warden and the opposing firing approach.
                        return Ok(vec![self.reserve(left), self.reserve(right)]);
                    }
                }
            }
        }
        Err("no reachable firing positions across a Gallows fence")
    }

    fn objectives(&mut self, quest: QuestId) -> Result<Vec<Vec2>, &'static str> {
        use FactionId::{Fire, Frost, Holy, Plague};
        use QuestId::*;
        let zone = quest.home();
        match quest {
            TheWarden => self.warden(),
            HoldTheFort => self.features(zone, |f| f.variant == "guardHouse", 1, 1, 0),
            BountyHunter | Gatecrasher | ACruelJoke | StayAWhile => Ok(Vec::new()),
            SmugglersRun | Letters | LostBrother | TuberEats => {
                let destination = self.distant(zone)?;
                Ok(vec![self.contact(destination)?])
            }
            TwoBanners => Ok(vec![self.camp(self.distant(zone)?, false)?]),
            EmberWalk => self.terrain(zone, 4, 3, 3.),
            TheBurningCrown => Ok(vec![self.camp(Fire, true)?]),
            LetHimCook => Ok(vec![
                self.contact(Holy)?,
                self.contact(Frost)?,
                self.camp(Plague, false)?,
            ]),
            EliteSlayer => Ok(vec![self.camp(Fire, true)?]),
            BarrowRun => {
                let mut points = self.features(
                    zone,
                    |f| f.kind == FeatureKind::Barrow && f.p.get(1) == Some(&0),
                    2,
                    2,
                    2,
                )?;
                points.extend(self.features(
                    zone,
                    |f| f.kind == FeatureKind::Barrow && f.p.get(1) == Some(&1),
                    1,
                    1,
                    0,
                )?);
                Ok(points)
            }
            TheBargain => Ok(vec![self.contact(zone)?]),
            ABoneToPick => Ok(vec![self.camp(Plague, true)?]),
            FeedOrHunt => {
                let mut points = vec![self.camp(Plague, false)?];
                for _ in 0..3 {
                    points.push(self.contact(Plague)?);
                }
                Ok(points)
            }
            RimeHunt => self.ice_camp(),
            Watchtowers => self.features(zone, |f| f.variant == "watchtower", 2, 2, 0),
            WhispersInTheBrush => self.brush(zone),
            EvenSkeletonsGetCold => Ok(vec![self.contact(Plague)?]),
            PilgrimsPath => self.pilgrims(),
            TradeUp => Ok(vec![
                self.contact(Fire)?,
                self.contact(Plague)?,
                self.contact(Frost)?,
            ]),
        }
    }
}

fn world(point: WorldPt) -> Vec2 {
    Vec2::new(point.x, point.y)
}

fn sq(value: f64) -> f64 {
    value * value
}

fn distance2(a: Vec2, b: Vec2) -> f64 {
    sq(a.x - b.x) + sq(a.y - b.y)
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn every_ordered_pair_is_drawn_once_per_zone() {
        let mut counts = [0; 25];
        for first in 0..5 {
            for second in 0..4 {
                let mut draws = (0..10).map(|i| {
                    if i % 2 == 0 {
                        (f64::from(first) + 0.5) / 5.
                    } else {
                        (f64::from(second) + 0.5) / 4.
                    }
                });
                let board = draw(&mut || draws.next().unwrap());
                assert!(draws.next().is_none());
                for pair in board.chunks_exact(2) {
                    assert_eq!(pair[0].home(), pair[1].home());
                    assert_ne!(pair[0], pair[1]);
                }
                for quest in board {
                    let index = QuestId::ALL.iter().position(|&id| id == quest).unwrap();
                    counts[index] += 1;
                }
            }
        }
        // Each quest is present in eight of the twenty equally likely pairings.
        assert_eq!(counts, [8; 25]);
    }

    #[test]
    fn exact_objective_landmarks_can_be_reserved_on_generated_maps() {
        use crate::mapgen::{coliseum, map_hash, pipeline};
        let layout = coliseum::fixed_layout();
        let generator = pipeline::Pipeline::new(&layout);
        for signature in [false, true] {
            let mut missing = QuestId::ALL.to_vec();
            let mut failures = Vec::new();
            for seed in 1..=8 {
                let options = pipeline::Options {
                    decor: false,
                    signature,
                    ..Default::default()
                };
                let Ok(map) = generator.generate_styled(seed, Some(0), options) else {
                    continue;
                };
                let arena = crate::mapdata::MapExport {
                    format: "rh-map/2".into(),
                    legacy_map_hash_u32: u32::from_str_radix(&map_hash::map_hash(&map), 16)
                        .unwrap(),
                    map,
                }
                .validate()
                .unwrap()
                .into_arena()
                .unwrap();
                let mut sites = Sites::new(&arena.map).unwrap();
                missing.retain(|&id| {
                    sites.reserved.fill(false);
                    match sites.objectives(id) {
                        Ok(points) => {
                            for point in points {
                                let index = sites.index(point).unwrap();
                                assert!(sites.reachable[index] && sites.reserved[index]);
                            }
                            false
                        }
                        Err(error) => {
                            failures.push((seed, id, error));
                            true
                        }
                    }
                });
                if missing.is_empty() {
                    break;
                }
            }
            assert!(missing.is_empty(), "{signature}: {missing:?}, {failures:?}");
        }
    }
}
