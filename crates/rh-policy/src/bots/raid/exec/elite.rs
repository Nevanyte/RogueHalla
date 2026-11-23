//! Elite executor (P-RAID-E): elite pricing, elite fight and the reward chest pick. Every number it prices or waits
//! with is a field of `Prices::elite` (`values.rs`); this file holds mechanics only. With `elite.on` false it observes,
//! proposes and rides along nothing. Nothing here draws from `cx.rng`.
//!
//! Three parts:
//! - **The candidate** (`propose`): each elite camp whose elite this seat can show alive (the camp untouched since its
//!   respawn, or the elite seen alive; `Watch`), priced by the elite bench's death table (`death_prior`) for the kind,
//!   the stacks banked, the weapon in hand and the hp held with potions counted, scaled by the share of the elite's hp
//!   still standing. The death term rises as hp falls, so the arbiter hands the seat away from a fight being lost: there
//!   is no leave rule of this file's own.
//! - **The fight and the chest** (`act`): the elite first (an add only while the elite is not in sight), committed while
//!   the camp is on the bot; once the elite is gone, the camp's chest, stood at until the card is picked, the chest is
//!   gone or `chest_wait_seconds` pass. Every stand is bounded by an `ElitePrices` field and ends in a ban or `Done`.
//! - **The priced pick** (`ride_along`, every goal): the card the price table ranks highest (`best_card`), sent after the
//!   chest module's own pick so it replaces it on the menu channel; with Loot's ask discipline (`ASK_WINDOW_MS`,
//!   `MAX_ASKS`).
use crate::bots::loot::{ASK_WINDOW_MS, MAX_ASKS};
use crate::bots::mob_hunt::best_slot_vs_mob;
use crate::bots::perception::ChestRef;
use crate::bots::planning::{matchup_share_of_kind, zone_faction};
use crate::bots::raid::exec::farm::{death_chance, dist, lock_range, DEFEND_U, FIGHT_U};
use crate::bots::raid::exec::travel::{self, eta_minutes, move_intent, step_toward, straight_minutes};
use crate::bots::raid::goal::{Candidate, Cx, Executor, GoalKind, RaidGoal, Status, Step, Target};
use crate::bots::raid::percept::{CampSeen, MobClass, MobSeen, CAMP_JOIN_RADIUS};
use crate::bots::raid::values::{ElitePrices, Prices};
use crate::bots::temporal_percept::live_chests::CHEST_HUNT;
use crate::bots::types::{
    ChooseMsg, Intent, IntentBody, IntentTiming, OwnerLock, Stimulus, StimulusKind, PRIORITY_COMBAT,
};
use rh_sim::cards::CardId;
use rh_sim::mob_catalog::MobTypeId;
use rh_sim::{Vec2, WeaponId};

/// What the seat knows of one elite camp's elite.
#[derive(Clone, Copy, Debug, PartialEq)]
struct Watch {
    camp: u32,
    alive: bool,
    /// Where the elite stood when last seen alive.
    at: Option<Vec2>,
}

/// An elite fight's exchange so far: hp the seat lost (never what a potion gave back, shares of its max hp) against the
/// camp's hp destroyed (elites' worth, `Elite::fight_left`), with the last frame's readings.
#[derive(Clone, Copy, Debug, PartialEq)]
struct Tally {
    camp: u32,
    lost: f64,
    dealt: f64,
    hp: f64,
    elite: Option<f64>,
}

/// One priced ask at a chest (Loot's discipline: another card only, at most `MAX_ASKS`, inside `ASK_WINDOW_MS`).
#[derive(Clone, Debug, PartialEq)]
struct Ask {
    chest: String,
    first: f64,
    option: usize,
    n: u32,
}

/// The Elite goal's executor: its memory between thinks (which camps' elites it saw alive or dead, the camps it gave
/// up on, the fight's start, the kills whose chest it still wants, the cards it took, its asks).
#[derive(Default)]
pub struct Elite {
    watch: Vec<Watch>,
    banned: Vec<(u32, f64)>,
    /// Server ms the held goal's fight began (a member or the elite on the bot), `None` before.
    fight_since: Option<f64>,
    empty_since: Option<f64>,
    /// Camps whose elite died while this seat fought it, with the server ms of the kill: the chest phase.
    killed: Vec<(u32, f64)>,
    /// Server ms each readable chest first showed its cards (the pick's React stimulus), by chest key.
    readable: Vec<(String, f64)>,
    asks: Vec<Ask>,
    /// Cards this seat took from a chest (a native card is not in the frame's build row).
    took: Vec<String>,
    /// Where the add the fight takes stood at the last think (npc keys live one frame).
    target: Option<Vec2>,
    /// The elite camp whose goal the seat held at the last frame.
    held: Option<u32>,
    /// What each elite camp's fights have cost and bought so far (until its elite dies or respawns).
    tallies: Vec<Tally>,
    trace_goal: String, // SCRATCH-TRACE
}

