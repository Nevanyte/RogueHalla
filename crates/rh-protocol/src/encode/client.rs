//! Client-to-server frames: command lengths and finiteness, and the [`Frame`] impl for [`ClientMsg`].

use super::*;

pub(super) fn command_len(c: &WireCommand) -> usize {
    match *c {
        WireCommand::Swap { slot } => 2 + usize::from(slot.is_some()),
        WireCommand::Pickup { .. } => 5,
        WireCommand::Choose { .. } => 6,
        WireCommand::Plan { spawn, boon, ready } => {
            2 + usize::from(spawn.is_some())
                + usize::from(boon.is_some())
                + usize::from(ready.is_some())
        }
        WireCommand::UseConsumable => 1,
        WireCommand::Target { .. } | WireCommand::HuntTarget { .. } => 5,
        WireCommand::ConsumeCorpse { .. } => 5,
        WireCommand::Move { seq, .. } | WireCommand::AttackMove { seq, .. } => {
            1 + 8 + opt_len(seq.is_some(), 1)
        }
        WireCommand::Stop { seq } => 1 + opt_len(seq.is_some(), 1),
        WireCommand::Aim { .. } => 1 + 4,
        WireCommand::Attack { t, .. } | WireCommand::Release { t, .. } => {
            1 + 4 + opt_len(t.is_some(), 8)
        }
        WireCommand::Train {
            id, option_count, ..
        } => 1 + 1 + varint_len(id) + 8 + 1 + usize::from(option_count),
        WireCommand::Cast { actor, t, seq, .. } => {
            1 + 1
                + 4
                + 4
                + opt_len(actor.is_some(), 4)
                + opt_len(t.is_some(), 8)
                + opt_len(seq.is_some(), 1)
        }
        WireCommand::QuestAccept { handle } => 1 + varint_len(handle.get()),
        WireCommand::QuestInteract { handle, .. } => 1 + varint_len(handle.get()) + 2,
        WireCommand::Trade { .. } => 3,
    }
}

pub(super) fn command_finite(c: &WireCommand) -> bool {
    match *c {
        WireCommand::Move { x, y, .. } | WireCommand::AttackMove { x, y, .. } => {
            x.is_finite() && y.is_finite()
        }
        WireCommand::Stop { .. }
        | WireCommand::Swap { .. }
        | WireCommand::Pickup { .. }
        | WireCommand::Choose { .. }
        | WireCommand::Plan { .. }
        | WireCommand::Target { .. }
        | WireCommand::HuntTarget { .. }
        | WireCommand::ConsumeCorpse { .. }
        | WireCommand::Train { .. }
        | WireCommand::UseConsumable => true,
        WireCommand::QuestAccept { .. } | WireCommand::QuestInteract { .. } => true,
        WireCommand::Trade { pick, .. } => pick <= 2,
        WireCommand::Aim { a } => a.is_finite(),
        WireCommand::Attack { a, t } | WireCommand::Release { a, t } => {
            a.is_finite() && t.is_none_or(f64::is_finite)
        }
        WireCommand::Cast { x, y, t, .. } => {
            x.is_finite() && y.is_finite() && t.is_none_or(f64::is_finite)
        }
    }
}

impl Frame for ClientMsg {
    const MAX_FRAME: usize = MAX_CLIENT_FRAME;

    fn encoded_len(&self) -> usize {
        match self {
            ClientMsg::Hello {
                build,
                profile,
                ticket,
                faction,
                resume,
                ..
            } => {
                1 + 2
                    + 8
                    + str_len(build)
                    + str_len(profile)
                    + blob_len(ticket)
                    + opt_len(faction.is_some(), 1)
                    + opt_len(resume.is_some(), 16)
            }
            ClientMsg::Input { commands, .. } => {
                1 + 4 + 2 + commands.iter().map(command_len).sum::<usize>()
            }
            ClientMsg::Ping { .. } => 1 + 4 + 8,
            ClientMsg::Leave | ClientMsg::Resync => 1,
        }
    }

