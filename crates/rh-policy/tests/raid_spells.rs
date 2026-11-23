//! P-RAID-S oracle (docs/backlog/packets/P-RAID-S.md): a `RaidBot` seated as a human casts the quest spells it holds by the
//! rule table in `raid/spells.rs`.
//!
//! **Which tests are live and which are hand-built.**
//! - Hand-built (pure): `decide_cast` and `CastGate` are driven with a `SpellScene` and `HeldSpell`s written in the test.
//!   Used for every rule's boundary (range, kind, cooldown), the retry gate (the sim's refusal of a cast is silent and
//!   cannot be staged live: every refusal the frame does not show is a taunt or a pull, which no test can set up), the table
//!   coverage and the executor's shape.
//! - Live (a real sim, `Sim::submit`, the seat's own frames): Cleanse while rooted (the plan's oracle), the slot a spell goes
//!   out on, a spent spell sending nothing, an unruled spell never cast, Smite at an elite, and the golden hash of a seat
//!   holding no spell. A spell is granted through `rh_sim::trusted_build::apply` (`rewards`), a root through a checkpoint
//!   (a status instance in `world.players[0].core.statuses`, `Sim::restore` validates it).
use rh_policy::bots::raid::acts::{Act, CastAim};
use rh_policy::bots::raid::exec;
use rh_policy::bots::raid::goal::{Executor, GoalKind};
use rh_policy::bots::raid::spells::{
    decide_cast, CastGate, Foe, FoeKind, HeldSpell, SpellScene, When, CAST_RETRY_WAIT_MS,
    SPELL_RULES,
};
use rh_policy::bots::raid::values::Prices;
use rh_policy::bots::{PersonaId, RaidBot, TierId};
use rh_policy::{
    drive_seats_with, feed_seats, scripted, InputKind, InputPreview, InputRefusal,
    InputRequirements, MatchStart, Policy, PolicyFinish, PolicyInput, PolicySpec, PolicyStats,
    Seat,
};
use rh_sim::abilities::Slot;
use rh_sim::mob_catalog::MobTypeId;
use rh_sim::mob_runtime::MobKind;
use rh_sim::quests::board::catalog::{RewardId, RewardKind, DEFINITIONS};
use rh_sim::trusted_build::TrustedBuild;
use rh_sim::{
    Arena, ArenaMode, Event, FactionId, ItemObserver, Observation, Placement, PlayerId,
    PublicCommand, Secrets, SightCache, Sim, SimConfig, Vec2, WeaponId,
};
use std::collections::BTreeSet;
use std::sync::{Arc, Mutex};

// Wire reward ids (`Award as u8`; see the packet, section 3).
const SHOULDER_CHARGE: u8 = 3;
const TURRET: u8 = 5;
const SMITE: u8 = 8;
const CLEANSE: u8 = 16;
const STILL_MIND: u8 = 22;
const HOOK: u8 = 2;

// ---------------------------------------------------------------------------------------------------------------------
// Hand-built scenes (pure)
// ---------------------------------------------------------------------------------------------------------------------

const NOW: f64 = 10_000.;

fn scene() -> SpellScene {
    SpellScene {
        me: Vec2::new(100., 100.),
        impaired: false,
        foe: None,
        hostile_distances: Vec::new(),
        leave_away: None,
        hp_share: 1.,
    }
}

fn with_foe(kind: FoeKind, distance: f64) -> SpellScene {
    SpellScene {
        foe: Some(Foe {
            key: "n7".into(),
            kind,
            at: Vec2::new(100. + distance, 100.),
            distance,
        }),
        hostile_distances: vec![distance],
        ..scene()
    }
}

fn held(slot: u8, reward: u8) -> HeldSpell {
    HeldSpell {
        slot,
        reward,
        charges: 1,
        ready_at: 0.,
    }
}

fn cast(slot: u8, reward: u8, aim: CastAim) -> Option<Act> {
    Some(Act::Cast { slot, reward, aim })
}

/// The sim's own list of awards, split into non-permanent and permanent wire ids. `RewardId as u8` is the wire id up to
/// `BlindRageAndStillMind` (21, which carries two awards: 21 BlindRage and 22 StillMind); every reward after it is one
/// higher on the wire. Single-use ids are those of `RewardKind::SingleUse` (and StillMind).
struct Awards {
    spells: BTreeSet<u8>,
    permanent: BTreeSet<u8>,
    single_use: BTreeSet<u8>,
}

