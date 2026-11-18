//! Bot fill: arrival schedule, newest-bot yield, result reports, Brain fallback and delivery latency.

use super::*;

/// The fill policy seats bots over the countdown (arrival schedule), tops the lobby up on the closing step and is a
/// pure function of its seed: the same room config replays the same seats.
#[test]
fn the_fill_policy_schedules_arrivals_over_the_countdown() {
    let now = Instant::now();
    let tick = Duration::from_millis(50);
    let run = || {
        let (tx, _rx) = mpsc::unbounded_channel();
        let mut config = cfg(tick);
        config.fill_bots_to = 0;
        config.fixed_bot_seed = Some(7);
        let mut room = Room::new([12; 16], config, tx, now).with_bot_fill(BotFillConfig {
            fill_to: 4,
            arrival_seconds: 1.0,
            ..BotFillConfig::default()
        });
        room.join(
            "human1".into(),
            None,
            Arc::new(Outbox::default()),
            1,
            1,
            None,
        )
        .unwrap();
        let mut before = 0;
        let mut first_arrival = None;
        for i in 1..=609 {
            room.wake(now + tick * i);
            let bots = room.sim.players().iter().filter(|p| p.seat.bot).count();
            if i == 580 {
                before = bots;
            }
            if bots > 0 && first_arrival.is_none() {
                first_arrival = Some(i);
            }
        }
        let fill = room.fill.as_ref().expect("the fill policy is on");
        let lines: Vec<String> = fill.specs().iter().map(|spec| spec.line()).collect();
        let players: Vec<(String, FactionId, bool)> = room
            .sim
            .players()
            .iter()
            .map(|p| (p.seat.name.clone(), p.faction(), p.seat.bot))
            .collect();
        (before, first_arrival, lines, players, room.sim.phase())
    };
    let (before, first, lines, players, phase) = run();
    assert_eq!(before, 0, "no bot arrives before the arrival window");
    let first = first.expect("bots arrive");
    assert!(
        first > 560,
        "the first arrival is inside the last seconds, at tick {first}"
    );
    assert_eq!(lines.len(), 3, "the lobby is topped up to four players");
    assert_eq!(phase, Phase::Active, "the countdown closes into the raid");
    assert_eq!(players.len(), 4);
    assert_eq!(players.iter().filter(|(_, _, bot)| *bot).count(), 3);
    let names: Vec<&str> = lines
        .iter()
        .map(|line| line.split(' ').nth(1).expect("a rendered name"))
        .collect();
    assert_eq!(
        names.iter().collect::<std::collections::HashSet<_>>().len(),
        3,
        "callsigns are unique in the room"
    );
    for name in &names {
        assert!(
            crate::bot_fill::CALLSIGN_POOL.contains(name)
                || crate::bot_fill::CALLSIGN_POOL
                    .iter()
                    .any(|pool| name.starts_with(pool) && name.len() == pool.len() + 2),
            "the name is a callsign: {name}"
        );
    }
    // Every bot's sim seat carries the drawn faction, and the seat acts as a normal bot seat.
    for (name, faction, bot) in &players {
        if *bot {
            assert!(
                FactionId::ALL.contains(faction),
                "{name} has a known faction"
            );
        }
    }
    // Determinism: the same fixed bot seed and plan draw the same seats.
    let (_, _, again, _, _) = run();
    assert_eq!(lines, again, "the fill policy replays from its seed");
}

#[test]
fn a_human_joining_a_full_lobby_takes_the_newest_bot_seat() {
    let now = Instant::now();
    let tick = Duration::from_millis(50);
    let (tx, _rx) = mpsc::unbounded_channel();
    let mut config = cfg(tick);
    config.fill_bots_to = 0;
    config.fixed_bot_seed = Some(5);
    let mut room = Room::new([13; 16], config, tx, now).with_bot_fill(BotFillConfig {
        fill_to: 8,
        arrival_seconds: 0.2,
        ..BotFillConfig::default()
    });
    room.join(
        "human1".into(),
        None,
        Arc::new(Outbox::default()),
        1,
        1,
        None,
    )
    .unwrap();
    for i in 1..=600 {
        room.wake(now + tick * i);
        if room.sim.players().len() >= 8 {
            break;
        }
    }
    assert_eq!(room.sim.players().len(), 8, "the fill seats seven bots");
    assert_eq!(
        room.sim.phase(),
        Phase::Lobby,
        "the join happens in the lobby"
    );
    let specs_before = room.fill.as_ref().unwrap().specs().len();
    let bots_before = room.sim.players().iter().filter(|p| p.seat.bot).count();
    room.join(
        "human2".into(),
        None,
        Arc::new(Outbox::default()),
        2,
        2,
        None,
    )
    .unwrap();
    assert_eq!(room.sim.players().len(), 8, "the human takes the seat");
    assert_eq!(
        room.sim.players().iter().filter(|p| p.seat.bot).count(),
        bots_before - 1
    );
    assert_eq!(room.sim.players().iter().filter(|p| !p.seat.bot).count(), 2);
    let fill = room.fill.as_ref().unwrap();
    assert_eq!(fill.stats().yielded, 1);
    assert_eq!(fill.specs().len(), specs_before - 1);
    assert_eq!(fill.stats().arrived, 7);
}

