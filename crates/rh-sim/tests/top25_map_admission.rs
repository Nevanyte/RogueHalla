//! Fresh Top 25 simulations admit encounter geometry. Checkpoints must already contain it.
use rh_sim::mapdata::MapExport;
use rh_sim::mapgen::{coliseum, map_hash, pipeline};
use rh_sim::quests::board::map_admission;
use rh_sim::{Arena, Secrets, Sim, SimConfig};
use std::sync::Arc;

fn raw_map() -> Arc<Arena> {
    let fixed = coliseum::fixed_layout();
    let map = pipeline::Pipeline::new(&fixed)
        .generate_styled(
            1,
            Some(0),
            pipeline::Options {
                decor: false,
                ..Default::default()
            },
        )
        .unwrap();
    Arc::new(
        MapExport {
            format: "rh-map/2".into(),
            legacy_map_hash_u32: u32::from_str_radix(&map_hash::map_hash(&map), 16).unwrap(),
            map,
        }
        .validate()
        .unwrap()
        .into_arena()
        .unwrap(),
    )
}

const SECRETS: Secrets = Secrets {
    order_salt: 7,
    spawn_seed: 0,
    reward_salt: [1, 2, 3, 4],
};

#[test]
fn direct_live_constructor_repairs_the_original_arena_and_reuses_its_admitted_arc() {
    let source = raw_map();
    assert_eq!(map_admission::needs_repair(&source, SECRETS), Ok(true));
    let cfg = SimConfig::live_content();
    let sim = Sim::try_new(cfg.clone(), source.clone(), SECRETS).unwrap();
    assert_eq!(sim.arena().map.tiles, source.map.tiles);
    assert_eq!(sim.arena().spawns, source.spawns);
    assert_eq!(map_admission::needs_repair(sim.arena(), SECRETS), Ok(false));
    let admitted = sim.shared_arena();
    let rebuilt = Sim::try_new(cfg, admitted.clone(), SECRETS).unwrap();
    assert!(Arc::ptr_eq(rebuilt.shared_arena(), admitted));
    assert_eq!(rebuilt.state_hash(), sim.state_hash());

    let historical = Sim::try_new(
        SimConfig {
            quests: true,
            ..Default::default()
        },
        source.clone(),
        SECRETS,
    )
    .unwrap();
    assert!(Arc::ptr_eq(historical.shared_arena(), &source));
    assert_eq!(
        map_admission::needs_repair(historical.arena(), SECRETS),
        Ok(true)
    );
}

fn rename_camp(value: &mut serde_json::Value) -> usize {
    match value {
        serde_json::Value::String(id) if id == "quest-target-rime-hunt" => {
            *id = "wrong-encounter".into();
            1
        }
        serde_json::Value::Array(items) => items.iter_mut().map(rename_camp).sum(),
        serde_json::Value::Object(fields) => fields.values_mut().map(rename_camp).sum(),
        _ => 0,
    }
}

#[test]
fn checkpoint_refuses_unaugmented_and_poisoned_top25_maps() {
    let source = raw_map();
    let sim = Sim::try_new(SimConfig::live_content(), source.clone(), SECRETS).unwrap();
    let saved = serde_json::to_value(&sim).unwrap();
    let intact: Sim = serde_json::from_value(saved.clone()).unwrap();
    assert_eq!(intact.state_hash(), sim.state_hash());

    let refuse = |candidate| {
        let failure = serde_json::from_value::<Sim>(candidate).err().unwrap();
        assert!(
            failure
                .to_string()
                .contains("checkpoint quest map is missing"),
            "{failure}"
        );
    };
    let mut missing = saved.clone();
    missing["statics"]["arena"] = serde_json::to_value(source.as_ref()).unwrap();
    refuse(missing);

    let mut poisoned = saved;
    assert_eq!(rename_camp(&mut poisoned["statics"]["arena"]), 2);
    refuse(poisoned);
}
