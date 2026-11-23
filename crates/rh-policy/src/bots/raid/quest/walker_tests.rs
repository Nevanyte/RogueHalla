//! Walker tests (P-RAID-Q1 review notes): an accepted quest is held against a fresh, better-rate one and released when it
//! cannot finish, a Sustain still pre-empts it, and the spell slots count quests accepted but not finished.
use super::walker::{Walker, WALKER};
use crate::bots::paths::{PathBudget, RoomPathService};
use crate::bots::perception::LootFacts;
use crate::bots::planning::PlanMap;
use crate::bots::raid::arbiter::{rate, Arbiter};
use crate::bots::raid::goal::{Candidate, Cx, Executor, GoalKind, RaidGoal, Status, Step, Target};
use crate::bots::raid::percept::{Me, RaidPercept};
use crate::bots::raid::values::Prices;
use crate::bots::random::Rng;
use crate::bots::types::{
    HudView, OwnerLock, Percept, PersonaId, PersonaParams, SelfView, TierId, TierParams,
};
use rh_protocol::state::{
    WireQuestObjective, WireQuestPoint, WireQuestRow, WireQuestSpell, WireQuestView,
};
use rh_protocol::QuestHandle;
use rh_sim::quests::board::catalog::{RewardKind, DEFINITIONS};
use rh_sim::{FactionId, Vec2};

const NOW: f64 = 5_000.;

struct Scene {
    plan: PlanMap,
    paths: RoomPathService,
    p: RaidPercept,
}

