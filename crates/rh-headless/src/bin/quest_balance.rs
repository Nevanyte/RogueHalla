//! Read-only quest funnel and reward-use trace for one eligible RaidBot in the stock 40 Hz field cohort.
#[path = "quest_balance/route.rs"]
mod route;

use rh_headless::{
    balance_lab::{match_setup, Options, SeatPolicy, SEATS},
    Agent, Env,
};
use rh_policy::bots::raid::quest::decide::def_of_reward;
use rh_sim::quest_view::{QuestObserver, RowState};
use rh_sim::quests::board::catalog::{QuestId, RewardKind};
use rh_sim::{Event, Phase};
use serde::Serialize;
use serde_json::json;
use std::collections::BTreeMap;

#[derive(Serialize)]
struct Attempt {
    quest: QuestId,
    offer: u32,
    accept_s: f64,
    objective_s: Option<f64>,
    turn_in_s: Option<f64>,
    settled_s: Option<f64>,
    reward: Option<&'static str>,
    end_s: f64,
    outcome: &'static str,
    max_progress: u8,
    deadline_ms: f64,
    camps_during: u16,
}

#[derive(Serialize)]
struct RewardNotice<'a> {
    age_s: f64,
    locked: bool,
    text: &'a str,
}

fn play(seed: u64) -> Result<serde_json::Value, String> {
    let opts = Options {
        max_ticks: 72_000,
        seats: (0..SEATS).map(|s| (s, SeatPolicy::Raid)).collect(),
        ..Options::default()
    };
    let (mut cfg, mut agents) = match_setup(&opts, seed)?;
    agents[0].bot = false;
    cfg.sim.tick_ms = 25;
    let goal = route::Goal::default();
    agents[0].agent = Agent::Policy {
        policy: Box::new(route::ObserveGoal::new(seed, SEATS, goal.clone())),
    };
    let mut env = Env::new(cfg, agents, seed);
    let id = env.sim.players()[0].id;
    let mut observer = QuestObserver::new(id);
    let mut planning = Vec::new();
    let mut raid = Vec::new();
    let mut active = BTreeMap::<u32, (usize, u16)>::new();
    let mut attempts: Vec<Attempt> = Vec::new();
    let mut notices = Vec::new();
    let mut prelock_seconds = 0.;
    let mut without_open_seconds = 0.;
    let mut without_open_camps = 0u32;
    let mut eligible_farm_seconds = 0.;
    let mut focal_camps = 0;
    let mut latest_death: Option<u32> = None;
    let mut resolutions = Vec::new();
    let mut raid_ended_event = false;
    let mut route_legs: Vec<route::Leg> = Vec::new();
    let mut current_leg: Option<usize> = None;
    let mut normal_outside_s = 0.;
    let mut normal_outside_clears = 0u64;
    let mut normal_outside_bands = [[0u64; 2]; 3];
    let (progress, age) = loop {
        let previous_camps = focal_camps;
        let p = env.advance(&[]);
        let sim = &env.sim;
        let now = sim.server_time();
        let age = (now - sim.raid_started_at()).max(0.) / 1000.;
        let locked = sim.gates_locked();
        let phase = sim.phase();
        let step_s = f64::from(sim.tick_ms()) / 1000.;
        let player = sim.player(id).expect("focal seat");
        focal_camps = player.camps_cleared;
        if (phase == Phase::Planning && planning.is_empty())
            || (phase == Phase::Active && raid.len() < 10)
        {
            // Each definition is unique on the deal. A disposable preview suffices for the membership snapshot.
            // Neither these identities nor this observer's deliveries are ever used to submit commands.
            let view = observer.prepare(sim).map_err(|e| format!("{e:?}"))?.view;
            let destination = match (phase, view.rows.len()) {
                (Phase::Planning, 5) => Some((&mut planning, RowState::Preview)),
                (Phase::Active, 10) => Some((&mut raid, RowState::Open)),
                _ => None,
            };
            if let Some((offers, state)) = destination {
                offers.extend(view.rows.into_iter().map(|r| {
                    assert_eq!(r.state, state);
                    r.def
                }));
            }
        }
        let mut received = false;
        let mut choice = false;
        for event in env.last_events() {
            match event {
                Event::Death { id: victim, .. } if *victim == id => latest_death = Some(p.tick),
                Event::Resolved {
                    id: resolved_id,
                    result,
                    bot,
                } => {
                    if *resolved_id == id {
                        resolutions.push(json!({
                            "bot": bot, "outcome": format!("{:?}", result.outcome),
                            "duration_s": result.duration_sec, "kills": result.kills,
                            "place": result.place,
                        }));
                    }
                }
                Event::RaidEnded => raid_ended_event = true,
                Event::Toast {
                    to: Some(owner),
                    text,
                } if *owner == id => {
                    received |= text.starts_with("Reward received: ");
                    choice |= text == "Quest reward choice resolved";
                    if text.starts_with("Used ")
                        || text.starts_with("Reward received:")
                        || text == "Quest reward choice resolved"
                        || text.starts_with("Seed planted.")
                    {
                        notices.push(
                            serde_json::to_value(RewardNotice {
                                age_s: age,
                                locked,
                                text,
                            })
                            .map_err(|e| e.to_string())?,
                        );
                    }
                }
                _ => {}
            }
        }
        if phase == Phase::Active && !locked && player.standing() {
            // The player's delta credits each nearby contributing standing player. If elite and normal
            // camps closed on the same step, this public observer does not guess which was credited.
            let mut normal_events = 0;
            let mut elite_event = false;
            for event in env.last_events() {
                if let Event::CampCleared { camp } = event {
                    if let Some(roster) = sim.camp_rosters().get(*camp as usize) {
                        if roster.elite.is_some() {
                            elite_event = true;
                        } else {
                            normal_events += 1;
                        }
                    }
                }
            }
            let normal_credit = if !elite_event && normal_events > 0 {
                u64::from(focal_camps.saturating_sub(previous_camps))
            } else {
                0
            };
            if let Some(def) = goal.lock().unwrap().as_deref() {
                let index = current_leg
                    .filter(|&index| route_legs[index].quest == def)
                    .unwrap_or_else(|| {
                        route_legs.push(route::Leg {
                            quest: def.to_owned(),
                            start_s: age,
                            end_s: age,
                            standing_s: 0.,
                            normal_clears: 0,
                        });
                        route_legs.len() - 1
                    });
                let leg = &mut route_legs[index];
                leg.end_s = age;
                leg.standing_s += step_s;
                leg.normal_clears += normal_credit;
                current_leg = Some(index);
            } else {
                current_leg = None;
                normal_outside_s += step_s;
                normal_outside_clears += normal_credit;
                let band = ((age / 300.) as usize).min(2);
                normal_outside_bands[band][0] += 1;
                normal_outside_bands[band][1] += normal_credit;
            }
        } else {
            current_leg = None;
        }
        let rows = sim.quest_attempts(id);
        let equipped = if choice {
            observer
                .prepare(sim)
                .map_err(|e| format!("{e:?}"))?
                .view
                .spells
        } else {
            Vec::new()
        };
        for row in &rows {
            let (index, start_camps) = *active.entry(row.offer).or_insert_with(|| {
                attempts.push(Attempt {
                    quest: row.quest,
                    offer: row.offer,
                    accept_s: age,
                    objective_s: None,
                    turn_in_s: None,
                    settled_s: None,
                    reward: None,
                    end_s: age,
                    outcome: "open",
                    max_progress: 0,
                    deadline_ms: 0.,
                    camps_during: 0,
                });
                (attempts.len() - 1, previous_camps)
            });
            let a = &mut attempts[index];
            if a.settled_s.is_some() {
                continue;
            }
            a.end_s = age;
            a.camps_during = focal_camps.saturating_sub(start_camps);
            a.max_progress = a.max_progress.max(row.progress);
            if row.deadline > 0. {
                a.deadline_ms = row.deadline;
            }
            // Fast turn-ins may skip the separately observable ready and pending states in one step.
            if row.ready || row.turned_in || row.completed {
                a.objective_s.get_or_insert(age);
            }
            if row.turned_in || row.completed {
                a.turn_in_s.get_or_insert(age);
                a.outcome = "pending";
            }
            if row.completed {
                a.settled_s = Some(age);
                a.outcome = "settled";
                a.reward = Some(if received {
                    "received"
                } else if choice
                    && equipped.iter().any(|sp| {
                        def_of_reward(sp.reward)
                            .is_some_and(|(d, _)| d == row.quest.definition().id)
                    })
                {
                    "equipped_after_choice"
                } else if choice {
                    "left_on_ground"
                } else {
                    "no_reward_notice"
                });
            }
        }
        active.retain(|offer, (i, _)| {
            if rows.iter().any(|r| r.offer == *offer) {
                return true;
            }
            let a = &mut attempts[*i];
            if a.settled_s.is_none() {
                a.end_s = age;
                a.outcome = if !locked
                    && a.deadline_ms > 0.
                    && now >= a.deadline_ms
                    && now - a.deadline_ms <= 2. * f64::from(sim.tick_ms())
                {
                    "timer"
                } else if latest_death.is_some_and(|t| p.tick - t <= 1) {
                    "death"
                } else {
                    "lost"
                };
            }
            false
        });
        if phase == Phase::Active && !locked {
            prelock_seconds += step_s;
            if player.standing() {
                eligible_farm_seconds += step_s;
                if !rows.iter().any(|r| r.accepted && !r.turned_in) {
                    without_open_seconds += step_s;
                    without_open_camps += u32::from(focal_camps.saturating_sub(previous_camps));
                }
            }
        }
        if p.terminated || p.truncated {
            break (p, age);
        }
    };
    assert_eq!(planning.len(), 5, "seed {seed}: planning board");
    assert_eq!(raid.len(), 10, "seed {seed}: raid board");
    assert!(planning.iter().all(|def| raid.contains(def)));
    Ok(json!({
        "seed": seed, "tick_ms": env.sim.tick_ms(), "ticks": progress.tick, "focal": id.0,
        "faction": player_faction(&env),
        "terminated": progress.terminated, "truncated": progress.truncated, "age_s": age,
        "map_hash": env.sim.arena().map.layers().map(|l| format!("{:08x}", l.legacy_map_hash_u32())),
        "state_hash": format!("{:016x}", env.sim.state_hash()),
        "winner": env.sim.winner(), "resolutions": resolutions, "raid_ended_event": raid_ended_event,
        "planning": planning, "raid": raid, "attempts": attempts, "notices": notices,
        "prelock_seconds": prelock_seconds, "focal_camps": focal_camps,
        "standing_prelock_seconds": eligible_farm_seconds,
        "without_open_seconds": without_open_seconds, "without_open_camps": without_open_camps,
        "quest_goal_legs": route_legs, "normal_outside_s": normal_outside_s,
        "normal_outside_clears": normal_outside_clears, "normal_outside_bands": normal_outside_bands,
    }))
}

