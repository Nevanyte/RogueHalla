//! Comparison of the hand's results against the declared ones, exact or within tolerance.

use super::*;

// --------------------------------------------------------------------------------------------------------- comparison

pub(super) fn close(got: f64, want: f64, label: &str) {
    assert!(
        (got - want).abs() <= 1e-9 * want.abs().max(1.),
        "{label}: native {got} source {want}"
    );
}

pub(super) fn compare_stimulus(got: &Option<Stimulus>, want: &Option<Stimulus>, label: &str) {
    match (got, want) {
        (None, None) => {}
        (Some(got), Some(want)) => {
            assert_eq!(got.kind, want.kind, "{label} stimulus kind");
            assert_eq!(got.subject, want.subject, "{label} stimulus subject");
            assert_eq!(got.central, want.central, "{label} stimulus central");
            close(got.at, want.at, &format!("{label} stimulus at"));
            match (got.value, want.value) {
                (None, None) => {}
                (Some(got), Some(want)) => close(got, want, &format!("{label} stimulus value")),
                _ => panic!("{label}: stimulus value presence differs"),
            }
        }
        _ => panic!("{label}: stimulus presence differs"),
    }
}

pub(super) fn compare_command(got: &Command, want: &Command, label: &str) {
    match (got, want) {
        (Command::Move { x: gx, y: gy }, Command::Move { x: wx, y: wy }) => {
            close(*gx, *wx, &format!("{label} move.x"));
            close(*gy, *wy, &format!("{label} move.y"));
        }
        (Command::Stop {}, Command::Stop {}) => {}
        (Command::Target { id: got }, Command::Target { id: want }) => {
            assert_eq!(got, want, "{label} target id");
        }
        (Command::Attack { a: got }, Command::Attack { a: want }) => {
            close(*got, *want, &format!("{label} attack angle"));
        }
        (Command::Release { a: got }, Command::Release { a: want }) => {
            close(*got, *want, &format!("{label} release angle"));
        }
        (Command::Swap { slot: got }, Command::Swap { slot: want }) => {
            assert_eq!(got, want, "{label} swap slot");
        }
        (Command::Pickup { container_id: got }, Command::Pickup { container_id: want }) => {
            assert_eq!(got, want, "{label} pickup container");
        }
        (
            Command::Cast {
                slot: got_slot,
                x: got_x,
                y: got_y,
                id: got_id,
            },
            Command::Cast {
                slot: want_slot,
                x: want_x,
                y: want_y,
                id: want_id,
            },
        ) => {
            assert_eq!(got_slot, want_slot, "{label} cast slot");
            assert_eq!(got_id, want_id, "{label} cast id");
            close(*got_x, *want_x, &format!("{label} cast.x"));
            close(*got_y, *want_y, &format!("{label} cast.y"));
        }
        (
            Command::Plan {
                spawn: got_spawn,
                boon: got_boon,
                ready: got_ready,
            },
            Command::Plan {
                spawn: want_spawn,
                boon: want_boon,
                ready: want_ready,
            },
        ) => {
            assert_eq!(got_spawn, want_spawn, "{label} plan spawn");
            assert_eq!(got_boon, want_boon, "{label} plan boon");
            assert_eq!(got_ready, want_ready, "{label} plan ready");
        }
        (
            Command::Choose {
                chest: got_chest,
                pick: got_pick,
                replace: got_replace,
            },
            Command::Choose {
                chest: want_chest,
                pick: want_pick,
                replace: want_replace,
            },
        ) => {
            assert_eq!(got_chest, want_chest, "{label} choose chest");
            assert_eq!(got_pick, want_pick, "{label} choose pick");
            assert_eq!(got_replace, want_replace, "{label} choose replace");
        }
        _ => panic!("{label}: released command kind differs"),
    }
}

pub(super) fn compare_commands(got: &[TimedCommand], want: &[TimedCommand], label: &str) {
    assert_eq!(got.len(), want.len(), "{label}: released command count");
    for (index, (got, want)) in got.iter().zip(want.iter()).enumerate() {
        let command_label = format!("{label} result[{index}]");
        close(got.at, want.at, &format!("{command_label}.at"));
        assert_eq!(got.intent, want.intent, "{command_label}.intent");
        assert_eq!(got.priority, want.priority, "{command_label}.priority");
        compare_stimulus(&got.stimulus, &want.stimulus, &command_label);
        compare_command(&got.cmd, &want.cmd, &command_label);
    }
}

