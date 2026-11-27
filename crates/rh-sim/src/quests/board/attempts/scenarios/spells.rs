//! Exercise each awarded spell through validated casts in a generated raid.
use super::*;
use crate::quests::board::rewards::{effects, Award};
use crate::quests::board::tuning::TOP25;
use crate::status_runtime::{self, StatusKind};

impl Raid {
    fn cast_input(&mut self, slot: usize, at: Vec2, target: Option<PlayerId>) {
        use crate::abilities::Slot;
        self.begin_cast_capture();
        self.raw(RawCommand::Cast {
            slot: if slot == 0 { Slot::Q } else { Slot::E },
            x: at.x,
            y: at.y,
            id: target,
            t: None,
            seq: None,
        });
        self.capture_cast_frame("start");
    }

    fn use_spell(&mut self, award: Award, at: Vec2, target: Option<PlayerId>) {
        let spells = rewards::owned_spells(&self.sim.w, self.seat);
        let slot = spells
            .iter()
            .position(|spell| spell.is_some_and(|s| s.award == award))
            .unwrap();
        self.cast_input(slot, at, target);
        let after = rewards::owned_spells(&self.sim.w, self.seat)[slot];
        let before = spells[slot].unwrap();
        if let Some(after) = after {
            assert_eq!(after.award, award);
            assert_eq!(
                after.charges + 1,
                before.charges,
                "{award:?} must consume one charge"
            );
            assert!(
                after.ready_at > self.sim.w.server_time,
                "{award:?} must begin recharging"
            );
        } else {
            assert_eq!(
                before.charges, 1,
                "{award:?} only leaves the slot on a final charge"
            );
        }
    }

    pub(super) fn open_cast_site(&self, offset: f64) -> (Vec2, Vec2) {
        let map = self.sim.collision_map();
        let data = map.layers().unwrap().data();
        let c = data.coliseum.centre;
        let from = Vec2::new(c.x, c.y);
        let to = [
            Vec2::new(offset, 0.),
            Vec2::new(0., offset),
            Vec2::new(-offset, 0.),
            Vec2::new(0., -offset),
        ]
        .into_iter()
        .map(|d| Vec2::new(from.x + d.x, from.y + d.y))
        .find(|&to| {
            crate::geometry::segment_clear(
                map,
                from,
                to,
                crate::constants::PLAYER_RADIUS,
                crate::map::MASK_MOVE,
            )
        })
        .unwrap();
        (from, to)
    }

