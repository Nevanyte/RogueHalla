//! The read-only observer of one `balance_lab` match: after each step it reads `&Sim`, the step's events and the seats'
//! ordinary observations, and folds them into an [`Acc`]. It never submits, draws or mutates (purity is tested by
//! comparing state hashes with an unwatched run). Sources per metric: docs/backlog/packets/P-BAL-LAB-1.md.
use super::acc::{Acc, Merge, MinuteAcc, NativeAcc, SeatReport};
use super::attempts::{ProbeRow, SeatTally, StepRead};
use super::chest_picks::{tally_chests, ChestKind, ChestRow, ChestStep, OptionRow};
use super::credit_window_open;
use super::elite_fights::{tally_elites, Contact, EliteDeath, EliteStep, SeatDeath};
use super::time_budget::{SeatStep, TimeStep, TimeTally};
use rh_sim::cards::native::readouts;
use rh_sim::mob_catalog::{normal_types_of, MobTypeId};
use rh_sim::quest_view::QuestView;
use rh_sim::reward_chests::{BoostId, OptionId, RewardChestView, DEFINITION};
use rh_sim::{Event, FactionId, Observation, Phase, PlayerId, Sim, Vec2};
use std::collections::{BTreeMap, BTreeSet};

/// Ticks in a match minute at the live 50 ms step.
const MINUTE: u32 = 60 * 20;
/// Match minutes whose build and stack state is sampled.
const SAMPLE_MINUTES: [u32; 3] = [5, 8, 12];
const HEALED: f64 = 0.95;

#[derive(Clone)]
struct MobInfo {
    camp: u32,
    elite: bool,
    kind: String,
}

/// Camp clear followed by heal-up and walk, until the seat's next hit on a mob.
struct Pending {
    cleared: u32,
    healed: Option<u32>,
}

/// An elite's death whose chest the observer is looking for. The sim drops it in the death's own tick with the
/// default lifetime, at the camp's chest spot (or the body of a campless elite): a chest row without a kill option
/// whose `expires_at - lifetime` is the death's time (within a tick) and whose position is within the camp's reach of
/// the elite's body or home is that chest. Once seen, its `expires_at` names it.
/// It is gone at that time, or `UNSEEN_WAIT_MS` after the kill when never seen.
struct ChestWatch {
    elite: PlayerId,
    killer: Option<(PlayerId, usize)>,
    death_ms: f64,
    body: Vec2,
    home: Vec2,
    expires_at: Option<f64>,
    picked: BTreeSet<usize>,
}

/// How long a chest nobody saw is waited for after the kill before it counts as none (twice its lifetime).
const UNSEEN_WAIT_MS: f64 = 30_000.;
/// A tick, plus slack, between a death's time and a chest's drop time.
const DROP_SLACK_MS: f64 = 60.;

#[derive(Clone, Copy, PartialEq)]
enum ChestClass {
    Elite(usize),
    Trophy,
    Ground,
}

#[derive(Default)]
struct SeatQuests {
    handles: BTreeMap<u32, ()>,
    /// slot -> (reward, charges, ready_at) as last seen.
    spells: BTreeMap<u8, (u8, u8, f64)>,
}

