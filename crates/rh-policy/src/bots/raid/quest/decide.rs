//! Quest decisions (P-RAID-Q3): the reward slot choice, the pick-up of a dropped quest spell and The Bargain. Two Banners and
//! Trade Up are P-RAID-Q3b's. Each decision is a `GoalKind::QuestDecide` goal on `Target::Quest { def, stage }`: stage 0
//! answers a pending slot choice (action 8), stage 1 picks a dropped spell up (action 9), stage 2 works The Bargain.
//!
//! The pure rules (`def_of_reward`, `spell_worth`, `slot_answer`, `pickup_wanted`, `stat_worth`, `bargain_trade`) are
//! public and unit tested; the executor reads the board each think and sends only what the frame offers (a Bargain trade is
//! copied from the frame's actions, never guessed). It draws nothing from `cx.rng`.
use crate::bots::raid::acts::Act;
use crate::bots::raid::exec::travel::{eta_minutes, move_intent, step_toward, straight_minutes};
use crate::bots::raid::goal::{Candidate, Cx, Executor, GoalKind, RaidGoal, Status, Step, Target};
use crate::bots::raid::percept::Me;
use crate::bots::raid::quest::SPELL_SLOTS;
use crate::bots::raid::values::Prices;
use rh_protocol::state::{WireQuestDrop, WireQuestPoint, WireQuestRow, WireQuestSpell};
use rh_sim::quests::board::catalog::{RewardKind, DEFINITIONS};
use rh_sim::{FactionId, Vec2, WeaponId};

/// The reward slot choice and the pick-up (`quests/board/attempts/choices.rs`, `quest_view/board.rs`).
const ACTION_SLOT_CHOICE: u8 = 8;
pub(super) const ACTION_PICKUP: u8 = 9;
/// The Bargain's trades: stacks (`give * 9 + receive`) and the spare weapon (`receive`).
const ACTION_STACKS: u8 = 5;
const ACTION_WEAPON: u8 = 7;
const BARGAIN: &str = "the_bargain";
/// Stacks one trade takes and gives (`TOP25.bargain_cost`, `bargain_stacks`: crate-private to `rh-sim`).
const COST_STACKS: f64 = 20.;
const GAIN_STACKS: f64 = 24.;
/// A Bargain stat index (0..9) to its HUD stat code in the build summary's mob rows.
const BARGAIN_TO_HUD: [u8; 9] = [2, 8, 5, 1, 6, 7, 0, 3, 4];
/// Quests whose turn-in point is a token, not the giver (`attempts.rs` `endpoint`): the view shows the token only before it is ready.
const TOKEN_QUESTS: [&str; 3] = ["letters", "tuber_eats", "even_skeletons_get_cold"];
/// Stages of the goal's target.
const STAGE_SLOT: u8 = 0;
const STAGE_PICKUP: u8 = 1;
const STAGE_BARGAIN: u8 = 2;
/// Walk to within this of a point (the server's reach is 64 u).
const STAND_U: f64 = 32.;
/// A send is not repeated before this long.
const RESEND_MS: f64 = 2_500.;
/// A Bargain with no improving trade, or refused at its giver, is left alone this long.
const BAN_MS: f64 = 30_000.;
/// Standing at a giver this long with the quest still refused bans it.
const REFUSED_MS: f64 = 1_500.;

/// The quest executor of `GoalKind::QuestDecide`.
#[derive(Default)]
pub struct Decide {
    /// When a send of (def, action, target) was last queued: (def, action, target, ms).
    sent: Vec<(String, u8, u8, f64)>,
    /// The last objective point seen while a quest was ready to turn in (a token quest's turn-in point): (def, point).
    turn_in: Vec<(String, Vec2)>,
    /// Quests not to propose until the time: (def, until ms).
    banned: Vec<(String, f64)>,
    /// Since when the owner has stood at the Bargain's giver unable to accept (ms).
    refused: Option<f64>,
    /// Since when the owner has stood where a decision is answered with nothing offered: (def, ms).
    stood: Option<(String, f64)>,
    /// `act` ran since the last `observe`: the seat's goal is this executor's. Without it the waits above start over.
    acted: bool,
}

fn dist(a: Vec2, b: Vec2) -> f64 {
    rh_sim::math::hypot(a.x - b.x, a.y - b.y)
}

fn point(p: WireQuestPoint) -> Vec2 {
    Vec2::new(p.x, p.y)
}

