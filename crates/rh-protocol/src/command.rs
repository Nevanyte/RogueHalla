//! Client-to-server types: [`RejectCode`], [`WireCommand`], [`ClientMsg`], and the decoder for a client frame.

use super::*;

/// Why the server refuses or closes a session. Codes are wire format and append only: a decoder that meets a code it does
/// not know keeps it as [`RejectCode::Unknown`] (it never fails the frame), so a newer server can always tell an older
/// client why it was refused. `Unknown(n)` with a known `n` encodes as that code but decodes as the named variant.
#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub enum RejectCode {
    ProtocolMismatch,
    ContentMismatch,
    BadCredentials,
    ProfileInUse,
    MatchFull,
    RateLimited,
    Malformed,
    TooSlow,
    ServerDraining,
    NoSuchSession,
    Kicked,
    /// A code this build does not name (including 0), kept raw.
    Unknown(u16),
}

impl RejectCode {
    /// The wire value.
    pub fn code(self) -> u16 {
        use RejectCode::*;
        match self {
            ProtocolMismatch => 1,
            ContentMismatch => 2,
            BadCredentials => 3,
            ProfileInUse => 4,
            MatchFull => 5,
            RateLimited => 6,
            Malformed => 7,
            TooSlow => 8,
            ServerDraining => 9,
            NoSuchSession => 10,
            Kicked => 11,
            Unknown(v) => v,
        }
    }

    /// Total: a value this build does not name becomes [`RejectCode::Unknown`].
    pub fn from_u16(v: u16) -> Self {
        use RejectCode::*;
        match v {
            1 => ProtocolMismatch,
            2 => ContentMismatch,
            3 => BadCredentials,
            4 => ProfileInUse,
            5 => MatchFull,
            6 => RateLimited,
            7 => Malformed,
            8 => TooSlow,
            9 => ServerDraining,
            10 => NoSuchSession,
            11 => Kicked,
            _ => Unknown(v),
        }
    }
}

/// A command as the client sends it (validated again by the server's simulation: never trusted).
#[derive(Clone, Copy, Debug, PartialEq)]
pub enum WireCommand {
    UseConsumable,
    Target {
        actor: u32,
    },
    HuntTarget {
        actor: u32,
    },
    ConsumeCorpse {
        handle: PickupHandle,
    },
    AttackMove {
        x: f32,
        y: f32,
        seq: Option<u8>,
    },
    Swap {
        slot: Option<u8>,
    },
    Pickup {
        handle: PickupHandle,
    },
    Choose {
        handle: RewardChestHandle,
        pick: i8,
    },
    /// The planning screen's private command (source arena planning only; dropped in any other phase). `spawn`/`boon` are
    /// indexes into the caller's own private plan options (0/1); `ready` latches. At least one field should be present.
    Plan {
        spawn: Option<u8>,
        boon: Option<u8>,
        ready: Option<bool>,
    },
    Move {
        x: f32,
        y: f32,
        seq: Option<u8>,
    },
    Stop {
        seq: Option<u8>,
    },
    Aim {
        a: f32,
    },
    /// `t`: the client's render time in server milliseconds (lag compensation; clamped server side).
    Attack {
        a: f32,
        t: Option<f64>,
    },
    Release {
        a: f32,
        t: Option<f64>,
    },
    /// The training room's practice console (protocol 11, tag 15): the source's five commands. The command byte names
    /// one (1 card, 2 stat, 3 camp, 4 chest, 5 clear); the id is a catalog position, the count is clamped by the sim, and
    /// the options are the chest's chosen card positions (at most three).
    Train {
        command: u8,
        id: u32,
        count: u64,
        /// The chest's chosen card positions (the first `option_count` are real; at most three).
        options: [u8; 3],
        option_count: u8,
    },
    /// Ordinary human Wish request; the server treats the fields as untrusted aim/input only.
    Cast {
        slot: u8,
        x: f32,
        y: f32,
        actor: Option<u32>,
        t: Option<f64>,
        seq: Option<u8>,
    },
    /// Accept a quest row through its observer-issued, quest-only handle.
    QuestAccept {
        handle: QuestHandle,
    },
    /// Execute one server-offered quest interaction. The simulation validates handle/action/target again.
    QuestInteract {
        handle: QuestHandle,
        action: u8,
        target: u8,
    },
    /// Trade with one of the two catalog merchants; pick is the 0..=2 offer index.
    Trade {
        kind: WireTradeKind,
        pick: u8,
    },
}

