//! Elite mode of `camp_bench` (packet P-ELITE-BENCH-1): how often a minute-5 build dies soloing an elite camp under the
//! Rework rules, plus the sampler that produces those builds.
//!
//! This file is compiled twice: by `src/bin/camp_bench.rs` under `--features elite-bench` (which turns on rh-sim's
//! `trusted-build-setup`), and by `tests/elite_bench.rs` through `#[path]`, where rh-sim's dev-dependency features
//! provide the same API. It must not be added to `lib.rs` (rh-server depends on rh-headless).
use rh_headless::{load_arena, Agent, AgentSpec, Env, EnvConfig};
use rh_policy::scripted::Brawler;
use rh_policy::{MatchStart, Policy};
use rh_sim::cards::CardId;
use rh_sim::events::Event;
use rh_sim::mob_catalog::{elites_of, normal_types_of, MobTypeId};
use rh_sim::reward_chests::BoostId;
use rh_sim::trusted_build::{apply, TrustedBuild};
use rh_sim::{
    Arena, ArenaMode, FactionId, Placement, PlayerId, Secrets, Sim, SimConfig, Vec2, WeaponId,
};
use serde::{Deserialize, Serialize};
use std::collections::BTreeMap;
use std::ops::Range;
use std::sync::Arc;

/// Minute 5 at 20 Hz.
pub const MINUTE5_TICK: u32 = 6000;
/// The checked-in sample, relative to the repository root.
pub const SAMPLE_PATH: &str = "tools/balance-camps/minute5_builds.json";

/// One bot's build at the sample tick.
#[derive(Clone, Debug, PartialEq, Serialize, Deserialize)]
#[serde(deny_unknown_fields)]
pub struct BuildSample {
    pub seed: u64,
    pub bot: u32,
    /// `FactionId::as_str` of the bot's faction.
    pub faction: String,
    pub build: TrustedBuild,
}

/// The sample file (`SAMPLE_PATH`).
#[derive(Clone, Debug, PartialEq, Serialize, Deserialize)]
#[serde(deny_unknown_fields)]
pub struct SampleFile {
    pub version: u32,
    pub tick: u32,
    /// Map path as given to `source_soak`, relative to the repository root.
    pub map: String,
    /// Half-open range of match seeds tried.
    pub seeds: [u64; 2],
    /// Seeds whose match ended before `tick` (they contribute no builds).
    pub ended_early: Vec<u64>,
    /// In (seed, bot) order.
    pub builds: Vec<BuildSample>,
}

const ROOT: &str = concat!(env!("CARGO_MANIFEST_DIR"), "/../..");
/// The map the sampler and the bench use, relative to the repository root.
const MAP_REL: &str = "crates/rh-sim/src/mapdata/fixtures/seed1.map.json";
/// Start distance of the fighter from the camp (as `camp_bench`): 10 tiles, then shorter fallbacks.
const START_DIST: f64 = 320.;
const CAVEAT: &str = "The fighter is the scripted Brawler (walks to the nearest actor and swings from 80 u whatever \
the weapon), which may skew death rates: read them as 'Brawler with weapon W carrying a bot's minute-5 build'. Builds \
are what bots pick today (they misvalue reworked cards until P-AUG-BOTS-1) and come only from matches that reached \
minute 5 (survivorship). Until P-ELITE-TUNE-1 the table is a baseline.";

fn root_path(p: &str) -> std::path::PathBuf {
    std::path::Path::new(ROOT).join(p)
}

/// The live world with the overrides a lone fighter needs: no match lifecycle, no regen, no quests, one-player lobby.
fn config() -> SimConfig {
    SimConfig {
        lifecycle: ArenaMode::Off,
        regen: false,
        quests: false,
        min_players: 1,
        lobby_seconds: 5.,
        fill_seconds: 3.,
        ..SimConfig::live_content()
    }
}

