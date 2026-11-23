//! Sustain (P-RAID-C): drink a potion, leave a fight the death model says is lost, heal up on purpose. Prices are
//! `Prices::sustain` (`values.rs`); with `sustain.on` false the executor proposes nothing, is never urgent and never
//! rides along. Nothing here draws from `cx.rng`.
//!
//! Three parts:
//! - **Drink** (`ride_along`): an `Act::UseConsumable` while hp is below `drink_below`, a potion is held and no heal runs.
//! - **Leave** (`Sustain` goal on `Target::Camp`): when the farm's own death model (`farm::death_chance`, from the hp
//!   held with potions counted and the members left) gives `disengage_death` or more, a candidate worth `leave_value`
//!   takes over from the committed fight, walks away from the nearest member and then stands still until `ready_hp`,
//!   so the farm does not walk back into the same fight (a camp left behind heals to full once its members get home).
//! - **Heal up** (`Sustain` goal on `Target::None`): between camps, hp below `ready_hp` is a candidate worth
//!   `rest_value` per share, over the minutes the sim's regeneration needs; a camp worth taking beats it.
use crate::bots::kite::away_point;
use crate::bots::planning::{matchup_share, zone_faction};
use crate::bots::raid::exec::farm::{death_chance, FIGHT_U};
use crate::bots::raid::exec::travel::move_intent;
use crate::bots::raid::goal::{Candidate, Cx, Executor, GoalKind, RaidGoal, Step, Target};
use crate::bots::raid::percept::{MobClass, MobSeen};
use crate::bots::types::PRIORITY_COMBAT;
use rh_sim::{FactionId, Vec2};

/// One step of a retreat (units): far enough that the walk goes somewhere, short enough to re-aim every think.
const STEP_U: f64 = 192.;
/// A bow reaches this far past its listed range once the shot is in flight (units).
const BOW_MARGIN_U: f64 = 60.;
/// Turns from straight away (radians) a retreat tries, in order, when the way directly away is blocked: the sides, then
/// back past the threat (a wall behind the bot is passed along, not stood against).
const SIDESTEPS: [f64; 5] = [0., 1.2, -1.2, 2.4, -2.4];
/// A cornered bot (no clear heading) does not try to leave again for this long (ms).
const CORNERED_MS: f64 = 3_000.;

/// The Sustain goal's executor: its edges and memory between thinks.
#[derive(Default)]
pub struct Sustain {
    shares: Vec<((FactionId, FactionId), f64)>,
    /// The camp being left, from the goal's adoption until the bot is healed (or cornered).
    leaving: Option<u32>,
    /// `urgent` already announced these (edges, not levels).
    was_low: bool,
    was_lost: bool,
    /// No leaving before this (server ms): the last attempt found no way out.
    cornered_until: f64,
    /// The fight so far (`track`): hp lost and member hp destroyed, the real exchange rate.
    seen: Seen,
    /// Server ms of the last frame the bot lost hp (or was hit), and the hp share it held the frame before.
    hurt_at: Option<f64>,
    prev_hp: f64,
    /// Potions held the frame before (large, small) and the shares of the heals still running, newest last.
    prev_stock: (u8, u8),
    hots: Vec<f64>,
}

/// What the current fight has cost and bought (units: shares of the bot's max hp, members' worth of hp).
#[derive(Default)]
struct Seen {
    camp: Option<u32>,
    hp: f64,
    left: f64,
    lost: f64,
    dealt: f64,
    /// Server ms of the last frame a fight was on.
    at: f64,
}

/// Quiet this long (ms) ends a fight's tally.
const TALLY_QUIET_MS: f64 = 5_000.;
/// The tally speaks once it holds this much destroyed member hp (members' worth) and this much lost hp (share).
const TALLY_MIN_DEALT: f64 = 0.5;
const TALLY_MIN_LOST: f64 = 0.1;

/// How far from the bot `m` is a threat: melee reach, or its projectile range plus a margin.
fn threat_u(m: &MobSeen) -> f64 {
    match m.weapon.map(|w| rh_sim::content::weapon(w).shape) {
        Some(rh_sim::content::Shape::Projectile { range, .. }) => {
            (range + BOW_MARGIN_U).max(FIGHT_U)
        }
        _ => FIGHT_U,
    }
}

