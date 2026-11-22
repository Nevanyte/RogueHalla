//! P-RAID-Q1 oracle (loose proof, docs/backlog/packets/P-RAID-Q1.md): a `RaidBot` seated as a HUMAN (`bot: false`) on the
//! live content world with a quest board, started a short walk from the giver of a walker quest, accepts walker quests,
//! completes a floor of them before their timers, accepts nothing outside the walker list, sends only commands the sim
//! accepts, and replays a seed exactly.
//!
//! The harness is the P-RAID-0 seat path (`drive_seats_with` -> `Sim::step_into` -> `feed_seats`). A quest is accepted
//! when its row shows `owner_accepted`, and completed when an accepted row later leaves the seat's view (the board never
//! sets `owner_completed`: `rh-sim/src/quest_view/board.rs:17-23,118`); a second `ItemObserver` of the same viewer reads
//! the sim's own observation every few ticks for that. A scripted mover (no policy) is the control: it shows the scene's
//! quest can be done by a straight walk, so a red RaidBot is the bot's.
use rh_policy::bots::{PersonaId, RaidBot, TierId};
use rh_policy::{drive_seats_with, feed_seats, scripted, MatchStart, Policy, Seat, SeatStats};
use rh_sim::{
    Arena, ArenaMode, Event, FactionId, ItemObserver, Observation, Placement, PlayerId,
    PublicCommand, Secrets, SightCache, Sim, SimConfig, Vec2, WeaponId,
};
use std::collections::BTreeSet;
use std::sync::Arc;

/// Seeds of the sweep: which quests are dealt varies by seed. Every one of these deals at least three walker quests
/// (measured on seeds 1-24) and a cheap one (`ember_walk`, `watchtowers`, `barrow_run` or `pilgrims_path`) at the start.
const SEEDS: std::ops::RangeInclusive<u64> = 1..=6;
/// 180 s of sim time at the 20 Hz step. The cheapest dealt quest is 8-19 s of straight walking against a timer of
/// 38-56 s, so one completion needs under a quarter of this even at twice the walk.
const MAX_TICKS: u32 = 3_600;
/// How far from the giver the fighter starts: a few seconds' walk (the accept range is 64 u).
const START_DIST: f64 = 160.;
/// The journal is read from the sim every this many ticks (a quest never leaves and returns, so sparse reads lose nothing).
const READ_EVERY: u32 = 4;
/// The seeds on which RaidBot must complete at least one walker quest, and the total over the sweep. Chosen from the
/// control: a scripted straight walk at the scene's quest completes it on all 6 seeds in under 180 s (one quest each, so
/// one per seed is what a straight walker is known to do), and the board gives two spell slots, so a seat with no slot
/// choice (Q3) finishes at most two spell-reward quests plus the permanent-reward ones (`tuber_eats`,
/// `even_skeletons_get_cold`, `let_him_cook`). One lost seed is allowed (a camp near the start can kill a fresh
/// fighter, and the arbiter may prefer a longer quest). Not measured on a RaidBot yet: the coordinator tightens both
/// once an implementation has produced a total.
const SEEDS_WITH_A_COMPLETION: usize = 5;
const TOTAL_COMPLETIONS: usize = 6;

/// The quests the walker may do (docs/backlog/packets/P-RAID-Q1.md F1; `feed_or_hunt` is not one of them), by the
/// preference the scene uses to pick its giver: cheapest round trip first.
const WALKER: [&str; 11] = [
    "ember_walk",
    "watchtowers",
    "barrow_run",
    "pilgrims_path",
    "a_cruel_joke",
    "even_skeletons_get_cold",
    "tuber_eats",
    "lost_brother",
    "smugglers_run",
    "letters",
    "let_him_cook",
];

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

/// The live content world (quests and the Top 25 board on), no match lifecycle: active at once, the fighter stands where
/// it is placed (as the P-RAID-0 oracle).
fn config() -> SimConfig {
    SimConfig {
        lifecycle: ArenaMode::Off,
        min_players: 1,
        ..SimConfig::live_content()
    }
}

