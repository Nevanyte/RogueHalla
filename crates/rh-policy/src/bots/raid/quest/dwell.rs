//! Dwell quests (P-RAID-Q2): Hold the Fort and Stay A While, as `GoalKind::QuestDwell`, `Target::Quest { def, stage: 0 }`.
//!
//! * **Hold the Fort** (`hold_the_fort`): after accepting, the row's objective is the guard house; stand in it, alone, for
//!   45 s, then walk back to the giver. The server's clock is the row's `owner_progress` (whole seconds): this module keeps
//!   no clock of its own for the dwell. It counts only while nobody else stands inside, and leaving resets it to zero.
//! * **Stay A While** (`stay_a_while`): at the monk (the giver) send action 6 ("Meditate"), then neither move, attack nor hold
//!   a button for 30 s; any damage breaks it, and the frame offering action 6 again is the signal to send it again. The
//!   quest turns itself in at the monk when the time is full.
//!
//! **The leave rule (Hold the Fort).** A fair seat cannot see a contestant it has no line to; the one reliable sign is the
//! row's progress staying flat while the seat stands inside. A contest only *pauses* the clock, so a passer-by costs a few
//! seconds and nothing else, and leaving costs everything (up to 40 s). The rule: progress flat for 3 s with another player
//! in sight inside the fort, or flat for 6 s regardless (the house may be held by someone out of sight, or the seat may
//! stand outside its walls: after 3 s flat the seat steps onto the token once), releases the goal and bans the quest for
//! 60 s. Neither shorter (a passer-by would cost a whole hold) nor unbounded (a held fort would pin the bot for ever, and
//! the lab would count a stuck seat as a quest holder). Meditation: two breaks (progress falls from 3 s or more, or damage)
//! ban it for 60 s. Low hp under fire (below a third, hit this frame) also releases either quest.
//!
//! **Commitment.** An accepted dwell is `Step::committed` while it runs: only a `Sustain` goal pre-empts it (a drink is not
//! a weapon action and does not break a meditation: the sim breaks it on movement, damage, a status block, or
//! `weapon.action`/`held`, none of which a potion touches). Neither quest has a timer: the bounds are the coliseum lock,
//! death, and the rule above. It never abandons (action 1) and never fights a contestant.
//!
//! **Stillness.** The goal's step during a meditation carries no intent. The send waits until `still_since` shows the seat
//! has stood still (the click to the giver may still be walking it in), so the position the server records is the final one.
use crate::bots::planning::{matchup_share, zone_faction};
use crate::bots::raid::acts::Act;
use crate::bots::raid::exec::farm::{death_chance, members_left};
use crate::bots::raid::exec::travel::{eta_minutes, move_intent, step_toward, straight_minutes};
use crate::bots::raid::goal::{Candidate, Cx, Executor, GoalKind, RaidGoal, Step, Target};
use crate::bots::raid::percept::CAMP_JOIN_RADIUS;
use crate::bots::raid::quest::{spell_slots_taken, SPELL_SLOTS};
use rh_protocol::state::{WireQuestRow, FIELD_KIND_QUEST_FORT};
use rh_sim::quests::board::catalog::{RewardKind, DEFINITIONS};
use rh_sim::{FactionId, Vec2};

const HOLD: &str = "hold_the_fort";
const STAY: &str = "stay_a_while";
/// Seconds of dwell each quest needs (`TOP25.fort_ms`, `meditate_ms`).
const HOLD_S: f64 = 45.;
const STAY_S: f64 = 30.;
/// The meditation action (`choices.rs`), target 0.
const ACTION_MEDITATE: u8 = 6;
/// A pseudo action number for the accept send in the resend throttle.
const ACCEPT_KEY: u8 = 255;
/// Walk to within this of a giver or the fort token (the server's contact range is 64 u).
const STAND_U: f64 = 32.;
/// Within this of the token the seat counts as inside the house (the server's `fort_range` is 72 u).
const INSIDE_U: f64 = 48.;
/// The fort's range, the proxy for "in the fort" for other players.
const FORT_RANGE: f64 = 72.;
/// Where the fort lies from the giver before accepting (measured on every seed that deals it), for the price only.
const FORT_WALK_U: f64 = 500.;
/// Flat progress inside the fort for this long, with a player in sight inside it, releases the hold...
const FLAT_SEEN_MS: f64 = 3_000.;
/// ...flat for this long regardless; at `FLAT_SEEN_MS` the seat first steps onto the token.
const FLAT_LEAVE_MS: f64 = 6_000.;
/// A quest left for these reasons is not taken again for this long.
const BAN_MS: f64 = 60_000.;
/// A send is not repeated before this long (the act queue pauses and the frame's lag).
const RESEND_MS: f64 = 2_500.;
/// The seat must have stood still this long before action 6 is sent.
const STILL_MS: f64 = 500.;
/// Standing at a refusing giver this long bans the quest...
const REFUSED_MS: f64 = 1_500.;
/// ...for this long.
const REFUSED_BAN_MS: f64 = 20_000.;
/// Players this near the monk are close enough to hit the seat while it meditates.
const MONK_DANGER_U: f64 = 400.;
/// The chance floor of a walk to die (the walker's).
const DEATH_FLOOR: f64 = 0.02;
/// Slack (time left over time needed) from which the dwell is certain to finish.
const SAFE_SLACK: f64 = 1.2;
/// Release a running dwell when hit below this hp share.
const LOW_HP: f64 = 1. / 3.;

