//! Oracle of P-RAID-Q3-fix2 (docs/backlog/packets/P-RAID-Q3-fix2.md): a pending slot choice with nothing offered is priced by its
//! walk (so Sustain's leave can outrank it), the pick-up stand beside a drop the sim never offers is bounded and banned, the
//! walker is not held by such a drop, The Bargain's giver-to-hag leg is a `Prices` field and its row is a documented switch,
//! and `stood` and `refused` do not survive another goal's turn. Frames are hand-built, as in `decide_fix_tests.rs`.
use super::decide::{def_of_reward, Decide};
use super::walker::Walker;
use crate::bots::paths::{PathBudget, RoomPathService};
use crate::bots::perception::LootFacts;
use crate::bots::planning::PlanMap;
use crate::bots::raid::acts::Act;
use crate::bots::raid::arbiter::{rate, Arbiter};
use crate::bots::raid::exec::travel::{eta_minutes, straight_minutes};
use crate::bots::raid::goal::{Candidate, Cx, Executor, GoalKind, RaidGoal, Status, Step, Target};
use crate::bots::raid::percept::{Me, RaidPercept};
use crate::bots::raid::values::Prices;
use crate::bots::random::Rng;
use crate::bots::types::{
    HudView, OwnerLock, Percept, PersonaId, PersonaParams, SelfView, TierId, TierParams,
};
use rh_protocol::state::{
    WireBuildSummary, WireMobBonusRow, WireQuestAction, WireQuestDrop, WireQuestObjective,
    WireQuestPoint, WireQuestRow, WireQuestSpell, WireQuestView,
};
use rh_protocol::QuestHandle;
use rh_sim::{FactionId, Vec2, WeaponId};

const T0: f64 = 5_000.;
const TICK_MS: f64 = 50.;
/// A stand with nothing offered ends within this (the code's `REFUSED_MS` is 1.5 s) and stays ended this long (`BAN_MS` 30 s).
const BOUND_MS: f64 = 5_000.;
const BANNED_AT_LEAST_MS: f64 = 10_000.;
/// A reward id whose quest the seat wants dropped spells of (`decide_fix_tests.rs` uses it too).
const DROP_REWARD: u8 = 17;

fn handle(n: u32) -> QuestHandle {
    QuestHandle::new(n).expect("a handle")
}

fn pt(v: Vec2) -> WireQuestPoint {
    WireQuestPoint { x: v.x, y: v.y }
}

fn dist(a: Vec2, b: Vec2) -> f64 {
    rh_sim::math::hypot(a.x - b.x, a.y - b.y)
}

fn sword() -> [Option<WeaponId>; 2] {
    [Some(WeaponId::Sword), None]
}

fn row(h: u32, def: &str, giver: Vec2) -> WireQuestRow {
    WireQuestRow {
        handle: handle(h),
        def: def.into(),
        giver: pt(giver),
        state: 1,
        expires_at: 0.,
        participants: Vec::new(),
        carrier: None,
        carrier_actor: None,
        done_by: None,
        can_accept: true,
        owner_accepted: false,
        owner_progress: 0,
        owner_visited: 0,
        name: String::new(),
        giver_label: String::new(),
        brief: String::new(),
        objective: String::new(),
        reward: String::new(),
        owner_completed: false,
        owner_ready: false,
    }
}

fn held(rewards: &[(u8, u8)]) -> Vec<WireQuestSpell> {
    rewards
        .iter()
        .map(|&(slot, reward)| WireQuestSpell {
            slot,
            reward,
            charges: 1,
            ready_at: 0.,
        })
        .collect()
}

fn action(quest: u32, action: u8, target: u8) -> WireQuestAction {
    WireQuestAction {
        quest: handle(quest),
        action,
        target,
        at: Vec::new(),
        label: String::new(),
    }
}

/// The seed1 map's path service and plan, with its camps as places to stand.
struct World {
    plan: PlanMap,
    paths: RoomPathService,
}

impl World {
    fn new() -> Self {
        let path = concat!(
            env!("CARGO_MANIFEST_DIR"),
            "/../rh-sim/src/mapdata/fixtures/seed1.map.json"
        );
        let text = std::fs::read_to_string(path).expect("the seed1 fixture map");
        let file: rh_sim::mapdata::MapExport = serde_json::from_str(&text).expect("rh-map/2 json");
        let arena = file
            .validate()
            .expect("an admitted map")
            .into_arena()
            .expect("an arena");
        let plan = PlanMap::from_arena(&arena);
        let paths = RoomPathService::new(&arena.map, PathBudget::default());
        Self { plan, paths }
    }

