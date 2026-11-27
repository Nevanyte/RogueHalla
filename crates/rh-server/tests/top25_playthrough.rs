//! P-QUEST25-PLAYTHROUGH. Two encoded seats share the board and keep separate rewards.
use rh_protocol::{ClientMsg, ServerMsg, StateItem, WireCommand, WireQuestPoint, WireQuestView};
use rh_server::outbox::Outbox;
use rh_server::room::{MapAssets, Room, RoomCmd, RoomConfig};
use rh_sim::mapgen::{coliseum, pipeline};
use rh_sim::quests::board::{catalog::RewardId, map_admission};
use rh_sim::{ArenaMode, ArenaRules, FactionId, Phase, PlayerId, Secrets, SimConfig};
use std::{ops::Range, sync::Arc, time::Duration};
use tokio::{
    sync::{mpsc, oneshot},
    time::Instant,
};

const QUEST: &str = "stay_a_while";

#[path = "top25_playthrough/capture.rs"]
mod capture;

#[path = "top25_playthrough/spell_cooldowns.rs"]
mod spell_cooldowns;

struct Client {
    player: PlayerId,
    conn: u64,
    seq: u32,
    session: [u8; 16],
    outbox: Arc<Outbox>,
    board: WireQuestView,
    last_quest_frame: Option<Vec<u8>>,
}

impl Client {
    fn join(room: &mut Room, profile: &str, conn: u64) -> Self {
        let outbox = Arc::new(Outbox::default());
        let (reply, mut joined) = oneshot::channel();
        room.handle(
            RoomCmd::Join {
                profile: profile.into(),
                faction: Some(FactionId::Holy),
                outbox: outbox.clone(),
                conn,
                fence: conn as i64,
                reply,
            },
            Instant::now(),
        );
        let joined = joined.try_recv().unwrap().unwrap();
        assert!(matches!(
            ServerMsg::decode(&joined.welcome).unwrap(),
            ServerMsg::Welcome { .. }
        ));
        Self {
            player: joined.player,
            conn,
            seq: 0,
            session: joined.session,
            outbox,
            board: Default::default(),
            last_quest_frame: None,
        }
    }

    fn receive(&mut self) {
        let mut frames = Vec::new();
        assert!(self.outbox.drain_into(&mut frames).close.is_none());
        for bytes in frames {
            if let ServerMsg::State { items, .. } = ServerMsg::decode(&bytes).unwrap() {
                for item in items {
                    if let StateItem::Quests(Some(view)) = item {
                        self.board = view;
                        self.last_quest_frame = Some(bytes.clone());
                    }
                }
            }
        }
    }

    fn input(&mut self, room: &mut Room, command: WireCommand) {
        self.seq += 1;
        let bytes = ClientMsg::Input {
            seq: self.seq,
            commands: vec![command],
        }
        .try_encode()
        .unwrap();
        let ClientMsg::Input { seq, commands } = ClientMsg::decode(&bytes).unwrap() else {
            unreachable!("a client Input just round-tripped")
        };
        room.handle(
            RoomCmd::Input {
                player: self.player,
                conn: self.conn,
                seq,
                commands,
                received: std::time::Instant::now(),
            },
            Instant::now(),
        );
    }

    fn resume(&mut self, room: &mut Room) {
        room.handle(
            RoomCmd::Disconnected {
                player: self.player,
                conn: self.conn,
            },
            Instant::now(),
        );
        self.conn += 10;
        self.outbox = Arc::new(Outbox::default());
        self.board = WireQuestView::default();
        self.last_quest_frame = None;
        let (reply, mut resumed) = oneshot::channel();
        room.handle(
            RoomCmd::Resume {
                session: self.session,
                outbox: self.outbox.clone(),
                conn: self.conn,
                reply,
            },
            Instant::now(),
        );
        let resumed = resumed.try_recv().unwrap().unwrap();
        assert_eq!(resumed.player, self.player);
        assert_eq!(resumed.session, self.session);
        assert!(matches!(
            ServerMsg::decode(&resumed.welcome).unwrap(),
            ServerMsg::Welcome { .. }
        ));
    }

    fn quest(&self) -> Option<&rh_protocol::WireQuestRow> {
        self.board.rows.iter().find(|row| row.def == QUEST)
    }

    fn pos(&self, room: &Room) -> WireQuestPoint {
        let p = room.sim().player(self.player).unwrap().pos();
        WireQuestPoint { x: p.x, y: p.y }
    }

