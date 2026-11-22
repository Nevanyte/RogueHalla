//! Farm (P-RAID-0): clearing a camp with the mob_hunt rules and a lock/attack loop (HumanBot's compatibility combat,
//! `bots/bot.rs`). Candidates are the camps with members alive and no elite (elite camps are P-RAID-E's), priced by
//! the shared planning duel (`planning::matchup_share`) against the owner's hp. Only regional mobs are targets: quest
//! givers are not in `RaidPercept::mobs`, and players are never farmed.
use crate::bots::mob_hunt::best_slot_vs_mob;
use crate::bots::planning::{matchup_share, zone_faction};
use crate::bots::raid::exec::travel::{
    self, eta_minutes, move_intent, step_toward, straight_minutes,
};
use crate::bots::raid::goal::{Candidate, Cx, Executor, GoalKind, RaidGoal, Status, Step, Target};
use crate::bots::raid::percept::{CampSeen, MobClass, MobSeen};
use crate::bots::raid::values::DeathModel;
use crate::bots::types::{Intent, IntentBody, IntentTiming, PRIORITY_COMBAT};
use rh_sim::{FactionId, Vec2, WeaponId};

/// A melee lock is clicked inside this (HumanBot's compatibility front end).
pub const MELEE_LOCK_U: f64 = 40.;
/// A mob of another camp this close that is fighting is fought first: it is on us.
pub const DEFEND_U: f64 = 96.;
/// A living member of the camp this close means the fight is on (an aggroed camp chases).
pub const FIGHT_U: f64 = 240.;
/// Standing at the camp with no member in sight this long fails the goal...
const EMPTY_MS: f64 = 3_000.;
/// ...and the camp is not proposed again for this long.
const FAILED_BAN_MS: f64 = 20_000.;
/// A member is the same one next think when it stands within this of where the last one was (npc keys live one frame).
const TRACK_U: f64 = 80.;

/// The share of max hp clearing `members` members costs, each a duel the bot keeps `share` of its hp in. `members` is
/// in whole members' worth of hp (a member at half hp counts one half). Priced by `model` (`Prices::death`).
pub fn hp_needed(share: f64, members: f64, model: &DeathModel) -> f64 {
    let n = members.max(0.);
    (1. - share.clamp(0., 1.)) * n * (n + 1.) / 2. * model.fight_weight
}

/// The chance to die clearing `members` members' worth of hp from `hp` share of max hp, by `model` (`Prices::death`,
/// until P-RAID-C and P-RAID-V price sustain and the real fight). Clamped to 0.02..=0.98.
pub fn death_chance(hp: f64, share: f64, members: f64, model: &DeathModel) -> f64 {
    let ratio = hp_needed(share, members, model) / hp.max(0.05);
    (model.death_per_ratio * (ratio - model.safe_ratio)).clamp(0.02, 0.98)
}

/// The camp's members in whole members' worth of hp: those alive, less what the visible ones have lost.
pub fn members_left(p: &crate::bots::raid::percept::RaidPercept, camp: &CampSeen) -> f64 {
    let lost: f64 = p
        .mobs
        .iter()
        .filter(|m| m.alive && m.class == MobClass::Regional && m.camp == Some(camp.index))
        .map(|m| 1. - m.hp_share.clamp(0., 1.))
        .sum();
    (f64::from(camp.alive) - lost).max(0.)
}

/// The Farm goal: walk to the camp, take the nearest living regional member (or one fighting at our side), lock it
/// inside weapon reach, else walk to it, holding the better weapon slot (`mob_hunt::best_slot_vs_mob`). Its state: the
/// duel shares it computed, where the member it fights stood, the camps it gave up on.
#[derive(Default)]
pub struct Farm {
    shares: Vec<((FactionId, FactionId), f64)>,
    /// Where the member it fights stood at the last think.
    target: Option<Vec2>,
    empty_since: Option<f64>,
    banned: Vec<(u32, f64)>,
}

impl Farm {
    /// The shared planning duel's share (public content, exact): the lab's player prices camps by the rules, not by
    /// Knowledge's per-bot error (which swings a three-member camp's death term several-fold).
    fn share(&mut self, me: FactionId, region: FactionId) -> f64 {
        if let Some((_, s)) = self.shares.iter().find(|(k, _)| *k == (me, region)) {
            return *s;
        }
        let s = matchup_share(me, region, false);
        self.shares.push(((me, region), s));
        s
    }