/// Plays `source_soak`-style live matches (8 `HumanBot`s, `SimConfig::live_content()`) for each seed and reads every
/// bot's build at `tick`. Deterministic.
pub fn sample_builds(map: &str, seeds: Range<u64>, tick: u32) -> Result<SampleFile, String> {
    let arena = load_arena(map)?;
    let map_field = map
        .find("crates/rh-sim/")
        .map_or(map, |i| &map[i..])
        .to_string();
    let mut out = SampleFile {
        version: 1,
        tick,
        map: map_field,
        seeds: [seeds.start, seeds.end],
        ended_early: Vec::new(),
        builds: Vec::new(),
    };
    let mobs: Vec<MobTypeId> = FactionId::ALL
        .into_iter()
        .flat_map(|z| normal_types_of(z).map(|t| t.id))
        .collect();
    for seed in seeds {
        let cfg = SimConfig {
            min_players: 1,
            lobby_seconds: 5.,
            fill_seconds: 3.,
            ..SimConfig::live_content()
        };
        let agents = (0..8)
            .map(|i| AgentSpec {
                profile: format!("bot{i}"),
                faction: FactionId::ALL[(i + seed as usize) % 5],
                bot: true,
                agent: Agent::Policy {
                    policy: Box::new(rh_policy::bots::HumanBot::new(
                        rh_policy::bots::TierId::ALL[(i + seed as usize) % 3],
                        rh_policy::bots::PersonaId::ALL[i % 4],
                    )),
                },
            })
            .collect();
        let mut env = Env::new(
            EnvConfig {
                arena: arena.clone(),
                sim: cfg,
                max_ticks: tick.max(12_000),
                record: false,
            },
            agents,
            seed,
        );
        let mut ended = false;
        for t in 1..=tick {
            let p = env.advance(&[]);
            if (p.terminated || p.truncated) && t < tick {
                ended = true;
                break;
            }
        }
        if ended {
            out.ended_early.push(seed);
            continue;
        }
        for (bot, p) in env.sim.players().iter().enumerate() {
            if !p.standing() {
                continue;
            }
            let counts = p.build.counts();
            let boosts = p.build.boosts();
            out.builds.push(BuildSample {
                seed,
                bot: bot as u32,
                faction: p.faction().as_str().to_string(),
                build: TrustedBuild {
                    cards: CardId::ALL
                        .into_iter()
                        .filter(|c| counts[c.index()] > 0)
                        .map(|c| (c, counts[c.index()]))
                        .collect(),
                    stacks: mobs
                        .iter()
                        .filter(|&&m| p.farm.count(m) > 0)
                        .map(|&m| (m, p.farm.count(m)))
                        .collect(),
                    boosts: BoostId::ALL
                        .into_iter()
                        .filter(|b| boosts[b.index()] > 0)
                        .map(|b| (b, boosts[b.index()]))
                        .collect(),
                },
            });
        }
    }
    Ok(out)
}

/// What a run prints: the JSON report (stdout) and the readable table (stderr).
pub struct Output {
    pub json: String,
    pub table: String,
}

