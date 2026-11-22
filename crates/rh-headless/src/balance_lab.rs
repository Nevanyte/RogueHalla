//! `balance_lab`: seeded 8-seat matches under `SimConfig::live_content()`, observed read-only, folded into one report
//! (schema `balance-lab/1`). The observer takes `&Sim`, events and ordinary observations; it never changes the sim.
//! Used by `src/bin/balance_lab.rs` and `tests/balance_lab.rs`; see `tools/balance-lab/README.md`.
use crate::{Agent, AgentSpec, BotKind, EnvConfig};
use rh_sim::{FactionId, SimConfig};
use std::ops::Range;

pub const SCHEMA: &str = "balance-lab/1";
/// Seats in a lab match.
pub const SEATS: usize = 8;

/// The policy of one seat (`--seat I=POLICY`).
#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub enum SeatPolicy {
    Brawler,
    Human,
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
pub fn parse_args(_args: &[String]) -> Result<Options, String> {
    Err("balance_lab::parse_args is not implemented".to_string())
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

/// Plays one seed with the observer attached.
pub fn play_seed(_opts: &Options, seed: u64) -> Result<SeedRun, String> {
    Ok(SeedRun {
        seed,
        ticks: 0,
        terminated: false,
        truncated: false,
        state_hash: 0,
    })
}

pub struct Output {
    /// The report: one JSON object, compact.
    pub json: String,
    /// The same numbers as a table for stderr.
    pub table: String,
}

/// Plays every seed of `opts.seeds` on `opts.jobs` threads and folds the records in seed order.
pub fn run(_opts: &Options) -> Result<Output, String> {
    Ok(Output {
        json: format!("{{\"schema\":\"{SCHEMA}\"}}"),
        table: String::new(),
    })
}
