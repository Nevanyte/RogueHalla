//! P-RAID-Q3-fix oracle, scene half (docs/backlog/packets/P-RAID-Q3-fix.md): a `RaidBot` seated as a human on the live content
//! world is never held by a quest decision it cannot finish. A scripted prelude (the control of `raid_decide.rs`) earns a
//! third spell reward on `letters`, whose slot choice waits at the token; the script then pushes the seat away from the
//! token (what a death, a knock-back or an expired paced act does) and a `RaidBot` takes over. The pending answer cannot
//! land from where the seat stands, and the seat does not know the token (a pending row has no objective), so it can only
//! walk to the giver and find nothing offered there. On the base it stands there for the rest of the run; with the fix it
//! gives the def up for a while and goes back to farming and roaming. The unit scenes of `decide_fix_tests.rs` pin the
//! token walk itself (a seat that saw the accepted row remembers the token).
use rh_policy::bots::{PersonaId, RaidBot, TierId};
use rh_policy::{drive_seats_with, feed_seats, scripted, MatchStart, Policy, Seat};
use rh_sim::{
    Arena, ArenaMode, Event, FactionId, ItemObserver, Observation, Placement, PlayerId,
    PublicCommand, Secrets, SightCache, Sim, SimConfig, Vec2, WeaponId,
};
use std::sync::Arc;

/// Seeds whose board deals `letters` (measured on seeds 4 and 5 in `P-RAID-Q3.md`) and the prelude that makes it the third
/// spell: two spells held, then `letters` pending with the choice offered.
const CASES: [(u64, [&str; 3]); 2] = [
    (4, ["pilgrims_path", "lost_brother", "letters"]),
    (5, ["barrow_run", "a_cruel_joke", "letters"]),
];
/// How many of the two seeds must reach the scene (the prelude is a script: a seed it cannot play is not the bot's).
const SCENES_FLOOR: usize = 1;
/// The script pushes the seat this far from the token before the handover (more than the 64 u reach).
const PUSH_TICKS: u32 = 120;
/// The seat runs this long under the bot (60 s).
const RUN_TICKS: u32 = 1_200;
/// The last stretch of the run (the final 30 s) in which the seat must have gone somewhere.
const LATE_TICKS: u32 = 600;
/// The seat's farthest and nearest points in the late stretch must differ by at least this (units): a seat held by a goal
/// stands (a few units of jitter at most); one that roams or farms crosses camps (hundreds).
const MOVED_AT_LEAST: f64 = 60.;

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
        &placement(arena.centre()),
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

/// A sim, its seat and the observer a scripted control reads and sends through.
struct Script {
    sim: Sim,
    me: PlayerId,
    observer: ItemObserver,
    events: Vec<Event>,
    seed: u64,
}

#[derive(Debug, PartialEq)]
enum Stage {
    Done,
    Pending,
    Timeout,
}