#[derive(Serialize)]
struct CellOut {
    faction: String,
    weapon: String,
    kind: String,
    zone: String,
    elite: bool,
    seed: u64,
    camp: u32,
    column: String,
    camps: Option<u32>,
    build_index: Option<usize>,
    build_applied_tick: u32,
    start_dist: f64,
    outcome: &'static str,
    ticks: u32,
    first_hit_tick: Option<u32>,
    hp_start: f64,
    hp_remaining: f64,
    damage_dealt: f64,
    damage_taken: f64,
}
#[derive(Serialize)]
struct SkipOut {
    faction: String,
    weapon: String,
    kind: String,
    seed: u64,
}
#[derive(Serialize)]
struct ColumnOut {
    column: String,
    camps: Option<u32>,
    samples: u64,
    deaths: u64,
    death_rate: Option<f64>,
    ci95: Option<[f64; 2]>,
}
#[derive(Serialize)]
struct PerCamp {
    kind: String,
    zone: String,
    samples: u64,
    deaths: u64,
    death_rate: Option<f64>,
    columns: Vec<ColumnOut>,
}
#[derive(Serialize)]
struct Overall {
    samples: u64,
    deaths: u64,
    death_rate: Option<f64>,
    columns: Vec<ColumnOut>,
}
#[derive(Serialize)]
struct Rules {
    elites: &'static str,
    augments: &'static str,
}
#[derive(Serialize)]
struct BuildsOut {
    source: String,
    tick: u32,
    count: usize,
}
#[derive(Serialize)]
struct Report {
    version: u32,
    mode: &'static str,
    quick: bool,
    max_ticks: u32,
    config: &'static str,
    rules: Rules,
    solo: bool,
    caveat: &'static str,
    reference: String,
    builds: BuildsOut,
    cells: Vec<CellOut>,
    skipped: Vec<SkipOut>,
    per_camp: Vec<PerCamp>,
    overall: Overall,
}

struct Scene {
    camp: u32,
    start: Vec2,
    dist: f64,
}

fn floor_line(arena: &Arena, a: Vec2, b: Vec2) -> bool {
    (0..=40).all(|i| {
        let t = i as f64 / 40.;
        let x = a.x + (b.x - a.x) * t;
        let y = a.y + (b.y - a.y) * t;
        arena
            .map
            .is_floor(rh_sim::map::world_to_tile(x), rh_sim::map::world_to_tile(y))
    })
}

/// The first camp rolling this elite, and a floor start point near it (as `camp_bench`).
fn scene_for(probe: &Sim, arena: &Arena, elite: MobTypeId) -> Option<Scene> {
    let rosters = probe.camp_rosters();
    let camp = rosters.iter().position(|r| r.elite == Some(elite))? as u32;
    let members: Vec<Vec2> = probe
        .mobs()
        .iter()
        .filter(|m| m.camp == camp && m.zombie.is_none())
        .map(|m| m.core.pos())
        .collect();
    if members.is_empty() {
        return None;
    }
    let n = members.len() as f64;
    let centre = Vec2::new(
        members.iter().map(|p| p.x).sum::<f64>() / n,
        members.iter().map(|p| p.y).sum::<f64>() / n,
    );
    for dist in [START_DIST, 256., 192., 128.] {
        for k in 0..16 {
            let a = k as f64 * std::f64::consts::TAU / 16.;
            let p = Vec2::new(centre.x + a.cos() * dist, centre.y + a.sin() * dist);
            if floor_line(arena, p, centre) {
                return Some(Scene {
                    camp,
                    start: p,
                    dist,
                });
            }
        }
    }
    None
}

struct Fought {
    outcome: &'static str,
    ticks: u32,
    first_hit: Option<u32>,
    hp_start: f64,
    hp_left: f64,
    dealt: f64,
    taken: f64,
}