/// A fight the death model prices.
struct Fight {
    camp: u32,
    lost: bool,
}

impl Sustain {
    fn share(&mut self, me: FactionId, region: FactionId) -> f64 {
        if let Some((_, s)) = self.shares.iter().find(|(k, _)| *k == (me, region)) {
            return *s;
        }
        let s = matchup_share(me, region, false);
        self.shares.push(((me, region), s));
        s
    }

    /// Hp share held, plus the potions held, plus what the heals already running will still give back (a drunk potion
    /// leaves the stock at once while the sim returns its share over `heal_seconds`).
    fn hp_held(&self, cx: &Cx<'_>) -> f64 {
        let s = &cx.prices.sustain;
        let me = &cx.p.me;
        let mut held = me.hp_share() + s.potion_hp(me.potion_large, me.potion_small);
        if me.potions_healing > 0 {
            if let Some(until) = me.healing_until {
                let to_come = ((until - cx.p.now) / (s.heal_seconds * 1_000.)).clamp(0., 1.);
                let n = usize::from(me.potions_healing).min(self.hots.len());
                held += self.hots[self.hots.len() - n..].iter().sum::<f64>() * to_come;
            }
        }
        held
    }

    /// Keep the hurt clock and the shares of the heals running (a stock that fell by one is a drink of that size).
    fn watch(&mut self, cx: &Cx<'_>) {
        let me = &cx.p.me;
        let s = &cx.prices.sustain;
        if me.hp_share() < self.prev_hp - 1e-9 || cx.p.hit_at.is_some() {
            self.hurt_at = Some(cx.p.now);
        }
        self.prev_hp = me.hp_share();
        if me.potion_large < self.prev_stock.0 {
            self.hots.push(s.large_share);
        } else if me.potion_small < self.prev_stock.1 {
            self.hots.push(s.small_share);
        }
        self.prev_stock = (me.potion_large, me.potion_small);
        if me.potions_healing == 0 {
            self.hots.clear();
        } else {
            let keep = usize::from(me.potions_healing);
            if self.hots.len() > keep {
                self.hots.drain(..self.hots.len() - keep);
            }
        }
    }

    /// The bot is in a fight rather than near one: it lost hp lately, or a member stands within reach on the camp it
    /// works (a camp passed at a distance, or the bow range of one, is not a fight).
    fn in_fight(cx: &Cx<'_>, hurt_at: Option<f64>) -> bool {
        let hurt = hurt_at.is_some_and(|t| cx.p.now - t <= cx.prices.sustain.hurt_seconds * 1_000.);
        let works_camp = matches!(
            (cx.goal.kind, &cx.goal.target),
            (GoalKind::Farm | GoalKind::Sustain, Target::Camp(_))
        );
        hurt || (works_camp
            && cx
                .p
                .mobs
                .iter()
                .any(|m| m.alive && m.class == MobClass::Regional && cx.p.dist(m.at) <= FIGHT_U))
    }

    /// A potion could be drunk now: one is held and no heal runs.
    fn can_drink(cx: &Cx<'_>) -> bool {
        let me = &cx.p.me;
        me.alive
            && u32::from(me.potion_small) + u32::from(me.potion_large) > 0
            && me.potions_healing == 0
            && me.healing_until.is_none_or(|until| until <= cx.p.now)
    }

    fn low(cx: &Cx<'_>) -> bool {
        cx.p.me.hp_share() < cx.prices.sustain.drink_below && Self::can_drink(cx)
    }

    /// The nearest living regional mob that can hit the bot where it stands: within `FIGHT_U` (melee) or within its
    /// bow's range plus a margin (a fight is not left by walking out of reach of the shorter weapons only).
    fn nearest<'a>(cx: &'a Cx<'_>) -> Option<&'a MobSeen> {
        cx.p.mobs
            .iter()
            .filter(|m| m.alive && m.class == MobClass::Regional && cx.p.dist(m.at) <= threat_u(m))
            .min_by(|a, b| {
                cx.p.dist(a.at)
                    .total_cmp(&cx.p.dist(b.at))
                    .then_with(|| a.key.cmp(&b.key))
            })
    }

