//! Quest walker (P-RAID-Q1): accept a walker quest at its giver, walk to its objectives, send the interaction the server
//! offers, and stand at the end point until the quest turns in. Eleven quests (`WALKER`); dwell, decision, kill, PvP,
//! search quests and `feed_or_hunt` are other packets' and are never accepted here.
//!
//! The goal is `Target::Quest { def, stage: 0 }`: the quest's def id names it, and what to do next (accept, walk, send,
//! stand) is read from the board each think, because a quest's progress lives in the server's rows, not in the goal.
//! Everything frame-local (quest handles, the offered action list) is read from this frame and resolved by `refs.rs` at
//! send time.
//!
//! **A walk holds the bot.** Once a quest is accepted, its walk and stand steps are `Step::committed`: the arbiter keeps
//! the goal while its own score is positive and only a `Sustain` goal may pre-empt it (`GoalKind::preempts_commitment`).
//! So a chest that shows up on the route waits for the quest, and a camp on the route is not farmed (the death term
//! prices a living camp near an objective, and the walker proposes nothing once it can no longer finish in time, which
//! ends the commitment and lets the attempt expire for free). A quest with no route timer (`letters`) is held the same
//! way, bounded by the coliseum lock. Only the walk after accepting is held; going to a giver is not.
//!
//! The walker never abandons (an expired attempt resets for free), never attacks and never targets a player.
use crate::bots::planning::{matchup_share, zone_faction};
use crate::bots::raid::acts::Act;
use crate::bots::raid::exec::farm::{death_chance, members_left};
use crate::bots::raid::exec::travel::{eta_minutes, move_intent, step_toward, straight_minutes};
use crate::bots::raid::goal::{Candidate, Cx, Executor, GoalKind, RaidGoal, Status, Step, Target};
use crate::bots::raid::percept::CAMP_JOIN_RADIUS;
use crate::bots::raid::quest::SPELL_SLOTS;
use rh_protocol::state::{WireQuestObjective, WireQuestRow};
use rh_sim::mapdata::Zone;
use rh_sim::quests::board::catalog::{RewardKind, DEFINITIONS};
use rh_sim::{FactionId, Vec2};

/// The quests this executor does (docs/backlog/packets/P-RAID-Q1.md F1/F2).
pub const WALKER: [&str; 11] = [
    "ember_walk",
    "watchtowers",
    "barrow_run",
    "pilgrims_path",
    "a_cruel_joke",
    "even_skeletons_get_cold",
    "tuber_eats",
    "lost_brother",
    "smugglers_run",
    "letters",
    "let_him_cook",
];
/// Quests whose end point is the delivery token, not the giver (`attempts.rs:387`).
const ENDS_AT_TOKEN: [&str; 3] = ["tuber_eats", "even_skeletons_get_cold", "letters"];
/// The contact action (take, collect, deliver, scare): `quests/board/attempts/choices.rs`.
const ACTION_CONTACT: u8 = 3;
/// The reward slot choice (a decision quest packet's).
const ACTION_SLOT_CHOICE: u8 = 8;
/// A pseudo action number for the accept send in the resend throttle.
const ACCEPT_KEY: u8 = 255;
/// Walk to within this of a point to accept, claim, deliver or stand (the server's range is 64 u).
const STAND_U: f64 = 32.;
/// A send is not repeated before this long (the act queue pauses and the frame's lag).
const RESEND_MS: f64 = 2_500.;
/// Standing at a giver this long with the quest still refused (a scared-off giver, a taken offer) bans it...
const REFUSED_MS: f64 = 1_500.;
/// ...for this long.
const BAN_MS: f64 = 20_000.;
/// A dropped quest spell this near is another packet's pick-up: the walker does nothing.
const DROP_NEAR_U: f64 = 160.;
/// Seconds of timer the server adds to 1.5 times its own path walk (`attempts.rs:302-305`).
const TIMER_BASE_S: f64 = 15.;
/// A quest with no route timer and no lock time yet: the walk assumed before accepting (min).
const UNTIMED_WALK_MIN: f64 = 1.;
/// Slack (time left over walk still to do) from which the walk is certain to finish; below 1 it cannot.
const SAFE_SLACK: f64 = 1.2;

#[derive(Default)]
pub struct Walker {
    shares: Vec<((FactionId, FactionId), f64)>,
    /// Quests not to propose or work on until the time: (def, until ms).
    banned: Vec<(String, f64)>,
    /// When a send of (def, action, target) was last queued: (def, action, target, ms).
    sent: Vec<(String, u8, u8, f64)>,
    /// Since when the owner has stood at this quest's giver unable to accept: (def, ms).
    refused: Option<(String, f64)>,
}

