//! P-RAID-E-leave-fix oracle (docs/backlog/packets/P-RAID-E-leave-fix.md), in-crate on hand-built frames: a seat that
//! killed its elite is not pulled off the chest by Sustain's farm-model leave, a Plague seat's own zombie is not a
//! threat, and one give-up counts once however it ends. Read-only for the implementer.
use super::*;
use crate::bots::paths::{PathBudget, RoomPathService};
use crate::bots::perception::{ChestRef, LootFacts};
use crate::bots::planning::PlanMap;
use crate::bots::raid::exec::sustain::Sustain;
use crate::bots::raid::percept::{Me, RaidPercept};
use crate::bots::random::Rng;
use crate::bots::types::{HudView, OwnerLock, Percept, PersonaId, SelfView, TierId};
use rh_protocol::state::{WireBuildSummary, WireMobBonusRow};
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

fn mob(id: u32, at: Vec2, class: MobClass, hp_share: f64) -> MobSeen {
    MobSeen {
        key: format!("n{id}"),
        at,
        hp_share,
        alive: true,
        class,
        mob_type: (class == MobClass::Elite).then_some(MobTypeId::Executioner),
        weapon: None,
        faction: None,
        camp: Some(0),
        engaged: false,
    }
}

/// Camp 0 of the seed1 map, rolled as an executioner camp when `elite` (else a normal camp), with `adds` regional members
/// at the camp centre (hp shares as given), the elite (when `elite`) at the centre with `elite_hp` left, and a Holy seat
/// with a sword and `stacks` normal-mob stacks, `seat_off` from the centre at `hp`, holding `large` large potions.
fn frame(
    elite: Option<f64>,
    adds: &[f64],
    seat_off: Vec2,
    hp: f64,
    large: u8,
    stacks: u32,
) -> Frame {
    let arena = arena();
    let plan = PlanMap::from_arena(&arena);
    let paths = RoomPathService::new(&arena.map, PathBudget::default());
    let c = &plan.camps[0];
    let centre = Vec2::new(c.x, c.y);
    let at = Vec2::new(centre.x + seat_off.x, centre.y + seat_off.y);
    let mut mobs: Vec<MobSeen> = adds
        .iter()
        .enumerate()
        .map(|(i, h)| {
            mob(
                10 + i as u32,
                Vec2::new(centre.x + 8. * i as f64, centre.y + 8.),
                MobClass::Regional,
                *h,
            )
        })
        .collect();
    if let Some(h) = elite {
        mobs.push(mob(99, centre, MobClass::Elite, h));
    }
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
            hp: 100. * hp,
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
            hp: 100. * hp,
            max_hp: 100.,
            alive: true,
            faction: Some(FactionId::Holy),
            weapons: [Some(WeaponId::Sword), None],
            slot: 0,
            effects: 0,
            ready_at: 0.,
            potion_small: 0,
            potion_large: large,
            potions_healing: 0,
            healing_until: None,
        },
        active: true,
        camps: vec![CampSeen {
            index: 0,
            at: centre,
            zone: c.zone,
            elite_spot: if elite.is_some() { 0 } else { -1 },
            mob_type: 0,
            reward_stat: None,
            elite: elite.map(|_| MobTypeId::Executioner),
            alive: adds.len() as u32 + u32::from(elite.is_some()),
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
        summary: (stacks > 0).then(|| WireBuildSummary {
            gains: [0; 9],
            mob_rows: vec![WireMobBonusRow {
                mob_type: 0,
                stat: 2,
                stacks,
                total: 0,
            }],
            panel: Vec::new(),
        }),
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

fn cx<'a>(
    f: &'a mut Frame,
    goal: &'a RaidGoal,
    status: Status,
    rng: &'a mut Rng,
    prices: &'a Prices,
) -> Cx<'a> {
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
        status,
        rng,
    }
}

fn elite_goal() -> RaidGoal {
    RaidGoal::new(GoalKind::Elite, Target::Camp(0))
}

// ---- Note 1: no heal-up beside a live elite; the leave walks away from it ----------------------------------------------

fn leave_candidates(out: &[Candidate]) -> Vec<&Candidate> {
    out.iter()
        .filter(|c| c.goal.kind == GoalKind::Sustain && matches!(c.goal.target, Target::Camp(_)))
        .collect()
}

fn heal_ups(out: &[Candidate]) -> Vec<&Candidate> {
    out.iter()
        .filter(|c| c.goal.kind == GoalKind::Sustain && c.goal.target == Target::None)
        .collect()
}

