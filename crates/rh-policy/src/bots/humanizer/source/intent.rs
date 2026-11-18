//! Typed reads of stimuli, plan/choose messages, intents, commands, misses and stats.

use super::*;

pub(super) fn read_intents(value: &Mv, path: &str) -> Result<Vec<Intent>, String> {
    let mut intents = Vec::new();
    for (index, item) in value.arr(path)?.iter().enumerate() {
        intents.push(read_intent(item, &format!("{path}[{index}]"))?);
    }
    Ok(intents)
}

pub(super) fn read_intent(value: &Mv, path: &str) -> Result<Intent, String> {
    let kind = value.get("kind", path)?.text(&format!("{path}.kind"))?;
    fn keys(extra: &[&'static str]) -> Vec<&'static str> {
        ["kind", "priority", "timing", "why"]
            .into_iter()
            .chain(extra.iter().copied())
            .collect()
    }
    let body = match kind {
        "lock" => {
            value.exact(&keys(&["target", "at"]), path)?;
            IntentBody::Lock {
                target: value
                    .get("target", path)?
                    .text(&format!("{path}.target"))?
                    .to_string(),
                at: read_vec2(value.get("at", path)?, &format!("{path}.at"))?,
            }
        }
        "move" => {
            value.exact(&keys(&["to"]), path)?;
            IntentBody::Move {
                to: read_vec2(value.get("to", path)?, &format!("{path}.to"))?,
            }
        }
        "stop" => {
            value.exact(&keys(&[]), path)?;
            IntentBody::Stop {}
        }
        "swap" => {
            value.exact(&keys(&["slot"]), path)?;
            IntentBody::Swap {
                slot: value.get("slot", path)?.uint(&format!("{path}.slot"))? as u8,
            }
        }
        "pickup" => {
            value.exact(&keys(&["container"]), path)?;
            IntentBody::Pickup {
                container: value
                    .get("container", path)?
                    .text(&format!("{path}.container"))?
                    .to_string(),
            }
        }
        "cast" => {
            value.closed(
                &["kind", "slot", "x", "y", "priority", "timing", "why"],
                &["target", "at", "snapRadius"],
                path,
            )?;
            let target = match value.optional("target", path)? {
                None | Some(Mv::Null) => None,
                Some(target) => Some(target.text(&format!("{path}.target"))?.to_string()),
            };
            let at = match value.optional("at", path)? {
                None | Some(Mv::Null) => None,
                Some(point) => Some(read_vec2(point, &format!("{path}.at"))?),
            };
            let snap_radius = match value.optional("snapRadius", path)? {
                None | Some(Mv::Null) => None,
                Some(radius) => Some(radius.num(&format!("{path}.snapRadius"))?),
            };
            IntentBody::Cast {
                slot: value
                    .get("slot", path)?
                    .text(&format!("{path}.slot"))?
                    .to_string(),
                x: value.get("x", path)?.num(&format!("{path}.x"))?,
                y: value.get("y", path)?.num(&format!("{path}.y"))?,
                target,
                at,
                snap_radius,
            }
        }
        "plan" => {
            value.exact(&keys(&["msg"]), path)?;
            IntentBody::Plan {
                msg: read_plan_msg(value.get("msg", path)?, &format!("{path}.msg"))?,
            }
        }
        "choose" => {
            value.exact(&keys(&["msg"]), path)?;
            IntentBody::Choose {
                msg: read_choose_msg(value.get("msg", path)?, &format!("{path}.msg"))?,
            }
        }
        other => {
            return Err(err(
                &format!("{path}.kind"),
                format!("unknown intent kind {other}"),
            ))
        }
    };
    Ok(Intent {
        body,
        priority: value
            .get("priority", path)?
            .uint(&format!("{path}.priority"))? as u8,
        timing: read_timing(value.get("timing", path)?, &format!("{path}.timing"))?,
        why: value
            .get("why", path)?
            .text(&format!("{path}.why"))?
            .to_string(),
    })
}

pub(super) fn read_intent_kind(text: &str) -> Option<IntentKind> {
    match text {
        "lock" => Some(IntentKind::Lock),
        "move" => Some(IntentKind::Move),
        "stop" => Some(IntentKind::Stop),
        "swap" => Some(IntentKind::Swap),
        "pickup" => Some(IntentKind::Pickup),
        "cast" => Some(IntentKind::Cast),
        "plan" => Some(IntentKind::Plan),
        "choose" => Some(IntentKind::Choose),
        _ => None,
    }
}

pub(super) fn read_timed_commands(value: &Mv, path: &str) -> Result<Vec<TimedCommand>, String> {
    let mut commands = Vec::new();
    for (index, item) in value.arr(path)?.iter().enumerate() {
        let item_path = format!("{path}[{index}]");
        item.exact(&["at", "cmd", "intent", "priority", "stimulus"], &item_path)?;
        let intent_text = item
            .get("intent", &item_path)?
            .text(&format!("{item_path}.intent"))?;
        let intent = read_intent_kind(intent_text).ok_or_else(|| {
            err(
                &format!("{item_path}.intent"),
                format!("unknown released intent {intent_text}"),
            )
        })?;
        let stimulus = match item.get("stimulus", &item_path)? {
            Mv::Null => None,
            stimulus => Some(read_stimulus(stimulus, &format!("{item_path}.stimulus"))?),
        };
        commands.push(TimedCommand {
            at: item
                .get("at", &item_path)?
                .num(&format!("{item_path}.at"))?,
            cmd: read_command(item.get("cmd", &item_path)?, &format!("{item_path}.cmd"))?,
            intent,
            priority: item
                .get("priority", &item_path)?
                .uint(&format!("{item_path}.priority"))? as u8,
            stimulus,
        });
    }
    Ok(commands)
}

pub(super) fn read_command(value: &Mv, path: &str) -> Result<Command, String> {
    value.exact(&["type", "msg"], path)?;
    let kind = value.get("type", path)?.text(&format!("{path}.type"))?;
    let msg = value.get("msg", path)?;
    let msg_path = format!("{path}.msg");
    match kind {
        "move" => {
            msg.exact(&["x", "y"], &msg_path)?;
            Ok(Command::Move {
                x: msg.get("x", &msg_path)?.num(&format!("{msg_path}.x"))?,
                y: msg.get("y", &msg_path)?.num(&format!("{msg_path}.y"))?,
            })
        }
        "stop" => {
            msg.exact(&[], &msg_path)?;
            Ok(Command::Stop {})
        }
        "target" => {
            msg.exact(&["id"], &msg_path)?;
            Ok(Command::Target {
                id: msg
                    .get("id", &msg_path)?
                    .text(&format!("{msg_path}.id"))?
                    .to_string(),
            })
        }
        "attack" => {
            msg.exact(&["a"], &msg_path)?;
            Ok(Command::Attack {
                a: msg.get("a", &msg_path)?.num(&format!("{msg_path}.a"))?,
            })
        }
        "release" => {
            msg.exact(&["a"], &msg_path)?;
            Ok(Command::Release {
                a: msg.get("a", &msg_path)?.num(&format!("{msg_path}.a"))?,
            })
        }
        "swap" => {
            msg.exact(&["slot"], &msg_path)?;
            Ok(Command::Swap {
                slot: msg
                    .get("slot", &msg_path)?
                    .uint(&format!("{msg_path}.slot"))? as u8,
            })
        }
        "pickup" => {
            msg.exact(&["containerId"], &msg_path)?;
            Ok(Command::Pickup {
                container_id: msg
                    .get("containerId", &msg_path)?
                    .text(&format!("{msg_path}.containerId"))?
                    .to_string(),
            })
        }
        "cast" => {
            msg.closed(&["slot", "x", "y"], &["id"], &msg_path)?;
            let id = match msg.optional("id", &msg_path)? {
                None | Some(Mv::Null) => None,
                Some(id) => Some(id.text(&format!("{msg_path}.id"))?.to_string()),
            };
            Ok(Command::Cast {
                slot: msg
                    .get("slot", &msg_path)?
                    .text(&format!("{msg_path}.slot"))?
                    .to_string(),
                x: msg.get("x", &msg_path)?.num(&format!("{msg_path}.x"))?,
                y: msg.get("y", &msg_path)?.num(&format!("{msg_path}.y"))?,
                id,
            })
        }
        "plan" => {
            msg.closed(&[], &["spawn", "boon", "ready"], &msg_path)?;
            Ok(Command::Plan {
                spawn: match msg.optional("spawn", &msg_path)? {
                    None => None,
                    Some(spawn) => Some(spawn.uint(&format!("{msg_path}.spawn"))? as u8),
                },
                boon: match msg.optional("boon", &msg_path)? {
                    None => None,
                    Some(boon) => Some(boon.uint(&format!("{msg_path}.boon"))? as u8),
                },
                ready: match msg.optional("ready", &msg_path)? {
                    None => None,
                    Some(ready) => Some(ready.boolean(&format!("{msg_path}.ready"))?),
                },
            })
        }
        "choose" => {
            msg.closed(&["chest", "pick"], &["replace"], &msg_path)?;
            Ok(Command::Choose {
                chest: msg
                    .get("chest", &msg_path)?
                    .text(&format!("{msg_path}.chest"))?
                    .to_string(),
                pick: msg
                    .get("pick", &msg_path)?
                    .i8_number(&format!("{msg_path}.pick"))?,
                replace: match msg.optional("replace", &msg_path)? {
                    None => None,
                    Some(replace) => Some(replace.uint(&format!("{msg_path}.replace"))? as u8),
                },
            })
        }
        other => Err(err(
            &format!("{path}.type"),
            format!("unknown command type {other}"),
        )),
    }
}

pub(super) fn read_misses(value: &Mv, path: &str) -> Result<Vec<ClickMiss>, String> {
    let mut misses = Vec::new();
    for (index, item) in value.arr(path)?.iter().enumerate() {
        let item_path = format!("{path}[{index}]");
        item.exact(&["target", "at"], &item_path)?;
        misses.push(ClickMiss {
            target: item
                .get("target", &item_path)?
                .text(&format!("{item_path}.target"))?
                .to_string(),
            at: item
                .get("at", &item_path)?
                .num(&format!("{item_path}.at"))?,
        });
    }
    Ok(misses)
}

pub(super) fn read_stats(value: &Mv, path: &str) -> Result<HumanizerStats, String> {
    value.exact(
        &[
            "commands",
            "reactions",
            "targetClicks",
            "clickMissed",
            "clickWrong",
            "hesitations",
            "lapses",
            "throttled",
            "superseded",
            "apmPeak",
        ],
        path,
    )?;
    let commands_value = value.get("commands", path)?;
    let commands_path = format!("{path}.commands");
    commands_value.exact(
        &[
            "move", "stop", "target", "attack", "release", "swap", "pickup", "cast", "plan",
            "choose",
        ],
        &commands_path,
    )?;
    let mut commands = [0u64; 10];
    for (index, command_type) in COMMAND_TYPES.iter().enumerate() {
        commands[index] = u64::from(
            commands_value
                .get(command_type.as_str(), &commands_path)?
                .uint(&format!("{commands_path}.{}", command_type.as_str()))?,
        );
    }
    let reactions_path = format!("{path}.reactions");
    let mut reactions = std::collections::BTreeMap::new();
    for (index, (kind, times)) in value
        .get("reactions", path)?
        .obj(&reactions_path)?
        .iter()
        .enumerate()
    {
        let stimulus_kind = StimulusKind::parse(kind).ok_or_else(|| {
            err(
                &format!("{reactions_path}.{kind}"),
                format!("unknown reaction kind {kind}"),
            )
        })?;
        let mut samples = Vec::new();
        for (sample_index, sample) in times
            .arr(&format!("{reactions_path}[{index}]"))?
            .iter()
            .enumerate()
        {
            samples.push(sample.num(&format!("{reactions_path}.{kind}[{sample_index}]"))?);
        }
        reactions.insert(stimulus_kind, samples);
    }
    Ok(HumanizerStats {
        commands,
        reactions,
        target_clicks: u64::from(
            value
                .get("targetClicks", path)?
                .uint(&format!("{path}.targetClicks"))?,
        ),
        click_missed: u64::from(
            value
                .get("clickMissed", path)?
                .uint(&format!("{path}.clickMissed"))?,
        ),
        click_wrong: u64::from(
            value
                .get("clickWrong", path)?
                .uint(&format!("{path}.clickWrong"))?,
        ),
        hesitations: u64::from(
            value
                .get("hesitations", path)?
                .uint(&format!("{path}.hesitations"))?,
        ),
        lapses: u64::from(value.get("lapses", path)?.uint(&format!("{path}.lapses"))?),
        throttled: u64::from(
            value
                .get("throttled", path)?
                .uint(&format!("{path}.throttled"))?,
        ),
        superseded: u64::from(
            value
                .get("superseded", path)?
                .uint(&format!("{path}.superseded"))?,
        ),
        apm_peak: u64::from(
            value
                .get("apmPeak", path)?
                .uint(&format!("{path}.apmPeak"))?,
        ),
    })
}