/// P-BOTS-5 remainder: a resolved fill bot's report carries the spec the record caller needs (tier/persona/faction),
/// so the director can build the learning summary without guessing from the wire result.
#[test]
fn a_bot_result_report_carries_its_fill_spec_once() {
    use rh_policy::bots::{PersonaId, TierId};
    let (reports, mut rx) = mpsc::unbounded_channel();
    let mut room = Room::new(
        [7; 16],
        cfg(Duration::from_millis(50)),
        reports,
        Instant::now(),
    );
    let spec = BotSeatSpec {
        number: 1,
        name: "Wren".into(),
        tier: TierId::Hard,
        persona: PersonaId::Hunter,
        faction: FactionId::Plague,
        seed: 5,
    };
    let bot = PlayerId(1);
    room.bot_specs.push((bot, spec.clone()));
    let result = rh_sim::RunResult {
        outcome: rh_sim::Outcome::Died,
        profile: "Wren".into(),
        faction: Some(FactionId::Plague),
        weapon: None,
        duration_sec: 3,
        kills: 1,
        killed_by: None,
        killed_by_bot: false,
        place: 2,
        players: 2,
        gv: Default::default(),
        win_bonus_withheld: false,
        win_bonus_why: None,
    };
    room.report_results(&[Event::Resolved {
        id: bot,
        result: result.clone(),
        bot: true,
    }]);
    // Source BotManager.onResult's ended guard: a repeated native event must not train the cell twice.
    room.report_results(&[Event::Resolved {
        id: bot,
        result: result.clone(),
        bot: true,
    }]);
    match rx.try_recv().expect("a bot result report") {
        RoomReport::Result {
            bot: true,
            bot_spec: Some(got),
            result: reported,
            ..
        } => {
            assert_eq!(got, spec);
            assert_eq!(reported, result);
        }
        other => panic!("expected a bot result with its fill spec, got {other:?}"),
    }
    assert!(
        rx.try_recv().is_err(),
        "one managed seat records at most one result"
    );
}

/// P-BOT-BRAIN-LIVE-1: `--bot-brain full` seats BrainBot with a decision budget, HumanBot as its fault fallback and the
/// replay name "brain"; `off` keeps HumanBot, Idle and no budget. On this core (non-source) runtime the Brain finds no
/// source arena group, refuses, and its HumanBot fallback plays on.
#[test]
fn a_brain_fill_room_seats_budgeted_brains_that_fall_back_to_humanbot() {
    let run = |brain: crate::bot_fill::BotBrain| {
        let start = Instant::now();
        let tick = Duration::from_millis(50);
        let (tx, _rx) = mpsc::unbounded_channel();
        let mut config = cfg(tick);
        config.fill_bots_to = 0;
        config.record = true;
        config.fixed_bot_seed = Some(105);
        config.fixed_secrets = Some(Secrets::from_seed(7));
        let mut room = Room::new([22; 16], config, tx, start).with_bot_fill(BotFillConfig {
            fill_to: 4,
            arrival_seconds: 0.,
            jitter_ms: 0.,
            seed: Some(5),
            brain,
            ..BotFillConfig::default()
        });
        for i in 0..2u64 {
            room.join(
                format!("human{i}"),
                None,
                Arc::new(Outbox::default()),
                i + 1,
                1,
                None,
            )
            .unwrap();
        }
        let mut at = start;
        for _ in 0..600 {
            at += tick;
            room.wake(at);
            if room.sim.phase() == Phase::Active {
                break;
            }
        }
        for _ in 0..200 {
            at += tick;
            room.wake(at);
        }
        room
    };
    let names = |room: &Room| -> Vec<String> {
        room.recorder
            .as_ref()
            .expect("recording")
            .replay
            .ticks
            .iter()
            .flat_map(|t| &t.inputs)
            .filter_map(|input| match input {
                ReplayInput::Join {
                    policy: Some(p), ..
                } => Some(p.name.clone()),
                _ => None,
            })
            .collect()
    };
    let brain = run(crate::bot_fill::BotBrain::Full);
    assert!(!brain.bots.is_empty(), "the fill seated bots");
    for seat in &brain.bots {
        assert_eq!(seat.budget, Some(crate::bot_fill::BRAIN_DECISION_BUDGET));
        assert_eq!(
            seat.fault,
            Some(rh_policy::Fault::Input(
                rh_policy::InputRefusal::ArenaUnavailable
            ))
        );
        assert!(seat.stats.fallback_decisions > 0, "HumanBot plays on");
    }
    assert!(
        brain.bots.iter().any(|s| s.stats.proposed > 0),
        "the HumanBot fallback proposes"
    );
    assert!(!names(&brain).is_empty() && names(&brain).iter().all(|n| n == "brain"));
    let human = run(crate::bot_fill::BotBrain::Off);
    for seat in &human.bots {
        assert_eq!(seat.budget, None);
        assert_eq!(seat.fault, None);
    }
    assert!(!names(&human).is_empty() && names(&human).iter().all(|n| n == "human"));
}