/// What Sustain proposes in this frame for this goal (one `observe`, then `propose`).
fn sustain_proposes(f: &mut Frame, goal: &RaidGoal) -> Vec<Candidate> {
    let prices = &Prices::LOCAL;
    let mut rng = Rng::new(1);
    let mut s = Sustain::default();
    s.observe(&mut cx(f, goal, Status::Running, &mut rng, prices));
    let mut out = Vec::new();
    s.propose(
        &mut cx(f, goal, Status::Running, &mut rng, prices),
        &mut out,
    );
    out
}

// ---- Note 1: the kill's chest is not lost to the leave ---------------------------------------------------------------

/// The seat at `hp` holds the Elite goal on camp 0 right after the kill: the elite mob is gone, `adds` regional members
/// (full hp) stand at the camp within `FIGHT_U`, the camp's chest stands at its centre when `chest`. No potion.
fn after_the_kill(hp: f64, adds: usize, chest: bool) -> Frame {
    let mut f = frame(Some(0.9), &vec![1.; adds], Vec2::new(20., 0.), hp, 0, 0);
    f.p.mobs.retain(|m| m.class != MobClass::Elite);
    f.p.camps[0].alive = adds as u32;
    if chest {
        f.p.facts.chests.push(ChestRef {
            key: "c1".into(),
            handle: rh_protocol::RewardChestHandle::new(1).expect("a handle"),
            at: f.centre,
            expires_at: f.p.now + 55_000.,
            life_ms: 60_000.,
            options: None,
            taken_by: Vec::new(),
            picked: false,
        });
    }
    f
}

/// Required behaviour: a seat that has killed its elite is not pulled off the chest by the leave while the chest phase
/// is within its bounds (`chest_find_seconds`, `chest_wait_seconds`, both Elite's). Three adds within `FIGHT_U`, hp 0.4,
/// no potion, a chest at the camp. On the base `elite_lost` is `None` once the elite is dead, the farm model prices the
/// three adds against hp 0.4 (death chance 0.36, measured; two adds are 0.05, not lost) as lost, and Sustain proposes `Sustain/Camp(0)`: the seat walks out of the chest's reach.
#[test]
fn a_seat_that_killed_its_elite_is_not_led_off_its_chest_by_the_leave() {
    let mut f = after_the_kill(0.4, 3, true);
    assert!(
        f.p.mobs.iter().all(|m| m.class != MobClass::Elite),
        "premise: the elite is dead"
    );
    let out = sustain_proposes(&mut f, &elite_goal());
    assert!(
        leave_candidates(&out).is_empty(),
        "a leave off the elite's chest: {out:?}"
    );
}

/// Guard (green on the base): the exemption is the chest's. The same frame with no chest at the camp is a lost farm
/// fight and the leave stands (what the reviewer's smallest fix leaves alone).
#[test]
fn without_a_chest_at_the_camp_the_leave_of_a_lost_fight_stands() {
    let mut f = after_the_kill(0.4, 3, false);
    let out = sustain_proposes(&mut f, &elite_goal());
    assert!(
        !leave_candidates(&out).is_empty(),
        "premise: the farm model rates this fight lost: {out:?}"
    );
}

/// Guard (green on the base): the exemption belongs to the Elite goal. The same frame under a Farm goal on the camp
/// (the seat is no longer in the chest phase) is left by the leave.
#[test]
fn the_chest_exemption_is_for_the_elite_goal_only() {
    let mut f = after_the_kill(0.4, 3, true);
    let goal = RaidGoal::new(GoalKind::Farm, Target::Camp(0));
    let out = sustain_proposes(&mut f, &goal);
    assert!(
        !leave_candidates(&out).is_empty(),
        "a Farm goal beside a lost fight is left: {out:?}"
    );
}

/// Guard (green on the base, must stay green): the chest does not make the seat die at it. Six full adds on a seat at
/// hp 0.05 is a fight the farm model rates far past `disengage_death` (0.5, the full line, not the early one): the leave
/// still comes. This pins the bound the brief decides (D1): a death chance at or above `disengage_death` ends the chest
/// phase by Sustain's own rule.
#[test]
fn adds_that_will_kill_the_seat_still_pull_it_off_the_chest() {
    let mut f = after_the_kill(0.05, 6, true);
    let out = sustain_proposes(&mut f, &elite_goal());
    assert!(
        !leave_candidates(&out).is_empty(),
        "no leave from six adds at hp 0.05 beside the chest: {out:?}"
    );
}

// ---- Note 2: a Plague seat's own zombie is not a threat ----------------------------------------------------------------