    fn camp(&self, i: usize) -> Vec2 {
        Vec2::new(self.plan.camps[i].x, self.plan.camps[i].y)
    }

    /// The camp farthest from `from`, and the nearest one more than 100 u from it.
    fn far_and_near(&self, from: Vec2) -> (Vec2, Vec2) {
        let all: Vec<Vec2> = (0..self.plan.camps.len()).map(|i| self.camp(i)).collect();
        let far = all
            .iter()
            .copied()
            .max_by(|a, b| dist(*a, from).total_cmp(&dist(*b, from)))
            .expect("a camp");
        let near = all
            .iter()
            .copied()
            .filter(|c| dist(*c, from) > 100.)
            .min_by(|a, b| dist(*a, from).total_cmp(&dist(*b, from)))
            .expect("a camp");
        (far, near)
    }

    fn frame(
        &self,
        now: f64,
        at: Vec2,
        quests: WireQuestView,
        summary: Option<WireBuildSummary>,
    ) -> RaidPercept {
        let base = Percept {
            now,
            hud: HudView {
                phase: "active".into(),
                gates_locked: false,
            },
            self_view: SelfView {
                x: at.x,
                y: at.y,
                status: "alive".into(),
                hp: 100.,
                max_hp: 100.,
                owner_lock: Some(OwnerLock::Clear),
                ..SelfView::default()
            },
            ..Percept::default()
        };
        RaidPercept {
            now,
            tick: (now / TICK_MS) as u32,
            base,
            facts: LootFacts::default(),
            toasts: Vec::new(),
            self_key: "p1".into(),
            me: Me {
                at,
                hp: 100.,
                max_hp: 100.,
                alive: true,
                faction: Some(FactionId::Holy),
                weapons: sword(),
                slot: 0,
                effects: 0,
                ready_at: 0.,
                potion_small: 0,
                potion_large: 0,
                potions_healing: 0,
                healing_until: None,
            },
            active: true,
            camps: Vec::new(),
            mobs: Vec::new(),
            givers: Vec::new(),
            players: Vec::new(),
            quests: Some(quests),
            trade: None,
            fields: Vec::new(),
            build: None,
            summary,
            arena: None,
            hit_at: None,
        }
    }

    fn cx<'a>(
        &'a mut self,
        p: &'a RaidPercept,
        prices: &'a Prices,
        goal: &'a RaidGoal,
        rng: &'a mut Rng,
    ) -> Cx<'a> {
        Cx {
            p,
            paths: &mut self.paths,
            plan: &self.plan,
            knowledge: None,
            tier: TierId::Normal,
            persona: PersonaId::Farmer,
            prices,
            still_since: 0.,
            goal,
            status: Status::Running,
            rng,
        }
    }
}

struct Tick {
    out: Vec<Candidate>,
    step: Option<Step>,
}

/// The seat's tick when the arbiter has nothing else to choose: observe, propose, act on the best rate.
fn tick_with(d: &mut Decide, w: &mut World, p: &RaidPercept, prices: &Prices) -> Tick {
    let none = RaidGoal::new(GoalKind::Roam, Target::None);
    let mut rng = Rng::new(7);
    let mut out = Vec::new();
    d.observe(&mut w.cx(p, prices, &none, &mut rng));
    d.propose(&mut w.cx(p, prices, &none, &mut rng), &mut out);
    let best = out
        .iter()
        .max_by(|a, b| rate(a).total_cmp(&rate(b)))
        .map(|c| c.goal.clone());
    let step = best.map(|goal| d.act(&goal, &mut w.cx(p, prices, &goal, &mut rng)));
    Tick { out, step }
}

/// A tick in which another goal has the seat: `observe` and `propose` only, under a Roam goal.
fn idle_tick(d: &mut Decide, w: &mut World, p: &RaidPercept, prices: &Prices) {
    let none = RaidGoal::new(GoalKind::Roam, Target::None);
    let mut rng = Rng::new(7);
    let mut out = Vec::new();
    d.observe(&mut w.cx(p, prices, &none, &mut rng));
    d.propose(&mut w.cx(p, prices, &none, &mut rng), &mut out);
}