    fn eligible(&self, camp: &CampSeen, now: f64) -> bool {
        camp.alive > 0
            && camp.elite.is_none()
            && !self
                .banned
                .iter()
                .any(|(c, until)| *c == camp.index && *until > now)
    }

    /// The member to fight: a living regional mob of the camp, or a fighting one on top of us; the nearest.
    fn pick<'a>(cx: &'a Cx<'_>, camp: u32) -> Option<&'a MobSeen> {
        let at = cx.p.me.at;
        cx.p.mobs
            .iter()
            .filter(|m| m.alive && m.class == MobClass::Regional)
            .filter(|m| m.camp == Some(camp) || (m.engaged && cx.p.dist(m.at) <= DEFEND_U))
            .min_by(|a, b| {
                let (da, db) = (dist(at, a.at), dist(at, b.at));
                da.total_cmp(&db).then_with(|| a.key.cmp(&b.key))
            })
    }
}

fn dist(a: Vec2, b: Vec2) -> f64 {
    rh_sim::math::hypot(a.x - b.x, a.y - b.y)
}

/// The distance a lock is clicked at with `weapon` in hand.
fn lock_range(weapon: Option<WeaponId>) -> f64 {
    match weapon.map(|w| rh_sim::content::weapon(w).shape) {
        Some(rh_sim::content::Shape::Projectile { range, .. }) => range * 0.8,
        _ => MELEE_LOCK_U,
    }
}

