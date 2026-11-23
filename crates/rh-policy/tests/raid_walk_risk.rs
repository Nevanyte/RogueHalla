//! P-RAID-Q3b-fix oracle (docs/backlog/packets/P-RAID-Q3b-fix.md): a quest's walk past an idle camp is not a fight, and the
//! Trade Up cash-out stand outlasts the trader's patrol.
//!
//! The scenes are the executors themselves (`Walker`, `Dwell`, `Decide`) driven on hand-built percepts, as
//! `raid_hold.rs` does: a camp "near a point", a mob `engaged` or not, a hurt or healthy Holy seat, and the death term read
//! off the candidate the executor proposes (`Candidate::death`), the smallest public entry that shows it. Each test names
//! its state on the base (`coord/int-88`) and the measured value its floor stands on.
use rh_policy::bots::paths::{PathBudget, RoomPathService};
use rh_policy::bots::perception::LootFacts;
use rh_policy::bots::planning::PlanMap;
use rh_policy::bots::raid::acts::Act;
use rh_policy::bots::raid::goal::{
    Candidate, Cx, Executor, GoalKind, RaidGoal, Status, Step, Target,
};
use rh_policy::bots::raid::percept::{CampSeen, Me, MobClass, MobSeen, RaidPercept};
use rh_policy::bots::raid::quest::decide::Decide;
use rh_policy::bots::raid::quest::dwell::Dwell;
use rh_policy::bots::raid::quest::walker::Walker;
use rh_policy::bots::raid::values::Prices;
use rh_policy::bots::random::Rng;
use rh_policy::bots::types::{HudView, OwnerLock, Percept, PersonaId, SelfView, TierId};
use rh_protocol::state::{
    WireQuestAction, WireQuestObjective, WireQuestPoint, WireQuestRow, WireQuestView,
};
use rh_protocol::QuestHandle;
use rh_sim::mapdata::Zone;
use rh_sim::{FactionId, Vec2, WeaponId};

const NOW: f64 = 60_000.;
/// The decision executor's Trade Up stage (`decide.rs` `STAGE_TRADE`, private).
const STAGE_TRADE: u8 = 4;
/// A five-member camp this far from the point: the reviewer's measurement (0.36 Holy, 0.61 others at full hp).
const CAMP_GAP: f64 = 256.;
/// Pilot rows: Let Him Cook 7 of 189, Hold the Fort 9 of 32.
const LHC_ROW: f64 = 7. / 189.;
const HOLD_ROW: f64 = 9. / 32.;

fn handle(n: u32) -> QuestHandle {
    QuestHandle::new(n).expect("a handle")
}
fn pt(v: Vec2) -> WireQuestPoint {
    WireQuestPoint { x: v.x, y: v.y }
}
fn dist(a: Vec2, b: Vec2) -> f64 {
    rh_sim::math::hypot(a.x - b.x, a.y - b.y)
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

fn action(h: u32, action: u8, target: u8) -> WireQuestAction {
    WireQuestAction {
        quest: handle(h),
        action,
        target,
        at: Vec::new(),
        label: String::new(),
    }
}

fn camp(index: u32, at: Vec2, zone: Zone, alive: u32) -> CampSeen {
    CampSeen {
        index,
        at,
        zone,
        elite_spot: -1,
        mob_type: 0,
        elite: None,
        alive,
        state: 0,
        cleared_at: 0.,
        respawn_at: 0.,
        ward: None,
    }
}

/// A regional member of camp `index`, `engaged` or idle.
fn member(key: &str, at: Vec2, index: u32, engaged: bool) -> MobSeen {
    MobSeen {
        key: key.into(),
        at,
        hp_share: 1.,
        alive: true,
        class: MobClass::Regional,
        mob_type: None,
        weapon: None,
        faction: None,
        camp: Some(index),
        engaged,
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

    /// The candidates `exec` proposes at this frame (after its `observe`).
    fn propose(&mut self, exec: &mut dyn Executor, p: &RaidPercept) -> Vec<Candidate> {
        let goal = RaidGoal::new(GoalKind::Roam, Target::None);
        let mut rng = Rng::new(7);
        let mut out = Vec::new();
        let prices = Prices::LOCAL;
        exec.observe(&mut self.cx(p, &prices, &goal, &mut rng));
        exec.propose(&mut self.cx(p, &prices, &goal, &mut rng), &mut out);
        out
    }

    /// One think of `exec` holding `goal`: `observe`, then `act`.
    fn act(&mut self, exec: &mut dyn Executor, p: &RaidPercept, goal: &RaidGoal) -> Step {
        let mut rng = Rng::new(7);
        let prices = Prices::LOCAL;
        exec.observe(&mut self.cx(p, &prices, goal, &mut rng));
        exec.act(goal, &mut self.cx(p, &prices, goal, &mut rng))
    }
}

fn candidate_of<'a>(out: &'a [Candidate], def: &str) -> &'a Candidate {
    out.iter()
        .find(|c| matches!(&c.goal.target, Target::Quest { def: d, .. } if d == def))
        .unwrap_or_else(|| panic!("no candidate for {def}: {out:?}"))
}