    pub(super) fn verify_spell(&mut self, award: Award) {
        use Award::*;
        // Leave at least one live 25 ms sample between the projectile launch and its impact.
        let offset = if matches!(award, Hook | Bolas) {
            180.
        } else {
            90.
        };
        let (from, to) = self.open_cast_site(offset);
        let rival = [self.opponent, self.bot]
            .into_iter()
            .find(|&id| self.sim.w.player(id).is_some_and(|p| p.standing()))
            .unwrap();
        let other_seats: Vec<_> = self
            .sim
            .w
            .players
            .iter()
            .filter(|p| p.id != self.seat && p.id != rival && p.standing())
            .map(|p| p.id)
            .collect();
        for seat in other_seats {
            self.arrange(seat, self.giver);
        }
        self.arrange(self.seat, from);
        self.arrange(rival, to);
        match award {
            WardensCage => {
                self.use_spell(award, from, None);
                let w = &self.sim.w;
                let abilities = w.abilities.as_ref().unwrap();
                let fence = abilities
                    .fields
                    .iter()
                    .find(|field| effects::is_cage(w, field))
                    .unwrap();
                let barrier = abilities
                    .barriers
                    .0
                    .iter()
                    .find(|barrier| barrier.field == fence.id)
                    .unwrap();
                let held = crate::barriers::held(w);
                let map = crate::barriers::body_map(&self.sim.k, &held, w.rt.gates_locked);
                assert!(!barrier.cells.is_empty());
                for &cell in &barrier.cells {
                    let x = i64::from(cell % map.w);
                    let y = i64::from(cell / map.w);
                    let at = Vec2::new(
                        crate::map::tile_to_world_center(x),
                        crate::map::tile_to_world_center(y),
                    );
                    assert!(!crate::geometry::circle_clear(
                        map,
                        at,
                        0.,
                        crate::map::MASK_MOVE
                    ));
                    assert!(!crate::geometry::circle_clear(
                        map,
                        at,
                        0.,
                        crate::map::MASK_MELEE
                    ));
                    assert!(crate::geometry::circle_clear(
                        map,
                        at,
                        0.,
                        crate::map::MASK_SIGHT
                    ));
                }
            }
            Hook => {
                self.use_spell(award, to, None);
                self.legacy_steps(4);
                assert!(
                    distance(self.sim.w.player(rival).unwrap().pos(), from)
                        < distance(to, from) - 1.,
                    "Hook did not move its enemy toward the caster"
                );
            }
            ShoulderCharge => {
                let before = self.sim.w.player(self.seat).unwrap().motion.displace_seq;
                self.use_spell(award, to, None);
                let caster = self.sim.w.player(self.seat).unwrap();
                assert_ne!(caster.motion.displace_seq, before);
                assert!(
                    distance(caster.pos(), to) < distance(from, to),
                    "Shoulder Charge did not close ground toward its target"
                );
                assert!(
                    self.has_status(rival, StatusKind::Stun),
                    "Charge contact must stun"
                );
                assert!(
                    distance(self.sim.w.player(rival).unwrap().pos(), from) > distance(to, from),
                    "Charge contact must knock the victim away"
                );
            }
            Tornado => {
                let max = self.sim.w.player(self.seat).unwrap().vitals.max_hp * 0.5;
                let victim = self.sim.w.players.get_mut(rival).unwrap();
                let prior = (victim.vitals.max_hp, victim.vitals.hp);
                victim.vitals.max_hp = max;
                victim.vitals.hp = max;
                self.use_spell(award, from, None);
                self.legacy_steps(11);
                assert_eq!(
                    self.sim.w.player(rival).unwrap().pos(),
                    to,
                    "Tornado must wait for impact"
                );
                self.legacy_steps(3);
                assert!(
                    distance(self.sim.w.player(rival).unwrap().pos(), from)
                        < distance(to, from) - 1.
                );
                let victim = self.sim.w.players.get_mut(rival).unwrap();
                (victim.vitals.max_hp, victim.vitals.hp) = prior;
            }
            Turret => {
                let now = self.sim.w.server_time;
                let victim = self.sim.w.players.get_mut(rival).unwrap();
                victim.vitals.immune_until = now;
                let hp = victim.vitals.hp;
                self.use_spell(award, to, None);
                let mut hit = false;
                for _ in 0..self.sim.k.steps_of_legacy_ticks(25) {
                    self.step();
                    if !hit && self.sim.w.player(rival).unwrap().vitals.hp < hp {
                        self.capture_cast_frame("hit");
                        hit = true;
                    }
                }
                assert!(hit);
                assert!(self.sim.w.player(rival).unwrap().vitals.hp < hp);
            }
            LanternWard | ScoutFalcon => {
                let remote = self
                    .sim
                    .k
                    .arena
                    .spawns
                    .iter()
                    .copied()
                    .find(|&at| distance(at, to) > crate::constants::VISION_RADIUS)
                    .expect("a seat outside ordinary vision");
                self.arrange(rival, remote);
                self.sim.w.players.get_mut(rival).unwrap().vision = Default::default();
                self.use_spell(award, to, None);
                self.legacy_steps(6);
                self.arrange(self.seat, remote);
                // Prior ordinary sight has a 30-second trail. Clear it to isolate the cast.
                self.sim.w.players.get_mut(self.seat).unwrap().vision = Default::default();
                assert!(self.sim.can_see_point(self.seat, to));
                assert!(!self.sim.can_see_point(rival, to));
            }
            Warhorn => {
                let now = self.sim.w.server_time;
                self.sim
                    .w
                    .players
                    .get_mut(rival)
                    .unwrap()
                    .vitals
                    .immune_until = now;
                self.use_spell(award, from, None);
                assert_eq!(effects::taunt(&self.sim.w, rival), Some(self.seat));
                self.legacy_steps(4);
                assert!(
                    distance(self.sim.w.player(rival).unwrap().pos(), from) < distance(to, from)
                );
                assert!(effects::damage_factor(&self.sim.w, self.seat) >= 1.2);
                assert!(effects::damage_taken_factor(&self.sim.w, self.seat) <= 0.85);
            }
            Smite => {
                let target = self
                    .sim
                    .w
                    .mobs
                    .iter()
                    .find(|m| {
                        m.kind == crate::mob_runtime::MobKind::Regional
                            && !m.elite
                            && m.status == crate::state::Status::Alive
                    })
                    .unwrap();
                let (id, at) = (target.core.id, target.core.pos());
                self.arrange(self.seat, at);
                let mob = self.sim.w.mobs.get_mut(id).unwrap();
                mob.core.vitals.hp = 1.;
                // This rival wins ordinary damage credit. Lethal Smite should take the kill.
                crate::systems::mob_ai::add_threat(mob.regional_brain_mut(), rival, 100.);
                let before = self.sim.w.player(self.seat).unwrap().credit.clone();
                let rival_kills = self.sim.w.player(rival).unwrap().credit.mob_kills;
                self.use_spell(award, at, Some(id));
                assert!(self
                    .sim
                    .w
                    .mobs
                    .get(id)
                    .is_none_or(|m| m.status == crate::state::Status::Dead));
                let after = &self.sim.w.player(self.seat).unwrap().credit;
                assert_eq!(after.mob_kills - before.mob_kills, 1);
                assert_eq!(
                    self.sim.w.player(rival).unwrap().credit.mob_kills,
                    rival_kills
                );
                assert!(
                    after.gv_parts.mobs - before.gv_parts.mobs > 15,
                    "Smite should grant its 15 GV bonus in addition to the normal mob award"
                );
            }
            SpellShield => {
                self.use_spell(award, from, None);
                assert!(effects::shielding(&self.sim.w, self.seat));
                let owner = self.seat;
                self.equip_fixture(rival, Bolas);
                let cast = validate_input(RawCommand::Cast {
                    slot: crate::abilities::Slot::Q,
                    x: from.x,
                    y: from.y,
                    id: None,
                    t: None,
                    seq: None,
                })
                .unwrap();
                self.sim.submit(rival, cast).unwrap();
                self.legacy_steps(4);
                assert!(!self.has_status(owner, StatusKind::Root));
                assert!(!effects::shielding(&self.sim.w, owner));
                let shield = rewards::owned_spells(&self.sim.w, owner)[0].unwrap();
                assert_eq!(shield.charges, 1);
                assert!(shield.ready_at <= self.sim.w.server_time);
            }
            RebirthEgg => {
                self.arrange(self.seat, self.giver);
                self.use_spell(award, self.giver, None);
                assert!(self.sim.w.rebirth_eggs.iter().any(|e| e.owner == self.seat));

                let owner = self.seat;
                let gear = self.sim.w.player(owner).unwrap().active_weapon();
                let survivors = self
                    .sim
                    .w
                    .players
                    .iter()
                    .filter(|p| p.id != owner && p.standing())
                    .count();
                self.arrange(owner, from);
                self.seat = rival;
                self.strike(owner, true);
                self.seat = owner;
                assert!(!self.sim.w.player(owner).unwrap().standing());
                self.capture_cast_frame("death");
                self.legacy_steps(65);
                let revived = self.sim.w.player(owner).unwrap();
                assert_eq!(revived.standing(), survivors > 3);
                if survivors > 3 {
                    assert!(distance(revived.pos(), self.giver) < 5.);
                    assert_eq!(revived.active_weapon(), gear);
                    assert!(
                        (TOP25.egg_hatch_hp_share..=TOP25.egg_hatch_hp_share + 0.02)
                            .contains(&(revived.vitals.hp / revived.vitals.max_hp)),
                        "Hatch must restore about 40% maximum health"
                    );
                }
                assert!(!self.sim.w.rebirth_eggs.iter().any(|e| e.owner == owner));
            }
            ForgetMeNotSeed => {
                let slot = rewards::owned_spells(&self.sim.w, self.seat)
                    .iter()
                    .position(|s| s.is_some_and(|s| s.award == award))
                    .unwrap();
                self.cast_input(slot, from, None);
                self.capture_cast_frame("end");
                self.arrange(self.seat, to);
                self.use_spell(award, from, None);
                assert!(distance(self.sim.w.player(self.seat).unwrap().pos(), from) < 5.);
            }
            SwapStick => {
                let swap = validate_input(RawCommand::Swap { slot: Some(1) }).unwrap();
                self.sim.submit(rival, swap).unwrap();
                self.step();
                let before = self.sim.w.player(rival).unwrap().active_weapon();
                let own_weapon = self.sim.w.player(self.seat).unwrap().active_weapon();
                self.use_spell(award, to, Some(rival));
                assert_eq!(
                    self.sim.w.player(self.seat).unwrap().active_weapon(),
                    before
                );
                assert_eq!(
                    self.sim.w.player(rival).unwrap().active_weapon(),
                    own_weapon
                );
                self.capture_cast_frame("effect");
                self.steps((TOP25.swap_ms / self.sim.k.tick_ms()) as usize);
                assert_eq!(
                    self.sim.w.player(self.seat).unwrap().active_weapon(),
                    own_weapon
                );
                assert_eq!(self.sim.w.player(rival).unwrap().active_weapon(), before);
            }
            Cleanse => {
                self.root_fixture_target(self.seat, rival);
                self.fixture_status(self.seat, rival, StatusKind::Stun);
                assert!(self.has_status(self.seat, StatusKind::Root));
                assert!(self.has_status(self.seat, StatusKind::Stun));
                self.use_spell(award, from, None);
                assert!(!self.has_status(self.seat, StatusKind::Root));
                assert!(!self.has_status(self.seat, StatusKind::Stun));
            }
            Stasis => {
                self.use_spell(award, from, None);
                assert!(effects::stasis(&self.sim.w, self.seat));
                self.raw(RawCommand::Move {
                    x: to.x,
                    y: to.y,
                    seq: None,
                });
                self.legacy_steps(4);
                assert_eq!(self.sim.w.player(self.seat).unwrap().pos(), from);
                self.capture_cast_frame("effect");
                self.steps((TOP25.stasis_ms / self.sim.k.tick_ms()) as usize);
                assert!(!effects::stasis(&self.sim.w, self.seat));
                self.raw(RawCommand::Move {
                    x: to.x,
                    y: to.y,
                    seq: None,
                });
                self.legacy_steps(4);
                assert!(
                    distance(self.sim.w.player(self.seat).unwrap().pos(), to) < distance(from, to)
                );
            }
            Bolas => {
                self.use_spell(award, to, None);
                let mut rooted = false;
                for _ in 0..self.sim.k.steps_of_legacy_ticks(4) {
                    self.step();
                    if !rooted && self.has_status(rival, StatusKind::Root) {
                        self.capture_cast_frame("hit");
                        rooted = true;
                    }
                }
                let target = self.sim.w.player(rival).unwrap();
                assert!(status_runtime::has(
                    &target.statuses.instances,
                    StatusKind::Root,
                    self.sim.w.server_time
                ));
            }
            WallBuster => self.bust_wall(),
            BlindRage => {
                self.use_spell(award, from, None);
                assert_eq!(effects::attack_rate(&self.sim.w, self.seat), 1.2);
                self.capture_cast_frame("end");
                self.fixture_status(self.seat, rival, StatusKind::Stun);
                let seat = self.seat;
                let stunned = |w: &World| {
                    status_runtime::has(
                        &w.player(seat).unwrap().statuses.instances,
                        StatusKind::Stun,
                        w.server_time,
                    )
                };
                assert!(stunned(&self.sim.w));
                let slot = rewards::owned_spells(&self.sim.w, self.seat)
                    .iter()
                    .position(|s| s.is_some_and(|s| s.award == StillMind))
                    .unwrap();
                self.cast_input(slot, from, None);
                assert!(!stunned(&self.sim.w), "StillMind must break an active stun");
                assert!(rewards::owned_spells(&self.sim.w, self.seat)[slot].is_none());
                assert!(effects::blocks_status(
                    &self.sim.w,
                    self.seat,
                    StatusKind::Stun
                ));
            }
            _ => panic!("{award:?} is not a quest spell under test"),
        }
    }

