//! Stand-and-fight player duels through actual weapon attacks and the damage ledger.
use crate::elite_bench::{banked_camp_build, CAMP_STACKS};
use rh_headless::balance_lab::{match_setup, Options, SeatPolicy, SEATS};
use rh_headless::{load_arena, Env};
use rh_sim::cards::CardId;
use rh_sim::events::Event;
use rh_sim::factions::SignatureKind;
use rh_sim::mob_catalog::normal_types_of;
use rh_sim::reward_chests::BoostId;
use rh_sim::state::Player;
use rh_sim::status_runtime::AbilityId;
use rh_sim::trusted_build::{apply, TrustedBuild};
use rh_sim::{
    validate, Arena, ArenaMode, CombatPace, FactionId, Placement, RawCommand, Secrets, Sim,
    SimConfig, Vec2, WeaponId,
};
use std::sync::Arc;

const SEEDS: std::ops::RangeInclusive<u64> = 1..=6;
const WEAPONS: [WeaponId; 5] = [
    WeaponId::Sword,
    WeaponId::Bow,
    WeaponId::Wand,
    WeaponId::Shield,
    WeaponId::Axe,
];
// Pilot 2 held 164 stacks at minute 12 and gained about 15 per minute between minutes 8 and 12.
// Twenty-four banked camps are 288 stacks, an extrapolation of that rate to minute 20.
const FARMED_CAMPS: u32 = 24;
const TIME_LIMIT_MS: u32 = 90_000;

fn config(tick_ms: u32, combat_pace: CombatPace) -> SimConfig {
    SimConfig {
        lifecycle: ArenaMode::Off,
        quests: false,
        combat_pace,
        consumables: false,
        tick_ms,
        ..SimConfig::live_content()
    }
}

fn duel_scene(
    arena: &Arc<Arena>,
    mut config: SimConfig,
    seed: u64,
    factions: [FactionId; 2],
    weapon: WeaponId,
    build: &TrustedBuild,
) -> Sim {
    config.mob_mode = rh_sim::mob_runtime::MobMode::Off;
    config.initial_loot = false;
    let placements: [_; 2] = std::array::from_fn(|i| Placement {
        profile: format!("fighter{i}"),
        faction: factions[i],
        bot: false,
        at: Vec2::new(400. + 40. * i as f64, 320.),
        hp: None,
        weapon0: Some(weapon),
        weapon1: None,
    });
    let mut sim = Sim::reset_active(config, arena.clone(), Secrets::from_seed(seed), &placements);
    let ids = [sim.players()[0].id, sim.players()[1].id];
    for id in ids {
        apply(&mut sim, id, build).expect("measured duel build");
    }
    sim
}

#[derive(Default)]
struct Duel {
    seconds: Option<f64>,
    burn_amount: f64,
    mark_amount: f64,
    roam_amount: f64,
}

fn duel(
    arena: &Arc<Arena>,
    config: SimConfig,
    seed: u64,
    factions: [FactionId; 2],
    weapon: WeaponId,
    build: &TrustedBuild,
    warmup_ms: u32,
) -> Duel {
    let tick_ms = config.tick_ms;
    let mut sim = duel_scene(arena, config, seed, factions, weapon, build);
    let ids = [sim.players()[0].id, sim.players()[1].id];
    let mut events = Vec::new();
    for _ in 0..warmup_ms / tick_ms {
        events.clear();
        sim.step_into(&mut events);
    }
    for (id, a) in ids.into_iter().zip([0., rh_sim::math::PI]) {
        sim.submit(id, validate(RawCommand::Attack { a, t: None }).unwrap())
            .unwrap();
    }
    let mut result = Duel::default();
    for step in 1..=TIME_LIMIT_MS / tick_ms {
        events.clear();
        sim.step_into(&mut events);
        for e in &events {
            match *e {
                Event::SignatureHit {
                    sig: SignatureKind::Burn,
                    amount,
                    ..
                } => result.burn_amount += amount,
                Event::SignatureHit {
                    sig: SignatureKind::Mark,
                    amount,
                    ..
                } => result.mark_amount += amount,
                Event::Hit {
                    by: Some(_),
                    weapon: None,
                    passive: false,
                    amount,
                    ..
                } => {
                    // The standalone attack-only scene installs no other weaponless direct hits.
                    result.roam_amount += amount;
                }
                _ => {}
            }
        }
        if sim.players().iter().any(|p| !p.standing()) {
            result.seconds = Some(f64::from(step * tick_ms) / 1000.);
            break;
        }
    }
    result
}

