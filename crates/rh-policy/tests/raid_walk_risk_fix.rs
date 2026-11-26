//! P-RAID-Q3b-fix2 oracle (docs/backlog/packets/P-RAID-Q3b-fix2.md): the health factor on a quest's death share is capped, so
//! an accepted Hold the Fort stays worth holding for a hurt seat; the term does not panic on a row at the top of the
//! allowed range. (Item 3, one walk measure for Trade Up, has no scene here: see the packet.)
//!
//! Hand-built frames through the public executors (`Dwell`, `Walker`, `Decide`), as `raid_walk_risk.rs` does (its helpers
//! are copied here; that file is not edited). The state on the base (`coord/int-88` @ 58e36bb96) is named on each test.
use rh_policy::bots::paths::{PathBudget, RoomPathService};
use rh_policy::bots::perception::LootFacts;
use rh_policy::bots::planning::PlanMap;
use rh_policy::bots::raid::goal::{Candidate, Cx, Executor, GoalKind, RaidGoal, Status, Target};
use rh_policy::bots::raid::percept::{CampSeen, Me, MobSeen, PlayerSeen, RaidPercept};
use rh_policy::bots::raid::quest::dwell::Dwell;
use rh_policy::bots::raid::quest::walker::Walker;
use rh_policy::bots::raid::values::Prices;
use rh_policy::bots::random::Rng;
use rh_policy::bots::types::{HudView, OwnerLock, Percept, PersonaId, SelfView, TierId};
use rh_protocol::state::{WireQuestObjective, WireQuestPoint, WireQuestRow, WireQuestView};
use rh_protocol::QuestHandle;
use rh_sim::{FactionId, Vec2, WeaponId};

const NOW: f64 = 60_000.;
/// Pilot row: Hold the Fort 9 of 32 (0.281).
const HOLD_ROW: f64 = 9. / 32.;