fn fight(
    arena: &Arc<Arena>,
    seed: u64,
    faction: FactionId,
    weapon: WeaponId,
    scene: &Scene,
    build: Option<&TrustedBuild>,
    max_ticks: u32,
) -> Result<Fought, String> {
    let placement = [Placement {
        profile: "fighter".into(),
        faction,
        bot: false,
        at: scene.start,
        hp: None,
        weapon0: Some(weapon),
        weapon1: None,
    }];
    let mut sim = Sim::reset_active(
        config(),
        arena.clone(),
        Secrets::from_seed(seed),
        &placement,
    );
    let me: PlayerId = sim.players()[0].id;
    if let Some(b) = build {
        apply(&mut sim, me, b).map_err(|e| format!("a sampled build was refused: {e:?}"))?;
    }
    let hp_start = sim.players()[0].vitals.hp;
    let mut brawler = Brawler::new(1);
    brawler.reset(&MatchStart::of(&sim, me, seed));
    let (mut env, mut obs) = Env::from_external_checkpoint(sim, max_ticks).expect("checkpoint");
    let mut m = Fought {
        outcome: "truncated",
        ticks: max_ticks,
        first_hit: None,
        hp_start,
        hp_left: hp_start,
        dealt: 0.,
        taken: 0.,
    };
    let mut healed = 0.;
    for tick in 1..=max_ticks {
        let cmds = brawler.decide(&obs[0]);
        let out = env.step(&[cmds]);
        let mut tally = |victim: PlayerId, by: Option<PlayerId>, amount: f64| {
            if victim == me {
                m.taken += amount;
            } else if by == Some(me) {
                m.dealt += amount;
                m.first_hit.get_or_insert(tick);
            }
        };
        for e in &out.events {
            match e {
                Event::Hit {
                    victim, by, amount, ..
                }
                | Event::AbilityHit {
                    victim, by, amount, ..
                } => tally(*victim, *by, *amount),
                Event::SignatureHit {
                    victim, by, amount, ..
                } => tally(*victim, Some(*by), *amount),
                Event::CardBleedHit {
                    victim,
                    by,
                    amount: rh_sim::events::CardBleedAmount::Finite(a),
                } => tally(*victim, *by, *a),
                Event::Heal { id, amount } if *id == me => healed += amount,
                _ => {}
            }
        }
        obs = out.observations;
        m.ticks = tick;
        let hp = env.sim.player(me).map_or(0., |p| p.vitals.hp);
        m.hp_left = hp.max(0.);
        if hp <= 0. {
            m.outcome = "died";
            m.hp_left = 0.;
            break;
        }
        let alive = env.sim.camp_rosters()[scene.camp as usize].alive;
        let any = env.sim.mobs().iter().any(|b| {
            b.camp == scene.camp && b.zombie.is_none() && b.status == rh_sim::state::Status::Alive
        });
        if alive == 0 && !any {
            m.outcome = "cleared";
            break;
        }
    }
    m.taken = m.taken.max(m.hp_start - m.hp_left + healed);
    Ok(m)
}

fn rate(deaths: u64, samples: u64) -> Option<f64> {
    (samples > 0).then(|| deaths as f64 / samples as f64)
}

fn pct(deaths: u64, samples: u64) -> String {
    rate(deaths, samples).map_or("n/a".into(), |r| format!("{:.1}%", 100. * r))
}

