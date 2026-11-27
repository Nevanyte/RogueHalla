//! Explicit endpoint profiles through production public/control routers and encoded protocol-14 clients.
#[path = "support/http.rs"]
mod http;
use futures_util::{SinkExt, StreamExt};
use rh_protocol::{
    ClientMsg, ServerMsg, SnapshotTracker, StateItem, WireCommand, PROTOCOL_VERSION,
};
use rh_server::{
    director::{Director, DirectorConfig},
    hosting::Lifecycle,
    persist::{Auth, Persistence},
    runtime::Settings,
};
use serde_json::{json, Value};
use std::{net::SocketAddr, sync::Arc, time::Duration};
use tokio_tungstenite::{tungstenite::Message, MaybeTlsStream, WebSocketStream};

/// How long a client waits for a public state condition. The test server free-runs its clock, so on a loaded machine
/// the client can fall behind the frames it has to read; the wait is generous by default and `RH_TEST_WAIT_SECS`
/// overrides it. The predicates and the socket-level diagnostics are unchanged: only the deadline moved (it was 8 s).
fn wait_deadline() -> Duration {
    let secs = std::env::var("RH_TEST_WAIT_SECS")
        .ok()
        .and_then(|v| v.parse::<u64>().ok())
        .filter(|&v| v > 0)
        .unwrap_or(40);
    Duration::from_secs(secs)
}

struct Rig {
    director: Arc<Director>,
    public: SocketAddr,
    control: SocketAddr,
    handle: axum_server::Handle,
    control_task: tokio::task::JoinHandle<()>,
}
impl Drop for Rig {
    fn drop(&mut self) {
        self.handle.shutdown();
        self.control_task.abort();
    }
}
impl Rig {
    async fn start(mode: &str, hosted: bool) -> Self {
        Self::start_arena(mode, hosted, None).await
    }
    async fn start_arena(mode: &str, hosted: bool, arena: Option<rh_sim::Arena>) -> Self {
        Self::start_seeded(mode, hosted, arena, None).await
    }
    async fn start_seeded(
        mode: &str,
        hosted: bool,
        arena: Option<rh_sim::Arena>,
        seed: Option<u64>,
    ) -> Self {
        let mut args = vec!["--runtime".into(), mode.into()];
        if mode == "source-arena" {
            args.extend([
                "--map".into(),
                concat!(
                    env!("CARGO_MANIFEST_DIR"),
                    "/../../maps/first-playable.json"
                )
                .into(),
                "--fill-seconds".into(),
                "0.5".into(),
            ]);
        }
        let settings = Settings::from_args(&args).unwrap().unwrap();
        let cfg = DirectorConfig {
            max_rooms: 2,
            require_admit: hosted,
            fixed_board_secrets: seed.map(rh_sim::Secrets::from_seed),
            tick: Duration::from_millis(3),
            ..DirectorConfig::new(
                settings.sim(),
                arena.unwrap_or_else(|| settings.arena().unwrap()),
            )
        };
        let lifecycle = Arc::new(Lifecycle::default());
        let director = Director::new(
            cfg,
            Arc::new(Persistence::memory()),
            Arc::new(Auth::Static(
                [
                    ("alice".into(), "alice-secret".into()),
                    ("bob".into(), "bob-secret".into()),
                    ("carol".into(), "carol-secret".into()),
                ]
                .into(),
            )),
            lifecycle.clone(),
        );
        let app = rh_server::public_router(director.clone());
        let handle = axum_server::Handle::new();
        let h = handle.clone();
        tokio::spawn(async move {
            rh_server::serve("127.0.0.1:0".parse().unwrap(), None, app, h)
                .await
                .unwrap();
        });
        let public = handle.listening().await.unwrap();
        let listener = tokio::net::TcpListener::bind("127.0.0.1:0").await.unwrap();
        let control = listener.local_addr().unwrap();
        let app = rh_server::control_router(director.clone());
        let control_task = tokio::spawn(async move {
            axum::serve(listener, app).await.unwrap();
        });
        lifecycle.ready();
        Self {
            director,
            public,
            control,
            handle,
            control_task,
        }
    }
    async fn post(&self, path: &str, body: Value) -> http::Resp {
        http::post_json(self.control, path, &[], &body.to_string()).await
    }
    async fn create(&self, id: &str, properties: Value) -> http::Resp {
        self.post(
            "/control/session",
            json!({"v":1,"gameSessionId":id,"properties":properties}),
        )
        .await
    }
    async fn admit(&self, id: &str, name: &str) -> http::Resp {
        self.post(
            "/control/admit",
            json!({"v":1,"gameSessionId":id,"playerSessionId":name,"callsign":name}),
        )
        .await
    }
}

