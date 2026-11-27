//! A generated board, normal seat actions and observed objectives for every catalog quest.
mod full_slots;
mod probe_tests;
mod spells;
use super::*;
use crate::input::{validate as validate_input, RawCommand};
use crate::mapgen::{coliseum, pipeline};
use crate::quest_view::{QuestHandle, QuestObserver, QuestView};
use crate::quests::board::{map_admission, placement, rewards};
use crate::{ArenaRules, Placement, Secrets, Sim, SimConfig, WeaponId};
use QuestId::*;

struct Raid {
    sim: Sim,
    seat: PlayerId,
    opponent: PlayerId,
    witness: PlayerId,
    bot: PlayerId,
    observer: QuestObserver,
    quest: QuestId,
    offer: u32,
    giver: Vec2,
}

fn new_raid(gen: &pipeline::Pipeline<'_>, secrets: Secrets) -> Sim {
    new_raid_with_patience(gen, secrets, 0.)
}

fn new_raid_with_patience(
    gen: &pipeline::Pipeline<'_>,
    secrets: Secrets,
    first_patience: f64,
) -> Sim {
    new_raid_at(
        gen,
        secrets,
        first_patience,
        crate::constants::LEGACY_TICK_MS as u32,
    )
}

/// A generated raid at a chosen step: 50 ms (the legacy rate) or 25 ms (the live server's 40 Hz default).
fn new_raid_at(
    gen: &pipeline::Pipeline<'_>,
    secrets: Secrets,
    first_patience: f64,
    tick_ms: u32,
) -> Sim {
    let arena = map_admission::generate(
        gen,
        1,
        0..32,
        pipeline::Options {
            decor: false,
            ..Default::default()
        },
        secrets,
    )
    .unwrap()
    .validate()
    .unwrap()
    .into_arena()
    .unwrap();
    let players: [Placement; 6] = std::array::from_fn(|i| Placement {
        profile: format!("accepted-route-{i}"),
        faction: crate::FactionId::Holy,
        bot: i >= 4,
        at: arena.spawns[i],
        hp: None,
        weapon0: Some(WeaponId::Sword),
        weapon1: Some(WeaponId::Bow),
    });
    Sim::reset_active(
        SimConfig {
            lifecycle: crate::ArenaMode::Source(ArenaRules {
                plan_seconds: 0.,
                first_patience_seconds: Some(first_patience),
                coliseum_schedule: None,
                patience_seconds: if first_patience > 0. { 6. } else { 300. },
                patience_min_seconds: if first_patience > 0. { 6. } else { 120. },
                ..Default::default()
            }),
            tick_ms,
            ..SimConfig::live_content()
        },
        arena,
        secrets,
        &players,
    )
}

impl Raid {
    fn new(sim: Sim, quest: QuestId) -> Self {
        let seat = sim.w.players[0].id;
        let opponent = sim.w.players[1].id;
        let witness = sim.w.players[2].id;
        let bot = sim.w.players[4].id;
        let o = sim
            .w
            .quests
            .as_ref()
            .unwrap()
            .offers
            .iter()
            .find(|o| kind(o) == Some(quest))
            .unwrap();
        let (offer, giver) = (o.id, o.at);
        Self {
            sim,
            seat,
            opponent,
            witness,
            bot,
            observer: QuestObserver::new(seat),
            quest,
            offer,
            giver,
        }
    }

    fn view(&mut self) -> QuestView {
        let prepared = self.observer.prepare(&self.sim).unwrap();
        let view = prepared.view.clone();
        prepared.commit();
        view
    }

    fn entry(&mut self) -> (QuestHandle, QuestView) {
        let view = self.view();
        let row = view
            .rows
            .iter()
            .find(|r| r.def == self.quest.definition().id)
            .unwrap_or_else(|| panic!("{:?} missing from journal", self.quest));
        (row.handle, view)
    }