pub struct Observer {
    ids: Vec<PlayerId>,
    seat_of: BTreeMap<PlayerId, usize>,
    mobs: BTreeMap<PlayerId, MobInfo>,
    camp_damage: BTreeMap<u32, BTreeMap<PlayerId, f64>>,
    camp_first_hit: BTreeMap<(u32, PlayerId), u32>,
    pending: BTreeMap<PlayerId, Pending>,
    /// Attacker and server ms of the latest hit on each player.
    last_hit_on_player: BTreeMap<PlayerId, (Option<PlayerId>, f64)>,
    /// Per seat: the probe-fed quest funnel (None for a bot seat, whose probe is empty).
    attempts: Vec<Option<SeatTally>>,
    /// Every step's elite facts, tallied at the end by `elite_fights::tally_elites`.
    elite_steps: Vec<EliteStep>,
    /// The facts of the step being read.
    cur: EliteStep,
    /// Camps that hold an elite, and the live elite of a camp.
    elite_camps: BTreeSet<u32>,
    camp_elite: BTreeMap<u32, PlayerId>,
    dead_elites: BTreeSet<PlayerId>,
    /// Last position and home of each elite mob (a dead mob has left the list).
    elite_pos: BTreeMap<PlayerId, (Vec2, Vec2)>,
    /// Chests of elite deaths still to be recognised, picked or expired.
    chest_watch: Vec<ChestWatch>,
    camp_count: usize,
    quests: Vec<SeatQuests>,
    /// Every step's chest facts, tallied at the end by `chest_picks::tally_chests`.
    chest_steps: Vec<ChestStep>,
    /// The chest facts of the step being read.
    cur_chests: ChestStep,
    farm_types: Vec<MobTypeId>,
    /// Where each seat's time goes: the tally, the facts of the step being read, and the few last values it needs.
    time: TimeTally,
    cur_time: Vec<SeatStep>,
    prev_pos: Vec<Option<(f64, f64)>>,
    last_weapon: Vec<Option<rh_sim::WeaponId>>,
    factions: Vec<String>,
    /// `(quest, offer)` already counted as completed, per seat (the rule of `SeatTally`: a paid row, once).
    paid: Vec<BTreeSet<(String, u32)>>,
    /// Chest options already counted as picked, named as `tally_chests` names them: (seat, expires_at, x, y, option).
    picked_seen: BTreeSet<(usize, u64, u64, u64, usize)>,
    pub acc: Acc,
}

impl Observer {
    /// `sim` is the freshly built match; seats are its players in order.
    pub fn new(sim: &Sim) -> Self {
        let ids: Vec<PlayerId> = sim.players().iter().map(|p| p.core.id).collect();
        let seat_of = ids.iter().enumerate().map(|(i, id)| (*id, i)).collect();
        let farm_types = FactionId::ALL
            .into_iter()
            .flat_map(|z| normal_types_of(z).map(|t| t.id))
            .collect();
        Self {
            seat_of,
            attempts: sim
                .players()
                .iter()
                .map(|p| (!p.seat.bot).then(SeatTally::default))
                .collect(),
            quests: ids.iter().map(|_| SeatQuests::default()).collect(),
            chest_steps: Vec::new(),
            cur_chests: ChestStep::default(),
            ids,
            mobs: BTreeMap::new(),
            camp_damage: BTreeMap::new(),
            camp_first_hit: BTreeMap::new(),
            pending: BTreeMap::new(),
            last_hit_on_player: BTreeMap::new(),
            elite_steps: Vec::new(),
            cur: EliteStep::default(),
            elite_camps: BTreeSet::new(),
            camp_elite: BTreeMap::new(),
            dead_elites: BTreeSet::new(),
            elite_pos: BTreeMap::new(),
            chest_watch: Vec::new(),
            camp_count: sim.camp_rosters().len(),
            farm_types,
            time: TimeTally::new(sim.players().len()),
            cur_time: vec![SeatStep::default(); sim.players().len()],
            prev_pos: vec![None; sim.players().len()],
            last_weapon: vec![None; sim.players().len()],
            factions: sim
                .players()
                .iter()
                .map(|p| {
                    p.core
                        .vitals
                        .faction
                        .faction_id()
                        .map_or_else(|| "none".to_string(), |f| format!("{f:?}"))
                })
                .collect(),
            paid: vec![BTreeSet::new(); sim.players().len()],
            picked_seen: BTreeSet::new(),
            acc: Acc::default(),
        }
    }