    /// The camp the bot is fighting, its zone and the members' worth of hp it has left (`None`: no fight).
    fn view(cx: &Cx<'_>) -> Option<(u32, rh_sim::mapdata::Zone, f64)> {
        let near = Self::nearest(cx)?;
        // The camp the bot works (the farm's, or the one it is leaving): a member that chases is labelled by where it
        // stands, which may be another camp. Else the label, else the nearest camp.
        let held = match (cx.goal.kind, &cx.goal.target) {
            (GoalKind::Farm | GoalKind::Sustain, Target::Camp(c)) => {
                cx.p.camp(*c).filter(|c| c.alive > 0)
            }
            _ => None,
        };
        let camp = match (held, near.camp) {
            (Some(c), _) => c,
            (None, Some(c)) => cx.p.camp(c)?,
            (None, None) => {
                cx.p.camps
                    .iter()
                    .filter(|c| c.alive > 0)
                    .min_by(|a, b| cx.p.dist(a.at).total_cmp(&cx.p.dist(b.at)))?
            }
        };
        // Members' worth of hp left: those alive, less what the visible ones around the camp have lost.
        let lost_hp: f64 =
            cx.p.mobs
                .iter()
                .filter(|m| m.alive && m.class == MobClass::Regional)
                .filter(|m| m.camp == Some(camp.index) || cx.p.dist(m.at) <= FIGHT_U)
                .map(|m| 1. - m.hp_share.clamp(0., 1.))
                .sum();
        Some((
            camp.index,
            camp.zone,
            (f64::from(camp.alive) - lost_hp).max(0.),
        ))
    }

    /// Tally this frame of the fight: hp the bot has lost (never what a potion gave back) against member hp destroyed.
    fn track(&mut self, cx: &Cx<'_>) {
        let now = cx.p.now;
        let Some((camp, _, left)) = Self::view(cx) else {
            if now - self.seen.at > TALLY_QUIET_MS {
                self.seen = Seen::default();
            }
            return;
        };
        let hp = cx.p.me.hp_share();
        let seen = &mut self.seen;
        if seen.camp != Some(camp) || now - seen.at > TALLY_QUIET_MS {
            *seen = Seen {
                camp: Some(camp),
                hp,
                left,
                ..Seen::default()
            };
        }
        seen.lost += (seen.hp - hp).max(0.);
        seen.dealt += (seen.left - left).max(0.);
        seen.hp = hp;
        seen.left = left;
        seen.at = now;
    }

    /// The death model's chance, raised to what the fight's own exchange rate says once the tally has enough to say it:
    /// the hp it has cost per member hp destroyed, times the members left, against the hp held.
    fn death(&mut self, cx: &Cx<'_>, zone: rh_sim::mapdata::Zone, left: f64) -> Option<f64> {
        let me = cx.p.me.faction?;
        let share = self.share(me, zone_faction(zone));
        let hp = self.hp_held(cx);
        let model = &cx.prices.death;
        let by_model = death_chance(hp, share, left, model);
        let seen = &self.seen;
        if seen.dealt < TALLY_MIN_DEALT || seen.lost < TALLY_MIN_LOST {
            return Some(by_model);
        }
        let need = seen.lost / seen.dealt * left;
        let by_rate =
            (model.death_per_ratio * (need / hp.max(0.05) - model.safe_ratio)).clamp(0.02, 0.98);
        Some(by_model.max(by_rate))
    }

    /// The fight the bot is in (a member within reach) and whether it is lost: the death model says so AND the bot has
    /// already lost hp (below `ready_hp`). A model's verdict at full hp is a forecast, not a fight; leaving on it made a
    /// committed walk to a camp turn back over and over (P-RAID-C attempt 1: 30 of 34 leaves began at hp 1.0).
    fn fight(&mut self, cx: &Cx<'_>) -> Option<Fight> {
        if !Self::in_fight(cx, self.hurt_at) {
            return None;
        }
        let (camp, zone, left) = Self::view(cx)?;
        let death = self.death(cx, zone, left)?;
        Some(Fight {
            camp,
            lost: death >= cx.prices.sustain.disengage_death - cx.prices.sustain.disengage_lead
                && cx.p.me.hp_share() < cx.prices.sustain.ready_hp,
        })
    }

    fn leave_candidate(cx: &Cx<'_>, camp: u32) -> Candidate {
        let s = &cx.prices.sustain;
        Candidate {
            goal: RaidGoal::new(GoalKind::Sustain, Target::Camp(camp)),
            value: s.leave_value,
            success: 1.,
            death: 0.,
            loss: 0.,
            minutes: s.leave_seconds / 60.,
        }
    }
}