/// Minutes of walk a route timer of `ms` stands for: the timer is a floor under `route_pace x walk + slack`.
pub(super) fn timer_spend_minutes(ms: u64) -> f64 {
    ((ms as f64 / 1000. - TIMER_BASE_S) / 1.5 / 60.).max(0.1)
}

fn dist(a: Vec2, b: Vec2) -> f64 {
    rh_sim::math::hypot(a.x - b.x, a.y - b.y)
}

fn point(p: rh_protocol::state::WireQuestPoint) -> Vec2 {
    Vec2::new(p.x, p.y)
}

fn definition(def: &str) -> Option<&'static rh_sim::quests::board::catalog::BoardDef> {
    DEFINITIONS.iter().find(|d| d.id == def)
}

/// This frame's objective points of the row, in the view's order.
fn objectives(cx: &Cx<'_>, row: &WireQuestRow) -> Vec<Vec2> {
    cx.p.quests.as_ref().map_or_else(Vec::new, |q| {
        q.objectives
            .iter()
            .filter(|o: &&WireQuestObjective| o.quest == row.handle)
            .map(|o| point(o.at))
            .collect()
    })
}

/// The row has a reward slot choice waiting (`pending`, action 8): a decision packet's.
fn slot_choice(cx: &Cx<'_>, row: &WireQuestRow) -> bool {
    cx.p.quests.as_ref().is_some_and(|q| {
        q.pending.contains(&row.handle)
            || q.actions
                .iter()
                .any(|a| a.quest == row.handle && a.action == ACTION_SLOT_CHOICE)
    })
}

/// A dropped quest spell lies near the owner and a slot is free to pick it up (decide.rs's job): with both slots full the
/// server offers no pick-up, and the drop must not freeze the walker. A drop within `STAND_U` for which the frame offers no
/// pick-up (a wall, no room) is not waited for either: decide.rs gives that stand up.
fn drop_near(cx: &Cx<'_>) -> bool {
    cx.p.quests.as_ref().is_some_and(|q| {
        q.spells.len() < SPELL_SLOTS
            && q.drops.iter().any(|d| {
                let gap = dist(point(d.at), cx.p.me.at);
                gap <= DROP_NEAR_U
                    && (gap > STAND_U
                        || q.actions.iter().any(|a| {
                            a.action == super::decide::ACTION_PICKUP && a.target == d.index
                        }))
            })
    })
}

/// Spell slots spoken for: quest spells held, plus accepted quests whose reward is a spell and that are not finished (their
/// reward will take a slot, and a third one sticks in `pending`).
fn slots_taken(cx: &Cx<'_>) -> usize {
    cx.p.quests.as_ref().map_or(0, |q| {
        q.spells.len()
            + q.rows
                .iter()
                .filter(|r| r.owner_accepted && r.state == 1)
                .filter(|r| {
                    definition(&r.def).is_some_and(|d| d.reward.kind() != RewardKind::Permanent)
                })
                .count()
    })
}

/// Minutes to walk from the owner through every point of `objs` (nearest first), then to `end`.
fn trip_minutes(cx: &mut Cx<'_>, objs: &[Vec2], end: Option<Vec2>) -> f64 {
    let mut at = cx.p.me.at;
    let mut left = objs.to_vec();
    let mut minutes = 0.;
    let mut first = true;
    while !left.is_empty() {
        let (i, _) = left
            .iter()
            .enumerate()
            .map(|(i, p)| (i, dist(at, *p)))
            .min_by(|a, b| a.1.total_cmp(&b.1))
            .unwrap_or((0, 0.));
        let next = left.remove(i);
        minutes += if first {
            eta_minutes(cx.paths, at, next).unwrap_or_else(|| straight_minutes(at, next))
        } else {
            straight_minutes(at, next)
        };
        first = false;
        at = next;
    }
    if let Some(e) = end {
        minutes += straight_minutes(at, e);
    }
    minutes
}

impl Walker {
    fn banned(&self, def: &str, now: f64) -> bool {
        self.banned
            .iter()
            .any(|(d, until)| d == def && *until > now)
    }

    /// True when `key` was queued within `RESEND_MS`; otherwise records it as queued now.
    fn throttled(&mut self, def: &str, action: u8, target: u8, now: f64) -> bool {
        self.sent.retain(|s| now - s.3 < RESEND_MS);
        if self
            .sent
            .iter()
            .any(|s| s.0 == def && s.1 == action && s.2 == target)
        {
            return true;
        }
        self.sent.push((def.to_string(), action, target, now));
        false
    }

