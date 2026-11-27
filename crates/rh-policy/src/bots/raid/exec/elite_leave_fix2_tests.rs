//! P-RAID-E-leave-fix2 oracle (docs/backlog/packets/P-RAID-E-leave-fix2.md), in-crate on hand-built frames: a seat out of
//! the chest's reach walks to it whatever travel's stillness rule says, and a ranged seat that attacks the camp's elite
//! or an add from beyond `FIGHT_U` (its attack lock names it) has its fight recorded. Read-only for the implementer.
//!
//! Hand-built facts (each is a frame the sim can produce): the camp row, the mob rows and the chest row, as in
//! `elite_leave_fix_tests.rs` (whose helpers are private, so copied here); `still_since` 0 with `now` 5 000 ms (a seat
//! that has stood still for 5 s, as a killer does); the seat's `owner_lock` `Held { target: Some(key) }` with the key of
//! a mob it sees (the sim fills `attack_target` for a held lock, `perception.rs:443-465`; a Wand's lock holds at 400 u,
//! its `lock_range` 512, and the mob is inside `VISION_RADIUS` 900).
use super::*;
use crate::bots::paths::{PathBudget, RoomPathService};
use crate::bots::perception::{ChestRef, LootFacts};
use crate::bots::planning::PlanMap;
use crate::bots::raid::percept::{Me, RaidPercept};
use crate::bots::random::Rng;
use crate::bots::types::{HudView, OwnerLock, Percept, PersonaId, SelfView, TierId};
use rh_sim::FactionId;

