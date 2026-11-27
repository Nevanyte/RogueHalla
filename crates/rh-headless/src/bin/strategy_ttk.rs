//! Field TTK sampler: authoritative RaidBot matches, observer-only, no synthetic duels.
use rh_headless::balance_lab::{match_setup, MapChoice, Options, SeatPolicy, SEATS};
use rh_headless::Env;
use rh_sim::{Event, Phase, PlayerId};
use serde::Serialize;
use std::{
    collections::{BTreeMap, BTreeSet},
    fs,
    path::PathBuf,
};

#[derive(Serialize)]
struct Episode {
    seed: u64,
    band: String,
    kind: String,
    subject: String,
    start_s: f64,
    elapsed_s: f64,
    outcome: String,
    hits: u32,
    attributed_damage: f64,
    duel: bool,
    third_party: bool,
    environment: bool,
    #[serde(skip_serializing_if = "Option::is_none")]
    start_hp_fraction: Option<f64>,
    #[serde(skip_serializing_if = "Option::is_none")]
    gates_locked_start: Option<bool>,
    #[serde(skip_serializing_if = "Option::is_none")]
    prior_pull: Option<bool>,
}
#[derive(Serialize)]
struct ResolvedRecord {
    seat: Option<usize>,
    bot: bool,
    outcome: String,
    duration_s: u32,
    kills: u32,
    killed_by: Option<String>,
    killed_by_bot: bool,
    place: u32,
    players: u32,
    gates_locked: bool,
}
#[derive(Serialize)]
struct MatchRecord {
    seed: u64,
    ticks: u32,
    terminated: bool,
    truncated: bool,
    raid_ended_event: bool,
    resolved: Vec<ResolvedRecord>,
}
#[derive(Default)]
struct Window {
    start: f64,
    last: f64,
    hits: u32,
    damage: f64,
    attackers: BTreeSet<PlayerId>,
    env: bool,
    start_hp_fraction: Option<f64>,
    gates_locked_start: bool,
    prior_pull: bool,
}
fn pvp_flags(w: &Window) -> (bool, bool) {
    (w.attackers.len() == 1, w.attackers.len() > 1)
}
fn death_close(
    w: &Window,
    victim: PlayerId,
    final_owner: Option<PlayerId>,
    age: f64,
) -> (&'static str, f64) {
    let elapsed = age - w.start;
    if let Some(owner) = final_owner.filter(|owner| *owner != victim) {
        let (_, third_party) = pvp_flags(w);
        let mixed = third_party || w.env || !w.attackers.contains(&owner);
        (if mixed { "death_mixed" } else { "death_clean" }, elapsed)
    } else if final_owner.is_some() {
        ("competing_self_final_blow", elapsed)
    } else {
        ("competing_non_player_final_blow", elapsed)
    }
}
fn hostile_owner(victim: PlayerId, owner: Option<PlayerId>) -> Option<PlayerId> {
    owner.filter(|owner| *owner != victim)
}
fn camp_damage_is_counted(elite: bool, owner: Option<PlayerId>, amount: f64) -> bool {
    !elite && owner.is_some() && amount > 0.
}
struct HitContext<'a> {
    mob_facts: &'a BTreeMap<PlayerId, MobFact>,
    partial_pending: &'a mut BTreeSet<u32>,
    camp_total: &'a BTreeMap<u32, u32>,
    hp_before: &'a BTreeMap<PlayerId, (f64, f64)>,
    camp_alive_before: &'a BTreeMap<u32, u32>,
    gates_locked_before: bool,
}
#[derive(Clone, Copy)]
struct MobFact {
    camp: u32,
    owner: Option<PlayerId>,
    elite_camp: bool,
}
#[derive(Clone, Copy)]
struct LandedFact {
    victim: PlayerId,
    by: Option<PlayerId>,
    amount: f64,
    age: f64,
}
fn record_hit(
    sim: &rh_sim::Sim,
    hit: LandedFact,
    pv: &mut BTreeMap<PlayerId, Window>,
    camps: &mut BTreeMap<u32, Window>,
    context: &mut HitContext<'_>,
) {
    let owner = hit.by.and_then(|a| {
        sim.player(a)
            .map(|_| a)
            .or_else(|| context.mob_facts.get(&a).and_then(|fact| fact.owner))
    });
    if sim.player(hit.victim).is_some() {
        if let Some(attacker) = hostile_owner(hit.victim, owner).filter(|_| hit.amount > 0.) {
            let health =
                context
                    .hp_before
                    .get(&hit.victim)
                    .map(|(hp, max)| if *max > 0. { hp / max } else { 0. });
            let w = pv.entry(hit.victim).or_insert(Window {
                start: hit.age,
                last: hit.age,
                start_hp_fraction: health,
                gates_locked_start: context.gates_locked_before,
                ..Window::default()
            });
            w.last = hit.age;
            w.hits += 1;
            w.damage += hit.amount;
            w.attackers.insert(attacker);
        } else if let Some(w) = pv.get_mut(&hit.victim) {
            w.env = true;
        }
    } else if let Some(fact) = sim
        .mobs()
        .iter()
        .find(|m| m.core.id == hit.victim)
        .map(|m| MobFact {
            camp: m.camp,
            owner: m.owner,
            elite_camp: sim
                .camp_rosters()
                .get(m.camp as usize)
                .is_some_and(|r| r.elite.is_some()),
        })
        .or_else(|| context.mob_facts.get(&hit.victim).copied())
    {
        if fact.owner.is_none() && camp_damage_is_counted(fact.elite_camp, owner, hit.amount) {
            let prior_pull = if camps.contains_key(&fact.camp) {
                false
            } else {
                let alive = context
                    .camp_alive_before
                    .get(&fact.camp)
                    .copied()
                    .unwrap_or(0);
                let partial = alive > 0
                    && alive < context.camp_total.get(&fact.camp).copied().unwrap_or(alive);
                partial && context.partial_pending.remove(&fact.camp)
            };
            let w = camps.entry(fact.camp).or_insert(Window {
                start: hit.age,
                last: hit.age,
                gates_locked_start: context.gates_locked_before,
                prior_pull,
                ..Window::default()
            });
            w.last = hit.age;
            w.hits += 1;
            w.damage += hit.amount;
        }
    }
}
fn band(t: f64) -> String {
    match t as u32 {
        0..=299 => "0-5",
        300..=599 => "5-10",
        600..=899 => "10-15",
        900..=1199 => "15-20",
        _ => "20+",
    }
    .into()
}
fn quant(v: &mut [f64]) -> (usize, Option<f64>, Option<f64>, Option<f64>) {
    v.sort_by(f64::total_cmp);
    let q = |p: f64| {
        v.get(((v.len().saturating_sub(1)) as f64 * p).round() as usize)
            .copied()
    };
    (v.len(), q(0.25), q(0.5), q(0.75))
}
fn main() {
    if let Err(e) = run() {
        eprintln!("strategy_ttk: {e}");
        std::process::exit(1)
    }
}
fn run() -> Result<(), String> {
    let args: Vec<String> = std::env::args().skip(1).collect();
    let (mut lo, mut hi) = (1u64, 2u64);
    let mut out = PathBuf::from(".agent-runs/strategy-ttk");
    let mut i = 0;
    while i < args.len() {
        let f = &args[i];
        let v = args.get(i + 1).ok_or("missing value")?;
        match f.as_str() {
            "--seeds" => {
                let (a, b) = v.split_once("..").ok_or("--seeds wants A..B")?;
                lo = a.parse().map_err(|_| "bad seed")?;
                hi = b.parse().map_err(|_| "bad seed")?
            }
            "--out" => out = v.into(),
            _ => return Err(format!("unknown flag {f}")),
        }
        i += 2
    }
    if hi <= lo {
        return Err("empty seed range".into());
    }
    fs::create_dir_all(&out).map_err(|e| e.to_string())?;
    let mut rows = Vec::new();
    let mut matches = Vec::new();
    let mut landed_audit = BTreeMap::<String, u64>::new();
    for seed in lo..hi {
        let opts = Options {
            seeds: seed..seed + 1,
            max_ticks: 72_000,
            map: MapChoice::Generate,
            seats: (0..SEATS).map(|s| (s, SeatPolicy::Raid)).collect(),
            human_seats: false,
            ..Options::default()
        };
        let (mut cfg, mut agents) = match_setup(&opts, seed)?;
        agents[0].bot = false;
        cfg.sim.tick_ms = 25;
        let mut env = Env::new(cfg, agents, seed);
        let mut camps = BTreeMap::<u32, Window>::new();
        let mut pv = BTreeMap::<PlayerId, Window>::new();
        let mut partial_pending = BTreeSet::<u32>::new();
        let mut camp_total = BTreeMap::<u32, u32>::new();
        for m in env.sim.mobs() {
            if m.owner.is_none()
                && env
                    .sim
                    .camp_rosters()
                    .get(m.camp as usize)
                    .is_some_and(|r| r.elite.is_none())
            {
                *camp_total.entry(m.camp).or_default() += 1;
            }
        }
        let mut mob_facts = BTreeMap::<PlayerId, MobFact>::new();
        let mut resolved = Vec::new();
        let mut raid_ended_event = false;
        let (terminal_age, ticks, terminated, truncated) = loop {
            let gates_locked_before = env.sim.gates_locked();
            let hp_before = env
                .sim
                .players()
                .iter()
                .map(|p| (p.core.id, (p.vitals.hp, p.vitals.max_hp)))
                .collect::<BTreeMap<_, _>>();
            let camp_alive_before = env
                .sim
                .camp_rosters()
                .iter()
                .enumerate()
                .map(|(i, r)| (i as u32, r.alive))
                .collect::<BTreeMap<_, _>>();
            for m in env.sim.mobs() {
                let elite_camp = env
                    .sim
                    .camp_rosters()
                    .get(m.camp as usize)
                    .is_some_and(|r| r.elite.is_some());
                mob_facts.insert(
                    m.core.id,
                    MobFact {
                        camp: m.camp,
                        owner: m.owner,
                        elite_camp,
                    },
                );
            }
            let p = env.advance(&[]);
            let now = env.sim.server_time();
            let age = (now - env.sim.raid_started_at()).max(0.) / 1000.;
            let idle: Vec<_> = pv
                .iter()
                .filter_map(|(id, w)| (age - w.last >= 10.).then_some(*id))
                .collect();
            for id in idle {
                if let Some(w) = pv.remove(&id) {
                    let (duel, third_party) = pvp_flags(&w);
                    rows.push(Episode {
                        seed,
                        band: band(w.start),
                        kind: "pvp".into(),
                        subject: format!("{:?}", id),
                        start_s: w.start,
                        elapsed_s: w.last - w.start,
                        outcome: "interrupted_after_10s_idle".into(),
                        hits: w.hits,
                        attributed_damage: w.damage,
                        duel,
                        third_party,
                        environment: w.env,
                        start_hp_fraction: w.start_hp_fraction,
                        gates_locked_start: Some(w.gates_locked_start),
                        prior_pull: None,
                    });
                }
            }
            let abandoned: Vec<_> = camps
                .iter()
                .filter_map(|(camp, w)| (age - w.last >= 30.).then_some(*camp))
                .collect();
            for camp in abandoned {
                if env.sim.camp_rosters().get(camp as usize).is_some_and(|r| {
                    r.alive > 0 && r.alive < camp_total.get(&camp).copied().unwrap_or(r.alive)
                }) {
                    partial_pending.insert(camp);
                }
                if let Some(w) = camps.remove(&camp) {
                    rows.push(Episode {
                        seed,
                        band: band(w.start),
                        kind: "camp".into(),
                        subject: camp.to_string(),
                        start_s: w.start,
                        elapsed_s: w.last - w.start,
                        outcome: "abandoned_after_30s_idle".into(),
                        hits: w.hits,
                        attributed_damage: w.damage,
                        duel: false,
                        third_party: false,
                        environment: false,
                        start_hp_fraction: None,
                        gates_locked_start: Some(w.gates_locked_start),
                        prior_pull: Some(w.prior_pull),
                    });
                }
            }
            let has_damage_event = env.last_events().iter().any(|e| {
                matches!(
                    e,
                    Event::Hit { .. }
                        | Event::SignatureHit { .. }
                        | Event::AbilityHit { .. }
                        | Event::CardBleedHit { .. }
                )
            });
            if env.sim.phase() == Phase::Active || has_damage_event {
                for h in env.sim.landed_hits() {
                    let source = if h.dot {
                        "dot"
                    } else if h.sig.is_some() {
                        "signature"
                    } else if h.ability.is_some() {
                        "ability"
                    } else {
                        "weapon_or_other"
                    };
                    *landed_audit.entry(source.into()).or_default() += 1;
                    let mut context = HitContext {
                        mob_facts: &mob_facts,
                        partial_pending: &mut partial_pending,
                        camp_total: &camp_total,
                        hp_before: &hp_before,
                        camp_alive_before: &camp_alive_before,
                        gates_locked_before,
                    };
                    record_hit(
                        &env.sim,
                        LandedFact {
                            victim: h.victim,
                            by: h.by,
                            amount: h.amount,
                            age,
                        },
                        &mut pv,
                        &mut camps,
                        &mut context,
                    );
                }
            }
            for e in env.last_events() {
                match e {
                    Event::CampCleared { camp } => {
                        if env
                            .sim
                            .camp_rosters()
                            .get(*camp as usize)
                            .is_some_and(|c| c.elite.is_none())
                        {
                            if let Some(w) = camps.remove(camp) {
                                rows.push(Episode {
                                    seed,
                                    band: band(w.start),
                                    kind: "camp".into(),
                                    subject: camp.to_string(),
                                    start_s: w.start,
                                    elapsed_s: age - w.start,
                                    outcome: "camp_cleared".into(),
                                    hits: w.hits,
                                    attributed_damage: w.damage,
                                    duel: false,
                                    third_party: false,
                                    environment: false,
                                    start_hp_fraction: None,
                                    gates_locked_start: Some(w.gates_locked_start),
                                    prior_pull: Some(w.prior_pull),
                                })
                            }
                        }
                    }
                    Event::Death { id, by, .. } => {
                        if env.sim.player(*id).is_some() {
                            let final_owner = by.and_then(|a| {
                                env.sim.player(a).map(|_| a).or_else(|| {
                                    env.sim
                                        .mobs()
                                        .iter()
                                        .find(|m| m.core.id == a)
                                        .and_then(|m| m.owner)
                                        .or_else(|| mob_facts.get(&a).and_then(|fact| fact.owner))
                                })
                            });
                            if let Some(w) = pv.remove(id) {
                                let (duel, third_party) = pvp_flags(&w);
                                let (outcome, elapsed_s) = death_close(&w, *id, final_owner, age);
                                rows.push(Episode {
                                    seed,
                                    band: band(w.start),
                                    kind: "pvp".into(),
                                    subject: format!("{:?}", id),
                                    start_s: w.start,
                                    elapsed_s,
                                    outcome: outcome.into(),
                                    hits: w.hits,
                                    attributed_damage: w.damage,
                                    duel,
                                    third_party,
                                    environment: w.env,
                                    start_hp_fraction: w.start_hp_fraction,
                                    gates_locked_start: Some(w.gates_locked_start),
                                    prior_pull: None,
                                })
                            } else {
                                rows.push(Episode {
                                    seed,
                                    band: band(age),
                                    kind: "pvp".into(),
                                    subject: format!("{:?}", id),
                                    start_s: age,
                                    elapsed_s: 0.,
                                    outcome: match final_owner {
                                        Some(owner) if owner != *id => {
                                            "death_no_contact_player_final_blow"
                                        }
                                        Some(_) => "death_no_contact_self_final_blow",
                                        None => "death_no_contact_missing_or_non_player_killer",
                                    }
                                    .into(),
                                    hits: 0,
                                    attributed_damage: 0.,
                                    duel: false,
                                    third_party: false,
                                    environment: true,
                                    start_hp_fraction: None,
                                    gates_locked_start: None,
                                    prior_pull: None,
                                });
                            }
                        }
                    }
                    Event::Resolved { id, result, bot } => resolved.push(ResolvedRecord {
                        seat: env.sim.players().iter().position(|p| p.core.id == *id),
                        bot: *bot,
                        outcome: format!("{:?}", result.outcome),
                        duration_s: result.duration_sec,
                        kills: result.kills,
                        killed_by: result.killed_by.clone(),
                        killed_by_bot: result.killed_by_bot,
                        place: result.place,
                        players: result.players,
                        gates_locked: env.sim.gates_locked(),
                    }),
                    Event::RaidEnded => raid_ended_event = true,
                    _ => {}
                }
            }
            if p.terminated || p.truncated {
                break (age, p.tick, p.terminated, p.truncated);
            }
        };
        matches.push(MatchRecord {
            seed,
            ticks,
            terminated,
            truncated,
            raid_ended_event,
            resolved,
        });
        for (id, w) in pv {
            let (duel, third_party) = pvp_flags(&w);
            rows.push(Episode {
                seed,
                band: band(w.start),
                kind: "pvp".into(),
                subject: format!("{:?}", id),
                start_s: w.start,
                elapsed_s: terminal_age - w.start,
                outcome: "open_or_interrupted".into(),
                hits: w.hits,
                attributed_damage: w.damage,
                duel,
                third_party,
                environment: w.env,
                start_hp_fraction: w.start_hp_fraction,
                gates_locked_start: Some(w.gates_locked_start),
                prior_pull: None,
            })
        }
        for (c, w) in camps {
            rows.push(Episode {
                seed,
                band: band(w.start),
                kind: "camp".into(),
                subject: c.to_string(),
                start_s: w.start,
                elapsed_s: terminal_age - w.start,
                outcome: "open_or_interrupted".into(),
                hits: w.hits,
                attributed_damage: w.damage,
                duel: false,
                third_party: false,
                environment: false,
                start_hp_fraction: None,
                gates_locked_start: Some(w.gates_locked_start),
                prior_pull: Some(w.prior_pull),
            })
        }
    }
    let mut distributions = BTreeMap::<(String, String), Vec<f64>>::new();
    let mut seedsets = BTreeMap::<(String, String), BTreeSet<u64>>::new();
    let mut complete_seedsets = BTreeMap::<(String, String), BTreeSet<u64>>::new();
    let mut outcomes = BTreeMap::<(String, String, String), u64>::new();
    for r in &rows {
        *outcomes
            .entry((r.band.clone(), r.kind.clone(), r.outcome.clone()))
            .or_default() += 1;
        seedsets
            .entry((r.band.clone(), r.kind.clone()))
            .or_default()
            .insert(r.seed);
        if (r.kind == "camp" && r.outcome == "camp_cleared")
            || (r.kind == "pvp" && (r.outcome == "death_clean" || r.outcome == "death_mixed"))
        {
            complete_seedsets
                .entry((r.band.clone(), r.kind.clone()))
                .or_default()
                .insert(r.seed);
            distributions
                .entry((r.band.clone(), r.kind.clone()))
                .or_default()
                .push(r.elapsed_s)
        }
    }
    let mut summary = Vec::new();
    for b in ["0-5", "5-10", "10-15", "15-20", "20+"] {
        for k in ["camp", "pvp"] {
            let mut values = distributions
                .remove(&(b.into(), k.into()))
                .unwrap_or_default();
            let (n, p25, median, p75) = quant(&mut values);
            summary.push(serde_json::json!({"band":b,"kind":k,"completed_n":n,
                "seed_n":complete_seedsets.get(&(b.into(),k.into())).map_or(0,|s|s.len()),
                "contact_seed_n":seedsets.get(&(b.into(),k.into())).map_or(0,|s|s.len()),
                "p25_s":p25,"median_s":median,"p75_s":p75}));
        }
    }
    let counts = outcomes
        .into_iter()
        .map(|((b, k, o), n)| serde_json::json!({"band":b,"kind":k,"outcome":o,"n":n}))
        .collect::<Vec<_>>();
    let doc = serde_json::json!({"schema":"strategy-field-ttk/1","command":format!("cargo run --release -p rh-headless --bin strategy_ttk -- --seeds {lo}..{hi}"),"simulated_seeds":(lo..hi).collect::<Vec<_>>() ,"tick_ms":25,"seats":8,"seat_policy":"RaidBot","focal_seat":"seat 0 bot:false (human-eligible); seats 1..7 bot:true RaidBot fill seats","map":"certified generated coliseum map","lobby_config":"balance_lab match setup: min_players=8, lobby=5s, fill=3s; hosted Source Arena defaults differ (min=2,lobby=30s,fill=5s); raid_started_at resets the clock, so lobby delay is excluded","clock":"raid age = max(0, Sim.server_time - Sim.raid_started_at); no wall clock used","damage_feed":"Sim::landed_hits trusted scratch read once in Phase::Active per step; positive settled per-entry records uniquely cover weapon/signature/ability/card dots and exclude arena damage; terminal CampCleared and Death from last_events","definitions":{"camp":"positive settled player/owned-companion damage to an ownerless normal camp member starts window; CampCleared closes it; 30s without attributed damage is abandoned; match-end windows are right-censored","pvp":"positive settled damage from another player owner, directly or via owned companion, opens/extends victim window; one row per Death; only Death.by resolved to another player owner is a player-final-blow sample; a 10s hostile-contact gap censors; clean has one attacker, no environmental hits, and finisher matches; mixed includes multiple attackers, environmental damage or finisher mismatch","duel":"one distinct hostile player owner attributed during window","third_party":"more than one distinct hostile player owner attributed during window","environment":"non-player-owned settled damage during an open PvP window marks a mixed outcome but cannot start a PvP window"},"event_audit":landed_audit,"quantiles":"nearest-rank index round((n-1)*p), whole-camp clears and player-final-blow clean+mixed only","episodes":rows,"summary":summary,"outcomes":counts,"match_results":matches,"result_note":"Resolved.killed_by is credited top killer and may differ from bounty credit; gates_locked is recorded at result; sudden-death killer text remains verbatim."});
    fs::write(
        out.join("field-ttk.json"),
        serde_json::to_vec_pretty(&doc).map_err(|e| e.to_string())?,
    )
    .map_err(|e| e.to_string())?;
    Ok(())
}