    /// Reads one finished step. `sample` is false on the step that ended the match (observations are empty after it).
    pub fn step(&mut self, sim: &Sim, events: &[Event], sample: bool) {
        let tick = sim.tick();
        self.cur = EliteStep {
            now_ms: sim.server_time(),
            ..EliteStep::default()
        };
        self.cur_chests = ChestStep::default();
        self.cur_time = vec![SeatStep::default(); self.ids.len()];
        for m in sim.mobs() {
            self.mobs.entry(m.core.id).or_insert_with(|| MobInfo {
                camp: m.camp,
                elite: m.elite,
                kind: m
                    .mob_type
                    .map_or_else(|| "none".to_string(), |t| format!("{t:?}")),
            });
            if m.elite && !self.dead_elites.contains(&m.core.id) {
                self.elite_pos.insert(m.core.id, (m.core.pos(), m.home));
                if self.camp_of(m.camp) >= 0 {
                    self.elite_camps.insert(m.camp);
                    self.camp_elite.insert(m.camp, m.core.id);
                }
            }
        }
        self.minute_clock(sim, tick);
        let mut died = BTreeSet::new();
        for e in events {
            self.event(sim, e, tick, &mut died);
        }
        self.heal_check(sim, tick);
        self.probe_quests(sim, tick, &died);
        if sample {
            let now = sim.server_time();
            for seat in 0..self.ids.len() {
                let obs = rh_sim::observe(sim, self.ids[seat], events);
                self.observation(seat, &obs, now);
            }
        }
        self.chests_gone();
        self.chest_steps.push(std::mem::take(&mut self.cur_chests));
        self.elite_steps.push(std::mem::take(&mut self.cur));
        self.minute_sample(sim, tick);
        self.time_step(sim, tick);
    }

    /// Closes the step's per-seat time facts (position, hp, weapon) and feeds the tally.
    fn time_step(&mut self, sim: &Sim, tick: u32) {
        let mut seats = std::mem::take(&mut self.cur_time);
        for (seat, s) in seats.iter_mut().enumerate() {
            let Some(p) = sim.player(self.ids[seat]) else {
                continue;
            };
            s.alive = p.standing();
            s.hp_full = p.vitals.hp >= p.vitals.max_hp;
            let pos = p.core.pos();
            if s.alive {
                s.moved = self.prev_pos[seat].map_or(0., |(x, y)| (pos.x - x).hypot(pos.y - y));
                self.prev_pos[seat] = Some((pos.x, pos.y));
            } else {
                // A dead step has no movement, and the respawn jump is not a move.
                self.prev_pos[seat] = None;
            }
            let w = p.active_weapon();
            if self.last_weapon[seat] != Some(w) {
                self.last_weapon[seat] = Some(w);
                s.weapon = Some(w.as_str().to_string());
            }
        }
        self.time.push(&TimeStep { tick, seats });
    }

    /// A hit between a seat and a mob: an elite fight when the mob is an elite or of an elite camp (a camp whose elite
    /// the observer has seen alive), else a camp fight (a mob with no camp too).
    fn hit_kind_mob(&mut self, seat: usize, camp: u32, elite: bool) {
        let is_elite = elite || (self.camp_of(camp) >= 0 && self.elite_camps.contains(&camp));
        let s = &mut self.cur_time[seat];
        if is_elite {
            s.hit_elite = true;
        } else {
            s.hit_camp = true;
        }
    }

    /// The camp index as the tally wants it: negative for a mob outside the map's camps (training, companions).
    fn camp_of(&self, camp: u32) -> i32 {
        if (camp as usize) < self.camp_count {
            camp as i32
        } else {
            -1
        }
    }

    /// The elite a hit is about, from either end: the mob itself, or the live elite of the mob's camp.
    fn elite_of(&self, mob: PlayerId) -> Option<(PlayerId, bool)> {
        let info = self.mobs.get(&mob)?;
        if info.elite {
            Some((mob, true))
        } else if self.camp_of(info.camp) >= 0 {
            self.camp_elite.get(&info.camp).map(|e| (*e, false))
        } else {
            None
        }
    }

    fn contact(
        &self,
        seat: usize,
        elite: PlayerId,
        on_elite: bool,
        dealt: bool,
        weapon: String,
    ) -> Contact {
        let info = &self.mobs[&elite];
        Contact {
            seat,
            elite: elite.0,
            camp: self.camp_of(info.camp),
            kind: info.kind.clone(),
            weapon,
            dealt,
            on_elite,
        }
    }

