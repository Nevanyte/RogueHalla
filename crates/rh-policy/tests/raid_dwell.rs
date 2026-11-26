//! P-RAID-Q2 oracle (loose proof, docs/backlog/packets/P-RAID-Q2.md): a `RaidBot` seated as a HUMAN (`bot: false`) on the
//! live content world with a quest board, started a short walk from the giver of a dwell quest (`hold_the_fort`: 45 s in
//! a Gallows guard house; `stay_a_while`: a 30 s meditation channel at the monk), accepts it, dwells and completes it, and
//! accepts nothing outside the walker and dwell lists; its commands are all accepted by the sim; a seed replays exactly.
//! A second player standing in the guard house contests the hold: the bot must not sit there for ever.
//!
//! The harness is `tests/raid_quests.rs`'s (P-RAID-Q1): `drive_seats_with` -> `Sim::step_into` -> `feed_seats`; a quest is
//! accepted when its row shows `owner_accepted` and completed when an accepted row later leaves the seat's view. The
//! scripted mover (no policy) is the control: it shows each scene can be done and the detector sees it.
use rh_policy::bots::raid::values::Prices;
use rh_policy::bots::{PersonaId, RaidBot, TierId};
use rh_policy::{drive_seats_with, feed_seats, scripted, MatchStart, Policy, Seat, SeatStats};
use rh_sim::{
    Arena, ArenaMode, Event, FactionId, ItemObserver, Observation, Placement, PlayerId,
    PublicCommand, Secrets, SightCache, Sim, SimConfig, Vec2, WeaponId,
};
use std::collections::BTreeSet;
use std::sync::Arc;

/// Seeds that deal each quest on `seed1.map.json` (measured on seeds 1-24, P-RAID-Q2 fact 3), both giver variants of each.
const HOLD_SEEDS: [u64; 5] = [2, 3, 4, 9, 12];
const STAY_SEEDS: [u64; 5] = [2, 5, 13, 22, 24];
/// Seeds that deal both (the default-price sweep, where the dwell and walker quests compete).
const BOTH_SEEDS: [u64; 4] = [13, 14, 19, 24];
/// 120 s of sim time: a 45 s hold plus the walks (about 3 s each way) leaves a wide margin; neither quest has a timer.
const MAX_TICKS: u32 = 2_400;
const START_DIST: f64 = 160.;
const READ_EVERY: u32 = 4;
/// The guard house the hold is dealt on every seed: the objective of `hold_the_fort` after accepting (measured).
const FORT: Vec2 = Vec2 { x: 4656., y: 2256. };
/// The server's own range for "inside" when the map has no layers (`interactions.rs` `inside_fort`); the proxy used here.
const FORT_RANGE: f64 = 72.;
/// Of 5 seeds per quest the scripted control completes all 5. A bot that walks and stands as well is allowed to lose one
/// (a camp near the route, a different start path). Not measured on a RaidBot yet: the coordinator tightens it from the
/// first implementation's total.
const FLOOR_PER_QUEST: usize = 4;

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
const DWELL: [&str; 2] = ["hold_the_fort", "stay_a_while"];
/// The two decision quests P-RAID-Q3b taught the bot (coordinator's oracle-amend, 2026-11-23): under the default prices
/// a seat may now accept them, as seed 19 does with Trade Up.
const DECIDE_B: [&str; 2] = ["two_banners", "trade_up"];

/// Only the two dwell rewards are worth anything: the bot is not drawn to a walker quest or a camp first, so the sweep
/// proves the executor, not the arbiter's preference.
const DWELL_ONLY: Prices = Prices {
    reward_rows: &[("hold_the_fort", 3.6), ("stay_a_while", 2.0)],
    ..Prices::farm_only()
};

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