fn blaze_damage(arena: &Arc<Arena>, tick_ms: u32, pace: CombatPace) -> f64 {
    let config = SimConfig {
        wish_lock_ms: 0.,
        ..config(tick_ms, pace)
    };
    let mut sim = duel_scene(
        arena,
        config,
        1,
        [FactionId::Fire, FactionId::Holy],
        WeaponId::Sword,
        &TrustedBuild::default(),
    );
    let caster = sim.players()[0].id;
    let victim = sim.players()[1].id;
    sim.submit(
        caster,
        validate(RawCommand::Cast {
            slot: rh_sim::abilities::Slot::Wish,
            x: 480.,
            y: 320.,
            id: None,
            t: None,
            seq: None,
        })
        .unwrap(),
    )
    .unwrap();
    let mut damage = 0.;
    let mut events = Vec::new();
    for _ in 0..10_000 / tick_ms {
        events.clear();
        sim.step_into(&mut events);
        for event in &events {
            match *event {
                Event::AbilityHit {
                    victim: id,
                    by: Some(by),
                    ability: AbilityId::Blaze,
                    amount,
                } if id == victim && by == caster => damage += amount,
                _ => {}
            }
        }
    }
    damage
}

// Actual named elite chest picks from pilot 2, seeds 22/seat 0, 23/seat 0, and 27/seat 4.
// Transplant each set onto a paired banked build to hold farm and gear constant across pace profiles.
const PILOT_PICKS: [&[CardId]; 3] = [
    &[
        CardId::SecondWind,
        CardId::TankEngine,
        CardId::ThirdTimesTheCharm,
    ],
    &[
        CardId::PlantYourFeet,
        CardId::Siphon,
        CardId::SpikedShell,
        CardId::TankEngine,
        CardId::ThirdTimesTheCharm,
        CardId::WishfulThinking,
    ],
    &[
        CardId::Committed,
        CardId::HereComesTheBoom,
        CardId::SecondWind,
        CardId::Siphon,
        CardId::SpikedShell,
        CardId::TankEngine,
    ],
];

pub fn run(args: &[String], map: &str) -> Result<(), String> {
    if args
        .iter()
        .any(|a| !matches!(a.as_str(), "--pvp" | "--verbose" | "--check" | "--sampled"))
    {
        return Err("--pvp accepts --verbose, --check and --sampled".into());
    }
    let probe = Sim::reset_active(
        config(50, CombatPace::Raid),
        load_arena(map)?,
        Secrets::from_seed(1),
        &[],
    );
    let builds: Vec<_> = SEEDS
        .enumerate()
        .map(|(i, seed)| {
            let build = banked_camp_build(&probe, seed, FARMED_CAMPS);
            assert_eq!(
                build.stacks.iter().map(|(_, stacks)| stacks).sum::<u64>(),
                u64::from(FARMED_CAMPS) * CAMP_STACKS
            );
            let mut augmented = build.clone();
            augmented.cards = PILOT_PICKS[i % PILOT_PICKS.len()]
                .iter()
                .map(|&card| (card, 1))
                .collect();
            (seed, [TrustedBuild::default(), build, augmented])
        })
        .collect();
    let arena = Arc::new(rh_sim::arenas::practice_arena());
    let mut failures = Vec::new();
    let verbose = args.iter().any(|arg| arg == "--verbose");
    let factions = FactionId::ALL
        .into_iter()
        .enumerate()
        .flat_map(|(i, a)| FactionId::ALL[i..].iter().copied().map(move |b| [a, b]))
        .collect::<Vec<_>>();
    for tick_ms in [50, 25] {
        for pace in [CombatPace::Legacy, CombatPace::Raid] {
            for (stage_index, stage) in ["starter", "farmed", "farmed + augments"]
                .into_iter()
                .enumerate()
            {
                let mut times = Vec::new();
                let mut truncated = 0;
                let mut burn_amount = 0.;
                let mut mark_amount = 0.;
                for (seed, builds) in &builds {
                    for &pair in &factions {
                        for weapon in WEAPONS {
                            let result = duel(
                                &arena,
                                config(tick_ms, pace),
                                *seed,
                                pair,
                                weapon,
                                &builds[stage_index],
                                0,
                            );
                            let time = result.seconds;
                            burn_amount += result.burn_amount;
                            mark_amount += result.mark_amount;
                            if let Some(time) = time {
                                times.push(time);
                            } else {
                                truncated += 1;
                            }
                            if verbose {
                                println!(
                                    "{tick_ms}ms {pace:?} {stage} {seed} {}/{} {} {time:?}",
                                    pair[0].as_str(),
                                    pair[1].as_str(),
                                    weapon.as_str()
                                );
                            }
                        }
                    }
                }
                times.sort_by(f64::total_cmp);
                let percentile = |p| {
                    times
                        .get((times.len().saturating_sub(1)) * p / 100)
                        .copied()
                        .unwrap_or(f64::NAN)
                };
                let median = percentile(50);
                println!(
                    "{tick_ms}ms {pace:?} {stage}: n={} timed_out={truncated} p10={:.2}s median={median:.2}s p90={:.2}s min={:.2}s max={:.2}s",
                    times.len(),
                    percentile(10),
                    percentile(90),
                    percentile(0),
                    percentile(100),
                );
                let band = if stage_index == 0 {
                    22. ..=28.
                } else {
                    8. ..=12.
                };
                if pace == CombatPace::Raid {
                    if truncated > 0 || !band.contains(&median) {
                        failures.push(format!(
                            "{tick_ms}ms {stage}: {median:.2}s / {truncated} truncated"
                        ));
                    }
                    if burn_amount <= 0. || mark_amount <= 0. {
                        failures.push(format!("{tick_ms}ms {stage}: burn or mark damage missing ({burn_amount}, {mark_amount})"));
                    }
                }
            }
        }
        let legacy_blaze = blaze_damage(&arena, tick_ms, CombatPace::Legacy);
        let raid_blaze = blaze_damage(&arena, tick_ms, CombatPace::Raid);
        println!("{tick_ms}ms Blaze cast: Legacy {legacy_blaze:.2}, Raid {raid_blaze:.2} damage");
        if legacy_blaze <= 0. || legacy_blaze != raid_blaze {
            failures.push(format!(
                "{tick_ms}ms: Blaze damage must be positive and independent of PvP pace"
            ));
        }
        let roam = duel(
            &arena,
            config(tick_ms, CombatPace::Raid),
            1,
            [FactionId::Fire; 2],
            WeaponId::Sword,
            &builds[0].1[2],
            rh_sim::constants::FIRE_ROAM_RAMP_MS as u32 + 2 * tick_ms,
        );
        println!(
            "{tick_ms}ms charged Fire with augments: {:?}s, {:.2} burst damage",
            roam.seconds, roam.roam_amount
        );
        if roam.roam_amount <= 0. {
            failures.push(format!(
                "{tick_ms}ms: charged Fire attack must deal its burst damage"
            ));
        }
    }
    if args.iter().any(|arg| arg == "--sampled") {
        run_sampled(&arena)?;
    }
    if args.iter().any(|arg| arg == "--check") && !failures.is_empty() {
        return Err(failures.join(", "));
    }
    Ok(())
}