/// The living elite of `kind` nearest `near` (ties by key), among the frame's mobs.
fn elite_mob<'a>(cx: &'a Cx<'_>, kind: MobTypeId, near: Vec2) -> Option<&'a MobSeen> {
    cx.p.mobs
        .iter()
        .filter(|m| m.alive && m.class == MobClass::Elite && m.mob_type == Some(kind))
        .min_by(|a, b| {
            dist(a.at, near)
                .total_cmp(&dist(b.at, near))
                .then_with(|| a.key.cmp(&b.key))
        })
}

/// The goal this think holds, when it is an Elite goal: its camp.
fn goal_camp(goal: &RaidGoal) -> Option<u32> {
    match (goal.kind, &goal.target) {
        (GoalKind::Elite, Target::Camp(c)) => Some(*c),
        _ => None,
    }
}

/// `at` is inside the seat's view with a margin: an actor standing there would be delivered.
fn sees(cx: &Cx<'_>, at: Vec2) -> bool {
    crate::bots::view::in_view_at(cx.p.me.at, at, -DEFEND_U)
}

/// The card definition of a chest option id, when it is a card (base, conditional, augment or native).
fn card_of(id: &str) -> Option<CardId> {
    CardId::ALL
        .into_iter()
        .chain(CardId::NATIVE)
        .find(|c| c.as_str() == id)
}

impl Elite {
    fn watch_of(&self, camp: u32) -> Option<&Watch> {
        self.watch.iter().find(|w| w.camp == camp)
    }

    fn known_alive(&self, camp: &CampSeen) -> bool {
        camp.state == 0 || self.watch_of(camp.index).is_some_and(|w| w.alive)
    }

    fn banned(&self, camp: u32, now: f64) -> bool {
        self.banned.iter().any(|(c, until)| *c == camp && *until > now)
    }

    fn ban(&mut self, camp: u32, now: f64, e: &ElitePrices) {
        self.banned.push((camp, now + e.ban_seconds * 1_000.));
    }

    /// Keep each elite camp's elite alive or dead: alive while the camp is untouched since its respawn or the elite is
    /// in sight (any distance for the held goal's camp, its camp's join radius for another), dead once cleared or once
    /// the camp (or where the elite last stood) is in sight with no living elite of the kind there. A death seen while
    /// this seat held and fought the camp's goal is a kill: the chest phase.
    fn watch_elites(&mut self, cx: &Cx<'_>) {
        let held = goal_camp(cx.goal);
        let now = cx.p.now;
        for camp in &cx.p.camps {
            let Some(kind) = camp.elite else {
                continue;
            };
            let i = match self.watch.iter().position(|w| w.camp == camp.index) {
                Some(i) => i,
                None => {
                    self.watch.push(Watch {
                        camp: camp.index,
                        alive: false,
                        at: None,
                    });
                    self.watch.len() - 1
                }
            };
            let was = self.watch[i];
            let near = was.at.unwrap_or(camp.at);
            let mine = held == Some(camp.index);
            let seen = elite_mob(cx, kind, near)
                .filter(|m| mine || dist(m.at, camp.at) <= CAMP_JOIN_RADIUS)
                .map(|m| m.at);
            let w = &mut self.watch[i];
            if let Some(at) = seen {
                w.alive = true;
                w.at = Some(at);
            } else if camp.state == 0 && camp.alive > 0 {
                w.alive = true;
            } else if camp.state == 2
                || camp.alive == 0
                || sees(cx, camp.at)
                || was.at.is_some_and(|at| sees(cx, at))
            {
                w.alive = false;
                w.at = None;
            }
            if was.alive && !w.alive && mine && self.fight_since.is_some() {
                self.killed.retain(|(c, _)| *c != camp.index);
                self.killed.push((camp.index, now));
            }
            // A dead elite or a fresh one (the camp respawned, untouched) has no fight on record.
            if !w.alive || (camp.state == 0 && !mine) {
                self.tallies.retain(|t| t.camp != camp.index);
            }
        }
    }

