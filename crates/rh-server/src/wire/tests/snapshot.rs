//! Snapshot publishing: content-code widths, server time, the Publisher and the actor memo, position bounds, `hp_pct`.

use super::*;

/// A faction or weapon code rides in a 4-bit nibble, a status or phase code in 2 bits: adding content past that is a
/// protocol change, and this test is where it shows up first.
#[test]
fn content_codes_fit_the_wire_bit_widths() {
    assert!(FactionId::ALL.len() as u8 <= rh_protocol::MAX_KIT_CODE + 1);
    assert!(rh_sim::content::WeaponId::ALL.len() as u8 <= rh_protocol::MAX_KIT_CODE + 1);
    for s in [Status::Lobby, Status::Alive, Status::Dead, Status::Mia] {
        assert!(status(s) <= rh_protocol::MAX_STATUS_CODE);
    }
    for p in [Phase::Lobby, Phase::Active, Phase::Ended] {
        assert!(phase(p) <= rh_protocol::MAX_PHASE_CODE);
    }
}

/// Ordinary source clocks equal `tick * 50 ms`; protocol 12 only needs the absolute clock override when that default
/// does not match the observation's source time.
#[test]
fn server_time_is_tick_times_50() {
    assert_eq!(rh_sim::constants::TICK_MS, TICK_MS as f64);
    let mut env = seed1_match(600);
    let ids: Vec<_> = env.sim.players().iter().map(|p| p.id).collect();
    for _ in 0..600 {
        let out = env.step(&[]);
        assert_eq!(
            env.sim.server_time(),
            env.sim.tick() as f64 * rh_sim::constants::TICK_MS
        );
        assert_eq!(env.sim.server_time(), snapshot_server_ms(env.sim.tick()));
        for id in &ids {
            let obs = rh_sim::observe(&env.sim, *id, &out.events);
            assert_eq!(obs.server_time, snapshot_server_ms(obs.tick));
        }
        if out.terminated {
            break;
        }
    }
    assert!(env.sim.tick() > 0);
}

/// The snapshot the Publisher hands a connection (its actors checked once for all viewers and remembered across ticks) is
/// the one built straight from the observation, and its Full frame is the message's frame, byte for byte, over a whole
/// match (every viewer, every tick).
#[test]
fn the_published_snapshot_is_the_one_built_from_the_observation_over_a_match() {
    let mut env = seed1_match(900);
    let ids: Vec<_> = env.sim.players().iter().map(|p| p.id).collect();
    let mut publisher = Publisher::default();
    let (mut frames, mut with_actors) = (0, 0);
    let mut spare = None;
    for t in 0..900u32 {
        let out = env.step(&[]);
        for id in &ids {
            let f = publisher.frames_in(
                spare.take(),
                &env.sim,
                *id,
                t,
                &out.events,
                &mut Vec::new(),
                &out.events,
            );
            let got = f.snapshot.expect("builds");
            let obs = publisher.observation();
            let mut want = SnapshotData::build(
                obs.tick,
                phase(obs.phase),
                t,
                obs.me.as_ref().map(wire_self),
                obs.visible.iter().map(|a| wire_actor(a).unwrap()),
            )
            .expect("builds");
            want.set_owner_combat(obs.owner_combat.map(wire_owner_combat))
                .expect("the same checked current-scene owner group");
            assert_eq!(got, want);
            let mut full = Vec::new();
            got.encode_full_into(&mut full);
            assert_eq!(full, snapshot(obs, t).try_encode().expect("encodes"));
            frames += 1;
            with_actors += !got.actors().is_empty() as u32;
            // A recycled block builds the same snapshot.
            spare = Some(got);
        }
    }
    assert!(frames > 1000 && with_actors > 300, "{frames} {with_actors}");
}

/// The memo is a cache of a pure function: whatever it holds, `get` is what a fresh conversion gives, for a hit, for every
/// field that changes (a miss), for the values that compare equal but convert differently (`0.0` and `-0.0`), for a
/// refused actor (and the actor after it is good again), and for an id past the memo.
#[test]
fn the_actor_memo_is_always_a_fresh_conversion() {
    let mut memo = ActorMemo::default();
    let mut a = view(5);
    type Edit = Box<dyn Fn(&mut ActorView)>;
    let edits: Vec<(&str, Edit)> = vec![
        ("same", Box::new(|_| {})),
        ("sig", Box::new(|a| a.sig = 255)),
        (
            "mob type",
            Box::new(|a| a.mob_type = Some(rh_sim::mob_catalog::MobTypeId::Archon)),
        ),
        (
            "kind",
            Box::new(|a| a.kind = rh_sim::observe::ActorKind::Regional),
        ),
        ("effects", Box::new(|a| a.effects = 4096)),
        ("displacement", Box::new(|a| a.displace_seq = 255)),
        ("x", Box::new(|a| a.x = 101.25)),
        ("y", Box::new(|a| a.y = 51.0)),
        ("facing", Box::new(|a| a.facing = -2.0)),
        ("hp", Box::new(|a| a.hp_frac = 0.01)),
        ("barrier share", Box::new(|a| a.barrier_frac = 0.25)),
        ("barrier ring", Box::new(|a| a.barrier_ring_step = 4)),
        ("bounty", Box::new(|a| a.bounty = 70_000)),
        ("status", Box::new(|a| a.status = Status::Dead)),
        (
            "faction",
            Box::new(|a| a.faction = FactionId::ALL[3].into()),
        ),
        (
            "weapon",
            Box::new(|a| a.weapon = Some(rh_sim::content::WeaponId::ALL[4])),
        ),
        ("zero", Box::new(|a| a.x = 0.0)),
        ("negative zero", Box::new(|a| a.x = -0.0)),
        ("off the grid", Box::new(|a| a.x = 1.0e9)),
        ("nan", Box::new(|a| a.y = f64::NAN)),
        ("good again", Box::new(|a| (a.x, a.y) = (10.0, 10.0))),
    ];
    for (name, edit) in edits {
        edit(&mut a);
        for round in 0..3 {
            assert_eq!(memo.get(&a), checked_actor(&a), "{name} round {round}");
        }
        // Another id never aliases it.
        let other = view(6);
        assert_eq!(memo.get(&other), checked_actor(&other), "{name}: other id");
        assert_eq!(memo.get(&a), checked_actor(&a), "{name}: after another id");
    }
    assert_eq!(checked_actor(&view(1)).map(|q| q.id), Ok(1));
    assert_eq!(
        checked_actor(&ActorView {
            x: 1.0e9,
            ..view(1)
        }),
        Err("position")
    );
    for id in [MEMO_IDS as u32 - 1, MEMO_IDS as u32, MEMO_IDS as u32 + 1000] {
        let v = view(id);
        assert_eq!(memo.get(&v), checked_actor(&v), "id {id}");
        assert_eq!(memo.get(&v), checked_actor(&v), "id {id} again");
    }
    assert!(memo.slots.len() <= MEMO_IDS);
}