    fn accept(&mut self) {
        self.arrange(self.seat, self.giver);
        let (handle, view) = self.entry();
        assert!(
            view.rows.iter().any(|r| r.handle == handle && r.can_accept),
            "{:?}",
            self.quest
        );
        let cmd = self.observer.resolve_accept(&self.sim, handle);
        self.sim.submit(self.seat, cmd).unwrap();
        self.sim.step();
        assert!(
            is_accepted(&self.sim.w, self.seat, self.offer),
            "{:?}",
            self.quest
        );
    }

    fn action(&mut self, action: u8, target: u8) {
        let (handle, view) = self.entry();
        let option = view
            .actions
            .iter()
            .find(|a| a.quest == handle && a.action == action && a.target == target)
            .unwrap_or_else(|| {
                panic!(
                    "{:?}: missing ({action}, {target}), had {:?}",
                    self.quest, view.actions
                )
            });
        assert!(!option.label.is_empty());
        let cmd = self
            .observer
            .resolve_interact(&self.sim, handle, action, target)
            .unwrap();
        self.sim.submit(self.seat, cmd).unwrap();
        self.sim.step();
    }

    fn action_near(&mut self, action: u8, target: u8, index: u8) {
        let at = self.objective(index);
        self.arrange(self.seat, at);
        self.action(action, target);
    }

    fn objective(&self, index: u8) -> Vec2 {
        let o = offer(&self.sim.w, self.offer).unwrap();
        let fallback = o.tokens.get(index as usize).copied();
        objective_for_owner(&self.sim.w, self.seat, self.offer, index)
            .or(fallback)
            .unwrap_or_else(|| panic!("{:?}: missing objective {index}", self.quest))
    }

    fn arrange(&mut self, id: PlayerId, at: Vec2) {
        let player = self.sim.w.players.get_mut(id).unwrap();
        player.body.x = at.x;
        player.body.y = at.y;
        player.body.history.reset(crate::state::Sample {
            t: self.sim.w.server_time,
            x: at.x,
            y: at.y,
        });
        player.motion.path.clear();
        player.motion.slip = None;
    }

    fn raw(&mut self, cmd: RawCommand) {
        self.sim
            .submit(self.seat, validate_input(cmd).unwrap())
            .unwrap();
        self.sim.step();
    }

    /// Steps covering `ticks` legacy 50 ms ticks of wall-clock time at this raid's step.
    fn legacy_steps(&mut self, ticks: u32) {
        let steps = self.sim.k.steps_of_legacy_ticks(ticks);
        self.steps(steps as usize);
    }

    fn steps(&mut self, ticks: usize) {
        for _ in 0..ticks {
            self.sim.step();
        }
    }

    fn spare_trades(&mut self) {
        let o = offer(&self.sim.w, self.offer).unwrap();
        self.arrange(self.seat, o.tokens[0]);
        self.action(7, 0);
    }

    fn root_fixture_target(&mut self, id: PlayerId, by: PlayerId) {
        self.fixture_status(id, by, crate::status_runtime::StatusKind::Root);
    }

    fn fixture_status(
        &mut self,
        id: PlayerId,
        by: PlayerId,
        kind: crate::status_runtime::StatusKind,
    ) {
        use crate::status_runtime::{self, StatusApply, StatusSource};
        let now = self.sim.w.server_time;
        let ix = self.sim.w.fighter_idx(id).unwrap();
        status_runtime::apply(
            &mut self.sim.w.core_mut(ix).statuses,
            now,
            StatusApply {
                kind,
                source: StatusSource {
                    id: Some(by),
                    owner_id: Some(by),
                    name: "Fixture status".into(),
                },
                ms: 2_000.,
                ability: None,
                tag: None,
                value: None,
                add: None,
                data: None,
            },
        );
    }

