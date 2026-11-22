//! Farm (P-RAID-0): clearing a camp with the mob_hunt rules and a lock/attack loop.
//!
//! Candidates: every camp with members alive and no elite (elite camps are P-RAID-E's), priced at `Prices::camp` per
//! `TYPICAL_MEMBERS` members, with a success and death chance from the duel share (`Knowledge::mob_share`, the shared
//! planning duel) against the owner's hp. Execution follows HumanBot's compatibility combat (`bots/bot.rs`): walk to
//! the camp, take the nearest living regional member, lock it inside weapon reach, else walk to it; hold the better
//! weapon slot (`mob_hunt::best_slot_vs_mob`). Only regional mobs are targets: quest givers (actor kind 4, filed under
//! camp 0 in the sim) are not in `RaidPercept::mobs`, and players are never farmed.
use crate::bots::mob_hunt::best_slot_vs_mob;
use crate::bots::planning::{matchup_share, zone_faction};
use crate::bots::raid::exec::travel::{self, eta_minutes, move_intent, step_toward, straight_minutes};
use crate::bots::raid::goal::{Candidate, Cx, Executor, GoalKind, RaidGoal, Status, Step, Target};
use crate::bots::raid::percept::{CampSeen, MobClass, MobSeen};
use crate::bots::types::{Intent, IntentBody, IntentTiming, PRIORITY_COMBAT};
use rh_sim::{FactionId, Vec2, WeaponId};

/// The member count one `Prices::camp` stands for (camps hold three to five).
pub const TYPICAL_MEMBERS: f64 = 4.;
/// A melee lock is clicked inside this (HumanBot's compatibility front end).
pub const MELEE_LOCK_U: f64 = 40.;
/// A mob of another camp this close that is fighting is fought first: it is on us.
pub const DEFEND_U: f64 = 96.;
/// Standing at the camp with no member in sight this long fails the goal...
const EMPTY_MS: f64 = 3_000.;
/// ...and the camp is not proposed again for this long.
const FAILED_BAN_MS: f64 = 20_000.;
/// The chance of death when the hp the fight needs equals the hp held (the curve's midpoint), and its slope per hp share.
const DEATH_MID: f64 = 0.5;
const DEATH_SLOPE: f64 = 2.;

/// The chance to die clearing `alive` members, each costing `1 - share` of max hp, starting at `hp` share.
pub fn death_chance(hp: f64, share: f64, alive: u32) -> f64 {
    let need = f64::from(alive) * (1. - share.clamp(0., 1.));
    (DEATH_MID + (need - hp) * DEATH_SLOPE).clamp(0.02, 0.98)
}

/// The Farm goal's state: the duel shares it computed, the member it fights, the camps it gave up on.
#[derive(Default)]
pub struct Farm {
    shares: Vec<((FactionId, FactionId), f64)>,
    target: Option<String>,
    empty_since: Option<f64>,
    banned: Vec<(u32, f64)>,
}

impl Farm {
    fn share(&mut self, cx: &Cx<'_>, me: FactionId, region: FactionId) -> f64 {
        if let Some((_, s)) = self.shares.iter().find(|(k, _)| *k == (me, region)) {
            return *s;
        }
        let s = cx.knowledge.map_or_else(
            || matchup_share(me, region, false),
            |k| k.mob_share(me, region, false),
        );
        self.shares.push(((me, region), s));
        s
    }

    fn eligible(&self, camp: &CampSeen, now: f64) -> bool {
        camp.alive > 0
            && camp.elite.is_none()
            && !self.banned.iter().any(|(c, until)| *c == camp.index && *until > now)
    }

    /// The member to fight: a living regional mob of the camp, or a fighting one on top of us; the nearest.
    fn pick<'a>(cx: &'a Cx<'_>, camp: u32) -> Option<&'a MobSeen> {
        let at = cx.p.me.at;
        cx.p
            .mobs
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
        let mut order: Vec<(f64, &CampSeen)> = cx
            .p
            .camps
            .iter()
            .filter(|c| self.eligible(c, now))
            .map(|c| (dist(at, c.at), c))
            .collect();
        order.sort_by(|a, b| a.0.total_cmp(&b.0).then(a.1.index.cmp(&b.1.index)));
        let horizon = cx.tier_params().route.horizon as usize;
        let camps: Vec<(usize, u32, Vec2, rh_sim::mapdata::Zone, u32)> = order
            .iter()
            .enumerate()
            .map(|(n, (_, c))| (n, c.index, c.at, c.zone, c.alive))
            .collect();
        for (n, index, camp_at, zone, alive) in camps {
            let walk = if n < horizon {
                match eta_minutes(cx.paths, at, camp_at) {
                    Some(m) => m,
                    None => continue,
                }
            } else {
                straight_minutes(at, camp_at)
            };
            let share = self.share(cx, me, zone_faction(zone));
            let death = death_chance(hp, share, alive);
            out.push(Candidate {
                goal: RaidGoal::new(GoalKind::Farm, Target::Camp(index)),
                value: cx.prices.camp * f64::from(alive) / TYPICAL_MEMBERS,
                success: 1. - death,
                death,
                loss: cx.prices.death_loss,
                minutes: walk + f64::from(alive) * cx.prices.kill_seconds / 60.,
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
        // Keep the member we fight while it lives; else take the nearest.
        let kept = self.target.as_ref().and_then(|key| {
            cx.p
                .mobs
                .iter()
                .find(|m| &m.key == key && m.alive && m.class == MobClass::Regional)
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
        self.target = Some(mob.key.clone());
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
        Step::running(intents, "farming")
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn death_chance_grows_with_the_fight_and_falls_with_hp() {
        assert!(death_chance(1., 0.8, 1) < death_chance(1., 0.8, 3));
        assert!(death_chance(1., 0.5, 3) > death_chance(1., 0.8, 3));
        assert!(death_chance(0.3, 0.8, 2) > death_chance(1., 0.8, 2));
        assert_eq!(death_chance(1., 1., 0), 0.02);
    }
}