/// What the seat has seen of the dwell clock of the quest it works on.
struct Track {
    def: String,
    progress: u32,
    /// Since when `progress` has not risen.
    flat_since: f64,
    /// The seat already stepped onto the token for this flat spell.
    nudged: bool,
}

#[derive(Default)]
pub struct Dwell {
    shares: Vec<((FactionId, FactionId), f64)>,
    /// Quests not to propose or work on until the time: (def, until ms).
    banned: Vec<(String, f64)>,
    /// When a send of (def, action) was last queued: (def, action, ms).
    sent: Vec<(String, u8, f64)>,
    /// Since when the owner has stood at this quest's giver unable to accept: (def, ms).
    refused: Option<(String, f64)>,
    track: Option<Track>,
    /// Meditation breaks seen on the quest in hand.
    breaks: u8,
}

fn dist(a: Vec2, b: Vec2) -> f64 {
    rh_sim::math::hypot(a.x - b.x, a.y - b.y)
}

fn at(p: rh_protocol::state::WireQuestPoint) -> Vec2 {
    Vec2::new(p.x, p.y)
}

/// The fort's token in this frame's view, once the quest is accepted.
fn token(cx: &Cx<'_>, row: &WireQuestRow) -> Option<Vec2> {
    cx.p.quests
        .as_ref()?
        .objectives
        .iter()
        .find(|o| o.quest == row.handle)
        .map(|o| at(o.at))
}

fn slot_choice(cx: &Cx<'_>, row: &WireQuestRow) -> bool {
    cx.p.quests
        .as_ref()
        .is_some_and(|q| q.pending.contains(&row.handle))
}

/// Players in sight, alive, other than the seat, within `radius` of `to`.
fn others_near(cx: &Cx<'_>, to: Vec2, radius: f64) -> usize {
    cx.p.players
        .iter()
        .filter(|o| o.alive && o.key != cx.p.self_key && dist(o.at, to) <= radius)
        .count()
}

/// Quest fort marks (other holders) in sight, own mark excluded (it sits at the seat).
fn fort_marks(cx: &Cx<'_>) -> usize {
    cx.p.fields
        .iter()
        .filter(|f| {
            f.kind == FIELD_KIND_QUEST_FORT
                && dist(Vec2::new(f64::from(f.x), f64::from(f.y)), cx.p.me.at) > 8.
        })
        .count()
}

/// Walk toward `to` unless already within `STAND_U` of it.
fn go(cx: &mut Cx<'_>, to: Vec2, why: &'static str, committed: bool) -> Step {
    let mut step = if cx.p.dist(to) <= STAND_U {
        Step::running(Vec::new(), "standing")
    } else {
        match step_toward(cx.paths, cx.p.me.at, to) {
            Some(next) => Step::running(vec![move_intent(next, why)], why),
            None => return Step::failed("no way there"),
        }
    };
    step.committed = committed;
    step
}

impl Dwell {
    fn banned(&self, def: &str, now: f64) -> bool {
        self.banned
            .iter()
            .any(|(d, until)| d == def && *until > now)
    }

    fn ban(&mut self, def: &str, now: f64, ms: f64) {
        self.banned.push((def.to_string(), now + ms));
        self.track = None;
        self.breaks = 0;
    }