    fn strike(&mut self, victim: PlayerId, lethal: bool) {
        let ready = self.sim.w.player(self.seat).unwrap().weapon.ready_at;
        let wait =
            ((ready - self.sim.w.server_time).max(0.) / self.sim.k.tick_ms()).ceil() as usize;
        self.steps(wait);
        if self.sim.w.mobs.contains(victim) {
            // Trusted opponent setup freezes kiting, but the normal weapon
            // action, authoritative ledger and credited death remain in use.
            self.root_fixture_target(victim, self.seat);
        }
        let point = self.sim.w.actor(victim).unwrap().pos();
        let open = self.sim.k.collision_map(self.sim.w.rt.gates_locked);
        let offset = [
            Vec2::new(-30., 0.),
            Vec2::new(0., 30.),
            Vec2::new(30., 0.),
            Vec2::new(0., -30.),
        ]
        .into_iter()
        .find(|d| {
            crate::geometry::segment_clear(
                open,
                point,
                Vec2::new(point.x + d.x, point.y + d.y),
                crate::constants::PLAYER_RADIUS,
                crate::map::MASK_MOVE,
            )
        })
        .expect("one adjacent body-clear attack position");
        let at = Vec2::new(point.x + offset.x, point.y + offset.y);
        self.arrange(self.seat, at);
        let now = self.sim.w.server_time;
        let idx = self.sim.w.fighter_idx(victim).unwrap();
        let player_target = self.sim.w.players.contains(victim);
        let target = self.sim.w.core_mut(idx);
        if player_target {
            target.vitals.immune_until = now;
        }
        if lethal {
            target.vitals.hp = 0.1;
        }
        let aim = crate::math::atan2(point.y - at.y, point.x - at.x);
        self.raw(RawCommand::Attack { a: aim, t: None });
        self.legacy_steps(12);
        self.raw(RawCommand::Release { a: aim, t: None });
        self.legacy_steps(10);
        if lethal {
            assert!(
                !self.sim.w.standing(idx),
                "{:?}: expected kill credit for {victim:?}",
                self.quest
            );
        }
    }

    fn defeat_regional(&mut self, victim: PlayerId) {
        // Keep the native actor and its camp ID. Isolate the ranged test
        // target from terrain and unrelated opponents.
        let (at, ranged_from) = self.open_cast_site();
        let melee = self.quest == ABoneToPick;
        let from = if melee { at } else { ranged_from };
        let nearby: Vec<_> = self
            .sim
            .w
            .players
            .iter()
            .filter(|p| p.id != self.seat && p.standing())
            .map(|p| p.id)
            .collect();
        for id in nearby {
            self.arrange(id, self.giver);
        }
        let shooter = self.sim.w.player(self.seat).unwrap();
        let target = self.sim.w.mobs.get(victim).unwrap();
        let target_y = at.y + target.core.hull().body_oy() + shooter.core.hull().chest;
        let aim = if melee {
            0.
        } else {
            crate::math::atan2(target_y - from.y, at.x - from.x)
        };
        let weapon = if melee {
            WeaponId::Sword
        } else {
            WeaponId::Bow
        };
        if self.sim.w.player(self.seat).unwrap().active_weapon() != weapon {
            self.raw(RawCommand::Swap {
                slot: Some(u8::from(!melee)),
            });
        }
        let p = self.sim.w.player(self.seat).unwrap();
        let delay = ((p.weapon.ready_at - self.sim.w.server_time).max(0.) / self.sim.k.tick_ms())
            .ceil() as usize;
        self.steps(delay);
        self.arrange(self.seat, from);
        self.root_fixture_target(victim, self.seat);
        let now = self.sim.w.server_time;
        let mob = self.sim.w.mobs.get_mut(victim).unwrap();
        mob.core.body.x = at.x;
        mob.core.body.y = at.y;
        mob.core.body.history.reset(crate::state::Sample {
            t: now,
            x: at.x,
            y: at.y,
        });
        mob.core.motion.path.clear();
        mob.core.motion.slip = None;
        mob.core.weapon.ready_at = now + 1800.;
        mob.core.vitals.hp = 0.1;
        mob.regional_brain_mut().mode = crate::mob_runtime::BrainMode::Idle;
        self.raw(RawCommand::Attack { a: aim, t: None });
        self.legacy_steps(13);
        self.raw(RawCommand::Release { a: aim, t: None });
        self.legacy_steps(15);
        assert!(
            self.sim
                .w
                .mobs
                .get(victim)
                .is_none_or(|mob| mob.status == crate::state::Status::Dead),
            "validated {weapon:?} did not kill regional actor {victim:?}"
        );
    }