    /// Chests whose time has come: expired (the chest's own `expires_at`), or never seen and past the unseen wait.
    fn chests_gone(&mut self) {
        let now = self.cur.now_ms;
        let cur = &mut self.cur;
        let by_exp = &mut self.chest_watch;
        by_exp.retain(|w| {
            let gone = w
                .expires_at
                .map_or(now >= w.death_ms + UNSEEN_WAIT_MS, |e| now >= e);
            if gone {
                cur.chests_gone.push(w.elite.0);
            }
            !gone
        });
    }

    /// Reads `Sim::quest_attempts` for every human seat (every step, including the last: the probe needs no observation).
    fn probe_quests(&mut self, sim: &Sim, tick: u32, died: &BTreeSet<PlayerId>) {
        let (now_ms, locked) = (sim.server_time(), sim.gates_locked());
        for (seat, tally) in self.attempts.iter_mut().enumerate() {
            let Some(tally) = tally else { continue };
            let id = self.ids[seat];
            let rows: Vec<ProbeRow> = sim
                .quest_attempts(id)
                .into_iter()
                .map(|r| ProbeRow {
                    offer: r.offer,
                    quest: r.quest.definition().id.to_string(),
                    accepted: r.accepted,
                    ready: r.ready,
                    turned_in: r.turned_in,
                    completed: r.completed,
                    deadline: r.deadline,
                    progress: r.progress,
                })
                .collect();
            let t = &mut self.cur_time[seat];
            for r in &rows {
                t.quest_open |= r.accepted && !r.turned_in && !r.completed;
                if r.completed && self.paid[seat].insert((r.quest.clone(), r.offer)) {
                    t.quests_completed += 1;
                }
            }
            tally.push(&StepRead {
                tick,
                now_ms,
                locked,
                died: died.contains(&id),
                rows,
            });
        }
    }

    /// Closes the books: fights still undecided are `open`; quest attempts are folded in seat order.
    pub fn finish(mut self) -> Acc {
        let rows = std::mem::replace(&mut self.time, TimeTally::new(0)).finish();
        for r in rows {
            self.acc.world.time.add_row(&r);
            self.acc.seats.push(SeatReport {
                seed: 0,
                faction: self.factions[r.seat].clone(),
                row: r,
            });
        }
        for tally in std::mem::take(&mut self.attempts).into_iter().flatten() {
            for (quest, t) in tally.finish() {
                let c = self.acc.quests.by_quest.entry(quest).or_default();
                c.accepted += t.accepted;
                c.attempts += t.attempts;
                c.completed += t.completed;
                c.ended_timer += t.ended_timer;
                c.ended_death += t.ended_death;
                c.abandoned += t.unknown;
                c.open += t.open;
                c.turned_in_unpaid += t.turned_in_unpaid;
                for s in t.complete_secs {
                    c.complete_secs.add(s);
                }
                for tick in t.first_accept_ticks {
                    *self
                        .acc
                        .quests
                        .accepted_by_minute
                        .entry(tick / MINUTE)
                        .or_default() += 1;
                }
            }
        }
        let tally = tally_chests(&self.chest_steps);
        let rw = &mut self.acc.rewards;
        rw.chests_offered += tally.chests_offered;
        rw.picks_by_kind.fold_in(tally.picks_by_kind);
        rw.picks_by_chest_kind.fold_in(tally.picks_by_chest_kind);
        rw.picks_unnamed += tally.picks_unnamed;
        for (name, c) in tally.cards {
            // `victory` already wrote `held_by_winner` into the same entries: add, never overwrite.
            let a = self.acc.augments.by_card.entry(name).or_default();
            a.offered += c.offered;
            a.picked += c.picked;
            a.picked_by_killer += c.picked_by_killer;
            a.picked_by_bystander += c.picked_by_bystander;
        }
        let last = self.elite_steps.last().map_or(0., |s| s.now_ms);
        self.elite_steps.push(EliteStep {
            now_ms: last,
            ..EliteStep::default()
        });
        for (key, t) in tally_elites(&self.elite_steps) {
            let c = self.acc.elites.by_kind_weapon.entry(key).or_default();
            c.attempts = t.attempts;
            c.killed = t.killed;
            c.taken_by_other = t.taken_by_other;
            c.died = t.died;
            c.died_elsewhere = t.died_elsewhere;
            c.left = t.left;
            c.open = t.open;
            for s in t.kill_secs {
                c.kill_secs.add(s);
            }
            c.chest_killer = t.chest_killer;
            c.chest_other = t.chest_other;
            c.chest_none = t.chest_none;
            c.chest_open = t.chest_open;
            c.chest_bystander_picks = t.chest_bystander_picks;
        }
        self.acc
    }