impl Executor for Farm {
    fn kinds(&self) -> &'static [GoalKind] {
        &[GoalKind::Farm]
    }
    fn reset(&mut self) {
        *self = Self::default();
    }
    fn propose(&mut self, cx: &mut Cx<'_>, out: &mut Vec<Candidate>) {
        let now = cx.p.now;
        self.banned.retain(|(_, until)| *until > now);
        let Some(me) = cx.p.me.faction else {
            return;
        };
        let at = cx.p.me.at;
        let hp = cx.p.me.hp_share();
        // Path searches for the nearest few (the tier's route horizon); straight estimates beyond it.
        let mut order: Vec<(f64, &CampSeen)> =
            cx.p.camps
                .iter()
                .filter(|c| self.eligible(c, now))
                .map(|c| (dist(at, c.at), c))
                .collect();
        order.sort_by(|a, b| a.0.total_cmp(&b.0).then(a.1.index.cmp(&b.1.index)));
        let horizon = cx.tier_params().route.horizon as usize;
        let camps: Vec<(usize, u32, Vec2, rh_sim::mapdata::Zone, u32, f64)> = order
            .iter()
            .enumerate()
            .map(|(n, (_, c))| (n, c.index, c.at, c.zone, c.alive, members_left(cx.p, c)))
            .collect();
        for (n, index, camp_at, zone, alive, left) in camps {
            // A failed search (a camp spot or the bot's own spot against a wall) falls back to the straight estimate:
            // the walk itself finds the way, and dropping the camp would abandon a fight in progress.
            let walk = if n < horizon {
                eta_minutes(cx.paths, at, camp_at).unwrap_or_else(|| straight_minutes(at, camp_at))
            } else {
                straight_minutes(at, camp_at)
            };
            let share = self.share(me, zone_faction(zone));
            let death = death_chance(hp, share, left, &cx.prices.death);
            out.push(Candidate {
                goal: RaidGoal::new(GoalKind::Farm, Target::Camp(index)),
                value: cx.prices.camp * f64::from(alive) / cx.prices.death.typical_members,
                success: 1. - death,
                death,
                loss: cx.prices.death_loss,
                minutes: walk + left * cx.prices.kill_seconds / 60.,
            });
        }
    }
    fn start(&mut self, _goal: &RaidGoal, _cx: &mut Cx<'_>) {
        self.target = None;
        self.empty_since = None;
    }
    fn act(&mut self, goal: &RaidGoal, cx: &mut Cx<'_>) -> Step {
        let Target::Camp(index) = goal.target else {
            return Step::failed("farm without a camp");
        };
        let Some(camp) = cx.p.camp(index) else {
            return Step::failed("camp not delivered");
        };
        if camp.alive == 0 {
            return Step::done("camp cleared");
        }
        let camp_at = camp.at;
        let now = cx.p.now;
        // Keep the member we fight while it lives (found where it stood: npc keys live one frame); else the nearest.
        let kept = self.target.and_then(|was| {
            cx.p.mobs
                .iter()
                .filter(|m| m.alive && m.class == MobClass::Regional && dist(m.at, was) <= TRACK_U)
                .min_by(|a, b| {
                    dist(a.at, was)
                        .total_cmp(&dist(b.at, was))
                        .then_with(|| a.key.cmp(&b.key))
                })
        });
        let Some(mob) = kept.or_else(|| Self::pick(cx, index)).cloned() else {
            self.target = None;
            let step = travel::walk(cx, camp_at, "to camp");
            if step.status == Status::Running {
                self.empty_since = None;
                return step;
            }
            // At the camp with nobody in sight: wait a moment, then give the camp up for a while.
            let since = *self.empty_since.get_or_insert(now);
            if now - since >= EMPTY_MS {
                self.banned.push((index, now + FAILED_BAN_MS));
                return Step::failed("no member in sight at the camp");
            }
            return Step::running(Vec::new(), "waiting at the camp");
        };
        self.empty_since = None;
        self.target = Some(mob.at);
        let mut intents = Vec::new();
        let slots = cx.p.me.weapons;
        let slot = best_slot_vs_mob(slots, cx.p.me.slot, mob.weapon);
        if slot != cx.p.me.slot.min(1) {
            intents.push(Intent {
                body: IntentBody::Swap { slot },
                priority: PRIORITY_COMBAT,
                timing: IntentTiming::Step {},
                why: "best slot vs the mob".into(),
            });
        }
        let in_hand = slots[usize::from(cx.p.me.slot.min(1))];
        let d = cx.p.dist(mob.at);
        // A fight is on once a member of the camp is close or a mob is fighting next to us: they follow if we leave.
        let fighting = cx.p.mobs.iter().any(|m| {
            m.alive
                && m.class == MobClass::Regional
                && ((m.camp == Some(index) && cx.p.dist(m.at) <= FIGHT_U)
                    || (m.engaged && cx.p.dist(m.at) <= DEFEND_U))
        });
        if d <= lock_range(in_hand) {
            intents.push(Intent {
                body: IntentBody::Lock {
                    target: mob.key.clone(),
                    at: mob.at,
                },
                priority: PRIORITY_COMBAT,
                timing: IntentTiming::Step {},
                why: "farm engage".into(),
            });
        } else if let Some(next) = step_toward(cx.paths, cx.p.me.at, mob.at) {
            intents.push(move_intent(next, "farm approach"));
        }
        if fighting {
            Step::committed(intents, "fighting")
        } else {
            Step::running(intents, "approaching")
        }
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    use crate::bots::raid::values::Prices;

    #[test]
    fn death_chance_grows_with_the_fight_and_falls_with_hp() {
        assert!(
            death_chance(1., 0.965, 1., &Prices::LOCAL.death)
                < death_chance(1., 0.965, 4., &Prices::LOCAL.death)
        );
        assert!(
            death_chance(1., 0.9, 3., &Prices::LOCAL.death)
                > death_chance(1., 0.965, 3., &Prices::LOCAL.death)
        );
        assert!(
            death_chance(0.3, 0.965, 2., &Prices::LOCAL.death)
                > death_chance(1., 0.965, 2., &Prices::LOCAL.death)
        );
        assert_eq!(death_chance(1., 1., 0., &Prices::LOCAL.death), 0.02);
        // A fresh fighter: a three-member camp is a small risk, a five-member camp a likely death (Holy vs Fire share).
        assert!(death_chance(1., 0.965, 3., &Prices::LOCAL.death) < 0.1);
        assert!(death_chance(1., 0.965, 5., &Prices::LOCAL.death) > 0.3);
        // A camp worn down is cheaper to finish than a fresh one.
        assert!(
            death_chance(0.6, 0.965, 1.6, &Prices::LOCAL.death)
                < death_chance(0.6, 0.965, 3., &Prices::LOCAL.death)
        );
    }
}
