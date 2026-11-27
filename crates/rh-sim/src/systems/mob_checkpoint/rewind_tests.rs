use super::bleed_tests::{pve_config_with, seed1_arena};
use crate::abilities::{Mode, Slot};
use crate::archetype::HitboxMode;
use crate::input::{validate, RawCommand};
use crate::mob_runtime::MobKind;
use crate::{CheckpointError, FactionId, FrostWish, Secrets, Sim, SimConfig, Vec2};
use serde_json::json;

#[test]
fn regional_mob_restore_admits_only_the_first_same_tick_rewind_pair() {
    let mut sim = Sim::try_new(
        SimConfig {
            abilities: Mode::Full,
            frost_wish: FrostWish::Wall,
            wish_lock_ms: 0.,
            tick_ms: 25,
            ..pve_config_with(HitboxMode::LegacyCircle)
        },
        seed1_arena(),
        Secrets::from_seed(5),
    )
    .unwrap();
    let frost = sim
        .add_player("rewind-test", Some(FactionId::Frost), false)
        .unwrap();
    sim.step();

    // A clear strip of the seed1 arena runs west from the wall target's tile centre.
    let target = Vec2::new(1264., 1296.);
    let caster = sim.w.players.get_mut(frost).unwrap();
    caster.core.body.x = target.x - 96.;
    caster.core.body.y = target.y;
    let cast = validate(RawCommand::Cast {
        slot: Slot::Wish,
        x: target.x,
        y: target.y,
        id: None,
        t: None,
        seq: None,
    })
    .unwrap();
    sim.submit(frost, cast).unwrap();
    sim.step();
    let lands_at = sim
        .w
        .players
        .get(frost)
        .unwrap()
        .core
        .statuses
        .cast
        .expect("Frost Wall is in flight")
        .lands_at;
    let tick_ms = f64::from(sim.tick_ms());
    while sim.server_time() + tick_ms < lands_at {
        sim.step();
    }

    // Arrange the regional mob inside the wall's footprint on the caster's side, already pathing farther west.
    let mob = sim
        .w
        .mobs
        .iter_mut()
        .find(|m| m.kind == MobKind::Regional)
        .unwrap();
    let id = mob.core.id;
    let before = Vec2::new(target.x - 4., target.y);
    let goal = Vec2::new(target.x - 64., target.y);
    mob.core.body.x = before.x;
    mob.core.body.y = before.y;
    mob.core.inputs = Default::default();
    mob.core.motion.path.set_from(&[goal]);
    sim.step();

    let now = sim.server_time();
    let core = &sim.w.mobs.get(id).unwrap().core;
    let history = core.body.history.as_slice();
    let after = core.pos();
    assert_eq!(history.len(), 2);
    assert_eq!((history[0].t, history[1].t), (now, now));
    let reset = Vec2::new(history[0].x, history[0].y);
    assert_ne!(reset, before, "Frost Wall displaces the mob on landing");
    assert_ne!(
        after, reset,
        "the mob walks after the reset on the same tick"
    );
    assert_eq!(core.position_at(now - 1.), reset);
    assert_eq!(core.position_at(now), after);
    assert_eq!(
        crate::systems::movement::tick_velocity(history),
        Vec2::new(0., 0.)
    );

    let checkpoint = serde_json::to_value(&sim).unwrap();
    let restored =
        Sim::restore(&checkpoint).expect("a real reset and same-tick movement sample restore");
    assert_eq!(restored.state_hash(), sim.state_hash());

    let mob_slot = checkpoint["world"]["mobs"]
        .as_array()
        .unwrap()
        .iter()
        .position(|m| m["core"]["id"] == json!(id.0))
        .unwrap();
    let sample = |t, x| json!({ "t": t, "x": x, "y": reset.y });
    for (case, history) in [
        ("empty", json!([])),
        (
            "later duplicate",
            json!([
                sample(now - tick_ms, reset.x),
                sample(now, reset.x),
                sample(now, reset.x)
            ]),
        ),
        (
            "off-grid short gap",
            json!([sample(now - 1., reset.x), sample(now, reset.x)]),
        ),
        (
            "descending",
            json!([sample(now, reset.x), sample(now - 1., reset.x)]),
        ),
        (
            "negative time",
            json!([sample(-1., reset.x), sample(now, reset.x)]),
        ),
        (
            "future",
            json!([sample(now, reset.x), sample(now + tick_ms, reset.x)]),
        ),
        (
            "outside the arena",
            json!([sample(now, -100_000.), sample(now, reset.x)]),
        ),
    ] {
        let mut malformed = checkpoint.clone();
        malformed["world"]["mobs"][mob_slot]["core"]["body"]["history"] = history;
        assert_eq!(
            Sim::restore(&malformed).unwrap_err(),
            CheckpointError::Malformed("invalid mob rewind".into()),
            "{case}",
        );
    }
}
