//! P-COLISEUM-HEALS-1 oracle: the four heal pickups at the grand coliseum centre (owner request 2026-11-19, "should
//! heal 40% HP overtime"). Loose proof (no legacy counterpart): the heal is exactly the legacy Large potion's Hot
//! (`potions::DEFINITION`), a pickup returns after `DEFINITION.respawn_ms`, same-tick competitions follow the wind
//! emblem's legacy rule (nearest, then `tie_rank_origin`), and legacy worlds are unchanged.
use super::{spots, DEFINITION};
use crate::constants::{MASK_MOVE, PLAYER_RADIUS, TILE};
use crate::potions::{Healing, Hot, DEFINITION as POTION};
use crate::sim::{Placement, Secrets, Sim, SimConfig};
use crate::state::ContainerKind;
use crate::{Arena, FactionId, Vec2};
use serde_json::json;
use std::sync::Arc;

fn arena_of(native: crate::mapdata::GameMapData) -> Arena {
    let hash = u32::from_str_radix(&crate::mapgen::map_hash::map_hash(&native), 16).unwrap();
    let v = crate::mapdata::MapExport {
        format: "rh-map/2".into(),
        legacy_map_hash_u32: hash,
        map: native,
    }
    .validate()
    .unwrap();
    let (w, h, t, l) = v.into_parts();
    let spawns = l
        .data()
        .spawns
        .iter()
        .map(|p| Vec2::new(p.x, p.y))
        .collect();
    Arena {
        map: crate::TileMap::new(w, h, t)
            .unwrap()
            .with_layers(Arc::new(l))
            .unwrap(),
        spawns,
    }
}

/// A certified styled map around the grand coliseum (the server's options: decor off). Built once per test and passed
/// along: no static (the source-tree const scanner in `tests/content.rs` counts statics).
fn grand_arena() -> Arc<Arena> {
    let f = crate::mapgen::coliseum::grand_layout();
    let options = crate::mapgen::pipeline::Options {
        decor: false,
        ..crate::mapgen::pipeline::Options::default()
    };
    let native = (1..=4)
        .find_map(|seed| {
            crate::mapgen::pipeline::Pipeline::new(&f)
                .certified_styled(seed, options)
                .expect("the generator runs")
        })
        .expect("a grand map certifies on seeds 1..=4");
    Arc::new(arena_of(native))
}

/// A legacy-coliseum map (the initial-loot parity input).
fn legacy_arena() -> Arena {
    arena_of(
        serde_json::from_str(include_str!(
            "../../../../parity/initial-loot/input-map.json"
        ))
        .unwrap(),
    )
}

/// The four pickup positions the brief names: tiles (79,79), (80,79), (79,80), (80,80), centres, in that order.
fn want_spots() -> [Vec2; 4] {
    let c = |t: f64| (t + 0.5) * TILE;
    [
        Vec2::new(c(79.), c(79.)),
        Vec2::new(c(80.), c(79.)),
        Vec2::new(c(79.), c(80.)),
        Vec2::new(c(80.), c(80.)),
    ]
}

fn placement(i: usize, at: Vec2) -> Placement {
    Placement {
        profile: format!("heal{i}"),
        faction: FactionId::Fire,
        bot: false,
        at,
        hp: None,
        weapon0: None,
        weapon1: None,
    }
}

/// A grand-map run, active, with `n` players parked far from the centre (on the map's first spawns).
fn grand_sim(arena: &Arc<Arena>, seed: u64, n: usize) -> Sim {
    let arena = arena.clone();
    let at: Vec<Placement> = (0..n)
        .map(|i| placement(i, arena.spawns[i * 3 % arena.spawns.len()]))
        .collect();
    let s = Sim::reset_active(SimConfig::default(), arena, Secrets::from_seed(seed), &at);
    assert_eq!(s.w.phase, crate::Phase::Active, "the run is active");
    assert_eq!(heals(&s).len(), 4, "four heal pickups in a grand world");
    s
}

