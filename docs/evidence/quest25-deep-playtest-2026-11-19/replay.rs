//! Replays encoded player inputs from the local Top 25 verification run.
use rh_protocol::{ClientMsg, ServerMsg, StateItem, WireCommand, WireEvent, WireQuestView};
use rh_server::outbox::Outbox;
use rh_server::room::{MapAssets, Room, RoomCmd, RoomConfig};
use rh_sim::mapgen::{coliseum, pipeline};
use rh_sim::quests::board::map_admission;
use rh_sim::{ArenaMode, ArenaRules, FactionId, Phase, PlayerId, Secrets, SimConfig};
use serde::Deserialize;
use std::sync::Arc;
use std::time::Duration;
use tokio::sync::{mpsc, oneshot};
use tokio::time::Instant;

#[derive(Deserialize)]
struct Case {
    quest: String,
    seed: u64,
    min_players: usize,
    first_patience: Option<f64>,
    seats: Vec<(String, u64, FactionId)>,
    winner: usize,
    witness: usize,
    inputs: Vec<(u32, u64, String)>,
    steps: u32,
    expected_spells: Vec<u8>,
    expected_reward_notice: String,
}

struct Seat {
    id: PlayerId,
    conn: u64,
    session: [u8; 16],
    seq: u32,
    outbox: Arc<Outbox>,
    view: WireQuestView,
    rewards_announced: Vec<String>,
}

fn join(room: &mut Room, profile: &str, conn: u64, faction: FactionId) -> Seat {
    let outbox = Arc::new(Outbox::default());
    let (reply, mut answer) = oneshot::channel();
    room.handle(
        RoomCmd::Join {
            profile: profile.into(),
            faction: Some(faction),
            outbox: outbox.clone(),
            conn,
            fence: conn as i64,
            reply,
        },
        Instant::now(),
    );
    let joined = answer.try_recv().unwrap().unwrap();
    assert!(matches!(
        ServerMsg::decode(&joined.welcome).unwrap(),
        ServerMsg::Welcome { .. }
    ));
    Seat {
        id: joined.player,
        conn,
        session: joined.session,
        seq: 0,
        outbox,
        view: WireQuestView::default(),
        rewards_announced: Vec::new(),
    }
}

fn receive(seat: &mut Seat) {
    let mut frames = Vec::new();
    assert!(seat.outbox.drain_into(&mut frames).close.is_none());
    for frame in frames {
        match ServerMsg::decode(&frame).unwrap() {
            ServerMsg::State { items, .. } => {
                for item in items {
                    if let StateItem::Quests(Some(view)) = item {
                        seat.view = view;
                    }
                }
            }
            ServerMsg::Events { events, .. } => {
                for event in events {
                    if let WireEvent::Toast { text } = event {
                        if let Some(reward) = text.strip_prefix("Reward received: ") {
                            seat.rewards_announced.push(reward.to_owned());
                        }
                    }
                }
            }
            _ => {}
        }
    }
}

fn replay_input(room: &mut Room, seat: &mut Seat, hex: &str) {
    assert_eq!(hex.len() % 2, 0);
    let bytes = (0..hex.len())
        .step_by(2)
        .map(|i| u8::from_str_radix(&hex[i..i + 2], 16).unwrap())
        .collect::<Vec<_>>();
    send(room, seat, &bytes);
}

fn send(room: &mut Room, seat: &mut Seat, bytes: &[u8]) {
    let ClientMsg::Input { seq, commands } = ClientMsg::decode(bytes).unwrap() else {
        panic!("expected a client input frame");
    };
    seat.seq = seq;
    room.handle(
        RoomCmd::Input {
            player: seat.id,
            conn: seat.conn,
            seq,
            commands,
            received: std::time::Instant::now(),
        },
        Instant::now(),
    );
}

fn resume(room: &mut Room, seat: &mut Seat) {
    room.handle(
        RoomCmd::Disconnected {
            player: seat.id,
            conn: seat.conn,
        },
        Instant::now(),
    );
    seat.conn += 1000;
    seat.outbox = Arc::new(Outbox::default());
    seat.view = WireQuestView::default();
    let (reply, mut answer) = oneshot::channel();
    room.handle(
        RoomCmd::Resume {
            session: seat.session,
            outbox: seat.outbox.clone(),
            conn: seat.conn,
            reply,
        },
        Instant::now(),
    );
    let resumed = answer.try_recv().unwrap().unwrap();
    assert_eq!((resumed.player, resumed.session), (seat.id, seat.session));
    assert!(matches!(
        ServerMsg::decode(&resumed.welcome).unwrap(),
        ServerMsg::Welcome { .. }
    ));
}