#[cfg(test)]
mod tests {
    use super::*;
    #[test]
    fn pvp_contact_requires_positive_damage_from_a_different_owner() {
        let victim = PlayerId(1);
        let attacker = PlayerId(2);
        assert_eq!(hostile_owner(victim, Some(attacker)), Some(attacker));
        assert_eq!(hostile_owner(victim, Some(victim)), None);
        assert_eq!(hostile_owner(victim, None), None);
        assert!(!camp_damage_is_counted(false, Some(attacker), 0.));
        assert!(!camp_damage_is_counted(true, Some(attacker), 1.));
        assert!(!camp_damage_is_counted(false, None, 1.));
        assert!(camp_damage_is_counted(false, Some(attacker), 1.));
    }
    #[test]
    fn one_hit_camp_clear_at_zero_seconds_stays_in_the_ttk_distribution() {
        assert!(camp_damage_is_counted(false, Some(PlayerId(2)), 1.));
        let (n, p25, median, p75) = quant(&mut [0.]);
        assert_eq!((n, p25, median, p75), (1, Some(0.), Some(0.), Some(0.)));
    }
    #[test]
    fn distinct_attack_owners_flag_third_party_contact() {
        let mut w = Window::default();
        w.attackers.insert(PlayerId(2));
        assert_eq!(pvp_flags(&w), (true, false));
        w.attackers.insert(PlayerId(3));
        assert_eq!(pvp_flags(&w), (false, true));
    }
    #[test]
    fn death_closes_window_or_records_competing_cause_with_original_elapsed_time() {
        let victim = PlayerId(1);
        let attacker = PlayerId(2);
        let mut w = Window {
            start: 10.,
            last: 14.,
            hits: 3,
            damage: 9.,
            ..Window::default()
        };
        w.attackers.insert(attacker);
        assert_eq!(
            death_close(&w, victim, Some(attacker), 18.),
            ("death_clean", 8.)
        );
        assert_eq!(
            death_close(&w, victim, None, 18.),
            ("competing_non_player_final_blow", 8.)
        );
        assert_eq!(
            death_close(&w, victim, Some(victim), 18.),
            ("competing_self_final_blow", 8.)
        );
        assert_eq!((w.start, w.last, w.hits, w.damage), (10., 14., 3, 9.));
    }
}
