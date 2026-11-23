//! Dwell tests (P-RAID-Q2 review round): a hold is waited out for as long as redoing it would take, repeated losses ban the
//! quest for longer, and every dead end ends in a ban instead of an endless committed or failing step.
use super::dwell::Dwell;
use crate::bots::paths::{PathBudget, RoomPathService};
use crate::bots::perception::LootFacts;
use crate::bots::planning::PlanMap;
use crate::bots::raid::goal::{Candidate, Cx, Executor, GoalKind, RaidGoal, Status, Step, Target};
use crate::bots::raid::percept::{Me, RaidPercept};
use crate::bots::raid::values::Prices;
use crate::bots::random::Rng;
use crate::bots::types::{HudView, OwnerLock, Percept, PersonaId, SelfView, TierId};
use rh_protocol::state::{WireQuestObjective, WireQuestPoint, WireQuestRow, WireQuestView};
use rh_protocol::QuestHandle;
use rh_sim::{FactionId, Vec2};

const HOLD: &str = "hold_the_fort";
const STAY: &str = "stay_a_while";
const S: f64 = 1_000.;

struct Scene {
    plan: PlanMap,
    paths: RoomPathService,
    p: RaidPercept,
    at: Vec2,
}

fn row(def: &str, giver: Vec2) -> WireQuestRow {
    WireQuestRow {
        handle: QuestHandle::new(1).expect("a handle"),
        def: def.into(),
        giver: WireQuestPoint {
            x: giver.x,
            y: giver.y,
        },
        state: 1,
        expires_at: 0.,
        participants: Vec::new(),
        carrier: None,
        carrier_actor: None,
        done_by: None,
        can_accept: false,
        owner_accepted: true,
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

/// The owner at camp 0 of the seed1 map in an active raid, holding one accepted dwell quest. `giver_here`: the giver
/// stands at the owner (else 2000 u away); `token`: the fort token at the owner, if any.
fn scene(def: &str, giver_here: bool, token: bool) -> Scene {
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
    let giver = if giver_here {
        at
    } else {
        Vec2::new(at.x + 2_000., at.y)
    };
    let quests = WireQuestView {
        rows: vec![row(def, giver)],
        objectives: if token {
            vec![WireQuestObjective {
                quest: QuestHandle::new(1).expect("a handle"),
                at: WireQuestPoint { x: at.x, y: at.y },
            }]
        } else {
            Vec::new()
        },
        ..WireQuestView::default()
    };
    let base = Percept {
        now: 5_000.,
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
        now: 5_000.,
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
    Scene { plan, paths, p, at }
}

impl Scene {
    fn row(&mut self) -> &mut WireQuestRow {
        &mut self.p.quests.as_mut().expect("a board").rows[0]
    }
    fn at_ms(&mut self, now: f64) {
        self.p.now = now;
    }
    fn cx<R>(&mut self, goal: &RaidGoal, f: impl FnOnce(&mut Cx<'_>) -> R) -> R {
        let mut rng = Rng::new(16);
        let mut cx = Cx {
            p: &self.p,
            paths: &mut self.paths,
            plan: &self.plan,
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
    fn act(&mut self, d: &mut Dwell, def: &str) -> Step {
        let goal = goal_of(def);
        self.cx(&goal, |cx| d.act(&goal, cx))
    }
    fn propose(&mut self, d: &mut Dwell) -> Vec<Candidate> {
        let goal = RaidGoal::new(GoalKind::Roam, Target::None);
        let mut out = Vec::new();
        self.cx(&goal, |cx| d.propose(cx, &mut out));
        out
    }
}

fn goal_of(def: &str) -> RaidGoal {
    RaidGoal::new(
        GoalKind::QuestDwell,
        Target::Quest {
            def: def.into(),
            stage: 0,
        },
    )
}

fn kept(step: &Step) -> bool {
    step.status == Status::Running && step.committed
}

/// A hold at `progress` s on the token, the reading flat from 100 s on: the step at each flat time in `flat_s`.
fn flat_steps(progress: u32, flat_s: &[f64]) -> Vec<Step> {
    let mut s = scene(HOLD, false, true);
    let mut d = Dwell::default();
    s.row().owner_progress = progress;
    s.at_ms(100. * S);
    let first = s.act(&mut d, HOLD);
    assert!(kept(&first), "the first read is held: {first:?}");
    flat_s
        .iter()
        .map(|t| {
            s.at_ms(100. * S + t * S);
            s.act(&mut d, HOLD)
        })
        .collect()
}

#[test]
fn a_hold_is_waited_out_for_as_long_as_redoing_it_would_take() {
    // 40 s held: kept at 10 s and 30 s flat, still kept at 45 s, released only past base (6 s) plus 40 s.
    let steps = flat_steps(40, &[10., 30., 45.5, 46.5]);
    assert!(
        kept(&steps[0]) && kept(&steps[1]) && kept(&steps[2]),
        "{steps:?}"
    );
    assert_eq!(
        steps[3].status,
        Status::Failed,
        "released after 46 s: {:?}",
        steps[3]
    );
    // Nothing held: released after the base alone.
    let steps = flat_steps(0, &[5., 6.5]);
    assert!(kept(&steps[0]), "{:?}", steps[0]);
    assert_eq!(steps[1].status, Status::Failed, "{:?}", steps[1]);
}

#[test]
fn a_player_in_sight_does_not_shorten_the_wait() {
    let mut s = scene(HOLD, false, true);
    let mut d = Dwell::default();
    s.row().owner_progress = 40;
    s.p.players.push(crate::bots::raid::percept::PlayerSeen {
        key: "p2".into(),
        at: s.at,
        hp_share: 1.,
        alive: true,
        targetable: true,
        faction: None,
        weapon: None,
    });
    s.at_ms(100. * S);
    s.act(&mut d, HOLD);
    s.at_ms(120. * S);
    let step = s.act(&mut d, HOLD);
    assert!(
        kept(&step),
        "20 s flat with the contestant in sight: {step:?}"
    );
}

#[test]
fn the_ban_lengthens_with_each_lost_hold() {
    let mut s = scene(HOLD, false, true);
    let mut d = Dwell::default();
    let mut now = 100. * S;
    // (ban in seconds that must still hold just before its end, and be over just after)
    for (round, ban_s) in [(1, Some(60.)), (2, Some(180.)), (3, None)] {
        s.at_ms(now);
        s.row().owner_progress = 0;
        // A fresh read, then 7 s flat: released (base 6 s).
        assert!(kept(&s.act(&mut d, HOLD)), "round {round}: taken up again");
        s.at_ms(now + 7. * S);
        assert_eq!(s.act(&mut d, HOLD).status, Status::Failed, "round {round}");
        let lost_at = now + 7. * S;
        match ban_s {
            Some(b) => {
                s.at_ms(lost_at + (b - 1.) * S);
                assert!(s.propose(&mut d).is_empty(), "round {round}: banned {b} s");
                s.at_ms(lost_at + (b + 1.) * S);
                assert_eq!(
                    s.propose(&mut d).len(),
                    1,
                    "round {round}: free after {b} s"
                );
                now = lost_at + (b + 2.) * S;
            }
            None => {
                s.at_ms(lost_at + 1.0e9);
                assert!(
                    s.propose(&mut d).is_empty(),
                    "round 3: banned for the match"
                );
            }
        }
    }
}

#[test]
fn a_fort_missing_from_the_view_is_banned_not_retried() {
    let mut s = scene(HOLD, false, false);
    let mut d = Dwell::default();
    assert_eq!(s.act(&mut d, HOLD).status, Status::Failed);
    assert!(s.propose(&mut d).is_empty(), "banned after the failure");
}

#[test]
fn an_unreachable_token_is_banned_not_retried() {
    let mut s = scene(HOLD, false, true);
    // The owner stands off the map, so the path service knows no way to the token.
    s.p.me.at = Vec2::new(-50_000., -50_000.);
    let mut d = Dwell::default();
    let step = s.act(&mut d, HOLD);
    assert_eq!(step.status, Status::Failed, "no way there: {step:?}");
    assert!(s.propose(&mut d).is_empty(), "banned after the failure");
}

#[test]
fn a_meditation_that_never_starts_is_banned() {
    let mut s = scene(STAY, true, false);
    let mut d = Dwell::default();
    s.at_ms(100. * S);
    assert!(
        kept(&s.act(&mut d, STAY)),
        "no action 6 offered, still waiting"
    );
    s.at_ms(105. * S);
    assert!(kept(&s.act(&mut d, STAY)));
    s.at_ms(109. * S);
    assert_eq!(s.act(&mut d, STAY).status, Status::Failed);
    assert!(s.propose(&mut d).is_empty(), "banned after the stall");
}

#[test]
fn a_ready_hold_whose_turn_in_does_not_land_is_banned() {
    let mut s = scene(HOLD, true, false);
    let mut d = Dwell::default();
    s.row().owner_ready = true;
    s.at_ms(100. * S);
    assert!(kept(&s.act(&mut d, HOLD)));
    s.at_ms(108. * S);
    assert!(kept(&s.act(&mut d, HOLD)));
    s.at_ms(111. * S);
    assert_eq!(s.act(&mut d, HOLD).status, Status::Failed);
    assert!(s.propose(&mut d).is_empty(), "banned after the stall");
}