fn reward_kind(def: &str) -> Option<RewardKind> {
    DEFINITIONS
        .iter()
        .find(|d| d.id == def)
        .map(|d| d.reward.kind())
}

/// The quest a spell's wire reward id came from, and the share of that quest's table row the spell stands for (1 for a
/// single reward; one half for each of `lost_brother`'s two). `None` for an id no spell has.
pub fn def_of_reward(reward: u8) -> Option<(&'static str, f64)> {
    if reward == 21 || reward == 22 {
        return Some(("lost_brother", 0.5));
    }
    DEFINITIONS
        .iter()
        .find(|d| d.reward as u8 == reward && d.reward.kind() != RewardKind::Permanent)
        .map(|d| (d.id, 1.))
}

/// What a held or dropped spell is worth in camp-equivalents: `prices.reward_value(def)` times its share, halved while a
/// reusable spell has no charge left (it is recharging).
pub fn spell_worth(prices: &Prices, reward: u8, charges: u8) -> f64 {
    let Some((def, share)) = def_of_reward(reward) else {
        return 0.;
    };
    let worth = prices.reward_value(def) * share;
    if charges == 0 && reward_kind(def) == Some(RewardKind::Reusable) {
        worth * prices.recharging_share
    } else {
        worth
    }
}

fn is_reusable(reward: u8) -> bool {
    def_of_reward(reward).is_some_and(|(d, _)| reward_kind(d) == Some(RewardKind::Reusable))
}

/// The answer to a pending reward slot choice (action 8, target): 0 or 1 drops that slot's spell and equips the new
/// reward (an empty slot is simply filled); 2 leaves the new reward on the ground. `new_def` is the pending quest's def.
pub fn slot_answer(prices: &Prices, held: &[WireQuestSpell], new_def: &str) -> u8 {
    let mut slots = (0..SPELL_SLOTS).filter_map(|s| u8::try_from(s).ok());
    if let Some(free) = slots.find(|s| !held.iter().any(|h| h.slot == *s)) {
        return free;
    }
    let share = if new_def == "lost_brother" { 0.5 } else { 1. };
    let new_worth = prices.reward_value(new_def) * share;
    // The lowest worth goes; of equals the single-use spell, then the lower slot.
    let lowest = held.iter().min_by(|a, b| {
        spell_worth(prices, a.reward, a.charges)
            .total_cmp(&spell_worth(prices, b.reward, b.charges))
            .then(is_reusable(a.reward).cmp(&is_reusable(b.reward)))
            .then(a.slot.cmp(&b.slot))
    });
    match lowest {
        Some(h) if new_worth > spell_worth(prices, h.reward, h.charges) => h.slot,
        _ => 2,
    }
}

/// Whether to pick a dropped spell up (action 9): a slot is free (the server offers the pick-up only then) and the spell
/// is worth something.
pub fn pickup_wanted(prices: &Prices, held: &[WireQuestSpell], drop: &WireQuestDrop) -> bool {
    held.len() < SPELL_SLOTS && spell_worth(prices, drop.reward, drop.charges) > 0.
}

/// `stat_worth` with the prices of `prices` (the spare-weapon-free half of the Bargain's valuation).
pub fn stat_worth_at(prices: &Prices, stat: u8, me: &Me) -> f64 {
    let fit = prices.stat_fit;
    let weapon = me.weapons.get(usize::from(me.slot)).copied().flatten();
    let fit = match stat {
        6 => match weapon {
            Some(WeaponId::Sword | WeaponId::Axe | WeaponId::Bow) => fit.matched,
            None | Some(WeaponId::Fists) => fit.fists,
            Some(WeaponId::Wand) => fit.wand_attack,
            Some(WeaponId::Shield) => fit.mismatched,
        },
        3 => match weapon {
            Some(WeaponId::Wand) => fit.matched,
            None | Some(WeaponId::Fists) => fit.fists,
            _ => {
                if matches!(
                    me.faction,
                    Some(FactionId::Fire | FactionId::Sentenced | FactionId::Plague)
                ) {
                    0.
                } else {
                    fit.mismatched
                }
            }
        },
        4 | 5 if weapon == Some(WeaponId::Shield) => fit.matched,
        _ => 0.,
    };
    prices.neutral_stat + fit
}

/// What a stat of The Bargain (index 0..9: damage, move speed, max HP, ability power, armor, shield, attack damage,
/// attack speed, lifesteal) is worth per stack to this build, in the units of `Prices::chest` boosts (2.5 neutral).
pub fn stat_worth(stat: u8, me: &Me) -> f64 {
    stat_worth_at(&Prices::LOCAL, stat, me)
}

