//! The normal raid's planning board and neutral givers, measured through the viewer API.
use rh_sim::mapdata::{MapExport, RegionId};
use rh_sim::mapgen::{coliseum, map_hash, pipeline};
use rh_sim::quest_view::{QuestObserver, QuestView};
use rh_sim::{FactionId, Phase, PlayerId, Secrets, Sim, SimConfig};
use std::collections::BTreeSet;

fn board(sim: &Sim, viewer: PlayerId) -> QuestView {
    let mut observer = QuestObserver::new(viewer);
    let pending = observer.prepare(sim).unwrap();
    pending.view.clone()
}

fn home(name: &str) -> RegionId {
    use RegionId::*;
    match name {
        "The Warden" | "Hold the Fort" | "Bounty Hunter" | "Smuggler's Run" | "Two Banners" => {
            Sentenced
        }
        "Ember Walk" | "The Burning Crown" | "Let Him Cook" | "Elite Slayer" | "Tuber Eats" => Fire,
        "Barrow Run" | "The Bargain" | "A Cruel Joke" | "A Bone to Pick" | "Feed or Hunt" => Plague,
        "Rime Hunt"
        | "Watchtowers"
        | "Whispers in the Brush"
        | "Even Skeletons Get Cold"
        | "Lost Brother" => Frost,
        "Pilgrim's Path" | "Gatecrasher" | "Letters" | "Stay A While" | "Trade Up" => Holy,
        _ => panic!("unexpected quest on the normal raid board: {name}"),
    }
}

#[test]
fn ten_public_givers_two_per_home_zone_survive_planning_and_restore() {
    let layout = coliseum::fixed_layout();
    let map = pipeline::Pipeline::new(&layout)
        .generate_styled(
            1,
            Some(0),
            pipeline::Options {
                decor: false,
                ..Default::default()
            },
        )
        .unwrap();
    let arena = MapExport {
        format: "rh-map/2".into(),
        legacy_map_hash_u32: u32::from_str_radix(&map_hash::map_hash(&map), 16).unwrap(),
        map,
    }
    .validate()
    .unwrap()
    .into_arena()
    .unwrap();
    let config = SimConfig {
        min_players: 2,
        lobby_seconds: 0.05,
        fill_seconds: 0.05,
        lifecycle: rh_sim::ArenaMode::Source(rh_sim::ArenaRules {
            plan_seconds: 1.,
            ..Default::default()
        }),
        ..SimConfig::live_content()
    };
    let mut sim = Sim::try_new(
        config,
        arena,
        Secrets {
            order_salt: 7,
            spawn_seed: 0,
            reward_salt: [1, 2, 3, 4],
        },
    )
    .unwrap();
    let a = sim
        .add_player("first", Some(FactionId::Holy), false)
        .unwrap();
    let b = sim
        .add_player("second", Some(FactionId::Fire), false)
        .unwrap();
    for _ in 0..10 {
        sim.step();
        if sim.phase() == Phase::Planning {
            break;
        }
    }
    assert_eq!(sim.phase(), Phase::Planning);
    let planned = board(&sim, a);
    assert_eq!(planned.rows.len(), 10);
    assert_eq!(
        planned.unknown_count, 0,
        "normal raids have no hidden quest roster"
    );
    let positions = |v: &QuestView| {
        v.rows
            .iter()
            .map(|r| (r.def.clone(), r.giver))
            .collect::<Vec<_>>()
    };
    assert_eq!(positions(&planned), positions(&board(&sim, b)));
    assert_eq!(
        planned
            .rows
            .iter()
            .map(|r| &r.def)
            .collect::<BTreeSet<_>>()
            .len(),
        10
    );
    let layers = sim.arena().map.layers().unwrap();
    let mut quotas = [0; 7];
    for row in &planned.rows {
        let actual = layers.region_at_world(row.giver.x, row.giver.y);
        let name = row
            .name
            .replace('’', "'")
            .replace(" (fixed)", "")
            .replace(" (feed path)", "");
        assert_eq!(actual, home(&name), "wrong starting zone for {name}");
        quotas[actual.code() as usize] += 1;
    }
    assert_eq!(quotas, [0, 2, 2, 2, 2, 2, 0]);
    for _ in 0..25 {
        sim.step();
        if sim.phase() == Phase::Active {
            break;
        }
    }
    assert_eq!(sim.phase(), Phase::Active);
    assert_eq!(
        sim.mobs()
            .iter()
            .filter(|m| m.kind == rh_sim::mob_runtime::MobKind::QuestGiver)
            .count(),
        10
    );
    assert_eq!(positions(&planned), positions(&board(&sim, a)));
    let saved = serde_json::to_vec(&sim).unwrap();
    let restored: Sim = serde_json::from_slice(&saved).unwrap();
    assert_eq!(sim.state_hash(), restored.state_hash());
    assert_eq!(positions(&planned), positions(&board(&restored, a)));
}