// ---------------------------------------------------------------------------------------------------------------
// The death term of a quest's walk and stand
// ---------------------------------------------------------------------------------------------------------------

/// Which camp setting a death-term scene has near the quest's points.
#[derive(Clone, Copy, PartialEq)]
enum Camps {
    /// No camp in view.
    None,
    /// A living five-member camp `CAMP_GAP` from the point, every member idle (none `engaged`).
    Idle,
    /// The same camp, one of its members `engaged` in the frame (someone is fighting it).
    Awake,
}

/// The death term of `def`'s candidate for a Holy seat at hp share `hp`, with `camps` near the quest's points.
/// Walker: Let Him Cook before and after accepting; Dwell: Hold the Fort and Stay A While.
fn death_of(def: &str, accepted: bool, hp: f64, camps: Camps) -> f64 {
    let mut w = World::new();
    let home = w.home();
    let giver = home;
    let far = Vec2::new(home.x + 900., home.y);
    // Where the quest's danger points lie: the fort token / the objectives (accepted), else the giver.
    // (Stay A While has no objective: its point is always the monk, the giver.)
    let spot = if accepted && def != "stay_a_while" {
        far
    } else {
        giver
    };
    let zone = if def == "let_him_cook" {
        Zone::Plague
    } else {
        Zone::Fire
    };
    let camp_at = Vec2::new(spot.x + CAMP_GAP, spot.y);
    let (camp_list, mobs) = match camps {
        Camps::None => (Vec::new(), Vec::new()),
        Camps::Idle => (
            vec![camp(900, camp_at, zone, 5)],
            vec![member("n1", camp_at, 900, false)],
        ),
        Camps::Awake => (
            vec![camp(900, camp_at, zone, 5)],
            vec![member("n1", camp_at, 900, true)],
        ),
    };
    let objectives = if accepted {
        if def == "let_him_cook" {
            vec![
                objective(1, Vec2::new(home.x + 100., home.y)),
                objective(1, Vec2::new(home.x + 500., home.y)),
                objective(1, far),
            ]
        } else {
            vec![objective(1, far)]
        }
    } else {
        Vec::new()
    };
    let quests = WireQuestView {
        rows: vec![row(1, def, giver, accepted, 0, 0.)],
        objectives,
        ..WireQuestView::default()
    };
    let p = w.frame(
        NOW,
        Scene {
            at: home,
            hp,
            faction: FactionId::Holy,
            camps: camp_list,
            mobs,
            quests,
        },
    );
    let out = if def == "let_him_cook" || def == "ember_walk" {
        w.propose(&mut Walker::default(), &p)
    } else {
        w.propose(&mut Dwell::default(), &p)
    };
    candidate_of(&out, def).death
}

fn within(x: f64, target: f64, tol: f64) -> bool {
    (x - target).abs() <= target * tol
}