#[derive(Clone, Debug, PartialEq)]
pub enum ClientMsg {
    /// First frame of every connection. `resume` is the session token of a dropped connection (reconnect).
    Hello {
        protocol: u16,
        content_hash: u64,
        build: String,
        /// The requested display name (the callsign).
        profile: String,
        /// Opaque admission credential, at most [`MAX_TICKET`] bytes: what P-HOST-1 defines (a game-session player id or a
        /// signed join token). The server's admission checks it where the credentials were checked before; with no hosting
        /// layer it is the callsign's secret as UTF-8 bytes.
        ticket: Vec<u8>,
        faction: Option<u8>,
        resume: Option<[u8; 16]>,
    },
    /// `seq`: the client's input frame counter, echoed in Snapshot.ack_input (prediction reconciliation).
    Input {
        seq: u32,
        commands: Vec<WireCommand>,
    },
    Ping {
        nonce: u32,
        client_ms: f64,
    },
    Leave,
    /// "Send me a full snapshot": the client refused a delta it could not apply (v3). The server answers with a Full
    /// snapshot at its next tick, or at the first tick at least 20 (one second) after its previous forced Full if that is
    /// later: a request is never dropped, only deferred ([`delta::MIN_FORCED_FULL_TICKS`]).
    Resync,
}

impl ClientMsg {
    /// The protocol version a Hello frame announces, read from its first three bytes (`[0x01][protocol u16]`) and nothing
    /// else: `None` if the frame is not a Hello or is shorter than that prefix. Those three bytes and the whole Reject frame
    /// never change across protocol versions, so a server reads this BEFORE [`ClientMsg::decode`] and answers a client of
    /// another version with [`RejectCode::ProtocolMismatch`] whatever follows (a future Hello may add fields).
    pub fn hello_protocol(frame: &[u8]) -> Option<u16> {
        match frame {
            [T_HELLO, lo, hi, ..] => Some(u16::from_le_bytes([*lo, *hi])),
            _ => None,
        }
    }

