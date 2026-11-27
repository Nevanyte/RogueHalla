//! P-RAID-0-fix oracle (docs/backlog/packets/P-RAID-0-fix.md): the arbiter lets a Sustain candidate pre-empt a committed
//! fight, the death model and the chest pick are priced through `Prices`, `Prices` answers for quest rewards and
//! augments, and the farm follows the mob the owner has locked. Read-only for the implementer.
use super::arbiter::Arbiter;
use super::exec::chest::Chest;
use super::exec::farm::Farm;
use super::goal::{Candidate, Cx, Executor, GoalKind, RaidGoal, Status, Target};
use super::percept::{CampSeen, Me, MobClass, MobSeen, RaidPercept};
use super::values::Prices;
use crate::bots::paths::{PathBudget, RoomPathService};
use crate::bots::perception::{ChestRef, LootFacts};
use crate::bots::planning::PlanMap;
use crate::bots::random::Rng;
use crate::bots::types::{
    HudView, IntentBody, OwnerLock, Percept, PersonaId, PersonaParams, SelfView, TierId, TierParams,
};
use rh_sim::{Arena, FactionId, Vec2};

fn tier() -> &'static TierParams {
    TierParams::by_id(TierId::Normal)
}
fn persona() -> &'static PersonaParams {
    PersonaParams::by_id(PersonaId::Farmer)
}

fn cand(kind: GoalKind, target: Target, value: f64, minutes: f64) -> Candidate {
    Candidate {
        goal: RaidGoal::new(kind, target),
        value,
        success: 1.,
        death: 0.,
        loss: 0.,
        minutes,
    }
}

// ---- 1. a committed fight can be pre-empted -------------------------------------------------------------------

/// The held goal: a farm fight, committed and running, whose own score stays positive (death priced at 0.02).
fn held_farm() -> (RaidGoal, Candidate) {
    let goal = RaidGoal::new(GoalKind::Farm, Target::Camp(7));
    let mut c = cand(GoalKind::Farm, Target::Camp(7), 1., 1.);
    c.death = 0.02;
    c.loss = 6.;
    (goal, c)
}

fn choose_committed(candidates: Vec<Candidate>) -> Option<RaidGoal> {
    let (held, _) = held_farm();
    Arbiter::default().choose(
        0.,
        candidates,
        (&held, Status::Running, true),
        tier(),
        persona(),
        &mut Rng::new(16),
    )
}

#[test]
fn only_sustain_may_claim_a_committed_fight() {
    assert!(
        GoalKind::Sustain.preempts_commitment(),
        "Sustain (potion, retreat) is the kind that escapes a fight under way"
    );
    for kind in [GoalKind::Farm, GoalKind::Roam, GoalKind::Chest] {
        assert!(
            !kind.preempts_commitment(),
            "{kind:?} must not walk away from a committed fight"
        );
    }
}

#[test]
fn a_top_ranked_sustain_candidate_takes_over_from_a_committed_fight_with_a_positive_score() {
    let (_, held_c) = held_farm();
    assert!(
        super::arbiter::rate(&held_c) > 0.,
        "the fight scores positive"
    );
    // Sustain is far better per minute than the fight, so it ranks first.
    let sustain = cand(GoalKind::Sustain, Target::None, 3., 0.2);
    let got = choose_committed(vec![held_c, sustain]);
    assert_eq!(
        got,
        Some(RaidGoal::new(GoalKind::Sustain, Target::None)),
        "a committed fight must not keep Sustain from winning"
    );
}

#[test]
fn a_committed_fight_is_kept_against_a_top_ranked_candidate_that_does_not_pre_empt() {
    let (held, held_c) = held_farm();
    // Another camp ranks first, and so does a roam: neither pre-empts, so the fight stays (as before the fix).
    let other = cand(GoalKind::Farm, Target::Camp(3), 4., 1.);
    assert_eq!(
        choose_committed(vec![held_c.clone(), other]),
        Some(held.clone())
    );
    let roam = cand(GoalKind::Roam, Target::None, 9., 1.);
    assert_eq!(
        choose_committed(vec![held_c.clone(), roam]),
        Some(held.clone())
    );
    // A Sustain candidate that does not rank first does not take over either: only the top-ranked one counts.
    let weak_sustain = cand(GoalKind::Sustain, Target::None, 0.2, 1.);
    let strong_other = cand(GoalKind::Farm, Target::Camp(3), 4., 1.);
    assert_eq!(
        choose_committed(vec![held_c, weak_sustain, strong_other]),
        Some(held)
    );
}