/// RED on the base. An idle five-member camp 256 u from Let Him Cook's points (before and after accepting) prices the quest
/// at its row (7 of 189 = 0.037), not at the camp's death: measured on the base 0.125 for this Holy seat at full hp against a
/// five-member Plague camp, 0.02 (the floor) without one. Within 15 % of the row, with a camp and without.
#[test]
fn an_idle_camp_near_let_him_cooks_points_prices_it_at_its_row() {
    for accepted in [false, true] {
        let none = death_of("let_him_cook", accepted, 1., Camps::None);
        let idle = death_of("let_him_cook", accepted, 1., Camps::Idle);
        eprintln!("let_him_cook accepted={accepted}: no camp {none:.4}, idle camp {idle:.4}, row {LHC_ROW:.4}");
        assert!(
            within(none, LHC_ROW, 0.15),
            "accepted={accepted}: with no camp the term is {none}, the row is {LHC_ROW}"
        );
        assert!(
            within(idle, LHC_ROW, 0.15),
            "accepted={accepted}: an idle camp near the points moved the term to {idle} (row {LHC_ROW})"
        );
    }
}

/// RED on the base. Hold the Fort's term is its row (9 of 32, 0.28) at full hp whether or not a camp stands near the fort
/// (base: 0.02 without a camp, the camp's 0.36 with the five-member one).
#[test]
fn hold_the_fort_is_priced_at_its_row_with_or_without_an_idle_camp() {
    for accepted in [false, true] {
        for camps in [Camps::None, Camps::Idle] {
            let d = death_of("hold_the_fort", accepted, 1., camps);
            eprintln!("hold_the_fort accepted={accepted}: {d:.4} (row {HOLD_ROW:.4})");
            assert!(
                within(d, HOLD_ROW, 0.15),
                "accepted={accepted}: the term is {d}, the row is {HOLD_ROW}"
            );
        }
    }
}

/// RED on the base (0.36 with the camp). A quest with no row (Stay A While) is priced at `death_floor` whatever idle camp
/// stands near its giver; the walker's `ember_walk` is the same on the base already (green there).
#[test]
fn an_idle_camp_adds_nothing_to_a_quest_without_a_row() {
    let floor = Prices::LOCAL.death_floor;
    for accepted in [false, true] {
        let stay = death_of("stay_a_while", accepted, 1., Camps::Idle);
        assert!(
            (stay - floor).abs() < 1e-9,
            "stay_a_while accepted={accepted}: {stay}, the default is {floor}"
        );
        let ember = death_of("ember_walk", accepted, 1., Camps::Idle);
        assert!(
            (ember - floor).abs() < 1e-9,
            "ember_walk accepted={accepted}: {ember}, the default is {floor}"
        );
    }
}

/// Guard (green on the base): a camp that is already awake (a member `engaged`) near a quest without a row still counts: the
/// term is the camp fight's: Stay A While 0.36 for this Holy seat against a five-member Fire camp, Let Him Cook 0.125
/// against a Plague one (both measured on the base, where they hold because the base counts idle camps too).
#[test]
fn an_awake_camp_near_the_points_still_counts() {
    for accepted in [false, true] {
        let awake = death_of("stay_a_while", accepted, 1., Camps::Awake);
        eprintln!("stay_a_while accepted={accepted}: awake {awake:.4}");
        assert!(
            awake >= 0.25,
            "stay_a_while: awake camp priced {awake}, under 0.25"
        );
        let lhc = death_of("let_him_cook", accepted, 1., Camps::Awake);
        eprintln!("let_him_cook accepted={accepted}: awake {lhc:.4}");
        assert!(
            lhc >= 0.1,
            "let_him_cook with an awake camp: {lhc}, under 0.1"
        );
    }
}