pub(super) fn compare_misses(got: &[ClickMiss], want: &[ClickMiss], label: &str) {
    assert_eq!(got.len(), want.len(), "{label}: miss count");
    for (index, (got, want)) in got.iter().zip(want.iter()).enumerate() {
        assert_eq!(got.target, want.target, "{label} miss[{index}].target");
        close(got.at, want.at, &format!("{label} miss[{index}].at"));
    }
}

pub(super) fn compare_stats(got: &HumanizerStats, want: &HumanizerStats, label: &str) {
    for command_type in COMMAND_TYPES {
        assert_eq!(
            got.command(command_type),
            want.command(command_type),
            "{label}: commands.{}",
            command_type.as_str()
        );
    }
    assert_eq!(
        got.target_clicks, want.target_clicks,
        "{label}: targetClicks"
    );
    assert_eq!(got.click_missed, want.click_missed, "{label}: clickMissed");
    assert_eq!(got.click_wrong, want.click_wrong, "{label}: clickWrong");
    assert_eq!(got.hesitations, want.hesitations, "{label}: hesitations");
    assert_eq!(got.lapses, want.lapses, "{label}: lapses");
    assert_eq!(got.throttled, want.throttled, "{label}: throttled");
    assert_eq!(got.superseded, want.superseded, "{label}: superseded");
    assert_eq!(got.apm_peak, want.apm_peak, "{label}: apmPeak");
    assert_eq!(
        got.reactions.keys().collect::<Vec<_>>(),
        want.reactions.keys().collect::<Vec<_>>(),
        "{label}: reaction kinds"
    );
    for (kind, got_times) in &got.reactions {
        let want_times = &want.reactions[kind];
        assert_eq!(
            got_times.len(),
            want_times.len(),
            "{label}: reaction {kind:?} count"
        );
        for (index, (got, want)) in got_times.iter().zip(want_times.iter()).enumerate() {
            close(*got, *want, &format!("{label}: reaction {kind:?}[{index}]"));
        }
    }
}

/// The actual draw count and values between `from` and `to` on one lane, found by replaying the declared stream (the
/// hand API is driven, not instrumented: the seeded mulberry32 stream after n draws is unique for these bounded phases).
pub(super) fn replay_draws(from: &Rng, to: &Rng, limit: usize) -> Option<(usize, Vec<f64>)> {
    let mut probe = from.clone();
    let mut values = Vec::new();
    for drawn in 0..=limit {
        if &probe == to {
            return Some((drawn, values));
        }
        values.push(probe.next_f64());
    }
    None
}

pub(super) fn check_phase(
    label: &str,
    phase: &str,
    trace: &[Draw],
    replay: &mut [Rng; 2],
    actual: &[Rng; 2],
    drawn: &mut Counts,
) -> Result<(), String> {
    let lanes = [(Lane::Motor, 0usize), (Lane::Lapse, 1usize)];
    for (lane, slot) in lanes {
        let expected: Vec<f64> = trace
            .iter()
            .filter(|draw| draw.lane == lane)
            .map(|draw| draw.value)
            .collect();
        let (count, values) =
            replay_draws(&replay[slot], &actual[slot], 4096).ok_or_else(|| {
                err(
                    label,
                    format!(
                        "{phase} {lane:?} draws are not a prefix of the declared seeded stream"
                    ),
                )
            })?;
        if count != expected.len() {
            return Err(err(
                label,
                format!(
                    "{phase} {lane:?} consumed {count} draws but the source recorded {}",
                    expected.len()
                ),
            ));
        }
        for (index, (got, want)) in values.iter().zip(expected.iter()).enumerate() {
            if got.to_bits() != want.to_bits() {
                return Err(err(
                    label,
                    format!("{phase} {lane:?} draw[{index}] native {got} source {want}"),
                ));
            }
        }
        replay[slot] = actual[slot].clone();
        drawn.add(lane, count as u32);
    }
    Ok(())
}
