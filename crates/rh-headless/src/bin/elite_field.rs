//! Read-only elite and normal camp timing from authoritative RaidBot field matches.
use rh_headless::balance_lab::{match_setup, MapChoice, Options, SeatPolicy, SEATS};
use rh_headless::Env;
use rh_sim::{results::GvReason, Event, PlayerId};
use serde::Serialize;
use std::{
    collections::{BTreeMap, BTreeSet},
    env, fs,
    path::PathBuf,
};

const MAX_TICKS: u32 = 72_000;
const IDLE_SECONDS: f64 = 10.;

#[derive(Clone, Copy)]
struct MobFact {
    camp: u32,
    elite: bool,
    owner: Option<PlayerId>,
}
#[derive(Serialize)]
struct Episode {
    seed: u64,
    camp: u32,
    camp_kind: String,
    elite_kind: Option<String>,
    start_s: f64,
    start_minute: u32,
    boss_contact_s: Option<f64>,
    boss_kill_s: Option<f64>,
    full_clear_s: Option<f64>,
    elapsed_s: f64,
    outcome: String,
    first_boss_to_kill_s: Option<f64>,
    start_to_clear_s: Option<f64>,
    hits: u32,
    landed_damage: f64,
    boss_hits: u32,
    participants: Vec<usize>,
    boss_killer: Option<usize>,
    participant_deaths: Vec<ParticipantDeath>,
}
#[derive(Serialize)]
struct ParticipantDeath {
    seat: usize,
    age_s: f64,
    by: Option<String>,
}
#[derive(Serialize)]
struct Payout {
    age_s: f64,
    seat: usize,
    amount: u32,
    reason: String,
}
#[derive(Serialize)]
struct ChestPick {
    age_s: f64,
    chest: u32,
    seat: usize,
    pick: i8,
}
#[derive(Serialize)]
struct MatchRow {
    seed: u64,
    ticks: u32,
    terminated: bool,
    truncated: bool,
    raid_ended: bool,
    elites: Vec<Episode>,
    normals: Vec<Episode>,
    payouts: Vec<Payout>,
    chest_picks: Vec<ChestPick>,
    active_minute_attempts: BTreeMap<String, u32>,
}
struct Open {
    row: usize,
    start: f64,
    last: f64,
    boss_contact: Option<f64>,
    boss_kill: Option<f64>,
    hits: u32,
    damage: f64,
    boss_hits: u32,
    participants: BTreeSet<usize>,
    boss_killer: Option<usize>,
    deaths: Vec<ParticipantDeath>,
}
fn owner_of(
    by: Option<PlayerId>,
    players: &BTreeMap<PlayerId, usize>,
    mobs: &BTreeMap<PlayerId, MobFact>,
) -> Option<usize> {
    by.and_then(|id| {
        players.get(&id).copied().or_else(|| {
            mobs.get(&id)
                .and_then(|m| m.owner)
                .and_then(|o| players.get(&o).copied())
        })
    })
}
fn finish(row: &mut Episode, open: Open, age: f64, outcome: &str) {
    row.elapsed_s = (age - open.start).max(0.);
    row.boss_contact_s = open.boss_contact;
    row.boss_kill_s = open.boss_kill;
    row.full_clear_s = if outcome == "full_clear" {
        Some(age)
    } else {
        None
    };
    row.first_boss_to_kill_s = open
        .boss_contact
        .zip(open.boss_kill)
        .map(|(a, b)| (b - a).max(0.));
    row.start_to_clear_s = row.full_clear_s.map(|t| (t - open.start).max(0.));
    row.hits = open.hits;
    row.landed_damage = open.damage;
    row.boss_hits = open.boss_hits;
    row.participants = open.participants.into_iter().collect();
    row.boss_killer = open.boss_killer;
    row.participant_deaths = open.deaths;
    row.outcome = outcome.into();
}
fn play(seed: u64) -> Result<MatchRow, String> {
    let opts = Options {
        seeds: seed..seed + 1,
        max_ticks: MAX_TICKS,
        map: MapChoice::Generate,
        seats: (0..SEATS).map(|s| (s, SeatPolicy::Raid)).collect(),
        human_seats: false,
        ..Options::default()
    };
    let (mut cfg, mut agents) = match_setup(&opts, seed)?;
    agents[0].bot = false;
    cfg.sim.tick_ms = 25;
    let mut env = Env::new(cfg, agents, seed);
    let players: BTreeMap<_, _> = env
        .sim
        .players()
        .iter()
        .enumerate()
        .map(|(i, p)| (p.core.id, i))
        .collect();
    let rosters: Vec<_> = env
        .sim
        .camp_rosters()
        .iter()
        .map(|r| {
            (
                format!("{:?}", r.mob_type),
                r.elite.map(|e| format!("{e:?}")),
            )
        })
        .collect();
    let mut elites = Vec::new();
    let mut normals = Vec::new();
    let mut open = BTreeMap::<u32, Open>::new();
    let mut payouts = Vec::new();
    let mut chest_picks = Vec::new();
    let mut attempt_counts = BTreeMap::new();
    let mut raid_ended = false;
    let (ticks, terminated, truncated) = loop {
        let before_mobs: BTreeMap<_, _> = env
            .sim
            .mobs()
            .iter()
            .map(|m| {
                (
                    m.core.id,
                    MobFact {
                        camp: m.camp,
                        elite: m.elite,
                        owner: m.owner,
                    },
                )
            })
            .collect();
        let p = env.advance(&[]);
        let now = env.sim.server_time();
        let age = (now - env.sim.raid_started_at()).max(0.) / 1000.;
        // Expire an abandoned pull before accepting any new damage this step.
        let stale: Vec<_> = open
            .iter()
            .filter_map(|(camp, o)| (age - o.last >= IDLE_SECONDS).then_some(*camp))
            .collect();
        for camp in stale {
            if let Some(o) = open.remove(&camp) {
                let row = if rosters[camp as usize].1.is_some() {
                    &mut elites[o.row]
                } else {
                    &mut normals[o.row]
                };
                finish(row, o, age, "abandoned_no_damage_10s");
            }
        }
        for h in env.sim.landed_hits() {
            if h.amount <= 0. {
                continue;
            }
            let Some(fact) = before_mobs.get(&h.victim).copied() else {
                continue;
            };
            if fact.owner.is_some() {
                continue;
            }
            let Some(seat) = owner_of(h.by, &players, &before_mobs) else {
                continue;
            };
            let roster = rosters
                .get(fact.camp as usize)
                .ok_or_else(|| format!("invalid camp {}", fact.camp))?;
            let is_elite = roster.1.is_some();
            let target = if is_elite { &mut elites } else { &mut normals };
            let idx = if let Some(o) = open.get_mut(&fact.camp) {
                o.last = age;
                o.hits += 1;
                o.damage += h.amount;
                o.participants.insert(seat);
                if fact.elite {
                    o.boss_contact.get_or_insert(age);
                    o.boss_hits += 1;
                }
                o.row
            } else {
                target.push(Episode {
                    seed,
                    camp: fact.camp,
                    camp_kind: roster.0.clone(),
                    elite_kind: roster.1.clone(),
                    start_s: age,
                    start_minute: (age / 60.) as u32,
                    boss_contact_s: fact.elite.then_some(0.),
                    boss_kill_s: None,
                    full_clear_s: None,
                    elapsed_s: 0.,
                    outcome: "open".into(),
                    first_boss_to_kill_s: None,
                    start_to_clear_s: None,
                    hits: 1,
                    landed_damage: h.amount,
                    boss_hits: u32::from(fact.elite),
                    participants: vec![seat],
                    boss_killer: None,
                    participant_deaths: Vec::new(),
                });
                let row = target.len() - 1;
                let mut participants = BTreeSet::new();
                participants.insert(seat);
                open.insert(
                    fact.camp,
                    Open {
                        row,
                        start: age,
                        last: age,
                        boss_contact: fact.elite.then_some(age),
                        boss_kill: None,
                        hits: 1,
                        damage: h.amount,
                        boss_hits: u32::from(fact.elite),
                        participants,
                        boss_killer: None,
                        deaths: Vec::new(),
                    },
                );
                *attempt_counts
                    .entry((is_elite, (age / 60.) as u32))
                    .or_insert(0) += 1;
                row
            };
            let _ = idx;
        }
        for e in env.last_events() {
            match e {
                Event::Death { id, by, .. } if !players.contains_key(id) => {
                    if let Some(f) = before_mobs.get(id) {
                        if f.elite {
                            if let Some(o) = open.get_mut(&f.camp) {
                                o.boss_kill = Some(age);
                                o.boss_killer = owner_of(*by, &players, &before_mobs);
                            }
                        }
                    }
                }
                Event::Death { id, by, .. } => {
                    if let Some(&seat) = players.get(id) {
                        for o in open.values_mut() {
                            if o.participants.contains(&seat) {
                                o.deaths.push(ParticipantDeath {
                                    seat,
                                    age_s: age,
                                    by: by.map(|x| format!("{:?}", x)),
                                });
                            }
                        }
                    }
                }
                Event::CampCleared { camp } => {
                    if let Some(o) = open.remove(camp) {
                        let elite = rosters.get(*camp as usize).is_some_and(|r| r.1.is_some());
                        let row = if elite {
                            &mut elites[o.row]
                        } else {
                            &mut normals[o.row]
                        };
                        finish(row, o, age, "full_clear");
                    }
                }
                Event::Gv { id, amount, reason } if matches!(reason, GvReason::Elite) => {
                    if let Some(&seat) = players.get(id) {
                        payouts.push(Payout {
                            age_s: age,
                            seat,
                            amount: *amount,
                            reason: format!("{reason:?}"),
                        });
                    }
                }
                Event::ChestTaken { chest, by, pick } => {
                    if let Some(&seat) = players.get(by) {
                        chest_picks.push(ChestPick {
                            age_s: age,
                            chest: *chest,
                            seat,
                            pick: *pick,
                        });
                    }
                }
                Event::RaidEnded => raid_ended = true,
                _ => {}
            }
        }
        if p.terminated || p.truncated {
            break (p.tick, p.terminated, p.truncated);
        }
    };
    let age = (env.sim.server_time() - env.sim.raid_started_at()).max(0.) / 1000.;
    for (camp, o) in open {
        let elite = rosters.get(camp as usize).is_some_and(|r| r.1.is_some());
        let row = if elite {
            &mut elites[o.row]
        } else {
            &mut normals[o.row]
        };
        let outcome = if truncated {
            "timeout_open"
        } else if o.boss_kill.is_some() {
            "boss_killed_camp_open"
        } else {
            "match_end_before_clear"
        };
        finish(row, o, age, outcome);
    }
    let active_minute_attempts = attempt_counts
        .into_iter()
        .map(|((elite, minute), n)| {
            (
                format!("{}:{minute}", if elite { "elite" } else { "normal" }),
                n,
            )
        })
        .collect();
    Ok(MatchRow {
        seed,
        ticks,
        terminated,
        truncated,
        raid_ended,
        elites,
        normals,
        payouts,
        chest_picks,
        active_minute_attempts,
    })
}
fn main() -> Result<(), String> {
    let mut seeds = 1..2;
    let mut out = PathBuf::from("-");
    let mut args = env::args().skip(1);
    while let Some(a) = args.next() {
        match a.as_str() {
            "--seeds" => {
                let v = args.next().ok_or("--seeds needs A..B")?;
                let (x, y) = v.split_once("..").ok_or("--seeds wants A..B")?;
                seeds = x.parse().map_err(|_| "bad seed start")?
                    ..y.parse().map_err(|_| "bad seed end")?;
            }
            "--out" => out = PathBuf::from(args.next().ok_or("--out needs path")?),
            "--help" => {
                println!("elite_field --seeds A..B --out PATH");
                return Ok(());
            }
            x => return Err(format!("unknown arg {x}")),
        }
    }
    let rows = seeds.map(play).collect::<Result<Vec<_>, _>>()?;
    let doc = serde_json::json!({"schema":"elite-field/1","seeds":rows,"method":{"tick_ms":25,"max_ticks":MAX_TICKS,"seating":"seat 0 bot:false human-eligible, seats 1..7 bot:true RaidBot","map":"balance_lab generated certified coliseum map, seed matched","clock":"raid age from Sim.server_time - Sim.raid_started_at; active minute=floor(raid_age/60)","attempt":"first positive settled landed_hits damage by a player or owned companion to a regional mob in each elite/normal camp; 10s no contact closes as abandonment; boss contact and kill are recorded separately; CampCleared closes full clear","limits":"payout events are credited Elite Gv; ChestTaken picks are raw and not associated with a specific elite chest; abandonment is an inactivity proxy, not confirmed retreat"}});
    let text = serde_json::to_string(&doc).map_err(|e| e.to_string())?;
    if out.as_os_str() == "-" {
        println!("{text}");
    } else {
        if let Some(p) = out.parent() {
            fs::create_dir_all(p).map_err(|e| e.to_string())?;
        }
        fs::write(out, text).map_err(|e| e.to_string())?;
    }
    Ok(())
}