fn placement(start: Vec2) -> [Placement; 1] {
    [Placement {
        profile: "raider".into(),
        faction: FactionId::Holy,
        bot: false,
        at: start,
        hp: None,
        weapon0: Some(WeaponId::Sword),
        weapon1: None,
    }]
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

fn observe(sim: &Sim, observer: &mut ItemObserver) -> Observation {
    let mut obs = Observation::default();
    observer
        .prepare_into(sim, &[], &mut obs, None)
        .expect("an observation")
        .commit();
    obs
}

struct Scene {
    seed: u64,
    arena: Arc<Arena>,
    /// The walker quest whose giver the fighter starts beside.
    def: &'static str,
    giver: Vec2,
    start: Vec2,
}

/// The cheapest dealt walker quest (by `WALKER` order), and a start `START_DIST` from its giver with a straight floor
/// line to it.
fn scene(seed: u64) -> Scene {
    let secrets = Secrets::from_seed(seed);
    let arena = rh_sim::quests::board::map_admission::ensure(Arc::new(map()), secrets)
        .expect("a board-admitted map");
    let probe = Sim::reset_active(config(), arena.clone(), secrets, &placement(arena.centre()));
    let me = probe.players()[0].id;
    let obs = observe(&probe, &mut ItemObserver::new(me));
    let rows = obs.quests.expect("a human seat sees the board").rows;
    for def in WALKER {
        let Some(row) = rows.iter().find(|r| r.def == def) else {
            continue;
        };
        let giver = row.giver;
        for k in 0..16 {
            let a = f64::from(k) * std::f64::consts::TAU / 16.;
            let start = Vec2::new(
                giver.x + rh_sim::math::cos(a) * START_DIST,
                giver.y + rh_sim::math::sin(a) * START_DIST,
            );
            if floor_line(&arena, start, giver) {
                return Scene {
                    seed,
                    arena,
                    def,
                    giver,
                    start,
                };
            }
        }
    }
    panic!("seed {seed}: no walker quest with a floor start");
}

fn world(s: &Scene) -> (Sim, PlayerId) {
    let sim = Sim::reset_active(
        config(),
        s.arena.clone(),
        Secrets::from_seed(s.seed),
        &placement(s.start),
    );
    let me = sim.players()[0].id;
    (sim, me)
}

/// What the seat's own view shows over a run.
#[derive(Debug, Default)]
struct Journal {
    /// Quests whose row showed `owner_accepted` at some read.
    accepted: BTreeSet<String>,
    /// Quests accepted whose row later left the view: turned in and rewarded.
    completed: BTreeSet<String>,
    /// The most quest spells held at once.
    spells: usize,
}

impl Journal {
    fn read(&mut self, obs: &Observation) {
        let Some(view) = obs.quests.as_ref() else {
            return;
        };
        if view.rows.is_empty() && self.accepted.is_empty() {
            return;
        }
        for row in &view.rows {
            if row.owner_accepted {
                self.accepted.insert(row.def.clone());
            }
        }
        for def in &self.accepted {
            if !view.rows.iter().any(|r| r.def == *def) {
                self.completed.insert(def.clone());
            }
        }
        self.spells = self.spells.max(view.spells.len());
    }
}

#[derive(Debug)]
struct Run {
    journal: Journal,
    stats: SeatStats,
    fault: Option<rh_policy::Fault>,
    hash: u64,
}

/// Seat `policy` for the scene's human fighter and step `MAX_TICKS`.
fn run(s: &Scene, policy: Box<dyn Policy>) -> Run {
    let (mut sim, me) = world(s);
    let start = MatchStart::of(&sim, me, s.seed);
    let mut seats = vec![Seat::new(&start, policy, Box::new(scripted::Idle))];
    seats[0].bind_fx(&sim).expect("a new seat binds");
    let mut sight = SightCache::new();
    let mut events: Vec<Event> = Vec::new();
    let mut reader = ItemObserver::new(me);
    let mut journal = Journal::default();
    for tick in 0..MAX_TICKS {
        drive_seats_with(&mut sim, &mut seats, &events, &mut sight, |_, _| {});
        events.clear();
        sim.step_into(&mut events);
        feed_seats(&sim, &mut seats);
        if tick % READ_EVERY == 0 {
            journal.read(&observe(&sim, &mut reader));
        }
    }
    Run {
        journal,
        stats: seats[0].stats.clone(),
        fault: seats[0].fault.clone(),
        hash: sim.state_hash(),
    }
}

fn raid() -> Box<dyn Policy> {
    Box::new(RaidBot::new(TierId::Normal, PersonaId::Farmer))
}

/// The control: no policy. Every few ticks it sends one command straight from the sim's own observation: accept the
/// scene's quest at its giver, the offered contact (action 3) when one is, else a straight walk to the first objective
/// (or to the giver before accepting).
fn scripted_run(s: &Scene) -> Journal {
    let (mut sim, me) = world(s);
    let mut observer = ItemObserver::new(me);
    let mut journal = Journal::default();
    let mut events: Vec<Event> = Vec::new();
    for tick in 0..MAX_TICKS {
        sim.step_into(&mut events);
        events.clear();
        let obs = observe(&sim, &mut observer);
        journal.read(&obs);
        if tick % 5 != 0 {
            continue;
        }
        let (Some(view), Some(owner)) = (obs.quests.as_ref(), obs.me.as_ref()) else {
            continue;
        };
        let Some(row) = view.rows.iter().find(|r| r.def == s.def) else {
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
            .min_by(|a, b| {
                let d = |p: Vec2| rh_sim::math::hypot(p.x - owner.x, p.y - owner.y);
                d(a.at).total_cmp(&d(b.at))
            })
        {
            PublicCommand::Move {
                x: o.at.x,
                y: o.at.y,
                seq: None,
            }
        } else {
            continue;
        };
        if let Ok(cmd) = observer.resolve(&sim, proposal) {
            let _ = sim.submit(me, cmd);
        }
    }
    journal
}

#[test]
fn raid_quests_scene_is_a_human_seat_offering_an_acceptable_quest_at_the_start() {
    for seed in SEEDS {
        let s = scene(seed);
        // The start is a short walk away; stand AT the giver for the offer check.
        let at_giver = Scene {
            start: s.giver,
            arena: s.arena.clone(),
            ..s
        };
        let (sim, me) = world(&at_giver);
        assert!(
            !sim.player(me).expect("seated").seat.bot,
            "seed {seed}: the fighter is a human seat"
        );
        let obs = observe(&sim, &mut ItemObserver::new(me));
        let rows = obs.quests.expect("a human seat sees the board").rows;
        assert!(
            rows.iter().any(|r| r.def == at_giver.def && r.can_accept),
            "seed {seed}: at the giver the frame offers {} for acceptance",
            at_giver.def
        );
        let away = Scene { ..scene(seed) };
        let (sim, me) = world(&away);
        let obs = observe(&sim, &mut ItemObserver::new(me));
        assert!(
            !obs.quests
                .expect("board")
                .rows
                .iter()
                .any(|r| r.def == away.def && r.can_accept),
            "seed {seed}: {START_DIST} u from the giver nothing is acceptable yet: the bot must walk"
        );
    }
}

#[test]
fn a_scripted_straight_walk_completes_the_scenes_quest() {
    let mut done = Vec::new();
    for seed in SEEDS {
        let s = scene(seed);
        let j = scripted_run(&s);
        if j.completed.contains(s.def) {
            done.push(seed);
        }
    }
    assert!(
        done.len() == SEEDS.count(),
        "control: a scripted straight walk completed the scene's quest on {done:?} only"
    );
}

#[test]
fn raid_bot_accepts_and_completes_walker_quests_before_their_timers() {
    let mut with_completion = 0;
    let mut total = 0;
    let mut report = Vec::new();
    for seed in SEEDS {
        let s = scene(seed);
        let r = run(&s, raid());
        assert!(
            !r.journal.accepted.is_empty(),
            "seed {seed}: RaidBot accepted no quest (scene quest {}): {r:?}",
            s.def
        );
        assert!(
            r.journal
                .accepted
                .iter()
                .any(|d| WALKER.contains(&d.as_str())),
            "seed {seed}: RaidBot accepted no walker quest: {r:?}"
        );
        assert!(
            r.journal.accepted.iter().all(|d| WALKER.contains(&d.as_str())),
            "seed {seed}: RaidBot accepted a quest outside the walker list (dwell, decision, kill, PvP, search and \
             feed_or_hunt are other packets'): {r:?}"
        );
        let n = r.journal.completed.len();
        total += n;
        with_completion += usize::from(n > 0);
        report.push((seed, s.def, r.journal));
    }
    assert!(
        with_completion >= SEEDS_WITH_A_COMPLETION && total >= TOTAL_COMPLETIONS,
        "RaidBot completed walker quests on {with_completion} seeds, {total} in all (floor {SEEDS_WITH_A_COMPLETION} \
         seeds, {TOTAL_COMPLETIONS} in all): {report:#?}"
    );
}

#[test]
fn raid_bot_sends_only_commands_the_sim_accepts_on_the_quest_board() {
    for seed in SEEDS {
        let r = run(&scene(seed), raid());
        assert!(r.fault.is_none(), "seed {seed}: no seat fault: {r:?}");
        assert_eq!(
            r.stats.fallback_decisions, 0,
            "seed {seed}: the policy decided every turn itself: {r:?}"
        );
        assert!(
            !r.journal.accepted.is_empty(),
            "seed {seed}: the bot took part in the board: {r:?}"
        );
        assert_eq!(
            r.stats.rejected, 0,
            "seed {seed}: every proposal resolved to a handle of its frame and Sim::submit took it: {r:?}"
        );
        assert_eq!(r.stats.accepted, r.stats.proposed, "seed {seed}: {r:?}");
    }
}

#[test]
fn raid_bot_replays_a_quest_run_exactly() {
    let s = scene(1);
    let a = run(&s, raid());
    let b = run(&s, raid());
    // A bot that does nothing replays trivially: the run must have taken a quest.
    assert!(
        !a.journal.accepted.is_empty(),
        "seed 1: the replayed run accepts a quest: {a:?}"
    );
    assert_eq!(a.journal.accepted, b.journal.accepted, "seed 1: accepted");
    assert_eq!(
        a.journal.completed, b.journal.completed,
        "seed 1: completed"
    );
    assert_eq!(a.stats, b.stats, "seed 1: the same proposals");
    assert_eq!(a.hash, b.hash, "seed 1: the same final state hash");
}