fn heals(s: &Sim) -> Vec<crate::state::Container> {
    s.containers()
        .iter()
        .filter(|c| c.kind == ContainerKind::Heal)
        .cloned()
        .collect()
}

/// Puts player `slot` at `at`, standing still, at `share` of its max hp.
fn put(s: &mut Sim, slot: usize, at: Vec2, share: f64) {
    let now = s.server_time();
    let p = &mut s.w.players[slot];
    p.body.x = at.x;
    p.body.y = at.y;
    p.motion.path.clear();
    p.body.history.reset(crate::state::Sample {
        t: now,
        x: at.x,
        y: at.y,
    });
    p.vitals.hp = p.vitals.max_hp * share;
}

fn hp_share(s: &Sim, slot: usize) -> f64 {
    let p = &s.players()[slot];
    p.vitals.hp / p.vitals.max_hp
}

/// Steps until the server clock reads `t` (exactly: the clock moves in 50 ms steps).
fn step_to(s: &mut Sim, t: f64) {
    while s.server_time() < t {
        s.step_into(&mut Vec::new());
    }
    assert_eq!(s.server_time(), t, "the clock lands on {t}");
}

fn far(s: &Sim, slot: usize) -> Vec2 {
    let a = &s.k.arena;
    a.spawns[(slot * 3 + 1) % a.spawns.len()]
}

#[test]
fn the_rule_numbers_are_the_lane_defaults() {
    assert_eq!(DEFINITION.respawn_ms, 30_000.0, "30 s, the owner's dial");
    assert_eq!(DEFINITION.reach, 20.0, "a 12 u body plus 8 u");
    // A body touches at most two pickups (they are a tile apart) and none from the exact cluster centre.
    assert!(DEFINITION.reach < TILE && DEFINITION.reach < TILE * std::f64::consts::FRAC_1_SQRT_2);
}

/// Four pickups on the grand map, on open floor around the coliseum centre, in NW, NE, SW, SE order; none on a legacy
/// map or the practice arena (no layers).
#[test]
fn pickups_stand_at_the_grand_centre_and_nowhere_else() {
    let arena = grand_arena();
    let got = spots(&arena);
    assert_eq!(got, Some(want_spots()), "no heal spots on the grand map");
    let centre = arena.map.layers().unwrap().coliseum().centre;
    for (k, p) in want_spots().iter().enumerate() {
        assert_eq!(
            ((p.x - centre.x).abs(), (p.y - centre.y).abs()),
            (TILE / 2., TILE / 2.),
            "spot {k} is half a tile from the coliseum centre"
        );
        assert!(
            crate::geometry::circle_clear(&arena.map, *p, PLAYER_RADIUS, MASK_MOVE),
            "spot {k} is open floor for a body"
        );
    }
    let s = grand_sim(&arena, 7, 2);
    let h = heals(&s);
    assert_eq!(h.len(), 4, "four heal pickups in a grand world");
    for (k, c) in h.iter().enumerate() {
        assert_eq!(c.at, want_spots()[k], "pickup {k} position and order");
        assert!(c.items.is_empty() && !c.opened && c.target.is_none());
        assert_eq!(c.ready_at, 0.0, "available from the start");
    }
    assert!(
        h.windows(2).all(|w| w[1].id == w[0].id + 1),
        "consecutive ids in NW, NE, SW, SE order"
    );
    assert!(
        s.containers()
            .iter()
            .filter(|c| c.kind != ContainerKind::Heal && c.born_tick == 0)
            .all(|c| c.id < h[0].id),
        "allocated after every construction-time container"
    );

    assert_eq!(spots(&legacy_arena()), None, "legacy coliseum");
    assert_eq!(spots(&crate::arenas::practice_arena()), None, "no layers");
    let legacy = Sim::reset_active(
        SimConfig::default(),
        legacy_arena(),
        Secrets::from_seed(7),
        &[placement(0, Vec2::new(112., 112.))],
    );
    assert!(heals(&legacy).is_empty(), "no pickup in a legacy world");
}

