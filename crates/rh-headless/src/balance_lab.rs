//! `balance_lab`: seeded 8-seat matches under `SimConfig::live_content()`, observed read-only, folded into one report
//! (schema `balance-lab/1`). The observer takes `&Sim`, events and ordinary observations; it never changes the sim.
//! Used by `src/bin/balance_lab.rs` and `tests/balance_lab.rs`; see `tools/balance-lab/README.md`.
mod acc;
pub mod attempts;
pub mod chest_picks;
pub mod elite_fights;
mod observer;
mod table;
pub mod time_budget;

use crate::{Agent, AgentSpec, BotKind, Env, EnvConfig};
use acc::{Acc, Augments, Elites, Merge, Quests, Rewards, SeatReport, World};
use observer::Observer;
use rh_sim::{FactionId, SimConfig};
use serde::Serialize;
use std::ops::Range;
use std::panic::{catch_unwind, AssertUnwindSafe};
use std::sync::atomic::{AtomicUsize, Ordering};
use std::sync::Mutex;

pub const SCHEMA: &str = "balance-lab/1";
/// Seats in a lab match.
pub const SEATS: usize = 8;

/// The sim's own credit window (`systems/credit.rs:24`): a hit counts for a death while
/// `death_at_ms - hit_at_ms <= KILL_CREDIT_SECONDS * 1000`.
pub fn credit_window_open(hit_at_ms: f64, death_at_ms: f64) -> bool {
    death_at_ms - hit_at_ms <= rh_sim::constants::KILL_CREDIT_SECONDS * 1000.
}

/// The policy of one seat (`--seat I=POLICY`).
#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub enum SeatPolicy {
    Brawler,
    Human,
    Raid,
}

/// Where a seed's map comes from (`--map`).
#[derive(Clone, Debug, PartialEq, Eq)]
pub enum MapChoice {
    /// One generated map per seed, as the live server makes them.
    Generate,
    /// One map file for every seed (the sim still swaps a map that cannot host the quest board).
    File(String),
}

#[derive(Clone, Debug)]
pub struct Options {
    /// Half-open: `1..4` plays seeds 1, 2 and 3.
    pub seeds: Range<u64>,
    pub jobs: usize,
    pub max_ticks: u32,
    pub map: MapChoice,
    /// Seat index and policy; a seat not listed plays [`SeatPolicy::Human`].
    pub seats: Vec<(usize, SeatPolicy)>,
    /// Seat every player with `bot: false`, which is what lets a seat take quests.
    pub human_seats: bool,
}

impl Default for Options {
    fn default() -> Self {
        Self {
            seeds: 1..2,
            jobs: 1,
            max_ticks: 24_000,
            map: MapChoice::Generate,
            seats: Vec::new(),
            human_seats: false,
        }
    }
}

/// `balance_lab [--seeds A..B] [--jobs N] [--max-ticks N] [--map generate|PATH] [--seat I=POLICY]... [--human-seats]`
/// without the program name.
pub fn parse_args(args: &[String]) -> Result<Options, String> {
    let mut o = Options::default();
    let mut it = args.iter();
    while let Some(flag) = it.next() {
        if flag == "--human-seats" {
            o.human_seats = true;
            continue;
        }
        let value = it.next().ok_or_else(|| format!("{flag} needs a value"))?;
        match flag.as_str() {
            "--seeds" => {
                let (a, b) = value
                    .split_once("..")
                    .ok_or_else(|| format!("--seeds wants A..B, got {value}"))?;
                let (a, b): (u64, u64) = (
                    a.parse().map_err(|_| format!("bad seed {a}"))?,
                    b.parse().map_err(|_| format!("bad seed {b}"))?,
                );
                if b <= a {
                    return Err(format!("--seeds {value} is empty"));
                }
                o.seeds = a..b;
            }
            "--jobs" => {
                o.jobs = value.parse().map_err(|_| format!("bad --jobs {value}"))?;
                if o.jobs == 0 {
                    return Err("--jobs must be at least 1".to_string());
                }
            }
            "--max-ticks" => {
                o.max_ticks = value
                    .parse()
                    .map_err(|_| format!("bad --max-ticks {value}"))?;
            }
            "--map" => {
                o.map = if value == "generate" {
                    MapChoice::Generate
                } else {
                    MapChoice::File(value.clone())
                };
            }
            "--seat" => {
                let (i, policy) = value
                    .split_once('=')
                    .ok_or_else(|| format!("--seat wants I=POLICY, got {value}"))?;
                let i: usize = i.parse().map_err(|_| format!("bad seat {i}"))?;
                if i >= SEATS {
                    return Err(format!("seat {i} is outside 0..{SEATS}"));
                }
                let policy = match policy {
                    "human" => SeatPolicy::Human,
                    "brawler" => SeatPolicy::Brawler,
                    "raid" => SeatPolicy::Raid,
                    other => {
                        return Err(format!("unknown seat policy {other} (human|brawler|raid)"))
                    }
                };
                o.seats.push((i, policy));
            }
            other => return Err(format!("unknown flag {other}")),
        }
    }
    Ok(o)
}

