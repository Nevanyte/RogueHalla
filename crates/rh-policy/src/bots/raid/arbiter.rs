//! The arbiter (P-RAID-0): candidate goals scored in camp-equivalents per minute, with hysteresis.
//!
//! Every strategy tick each registered executor proposes candidates (`goal::Executor::propose`); the arbiter scores
//! each `(value x success - death x loss) / minutes` (the workbook's currency: one normal camp = 12 stat stacks = one
//! camp-equivalent), times its kind's tier and persona multipliers (`goal::GoalKind`). The incumbent goal, while it
//! runs, gets a hysteresis margin so the bot does not dither; a committed incumbent (a fight under way: `Step::committed`)
//! is kept outright while its own score stays positive. Ties fall to the kind's declaration order, then the
//! target's stable key: the choice depends on nothing but the candidates (no hash order, no float-order surprise).
//!
//! Prices are local constants (`Prices::LOCAL`) until P-RAID-V's value table; a caller swaps them with
//! `RaidBot::with_prices`, so no file here changes when the table lands.
use super::goal::{Candidate, RaidGoal, Status};
use crate::bots::random::{uniform, Rng};
use crate::bots::types::{PersonaParams, TierParams};

/// The incumbent's score is raised by this share of its magnitude...
pub const HYSTERESIS: f64 = 0.25;
/// ...and by at least this much (camp-equivalents per minute).
pub const HYSTERESIS_FLOOR: f64 = 0.05;
/// A goal underfoot still takes this long (minutes): no candidate divides by zero.
pub const MIN_MINUTES: f64 = 0.05;

/// The value inputs, in camp-equivalents (ce). Local constants until P-RAID-V (`data/raid_values.json`).
#[derive(Clone, Copy, Debug, PartialEq)]
pub struct Prices {
    /// One normal camp cleared.
    pub camp: f64,
    /// Stat stacks one camp-equivalent stands for (the workbook's unit).
    pub stacks_per_camp: f64,
    /// One reward chest's pick.
    pub chest: f64,
    /// What roaming finds per minute on average (the floor every other goal must beat).
    pub roam_per_minute: f64,
    /// What dying loses: the run's progress and the time to get back to farming.
    pub death_loss: f64,
    /// Seconds to kill one normal camp member, fighting it alone.
    pub kill_seconds: f64,
}

impl Prices {
    pub const LOCAL: Self = Self {
        camp: 1.,
        stacks_per_camp: 12.,
        chest: 1.5,
        roam_per_minute: 0.1,
        death_loss: 6.,
        kill_seconds: 10.,
    };
}

impl Default for Prices {
    fn default() -> Self {
        Self::LOCAL
    }
}

/// One candidate with its score (ce per minute, multipliers applied).
#[derive(Clone, Debug, PartialEq)]
pub struct Scored {
    pub candidate: Candidate,
    pub score: f64,
}

/// The raw rate: `(value x success - death x loss) / minutes`.
pub fn rate(c: &Candidate) -> f64 {
    (c.value * c.success.clamp(0., 1.) - c.death.clamp(0., 1.) * c.loss) / c.minutes.max(MIN_MINUTES)
}

/// The candidate's score: its rate times its kind's tier and persona multipliers.
pub fn score(c: &Candidate, tier: &TierParams, persona: &PersonaParams) -> f64 {
    rate(c) * c.goal.kind.tier_mult(tier) * c.goal.kind.persona_mult(persona)
}

/// The strict order the arbiter ranks by: score high to low, then kind, then target key.
fn better(a: &Scored, b: &Scored) -> std::cmp::Ordering {
    b.score
        .total_cmp(&a.score)
        .then(a.candidate.goal.kind.cmp(&b.candidate.goal.kind))
        .then_with(|| {
            a.candidate
                .goal
                .target
                .order_key()
                .partial_cmp(&b.candidate.goal.target.order_key())
                .unwrap_or(std::cmp::Ordering::Equal)
        })
}

/// The strategy cadence and the last ranking.
#[derive(Clone, Debug, Default, PartialEq)]
pub struct Arbiter {
    started: bool,
    next_at: f64,
    /// The last strategy tick's ranking, best first (for the lab and for tests).
    pub last: Vec<Scored>,
}

impl Arbiter {
    pub fn reset(&mut self) {
        *self = Self::default();
    }

    /// A strategy tick is due: the first frame, the cadence passed, the goal finished, or something urgent happened.
    pub fn due(&self, now: f64, status: Status, urgent: bool) -> bool {
        !self.started || now >= self.next_at || status != Status::Running || urgent
    }

