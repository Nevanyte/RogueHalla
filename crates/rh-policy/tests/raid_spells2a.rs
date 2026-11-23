//! P-RAID-S2a oracle (docs/backlog/packets/P-RAID-S2a.md): the Hook and Bolas rules, the first two of the fifteen spells the
//! rule table left `Never`. Both are skillshots aimed at the enemy player the seat is fighting; neither is ever cast at a
//! camp (the shot hits the first enemy on its line and would wake what it meets).
//!
//! Like `raid_spells_fix.rs`, the scene is the executor itself (`exec::spell::Spell::ride_along`) on hand-built percepts,
//! one spell held in wire slot 1 (the E key), the seat at `ME` (west of every foe), a fresh executor per scene.
use rh_policy::bots::paths::{PathBudget, RoomPathService};
use rh_policy::bots::perception::LootFacts;
use rh_policy::bots::planning::PlanMap;
use rh_policy::bots::raid::acts::{Act, CastAim};
use rh_policy::bots::raid::exec::combat::may_fight_from;
use rh_policy::bots::raid::exec::spell::Spell;
use rh_policy::bots::raid::goal::{Cx, Executor, GoalKind, RaidGoal, Status, Target};
use rh_policy::bots::raid::percept::{Me, MobClass, MobSeen, PlayerSeen, RaidPercept};
use rh_policy::bots::raid::values::Prices;
use rh_policy::bots::random::Rng;
use rh_policy::bots::types::{HudView, OwnerLock, Percept, PersonaId, SelfView, TierId};
use rh_protocol::{WireQuestSpell, WireQuestView};
use rh_sim::{Arena, FactionId, Vec2};

const HOOK: u8 = 2;
const BOLAS: u8 = 19;
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

/// Camp 0's first resting spot on the fixture map: a point inside that camp's leash band, where a seat may fight its
/// members (`exec::combat::may_fight_from`). `ME` is outside every band of camp 0 (P-RAID-S-fix2).
fn in_camp_0() -> Vec2 {
    let arena = arena();
    let data = arena.map.layers().expect("layers").data();
    let s = &data.camps[0].spots[0];
    let at = Vec2::new(s.x, s.y);
    let paths = RoomPathService::new(&arena.map, PathBudget::default());
    assert!(
        may_fight_from(&paths, 0, true, at) && !may_fight_from(&paths, 0, true, ME),
        "the spot is inside camp 0's band and ME is not"
    );
    at
}

/// `p` with the seat moved to `at`.
fn seat_at(mut p: RaidPercept, at: Vec2) -> RaidPercept {
    p.me.at = at;
    p.base.self_view.x = at.x;
    p.base.self_view.y = at.y;
    p
}

/// The rules' numbers (the packet, section "The rules"): Hook asks for a foe from `HOOK_NEAR` to `HOOK_FAR` u away (a
/// pull to 42 u is worth a cooldown only from a gap; the shot flies 700 u and the foe moves while it does), Bolas for a
/// foe up to `BOLAS_FAR` u away (the shot flies 550 u). The test points sit well inside or outside each edge.
const HOOK_NEAR: f64 = 220.;
const HOOK_FAR: f64 = 450.;
const BOLAS_FAR: f64 = 350.;
const FOE: &str = "p9";
/// `effects` bits (`packages/shared/src/status.ts`): stunned, silenced, casting.
const STUNNED: u16 = 1;
const SILENCED: u16 = 4;
const CASTING: u16 = 4096;

fn fight() -> RaidGoal {
    RaidGoal::new(GoalKind::Fight, Target::Actor(FOE.into()))
}

fn foe_at(d: f64) -> PlayerSeen {
    enemy(FOE, Vec2::new(ME.x + d, ME.y))
}

/// The one cast asked in `p` under `goal`, if any, as (reward, aim).
fn cast_of(goal: &RaidGoal, p: &RaidPercept) -> Option<(u8, CastAim)> {
    let acts = asked(goal, p);
    assert!(acts.len() <= 1, "at most one act a think: {acts:?}");
    acts.into_iter().find_map(|a| match a {
        Act::Cast { reward, aim, .. } => Some((reward, aim)),
        _ => None,
    })
}

fn player_scene(spell: u8, effects: u16, d: f64) -> RaidPercept {
    frame(spell, effects, 1., vec![], vec![foe_at(d)])
}

/// The shot is aimed at where the foe stands, not at the foe's key: a skillshot has no target actor.
fn assert_shot_at_foe(spell: u8, d: f64, got: Option<(u8, CastAim)>) {
    match got {
        Some((reward, CastAim::Point(at))) => {
            assert_eq!(reward, spell, "the spell asked at {d} u");
            assert!(
                (at.x - (ME.x + d)).abs() < 1e-6 && (at.y - ME.y).abs() < 1e-6,
                "aimed at the foe's position at {d} u: {at:?}"
            );
        }
        other => panic!("{spell} at {d} u: a cast at the foe's position is asked, got {other:?}"),
    }
}

fn band_holds(spell: u8, inside: &[f64], outside: &[f64]) {
    for &d in inside {
        let got = cast_of(&fight(), &player_scene(spell, 0, d));
        assert_shot_at_foe(spell, d, got);
    }
    for &d in outside {
        let got = cast_of(&fight(), &player_scene(spell, 0, d));
        assert!(
            got.is_none(),
            "{spell} at {d} u is out of its band: {got:?}"
        );
    }
}

// RED on the base: no rule, so no act is asked.
#[test]
fn hook_is_cast_at_a_fought_player_from_a_gap() {
    band_holds(HOOK, &[HOOK_NEAR + 10., 330., HOOK_FAR - 10.], &[]);
}

