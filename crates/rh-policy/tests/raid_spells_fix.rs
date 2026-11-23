//! P-RAID-S-fix oracle (docs/backlog/packets/P-RAID-S-fix.md): ShoulderCharge's "leaving" is a threat in reach, and the
//! dash is not asked toward an enemy player.
//!
//! The scene is the executor itself (`exec::spell::Spell::ride_along`) on hand-built percepts, the way
//! `tests/raid_hold.rs` drives `Sustain`: the scene's `leave_away` is private to the executor, so the whole path from
//! percept to the asked act is under test. Each scene holds one spell, the seat stands at `ME`, the goal is the one the
//! test names, and a fresh executor (a fresh `CastGate`) is used per scene.
use rh_policy::bots::paths::{PathBudget, RoomPathService};
use rh_policy::bots::perception::LootFacts;
use rh_policy::bots::planning::PlanMap;
use rh_policy::bots::raid::acts::{Act, CastAim};
use rh_policy::bots::raid::exec::spell::Spell;
use rh_policy::bots::raid::goal::{Cx, Executor, GoalKind, RaidGoal, Status, Target};
use rh_policy::bots::raid::percept::{Me, MobClass, MobSeen, PlayerSeen, RaidPercept};
use rh_policy::bots::raid::values::Prices;
use rh_policy::bots::random::Rng;
use rh_policy::bots::types::{HudView, OwnerLock, Percept, PersonaId, SelfView, TierId};
use rh_protocol::{WireQuestSpell, WireQuestView};
use rh_sim::{Arena, FactionId, Vec2};

const SHOULDER_CHARGE: u8 = 3;
const TURRET: u8 = 5;
const SMITE: u8 = 8;
const CLEANSE: u8 = 16;
/// The sim's `charge_range`, and the executor's reach for "a threat in reach" (`exec::farm::FIGHT_U`).
const CHARGE_RANGE: f64 = 220.;
const FIGHT_U: f64 = 240.;
const ME: Vec2 = Vec2 { x: 4000., y: 4000. };
const NOW: f64 = 10_000.;

fn arena() -> Arena {
    let path = concat!(
        env!("CARGO_MANIFEST_DIR"),
        "/../rh-sim/src/mapdata/fixtures/seed1.map.json"
    );
    let text = std::fs::read_to_string(path).expect("the seed1 fixture map");
    let file: rh_sim::mapdata::MapExport = serde_json::from_str(&text).expect("rh-map/2 json");
    file.validate()
        .expect("admitted")
        .into_arena()
        .expect("an arena")
}

fn mob(key: &str, class: MobClass, at: Vec2) -> MobSeen {
    MobSeen {
        key: key.into(),
        at,
        hp_share: 1.,
        alive: true,
        class,
        mob_type: None,
        weapon: None,
        faction: None,
        camp: Some(0),
        engaged: false,
    }
}

fn enemy(key: &str, at: Vec2) -> PlayerSeen {
    PlayerSeen {
        key: key.into(),
        at,
        hp_share: 1.,
        alive: true,
        targetable: true,
        faction: Some(FactionId::Fire),
        weapon: None,
    }
}

