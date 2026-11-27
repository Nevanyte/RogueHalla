//! Oracle of P-RAID-Q3-fix (docs/backlog/packets/P-RAID-Q3-fix.md): `decide.rs` never holds a seat without progress beyond a
//! bounded wait (a pending token quest walks back to its token; a stand with nothing offered gives the def up for a while),
//! its prices are fields of `Prices`, and The Bargain's candidate is worth the chosen trade's gain. Scenes are hand-built
//! frames fed to one `Decide` in turn (the percept of each tick), as `decide_tests.rs` builds them.
use super::decide::{bargain_trade, spell_worth, stat_worth, stat_worth_at, Decide};
use crate::bots::paths::{PathBudget, RoomPathService};
use crate::bots::perception::LootFacts;
use crate::bots::planning::PlanMap;
use crate::bots::raid::acts::Act;
use crate::bots::raid::goal::{Candidate, Cx, Executor, GoalKind, RaidGoal, Status, Step, Target};
use crate::bots::raid::percept::{Me, RaidPercept};
use crate::bots::raid::values::{Prices, StatFit};
use crate::bots::random::Rng;
use crate::bots::types::{HudView, IntentBody, OwnerLock, Percept, PersonaId, SelfView, TierId};
use rh_protocol::state::{
    WireBuildSummary, WireMobBonusRow, WireQuestAction, WireQuestDrop, WireQuestObjective,
    WireQuestPoint, WireQuestRow, WireQuestSpell, WireQuestView,
};
use rh_protocol::QuestHandle;
use rh_sim::{FactionId, Vec2, WeaponId};

const T0: f64 = 5_000.;
/// One decision tick (20 Hz).
const TICK_MS: f64 = 50.;
/// A wait with nothing offered must end within this (the code's `REFUSED_MS` is 1.5 s; the oracle allows 5 s) ...
const BOUND_MS: f64 = 5_000.;
/// ... and the def stays given up for at least this long (the code's `BAN_MS` is 30 s).
const BANNED_AT_LEAST_MS: f64 = 10_000.;

fn handle(n: u32) -> QuestHandle {
    QuestHandle::new(n).expect("a handle")
}

fn pt(v: Vec2) -> WireQuestPoint {
    WireQuestPoint { x: v.x, y: v.y }
}

fn dist(a: Vec2, b: Vec2) -> f64 {
    rh_sim::math::hypot(a.x - b.x, a.y - b.y)
}