/// The config and the eight seats of one seed. The unobserved run the purity oracle compares with is built from this.
pub fn match_setup(opts: &Options, seed: u64) -> Result<(EnvConfig, Vec<AgentSpec>), String> {
    let arena = match &opts.map {
        MapChoice::Generate => generated_arena(seed)?,
        MapChoice::File(path) => crate::load_arena(path)?,
    };
    let sim = SimConfig {
        min_players: SEATS,
        lobby_seconds: 5.,
        fill_seconds: 3.,
        ..SimConfig::live_content()
    };
    let agents = (0..SEATS)
        .map(|i| {
            let policy = opts
                .seats
                .iter()
                .rev()
                .find(|(seat, _)| *seat == i)
                .map_or(SeatPolicy::Human, |(_, p)| *p);
            let kind = match policy {
                SeatPolicy::Brawler => BotKind::Brawler,
                SeatPolicy::Human => BotKind::Human,
                SeatPolicy::Raid => BotKind::Raid,
            };
            AgentSpec {
                profile: format!("bot{i}"),
                faction: FactionId::ALL[(i + seed as usize) % 5],
                bot: !opts.human_seats,
                agent: Agent::Policy {
                    policy: kind.policy(&arena, &sim, SEATS, seed, i),
                },
            }
        })
        .collect();
    Ok((
        EnvConfig {
            arena,
            sim,
            max_ticks: opts.max_ticks,
            record: false,
        },
        agents,
    ))
}

fn generated_arena(seed: u64) -> Result<rh_sim::Arena, String> {
    use rh_sim::mapgen::{coliseum, pipeline};
    let layout = coliseum::fixed_layout();
    let generator = pipeline::Pipeline::new(&layout);
    let options = pipeline::Options {
        decor: false,
        ..Default::default()
    };
    let map = generator
        .certified_styled(seed as i64, options)
        .map_err(|e| format!("seed {seed}: map generation failed: {e:?}"))?
        .ok_or_else(|| format!("seed {seed}: the generator refused the seed"))?;
    let export = rh_sim::mapdata::MapExport {
        format: "rh-map/2".into(),
        legacy_map_hash_u32: u32::from_str_radix(&rh_sim::mapgen::map_hash::map_hash(&map), 16)
            .map_err(|e| e.to_string())?,
        map,
    };
    export
        .validate()
        .map_err(|e| e.to_string())?
        .into_arena()
        .map_err(|e| e.to_string())
}

/// What one observed seed ended as.
#[derive(Clone, Debug, PartialEq, Eq)]
pub struct SeedRun {
    pub seed: u64,
    pub ticks: u32,
    pub terminated: bool,
    pub truncated: bool,
    pub state_hash: u64,
}

/// One seed played: its record, its admitted map and what the observer counted.
struct Played {
    run: SeedRun,
    map_hash: String,
    acc: Acc,
}

fn play_full(opts: &Options, seed: u64) -> Result<Played, String> {
    let (cfg, agents) = match_setup(opts, seed)?;
    let mut env = Env::new(cfg, agents, seed);
    // The map the sim ended up with (it swaps one that cannot host the quest board), not the one passed in.
    let map_hash = env.sim.arena().map.layers().map_or_else(
        || "unlayered".to_string(),
        |l| format!("{:08x}", l.legacy_map_hash_u32()),
    );
    let mut observer = Observer::new(&env.sim);
    let mut ticks = 0;
    let (terminated, truncated) = loop {
        let p = env.advance(&[]);
        ticks += 1;
        observer.step(&env.sim, env.last_events(), !p.terminated);
        if p.terminated || p.truncated {
            break (p.terminated, p.truncated);
        }
    };
    Ok(Played {
        run: SeedRun {
            seed,
            ticks,
            terminated,
            truncated,
            state_hash: env.sim.state_hash(),
        },
        map_hash,
        acc: observer.finish(),
    })
}

/// Plays one seed with the observer attached.
pub fn play_seed(opts: &Options, seed: u64) -> Result<SeedRun, String> {
    play_full(opts, seed).map(|p| p.run)
}

pub struct Output {
    /// The report: one JSON object, compact.
    pub json: String,
    /// The same numbers as a table for stderr.
    pub table: String,
}

#[derive(Serialize)]
struct SeedRecord {
    seed: u64,
    ticks: u32,
    terminated: bool,
    truncated: bool,
    state_hash: String,
}