    /// A fight the arbiter handed to another goal while the elite stood is given up on for `ban_seconds`: the next
    /// proposal of the camp, after the ban, is priced with the exchange the fight showed (`tallies`).
    fn watch_goal(&mut self, cx: &Cx<'_>) {
        let now = cx.p.now;
        let held = goal_camp(cx.goal);
        if let Some(was) = self.held.filter(|c| held != Some(*c)) {
            let alive = self.watch_of(was).is_some_and(|w| w.alive);
            if self.fight_since.is_some() && alive && !self.killed.iter().any(|(c, _)| *c == was) {
                let e = cx.prices.elite;
                self.ban(was, now, &e);
            }
            self.fight_since = None;
        }
        self.held = held;
    }

    /// Add this frame to the held fight's tally: hp lost and elite hp destroyed since the last frame.
    fn track(&mut self, cx: &Cx<'_>) {
        let hp = cx.p.me.hp_share();
        let Some(index) = self.held.filter(|_| self.fight_since.is_some()) else {
            for t in &mut self.tallies {
                t.hp = hp;
                t.elite = None;
            }
            return;
        };
        let Some(camp) = cx.p.camp(index) else {
            return;
        };
        let Some(kind) = camp.elite else {
            return;
        };
        let elite = self.fight_left(cx, camp, kind, true);
        let i = match self.tallies.iter().position(|t| t.camp == index) {
            Some(i) => i,
            None => {
                self.tallies.push(Tally {
                    camp: index,
                    lost: 0.,
                    dealt: 0.,
                    hp,
                    elite,
                });
                self.tallies.len() - 1
            }
        };
        let t = &mut self.tallies[i];
        t.lost += (t.hp - hp).max(0.);
        if let (Some(was), Some(now)) = (t.elite, elite) {
            t.dealt += (was - now).max(0.);
        }
        t.hp = hp;
        if elite.is_some() {
            t.elite = elite;
        }
    }

    /// The fight left at the camp in elites' worth of hp: the elite's share standing plus `add_hp` per member's worth
    /// left (the members alive less what the visible ones have lost). `None` when `seen` asks for the elite in sight and
    /// it is not (its share is then taken as full).
    fn fight_left(&self, cx: &Cx<'_>, camp: &CampSeen, kind: MobTypeId, seen: bool) -> Option<f64> {
        let near = self.watch_of(camp.index).and_then(|w| w.at).unwrap_or(camp.at);
        let elite = elite_mob(cx, kind, near).map(|m| m.hp_share.clamp(0., 1.));
        if seen && elite.is_none() {
            return None;
        }
        let lost: f64 = cx
            .p
            .mobs
            .iter()
            .filter(|m| m.alive && m.class == MobClass::Regional && m.camp == Some(camp.index))
            .map(|m| 1. - m.hp_share.clamp(0., 1.))
            .sum();
        let adds = (f64::from(camp.alive.saturating_sub(1)) - lost).max(0.);
        Some(elite.unwrap_or(1.) + cx.prices.elite.add_hp * adds)
    }

    /// The readable chests' first-readable times, the asks of closed panels dropped, the cards this seat took.
    fn watch_chests(&mut self, cx: &Cx<'_>) {
        let now = cx.p.now;
        for c in &cx.p.facts.chests {
            let Some(options) = &c.options else {
                self.readable.retain(|(k, _)| *k != c.key);
                self.asks.retain(|a| a.chest != c.key);
                continue;
            };
            if !self.readable.iter().any(|(k, _)| *k == c.key) {
                self.readable.push((c.key.clone(), now));
            }
            for (i, by) in c.taken_by.iter().enumerate() {
                if *by == cx.p.self_key {
                    if let Some(card) = options.get(i).filter(|s| !s.is_empty()) {
                        if !self.took.contains(card) {
                            self.took.push(card.clone());
                        }
                    }
                }
            }
        }
    }

    /// The card is not pickable for this seat: not a card (a boost or a kill option stays the chest module's), held (the
    /// frame's build row or a card taken here: `Owned`), or a conditional whose base the seat does not hold (`Needs`).
    fn refused(&self, cx: &Cx<'_>, id: &str) -> bool {
        let holds = |card: &str| {
            self.took.iter().any(|t| t == card)
                || cx
                    .p
                    .facts
                    .build
                    .as_ref()
                    .is_some_and(|b| b.iter().any(|(k, n)| k == card && *n > 0.))
        };
        let Some(card) = card_of(id) else {
            return true;
        };
        if holds(id) {
            return true;
        }
        card.definition().requires.is_some_and(|req| {
            !CardId::ALL
                .into_iter()
                .any(|base| base.definition().base == Some(req) && holds(base.as_str()))
        })
    }