fn player_faction(env: &Env) -> String {
    format!(
        "{:?}",
        env.sim.players()[0]
            .core
            .vitals
            .faction
            .faction_id()
            .unwrap()
    )
}

fn catalog() -> serde_json::Value {
    // These prices explain what the policy chooses. They are design inputs, not measured reward value.
    let prices = rh_policy::bots::raid::values::Prices::default();
    let rows: Vec<_> = QuestId::ALL
        .into_iter()
        .map(|q| {
            let d = q.definition();
            let kind = match d.reward.kind() {
                RewardKind::SingleUse => "single_use",
                RewardKind::Reusable => "reusable",
                RewardKind::Permanent => "permanent",
            };
            json!({"quest":q, "id":d.id, "name":d.name, "home":format!("{:?}", q.home()),
            "reward":d.reward, "reward_text":d.reward_text, "reward_kind":kind,
            "planner_reward_ce":prices.reward_value(d.id)})
        })
        .collect();
    json!({"schema":"quest-balance/1", "content_hash":format!("{:016x}", rh_sim::content::content_hash()),
        "catalog":rows})
}

fn main() -> Result<(), String> {
    let args: Vec<_> = std::env::args().skip(1).collect();
    let [lo, hi] = args.as_slice() else {
        return Err("usage: quest_balance FIRST_SEED EXCLUSIVE_END".into());
    };
    let lo = lo.parse::<u64>().map_err(|_| "invalid first seed")?;
    let hi = hi.parse::<u64>().map_err(|_| "invalid exclusive end")?;
    if lo >= hi || hi > i64::MAX as u64 {
        return Err("empty or invalid seed range".into());
    }
    println!("{}", catalog());
    for seed in lo..hi {
        println!("{}", play(seed)?);
    }
    Ok(())
}