    /// True when (def, action) was queued within `RESEND_MS`; otherwise records it as queued now.
    fn throttled(&mut self, def: &str, action: u8, now: f64) -> bool {
        self.sent.retain(|s| now - s.2 < RESEND_MS);
        if self.sent.iter().any(|s| s.0 == def && s.1 == action) {
            return true;
        }
        self.sent.push((def.to_string(), action, now));
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

    /// The chance to die to the living camps whose centre lies within `CAMP_JOIN_RADIUS` of `point`, floored.
    fn death_near(&mut self, cx: &Cx<'_>, point: Vec2) -> f64 {
        let Some(me) = cx.p.me.faction else {
            return DEATH_FLOOR;
        };
        let hp = cx.p.me.hp_share();
        let mut survive = 1.;
        for camp in &cx.p.camps {
            if camp.alive == 0 || dist(point, camp.at) > CAMP_JOIN_RADIUS {
                continue;
            }
            let share = self.share(me, zone_faction(camp.zone));
            survive *= 1. - death_chance(hp, share, members_left(cx.p, camp), &cx.prices.death);
        }
        (1. - survive).max(DEATH_FLOOR)
    }

    fn candidate(&mut self, cx: &mut Cx<'_>, row: &WireQuestRow) -> Option<Candidate> {
        let def = row.def.as_str();
        let value = cx.prices.reward_value(def);
        if value <= 0. || row.state != 1 || row.owner_completed || slot_choice(cx, row) {
            return None;
        }
        let book = DEFINITIONS.iter().find(|d| d.id == def)?;
        if !row.owner_accepted
            && book.reward.kind() != RewardKind::Permanent
            && spell_slots_taken(cx) >= SPELL_SLOTS
        {
            return None;
        }
        let giver = at(row.giver);
        let me = cx.p.me.at;
        let dwell_s = if def == HOLD { HOLD_S } else { STAY_S };
        let left_s = (dwell_s - f64::from(row.owner_progress)).max(0.);
        // The point the dwell happens at, the minutes it takes (the walk, and for the hold the way back), and what can cut it.
        let (minutes, spot, success) = if def == HOLD {
            let fort = token(cx, row).filter(|_| row.owner_accepted);
            let (to_fort, back) = match fort {
                Some(f) => (
                    eta_minutes(cx.paths, me, f).unwrap_or_else(|| straight_minutes(me, f)),
                    straight_minutes(f, giver),
                ),
                None => {
                    let far = Vec2::new(giver.x + FORT_WALK_U, giver.y);
                    (
                        straight_minutes(me, giver) + straight_minutes(giver, far),
                        straight_minutes(far, giver),
                    )
                }
            };
            let minutes = if row.owner_ready {
                straight_minutes(me, giver)
            } else {
                to_fort + back + left_s / 60.
            };
            let seen = fort.map_or(0, |f| others_near(cx, f, FORT_RANGE * 4.)) + fort_marks(cx);
            (minutes, fort.unwrap_or(giver), 0.8_f64.powi(seen as i32))
        } else {
            let seen = others_near(cx, giver, MONK_DANGER_U);
            let broken = 0.7_f64.powi(i32::from(self.breaks));
            (
                straight_minutes(me, giver) + left_s / 60.,
                giver,
                0.85_f64.powi(seen as i32) * broken,
            )
        };
        let time_left_s =
            cx.p.arena
                .and_then(|a| a.patience_ends_at)
                .map_or(f64::INFINITY, |t| (t - cx.p.now) / 1000.);
        let success = if time_left_s.is_finite() {
            let slack = time_left_s / (minutes * 60.).max(1.);
            success * ((slack - 1.) / (SAFE_SLACK - 1.)).clamp(0., 1.)
        } else {
            success
        };
        if success <= 0. {
            return None;
        }
        Some(Candidate {
            goal: RaidGoal::new(
                GoalKind::QuestDwell,
                Target::Quest {
                    def: def.to_string(),
                    stage: 0,
                },
            ),
            value,
            success,
            death: self.death_near(cx, spot),
            loss: cx.prices.death_loss,
            minutes,
        })
    }

    /// The hold, accepted: reach the token, stand, watch the clock; once ready, go back to the giver.
    fn hold(&mut self, cx: &mut Cx<'_>, row: &WireQuestRow) -> Step {
        let now = cx.p.now;
        let giver = at(row.giver);
        if row.owner_ready {
            self.track = None;
            return go(cx, giver, "back to the giver", true);
        }
        let Some(fort) = token(cx, row) else {
            return Step::failed("no fort in the view");
        };
        let d = cx.p.dist(fort);
        if d > INSIDE_U {
            self.track = None;
            return go(cx, fort, "to the fort", true);
        }
        let track = match self.track.take() {
            Some(mut t) if t.def == row.def && row.owner_progress >= t.progress => {
                if row.owner_progress > t.progress {
                    t.progress = row.owner_progress;
                    t.flat_since = now;
                    t.nudged = false;
                }
                t
            }
            _ => Track {
                def: row.def.clone(),
                progress: row.owner_progress,
                flat_since: now,
                nudged: false,
            },
        };
        let flat = now - track.flat_since;
        let seen = others_near(cx, fort, FORT_RANGE) > 0;
        if flat >= FLAT_LEAVE_MS || (flat >= FLAT_SEEN_MS && seen) {
            self.ban(&row.def, now, BAN_MS);
            return Step::failed("the fort is held by someone else");
        }
        let nudge = flat >= FLAT_SEEN_MS && !track.nudged && d > 8.;
        self.track = Some(Track {
            nudged: track.nudged || nudge,
            ..track
        });
        if nudge {
            // Maybe outside the walls: step onto the token once.
            return Step::committed(vec![move_intent(fort, "onto the fort token")], "nudging");
        }
        Step::committed(Vec::new(), "holding the fort")
    }

    /// The meditation, accepted: walk to the monk, stop, send action 6 when offered, then do nothing.
    fn meditate(&mut self, cx: &mut Cx<'_>, row: &WireQuestRow) -> Step {
        let now = cx.p.now;
        let giver = at(row.giver);
        if cx.p.dist(giver) > STAND_U {
            return go(cx, giver, "to the monk", true);
        }
        // A break: progress fell from three seconds or more, or damage landed while it ran.
        if let Some(t) = &self.track {
            let broke = t.def == row.def
                && ((t.progress >= 3 && row.owner_progress < t.progress)
                    || (cx.p.hit_at.is_some() && t.progress >= 1));
            if broke {
                self.breaks += 1;
                if self.breaks >= 2 {
                    self.ban(&row.def, now, BAN_MS);
                    return Step::failed("two breaks");
                }
            }
        }
        self.track = Some(Track {
            def: row.def.clone(),
            progress: row.owner_progress,
            flat_since: now,
            nudged: false,
        });
        let mut step = Step::committed(Vec::new(), "meditating");
        let offered = cx.p.quests.as_ref().is_some_and(|q| {
            q.actions
                .iter()
                .any(|a| a.quest == row.handle && a.action == ACTION_MEDITATE && a.target == 0)
        });
        if offered
            && now - cx.still_since >= STILL_MS
            && !self.throttled(&row.def, ACTION_MEDITATE, now)
        {
            step.acts.push(Act::QuestInteract {
                def: row.def.clone(),
                action: ACTION_MEDITATE,
                target: 0,
            });
            step.why = "sending meditate";
        }
        step
    }
}

impl Executor for Dwell {
    fn kinds(&self) -> &'static [GoalKind] {
        &[GoalKind::QuestDwell]
    }
    fn reset(&mut self) {
        *self = Self::default();
    }
    fn propose(&mut self, cx: &mut Cx<'_>, out: &mut Vec<Candidate>) {
        let now = cx.p.now;
        self.banned.retain(|(_, until)| *until > now);
        let Some(q) = cx.p.quests.as_ref() else {
            return;
        };
        if !cx.p.active || cx.p.arena.is_some_and(|a| a.gates_locked) {
            return;
        }
        let rows: Vec<WireQuestRow> = q
            .rows
            .iter()
            .filter(|r| (r.def == HOLD || r.def == STAY) && !self.banned(&r.def, now))
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
            return Step::failed("dwell without a quest");
        };
        let now = cx.p.now;
        let Some(q) = cx.p.quests.as_ref() else {
            return Step::failed("no quest board");
        };
        if cx.p.arena.is_some_and(|a| a.gates_locked) {
            return Step::failed("the board is locked");
        }
        let Some(row) = q.rows.iter().find(|r| r.def == *def).cloned() else {
            self.track = None;
            self.breaks = 0;
            return Step::done("quest turned in");
        };
        if self.banned(def, now) || slot_choice(cx, &row) {
            return Step::failed("quest refused or waiting for a choice");
        }
        let giver = at(row.giver);
        if row.owner_accepted || row.can_accept || cx.p.dist(giver) > STAND_U {
            self.refused = None;
        }
        if !row.owner_accepted {
            if row.can_accept {
                let mut step = Step::running(Vec::new(), "accepting");
                if !self.throttled(def, ACCEPT_KEY, now) {
                    step.acts.push(Act::QuestAccept { def: def.clone() });
                }
                return step;
            }
            if cx.p.dist(giver) > STAND_U {
                return go(cx, giver, "to the giver", false);
            }
            let since = match &self.refused {
                Some((d, t)) if d == def => *t,
                _ => {
                    self.refused = Some((def.clone(), now));
                    now
                }
            };
            if now - since >= REFUSED_MS {
                self.ban(def, now, REFUSED_BAN_MS);
                self.refused = None;
                return Step::failed("the giver refuses");
            }
            return Step::running(Vec::new(), "waiting at the giver");
        }
        if cx.p.me.hp_share() < LOW_HP && cx.p.hit_at.is_some() {
            self.ban(def, now, BAN_MS);
            return Step::failed("hurt under fire");
        }
        if *def == HOLD {
            self.hold(cx, &row)
        } else {
            self.meditate(cx, &row)
        }
    }
}
