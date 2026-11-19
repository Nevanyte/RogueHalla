//! P-TICK-1a (ADR 0029): the step length is a per-sim setting, `SimConfig::tick_ms`, 50 (default, legacy, parity) or 25.
//! Sim time advances by the sim's own value each step (legacy `step(world, dtMs)`: `serverTime += dtMs; tick++`,
//! `packages/server/src/sim/step.ts:302-305`), the value is saved, restored, replayed and folded into the state hash, and
//! any other value is refused wherever a config enters (construction, checkpoint). The 50 ms digest guard is
//! `crates/rh-headless/tests/tick_ms_digest_guard.rs`.

use rh_sim::arenas::practice_arena;
use rh_sim::replay::{apply_input, run_replay, Recorder, Replay, ReplayInput, ReplayStart};
use rh_sim::{
    validate, Command, FactionId, Placement, PlayerId, RawCommand, Secrets, Sim, SimConfig, Vec2,
};
use serde_json::Value;

fn cmd(raw: RawCommand) -> Command {
    validate(raw).expect("a valid test command")
}

fn config(tick_ms: u32) -> SimConfig {
    SimConfig {
        tick_ms,
        ..SimConfig::default()
    }
}

fn placements() -> Vec<Placement> {
    [
        FactionId::Holy,
        FactionId::Fire,
        FactionId::Frost,
        FactionId::Plague,
    ]
    .iter()
    .enumerate()
    .map(|(i, &faction)| Placement {
        profile: format!("tick{i}"),
        faction,
        bot: false,
        at: Vec2::new(400.0 + 28.0 * i as f64, 400.0),
        hp: None,
        weapon0: None,
        weapon1: None,
    })
    .collect()
}

/// Four fighters side by side on the practice arena, swinging at their neighbours (the world moves, fights and hashes).
fn fight(tick_ms: u32) -> Sim {
    Sim::reset_active(
        config(tick_ms),
        practice_arena(),
        Secrets::from_seed(7),
        &placements(),
    )
}

fn inputs(n: u32) -> Vec<(u32, Command)> {
    let mut out = Vec::new();
    for i in 1..=4u32 {
        let a = n as f64 * 0.05 + i as f64;
        out.push((i, cmd(RawCommand::Aim { a })));
        if n % 7 == 0 {
            out.push((i, cmd(RawCommand::Attack { a, t: None })));
        }
        if n % 7 == 3 {
            out.push((i, cmd(RawCommand::Release { a, t: None })));
        }
    }
    out
}

fn drive(sim: &mut Sim, from: u32, steps: u32) {
    for n in from..from + steps {
        for (id, c) in inputs(n) {
            let _ = sim.submit(PlayerId(id), c);
        }
        sim.step();
    }
}

fn restore(sim: &Sim) -> Sim {
    Sim::restore(serde_json::to_value(sim).expect("checkpoint")).expect("restore")
}

/// Every value of every key named `tick_ms` anywhere in `v`.
fn tick_ms_values(v: &Value, out: &mut Vec<Value>) {
    match v {
        Value::Object(m) => {
            for (k, v) in m {
                if k == "tick_ms" {
                    out.push(v.clone());
                }
                tick_ms_values(v, out);
            }
        }
        Value::Array(a) => a.iter().for_each(|v| tick_ms_values(v, out)),
        _ => {}
    }
}

fn set_tick_ms(v: &mut Value, to: u32) -> usize {
    match v {
        Value::Object(m) => {
            let mut n = 0;
            for (k, v) in m.iter_mut() {
                if k == "tick_ms" {
                    *v = Value::from(to);
                    n += 1;
                } else {
                    n += set_tick_ms(v, to);
                }
            }
            n
        }
        Value::Array(a) => a.iter_mut().map(|v| set_tick_ms(v, to)).sum(),
        _ => 0,
    }
}

#[test]
fn the_default_step_is_the_legacy_50_ms() {
    assert_eq!(SimConfig::default().tick_ms, 50);
    let sim = fight(50);
    assert_eq!(sim.tick_ms(), 50);
    // A config written before the field existed reads as 50.
    let mut v = serde_json::to_value(SimConfig::default()).unwrap();
    v.as_object_mut().unwrap().remove("tick_ms");
    let back: SimConfig = serde_json::from_value(v).unwrap();
    assert_eq!(back.tick_ms, 50);
}

#[test]
fn a_config_carries_25_ms_through_json() {
    let v = serde_json::to_value(config(25)).unwrap();
    assert_eq!(
        v.get("tick_ms"),
        Some(&Value::from(25)),
        "a 25 ms config writes its step"
    );
    let back: SimConfig = serde_json::from_value(v).unwrap();
    assert_eq!(back.tick_ms, 25);
}

#[test]
fn a_25_ms_sim_keeps_its_clock_exactly_on_the_grid_for_10000_steps() {
    let mut sim = fight(25);
    assert_eq!(sim.tick_ms(), 25);
    assert_eq!(sim.config().tick_ms, 25);
    let start_tick = sim.tick();
    assert_eq!(sim.server_time(), f64::from(start_tick) * 25.0);
    for n in 0..10_000u32 {
        for (id, c) in inputs(n) {
            let _ = sim.submit(PlayerId(id), c);
        }
        sim.step();
        assert_eq!(
            sim.server_time(),
            f64::from(sim.tick()) * 25.0,
            "step {n}: server_time must be exactly tick * 25"
        );
    }
    assert_eq!(sim.tick(), start_tick + 10_000);
    assert_eq!(sim.server_time(), f64::from(start_tick + 10_000) * 25.0);
}