    fn has_status(&self, id: PlayerId, kind: StatusKind) -> bool {
        status_runtime::has(
            &self.sim.w.player(id).unwrap().statuses.instances,
            kind,
            self.sim.w.server_time,
        )
    }

    fn equip_fixture(&mut self, id: PlayerId, award: Award) {
        let now = self.sim.w.server_time;
        let state = self
            .sim
            .w
            .quests
            .as_mut()
            .unwrap()
            .rewards
            .as_mut()
            .unwrap();
        assert!(!state.owners.iter().any(|owner| owner.id == id));
        state.owners.push(Default::default());
        let owner = state.owners.last_mut().unwrap();
        owner.id = id;
        owner.slots[0] = Some(rewards::Owned::new(award, now));
    }

    fn bust_wall(&mut self) {
        let map = self.sim.collision_map();
        let w = map.w as i64;
        let h = map.h as i64;
        let data = map.layers().unwrap().data();
        let candidate = |x, y| {
            let i = (y * w + x) as usize;
            data.region_grid[i] != 0
                && matches!(data.terrain[i], 1..=3)
                && map.tile(x, y).is_some_and(|t| t != crate::map::TILE_FLOOR)
                && !data.camps.iter().any(|c| {
                    c.chest.x == crate::map::tile_to_world_center(x)
                        && c.chest.y == crate::map::tile_to_world_center(y)
                })
        };
        let mut trial = Vec::new();
        for y in 3..h - 3 {
            for x in 3..w - 3 {
                if !candidate(x, y) {
                    continue;
                }
                let at = |x, y| {
                    Vec2::new(
                        crate::map::tile_to_world_center(x),
                        crate::map::tile_to_world_center(y),
                    )
                };
                if let Some((a, b)) = [(x - 2, y), (x + 2, y), (x, y - 2), (x, y + 2)]
                    .into_iter()
                    .find(|&(a, b)| {
                        crate::geometry::circle_clear(
                            map,
                            at(a, b),
                            crate::constants::PLAYER_RADIUS,
                            crate::map::MASK_MOVE,
                        )
                    })
                {
                    trial.push((at(a, b), at(x, y)));
                }
            }
        }
        for (from, at) in trial.into_iter().take(10) {
            self.arrange(self.seat, from);
            let before = self.sim.w.destroyed_terrain.cells.len();
            self.cast_input(0, at, None);
            if self.sim.w.destroyed_terrain.cells.len() > before {
                let x = crate::map::world_to_tile(at.x);
                let y = crate::map::world_to_tile(at.y);
                let index = y as u32 * self.sim.arena().map.w + x as u32;
                assert!(self.sim.w.destroyed_terrain.cells.contains(&index));
                assert_eq!(
                    self.sim.collision_map().tile(x, y),
                    Some(crate::map::TILE_FLOOR)
                );
                return;
            }
            self.discard_cast_capture();
        }
        panic!("WallBuster found no editable interior wall");
    }
}
