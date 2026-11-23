//! P-RAID-Q3b-fix2 items 3 and 4 (not oracle-protected): Trade Up's cash-out candidate and its give-up read one walk measure
//! (the path), and the two fallbacks of `decide.rs` are the death helper's default, not a flat `death_floor`. A child of
//! `decide` so it reaches `Decide`'s private fields.
use super::*;
use crate::bots::paths::{PathBudget, RoomPathService};
use crate::bots::perception::LootFacts;
use crate::bots::planning::PlanMap;
use crate::bots::raid::goal::{Cx, GoalKind, RaidGoal, Status, Target};
use crate::bots::raid::percept::{Me, RaidPercept};
use crate::bots::random::Rng;
use crate::bots::types::{HudView, OwnerLock, Percept, PersonaId, SelfView, TierId};
use rh_protocol::state::{WireQuestObjective, WireQuestPoint, WireQuestRow, WireQuestView};
use rh_protocol::QuestHandle;
use rh_sim::{FactionId, WeaponId};

const NOW: f64 = 5_000.;

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

    fn frame(&self, at: Vec2, hp: f64, quests: WireQuestView) -> RaidPercept {
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
                hp: 100. * hp,
                max_hp: 100.,
                owner_lock: Some(OwnerLock::Clear),
                ..SelfView::default()
            },
            ..Percept::default()
        };
        RaidPercept {
            now: NOW,
            tick: 100,
            base,
            facts: LootFacts::default(),
            toasts: Vec::new(),
            self_key: "p1".into(),
            me: Me {
                at,
                hp: 100. * hp,
                max_hp: 100.,
                alive: true,
                faction: Some(FactionId::Holy),
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

fn row_of_def(def: &str, giver: Vec2) -> WireQuestRow {
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
        can_accept: true,
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

fn view(row: WireQuestRow, objective: Option<Vec2>) -> WireQuestView {
    let objectives = objective
        .map(|at| WireQuestObjective {
            quest: row.handle,
            at: WireQuestPoint { x: at.x, y: at.y },
        })
        .into_iter()
        .collect();
    WireQuestView {
        rows: vec![row],
        objectives,
        ..WireQuestView::default()
    }
}

/// Where the path walk and the straight x 1.3 differ (they do on the fixture map: its paths run at most 1.10 x the line), the
/// timer is set between the two minutes: the cash-out candidate says reachable exactly when `act_trade` does not give up.
#[test]
fn the_cash_out_candidate_and_the_give_up_read_the_same_walk() {
    let mut w = World::new();
    let prices = Prices::LOCAL;
    let (at, last, next) = (w.camp(0), w.camp(1), w.camp(2));
    let path = eta_minutes(&mut w.paths, at, last).expect("a path");
    let line = straight_minutes(at, last);
    assert!(
        path < line - 1e-6,
        "scene: path {path} is under line {line}"
    );
    let mut row = row_of_def(TRADE_UP, last);
    row.owner_progress = 1;
    row.expires_at = NOW + (path + line) / 2. * 60_000.;
    let p = w.frame(at, 1., view(row, Some(next)));
    let mut d = Decide {
        last_trader: Some((1, last)),
        ..Decide::default()
    };
    let none = RaidGoal::new(GoalKind::Roam, Target::None);
    let mut rng = Rng::new(7);
    let mut out = Vec::new();
    d.propose_trade(&mut w.cx(&p, &prices, &none, &mut rng), &mut out);
    let c = out.first().expect("a Trade Up candidate");
    // The time left is over the path walk, so the cash-out is reachable: not a failed step.
    assert!(
        c.success > 0.,
        "the candidate says unreachable: {}",
        c.success
    );
    let goal = c.goal.clone();
    let step = d.act_trade(&mut w.cx(&p, &prices, &goal, &mut rng));
    assert!(
        step.status != Status::Failed,
        "act_trade gave up where the candidate said reachable: {:?}",
        step.status
    );
}

/// Item 4: with no outpost guess / no trader shown the death term is the helper's default (the row, else `death_floor`, at
/// the seat's health), so a hurt seat pays more and a healthy one pays the floor.
#[test]
fn the_two_fallbacks_are_the_helpers_default_not_a_flat_floor() {
    let prices = Prices::LOCAL;
    let mut seen = Vec::new();
    for hp in [1., 0.5] {
        let mut w = World::new();
        let at = w.camp(0);
        // No objective and a giver nowhere to guess an outpost from: both fallbacks.
        let trade = row_of_def(TRADE_UP, at);
        let p = w.frame(at, hp, view(trade.clone(), None));
        let none = RaidGoal::new(GoalKind::Roam, Target::None);
        let mut rng = Rng::new(7);
        let mut d = Decide::default();
        let read_death = d
            .trade_read(&mut w.cx(&p, &prices, &none, &mut rng), &trade)
            .death;
        let banners = row_of_def(TWO_BANNERS, at);
        let p = w.frame(at, hp, view(banners.clone(), None));
        let banners_death = d
            .banners_terms(&mut w.cx(&p, &prices, &none, &mut rng), &banners)
            .0;
        seen.push((read_death, banners_death));
    }
    let floor = prices.death_floor;
    assert!(
        (seen[0].0 - floor).abs() < 1e-12,
        "trade, full health: {}",
        seen[0].0
    );
    assert!(
        (seen[0].1 - floor).abs() < 1e-12,
        "banners, full health: {}",
        seen[0].1
    );
    assert!(seen[1].0 > floor && seen[1].1 > floor, "hurt: {seen:?}");
}