    fn minute_clock(&mut self, sim: &Sim, tick: u32) {
        if sim.phase() != Phase::Active {
            return;
        }
        let standing = sim.players().iter().filter(|p| p.standing()).count() as u64;
        self.acc
            .world
            .player_deaths
            .by_minute
            .entry(tick / MINUTE)
            .or_default()
            .player_ticks += standing;
    }

    fn event(&mut self, sim: &Sim, e: &Event, tick: u32, died: &mut BTreeSet<PlayerId>) {
        match e {
            Event::Hit {
                victim,
                by,
                amount,
                weapon,
                ..
            } => self.hit(sim, *victim, *by, *amount, *weapon, tick),
            Event::CampCleared { camp } => self.camp_cleared(sim, *camp, tick),
            Event::Death { id, by, .. } => self.death(sim, *id, *by, tick, died),
            Event::ChestTaken { .. } => {
                self.acc.rewards.chest_taken_events += 1;
                self.cur_chests.taken_events += 1;
            }
            Event::Victory { id, draw } => self.victory(sim, *id, *draw),
            _ => {}
        }
    }

    fn hit(
        &mut self,
        sim: &Sim,
        victim: PlayerId,
        by: Option<PlayerId>,
        amount: f64,
        weapon: Option<rh_sim::WeaponId>,
        tick: u32,
    ) {
        if sim.player(victim).is_some() {
            let d = &mut self.acc.world.player_deaths;
            d.hits_on_players += 1;
            if by.is_some_and(|b| self.mobs.contains_key(&b)) {
                d.hits_on_players_by_mob += 1;
            }
            self.last_hit_on_player
                .insert(victim, (by, sim.server_time()));
            if let (Some(&seat), Some(b)) = (self.seat_of.get(&victim), by) {
                if let Some(info) = self.mobs.get(&b) {
                    let (camp, elite) = (info.camp, info.elite);
                    self.hit_kind_mob(seat, camp, elite);
                } else if let Some(&other) = self.seat_of.get(&b) {
                    self.cur_time[seat].hit_player = true;
                    self.cur_time[other].hit_player = true;
                }
            }
            if let (Some(&seat), Some((elite, on_elite))) =
                (self.seat_of.get(&victim), by.and_then(|b| self.elite_of(b)))
            {
                let c = self.contact(seat, elite, on_elite, false, String::new());
                self.cur.contacts.push(c);
            }
            if let Some(p) = self.pending.get_mut(&victim) {
                p.healed.get_or_insert(tick);
            }
            return;
        }
        let (Some(info), Some(seat_id)) = (
            self.mobs.get(&victim).cloned(),
            by.filter(|b| self.seat_of.contains_key(b)),
        ) else {
            return;
        };
        self.hit_kind_mob(self.seat_of[&seat_id], info.camp, info.elite);
        *self
            .camp_damage
            .entry(info.camp)
            .or_default()
            .entry(seat_id)
            .or_insert(0.) += amount;
        self.camp_first_hit
            .entry((info.camp, seat_id))
            .or_insert(tick);
        if let Some(p) = self.pending.remove(&seat_id) {
            let healed = p.healed.unwrap_or(tick);
            self.acc
                .world
                .heal_secs
                .add(f64::from(healed - p.cleared) / 20.);
            self.acc.world.walk_secs.add(f64::from(tick - healed) / 20.);
        }
        if let Some((elite, on_elite)) = self.elite_of(victim) {
            let seat = self.seat_of[&seat_id];
            // The weapon is read only where it can open an attempt: a hit dealt to the elite itself.
            let weapon = if on_elite {
                weapon
                    .unwrap_or_else(|| {
                        sim.player(seat_id)
                            .map_or(rh_sim::WeaponId::Fists, |p| p.active_weapon())
                    })
                    .as_str()
                    .to_string()
            } else {
                String::new()
            };
            let c = self.contact(seat, elite, on_elite, true, weapon);
            self.cur.contacts.push(c);
        }
    }