    fn bound_target(&self) -> Option<PlayerId> {
        board(&self.sim.w)?
            .targets
            .iter()
            .find(|t| t.offer == self.offer && t.alive)
            .map(|t| t.actor)
    }

    fn target(&self) -> PlayerId {
        self.bound_target().expect("a live quest actor")
    }

    fn wait_for_next_encounter(&mut self) {
        if !matches!(self.quest, TheWarden | ABoneToPick | RimeHunt) {
            return;
        }
        let actor = board(&self.sim.w)
            .unwrap()
            .targets
            .iter()
            .find(|target| target.offer == self.offer)
            .expect("prior quest encounter")
            .actor;
        let Some(crate::mob_runtime::ActorOrigin::Mob { camp, .. }) =
            self.sim.w.actor_origin(actor)
        else {
            panic!("quest encounter has no native camp");
        };
        let camp = *camp;
        if self.quest == ABoneToPick {
            let survivors: Vec<_> = self
                .sim
                .w
                .mobs
                .iter()
                .filter(|mob| mob.camp == camp && mob.status == crate::state::Status::Alive)
                .map(|mob| mob.core.id)
                .collect();
            for survivor in survivors {
                self.defeat_regional(survivor);
            }
        }
        let deadline = self.sim.w.mob_runtime.as_ref().unwrap().camps[camp as usize].respawn_at;
        assert!(deadline > self.sim.w.server_time);
        let wait = ((deadline - self.sim.w.server_time) / self.sim.k.tick_ms()).ceil() as usize;
        self.steps(wait + 1);
        assert!(
            self.bound_target().is_some_and(|next| next != actor),
            "{:?}: expected a new native encounter",
            self.quest
        );
    }

    fn gatecrash(&mut self) {
        // A short fixture timer still uses the normal arena teleport producer.
        // The lock reopens through settlement when the second opponent dies.
        let next = self.sim.w.rt.patience_ends_at.expect("first arena lock");
        let wait = ((next - self.sim.w.server_time) / self.sim.k.tick_ms()).ceil() as usize;
        self.steps(wait);
        assert!(self.sim.w.rt.gates_locked);
        self.strike(self.opponent, false);
        self.strike(self.bot, true);
        assert!(!self.sim.w.rt.gates_locked);
        assert!(ready(&self.sim.w, self.seat, self.offer));
    }