/// The whole elite mode minus the process: `args` are the flags after the program name (`--quick`, `--seed N`,
/// `--seeds N`, `--max-ticks N`, `--builds PATH`; or `--sample-builds [--seeds A..B]`). `Ok` whenever
/// it reported, even if every fighter died or every fight was cut off; `Err` only for bad arguments or an unreadable
/// sample.
pub fn run(args: &[String]) -> Result<Output, String> {
    let flag = |n: &str| args.iter().any(|a| a == n);
    let value = |n: &str| -> Result<Option<&str>, String> {
        match args.iter().position(|a| a == n) {
            None => Ok(None),
            Some(i) => args
                .get(i + 1)
                .map(|v| Some(v.as_str()))
                .ok_or_else(|| format!("{n} needs a value")),
        }
    };
    if flag("--sample-builds") {
        let (a, b) = match value("--seeds")? {
            None => (1, 21),
            Some(v) => {
                let (a, b) = v.split_once("..").ok_or("--seeds A..B")?;
                (
                    a.parse::<u64>().map_err(|_| "bad --seeds")?,
                    b.parse::<u64>().map_err(|_| "bad --seeds")?,
                )
            }
        };
        if a >= b {
            return Err("--seeds A..B needs A < B".into());
        }
        let map = root_path(MAP_REL);
        let file = sample_builds(map.to_str().ok_or("map path")?, a..b, MINUTE5_TICK)?;
        let table = format!(
            "{} builds from seeds {a}..{b}; ended before minute 5: {:?}",
            file.builds.len(),
            file.ended_early
        );
        let json = serde_json::to_string_pretty(&file).map_err(|e| e.to_string())?;
        return Ok(Output { json, table });
    }
    let quick = flag("--quick");
    let num = |n: &str, d: u64| -> Result<u64, String> {
        value(n)?.map_or(Ok(d), |v| v.parse().map_err(|_| format!("bad {n}")))
    };
    let seed0 = num("--seed", 1)?;
    let seeds = num("--seeds", 1)?;
    let max_ticks = u32::try_from(num("--max-ticks", 1800)?).map_err(|_| "bad --max-ticks")?;
    let (source, path) = match value("--builds")? {
        Some(p) => (p.to_string(), root_path(p)),
        None => (SAMPLE_PATH.to_string(), root_path(SAMPLE_PATH)),
    };
    let sample: SampleFile = {
        let text = std::fs::read_to_string(&path)
            .map_err(|e| format!("cannot read builds {}: {e}", path.display()))?;
        serde_json::from_str(&text).map_err(|e| format!("cannot parse builds {source}: {e}"))?
    };
    if sample.builds.is_empty() {
        return Err("the build sample is empty".into());
    }

    let factions: Vec<FactionId> = FactionId::ALL
        .into_iter()
        .filter(|f| !quick || matches!(f, FactionId::Frost | FactionId::Holy))
        .collect();
    let weapons: Vec<WeaponId> = WeaponId::ALL
        .into_iter()
        .filter(|w| !quick || matches!(w, WeaponId::Sword | WeaponId::Wand))
        .collect();
    let mut kinds: Vec<(FactionId, MobTypeId)> = Vec::new();
    for zone in FactionId::ALL {
        if quick && zone != FactionId::ALL[0] {
            continue;
        }
        kinds.extend(elites_of(zone).map(|t| (zone, t.id)));
    }
    let map = root_path(MAP_REL);
    let arena = Arc::new(load_arena(map.to_str().ok_or("map path")?)?);
    // One probe world per seed picks each elite's camp and the banked camps; every fighter then fights the same camp
    // from the same point.
    let mut scenes: BTreeMap<u64, Vec<Option<Scene>>> = BTreeMap::new();
    let mut banked: BTreeMap<(u64, u32), TrustedBuild> = BTreeMap::new();
    for seed in seed0..seed0.saturating_add(seeds) {
        let probe = Sim::reset_active(
            config(),
            arena.clone(),
            Secrets::from_seed(seed),
            &[Placement {
                profile: "probe".into(),
                faction: FactionId::Frost,
                bot: false,
                at: arena.centre(),
                hp: None,
                weapon0: None,
                weapon1: None,
            }],
        );
        scenes.insert(
            seed,
            kinds
                .iter()
                .map(|&(_, id)| scene_for(&probe, &arena, id))
                .collect(),
        );
        for n in BANKED_CAMPS {
            banked.insert((seed, n), banked_camp_build(&probe, seed, n));
        }
    }

    // Columns: the banked ones, then the bot sample.
    let col_names: Vec<String> = BANKED_CAMPS
        .iter()
        .map(|n| format!("banked_{n}"))
        .chain(std::iter::once(BOT_COLUMN.to_string()))
        .collect();
    let ncol = col_names.len();
    let mut cells = Vec::new();
    let mut skipped = Vec::new();
    // tally[kind][column] = (samples, deaths)
    let mut tally: Vec<Vec<(u64, u64)>> = vec![vec![(0, 0); ncol]; kinds.len()];
    let mut ordinal = 0usize;
    for &f in &factions {
        for &w in &weapons {
            for (ki, &(zone, kind)) in kinds.iter().enumerate() {
                for seed in seed0..seed0.saturating_add(seeds) {
                    let ord = ordinal;
                    ordinal += 1;
                    let Some(scene) = &scenes[&seed][ki] else {
                        skipped.push(SkipOut {
                            faction: f.as_str().into(),
                            weapon: w.as_str().into(),
                            kind: kind.as_str().into(),
                            seed,
                        });
                        continue;
                    };
                    for (ci, name) in col_names.iter().enumerate() {
                        let (camps, idx, build) = if ci < BANKED_CAMPS.len() {
                            let n = BANKED_CAMPS[ci];
                            (Some(n), None, &banked[&(seed, n)])
                        } else {
                            let i = ord % sample.builds.len();
                            (None, Some(i), &sample.builds[i].build)
                        };
                        let m = fight(&arena, seed, f, w, scene, Some(build), max_ticks)?;
                        tally[ki][ci].0 += 1;
                        tally[ki][ci].1 += u64::from(m.outcome == "died");
                        cells.push(CellOut {
                            faction: f.as_str().into(),
                            weapon: w.as_str().into(),
                            kind: kind.as_str().into(),
                            zone: zone.as_str().into(),
                            elite: true,
                            seed,
                            camp: scene.camp,
                            column: name.clone(),
                            camps,
                            build_index: idx,
                            build_applied_tick: 0,
                            start_dist: scene.dist,
                            outcome: m.outcome,
                            ticks: m.ticks,
                            first_hit_tick: m.first_hit,
                            hp_start: m.hp_start,
                            hp_remaining: m.hp_left,
                            damage_dealt: m.dealt,
                            damage_taken: m.taken,
                        });
                    }
                }
            }
        }
    }
    let refc = BANKED_CAMPS
        .iter()
        .position(|&n| n == REFERENCE_CAMPS)
        .expect("the reference column is a banked column");
    let columns_of = |t: &[(u64, u64)]| -> Vec<ColumnOut> {
        col_names
            .iter()
            .zip(t)
            .enumerate()
            .map(|(ci, (name, &(n, d)))| ColumnOut {
                column: name.clone(),
                camps: BANKED_CAMPS.get(ci).copied(),
                samples: n,
                deaths: d,
                death_rate: rate(d, n),
                ci95: wilson(d, n),
            })
            .collect()
    };
    let per_camp: Vec<PerCamp> = kinds
        .iter()
        .zip(&tally)
        .map(|(&(zone, kind), t)| PerCamp {
            kind: kind.as_str().into(),
            zone: zone.as_str().into(),
            samples: t[refc].0,
            deaths: t[refc].1,
            death_rate: rate(t[refc].1, t[refc].0),
            columns: columns_of(t),
        })
        .collect();
    let all: Vec<(u64, u64)> = (0..ncol)
        .map(|ci| {
            tally
                .iter()
                .fold((0, 0), |a, t| (a.0 + t[ci].0, a.1 + t[ci].1))
        })
        .collect();
    let (n, d) = all[refc];

    let mut table = format!("elite camps, solo fighter. {CAVEAT}\n");
    table += &format!("  {:<24} {:<8} {:>13}", "camp", "zone", "samples");
    for c in &col_names {
        table += &format!(" {c:>11}");
    }
    table += "\n";
    let row = |label: &str, zone: &str, n: u64, cols: &[ColumnOut]| {
        let mut l = format!("  {label:<24} {zone:<8} {n:>13}");
        for c in cols {
            l += &format!(" {:>11}", pct(c.deaths, c.samples));
        }
        l + "\n"
    };
    for p in &per_camp {
        table += &row(&p.kind, &p.zone, p.samples, &p.columns);
    }
    let overall_cols = columns_of(&all);
    table += &row("overall", "", n, &overall_cols);
    table += "  reference column: banked_12 (12 camps banked, 12 stacks a camp: the workbook player); bots bank far fewer camps than the workbook player, so bot_sample is the bots' reality and the two are not comparable\n";
    table +=
        "  owner target: an average minute-5 build dies 10-20% of the time soloing an elite camp";
    let report = Report {
        version: 1,
        mode: "elite",
        quick,
        max_ticks,
        config: "live_content",
        rules: Rules {
            elites: "rework",
            augments: "rework",
        },
        solo: true,
        caveat: CAVEAT,
        reference: format!("banked_{REFERENCE_CAMPS}"),
        builds: BuildsOut {
            source,
            tick: sample.tick,
            count: sample.builds.len(),
        },
        cells,
        skipped,
        per_camp,
        overall: Overall {
            samples: n,
            deaths: d,
            death_rate: rate(d, n),
            columns: overall_cols,
        },
    };
    Ok(Output {
        json: serde_json::to_string(&report).map_err(|e| e.to_string())?,
        table,
    })
}