struct Client {
    sock: WebSocketStream<MaybeTlsStream<tokio::net::TcpStream>>,
    decoder: SnapshotTracker,
    seq: u32,
    ack_input: u32,
    room: [u8; 16],
    session: [u8; 16],
    phase: u8,
    training: bool,
    plan: Option<rh_protocol::WirePlan>,
    quests: Option<rh_protocol::WireQuestView>,
    trade: Option<rh_protocol::WireTradeView>,
    build: Option<rh_protocol::WireBuild>,
    me: Option<rh_protocol::WireSelf>,
    map: Option<rh_protocol::map::MapBlob>,
    tick: u32,
    server_time: f64,
    frames: u32,
    snapshots: u32,
    states: u32,
}
impl Client {
    async fn connect(
        rig: &Rig,
        name: &str,
        ticket: &str,
        resume: Option<[u8; 16]>,
    ) -> Result<Self, rh_protocol::RejectCode> {
        Self::connect_to(rig.public, name, ticket, resume).await
    }
    async fn connect_to(
        public: SocketAddr,
        name: &str,
        ticket: &str,
        resume: Option<[u8; 16]>,
    ) -> Result<Self, rh_protocol::RejectCode> {
        let (mut sock, _) = tokio_tungstenite::connect_async(format!("ws://{public}/ws"))
            .await
            .unwrap();
        sock.send(Message::Binary(
            ClientMsg::Hello {
                protocol: PROTOCOL_VERSION,
                content_hash: rh_sim::content::content_hash(),
                // These untrusted strings must never select an endpoint profile.
                build: "training-source-arena".into(),
                profile: name.into(),
                ticket: ticket.as_bytes().to_vec(),
                faction: Some(4),
                resume,
            }
            .encode()
            .into(),
        ))
        .await
        .unwrap();
        let mut client = Self {
            sock,
            decoder: SnapshotTracker::new(),
            seq: 0,
            ack_input: 0,
            room: [0; 16],
            session: [0; 16],
            phase: 0,
            training: false,
            plan: None,
            quests: None,
            trade: None,
            build: None,
            me: None,
            map: None,
            tick: 0,
            server_time: 0.,
            frames: 0,
            snapshots: 0,
            states: 0,
        };
        loop {
            match client.recv().await {
                ServerMsg::Welcome {
                    match_id,
                    session,
                    protocol,
                    map,
                    ..
                } => {
                    assert_eq!(protocol, rh_protocol::PROTOCOL_VERSION);
                    client.room = match_id;
                    client.session = session;
                    client.map = Some(map);
                    return Ok(client);
                }
                ServerMsg::Reject { code, .. } => return Err(code),
                _ => {}
            }
        }
    }
    async fn recv(&mut self) -> ServerMsg {
        loop {
            let frame = match tokio::time::timeout(Duration::from_secs(4), self.sock.next()).await {
                Ok(Some(Ok(frame))) => frame,
                result => panic!(
                    "public socket receive {result:?}: {}",
                    self.public_summary()
                ),
            };
            let bytes = match frame {
                Message::Binary(bytes) => bytes,
                Message::Ping(_) => {
                    // Tungstenite queues the automatic Pong. Flush it even when the
                    // test has no gameplay input to send during a long walk.
                    self.sock.flush().await.expect("send websocket Pong");
                    continue;
                }
                Message::Close(reason) => {
                    panic!("public socket closed {reason:?}: {}", self.public_summary())
                }
                _ => continue,
            };
            let msg = self
                .decoder
                .decode(&bytes)
                .expect("public frame decodes atomically");
            self.frames += 1;
            match &msg {
                ServerMsg::Snapshot {
                    tick,
                    server_time,
                    phase,
                    training,
                    me,
                    ack_input,
                    ..
                } => {
                    self.tick = *tick;
                    self.server_time = *server_time;
                    self.snapshots += 1;
                    self.phase = *phase;
                    self.training = *training;
                    self.me = *me;
                    self.ack_input = *ack_input;
                }
                ServerMsg::State { items, .. } => {
                    self.states += 1;
                    for item in items {
                        match item {
                            StateItem::Plan(v) => self.plan = v.clone(),
                            StateItem::Quests(v) => self.quests = v.clone(),
                            StateItem::Trade(v) => self.trade = v.clone(),
                            StateItem::Build(v) => self.build = *v,
                            _ => {}
                        }
                    }
                }
                _ => {}
            }
            return msg;
        }
    }
    fn public_summary(&self) -> String {
        format!("tick={} time={} phase={} training={} input={}/{} frames={} snapshots={} states={} me={:?} plan={:?} quests={:?} trade={:?}",
            self.tick,self.server_time,self.phase,self.training,self.ack_input,self.seq,self.frames,self.snapshots,self.states,
            self.me.map(|m|(m.x,m.y,m.hp,m.status)),self.plan.as_ref().map(|p|(p.pick,p.ready,p.ends_at)),
            self.quests.as_ref().map(|v|(v.unknown_count,v.rows.iter().map(|q|(q.handle,q.state,q.can_accept,q.owner_accepted,q.giver.x,q.giver.y)).collect::<Vec<_>>())),
            self.trade.as_ref().map(|v|(v.trade_used,v.offers.len(),v.merchants.iter().map(|m|(m.kind,m.x,m.y)).collect::<Vec<_>>())))
    }
    #[track_caller]
    fn until<'a>(
        &'a mut self,
        predicate: impl Fn(&Self) -> bool + 'a,
    ) -> impl std::future::Future<Output = ()> + 'a {
        let caller = std::panic::Location::caller();
        async move {
            let result = tokio::time::timeout(wait_deadline(), async {
                while !predicate(self) {
                    self.recv().await;
                }
            })
            .await;
            assert!(
                result.is_ok(),
                "public state condition at {caller}: {}",
                self.public_summary()
            );
            eprintln!(
                "public condition at {caller}: tick={} time={} input={}/{}",
                self.tick, self.server_time, self.ack_input, self.seq
            );
        }
    }
    async fn find_broker(&mut self) -> bool {
        let map = self.map.as_ref().unwrap();
        let w = usize::from(map.w);
        // Search from the public coliseum first, then sample the walkable map.
        // Brokers wander across regions and may never visit the coliseum.
        let sample = |index| {
            let tile = rh_sim::constants::TILE as f32;
            (
                (index % w) as f32 * tile + tile / 2.,
                (index / w) as f32 * tile + tile / 2.,
            )
        };
        let centre: Vec<_> = map
            .regions
            .as_ref()
            .unwrap()
            .iter()
            .enumerate()
            .filter(|&(i, region)| *region == 6 && map.tiles[i] == 0)
            .map(|(i, _)| sample(i))
            .collect();
        let (cx, cy) = centre[centre.len() / 2];
        let mut sites = broker_search_sites(map);
        self.walk(cx, cy).await;
        let mut next_search = self.tick + 120;
        let mut next_retarget = self.tick;
        let mut broker_move = None;
        let mut last_target: Option<(f32, f32)> = None;
        let mut followed_moving_broker = false;
        let result = tokio::time::timeout(wait_deadline(), async {
            loop {
                assert_eq!(
                    self.phase,
                    1,
                    "raid ended searching for broker: {}",
                    self.public_summary()
                );
                if let Some(seq) = broker_move {
                    if broker_ready(self.ack_input, seq, self.me, self.trade.as_ref()) {
                        break;
                    }
                }
                let sighting = self.trade.as_ref().and_then(|trade| {
                    trade
                        .merchants
                        .iter()
                        .find(|m| m.kind == rh_protocol::WireTradeKind::Stats)
                });
                let target = sighting
                    .map(|m| (m.x as f32, m.y as f32))
                    .filter(|&(x, y)| {
                        self.tick >= next_retarget
                            && last_target
                                .is_none_or(|(old_x, old_y)| (x - old_x).hypot(y - old_y) >= 16.)
                    });
                if let Some((x, y)) = target {
                    followed_moving_broker |= last_target.is_some();
                    self.walk(x, y).await;
                    broker_move = Some(self.seq);
                    last_target = Some((x, y));
                    next_retarget = self.tick + 12;
                    next_search = self.tick + 80;
                } else if self.tick >= next_search && !sites.is_empty() {
                    // A still-visible broker may be on the other side of an obstacle.
                    // Try another approach if the last walk did not get us in range.
                    let me = self.me.unwrap();
                    let nearest = sites
                        .iter()
                        .enumerate()
                        .min_by(|(_, a), (_, b)| {
                            let distance = |&(x, y): &(f32, f32)| (me.x - x).hypot(me.y - y);
                            distance(a).total_cmp(&distance(b))
                        })
                        .unwrap()
                        .0;
                    let (x, y) = sites.remove(nearest);
                    self.walk(x, y).await;
                    next_search = self.tick + 100;
                    next_retarget = self.tick + 30;
                    broker_move = None;
                    last_target = None;
                }
                self.recv().await;
            }
        })
        .await;
        assert!(
            result.is_ok(),
            "public broker walk to {last_target:?}: {}",
            self.public_summary()
        );
        followed_moving_broker
    }

    async fn walk(&mut self, x: f32, y: f32) {
        self.input(WireCommand::Move {
            x,
            y,
            seq: Some((self.seq + 1) as u8),
        })
        .await;
    }

    async fn input(&mut self, command: WireCommand) {
        self.seq += 1;
        self.sock
            .send(Message::Binary(
                ClientMsg::Input {
                    seq: self.seq,
                    commands: vec![command],
                }
                .encode()
                .into(),
            ))
            .await
            .unwrap();
    }
}