/// One frame: the seat at `ME`, holding `spell` in wire slot 1 with one charge, `effects` its status bits.
fn frame(
    spell: u8,
    effects: u16,
    hp: f64,
    mobs: Vec<MobSeen>,
    players: Vec<PlayerSeen>,
) -> RaidPercept {
    let base = Percept {
        now: NOW,
        hud: HudView {
            phase: "active".into(),
            gates_locked: false,
        },
        self_view: SelfView {
            x: ME.x,
            y: ME.y,
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
        tick: 200,
        base,
        facts: LootFacts::default(),
        toasts: Vec::new(),
        self_key: "p1".into(),
        me: Me {
            at: ME,
            hp: 100. * hp,
            max_hp: 100.,
            alive: true,
            faction: Some(FactionId::Holy),
            weapons: [None, None],
            slot: 0,
            effects,
            ready_at: 0.,
            potion_small: 0,
            potion_large: 0,
            potions_healing: 0,
            healing_until: None,
        },
        active: true,
        camps: Vec::new(),
        mobs,
        givers: Vec::new(),
        players,
        quests: Some(WireQuestView {
            spells: vec![WireQuestSpell {
                slot: 1,
                reward: spell,
                charges: 1,
                ready_at: 0.,
            }],
            ..WireQuestView::default()
        }),
        trade: None,
        fields: Vec::new(),
        build: None,
        summary: None,
        arena: None,
        hit_at: None,
    }
}

/// The acts a fresh spell executor asks for in `p` while the seat runs `goal`.
fn asked(goal: &RaidGoal, p: &RaidPercept) -> Vec<Act> {
    let arena = arena();
    let plan = PlanMap::from_arena(&arena);
    let mut paths = RoomPathService::new(&arena.map, PathBudget::default());
    let prices = Prices::farm_only();
    let mut rng = Rng::new(16);
    let mut cx = Cx {
        p,
        paths: &mut paths,
        plan: &plan,
        knowledge: None,
        tier: TierId::Normal,
        persona: PersonaId::Farmer,
        prices: &prices,
        still_since: 0.,
        goal,
        status: Status::Running,
        rng: &mut rng,
    };
    Spell::default().ride_along(goal, &mut cx).acts
}

fn leaving() -> RaidGoal {
    RaidGoal::new(GoalKind::Sustain, Target::Camp(0))
}

/// A mob `d` u east of the seat: the dash away runs west, landing at `ME.x - 220`.
fn mob_at(d: f64) -> MobSeen {
    mob("n7", MobClass::Regional, Vec2::new(ME.x + d, ME.y))
}

fn landing() -> Vec2 {
    Vec2::new(ME.x - CHARGE_RANGE, ME.y)
}

fn dashes(acts: &[Act]) -> Option<Vec2> {
    acts.iter().find_map(|a| match a {
        Act::Cast {
            reward: SHOULDER_CHARGE,
            aim: CastAim::Point(p),
            ..
        } => Some(*p),
        _ => None,
    })
}

// RED on the base: the heal-up after the seat walked out of reach is rest, not a leave.
#[test]
fn a_seat_resting_out_of_reach_of_every_mob_holds_the_dash() {
    for d in [FIGHT_U + 10., 500., 734.] {
        let p = frame(SHOULDER_CHARGE, 0, 0.58, vec![mob_at(d)], vec![]);
        let acts = asked(&leaving(), &p);
        assert!(acts.is_empty(), "a mob {d} u away is no leave: {acts:?}");
    }
}

// Guard: a threat in reach is the leave the rule is for.
#[test]
fn a_seat_with_a_mob_in_reach_dashes_away_from_it() {
    let p = frame(SHOULDER_CHARGE, 0, 0.3, vec![mob_at(150.)], vec![]);
    let acts = asked(&leaving(), &p);
    let at = dashes(&acts).unwrap_or_else(|| panic!("a dash is asked, got {acts:?}"));
    assert!(at.x < ME.x - 1., "away from the mob (west): {at:?}");
    assert!((at.y - ME.y).abs() < 1e-6, "straight away: {at:?}");
}

// RED on the base: an enemy player where the dash would land.
#[test]
fn the_dash_is_not_asked_toward_an_enemy_player_at_the_landing_point() {
    for offset in [0., 100., 150.] {
        let player = enemy("p9", Vec2::new(landing().x - offset, landing().y));
        let p = frame(SHOULDER_CHARGE, 0, 0.05, vec![mob_at(150.)], vec![player]);
        let acts = asked(&leaving(), &p);
        assert!(
            dashes(&acts).is_none(),
            "an enemy {offset} u past the landing point: {acts:?}"
        );
    }
}

// Guard: a far player, a dead one, and one of the seat's own faction do not stop the dash.
#[test]
fn a_player_far_from_the_landing_point_does_not_stop_the_dash() {
    let far = enemy("p9", Vec2::new(ME.x, ME.y + 700.));
    let mut dead = enemy("p8", landing());
    dead.alive = false;
    let mut friend = enemy("p6", landing());
    friend.faction = Some(FactionId::Holy);
    let p = frame(
        SHOULDER_CHARGE,
        0,
        0.3,
        vec![mob_at(150.)],
        vec![far, dead, friend],
    );
    let acts = asked(&leaving(), &p);
    assert!(dashes(&acts).is_some(), "the dash is asked: {acts:?}");
}

// Guards: the other three rules answer as before in the same kind of scene.
#[test]
fn cleanse_smite_and_turret_answer_as_before() {
    let rooted = frame(CLEANSE, 2, 0.5, vec![mob_at(500.)], vec![]);
    let acts = asked(&leaving(), &rooted);
    assert!(
        matches!(
            acts.as_slice(),
            [Act::Cast {
                reward: CLEANSE,
                aim: CastAim::OwnPosition,
                ..
            }]
        ),
        "Cleanse on a rooted seat: {acts:?}"
    );

    let elite = || mob("n3", MobClass::Elite, Vec2::new(ME.x + 300., ME.y));
    let fight = RaidGoal::new(GoalKind::Elite, Target::Camp(0));
    let smite = asked(&fight, &frame(SMITE, 0, 1., vec![elite()], vec![]));
    assert!(
        matches!(
            smite.as_slice(),
            [Act::Cast {
                reward: SMITE,
                aim: CastAim::Mob { elite: true, .. },
                ..
            }]
        ),
        "Smite at an elite in reach: {smite:?}"
    );
    let turret = asked(&fight, &frame(TURRET, 0, 1., vec![elite()], vec![]));
    assert!(
        matches!(
            turret.as_slice(),
            [Act::Cast {
                reward: TURRET,
                aim: CastAim::OwnPosition,
                ..
            }]
        ),
        "Turret with an elite in its reach: {turret:?}"
    );
}