/// The loose proof the lane asked for: a player at 50% who walks onto a pickup is at 90% five seconds later (the Large
/// potion's 40% in ten 500 ms steps), the stock is untouched, the pickup is gone for 30 s and then taken again.
#[test]
fn a_heal_restores_forty_percent_over_five_seconds_and_returns_after_thirty() {
    let arena = grand_arena();
    let mut s = grand_sim(&arena, 11, 2);
    let spot = want_spots()[0];
    let away = far(&s, 1);
    put(&mut s, 0, spot, 0.5);
    put(&mut s, 1, away, 0.5);
    let stock = (s.players()[0].potion_small, s.players()[0].potion_large);
    s.step_into(&mut Vec::new());
    let t0 = s.server_time();
    assert_eq!(
        heals(&s)[0].ready_at,
        t0 + DEFINITION.respawn_ms,
        "taken: unavailable for 30 s"
    );
    assert!(
        heals(&s)[1..].iter().all(|c| c.ready_at == 0.0),
        "only the touched pickup is taken"
    );
    let p = &s.players()[0];
    assert_eq!((p.potion_small, p.potion_large), stock, "not stored");
    assert_eq!(p.potions_healing, 1, "one heal running");
    assert_eq!(p.healing_until, t0 + POTION.heal_ms);
    let e =
        s.w.potion_healing
            .iter()
            .find(|e| e.id == p.id)
            .expect("the potion Hot machinery runs it");
    let steps = POTION.heal_ms / POTION.step_ms;
    assert_eq!(
        e.hots,
        vec![Hot {
            next: t0 + POTION.step_ms,
            left: steps as u8,
            amount: p.vitals.max_hp * POTION.large_share / steps,
        }],
        "exactly the Large potion's Hot"
    );
    // Off the pickup at once: the rest is the Hot alone.
    let (away0, now0) = (far(&s, 0), hp_share(&s, 0));
    put(&mut s, 0, away0, now0);
    step_to(&mut s, t0 + POTION.heal_ms - 50.);
    assert!(
        hp_share(&s, 0) < 0.87,
        "still healing at 4.95 s: {}",
        hp_share(&s, 0)
    );
    step_to(&mut s, t0 + POTION.heal_ms);
    assert!(
        (hp_share(&s, 0) - 0.9).abs() < 1e-9,
        "50% + 40% after 5 s, got {}",
        hp_share(&s, 0)
    );
    assert_eq!(s.players()[0].potions_healing, 0, "the heal is over");
    // A damaged player waits on the pickup: nothing until the clock reaches it, then it is taken again.
    put(&mut s, 1, spot, 0.5);
    step_to(&mut s, t0 + DEFINITION.respawn_ms - 50.);
    assert_eq!(heals(&s)[0].ready_at, t0 + DEFINITION.respawn_ms);
    assert_eq!(
        s.players()[1].potions_healing,
        0,
        "unavailable pickup ignored"
    );
    assert_eq!(hp_share(&s, 1), 0.5);
    s.step_into(&mut Vec::new());
    assert_eq!(s.server_time(), t0 + DEFINITION.respawn_ms);
    assert_eq!(s.players()[1].potions_healing, 1, "available again at 30 s");
    assert_eq!(
        heals(&s)[0].ready_at,
        s.server_time() + DEFINITION.respawn_ms
    );
}