    fn share(&mut self, me: FactionId, region: FactionId) -> f64 {
        if let Some((_, s)) = self.shares.iter().find(|(k, _)| *k == (me, region)) {
            return *s;
        }
        let s = matchup_share(me, region, false);
        self.shares.push(((me, region), s));
        s
    }

    /// The chance to die to the living camps whose centre lies within `CAMP_JOIN_RADIUS` of any of `points`: the walk
    /// passes where they stand and they join a fight (`let_him_cook`'s Plague contact). Floored at `prices.death_floor`.
    fn death_near(&mut self, cx: &Cx<'_>, points: &[Vec2]) -> f64 {
        let Some(me) = cx.p.me.faction else {
            return cx.prices.death_floor;
        };
        let hp = cx.p.me.hp_share();
        let mut survive = 1.;
        for camp in &cx.p.camps {
            if camp.alive == 0 || !points.iter().any(|p| dist(*p, camp.at) <= CAMP_JOIN_RADIUS) {
                continue;
            }
            let share = self.share(me, zone_faction(camp.zone));
            survive *= 1. - death_chance(hp, share, members_left(cx.p, camp), &cx.prices.death);
        }
        (1. - survive).max(cx.prices.death_floor)
    }

    fn candidate(&mut self, cx: &mut Cx<'_>, row: &WireQuestRow) -> Option<Candidate> {
        let def = row.def.as_str();
        let value = cx.prices.reward_value(def);
        if value <= 0. || row.state != 1 || row.owner_completed || slot_choice(cx, row) {
            return None;
        }
        let book = definition(def)?;
        let now = cx.p.now;
        let giver = point(row.giver);
        // Seconds the attempt may still take: the row's deadline, else the coliseum lock, else no limit.
        let time_left_s = if row.owner_accepted && row.expires_at > 0. {
            (row.expires_at - now) / 1000.
        } else {
            cx.p.arena
                .and_then(|a| a.patience_ends_at)
                .map_or(f64::INFINITY, |t| (t - now) / 1000.)
        };
        let (minutes, success, danger_points) = if row.owner_accepted {
            let objs = objectives(cx, row);
            if objs.is_empty() {
                return None;
            }
            let end = (!row.owner_ready && !ENDS_AT_TOKEN.contains(&def)).then_some(giver);
            let walk = trip_minutes(cx, &objs, end);
            let slack = time_left_s / (walk * 60.).max(1.);
            (walk, ((slack - 1.) / (SAFE_SLACK - 1.)).clamp(0., 1.), objs)
        } else {
            // Before accepting, the objectives are not in the view: the timer the server will set is at least 1.5 times
            // its own path walk plus 15 s, which says how far the route goes.
            // An upper bound on the walk: the route timer is a floor under the server's `1.5 x walk + 15 s`, so this
            // overstates short routes and biases the bot against short quests before it accepts.
            let spend = book
                .route_timer_ms
                .map_or(UNTIMED_WALK_MIN, timer_spend_minutes);
            if book.reward.kind() != RewardKind::Permanent && slots_taken(cx) >= SPELL_SLOTS {
                return None;
            }
            let walk = straight_minutes(cx.p.me.at, giver) + spend;
            let success = if time_left_s.is_finite() {
                ((time_left_s / (walk * 60.).max(1.) - 1.) / (SAFE_SLACK - 1.)).clamp(0., 1.)
            } else {
                1.
            };
            let danger = if def == "let_him_cook" {
                // The third contact stands in a Plague camp; which one is dealt is unknown before accepting: price the
                // nearest living one to the giver.
                cx.p.camps
                    .iter()
                    .filter(|c| c.zone == Zone::Plague && c.alive > 0)
                    .min_by(|a, b| dist(a.at, giver).total_cmp(&dist(b.at, giver)))
                    .map(|c| vec![c.at])
                    .unwrap_or_default()
            } else {
                Vec::new()
            };
            (walk, success, danger)
        };
        if success <= 0. {
            return None;
        }
        // Only `let_him_cook`'s third contact stands inside a camp that fights. Counting a camp near every quest's
        // tokens priced a quick touch as a camp fight and dropped accepted `ember_walk`s (measured on the oracle seeds:
        // four of six lost their first quest), so the other quests keep the floor.
        let death = if def == "let_him_cook" {
            self.death_near(cx, &danger_points)
        } else {
            cx.prices.death_floor
        };
        Some(Candidate {
            goal: RaidGoal::new(
                GoalKind::QuestWalk,
                Target::Quest {
                    def: def.to_string(),
                    stage: 0,
                },
            ),
            value,
            success,
            death,
            loss: cx.prices.death_loss,
            minutes,
        })
    }