fn awards() -> Awards {
    let bundle = RewardId::BlindRageAndStillMind as u8;
    let rewards: BTreeSet<u8> = DEFINITIONS.iter().map(|d| d.reward as u8).collect();
    let mut a = Awards {
        spells: BTreeSet::new(),
        permanent: BTreeSet::new(),
        single_use: BTreeSet::new(),
    };
    for d in DEFINITIONS
        .iter()
        .filter(|d| rewards.contains(&(d.reward as u8)))
    {
        let id = d.reward as u8;
        let wire = if id > bundle { id + 1 } else { id };
        match d.reward.kind() {
            RewardKind::Permanent => {
                a.permanent.insert(wire);
            }
            kind => {
                a.spells.insert(wire);
                if kind == RewardKind::SingleUse {
                    a.single_use.insert(wire);
                }
            }
        }
        if d.reward == RewardId::BlindRageAndStillMind {
            a.spells.insert(STILL_MIND);
            a.single_use.insert(STILL_MIND);
        }
    }
    a
}

#[test]
fn the_award_list_taken_from_the_sim_is_the_one_the_packet_counts() {
    let a = awards();
    assert_eq!(
        a.spells.len(),
        19,
        "19 non-permanent awards: {:?}",
        a.spells
    );
    assert_eq!(
        a.permanent.len(),
        7,
        "7 permanent awards: {:?}",
        a.permanent
    );
    assert_eq!(
        a.single_use.len(),
        9,
        "9 single-use awards: {:?}",
        a.single_use
    );
    for four in [SHOULDER_CHARGE, TURRET, SMITE, CLEANSE] {
        assert!(a.spells.contains(&four) && !a.single_use.contains(&four));
    }
}

#[test]
fn every_non_permanent_award_has_exactly_one_rule_row_and_no_permanent_has_one() {
    let a = awards();
    for id in &a.spells {
        let rows = SPELL_RULES.iter().filter(|r| r.reward == *id).count();
        assert_eq!(rows, 1, "award {id} needs exactly one rule row, has {rows}");
    }
    for id in &a.permanent {
        assert!(
            SPELL_RULES.iter().all(|r| r.reward != *id),
            "permanent award {id} must have no rule row"
        );
    }
    assert_eq!(
        SPELL_RULES.len(),
        a.spells.len(),
        "no row for anything else"
    );
    for r in SPELL_RULES {
        assert!(!r.why.trim().is_empty(), "award {} has a `why`", r.reward);
    }
}

#[test]
fn this_packet_rules_four_spells_and_leaves_the_rest_not_ruled_yet() {
    let ruled: BTreeSet<u8> = SPELL_RULES
        .iter()
        .filter(|r| r.when != When::Never)
        .map(|r| r.reward)
        .collect();
    assert_eq!(
        ruled,
        BTreeSet::from([SHOULDER_CHARGE, TURRET, SMITE, CLEANSE]),
        "Cleanse, Smite, ShoulderCharge and Turret are ruled; the other fifteen are `Never`"
    );
}

#[test]
fn cleanse_is_cast_on_the_seat_when_it_is_rooted_or_stunned_and_not_otherwise() {
    let impaired = SpellScene {
        impaired: true,
        ..scene()
    };
    assert_eq!(
        decide_cast(&impaired, &[held(0, CLEANSE)], NOW),
        cast(0, CLEANSE, CastAim::OwnPosition)
    );
    assert_eq!(decide_cast(&scene(), &[held(0, CLEANSE)], NOW), None);
}

#[test]
fn the_cast_names_the_wire_slot_that_holds_the_spell() {
    let impaired = SpellScene {
        impaired: true,
        ..scene()
    };
    assert_eq!(
        decide_cast(&impaired, &[held(0, HOOK), held(1, CLEANSE)], NOW),
        cast(1, CLEANSE, CastAim::OwnPosition),
        "Cleanse in the second slot goes out as slot 1 (the E key)"
    );
    assert_eq!(
        decide_cast(&impaired, &[held(0, CLEANSE), held(1, HOOK)], NOW),
        cast(0, CLEANSE, CastAim::OwnPosition),
        "Cleanse in the first slot goes out as slot 0 (the Q key)"
    );
}

#[test]
fn a_spell_with_no_charge_is_not_cast() {
    let impaired = SpellScene {
        impaired: true,
        ..scene()
    };
    assert!(decide_cast(&impaired, &[held(0, CLEANSE)], NOW).is_some());
    let spent = HeldSpell {
        charges: 0,
        ready_at: NOW + 20_000.,
        ..held(0, CLEANSE)
    };
    assert_eq!(decide_cast(&impaired, &[spent], NOW), None);
}