    fn deliver(&mut self) {
        match self.quest {
            TheWarden | ABoneToPick => self.strike(self.target(), true),
            EliteSlayer => {
                let victim = self
                    .bound_target()
                    .or_else(|| {
                        self.sim
                            .w
                            .mobs
                            .iter()
                            .find(|m| {
                                m.elite
                                    && m.status == crate::state::Status::Alive
                                    && m.core.faction_id() == Some(crate::FactionId::Fire)
                            })
                            .map(|m| m.core.id)
                    })
                    .expect("living Scorch elite");
                self.strike(victim, true);
            }
            RimeHunt => {
                for _ in 0..3 {
                    self.defeat_regional(self.target());
                }
            }
            HoldTheFort => {
                self.arrange(self.seat, self.objective(0));
                self.steps(
                    (crate::quests::board::tuning::TOP25.fort_ms / self.sim.k.tick_ms()) as usize
                        + 1,
                );
            }
            StayAWhile => {
                self.action(6, 0);
                self.steps(
                    (crate::quests::board::tuning::TOP25.meditate_ms / self.sim.k.tick_ms())
                        as usize
                        + 1,
                );
            }
            BountyHunter => {
                self.sim
                    .w
                    .players
                    .get_mut(self.opponent)
                    .unwrap()
                    .credit
                    .bounty = 10;
                self.arrange(self.opponent, self.giver);
                self.strike(self.opponent, true);
            }
            Gatecrasher => self.gatecrash(),
            SmugglersRun | LostBrother | TheBurningCrown | TuberEats | Letters
            | EvenSkeletonsGetCold => {
                self.action_near(3, 0, 0);
                if self.quest == TheBurningCrown {
                    self.legacy_steps(21);
                }
            }
            TwoBanners => {
                let branch = u8::from(self.seat == self.sim.w.players[0].id);
                self.action(2, branch);
                if branch == 1 {
                    self.action_near(3, 0, 0);
                }
            }
            EmberWalk | LetHimCook | WhispersInTheBrush | ACruelJoke | TradeUp => {
                for i in 0..3 {
                    self.action_near(3, i, i);
                }
            }
            FeedOrHunt => {
                for i in 1..=3 {
                    self.action_near(3, i, i);
                    self.action_near(3, 0, 0);
                    if i < 3 {
                        assert!(!is_completed(&self.sim.w, self.seat, self.offer));
                    }
                }
            }
            BarrowRun | Watchtowers | PilgrimsPath => {
                let n = offer(&self.sim.w, self.offer).unwrap().tokens.len();
                for i in 0..n {
                    self.arrange(self.seat, self.objective(i as u8));
                    self.sim.step();
                }
            }
            TheBargain => self.spare_trades(),
        }
        if !is_completed(&self.sim.w, self.seat, self.offer) {
            let turn_in = self.objective(0);
            self.arrange(self.seat, turn_in);
            self.sim.step();
            assert!(
                ready(&self.sim.w, self.seat, self.offer)
                    || is_completed(&self.sim.w, self.seat, self.offer),
                "{:?} did not finish its objective",
                self.quest
            );
        }
    }

    fn verify_coat(&mut self) {
        let layers = self.sim.k.arena.map.layers().unwrap();
        let data = layers.data();
        let tile = data
            .terrain
            .iter()
            .position(|&t| t == 5)
            .expect("Warren bog");
        let width = data.w as usize;
        let at = Vec2::new(
            crate::map::tile_to_world_center((tile % width) as i64),
            crate::map::tile_to_world_center((tile / width) as i64),
        );
        self.arrange(self.seat, at);
        self.arrange(self.opponent, at);
        let bog = |id| {
            crate::systems::terrain::bog_value(
                &self.sim.k,
                &self.sim.w,
                self.sim.w.fighter_idx(id).unwrap(),
                Default::default(),
            )
        };
        assert_eq!(bog(self.seat), 1.);
        assert!(bog(self.opponent) < 1.);
    }

    fn verify_helm(&self) {
        let shares = rewards::permanent::damage_shares(
            &self.sim.w,
            &crate::systems::ledger::Entry {
                terrain: false,
                dot: false,
                ability: None,
                arena: false,
                source: None,
                critical: false,
                critical_mult: None,
                sig: None,
                execute: false,
                victim: self.opponent,
                by: Some(self.seat),
                packet: crate::state::Packet {
                    raw: 100.,
                    phys: 1.,
                    magic: 0.,
                    true_: 0.,
                },
                weapon: Some(WeaponId::Axe),
                stagger: false,
                projectile: None,
                scale: 1.,
                derived: false,
                passive: false,
            },
        );
        assert_eq!((shares.phys, shares.true_), (0.8, 0.2));
    }

    fn compare_open_boards(&mut self) {
        let first = self.view();
        let mut second = QuestObserver::new(self.witness);
        let prepared = second.prepare(&self.sim).unwrap();
        let rows = |v: &QuestView| {
            v.rows
                .iter()
                .map(|row| (row.def.clone(), row.giver))
                .collect::<Vec<_>>()
        };
        assert_eq!(
            rows(&first),
            rows(&prepared.view),
            "{:?}: shared initial board",
            self.quest
        );
        assert_eq!(first.rows.len(), 10);
        prepared.commit();
    }