#[derive(Serialize)]
struct SeatRecord {
    seat: usize,
    policy: &'static str,
    bot: bool,
}

#[derive(Serialize)]
struct MapRecord {
    seed: u64,
    map_hash: String,
}

/// Declaration order is the key order of the report.
#[derive(Serialize)]
struct Report {
    schema: &'static str,
    content_hash: String,
    seeds: Vec<SeedRecord>,
    seating: Vec<SeatRecord>,
    maps: Vec<MapRecord>,
    world: World,
    elites: Elites,
    quests: Quests,
    rewards: Rewards,
    augments: Augments,
    seats: Vec<SeatReport>,
    notes: Vec<&'static str>,
}

const NOTES: [&str; 9] = [
    "A seat with bot:true has no quest view and cannot accept, interact or cast quest things (quest_view/mod.rs:203, quests/board/attempts.rs:217): without --human-seats the quests block is zero by construction.",
    "Kills of a bot:true seat are fill-bot kills (no GV, no damaged_humans entry); the lobby counts only bot:false seats.",
    "Camp kill/heal/walk seconds are heuristics: kill = the credited seat's first hit on the camp to the clear; heal = clear to 95% hp or the next hit taken; walk = the rest to the seat's next hit on any mob.",
    "Quests are read from Sim::quest_attempts(seat) after every step, for every bot:false seat (so completed is real; the seat's own journal drops a paid offer). attempts = completed + ended_timer + ended_death + abandoned + open + turned_in_unpaid; a retry of an offer is a new attempt, accepted counts offers once. complete_secs is the accept (first read of the row) to the first read of it turned in or paid, one sample a completion in `samples`.",
    "Quest end causes: a vanished row is ended_timer if its last deadline passed within the last two ticks (100 ms) and the gates were not locked, else ended_death if the seat died that step or the step before, else `abandoned` (a row gone later than that is booked death or `abandoned`, never timer), which therefore means ended otherwise (manual abandon, departure or unknown). Not measured: abandon versus departure, an abandon and retry inside one step, a death two or more steps before the row vanished, granted rewards (a turned-in attempt that is never paid is turned_in_unpaid, not a completion), what a seat chose not to do.",
    "Death cause: Death.by or the victim's latest Hit.by within the kill-credit window (5 s) resolved against remembered mob ids (elite/mob); otherwise hazard. world.player_deaths.hits_on_players_by_mob counts the hits that resolved to a mob.",
    "Elite fights (P-BAL-LAB-ELITE-1): a seat is engaged with an elite while it deals or takes a hit against the elite or a mob of its camp; an attempt opens on the first hit the seat deals to the elite itself and ends once, as killed (credited with the kill), taken_by_other (the elite died, by anyone else, while the seat was engaged), died (the seat died while engaged and the resolved attacker was the elite or a mob of its camp), died_elsewhere (the seat died while engaged to anything else), left (idle more than 10 s: a later death or kill is not this attempt's) or open (at the end). died and taken_by_other are narrower than before the change: a death after a give-up no longer counts. kill_secs is the seat's own engagement, first contact of the streak to the kill (0 when the first contact is the step the elite dies). A kill's chest is picked by the killer (the sim's credited killer, Death.by, who also holds the first pick), only by another seat, by nobody, or still open. A chest pick is one ChestTaken event and is counted once: a chest is named by seat, expiry and place, never by handle; sum(picks_by_kind) + picks_unnamed == chest_taken_events, where picks_unnamed is a skip, a pick on the last step or a chest that expired in the step of the pick. An elite's chest is recognised by its drop time and place (see README): a same-tick ground chest near the camp could be mistaken for it. Not measured: a pick on the last step, a hit with no attacker (area or status damage), the damage leader.",
    "Time (P-BAL-LAB-TIME-1): every seat-step (a seat's state after one match step, lobby included, alive or not) lands in exactly one bucket, first match wins: dead (the seat is not standing; its fight windows are cleared); elite, camp, player (the seat dealt or took a hit against a mob of an elite camp, a mob of a normal camp or with no camp, or another player, in the last 60 steps = 3 s counting the hit's own step; each kind has its own window and the highest open one wins: elite, then camp, then player); quest_moving, quest_still (an open quest attempt in the sim's probe, accepted and not turned in or completed, split by moved); moving (the seat's position changed by at least 1.0 unit since the last step); resting (standing still below full hp); idle (still at full hp). The 3 s window and the 1 u distance are choices, not measurements. world.time.buckets splits each bucket by minute band (tick / 1200: m0_5 ticks below 6000, m5_11 below 13200, m11_plus the rest) and seats has one row per seed and seat in seed then seat order (weapon is the last one in hand; farm_stacks are the stacks at the minute-5, 8 and 12 samples where the seat stood, else null; picks are named chest picks). Seat credits are the world's: the camp's best-damage seat (camps_normal, camps_elite), the elite's killing-blow seat (elite_kills), the lab's own death and quest-completion counts. A camp is elite when the observer has seen its elite alive; a camp whose elite nobody saw is counted as normal (camps_credited_normal, kill_secs_normal). kill_secs.n can be below camps_credited (a credited camp with no first hit adds no sample). world.elite_kills_credited is every elite death the sim credits to a seat (the seats' elite_kills summed); it minus the elites block's killed is the kills made by a seat with no open attempt (it only hit adds, it had gone idle for 10 s, or its attempt was cleared when it died). elite is time against a mob of an elite camp, the elite or its adds, including the adds after the elite is dead (the observer's set of elite camps is never pruned, which keeps the bucket consistent with camps_elite). dead includes a seat eliminated for good, until the match ends, so it dominates the late band of a full-length run; a living share is bucket / (steps - dead). resting is exactly 'did not move and hp below the maximum', with no tolerance: a seat at 99.9% health is resting; a seat waiting at a quest giver with no open attempt, standing at a chest, or frozen for any reason lands in resting or idle by its health. Not shown: a walk to a quest not yet accepted is moving, a flight is moving once the window passes, a hit with no attacker (area or status damage) is no fight, being knocked about is moving, a teleport counts as moved and a respawn does not, a Plague seat's zombie is a mob (a hit between it and another seat is that seat's camp-kind fight; a zombie's hit on a mob is nobody's).",
    "Observations are not sampled on the step that ends the match. Minute samples (world.minutes) carry their own sample counts; short matches have none at later minutes. Share reaching the coliseum is not measured.",
];

