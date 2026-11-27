use super::bleed_tests::pve_world;
use crate::mob_runtime::MobKind;
use crate::{CheckpointError, FactionId, Sim};
use serde_json::json;

#[test]
fn regional_mob_restore_admits_only_the_first_same_tick_rewind_pair() {
    let (mut sim, _) = pve_world(&[FactionId::Holy]);
    let mob = sim
        .w
        .mobs
        .iter()
        .find(|m| m.kind == MobKind::Regional)
        .unwrap();
    let (id, at) = (mob.core.id, mob.core.pos());
    let now = sim.server_time();
    let tick_ms = f64::from(sim.tick_ms());
    let map = sim.collision_map();
    let goal = [(16., 0.), (-16., 0.), (0., 16.), (0., -16.)]
        .into_iter()
        .map(|(dx, dy)| crate::Vec2::new(at.x + dx, at.y + dy))
        .find(|goal| {
            crate::geometry::circle_clear(
                map,
                *goal,
                crate::constants::PLAYER_RADIUS,
                crate::constants::MASK_MOVE,
            )
        })
        .expect("a nearby waypoint on the initial arena");
    sim.w
        .mobs
        .get_mut(id)
        .unwrap()
        .core
        .motion
        .path
        .set_from(&[goal]);

    // A reset in the actions phase preserves an existing destination. The real walk and recorder then run that tick.
    crate::systems::displace::apply(&sim.k, &mut sim.w, &mut sim.pf, id, at);
    let ix = sim.w.fighter_idx(id).unwrap();
    assert_eq!(sim.w.core(ix).body.history.len(), 1);
    assert!(
        !sim.w.core(ix).motion.path.is_empty(),
        "displacement keeps the planned walk"
    );
    let mult = crate::systems::weapons::move_multiplier(sim.w.core(ix), now);
    sim.s.standing.push(ix);
    sim.s
        .mults
        .push(crate::systems::movement::floor_multiplier(mult));
    let mut events = Vec::new();
    let mut cx = crate::systems::Cx {
        stats: Default::default(),
        #[cfg(feature = "trusted-status-probes")]
        probe: None,
        k: &sim.k,
        w: &mut sim.w,
        s: &mut sim.s,
        ev: &mut events,
        pf: &mut sim.pf,
    };
    crate::systems::movement::walk(&mut cx);
    crate::systems::movement::record_history(&mut cx);
    let core = sim.w.core(ix);
    let history = core.body.history.as_slice();
    let after = core.pos();
    assert_eq!(history.len(), 2);
    assert_eq!((history[0].t, history[1].t), (now, now));
    assert_ne!(
        after, at,
        "the real walk can follow the retained path on the reset tick"
    );
    assert_eq!(core.position_at(now - 1.), at);
    assert_eq!(core.position_at(now), after);
    assert_eq!(
        crate::systems::movement::tick_velocity(history),
        crate::Vec2::new(0., 0.)
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
    let sample = |t, x| json!({ "t": t, "x": x, "y": at.y });
    for (case, history) in [
        ("empty", json!([])),
        (
            "later duplicate",
            json!([
                sample(now - tick_ms, at.x),
                sample(now, at.x),
                sample(now, at.x)
            ]),
        ),
        (
            "off-grid short gap",
            json!([sample(now - 1., at.x), sample(now, at.x)]),
        ),
        (
            "descending",
            json!([sample(now, at.x), sample(now - 1., at.x)]),
        ),
        (
            "negative time",
            json!([sample(-1., at.x), sample(now, at.x)]),
        ),
        (
            "future",
            json!([sample(now, at.x), sample(now + tick_ms, at.x)]),
        ),
        (
            "outside the arena",
            json!([sample(now, -100_000.), sample(now, at.x)]),
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