    fn check(&self, len: usize) -> Result<(), EncodeError> {
        match self {
            ClientMsg::Hello {
                build,
                profile,
                ticket,
                ..
            } => {
                str_fits("build", build)?;
                str_fits("profile", profile)?;
                if ticket.len() > MAX_TICKET {
                    return Err(EncodeError::StringTooLong {
                        what: "ticket",
                        len: ticket.len(),
                    });
                }
            }
            ClientMsg::Input { commands, .. } => {
                list("commands", commands.len(), MAX_COMMANDS_PER_INPUT)?;
                if commands.iter().any(|c| {
                    matches!(c, WireCommand::Target { actor: 0 })
                        || matches!(c, WireCommand::Swap { slot: Some(s) } if *s > 1)
                        || matches!(c, WireCommand::Choose { pick, .. } if !(-1..=2).contains(pick))
                        || matches!(c, WireCommand::Plan { spawn, boon, .. } if spawn.is_some_and(|v| v > 1) || boon.is_some_and(|v| v > 1))
                        || matches!(c, WireCommand::Train { command, option_count, .. }
                            if !(1..=5).contains(command) || *option_count > 3)
                        || matches!(c, WireCommand::Cast { slot, actor, .. }
                            if *slot > 2 || actor.is_some_and(|id| id == 0))
                        || matches!(c, WireCommand::Trade { pick, .. } if *pick > 2)
                        || matches!(c, WireCommand::QuestInteract { action, target, .. } if !(1..=9).contains(action) || (matches!(*action, 1 | 4 | 6) && *target != 0) || (*action == 2 && *target > 1) || (*action == 5 && *target > 80) || (*action == 8 && *target > 2))
                }) {
                    return Err(EncodeError::BadValue("command"));
                }
                if !commands.iter().all(command_finite) {
                    return Err(EncodeError::NonFinite);
                }
            }
            ClientMsg::Ping { client_ms, .. } if !client_ms.is_finite() => {
                return Err(EncodeError::NonFinite);
            }
            _ => {}
        }
        frame_size(len, Self::MAX_FRAME)
    }