/// Wilson 95% interval of a rate (`None` without samples).
fn wilson(deaths: u64, samples: u64) -> Option<[f64; 2]> {
    if samples == 0 {
        return None;
    }
    let (n, p, z) = (
        samples as f64,
        deaths as f64 / samples as f64,
        1.959_963_984_540_054,
    );
    let denom = 1. + z * z / n;
    let centre = (p + z * z / (2. * n)) / denom;
    let half = z * (p * (1. - p) / n + z * z / (4. * n * n)).sqrt() / denom;
    Some([(centre - half).max(0.), (centre + half).min(1.)])
}

/// Banked-camp model (coordinator 2026-11-19, revising plan decision 8): workbook `Augment_Value`, assumption `banked`
/// = 12 camps already banked at minute 5, one camp = 12 stacks.
pub const CAMP_STACKS: u64 = 12;
/// The banked-camp columns of the report, in order; the bot sample is one more column after them.
pub const BANKED_CAMPS: [u32; 4] = [0, 6, 12, 18];
/// The column `per_camp` and `overall` report as their own rate.
pub const REFERENCE_CAMPS: u32 = 12;
/// Column name of the bot minute-5 sample (the banked ones are `banked_<N>`).
pub const BOT_COLUMN: &str = "bot_sample";

/// SplitMix64: the bench's own seeded draw (the sim's RNG is not touched).
fn splitmix(state: &mut u64) -> u64 {
    *state = state.wrapping_add(0x9E37_79B9_7F4A_7C15);
    let mut z = *state;
    z = (z ^ (z >> 30)).wrapping_mul(0xBF58_476D_1CE4_E5B9);
    z = (z ^ (z >> 27)).wrapping_mul(0x94D0_49BB_1331_11EB);
    z ^ (z >> 31)
}