    /// The chest the kill dropped: of the frame's chests within the camp's join radius, the nearest the camp.
    fn camp_chest<'a>(cx: &'a Cx<'_>, camp_at: Vec2) -> Option<&'a ChestRef> {
        cx.p.facts
            .chests
            .iter()
            .filter(|c| dist(c.at, camp_at) <= CAMP_JOIN_RADIUS)
            .min_by(|a, b| {
                dist(a.at, camp_at)
                    .total_cmp(&dist(b.at, camp_at))
                    .then_with(|| a.key.cmp(&b.key))
            })
    }

    /// The chest phase of `camp` is over: no chest found in time, the chest picked here, expired, or the wait ran out.
    fn chest_over(&self, cx: &Cx<'_>, camp_at: Vec2, killed_at: f64) -> bool {
        let e = &cx.prices.elite;
        let now = cx.p.now;
        if now - killed_at > e.chest_wait_seconds * 1_000. {
            return true;
        }
        match Self::camp_chest(cx, camp_at) {
            None => now - killed_at > e.chest_find_seconds * 1_000.,
            Some(c) => {
                c.picked
                    || c.expires_at <= now
                    || c.taken_by.iter().any(|by| *by == cx.p.self_key)
                    || self
                        .asks
                        .iter()
                        .any(|a| a.chest == c.key && now - a.first > ASK_WINDOW_MS)
            }
        }
    }

    /// The share of the elite's hp still standing (1 when it is not in sight).
    fn elite_left(&self, cx: &Cx<'_>, camp: &CampSeen, kind: MobTypeId) -> f64 {
        let near = self.watch_of(camp.index).and_then(|w| w.at).unwrap_or(camp.at);
        elite_mob(cx, kind, near)
            .filter(|m| {
                goal_camp(cx.goal) == Some(camp.index) || dist(m.at, camp.at) <= CAMP_JOIN_RADIUS
            })
            .map_or(1., |m| m.hp_share.clamp(0., 1.))
    }

    /// The chance this fight kills the seat: the bench table for the kind (`death_prior`) with the hp held set against
    /// the share of the elite still standing; a kind with no row, the farm's model against the kind's duel share.
    fn death(&self, cx: &Cx<'_>, camp: &CampSeen, kind: MobTypeId, left: f64) -> f64 {
        let e = &cx.prices.elite;
        let me = &cx.p.me;
        let held = me.hp_share() + cx.prices.sustain.potion_hp(me.potion_large, me.potion_small);
        let has_row = e.kind_death.iter().any(|(k, _)| *k == kind.as_str());
        let by_rate = self.by_rate(cx, camp, kind, held);
        if has_row {
            let stacks: f64 = cx.p.summary.as_ref().map_or(0., |s| {
                s.mob_rows.iter().map(|r| f64::from(r.stacks)).sum()
            });
            let weapon = me.weapons[usize::from(me.slot.min(1))];
            let held = held / left.max(f64::MIN_POSITIVE);
            let prior = death_prior(e, kind, weapon, stacks, cx.prices.stacks_per_camp, held);
            return prior.max(by_rate);
        }
        let Some(faction) = me.faction else {
            return e.death_range.1;
        };
        let rule = cx.knowledge.map(|k| k.elite_rule()).unwrap_or_default();
        let share = matchup_share_of_kind(faction, zone_faction(camp.zone), true, Some(kind), rule);
        let adds = f64::from(camp.alive.saturating_sub(1));
        let weight = (1. + e.add_weight * adds) * left;
        death_chance(held, share, weight, &cx.prices.death)
            .clamp(e.death_range.0, e.death_range.1)
            .max(by_rate)
    }

    /// The death chance the fight's own exchange says, once its tally holds `tally_min_dealt` of the elite and
    /// `tally_min_lost` of the seat's hp: the hp it has cost per elite destroyed, times the elite left, against the hp
    /// held, read by the farm's death model (`Prices::death`). The floor of `death_range` before.
    fn by_rate(&self, cx: &Cx<'_>, camp: &CampSeen, kind: MobTypeId, held: f64) -> f64 {
        let e = &cx.prices.elite;
        let Some(t) = self.tallies.iter().find(|t| {
            t.camp == camp.index && t.dealt >= e.tally_min_dealt && t.lost >= e.tally_min_lost
        }) else {
            return e.death_range.0;
        };
        let model = &cx.prices.death;
        let left = self.fight_left(cx, camp, kind, false).unwrap_or(1.);
        let need = t.lost / t.dealt * left;
        (model.death_per_ratio * (need / held.max(f64::MIN_POSITIVE) - model.safe_ratio))
            .clamp(e.death_range.0, e.death_range.1)
    }