fn me_with(weapons: [Option<WeaponId>; 2], at: Vec2) -> Me {
    Me {
        at,
        hp: 100.,
        max_hp: 100.,
        alive: true,
        faction: Some(FactionId::Holy),
        weapons,
        slot: 0,
        effects: 0,
        ready_at: 0.,
        potion_small: 0,
        potion_large: 0,
        potions_healing: 0,
        healing_until: None,
    }
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

/// The world a frame is built on: the seed1 map's path service and plan, with its camps as places to stand.
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

    /// The percept of the tick at `now`, the owner at `at`.
    fn frame(
        &self,
        now: f64,
        at: Vec2,
        weapons: [Option<WeaponId>; 2],
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
            me: me_with(weapons, at),
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

/// What one tick of the seat gives: the candidates `decide` proposes, and its step on the best of them (the arbiter has
/// nothing else to choose in these scenes: no other executor is present).
struct Tick {
    out: Vec<Candidate>,
    step: Option<Step>,
}

fn tick_with(d: &mut Decide, w: &mut World, p: &RaidPercept, prices: &Prices) -> Tick {
    let none = RaidGoal::new(GoalKind::Roam, Target::None);
    let mut rng = Rng::new(7);
    let mut out = Vec::new();
    d.observe(&mut w.cx(p, prices, &none, &mut rng));
    d.propose(&mut w.cx(p, prices, &none, &mut rng), &mut out);
    let rate = |c: &Candidate| c.value / c.minutes.max(0.05);
    let best = out
        .iter()
        .max_by(|a, b| rate(a).total_cmp(&rate(b)))
        .map(|c| c.goal.clone());
    let step = best.map(|goal| d.act(&goal, &mut w.cx(p, prices, &goal, &mut rng)));
    Tick { out, step }
}

fn quest_goal(out: &[Candidate], def: &str, stage: u8) -> bool {
    out.iter().any(|c| {
        c.goal.kind == GoalKind::QuestDecide
            && matches!(&c.goal.target, Target::Quest { def: d, stage: s } if d == def && *s == stage)
    })
}

fn first_move(step: &Step) -> Option<Vec2> {
    step.intents.iter().find_map(|i| match &i.body {
        IntentBody::Move { to } => Some(*to),
        _ => None,
    })
}

// ---------------------------------------------------------------------------------------------------------------
// The freeze: a goal held with no progress (P-RAID-Q3 review note 1)
// ---------------------------------------------------------------------------------------------------------------

/// `letters` pending (the slot choice waits at the token), the seat at `at`, no action offered: the frame at `now`.
fn pending_letters(w: &World, now: f64, at: Vec2, giver: Vec2) -> RaidPercept {
    let mut r = row(1, "letters", giver);
    r.owner_accepted = true;
    r.owner_ready = true;
    let quests = WireQuestView {
        rows: vec![r],
        spells: held(&[(0, 3), (1, 4)]),
        pending: vec![handle(1)],
        ..WireQuestView::default()
    };
    w.frame(now, at, sword(), quests, None)
}

/// Frame by frame from `T0` for `ms`: when the def is first given up (its step stops running, or it is no longer proposed),
/// and when it is wanted again after that. `frame` builds each tick's percept.
fn run_waiting(
    ms: f64,
    wanted_goal: (&str, u8),
    mut frame: impl FnMut(f64) -> RaidPercept,
) -> (Option<f64>, Option<f64>) {
    let mut w = World::new();
    let mut d = Decide::default();
    let prices = Prices::LOCAL;
    let (mut gave_up, mut came_back) = (None, None);
    let mut at = 0.;
    while at <= ms {
        let p = frame(T0 + at);
        let tick = tick_with(&mut d, &mut w, &p, &prices);
        let wanted = quest_goal(&tick.out, wanted_goal.0, wanted_goal.1);
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

#[test]
fn a_pending_choice_with_nothing_offered_at_the_giver_is_given_up_within_a_bounded_wait() {
    // The reviewer's scene: letters pending, the seat standing at its giver (the token is elsewhere and not in the view),
    // action 8 offered nowhere. Today the goal stands there for ever.
    let w = World::new();
    let g = w.camp(0);
    let (gave_up, came_back) = run_waiting(BOUND_MS + BANNED_AT_LEAST_MS, ("letters", 0), |now| {
        pending_letters(&w, now, g, g)
    });
    let gave_up = gave_up.expect("the seat waits for ever at the giver of a pending token quest");
    assert!(gave_up <= BOUND_MS, "given up after {gave_up} ms");
    assert!(
        came_back.is_none_or(|t| t - gave_up >= BANNED_AT_LEAST_MS),
        "back after {:?} ms: the def must stay given up for a while",
        came_back.map(|t| t - gave_up)
    );
}

#[test]
fn a_pending_choice_is_not_given_up_while_the_server_offers_the_answer() {
    // At the token the frame offers action 8: the seat answers (a send in the first ticks) and keeps the goal for 8 s of
    // resends. Guard: the ban must not fire while the sim is being answered.
    let mut w = World::new();
    let g = w.camp(0);
    let mut d = Decide::default();
    let mut sends = 0;
    for k in 0..160 {
        let mut p = pending_letters(&w, T0 + f64::from(k) * TICK_MS, g, g);
        if let Some(q) = p.quests.as_mut() {
            q.actions = (0..3)
                .map(|target| WireQuestAction {
                    quest: handle(1),
                    action: 8,
                    target,
                    at: Vec::new(),
                    label: String::new(),
                })
                .collect();
        }
        let tick = tick_with(&mut d, &mut w, &p, &Prices::LOCAL);
        let step = tick.step.expect("the answer is proposed every tick");
        assert_eq!(step.status, Status::Running, "tick {k}: {step:?}");
        sends += step
            .acts
            .iter()
            .filter(|a| matches!(a, Act::QuestInteract { action: 8, .. }))
            .count();
    }
    assert!(sends >= 1, "the answer is sent");
}

#[test]
fn a_seat_walking_to_a_far_turn_in_point_is_not_given_up_for_being_slow() {
    // Guard: the wait is bounded only for a seat standing in reach with nothing offered. A seat far from the giver (pushed
    // back, or walking round a wall) keeps its goal for 20 s.
    let w = World::new();
    let (seat, giver) = (w.camp(0), w.camp(2));
    assert!(dist(seat, giver) > 200., "the scene needs a real walk");
    let (gave_up, _) = run_waiting(20_000., ("letters", 0), |now| {
        pending_letters(&w, now, seat, giver)
    });
    assert_eq!(gave_up, None);
}

/// A seat, a token and a giver, the token and the giver on opposite sides of the seat, 300 to 900 u away.
fn far_apart(w: &World) -> (Vec2, Vec2, Vec2) {
    let n = w.plan.camps.len();
    for a in 0..n {
        for b in 0..n {
            for c in 0..n {
                let (s, t, g) = (w.camp(a), w.camp(b), w.camp(c));
                let (u, v) = ((t.x - s.x, t.y - s.y), (g.x - s.x, g.y - s.y));
                let (lu, lv) = (rh_sim::math::hypot(u.0, u.1), rh_sim::math::hypot(v.0, v.1));
                if a != b && a != c && (300. ..900.).contains(&lu) && (300. ..900.).contains(&lv) {
                    let cos = (u.0 * v.0 + u.1 * v.1) / (lu * lv);
                    if cos < -0.5 {
                        return (s, t, g);
                    }
                }
            }
        }
    }
    panic!("no three camps with the token and the giver on opposite sides of the seat");
}

#[test]
fn a_pending_choice_on_a_token_quest_walks_back_to_the_token_not_the_giver() {
    // letters is accepted and its objective is the token T (frame 1). The seat is later pushed away before its answer lands:
    // pending, no objective (the sim shows none for a pending row), no action 8, the seat at S, far from both T and the giver
    // G. The pending frame never shows the token, so the seat must have kept it from the accepted frame.
    let mut w = World::new();
    let (s, t, g) = far_apart(&w);
    let mut d = Decide::default();
    let mut r = row(1, "letters", g);
    r.owner_accepted = true;
    let accepted = WireQuestView {
        rows: vec![r],
        spells: held(&[(0, 3)]),
        objectives: vec![WireQuestObjective {
            quest: handle(1),
            at: pt(t),
        }],
        ..WireQuestView::default()
    };
    let p1 = w.frame(T0, s, sword(), accepted, None);
    let _ = tick_with(&mut d, &mut w, &p1, &Prices::LOCAL);
    let p2 = pending_letters(&w, T0 + 4_000., s, g);
    let tick = tick_with(&mut d, &mut w, &p2, &Prices::LOCAL);
    assert!(
        quest_goal(&tick.out, "letters", 0),
        "the answer is still wanted"
    );
    let step = tick.step.expect("a step");
    let to = first_move(&step).expect("it walks");
    assert!(
        dist(to, t) < dist(s, t) - 5.,
        "toward the token: from {s:?} to {to:?}, token {t:?}, giver {g:?}"
    );
    assert!(
        dist(to, g) > dist(s, g) - 5.,
        "not toward the giver: from {s:?} to {to:?}, giver {g:?}"
    );
}

fn bargain_view(giver: Vec2, accepted_objective: Option<Vec2>) -> WireQuestView {
    let mut r = row(2, "the_bargain", giver);
    let mut objectives = Vec::new();
    if let Some(hag) = accepted_objective {
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

/// A build summary with `stacks` stacks of Bargain stat 0 (damage; HUD stat code 2).
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

#[test]
fn a_hag_stand_with_no_trade_offered_is_given_up_within_a_bounded_wait() {
    // Accepted, the seat standing on the hag, its own count says 20 damage stacks (a trade exists) but the frame offers none.
    let w = World::new();
    let (hag, giver) = (w.camp(1), w.camp(2));
    let (gave_up, came_back) =
        run_waiting(BOUND_MS + BANNED_AT_LEAST_MS, ("the_bargain", 2), |now| {
            w.frame(
                now,
                hag,
                sword(),
                bargain_view(giver, Some(hag)),
                Some(damage_stacks(20)),
            )
        });
    let gave_up = gave_up.expect("the seat stands at the hag for ever");
    assert!(gave_up <= BOUND_MS, "given up after {gave_up} ms");
    assert!(
        came_back.is_none_or(|t| t - gave_up >= BANNED_AT_LEAST_MS),
        "back after {:?} ms",
        came_back.map(|t| t - gave_up)
    );
}

// ---------------------------------------------------------------------------------------------------------------
// The prices are fields of `Prices`
// ---------------------------------------------------------------------------------------------------------------

#[test]
fn the_local_decision_prices_are_the_numbers_the_executor_used() {
    let p = Prices::LOCAL;
    assert_eq!(p.spare_weapon, 1.5);
    assert_eq!(p.neutral_stat, 2.5);
    assert_eq!(
        p.stat_fit,
        StatFit {
            matched: 1.,
            fists: 0.5,
            wand_attack: -0.5,
            mismatched: -1.
        }
    );
    assert_eq!(p.death_floor, 0.02);
    assert_eq!(p.answer_minutes, 0.01);
    assert_eq!(p.recharging_share, 0.5);
    // A table that turns the quest family off keeps the same decision prices (they hold no reward).
    assert_eq!(Prices::farm_only().spare_weapon, 1.5);
    let me = me_with(sword(), Vec2::new(0., 0.));
    for stat in 0..9 {
        assert_eq!(stat_worth_at(&p, stat, &me), stat_worth(stat, &me));
    }
}

#[test]
fn a_stats_worth_follows_the_neutral_and_fit_prices() {
    let me = me_with(sword(), Vec2::new(0., 0.));
    let doctored = Prices {
        neutral_stat: 4.,
        stat_fit: StatFit {
            matched: 2.,
            mismatched: -2.,
            ..StatFit::LOCAL
        },
        ..Prices::LOCAL
    };
    // A sword: attack damage matches (+2), ability power does not (-2), the rest are neutral.
    assert_eq!(stat_worth_at(&doctored, 6, &me), 6.);
    assert_eq!(stat_worth_at(&doctored, 3, &me), 2.);
    assert_eq!(stat_worth_at(&doctored, 0, &me), 4.);
    let wand = me_with([Some(WeaponId::Wand), None], Vec2::new(0., 0.));
    assert_eq!(stat_worth_at(&doctored, 6, &wand), 3.5);
    let bare = me_with([None, None], Vec2::new(0., 0.));
    assert_eq!(stat_worth_at(&doctored, 6, &bare), 4.5);
}

#[test]
fn the_spare_weapons_price_decides_whether_it_is_traded() {
    // At the regular spare price, even the preferred stat does not cover the weapon.
    let me = me_with(sword(), Vec2::new(0., 0.));
    assert_eq!(bargain_trade(&Prices::LOCAL, &me, &[(7, 6)]), None);
    let between = Prices {
        spare_weapon: 1.1,
        ..Prices::LOCAL
    };
    assert_eq!(bargain_trade(&between, &me, &[(7, 6)]), Some((7, 6)));
    assert_eq!(bargain_trade(&between, &me, &[(7, 0)]), None);
    let cheap = Prices {
        spare_weapon: 0.75,
        ..Prices::LOCAL
    };
    assert_eq!(bargain_trade(&cheap, &me, &[(7, 0)]), Some((7, 0)));
}

#[test]
fn the_fit_prices_decide_whether_a_stack_trade_is_taken() {
    // Damage stacks (2.5) into attack damage (3.5 for a sword): taken on LOCAL; with the match worth less than the
    // mismatch the received stat is worth 1.5 and the swap loses.
    let me = me_with(sword(), Vec2::new(0., 0.));
    assert_eq!(bargain_trade(&Prices::LOCAL, &me, &[(5, 6)]), Some((5, 6)));
    let against = Prices {
        stat_fit: StatFit {
            matched: -1.,
            ..StatFit::LOCAL
        },
        ..Prices::LOCAL
    };
    assert_eq!(bargain_trade(&against, &me, &[(5, 6)]), None);
}

#[test]
fn the_recharging_share_prices_a_spell_with_no_charge() {
    // Cleanse (reward 16, pilgrims_path 2.0, reusable) with no charge: half on LOCAL, the doctored share otherwise.
    assert_eq!(spell_worth(&Prices::LOCAL, 16, 0), 1.0);
    let shy = Prices {
        recharging_share: 0.25,
        ..Prices::LOCAL
    };
    assert_eq!(spell_worth(&shy, 16, 0), 0.5);
    assert_eq!(spell_worth(&shy, 16, 1), 2.0);
}

#[test]
fn the_answer_minutes_and_the_death_floor_are_read_from_the_prices() {
    let mut w = World::new();
    let g = w.camp(0);
    let doctored = Prices {
        answer_minutes: 0.02,
        death_floor: 0.05,
        ..Prices::LOCAL
    };
    // A pending slot choice: its minutes are the answer minutes.
    let mut d = Decide::default();
    let p = pending_letters(&w, T0, g, g);
    let tick = tick_with(&mut d, &mut w, &p, &doctored);
    let answer = tick
        .out
        .iter()
        .find(|c| quest_goal(std::slice::from_ref(c), "letters", 0))
        .expect("the answer is proposed");
    assert_eq!(answer.minutes, 0.02);
    // A dropped spell to walk to (lying 40 u away): its death chance is the floor.
    let drop = WireQuestDrop {
        index: 0,
        reward: 17,
        charges: 1,
        ready_at: 0.,
        at: WireQuestPoint {
            x: g.x + 40.,
            y: g.y,
        },
    };
    let quests = WireQuestView {
        drops: vec![drop],
        ..WireQuestView::default()
    };
    let p = w.frame(T0, g, sword(), quests, None);
    let mut d = Decide::default();
    let tick = tick_with(&mut d, &mut w, &p, &doctored);
    let pick = tick
        .out
        .iter()
        .find(|c| quest_goal(std::slice::from_ref(c), "watchtowers", 1))
        .expect("the pick-up is proposed");
    assert_eq!(pick.death, 0.05);
}

#[test]
fn decide_declares_none_of_the_moved_constants() {
    let source = include_str!("decide.rs");
    for name in [
        "SPARE_WEAPON_WORTH",
        "NEUTRAL_STAT",
        "DEATH_FLOOR",
        "ANSWER_MINUTES",
        "SPELL_SLOTS",
    ] {
        let declared = source.lines().any(|l| {
            let l = l.trim_start();
            (l.starts_with("const ")
                || l.starts_with("pub const ")
                || l.starts_with("pub(crate) const "))
                && l.contains(&format!("{name}:"))
        });
        assert!(!declared, "decide.rs still declares `{name}`");
    }
}

// ---------------------------------------------------------------------------------------------------------------
// The Bargain is valued by the chosen trade's gain, once
// ---------------------------------------------------------------------------------------------------------------

/// The value of the Bargain candidate for a seat at camp 0 with `weapons` and `stacks` damage stacks, not yet accepted.
fn bargain_value(prices: &Prices, weapons: [Option<WeaponId>; 2], stacks: u32) -> Option<f64> {
    let mut w = World::new();
    let (at, giver) = (w.camp(0), w.camp(1));
    let summary = (stacks > 0).then(|| damage_stacks(stacks));
    let p = w.frame(T0, at, weapons, bargain_view(giver, None), summary);
    let mut d = Decide::default();
    tick_with(&mut d, &mut w, &p, prices)
        .out
        .iter()
        .find(|c| quest_goal(std::slice::from_ref(c), "the_bargain", 2))
        .map(|c| c.value)
}

fn close(a: Option<f64>, b: f64) -> bool {
    a.is_some_and(|a| (a - b).abs() < 1e-9)
}

#[test]
fn the_bargains_candidate_is_worth_the_gain_of_the_stack_trade() {
    let rules = rh_sim::quests::public_rules();
    let expected = (3.5 * rules.bargain_stacks as f64 - 2.5 * rules.bargain_cost as f64) / 30.;
    let got = bargain_value(&Prices::LOCAL, sword(), rules.bargain_cost as u32);
    assert!(close(got, expected), "{got:?}");
}

#[test]
fn the_bargains_candidate_is_worth_the_gain_of_the_weapon_trade() {
    let rules = rh_sim::quests::public_rules();
    let prices = Prices {
        spare_weapon: 0.75,
        ..Prices::LOCAL
    };
    let expected = 3.5 * rules.bargain_stacks as f64 / 30. - prices.spare_weapon;
    let two = [Some(WeaponId::Sword), Some(WeaponId::Axe)];
    let got = bargain_value(&prices, two, 0);
    assert!(close(got, expected), "{got:?}");
    let stack_gain = (3.5 * rules.bargain_stacks as f64 - 2.5 * rules.bargain_cost as f64) / 30.;
    let got = bargain_value(&prices, two, rules.bargain_cost as u32);
    assert!(close(got, stack_gain), "{got:?}");
}

#[test]
fn a_small_trade_is_not_priced_as_the_table_row() {
    // Set the spare value just below the return to check that a small positive trade keeps its own price.
    let full = 3.5 * rh_sim::quests::public_rules().bargain_stacks as f64 / 30.;
    let dear = Prices {
        spare_weapon: full - 0.3,
        ..Prices::LOCAL
    };
    let two = [Some(WeaponId::Sword), Some(WeaponId::Axe)];
    let got = bargain_value(&dear, two, 0);
    assert!(close(got, 0.3), "{got:?}");
}

#[test]
fn a_table_that_prices_the_bargain_at_nothing_proposes_none_and_so_does_a_build_with_no_trade() {
    // Guards, green today: the gate on the table's row stays, and nothing to trade proposes nothing.
    let two = [Some(WeaponId::Sword), Some(WeaponId::Axe)];
    assert_eq!(bargain_value(&Prices::farm_only(), two, 20), None);
    let off = Prices {
        reward_rows: &[("the_bargain", 0.)],
        ..Prices::LOCAL
    };
    assert_eq!(bargain_value(&off, two, 20), None);
    let short = (rh_sim::quests::public_rules().bargain_cost - 1) as u32;
    assert_eq!(bargain_value(&Prices::LOCAL, sword(), short), None);
}