    fn confirm_independent_restore(&mut self) {
        let mut witness = QuestObserver::new(self.witness);
        self.arrange(self.witness, self.giver);
        let initial = witness.prepare(&self.sim).unwrap();
        let row = initial
            .view
            .rows
            .iter()
            .find(|row| row.def == self.quest.definition().id)
            .expect("a different seat still sees the completed quest's offer");
        let handle = row.handle;
        assert!(row.can_accept && !row.owner_accepted);
        initial.commit();

        let command = witness.resolve_accept(&self.sim, handle);
        self.sim.submit(self.witness, command).unwrap();
        self.sim.step();
        assert!(is_accepted(&self.sim.w, self.witness, self.offer));

        let bytes = serde_json::to_vec(&self.sim).unwrap();
        let restored = Sim::restore(&mut serde_json::Deserializer::from_slice(&bytes))
            .unwrap_or_else(|e| panic!("{:?} checkpoint: {e}", self.quest));
        assert_eq!(
            restored.state_hash(),
            self.sim.state_hash(),
            "{:?}",
            self.quest
        );
        self.sim = restored;
        self.observer.reconnect();
        witness.reconnect();
        let first = self.view();
        let second = witness.prepare(&self.sim).unwrap();
        assert!(!first
            .rows
            .iter()
            .any(|row| row.def == self.quest.definition().id));
        assert!(second
            .view
            .rows
            .iter()
            .any(|row| row.def == self.quest.definition().id && row.owner_accepted));
        second.commit();
    }

    fn finish_as_second_owner(&mut self) {
        self.wait_for_next_encounter();
        if self.quest == ACruelJoke {
            self.steps((10_000. / self.sim.k.tick_ms()) as usize + 1);
        }
        self.confirm_independent_restore();
        let first = self.seat;
        let second = self.witness;
        self.seat = second;
        self.witness = first;
        self.opponent = self.sim.w.players[3].id;
        self.bot = self.sim.w.players[5].id;
        self.observer = QuestObserver::new(second);
        self.deliver();
        self.receipt();

        let bytes = serde_json::to_vec(&self.sim).unwrap();
        let restored = Sim::restore(&mut serde_json::Deserializer::from_slice(&bytes))
            .unwrap_or_else(|e| panic!("{:?} second completion checkpoint: {e}", self.quest));
        assert_eq!(
            restored.state_hash(),
            self.sim.state_hash(),
            "{:?}",
            self.quest
        );
        for owner in [first, second] {
            assert!(is_completed(&restored.w, owner, self.offer));
            let mut observer = QuestObserver::new(owner);
            let established = observer.prepare(&self.sim).unwrap();
            established.commit();
            observer.reconnect();
            let view = observer.prepare(&restored).unwrap();
            assert!(!view
                .view
                .rows
                .iter()
                .any(|row| row.def == self.quest.definition().id));
        }
    }

    fn receipt(&mut self) {
        use super::super::catalog::{RewardId, RewardKind};
        let reward = self.quest.definition().reward;
        let award = rewards::Award::from(reward);
        assert!(
            is_completed(&self.sim.w, self.seat, self.offer),
            "{:?} did not turn in",
            self.quest
        );
        let view = self.view();
        assert!(!view
            .rows
            .iter()
            .any(|r| r.def == self.quest.definition().id));
        if reward.kind() == RewardKind::Permanent {
            assert!(
                rewards::has(&self.sim.w, self.seat, award),
                "{:?}",
                self.quest
            );
            let p = self.sim.w.player(self.seat).unwrap();
            use crate::mob_catalog::MobTypeId::{EmberImp, Penitent};
            match self.quest {
                TheBurningCrown => assert_eq!(p.farm.count(EmberImp), 1),
                TheBargain => {
                    assert!(p.weapon.weapon1.is_none());
                    assert_eq!(p.farm.count(EmberImp), 29);
                }
                TradeUp => assert_eq!(p.farm.count(Penitent), 29),
                FeedOrHunt => assert_eq!(p.farm.count(EmberImp), 12),
                LetHimCook => {
                    assert!(rewards::permanent::chef_row(&self.sim.w, self.seat).is_some())
                }
                EvenSkeletonsGetCold => self.verify_coat(),
                TuberEats => self.verify_helm(),
                _ => {}
            }
        } else {
            let spells = rewards::owned_spells(&self.sim.w, self.seat);
            assert!(
                spells.into_iter().flatten().any(|s| s.award == award),
                "{:?}",
                self.quest
            );
            if reward == RewardId::BlindRageAndStillMind {
                assert!(spells
                    .into_iter()
                    .flatten()
                    .any(|s| s.award == rewards::Award::StillMind));
            }
        }
        if reward.kind() != RewardKind::Permanent {
            self.verify_spell(award);
        }
    }
}