/// P-BOT-BRAIN-FAULT-LOG-1: a Brain seat that falls back says so once per seat; the start line shows the Brain was
/// seated; with the flag off the log has neither. (The `eprintln!` itself is read, not tested.)
#[test]
fn a_brain_seat_fallback_is_logged_once_per_seat() {
    let run = |brain: crate::bot_fill::BotBrain, extra_ticks: usize| {
        let start = Instant::now();
        let tick = Duration::from_millis(50);
        let (tx, _rx) = mpsc::unbounded_channel();
        let mut config = cfg(tick);
        config.fill_bots_to = 0;
        config.fixed_bot_seed = Some(105);
        config.fixed_secrets = Some(Secrets::from_seed(7));
        let mut room = Room::new([22; 16], config, tx, start).with_bot_fill(BotFillConfig {
            fill_to: 4,
            arrival_seconds: 0.,
            jitter_ms: 0.,
            seed: Some(5),
            brain,
            ..BotFillConfig::default()
        });
        for i in 0..2u64 {
            room.join(
                format!("human{i}"),
                None,
                Arc::new(Outbox::default()),
                i + 1,
                1,
                None,
            )
            .unwrap();
        }
        let mut at = start;
        for _ in 0..600 {
            at += tick;
            room.wake(at);
            if room.sim.phase() == Phase::Active {
                break;
            }
        }
        for _ in 0..extra_ticks {
            at += tick;
            room.wake(at);
        }
        room
    };
    let id = crate::control::room_id_hex([22; 16]);
    let fallback_lines = |room: &Room| -> Vec<String> {
        room.log_lines()
            .iter()
            .filter(|l| l.contains("brain fell back to the fallback bot"))
            .cloned()
            .collect()
    };
    let short = run(crate::bot_fill::BotBrain::Full, 5);
    let long = run(crate::bot_fill::BotBrain::Full, 400);
    let seats = long.bots.len();
    assert!(seats > 0);
    for seat in &long.bots {
        assert!(seat.fault.is_some(), "the seat falls back as before");
        assert!(seat.stats.fallback_decisions > 0);
    }
    assert_eq!(fallback_lines(&short).len(), seats, "one line per seat");
    let lines = fallback_lines(&long);
    assert_eq!(lines.len(), seats, "no more lines after many more ticks");
    for (i, (_, spec)) in long.bot_specs().iter().enumerate() {
        let prefix = format!(
            "[bots] match={id} seat={} brain fell back to the fallback bot: ",
            spec.number
        );
        assert_eq!(
            lines.iter().filter(|l| l.starts_with(&prefix)).count(),
            1,
            "seat {i}: {lines:?}"
        );
    }
    let start_line = format!("[bots] match={id} fill bots: {seats} brain");
    assert_eq!(
        long.log_lines()
            .iter()
            .filter(|l| **l == start_line)
            .count(),
        1,
        "{:?}",
        long.log_lines()
    );
    let off = run(crate::bot_fill::BotBrain::Off, 50);
    assert!(
        off.log_lines().iter().all(|l| !l.starts_with("[bots]")),
        "{:?}",
        off.log_lines()
    );
}

/// P-BOTS-7 remainder: a fill room with a running raid, for the delay-line and name-seam tests.
fn running_fill_room(
    latency_ms: f64,
    seed: u32,
    taken: Arc<std::collections::HashSet<String>>,
) -> (Room, Instant) {
    let start = Instant::now();
    let tick = Duration::from_millis(50);
    let (tx, _rx) = mpsc::unbounded_channel();
    let mut config = cfg(tick);
    config.fill_bots_to = 0;
    config.fixed_bot_seed = Some(u64::from(seed) + 100);
    config.fixed_secrets = Some(Secrets::from_seed(7));
    let mut room = Room::new([21; 16], config, tx, start)
        .with_bot_fill(BotFillConfig {
            fill_to: 4,
            arrival_seconds: 0.,
            latency_ms,
            jitter_ms: 0.,
            seed: Some(seed),
            ..BotFillConfig::default()
        })
        .with_bot_names(taken);
    for i in 0..2u64 {
        room.join(
            format!("human{i}"),
            None,
            Arc::new(Outbox::default()),
            i + 1,
            1,
            None,
        )
        .unwrap();
    }
    let mut last = start;
    for i in 1..=600u32 {
        last = start + tick * i;
        room.wake(last);
        if room.sim.phase() == Phase::Active {
            break;
        }
    }
    assert_eq!(
        room.sim.phase(),
        Phase::Active,
        "the lobby fills and closes"
    );
    assert!(!room.bot_specs.is_empty(), "the fill seated bots");
    (room, last)
}

