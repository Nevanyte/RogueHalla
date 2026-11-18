//! Unit tests of the sim-to-wire conversions, with the helpers the sibling test files share (`source_arena`, `seed1_match`, `view`).

mod arena;
mod commands;
mod frames;
mod giver;
mod profiles;
mod snapshot;

use super::*;
use rh_headless::{Agent, AgentSpec, Env, EnvConfig};
use rh_policy::scripted::Brawler;
use rh_protocol::quant::POS_SCALE;
use rh_protocol::{snapshot_server_ms, StateItem, WireArena, WirePlan, TICK_MS};
use rh_sim::{FactionId, SimConfig};

/// The planning-capable source arena: the admitted rh-map/2 export the arena capture uses (spawn pool, coliseum and
/// planning walks), copied without modification into an Arena exactly as the parity harness does.
fn source_arena() -> rh_sim::Arena {
    let text = std::fs::read_to_string(concat!(
        env!("CARGO_MANIFEST_DIR"),
        "/../../parity/arena/source.json"
    ))
    .expect("parity/arena/source.json");
    let source: serde_json::Value = serde_json::from_str(&text).expect("arena capture json");
    let export: rh_sim::mapdata::MapExport =
        serde_json::from_value(source["map"]["export"].clone()).expect("rh-map/2 export");
    let (w, h, tiles, layers) = export.validate().expect("admitted map layers").into_parts();
    let spawns = layers
        .data()
        .spawns
        .iter()
        .map(|p| rh_sim::Vec2::new(p.x, p.y))
        .collect();
    rh_sim::Arena {
        map: rh_sim::TileMap::new(w, h, tiles)
            .expect("map tiles")
            .with_layers(layers)
            .expect("layers fit the tiles"),
        spawns,
    }
}

fn seed1_match(ticks: u32) -> Env {
    let path = concat!(
        env!("CARGO_MANIFEST_DIR"),
        "/../../parity/maps/seed1.map.json"
    );
    let arena = rh_headless::load_arena(path).expect("seed-1 map");
    let agents = (0..8)
        .map(|i| AgentSpec {
            profile: format!("b{i}"),
            faction: FactionId::ALL[i % 5],
            bot: true,
            agent: Agent::Policy {
                policy: Box::new(Brawler::new(2)),
            },
        })
        .collect();
    Env::new(
        EnvConfig {
            arena,
            sim: SimConfig::default(),
            max_ticks: ticks,
            record: false,
        },
        agents,
        1,
    )
}

fn view(id: u32) -> ActorView {
    ActorView {
        archetype: Default::default(),
        kind: rh_sim::observe::ActorKind::Player,
        owner: None,
        mob_type: None,
        effects: 0,
        displace_seq: 0,
        mark_tier: 0,
        brand: 0,
        mark_by: None,
        mark_tax: 0,
        sig: 0,
        id: rh_sim::PlayerId(id),
        faction: FactionId::ALL[1].into(),
        x: 100.3,
        y: 50.7,
        facing: 0.5,
        hp_frac: 0.75,
        barrier_frac: 0.,
        barrier_ring_step: 0,
        status: Status::Alive,
        weapon: Some(rh_sim::content::WeaponId::ALL[2]),
        bounty: 3,
        facts: Default::default(),
    }
}
