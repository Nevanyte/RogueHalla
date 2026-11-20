//! P-TICK-1b (ADR 0029 decision 3): every system reads the sim's own step, so a 25 ms sim plays the same game per second
//! as a 50 ms one, within one step. Public API only; the burn (a status the public API cannot apply on its own) is
//! `src/systems/tick_rate_tests.rs`. The 50 ms digest guard is `crates/rh-headless/tests/tick_ms_digest_guard.rs`.
//!
//! Legacy: `packages/shared/src/walk.ts:231-253` walks one step of `dtMs` per server tick; legacy
//! `packages/server/src/sim/step.ts:302-305` takes the step as a parameter. Each assertion compares a 25 ms run with the
//! same world at 50 ms, or with the legacy number per second.

use rh_sim::abilities::{Mode, Slot};
use rh_sim::constants::{MAX_REWIND_MS, PLAYER_REGEN_SHARE_OUT, PLAYER_SPEED};
use rh_sim::{
    validate, Arena, Command, Event, FactionId, Placement, PlayerId, RawCommand, Secrets, Sim,
    SimConfig, TileMap, Vec2, WeaponId,
};
use serde_json::Value;

fn cmd(raw: RawCommand) -> Command {
    validate(raw).expect("a valid test command")
}

/// An open floor: 64 x 40 tiles, walls only on the border.
fn open_floor(spawns: &[Vec2]) -> Arena {
    let (w, h) = (64usize, 40usize);
    let mut tiles = vec![0; w * h];
    for y in 0..h {
        for x in 0..w {
            if x == 0 || y == 0 || x + 1 == w || y + 1 == h {
                tiles[y * w + x] = 1;
            }
        }
    }
    Arena {
        map: TileMap::new(w as u32, h as u32, tiles).unwrap(),
        spawns: spawns.to_vec(),
    }
}

fn placement(i: usize, faction: FactionId, at: Vec2, weapon0: Option<WeaponId>) -> Placement {
    Placement {
        profile: format!("rate{i}"),
        faction,
        bot: false,
        at,
        hp: None,
        weapon0,
        weapon1: None,
    }
}

/// Two fighters far apart on the open floor (two keep the run going); player 1 is the subject.
fn world(tick_ms: u32, config: SimConfig, weapon0: Option<WeaponId>, hp: Option<f64>) -> Sim {
    let a = Vec2::new(176., 176.);
    let b = Vec2::new(176., 1100.);
    let mut ps = vec![
        placement(0, FactionId::Holy, a, weapon0),
        placement(1, FactionId::Holy, b, None),
    ];
    ps[0].hp = hp;
    Sim::reset_active(
        SimConfig { tick_ms, ..config },
        open_floor(&[a, b]),
        Secrets::from_seed(11),
        &ps,
    )
}

fn steps_per_second(sim: &Sim) -> u32 {
    1000 / sim.tick_ms()
}

fn pos(sim: &Sim) -> Vec2 {
    sim.player(PlayerId(1)).unwrap().fighter().pos()
}

fn player_json(sim: &Sim) -> Value {
    serde_json::to_value(sim).unwrap()["world"]["players"][0].clone()
}

/// Walks player 1 east along y = 176 for `seconds`, returning (distance covered in the last whole second, sim).
fn walk(tick_ms: u32, config: SimConfig, seconds: u32) -> (f64, Sim) {
    let mut sim = world(tick_ms, config, None, None);
    sim.submit(
        PlayerId(1),
        cmd(RawCommand::Move {
            x: 1900.,
            y: 176.,
            seq: Some(1),
        }),
    )
    .unwrap();
    let n = steps_per_second(&sim);
    for _ in 0..(seconds - 1) * n {
        sim.step();
    }
    let before = pos(&sim);
    for _ in 0..n {
        sim.step();
    }
    let after = pos(&sim);
    (
        ((after.x - before.x).powi(2) + (after.y - before.y).powi(2)).sqrt(),
        sim,
    )
}