#[test]
fn a_fill_bots_command_arrives_after_the_configured_latency() {
    let (mut room, last) =
        running_fill_room(100., 9, Arc::new(std::collections::HashSet::new()));
    let tick = Duration::from_millis(50);
    // Find the first wake on which the seated bots propose: with a 100 ms one-way latency those proposals cannot be
    // applied by the same or the next 50 ms step (the source deliver/beforeStep rule).
    let mut first = None;
    for i in 1..=12u32 {
        room.wake(last + tick * i);
        let (sent, applied, _, _) = room.bot_delivery();
        if sent > 0 {
            first = Some((i, sent, applied));
            break;
        }
    }
    let (i, sent, applied) = first.expect("the seated bots propose commands");
    assert!(sent > 0);
    assert_eq!(
        applied, 0,
        "a 100 ms command cannot arrive within the first step"
    );
    // The arrival is due before the next step.
    room.wake(last + tick * (i + 1));
    let (_, applied, refused, lost) = room.bot_delivery();
    assert!(applied > 0, "the command arrives at the next step");
    assert!(refused <= sent);
    assert_eq!(lost, 0);
    // Determinism: the same fixed seed draws the same manager stream and the same counters.
    let (mut again, last_again) =
        running_fill_room(100., 9, Arc::new(std::collections::HashSet::new()));
    for k in 1..=i + 1 {
        again.wake(last_again + tick * k);
    }
    assert_eq!(
        room.bot_delivery(),
        again.bot_delivery(),
        "the delay line replays from its seed"
    );
}

#[test]
fn a_fill_bot_never_draws_a_profile_owned_callsign() {
    let taken = crate::bot_fill::name_set(
        crate::bot_fill::CALLSIGN_POOL
            .iter()
            .map(|name| (*name).to_string()),
    );
    let (room, _last) = running_fill_room(30., 11, taken.clone());
    assert!(!room.bot_specs.is_empty());
    for (_, spec) in &room.bot_specs {
        assert!(
            !taken.contains(&spec.name.to_ascii_lowercase()),
            "drew a profile-owned callsign {}",
            spec.name
        );
    }
}

/// P-BOTS-7b repair: bot_refused is the source's DELIVERY-time intake refusal; a delay-line cap rejection (source
/// MAX_QUEUED, which the source stats do not report at all) is the separate native bot_line_dropped counter.
#[test]
fn bot_refused_is_delivery_intake_only_and_line_drops_are_separate() {
    let (mut room, _last) =
        running_fill_room(0., 13, Arc::new(std::collections::HashSet::new()));
    assert!(
        !room.bot_lines.is_empty(),
        "the fill seated bots with delay lines"
    );
    let player = room.bot_lines[0].0;
    let (sent0, _applied0, refused0, _lost0) = room.bot_delivery();
    let dropped0 = room.bot_line_dropped();
    let cap = rh_policy::bots::delay::MAX_QUEUED;
    let batch: Vec<(PlayerId, rh_sim::PublicCommand)> = (0..cap + 5)
        .map(|_| (player, rh_sim::PublicCommand::Stop { seq: None }))
        .collect();
    room.enqueue_bot_commands(batch);
    let (sent, _applied, refused, _lost) = room.bot_delivery();
    let accepted = sent - sent0;
    let dropped = room.bot_line_dropped() - dropped0;
    assert_eq!(dropped, 5, "the overflow past MAX_QUEUED is a line drop");
    assert_eq!(
        accepted + dropped,
        cap as u64 + 5,
        "every proposal is either queued or dropped"
    );
    assert!(accepted <= cap as u64);
    assert_eq!(
        refused, refused0,
        "a line-cap drop is not a delivery refusal"
    );
    // A delivery-time refusal (a delivered proposal the resolver rejects) is exactly what the source counts. Actor 0
    // is the reserved "no actor" id: the resolver refuses the target instead of validating a missing-target command.
    room.submit_bot_now(player, rh_sim::PublicCommand::Target { actor: PlayerId(0) });
    let (_, _, refused, _) = room.bot_delivery();
    assert_eq!(
        refused,
        refused0 + 1,
        "an unresolvable delivered command is refused"
    );
    assert_eq!(
        room.bot_line_dropped() - dropped0,
        5,
        "intake refusal is not a line drop"
    );
}