    fn move_to(&mut self, room: &mut Room, at: WireQuestPoint) {
        self.input(
            room,
            WireCommand::Move {
                x: at.x as f32,
                y: at.y as f32,
                seq: Some(self.seq as u8 + 1),
            },
        );
    }

    fn meditate(&mut self, room: &mut Room) {
        let row = self.quest().unwrap();
        let action = self
            .board
            .actions
            .iter()
            .find(|action| action.quest == row.handle && action.action == 6)
            .expect("server must offer the meditation action");
        self.input(
            room,
            WireCommand::QuestInteract {
                handle: row.handle,
                action: action.action,
                target: action.target,
            },
        );
    }

    fn cast_stasis(&mut self, room: &mut Room, at: WireQuestPoint) {
        let spell = &self.board.spells[0];
        assert_eq!(spell.reward, RewardId::Stasis as u8);
        assert_eq!(spell.charges, 1);
        self.input(
            room,
            WireCommand::Cast {
                slot: spell.slot + 1,
                x: at.x as f32,
                y: at.y as f32,
                actor: None,
                t: None,
                seq: None,
            },
        );
    }
}

fn wake(room: &mut Room, a: &mut Client, b: &mut Client) {
    assert!(room.wake(room.next_deadline()));
    a.receive();
    b.receive();
}

fn until(
    room: &mut Room,
    a: &mut Client,
    b: &mut Client,
    legacy_steps: usize,
    why: &str,
    condition: impl Fn(&Room, &Client, &Client) -> bool,
) {
    let steps = legacy_steps * 50 / room.sim().tick_ms() as usize;
    for _ in 0..steps {
        if condition(room, a, b) {
            return;
        }
        wake(room, a, b);
    }
    assert!(condition(room, a, b), "{why}");
}

fn test_room(
    secrets: Secrets,
    placement_attempts: Range<u64>,
    room_id: [u8; 16],
    rules: ArenaRules,
    tick_ms: u32,
) -> Room {
    let fixed = coliseum::fixed_layout();
    let generator = pipeline::Pipeline::new(&fixed);
    let export = map_admission::generate(
        &generator,
        1,
        placement_attempts,
        pipeline::Options {
            decor: false,
            ..Default::default()
        },
        secrets,
    )
    .unwrap();
    let assets = MapAssets::new(export.validate().unwrap().into_arena().unwrap()).unwrap();
    let (reports, _back) = mpsc::unbounded_channel();
    Room::new(
        room_id,
        RoomConfig {
            sim: SimConfig {
                tick_ms,
                min_players: 2,
                lobby_seconds: 0.05,
                fill_seconds: 0.05,
                lifecycle: ArenaMode::Source(rules),
                ..SimConfig::live_content()
            },
            arena: assets.arena,
            welcome_map: assets.welcome_map,
            fill_bots_to: 0,
            tick: Duration::from_millis(u64::from(tick_ms)),
            record: false,
            fixed_secrets: Some(secrets),
            fixed_bot_seed: None,
            reconnect_grace: Duration::from_secs(20),
            linger: Duration::from_secs(5),
        },
        reports,
        Instant::now(),
    )
}

#[tokio::test(start_paused = true)]
async fn two_players_independently_complete_and_resume_at_20_hz() {
    check_independent_completion(50);
}

#[tokio::test(start_paused = true)]
async fn two_players_independently_complete_and_resume_at_40_hz() {
    check_independent_completion(25);
}