#[test]
fn a_walker_on_open_floor_covers_player_speed_per_second_at_both_rates() {
    for tick_ms in [50, 25] {
        let (d, sim) = walk(tick_ms, SimConfig::default(), 2);
        assert_eq!(sim.tick_ms(), tick_ms);
        assert!(
            (d - PLAYER_SPEED).abs() <= 1.0,
            "{tick_ms} ms: {d} u in one second, want {PLAYER_SPEED} within 1 u"
        );
    }
    // And the two rates stand within one 25 ms step of walk of each other after the same sim time.
    let (_, fifty) = walk(50, SimConfig::default(), 3);
    let (_, quarter) = walk(25, SimConfig::default(), 3);
    assert_eq!(fifty.server_time(), quarter.server_time());
    let gap = (pos(&fifty).x - pos(&quarter).x).abs();
    assert!(
        gap <= PLAYER_SPEED * 0.025 + 1e-9,
        "after 3 s the 25 ms walker is {gap} u from the 50 ms one"
    );
}

/// The first bow release (launch time) and the weapon's next ready time while the trigger is held.
fn bow(tick_ms: u32) -> (f64, f64, f64) {
    let mut sim = world(tick_ms, SimConfig::default(), Some(WeaponId::Bow), None);
    let t0 = sim.server_time();
    sim.submit(PlayerId(1), cmd(RawCommand::Attack { a: 0.0, t: None }))
        .unwrap();
    let mut launches = Vec::new();
    for _ in 0..(4 * steps_per_second(&sim)) {
        for e in sim.step() {
            if let Event::ProjectileLaunched(l) = e {
                if l.owner == PlayerId(1) {
                    launches.push((
                        l.time,
                        player_json(&sim)["core"]["weapon"]["ready_at"].clone(),
                    ));
                }
            }
        }
        if launches.len() >= 2 {
            break;
        }
    }
    assert!(
        launches.len() >= 2,
        "{tick_ms} ms: a held bow fires twice in 4 s"
    );
    let ready = launches[0].1.as_f64().expect("weapon ready_at");
    (
        launches[0].0 - t0,
        ready - t0,
        launches[1].0 - launches[0].0,
    )
}

#[test]
fn a_held_bow_releases_and_is_ready_within_one_step_of_the_50_ms_run() {
    let (first50, ready50, gap50) = bow(50);
    let (first25, ready25, gap25) = bow(25);
    assert!(
        (first25 - first50).abs() <= 25.0,
        "first release: {first25} ms at 25 ms against {first50} ms at 50 ms"
    );
    assert!(
        (ready25 - ready50).abs() <= 25.0,
        "next ready: {ready25} ms at 25 ms against {ready50} ms at 50 ms"
    );
    assert!(
        (gap25 - gap50).abs() <= 25.0,
        "release to release: {gap25} ms at 25 ms against {gap50} ms at 50 ms"
    );
}

/// The arrow's flight: how far the first arrow has travelled 400 ms after its launch (a bow arrow flies 900 u/s for
/// 520 u, so it is still in flight).
fn arrow_after_400_ms(tick_ms: u32) -> f64 {
    let mut sim = world(tick_ms, SimConfig::default(), Some(WeaponId::Bow), None);
    sim.submit(PlayerId(1), cmd(RawCommand::Attack { a: 0.0, t: None }))
        .unwrap();
    let mut launched = None;
    for _ in 0..(4 * steps_per_second(&sim)) {
        for e in sim.step() {
            if let Event::ProjectileLaunched(l) = e {
                if l.owner == PlayerId(1) && launched.is_none() {
                    launched = Some(l);
                }
            }
        }
        if launched.is_some() {
            break;
        }
    }
    let l = launched.expect("the bow fires");
    sim.submit(PlayerId(1), cmd(RawCommand::Release { a: 0.0, t: None }))
        .unwrap();
    // Step until 400 ms after launch, then read the arrow's travel from the checkpoint's projectile row.
    while sim.server_time() < l.time + 400.0 {
        sim.step();
    }
    assert_eq!(sim.server_time(), l.time + 400.0);
    let json = serde_json::to_value(&sim).unwrap();
    json["world"]["projectiles"]
        .as_array()
        .and_then(|a| {
            a.iter()
                .find(|p| p["launch"]["id"].as_u64() == Some(u64::from(l.id)))
        })
        .and_then(|p| p["traveled"].as_f64())
        .unwrap_or(-1.0)
}