fn broker_search_sites(map: &rh_protocol::map::MapBlob) -> Vec<(f32, f32)> {
    let w = usize::from(map.w);
    let tile = rh_sim::constants::TILE as f32;
    (6..usize::from(map.h))
        .step_by(12)
        .flat_map(|row| (6..w).step_by(12).map(move |col| (col, row)))
        .filter(|&(col, row)| map.tiles[row * w + col] == 0)
        .map(|(col, row)| ((col as f32 + 0.5) * tile, (row as f32 + 0.5) * tile))
        .collect()
}

/// Test navigation gate, not trade authority: only the subsequent encoded Trade and trade_used update prove success.
fn broker_ready(
    ack: u32,
    move_seq: u32,
    me: Option<rh_protocol::WireSelf>,
    trade: Option<&rh_protocol::WireTradeView>,
) -> bool {
    let (Some(me), Some(trade)) = (me, trade) else {
        return false;
    };
    ack >= move_seq
        && me.status == 1
        && !trade.offers.is_empty()
        && trade.merchants.iter().any(|m| {
            m.kind == rh_protocol::WireTradeKind::Stats
                && (m.x - f64::from(me.x)).hypot(m.y - f64::from(me.y))
                    <= rh_sim::constants::TRADER_RANGE
        })
}