    /// The fight candidate of one elite camp.
    fn fight_candidate(&self, cx: &mut Cx<'_>, camp: &CampSeen, kind: MobTypeId) -> Candidate {
        let e = cx.prices.elite;
        let left = self.elite_left(cx, camp, kind);
        let death = self.death(cx, camp, kind, left);
        let at = cx.p.me.at;
        // One path search for the camp the seat already goes to; straight estimates for the rest, so a seat with no
        // elite goal spends none of the tick's search budget here.
        let walk = if goal_camp(cx.goal) == Some(camp.index) {
            eta_minutes(cx.paths, at, camp.at).unwrap_or_else(|| straight_minutes(at, camp.at))
        } else {
            straight_minutes(at, camp.at)
        };
        Candidate {
            goal: RaidGoal::new(GoalKind::Elite, Target::Camp(camp.index)),
            value: e.share + e.pool_pick,
            success: (1. - death) * clear_share(&e, kind) * cx.prices.pick_success,
            death,
            loss: cx.prices.death_loss,
            minutes: walk
                + e.fight_seconds * left / 60.
                + (e.chest_seconds + cx.prices.pick_seconds) / 60.,
        }
    }

    /// The chest candidate of a camp whose elite this seat killed: the card's worth (the best priced one offered when
    /// the cards are readable, else the pool's mean best).
    fn chest_candidate(&self, cx: &Cx<'_>, camp: &CampSeen) -> Candidate {
        let e = &cx.prices.elite;
        let chest = Self::camp_chest(cx, camp.at);
        let worth = chest
            .and_then(|c| {
                let options = c.options.as_ref()?;
                let i = best_card(cx.prices, options, &c.taken_by, &|id| self.refused(cx, id))?;
                Some(cx.prices.augment_value(&options[i]))
            })
            .unwrap_or(e.pool_pick);
        let to = chest.map_or(camp.at, |c| c.at);
        Candidate {
            goal: RaidGoal::new(GoalKind::Elite, Target::Camp(camp.index)),
            value: worth,
            success: cx.prices.pick_success,
            death: 0.,
            loss: cx.prices.death_loss,
            minutes: straight_minutes(cx.p.me.at, to) + cx.prices.pick_seconds / 60.,
        }
    }

    /// One think of the chest phase: walk to the camp's chest and stand in its panel's reach; the pick rides along.
    fn chest_step(&mut self, cx: &mut Cx<'_>, camp_at: Vec2, killed_at: f64) -> Step {
        if self.chest_over(cx, camp_at, killed_at) {
            return Step::done("the elite's chest is picked, gone or out of time");
        }
        let Some(at) = Self::camp_chest(cx, camp_at).map(|c| c.at) else {
            return Step::running(Vec::new(), "looking for the elite's chest");
        };
        let step = travel::walk(cx, at, "to the elite's chest");
        if step.status == Status::Running {
            return step;
        }
        Step::running(Vec::new(), "at the elite's chest")
    }