fn is_quest(c: &Candidate, def: &str, stage: u8) -> bool {
    c.goal.kind == GoalKind::QuestDecide
        && matches!(&c.goal.target, Target::Quest { def: d, stage: s } if d == def && *s == stage)
}

fn find<'a>(out: &'a [Candidate], def: &str, stage: u8) -> Option<&'a Candidate> {
    out.iter().find(|c| is_quest(c, def, stage))
}

/// Frame by frame from `T0` for `ms`: when the goal is first given up (its step stops running, or it is no longer proposed) and
/// when it is wanted again after that.
fn run_waiting(
    ms: f64,
    goal: (&str, u8),
    frame: impl Fn(&World, f64) -> RaidPercept,
) -> (Option<f64>, Option<f64>) {
    let mut w = World::new();
    let mut d = Decide::default();
    let (mut gave_up, mut came_back) = (None, None);
    let mut at = 0.;
    while at <= ms {
        let p = frame(&w, T0 + at);
        let tick = tick_with(&mut d, &mut w, &p, &Prices::LOCAL);
        let wanted = find(&tick.out, goal.0, goal.1).is_some();
        let failed = tick
            .step
            .as_ref()
            .is_some_and(|s| s.status != Status::Running);
        if gave_up.is_none() && (failed || !wanted) {
            gave_up = Some(at);
        } else if gave_up.is_some() && wanted && !failed && came_back.is_none() {
            came_back = Some(at);
        }
        at += TICK_MS;
    }
    (gave_up, came_back)
}

// ---------------------------------------------------------------------------------------------------------------
// A: a pending slot choice is priced by its walk when nothing is offered
// ---------------------------------------------------------------------------------------------------------------

/// `letters` accepted with its token at `token` (the frame before the seat is hurt), then pending at `seat`, with or without
/// action 8 offered. Returns the candidates of the pending frame.
fn letters_pending_far(w: &mut World, seat: Vec2, token: Vec2, offered: bool) -> Vec<Candidate> {
    let mut r = row(1, "letters", token);
    r.owner_accepted = true;
    let accepted = WireQuestView {
        rows: vec![r.clone()],
        spells: held(&[(0, 3), (1, 4)]),
        objectives: vec![WireQuestObjective {
            quest: handle(1),
            at: pt(token),
        }],
        ..WireQuestView::default()
    };
    let mut d = Decide::default();
    let p1 = w.frame(T0, seat, accepted, None);
    let _ = tick_with(&mut d, w, &p1, &Prices::LOCAL);
    r.owner_ready = true;
    let mut pending = WireQuestView {
        rows: vec![r],
        spells: held(&[(0, 3), (1, 4)]),
        pending: vec![handle(1)],
        ..WireQuestView::default()
    };
    if offered {
        pending.actions = (0..3).map(|t| action(1, 8, t)).collect();
    }
    let p2 = w.frame(T0 + 4_000., seat, pending, None);
    tick_with(&mut d, w, &p2, &Prices::LOCAL).out
}

/// The leave's rate (`SustainPrices::LOCAL`: value 3 over 0.1 minute = 30): the line a pending choice must fall under.
fn leave_rate() -> f64 {
    let s = Prices::LOCAL.sustain;
    s.leave_value / (s.leave_seconds / 60.)
}

#[test]
fn a_pending_choice_with_nothing_offered_costs_the_walk_to_its_turn_in_point() {
    let mut w = World::new();
    let seat = w.camp(0);
    let (far, near) = w.far_and_near(seat);
    let answer = Prices::LOCAL.answer_minutes;
    let mut rates = Vec::new();
    for token in [near, far] {
        let out = letters_pending_far(&mut w, seat, token, false);
        let c = find(&out, "letters", 0).expect("the answer is still wanted");
        let walk = straight_minutes(seat, token);
        assert!(
            c.minutes >= walk + answer - 1e-9,
            "minutes {} for a token {:.0} u away (straight walk {walk})",
            c.minutes,
            dist(seat, token)
        );
        rates.push(rate(c));
    }
    assert!(
        rates[1] < rates[0],
        "the rate falls with distance: {rates:?}"
    );
    assert!(
        rates[1] < leave_rate(),
        "a choice {:.0} u away must rate under the leave's {}: {}",
        dist(seat, far),
        leave_rate(),
        rates[1]
    );
}