fn arena() -> rh_sim::Arena {
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

struct Frame {
    plan: PlanMap,
    paths: RoomPathService,
    p: RaidPercept,
    centre: Vec2,
}

fn mob(key: &str, at: Vec2, class: MobClass, camp: Option<u32>) -> MobSeen {
    MobSeen {
        key: key.into(),
        at,
        hp_share: 1.,
        alive: true,
        class,
        mob_type: (class == MobClass::Elite).then_some(MobTypeId::Executioner),
        weapon: None,
        faction: None,
        camp,
        engaged: false,
    }
}

/// Camp 0 of the seed1 map as an executioner camp: the elite "elite" at the centre, one add "add" 8 u beside it, a seat
/// `off` from the centre holding `weapon` at full hp with its lock `lock` (a mob key, or none).
fn frame(off: Vec2, weapon: WeaponId, lock: Option<&str>) -> Frame {
    let arena = arena();
    let plan = PlanMap::from_arena(&arena);
    let paths = RoomPathService::new(&arena.map, PathBudget::default());
    let c = &plan.camps[0];
    let centre = Vec2::new(c.x, c.y);
    let at = Vec2::new(centre.x + off.x, centre.y + off.y);
    let mobs = vec![
        mob(
            "add",
            Vec2::new(centre.x + 8., centre.y + 8.),
            MobClass::Regional,
            Some(0),
        ),
        mob("elite", centre, MobClass::Elite, Some(0)),
        // A regional mob of another camp, far from this one: not a member.
        mob(
            "other",
            Vec2::new(centre.x, centre.y - 300.),
            MobClass::Regional,
            Some(3),
        ),
    ];
    let owner_lock = match lock {
        Some(k) => OwnerLock::Held {
            target: Some(k.into()),
        },
        None => OwnerLock::Clear,
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
            owner_lock: Some(owner_lock),
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
            weapons: [Some(weapon), None],
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
            elite_spot: 0,
            mob_type: 0,
            reward_stat: None,
            elite: Some(MobTypeId::Executioner),
            alive: 2,
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
    Frame {
        plan,
        paths,
        p,
        centre,
    }
}

fn cx<'a>(f: &'a mut Frame, goal: &'a RaidGoal, rng: &'a mut Rng, prices: &'a Prices) -> Cx<'a> {
    Cx {
        p: &f.p,
        paths: &mut f.paths,
        plan: &f.plan,
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

fn elite_goal() -> RaidGoal {
    RaidGoal::new(GoalKind::Elite, Target::Camp(0))
}

/// Elite's frame: `observe`, then `act`, at the frame's `now`.
fn elite_frame(el: &mut Elite, f: &mut Frame, goal: &RaidGoal) -> Step {
    let prices = &Prices::LOCAL;
    let mut rng = Rng::new(1);
    el.observe(&mut cx(f, goal, &mut rng, prices));
    el.act(goal, &mut cx(f, goal, &mut rng, prices))
}

/// The goal is dropped: the next frame holds a Farm goal on the camp, `observe` only.
fn hand_away(el: &mut Elite, f: &mut Frame) {
    f.p.now += 50.;
    let other = RaidGoal::new(GoalKind::Farm, Target::Camp(0));
    let mut rng = Rng::new(1);
    el.observe(&mut cx(f, &other, &mut rng, &Prices::LOCAL));
}

fn giveups_at(el: &Elite, camp: u32) -> Option<u32> {
    el.giveups.iter().find(|g| g.camp == camp).map(|g| g.n)
}

fn move_to(step: &Step) -> Option<Vec2> {
    step.intents.iter().find_map(|i| match i.body {
        IntentBody::Move { to } => Some(to),
        _ => None,
    })
}

fn chest_at(at: Vec2) -> ChestRef {
    ChestRef {
        key: "c1".into(),
        handle: rh_protocol::RewardChestHandle::new(1).expect("a handle"),
        at,
        expires_at: 5_000. + 55_000.,
        life_ms: 60_000.,
        options: None,
        taken_by: Vec::new(),
        picked: false,
    }
}

// ---- Defect 1: the chest's reach ---------------------------------------------------------------------------------------

/// The seat killed its elite 1 s ago (`killed` holds the camp), the chest stands at the camp centre, the seat is `d` u
/// from the chest along x, with a Sword. Hand-built: `killed` is set directly (the kill itself is Defect 2's tests); the
/// frame is what a killer standing still after the last blow shows.
fn chest_phase(d: f64) -> (Elite, Frame) {
    let mut f = frame(Vec2::new(d, 0.), WeaponId::Sword, None);
    f.p.mobs.clear();
    f.p.camps[0].alive = 0;
    f.p.camps[0].state = 2;
    let chest = chest_at(f.centre);
    f.p.facts.chests.push(chest);
    let mut el = Elite::default();
    el.killed.push((0, 4_000.));
    (el, f)
}

/// Premise of every walking test: the map has a way from the seat to the chest (`step_toward` answers), so a correct
/// fix can emit the move.
fn assert_way_to_chest(f: &mut Frame) {
    let (at, to) = (f.p.me.at, f.centre);
    assert!(
        travel::step_toward(&mut f.paths, at, to).is_some(),
        "premise: no way from {at:?} to the chest {to:?} on this map"
    );
}

/// Red on the base: 100, 120 and 149 u from the chest (past the panel's 88 u, inside travel's `NEAR_U` 150), standing
/// still for 5 s: `travel::walk` says arrived, the step stands with no intent. Required: a move intent toward the chest.
#[test]
fn a_seat_standing_out_of_the_panels_reach_walks_to_the_chest() {
    for d in [100., 120., 149.] {
        let (mut el, mut f) = chest_phase(d);
        assert_way_to_chest(&mut f);
        let step = elite_frame(&mut el, &mut f, &elite_goal());
        assert_eq!(step.status, Status::Running, "{d} u: {step:?}");
        let to = move_to(&step).unwrap_or_else(|| panic!("{d} u from the chest: no move {step:?}"));
        let seat = f.p.me.at;
        assert!(
            dist(to, f.centre) < dist(seat, f.centre) - 1.,
            "{d} u: the move {to:?} is not toward the chest {:?}",
            f.centre
        );
    }
}

/// Guard (green on the base, must stay green): inside the pick's reach the seat stands: running, no intent. 60 u is
/// inside `CHEST_PANEL_RANGE` (88) less any margin up to 28 u.
#[test]
fn a_seat_within_the_panels_reach_stands_at_the_chest() {
    let (mut el, mut f) = chest_phase(60.);
    let step = elite_frame(&mut el, &mut f, &elite_goal());
    assert_eq!(step.status, Status::Running, "{step:?}");
    assert!(
        step.intents.is_empty(),
        "a move from within reach: {step:?}"
    );
}

// ---- Defect 2: a ranged seat's fight is recorded -----------------------------------------------------------------------

/// A Wand seat 400 u from the camp (`lock_range` 512, beyond `FIGHT_U` 240) with its lock on `lock`, one think of the
/// Elite goal.
fn ranged_fight(lock: Option<&str>) -> (Elite, Frame) {
    let mut f = frame(Vec2::new(400., 0.), WeaponId::Wand, lock);
    let mut el = Elite::default();
    elite_frame(&mut el, &mut f, &elite_goal());
    (el, f)
}

/// The kill: the elite mob and the add gone, the camp cleared (`state` 2, nothing alive), the lock cleared, the chest
/// delivered at the camp; one second on. Hand-built: the frame after an elite's death.
fn after_kill(f: &mut Frame) {
    f.p.now += 1_000.;
    f.p.mobs.clear();
    f.p.camps[0].alive = 0;
    f.p.camps[0].state = 2;
    f.p.base.self_view.owner_lock = Some(OwnerLock::Clear);
    let chest = chest_at(f.centre);
    f.p.facts.chests.push(chest);
}

fn assert_chest_phase_after_kill(lock: &str) {
    let (mut el, mut f) = ranged_fight(Some(lock));
    assert!(
        el.fight_since.is_some(),
        "a Wand seat locked on {lock} at 400 u: its fight is not recorded"
    );
    after_kill(&mut f);
    assert_way_to_chest(&mut f);
    let step = elite_frame(&mut el, &mut f, &elite_goal());
    assert!(
        el.killed.iter().any(|(c, _)| *c == 0),
        "the kill is not recorded: {step:?}"
    );
    assert_eq!(
        step.status,
        Status::Running,
        "the goal is dropped instead of the chest phase: {step:?}"
    );
    let to =
        move_to(&step).unwrap_or_else(|| panic!("the seat does not head for the chest {step:?}"));
    assert!(
        dist(to, f.centre) < dist(f.p.me.at, f.centre) - 1.,
        "{to:?}"
    );
}

/// Red on the base: the lock names the elite, the seat is at 400 u, `fighting` is false, no `fight_since`, so no kill is
/// recorded and `act` answers "the elite is gone".
#[test]
fn a_ranged_seat_locked_on_the_elite_gets_the_chest_phase() {
    assert_chest_phase_after_kill("elite");
}

/// Red on the base, as above with the lock on an add of the camp (the add stands at the camp centre).
#[test]
fn a_ranged_seat_locked_on_an_add_gets_the_chest_phase() {
    assert_chest_phase_after_kill("add");
}

/// Guard (green on the base): a Wand seat at 400 u with no lock is not fighting: no fight on record, and a hand-away
/// bans nothing. Being within weapon reach is not fighting.
#[test]
fn a_ranged_seat_with_no_lock_is_not_fighting() {
    let (mut el, mut f) = ranged_fight(None);
    assert!(el.fight_since.is_none(), "fighting with no lock");
    hand_away(&mut el, &mut f);
    assert_eq!(giveups_at(&el, 0), None, "a ban for a fight never made");
}

/// Guard (green on the base): a lock on a mob that is not of this camp (a regional of camp 3, 300 u off) does not make
/// the seat fight this elite.
#[test]
fn a_lock_on_a_mob_of_another_camp_is_not_this_fight() {
    let (el, _) = ranged_fight(Some("other"));
    assert!(
        el.fight_since.is_none(),
        "fighting the camp by a lock elsewhere"
    );
}

/// Red on the base: a ranged seat that fought from 400 u and is handed away while the elite stands is banned once for
/// `ban_seconds` (20 s), as a melee seat is (`a_fight_handed_away_is_one_give_up`).
#[test]
fn a_ranged_fight_handed_away_is_one_give_up() {
    let e = Prices::LOCAL.elite;
    let (mut el, mut f) = ranged_fight(Some("elite"));
    f.p.now += 1_000.;
    let handed_at = f.p.now;
    hand_away(&mut el, &mut f);
    assert_eq!(giveups_at(&el, 0), Some(1), "no give-up for a ranged fight");
    assert!(el.banned(0, handed_at + e.ban_seconds * 1_000. - 1.));
    assert!(!el.banned(0, handed_at + e.ban_seconds * 1_000. + 100.));
}
