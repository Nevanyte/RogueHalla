//! Offline projection of public Sim observations through the same encoders as the room.
use rh_policy::delivery::{
    collections::CollectionPublisher,
    projection::{self, Wire},
};
use rh_protocol::{ServerMsg, WelcomeHeader};
use rh_server::{public_ground, room};
use rh_sim::{Arena, Observation, SimConfig};
use serde::{Deserialize, Serialize};
use std::{collections::BTreeMap, fs, path::Path};

#[derive(Deserialize)]
struct NativeScenario {
    id: String,
    name: String,
    reward: String,
    reward_detail: String,
    seed: u64,
    arena: String,
    frames: Vec<NativeFrame>,
}

#[derive(Deserialize)]
struct NativeFrame {
    phase: String,
    view: Observation,
}

#[derive(Serialize)]
#[serde(rename_all = "camelCase")]
struct Catalog {
    tick_ms: u32,
    quests: Vec<Scenario>,
}

#[derive(Serialize)]
#[serde(rename_all = "camelCase")]
struct Scenario {
    id: String,
    name: String,
    reward: String,
    reward_detail: String,
    seed: u64,
    welcome: String,
    frames: Vec<Frame>,
}

#[derive(Serialize)]
struct Frame {
    phase: String,
    snapshot: String,
    state: String,
    events: Option<String>,
    fx: Option<String>,
    tick: u32,
    quest: Option<rh_sim::quest_view::QuestRow>,
    spells: Vec<rh_sim::quest_view::QuestSpell>,
}

fn write_frame(output: &Path, name: String, bytes: &[u8]) -> String {
    ServerMsg::decode(bytes).unwrap_or_else(|err| panic!("{name}: {err}"));
    fs::write(output.join(&name), bytes).unwrap();
    name
}

fn encode(message: ServerMsg) -> Vec<u8> {
    message.try_encode().unwrap()
}

fn project_frame(output: &Path, id: &str, frame: NativeFrame, tick_ms: u32) -> Frame {
    let NativeFrame { phase, view } = frame;
    let base = format!("{id}/{phase}");
    let snapshot = write_frame(
        output,
        format!("{base}.snapshot.bin"),
        &encode(projection::checked_snapshot(&view, 0).unwrap()),
    );
    let mut publisher = CollectionPublisher::default();
    publisher.set_tick_ms(tick_ms).unwrap();
    let collections = publisher.prepare(&view, true, true, false).unwrap();
    let state = write_frame(
        output,
        format!("{base}.state.bin"),
        &collections.frame.unwrap(),
    );
    let mut events = Vec::new();
    let mut fx = Vec::new();
    for event in &view.events {
        match projection::to_wire(event) {
            Wire::Event(event) => events.push(event),
            Wire::Fx(effect) => fx.push(effect),
            Wire::Omitted => {}
            Wire::Refused(reason) => panic!("{base} event: {reason}"),
        }
    }
    let tick = view.tick;
    let events = (!events.is_empty()).then(|| {
        write_frame(
            output,
            format!("{base}.events.bin"),
            &encode(ServerMsg::Events { tick, events }),
        )
    });
    let fx = (!fx.is_empty()).then(|| {
        write_frame(
            output,
            format!("{base}.fx.bin"),
            &encode(ServerMsg::Fx { tick, fx }),
        )
    });
    let journal = view.quests.unwrap();
    let quest = journal.rows.into_iter().find(|row| row.def == id);
    Frame {
        phase,
        snapshot,
        state,
        events,
        fx,
        tick,
        quest,
        spells: journal.spells,
    }
}

#[test]
#[ignore = "opt-in local projection for the Unity playthrough"]
fn project_authoritative_quest_outcomes_to_wire() {
    let input = std::env::var_os("RH_QUEST_NATIVE_CAPTURE").unwrap();
    let output = std::env::var_os("RH_QUEST_WIRE_CAPTURE").unwrap();
    let (input, output) = (Path::new(&input), Path::new(&output));
    assert!(input.is_absolute() && output.is_absolute() && input != output);
    fs::create_dir_all(output).unwrap();
    let mut maps = BTreeMap::new();
    let mut quests = Vec::new();
    let tick_ms = 25;
    let config = SimConfig::live_content();
    for ordinal in 0..25 {
        let path = input.join(format!("quest-{ordinal:02}.json"));
        let scenario: NativeScenario = serde_json::from_slice(&fs::read(path).unwrap()).unwrap();
        let assets = maps.entry(scenario.arena.clone()).or_insert_with(|| {
            let arena: Arena =
                serde_json::from_slice(&fs::read(input.join(&scenario.arena)).unwrap()).unwrap();
            room::MapAssets::new(arena).unwrap()
        });
        fs::create_dir_all(output.join(&scenario.id)).unwrap();
        let initial = &scenario.frames.first().unwrap().view;
        let welcome = write_frame(
            output,
            format!("{}/welcome.bin", scenario.id),
            &rh_protocol::encode_welcome(
                &WelcomeHeader {
                    protocol: rh_protocol::PROTOCOL_VERSION,
                    content_hash: rh_sim::content::content_hash(),
                    match_id: [ordinal; 16],
                    player_id: initial.me.as_ref().unwrap().id.0,
                    session: [0; 16],
                    tick_rate: (1000 / tick_ms) as u8,
                    tick: initial.tick,
                    server_ms: initial.server_time,
                },
                room::archetype_table(),
                &projection::wire_vision(&assets.arena.map),
                room::weapon_table(),
                room::hud_content_for(config.frost_wish),
                &assets.welcome_map,
                public_ground::mode(config.hazards),
            ),
        );
        let frames: Vec<_> = scenario
            .frames
            .into_iter()
            .map(|frame| project_frame(output, &scenario.id, frame, tick_ms))
            .collect();
        let stage = |name| {
            frames
                .iter()
                .find(|frame| frame.phase == name)
                .unwrap_or_else(|| panic!("{} lacks {name}", scenario.id))
        };
        assert!(stage("offered").quest.as_ref().unwrap().can_accept);
        assert!(stage("accepted").quest.as_ref().unwrap().owner_accepted);
        assert!(stage("settled").quest.is_none());
        if !stage("settled").spells.is_empty() {
            let _ = (stage("cast_before"), stage("cast_start"), stage("cast_end"));
        }
        if scenario.id == "hold_the_fort" {
            assert!(
                stage("cast_hit").fx.is_some(),
                "turret impact is visible to its owner"
            );
        }
        if let Some(ready) = frames.iter().find(|frame| frame.phase == "ready") {
            assert!(ready.quest.as_ref().unwrap().owner_ready);
        }
        assert!(frames.windows(2).all(|pair| pair[0].tick <= pair[1].tick));
        quests.push(Scenario {
            id: scenario.id,
            name: scenario.name,
            reward: scenario.reward,
            reward_detail: scenario.reward_detail,
            seed: scenario.seed,
            welcome,
            frames,
        });
    }
    fs::write(
        output.join("catalog.json"),
        serde_json::to_vec_pretty(&Catalog { tick_ms, quests }).unwrap(),
    )
    .unwrap();
}