#[test]
fn smite_is_cast_at_an_elite_in_reach_and_never_at_a_player_or_a_camp_mob() {
    let smite = [held(0, SMITE)];
    assert_eq!(
        decide_cast(&with_foe(FoeKind::Elite, 400.), &smite, NOW),
        cast(0, SMITE, CastAim::Actor("n7".into())),
        "an elite 400 u away is inside Smite's 450 u"
    );
    assert_eq!(
        decide_cast(&with_foe(FoeKind::Elite, 520.), &smite, NOW),
        None,
        "past 450 u (+32 slack) the sim refuses"
    );
    assert_eq!(
        decide_cast(&with_foe(FoeKind::Player, 100.), &smite, NOW),
        None,
        "the sim refuses a player target (effects.rs:631 focused(.., player=false))"
    );
    assert_eq!(
        decide_cast(&with_foe(FoeKind::CampMob, 100.), &smite, NOW),
        None,
        "this table spends Smite on elites"
    );
}

#[test]
fn turret_is_cast_on_the_seat_when_an_elite_is_in_its_reach() {
    let turret = [held(0, TURRET)];
    assert_eq!(
        decide_cast(&with_foe(FoeKind::Elite, 300.), &turret, NOW),
        cast(0, TURRET, CastAim::OwnPosition)
    );
    assert_eq!(
        decide_cast(&with_foe(FoeKind::Elite, 500.), &turret, NOW),
        None,
        "the turret shoots 340 u"
    );
    assert_eq!(
        decide_cast(&with_foe(FoeKind::CampMob, 100.), &turret, NOW),
        None
    );
}

#[test]
fn shoulder_charge_is_cast_away_from_a_lost_fight_only_while_leaving() {
    let leaving = SpellScene {
        leave_away: Some(Vec2::new(1., 0.)),
        ..scene()
    };
    let got = decide_cast(&leaving, &[held(1, SHOULDER_CHARGE)], NOW);
    let Some(Act::Cast {
        slot: 1,
        reward: SHOULDER_CHARGE,
        aim: CastAim::Point(p),
    }) = got
    else {
        panic!("expected a ShoulderCharge at a point, got {got:?}");
    };
    assert!(p.x > 100. + 1., "the point is away along +x: {p:?}");
    assert!((p.y - 100.).abs() < 1e-6, "and straight along it: {p:?}");
    assert_eq!(
        decide_cast(&scene(), &[held(1, SHOULDER_CHARGE)], NOW),
        None
    );
}

#[test]
fn at_most_one_cast_a_think() {
    let busy = SpellScene {
        impaired: true,
        leave_away: Some(Vec2::new(0., 1.)),
        ..with_foe(FoeKind::Elite, 200.)
    };
    let all = [held(0, CLEANSE), held(1, SMITE)];
    assert!(decide_cast(&busy, &all, NOW).is_some());
    // The result type is one `Option<Act>`: the check is that a second ready spell does not also fire when the first is
    // withdrawn, i.e. each call decides on what is held now.
    assert!(decide_cast(&busy, &all[1..], NOW).is_some());
}

#[test]
fn a_single_use_spell_is_never_spent_on_a_camp_mob() {
    let a = awards();
    // Positive control: the same scene shape with an elite does fire a reusable spell.
    assert!(decide_cast(&with_foe(FoeKind::Elite, 100.), &[held(0, SMITE)], NOW).is_some());
    for id in &a.single_use {
        let camp = with_foe(FoeKind::CampMob, 50.);
        assert_eq!(
            decide_cast(&camp, &[held(0, *id)], NOW),
            None,
            "single-use award {id} must not be cast at a camp mob"
        );
        if let Some(rule) = SPELL_RULES.iter().find(|r| r.reward == *id) {
            if let When::Foe { kinds, .. } = rule.when {
                assert!(
                    !kinds.contains(&FoeKind::CampMob),
                    "single-use award {id}: a Foe rule must not name camp mobs"
                );
            }
        }
    }
}

#[test]
fn a_seat_holding_no_spell_casts_nothing_and_the_spell_executor_proposes_no_goal() {
    let busy = SpellScene {
        impaired: true,
        leave_away: Some(Vec2::new(1., 0.)),
        ..with_foe(FoeKind::Elite, 200.)
    };
    assert!(decide_cast(&busy, &[held(0, CLEANSE)], NOW).is_some());
    assert_eq!(decide_cast(&busy, &[], NOW), None);
    assert!(exec::spell::Spell::default().kinds().is_empty());
    let registry = exec::registry();
    assert_eq!(registry.len(), 9, "the spell executor is the ninth, last");
    assert!(
        registry.last().expect("an executor").kinds().is_empty(),
        "the last executor runs no goal kind"
    );
    assert!(
        registry[..8].iter().all(|e| !e.kinds().is_empty()),
        "every earlier executor runs a goal kind, so the empty one is the new one"
    );
}