    fn camp_cleared(&mut self, sim: &Sim, camp: u32, tick: u32) {
        let w = &mut self.acc.world;
        w.camps_cleared += 1;
        if self.elite_camps.contains(&camp) {
            w.elite_camps_cleared += 1;
        }
        if let Some(r) = sim.camp_rosters().get(camp as usize) {
            *w.camps_by_type
                .entry(format!("{:?}", r.mob_type))
                .or_default() += 1;
        }
        let dmg = self.camp_damage.remove(&camp).unwrap_or_default();
        let mut best: Option<(PlayerId, f64)> = None;
        for (id, d) in dmg {
            if best.is_none_or(|(_, b)| d > b) {
                best = Some((id, d));
            }
        }
        let firsts: Vec<_> = self
            .camp_first_hit
            .keys()
            .filter(|(c, _)| *c == camp)
            .copied()
            .collect();
        let Some((seat_id, _)) = best else {
            for k in firsts {
                self.camp_first_hit.remove(&k);
            }
            return;
        };
        w.camps_credited += 1;
        let elite = self.elite_camps.contains(&camp);
        let seat = self.seat_of[&seat_id];
        if elite {
            self.cur_time[seat].camps_elite += 1;
        } else {
            w.camps_credited_normal += 1;
            self.cur_time[seat].camps_normal += 1;
        }
        if let Some(first) = self.camp_first_hit.get(&(camp, seat_id)) {
            let secs = f64::from(tick - first) / 20.;
            w.kill_secs.add(secs);
            if elite {
                w.kill_secs_elite.add(secs);
            } else {
                w.kill_secs_normal.add(secs);
            }
        }
        for k in firsts {
            self.camp_first_hit.remove(&k);
        }
        self.pending.insert(
            seat_id,
            Pending {
                cleared: tick,
                healed: None,
            },
        );
    }

    fn heal_check(&mut self, sim: &Sim, tick: u32) {
        for (id, p) in &mut self.pending {
            if p.healed.is_none() {
                if let Some(pl) = sim.player(*id) {
                    if pl.vitals.hp >= HEALED * pl.vitals.max_hp {
                        p.healed = Some(tick);
                    }
                }
            }
        }
    }

    fn death(
        &mut self,
        sim: &Sim,
        id: PlayerId,
        by: Option<PlayerId>,
        tick: u32,
        died: &mut BTreeSet<PlayerId>,
    ) {
        if sim.player(id).is_none() {
            self.mob_death(sim, id, by);
            return;
        }
        died.insert(id);
        self.pending.remove(&id);
        let now = sim.server_time();
        let attacker = by.or_else(|| {
            self.last_hit_on_player
                .get(&id)
                .filter(|(_, at)| credit_window_open(*at, now))
                .and_then(|(a, _)| *a)
        });
        let d = &mut self.acc.world.player_deaths;
        let minute = d.by_minute.entry(tick / MINUTE).or_default();
        let causes = [&mut d.by_cause, &mut minute.by_cause];
        let mob = attacker.and_then(|a| self.mobs.get(&a));
        for c in causes {
            match (attacker, mob) {
                (_, Some(m)) if m.elite => c.elite += 1,
                (_, Some(_)) => c.mob += 1,
                (Some(a), None) if sim.player(a).is_some() => c.player += 1,
                _ => c.hazard += 1,
            }
        }
        d.total += 1;
        minute.total += 1;
        if let Some(&seat) = self.seat_of.get(&id) {
            self.cur_time[seat].deaths += 1;
            let mob_info = attacker.and_then(|a| self.mobs.get(&a));
            self.cur.seat_deaths.push(SeatDeath {
                seat,
                attacker_mob: attacker.filter(|a| self.mobs.contains_key(a)).map(|a| a.0),
                attacker_camp: mob_info.map(|m| self.camp_of(m.camp)),
            });
        }
    }