#[test]
fn saved_broker_quotes_require_move_ack_and_current_arrival() {
    use rh_protocol::{
        WireMerchant, WireSelf, WireTradeKind, WireTradeOffer, WireTradeStat, WireTradeView,
    };
    // Declared decoded public facts, not an earned quote/World fixture. These distinguish receipt from arrival and
    // preserved owner quotes from current merchant location; the socket test below proves the actual trade authority.
    let mut trade = WireTradeView {
        trade_used: false,
        offers: vec![WireTradeOffer {
            cost: WireTradeStat::Ad,
            cost_value: 1.,
            gain: WireTradeStat::Ap,
            gain_value: 1.,
            label: "diagnostic quote".into(),
        }],
        stats: vec![],
        barter_ready_at: None,
        merchants: vec![WireMerchant {
            kind: WireTradeKind::Stats,
            x: 592.,
            y: 208.,
            name: "Soul Broker".into(),
        }],
        barter_labels: Default::default(),
    };
    trade.check().unwrap();
    let far = WireSelf {
        x: 690.383,
        y: 124.323395,
        status: 1,
        ..Default::default()
    };
    let near = WireSelf {
        x: 592.,
        y: 208.,
        status: 1,
        ..Default::default()
    };
    assert!(
        !broker_ready(3, 6, Some(far), Some(&trade)),
        "saved quotes before move receipt are insufficient"
    );
    assert!(
        !broker_ready(6, 6, Some(far), Some(&trade)),
        "acknowledged frame alone is not arrival"
    );
    assert!(
        !broker_ready(3, 6, Some(near), Some(&trade)),
        "near state alone does not witness the requested frame"
    );
    assert!(
        broker_ready(6, 6, Some(near), Some(&trade)),
        "exact acknowledged arrival with quotes"
    );
    trade.merchants[0].x += rh_sim::constants::TRADER_RANGE + 1.;
    assert!(
        !broker_ready(6, 6, Some(near), Some(&trade)),
        "use the current broker location"
    );
    assert!(!broker_ready(6, 6, None, Some(&trade)));
    assert!(!broker_ready(6, 6, Some(near), None));
    trade.merchants[0].x = 592.;
    trade.offers.clear();
    assert!(
        !broker_ready(6, 6, Some(near), Some(&trade)),
        "arrival without owner quotes is insufficient"
    );
}