/// A Plague seat at hp 0.5 with one zombie of `faction` 50 u away and nothing else (no regional, no elite, no hit).
fn beside_a_zombie(faction: FactionId) -> Frame {
    let mut f = frame(None, &[], Vec2::new(100., 0.), 0.5, 0, 0);
    f.p.me.faction = Some(FactionId::Plague);
    let at = Vec2::new(f.p.me.at.x + 50., f.p.me.at.y);
    let mut z = mob(77, at, MobClass::Zombie, 1.);
    z.faction = Some(faction);
    z.camp = None;
    f.p.mobs.push(z);
    f
}

/// A Plague seat's own zombie walks beside it. A Plague-faction zombie 50 u away is no threat: the seat at hp 0.5 gets the
/// heal-up (`Sustain/None`). On the base `threat` counts every living mob but a dummy, `nearest` is never `None`, and no
/// heal-up is ever proposed for a Plague seat that has its pet.
#[test]
fn a_plague_seat_beside_its_own_zombie_is_offered_the_heal_up() {
    let mut f = beside_a_zombie(FactionId::Plague);
    let goal = RaidGoal::new(GoalKind::Farm, Target::Camp(0));
    let out = sustain_proposes(&mut f, &goal);
    assert!(
        !heal_ups(&out).is_empty(),
        "no heal-up beside the seat's own zombie: {out:?}"
    );
}

/// A held leave (`Sustain/Camp`) whose only company is the seat's own zombie stands (the stand: `Running`, no intent)
/// instead of walking away from its own pet until the arbiter drops the goal at `ready_hp`.
#[test]
fn a_leave_beside_the_seats_own_zombie_stands_to_heal() {
    let prices = &Prices::LOCAL;
    let mut f = beside_a_zombie(FactionId::Plague);
    let goal = RaidGoal::new(GoalKind::Sustain, Target::Camp(0));
    let mut rng = Rng::new(1);
    let mut s = Sustain::default();
    let c = &mut cx(&mut f, &goal, Status::Running, &mut rng, prices);
    s.start(&goal, c);
    let step = s.act(&goal, c);
    assert!(
        step.status == Status::Running && step.intents.is_empty() && step.acts.is_empty(),
        "the seat walks away from its own zombie: {step:?}"
    );
}

/// Guard (green on the base): another faction's zombie in reach is still a threat: no heal-up is offered, and a held
/// leave walks away from it.
#[test]
fn another_factions_zombie_in_reach_is_still_a_threat() {
    let prices = &Prices::LOCAL;
    let mut f = beside_a_zombie(FactionId::Holy);
    let goal = RaidGoal::new(GoalKind::Farm, Target::Camp(0));
    let out = sustain_proposes(&mut f, &goal);
    assert!(
        heal_ups(&out).is_empty(),
        "heal-up beside a foe's zombie: {out:?}"
    );
    let goal = RaidGoal::new(GoalKind::Sustain, Target::Camp(0));
    let mut rng = Rng::new(1);
    let mut s = Sustain::default();
    let c = &mut cx(&mut f, &goal, Status::Running, &mut rng, prices);
    s.start(&goal, c);
    let step = s.act(&goal, c);
    assert!(
        step.intents
            .iter()
            .any(|i| matches!(i.body, IntentBody::Move { .. })),
        "the leave does not move off a foe's zombie: {step:?}"
    );
}

// ---- Note 3: one time-out is one give-up -------------------------------------------------------------------------------

fn giveups_at(el: &Elite, camp: u32) -> Option<u32> {
    el.giveups.iter().find(|g| g.camp == camp).map(|g| g.n)
}

/// Elite's frames: `observe`, then `act`, at the frame's `now`.
fn elite_frame(el: &mut Elite, f: &mut Frame, goal: &RaidGoal) -> Step {
    let prices = &Prices::LOCAL;
    let mut rng = Rng::new(1);
    el.observe(&mut cx(f, goal, Status::Running, &mut rng, prices));
    el.act(goal, &mut cx(f, goal, Status::Running, &mut rng, prices))
}

