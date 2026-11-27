//! P-RAID-0-fix review notes (reviewer-opus on `ca6e6ac26`): the farm keeps the member it fights when the position label
//! `MobSeen::camp` relabels it mid-chase, and an executor's `urgent` makes the arbiter decide on that frame so a Sustain
//! can take over a committed fight at once. Written by the implementer, not an oracle file.
use super::arbiter::Arbiter;
use super::exec::{self, farm::Farm};
use super::goal::{any_urgent, Candidate, Cx, Executor, GoalKind, RaidGoal, Status, Step, Target};
use super::percept::{CampSeen, Me, MobClass, MobSeen, RaidPercept};
use super::values::Prices;
use crate::bots::paths::{PathBudget, RoomPathService};
use crate::bots::perception::LootFacts;
use crate::bots::planning::PlanMap;
use crate::bots::random::Rng;
use crate::bots::types::{
    HudView, IntentBody, OwnerLock, Percept, PersonaId, PersonaParams, SelfView, TierId, TierParams,
};
use rh_sim::{Arena, FactionId, Vec2};

fn arena() -> Arena {
    let path = concat!(
        env!("CARGO_MANIFEST_DIR"),
        "/../rh-sim/src/mapdata/fixtures/seed1.map.json"
    );
    let text = std::fs::read_to_string(path).expect("the seed1 fixture map");
    let file: rh_sim::mapdata::MapExport = serde_json::from_str(&text).expect("rh-map/2 json");
    file.validate()
        .expect("an admitted map")
        .into_arena()
        .expect("an arena")
}

struct Scene {
    plan: PlanMap,
    paths: RoomPathService,
    p: RaidPercept,
}

/// A regional mob `off` from camp 0's centre, labelled `camp`.
fn mob(
    key: &str,
    off: Vec2,
    camp: Option<u32>,
    engaged: bool,
) -> (String, Vec2, Option<u32>, bool) {
    (key.to_owned(), off, camp, engaged)
}

/// Camp 0 of the seed1 map (three members alive), the owner (Holy, full hp, no weapons) at its centre in an active
/// raid, and `mobs` as given (offsets from the centre).
fn scene(mobs: &[(String, Vec2, Option<u32>, bool)]) -> Scene {
    let arena = arena();
    let plan = PlanMap::from_arena(&arena);
    let paths = RoomPathService::new(&arena.map, PathBudget::default());
    let c = &plan.camps[0];
    let centre = Vec2::new(c.x, c.y);
    let mobs = mobs
        .iter()
        .map(|(key, off, camp, engaged)| MobSeen {
            key: key.clone(),
            at: Vec2::new(centre.x + off.x, centre.y + off.y),
            hp_share: 1.,
            alive: true,
            class: MobClass::Regional,
            mob_type: None,
            weapon: None,
            faction: None,
            camp: *camp,
            engaged: *engaged,
        })
        .collect();
    let base = Percept {
        now: 5_000.,
        hud: HudView {
            phase: "active".into(),
            gates_locked: false,
        },
        self_view: SelfView {
            x: centre.x,
            y: centre.y,
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
            at: centre,
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
        camps: vec![CampSeen {
            index: 0,
            at: centre,
            zone: c.zone,
            elite_spot: -1,
            mob_type: 0,
            reward_stat: None,
            elite: None,
            alive: 3,
            state: 0,
            cleared_at: 0.,
            respawn_at: 0.,
            ward: None,
        }],
        mobs,
        givers: Vec::new(),
        players: Vec::new(),
        quests: None,
        trade: None,
        fields: Vec::new(),
        build: None,
        summary: None,
        arena: None,
        hit_at: None,
    };
    Scene { plan, paths, p }
}

fn cx<'a>(s: &'a mut Scene, goal: &'a RaidGoal, status: Status, rng: &'a mut Rng) -> Cx<'a> {
    Cx {
        p: &s.p,
        paths: &mut s.paths,
        plan: &s.plan,
        knowledge: None,
        tier: TierId::Normal,
        persona: PersonaId::Farmer,
        prices: &Prices::LOCAL,
        still_since: 0.,
        goal,
        status,
        rng,
    }
}

