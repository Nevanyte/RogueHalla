use super::*;

#[test]
fn streams_default_off_and_opt_in_independently_on_source() {
    fn parse(text: &str, source: bool) -> Result<CaptureOptions, &'static str> {
        CaptureOptions::from_args(
            &text
                .split_whitespace()
                .map(str::to_owned)
                .collect::<Vec<_>>(),
            source,
        )
    }
    let disabled = parse("--runtime core", false).unwrap().start(false);
    assert_eq!(disabled.health()["metrics"]["configured"], false);
    assert_eq!(disabled.health()["replays"]["configured"], false);
    let metrics = "--alpha-metrics-dir metrics --alpha-metrics-key-file key --alpha-capture-recipient invalid";
    let replay = "--alpha-replay-dir replays --alpha-capture-recipient invalid";
    assert!(parse(metrics, false).is_err());
    assert!(parse(replay, false).is_err());
    assert!(parse(
        "--alpha-metrics-dir metrics --alpha-capture-recipient recipient",
        true
    )
    .is_err());
    assert!(parse("--alpha-replay-dir replay", true).is_err());
    let metrics = parse(metrics, true).unwrap().start(true).health();
    assert_eq!(metrics["metrics"]["startFailed"], true);
    assert_eq!(metrics["replays"]["configured"], false);
    let replay = parse(replay, true).unwrap().start(true).health();
    assert_eq!(replay["metrics"]["configured"], false);
    assert_eq!(replay["replays"]["startFailed"], true);
}

#[test]
fn replay_limits_drop_the_whole_capture() {
    let (sink, rx) = Stream::probe(Arc::new(StreamStats::default()), 1);
    let sim = Sim::new(
        Default::default(),
        rh_sim::arenas::practice_arena(),
        rh_sim::Secrets::from_seed(1),
    );
    let mut capture = RoomReplay::new(sink.clone(), [1; 16], &sim);
    capture.inputs = REPLAY_INPUTS;
    let mut room = Some(capture);
    RoomReplay::record(
        &mut room,
        ReplayInput::Leave {
            player: rh_sim::PlayerId(1),
        },
    );
    assert!(room.is_none());
    let mut capture = RoomReplay::new(sink.clone(), [2; 16], &sim);
    capture.recorder.replay.hashes.resize(REPLAY_TICKS, 0);
    let mut room = Some(capture);
    RoomReplay::step(&mut room, &sim, true);
    assert!(room.is_none());
    assert!(rx.try_recv().is_err());
}