fn play(case: &Case) {
    let secrets = Secrets::from_seed(case.seed);
    let layout = coliseum::fixed_layout();
    let generator = pipeline::Pipeline::new(&layout);
    let map = map_admission::generate(
        &generator,
        1,
        0..24,
        pipeline::Options {
            decor: false,
            ..Default::default()
        },
        secrets,
    )
    .unwrap();
    let assets = MapAssets::new(map.validate().unwrap().into_arena().unwrap()).unwrap();
    let (reports, _receiver) = mpsc::unbounded_channel();
    let mut room = Room::new(
        [4; 16],
        RoomConfig {
            sim: SimConfig {
                min_players: case.min_players,
                lobby_seconds: 0.05,
                fill_seconds: 0.05,
                lifecycle: ArenaMode::Source(ArenaRules {
                    plan_seconds: 3.,
                    first_patience_seconds: case.first_patience,
                    coliseum_schedule: None,
                    ..Default::default()
                }),
                ..SimConfig::live_content()
            },
            arena: assets.arena,
            welcome_map: assets.welcome_map,
            fill_bots_to: 0,
            tick: Duration::from_millis(50),
            record: false,
            fixed_secrets: Some(secrets),
            fixed_bot_seed: None,
            reconnect_grace: Duration::from_secs(20),
            linger: Duration::from_secs(5),
        },
        reports,
        Instant::now(),
    );
    let mut seats = case
        .seats
        .iter()
        .map(|(name, conn, faction)| join(&mut room, name, *conn, *faction))
        .collect::<Vec<_>>();
    let mut actions = case.inputs.iter().peekable();
    let mut shared = false;
    let mut accepted_handle = None;
    for wake in 1..=case.steps + 35 {
        assert!(room.wake(room.next_deadline()));
        for seat in &mut seats {
            receive(seat);
        }
        if !shared
            && room.sim().phase() == Phase::Planning
            && seats.iter().all(|s| s.view.rows.len() == 10)
        {
            let board = |seat: &Seat| {
                seat.view
                    .rows
                    .iter()
                    .map(|r| (r.def.clone(), r.giver))
                    .collect::<Vec<_>>()
            };
            let first = board(&seats[0]);
            assert!(seats.iter().all(|s| board(s) == first));
            shared = true;
        }
        let row = seats[case.winner]
            .view
            .rows
            .iter()
            .find(|r| r.def == case.quest);
        if let Some(row) = row.filter(|r| r.owner_accepted) {
            accepted_handle = Some(row.handle);
        }
        let disappeared = row.is_none();
        while let Some((_, conn, hex)) = actions.next_if(|(at, _, _)| *at == wake) {
            let seat = seats.iter_mut().find(|s| s.conn == *conn).unwrap();
            replay_input(&mut room, seat, hex);
        }
        if let Some(handle) = accepted_handle.filter(|_| disappeared && actions.peek().is_none()) {
            // Retry with a fresh sequence and the original handle, then reconnect both players.
            let winner = &mut seats[case.winner];
            let retry = ClientMsg::Input {
                seq: winner.seq.wrapping_add(1),
                commands: vec![WireCommand::QuestAccept { handle }],
            }
            .try_encode()
            .unwrap();
            send(&mut room, winner, &retry);
            for _ in 0..3 {
                assert!(room.wake(room.next_deadline()));
                for seat in &mut seats {
                    receive(seat);
                }
            }
            resume(&mut room, &mut seats[case.winner]);
            resume(&mut room, &mut seats[case.witness]);
            for _ in 0..10 {
                assert!(room.wake(room.next_deadline()));
                for seat in &mut seats {
                    receive(seat);
                }
            }
            let winner = &seats[case.winner];
            assert_eq!(winner.view.rows.len(), 9);
            assert!(!winner.view.rows.iter().any(|r| r.def == case.quest));
            assert_eq!(seats[case.witness].view.rows.len(), 10);
            assert!(shared);
            assert_eq!(
                winner.rewards_announced.as_slice(),
                std::slice::from_ref(&case.expected_reward_notice),
                "winner's reward notice for {}",
                case.quest
            );
            assert!(seats[case.witness].rewards_announced.is_empty());
            assert_eq!(room.sim().phase(), Phase::Active);
            assert!(room.sim().player(winner.id).unwrap().standing());
            assert!(seats[case.witness]
                .view
                .rows
                .iter()
                .any(|r| r.def == case.quest && !r.owner_accepted));
            let rewards = winner
                .view
                .spells
                .iter()
                .map(|s| {
                    assert!(s.charges > 0);
                    s.reward
                })
                .collect::<Vec<_>>();
            assert_eq!(rewards, case.expected_spells);
            println!(
                "PASS {} ({} input frames, {} seats, wake {})",
                case.quest,
                case.inputs.len(),
                seats.len(),
                wake
            );
            return;
        }
    }
    panic!(
        "{} never reached its recorded reward and independent witness state",
        case.quest
    );
}

#[tokio::main(flavor = "current_thread", start_paused = true)]
async fn main() {
    let cases: Vec<Case> = serde_json::from_reader(std::io::stdin().lock()).unwrap();
    let catalog = rh_sim::quests::board::catalog::QuestId::ALL;
    assert_eq!(cases.len(), catalog.len());
    for id in catalog {
        let case = cases
            .iter()
            .find(|c| c.quest == id.definition().id)
            .unwrap();
        play(case);
    }
}
