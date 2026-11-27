use super::*;
use crate::alpha_capture::test_probe::Probe;
use rh_protocol::WireCommand;

fn join_leased(room: &mut Room, profile: &str, id: i64, conn: u64, now: Instant) -> JoinOk {
    let (_sender, deadline) = tokio::sync::watch::channel(now + Duration::from_secs(3600));
    room.join(
        profile.into(),
        None,
        Arc::new(Outbox::default()),
        conn,
        1,
        Some((
            Admission {
                gen: conn,
                profile_id: Some(id),
            },
            deadline,
        )),
    )
    .unwrap()
}

fn input(room: &mut Room, player: PlayerId, conn: u64, commands: Vec<WireCommand>, now: Instant) {
    room.handle(
        RoomCmd::Input {
            player,
            conn,
            seq: room.sim.tick(),
            commands,
            received: std::time::Instant::now(),
        },
        now,
    );
}

#[test]
fn completed_room_capture_replays_every_hash_and_metrics_include_a_lobby_exit() {
    let probe = Probe::new(true, true);
    let (mut room, _reports) = arena_room(0.25, 0.25);
    room = room.with_alpha_capture(&probe.capture);
    let mut at = room.opened_at;
    let lobby = join_leased(&mut room, "LobbyPrivate", 91, 1, at);
    room.handle(
        RoomCmd::Leave {
            player: lobby.player,
            conn: 1,
        },
        at,
    );
    let lobby_metrics = probe.metrics();
    let (name, lobby_row) = &lobby_metrics[0];
    assert_eq!(
        name,
        &format!(
            "metrics-{}-{}.json.age",
            crate::control::room_id_hex(room.id),
            lobby.player.0
        )
    );
    assert_eq!(lobby_row["exitPhase"], "Lobby");
    assert_eq!(lobby_row["exitReason"], "left");
    assert!(lobby_row["startedTick"].is_null());
    assert!(!lobby_row.to_string().contains("LobbyPrivate"));
    assert!(lobby_row["admittedAtUnixMs"].as_u64().unwrap() > 0);
    assert!(probe.replay().is_none());

    let a = join_leased(&mut room, "ActivePrivate", 91, 2, at);
    let b = join_leased(&mut room, "OtherPrivate", 92, 3, at);
    let tick = Duration::from_millis(50);
    for _ in 0..20 {
        at += tick;
        assert!(room.wake(at));
        if room.sim.phase() == Phase::Active {
            break;
        }
    }
    assert_eq!(room.sim.phase(), Phase::Active);
    let started = room.sim.tick();
    let player = room.sim.player(a.player).unwrap();
    let (x, y) = (player.body.x as f32, player.body.y as f32);
    input(
        &mut room,
        a.player,
        2,
        vec![
            WireCommand::Move { x, y, seq: None },
            WireCommand::Attack { a: 0.0, t: None },
        ],
        at,
    );
    at += tick;
    assert!(room.wake(at));
    room.handle(
        RoomCmd::Disconnected {
            player: a.player,
            conn: 2,
        },
        at,
    );
    let (reply, mut resumed) = oneshot::channel();
    room.handle(
        RoomCmd::Resume {
            session: a.session,
            outbox: Arc::new(Outbox::default()),
            conn: 4,
            reply,
        },
        at,
    );
    assert!(resumed.try_recv().unwrap().is_ok());
    room.handle(
        RoomCmd::Leave {
            player: b.player,
            conn: 3,
        },
        at,
    );
    at += tick;
    assert!(room.wake(at));
    assert_eq!(room.sim.phase(), Phase::Ended);
    let (name, replay) = probe
        .replay()
        .expect("completed replay published without waiting for linger");
    assert_eq!(
        name,
        format!("replay-{}.json.age", crate::control::room_id_hex(room.id))
    );
    assert_eq!(
        replay.match_id.as_deref(),
        Some(match_id_string(room.id).as_str())
    );
    assert_eq!(replay.build, rh_sim::replay::BUILD_ID);
    assert_eq!(replay.content_hash, rh_sim::content::content_hash());
    let json = serde_json::to_vec(&replay).unwrap();
    let decoded = serde_json::from_slice(&json).unwrap();
    let (sim, events) = rh_sim::replay::run_replay(&decoded)
        .expect("every authoritative hash matches after disk serialization");
    assert_eq!(sim.state_hash(), room.sim.state_hash());
    assert_eq!(sim.tick(), room.sim.tick());
    assert!(events.iter().any(|event| matches!(event, Event::RaidEnded)));
    assert!(!room.close());
    let rows = probe.metrics();
    let a_row = rows
        .iter()
        .map(|(_, row)| row)
        .find(|row| row["player"] == a.player.0)
        .unwrap();
    assert_eq!(a_row["profilePseudonym"], lobby_row["profilePseudonym"]);
    assert_eq!(a_row["profilePseudonym"].as_str().unwrap().len(), 64);
    assert_eq!(a_row["startedTick"], started);
    assert_eq!(a_row["firstMoveTick"], started);
    assert_eq!(a_row["firstAttackTick"], started);
    assert_eq!(
        a_row["commands"],
        serde_json::json!({"attempts": 2, "accepted": 2, "moves": 1, "attacks": 1})
    );
    assert_eq!(a_row["disconnects"], 1);
    assert_eq!(a_row["reconnects"], 1);
    assert_eq!(a_row["exitReason"], "match_over");
    assert!(
        a_row["firstDamageDealtTick"].is_null(),
        "a distant attack did not land"
    );
    assert!(!a_row.to_string().contains("ActivePrivate"));
    assert!(
        probe.replay().is_none(),
        "linger and close do not publish again"
    );
}