/// One offered trade of The Bargain, scored.
#[derive(Clone, Copy)]
struct Trade {
    /// The gain in camp-equivalents.
    gain: f64,
    /// Whether it pays with the spare weapon (then `give` is 0 and means nothing).
    weapon: bool,
    give: u8,
    receive: u8,
    /// The `(action, target)` to send, as the frame offered it.
    pair: (u8, u8),
}

impl Trade {
    /// The larger gain wins; equal gains go to the weapon trade, then the lower give, then the lower receive.
    fn beats(&self, other: &Self) -> bool {
        if (self.gain - other.gain).abs() > 1e-9 {
            return self.gain > other.gain;
        }
        (
            self.weapon,
            std::cmp::Reverse(self.give),
            std::cmp::Reverse(self.receive),
        ) > (
            other.weapon,
            std::cmp::Reverse(other.give),
            std::cmp::Reverse(other.receive),
        )
    }
}

/// The trade to send at the hag, `(action, target)` copied from `offered` (the frame's actions of The Bargain's row: 5
/// with give * 9 + receive, 7 with the stat), or `None` when no offered trade is an improvement.
pub fn bargain_trade(prices: &Prices, me: &Me, offered: &[(u8, u8)]) -> Option<(u8, u8)> {
    best_trade(prices, me, offered).map(|t| t.pair)
}

/// The gain, in camp-equivalents, of the trade `bargain_trade` chooses from `offered`.
pub fn bargain_gain(prices: &Prices, me: &Me, offered: &[(u8, u8)]) -> Option<f64> {
    best_trade(prices, me, offered).map(|t| t.gain)
}

fn best_trade(prices: &Prices, me: &Me, offered: &[(u8, u8)]) -> Option<Trade> {
    let unit = prices.stacks_per_camp * prices.neutral_stat;
    let mut best: Option<Trade> = None;
    for &(action, target) in offered {
        let (gain, weapon, give, receive) = match action {
            ACTION_STACKS if target < 81 && target / 9 != target % 9 => {
                let (give, receive) = (target / 9, target % 9);
                (
                    (GAIN_STACKS * stat_worth_at(prices, receive, me)
                        - COST_STACKS * stat_worth_at(prices, give, me))
                        / unit,
                    false,
                    give,
                    receive,
                )
            }
            ACTION_WEAPON if target < 9 => (
                GAIN_STACKS * stat_worth_at(prices, target, me) / unit - prices.spare_weapon,
                true,
                0,
                target,
            ),
            _ => continue,
        };
        if gain <= 1e-9 {
            continue;
        }
        let trade = Trade {
            gain,
            weapon,
            give,
            receive,
            pair: (action, target),
        };
        if best.is_none_or(|b| trade.beats(&b)) {
            best = Some(trade);
        }
    }
    best
}

/// The trades the build could make with what it has: 20 stacks of a stat to any other, the spare weapon to any stat.
fn possible_trades(cx: &Cx<'_>) -> Vec<(u8, u8)> {
    let mut trades = Vec::new();
    if let Some(summary) = cx.p.summary.as_ref() {
        for give in 0..9u8 {
            let code = BARGAIN_TO_HUD[usize::from(give)];
            let stacks: u64 = summary
                .mob_rows
                .iter()
                .filter(|r| r.stat == code)
                .map(|r| u64::from(r.stacks))
                .sum();
            if stacks as f64 >= COST_STACKS {
                trades.extend(
                    (0..9u8)
                        .filter(|j| *j != give)
                        .map(|j| (ACTION_STACKS, give * 9 + j)),
                );
            }
        }
    }
    if cx.p.me.weapons.iter().all(Option::is_some) {
        trades.extend((0..9u8).map(|i| (ACTION_WEAPON, i)));
    }
    trades
}

impl Decide {
    fn banned(&self, def: &str, now: f64) -> bool {
        self.banned
            .iter()
            .any(|(d, until)| d == def && *until > now)
    }

    /// The ban and stand key of a def's pick-up, apart from its slot answer's.
    fn pickup_key(def: &str) -> String {
        format!("pickup:{def}")
    }

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

