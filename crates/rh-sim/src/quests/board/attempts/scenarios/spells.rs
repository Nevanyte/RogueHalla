//! Exercise each awarded spell through validated casts in a generated raid.
use super::*;
use crate::quests::board::rewards::{effects, Award};
use crate::status_runtime::{self, StatusKind};

impl Raid {
    fn cast_input(&mut self, slot: usize, at: Vec2, target: Option<PlayerId>) {
        use crate::abilities::Slot;
        self.raw(RawCommand::Cast {
            slot: if slot == 0 { Slot::Q } else { Slot::E },
            x: at.x,
            y: at.y,
            id: target,
            t: None,
            seq: None,
        });
    }

    fn use_spell(&mut self, award: Award, at: Vec2, target: Option<PlayerId>) {
        let spells = rewards::owned_spells(&self.sim.w, self.seat);
        let slot = spells
            .iter()
            .position(|spell| spell.is_some_and(|s| s.award == award))
            .unwrap();
        self.cast_input(slot, at, target);
        let after = rewards::owned_spells(&self.sim.w, self.seat)[slot];
        assert!(
            after.is_none_or(|s| s.charges < spells[slot].unwrap().charges
                || s.ready_at > self.sim.w.server_time),
            "{:?}: {award:?} was not used by the seat's validated cast",
            self.quest
        );
    }

    pub(super) fn open_cast_site(&self) -> (Vec2, Vec2) {
        let map = self.sim.collision_map();
        let data = map.layers().unwrap().data();
        let c = data.coliseum.centre;
        let from = Vec2::new(c.x, c.y);
        let to = [
            Vec2::new(90., 0.),
            Vec2::new(0., 90.),
            Vec2::new(-90., 0.),
            Vec2::new(0., -90.),
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
        let (from, to) = self.open_cast_site();
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
                assert!(w
                    .abilities
                    .as_ref()
                    .unwrap()
                    .fields
                    .iter()
                    .any(|f| effects::is_cage(w, f)));
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
            }
            Tornado => {
                let max = self.sim.w.player(self.seat).unwrap().vitals.max_hp * 0.5;
                let victim = self.sim.w.players.get_mut(rival).unwrap();
                let prior = (victim.vitals.max_hp, victim.vitals.hp);
                victim.vitals.max_hp = max;
                victim.vitals.hp = max;
                self.use_spell(award, from, None);
                self.legacy_steps(14);
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
                self.legacy_steps(25);
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
                self.use_spell(award, from, None);
                assert_eq!(effects::taunt(&self.sim.w, rival), Some(self.seat));
            }
            Smite => {
                let target = self
                    .sim
                    .w
                    .mobs
                    .iter()
                    .find(|m| {
                        m.kind == crate::mob_runtime::MobKind::Regional
                            && m.status == crate::state::Status::Alive
                    })
                    .unwrap();
                let (id, at, hp) = (target.core.id, target.core.pos(), target.core.vitals.hp);
                self.arrange(self.seat, at);
                self.use_spell(award, at, Some(id));
                self.legacy_steps(2);
                assert!(self.sim.w.actor(id).is_none_or(|a| a.vitals.hp < hp));
            }
            SpellShield => {
                self.use_spell(award, from, None);
                assert!(effects::shielding(&self.sim.w, self.seat));
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
                self.legacy_steps(65);
                let revived = self.sim.w.player(owner).unwrap();
                assert_eq!(revived.standing(), survivors > 3);
                if survivors > 3 {
                    assert!(distance(revived.pos(), self.giver) < 5.);
                    assert_eq!(revived.active_weapon(), gear);
                }
                assert!(!self.sim.w.rebirth_eggs.iter().any(|e| e.owner == owner));
            }
            ForgetMeNotSeed => {
                let slot = rewards::owned_spells(&self.sim.w, self.seat)
                    .iter()
                    .position(|s| s.is_some_and(|s| s.award == award))
                    .unwrap();
                self.cast_input(slot, from, None);
                self.arrange(self.seat, to);
                self.use_spell(award, from, None);
                assert!(distance(self.sim.w.player(self.seat).unwrap().pos(), from) < 5.);
            }
            SwapStick => {
                let swap = validate_input(RawCommand::Swap { slot: Some(1) }).unwrap();
                self.sim.submit(rival, swap).unwrap();
                self.sim.step();
                let before = self.sim.w.player(rival).unwrap().active_weapon();
                self.use_spell(award, to, Some(rival));
                assert_eq!(
                    self.sim.w.player(self.seat).unwrap().active_weapon(),
                    before
                );
            }
            Cleanse => {
                self.root_fixture_target(self.seat, rival);
                let rooted = |w: &World, id| {
                    status_runtime::has(
                        &w.player(id).unwrap().statuses.instances,
                        StatusKind::Root,
                        w.server_time,
                    )
                };
                assert!(rooted(&self.sim.w, self.seat));
                self.use_spell(award, from, None);
                assert!(!rooted(&self.sim.w, self.seat));
            }
            Stasis => {
                self.use_spell(award, from, None);
                assert!(effects::stasis(&self.sim.w, self.seat));
            }
            Bolas => {
                self.use_spell(award, to, None);
                self.legacy_steps(4);
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
                return;
            }
        }
        panic!("WallBuster found no editable interior wall");
    }
}