    /// One think of the fight: the elite first, an add of the camp (or one fighting at our side) while the elite is not
    /// in sight, else the walk to where the elite was or to the camp.
    fn fight_step(&mut self, cx: &mut Cx<'_>, camp: &CampSeen, kind: MobTypeId) -> Step {
        let e = cx.prices.elite;
        let now = cx.p.now;
        let index = camp.index;
        let near = self.watch_of(index).and_then(|w| w.at).unwrap_or(camp.at);
        let elite = elite_mob(cx, kind, near).cloned();
        // A member of this camp the fight may take: labelled with the camp or with none (a chaser is labelled by where it
        // stands), or fighting at our side.
        let member = |m: &MobSeen| {
            m.alive
                && m.class == MobClass::Regional
                && (m.camp == Some(index)
                    || m.camp.is_none()
                    || (m.engaged && cx.p.dist(m.at) <= DEFEND_U))
        };
        // The adds first, the one fought last kept while it stands (the owner's lock names it in this frame, else the
        // member nearest where it stood), else the nearest: an add's death removes its hits for good, and Sustain's
        // leave, which reads the regional members only, then sees the camp as it is. The elite once no add is near.
        let locked = match &cx.p.base.self_view.owner_lock {
            Some(OwnerLock::Held { target: Some(key) }) => {
                cx.p.mobs.iter().find(|m| m.key == *key && member(m))
            }
            _ => None,
        };
        let kept = locked.or_else(|| {
            self.target.and_then(|was| {
                cx.p.mobs
                    .iter()
                    .filter(|m| member(m) && dist(m.at, was) <= DEFEND_U)
                    .min_by(|a, b| {
                        dist(a.at, was)
                            .total_cmp(&dist(b.at, was))
                            .then_with(|| a.key.cmp(&b.key))
                    })
            })
        });
        let add = kept
            .or_else(|| {
                cx.p.mobs
                    .iter()
                    .filter(|m| member(m) && cx.p.dist(m.at) <= FIGHT_U)
                    .min_by(|a, b| {
                        cx.p.dist(a.at)
                            .total_cmp(&cx.p.dist(b.at))
                            .then_with(|| a.key.cmp(&b.key))
                    })
            })
            .cloned();
        // The camp is on the bot: the elite or a member close, or a mob fighting at our side.
        let fighting = elite.as_ref().is_some_and(|m| cx.p.dist(m.at) <= FIGHT_U)
            || cx.p.mobs.iter().any(|m| {
                m.alive
                    && m.class == MobClass::Regional
                    && ((m.camp == Some(index) && cx.p.dist(m.at) <= FIGHT_U)
                        || (m.engaged && cx.p.dist(m.at) <= DEFEND_U))
            });
        if fighting {
            let since = *self.fight_since.get_or_insert(now);
            if now - since > e.give_up_seconds * 1_000. {
                self.ban(index, now, &e);
                return Step::failed("the elite fight ran out of time");
            }
        }
        let Some(mob) = add.or(elite) else {
            self.target = None;
            let step = travel::walk(cx, near, "to the elite camp");
            match step.status {
                Status::Running => {
                    self.empty_since = None;
                    return step;
                }
                Status::Failed => {
                    self.ban(index, now, &e);
                    return step;
                }
                Status::Done => {}
            }
            let since = *self.empty_since.get_or_insert(now);
            if now - since >= e.empty_seconds * 1_000. {
                self.ban(index, now, &e);
                return Step::failed("no elite in sight at the camp");
            }
            return Step::running(Vec::new(), "waiting at the elite camp");
        };
        self.empty_since = None;
        self.target = (mob.class == MobClass::Regional).then_some(mob.at);
        let mut intents = Vec::new();
        let slots = cx.p.me.weapons;
        let slot = best_slot_vs_mob(slots, cx.p.me.slot, mob.weapon);
        if slot != cx.p.me.slot.min(1) {
            intents.push(Intent {
                body: IntentBody::Swap { slot },
                priority: PRIORITY_COMBAT,
                timing: IntentTiming::Step {},
                why: "best slot vs the elite".into(),
            });
        }
        let in_hand = slots[usize::from(cx.p.me.slot.min(1))];
        if cx.p.dist(mob.at) <= lock_range(in_hand) {
            intents.push(Intent {
                body: IntentBody::Lock {
                    target: mob.key.clone(),
                    at: mob.at,
                },
                priority: PRIORITY_COMBAT,
                timing: IntentTiming::Step {},
                why: "elite engage".into(),
            });
        } else if let Some(next) = step_toward(cx.paths, cx.p.me.at, mob.at) {
            intents.push(move_intent(next, "elite approach"));
        }
        if fighting {
            Step::committed(intents, "fighting the elite")
        } else {
            Step::running(intents, "approaching the elite")
        }
    }

    /// The readable chest the priced pick answers: the nearest with cards shown, not picked here, time left and out of
    /// its birth hold (Loot's `cards_in_reach` and `in_hold`); ties by key.
    fn pick_chest<'a>(cx: &'a Cx<'_>) -> Option<&'a ChestRef> {
        let now = cx.p.now;
        cx.p.facts
            .chests
            .iter()
            .filter(|c| c.options.is_some() && !c.picked)
            .filter(|c| c.expires_at - now > CHEST_HUNT.pick_margin_ms)
            .filter(|c| c.life_ms - (c.expires_at - now) >= CHEST_HUNT.hold_ms)
            .min_by(|a, b| {
                cx.p.dist(a.at)
                    .total_cmp(&cx.p.dist(b.at))
                    .then_with(|| a.key.cmp(&b.key))
            })
    }