// Guard: a foe too close (the pull would gain nothing) or too far for the shot is let be.
#[test]
fn hook_is_not_cast_at_a_player_in_melee_or_beyond_the_shot() {
    band_holds(
        HOOK,
        &[],
        &[60., 150., HOOK_NEAR - 10., HOOK_FAR + 10., 650.],
    );
}

// RED on the base.
#[test]
fn bolas_is_cast_at_a_fought_player_within_reach() {
    band_holds(BOLAS, &[60., 200., BOLAS_FAR - 10.], &[]);
}

// Guard.
#[test]
fn bolas_is_not_cast_at_a_player_beyond_reach() {
    band_holds(BOLAS, &[], &[BOLAS_FAR + 10., 450., 600.]);
}

// Guards, both spells: only a player is a foe for a skillshot. An elite or a camp mob the seat is fighting inside its
// camp's band is not (the shot would be spent on a mob that needs no pulling or rooting, and wakes what it meets).
#[test]
fn neither_shot_is_cast_at_an_elite_or_a_camp_mob() {
    let here = in_camp_0();
    for spell in [HOOK, BOLAS] {
        for (class, goal) in [
            (
                MobClass::Elite,
                RaidGoal::new(GoalKind::Elite, Target::Camp(0)),
            ),
            (
                MobClass::Regional,
                RaidGoal::new(GoalKind::Farm, Target::Camp(0)),
            ),
        ] {
            for d in [150., 300.] {
                let m = mob("n3", class, Vec2::new(here.x + d, here.y));
                let p = seat_at(frame(spell, 0, 1., vec![m], vec![]), here);
                let got = cast_of(&goal, &p);
                assert!(got.is_none(), "{spell} at a {class:?} {d} u away: {got:?}");
            }
        }
    }
}

// Guards: no fight on a player, no shot. A player merely standing in view, a seat resting from a lost fight, a seat
// farming, a foe that is dead, one that cannot be targeted (a shot at it hits whatever else is in line).
#[test]
fn neither_shot_is_cast_without_a_fight_on_a_targetable_player() {
    for spell in [HOOK, BOLAS] {
        let p = player_scene(spell, 0, 300.);
        for goal in [
            RaidGoal::new(GoalKind::Sustain, Target::Camp(0)),
            RaidGoal::new(GoalKind::Farm, Target::Camp(0)),
            RaidGoal::new(GoalKind::Fight, Target::Actor("p77".into())),
        ] {
            let got = cast_of(&goal, &p);
            assert!(got.is_none(), "{spell}, goal {:?}: {got:?}", goal.kind);
        }
        let mut dead = foe_at(300.);
        dead.alive = false;
        let got = cast_of(&fight(), &frame(spell, 0, 1., vec![], vec![dead]));
        assert!(got.is_none(), "{spell} at a dead foe: {got:?}");
        let mut hidden = foe_at(300.);
        hidden.targetable = false;
        let got = cast_of(&fight(), &frame(spell, 0, 1., vec![], vec![hidden]));
        assert!(
            got.is_none(),
            "{spell} at a foe that cannot be targeted: {got:?}"
        );
    }
}

// Guards: a seat that cannot cast asks nothing (the send would drop it and the gate would then hold the slot).
#[test]
fn neither_shot_is_asked_while_the_seat_is_stunned_silenced_or_casting() {
    for spell in [HOOK, BOLAS] {
        for effects in [STUNNED, SILENCED, CASTING] {
            let got = cast_of(&fight(), &player_scene(spell, effects, 300.));
            assert!(got.is_none(), "{spell} with effects {effects}: {got:?}");
        }
    }
}

// The shot hits the FIRST enemy on its line (`rewards/effects.rs:637 first_hit`): a mob standing between would take the
// pull or the root instead of the player, and a sleeping one would wake. The clear-line cases are RED on the base.
#[test]
fn a_shot_is_held_while_a_mob_stands_on_its_line_and_asked_when_the_line_is_clear() {
    for spell in [HOOK, BOLAS] {
        let d = 300.;
        let on_line = |class, y_off, alive| {
            let mut m = mob("n5", class, Vec2::new(ME.x + d / 2., ME.y + y_off));
            m.alive = alive;
            m
        };
        let scene = |m: MobSeen| frame(spell, 0, 1., vec![m], vec![foe_at(d)]);
        for class in [MobClass::Regional, MobClass::Elite] {
            let got = cast_of(&fight(), &scene(on_line(class, 0., true)));
            assert!(got.is_none(), "{spell}: a {class:?} on the line: {got:?}");
            let got = cast_of(&fight(), &scene(on_line(class, 30., true)));
            assert!(
                got.is_none(),
                "{spell}: a {class:?} 30 u off the line: {got:?}"
            );
        }
        // Clear: far to the side, dead, a training dummy, or behind the seat.
        let clear = [
            on_line(MobClass::Regional, 150., true),
            on_line(MobClass::Regional, 0., false),
            on_line(MobClass::Dummy, 0., true),
            mob("n5", MobClass::Regional, Vec2::new(ME.x - 100., ME.y)),
        ];
        for m in clear {
            let got = cast_of(&fight(), &scene(m));
            assert_shot_at_foe(spell, d, got);
        }
    }
}

// Guard: Cleanse on a rooted seat still comes first, and one cast a think.
#[test]
fn cleanse_still_comes_before_a_shot() {
    const CLEANSE: u8 = 16;
    let mut p = player_scene(HOOK, 2, 300.);
    p.quests.as_mut().unwrap().spells.push(WireQuestSpell {
        slot: 0,
        reward: CLEANSE,
        charges: 1,
        ready_at: 0.,
    });
    let got = cast_of(&fight(), &p);
    assert!(
        matches!(got, Some((CLEANSE, CastAim::OwnPosition))),
        "rooted: Cleanse first, one cast a think: {got:?}"
    );
}