    /// Remember where a ready quest ends (its single objective), for when the view no longer shows it.
    fn remember(&mut self, cx: &Cx<'_>) {
        let Some(q) = cx.p.quests.as_ref() else {
            return;
        };
        for row in q.rows.iter().filter(|r| {
            r.owner_ready
                || (r.owner_accepted
                    && !r.owner_completed
                    && TOKEN_QUESTS.contains(&r.def.as_str()))
        }) {
            let Some(o) = q.objectives.iter().find(|o| o.quest == row.handle) else {
                continue;
            };
            let at = point(o.at);
            match self.turn_in.iter_mut().find(|(d, _)| *d == row.def) {
                Some(entry) => entry.1 = at,
                None => self.turn_in.push((row.def.clone(), at)),
            }
        }
    }

    fn go(cx: &mut Cx<'_>, to: Vec2, why: &'static str) -> Step {
        if cx.p.dist(to) <= STAND_U {
            return Step::running(Vec::new(), "standing");
        }
        match step_toward(cx.paths, cx.p.me.at, to) {
            Some(next) => Step::committed(vec![move_intent(next, why)], why),
            None => Step::failed("no way there"),
        }
    }

    fn walk_minutes(cx: &mut Cx<'_>, to: Vec2) -> f64 {
        eta_minutes(cx.paths, cx.p.me.at, to).unwrap_or_else(|| straight_minutes(cx.p.me.at, to))
    }

    fn goal(def: &str, stage: u8) -> RaidGoal {
        RaidGoal::new(
            GoalKind::QuestDecide,
            Target::Quest {
                def: def.to_string(),
                stage,
            },
        )
    }

    fn bargain_row(cx: &Cx<'_>) -> Option<WireQuestRow> {
        cx.p.quests
            .as_ref()?
            .rows
            .iter()
            .find(|r| r.def == BARGAIN && r.state == 1 && !r.owner_completed)
            .cloned()
    }

    /// The nearest visible drop worth picking up whose spell came from `def`.
    fn wanted_drop(cx: &Cx<'_>, def: &str) -> Option<WireQuestDrop> {
        let q = cx.p.quests.as_ref()?;
        q.drops
            .iter()
            .filter(|d| {
                def_of_reward(d.reward).is_some_and(|(k, _)| k == def)
                    && pickup_wanted(cx.prices, &q.spells, d)
            })
            .min_by(|a, b| dist(point(a.at), cx.p.me.at).total_cmp(&dist(point(b.at), cx.p.me.at)))
            .copied()
    }

    fn act_slot(&mut self, def: &str, cx: &mut Cx<'_>) -> Step {
        let now = cx.p.now;
        let Some(q) = cx.p.quests.as_ref() else {
            return Step::failed("no quest board");
        };
        let Some(row) = q.rows.iter().find(|r| r.def == def) else {
            return Step::done("the choice is resolved");
        };
        if !q.pending.contains(&row.handle) {
            return Step::done("the choice is resolved");
        }
        let offered: Vec<u8> = q
            .actions
            .iter()
            .filter(|a| a.quest == row.handle && a.action == ACTION_SLOT_CHOICE)
            .map(|a| a.target)
            .collect();
        if offered.is_empty() {
            let to = self
                .turn_in
                .iter()
                .find(|(d, _)| d == def)
                .map_or_else(|| point(row.giver), |(_, at)| *at);
            if cx.p.dist(to) > STAND_U {
                self.stood = None;
                return Self::go(cx, to, "to the turn-in point");
            }
            return self.wait_or_give_up(def, now, "nothing offered at the turn-in point");
        }
        self.stood = None;
        let want = slot_answer(cx.prices, &q.spells, def);
        let answer = if offered.contains(&want) { want } else { 2 };
        let mut step = Step::committed(Vec::new(), "answering the slot choice");
        if offered.contains(&answer) && !self.throttled(def, ACTION_SLOT_CHOICE, answer, now) {
            step.acts.push(Act::QuestInteract {
                def: def.to_string(),
                action: ACTION_SLOT_CHOICE,
                target: answer,
            });
        }
        step
    }

    /// Standing where nothing is offered: running until `REFUSED_MS`, then the def is left alone for `BAN_MS`.
    fn wait_or_give_up(&mut self, def: &str, now: f64, why: &'static str) -> Step {
        let since = match &self.stood {
            Some((d, at)) if d == def => *at,
            _ => {
                self.stood = Some((def.to_string(), now));
                now
            }
        };
        if now - since >= REFUSED_MS {
            self.banned.push((def.to_string(), now + BAN_MS));
            self.stood = None;
            return Step::failed(why);
        }
        Step::running(Vec::new(), "standing")
    }