/// One think of `farm` on camp 0: the targets of the lock intents it sent.
fn locks(farm: &mut Farm, s: &mut Scene) -> Vec<String> {
    let goal = RaidGoal::new(GoalKind::Farm, Target::Camp(0));
    let mut rng = Rng::new(16);
    let step = farm.act(&goal, &mut cx(s, &goal, Status::Running, &mut rng));
    step.intents
        .iter()
        .filter_map(|i| match &i.body {
            IntentBody::Lock { target, .. } => Some(target.clone()),
            _ => None,
        })
        .collect()
}

fn hold(s: &mut Scene, key: &str) {
    s.p.base.self_view.owner_lock = Some(OwnerLock::Held {
        target: Some(key.to_owned()),
    });
}

// ---- note 1: the fought member is kept when the position label relabels it --------------------------------------

/// A farm that fought `n10` (10 east of the centre) last think. Next frame it stands at 15 east under key `n20`,
/// labelled `label` and `engaged`; a camp-0 member `n21` stands at 30 east. Returns whom the farm locks.
fn position_path(label: Option<u32>, engaged: bool) -> Vec<String> {
    let mut farm = Farm::default();
    let mut first = scene(&[
        mob("n10", Vec2::new(10., 0.), Some(0), false),
        mob("n11", Vec2::new(30., 0.), Some(0), false),
    ]);
    assert_eq!(locks(&mut farm, &mut first), vec!["n10".to_owned()]);
    let mut next = scene(&[
        mob("n20", Vec2::new(15., 0.), label, engaged),
        mob("n21", Vec2::new(30., 0.), Some(0), false),
    ]);
    locks(&mut farm, &mut next)
}

/// A fresh farm (no position memory) whose owner holds a lock on `n20` at 30 east, labelled `label` and `engaged`,
/// with a camp-0 member `n21` nearer, at 10 east. Returns whom the farm locks.
fn lock_path(label: Option<u32>, engaged: bool) -> Vec<String> {
    let mut farm = Farm::default();
    let mut s = scene(&[
        mob("n20", Vec2::new(30., 0.), label, engaged),
        mob("n21", Vec2::new(10., 0.), Some(0), false),
    ]);
    hold(&mut s, "n20");
    locks(&mut farm, &mut s)
}

#[test]
fn a_chasing_member_relabelled_by_position_is_kept_and_a_resting_foreign_mob_is_not() {
    // Position path: the fought member reads as no camp mid-chase, or as camp 1 while it fights at our side.
    assert_eq!(
        position_path(None, false),
        vec!["n20".to_owned()],
        "a member labelled no camp is still the one we fight"
    );
    assert_eq!(
        position_path(Some(1), true),
        vec!["n20".to_owned()],
        "an engaged mob within DEFEND_U labelled camp 1 is kept"
    );
    assert_eq!(
        position_path(Some(1), false),
        vec!["n21".to_owned()],
        "a resting mob labelled camp 1 is refused: the camp's own member instead"
    );
    // Held-lock path: the same three labels on the mob the owner has locked.
    assert_eq!(
        lock_path(None, false),
        vec!["n20".to_owned()],
        "a locked mob labelled no camp is kept over a nearer member"
    );
    assert_eq!(
        lock_path(Some(1), true),
        vec!["n20".to_owned()],
        "a locked engaged mob within DEFEND_U labelled camp 1 is kept"
    );
    assert_eq!(
        lock_path(Some(1), false),
        vec!["n21".to_owned()],
        "a lock on a resting mob of camp 1 is not followed: the nearest member of the camp"
    );
}

// ---- note 2: an urgent executor makes the arbiter decide on this frame --------------------------------------------