/// A fight begun (elite and one add within `FIGHT_U`), driven past `give_up_seconds`: the step fails ("ran out of time")
/// and bans the camp once. The arbiter then drops the goal; the next frame's goal switch must not ban it again. On the
/// base `fight_step` leaves `fight_since` set, `watch_goal` bans again: the count is 2 and the ban 40 s.
#[test]
fn a_fight_that_ran_out_of_time_is_one_give_up() {
    let e = Prices::LOCAL.elite;
    let mut f = frame(Some(0.9), &[1.], Vec2::new(100., 0.), 0.9, 0, 0);
    let goal = elite_goal();
    let mut el = Elite::default();
    let step = elite_frame(&mut el, &mut f, &goal);
    assert_eq!(step.status, Status::Running, "the fight begins: {step:?}");
    f.p.now += e.give_up_seconds * 1_000. + 50.;
    let step = elite_frame(&mut el, &mut f, &goal);
    assert_eq!(step.status, Status::Failed, "the time-out: {step:?}");
    let banned_at = f.p.now;
    // The goal is dropped: the next frame holds another goal.
    f.p.now += 50.;
    let other = RaidGoal::new(GoalKind::Farm, Target::Camp(0));
    let mut rng = Rng::new(1);
    el.observe(&mut cx(
        &mut f,
        &other,
        Status::Running,
        &mut rng,
        &Prices::LOCAL,
    ));
    assert_eq!(
        giveups_at(&el, 0),
        Some(1),
        "one time-out counted as more than one give-up"
    );
    assert!(
        !el.banned(0, banned_at + e.ban_seconds * 1_000. + 1.),
        "the first give-up bans for more than ban_seconds"
    );
}

/// Guard (green on the base): a fight handed away while the elite stood (the goal switches with no failure) is one
/// give-up of 20 s.
#[test]
fn a_fight_handed_away_is_one_give_up() {
    let e = Prices::LOCAL.elite;
    let mut f = frame(Some(0.9), &[1.], Vec2::new(100., 0.), 0.9, 0, 0);
    let goal = elite_goal();
    let mut el = Elite::default();
    elite_frame(&mut el, &mut f, &goal);
    f.p.now += 1_000.;
    let handed_at = f.p.now;
    let other = RaidGoal::new(GoalKind::Farm, Target::Camp(0));
    let mut rng = Rng::new(1);
    el.observe(&mut cx(
        &mut f,
        &other,
        Status::Running,
        &mut rng,
        &Prices::LOCAL,
    ));
    assert_eq!(giveups_at(&el, 0), Some(1));
    assert!(el.banned(0, handed_at + e.ban_seconds * 1_000. - 1.));
    assert!(!el.banned(0, handed_at + e.ban_seconds * 1_000. + 1.));
}

/// Guard (green on the base, must stay green): the same hp 0.4 with four adds is a death chance of 0.78 (measured), past
/// `disengage_death`: the leave comes even with the chest at the camp.
#[test]
fn four_adds_at_hp_0_4_still_pull_the_seat_off_the_chest() {
    let mut f = after_the_kill(0.4, 4, true);
    let out = sustain_proposes(&mut f, &elite_goal());
    assert!(
        !leave_candidates(&out).is_empty(),
        "no leave from four adds at hp 0.4 beside the chest: {out:?}"
    );
}

/// Note 4: a fight that began (one frame with the elite within `FIGHT_U`), then the elite alive and in sight but beyond
/// `FIGHT_U` (the seat 400 u off, no add near) for longer than `give_up_seconds`: the bound holds once `fight_since` is
/// set, `fighting` or not. The step fails and the camp is banned once. On the base the time-out is checked only while
/// `fighting`, so the step stays `Running` past the bound.
#[test]
fn a_fight_under_way_with_the_elite_out_of_reach_times_out_once() {
    let e = Prices::LOCAL.elite;
    let mut f = frame(Some(0.9), &[], Vec2::new(100., 0.), 0.9, 0, 0);
    let goal = elite_goal();
    let mut el = Elite::default();
    let step = elite_frame(&mut el, &mut f, &goal);
    assert_eq!(step.status, Status::Running, "the fight begins: {step:?}");
    assert!(el.fight_since.is_some(), "premise: the fight began");
    f.p.me.at = Vec2::new(f.centre.x + 400., f.centre.y);
    let mut failed_at = None;
    for k in 1..=130 {
        f.p.now += 1_000.;
        let step = elite_frame(&mut el, &mut f, &goal);
        if step.status == Status::Failed {
            failed_at = Some(k);
            break;
        }
    }
    let k = failed_at.expect("no time-out in 130 s with the elite out of reach");
    assert!(
        f64::from(k) > e.give_up_seconds - 1.,
        "failed early at {k} s"
    );
    let banned_at = f.p.now;
    f.p.now += 50.;
    let other = RaidGoal::new(GoalKind::Farm, Target::Camp(0));
    let mut rng = Rng::new(1);
    el.observe(&mut cx(
        &mut f,
        &other,
        Status::Running,
        &mut rng,
        &Prices::LOCAL,
    ));
    assert_eq!(giveups_at(&el, 0), Some(1), "one time-out, one give-up");
    assert!(!el.banned(0, banned_at + e.ban_seconds * 1_000. + 1.));
}