#[cfg(test)]
mod tests {
    use super::*;
    #[test]
    fn boss_kill_and_full_clear_elapsed_values_are_distinct() {
        let open = Open {
            row: 0,
            start: 12.,
            last: 18.,
            boss_contact: Some(14.),
            boss_kill: Some(17.),
            hits: 4,
            damage: 100.,
            boss_hits: 2,
            participants: BTreeSet::from([0]),
            boss_killer: Some(0),
            deaths: vec![],
        };
        let mut row = Episode {
            seed: 1,
            camp: 0,
            camp_kind: "normal".into(),
            elite_kind: Some("boss".into()),
            start_s: 12.,
            start_minute: 0,
            boss_contact_s: None,
            boss_kill_s: None,
            full_clear_s: None,
            elapsed_s: 0.,
            outcome: "open".into(),
            first_boss_to_kill_s: None,
            start_to_clear_s: None,
            hits: 0,
            landed_damage: 0.,
            boss_hits: 0,
            participants: vec![],
            boss_killer: None,
            participant_deaths: vec![],
        };
        finish(&mut row, open, 20., "full_clear");
        assert_eq!(row.first_boss_to_kill_s, Some(3.));
        assert_eq!(row.start_to_clear_s, Some(8.));
        assert_eq!(row.elapsed_s, 8.);
        assert_eq!(row.outcome, "full_clear");
    }
}