fn fighter(at: Vec2) -> Placement {
    Placement {
        profile: "raider".into(),
        faction: FactionId::Holy,
        bot: false,
        at,
        hp: None,
        weapon0: Some(WeaponId::Sword),
        weapon1: None,
    }
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

fn dist(a: Vec2, b: Vec2) -> f64 {
    rh_sim::math::hypot(a.x - b.x, a.y - b.y)
}

struct Scene {
    seed: u64,
    arena: Arc<Arena>,
    def: &'static str,
    giver: Vec2,
    start: Vec2,
    /// A second player standing in the guard house from the first tick (the contest scene).
    sentinel: bool,
}

fn scene(seed: u64, def: &'static str, sentinel: bool) -> Scene {
    let secrets = Secrets::from_seed(seed);
    let arena = rh_sim::quests::board::map_admission::ensure(Arc::new(map()), secrets)
        .expect("a board-admitted map");
    let probe = Sim::reset_active(config(), arena.clone(), secrets, &[fighter(arena.centre())]);
    let me = probe.players()[0].id;
    let rows = observe(&probe, &mut ItemObserver::new(me))
        .quests
        .expect("a human seat sees the board")
        .rows;
    let giver = rows
        .iter()
        .find(|r| r.def == def)
        .unwrap_or_else(|| panic!("seed {seed} deals {def}"))
        .giver;
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
                sentinel,
            };
        }
    }
    panic!("seed {seed}: no floor start near the giver of {def}");
}

fn world(s: &Scene) -> (Sim, PlayerId) {
    let mut placements = vec![fighter(s.start)];
    if s.sentinel {
        placements.push(fighter(FORT));
    }
    let sim = Sim::reset_active(
        config(),
        s.arena.clone(),
        Secrets::from_seed(s.seed),
        &placements,
    );
    let me = sim.players()[0].id;
    (sim, me)
}

/// What the seat's own view shows over a run.
#[derive(Default)]
struct Journal {
    accepted: BTreeSet<String>,
    completed: BTreeSet<String>,
    /// The most whole seconds of dwell the scene's row ever showed (`owner_progress` is the server's dwell clock).
    max_progress: u32,
    /// Per read: (tick, distance of the seat to the guard house centre).
    fort_samples: Vec<(u32, f64)>,
}

impl std::fmt::Debug for Journal {
    fn fmt(&self, f: &mut std::fmt::Formatter<'_>) -> std::fmt::Result {
        let near = self
            .fort_samples
            .iter()
            .filter(|s| s.1 <= FORT_RANGE)
            .count();
        write!(
            f,
            "accepted {:?}, completed {:?}, max dwell {} s, {near} of {} reads inside the fort",
            self.accepted,
            self.completed,
            self.max_progress,
            self.fort_samples.len()
        )
    }
}

impl Journal {
    fn read(&mut self, tick: u32, def: &str, obs: &Observation) {
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
            if row.def == def {
                self.max_progress = self.max_progress.max(row.owner_progress);
            }
        }
        for d in &self.accepted {
            if !view.rows.iter().any(|r| r.def == *d) {
                self.completed.insert(d.clone());
            }
        }
        if let Some(me) = obs.me.as_ref() {
            self.fort_samples
                .push((tick, dist(Vec2::new(me.x, me.y), FORT)));
        }
    }
}

#[derive(Debug)]
struct Run {
    journal: Journal,
    stats: SeatStats,
    fault: Option<rh_policy::Fault>,
    hash: u64,
    /// The sentinel was still standing at the end (true when there is none).
    sentinel_alive: bool,
}

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
            journal.read(tick, s.def, &observe(&sim, &mut reader));
        }
    }
    let sentinel_alive = !s.sentinel || sim.players().get(1).is_some_and(|p| p.standing());
    Run {
        journal,
        stats: seats[0].stats.clone(),
        fault: seats[0].fault.clone(),
        hash: sim.state_hash(),
        sentinel_alive,
    }
}

/// Only `def`'s reward is worth anything: a sweep that proves one quest prices only that quest (seed 2 deals both).
fn raid_only(def: &str) -> Box<dyn Policy> {
    const HOLD: Prices = Prices {
        reward_rows: &[("hold_the_fort", 3.6)],
        ..Prices::farm_only()
    };
    const STAY: Prices = Prices {
        reward_rows: &[("stay_a_while", 2.0)],
        ..Prices::farm_only()
    };
    let prices = if def == "hold_the_fort" { HOLD } else { STAY };
    Box::new(RaidBot::new(TierId::Normal, PersonaId::Farmer).with_prices(prices))
}

fn raid_dwell_only() -> Box<dyn Policy> {
    Box::new(RaidBot::new(TierId::Normal, PersonaId::Farmer).with_prices(DWELL_ONLY))
}

fn raid_default() -> Box<dyn Policy> {
    Box::new(RaidBot::new(TierId::Normal, PersonaId::Farmer))
}

