//! Travel (P-RAID-0): walking through RoomPathService within its per-tick budget (`step_toward`, `walk`), trip prices
//! for the arbiter (`eta_minutes`), and the Roam goal: a camp or spawn point drawn from the arbiter stream, the floor
//! every paying goal must beat (`Prices::roam_per_minute`).
use crate::bots::paths::RoomPathService;
use crate::bots::raid::goal::{Candidate, Cx, Executor, GoalKind, RaidGoal, Step, Target};
use crate::bots::types::{Intent, IntentBody, IntentTiming, PRIORITY_GOAL};
use rh_sim::constants::{PLAYER_RADIUS, PLAYER_SPEED};
use rh_sim::Vec2;

/// Within this of the destination the trip is over.
pub const ARRIVE_U: f64 = 48.;
/// Within this, and standing still a while, the trip is close enough.
pub const NEAR_U: f64 = 150.;
const STILL_MS: f64 = 1_200.;
/// A roam point nearer than this is not worth a roam.
const ROAM_MIN_U: f64 = 600.;

fn dist(a: Vec2, b: Vec2) -> f64 {
    rh_sim::math::hypot(a.x - b.x, a.y - b.y)
}

/// The point to click next on the way from `from` to `to`: the destination when the straight segment is clear, else
/// the next waypoint of a budgeted search (`BOT_PATH_BUDGET_PER_TICK`, a 1 s cache), else the destination when no
/// search ran this tick; `None` when the service knows there is no way.
pub fn step_toward(paths: &mut RoomPathService, from: Vec2, to: Vec2) -> Option<Vec2> {
    if dist(from, to) <= 2. * ARRIVE_U || paths.clear(from, to) {
        return Some(to);
    }
    match paths.path(from, to) {
        Some(route) => route
            .iter()
            .find(|w| dist(from, **w) > 2. * PLAYER_RADIUS)
            .or(route.last())
            .copied()
            .or(Some(to)),
        // No search this tick (budget spent, no fresh cache): a straight estimate says nothing about walls, so click the
        // destination; a known dead end answers None.
        None => paths.walk(from, to).map(|_| to),
    }
}

/// A goal-priority click toward `to`.
pub fn move_intent(to: Vec2, why: &str) -> Intent {
    Intent {
        body: IntentBody::Move { to },
        priority: PRIORITY_GOAL,
        timing: IntentTiming::Step {},
        why: why.into(),
    }
}

/// Minutes to walk from `from` to `to`: the path length when the service has one, else the straight line x 1.3
/// (`paths::STRAIGHT_FACTOR`); `None` when there is no way.
pub fn eta_minutes(paths: &mut RoomPathService, from: Vec2, to: Vec2) -> Option<f64> {
    paths
        .walk(from, to)
        .map(|a| a.dist / PLAYER_SPEED / 60.)
}

/// Straight-line minutes, for candidates beyond the path horizon (no search spent).
pub fn straight_minutes(from: Vec2, to: Vec2) -> f64 {
    dist(from, to) * crate::bots::paths::STRAIGHT_FACTOR / PLAYER_SPEED / 60.
}

/// One walking think toward `to`: `Done` on arrival (or close and standing still), `Failed` when there is no way.
pub fn walk(cx: &mut Cx<'_>, to: Vec2, why: &'static str) -> Step {
    let at = cx.p.me.at;
    let d = dist(at, to);
    if d <= ARRIVE_U || (d <= NEAR_U && cx.p.now - cx.still_since >= STILL_MS) {
        return Step::done("arrived");
    }
    match step_toward(cx.paths, at, to) {
        Some(next) => Step::running(vec![move_intent(next, why)], why),
        None => Step::failed("no way there"),
    }
}

/// The Roam goal: walk to a fresh camp or spawn point.
#[derive(Default)]
pub struct Roam {
    to: Option<Vec2>,
}

impl Roam {
    fn draw(&mut self, cx: &mut Cx<'_>) -> Option<Vec2> {
        let at = cx.p.me.at;
        let mut points: Vec<Vec2> = cx
            .plan
            .camps
            .iter()
            .map(|c| Vec2::new(c.x, c.y))
            .chain(cx.plan.spawn_pool.iter().map(|s| Vec2::new(s.x, s.y)))
            .filter(|p| dist(at, *p) >= ROAM_MIN_U)
            .collect();
        if points.is_empty() {
            points = cx.plan.camps.iter().map(|c| Vec2::new(c.x, c.y)).collect();
        }
        if points.is_empty() {
            return None;
        }
        let i = ((cx.rng.next_f64() * points.len() as f64) as usize).min(points.len() - 1);
        Some(points[i])
    }
}

impl Executor for Roam {
    fn kinds(&self) -> &'static [GoalKind] {
        &[GoalKind::Roam]
    }
    fn reset(&mut self) {
        self.to = None;
    }
    fn propose(&mut self, cx: &mut Cx<'_>, out: &mut Vec<Candidate>) {
        let held = cx.goal.kind == GoalKind::Roam && cx.status == crate::bots::raid::goal::Status::Running;
        if !held || self.to.is_none() {
            self.to = self.draw(cx);
        }
        let Some(to) = self.to else {
            return;
        };
        let minutes = straight_minutes(cx.p.me.at, to);
        out.push(Candidate {
            goal: RaidGoal::new(GoalKind::Roam, Target::Point(to)),
            value: cx.prices.roam_per_minute * minutes,
            success: 1.,
            death: 0.,
            loss: cx.prices.death_loss,
            minutes,
        });
    }
    fn act(&mut self, goal: &RaidGoal, cx: &mut Cx<'_>) -> Step {
        let Target::Point(to) = goal.target else {
            return Step::failed("roam without a point");
        };
        walk(cx, to, "roam")
    }
}