// ---------------------------------------------------------------------------------------------------------------------
// The retry gate (pure)
// ---------------------------------------------------------------------------------------------------------------------

fn ask(slot: u8, reward: u8) -> Option<Act> {
    cast(slot, reward, CastAim::OwnPosition)
}

#[test]
fn a_cast_the_sim_refused_is_not_asked_again_every_think() {
    let mut gate = CastGate::default();
    let held = [held(0, CLEANSE)];
    let mut asked = 0;
    // A think every 50 ms for one wait: the frame never shows the charge spent (the sim dropped the cast).
    let mut t = 1_000.;
    while t < 1_000. + CAST_RETRY_WAIT_MS {
        if gate.pass(ask(0, CLEANSE), &held, t).is_some() {
            asked += 1;
        }
        t += 50.;
    }
    assert_eq!(asked, 1, "one ask over the bounded wait, not one per think");
    assert_eq!(
        gate.pass(ask(0, CLEANSE), &held, 1_000. + CAST_RETRY_WAIT_MS + 50.),
        ask(0, CLEANSE),
        "after the wait it asks again"
    );
}

#[test]
fn the_gate_lets_a_slot_ask_again_once_the_frame_shows_the_cast_took() {
    let mut gate = CastGate::default();
    assert_eq!(
        gate.pass(ask(0, CLEANSE), &[held(0, CLEANSE)], 1_000.),
        ask(0, CLEANSE)
    );
    let spent = HeldSpell {
        charges: 0,
        ready_at: 31_000.,
        ..held(0, CLEANSE)
    };
    assert_eq!(gate.pass(None, &[spent], 1_050.), None);
    assert_eq!(
        gate.pass(ask(0, CLEANSE), &[held(0, CLEANSE)], 1_100.),
        ask(0, CLEANSE),
        "a recharged spell is asked for at once, not after the wait"
    );
}

#[test]
fn the_gate_waits_per_slot_and_per_spell() {
    let mut gate = CastGate::default();
    assert_eq!(
        gate.pass(ask(0, CLEANSE), &[held(0, CLEANSE)], 1_000.),
        ask(0, CLEANSE)
    );
    assert_eq!(
        gate.pass(ask(1, SMITE), &[held(0, CLEANSE), held(1, SMITE)], 1_050.),
        ask(1, SMITE),
        "the other slot is not held back"
    );
    assert_eq!(
        gate.pass(ask(0, SMITE), &[held(0, SMITE)], 1_100.),
        ask(0, SMITE),
        "a different spell in the slot is a new ask"
    );
}

// ---------------------------------------------------------------------------------------------------------------------
// Live scenes
// ---------------------------------------------------------------------------------------------------------------------