// ---- 3. the value table answers for rewards and augments -------------------------------------------------------

#[test]
fn prices_answer_for_quest_rewards_and_augments_from_their_table() {
    let local = Prices::LOCAL;
    for def in ["kill_slimes", "anything_else"] {
        let v = local.reward_value(def);
        assert!(v.is_finite() && v >= 0., "reward_value({def}) = {v}");
    }
    let v = local.augment_value("any_card");
    assert!(v.is_finite() && v >= 0., "augment_value = {v}");
    // The table is read: a doctored one changes what the accessors return.
    let doctored = Prices {
        reward_default: 7.5,
        augment_default: 2.25,
        ..Prices::LOCAL
    };
    assert_eq!(doctored.reward_value("kill_slimes"), 7.5);
    assert_eq!(
        doctored.reward_value("another"),
        7.5,
        "one flat default for now"
    );
    assert_eq!(doctored.augment_value("any_card"), 2.25);
    assert_eq!(doctored.augment_value("other_card"), 2.25);
}

// ---- scenes for the executors ------------------------------------------------------------------------------------

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

fn mob(id: u32, at: Vec2, camp: u32, hp_share: f64) -> MobSeen {
    MobSeen {
        key: format!("n{id}"),
        at,
        hp_share,
        alive: true,
        class: MobClass::Regional,
        mob_type: None,
        weapon: None,
        faction: None,
        camp: Some(camp),
        engaged: false,
    }
}