impl Sustain {
    fn think(&mut self, goal: &RaidGoal, cx: &mut Cx<'_>) -> Step {
        let s = cx.prices.sustain;
        if !s.on {
            return Step::failed("sustain is off");
        }
        let leaving = matches!(goal.target, Target::Camp(_));
        let threat = Self::nearest(cx).map(|m| m.at);
        if let Some(from) = threat {
            if !leaving {
                // Someone arrived while resting: the heal-up is over, the arbiter decides again.
                return Step::done("company while healing");
            }
            let at: Vec2 = cx.p.me.at;
            let heading = SIDESTEPS
                .iter()
                .find_map(|turn| away_point(cx.paths, at, from, STEP_U, *turn));
            return match heading {
                Some(to) => {
                    let mut mv = move_intent(to, "leave a lost fight");
                    mv.priority = PRIORITY_COMBAT;
                    Step::running(vec![mv], "leaving the fight")
                }
                None => {
                    self.leaving = None;
                    self.cornered_until = cx.p.now + CORNERED_MS;
                    Step::failed("cornered")
                }
            };
        }
        if cx.p.me.hp_share() >= s.ready_hp {
            self.leaving = None;
            return Step::done("healed");
        }
        Step::running(Vec::new(), "healing up")
    }
}

impl Executor for Sustain {
    fn kinds(&self) -> &'static [GoalKind] {
        &[GoalKind::Sustain]
    }
    fn reset(&mut self) {
        *self = Self::default();
    }
    fn observe(&mut self, cx: &mut Cx<'_>) {
        // A leave lives only while the seat's goal is Sustain: once another goal holds the seat the hold is over, and
        // the leave may come back only through a fresh `lost` fight (`start` re-arms it on adoption).
        if cx.goal.kind != GoalKind::Sustain {
            self.leaving = None;
        }
        if cx.prices.sustain.on && cx.p.active {
            self.watch(cx);
            self.track(cx);
        }
    }
    fn urgent(&mut self, cx: &mut Cx<'_>) -> bool {
        if !cx.prices.sustain.on || !cx.p.active {
            return false;
        }
        let low = Self::low(cx);
        let lost = self.fight(cx).is_some_and(|f| f.lost);
        let edge = (low && !self.was_low) || (lost && !self.was_lost);
        self.was_low = low;
        self.was_lost = lost;
        edge
    }
    fn propose(&mut self, cx: &mut Cx<'_>, out: &mut Vec<Candidate>) {
        let s = cx.prices.sustain;
        if !s.on {
            return;
        }
        let now = cx.p.now;
        let hp = cx.p.me.hp_share();
        let lost = self
            .fight(cx)
            .filter(|f| f.lost && now >= self.cornered_until);
        if let Some(camp) = self
            .leaving
            .filter(|_| hp < s.ready_hp)
            .or(lost.map(|f| f.camp))
        {
            out.push(Self::leave_candidate(cx, camp));
            return;
        }
        // Nobody on us and hp short of ready: a heal-up, priced by the regeneration it waits for.
        if hp < s.ready_hp && Self::nearest(cx).is_none() {
            let deficit = s.ready_hp - hp;
            out.push(Candidate {
                goal: RaidGoal::new(GoalKind::Sustain, Target::None),
                value: deficit * s.rest_value,
                success: 1.,
                death: 0.,
                loss: 0.,
                minutes: deficit / (s.regen_per_second * 60.),
            });
        }
    }
    fn start(&mut self, goal: &RaidGoal, _cx: &mut Cx<'_>) {
        self.leaving = match goal.target {
            Target::Camp(c) => Some(c),
            _ => None,
        };
    }
    fn act(&mut self, goal: &RaidGoal, cx: &mut Cx<'_>) -> Step {
        self.think(goal, cx)
    }
    fn ride_along(&mut self, _goal: &RaidGoal, cx: &mut Cx<'_>) -> Step {
        if !cx.prices.sustain.on || !Self::low(cx) {
            return Step::none();
        }
        let mut step = Step::none();
        step.acts.push(crate::bots::raid::acts::Act::UseConsumable);
        step.why = "drink a potion";
        step
    }
}