fn map() -> Arena {
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

fn config() -> SimConfig {
    SimConfig {
        lifecycle: ArenaMode::Off,
        min_players: 1,
        ..SimConfig::live_content()
    }
}

fn arena_of(seed: u64) -> Arc<Arena> {
    rh_sim::quests::board::map_admission::ensure(Arc::new(map()), Secrets::from_seed(seed))
        .expect("a board-admitted map")
}

fn placement(at: Vec2) -> [Placement; 1] {
    [Placement {
        profile: "raider".into(),
        faction: FactionId::Holy,
        bot: false,
        at,
        hp: None,
        weapon0: Some(WeaponId::Sword),
        weapon1: None,
    }]
}

fn dist(a: Vec2, b: Vec2) -> f64 {
    rh_sim::math::hypot(a.x - b.x, a.y - b.y)
}

fn floor_line(arena: &Arena, a: Vec2, b: Vec2) -> bool {
    (0..=40).all(|i| {
        let t = f64::from(i) / 40.;
        arena.map.is_floor(
            rh_sim::map::world_to_tile(a.x + (b.x - a.x) * t),
            rh_sim::map::world_to_tile(a.y + (b.y - a.y) * t),
        )
    })
}

/// A seat's `RaidBot` shared with the test (to read its goal) that logs every `Cast` it proposes.
#[derive(Clone)]
struct Spy {
    bot: Arc<Mutex<RaidBot>>,
    casts: Arc<Mutex<Vec<PublicCommand>>>,
}

impl Spy {
    fn new(prices: Prices) -> Self {
        Self {
            bot: Arc::new(Mutex::new(
                RaidBot::new(TierId::Normal, PersonaId::Farmer).with_prices(prices),
            )),
            casts: Arc::new(Mutex::new(Vec::new())),
        }
    }
    fn take(&self) -> Vec<PublicCommand> {
        std::mem::take(&mut *self.casts.lock().unwrap())
    }
}

impl Policy for Spy {
    fn input_requirements(&self) -> InputRequirements {
        self.bot.lock().unwrap().input_requirements()
    }
    fn input_kind(&self) -> InputKind {
        self.bot.lock().unwrap().input_kind()
    }
    fn check_input(&self, input: InputPreview<'_>) -> Result<(), InputRefusal> {
        self.bot.lock().unwrap().check_input(input)
    }
    fn decide_input_into(
        &mut self,
        input: PolicyInput<'_>,
        out: &mut Vec<PublicCommand>,
    ) -> Result<(), InputRefusal> {
        let before = out.len();
        let r = self.bot.lock().unwrap().decide_input_into(input, out);
        let mut log = self.casts.lock().unwrap();
        log.extend(
            out[before..]
                .iter()
                .filter(|c| matches!(c, PublicCommand::Cast { .. }))
                .copied(),
        );
        r
    }
    fn spec(&self) -> PolicySpec {
        self.bot.lock().unwrap().spec()
    }
    fn stats(&self) -> Option<PolicyStats> {
        self.bot.lock().unwrap().stats()
    }
    fn reset(&mut self, m: &MatchStart) {
        self.bot.lock().unwrap().reset(m)
    }
    fn finish(&mut self, result: Option<&rh_sim::RunResult>) -> Option<PolicyFinish> {
        self.bot.lock().unwrap().finish(result)
    }
    fn decide(&mut self, obs: &Observation) -> Vec<PublicCommand> {
        self.bot.lock().unwrap().decide(obs)
    }
}

/// A fresh sim with the seat at `at`, the spells granted (in slot order) and, when `root_for_ms` is set, a Root status
/// on it for that long from the first step.
fn world(
    seed: u64,
    at: Vec2,
    rewards: &[RewardId],
    root_for_ms: Option<f64>,
    banked: TrustedBuild,
) -> (Sim, PlayerId) {
    let mut sim = Sim::reset_active(
        config(),
        arena_of(seed),
        Secrets::from_seed(seed),
        &placement(at),
    );
    let me = sim.players()[0].id;
    let build = TrustedBuild {
        rewards: rewards.to_vec(),
        ..banked
    };
    rh_sim::trusted_build::apply(&mut sim, me, &build).expect("the declared build");
    if let Some(ms) = root_for_ms {
        sim = rooted(sim, ms);
    }
    (sim, me)
}

/// The same sim with a Root on the first player until `ms` from now (a checkpoint `Sim::restore` must accept).
fn rooted(sim: Sim, ms: f64) -> Sim {
    let mut cp = serde_json::to_value(&sim).expect("a checkpoint");
    let now = cp["world"]["server_time"].as_f64().expect("a clock");
    let statuses = &mut cp["world"]["players"][0]["core"]["statuses"];
    statuses["initialized"] = serde_json::json!(true);
    statuses["effects"] = serde_json::json!(2);
    statuses["instances"] = serde_json::json!([{
        "kind": "root",
        "source": {"id": null, "name": "test", "owner_id": null},
        "ability": null,
        "tag": null,
        "started_at": now,
        "until": now + ms,
        "value": 0.0,
        "reveal_from": null,
        "taken": null,
        "data": {"undefined_fields": 0}
    }]);
    Sim::restore(cp).expect("the declared root is accepted")
}

/// What one live run saw.
#[derive(Debug, Default)]
struct Live {
    /// Every `Cast` the seat proposed: (tick, command).
    casts: Vec<(u32, PublicCommand)>,
    /// The first tick the seat's spell slots showed a charge spent, by wire slot.
    spent: [Option<u32>; 2],
    /// The first tick the seat was no longer rooted, once it was.
    unrooted: Option<u32>,
    hash: u64,
    fault: Option<rh_policy::Fault>,
    /// Ticks where the goal was an Elite fight with an elite alive within 450 u.
    smite_window: u32,
    /// The tick `smite_window` reached `WINDOW_TICKS`.
    window_full: Option<u32>,
    /// The first tick the frame showed the seat rooted (the setup took).
    rooted_seen: Option<u32>,
    /// Ticks with an Elite goal, and the nearest the seat got to the camp's elite (diagnostics).
    elite_goal_ticks: u32,
    nearest_elite: f64,
}

/// `camps` normal camps banked as stat stacks (12 each, a fixed walk over the camps) and `potions` large potions: the
/// build of `raid_elite.rs`'s executioner scene, so the seat takes the elite fight it is offered.
fn strong(seed: u64, camps: u32, potions: u8, at: Vec2, rewards: &[RewardId]) -> (Sim, PlayerId) {
    let arena = arena_of(seed);
    let probe = Sim::reset_active(
        config(),
        arena.clone(),
        Secrets::from_seed(seed),
        &placement(arena.centre()),
    );
    let buffs = probe.camp_buffs();
    let pays: Vec<MobTypeId> = probe
        .camp_rosters()
        .iter()
        .enumerate()
        .filter(|(_, r)| r.elite.is_none())
        .map(|(i, r)| buffs.get(i).copied().flatten().unwrap_or(r.mob_type))
        .collect();
    let mut stacks: Vec<(MobTypeId, u64)> = Vec::new();
    for k in 0..camps as usize {
        let stat = pays[(k * 7 + 3) % pays.len()];
        match stacks.iter_mut().find(|(m, _)| *m == stat) {
            Some(e) => e.1 += 12,
            None => stacks.push((stat, 12)),
        }
    }
    let (mut sim, me) = world(
        seed,
        at,
        rewards,
        None,
        TrustedBuild {
            stacks,
            ..Default::default()
        },
    );
    if potions > 0 {
        let mut cp = serde_json::to_value(&sim).expect("a checkpoint");
        cp["world"]["players"][0]["potion_large"] = serde_json::json!(potions);
        sim = Sim::restore(cp).expect("the declared stock is accepted");
    }
    (sim, me)
}

/// Run `spy` on `sim` for `ticks`; `elite_camp` names the camp whose elite the Smite window is measured against.
fn play(
    mut sim: Sim,
    me: PlayerId,
    seed: u64,
    spy: &Spy,
    ticks: u32,
    elite_camp: Option<u32>,
) -> (Live, Sim) {
    let start = MatchStart::of(&sim, me, seed);
    let mut seats = vec![Seat::new(
        &start,
        Box::new(spy.clone()),
        Box::new(scripted::Idle),
    )];
    seats[0].bind_fx(&sim).expect("a new seat binds");
    let mut sight = SightCache::new();
    let mut events: Vec<Event> = Vec::new();
    let mut observer = ItemObserver::new(me);
    let mut live = Live::default();
    let mut was_rooted = false;
    for tick in 0..ticks {
        drive_seats_with(&mut sim, &mut seats, &events, &mut sight, |_, _| {});
        for c in spy.take() {
            live.casts.push((tick, c));
        }
        events.clear();
        sim.step_into(&mut events);
        feed_seats(&sim, &mut seats);
        let mut obs = Observation::default();
        observer
            .prepare_into(&sim, &[], &mut obs, None)
            .expect("an observation")
            .commit();
        if let Some(view) = obs.quests.as_ref() {
            for s in &view.spells {
                if s.charges == 0 && live.spent[usize::from(s.slot)].is_none() {
                    live.spent[usize::from(s.slot)] = Some(tick);
                }
            }
        }
        let Some(p) = sim.player(me).filter(|p| p.standing()) else {
            break;
        };
        let rooted_now = p.statuses.effects & 2 != 0;
        if was_rooted && !rooted_now && live.unrooted.is_none() {
            live.unrooted = Some(tick);
        }
        if rooted_now && live.rooted_seen.is_none() {
            live.rooted_seen = Some(tick);
        }
        was_rooted |= rooted_now;
        if let Some(camp) = elite_camp {
            let (goal, _, _) = {
                let bot = spy.bot.lock().unwrap();
                let (g, s, w) = bot.goal();
                (g.clone(), s, w)
            };
            let near = sim.mobs().iter().any(|m| {
                m.camp == camp
                    && m.elite
                    && m.status == rh_sim::Status::Alive
                    && dist(m.core.pos(), p.pos()) <= 450.
            });
            if goal.kind == GoalKind::Elite {
                live.elite_goal_ticks += 1;
            }
            let d = sim
                .mobs()
                .iter()
                .filter(|m| m.camp == camp && m.elite && m.status == rh_sim::Status::Alive)
                .map(|m| dist(m.core.pos(), p.pos()))
                .fold(f64::MAX, f64::min);
            live.nearest_elite = if live.nearest_elite == 0. {
                d
            } else {
                live.nearest_elite.min(d)
            };
            if goal.kind == GoalKind::Elite && near {
                live.smite_window += 1;
                if live.smite_window == WINDOW_TICKS {
                    live.window_full = Some(tick);
                }
            }
        }
    }
    live.hash = sim.state_hash();
    live.fault = seats[0].fault.clone();
    (live, sim)
}

/// A seat that has fought an elite this many ticks (3 s) in Smite's reach must have cast it.
const WINDOW_TICKS: u32 = 60;
/// Ticks allowed between the window filling and the spent charge showing (1 s).
const WINDOW_SLACK: u32 = 20;

fn any_prices() -> Prices {
    Prices::LOCAL
}

fn quiet_start(seed: u64) -> Vec2 {
    arena_of(seed).centre()
}

const ROOT_MS: f64 = 3_000.;

#[test]
fn holding_cleanse_while_rooted_the_seat_casts_within_500_ms_and_the_sim_takes_it() {
    for seed in 1..=4u64 {
        let (sim, me) = world(
            seed,
            quiet_start(seed),
            &[RewardId::Cleanse],
            Some(ROOT_MS),
            TrustedBuild::default(),
        );
        let spy = Spy::new(any_prices());
        let (live, _) = play(sim, me, seed, &spy, 80, None);
        assert!(live.fault.is_none(), "seed {seed}: {:?}", live.fault);
        assert!(
            live.rooted_seen.is_some(),
            "seed {seed}: setup: the seat is rooted"
        );
        let (tick, first) = live
            .casts
            .first()
            .unwrap_or_else(|| panic!("seed {seed}: no cast"));
        assert!(
            *tick <= 10,
            "seed {seed}: first cast at tick {tick}, 500 ms is 10 ticks"
        );
        let PublicCommand::Cast { slot, .. } = first else {
            unreachable!()
        };
        assert_eq!(
            *slot,
            Slot::Q,
            "seed {seed}: the first spell is on the Q key"
        );
        assert!(
            live.spent[0].is_some(),
            "seed {seed}: the sim took the cast (charge spent)"
        );
        assert!(live.unrooted.is_some(), "seed {seed}: and the root is gone");
        assert!(
            live.unrooted.unwrap() < 60,
            "seed {seed}: well before the root's own 3 s"
        );
    }
}

#[test]
fn a_spell_in_the_second_slot_goes_out_on_the_second_ability_key() {
    for seed in 1..=3u64 {
        let (sim, me) = world(
            seed,
            quiet_start(seed),
            &[RewardId::Hook, RewardId::Cleanse],
            Some(ROOT_MS),
            TrustedBuild::default(),
        );
        let spy = Spy::new(any_prices());
        let (live, _) = play(sim, me, seed, &spy, 80, None);
        assert!(
            !live.casts.is_empty(),
            "seed {seed}: Cleanse in slot 2 was cast"
        );
        for (tick, c) in &live.casts {
            let PublicCommand::Cast { slot, .. } = c else {
                unreachable!()
            };
            assert_eq!(
                *slot,
                Slot::E,
                "seed {seed} tick {tick}: only the E key is used"
            );
        }
        assert!(
            live.spent[1].is_some(),
            "seed {seed}: the second slot was spent"
        );
        assert!(live.spent[0].is_none(), "seed {seed}: Hook was not");
    }
}

#[test]
fn a_spell_that_is_spent_sends_nothing_when_the_seat_is_rooted_again() {
    let seed = 1;
    let (sim, me) = world(
        seed,
        quiet_start(seed),
        &[RewardId::Cleanse],
        Some(ROOT_MS),
        TrustedBuild::default(),
    );
    let (first, sim) = play(sim, me, seed, &Spy::new(any_prices()), 40, None);
    assert!(
        !first.casts.is_empty(),
        "control: the first root was cast on: {first:?}"
    );
    assert!(first.spent[0].is_some());
    // Rooted again with Cleanse on its 30 s cooldown; a new seat (no memory of the first cast), so only the frame can
    // stop it.
    let sim = rooted(sim, ROOT_MS);
    let (second, _) = play(sim, me, seed, &Spy::new(any_prices()), 60, None);
    assert!(second.rooted_seen.is_some(), "setup: rooted again");
    assert!(
        second.casts.is_empty(),
        "no charge, no cast: {:?}",
        second.casts
    );
}

#[test]
fn an_unruled_spell_is_never_cast() {
    for seed in 1..=2u64 {
        let rewards = [RewardId::Hook, RewardId::Tornado];
        let (sim, me) = world(
            seed,
            quiet_start(seed),
            &rewards,
            Some(ROOT_MS),
            TrustedBuild::default(),
        );
        let (live, _) = play(sim, me, seed, &Spy::new(any_prices()), 200, None);
        assert!(
            live.casts.is_empty(),
            "seed {seed}: Hook and Tornado are not ruled yet: {:?}",
            live.casts
        );
    }
}

/// A seat that holds no spell sends no cast over 600 ticks, on seed 1 from the arena centre. The state hash is printed,
/// not pinned (coordinator, 2026-11-23): a golden of the whole seat's behaviour moves with every other RaidBot packet, so
/// "the new executor moves no draw" rests on `review_tests.rs` (every executor's `urgent` is false and draws nothing in
/// a no-spell seat) and on the implementer's own comparison of this printed hash on the stub and on its head, on one
/// base (run with `--nocapture`).
#[test]
fn no_spell_seat_plays_as_before() {
    let seed = 1;
    let (sim, me) = world(seed, quiet_start(seed), &[], None, TrustedBuild::default());
    let spy = Spy::new(any_prices());
    let (live, _) = play(sim, me, seed, &spy, 600, None);
    assert!(live.casts.is_empty());
    println!("NO_SPELL_HASH {}", live.hash);
}

/// An elite camp's start: 256 u (or nearer) from its members, with a floor line.
fn elite_start(seed: u64, kind: MobTypeId) -> (Vec2, u32) {
    let arena = arena_of(seed);
    let probe = Sim::reset_active(
        config(),
        arena.clone(),
        Secrets::from_seed(seed),
        &placement(arena.centre()),
    );
    let camp = probe
        .camp_rosters()
        .iter()
        .position(|r| r.elite == Some(kind))
        .unwrap_or_else(|| panic!("seed {seed}: no {kind:?} camp")) as u32;
    let members: Vec<Vec2> = probe
        .mobs()
        .iter()
        .filter(|m| m.camp == camp && m.zombie.is_none() && m.kind == MobKind::Regional)
        .map(|m| m.core.pos())
        .collect();
    let n = members.len() as f64;
    let centre = Vec2::new(
        members.iter().map(|p| p.x).sum::<f64>() / n,
        members.iter().map(|p| p.y).sum::<f64>() / n,
    );
    for d in [256., 192., 128.] {
        for k in 0..16 {
            let a = f64::from(k) * std::f64::consts::TAU / 16.;
            let start = Vec2::new(
                centre.x + rh_sim::math::cos(a) * d,
                centre.y + rh_sim::math::sin(a) * d,
            );
            if floor_line(&arena, start, centre) {
                return (start, camp);
            }
        }
    }
    panic!("seed {seed}: no floor start near the camp");
}

const SMITE_SEEDS: std::ops::RangeInclusive<u64> = 1..=6;
/// Seeds (of six) on which the seat must fight an elite within 450 u for 3 s. Measured on the stub: all six, with 280 to
/// 1244 ticks of it each (an Executioner camp, 36 stacks, two large potions); one seed of slack for the fight a Smite
/// changes.
const SMITE_FLOOR: usize = 5;

#[test]
fn smite_is_cast_at_an_elite_the_seat_is_fighting_and_the_sim_takes_it() {
    let mut windows = 0;
    let mut report = Vec::new();
    let mut runs = Vec::new();
    for seed in SMITE_SEEDS {
        let (start, camp) = elite_start(seed, MobTypeId::Executioner);
        let (sim, me) = strong(seed, 3, 2, start, &[RewardId::Smite]);
        let spy = Spy::new(Prices {
            reward_default: 0.,
            reward_rows: &[],
            ..Prices::LOCAL
        });
        let (live, _) = play(sim, me, seed, &spy, 1_800, Some(camp));
        report.push((
            seed,
            live.smite_window,
            live.elite_goal_ticks,
            live.nearest_elite.round(),
            live.spent[0],
            live.casts.len(),
        ));
        assert!(live.fault.is_none(), "seed {seed}: {:?}", live.fault);
        windows += usize::from(live.window_full.is_some());
        runs.push((seed, live));
    }
    println!("SMITE REPORT (seed, window ticks, elite goal ticks, nearest elite, spent, casts) {report:?}");
    assert!(
        windows >= SMITE_FLOOR,
        "the seat fought an elite in Smite's reach on only {windows} seeds: {report:?}"
    );
    for (seed, live) in &runs {
        let Some(full) = live.window_full else {
            continue;
        };
        let spent = live.spent[0].unwrap_or(u32::MAX);
        assert!(
            spent <= full + WINDOW_SLACK,
            "seed {seed}: fought an elite in reach from tick {full}, Smite spent at {spent}: {report:?}"
        );
        assert!(
            live.casts.iter().all(|(_, c)| matches!(
                c,
                PublicCommand::Cast {
                    slot: Slot::Q,
                    id: Some(_),
                    ..
                }
            )),
            "seed {seed}: Smite goes on Q with the elite as its target"
        );
    }
}