#[tokio::test]
async fn source_endpoint_hello_planning_ready_and_live_quest_trade_state() {
    // Run the seeded rooms serially so they do not compete for the arena map admission workers.
    source_endpoint_playthrough(2).await;
    assert!(
        source_endpoint_playthrough(5).await,
        "the moving broker on seed 5 requires an updated walk destination"
    );
    source_endpoint_playthrough(8).await;
}

async fn source_endpoint_playthrough(seed: u64) -> bool {
    let rig = Rig::start_seeded("source-arena", true, None, Some(seed)).await;
    assert_eq!(
        rig.create("arena", json!({"maxPlayers":2,"fillBotsTo":0}))
            .await
            .status,
        200
    );
    assert_eq!(rig.admit("arena", "alice").await.status, 200);
    assert_eq!(rig.admit("arena", "bob").await.status, 200);
    assert_eq!(
        Client::connect(&rig, "alice", "wrong", None).await.err(),
        Some(rh_protocol::RejectCode::BadCredentials)
    );
    let mut alice = Client::connect(&rig, "alice", "alice-secret", None)
        .await
        .unwrap();
    let mut bob = Client::connect(&rig, "bob", "bob-secret", None)
        .await
        .unwrap();
    assert_eq!(alice.room, bob.room);
    assert_eq!(
        alice.map.as_ref().unwrap().terrain_mode,
        rh_protocol::GroundMode::Map
    );
    assert!(alice.map.as_ref().unwrap().terrain.is_some());
    let planning_board = |c: &Client| {
        c.phase == 3
            && c.plan.is_some()
            && c.quests
                .as_ref()
                .is_some_and(|board| board.unknown_count == 5 && board.rows.len() == 5)
    };
    alice.until(planning_board).await;
    bob.until(planning_board).await;
    let board_sites = |c: &Client| {
        c.quests
            .as_ref()
            .unwrap()
            .rows
            .iter()
            .map(|row| (row.def.clone(), row.giver))
            .collect::<Vec<_>>()
    };
    assert_eq!(board_sites(&alice), board_sites(&bob));
    let rooms = http::get(rig.control, "/control/rooms", &[]).await.json();
    assert_eq!(rooms["rooms"][0]["phase"], "planning");
    assert_eq!(
        rig.admit("arena", "carol").await.status,
        409,
        "planning closes new admission"
    );
    let a = alice.plan.as_ref().unwrap();
    assert!(a.options.iter().all(Option::is_some));
    // An unready owner's choice is independent; a Plan does not grant a training console.
    alice
        .input(WireCommand::Plan {
            spawn: Some(0),
            boon: None,
            ready: Some(false),
        })
        .await;
    alice
        .until(|c| c.plan.as_ref().is_some_and(|p| p.pick == Some(0)))
        .await;
    assert!(bob.plan.as_ref().unwrap().pick.is_none());
    bob.input(WireCommand::Plan {
        spawn: Some(1),
        boon: None,
        ready: Some(true),
    })
    .await;
    alice
        .input(WireCommand::Plan {
            spawn: None,
            boon: None,
            ready: Some(true),
        })
        .await;
    alice
        .until(|c| c.phase == 1 && c.plan.is_none() && c.quests.is_some() && c.trade.is_some())
        .await;
    bob.until(|c| c.phase == 1 && c.plan.is_none()).await;
    assert!(!alice.training && !bob.training);
    let alice_board = alice.quests.as_ref().unwrap();
    assert_eq!(alice_board.unknown_count, 0);
    assert_eq!(alice_board.rows.len(), 10);
    assert_eq!(board_sites(&alice), board_sites(&bob));
    assert!(!alice.trade.as_ref().unwrap().trade_used);

    // Navigate only to positions delivered in the public quest view. No private map/entity lookup or installed offer.
    let me = alice.me.as_ref().unwrap();
    let quest = alice
        .quests
        .as_ref()
        .unwrap()
        .rows
        .iter()
        .filter(|q| q.state == 1)
        .min_by(|a, b| {
            let distance = |q: &rh_protocol::WireQuestRow| {
                (q.giver.x - f64::from(me.x)).hypot(q.giver.y - f64::from(me.y))
            };
            distance(a).total_cmp(&distance(b))
        })
        .expect("ordinary constructor publishes known quests")
        .clone();
    alice
        .input(WireCommand::Move {
            x: quest.giver.x as f32,
            y: quest.giver.y as f32,
            seq: Some(1),
        })
        .await;
    alice
        .until(|c| {
            c.quests
                .as_ref()
                .unwrap()
                .rows
                .iter()
                .any(|q| q.handle == quest.handle && q.can_accept)
        })
        .await;
    alice
        .input(WireCommand::QuestAccept {
            handle: quest.handle,
        })
        .await;
    alice
        .until(|c| {
            c.quests
                .as_ref()
                .unwrap()
                .rows
                .iter()
                .any(|q| q.handle == quest.handle && q.owner_accepted)
        })
        .await;
    bob.input(WireCommand::Aim { a: 0. }).await;
    let bob_seq = bob.seq;
    bob.until(|c| c.ack_input >= bob_seq).await;
    assert!(
        !bob.quests
            .as_ref()
            .unwrap()
            .rows
            .iter()
            .any(|q| q.owner_accepted),
        "quest acceptance belongs to its authenticated owner"
    );
    let followed_moving_broker = alice.find_broker().await;
    let offers = alice.trade.as_ref().unwrap().offers.clone();
    assert_eq!(offers.len(), 3, "nearby broker rolls genuine owner offers");
    alice
        .input(WireCommand::Trade {
            kind: rh_protocol::WireTradeKind::Stats,
            pick: 0,
        })
        .await;
    alice
        .until(|c| c.trade.as_ref().is_some_and(|v| v.trade_used))
        .await;
    assert!(!alice.trade.as_ref().unwrap().stats.is_empty());
    let card = rh_sim::cards::CardId::ALL
        .iter()
        .position(|id| *id == rh_sim::cards::CardId::TankEngine)
        .unwrap();
    alice
        .input(WireCommand::Train {
            command: 1,
            id: card as u32,
            count: 1,
            options: [0; 3],
            option_count: 0,
        })
        .await;
    let seq = alice.seq;
    alice.until(|c| c.build.is_some() && c.me.is_some()).await;
    alice.until(|c| c.ack_input >= seq).await;
    assert_eq!(
        alice.build.as_ref().unwrap().cards[card],
        0,
        "arena rejects training grants"
    );
    followed_moving_broker
}