/// Guard: Let Him Cook and Hold the Fort are priced with their rows, above the default (0.02) and in the order the pilot
/// measured (Hold 28 % over Cook 3.7 %).
#[test]
fn the_rows_sit_above_the_default_and_in_the_pilots_order() {
    let floor = Prices::LOCAL.death_floor;
    let cook = death_of("let_him_cook", true, 1., Camps::None);
    let hold = death_of("hold_the_fort", true, 1., Camps::None);
    assert!(
        cook > floor * 1.3,
        "let_him_cook {cook} is not above the default {floor}"
    );
    assert!(
        hold > cook * 3.,
        "hold_the_fort {hold} is not well above let_him_cook {cook}"
    );
}

/// A hurt seat's term is higher than a healthy one's for the same quest, row or default (RED on the base for the quests
/// without a camp: floor both; the hp scaling is kept, the formula is not pinned), and never below `death_floor`.
#[test]
fn a_hurt_seat_is_likelier_to_die_to_the_same_walk() {
    let floor = Prices::LOCAL.death_floor;
    for (def, accepted) in [
        ("let_him_cook", true),
        ("hold_the_fort", true),
        ("stay_a_while", false),
    ] {
        let healthy = death_of(def, accepted, 1., Camps::None);
        let hurt = death_of(def, accepted, 0.3, Camps::None);
        eprintln!("{def}: healthy {healthy:.4}, hurt {hurt:.4}");
        assert!(hurt > healthy, "{def}: hurt {hurt}, healthy {healthy}");
        assert!(
            hurt <= 0.98 + 1e-9 && healthy >= floor - 1e-9,
            "{def}: {healthy} {hurt}"
        );
    }
}

// ---------------------------------------------------------------------------------------------------------------
// Two Banners: the outpost keeps its pricing (decision 6, OPEN in the packet)
// ---------------------------------------------------------------------------------------------------------------

/// Guard (green on the base): Two Banners' outpost, a camp's centre, keeps the camp-fight term: with a five-member camp on
/// the outpost the candidate's death is the camp's (measured 0.36 at full hp), far above the default.
#[test]
fn guard_two_banners_keeps_pricing_the_camp_at_its_outpost() {
    let mut w = World::new();
    let home = w.home();
    let outpost = Vec2::new(home.x + 900., home.y);
    let quests = WireQuestView {
        rows: vec![row(1, "two_banners", home, true, 0, 0.)],
        objectives: vec![objective(1, outpost)],
        ..WireQuestView::default()
    };
    let p = w.frame(
        NOW,
        Scene {
            at: home,
            hp: 1.,
            faction: FactionId::Holy,
            camps: vec![camp(900, outpost, Zone::Fire, 5)],
            mobs: Vec::new(),
            quests,
        },
    );
    let out = w.propose(&mut Decide::default(), &p);
    let d = candidate_of(&out, "two_banners").death;
    eprintln!("two_banners outpost death {d:.4}");
    assert!(d >= 0.25, "the outpost's camp is priced {d}");
}

// ---------------------------------------------------------------------------------------------------------------
// Trade Up goes on past an idle camp
// ---------------------------------------------------------------------------------------------------------------

/// The seat stands at the next trader (index `progress`), the previous one out of reach; an idle five-member camp stands
/// `CAMP_GAP` from the next trader when `camp` is set. Returns the decision executor's candidate and its acts.
fn trade_up_scene(progress: u32, faction: FactionId, camp_near: bool) -> (Candidate, Vec<Act>) {
    let mut w = World::new();
    let trader = w.home();
    let camps = if camp_near {
        vec![camp(
            900,
            Vec2::new(trader.x + CAMP_GAP, trader.y),
            Zone::Fire,
            5,
        )]
    } else {
        Vec::new()
    };
    let mobs = if camp_near {
        vec![member(
            "n1",
            Vec2::new(trader.x + CAMP_GAP, trader.y),
            900,
            false,
        )]
    } else {
        Vec::new()
    };
    let quests = WireQuestView {
        rows: vec![row(1, "trade_up", trader, true, progress, NOW + 60_000.)],
        objectives: vec![objective(1, trader)],
        // Only the next trader's barter is offered: the last trader is out of reach (no cash-out offered).
        actions: vec![action(1, 3, progress as u8)],
        ..WireQuestView::default()
    };
    let p = w.frame(
        NOW,
        Scene {
            at: Vec2::new(trader.x + 10., trader.y),
            hp: 1.,
            faction,
            camps,
            mobs,
            quests,
        },
    );
    let mut d = Decide::default();
    let out = w.propose(&mut d, &p);
    let c = candidate_of(&out, "trade_up").clone();
    let goal = RaidGoal::new(
        GoalKind::QuestDecide,
        Target::Quest {
            def: "trade_up".into(),
            stage: STAGE_TRADE,
        },
    );
    let step = w.act(&mut d, &p, &goal);
    (c, step.acts)
}