/// A sustain module stand-in: answers `urgent` as set and proposes one Sustain far better per minute than any farm.
struct Alarm {
    urgent: bool,
}

impl Executor for Alarm {
    fn kinds(&self) -> &'static [GoalKind] {
        &[GoalKind::Sustain]
    }
    fn urgent(&mut self, _cx: &mut Cx<'_>) -> bool {
        self.urgent
    }
    fn propose(&mut self, _cx: &mut Cx<'_>, out: &mut Vec<Candidate>) {
        out.push(Candidate {
            goal: RaidGoal::new(GoalKind::Sustain, Target::None),
            value: 3.,
            success: 1.,
            death: 0.,
            loss: 0.,
            minutes: 0.2,
        });
    }
    fn act(&mut self, _goal: &RaidGoal, _cx: &mut Cx<'_>) -> Step {
        Step::none()
    }
}

/// The registry's executors plus an `Alarm` answering `urgent`.
fn executors(urgent: bool) -> Vec<Box<dyn Executor>> {
    let mut v = exec::registry();
    v.push(Box::new(Alarm { urgent }));
    v
}

#[test]
fn an_urgent_executor_makes_the_arbiter_decide_now_and_sustain_takes_over_a_committed_fight() {
    let tier = TierParams::by_id(TierId::Normal);
    let persona = PersonaParams::by_id(PersonaId::Farmer);
    let members = [
        mob("n10", Vec2::new(10., 0.), Some(0), true),
        mob("n11", Vec2::new(20., 0.), Some(0), false),
        mob("n12", Vec2::new(30., 0.), Some(0), false),
    ];
    let held = RaidGoal::new(GoalKind::Farm, Target::Camp(0));
    // The arbiter decided at 0 (next strategy tick 1.4 to 2.6 s later); the frame below is at 0.5 s.
    let mut arbiter = Arbiter::default();
    let idle = RaidGoal::new(GoalKind::Roam, Target::None);
    let mut stream = Rng::new(16);
    arbiter.choose(
        0.,
        Vec::new(),
        (&idle, Status::Done, false),
        tier,
        persona,
        &mut stream,
    );
    let (now, later) = (500., 3_000.);

    // Every `urgent` false: the registry's executors answer false, draw nothing, and the cadence is as before.
    let mut s = scene(&members);
    let mut rng = Rng::new(16);
    let before = rng.clone();
    let quiet = any_urgent(
        &mut executors(false),
        &mut cx(&mut s, &held, Status::Running, &mut rng),
    );
    assert!(!quiet, "no registered executor is urgent");
    assert_eq!(
        rng, before,
        "asking `urgent` draws nothing from the arbiter stream"
    );
    assert!(
        !arbiter.due(now, Status::Running, quiet),
        "not due before the cadence"
    );
    assert!(
        arbiter.due(later, Status::Running, quiet),
        "due once the cadence passes"
    );

    // One `urgent` true: due on this frame, and the Sustain it proposes takes the committed fight over.
    let mut execs = executors(true);
    let mut rng = Rng::new(16);
    let mut c = cx(&mut s, &held, Status::Running, &mut rng);
    let urgent = any_urgent(&mut execs, &mut c);
    assert!(urgent, "the alarm's answer reaches the OR");
    assert!(
        arbiter.due(now, Status::Running, urgent),
        "due on this frame, before the cadence"
    );
    let mut candidates = Vec::new();
    for e in &mut execs {
        e.propose(&mut c, &mut candidates);
    }
    let farm = candidates
        .iter()
        .find(|c| c.goal == held)
        .expect("the farm proposes the camp it fights");
    assert!(
        super::arbiter::rate(farm) > 0.,
        "the fight's own score is positive: {farm:?}"
    );
    let got = arbiter.choose(
        now,
        candidates,
        (&held, Status::Running, true),
        tier,
        persona,
        &mut stream,
    );
    assert_eq!(got, Some(RaidGoal::new(GoalKind::Sustain, Target::None)));
}