    /// An elite's death: the facts for the tally, and a watch for the chest it drops.
    fn mob_death(&mut self, sim: &Sim, mob: PlayerId, by: Option<PlayerId>) {
        let Some(info) = self.mobs.get(&mob) else {
            return;
        };
        if !info.elite {
            return;
        }
        let killer = by.and_then(|b| self.seat_of.get(&b).map(|s| (b, *s)));
        if let Some((_, seat)) = killer {
            self.cur_time[seat].elite_kills += 1;
        }
        self.cur.elite_deaths.push(EliteDeath {
            elite: mob.0,
            killer: killer.map(|(_, s)| s),
        });
        self.dead_elites.insert(mob);
        if self.camp_elite.get(&info.camp) == Some(&mob) {
            self.camp_elite.remove(&info.camp);
        }
        let (body, home) = self
            .elite_pos
            .remove(&mob)
            // No known position (the elite was never listed): the infinite position matches no chest by place, so this
            // elite's chest ends as `none` after the unseen wait.
            .unwrap_or((Vec2::new(f64::INFINITY, 0.), Vec2::new(f64::INFINITY, 0.)));
        self.chest_watch.push(ChestWatch {
            elite: mob,
            killer,
            death_ms: sim.server_time(),
            body,
            home,
            expires_at: None,
            picked: BTreeSet::new(),
        });
    }

    fn victory(&mut self, sim: &Sim, id: Option<PlayerId>, draw: bool) {
        if draw {
            self.acc.augments.draws += 1;
        }
        let Some(p) = id.and_then(|i| sim.player(i)) else {
            return;
        };
        self.acc.augments.winners += 1;
        for card in p.build.cards() {
            self.acc
                .augments
                .by_card
                .entry(card.as_str().to_string())
                .or_default()
                .held_by_winner += 1;
        }
    }

    fn observation(&mut self, seat: usize, obs: &Observation, now: f64) {
        if let Some(view) = &obs.quests {
            self.quest_rows(seat, view);
            self.quest_spells(seat, view, now);
        }
        self.chest_rows(seat, &obs.reward_chests);
    }

    /// `dealt`: a row handle's first sighting in the seat's observation. Everything else about a quest comes from the probe.
    fn quest_rows(&mut self, seat: usize, view: &QuestView) {
        let acc = &mut self.acc.quests;
        let sq = &mut self.quests[seat];
        for row in &view.rows {
            sq.handles.entry(row.handle.get()).or_insert_with(|| {
                acc.by_quest.entry(row.def.clone()).or_default().dealt += 1;
            });
        }
    }

    fn quest_spells(&mut self, seat: usize, view: &QuestView, now: f64) {
        let sq = &mut self.quests[seat];
        for sp in &view.spells {
            let seen = (sp.reward, sp.charges, sp.ready_at);
            if let Some(&(reward, charges, ready_at)) = sq.spells.get(&sp.slot) {
                let cast = reward == sp.reward
                    && (sp.charges < charges
                        || (sp.charges == charges && ready_at <= now && sp.ready_at > ready_at));
                if cast {
                    *self.acc.quests.spell_casts.entry(sp.reward).or_default() += 1;
                }
            }
            sq.spells.insert(sp.slot, seen);
        }
    }