fn bartered(acts: &[Act], target: u8) -> bool {
    acts.iter().any(|a| {
        matches!(a, Act::QuestInteract { def, action: 3, target: t } if def == "trade_up" && *t == target)
    })
}

/// RED on the base. Trade Up at progress 1, an idle five-member camp 256 u from the next trader: the seat barters (stage two)
/// and the candidate is worth the next stage's share, 5.6 x 30/54 = 3.11 (within 2 %). Base: the camp's 0.36 outweighs the
/// next stage's extra (1.87), the seat cashes out at 12 stacks, and the candidate is not that value either.
#[test]
fn trade_up_goes_on_past_an_idle_camp_at_progress_one() {
    let (c, acts) = trade_up_scene(1, FactionId::Holy, true);
    eprintln!("progress 1, camp: {c:?} {acts:?}");
    assert!(
        bartered(&acts, 1),
        "no barter sent with an idle camp near the trader: {acts:?}"
    );
    let next_stage = Prices::LOCAL.reward_value("trade_up") * 30. / 54.;
    assert!(
        within(c.value, next_stage, 0.02),
        "going on is worth {} not the next stage's {next_stage}",
        c.value
    );
}

/// RED on the base (the whole route, 5.6, is the going-on value there). The same without any camp: the candidate is the
/// next stage's worth, at progress 1 and at progress 2 (5.6 x 54/54 = 5.6 at the last stage), and the seat barters.
#[test]
fn trade_up_going_on_is_worth_the_next_stage_not_the_whole_route() {
    let (c1, a1) = trade_up_scene(1, FactionId::Holy, false);
    assert!(bartered(&a1, 1), "{a1:?}");
    let want1 = Prices::LOCAL.reward_value("trade_up") * 30. / 54.;
    assert!(
        within(c1.value, want1, 0.02),
        "progress 1: {} vs {want1}",
        c1.value
    );
    let (c2, a2) = trade_up_scene(2, FactionId::Holy, false);
    assert!(bartered(&a2, 2), "{a2:?}");
    let want2 = Prices::LOCAL.reward_value("trade_up");
    assert!(
        within(c2.value, want2, 0.02),
        "progress 2: {} vs {want2}",
        c2.value
    );
}

// ---------------------------------------------------------------------------------------------------------------
// The cash-out stand outlasts the trader's patrol
// ---------------------------------------------------------------------------------------------------------------

/// The trader's patrol as the sim walks it (`interactions.rs:436-437`, guarded below): `i`'s fraction along 72 u.
fn trader_fraction(now: f64, i: u32) -> f64 {
    let phase = ((now + 3000. * f64::from(i)) % 12_000.) / 6000.;
    1. - (phase - 1.).abs()
}

/// Guard: the patrol this file mirrors is the sim's (loud on a change of its source).
#[test]
fn guard_the_patrol_mirrored_here_is_the_sims() {
    const SRC: &str = include_str!("../../rh-sim/src/quests/board/attempts/interactions.rs");
    for line in [
        "let phase = ((now + 3000. * i as f64) % 12_000.) / 6000.;",
        "let fraction = 1. - (phase - 1.).abs();",
        "Vec2::new(72., 0.),",
    ] {
        assert!(SRC.contains(line), "interactions.rs no longer has `{line}`");
    }
}