#[tokio::test]
async fn training_endpoint_concurrent_hellos_get_solo_rooms_and_real_console() {
    let rig = Rig::start("training", false).await;
    let (alice, bob) = tokio::join!(
        Client::connect(&rig, "alice", "alice-secret", None),
        Client::connect(&rig, "bob", "bob-secret", None)
    );
    let mut alice = alice.unwrap();
    let mut bob = bob.unwrap();
    assert_ne!(
        alice.room, bob.room,
        "reservations enforce solo capacity before seating reports"
    );
    alice
        .until(|c| c.phase == 1 && c.training && c.build.is_some())
        .await;
    bob.until(|c| c.phase == 1 && c.training && c.build.is_some())
        .await;
    assert!(alice.plan.is_none() && alice.quests.is_none() && alice.trade.is_none());
    let card = rh_sim::cards::CardId::ALL
        .iter()
        .position(|id| *id == rh_sim::cards::CardId::TankEngine)
        .unwrap();
    alice
        .input(WireCommand::Train {
            command: 1,
            id: card as u32,
            count: 1,
            options: [0; 3],
            option_count: 0,
        })
        .await;
    alice
        .until(|c| c.build.as_ref().is_some_and(|b| b.cards[card] == 1))
        .await;
    bob.input(WireCommand::Aim { a: 0. }).await;
    let bob_seq = bob.seq;
    bob.until(|c| c.ack_input >= bob_seq).await;
    assert_eq!(
        bob.build.as_ref().unwrap().cards[card],
        0,
        "training console is owner only"
    );
    assert_eq!(
        Client::connect(&rig, "carol", "carol-secret", None)
            .await
            .err(),
        Some(rh_protocol::RejectCode::MatchFull)
    );
    let room = alice.room;
    let session = alice.session;
    drop(alice);
    let mut resumed = Client::connect(&rig, "ignored", "", Some(session))
        .await
        .unwrap();
    assert_eq!(resumed.room, room);
    resumed
        .until(|c| {
            c.phase == 1 && c.training && c.build.as_ref().is_some_and(|b| b.cards[card] == 1)
        })
        .await;
    resumed
        .sock
        .send(Message::Binary(ClientMsg::Leave.encode().into()))
        .await
        .unwrap();
    bob.sock
        .send(Message::Binary(ClientMsg::Leave.encode().into()))
        .await
        .unwrap();
    tokio::time::timeout(Duration::from_secs(2), async {
        while rig.director.info()["rooms"] != 0 {
            tokio::time::sleep(Duration::from_millis(10)).await;
        }
    })
    .await
    .expect("both training rooms close after leave");
    assert_eq!(
        rig.director.info()["closedRooms"].as_array().unwrap().len(),
        2
    );
    assert_eq!(rig.director.info()["resultsOk"], 0);
    assert!(rig.director.take_replays().is_empty());
}

