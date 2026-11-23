//! The read-only observer of one `balance_lab` match: after each step it reads `&Sim`, the step's events and the seats'
//! ordinary observations, and folds them into an [`Acc`]. It never submits, draws or mutates (purity is tested by
//! comparing state hashes with an unwatched run). Sources per metric: docs/backlog/packets/P-BAL-LAB-1.md.
use super::acc::{Acc, AugCounts, EliteCounts, MinuteAcc, NativeAcc};
use super::attempts::{ProbeRow, SeatTally, StepRead};
use super::credit_window_open;
use rh_sim::cards::native::readouts;
use rh_sim::mob_catalog::{normal_types_of, MobTypeId};
use rh_sim::quest_view::QuestView;
use rh_sim::reward_chests::{BoostId, OptionId, RewardChestView};
use rh_sim::{Event, FactionId, Observation, Phase, PlayerId, Sim};
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

#[derive(Default)]
struct SeatQuests {
    handles: BTreeMap<u32, ()>,
    /// slot -> (reward, charges, ready_at) as last seen.
    spells: BTreeMap<u8, (u8, u8, f64)>,
}

#[derive(Default)]
struct SeatChests {
    /// chest handle -> option names as last seen.
    options: BTreeMap<u32, Vec<(String, &'static str)>>,
    offered: BTreeSet<(u32, usize)>,
    chest_offered: BTreeSet<u32>,
    picked: BTreeSet<(u32, usize)>,
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
    /// (seat id, elite mob id) -> (kind, weapon) while the fight is undecided.
    elite_open: BTreeMap<(PlayerId, PlayerId), (String, String)>,
    quests: Vec<SeatQuests>,
    chests: Vec<SeatChests>,
    farm_types: Vec<MobTypeId>,
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
            chests: ids.iter().map(|_| SeatChests::default()).collect(),
            ids,
            mobs: BTreeMap::new(),
            camp_damage: BTreeMap::new(),
            camp_first_hit: BTreeMap::new(),
            pending: BTreeMap::new(),
            last_hit_on_player: BTreeMap::new(),
            elite_open: BTreeMap::new(),
            farm_types,
            acc: Acc::default(),
        }
    }

    /// Reads one finished step. `sample` is false on the step that ended the match (observations are empty after it).
    pub fn step(&mut self, sim: &Sim, events: &[Event], sample: bool) {
        let tick = sim.tick();
        for m in sim.mobs() {
            self.mobs.entry(m.core.id).or_insert_with(|| MobInfo {
                camp: m.camp,
                elite: m.elite,
                kind: m
                    .mob_type
                    .map_or_else(|| "none".to_string(), |t| format!("{t:?}")),
            });
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
        self.minute_sample(sim, tick);
    }

    /// Reads `Sim::quest_attempts` for every human seat (every step, including the last: the probe needs no observation).
    fn probe_quests(&mut self, sim: &Sim, tick: u32, died: &BTreeSet<PlayerId>) {
        let (now_ms, locked) = (sim.server_time(), sim.gates_locked());
        for (seat, tally) in self.attempts.iter_mut().enumerate() {
            let Some(tally) = tally else { continue };
            let id = self.ids[seat];
            let rows = sim
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
        for (kind, weapon) in std::mem::take(&mut self.elite_open).into_values() {
            self.elite(&kind, &weapon).open += 1;
        }
        self.acc
    }

    fn elite(&mut self, kind: &str, weapon: &str) -> &mut EliteCounts {
        self.acc
            .elites
            .by_kind_weapon
            .entry((kind.to_string(), weapon.to_string()))
            .or_default()
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
            Event::ChestTaken { .. } => self.acc.rewards.chest_taken_events += 1,
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
        if info.elite && !self.elite_open.contains_key(&(seat_id, victim)) {
            let weapon = weapon.unwrap_or_else(|| {
                sim.player(seat_id)
                    .map_or(rh_sim::WeaponId::Fists, |p| p.active_weapon())
            });
            self.elite_open.insert(
                (seat_id, victim),
                (info.kind.clone(), weapon.as_str().into()),
            );
            self.elite(&info.kind, weapon.as_str()).attempts += 1;
        }
    }

    fn camp_cleared(&mut self, sim: &Sim, camp: u32, tick: u32) {
        let w = &mut self.acc.world;
        w.camps_cleared += 1;
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
        if let Some(first) = self.camp_first_hit.get(&(camp, seat_id)) {
            w.kill_secs.add(f64::from(tick - first) / 20.);
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
            self.mob_death(id, by);
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
        let lost: Vec<_> = self
            .elite_open
            .keys()
            .filter(|(s, _)| *s == id)
            .copied()
            .collect();
        for k in lost {
            if let Some((kind, weapon)) = self.elite_open.remove(&k) {
                self.elite(&kind, &weapon).died += 1;
            }
        }
    }

    fn mob_death(&mut self, mob: PlayerId, by: Option<PlayerId>) {
        let fights: Vec<_> = self
            .elite_open
            .keys()
            .filter(|(_, m)| *m == mob)
            .copied()
            .collect();
        for k in fights {
            if let Some((kind, weapon)) = self.elite_open.remove(&k) {
                let counts = self.elite(&kind, &weapon);
                if by == Some(k.0) {
                    counts.killed += 1;
                } else {
                    counts.taken_by_other += 1;
                }
            }
        }
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

    fn chest_rows(&mut self, seat: usize, chests: &[RewardChestView]) {
        let me = self.ids[seat];
        let sc = &mut self.chests[seat];
        for c in chests {
            let h = c.handle.wire();
            let names: Vec<(String, &'static str)> = c
                .options
                .iter()
                .map(|o| {
                    let kind = match o.id {
                        OptionId::Card(_) => "card",
                        OptionId::Boost(_) => "boost",
                        OptionId::Kill(_) => "kill",
                    };
                    (o.id.as_str().into_owned(), kind)
                })
                .collect();
            if c.can_choose {
                if sc.chest_offered.insert(h) {
                    self.acc.rewards.chests_offered += 1;
                }
                for (i, (name, _)) in names.iter().enumerate() {
                    if sc.offered.insert((h, i)) {
                        self.acc
                            .augments
                            .by_card
                            .entry(name.clone())
                            .or_default()
                            .offered += 1;
                    }
                }
            }
            for (i, o) in c.options.iter().enumerate() {
                if o.taken_by == Some(me) && sc.picked.insert((h, i)) {
                    let (name, kind) = &names[i];
                    let by_card = &mut self.acc.augments.by_card;
                    let counts: &mut AugCounts = by_card.entry(name.clone()).or_default();
                    if sc.offered.insert((h, i)) {
                        counts.offered += 1;
                    }
                    counts.picked += 1;
                    *self
                        .acc
                        .rewards
                        .picks_by_kind
                        .entry((*kind).to_string())
                        .or_default() += 1;
                }
            }
            sc.options.insert(h, names);
        }
    }

    fn minute_sample(&mut self, sim: &Sim, tick: u32) {
        let Some(minute) = SAMPLE_MINUTES.into_iter().find(|m| tick == m * MINUTE) else {
            return;
        };
        let mut acc = MinuteAcc::default();
        for p in sim.players().iter().filter(|p| p.standing()) {
            acc.samples += 1;
            for t in &self.farm_types {
                let n = p.farm.count(*t);
                if n > 0 {
                    *acc.farm_stacks.entry(format!("{t:?}")).or_default() += n;
                }
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