/// RED on the base. A seat that bartered with trader 0 at fraction 0, left, and comes back to the remembered point (32 u
/// from it, away from the patrol) is out of contact for 6.3 s (the trader walks out to 72 u). Told to cash out (the next
/// trader is far and the timer short), it stands for the patrol, is never failed, sends the cash-out once the trader is back
/// in contact (about 6.3 s after it arrived), and Trade Up is not banned afterwards. Base: the 4 s stand fails and bans.
#[test]
fn a_cash_out_stand_waits_out_the_patrol_and_is_not_banned() {
    let mut w = World::new();
    let p0 = w.home();
    let p1 = Vec2::new(p0.x + 3000., p0.y);
    let contact = rh_sim::quests::public_rules().contact_range;
    let goal = RaidGoal::new(
        GoalKind::QuestDecide,
        Target::Quest {
            def: "trade_up".into(),
            stage: STAGE_TRADE,
        },
    );
    let mut d = Decide::default();
    let expires = NOW + 20_000.;
    // The barter: progress 0, beside trader 0 (fraction 0 at NOW: the token itself).
    assert!(trader_fraction(NOW, 0) < 1e-9);
    let quests = WireQuestView {
        rows: vec![row(1, "trade_up", p0, true, 0, expires)],
        objectives: vec![objective(1, p0)],
        actions: vec![action(1, 3, 0)],
        ..WireQuestView::default()
    };
    let p = w.frame(
        NOW,
        Scene {
            at: Vec2::new(p0.x + 10., p0.y),
            hp: 1.,
            faction: FactionId::Holy,
            camps: Vec::new(),
            mobs: Vec::new(),
            quests,
        },
    );
    let step = w.act(&mut d, &p, &goal);
    assert!(
        bartered(&step.acts, 0),
        "the scene's barter was not sent: {step:?}"
    );
    // Back at the remembered point 2.9 s later, 30 u from it on the side away from the patrol; one frame per 0.5 s.
    let seat = Vec2::new(p0.x - 30., p0.y);
    let arrived = NOW + 2_900.;
    let mut cashed_at = None;
    let mut last = None;
    let mut t = arrived;
    while t <= arrived + 13_000. {
        let trader = Vec2::new(p0.x + 72. * trader_fraction(t, 0), p0.y);
        let mut actions = Vec::new();
        if dist(seat, trader) <= contact {
            actions.push(action(1, 4, 0));
        }
        let quests = WireQuestView {
            rows: vec![row(1, "trade_up", p0, true, 1, expires)],
            objectives: vec![objective(1, p1)],
            actions,
            ..WireQuestView::default()
        };
        let p = w.frame(
            t,
            Scene {
                at: seat,
                hp: 1.,
                faction: FactionId::Holy,
                camps: Vec::new(),
                mobs: Vec::new(),
                quests,
            },
        );
        let step = w.act(&mut d, &p, &goal);
        assert_ne!(
            step.status,
            Status::Failed,
            "the stand failed {:.1} s after arriving: {}",
            (t - arrived) / 1000.,
            step.why
        );
        let sent = step
            .acts
            .iter()
            .any(|a| matches!(a, Act::QuestInteract { def, action: 4, .. } if def == "trade_up"));
        if sent {
            cashed_at = Some(t);
            last = Some(p);
            break;
        }
        last = Some(p);
        t += 500.;
    }
    let cashed = cashed_at.expect("the cash-out was never sent in 13 s of standing");
    eprintln!(
        "cash-out sent {:.1} s after arriving",
        (cashed - arrived) / 1000.
    );
    assert!(
        cashed - arrived >= 5_000.,
        "sent while the trader was out of contact?"
    );
    // Not banned: the executor still proposes Trade Up with progress held.
    let out = w.propose(&mut d, &last.expect("a frame"));
    assert!(
        out.iter()
            .any(|c| matches!(&c.goal.target, Target::Quest { def, .. } if def == "trade_up")),
        "Trade Up is banned after the stand: {out:?}"
    );
}