#[test]
fn an_arrow_flies_the_same_distance_per_second_at_both_rates() {
    let fifty = arrow_after_400_ms(50);
    let quarter = arrow_after_400_ms(25);
    assert!(
        fifty > 0.0,
        "the 50 ms arrow is in flight 400 ms after launch ({fifty})"
    );
    assert!(
        (quarter - fifty).abs() <= 1e-6 * fifty,
        "arrow 400 ms after launch: {quarter} u at 25 ms against {fifty} u at 50 ms"
    );
}

#[test]
fn passive_regen_heals_the_same_per_second_at_both_rates() {
    let config = SimConfig {
        regen: true,
        ..SimConfig::default()
    };
    let mut healed = Vec::new();
    for tick_ms in [50, 25] {
        let mut sim = world(tick_ms, config.clone(), None, Some(50.0));
        let max = player_json(&sim)["core"]["vitals"]["max_hp"]
            .as_f64()
            .unwrap();
        let hp0 = sim.player(PlayerId(1)).unwrap().fighter().hp();
        for _ in 0..2 * steps_per_second(&sim) {
            sim.step();
        }
        let got = sim.player(PlayerId(1)).unwrap().fighter().hp() - hp0;
        let want = 2.0 * max * PLAYER_REGEN_SHARE_OUT;
        assert!(
            (got - want).abs() <= 1e-6 * want,
            "{tick_ms} ms: healed {got} in 2 s, want {want}"
        );
        healed.push(got);
    }
    assert!((healed[0] - healed[1]).abs() <= 1e-6 * healed[0]);
}

/// The walk hint commits the walker for 150 ms of sim time (ADR 0029 decision 3: `WALK_HINT_MAX_TICKS` 3 ticks at
/// 50 ms becomes 150 ms): 3 steps at 50 ms, 6 at 25 ms. A checkpoint holding the 25 ms hint restores.
#[test]
fn the_walk_hint_commits_for_150_ms_at_both_rates() {
    let config = SimConfig {
        walk_hints: Some(true),
        ..SimConfig::default()
    };
    for (tick_ms, want) in [(50u32, 3u64), (25, 6)] {
        let (_, sim) = walk(tick_ms, config.clone(), 1);
        let ticks = player_json(&sim)["walk_ticks"]
            .as_u64()
            .expect("walk_ticks");
        assert_eq!(
            ticks, want,
            "{tick_ms} ms: a walker in the open commits {ticks} steps, want {want} (150 ms)"
        );
        assert_eq!(ticks * u64::from(tick_ms), 150);
        let restored = Sim::restore(serde_json::to_value(&sim).unwrap())
            .unwrap_or_else(|e| panic!("{tick_ms} ms: a walking checkpoint restores: {e}"));
        assert_eq!(restored.state_hash(), sim.state_hash());
    }
}

/// The rewind history at 25 ms: samples one 25 ms step apart, trimmed with the sim's step (legacy `recordHistory`,
/// `packages/server/src/sim/movement.ts:101-105`: `cutoff = now - MAX_REWIND_MS - TICK_MS`, keep two).
#[test]
fn a_25_ms_rewind_history_is_spaced_and_trimmed_by_the_sim_step() {
    let (_, sim) = walk(25, SimConfig::default(), 2);
    let now = sim.server_time();
    let h = player_json(&sim)["core"]["body"]["history"].clone();
    let t: Vec<f64> = h
        .as_array()
        .expect("history")
        .iter()
        .map(|s| s["t"].as_f64().unwrap())
        .collect();
    assert_eq!(*t.last().unwrap(), now);
    assert!(t.windows(2).all(|w| w[1] - w[0] == 25.0), "{t:?}");
    // The oldest kept sample is the first at or after the cutoff `now - MAX_REWIND_MS - 25`.
    assert_eq!(
        t[0],
        now - MAX_REWIND_MS - 25.0,
        "the oldest sample of a 25 ms history: {t:?}"
    );
}

