//! Opt-in route experiment over authoritative live-content matches.
#[cfg(feature = "strategy-route-lab")]
fn parse_args(args: &[String]) -> Result<(u64, u64, &'static [&'static str]), String> {
    if !(2..=3).contains(&args.len()) {
        return Err(
            "usage: strategy_route_lab COUNT FIRST_SEED [live|local-target|calibrated|safe-target|nearest|focus-target|focus-route|focus-risk|focus-nearest|focus-live|theme-stat|theme-risk]"
                .into(),
        );
    }
    let count: u64 = args[0]
        .parse()
        .map_err(|_| "COUNT must be a positive integer")?;
    let first: u64 = args[1]
        .parse()
        .map_err(|_| "FIRST_SEED must be a positive integer")?;
    if count == 0 || first == 0 {
        return Err("COUNT and FIRST_SEED must be positive".into());
    }
    let end = first.checked_add(count).ok_or("seed range overflows")?;
    if end > i64::MAX as u64 {
        return Err("seed range exceeds map generator range".into());
    }
    let arms: &'static [&'static str] = match args.get(2).map(String::as_str) {
        None => &["target", "nearest", "random5"],
        Some("live") => &["live"],
        Some("local-target") => &["local-target"],
        Some("calibrated") => &["calibrated"],
        Some("safe-target") => &["safe-target"],
        Some("nearest") => &["nearest"],
        Some("focus-target") => &["focus-target"],
        Some("focus-route") => &["focus-route"],
        Some("focus-risk") => &["focus-risk"],
        Some("focus-nearest") => &["focus-nearest"],
        Some("focus-live") => &["focus-live"],
        Some("theme-stat") => &["theme-stat"],
        Some("theme-risk") => &["theme-risk"],
        Some(_) => {
            return Err(
                "unknown arm; expected live, local-target, calibrated, safe-target, nearest, focus-target, focus-route, focus-risk, focus-nearest, focus-live, theme-stat or theme-risk"
                    .into(),
            )
        }
    };
    Ok((count, first, arms))
}

#[cfg(not(feature = "strategy-route-lab"))]
fn main() {
    eprintln!("enable --features strategy-route-lab");
    std::process::exit(2);
}

#[cfg(all(test, feature = "strategy-route-lab"))]
mod tests {
    use super::parse_args;
    fn args(words: &[&str]) -> Vec<String> {
        words.iter().map(|s| (*s).to_owned()).collect()
    }
    #[test]
    fn validates_seed_ranges_and_arm_names() {
        assert_eq!(
            parse_args(&args(&["10", "41", "local-target"])).unwrap().0,
            10
        );
        assert!(parse_args(&args(&["10", "41", "focus-risk"])).is_ok());
        assert!(parse_args(&args(&["10", "41", "theme-stat"])).is_ok());
        for bad in [
            &["0", "1"][..],
            &["1", "0"],
            &["x", "1"],
            &["1", "x"],
            &["1", "1", "unknown"],
            &["18446744073709551615", "1"],
        ] {
            assert!(parse_args(&args(bad)).is_err(), "accepted {bad:?}");
        }
    }
}

