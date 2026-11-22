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
}

/// How far from the bot `m` is a threat: melee reach, or its projectile range plus a margin.
fn threat_u(m: &MobSeen) -> f64 {
    match m.weapon.map(|w| rh_sim::content::weapon(w).shape) {
        Some(rh_sim::content::Shape::Projectile { range, .. }) => (range + BOW_MARGIN_U).max(FIGHT_U),
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

    /// Hp share held plus the potions held (the farm's view of what the bot can still spend).
    fn hp_held(cx: &Cx<'_>) -> f64 {
        cx.p.me.hp_share()
            + cx.prices
                .sustain
                .potion_hp(cx.p.me.potion_large, cx.p.me.potion_small)
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

    /// The fight the bot is in (a member within reach) and whether the death model says it is lost.
    fn fight(&mut self, cx: &Cx<'_>) -> Option<Fight> {
        let me = cx.p.me.faction?;
        let near = Self::nearest(cx)?;
        let camp = match near.camp {
            Some(c) => cx.p.camp(c)?,
            None => cx
                .p
                .camps
                .iter()
                .filter(|c| c.alive > 0)
                .min_by(|a, b| cx.p.dist(a.at).total_cmp(&cx.p.dist(b.at)))?,
        };
        // Members' worth of hp left: those alive, less what the visible ones around the camp have lost.
        let lost_hp: f64 = cx
            .p
            .mobs
            .iter()
            .filter(|m| m.alive && m.class == MobClass::Regional)
            .filter(|m| m.camp == Some(camp.index) || (m.camp.is_none() && cx.p.dist(m.at) <= FIGHT_U))
            .map(|m| 1. - m.hp_share.clamp(0., 1.))
            .sum();
        let left = (f64::from(camp.alive) - lost_hp).max(0.);
        let share = self.share(me, zone_faction(camp.zone));
        let death = death_chance(Self::hp_held(cx), share, left, &cx.prices.death);
        Some(Fight {
            camp: camp.index,
            lost: death >= cx.prices.sustain.disengage_death - cx.prices.sustain.disengage_lead,
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

impl Executor for Sustain {
    fn kinds(&self) -> &'static [GoalKind] {
        &[GoalKind::Sustain]
    }
    fn reset(&mut self) {
        *self = Self::default();
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
        let lost = self.fight(cx).filter(|f| f.lost && now >= self.cornered_until);
        if let Some(camp) = self.leaving.filter(|_| hp < s.ready_hp).or(lost.map(|f| f.camp)) {
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
            return match away_point(cx.paths, at, from, STEP_U, 0.) {
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