    fn pick(&mut self, cx: &Cx<'_>) -> Option<Intent> {
        use crate::bots::wish::{EFFECT_CASTING, EFFECT_STUNNED};
        // The server refuses a pick from a stunned or casting player, and a refusal is an ask spent.
        if cx.p.me.effects & (EFFECT_STUNNED | EFFECT_CASTING) != 0 || !cx.p.me.alive {
            return None;
        }
        let now = cx.p.now;
        let chest = Self::pick_chest(cx)?;
        let options = chest.options.as_ref()?;
        let option = best_card(cx.prices, options, &chest.taken_by, &|id| self.refused(cx, id))?;
        match self.asks.iter().position(|a| a.chest == chest.key) {
            None => self.asks.push(Ask {
                chest: chest.key.clone(),
                first: now,
                option,
                n: 1,
            }),
            Some(i) => {
                let ask = &mut self.asks[i];
                if now - ask.first > ASK_WINDOW_MS {
                    return None;
                }
                if ask.option != option {
                    if ask.n >= MAX_ASKS {
                        return None;
                    }
                    ask.option = option;
                    ask.n += 1;
                }
            }
        }
        let opened = self
            .readable
            .iter()
            .find(|(k, _)| *k == chest.key)
            .map_or(now, |(_, at)| *at);
        Some(Intent {
            body: IntentBody::Choose {
                msg: ChooseMsg {
                    chest: chest.key.clone(),
                    pick: option as i8,
                    replace: None,
                },
            },
            priority: PRIORITY_COMBAT,
            timing: IntentTiming::React {
                stimulus: Stimulus {
                    kind: StimulusKind::ChestUp,
                    at: opened,
                    subject: Some(chest.key.clone()),
                    central: true,
                    value: None,
                },
            },
            why: format!("chest {}: priced card {option} ({})", chest.key, options[option]),
        })
    }
}

impl Executor for Elite {
    fn kinds(&self) -> &'static [GoalKind] {
        &[GoalKind::Elite]
    }
    fn reset(&mut self) {
        *self = Self::default();
    }
    fn observe(&mut self, cx: &mut Cx<'_>) {
        if !cx.prices.elite.on || !cx.p.active {
            return;
        }
        self.watch_goal(cx);
        self.watch_elites(cx);
        self.track(cx);
        self.watch_chests(cx);
        // SCRATCH-TRACE
        if std::env::var("ELITE_TRACE").is_ok() {
            let g = format!("{:?}/{:?}/{:?}", cx.goal.kind, cx.goal.target, cx.status);
            if g != self.trace_goal || cx.p.tick % 100 == 0 {
                let el: Vec<String> = cx.p.mobs.iter().filter(|m| m.class == MobClass::Elite).map(|m| format!("{:?}@{:.0},{:.0} hp{:.2} d{:.0}", m.mob_type, m.at.x, m.at.y, m.hp_share, cx.p.dist(m.at))).collect();
                eprintln!("T{} goal {} hp {:.2} pots {} elites {:?} watch {:?} killed {:?} banned {:?}", cx.p.tick, g, cx.p.me.hp_share(), cx.p.me.potion_large, el, self.watch.iter().filter(|w| w.alive).map(|w| w.camp).collect::<Vec<_>>(), self.killed, self.banned);
                self.trace_goal = g;
            }
        }
    }
    fn propose(&mut self, cx: &mut Cx<'_>, out: &mut Vec<Candidate>) {
        let e = cx.prices.elite;
        if !e.on {
            return;
        }
        let now = cx.p.now;
        self.banned.retain(|(_, until)| *until > now);
        let p = cx.p;
        let live: Vec<(u32, f64)> = self
            .killed
            .iter()
            .filter(|(c, at)| p.camp(*c).is_some_and(|camp| !self.chest_over(cx, camp.at, *at)))
            .copied()
            .collect();
        self.killed = live;
        for camp in &p.camps {
            let Some(kind) = camp.elite else {
                continue;
            };
            if self.killed.iter().any(|(c, _)| *c == camp.index) {
                out.push(self.chest_candidate(cx, camp));
                continue;
            }
            if camp.alive == 0 || self.banned(camp.index, now) || !self.known_alive(camp) {
                continue;
            }
            let c = self.fight_candidate(cx, camp, kind);
            if std::env::var("ELITE_TRACE").is_ok() && (goal_camp(cx.goal) == Some(camp.index) || cx.p.tick % 200 < 20) { eprintln!("T{} cand camp {} {:?} death {:.3} min {:.2} rate {:.2}", cx.p.tick, camp.index, kind, c.death, c.minutes, crate::bots::raid::arbiter::rate(&c)); } // SCRATCH-TRACE
            out.push(c);
        }
    }
    fn start(&mut self, goal: &RaidGoal, _cx: &mut Cx<'_>) {
        if goal.kind == GoalKind::Elite {
            self.fight_since = None;
            self.empty_since = None;
        }
    }
    fn act(&mut self, goal: &RaidGoal, cx: &mut Cx<'_>) -> Step {
        let Target::Camp(index) = goal.target else {
            return Step::failed("elite without a camp");
        };
        let Some(camp) = cx.p.camp(index).cloned() else {
            return Step::failed("camp not delivered");
        };
        let Some(kind) = camp.elite else {
            return Step::failed("the camp has no elite");
        };
        if let Some(&(_, killed_at)) = self.killed.iter().find(|(c, _)| *c == index) {
            return self.chest_step(cx, camp.at, killed_at);
        }
        if !self.known_alive(&camp) {
            return Step::done("the elite is gone");
        }
        let st = self.fight_step(cx, &camp, kind);
        if std::env::var("ELITE_TRACE").is_ok() && st.status != Status::Running { eprintln!("T{} act -> {:?} {}", cx.p.tick, st.status, st.why); } // SCRATCH-TRACE
        st
    }
    fn ride_along(&mut self, _goal: &RaidGoal, cx: &mut Cx<'_>) -> Step {
        if !cx.prices.elite.on {
            return Step::none();
        }
        match self.pick(cx) {
            Some(intent) => Step::running(vec![intent], "priced chest pick rides along"),
            None => Step::none(),
        }
    }
}