    fn act_pickup(&mut self, def: &str, cx: &mut Cx<'_>) -> Step {
        let now = cx.p.now;
        let Some(drop) = Self::wanted_drop(cx, def) else {
            return Step::done("nothing to pick up");
        };
        let reach = cx.p.quests.as_ref().is_some_and(|q| {
            q.actions
                .iter()
                .any(|a| a.action == ACTION_PICKUP && a.target == drop.index)
        });
        if reach {
            let mut step = Step::running(Vec::new(), "picking the spell up");
            if !self.throttled(def, ACTION_PICKUP, drop.index, now) {
                step.acts.push(Act::QuestPickup {
                    index: drop.index,
                    reward: drop.reward,
                });
            }
            return step;
        }
        if cx.p.dist(point(drop.at)) <= STAND_U {
            // The sim never offers it from here (a wall, or no room): a bounded stand, then the def is left alone.
            return self.wait_or_give_up(&Self::pickup_key(def), now, "the pick-up is not offered");
        }
        Self::go(cx, point(drop.at), "to the dropped spell")
    }

    fn act_bargain(&mut self, cx: &mut Cx<'_>) -> Step {
        let now = cx.p.now;
        let Some(row) = Self::bargain_row(cx) else {
            return Step::done("the bargain is done");
        };
        if self.banned(BARGAIN, now) {
            return Step::failed("the bargain is left alone");
        }
        let giver = point(row.giver);
        if !row.owner_accepted {
            if row.can_accept {
                self.refused = None;
                let mut step = Step::running(Vec::new(), "accepting");
                if !self.throttled(BARGAIN, 255, 0, now) {
                    step.acts.push(Act::QuestAccept {
                        def: BARGAIN.to_string(),
                    });
                }
                return step;
            }
            if cx.p.dist(giver) > STAND_U {
                self.refused = None;
                return Self::go(cx, giver, "to the giver");
            }
            let since = *self.refused.get_or_insert(now);
            if now - since >= REFUSED_MS {
                self.banned.push((BARGAIN.to_string(), now + BAN_MS));
                self.refused = None;
                return Step::failed("the giver refuses");
            }
            return Step::running(Vec::new(), "waiting at the giver");
        }
        let Some(q) = cx.p.quests.as_ref() else {
            return Step::failed("no quest board");
        };
        let offered: Vec<(u8, u8)> = q
            .actions
            .iter()
            .filter(|a| {
                a.quest == row.handle && (a.action == ACTION_STACKS || a.action == ACTION_WEAPON)
            })
            .map(|a| (a.action, a.target))
            .collect();
        if !offered.is_empty() {
            self.stood = None;
            return match bargain_trade(cx.prices, &cx.p.me, &offered) {
                Some((action, target)) => {
                    let mut step = Step::committed(Vec::new(), "trading at the hag");
                    if !self.throttled(BARGAIN, action, target, now) {
                        step.acts.push(Act::QuestInteract {
                            def: BARGAIN.to_string(),
                            action,
                            target,
                        });
                    }
                    step
                }
                None => {
                    self.banned.push((BARGAIN.to_string(), now + BAN_MS));
                    Step::failed("no improving trade")
                }
            };
        }
        let Some(hag) = q
            .objectives
            .iter()
            .filter(|o| o.quest == row.handle)
            .map(|o| point(o.at))
            .min_by(|a, b| dist(*a, cx.p.me.at).total_cmp(&dist(*b, cx.p.me.at)))
        else {
            return Step::failed("no objective in the view");
        };
        if cx.p.dist(hag) > STAND_U {
            self.stood = None;
            return Self::go(cx, hag, "to the hag");
        }
        self.wait_or_give_up(BARGAIN, now, "no trade offered at the hag")
    }
}