#[tokio::test]
async fn hosted_training_validates_capacity_and_admits_only_first_solo_seat() {
    let rig = Rig::start("training", true).await;
    assert_eq!(
        rig.create("bad-seats", json!({"maxPlayers":8}))
            .await
            .status,
        400
    );
    assert_eq!(
        rig.create("bad-fill", json!({"fillBotsTo":1})).await.status,
        400
    );
    let created = rig.create("solo", json!({})).await;
    assert_eq!(created.status, 200, "{}", created.body);
    assert_eq!(created.json()["maxPlayers"], 1);
    assert_eq!(
        Client::connect(&rig, "alice", "alice-secret", None)
            .await
            .err(),
        Some(rh_protocol::RejectCode::BadCredentials)
    );
    assert_eq!(rig.admit("solo", "alice").await.status, 200);
    let mut alice = Client::connect(&rig, "alice", "alice-secret", None)
        .await
        .unwrap();
    alice.until(|c| c.phase == 1 && c.training).await;
    assert_eq!(rig.admit("solo", "bob").await.status, 409);
    assert_eq!(rig.create("solo", json!({})).await.json()["maxPlayers"], 1);
    let session = alice.session;
    let room = alice.room;
    drop(alice);
    let mut resumed = Client::connect(&rig, "ignored", "", Some(session))
        .await
        .unwrap();
    assert_eq!(resumed.room, room);
    resumed.until(|c| c.training && c.phase == 1).await;
}

struct Process(std::process::Child);
impl Drop for Process {
    fn drop(&mut self) {
        if self
            .0
            .try_wait()
            .expect("inspect task-owned server")
            .is_none()
        {
            self.0.kill().expect("stop task-owned server");
            self.0.wait().expect("reap task-owned server");
        }
    }
}

#[tokio::test]
async fn real_binary_composes_core_source_and_training_without_a_hello_mode() {
    for mode in ["core", "source-arena", "training"] {
        let reserved = std::net::TcpListener::bind("127.0.0.1:0").unwrap();
        let public = reserved.local_addr().unwrap();
        let mut cmd = std::process::Command::new(env!("CARGO_BIN_EXE_rh-server"));
        cmd.args([
            "--bind",
            &public.to_string(),
            "--runtime",
            mode,
            "--max-rooms",
            "2",
        ]);
        // Isolate inherited bot environment and prove Training overrides an explicitly nonzero environment fill.
        for (key, _) in std::env::vars_os() {
            if key.to_string_lossy().starts_with("BOTS_") || key == "BOT_STATS_FILE" {
                cmd.env_remove(key);
            }
        }
        cmd.env("BOTS_FILL_TO", if mode == "training" { "8" } else { "0" });
        if mode == "source-arena" {
            cmd.args([
                "--map",
                concat!(
                    env!("CARGO_MANIFEST_DIR"),
                    "/../../maps/first-playable.json"
                ),
                "--fill-seconds",
                "1",
            ]);
        } else if mode == "core" {
            cmd.args(["--fill-seconds", "0"]);
        }
        cmd.stdout(std::process::Stdio::null())
            .stderr(std::process::Stdio::null());
        drop(reserved);
        let mut process = Process(cmd.spawn().unwrap());
        tokio::time::timeout(Duration::from_secs(4), async {
            loop {
                assert!(
                    process.0.try_wait().unwrap().is_none(),
                    "{mode} stays running"
                );
                if tokio::net::TcpStream::connect(public).await.is_ok() {
                    break;
                }
                tokio::time::sleep(Duration::from_millis(10)).await;
            }
        })
        .await
        .expect("binary listens");
        let mut alice = Client::connect_to(public, "alice", "", None).await.unwrap();
        if mode == "training" {
            alice
                .until(|c| c.phase == 1 && c.training && c.build.is_some())
                .await;
            assert!(alice.quests.is_none() && alice.trade.is_none() && alice.plan.is_none());
        } else {
            let mut bob = Client::connect_to(public, "bob", "", None).await.unwrap();
            assert_eq!(alice.room, bob.room);
            if mode == "source-arena" {
                alice.until(|c| c.phase == 3 && c.plan.is_some()).await;
                bob.until(|c| c.phase == 3 && c.plan.is_some()).await;
                for client in [&mut alice, &mut bob] {
                    client
                        .input(WireCommand::Plan {
                            spawn: None,
                            boon: None,
                            ready: Some(true),
                        })
                        .await;
                }
                alice
                    .until(|c| c.phase == 1 && c.quests.is_some() && c.trade.is_some())
                    .await;
            } else {
                alice.until(|c| c.phase == 1 && c.build.is_some()).await;
                assert!(alice.quests.is_none() && alice.trade.is_none() && alice.plan.is_none());
            }
            assert!(!alice.training);
        }
    }
}

