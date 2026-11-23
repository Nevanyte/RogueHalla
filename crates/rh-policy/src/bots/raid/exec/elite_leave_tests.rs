//! P-RAID-E-leave oracle (docs/backlog/packets/P-RAID-E-leave.md), in-crate on hand-built frames: a seat beside a live
//! elite does not heal up there and walks away, Sustain's "lost" decision for an Elite goal is the elite's death term,
//! the ban of a camp grows with its give-ups, and a drink does not change the hp held or the price of the fight.
//! Read-only for the implementer.
use super::*;
use crate::bots::paths::{PathBudget, RoomPathService};
use crate::bots::perception::LootFacts;
use crate::bots::planning::PlanMap;
use crate::bots::raid::exec::farm::Farm;
use crate::bots::raid::exec::held::{hp_held, HotWatch};
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

fn near(a: f64, b: f64) -> bool {
    (a - b).abs() < 1e-9
}

// ---- Note 1: no heal-up beside a live elite; the leave walks away from it ----------------------------------------------

/// The adds are dead, the elite stands 100 u from the seat, hp 0.5: the base proposes `Sustain/None` (nothing regional is
/// near), takes the seat from the falling Elite goal and stands still beside the elite until it dies.
#[test]
fn no_heal_up_is_proposed_with_a_live_elite_in_reach() {
    let prices = &Prices::LOCAL;
    let mut f = frame(Some(0.9), &[], Vec2::new(100., 0.), 0.5, 0, 0);
    let goal = elite_goal();
    let mut rng = Rng::new(1);
    let mut s = Sustain::default();
    s.observe(&mut cx(&mut f, &goal, Status::Running, &mut rng, prices));
    let mut out = Vec::new();
    s.propose(
        &mut cx(&mut f, &goal, Status::Running, &mut rng, prices),
        &mut out,
    );
    let heal_up: Vec<&Candidate> = out
        .iter()
        .filter(|c| c.goal.kind == GoalKind::Sustain && c.goal.target == Target::None)
        .collect();
    assert!(
        heal_up.is_empty(),
        "a heal-up beside a live elite (100 u away): {out:?}"
    );
}

/// A `Sustain/None` goal already held when the elite is beside the seat does not stand still ("healing up" with no
/// intent leaves the sim's target lock on the elite): it ends, or it moves.
#[test]
fn a_heal_up_held_beside_a_live_elite_does_not_stand() {
    let prices = &Prices::LOCAL;
    let mut f = frame(Some(0.9), &[], Vec2::new(100., 0.), 0.5, 0, 0);
    let goal = RaidGoal::new(GoalKind::Sustain, Target::None);
    let mut rng = Rng::new(1);
    let mut s = Sustain::default();
    let c = &mut cx(&mut f, &goal, Status::Running, &mut rng, prices);
    s.start(&goal, c);
    let step = s.act(&goal, c);
    let stands = step.status == Status::Running && step.intents.is_empty() && step.acts.is_empty();
    assert!(!stands, "stands still beside the elite: {step:?}");
}

/// A leave (`Sustain/Camp`) with only the elite near walks away from it: a move whose target is farther from the elite
/// than the seat is.
#[test]
fn a_leave_with_only_the_elite_near_walks_away_from_it() {
    let prices = &Prices::LOCAL;
    let mut f = frame(Some(0.9), &[], Vec2::new(100., 0.), 0.5, 0, 0);
    let elite_at = f.centre;
    let seat = f.p.me.at;
    let goal = RaidGoal::new(GoalKind::Sustain, Target::Camp(0));
    let mut rng = Rng::new(1);
    let mut s = Sustain::default();
    let c = &mut cx(&mut f, &goal, Status::Running, &mut rng, prices);
    s.start(&goal, c);
    let step = s.act(&goal, c);
    let to = step.intents.iter().find_map(|i| match &i.body {
        IntentBody::Move { to } => Some(*to),
        _ => None,
    });
    let to = to.unwrap_or_else(|| panic!("no move away from the elite: {step:?}"));
    assert!(
        dist(to, elite_at) >= dist(seat, elite_at) + 20.,
        "the move {to:?} is not away from the elite at {elite_at:?} (seat {seat:?})"
    );
}

// ---- Note 2a: one view of an elite fight -------------------------------------------------------------------------------

/// The pure decision: a favourable fight (executioner, sword, 36 stacks, hp 0.5 with potions to 1.3, the elite at 0.365)
/// is not lost; an unfavourable one (avalancheTroll, sword, 24 stacks, hp 0.5 and nothing else, the elite full) is; a seat
/// at or above `ready_hp` is never leaving.
#[test]
fn the_elite_death_term_says_when_the_elite_fight_is_lost() {
    let p = &Prices::LOCAL;
    let winning = elite_fight_lost(
        p,
        MobTypeId::Executioner,
        Some(WeaponId::Sword),
        36.,
        0.5,
        1.3,
        0.365,
    );
    assert!(!winning, "a winning elite fight is called lost");
    let losing = elite_fight_lost(
        p,
        MobTypeId::AvalancheTroll,
        Some(WeaponId::Sword),
        24.,
        0.5,
        0.5,
        1.,
    );
    assert!(losing, "a lost elite fight is not called lost");
    let healthy = elite_fight_lost(
        p,
        MobTypeId::AvalancheTroll,
        Some(WeaponId::Sword),
        24.,
        0.9,
        0.9,
        1.,
    );
    assert!(!healthy, "a seat at hp 0.9 (ready_hp 0.85) is leaving");
}