/// The share of fights with the elite of `kind` the bot ends with a kill (`ElitePrices::kind_clear`, 1 with no row).
pub fn clear_share(elite: &ElitePrices, kind: MobTypeId) -> f64 {
    elite
        .kind_clear
        .iter()
        .find(|(k, _)| *k == kind.as_str())
        .map_or(1., |(_, c)| c.clamp(0., 1.))
}

/// The bench row of `kind` read at `camps` banked camps: linear between the `camp_columns`, held at the ends.
fn row_at(columns: &[f64; 4], row: &[f64; 4], camps: f64) -> f64 {
    if camps <= columns[0] {
        return row[0];
    }
    for i in 0..3 {
        let (a, b) = (columns[i], columns[i + 1]);
        if camps <= b {
            let t = if b > a { (camps - a) / (b - a) } else { 1. };
            return row[i] + (row[i + 1] - row[i]) * t;
        }
    }
    row[3]
}

/// The chance a fight with the elite of `kind` kills the bot, within `death_range`: the bench table row of `kind`
/// (`ElitePrices::kind_death`) read at `stacks / stacks_per_camp` banked camps (linear between `camp_columns`, held at
/// the ends), times the weapon factor of the weapon in hand (1 when it has no row), divided by `held` (the hp share held
/// plus the potions held, as `Farm` counts it). A kind with no row reads the top of `death_range`: the executor prices
/// it with the farm's death model instead.
pub fn death_prior(
    elite: &ElitePrices,
    kind: MobTypeId,
    weapon: Option<WeaponId>,
    stacks: f64,
    stacks_per_camp: f64,
    held: f64,
) -> f64 {
    let (lo, hi) = elite.death_range;
    let Some((_, row)) = elite.kind_death.iter().find(|(k, _)| *k == kind.as_str()) else {
        return hi;
    };
    let camps = if stacks_per_camp > 0. {
        stacks.max(0.) / stacks_per_camp
    } else {
        0.
    };
    let base = row_at(&elite.camp_columns, row, camps);
    let factor = weapon
        .and_then(|w| elite.weapon_factor.iter().find(|(k, _)| *k == w.as_str()))
        .map_or(1., |(_, f)| *f);
    let p = base * factor / held.max(f64::MIN_POSITIVE);
    if p.is_finite() {
        p.clamp(lo, hi)
    } else {
        hi
    }
}

/// The index of the offered card the price table ranks highest: the greatest `Prices::augment_value` among the options
/// not taken (`taken_by[i]` empty) and not held (`held(card)`), ties to the lowest index. `None` when the table prices
/// every pickable card at 0 (`farm_only`), when `prices.elite.on` is false, or when no card is pickable: the chest
/// module's own pick (and its skip) then stands.
pub fn best_card(
    prices: &Prices,
    options: &[String],
    taken_by: &[String],
    held: &dyn Fn(&str) -> bool,
) -> Option<usize> {
    if !prices.elite.on {
        return None;
    }
    let mut best: Option<(usize, f64)> = None;
    for (i, card) in options.iter().enumerate() {
        if taken_by.get(i).is_some_and(|by| !by.is_empty()) || card.is_empty() || held(card) {
            continue;
        }
        let v = prices.augment_value(card);
        if v > 0. && best.is_none_or(|(_, b)| v > b) {
            best = Some((i, v));
        }
    }
    best.map(|(i, _)| i)
}

#[cfg(test)]
#[path = "elite_tests.rs"]
mod tests;