    /// Which kind of chest a row is. A row with a kill option is a Trophy. An elite's chest is found as described on
    /// [`ChestWatch`]; what would fool it is another non-trophy chest dropped in the same tick (within a tick of the
    /// death) within `CAMP_HEAL_RANGE` of the elite's body or home. Anything else is ground loot.
    fn classify(&mut self, c: &RewardChestView) -> ChestClass {
        if c.options.iter().any(|o| matches!(o.id, OptionId::Kill(_))) {
            return ChestClass::Trophy;
        }
        if self.chest_watch.is_empty() {
            return ChestClass::Ground;
        }
        let near =
            |a: Vec2, b: Vec2| (a.x - b.x).hypot(a.y - b.y) <= rh_sim::constants::CAMP_HEAL_RANGE;
        let drop_ms = c.expires_at - DEFINITION.lifetime_ms;
        let found = self.chest_watch.iter().position(|w| match w.expires_at {
            Some(e) => e == c.expires_at,
            None => {
                (drop_ms - w.death_ms).abs() <= DROP_SLACK_MS
                    && (near(c.at, w.body) || near(c.at, w.home))
            }
        });
        match found {
            Some(i) => {
                self.chest_watch[i].expires_at = Some(c.expires_at);
                ChestClass::Elite(i)
            }
            None => ChestClass::Ground,
        }
    }

    /// One seat's chest rows of this step, as facts for `chest_picks::tally_chests` (no handle: it is reissued).
    fn chest_rows(&mut self, seat: usize, chests: &[RewardChestView]) {
        let me = self.ids[seat];
        for c in chests {
            let at = (c.expires_at.to_bits(), c.at.x.to_bits(), c.at.y.to_bits());
            for (i, o) in c.options.iter().enumerate() {
                if o.taken_by == Some(me) && self.picked_seen.insert((seat, at.0, at.1, at.2, i)) {
                    self.cur_time[seat].picks.push(o.id.as_str().into_owned());
                }
            }
            let class = self.classify(c);
            let (kind, seat_is_killer) = match class {
                ChestClass::Elite(w) => {
                    let w = &mut self.chest_watch[w];
                    if c.options.iter().any(|o| o.taken_by == Some(me)) && w.picked.insert(seat) {
                        self.cur.chest_picks.push((w.elite.0, seat));
                    }
                    (ChestKind::Elite, w.killer.is_some_and(|(id, _)| id == me))
                }
                ChestClass::Trophy => (ChestKind::Trophy, false),
                ChestClass::Ground => (ChestKind::Ground, false),
            };
            let options = c
                .options
                .iter()
                .map(|o| OptionRow {
                    name: o.id.as_str().into_owned(),
                    kind: match o.id {
                        OptionId::Card(_) => "card",
                        OptionId::Boost(_) => "boost",
                        OptionId::Kill(_) => "kill",
                    }
                    .to_string(),
                    taken_by_seat: o.taken_by == Some(me),
                })
                .collect();
            self.cur_chests.rows.push(ChestRow {
                seat,
                x: c.at.x,
                y: c.at.y,
                expires_at: c.expires_at,
                can_choose: c.can_choose,
                kind,
                seat_is_killer,
                options,
            });
        }
    }

    fn minute_sample(&mut self, sim: &Sim, tick: u32) {
        let Some(minute) = SAMPLE_MINUTES.into_iter().find(|m| tick == m * MINUTE) else {
            return;
        };
        let mut acc = MinuteAcc::default();
        for p in sim.players().iter().filter(|p| p.standing()) {
            acc.samples += 1;
            let mut stacks = 0;
            for t in &self.farm_types {
                let n = p.farm.count(*t);
                if n > 0 {
                    *acc.farm_stacks.entry(format!("{t:?}")).or_default() += n;
                    stacks += n;
                }
            }
            if let Some(&seat) = self.seat_of.get(&p.core.id) {
                self.cur_time[seat].farm_stacks = Some((minute, stacks));
            }
            for (b, n) in BoostId::ALL.into_iter().zip(p.build.boosts()) {
                if n > 0 {
                    *acc.boosts.entry(b.as_str().to_string()).or_default() += u64::from(n);
                }
            }
            for r in readouts(sim, p.core.id).rows() {
                let n: &mut NativeAcc = acc.native.entry(r.card.as_str().to_string()).or_default();
                n.holders += 1;
                n.value_sum += r.value;
                n.max_sum += r.max;
            }
        }
        self.acc.world.minutes.insert(minute, acc);
    }
}