/// Sustain's own proposal for a held Elite goal: four regional adds hit the seat (hp 0.5, one large potion) while the
/// elite, at 0.365 of its hp, is a fight the elite term rates as winning (36 stacks, sword). The farm model on the four adds
/// calls it lost (the base proposes a leave); with one view no leave is proposed.
#[test]
fn sustain_does_not_leave_a_fight_the_elite_term_rates_as_winning() {
    let prices = &Prices::LOCAL;
    let mut f = frame(
        Some(0.365),
        &[1., 1., 1., 1.],
        Vec2::new(40., 0.),
        0.5,
        1,
        36,
    );
    f.p.hit_at = Some(f.p.now);
    let goal = elite_goal();
    let mut rng = Rng::new(1);
    let mut s = Sustain::default();
    s.observe(&mut cx(&mut f, &goal, Status::Running, &mut rng, prices));
    let mut out = Vec::new();
    s.propose(
        &mut cx(&mut f, &goal, Status::Running, &mut rng, prices),
        &mut out,
    );
    let leaves: Vec<&Candidate> = out
        .iter()
        .filter(|c| c.goal.kind == GoalKind::Sustain && c.goal.target != Target::None)
        .collect();
    assert!(
        leaves.is_empty(),
        "Sustain leaves a fight the elite term rates as winning: {out:?}"
    );
}

// ---- Note 2b: the ban grows with the give-ups --------------------------------------------------------------------------

#[test]
fn the_ban_after_n_give_ups_is_longer_than_after_one_and_capped() {
    let e = Prices::LOCAL.elite;
    let first = ban_seconds_after(&e, 1);
    assert!(near(first, e.ban_seconds), "the first give-up: {first}");
    let mut was = first;
    for n in 2..=4 {
        let s = ban_seconds_after(&e, n);
        assert!(s > was, "give-up {n}: {s} s is not longer than {was} s");
        was = s;
    }
    assert!(near(ban_seconds_after(&e, 40), e.ban_max_seconds));
    assert!(ban_seconds_after(&e, 40) > first);
    let flat = ElitePrices {
        ban_growth: 1.,
        ..e
    };
    assert!(near(ban_seconds_after(&flat, 5), flat.ban_seconds));
}

/// The bookkeeping: `Elite::ban` at one camp three times (each after the last ban ran out): the second ban outlasts the
/// first's length, the third outlasts the second's, and many are held to `ban_max_seconds`.
#[test]
fn the_executor_bans_a_camp_longer_at_each_give_up() {
    let e = Prices::LOCAL.elite;
    let ms = |s: f64| s * 1_000.;
    let mut el = Elite::default();
    el.ban(1, 0., &e);
    assert!(el.banned(1, ms(e.ban_seconds) - 1.));
    assert!(!el.banned(1, ms(e.ban_seconds) + 1.));
    let t2 = ms(100.);
    el.ban(1, t2, &e);
    assert!(
        el.banned(1, t2 + ms(e.ban_seconds) + 1.),
        "the second give-up bans no longer than the first"
    );
    let t3 = ms(1_000.);
    el.ban(1, t3, &e);
    assert!(
        el.banned(1, t3 + ms(ban_seconds_after(&e, 2)) + 1.),
        "the third give-up bans no longer than the second"
    );
    let mut many = Elite::default();
    for k in 0..12 {
        many.ban(1, ms(10_000. * f64::from(k)), &e);
    }
    let t = ms(10_000. * 11.);
    assert!(many.banned(1, t + ms(e.ban_max_seconds) - 1.));
    assert!(!many.banned(1, t + ms(e.ban_max_seconds) + 1.));
    assert!(
        !many.banned(2, t + 1.),
        "another camp is not banned by this one's count"
    );
}

// ---- Note 3: a drink changes no hp held and no price -------------------------------------------------------------------

#[test]
fn a_drink_does_not_change_the_hp_held() {
    let s = Prices::LOCAL.sustain;
    let before = hp_held(&s, 0.5, 1, 0, 0, None, 1_000., &[]);
    assert!(near(before, 0.5 + s.large_share), "before: {before}");
    let at_drink = hp_held(
        &s,
        0.5,
        0,
        0,
        1,
        Some(1_000. + s.heal_seconds * 1_000.),
        1_000.,
        &[s.large_share],
    );
    assert!(
        near(at_drink, before),
        "a drink moved the hp held from {before} to {at_drink}"
    );
    // Half way through the heal: the hp has risen by half the share and half of it is still to come.
    let half = hp_held(
        &s,
        0.5 + s.large_share / 2.,
        0,
        0,
        1,
        Some(1_000. + s.heal_seconds * 500.),
        1_000. + s.heal_seconds * 500.,
        &[s.large_share],
    );
    assert!(near(half, before), "half way through: {half}");
    // Sustain off: the hp share alone.
    assert!(near(
        hp_held(&Prices::farm_only().sustain, 0.5, 1, 0, 0, None, 0., &[]),
        0.5
    ));
}