fn handle(n: u32) -> QuestHandle {
    QuestHandle::new(n).expect("a handle")
}
fn pt(v: Vec2) -> WireQuestPoint {
    WireQuestPoint { x: v.x, y: v.y }
}
fn row(
    h: u32,
    def: &str,
    giver: Vec2,
    accepted: bool,
    progress: u32,
    expires_at: f64,
) -> WireQuestRow {
    WireQuestRow {
        handle: handle(h),
        def: def.into(),
        giver: pt(giver),
        state: 1,
        expires_at,
        participants: Vec::new(),
        carrier: None,
        carrier_actor: None,
        done_by: None,
        can_accept: !accepted,
        owner_accepted: accepted,
        owner_progress: progress,
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

fn objective(h: u32, at: Vec2) -> WireQuestObjective {
    WireQuestObjective {
        quest: handle(h),
        at: pt(at),
    }
}

struct World {
    plan: PlanMap,
    paths: RoomPathService,
}

/// What a frame holds besides the seat and the board.
struct Scene {
    at: Vec2,
    hp: f64,
    faction: FactionId,
    camps: Vec<CampSeen>,
    mobs: Vec<MobSeen>,
    quests: WireQuestView,
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
        Self {
            plan: PlanMap::from_arena(&arena),
            paths: RoomPathService::new(&arena.map, PathBudget::default()),
        }
    }

    /// A free point of the map to build a scene around.
    fn home(&self) -> Vec2 {
        Vec2::new(self.plan.camps[0].x, self.plan.camps[0].y)
    }

    fn frame(&self, now: f64, s: Scene) -> RaidPercept {
        let base = Percept {
            now,
            hud: HudView {
                phase: "active".into(),
                gates_locked: false,
            },
            self_view: SelfView {
                x: s.at.x,
                y: s.at.y,
                status: "alive".into(),
                hp: 100. * s.hp,
                max_hp: 100.,
                owner_lock: Some(OwnerLock::Clear),
                ..SelfView::default()
            },
            ..Percept::default()
        };
        RaidPercept {
            now,
            tick: (now / 50.) as u32,
            base,
            facts: LootFacts::default(),
            toasts: Vec::new(),
            self_key: "p1".into(),
            me: Me {
                at: s.at,
                hp: 100. * s.hp,
                max_hp: 100.,
                alive: true,
                faction: Some(s.faction),
                weapons: [Some(WeaponId::Sword), None],
                slot: 0,
                effects: 0,
                ready_at: 0.,
                potion_small: 0,
                potion_large: 0,
                potions_healing: 0,
                healing_until: None,
            },
            active: true,
            camps: s.camps,
            mobs: s.mobs,
            givers: Vec::new(),
            players: Vec::new(),
            quests: Some(s.quests),
            trade: None,
            fields: Vec::new(),
            build: None,
            summary: None,
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

    /// The candidates `exec` proposes at this frame (after its `observe`), under `prices`.
    fn propose(
        &mut self,
        exec: &mut dyn Executor,
        p: &RaidPercept,
        prices: &Prices,
    ) -> Vec<Candidate> {
        let goal = RaidGoal::new(GoalKind::Roam, Target::None);
        let mut rng = Rng::new(7);
        let mut out = Vec::new();
        exec.observe(&mut self.cx(p, prices, &goal, &mut rng));
        exec.propose(&mut self.cx(p, prices, &goal, &mut rng), &mut out);
        out
    }
}

fn candidate_of<'a>(out: &'a [Candidate], def: &str) -> &'a Candidate {
    out.iter()
        .find(|c| matches!(&c.goal.target, Target::Quest { def: d, .. } if d == def))
        .unwrap_or_else(|| panic!("no candidate for {def}: {out:?}"))
}

fn scene(at: Vec2, hp: f64, quests: WireQuestView) -> Scene {
    Scene {
        at,
        hp,
        faction: FactionId::Holy,
        camps: Vec::new(),
        mobs: Vec::new(),
        quests,
    }
}

// ---------------------------------------------------------------------------------------------------------------
// The health factor on a quest's death share
// ---------------------------------------------------------------------------------------------------------------

/// Hold the Fort, accepted, the seat on the fort token, `contestants` other players beside it, the seat at hp share `hp`.
/// The candidate the dwell executor proposes under `prices`.
fn hold_candidate(hp: f64, contestants: usize, prices: &Prices) -> Candidate {
    let mut w = World::new();
    let giver = w.home();
    let fort = Vec2::new(giver.x + 900., giver.y);
    let quests = WireQuestView {
        rows: vec![row(1, "hold_the_fort", giver, true, 0, 0.)],
        objectives: vec![objective(1, fort)],
        ..WireQuestView::default()
    };
    let mut p = w.frame(NOW, scene(fort, hp, quests));
    for i in 0..contestants {
        p.players.push(PlayerSeen {
            key: format!("c{i}"),
            at: Vec2::new(fort.x + 20., fort.y),
            hp_share: 1.,
            alive: true,
            targetable: true,
            faction: Some(FactionId::Fire),
            weapon: Some(WeaponId::Sword),
        });
    }
    let out = w.propose(&mut Dwell::default(), &p, prices);
    candidate_of(&out, "hold_the_fort").clone()
}

/// The arbiter's score of a candidate (`arbiter.rs:26`): value x success - death x loss.
fn score(c: &Candidate) -> f64 {
    c.value * c.success.clamp(0., 1.) - c.death.clamp(0., 1.) * c.loss
}

/// RED on the base. An accepted Hold the Fort in a fort nobody contests keeps a positive score at every health. The row is
/// 0.281, the loss 6, the value 3.6; on the base the term is `row / hp` (0.62 at 45 %, 0.80 at 35 %, 0.98 at 25 %), so the
/// score is negative from 47 % down and the arbiter drops the committed hold (the seat walks out of a fort it holds). With the
/// health factor capped at 1.5 the term is at most 0.42 and the score stays about 3.6 - 2.53 = 1.07.
#[test]
fn an_uncontested_hold_stays_worth_holding_at_any_health() {
    for hp in [1., 0.45, 0.35, 0.25, 0.1] {
        let c = hold_candidate(hp, 0, &Prices::LOCAL);
        eprintln!("uncontested hp {hp}: {c:?} score {:.3}", score(&c));
        assert!(
            score(&c) > 0.,
            "hp {hp}: value {} x success {} - death {} x loss {} is not above 0",
            c.value,
            c.success,
            c.death,
            c.loss
        );
    }
}

/// RED on the base. One contestant in the fort (success 0.8) and the seat at 50 %: by decision 1 the term is 0.281 x 1.5 =
/// 0.42, the score 3.6 x 0.8 - 0.42 x 6 = 0.35 (base: term 0.56, score -0.48). Two contestants are NOT pinned: 3.6 x 0.64 =
/// 2.3 is under 2.53, so a hurt seat may leave a fort two players contest.
#[test]
fn a_hold_with_one_contestant_stays_worth_holding_at_half_health() {
    let c = hold_candidate(0.5, 1, &Prices::LOCAL);
    eprintln!("one contestant, hp 0.5: {c:?} score {:.3}", score(&c));
    assert!(
        score(&c) > 0.,
        "value {} x success {} - death {} x loss {}",
        c.value,
        c.success,
        c.death,
        c.loss
    );
}

/// RED on the base (term 0.62 at 45 %, 0.98 at 10 %). The health factor is capped at 1.5: at any health the term is at most
/// 1.5 x the row and at most 0.98.
#[test]
fn the_health_factor_is_capped_at_one_and_a_half() {
    let row = Prices::LOCAL.quest_death_share("hold_the_fort");
    assert!((row - HOLD_ROW).abs() < 1e-12);
    for hp in [0.45, 0.25, 0.1, 0.01] {
        let t = hold_candidate(hp, 0, &Prices::LOCAL).death;
        assert!(
            t <= row * 1.5 * (1. + 1e-9) && t <= 0.98,
            "hp {hp}: {t} is above 1.5 x {row}"
        );
    }
}

/// The death term of `def`'s candidate for a Holy seat at `hp`, no camp near: Hold the Fort and Let Him Cook accepted
/// (Let Him Cook through the walker), Stay A While (no row) before accepting.
fn death_of_def(def: &str, hp: f64, prices: &Prices) -> f64 {
    let mut w = World::new();
    let giver = w.home();
    let fort = Vec2::new(giver.x + 900., giver.y);
    let accepted = def != "stay_a_while";
    let objectives = match def {
        "let_him_cook" => vec![
            objective(1, Vec2::new(giver.x + 100., giver.y)),
            objective(1, Vec2::new(giver.x + 500., giver.y)),
            objective(1, fort),
        ],
        "hold_the_fort" => vec![objective(1, fort)],
        _ => Vec::new(),
    };
    let quests = WireQuestView {
        rows: vec![row(1, def, giver, accepted, 0, 0.)],
        objectives,
        ..WireQuestView::default()
    };
    let p = w.frame(NOW, scene(giver, hp, quests));
    let out = if def == "let_him_cook" {
        w.propose(&mut Walker::default(), &p, prices)
    } else {
        w.propose(&mut Dwell::default(), &p, prices)
    };
    candidate_of(&out, def).death
}

/// Guard (green on the base; the cap's other side). A hurt seat's term is strictly higher than a healthy one's for Hold the
/// Fort (row 0.281), Let Him Cook (row 0.037) and a quest with no row (default 0.02), never below the row, never above 0.98.
#[test]
fn guard_a_hurt_seat_is_priced_higher_than_a_healthy_one() {
    let prices = Prices::LOCAL;
    for def in ["hold_the_fort", "let_him_cook", "stay_a_while"] {
        let row = prices.quest_death_share(def);
        let healthy = death_of_def(def, 1., &prices);
        assert!(healthy >= row - 1e-9 && healthy <= 0.98, "{def}: {healthy}");
        for hp in [0.8, 0.6, 0.45, 0.2] {
            let hurt = death_of_def(def, hp, &prices);
            assert!(
                hurt > healthy && hurt >= row - 1e-9 && hurt <= 0.98 + 1e-9,
                "{def} at {hp}: hurt {hurt}, healthy {healthy}, row {row}"
            );
        }
    }
}

/// RED on the base (`f64::clamp` panics when its minimum is above its maximum). `quest_death_rows` allows
/// `death_floor..=0.98` (`quest/death_tests.rs`); a row at its top (0.98) must price (green on the base), and the helper is
/// total beyond it: rows of 0.99 and 1.0 and a `death_floor` of 0.99 price without a panic and give a term in 0..=1. The
/// implementer makes the helper total; narrowing the range test alone does not satisfy this.
#[test]
fn a_row_at_or_above_the_top_of_the_range_prices_without_a_panic() {
    for top in [0.98, 0.99, 1.0] {
        let rows: &'static [(&'static str, f64)] = Box::leak(Box::new([("hold_the_fort", top)]));
        let prices = Prices {
            quest_death_rows: rows,
            ..Prices::LOCAL
        };
        let got = std::panic::catch_unwind(|| hold_candidate(0.4, 0, &prices).death);
        let term = got.unwrap_or_else(|_| panic!("a row of {top} panics the death term"));
        assert!((0. ..=1.).contains(&term), "row {top}: term {term}");
    }
    let prices = Prices {
        death_floor: 0.99,
        ..Prices::LOCAL
    };
    let got = std::panic::catch_unwind(|| death_of_def("stay_a_while", 0.4, &prices));
    let term = got.unwrap_or_else(|_| panic!("a death_floor of 0.99 panics the death term"));
    assert!((0. ..=1.).contains(&term), "death_floor 0.99: term {term}");
}