#[test]
fn interrupted_and_disabled_capture_never_enqueue_replay() {
    for (metrics, replay) in [(true, false), (false, true), (false, false)] {
        let probe = Probe::new(metrics, replay);
        let (mut room, _reports) = arena_room(1.0, 1.0);
        room = room.with_alpha_capture(&probe.capture);
        let now = room.opened_at;
        join_leased(&mut room, "Seat", 23, 1, now);
        room.handle(RoomCmd::Abort, now);
        assert_eq!(probe.metrics().len(), usize::from(metrics));
        assert!(probe.replay().is_none());
    }
}

#[cfg(unix)]
#[test]
#[ignore = "requires age and age-keygen on PATH"]
fn real_age_source_room_40_hz_replay_and_metrics() {
    use crate::alpha_capture::CaptureOptions;
    use std::fs::{self, OpenOptions};
    use std::io::Write;
    use std::os::unix::fs::OpenOptionsExt;
    use std::path::{Path, PathBuf};
    use std::process::{Command, Stdio};
    let root = std::env::temp_dir().join(format!(
        "rh-alpha-source-{}",
        crate::control::room_id_hex(random16())
    ));
    struct Cleanup(PathBuf);
    impl Drop for Cleanup {
        fn drop(&mut self) {
            let _ = fs::remove_dir_all(&self.0);
        }
    }
    fs::create_dir(&root).unwrap();
    let _cleanup = Cleanup(root.clone());
    let age_key = root.join("fixture.agekey");
    assert!(Command::new("age-keygen")
        .arg("-o")
        .arg(&age_key)
        .stderr(Stdio::null())
        .status()
        .unwrap()
        .success());
    let recipient = Command::new("age-keygen")
        .arg("-y")
        .arg(&age_key)
        .output()
        .unwrap();
    assert!(recipient.status.success());
    let recipient = String::from_utf8(recipient.stdout)
        .unwrap()
        .trim()
        .to_owned();
    let metrics_key = root.join("metrics.key");
    OpenOptions::new()
        .create_new(true)
        .write(true)
        .mode(0o600)
        .open(&metrics_key)
        .unwrap()
        .write_all(&[7; 32])
        .unwrap();
    let metrics_dir = root.join("metrics");
    let replay_dir = root.join("replay");
    let args = vec![
        "--runtime".into(),
        "source-arena".into(),
        "--map".into(),
        "source.json".into(),
        "--alpha-capture-recipient".into(),
        recipient,
        "--alpha-metrics-dir".into(),
        metrics_dir.to_str().unwrap().into(),
        "--alpha-metrics-key-file".into(),
        metrics_key.to_str().unwrap().into(),
        "--alpha-replay-dir".into(),
        replay_dir.to_str().unwrap().into(),
    ];
    let source = crate::runtime::Settings::from_args(&args).unwrap().unwrap();
    assert_eq!(source.profile, crate::runtime::Profile::SourceArena);
    let start = || CaptureOptions::from_args(&args, true).unwrap().start(true);
    let capture = start();
    assert_eq!(capture.health()["replays"]["started"], true);
    assert_eq!(capture.health()["metrics"]["started"], true);

    let tick = Duration::from_millis(25);
    let mut config = cfg(tick);
    config.sim = source.sim();
    config.sim.tick_ms = 25;
    config.sim.lobby_seconds = 0.25;
    config.sim.fill_seconds = 0.0;
    config.sim.min_players = 2;
    config.sim.lifecycle = rh_sim::ArenaMode::Source(rh_sim::ArenaRules {
        plan_seconds: 0.25,
        ..Default::default()
    });
    let assets = crate::map_pool::for_quest_board(
        MapAssets::new(source_arena()).unwrap(),
        config.fixed_secrets.unwrap(),
        source.styled_maker(),
    )
    .expect("the live map admission may prepare the quest board from the replayed room secrets");
    config.arena = assets.arena;
    config.welcome_map = assets.welcome_map;
    let (tx, _rx) = mpsc::unbounded_channel();
    let mut at = Instant::now();
    let mut room = Room::new([8; 16], config, tx, at).with_alpha_capture(&capture);
    let a = join_leased(&mut room, "AlphaReplayName", 301, 1, at);
    let b = join_leased(&mut room, "AlphaSecondName", 302, 2, at);
    for _ in 0..40 {
        at += tick;
        assert!(room.wake(at));
        if room.sim.phase() == Phase::Active {
            break;
        }
    }
    assert_eq!(room.sim.phase(), Phase::Active);
    input(
        &mut room,
        a.player,
        1,
        vec![WireCommand::Attack { a: 1.0, t: None }],
        at,
    );
    at += tick;
    assert!(room.wake(at));
    room.handle(
        RoomCmd::Leave {
            player: b.player,
            conn: 2,
        },
        at,
    );
    at += tick;
    assert!(room.wake(at));
    assert_eq!(room.sim.phase(), Phase::Ended);
    let final_hash = room.sim.state_hash();
    let final_tick = room.sim.tick();
    assert!(!room.close());
    let deadline = std::time::Instant::now() + Duration::from_secs(10);
    while capture.health()["replays"]["pending"] != 0 || capture.health()["metrics"]["pending"] != 0
    {
        assert!(std::time::Instant::now() < deadline, "{}", capture.health());
        std::thread::sleep(Duration::from_millis(10));
    }
    assert_eq!(capture.health()["metrics"]["saved"], 2);
    assert_eq!(capture.health()["replays"]["saved"], 1);
    drop(room);
    drop(capture);

    let restarted = start();
    assert_eq!(restarted.health()["replays"]["started"], true);
    let hex = crate::control::room_id_hex([8; 16]);
    let replay_path = replay_dir.join(format!("replay-{hex}.json.age"));
    let decrypt = |path: &Path| {
        assert!(fs::read(path)
            .unwrap()
            .starts_with(b"age-encryption.org/v1\n"));
        let result = Command::new("age")
            .args(["--decrypt", "--identity"])
            .arg(&age_key)
            .arg(path)
            .output()
            .unwrap();
        assert!(result.status.success());
        result.stdout
    };
    let replay_json = decrypt(&replay_path);
    assert!(replay_json
        .windows(b"AlphaReplayName".len())
        .any(|w| w == b"AlphaReplayName"));
    let replay: Replay = serde_json::from_slice(&replay_json).unwrap();
    assert_eq!(replay.config.tick_ms, 25);
    assert_eq!(replay.hashes.len(), final_tick as usize);
    let (sim, _) = rh_sim::replay::run_replay(&replay)
        .expect("every encrypted replay hash matches the live Source room");
    assert_eq!(sim.state_hash(), final_hash);
    let metric_path = metrics_dir.join(format!("metrics-{hex}-{}.json.age", a.player.0));
    let metric_json = decrypt(&metric_path);
    assert!(!metric_json
        .windows(b"AlphaReplayName".len())
        .any(|w| w == b"AlphaReplayName"));
    let row: serde_json::Value = serde_json::from_slice(&metric_json).unwrap();
    assert_eq!(row["tickMs"], 25);
    assert_eq!(row["commands"]["attacks"], 1);
    assert_eq!(row["exitReason"], "match_over");
}