/// The seed-1 160 x 160 map (and the nominal world) fits the u16 position grid, and over a whole match every remote
/// actor a client decodes is within the documented error of the observation (position 1/16 u, facing 4.85e-5 rad),
/// with the same ids, in the same order, and everything else exact (C-OBS: only the encoding changed).
#[test]
fn remote_actors_decode_within_the_documented_bound_over_a_match() {
    let mut env = seed1_match(1200);
    let tile = rh_sim::constants::TILE as f32;
    assert_eq!(env.sim.arena().map.w, 160);
    assert_eq!(env.sim.arena().map.h, 160);
    assert!(env.sim.arena().map.w as f32 * tile * POS_SCALE <= 65535.0);
    assert!(env.sim.arena().map.h as f32 * tile * POS_SCALE <= 65535.0);
    assert!(rh_sim::constants::WORLD_W as f32 <= POS_MAX);
    assert!(rh_sim::constants::WORLD_H as f32 <= POS_MAX);
    let ids: Vec<_> = env.sim.players().iter().map(|p| p.id).collect();
    let (mut actors_seen, mut worst_pos, mut worst_facing) = (0usize, 0f64, 0f64);
    for _ in 0..1200 {
        let out = env.step(&[]);
        for id in &ids {
            let obs = rh_sim::observe(&env.sim, *id, &out.events);
            let ServerMsg::Snapshot {
                tick,
                server_time,
                me,
                actors,
                phase: p,
                ack_input,
                training: _,
                owner_combat: _,
            } = ServerMsg::decode(&snapshot(&obs, 9).encode()).expect("decodes")
            else {
                panic!("a snapshot")
            };
            assert_eq!((tick, ack_input, p), (obs.tick, 9, phase(obs.phase)));
            assert_eq!(server_time, obs.server_time);
            // Self is exact (the f32 of the sim's f64, as before).
            assert_eq!(
                me.map(|m| (m.x, m.y)),
                obs.me.as_ref().map(|m| (m.x as f32, m.y as f32))
            );
            assert_eq!(actors.len(), obs.visible.len());
            for (w, a) in actors.iter().zip(&obs.visible) {
                actors_seen += 1;
                assert_eq!(w.id, a.id.0);
                assert_eq!(w.faction, a.faction.faction_id().unwrap().code());
                assert_eq!(w.status, status(a.status));
                assert_eq!(w.weapon, a.weapon.unwrap().code());
                assert_eq!(w.bounty, sat16(a.bounty));
                assert_eq!(w.hp_pct, hp_pct(a.hp_frac));
                worst_pos = worst_pos
                    .max((w.x as f64 - a.x).abs())
                    .max((w.y as f64 - a.y).abs());
                let tau = std::f64::consts::TAU;
                let mut d = (w.facing as f64 - a.facing).rem_euclid(tau);
                if d > tau / 2.0 {
                    d -= tau;
                }
                worst_facing = worst_facing.max(d.abs());
            }
        }
    }
    assert!(
        actors_seen > 1000,
        "the match must show remote actors: {actors_seen}"
    );
    // 1/16 u from the grid, plus the f32 rounding of the sim's f64 position (under 0.001 at 5120 u).
    assert!(worst_pos <= 0.0626, "position error {worst_pos}");
    assert!(worst_facing <= 4.9e-5, "facing error {worst_facing}");
}

/// `hp_pct` is the `round` expression it replaced for every fraction a match produces, ties and edges included.
#[test]
fn hp_pct_matches_the_f64_round_reference() {
    let reference = |f: f64| (f * 100.0).round().clamp(0.0, 100.0) as u8;
    for i in 0..=200_000u32 {
        let f = i as f64 / 100_000.0 - 0.5;
        assert_eq!(hp_pct(f), reference(f), "{f}");
    }
    for k in 0..=100u32 {
        for d in [-1i64, 0, 1] {
            let tie = (k as f64 + 0.5) / 100.0;
            let f = f64::from_bits((tie.to_bits() as i64 + d) as u64);
            assert_eq!(hp_pct(f), reference(f), "{f}");
        }
    }
    for f in [
        f64::NAN,
        f64::INFINITY,
        f64::NEG_INFINITY,
        -0.0,
        1e300,
        -1e300,
    ] {
        assert_eq!(hp_pct(f), reference(f), "{f}");
    }
}