#[cfg(feature = "strategy-route-lab")]
fn main() {
    use rh_headless::{balance_lab, Agent, Env};
    use rh_policy::bots::raid::exec::farm::LabRoute;
    use rh_policy::bots::{assign_seats, build_route::BUILD_GOALS, RaidBot};
    use serde_json::json;
    let args: Vec<String> = std::env::args().skip(1).collect();
    let (seeds, first, arms) = parse_args(&args).unwrap_or_else(|error| {
        eprintln!("{error}");
        std::process::exit(2)
    });
    for seed in first..first + seeds {
        for &arm in arms {
            let opts = balance_lab::Options {
                max_ticks: 52_000,
                seats: (0..balance_lab::SEATS)
                    .map(|s| (s, balance_lab::SeatPolicy::Raid))
                    .collect(),
                ..Default::default()
            };
            let (mut cfg, mut agents) =
                balance_lab::match_setup(&opts, seed).expect("certified map");
            cfg.sim.tick_ms = 25;
            if arm.starts_with("focus-") {
                cfg.sim.camp_buffs = cfg.sim.camp_buffs.with_faction_focus();
            }
            let assignments = assign_seats(seed, agents.len());
            let cohort = (seed - 1) as usize;
            let desired_faction = cohort % 5;
            let focal = (desired_faction + 5 - (seed as usize % 5)) % 5;
            let build_index = (cohort / 5) % 2;
            for (seat, a) in agents.iter_mut().enumerate() {
                let route = match arm {
                    "target" | "focus-target" => LabRoute::TargetBuild(build_index),
                    "focus-route" => LabRoute::FocusRoute,
                    "focus-risk" | "theme-risk" => LabRoute::FocusRiskControl,
                    "theme-stat" => LabRoute::FocusStatRisk(build_index),
                    "local-target" => LabRoute::LocalTarget(build_index),
                    "calibrated" => LabRoute::CalibratedBuild(build_index),
                    "safe-target" => LabRoute::SafeTargetBuild(build_index),
                    "nearest" | "focus-nearest" => LabRoute::Nearest,
                    _ => LabRoute::RandomNearby(seed.wrapping_mul(31).wrapping_add(seat as u64)),
                };
                let (tier, persona) = assignments[seat];
                // Seven ordinary live fill policies; only the focal RaidBot is a quest-eligible human seat.
                a.bot = seat != focal;
                if seat == focal {
                    a.agent = Agent::Policy {
                        policy: if arm == "live" || arm == "focus-live" {
                            Box::new(RaidBot::new(tier, persona))
                        } else {
                            Box::new(RaidBot::new(tier, persona).with_lab_route(route))
                        },
                    };
                }
            }
            assert!(agents.iter().all(|a| matches!(&a.agent, Agent::Policy { policy } if policy.spec().name.starts_with("raid-"))));
            let mut env = Env::new(cfg, agents, seed);
            let map_hash = env.sim.arena().map.layers().map_or_else(
                || "unlayered".to_string(),
                |l| format!("{:08x}", l.legacy_map_hash_u32()),
            );
            let mut progress = None;
            let mut travelled_u = 0.0;
            let mut moved_ticks = 0u64;
            let mut focus_bonus_kills = 0u32;
            let mut previous = env.sim.players()[focal].core.pos();
            let focal_id = env.sim.players()[focal].id;
            let mut result = None;
            let mut active_start = None;
            let mut next_minute = 5u32;
            for _ in 1..=52_000 {
                let p = env.advance(&[]);
                let current = env.sim.players()[focal].core.pos();
                let delta = rh_sim::math::hypot(current.x - previous.x, current.y - previous.y);
                travelled_u += delta;
                moved_ticks += u64::from(delta >= 1.0);
                previous = current;
                for event in env.last_events() {
                    if matches!(event, rh_sim::Event::Toast { to: Some(id), text }
                        if *id == focal_id && text.contains("Focus route +3"))
                    {
                        focus_bonus_kills += 1;
                    }
                    if let rh_sim::Event::Resolved { id, result: r, .. } = event {
                        if *id == focal_id {
                            result = Some(r.clone());
                        }
                    }
                }
                if active_start.is_none() && env.sim.phase() == rh_sim::Phase::Active {
                    active_start = Some(env.sim.server_time());
                }
                if let Some(start) = active_start {
                    if next_minute <= 20
                        && env.sim.server_time() - start >= f64::from(next_minute) * 60_000.
                    {
                        let player = &env.sim.players()[focal];
                        let obs = rh_sim::observe::observe(&env.sim, player.id, &[]);
                        let me = obs.me.as_ref().expect("seated owner");
                        let faction = me.faction;
                        let build = &BUILD_GOALS[faction as usize][build_index];
                        let stacks: Vec<_> = obs
                            .summary
                            .as_ref()
                            .expect("owner summary")
                            .mob_rows
                            .iter()
                            .map(|r| json!([r.mob, r.stacks]))
                            .collect();
                        println!(
                            "{}",
                            json!({
                                "seed":seed, "arm":arm, "minute":next_minute, "seat":focal,
                                "faction":format!("{faction:?}"), "build_id":build.id,
                                "alive":me.status == rh_sim::Status::Alive, "stacks":stacks,
                                "camp_clears":player.camps_cleared,
                                "focus_bonus_kills":focus_bonus_kills,
                                "movement_distance_u":travelled_u,"moved_ticks":moved_ticks,
                                "map_hash":map_hash, "ticks":p.tick, "reached":true,
                            })
                        );
                        next_minute += 5;
                    }
                }
                if p.terminated || p.truncated {
                    progress = Some(p);
                    break;
                }
            }
            let p = progress.expect("tick limit or match end");
            while next_minute <= 20 {
                let faction = env.sim.players()[focal]
                    .core
                    .vitals
                    .faction
                    .faction_id()
                    .expect("faction");
                let build = &BUILD_GOALS[faction as usize][build_index];
                println!(
                    "{}",
                    json!({"seed":seed,"arm":arm,"minute":next_minute,"seat":focal,
                    "faction":format!("{faction:?}"),"build_id":build.id,"alive":null,"stacks":null,
                    "map_hash":map_hash,"ticks":p.tick,"reached":false,
                    "camp_clears":null,"focus_bonus_kills":null,"movement_distance_u":null,"moved_ticks":null})
                );
                next_minute += 5;
            }
            eprintln!(
                "{}",
                json!({"seed":seed,"arm":arm,"focal":focal,"ticks":p.tick,
                "terminated":p.terminated,"state_hash":format!("{:016x}",env.sim.state_hash()),
                "result":result})
            );
        }
    }
}