fn row(handle: u32, def: &str, giver: Vec2, accepted: bool, expires_at: f64) -> WireQuestRow {
    WireQuestRow {
        handle: QuestHandle::new(handle).expect("a handle"),
        def: def.into(),
        giver: WireQuestPoint {
            x: giver.x,
            y: giver.y,
        },
        state: 1,
        expires_at,
        participants: Vec::new(),
        carrier: None,
        carrier_actor: None,
        done_by: None,
        can_accept: !accepted,
        owner_accepted: accepted,
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

/// The owner at camp 0 of the seed1 map in an active raid, holding the view `view` builds.
fn scene(view: impl FnOnce(Vec2, &PlanMap) -> WireQuestView) -> Scene {
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
    let at = Vec2::new(plan.camps[0].x, plan.camps[0].y);
    let quests = view(at, &plan);
    let base = Percept {
        now: NOW,
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
    let p = RaidPercept {
        now: NOW,
        tick: 100,
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
            weapons: [None, None],
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
        summary: None,
        arena: None,
        hit_at: None,
    };
    Scene { plan, paths, p }
}

fn with_cx<R>(s: &mut Scene, goal: &RaidGoal, f: impl FnOnce(&mut Cx<'_>) -> R) -> R {
    let mut rng = Rng::new(16);
    let mut cx = Cx {
        p: &s.p,
        paths: &mut s.paths,
        plan: &s.plan,
        knowledge: None,
        tier: TierId::Normal,
        persona: PersonaId::Farmer,
        prices: &Prices::LOCAL,
        still_since: 0.,
        goal,
        status: Status::Running,
        rng: &mut rng,
    };
    f(&mut cx)
}

fn propose(s: &mut Scene) -> Vec<Candidate> {
    let goal = RaidGoal::new(GoalKind::Roam, Target::None);
    let mut out = Vec::new();
    with_cx(s, &goal, |cx| Walker::default().propose(cx, &mut out));
    out
}

fn goal_of(def: &str) -> RaidGoal {
    RaidGoal::new(
        GoalKind::QuestWalk,
        Target::Quest {
            def: def.into(),
            stage: 0,
        },
    )
}

fn def_of(c: &Candidate) -> &str {
    match &c.goal.target {
        Target::Quest { def, .. } => def,
        _ => "",
    }
}

fn is_spell(def: &str) -> bool {
    DEFINITIONS
        .iter()
        .find(|d| d.id == def)
        .is_some_and(|d| d.reward.kind() != RewardKind::Permanent)
}

/// A far objective on the map (the camp farthest from `at`).
fn far_point(at: Vec2, plan: &PlanMap) -> Vec2 {
    let d = |p: &Vec2| rh_sim::math::hypot(p.x - at.x, p.y - at.y);
    plan.camps
        .iter()
        .map(|c| Vec2::new(c.x, c.y))
        .max_by(|a, b| d(a).total_cmp(&d(b)))
        .expect("a camp")
}

/// An accepted long quest with `left_ms` on its timer (objective far away) and a fresh short one at the owner's feet.
fn held_and_fresh(left_ms: f64) -> Scene {
    scene(|at, plan| {
        let far = far_point(at, plan);
        WireQuestView {
            rows: vec![
                row(1, "watchtowers", at, true, NOW + left_ms),
                row(2, "ember_walk", at, false, 0.),
            ],
            objectives: vec![WireQuestObjective {
                quest: QuestHandle::new(1).expect("a handle"),
                at: WireQuestPoint { x: far.x, y: far.y },
            }],
            ..WireQuestView::default()
        }
    })
}

fn choose(cands: Vec<Candidate>, held: &RaidGoal) -> Option<RaidGoal> {
    Arbiter::default().choose(
        NOW,
        cands,
        (held, Status::Running, true),
        TierParams::by_id(TierId::Normal),
        PersonaParams::by_id(PersonaId::Farmer),
        &mut Rng::new(16),
    )
}

fn by_def<'a>(cands: &'a [Candidate], def: &str) -> &'a Candidate {
    cands
        .iter()
        .find(|c| def_of(c) == def)
        .unwrap_or_else(|| panic!("no candidate for {def}: {cands:?}"))
}

#[test]
fn an_accepted_quest_is_kept_against_a_better_rate_one_and_released_when_it_cannot_finish() {
    let held = goal_of("watchtowers");
    // 10 minutes left: it finishes, and the walk is committed.
    let mut s = held_and_fresh(600_000.);
    let cands = propose(&mut s);
    let kept = by_def(&cands, "watchtowers");
    let fresh = by_def(&cands, "ember_walk");
    assert!(rate(kept) > 0., "{kept:?}");
    assert!(
        rate(fresh) > rate(kept),
        "precondition: the fresh quest ranks higher on its own: {fresh:?} vs {kept:?}"
    );
    let step: Step = with_cx(&mut s, &held, |cx| Walker::default().act(&held, cx));
    assert!(step.committed && step.status == Status::Running, "{step:?}");
    assert_eq!(choose(cands.clone(), &held), Some(held.clone()));
    // A Sustain candidate still takes over.
    let sustain = Candidate {
        goal: RaidGoal::new(GoalKind::Sustain, Target::None),
        value: 3.,
        success: 1.,
        death: 0.,
        loss: 0.,
        minutes: 0.2,
    };
    let mut with_sustain = cands;
    with_sustain.push(sustain);
    assert_eq!(
        choose(with_sustain, &held),
        Some(RaidGoal::new(GoalKind::Sustain, Target::None))
    );
    // One second left: it cannot finish, the walker proposes nothing for it, and the fresh quest wins.
    let mut late = held_and_fresh(1_000.);
    let cands = propose(&mut late);
    assert!(
        cands.iter().all(|c| def_of(c) != "watchtowers"),
        "{cands:?}"
    );
    assert_eq!(choose(cands, &held), Some(goal_of("ember_walk")));
}

#[test]
fn a_quest_accepted_and_unfinished_takes_a_spell_slot_before_a_second_is_accepted() {
    let spells: Vec<&str> = WALKER.iter().copied().filter(|d| is_spell(d)).collect();
    let permanent: Vec<&str> = WALKER.iter().copied().filter(|d| !is_spell(d)).collect();
    assert!(spells.len() >= 2 && !permanent.is_empty());
    let (accepted, second, perm) = (spells[0], spells[1], permanent[0]);
    let mut s = scene(|at, plan| {
        let far = far_point(at, plan);
        WireQuestView {
            rows: vec![
                row(1, accepted, at, true, NOW + 600_000.),
                row(2, second, at, false, 0.),
                row(3, perm, at, false, 0.),
            ],
            objectives: vec![WireQuestObjective {
                quest: QuestHandle::new(1).expect("a handle"),
                at: WireQuestPoint { x: far.x, y: far.y },
            }],
            // One quest spell already held: with the accepted quest's reward, both slots are spoken for.
            spells: vec![WireQuestSpell {
                slot: 0,
                reward: 0,
                charges: 1,
                ready_at: 0.,
            }],
            ..WireQuestView::default()
        }
    });
    let cands = propose(&mut s);
    assert!(cands.iter().any(|c| def_of(c) == accepted), "{cands:?}");
    assert!(
        cands.iter().all(|c| def_of(c) != second),
        "a second spell-reward quest would stick in pending: {cands:?}"
    );
    assert!(
        cands.iter().any(|c| def_of(c) == perm),
        "a permanent reward uses no slot: {cands:?}"
    );
}