/// Camp 0 of the seed1 map with `members` regional members standing at it, the owner (Holy, full hp unless set)
/// `owner_at` from the camp centre, in an active raid.
fn scene(members: &[(Vec2, f64)], owner_off: Vec2, hp_share: f64) -> Scene {
    let arena = arena();
    let plan = PlanMap::from_arena(&arena);
    let paths = RoomPathService::new(&arena.map, PathBudget::default());
    let c = &plan.camps[0];
    let centre = Vec2::new(c.x, c.y);
    let mobs: Vec<MobSeen> = members
        .iter()
        .enumerate()
        .map(|(i, (off, hp))| {
            mob(
                10 + i as u32,
                Vec2::new(centre.x + off.x, centre.y + off.y),
                0,
                *hp,
            )
        })
        .collect();
    let at = Vec2::new(centre.x + owner_off.x, centre.y + owner_off.y);
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
            hp: 100. * hp_share,
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
            hp: 100. * hp_share,
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
            alive: members.len() as u32,
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

fn farm_candidate(prices: &Prices, members: usize, hp_share: f64) -> Candidate {
    let ms: Vec<(Vec2, f64)> = (0..members)
        .map(|i| (Vec2::new(8. * i as f64, 0.), 1.))
        .collect();
    let mut s = scene(&ms, Vec2::new(200., 0.), hp_share);
    let goal = RaidGoal::new(GoalKind::Roam, Target::None);
    let mut rng = Rng::new(16);
    let mut cx = Cx {
        p: &s.p,
        paths: &mut s.paths,
        plan: &s.plan,
        knowledge: None,
        tier: TierId::Normal,
        persona: PersonaId::Farmer,
        prices,
        still_since: 0.,
        goal: &goal,
        status: Status::Done,
        rng: &mut rng,
    };
    let mut out = Vec::new();
    Farm::default().propose(&mut cx, &mut out);
    assert_eq!(out.len(), 1, "one eligible camp: {out:?}");
    out.remove(0)
}

fn near(a: f64, b: f64) -> bool {
    (a - b).abs() < 1e-9
}

// ---- 2. the death model is part of `Prices` ----------------------------------------------------------------------

/// `Prices::LOCAL` is today's numbers: the values below were measured on `coord/int-82` @ `81c42a70a` before the
/// constants moved (`FIGHT_WEIGHT 3`, `DEATH_PER_RATIO 0.4`, `SAFE_RATIO 0.65`, `TYPICAL_MEMBERS 4`), so moving them into
/// `Prices` must not move RaidBot's play.
#[test]
fn local_prices_reproduce_the_death_model_the_bot_played_with() {
    let five = farm_candidate(&Prices::LOCAL, 5, 1.);
    assert!(near(five.death, 0.3621675388783048), "{five:?}");
    assert!(near(five.success, 1. - 0.3621675388783048), "{five:?}");
    assert!(
        near(five.value, 1.25),
        "camp x alive / typical members: {five:?}"
    );
    assert!(near(five.minutes, 0.8529411764705883), "{five:?}");
    assert!(near(five.loss, 6.));
    let three = farm_candidate(&Prices::LOCAL, 3, 0.5);
    assert!(near(three.death, 0.23773403110264382), "{three:?}");
    assert!(near(three.value, 0.75), "{three:?}");
    let fresh = farm_candidate(&Prices::LOCAL, 3, 1.);
    assert!(
        near(fresh.death, 0.02),
        "a fresh three-member camp is the floor: {fresh:?}"
    );
    // The model itself (a field, not a hidden constant).
    let m = Prices::LOCAL.death;
    assert_eq!(
        (
            m.fight_weight,
            m.death_per_ratio,
            m.safe_ratio,
            m.typical_members
        ),
        (3., 0.4, 0.65, 4.)
    );
    assert_eq!(
        (Prices::LOCAL.pick_seconds, Prices::LOCAL.pick_success),
        (3., 0.9)
    );
}

#[test]
fn the_farm_candidate_reads_the_death_model_from_prices() {
    let base3 = farm_candidate(&Prices::LOCAL, 3, 0.5);
    let base5 = farm_candidate(&Prices::LOCAL, 5, 1.);
    let with = |f: &dyn Fn(&mut Prices)| {
        let mut p = Prices::LOCAL;
        f(&mut p);
        p
    };
    // A later safe point lowers the death chance by exactly death_per_ratio x the shift (both unclamped).
    let safer = farm_candidate(&with(&|p| p.death.safe_ratio = 0.95), 3, 0.5);
    assert!(
        near(safer.death, base3.death - 0.4 * 0.3),
        "safe_ratio unread: {safer:?}"
    );
    // Half the slope halves the death chance (unclamped at both).
    let gentler = farm_candidate(&with(&|p| p.death.death_per_ratio = 0.2), 5, 1.);
    assert!(
        near(gentler.death, base5.death / 2.),
        "death_per_ratio unread: {gentler:?}"
    );
    // Twice the fight weight doubles the hp a clear costs: death = 0.4 (2r - 0.65) = 2 x base + 0.26.
    let heavier = farm_candidate(&with(&|p| p.death.fight_weight = 6.), 3, 0.5);
    assert!(
        near(heavier.death, 2. * base3.death + 0.26),
        "fight_weight unread: {heavier:?}"
    );
    // Typical members rescale what a camp is worth (the camp's value is camp x alive / typical).
    let bigger = farm_candidate(&with(&|p| p.death.typical_members = 8.), 5, 1.);
    assert!(
        near(bigger.value, base5.value / 2.),
        "typical_members unread: {bigger:?}"
    );
    // The death loss stays a Prices field (already read before this packet).
    let costly = farm_candidate(&with(&|p| p.death_loss = 9.), 5, 1.);
    assert!(near(costly.loss, 9.));
}

// ---- 2b. the chest pick is part of `Prices` ----------------------------------------------------------------------

fn chest_candidate(prices: &Prices) -> Candidate {
    let mut s = scene(&[], Vec2::new(0., 0.), 1.);
    let at = s.p.me.at;
    s.p.facts.chests.push(ChestRef {
        key: "chest-1".into(),
        handle: rh_protocol::RewardChestHandle::new(1).expect("a handle"),
        at: Vec2::new(at.x + 20., at.y),
        expires_at: s.p.now + 60_000.,
        life_ms: 60_000.,
        options: Some(Vec::new()),
        taken_by: Vec::new(),
        picked: false,
    });
    let goal = RaidGoal::new(GoalKind::Roam, Target::None);
    let mut rng = Rng::new(16);
    let mut cx = Cx {
        p: &s.p,
        paths: &mut s.paths,
        plan: &s.plan,
        knowledge: None,
        tier: TierId::Normal,
        persona: PersonaId::Farmer,
        prices,
        still_since: 0.,
        goal: &goal,
        status: Status::Done,
        rng: &mut rng,
    };
    let mut out = Vec::new();
    Chest::default().propose(&mut cx, &mut out);
    assert_eq!(out.len(), 1, "an open chest in reach is proposed: {out:?}");
    out.remove(0)
}

#[test]
fn the_chest_candidate_reads_the_pick_from_prices() {
    let base = chest_candidate(&Prices::LOCAL);
    assert!(near(base.success, 0.9), "today's PICK_SUCCESS: {base:?}");
    assert!(near(base.value, 1.5));
    let slow = chest_candidate(&Prices {
        pick_seconds: 33.,
        pick_success: 0.5,
        ..Prices::LOCAL
    });
    assert!(near(slow.success, 0.5), "pick_success unread: {slow:?}");
    // 30 more seconds of picking: exactly half a minute longer.
    assert!(
        near(slow.minutes, base.minutes + 0.5),
        "pick_seconds unread: {slow:?} vs {base:?}"
    );
}

// ---- 4. the farm follows the mob the owner has locked ------------------------------------------------------------

/// One think of the farm on camp 0 in `s` (status Running); returns the targets of the lock intents it sent.
fn locks(farm: &mut Farm, s: &mut Scene) -> Vec<String> {
    let goal = RaidGoal::new(GoalKind::Farm, Target::Camp(0));
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
        goal: &goal,
        status: Status::Running,
        rng: &mut rng,
    };
    let step = farm.act(&goal, &mut cx);
    assert_eq!(step.status, Status::Running, "{step:?}");
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

/// Two members in melee reach, the nearer one fought first. Next frame their keys are new (as in the wire) and the
/// owner holds a lock on the FARTHER one: that is the mob it fights, wherever position tracking would point.
#[test]
fn the_farm_keeps_the_mob_the_owner_has_locked() {
    let members = [(Vec2::new(10., 0.), 1.), (Vec2::new(30., 0.), 1.)];
    let mut farm = Farm::default();
    let mut first = scene(&members, Vec2::new(0., 0.), 1.);
    assert_eq!(
        locks(&mut farm, &mut first),
        vec!["n10".to_owned()],
        "the nearest first"
    );
    let mut next = scene(&members, Vec2::new(0., 0.), 1.);
    next.p.mobs[0].key = "n20".into();
    next.p.mobs[1].key = "n21".into();
    hold(&mut next, "n21");
    assert_eq!(
        locks(&mut farm, &mut next),
        vec!["n21".to_owned()],
        "the held lock is the mob's identity; position is only the fallback"
    );
    // Without a held lock the position fallback still follows the member it was fighting.
    let mut moved = scene(&members, Vec2::new(0., 0.), 1.);
    moved.p.mobs[0].key = "n30".into();
    moved.p.mobs[1].key = "n31".into();
    assert_eq!(locks(&mut farm, &mut moved), vec!["n31".to_owned()]);
}

/// A held lock on a mob of another camp is not followed: the kept target is restricted to the goal's camp, even when
/// that mob stands nearer to where the last target was than any member of the camp.
#[test]
fn the_farm_does_not_follow_a_lock_or_a_position_into_another_camp() {
    let members = [(Vec2::new(10., 0.), 1.), (Vec2::new(30., 0.), 1.)];
    let mut farm = Farm::default();
    let mut first = scene(&members, Vec2::new(0., 0.), 1.);
    assert_eq!(locks(&mut farm, &mut first), vec!["n10".to_owned()]);
    // Next frame: the first member has moved out of melee reach (60), a foreign mob (camp 1) stands where it was
    // (12, not engaged) and the owner's lock is on the foreign mob.
    let mut next = scene(&members, Vec2::new(0., 0.), 1.);
    next.p.mobs[0].at.x += 50.;
    next.p.mobs[0].key = "n20".into();
    next.p.mobs[1].key = "n21".into();
    let centre = next.p.camps[0].at;
    next.p
        .mobs
        .push(mob(30, Vec2::new(centre.x + 12., centre.y), 1, 1.));
    hold(&mut next, "n30");
    assert_eq!(
        locks(&mut farm, &mut next),
        vec!["n21".to_owned()],
        "the camp's own member in reach, not the foreign mob"
    );
}