#[test]
fn a_pending_choice_on_a_non_token_quest_costs_the_walk_to_its_giver() {
    let mut w = World::new();
    let seat = w.camp(0);
    let (giver, _) = w.far_and_near(seat);
    let mut r = row(1, "hold_the_fort", giver);
    r.owner_accepted = true;
    r.owner_ready = true;
    let quests = WireQuestView {
        rows: vec![r],
        spells: held(&[(0, 3), (1, 4)]),
        pending: vec![handle(1)],
        ..WireQuestView::default()
    };
    let p = w.frame(T0, seat, quests, None);
    let out = tick_with(&mut Decide::default(), &mut w, &p, &Prices::LOCAL).out;
    let c = find(&out, "hold_the_fort", 0).expect("proposed");
    let floor = straight_minutes(seat, giver) + Prices::LOCAL.answer_minutes;
    assert!(
        c.minutes >= floor - 1e-9,
        "minutes {} under the walk {floor}",
        c.minutes
    );
}

#[test]
fn guard_an_offered_answer_costs_the_answer_minutes_at_any_distance() {
    let mut w = World::new();
    let seat = w.camp(0);
    let (far, _) = w.far_and_near(seat);
    let out = letters_pending_far(&mut w, seat, far, true);
    let c = find(&out, "letters", 0).expect("the answer is wanted");
    assert_eq!(c.minutes, Prices::LOCAL.answer_minutes);
}

#[test]
fn the_sustain_leave_is_adopted_over_a_pending_choice_walk_when_the_fight_is_lost() {
    // The reviewer's scene with the death model stood in for by the leave candidate `Sustain` proposes for a lost fight
    // (`exec/sustain.rs` `leave_candidate`: value `leave_value`, `leave_seconds`, kind Sustain): the seat is walking back to a
    // far token for a pending `letters` choice (a committed Decide goal), and the arbiter must adopt the leave.
    let mut w = World::new();
    let seat = w.camp(0);
    let (far, _) = w.far_and_near(seat);
    assert!(
        straight_minutes(seat, far) > 0.1,
        "the scene needs a real walk"
    );
    let mut cands = letters_pending_far(&mut w, seat, far, false);
    let walk = find(&cands, "letters", 0).expect("wanted").goal.clone();
    let s = Prices::LOCAL.sustain;
    cands.push(Candidate {
        goal: RaidGoal::new(GoalKind::Sustain, Target::Camp(0)),
        value: s.leave_value,
        success: 1.,
        death: 0.,
        loss: 0.,
        minutes: s.leave_seconds / 60.,
    });
    let won = Arbiter::default().choose(
        T0 + 4_000.,
        cands,
        (&walk, Status::Running, true),
        TierParams::by_id(TierId::Normal),
        PersonaParams::by_id(PersonaId::Farmer),
        &mut Rng::new(16),
    );
    assert_eq!(won.map(|g| g.kind), Some(GoalKind::Sustain));
}

// ---------------------------------------------------------------------------------------------------------------
// B: the pick-up stand is bounded; the walker is not held by a drop the sim does not offer
// ---------------------------------------------------------------------------------------------------------------

fn drop_view(at: Vec2, drop_at: Vec2, offered: bool) -> WireQuestView {
    WireQuestView {
        drops: vec![WireQuestDrop {
            index: 0,
            reward: DROP_REWARD,
            charges: 1,
            ready_at: 0.,
            at: pt(drop_at),
        }],
        actions: if offered {
            vec![action(1, 9, 0)]
        } else {
            Vec::new()
        },
        spells: Vec::new(),
        rows: vec![row(9, "ember_walk", at)],
        ..WireQuestView::default()
    }
}

fn drop_def() -> &'static str {
    def_of_reward(DROP_REWARD).expect("a spell").0
}

#[test]
fn a_pickup_stand_with_action_9_never_offered_is_given_up_within_a_bounded_wait() {
    let (gave_up, came_back) =
        run_waiting(BOUND_MS + BANNED_AT_LEAST_MS, (drop_def(), 1), |w, now| {
            let at = w.camp(0);
            w.frame(
                now,
                at,
                drop_view(at, Vec2::new(at.x + 10., at.y), false),
                None,
            )
        });
    let gave_up = gave_up.expect("the seat stands beside the drop for ever");
    assert!(gave_up <= BOUND_MS, "given up after {gave_up} ms");
    assert!(
        came_back.is_none_or(|t| t - gave_up >= BANNED_AT_LEAST_MS),
        "back after {:?} ms: the def must stay given up for a while",
        came_back.map(|t| t - gave_up)
    );
}