/// The control: no policy. Every 5 ticks, from the sim's own observation: accept the scene's quest at its giver; when the
/// frame offers action 6 (meditate) send it; otherwise walk to the first objective and, once within 40 u of it, send
/// nothing (a Move to the spot would stir the seat: a meditation breaks on any change of position). Once ready the
/// objective is the giver, so the same rule walks back to turn in.
fn scripted_run(s: &Scene) -> (Journal, bool) {
    let (mut sim, me) = world(s);
    let mut observer = ItemObserver::new(me);
    let mut journal = Journal::default();
    let mut events: Vec<Event> = Vec::new();
    for tick in 0..MAX_TICKS {
        sim.step_into(&mut events);
        events.clear();
        let obs = observe(&sim, &mut observer);
        journal.read(tick, s.def, &obs);
        if tick % 5 != 0 {
            continue;
        }
        let (Some(view), Some(owner)) = (obs.quests.as_ref(), obs.me.as_ref()) else {
            continue;
        };
        let Some(row) = view.rows.iter().find(|r| r.def == s.def) else {
            break;
        };
        let me_at = Vec2::new(owner.x, owner.y);
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
            .any(|a| a.quest == row.handle && a.action == 6)
        {
            PublicCommand::QuestInteract {
                handle: row.handle,
                action: 6,
                target: 0,
            }
        } else if let Some(o) = view
            .objectives
            .iter()
            .filter(|o| o.quest == row.handle)
            .min_by(|a, b| dist(a.at, me_at).total_cmp(&dist(b.at, me_at)))
            .filter(|o| dist(o.at, me_at) > 40.)
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
    let alive = !s.sentinel || sim.players().get(1).is_some_and(|p| p.standing());
    (journal, alive)
}

#[test]
fn dwell_scenes_offer_the_quest_at_the_giver_and_not_at_the_start() {
    for (def, seeds) in [("hold_the_fort", HOLD_SEEDS), ("stay_a_while", STAY_SEEDS)] {
        for seed in seeds {
            let s = scene(seed, def, false);
            let at_giver = Scene {
                start: s.giver,
                arena: s.arena.clone(),
                ..s
            };
            let (sim, me) = world(&at_giver);
            let rows = observe(&sim, &mut ItemObserver::new(me))
                .quests
                .expect("board")
                .rows;
            assert!(
                rows.iter().any(|r| r.def == def && r.can_accept),
                "seed {seed}: at the giver the frame offers {def}"
            );
            let away = scene(seed, def, false);
            let (sim, me) = world(&away);
            let rows = observe(&sim, &mut ItemObserver::new(me))
                .quests
                .expect("board")
                .rows;
            assert!(
                !rows.iter().any(|r| r.def == def && r.can_accept),
                "seed {seed}: {START_DIST} u from the giver nothing is acceptable"
            );
        }
    }
}

#[test]
fn a_scripted_dwell_completes_each_scene() {
    for (def, seeds) in [("hold_the_fort", HOLD_SEEDS), ("stay_a_while", STAY_SEEDS)] {
        let mut done = Vec::new();
        for seed in seeds {
            let (j, _) = scripted_run(&scene(seed, def, false));
            // The row shows the server's dwell clock in whole seconds: a finished dwell showed the full time.
            let full = if def == "hold_the_fort" { 44 } else { 29 };
            if j.completed.contains(def) && j.max_progress >= full {
                done.push(seed);
            }
        }
        assert!(
            done.len() == seeds.len(),
            "control: a scripted dwell completed {def} on {done:?} only"
        );
    }
}

#[test]
fn a_second_player_in_the_guard_house_stops_the_scripted_hold() {
    for seed in [3, 9] {
        let (j, alive) = scripted_run(&scene(seed, "hold_the_fort", true));
        assert!(
            alive,
            "seed {seed}: the sentinel stayed standing in the fort"
        );
        assert!(
            j.accepted.contains("hold_the_fort") && !j.completed.contains("hold_the_fort"),
            "seed {seed}: contested, the hold never completes (the server pauses it): {j:?}"
        );
    }
}

#[test]
fn raid_bot_accepts_and_completes_each_dwell_quest() {
    for (def, seeds) in [("hold_the_fort", HOLD_SEEDS), ("stay_a_while", STAY_SEEDS)] {
        let mut done = Vec::new();
        for seed in seeds {
            let r = run(&scene(seed, def, false), raid_only(def));
            assert!(
                r.journal.accepted.contains(def),
                "seed {seed}: RaidBot accepted {def}: {r:?}"
            );
            assert!(
                r.journal.accepted.iter().all(|d| d == def),
                "seed {seed}: with only {def} worth anything, nothing else is accepted: {r:?}"
            );
            if r.journal.completed.contains(def) {
                done.push(seed);
            }
        }
        assert!(
            done.len() >= FLOOR_PER_QUEST,
            "RaidBot completed {def} on {done:?} (floor {FLOOR_PER_QUEST} of {})",
            seeds.len()
        );
    }
}

#[test]
fn raid_bot_takes_only_walker_and_dwell_quests_and_sends_only_accepted_commands() {
    for (def, seeds) in [("hold_the_fort", HOLD_SEEDS), ("stay_a_while", STAY_SEEDS)] {
        for seed in seeds {
            // Dwell-only prices: every command goes through the sim.
            let r = run(&scene(seed, def, false), raid_dwell_only());
            assert!(r.fault.is_none(), "seed {seed} {def}: no seat fault: {r:?}");
            assert_eq!(r.stats.fallback_decisions, 0, "seed {seed} {def}: {r:?}");
            assert_eq!(r.stats.rejected, 0, "seed {seed} {def}: {r:?}");
            assert_eq!(
                r.stats.accepted, r.stats.proposed,
                "seed {seed} {def}: {r:?}"
            );
            assert!(
                !r.journal.accepted.is_empty(),
                "seed {seed} {def}: the bot took part in the board: {r:?}"
            );
        }
    }
    // Default prices (walker, dwell and decision quests compete): whatever it chooses is a quest the bot can play.
    for seed in BOTH_SEEDS {
        let r = run(&scene(seed, "stay_a_while", false), raid_default());
        assert!(
            r.journal.accepted.iter().all(|d| WALKER.contains(&d.as_str())
                || DWELL.contains(&d.as_str())
                || DECIDE_B.contains(&d.as_str())),
            "seed {seed}: RaidBot accepted a quest outside the walker, dwell and decision lists: {r:?}"
        );
        assert_eq!(r.stats.rejected, 0, "seed {seed}: default prices: {r:?}");
        assert!(r.fault.is_none(), "seed {seed}: {r:?}");
    }
}

#[test]
fn raid_bot_replays_a_dwell_run_exactly() {
    for (seed, def) in [(3, "hold_the_fort"), (5, "stay_a_while")] {
        let s = scene(seed, def, false);
        let a = run(&s, raid_dwell_only());
        let b = run(&s, raid_dwell_only());
        assert!(
            a.journal.accepted.contains(def),
            "seed {seed}: the replayed run takes {def}: {a:?}"
        );
        assert_eq!(a.journal.accepted, b.journal.accepted, "seed {seed}");
        assert_eq!(a.journal.completed, b.journal.completed, "seed {seed}");
        assert_eq!(a.stats, b.stats, "seed {seed}: the same proposals");
        assert_eq!(a.hash, b.hash, "seed {seed}: the same final state hash");
    }
}

#[test]
fn raid_bot_does_not_sit_in_a_contested_fort_for_ever() {
    for seed in [3, 9] {
        let r = run(&scene(seed, "hold_the_fort", true), raid_dwell_only());
        let j = &r.journal;
        assert!(
            r.sentinel_alive,
            "seed {seed}: the sentinel kept the fort: {r:?}"
        );
        assert!(
            j.accepted.contains("hold_the_fort"),
            "seed {seed}: the bot took the hold: {r:?}"
        );
        assert!(
            j.fort_samples.iter().any(|&(_, d)| d <= FORT_RANGE),
            "seed {seed}: the bot reached the contested fort at all (else this proves nothing)"
        );
        assert!(
            !j.completed.contains("hold_the_fort"),
            "seed {seed}: a contested hold cannot complete"
        );
        // The last third of the window: it spends under 90% of its reads inside the fort.
        let late: Vec<_> = j
            .fort_samples
            .iter()
            .filter(|&&(t, _)| t >= MAX_TICKS * 2 / 3)
            .collect();
        let inside = late.iter().filter(|&&&(_, d)| d <= FORT_RANGE).count();
        assert!(
            !late.is_empty() && inside * 10 < late.len() * 9,
            "seed {seed}: the bot stayed in a contested fort ({inside} of {} late reads inside)",
            late.len()
        );
    }
}