impl Script {
    fn new(seed: u64, start: Vec2) -> Self {
        let sim = Sim::reset_active(
            config(),
            arena_of(seed),
            Secrets::from_seed(seed),
            &placement(start),
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

    /// Play `def` straight from the sim's observation (as `raid_decide.rs`): accept at the giver, the offered contact, a
    /// straight walk to the nearest objective. With `stop_pending` it returns when the slot choice is offered.
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
                .min_by(|a, b| {
                    dist(Vec2::new(a.at.x, a.at.y), at)
                        .total_cmp(&dist(Vec2::new(b.at.x, b.at.y), at))
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
            self.send(proposal);
        }
        Stage::Timeout
    }

    /// The seat after the prelude with the choice pending and offered, then pushed toward `away` for `PUSH_TICKS`: its
    /// action 8 is gone (it stands far from the token). `None` when the prelude cannot be played on this seed.
    fn pushed(seed: u64, prelude: [&str; 3], away: Vec2) -> Option<Self> {
        let mut s = Self::new(seed, giver_of(seed, prelude[0]));
        if s.quest(prelude[0], false, 3_000) != Stage::Done
            || s.quest(prelude[1], false, 3_000) != Stage::Done
            || s.quest(prelude[2], true, 3_000) != Stage::Pending
        {
            return None;
        }
        for tick in 0..PUSH_TICKS {
            if tick % 10 == 0 {
                s.send(PublicCommand::Move {
                    x: away.x,
                    y: away.y,
                    seq: None,
                });
            }
            let _ = s.step();
        }
        Some(s)
    }

    /// What the seat's view shows: (letters pending, action 8 offered).
    fn pending_and_offered(&mut self) -> (bool, bool) {
        let obs = self.step();
        let view = obs.quests.expect("a view");
        (
            !view.pending.is_empty(),
            view.actions.iter().any(|a| a.action == 8),
        )
    }
}

fn raid() -> Box<dyn Policy> {
    Box::new(RaidBot::new(TierId::Normal, PersonaId::Farmer))
}

/// The seat's positions, one per tick, with `policy` seated for `ticks` ticks, and the last view's (pending, offered).
fn run(mut s: Script, policy: Box<dyn Policy>, ticks: u32) -> (Vec<Vec2>, (bool, bool)) {
    let start = MatchStart::of(&s.sim, s.me, s.seed);
    let mut seats = vec![Seat::new(&start, policy, Box::new(scripted::Idle))];
    seats[0].bind_fx(&s.sim).expect("a new seat binds");
    let mut sight = SightCache::new();
    let mut reader = ItemObserver::new(s.me);
    let mut at = Vec::new();
    let mut last = (false, false);
    for _ in 0..ticks {
        drive_seats_with(&mut s.sim, &mut seats, &s.events, &mut sight, |_, _| {});
        s.events.clear();
        s.sim.step_into(&mut s.events);
        feed_seats(&s.sim, &mut seats);
        let obs = observe(&s.sim, &mut reader);
        if let (Some(me), Some(view)) = (obs.me.as_ref(), obs.quests.as_ref()) {
            at.push(Vec2::new(me.x, me.y));
            last = (
                !view.pending.is_empty(),
                view.actions.iter().any(|a| a.action == 8),
            );
        }
    }
    (at, last)
}

fn span(points: &[Vec2]) -> f64 {
    points
        .iter()
        .flat_map(|a| points.iter().map(move |b| dist(*a, *b)))
        .fold(0., f64::max)
}

#[test]
fn a_seat_pushed_off_a_pending_token_quest_is_not_held_by_it() {
    let mut scenes = 0;
    let mut held = Vec::new();
    let mut report = Vec::new();
    for (seed, prelude) in CASES {
        let giver = giver_of(seed, "letters");
        let Some(mut s) = Script::pushed(seed, prelude, giver) else {
            report.push((seed, "prelude not playable"));
            continue;
        };
        let (pending, offered) = s.pending_and_offered();
        if !pending || offered {
            report.push((seed, "choice not pending out of reach"));
            continue;
        }
        let (at, (still_pending, _)) = run(s, raid(), RUN_TICKS);
        scenes += 1;
        let late = &at[at.len().saturating_sub(LATE_TICKS as usize)..];
        let moved = span(late);
        // Measured aid: the first tick from which the seat never moved again (the freeze's start under the bot).
        let end = *at.last().expect("reads");
        let still_from = at
            .iter()
            .rposition(|p| dist(*p, end) >= 1.)
            .map_or(0, |i| i + 1);
        eprintln!(
            "seed {seed}: still from tick {still_from} of {}, late span {moved:.1}",
            at.len()
        );
        assert!(
            still_pending,
            "seed {seed}: nothing answers a choice out of reach: the scene is not the freeze"
        );
        if moved < MOVED_AT_LEAST {
            held.push((seed, still_from));
        }
    }
    assert!(
        scenes >= SCENES_FLOOR,
        "the scene was reached on {scenes} of {} seeds: {report:?}",
        CASES.len()
    );
    assert!(
        held.is_empty(),
        "seats that stood still for the last {LATE_TICKS} ticks, held by a pending choice they cannot answer: (seed, tick from \
         which the seat never moved) {held:?}"
    );
}