/// A build of `camps` normal camps banked: draw `camps` non-elite camps of `probe`'s world (with replacement, seeded by
/// `seed`) and add `CAMP_STACKS` stacks of the stat each drawn camp's kills pay (`camp_buffs()[i]` when set, else the
/// camp's own `mob_type`); stacks of one stat merge into one entry. No cards, no boosts. Pure in (`probe`, `seed`,
/// `camps`).
pub fn banked_camp_build(probe: &Sim, seed: u64, camps: u32) -> TrustedBuild {
    let buffs = probe.camp_buffs();
    let pays: Vec<MobTypeId> = probe
        .camp_rosters()
        .iter()
        .enumerate()
        .filter(|(_, r)| r.elite.is_none())
        .map(|(i, r)| buffs.get(i).copied().flatten().unwrap_or(r.mob_type))
        .collect();
    let mut stacks: Vec<(MobTypeId, u64)> = Vec::new();
    if pays.is_empty() {
        return TrustedBuild::default();
    }
    let mut state = seed ^ 0xBA4C_ED00_CA3D_5EED;
    for _ in 0..camps {
        let stat = pays[(splitmix(&mut state) % pays.len() as u64) as usize];
        match stacks.iter_mut().find(|(m, _)| *m == stat) {
            Some(e) => e.1 += CAMP_STACKS,
            None => stacks.push((stat, CAMP_STACKS)),
        }
    }
    stacks.sort_by_key(|(m, _)| m.as_str());
    TrustedBuild {
        cards: Vec::new(),
        stacks,
        boosts: Vec::new(),
    }
}