    /// Rank `candidates` and return the winner. The incumbent, while it runs, keeps a hysteresis margin, and while it is
    /// also committed it wins outright as long as its score is positive. Draws the next strategy time from `rng` (the
    /// arbiter stream, tag 16) once per call.
    pub fn choose(
        &mut self,
        now: f64,
        candidates: Vec<Candidate>,
        incumbent: (&RaidGoal, Status, bool),
        tier: &TierParams,
        persona: &PersonaParams,
        rng: &mut Rng,
    ) -> Option<RaidGoal> {
        self.started = true;
        self.next_at = now
            + uniform(
                rng,
                tier.strategy_ms.mean - tier.strategy_ms.jitter,
                tier.strategy_ms.mean + tier.strategy_ms.jitter,
            );
        let (held, status, committed) = incumbent;
        let mut ranked: Vec<Scored> = candidates
            .into_iter()
            .filter_map(|candidate| {
                let mut s = score(&candidate, tier, persona);
                if !s.is_finite() {
                    return None;
                }
                if status == Status::Running && candidate.goal.same(held) {
                    s += (s.abs() * HYSTERESIS).max(HYSTERESIS_FLOOR);
                }
                Some(Scored {
                    candidate,
                    score: s,
                })
            })
            .collect();
        ranked.sort_by(better);
        let kept = (status == Status::Running && committed)
            .then(|| ranked.iter().find(|s| s.candidate.goal.same(held) && s.score > 0.))
            .flatten();
        let winner = kept.or(ranked.first()).map(|s| s.candidate.goal.clone());
        self.last = ranked;
        winner
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    use crate::bots::raid::goal::{GoalKind, Target};
    use crate::bots::types::{PersonaId, TierId};

    fn cand(kind: GoalKind, target: Target, value: f64, minutes: f64) -> Candidate {
        Candidate {
            goal: RaidGoal::new(kind, target),
            value,
            success: 1.,
            death: 0.,
            loss: 0.,
            minutes,
        }
    }

    #[test]
    fn death_term_and_ties_are_deterministic() {
        let tier = TierParams::by_id(TierId::Normal);
        let persona = PersonaParams::by_id(PersonaId::Farmer);
        let mut risky = cand(GoalKind::Farm, Target::Camp(1), 1., 1.);
        risky.death = 0.5;
        risky.loss = 6.;
        assert!(rate(&risky) < 0., "a likely death outweighs one camp");
        let mut arb = Arbiter::default();
        let idle = RaidGoal::new(GoalKind::Roam, Target::None);
        let mut rng = Rng::new(16);
        // Two equal camps: the lower index wins, whatever order they were proposed in.
        let a = vec![
            cand(GoalKind::Farm, Target::Camp(7), 1., 1.),
            cand(GoalKind::Farm, Target::Camp(3), 1., 1.),
        ];
        let got = arb.choose(0., a, (&idle, Status::Done, false), tier, persona, &mut rng);
        assert_eq!(got, Some(RaidGoal::new(GoalKind::Farm, Target::Camp(3))));
        // The incumbent keeps a slightly worse score...
        let held = RaidGoal::new(GoalKind::Farm, Target::Camp(7));
        let b = vec![
            cand(GoalKind::Farm, Target::Camp(3), 1.1, 1.),
            cand(GoalKind::Farm, Target::Camp(7), 1., 1.),
        ];
        let got = arb.choose(0., b, (&held, Status::Running, false), tier, persona, &mut rng);
        assert_eq!(got, Some(held.clone()));
        // ...but not a much worse one.
        let c = vec![
            cand(GoalKind::Farm, Target::Camp(3), 2., 1.),
            cand(GoalKind::Farm, Target::Camp(7), 1., 1.),
        ];
        let got = arb.choose(0., c.clone(), (&held, Status::Running, false), tier, persona, &mut rng);
        assert_eq!(got, Some(RaidGoal::new(GoalKind::Farm, Target::Camp(3))));
        // A committed incumbent (a fight under way) stays while its score is positive...
        let got = arb.choose(0., c, (&held, Status::Running, true), tier, persona, &mut rng);
        assert_eq!(got, Some(held.clone()));
        // ...and yields once the fight is a losing one.
        let mut losing = cand(GoalKind::Farm, Target::Camp(7), 1., 1.);
        losing.death = 0.5;
        losing.loss = 6.;
        let d = vec![losing, cand(GoalKind::Roam, Target::None, 0.1, 1.)];
        let got = arb.choose(0., d, (&held, Status::Running, true), tier, persona, &mut rng);
        assert_eq!(got, Some(RaidGoal::new(GoalKind::Roam, Target::None)));
    }
}