#[test]
fn real_binary_refuses_unknown_runtime_and_incompatible_configuration() {
    for args in [
        vec!["--runtime", "unknown"],
        vec!["--runtime-mode", "training"],
        vec!["--runtime=training"],
        vec!["--runtime", "training", "--first-playable"],
        vec!["--runtime", "training", "--fill-bots", "1"],
        vec!["--runtime", "training", "--min-players", "2"],
        vec!["--runtime", "source-arena"],
        vec![
            "--runtime",
            "source-arena",
            "--map",
            concat!(
                env!("CARGO_MANIFEST_DIR"),
                "/../../parity/maps/duel.map.json"
            ),
        ],
    ] {
        let mut process = Process(
            std::process::Command::new(env!("CARGO_BIN_EXE_rh-server"))
                .args(&args)
                .stdout(std::process::Stdio::null())
                .stderr(std::process::Stdio::piped())
                .spawn()
                .unwrap(),
        );
        let deadline = std::time::Instant::now() + Duration::from_secs(3);
        let status = loop {
            if let Some(status) = process.0.try_wait().unwrap() {
                break status;
            }
            assert!(
                std::time::Instant::now() < deadline,
                "invalid runtime exits: {args:?}"
            );
            std::thread::sleep(Duration::from_millis(5));
        };
        let mut stderr = String::new();
        std::io::Read::read_to_string(process.0.stderr.as_mut().unwrap(), &mut stderr).unwrap();
        assert_eq!(status.code(), Some(2), "{args:?}: {stderr}");
        assert!(
            stderr.contains("invalid runtime settings") || stderr.contains("invalid runtime map"),
            "{args:?}: {stderr}"
        );
    }
}

#[tokio::test]
async fn captured_full_source_map_admits_encoded_planning_and_live_public_state() {
    // Only the unchanged map export is read from the capture; no captured inputs, secrets or expected outcomes enter play.
    let capture: Value =
        serde_json::from_slice(include_bytes!("../../../parity/arena/source.json")).unwrap();
    let arena =
        rh_server::runtime::source_map(&serde_json::to_vec(&capture["map"]["export"]).unwrap())
            .unwrap();
    let rig = Rig::start_arena("source-arena", false, Some(arena)).await;
    let mut alice = Client::connect(&rig, "alice", "alice-secret", None)
        .await
        .unwrap();
    let mut bob = Client::connect(&rig, "bob", "bob-secret", None)
        .await
        .unwrap();
    let map = alice.map.as_ref().unwrap();
    assert_eq!((map.w, map.h), (160, 160));
    assert_eq!(map.terrain_mode, rh_protocol::GroundMode::Map);
    assert!(map.terrain.is_some() && map.regions.is_some());
    assert_eq!(alice.room, bob.room);
    alice.until(|c| c.phase == 3 && c.plan.is_some()).await;
    bob.until(|c| c.phase == 3 && c.plan.is_some()).await;
    for client in [&mut alice, &mut bob] {
        client
            .input(WireCommand::Plan {
                spawn: None,
                boon: None,
                ready: Some(true),
            })
            .await;
    }
    alice
        .until(|c| c.phase == 1 && c.plan.is_none() && c.quests.is_some() && c.trade.is_some())
        .await;
    bob.until(|c| c.phase == 1 && c.plan.is_none() && c.quests.is_some() && c.trade.is_some())
        .await;
    for _ in 0..8 {
        alice.recv().await;
    }
    assert!(!alice.training && !bob.training);
}
