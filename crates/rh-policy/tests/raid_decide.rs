//! P-RAID-Q3 oracle (loose proof, docs/backlog/packets/P-RAID-Q3.md): a `RaidBot` seated as a HUMAN (`bot: false`) on the
//! live content world answers the quest decisions the walker leaves alone: it resolves a pending reward slot choice
//! keeping the two spells of higher table value, picks a dropped quest spell up when a slot is free, and takes The
//! Bargain's improving trade (or none). Two Banners and Trade Up are P-RAID-Q3b's.
//!
//! **How a scene reaches its decision.** The walker never accepts a third spell reward (it counts held spells plus accepted
//! spell quests against two slots), so RaidBot alone never reaches a pending slot choice. A scripted prelude (no policy, the
//! control of `raid_quests.rs` extended to a list of quests) plays the first quests straight from the sim's own
//! observation and stops at the decision; then a `RaidBot` seat takes over the same sim. `Bargain` scenes start the seat a
//! short walk from the giver with stacks granted by `rh_sim::trusted_build` (a dev-only feature of this crate's tests).
//! Every scene has a scripted control that does the same decision itself, so a red RaidBot is the bot's.
use rh_policy::bots::raid::values::Prices;
use rh_policy::bots::{PersonaId, RaidBot, TierId};
use rh_policy::{drive_seats_with, feed_seats, scripted, MatchStart, Policy, Seat, SeatStats};
use rh_sim::abilities::Slot;
use rh_sim::mob_catalog::MobTypeId;
use rh_sim::trusted_build::{apply, TrustedBuild};
use rh_sim::{
    Arena, ArenaMode, Event, FactionId, ItemObserver, Observation, Placement, PlayerId,
    PublicCommand, Secrets, SightCache, Sim, SimConfig, Vec2, WeaponId,
};
use std::collections::BTreeSet;
use std::sync::Arc;

/// Wire reward ids of the spells (`rh_sim` `Award as u8`, equal to `RewardId as u8` up to 21; 22 is Still Mind, the
/// second award of `lost_brother`).
const WALLBUSTER: u8 = 20;
const SWAP_STICK: u8 = 13;
const SPELL_SHIELD: u8 = 9;
const CLEANSE: u8 = 16;
const FORGET_ME_NOT: u8 = 12;
const BLIND_RAGE: u8 = 21;
const STILL_MIND: u8 = 22;