// Transplant the sampled standing fighter's normal-mob stacks, held augments, boosts and equipped weapon.
fn sample_build(player: &Player) -> TrustedBuild {
    let boosts = player.build.boosts();
    TrustedBuild {
        cards: player
            .build
            .cards()
            .map(|id| (id, player.build.count(id)))
            .collect(),
        stacks: FactionId::ALL
            .into_iter()
            .flat_map(normal_types_of)
            .filter_map(|mob| {
                let count = player.farm.count(mob.id);
                (count > 0).then_some((mob.id, count))
            })
            .collect(),
        boosts: BoostId::ALL
            .into_iter()
            .filter_map(|id| {
                let amount = boosts[id.index()];
                (amount > 0).then_some((id, amount))
            })
            .collect(),
        rewards: Vec::new(),
    }
}

fn run_sampled(duel_arena: &Arc<Arena>) -> Result<(), String> {
    let options = Options {
        seats: (0..SEATS).map(|i| (i, SeatPolicy::Raid)).collect(),
        human_seats: true,
        ..Options::default()
    };
    let mut sampled = 0;
    for seed in [22, 23, 39] {
        let (cfg, agents) = match_setup(&options, seed)?;
        let mut env = Env::new(cfg, agents, seed);
        let mut last = None;
        loop {
            let progress = env.advance(&[]);
            if progress.terminated {
                break;
            }
            let tick = env.sim.tick();
            if tick >= 19_200 && tick % 1_200 == 0 {
                last = Some((
                    tick,
                    env.sim
                        .players()
                        .iter()
                        .filter(|p| p.standing())
                        .map(|p| (p.faction(), p.active_weapon(), sample_build(p)))
                        .collect::<Vec<_>>(),
                ));
            }
            if progress.truncated {
                break;
            }
        }
        let Some((tick, fighters)) = last else {
            println!("sampled seed {seed}: ended before minute 16");
            continue;
        };
        for (faction, weapon, build) in fighters {
            let stacks: u64 = build.stacks.iter().map(|(_, count)| count).sum();
            if stacks == 0 {
                continue;
            }
            sampled += 1;
            let power = rh_sim::cards::Build::from_counts(build.cards.iter().copied()).power();
            let times = [50, 25].map(|tick_ms| {
                duel(
                    duel_arena,
                    config(tick_ms, CombatPace::Raid),
                    seed,
                    [faction; 2],
                    weapon,
                    &build,
                    0,
                )
                .seconds
            });
            println!(
                "sampled seed {seed} tick {tick} {} {} {stacks} stacks, {power} card power: {times:?} seconds (20, 40Hz)",
                faction.as_str(), weapon.as_str()
            );
        }
    }
    if sampled == 0 {
        return Err("no standing farmed fighters sampled at minute 16 or later".into());
    }
    Ok(())
}