/// A rewind history denser than the 25 ms step is refused at restore; one exactly a step apart is what a 25 ms run
/// writes and restores.
#[test]
fn a_too_dense_25_ms_rewind_history_is_refused() {
    let (_, sim) = walk(25, SimConfig::default(), 2);
    let json = serde_json::to_value(&sim).unwrap();
    assert!(
        Sim::restore(json.clone()).is_ok(),
        "a real 25 ms history restores"
    );
    let now = sim.server_time();
    for gap in [0.0, 1.0, 12.5, 24.0] {
        let mut bad = json.clone();
        let x = pos(&sim).x;
        let h: Vec<Value> = (0..9)
            .map(|i| serde_json::json!({"t": now - gap * (8 - i) as f64, "x": x, "y": 176.0}))
            .collect();
        bad["world"]["players"][0]["core"]["body"]["history"] = h.into();
        let err = Sim::restore(bad)
            .err()
            .map(|e| e.to_string())
            .unwrap_or_default();
        assert!(err.contains("tick apart"), "gap {gap} ms: {err:?}");
    }
}

fn wish_world(tick_ms: u32) -> Sim {
    let placements = [
        (FactionId::Frost, Vec2::new(300., 320.)),
        (FactionId::Holy, Vec2::new(440., 320.)),
    ];
    let mut tiles = vec![0; 40 * 24];
    for y in 0..24 {
        for x in 0..40 {
            if x == 0 || y == 0 || x == 39 || y == 23 {
                tiles[y * 40 + x] = 1;
            }
        }
    }
    Sim::reset_active(
        SimConfig {
            quests: false,
            consumables: false,
            abilities: Mode::Full,
            lifecycle: rh_sim::ArenaMode::Source(rh_sim::ArenaRules {
                plan_seconds: 0.,
                first_patience_seconds: None,
                coliseum_schedule: None,
                patience_seconds: 3600.,
                patience_min_seconds: 3600.,
                sudden_death_seconds: 60.,
            }),
            wish_lock_ms: 0.,
            tick_ms,
            ..Default::default()
        },
        Arena {
            map: TileMap::new(40, 24, tiles).unwrap(),
            spawns: placements.iter().map(|p| p.1).collect(),
        },
        Secrets::from_seed(31),
        &placements
            .iter()
            .enumerate()
            .map(|(i, (faction, at))| placement(i, *faction, *at, None))
            .collect::<Vec<_>>(),
    )
}

/// A 25 ms checkpoint whose clocks sit on the 25 ms grid but not on the 50 ms one restores (the `% TICK_MS` checks in
/// `abilities/checkpoint.rs` and `cards/fight_clock.rs` read the sim's step).
#[test]
fn a_25_ms_checkpoint_with_a_wish_cast_at_an_odd_step_restores() {
    let mut sim = wish_world(25);
    // Clear of the raid start, then submit on a 50 ms boundary so the cast is accepted at an odd 25 ms step.
    for _ in 0..10 {
        sim.step();
    }
    if sim.server_time() % 50.0 != 0.0 {
        sim.step();
    }
    assert_eq!(sim.server_time() % 50.0, 0.0);
    sim.submit(
        PlayerId(1),
        cmd(RawCommand::Cast {
            slot: Slot::Wish,
            x: 440.,
            y: 320.,
            id: Some(PlayerId(2)),
            t: None,
            seq: None,
        }),
    )
    .unwrap();
    let mut started = None;
    let mut landed = None;
    for _ in 0..80 {
        for e in sim.step() {
            match e {
                Event::CastStarted { event_time, .. } => started = Some(event_time),
                Event::CastLanded { event_time, .. } => landed = Some(event_time),
                _ => {}
            }
        }
        if let Some(at) = started {
            assert_eq!(
                at % 50.0,
                25.0,
                "the cast is accepted on the odd step after its submission"
            );
        }
        let json = serde_json::to_value(&sim).unwrap();
        if let Err(e) = Sim::restore(json) {
            panic!(
                "25 ms checkpoint at {} ms (cast started {started:?}, landed {landed:?}) refused: {e}",
                sim.server_time()
            );
        }
    }
    assert!(landed.is_some(), "the Wish lands");
}