    fn write(&self, w: &mut Writer) {
        match self {
            ClientMsg::Hello {
                protocol,
                content_hash,
                build,
                profile,
                ticket,
                faction,
                resume,
            } => {
                w.u8(T_HELLO);
                w.u16(*protocol);
                w.u64(*content_hash);
                w.str(build);
                w.str(profile);
                w.blob(ticket);
                w.opt(faction.as_ref(), |w, f| w.u8(*f));
                w.opt(resume.as_ref(), |w, r| w.bytes(r));
            }
            ClientMsg::Input { seq, commands } => {
                w.u8(T_INPUT);
                w.u32(*seq);
                w.u16(commands.len() as u16);
                for c in commands {
                    match *c {
                        WireCommand::Swap { slot } => {
                            w.u8(6);
                            w.opt(slot.as_ref(), |w, s| w.u8(*s));
                        }
                        WireCommand::Pickup { handle } => {
                            w.u8(7);
                            w.u32(handle.get());
                        }
                        WireCommand::Choose { handle, pick } => {
                            w.u8(8);
                            w.u32(handle.get());
                            w.u8(pick as u8);
                        }
                        WireCommand::Plan { spawn, boon, ready } => {
                            w.u8(C_PLAN);
                            w.u8(u8::from(spawn.is_some())
                                | (u8::from(boon.is_some()) << 1)
                                | (u8::from(ready.is_some()) << 2));
                            if let Some(v) = spawn {
                                w.u8(v);
                            }
                            if let Some(v) = boon {
                                w.u8(v);
                            }
                            if let Some(v) = ready {
                                w.u8(u8::from(v));
                            }
                        }
                        WireCommand::UseConsumable => w.u8(9),
                        WireCommand::Target { actor } => {
                            w.u8(10);
                            w.u32(actor);
                        }
                        WireCommand::HuntTarget { actor } => {
                            w.u8(12);
                            w.u32(actor);
                        }
                        WireCommand::ConsumeCorpse { handle } => {
                            w.u8(13);
                            w.u32(handle.get());
                        }
                        WireCommand::AttackMove { x, y, seq } => {
                            w.u8(11);
                            w.f32(x);
                            w.f32(y);
                            w.opt(seq.as_ref(), |w, s| w.u8(*s));
                        }
                        WireCommand::Move { x, y, seq } => {
                            w.u8(C_MOVE);
                            w.f32(x);
                            w.f32(y);
                            w.opt(seq.as_ref(), |w, s| w.u8(*s));
                        }
                        WireCommand::Stop { seq } => {
                            w.u8(C_STOP);
                            w.opt(seq.as_ref(), |w, s| w.u8(*s));
                        }
                        WireCommand::Aim { a } => {
                            w.u8(C_AIM);
                            w.f32(a);
                        }
                        WireCommand::Attack { a, t } => {
                            w.u8(C_ATTACK);
                            w.f32(a);
                            w.opt(t.as_ref(), |w, t| w.f64(*t));
                        }
                        WireCommand::Release { a, t } => {
                            w.u8(C_RELEASE);
                            w.f32(a);
                            w.opt(t.as_ref(), |w, t| w.f64(*t));
                        }
                        WireCommand::Train {
                            command,
                            id,
                            count,
                            options,
                            option_count,
                        } => {
                            w.u8(15);
                            w.u8(command);
                            w.varint(id);
                            w.u64(count);
                            w.u8(option_count);
                            for option in options.iter().take(usize::from(option_count)) {
                                w.u8(*option);
                            }
                        }
                        WireCommand::Cast {
                            slot,
                            x,
                            y,
                            actor,
                            t,
                            seq,
                        } => {
                            w.u8(C_CAST);
                            w.u8(slot);
                            w.f32(x);
                            w.f32(y);
                            w.opt(actor.as_ref(), |w, id| w.u32(*id));
                            w.opt(t.as_ref(), |w, ms| w.f64(*ms));
                            w.opt(seq.as_ref(), |w, seq| w.u8(*seq));
                        }
                        WireCommand::QuestAccept { handle } => {
                            w.u8(C_QUEST_ACCEPT);
                            w.varint(handle.get());
                        }
                        WireCommand::QuestInteract {
                            handle,
                            action,
                            target,
                        } => {
                            w.u8(C_QUEST_INTERACT);
                            w.varint(handle.get());
                            w.u8(action);
                            w.u8(target);
                        }
                        WireCommand::Trade { kind, pick } => {
                            w.u8(C_TRADE);
                            w.u8(kind.code());
                            w.u8(pick);
                        }
                    }
                }
            }
            ClientMsg::Ping { nonce, client_ms } => {
                w.u8(T_PING);
                w.u32(*nonce);
                w.f64(*client_ms);
            }
            ClientMsg::Leave => w.u8(T_LEAVE),
            ClientMsg::Resync => w.u8(T_RESYNC),
        }
    }
}

impl ClientMsg {
    /// The frame, in one allocation of exactly [`ClientMsg::encoded_len`] bytes. A message decoders would reject (more
    /// than [`MAX_COMMANDS_PER_INPUT`] commands, a non-finite float) panics in debug builds; use [`ClientMsg::try_encode`]
    /// where that can happen.
    pub fn encode(&self) -> Vec<u8> {
        encode_exact(self)
    }

    /// Like [`ClientMsg::encode`] (one allocation of the exact size), but a message decoders would reject is an error.
    pub fn try_encode(&self) -> Result<Vec<u8>, EncodeError> {
        checked_exact(self)
    }

    /// Appends the frame to `out` and returns its length; on error `out` is unchanged.
    pub fn encode_into(&self, out: &mut Vec<u8>) -> Result<usize, EncodeError> {
        encode_into(self, out)
    }

    /// Exact size of the frame [`ClientMsg::encode`] writes.
    pub fn encoded_len(&self) -> usize {
        Frame::encoded_len(self)
    }
}