impl Executor for Decide {
    fn kinds(&self) -> &'static [GoalKind] {
        &[GoalKind::QuestDecide]
    }
    fn reset(&mut self) {
        *self = Self::default();
    }
    fn observe(&mut self, cx: &mut Cx<'_>) {
        // Another goal had the seat since the last think: a stand's clock does not run while it is away.
        if !std::mem::take(&mut self.acted) {
            self.stood = None;
            self.refused = None;
        }
        self.remember(cx);
    }
    fn propose(&mut self, cx: &mut Cx<'_>, out: &mut Vec<Candidate>) {
        let now = cx.p.now;
        self.banned.retain(|(_, until)| *until > now);
        self.remember(cx);
        if cx.p.quests.is_none()
            || !cx.p.active
            || cx.p.arena.is_some_and(|a| a.gates_locked)
            || !cx.p.me.alive
        {
            return;
        }
        let (pending, drops): (Vec<(String, Vec2, bool)>, Vec<WireQuestDrop>) =
            cx.p.quests.as_ref().map_or_else(
                || (Vec::new(), Vec::new()),
                |q| {
                    (
                        q.rows
                            .iter()
                            .filter(|r| q.pending.contains(&r.handle))
                            .map(|r| {
                                let offered = q
                                    .actions
                                    .iter()
                                    .any(|a| a.quest == r.handle && a.action == ACTION_SLOT_CHOICE);
                                (r.def.clone(), point(r.giver), offered)
                            })
                            .collect(),
                        q.drops.clone(),
                    )
                },
            );
        let spells =
            cx.p.quests
                .as_ref()
                .map_or_else(Vec::new, |q| q.spells.clone());
        // A pending slot choice: answered first (it costs nothing and holds a reward), but a seat far from its turn-in
        // point is priced by the walk, so a better use of the minutes (Sustain's leave) can rank first.
        for (def, giver, offered) in pending {
            let value = cx.prices.reward_value(&def);
            if value > 0. && !self.banned(&def, now) {
                let to = self
                    .turn_in
                    .iter()
                    .find(|(d, _)| *d == def)
                    .map_or(giver, |(_, at)| *at);
                let walk = if offered || cx.p.dist(to) <= STAND_U {
                    0.
                } else {
                    Self::walk_minutes(cx, to)
                };
                out.push(Candidate {
                    goal: Self::goal(&def, STAGE_SLOT),
                    value,
                    success: 1.,
                    death: 0.,
                    loss: cx.prices.death_loss,
                    minutes: walk + cx.prices.answer_minutes,
                });
            }
        }
        // A dropped spell worth taking, one candidate per quest it came from (its nearest drop).
        let mut seen: Vec<&'static str> = Vec::new();
        for d in &drops {
            let Some((def, _)) = def_of_reward(d.reward) else {
                continue;
            };
            if seen.contains(&def)
                || !pickup_wanted(cx.prices, &spells, d)
                || self.banned(&Self::pickup_key(def), now)
            {
                continue;
            }
            seen.push(def);
            let Some(near) = Self::wanted_drop(cx, def) else {
                continue;
            };
            let minutes = Self::walk_minutes(cx, point(near.at));
            out.push(Candidate {
                goal: Self::goal(def, STAGE_PICKUP),
                value: spell_worth(cx.prices, near.reward, near.charges),
                success: 1.,
                death: cx.prices.death_floor,
                loss: cx.prices.death_loss,
                minutes,
            });
        }
        // The Bargain, while a trade can improve the build.
        if cx.prices.reward_value(BARGAIN) <= 0. || self.banned(BARGAIN, now) {
            return;
        }
        let Some(row) = Self::bargain_row(cx) else {
            return;
        };
        let Some(value) = bargain_gain(cx.prices, &cx.p.me, &possible_trades(cx)) else {
            return;
        };
        let target = if row.owner_accepted {
            cx.p.quests.as_ref().and_then(|q| {
                q.objectives
                    .iter()
                    .find(|o| o.quest == row.handle)
                    .map(|o| point(o.at))
            })
        } else {
            Some(point(row.giver))
        };
        let Some(target) = target else {
            return;
        };
        let minutes = Self::walk_minutes(cx, target)
            + if row.owner_accepted {
                0.
            } else {
                cx.prices.bargain_leg_minutes
            };
        out.push(Candidate {
            goal: Self::goal(BARGAIN, STAGE_BARGAIN),
            value,
            success: 1.,
            death: cx.prices.death_floor,
            loss: cx.prices.death_loss,
            minutes,
        });
    }
    fn act(&mut self, goal: &RaidGoal, cx: &mut Cx<'_>) -> Step {
        let Target::Quest { def, stage } = &goal.target else {
            return Step::failed("quest decision without a quest");
        };
        if cx.p.arena.is_some_and(|a| a.gates_locked) {
            return Step::failed("the board is locked");
        }
        self.acted = true;
        self.remember(cx);
        let step = match *stage {
            STAGE_SLOT => self.act_slot(def, cx),
            STAGE_PICKUP => self.act_pickup(def, cx),
            _ => self.act_bargain(cx),
        };
        if step.status == Status::Done {
            self.sent.retain(|s| s.0 != *def);
        }
        step
    }
}