#[test]
fn all_twenty_five_quests_accept_exercise_their_objective_and_turn_in() {
    sweep_every_quest(crate::constants::LEGACY_TICK_MS as u32);
}

/// The same sweep at the live server's default step (40 Hz): every quest's timers, channels, attacks and turn-ins hold
/// when each step is 25 ms.
#[test]
fn all_twenty_five_quests_complete_at_the_live_40_hz_step() {
    sweep_every_quest(25);
}

fn sweep_every_quest(tick_ms: u32) {
    let fixed = coliseum::fixed_layout();
    let generator = pipeline::Pipeline::new(&fixed);
    let mut uncovered = [true; 25];
    let mut failed = Vec::new();
    for seed in 1..=128 {
        let secrets = Secrets::from_seed(seed);
        let mut stream = crate::private_rng::Sfc32::from_words(crate::quests::live::quest_salt(
            secrets.reward_salt,
        ));
        let assignment = placement::draw(&mut || stream.next_f64());
        if !assignment.iter().any(|id| uncovered[*id as usize]) {
            continue;
        }
        let sim = new_raid_at(&generator, secrets, 0., tick_ms);
        for quest in assignment {
            if uncovered[quest as usize] {
                let result = std::panic::catch_unwind(|| {
                    let world = if quest == Gatecrasher {
                        new_raid_at(&generator, secrets, 1., tick_ms)
                    } else {
                        sim.clone()
                    };
                    let mut raid = Raid::new(world, quest);
                    raid.compare_open_boards();
                    raid.accept();
                    raid.deliver();
                    raid.receipt();
                    raid.finish_as_second_owner();
                });
                if result.is_err() {
                    failed.push(quest);
                }
                uncovered[quest as usize] = false;
            }
        }
        if !uncovered.contains(&true) {
            assert!(failed.is_empty(), "catalog scenarios failed: {failed:?}");
            return;
        }
    }
    panic!("catalog sweep missed quests: {uncovered:?}");
}

#[test]
fn a_published_lifesteal_trade_can_be_submitted_and_settled() {
    use crate::mob_catalog::MobTypeId::{PlagueRat, RotMonk};
    let layout = coliseum::fixed_layout();
    let generator = pipeline::Pipeline::new(&layout);
    let mut raid = Raid::new(new_raid(&generator, Secrets::from_seed(2)), TheBargain);
    raid.accept();
    let at = raid.objective(0);
    raid.arrange(raid.seat, at);
    let farm = &mut raid.sim.w.players.get_mut(raid.seat).unwrap().farm;
    farm.set(RotMonk, crate::quests::board::tuning::TOP25.bargain_cost);
    let previous = farm.count(PlagueRat);

    let (handle, journal) = raid.entry();
    let choice = journal
        .actions
        .iter()
        .find(|a| a.quest == handle && a.label == "Trade lifesteal for attack speed")
        .expect("the broker offers the player's lifesteal stacks");
    raid.action(choice.action, choice.target);

    assert!(is_completed(&raid.sim.w, raid.seat, raid.offer));
    let farm = &raid.sim.w.player(raid.seat).unwrap().farm;
    assert_eq!(farm.count(RotMonk), 0);
    assert_eq!(farm.count(PlagueRat), previous + 29);
    assert!(!is_completed(&raid.sim.w, raid.witness, raid.offer));
}