#[test]
fn a_50_ms_sim_still_advances_50_ms_per_step() {
    let mut sim = fight(50);
    let start_tick = sim.tick();
    drive(&mut sim, 0, 400);
    assert_eq!(sim.tick(), start_tick + 400);
    assert_eq!(sim.server_time(), f64::from(sim.tick()) * 50.0);
}

#[test]
fn a_25_ms_checkpoint_round_trips_with_the_same_hash_and_keeps_stepping_at_25() {
    let mut sim = fight(25);
    drive(&mut sim, 0, 300);
    let cp = serde_json::to_value(&sim).unwrap();
    let mut found = Vec::new();
    tick_ms_values(&cp, &mut found);
    assert!(
        found.contains(&Value::from(25)),
        "a 25 ms checkpoint records its step (keys named tick_ms: {found:?})"
    );
    let mut back = restore(&sim);
    assert_eq!(back.tick_ms(), 25);
    assert_eq!(back.state_hash(), sim.state_hash());
    assert_eq!(back.server_time(), sim.server_time());
    drive(&mut sim, 300, 200);
    drive(&mut back, 300, 200);
    assert_eq!(back.state_hash(), sim.state_hash());
    assert_eq!(back.server_time(), f64::from(back.tick()) * 25.0);
}

#[test]
fn a_50_ms_checkpoint_writes_no_step_value() {
    let mut sim = fight(50);
    drive(&mut sim, 0, 50);
    let mut found = Vec::new();
    tick_ms_values(&serde_json::to_value(&sim).unwrap(), &mut found);
    assert!(
        found.iter().all(Value::is_null),
        "at 50 ms a checkpoint keeps the shape it had before the field (keys named tick_ms: {found:?})"
    );
    let back = restore(&sim);
    assert_eq!(back.tick_ms(), 50);
    assert_eq!(back.state_hash(), sim.state_hash());
}

#[test]
fn the_step_length_reaches_the_state_hash() {
    let (mut a, mut b) = (fight(50), fight(25));
    assert_ne!(
        a.state_hash(),
        b.state_hash(),
        "the same world at 50 and 25 ms hashes differently from construction"
    );
    drive(&mut a, 0, 1);
    drive(&mut b, 0, 1);
    assert_ne!(a.state_hash(), b.state_hash());
    // A 25 ms sim restored as 50 ms is a different match.
    let mut cp = serde_json::to_value(&b).unwrap();
    assert!(set_tick_ms(&mut cp, 50) >= 1);
    let as_50 = Sim::restore(cp).expect("50 is an allowed step");
    assert_eq!(as_50.tick_ms(), 50);
    assert_ne!(as_50.state_hash(), b.state_hash());
}

#[test]
fn any_other_step_length_is_refused_at_construction() {
    for ok in [50, 25] {
        assert!(
            Sim::try_new(config(ok), practice_arena(), Secrets::from_seed(1)).is_ok(),
            "{ok} ms is allowed"
        );
    }
    for bad in [33, 0, 1, 20, 24, 40, 75, 100, 1000, u32::MAX] {
        assert!(
            Sim::try_new(config(bad), practice_arena(), Secrets::from_seed(1)).is_err(),
            "{bad} ms must be refused"
        );
    }
}

#[test]
fn a_checkpoint_with_another_step_length_is_refused() {
    let mut sim = fight(25);
    drive(&mut sim, 0, 10);
    let mut cp = serde_json::to_value(&sim).unwrap();
    assert!(
        set_tick_ms(&mut cp, 33) >= 1,
        "a 25 ms checkpoint has a tick_ms key to edit"
    );
    assert!(
        Sim::restore(cp).is_err(),
        "a checkpoint at 33 ms is refused"
    );
}

#[test]
fn a_25_ms_replay_verifies_at_every_step_and_stays_at_25() {
    let ps = placements();
    let mut sim = fight(25);
    let mut rec = Recorder::new(&sim, config(25), ReplayStart::Active { placements: ps });
    for n in 0..200u32 {
        for (id, c) in inputs(n) {
            let input = ReplayInput::Command {
                player: PlayerId(id),
                cmd: c,
            };
            apply_input(&mut sim, &input);
            rec.record(input);
        }
        sim.step();
        rec.stepped(&sim);
    }
    let r = rec.replay;
    let back: Replay = serde_json::from_str(&serde_json::to_string(&r).unwrap()).unwrap();
    assert_eq!(back.config.tick_ms, 25, "a replay records its step");
    let (replayed, _) = run_replay(&back).expect("the 25 ms recording verifies at every step");
    assert_eq!(replayed.tick_ms(), 25);
    assert_eq!(replayed.state_hash(), sim.state_hash());
    assert_eq!(replayed.server_time(), f64::from(replayed.tick()) * 25.0);
}