#[test]
fn guard_a_pickup_is_not_given_up_while_action_9_is_offered() {
    let mut w = World::new();
    let at = w.camp(0);
    let mut d = Decide::default();
    let mut sends = 0;
    for k in 0..160 {
        let p = w.frame(
            T0 + f64::from(k) * TICK_MS,
            at,
            drop_view(at, Vec2::new(at.x + 10., at.y), true),
            None,
        );
        let tick = tick_with(&mut d, &mut w, &p, &Prices::LOCAL);
        let step = tick.step.expect("the pick-up is proposed every tick");
        assert_eq!(step.status, Status::Running, "tick {k}: {step:?}");
        sends += step
            .acts
            .iter()
            .filter(|a| matches!(a, Act::QuestPickup { .. }))
            .count();
    }
    assert!(sends >= 1, "the pick-up is sent");
}

fn walker_candidates(drop: Option<(Vec2, bool)>) -> Vec<Candidate> {
    let mut w = World::new();
    let at = w.camp(0);
    let view = drop.map_or_else(
        || WireQuestView {
            rows: vec![row(9, "ember_walk", at)],
            ..WireQuestView::default()
        },
        |(d, offered)| drop_view(at, d, offered),
    );
    let p = w.frame(T0, at, view, None);
    let goal = RaidGoal::new(GoalKind::Roam, Target::None);
    let mut out = Vec::new();
    let mut rng = Rng::new(16);
    Walker::default().propose(&mut w.cx(&p, &Prices::LOCAL, &goal, &mut rng), &mut out);
    out
}

#[test]
fn guard_the_walker_proposes_a_fresh_quest_with_no_drop() {
    assert!(
        !walker_candidates(None).is_empty(),
        "the scene must give the walker something to propose"
    );
}

#[test]
fn the_walker_is_not_held_by_a_drop_beside_the_seat_that_the_sim_does_not_offer() {
    let at = World::new().camp(0);
    let got = walker_candidates(Some((Vec2::new(at.x + 20., at.y), false)));
    assert!(
        !got.is_empty(),
        "a drop within 32 u with no action 9 must not hold the walker"
    );
}

#[test]
fn guard_the_walker_stays_held_by_a_drop_that_can_be_picked_up_or_is_still_a_walk_away() {
    let at = World::new().camp(0);
    assert!(
        walker_candidates(Some((Vec2::new(at.x + 20., at.y), true))).is_empty(),
        "offered"
    );
    assert!(
        walker_candidates(Some((Vec2::new(at.x + 100., at.y), false))).is_empty(),
        "100 u away"
    );
}

// ---------------------------------------------------------------------------------------------------------------
// C: The Bargain's leg is a price, its row a switch
// ---------------------------------------------------------------------------------------------------------------

fn bargain_view(giver: Vec2, hag: Option<Vec2>, can_accept: bool) -> WireQuestView {
    let mut r = row(2, "the_bargain", giver);
    r.can_accept = can_accept;
    let mut objectives = Vec::new();
    if let Some(hag) = hag {
        r.owner_accepted = true;
        objectives.push(WireQuestObjective {
            quest: handle(2),
            at: pt(hag),
        });
    }
    WireQuestView {
        rows: vec![r],
        objectives,
        ..WireQuestView::default()
    }
}

fn damage_stacks(stacks: u32) -> WireBuildSummary {
    WireBuildSummary {
        mob_rows: vec![WireMobBonusRow {
            mob_type: 0,
            stat: 2,
            stacks,
            total: 0,
        }],
        ..WireBuildSummary::default()
    }
}

/// The Bargain candidate's (minutes, value, walk to the target) for a seat at camp 0, the giver at camp 1 and, once accepted,
/// the hag at camp 2.
fn bargain_candidate(prices: &Prices, accepted: bool) -> (f64, f64, f64) {
    let mut w = World::new();
    let (at, giver, hag) = (w.camp(0), w.camp(1), w.camp(2));
    let target = if accepted { hag } else { giver };
    let walk =
        eta_minutes(&mut w.paths, at, target).unwrap_or_else(|| straight_minutes(at, target));
    let view = bargain_view(giver, accepted.then_some(hag), true);
    let p = w.frame(T0, at, view, Some(damage_stacks(20)));
    let out = tick_with(&mut Decide::default(), &mut w, &p, prices).out;
    let c = find(&out, "the_bargain", 2).expect("the bargain is proposed");
    (c.minutes, c.value, walk)
}