fn seat_policy(opts: &Options, i: usize) -> SeatPolicy {
    opts.seats
        .iter()
        .rev()
        .find(|(seat, _)| *seat == i)
        .map_or(SeatPolicy::Human, |(_, p)| *p)
}

/// Plays every seed of `opts.seeds` on `opts.jobs` threads and folds the records in seed order.
pub fn run(opts: &Options) -> Result<Output, String> {
    let seeds: Vec<u64> = opts.seeds.clone().collect();
    let slots: Vec<Mutex<Option<Result<Played, String>>>> =
        seeds.iter().map(|_| Mutex::new(None)).collect();
    let next = AtomicUsize::new(0);
    std::thread::scope(|scope| {
        for _ in 0..opts.jobs.min(seeds.len()).max(1) {
            scope.spawn(|| loop {
                let i = next.fetch_add(1, Ordering::SeqCst);
                let Some(&seed) = seeds.get(i) else { break };
                let result = catch_unwind(AssertUnwindSafe(|| play_full(opts, seed)))
                    .unwrap_or_else(|_| Err(format!("seed {seed}: the match panicked")));
                *slots[i].lock().unwrap_or_else(|e| e.into_inner()) = Some(result);
            });
        }
    });
    let mut report = Report {
        schema: SCHEMA,
        content_hash: format!("{:016x}", rh_sim::content::content_hash()),
        seeds: Vec::new(),
        seating: (0..SEATS)
            .map(|i| SeatRecord {
                seat: i,
                policy: match seat_policy(opts, i) {
                    SeatPolicy::Brawler => "brawler",
                    SeatPolicy::Human => "human",
                    SeatPolicy::Raid => "raid",
                },
                bot: !opts.human_seats,
            })
            .collect(),
        maps: Vec::new(),
        world: World::default(),
        elites: Elites::default(),
        quests: Quests::default(),
        rewards: Rewards::default(),
        augments: Augments::default(),
        seats: Vec::new(),
        notes: NOTES.to_vec(),
    };
    let mut all = Acc::default();
    for slot in slots {
        let played = slot
            .into_inner()
            .unwrap_or_else(|e| e.into_inner())
            .ok_or("a seed was never played")??;
        report.seeds.push(SeedRecord {
            seed: played.run.seed,
            ticks: played.run.ticks,
            terminated: played.run.terminated,
            truncated: played.run.truncated,
            state_hash: format!("{:016x}", played.run.state_hash),
        });
        report.maps.push(MapRecord {
            seed: played.run.seed,
            map_hash: played.map_hash,
        });
        let mut acc = played.acc;
        for s in &mut acc.seats {
            s.seed = played.run.seed;
        }
        all.fold_in(acc);
    }
    report.world = all.world;
    report.elites = all.elites;
    report.quests = all.quests;
    report.rewards = all.rewards;
    report.augments = all.augments;
    report.seats = all.seats;
    Ok(Output {
        json: serde_json::to_string(&report).map_err(|e| e.to_string())?,
        table: table::render(&report),
    })
}