    /// Decodes an untrusted client frame. Size limit, finite floats, list bounds and no trailing bytes are enforced here.
    pub fn decode(frame: &[u8]) -> Result<Self, DecodeError> {
        if frame.len() > MAX_CLIENT_FRAME {
            return Err(DecodeError::TooLarge(frame.len()));
        }
        let mut r = Reader::new(frame);
        r.finite_only = true;
        let msg = match r.u8()? {
            T_HELLO => ClientMsg::Hello {
                protocol: r.u16()?,
                content_hash: r.u64()?,
                build: r.str()?,
                profile: r.str()?,
                ticket: r.blob(MAX_TICKET)?,
                faction: r.opt(|r| r.u8())?,
                resume: r.opt(|r| r.array16())?,
            },
            T_INPUT => {
                let seq = r.u32()?;
                let n = r.u16()? as usize;
                if n > MAX_COMMANDS_PER_INPUT {
                    return Err(DecodeError::TooMany(n));
                }
                let mut commands = Vec::with_capacity(n);
                for _ in 0..n {
                    commands.push(match r.u8()? {
                        6 => {
                            let slot = r.opt(|r| r.u8())?;
                            if slot.is_some_and(|s| s > 1) {
                                return Err(DecodeError::BadField("combat"));
                            }
                            WireCommand::Swap { slot }
                        }
                        7 => {
                            let handle = PickupHandle::new(r.u32()?)
                                .ok_or(DecodeError::BadField("combat"))?;
                            WireCommand::Pickup { handle }
                        }
                        8 => {
                            let handle = RewardChestHandle::new(r.u32()?)
                                .ok_or(DecodeError::BadField("reward choice"))?;
                            let pick = r.u8()? as i8;
                            if !(-1..=2).contains(&pick) {
                                return Err(DecodeError::BadField("reward choice"));
                            }
                            WireCommand::Choose { handle, pick }
                        }
                        9 => WireCommand::UseConsumable,
                        10 => {
                            let actor = r.u32()?;
                            if actor == 0 {
                                return Err(DecodeError::BadField("target"));
                            }
                            WireCommand::Target { actor }
                        }
                        15 => {
                            let command = r.u8()?;
                            if !(1..=5).contains(&command) {
                                return Err(DecodeError::BadField("train command"));
                            }
                            let id = r.varint()?;
                            let count = r.u64()?;
                            let n = r.u8()? as usize;
                            if n > 3 {
                                return Err(DecodeError::TooMany(n));
                            }
                            let mut options = [0u8; 3];
                            for option in options.iter_mut().take(n) {
                                *option = r.u8()?;
                            }
                            WireCommand::Train {
                                command,
                                id,
                                count,
                                options,
                                option_count: n as u8,
                            }
                        }
                        C_CAST => {
                            let slot = r.u8()?;
                            if slot > 2 {
                                return Err(DecodeError::BadField("cast slot"));
                            }
                            let x = r.f32()?;
                            let y = r.f32()?;
                            let actor = r.opt(|r| {
                                let id = r.u32()?;
                                if id == 0 {
                                    return Err(DecodeError::BadField("cast actor"));
                                }
                                Ok(id)
                            })?;
                            WireCommand::Cast {
                                slot,
                                x,
                                y,
                                actor,
                                t: r.opt(|r| r.f64())?,
                                seq: r.opt(|r| r.u8())?,
                            }
                        }
                        C_QUEST_ACCEPT => WireCommand::QuestAccept {
                            handle: QuestHandle::new(r.varint()?)
                                .ok_or(DecodeError::BadField("quest handle"))?,
                        },
                        C_QUEST_INTERACT => {
                            let handle = QuestHandle::new(r.varint()?)
                                .ok_or(DecodeError::BadField("quest handle"))?;
                            let action = r.u8()?;
                            let target = r.u8()?;
                            if !(1..=9).contains(&action)
                                || (matches!(action, 1 | 4 | 6) && target != 0)
                                || (action == 2 && target > 1)
                                || (action == 5 && target > 80)
                                || (action == 8 && target > 2)
                            {
                                return Err(DecodeError::BadField("quest interaction"));
                            }
                            WireCommand::QuestInteract {
                                handle,
                                action,
                                target,
                            }
                        }
                        C_TRADE => {
                            let kind = WireTradeKind::from_code(r.u8()?)
                                .ok_or(DecodeError::BadField("trade command"))?;
                            let pick = r.u8()?;
                            if pick > 2 {
                                return Err(DecodeError::BadField("trade command"));
                            }
                            WireCommand::Trade { kind, pick }
                        }
                        11 => WireCommand::AttackMove {
                            x: r.f32()?,
                            y: r.f32()?,
                            seq: r.opt(|r| r.u8())?,
                        },
                        12 => {
                            let actor = r.u32()?;
                            if actor == 0 {
                                return Err(DecodeError::BadField("hunt target"));
                            }
                            WireCommand::HuntTarget { actor }
                        }
                        13 => {
                            let handle = PickupHandle::new(r.u32()?)
                                .ok_or(DecodeError::BadField("corpse handle"))?;
                            WireCommand::ConsumeCorpse { handle }
                        }
                        C_PLAN => {
                            let flags = r.u8()?;
                            if flags & !0b111 != 0 {
                                return Err(DecodeError::BadField("plan flags"));
                            }
                            let spawn = if flags & 1 != 0 {
                                let v = r.u8()?;
                                if v > 1 {
                                    return Err(DecodeError::BadField("plan spawn"));
                                }
                                Some(v)
                            } else {
                                None
                            };
                            let boon = if flags & 2 != 0 {
                                let v = r.u8()?;
                                if v > 1 {
                                    return Err(DecodeError::BadField("plan boon"));
                                }
                                Some(v)
                            } else {
                                None
                            };
                            let ready = if flags & 4 != 0 {
                                Some(r.u8()? != 0)
                            } else {
                                None
                            };
                            WireCommand::Plan { spawn, boon, ready }
                        }
                        C_MOVE => WireCommand::Move {
                            x: r.f32()?,
                            y: r.f32()?,
                            seq: r.opt(|r| r.u8())?,
                        },
                        C_STOP => WireCommand::Stop {
                            seq: r.opt(|r| r.u8())?,
                        },
                        C_AIM => WireCommand::Aim { a: r.f32()? },
                        C_ATTACK => WireCommand::Attack {
                            a: r.f32()?,
                            t: r.opt(|r| r.f64())?,
                        },
                        C_RELEASE => WireCommand::Release {
                            a: r.f32()?,
                            t: r.opt(|r| r.f64())?,
                        },
                        t => return Err(DecodeError::BadTag(t)),
                    });
                }
                ClientMsg::Input { seq, commands }
            }
            T_PING => ClientMsg::Ping {
                nonce: r.u32()?,
                client_ms: r.f64()?,
            },
            T_LEAVE => ClientMsg::Leave,
            T_RESYNC => ClientMsg::Resync,
            t => return Err(DecodeError::BadTag(t)),
        };
        r.end()?;
        Ok(msg)
    }
}