fn check_independent_completion(tick_ms: u32) {
    let secrets = Secrets::from_seed(14);
    let mut room = test_room(
        secrets,
        0..24,
        [4; 16],
        ArenaRules {
            plan_seconds: 3.,
            first_patience_seconds: None,
            coliseum_schedule: None,
            ..Default::default()
        },
        tick_ms,
    );
    let mut a = Client::join(&mut room, "alice", 1);
    let mut b = Client::join(&mut room, "bob", 2);
    until(
        &mut room,
        &mut a,
        &mut b,
        20,
        "both receive five offers during planning",
        |room, a, b| {
            room.sim().phase() == Phase::Planning
                && a.board.rows.len() == 5
                && b.board.rows.len() == 5
        },
    );
    let public = |c: &Client| {
        c.board
            .rows
            .iter()
            .map(|row| (row.def.clone(), row.giver))
            .collect::<Vec<_>>()
    };
    assert_eq!(public(&a), public(&b));
    until(
        &mut room,
        &mut a,
        &mut b,
        80,
        "raid must start",
        |room, _, _| room.sim().phase() == Phase::Active,
    );
    let at = a.quest().unwrap().giver;
    let a_start = a.pos(&room);
    let b_start = b.pos(&room);
    a.move_to(&mut room, at);
    b.move_to(&mut room, at);
    until(
        &mut room,
        &mut a,
        &mut b,
        1000,
        "both reach the giver by ordinary movement",
        |room, a, b| {
            [a, b].into_iter().all(|c| {
                let p = c.pos(room);
                c.quest().is_some_and(|row| row.can_accept) && (p.x - at.x).hypot(p.y - at.y) < 30.
            })
        },
    );
    for c in [&mut a, &mut b] {
        c.input(
            &mut room,
            WireCommand::Stop {
                seq: Some(c.seq as u8 + 1),
            },
        );
    }
    wake(&mut room, &mut a, &mut b);
    for c in [&mut a, &mut b] {
        c.input(
            &mut room,
            WireCommand::QuestAccept {
                handle: c.quest().unwrap().handle,
            },
        );
    }
    until(
        &mut room,
        &mut a,
        &mut b,
        5,
        "both independently accept the same offer",
        |_, a, b| {
            [a, b]
                .into_iter()
                .all(|c| c.quest().is_some_and(|row| row.owner_accepted))
        },
    );
    let a_channel_began = room.sim().server_time();
    a.meditate(&mut room);
    for _ in 0..1000 / tick_ms {
        wake(&mut room, &mut a, &mut b);
    }
    let b_channel_began = room.sim().server_time();
    b.meditate(&mut room);
    until(
        &mut room,
        &mut a,
        &mut b,
        601,
        "first client independently earns Stasis",
        |_, a, _| a.board.spells.len() == 1,
    );
    assert!((30_000.0..=30_100.0).contains(&(room.sim().server_time() - a_channel_began)));
    assert!(a.quest().is_none(), "own completion retires the quest row");
    assert!(b.quest().is_some_and(|row| row.owner_accepted));
    assert!(b.board.spells.is_empty());

    a.resume(&mut room);
    until(
        &mut room,
        &mut a,
        &mut b,
        8,
        "first owner's unspent spell survives resume",
        |_, a, _| a.board.spells.len() == 1 && a.quest().is_none(),
    );
    a.cast_stasis(&mut room, at);
    until(
        &mut room,
        &mut a,
        &mut b,
        4,
        "first cast consumes its own charge",
        |_, a, _| a.board.spells.is_empty(),
    );
    let a_frozen = a.pos(&room);
    a.move_to(&mut room, a_start);
    for _ in 0..300 / tick_ms {
        wake(&mut room, &mut a, &mut b);
    }
    assert_eq!(
        a.pos(&room),
        a_frozen,
        "Stasis must prevent ordinary movement"
    );

    until(
        &mut room,
        &mut a,
        &mut b,
        30,
        "second client independently earns Stasis",
        |_, _, b| b.board.spells.len() == 1 && b.quest().is_none(),
    );
    assert!((30_000.0..=30_100.0).contains(&(room.sim().server_time() - b_channel_began)));
    a.resume(&mut room);
    until(
        &mut room,
        &mut a,
        &mut b,
        8,
        "first owner's spent charge stays spent after resume",
        |_, a, _| a.board.rows.len() == 9 && a.board.spells.is_empty() && a.quest().is_none(),
    );
    b.resume(&mut room);
    until(
        &mut room,
        &mut a,
        &mut b,
        8,
        "second owner's own unspent spell survives resume",
        |_, a, b| a.board.spells.is_empty() && b.board.spells.len() == 1 && b.quest().is_none(),
    );
    b.cast_stasis(&mut room, at);
    until(
        &mut room,
        &mut a,
        &mut b,
        4,
        "second cast consumes its own charge",
        |_, _, b| b.board.spells.is_empty(),
    );
    let b_frozen = b.pos(&room);
    b.move_to(&mut room, b_start);
    for _ in 0..300 / tick_ms {
        wake(&mut room, &mut a, &mut b);
    }
    assert_eq!(b.pos(&room), b_frozen);
    for _ in 0..2500 / tick_ms {
        wake(&mut room, &mut a, &mut b);
    }
    b.move_to(&mut room, b_start);
    until(
        &mut room,
        &mut a,
        &mut b,
        12,
        "ordinary movement returns when Stasis expires",
        |room, _, b| b.pos(room) != b_frozen,
    );
}