/// Who does not take a pickup: a full-hp player, one with a potion heal running, one just out of reach; and a body
/// touching two pickups takes only the first in container order.
#[test]
fn full_busy_and_distant_players_leave_it_and_one_body_takes_one() {
    let arena = grand_arena();
    let mut s = grand_sim(&arena, 13, 4);
    let sp = want_spots();
    // NW: full hp. NE: a potion heal running. SW: 0.5 u beyond reach. SE: within reach.
    put(&mut s, 0, sp[0], 1.0);
    put(&mut s, 1, sp[1], 0.5);
    let (id, now) = (s.players()[1].id, s.server_time());
    s.w.potion_healing.push(Healing {
        id,
        hots: vec![Hot {
            next: now + POTION.step_ms,
            left: 10,
            amount: 1.0,
        }],
    });
    s.w.players[1].potions_healing = 1;
    s.w.players[1].healing_until = now + POTION.step_ms * 10.;
    put(
        &mut s,
        2,
        Vec2::new(sp[2].x - DEFINITION.reach - 0.5, sp[2].y),
        0.5,
    );
    put(
        &mut s,
        3,
        Vec2::new(sp[3].x + DEFINITION.reach - 0.5, sp[3].y),
        0.5,
    );
    s.step_into(&mut Vec::new());
    let h = heals(&s);
    assert_eq!(h[0].ready_at, 0.0, "a full-hp player leaves it");
    assert_eq!(h[1].ready_at, 0.0, "a player already healing leaves it");
    assert_eq!(h[2].ready_at, 0.0, "out of reach");
    assert_eq!(
        h[3].ready_at,
        s.server_time() + DEFINITION.respawn_ms,
        "within reach"
    );
    assert_eq!(s.players()[3].potions_healing, 1);

    // One body between NW and NE (16 u from both) takes NW only.
    let mut s = grand_sim(&arena, 13, 2);
    put(&mut s, 0, Vec2::new((sp[0].x + sp[1].x) / 2., sp[0].y), 0.5);
    let away1 = far(&s, 1);
    put(&mut s, 1, away1, 1.0);
    s.step_into(&mut Vec::new());
    let h = heals(&s);
    assert_eq!(
        h[0].ready_at,
        s.server_time() + DEFINITION.respawn_ms,
        "first in order"
    );
    assert_eq!(h[1].ready_at, 0.0, "one pickup per body");
    assert_eq!(s.players()[0].potions_healing, 1);
}

/// Two players touching one pickup in the same tick: the nearer takes it; at equal distance the lower
/// `tie_rank_origin` (the wind emblem's legacy rule), so over a sweep of seeds either player wins sometimes.
#[test]
fn same_tick_competitions_go_to_the_nearest_then_the_tie_rank() {
    let arena = grand_arena();
    let sp = want_spots();
    let mut s = grand_sim(&arena, 17, 2);
    put(&mut s, 0, Vec2::new(sp[0].x - 10., sp[0].y), 0.5);
    put(&mut s, 1, Vec2::new(sp[0].x + 4., sp[0].y), 0.5);
    s.step_into(&mut Vec::new());
    assert_eq!(
        (
            s.players()[0].potions_healing,
            s.players()[1].potions_healing
        ),
        (0, 1),
        "the nearer body takes it"
    );

    let mut wins = [0u32; 2];
    for seed in 1..=12 {
        let mut s = grand_sim(&arena, seed, 2);
        put(&mut s, 0, sp[3], 0.5);
        put(&mut s, 1, sp[3], 0.5);
        let tick = s.w.tick + 1;
        let rank = |slot: usize| {
            crate::rng::tie_rank_origin(
                s.k.secrets.order_salt,
                tick,
                s.w.actor_origin(s.w.players[slot].id).unwrap(),
            )
        };
        let want = if rank(0) < rank(1) { 0 } else { 1 };
        s.step_into(&mut Vec::new());
        assert_eq!(s.w.tick, tick);
        let got: Vec<u8> = (0..2).map(|i| s.players()[i].potions_healing).collect();
        let mut expect = vec![0u8, 0];
        expect[want] = 1;
        assert_eq!(got, expect, "seed {seed}: the lower tie rank takes it");
        wins[want] += 1;
    }
    assert!(
        wins[0] >= 1 && wins[1] >= 1,
        "the tie is not join order: {wins:?}"
    );
}