#[test]
fn the_giver_to_hag_leg_is_the_prices_field() {
    let (local, _, walk) = bargain_candidate(&Prices::LOCAL, false);
    assert!(
        (local - (walk + 0.5)).abs() < 1e-9,
        "today's leg is 0.5: {local} vs walk {walk}"
    );
    let long = Prices {
        bargain_leg_minutes: 2.,
        ..Prices::LOCAL
    };
    let (moved, _, _) = bargain_candidate(&long, false);
    assert!(
        (moved - (walk + 2.)).abs() < 1e-9,
        "a leg of 2 minutes: {moved} vs walk {walk}"
    );
}

#[test]
fn guard_an_accepted_bargain_has_no_leg_to_price() {
    let long = Prices {
        bargain_leg_minutes: 2.,
        ..Prices::LOCAL
    };
    let (minutes, _, walk) = bargain_candidate(&long, true);
    assert!((minutes - walk).abs() < 1e-9, "{minutes} vs walk {walk}");
}

#[test]
fn decide_holds_no_timing_literal_for_the_bargain() {
    let source = include_str!("decide.rs");
    assert!(
        !source.contains("+ 0.5"),
        "decide.rs still adds a literal half minute"
    );
}

#[test]
fn guard_the_bargain_row_is_a_switch_not_a_cap() {
    // A table that prices the quest at 0.01 keeps the quest on at the trade's full gain (34/30), as the owner decided for
    // P-RAID-Q3-fix; a cap at the row would make it 0.01.
    let thin = Prices {
        reward_rows: &[("the_bargain", 0.01)],
        ..Prices::LOCAL
    };
    let (_, value, _) = bargain_candidate(&thin, false);
    assert!((value - 34. / 30.).abs() < 1e-9, "{value}");
}

#[test]
fn the_docs_of_the_bargain_row_say_it_is_a_switch() {
    let values = include_str!("../values.rs");
    assert!(
        values.to_lowercase().contains("switch"),
        "values.rs does not document the row as a switch"
    );
}

// ---------------------------------------------------------------------------------------------------------------
// D: a stand's clock does not survive another goal's turn
// ---------------------------------------------------------------------------------------------------------------

/// Wait 1.2 s (under `REFUSED_MS`), let another goal have the seat for 5 s (observe and propose only), come back: the wait
/// starts over, so the first 0.5 s back is still a wait.
fn waits_start_over(frame: impl Fn(&World, f64) -> RaidPercept) {
    let mut w = World::new();
    let mut d = Decide::default();
    let mut now = T0;
    while now < T0 + 1_200. {
        let p = frame(&w, now);
        let step = tick_with(&mut d, &mut w, &p, &Prices::LOCAL)
            .step
            .expect("a step");
        assert_eq!(step.status, Status::Running, "waiting at {now}");
        now += TICK_MS;
    }
    while now < T0 + 6_200. {
        let p = frame(&w, now);
        idle_tick(&mut d, &mut w, &p, &Prices::LOCAL);
        now += TICK_MS;
    }
    for k in 0..10 {
        let p = frame(&w, now);
        let step = tick_with(&mut d, &mut w, &p, &Prices::LOCAL)
            .step
            .expect("a step");
        assert_eq!(
            step.status,
            Status::Running,
            "back for {k} ticks: a stale clock gave the def up at once"
        );
        now += TICK_MS;
    }
}

#[test]
fn a_slot_answer_wait_starts_over_when_another_goal_had_the_seat() {
    waits_start_over(|w, now| {
        let g = w.camp(0);
        let mut r = row(1, "hold_the_fort", g);
        r.owner_accepted = true;
        r.owner_ready = true;
        let quests = WireQuestView {
            rows: vec![r],
            spells: held(&[(0, 3), (1, 4)]),
            pending: vec![handle(1)],
            ..WireQuestView::default()
        };
        w.frame(now, g, quests, None)
    });
}

#[test]
fn a_refusing_giver_wait_starts_over_when_another_goal_had_the_seat() {
    waits_start_over(|w, now| {
        let g = w.camp(0);
        w.frame(
            now,
            g,
            bargain_view(g, None, false),
            Some(damage_stacks(20)),
        )
    });
}
