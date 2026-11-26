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
fn five_known_givers_expand_to_ten_separated_givers_at_raid_start() {
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
    assert_eq!(planned.rows.len(), 5);
    assert_eq!(
        planned.unknown_count, 5,
        "one additional offer per zone opens at raid start"
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
        5
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
    assert_eq!(quotas, [0, 1, 1, 1, 1, 1, 0]);
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
    let active = board(&sim, a);
    assert_eq!(active.rows.len(), 10);
    assert_eq!(active.unknown_count, 0);
    let all = positions(&active);
    assert!(positions(&planned).iter().all(|row| all.contains(row)));
    assert_eq!(
        active
            .rows
            .iter()
            .map(|r| &r.def)
            .collect::<BTreeSet<_>>()
            .len(),
        10
    );
    let layers = sim.arena().map.layers().unwrap();
    for preview in &planned.rows {
        let region = layers.region_at_world(preview.giver.x, preview.giver.y);
        let others: Vec<_> = active
            .rows
            .iter()
            .filter(|r| {
                r.def != preview.def && layers.region_at_world(r.giver.x, r.giver.y) == region
            })
            .collect();
        assert_eq!(others.len(), 1);
        let other = others[0].giver;
        assert!(
            rh_sim::math::hypot(other.x - preview.giver.x, other.y - preview.giver.y)
                >= 7. * rh_sim::constants::TILE
        );
    }
    let saved = serde_json::to_vec(&sim).unwrap();
    let restored: Sim = serde_json::from_slice(&saved).unwrap();
    assert_eq!(sim.state_hash(), restored.state_hash());
    assert_eq!(all, positions(&board(&restored, a)));
}

#[test]
fn historic_quest_layout_is_implicit_in_old_configs() {
    let mut config = SimConfig::live_content();
    let json = serde_json::to_value(&config).unwrap();
    assert_eq!(json["spread_quest_givers"], true);
    config.spread_quest_givers = false;
    let historical = serde_json::to_value(&config).unwrap();
    assert!(historical.get("spread_quest_givers").is_none());
    let restored: SimConfig = serde_json::from_value(historical).unwrap();
    assert_eq!(restored, config);
}