/// The pickups survive a checkpoint, and `check_world` refuses each broken shape (one refusal per rule).
#[test]
fn checkpoints_keep_the_pickups_and_refuse_broken_ones() {
    let arena = grand_arena();
    let mut s = grand_sim(&arena, 19, 2);
    put(&mut s, 0, want_spots()[2], 0.5);
    s.step_into(&mut Vec::new());
    assert_eq!(
        heals(&s)[2].ready_at,
        s.server_time() + DEFINITION.respawn_ms
    );
    let cp = serde_json::to_value(&s).unwrap();
    let back = Sim::restore(&cp).expect("a grand world with a taken pickup restores");
    assert_eq!(back.state_hash(), s.state_hash());
    assert_eq!(heals(&back), heals(&s));

    let index = |kind_k: usize| {
        let ids: Vec<u32> = heals(&s).iter().map(|c| c.id).collect();
        s.containers()
            .iter()
            .position(|c| c.id == ids[kind_k])
            .unwrap()
    };
    let refused = |edit: &dyn Fn(&mut serde_json::Value), why: &str| {
        let mut bad = cp.clone();
        edit(&mut bad);
        assert!(Sim::restore(&bad).is_err(), "refused: {why}");
    };
    let (i0, i2) = (index(0), index(2));
    let now = s.server_time();
    refused(
        &|v| v["world"]["containers"][i2]["ready_at"] = json!(now + DEFINITION.respawn_ms + 50.),
        "a clock past the respawn",
    );
    refused(
        &|v| v["world"]["containers"][i0]["ready_at"] = json!(-50.),
        "a negative clock",
    );
    refused(
        &|v| v["world"]["containers"][i0]["at"]["x"] = json!(want_spots()[0].x + TILE),
        "a moved pickup",
    );
    refused(
        &|v| {
            v["world"]["containers"].as_array_mut().unwrap().remove(i0);
        },
        "a missing pickup",
    );
    refused(
        &|v| v["world"]["containers"][i0]["opened"] = json!(true),
        "an opened pickup",
    );
    let item = s
        .containers()
        .iter()
        .flat_map(|c| c.items.iter())
        .next()
        .map(|i| serde_json::to_value(i).unwrap());
    if let Some(item) = item {
        refused(
            &|v| v["world"]["containers"][i0]["items"] = json!([item.clone()]),
            "a pickup holding an item",
        );
    }

    // A heal pickup smuggled into a legacy world is refused, where the same world without it restores.
    let legacy = Sim::reset_active(
        SimConfig::default(),
        legacy_arena(),
        Secrets::from_seed(19),
        &[placement(0, Vec2::new(112., 112.))],
    );
    let mut cp = serde_json::to_value(&legacy).unwrap();
    assert!(Sim::restore(&cp).is_ok());
    let next = legacy.w.entity_counter + 1;
    let mut row = serde_json::to_value(&heals(&s)[0]).unwrap();
    row["id"] = json!(next);
    row["ready_at"] = json!(0.0);
    cp["world"]["entity_counter"] = json!(next);
    cp["world"]["containers"].as_array_mut().unwrap().push(row);
    assert!(Sim::restore(&cp).is_err(), "no heal pickup on a legacy map");
}

/// Legacy worlds are unchanged: no pickup, and the state hash of a legacy-map run measured on the packet's base
/// (before the pickups existed). A changed value means the implementation moved a legacy world.
#[test]
fn legacy_worlds_are_unchanged() {
    let mut s = Sim::reset_active(
        SimConfig::default(),
        legacy_arena(),
        Secrets::from_seed(23),
        &[
            placement(0, Vec2::new(112., 112.)),
            placement(1, Vec2::new(176., 112.)),
        ],
    );
    s.w.players[0].vitals.hp *= 0.5;
    for _ in 0..200 {
        s.step_into(&mut Vec::new());
    }
    assert!(heals(&s).is_empty());
    // Measured on packet/P-COLISEUM-HEALS-1's stub (no pickup code; the appended `ContainerKind::Heal` moves nothing).
    let legacy_hash: u64 = 0xba40_e6d0_daec_05d9;
    assert_eq!(s.state_hash(), legacy_hash, "{:#018x}", s.state_hash());
}