    /// Walk toward `to` unless already standing at it (`STAND_U`).
    fn go(cx: &mut Cx<'_>, to: Vec2, why: &'static str) -> Step {
        if cx.p.dist(to) <= STAND_U {
            return Step::running(Vec::new(), "standing");
        }
        match step_toward(cx.paths, cx.p.me.at, to) {
            Some(next) => Step::running(vec![move_intent(next, why)], why),
            None => Step::failed("no way there"),
        }
    }
}

impl Executor for Walker {
    fn kinds(&self) -> &'static [GoalKind] {
        &[GoalKind::QuestWalk]
    }
    fn reset(&mut self) {
        *self = Self::default();
    }
    fn propose(&mut self, cx: &mut Cx<'_>, out: &mut Vec<Candidate>) {
        let now = cx.p.now;
        self.banned.retain(|(_, until)| *until > now);
        let p = cx.p;
        let Some(q) = p.quests.as_ref() else {
            return;
        };
        if !cx.p.active || cx.p.arena.is_some_and(|a| a.gates_locked) || drop_near(cx) {
            return;
        }
        let rows: Vec<WireQuestRow> = q
            .rows
            .iter()
            .filter(|r| WALKER.contains(&r.def.as_str()) && !self.banned(&r.def, now))
            .cloned()
            .collect();
        for row in rows {
            if let Some(c) = self.candidate(cx, &row) {
                out.push(c);
            }
        }
    }
    fn act(&mut self, goal: &RaidGoal, cx: &mut Cx<'_>) -> Step {
        let Target::Quest { def, .. } = &goal.target else {
            return Step::failed("quest walk without a quest");
        };
        let now = cx.p.now;
        let p = cx.p;
        let Some(q) = p.quests.as_ref() else {
            return Step::failed("no quest board");
        };
        if cx.p.arena.is_some_and(|a| a.gates_locked) || drop_near(cx) {
            return Step::failed("the board is locked or a drop is near");
        }
        let Some(row) = q.rows.iter().find(|r| r.def == *def).cloned() else {
            return Step::done("quest turned in");
        };
        if self.banned(def, now) || slot_choice(cx, &row) {
            return Step::failed("quest refused or waiting for a choice");
        }
        let giver = point(row.giver);
        if row.owner_accepted || row.can_accept || cx.p.dist(giver) > STAND_U {
            // Not waiting at a refusing giver: the refusal clock starts over next time.
            self.refused = None;
        }
        if !row.owner_accepted {
            if row.can_accept {
                let mut step = Step::running(Vec::new(), "accepting");
                if !self.throttled(def, ACCEPT_KEY, 0, now) {
                    step.acts.push(Act::QuestAccept { def: def.clone() });
                }
                return step;
            }
            if cx.p.dist(giver) > STAND_U {
                return Self::go(cx, giver, "to the giver");
            }
            // At the giver and still refused: wait a moment, then leave it for a while.
            let since = match &self.refused {
                Some((d, t)) if d == def => *t,
                _ => {
                    self.refused = Some((def.clone(), now));
                    now
                }
            };
            if now - since >= REFUSED_MS {
                self.banned.push((def.clone(), now + BAN_MS));
                self.refused = None;
                return Step::failed("the giver refuses");
            }
            return Step::running(Vec::new(), "waiting at the giver");
        }
        let objs = objectives(cx, &row);
        let Some(first) = objs
            .iter()
            .copied()
            .min_by(|a, b| dist(cx.p.me.at, *a).total_cmp(&dist(cx.p.me.at, *b)))
        else {
            return Step::failed("no objective in the view");
        };
        let mut step = Self::go(cx, first, "to the objective");
        // An accepted attempt is kept: its timer (or the coliseum lock, for a quest with none) is running, so a fresh,
        // shorter quest must not take the bot away and let it expire. It ends when the walker proposes nothing for it.
        step.committed = step.status == Status::Running;
        if step.status == Status::Failed || row.owner_ready {
            // Ready: the single objective is the end point; standing there turns the quest in (no command).
            return step;
        }
        // The interactions the server offers for this quest, by their action code (never abandon, action 1).
        let offered: Vec<u8> = q
            .actions
            .iter()
            .filter(|a| a.quest == row.handle && a.action == ACTION_CONTACT)
            .map(|a| a.target)
            .collect();
        for target in offered {
            if !self.throttled(def, ACTION_CONTACT, target, now) {
                step.acts.push(Act::QuestInteract {
                    def: def.clone(),
                    action: ACTION_CONTACT,
                    target,
                });
            }
        }
        step
    }
}