#[test]
fn the_heal_watch_reads_a_stock_that_fell_as_a_drink() {
    let s = Prices::LOCAL.sustain;
    let mut w = HotWatch::default();
    w.watch(&s, 1, 1, 0);
    assert!(w.shares().is_empty());
    w.watch(&s, 0, 1, 1);
    assert_eq!(w.shares(), &[s.large_share]);
    w.watch(&s, 0, 0, 2);
    assert_eq!(w.shares(), &[s.large_share, s.small_share]);
    w.watch(&s, 0, 0, 0);
    assert!(w.shares().is_empty(), "no heal runs: nothing to come");
}

/// The frame before a drink (hp 0.6, one large potion) and the frame it is drunk in (hp 0.6, no potion, one heal running
/// with its whole share to come): the elite candidate's death term is the same.
#[test]
fn the_elite_candidate_does_not_change_when_the_seat_drinks() {
    let prices = &Prices::LOCAL;
    let death_in = |el: &mut Elite, f: &mut Frame| {
        let goal = RaidGoal::new(GoalKind::Roam, Target::None);
        let mut rng = Rng::new(1);
        el.observe(&mut cx(f, &goal, Status::Done, &mut rng, prices));
        let mut out = Vec::new();
        el.propose(&mut cx(f, &goal, Status::Done, &mut rng, prices), &mut out);
        out.iter()
            .find(|c| c.goal.kind == GoalKind::Elite)
            .map(|c| c.death)
            .expect("the elite camp is proposed")
    };
    let mut el = Elite::default();
    let mut f = frame(Some(1.), &[1., 1.], Vec2::new(200., 0.), 0.6, 1, 0);
    let before = death_in(&mut el, &mut f);
    f.p.now += 50.;
    f.p.me.potion_large = 0;
    f.p.me.potions_healing = 1;
    f.p.me.healing_until = Some(f.p.now + prices.sustain.heal_seconds * 1_000.);
    let after = death_in(&mut el, &mut f);
    assert!(
        before > 0.02 && before < 0.98,
        "the frame must not sit on a clamp: {before}"
    );
    assert!(
        near(before, after),
        "a drink moved the elite death term from {before} to {after}"
    );
}

/// The same for Farm's candidate on a normal camp of four members.
#[test]
fn the_farm_candidate_does_not_change_when_the_seat_drinks() {
    let prices = &Prices::LOCAL;
    let death_in = |farm: &mut Farm, f: &mut Frame| {
        let goal = RaidGoal::new(GoalKind::Roam, Target::None);
        let mut rng = Rng::new(1);
        farm.observe(&mut cx(f, &goal, Status::Done, &mut rng, prices));
        let mut out = Vec::new();
        farm.propose(&mut cx(f, &goal, Status::Done, &mut rng, prices), &mut out);
        out.iter()
            .find(|c| c.goal.kind == GoalKind::Farm)
            .map(|c| c.death)
            .expect("the camp is proposed")
    };
    let mut farm = Farm::default();
    let mut f = frame(None, &[1., 1., 1., 1.], Vec2::new(200., 0.), 0.6, 1, 0);
    let before = death_in(&mut farm, &mut f);
    f.p.now += 50.;
    f.p.me.potion_large = 0;
    f.p.me.potions_healing = 1;
    f.p.me.healing_until = Some(f.p.now + prices.sustain.heal_seconds * 1_000.);
    let after = death_in(&mut farm, &mut f);
    assert!(
        before > 0.02 && before < 0.98,
        "the frame must not sit on a clamp: {before}"
    );
    assert!(
        near(before, after),
        "a drink moved the farm death term from {before} to {after}"
    );
}

// ---- Guard --------------------------------------------------------------------------------------------------------------

/// `farm_only()` still switches Elite and Sustain off, in the frames above.
#[test]
fn farm_only_proposes_nothing_from_elite_or_sustain() {
    let prices = &Prices::farm_only();
    let mut f = frame(Some(0.9), &[], Vec2::new(100., 0.), 0.5, 1, 36);
    let goal = elite_goal();
    let mut rng = Rng::new(1);
    let mut out = Vec::new();
    let mut el = Elite::default();
    el.observe(&mut cx(&mut f, &goal, Status::Running, &mut rng, prices));
    el.propose(
        &mut cx(&mut f, &goal, Status::Running, &mut rng, prices),
        &mut out,
    );
    let mut s = Sustain::default();
    s.observe(&mut cx(&mut f, &goal, Status::Running, &mut rng, prices));
    s.propose(
        &mut cx(&mut f, &goal, Status::Running, &mut rng, prices),
        &mut out,
    );
    assert!(out.is_empty(), "farm_only proposed {out:?}");
}