/// One slot-choice scene: the quests the prelude plays (the last stops pending), what the table says to keep, and what lies
/// on the ground after. Values (docs/backlog/packets/P-RAID-Q3.md section 3, `Prices::LOCAL` rows): seed 5 holds
/// barrow_run's WallBuster 1.5 and a_cruel_joke's Swap-Stick 2.0, then earns ember_walk's Spell Shield 2.0: the lowest is
/// the WallBuster (answer: drop slot 0). Seed 4 holds pilgrims_path's Cleanse 2.0 and letters' Forget-Me-Not 2.0, then
/// earns lost_brother's two spells at half its row (2.5) each, 1.25: the new one is the lowest (answer 2, leave it).
struct SlotCase {
    seed: u64,
    prelude: [&'static str; 3],
    held_before: [u8; 2],
    held_after: [u8; 2],
    ground_after: &'static [u8],
}
const SLOT_CASES: [SlotCase; 2] = [
    SlotCase {
        seed: 5,
        prelude: ["barrow_run", "a_cruel_joke", "ember_walk"],
        held_before: [WALLBUSTER, SWAP_STICK],
        held_after: [SWAP_STICK, SPELL_SHIELD],
        ground_after: &[WALLBUSTER],
    },
    SlotCase {
        seed: 4,
        prelude: ["pilgrims_path", "letters", "lost_brother"],
        held_before: [CLEANSE, FORGET_ME_NOT],
        held_after: [CLEANSE, FORGET_ME_NOT],
        ground_after: &[BLIND_RAGE, STILL_MIND],
    },
];
/// The pick-up scene (seed 4): lost_brother's two spells held, pilgrims_path's Cleanse left on the ground, the Blind Rage
/// cast away (single use) so a slot is free, then handed to RaidBot.
const PICKUP_SEED: u64 = 4;
/// Seeds that deal `the_bargain` (measured on seeds 1-24: 2, 4, 6, 9, 10, 11, 12, 13, 17, 20, 22), four of them used.
const BARGAIN_SEEDS: [u64; 4] = [2, 4, 6, 9];
/// How many of the four seeds the bot must complete a scene on (the scripted control completes all four).
const BARGAIN_FLOOR: usize = 3;
/// From the handover the pending choice must be gone within this many ticks (10 s: "a few seconds").
const RESOLVE_WITHIN: u32 = 200;
/// The bot runs this long after a handover in the slot and pick-up scenes (60 s), to see what it does next.
const AFTER_TICKS: u32 = 1_200;
/// The Bargain scenes run this long (90 s): a 160 u walk to the giver, the accept and a 140 u walk to the hag.
const BARGAIN_TICKS: u32 = 1_800;
/// The seat starts this far from the Bargain's giver (as `raid_quests.rs`).
const START_DIST: f64 = 160.;
/// The journal is read from the sim every this many ticks.
const READ_EVERY: u32 = 4;

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

fn placement(at: Vec2, weapons: [Option<WeaponId>; 2]) -> [Placement; 1] {
    [Placement {
        profile: "raider".into(),
        faction: FactionId::Holy,
        bot: false,
        at,
        hp: None,
        weapon0: weapons[0],
        weapon1: weapons[1],
    }]
}

fn observe(sim: &Sim, observer: &mut ItemObserver) -> Observation {
    let mut obs = Observation::default();
    observer
        .prepare_into(sim, &[], &mut obs, None)
        .expect("an observation")
        .commit();
    obs
}

fn dist(a: Vec2, b: Vec2) -> f64 {
    rh_sim::math::hypot(a.x - b.x, a.y - b.y)
}

/// Where the board puts `def`'s giver on `seed`.
fn giver_of(seed: u64, def: &str) -> Vec2 {
    let arena = arena_of(seed);
    let probe = Sim::reset_active(
        config(),
        arena.clone(),
        Secrets::from_seed(seed),
        &placement(arena.centre(), [Some(WeaponId::Sword), None]),
    );
    let me = probe.players()[0].id;
    let obs = observe(&probe, &mut ItemObserver::new(me));
    let row = obs
        .quests
        .expect("a human seat sees the board")
        .rows
        .into_iter()
        .find(|r| r.def == def)
        .unwrap_or_else(|| panic!("seed {seed} deals no {def}"));
    Vec2::new(row.giver.x, row.giver.y)
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

/// A point `START_DIST` from `giver` with a straight floor line to it.
fn start_near(arena: &Arena, giver: Vec2) -> Vec2 {
    for k in 0..16 {
        let a = f64::from(k) * std::f64::consts::TAU / 16.;
        let start = Vec2::new(
            giver.x + rh_sim::math::cos(a) * START_DIST,
            giver.y + rh_sim::math::sin(a) * START_DIST,
        );
        if floor_line(arena, start, giver) {
            return start;
        }
    }
    panic!("no floor start beside {giver:?}");
}

/// What the seat's own view shows at one read.
#[derive(Clone, Debug, Default, PartialEq)]
struct Snap {
    tick: u32,
    held: Vec<u8>,
    ground: Vec<u8>,
    pending: usize,
    accepted: BTreeSet<String>,
}

impl Snap {
    fn of(tick: u32, obs: &Observation) -> Option<Self> {
        let view = obs.quests.as_ref()?;
        let mut held: Vec<u8> = view.spells.iter().map(|s| s.reward).collect();
        let mut ground: Vec<u8> = view.drops.iter().map(|d| d.reward).collect();
        held.sort_unstable();
        ground.sort_unstable();
        Some(Self {
            tick,
            held,
            ground,
            pending: view.pending.len(),
            accepted: view
                .rows
                .iter()
                .filter(|r| r.owner_accepted)
                .map(|r| r.def.clone())
                .collect(),
        })
    }
}

/// A sim, its seat and the observer a scripted control reads and sends through.
struct Script {
    sim: Sim,
    me: PlayerId,
    observer: ItemObserver,
    events: Vec<Event>,
    seed: u64,
}

/// How a scripted quest ended.
#[derive(Debug, PartialEq)]
enum Stage {
    /// The row left the view: turned in and paid.
    Done,
    /// Pending with the reward slot choice offered (action 8).
    Pending,
    Timeout,
}

impl Script {
    fn new(seed: u64, start: Vec2, weapons: [Option<WeaponId>; 2]) -> Self {
        let sim = Sim::reset_active(
            config(),
            arena_of(seed),
            Secrets::from_seed(seed),
            &placement(start, weapons),
        );
        let me = sim.players()[0].id;
        Self {
            sim,
            me,
            observer: ItemObserver::new(me),
            events: Vec::new(),
            seed,
        }
    }

    fn step(&mut self) -> Observation {
        self.sim.step_into(&mut self.events);
        self.events.clear();
        observe(&self.sim, &mut self.observer)
    }

    fn send(&mut self, proposal: PublicCommand) {
        if let Ok(cmd) = self.observer.resolve(&self.sim, proposal) {
            let _ = self.sim.submit(self.me, cmd);
        }
    }

    /// Play `def` straight from the sim's observation (accept at the giver, the offered contact, a straight walk to the
    /// nearest objective), every fifth tick, for at most `budget` ticks. With `stop_pending` it returns as soon as the
    /// slot choice is offered.
    fn quest(&mut self, def: &str, stop_pending: bool, budget: u32) -> Stage {
        for tick in 0..budget {
            let obs = self.step();
            if tick % 5 != 0 {
                continue;
            }
            let (Some(view), Some(owner)) = (obs.quests.as_ref(), obs.me.as_ref()) else {
                continue;
            };
            let Some(row) = view.rows.iter().find(|r| r.def == def) else {
                if tick > 0 {
                    return Stage::Done;
                }
                continue;
            };
            if view.pending.contains(&row.handle) {
                if stop_pending && view.actions.iter().any(|a| a.action == 8) {
                    return Stage::Pending;
                }
                continue;
            }
            let at = Vec2::new(owner.x, owner.y);
            let proposal = if !row.owner_accepted {
                if row.can_accept {
                    PublicCommand::QuestAccept { handle: row.handle }
                } else {
                    PublicCommand::Move {
                        x: row.giver.x,
                        y: row.giver.y,
                        seq: None,
                    }
                }
            } else if let Some(a) = view
                .actions
                .iter()
                .find(|a| a.quest == row.handle && a.action == 3)
            {
                PublicCommand::QuestInteract {
                    handle: row.handle,
                    action: 3,
                    target: a.target,
                }
            } else if let Some(o) = view
                .objectives
                .iter()
                .filter(|o| o.quest == row.handle)
                .min_by(|a, b| dist(a.at, at).total_cmp(&dist(b.at, at)))
            {
                PublicCommand::Move {
                    x: o.at.x,
                    y: o.at.y,
                    seq: None,
                }
            } else {
                continue;
            };
            self.send(proposal);
        }
        Stage::Timeout
    }

    /// Answer the pending slot choice with `target` and step until the view shows no pending quest.
    fn answer(&mut self, target: u8) -> Snap {
        for tick in 0..200u32 {
            let obs = self.step();
            let Some(view) = obs.quests.as_ref() else {
                continue;
            };
            if view.pending.is_empty() {
                return Snap::of(tick, &obs).expect("a view");
            }
            if tick % 5 == 0 {
                if let Some(a) = view
                    .actions
                    .iter()
                    .find(|a| a.action == 8 && a.target == target)
                {
                    self.send(PublicCommand::QuestInteract {
                        handle: a.quest,
                        action: 8,
                        target,
                    });
                }
            }
        }
        panic!(
            "seed {}: the scripted answer {target} never cleared the choice",
            self.seed
        );
    }

    fn snap(&mut self) -> Snap {
        let obs = self.step();
        Snap::of(0, &obs).expect("a view")
    }
}

/// The seat after the prelude of `case`: the first two quests done, the third pending with the choice offered.
fn at_pending(case: &SlotCase) -> Script {
    let giver = giver_of(case.seed, case.prelude[0]);
    let mut s = Script::new(case.seed, giver, [Some(WeaponId::Sword), None]);
    assert_eq!(
        s.quest(case.prelude[0], false, 3_000),
        Stage::Done,
        "seed {}: {}",
        case.seed,
        case.prelude[0]
    );
    assert_eq!(
        s.quest(case.prelude[1], false, 3_000),
        Stage::Done,
        "seed {}: {}",
        case.seed,
        case.prelude[1]
    );
    assert_eq!(
        s.quest(case.prelude[2], true, 3_000),
        Stage::Pending,
        "seed {}: {}",
        case.seed,
        case.prelude[2]
    );
    s
}

/// What a seated policy leaves behind.
#[derive(Debug)]
struct Run {
    reads: Vec<Snap>,
    stats: SeatStats,
    fault: Option<rh_policy::Fault>,
    hash: u64,
}

fn raid() -> Box<dyn Policy> {
    Box::new(RaidBot::new(TierId::Normal, PersonaId::Farmer))
}

/// Seat `policy` for the fighter of `s` (whatever state the script left it in) and step `ticks`.
fn seat(s: Script, policy: Box<dyn Policy>, ticks: u32) -> (Run, Script) {
    let mut s = s;
    let start = MatchStart::of(&s.sim, s.me, s.seed);
    let mut seats = vec![Seat::new(&start, policy, Box::new(scripted::Idle))];
    seats[0].bind_fx(&s.sim).expect("a new seat binds");
    let mut sight = SightCache::new();
    let mut reader = ItemObserver::new(s.me);
    let mut reads = Vec::new();
    for tick in 0..ticks {
        drive_seats_with(&mut s.sim, &mut seats, &s.events, &mut sight, |_, _| {});
        s.events.clear();
        s.sim.step_into(&mut s.events);
        feed_seats(&s.sim, &mut seats);
        if tick % READ_EVERY == 0 {
            if let Some(snap) = Snap::of(tick, &observe(&s.sim, &mut reader)) {
                reads.push(snap);
            }
        }
    }
    let run = Run {
        reads,
        stats: seats[0].stats.clone(),
        fault: seats[0].fault.clone(),
        hash: s.sim.state_hash(),
    };
    (run, s)
}

fn sorted(mut v: Vec<u8>) -> Vec<u8> {
    v.sort_unstable();
    v
}

// ---------------------------------------------------------------------------------------------------------------
// The slot choice
// ---------------------------------------------------------------------------------------------------------------

#[test]
fn a_scripted_prelude_reaches_the_slot_choice_and_a_scripted_answer_resolves_it() {
    for case in &SLOT_CASES {
        let mut s = at_pending(case);
        let snap = s.snap();
        assert_eq!(
            snap.held,
            sorted(case.held_before.to_vec()),
            "seed {}: two spells held before the third reward",
            case.seed
        );
        assert_eq!(snap.pending, 1, "seed {}: one pending choice", case.seed);
        let obs = s.step();
        let view = obs.quests.expect("a view");
        let targets: BTreeSet<u8> = view
            .actions
            .iter()
            .filter(|a| a.action == 8)
            .map(|a| a.target)
            .collect();
        assert_eq!(
            targets,
            BTreeSet::from([0, 1, 2]),
            "seed {}: the frame offers drop slot 0, drop slot 1 and leave it",
            case.seed
        );
        // The control answers as the table says: the detector then sees the outcome the bot is held to.
        let answer = if case.held_after == case.held_before {
            2
        } else {
            0
        };
        let done = s.answer(answer);
        assert_eq!(
            done.held,
            sorted(case.held_after.to_vec()),
            "seed {}",
            case.seed
        );
        assert_eq!(
            done.ground,
            sorted(case.ground_after.to_vec()),
            "seed {}",
            case.seed
        );
    }
}

#[test]
fn raid_bot_resolves_a_pending_slot_choice_keeping_the_two_spells_of_higher_table_value() {
    let mut accepted_next = 0;
    let mut report = Vec::new();
    for case in &SLOT_CASES {
        let (run, _) = seat(at_pending(case), raid(), AFTER_TICKS);
        let first_clear = run.reads.iter().find(|r| r.pending == 0).map(|r| r.tick);
        assert!(
            first_clear.is_some_and(|t| t <= RESOLVE_WITHIN),
            "seed {}: the pending choice was not answered within {RESOLVE_WITHIN} ticks ({first_clear:?}): {run:?}",
            case.seed
        );
        // What the answer left, read at the first look after it (a bot that walks on leaves the ground out of sight).
        let cleared = run
            .reads
            .iter()
            .find(|r| r.pending == 0)
            .expect("a read after the answer");
        assert_eq!(
            cleared.held,
            sorted(case.held_after.to_vec()),
            "seed {}: the bot keeps the two spells of higher table value",
            case.seed
        );
        assert_eq!(
            cleared.ground,
            sorted(case.ground_after.to_vec()),
            "seed {}: the spell it gives up lies on the ground",
            case.seed
        );
        // After the answer a permanent-reward walker quest is open to it (the slots are full, so the walker takes only
        // those): the freeze is gone when it accepts one. Not measured on an implementation: see the packet.
        let accepted: BTreeSet<&String> = run
            .reads
            .iter()
            .filter(|r| r.pending == 0)
            .flat_map(|r| r.accepted.iter())
            .collect();
        accepted_next += usize::from(!accepted.is_empty());
        report.push((case.seed, accepted.len()));
    }
    assert!(
        accepted_next >= 1,
        "after answering, the bot accepted a quest on {accepted_next} of {} seeds: {report:?}",
        SLOT_CASES.len()
    );
}

// ---------------------------------------------------------------------------------------------------------------
// The pick-up
// ---------------------------------------------------------------------------------------------------------------

/// Seed 4 after the prelude: Cleanse left on the ground, Blind Rage cast away, so a slot is free and the drop lies at
/// the seat's feet.
fn with_free_slot_and_a_drop() -> Script {
    let case = &SLOT_CASES[1];
    assert_eq!(case.seed, PICKUP_SEED);
    let giver = giver_of(PICKUP_SEED, "lost_brother");
    let mut s = Script::new(PICKUP_SEED, giver, [Some(WeaponId::Sword), None]);
    assert_eq!(s.quest("lost_brother", false, 3_000), Stage::Done);
    assert_eq!(s.quest("pilgrims_path", true, 3_000), Stage::Pending);
    let left = s.answer(2);
    assert_eq!(left.ground, vec![CLEANSE], "Cleanse lies on the ground");
    for _ in 0..40 {
        s.step();
    }
    // The first slot holds Blind Rage (the first award of lost_brother): casting it uses it up.
    let at = s.sim.player(s.me).expect("seated").pos();
    s.send(PublicCommand::Cast {
        slot: Slot::Q,
        x: at.x,
        y: at.y,
        id: None,
        t: None,
        seq: None,
    });
    for _ in 0..20 {
        s.step();
    }
    let snap = s.snap();
    assert_eq!(
        snap.held,
        vec![STILL_MIND],
        "one slot is free after the cast: {snap:?}"
    );
    assert_eq!(snap.ground, vec![CLEANSE]);
    s
}

#[test]
fn a_scripted_pickup_takes_the_dropped_spell_when_a_slot_is_free() {
    let mut s = with_free_slot_and_a_drop();
    for _ in 0..40 {
        let obs = s.step();
        let view = obs.quests.expect("a view");
        if let Some(a) = view.actions.iter().find(|a| a.action == 9) {
            s.send(PublicCommand::QuestInteract {
                handle: a.quest,
                action: 9,
                target: a.target,
            });
        }
    }
    let snap = s.snap();
    assert_eq!(snap.held, sorted(vec![STILL_MIND, CLEANSE]), "{snap:?}");
    assert!(snap.ground.is_empty(), "{snap:?}");
}

#[test]
fn raid_bot_picks_up_a_dropped_quest_spell_when_a_slot_is_free() {
    let (run, _) = seat(with_free_slot_and_a_drop(), raid(), 400);
    let last = run.reads.last().expect("reads");
    assert!(
        last.held.contains(&CLEANSE) && last.ground.is_empty(),
        "the bot picked the dropped Cleanse up: {last:?} in {run:?}"
    );
    assert_eq!(run.stats.rejected, 0, "{run:?}");
}

// ---------------------------------------------------------------------------------------------------------------
// The Bargain
// ---------------------------------------------------------------------------------------------------------------

/// What a Bargain scene gives the seat: stacks (by the mob that pays them) and whether it carries a spare weapon.
struct Gift {
    stacks: Vec<(MobTypeId, u64)>,
    spare: bool,
}

/// What a finished Bargain scene shows.
#[derive(Debug)]
struct Outcome {
    completed: bool,
    ember_imp: u64,
    pardoner: u64,
    spare_weapon: bool,
}

fn bargain_world(seed: u64, gift: &Gift) -> Script {
    let giver = giver_of(seed, "the_bargain");
    let arena = arena_of(seed);
    let weapons = [Some(WeaponId::Sword), gift.spare.then_some(WeaponId::Axe)];
    let mut s = Script::new(seed, start_near(&arena, giver), weapons);
    if !gift.stacks.is_empty() {
        apply(
            &mut s.sim,
            s.me,
            &TrustedBuild {
                stacks: gift.stacks.clone(),
                ..TrustedBuild::default()
            },
        )
        .expect("the stacks are granted");
    }
    s
}

fn outcome(s: &Script, reads: &[Snap]) -> Outcome {
    let player = s.sim.player(s.me).expect("seated");
    let accepted = reads.iter().any(|r| r.accepted.contains("the_bargain"));
    // The row leaves the view when the quest is paid; "completed" is an accepted row that is gone at the end.
    let completed = accepted
        && !reads
            .last()
            .is_some_and(|r| r.accepted.contains("the_bargain"));
    Outcome {
        completed,
        ember_imp: player.farm.count(MobTypeId::EmberImp),
        pardoner: player.farm.count(MobTypeId::Pardoner),
        spare_weapon: player.weapon.weapon1.is_some(),
    }
}

/// The control: no policy. Accept, walk to the hag, send `(action, target)` once it is offered.
fn scripted_bargain(seed: u64, gift: &Gift, trade: (u8, u8)) -> Outcome {
    let mut s = bargain_world(seed, gift);
    let mut reads = Vec::new();
    for tick in 0..BARGAIN_TICKS {
        let obs = s.step();
        if let Some(snap) = Snap::of(tick, &obs) {
            reads.push(snap);
        }
        if tick % 5 != 0 {
            continue;
        }
        let Some(view) = obs.quests.as_ref() else {
            continue;
        };
        let Some(row) = view.rows.iter().find(|r| r.def == "the_bargain") else {
            break;
        };
        let proposal = if !row.owner_accepted {
            if row.can_accept {
                PublicCommand::QuestAccept { handle: row.handle }
            } else {
                PublicCommand::Move {
                    x: row.giver.x,
                    y: row.giver.y,
                    seq: None,
                }
            }
        } else if view
            .actions
            .iter()
            .any(|a| a.quest == row.handle && (a.action, a.target) == trade)
        {
            PublicCommand::QuestInteract {
                handle: row.handle,
                action: trade.0,
                target: trade.1,
            }
        } else if let Some(o) = view.objectives.iter().find(|o| o.quest == row.handle) {
            PublicCommand::Move {
                x: o.at.x,
                y: o.at.y,
                seq: None,
            }
        } else {
            continue;
        };
        s.send(proposal);
    }
    outcome(&s, &reads)
}

/// The Bargain scenes prove which trade the bot makes, not that The Bargain outbids the rest of the map. Since
/// P-RAID-Q3-fix the candidate is worth the chosen trade's own gain (about 1.1 to 1.3 camp-equivalents), not the table
/// row's 2.7, and under the default prices another quest or a camp outbids it on these maps. This table keeps the
/// scenes on the decision: The Bargain's row stays (it gates the candidate), every other quest and augment is 0.
fn bargain_prices() -> Prices {
    Prices {
        reward_rows: &[("the_bargain", 2.7)],
        augment_rows: &[],
        reward_default: 0.,
        augment_default: 0.,
        ..Prices::LOCAL
    }
}

fn raid_bargain(seed: u64, gift: &Gift) -> (Outcome, Run) {
    let policy: Box<dyn Policy> =
        Box::new(RaidBot::new(TierId::Normal, PersonaId::Farmer).with_prices(bargain_prices()));
    let (run, s) = seat(bargain_world(seed, gift), policy, BARGAIN_TICKS);
    (outcome(&s, &run.reads), run)
}

/// 20 stacks of damage (`EmberImp`), no spare weapon: the one improving trade is damage for attack damage (a sword's
/// best stat), action 5 with target 0 * 9 + 6.
fn stacks_gift() -> Gift {
    Gift {
        stacks: vec![(MobTypeId::EmberImp, 20)],
        spare: false,
    }
}
/// A spare axe and no stacks: the only trades are action 7; the best stat for a sword is attack damage (target 6).
fn weapon_gift() -> Gift {
    Gift {
        stacks: Vec::new(),
        spare: true,
    }
}
/// 20 stacks of attack damage (`Pardoner`, the sword's best stat), no spare: no offered trade is an improvement.
fn best_stat_gift() -> Gift {
    Gift {
        stacks: vec![(MobTypeId::Pardoner, 20)],
        spare: false,
    }
}

#[test]
fn a_scripted_bargain_trades_stacks_or_the_spare_weapon_on_every_seed() {
    for seed in BARGAIN_SEEDS {
        let a = scripted_bargain(seed, &stacks_gift(), (5, 6));
        assert!(
            a.completed && a.ember_imp == 0 && a.pardoner == 24,
            "seed {seed}: 20 damage stacks become 24 attack damage: {a:?}"
        );
        let b = scripted_bargain(seed, &weapon_gift(), (7, 6));
        assert!(
            b.completed && !b.spare_weapon && b.pardoner == 24,
            "seed {seed}: the spare weapon becomes 24 attack damage: {b:?}"
        );
    }
}

#[test]
fn raid_bot_trades_twenty_stacks_into_the_stat_its_build_values_most() {
    let mut done = Vec::new();
    let mut report = Vec::new();
    for seed in BARGAIN_SEEDS {
        let (o, run) = raid_bargain(seed, &stacks_gift());
        // At least 24: a kill on the way can add the three stacks a normal mob pays.
        if o.completed && o.ember_imp == 0 && o.pardoner >= 24 {
            done.push(seed);
        }
        report.push((seed, o, run.stats));
    }
    assert!(
        done.len() >= BARGAIN_FLOOR,
        "RaidBot completed The Bargain with the right trade on {done:?} of {BARGAIN_SEEDS:?} (floor {BARGAIN_FLOOR}): {report:#?}"
    );
}

#[test]
fn raid_bot_trades_its_spare_weapon_for_the_stat_its_build_values_most() {
    let mut done = Vec::new();
    for seed in BARGAIN_SEEDS {
        let (o, _) = raid_bargain(seed, &weapon_gift());
        if o.completed && !o.spare_weapon && o.pardoner >= 24 {
            done.push(seed);
        }
    }
    assert!(
        done.len() >= BARGAIN_FLOOR,
        "RaidBot traded the spare weapon on {done:?} of {BARGAIN_SEEDS:?} (floor {BARGAIN_FLOOR})"
    );
}

/// A guard that holds on the stub too: with its best stat the only one it has 20 of, no offered trade is an improvement
/// and the bot must not trade (its stacks stay, the quest is never paid).
#[test]
fn raid_bot_makes_no_bargain_that_is_not_an_improvement() {
    for seed in BARGAIN_SEEDS {
        let (o, run) = raid_bargain(seed, &best_stat_gift());
        assert!(
            !o.completed && o.pardoner >= 20,
            "seed {seed}: the bot traded away its best stat: {o:?} {run:?}"
        );
    }
}

// ---------------------------------------------------------------------------------------------------------------
// Commands and replay
// ---------------------------------------------------------------------------------------------------------------

#[test]
fn raid_bot_sends_only_commands_the_sim_accepts_on_the_decision_scenes() {
    // The slot scene, the pick-up scene and a Bargain scene: every proposal resolved to a handle of its frame.
    let (slot, _) = seat(at_pending(&SLOT_CASES[0]), raid(), AFTER_TICKS);
    let (pick, _) = seat(with_free_slot_and_a_drop(), raid(), 400);
    let (barg, run) = raid_bargain(2, &stacks_gift());
    for (name, r) in [("slot", &slot), ("pickup", &pick), ("bargain", &run)] {
        assert!(r.fault.is_none(), "{name}: no seat fault: {r:?}");
        assert_eq!(r.stats.fallback_decisions, 0, "{name}: {r:?}");
        assert_eq!(
            r.stats.rejected, 0,
            "{name}: every proposal was accepted: {r:?}"
        );
        assert_eq!(r.stats.accepted, r.stats.proposed, "{name}: {r:?}");
    }
    // Not vacuous: the scenes' decisions were made.
    let last = slot.reads.last().expect("reads");
    assert_eq!(
        last.held,
        sorted(SLOT_CASES[0].held_after.to_vec()),
        "slot scene: {slot:?}"
    );
    assert!(barg.completed, "bargain scene: {barg:?}");
}

#[test]
fn raid_bot_replays_a_decision_run_exactly() {
    let case = &SLOT_CASES[0];
    let (a, _) = seat(at_pending(case), raid(), AFTER_TICKS);
    let (b, _) = seat(at_pending(case), raid(), AFTER_TICKS);
    // A bot that does nothing replays trivially: the run must have answered the choice.
    assert_eq!(
        a.reads.last().expect("reads").held,
        sorted(case.held_after.to_vec()),
        "seed {}: the replayed run answers the choice: {a:?}",
        case.seed
    );
    assert_eq!(a.reads, b.reads, "seed {}: the same journal", case.seed);
    assert_eq!(a.stats, b.stats, "seed {}: the same proposals", case.seed);
    assert_eq!(
        a.hash, b.hash,
        "seed {}: the same final state hash",
        case.seed
    );
}
