//! Server-to-client types: [`WireEvent`], [`WireFx`], [`WireResult`], [`ServerMsg`], and the decoder for a server frame.

use super::*;

/// Important events: delivered reliably and in order (never dropped for a slow client).
#[derive(Clone, Debug, PartialEq)]
pub enum WireEvent {
    PotionNotice {
        notice: u8,
    },
    Death {
        id: u32,
        by: Option<u32>,
    },
    Bounty {
        id: u32,
        bounty: u16,
    },
    MatchEnded {
        winner: Option<u32>,
        draw: bool,
    },
    /// The player's own resolved run.
    Result(WireResult),
    /// A wind emblem was walked over (the client's fixed "+50% movement for 5s" toast).
    WindEmblem,
    PickupRefused {
        reason: u8,
    },
    ChestRefused {
        reason: u8,
    },
    /// The owner's travel-cannon notice: 1 sealed, 2 cannot enter, 3 blast, or 4 reloading with the remaining seconds.
    CannonNotice {
        code: u8,
        seconds: Option<u16>,
    },
    /// The practice console's owner-only notice (protocol 11, tag 10): routed only to the connection that owns the event.
    Toast {
        text: String,
    },
    /// A camp was cleared (protocol 16, tag 11, ADR0026): its index in the map's camp list, the legacy camp-clear ring cue.
    /// Public to every viewer, as the sim declares it.
    CampCleared {
        camp: u16,
    },
}

/// Presentation effects: a slow client may lose them (the server drops the oldest first under backpressure).
#[derive(Clone, Copy, Debug, PartialEq)]
pub enum WireFx {
    Block {
        id: u32,
        by: u32,
        weapon: u8,
    },
    Stagger {
        id: u32,
    },
    Swing {
        by: u32,
        weapon: u8,
        x: f32,
        y: f32,
        a: f32,
    },
    /// `passive` and `critical` share one flags byte (bit 0 passive, bit 1 critical strike, ADR0026); other bits are refused.
    Hit {
        victim: u32,
        by: u32,
        amount: f32,
        passive: bool,
        critical: bool,
    },
    /// An attack windup started (protocol 16, tag 9, ADR0026): `by` will swing `weapon` toward `a` in `ms` milliseconds unless
    /// a [`WireFx::Cancel`] or a [`WireFx::Stagger`] for `by` ends it first. Shown to whoever its swing is shown to.
    Windup {
        by: u32,
        weapon: u8,
        a: f32,
        ms: u16,
    },
    /// `id`'s own order cancelled the attack it was winding up (tag 10): drop its telegraph.
    Cancel {
        id: u32,
    },
    /// Signature or ability damage (tag 11): a chunk of damage over time or an instant signature hit, with its kind
    /// ([`HIT_KIND_BURN`]...); `by` is u32::MAX when absent or redacted, as for [`WireFx::Hit`].
    SigHit {
        victim: u32,
        by: u32,
        kind: u8,
        amount: f32,
    },
    /// `id` healed `amount` (tag 12): lifesteal, regeneration, potions, terrain.
    Heal {
        id: u32,
        amount: f32,
    },
    /// The coliseum teleport happened at this tick (source sends the `teleport` fx to the whole room).
    Teleport,
    WishStarted {
        event_time: f64,
        caster: u32,
        ability: u8,
        duration: f64,
        target: Option<u32>,
        anchor: Option<WirePoint>,
    },
    WishLanded {
        event_time: f64,
        ability: u8,
        caster: Option<u32>,
        target: Option<u32>,
        anchor: Option<WirePoint>,
        destination: Option<WirePoint>,
    },
    WishEnded {
        event_time: f64,
        caster: u32,
        ability: u8,
        reason: u8,
    },
}

#[derive(Clone, Debug, PartialEq)]
pub struct WireResult {
    pub outcome: u8,
    pub place: u8,
    pub players: u8,
    pub kills: u16,
    pub killed_by: Option<String>,
    pub gv_total: u32,
    pub gv_win: u32,
    pub duration_sec: u32,
    pub win_bonus_withheld: bool,
}

#[derive(Clone, Debug, PartialEq)]
#[allow(clippy::large_enum_variant)] // The Welcome (one per connection) carries the map blob inline; boxing it would change every caller.
pub enum ServerMsg {
    Welcome {
        protocol: u16,
        content_hash: u64,
        match_id: [u8; 16],
        player_id: u32,
        session: [u8; 16],
        tick_rate: u8,
        tick: u32,
        server_ms: f64,
        /// The hitbox archetype table (protocol 16, ADR0024), between the header and the map.
        archetypes: Vec<archetype::WireArchetype>,
        /// The vision rule's parameters (protocol 16), after the archetype table and before the map.
        vision: vision::WireVision,
        /// The weapon table (protocol 16): each weapon's hit shape from the sim content, rows by ascending id, after the
        /// vision block and before the map.
        weapons: Vec<weapon::WireWeapon>,
        /// The HUD content block (protocol 17, P-WIRE17-HUD-2b): weapon scaling rows and public HUD constants, after the
        /// weapon table and before the map.
        hud: hud_content::WireHudContent,
        /// The canonical public map (server-sent; clients never generate it): docs/adr/0003-canonical-map.md.
        map: map::MapBlob,
    },
    Reject {
        code: RejectCode,
        reason: String,
    },
    /// The receiving player's own interest-managed view (only what it can see), once per tick.
    /// Its source clock is absolute; the wire omits it only when it equals [`snapshot_server_ms`]`(tick)`.
    Snapshot {
        tick: u32,
        /// Logical source clock in milliseconds; absent wire overrides decode to `snapshot_server_ms(tick)`.
        server_time: f64,
        phase: u8,
        ack_input: u32,
        me: Option<WireSelf>,
        actors: Vec<WireActor>,
        /// Protocol 11: the source's public training fact (head bit 16; absent means false, so a non-training frame is
        /// byte-identical to v7 apart from the version number).
        training: bool,
        /// Owner-only source lock/timing and mirror group. `None` is explicitly unavailable in a Full frame.
        owner_combat: Option<WireOwnerCombat>,
    },
    /// A snapshot as a delta against the last one the connection received (v3); see [`delta`].
    SnapshotDelta(SnapshotDelta),
    Events {
        tick: u32,
        events: Vec<WireEvent>,
    },
    Fx {
        tick: u32,
        fx: Vec<WireFx>,
    },
    Pong {
        nonce: u32,
        client_ms: f64,
        server_ms: f64,
    },
    /// Keyed state, the latest value per key (keys 1..=11; see the crate docs). Each item is a complete replacement.
    State {
        tick: u32,
        items: Vec<StateItem>,
    },
}

impl WireFx {
    /// What a client decodes after this effect went over the wire (a swing's position and angle quantized).
    pub fn quantized(&self) -> WireFx {
        match *self {
            WireFx::Swing {
                by,
                weapon,
                x,
                y,
                a,
            } => WireFx::Swing {
                by,
                weapon,
                x: quant::dequantize_pos(quant::quantize_pos(x)),
                y: quant::dequantize_pos(quant::quantize_pos(y)),
                a: quant::dequantize_facing(quant::quantize_facing(a)),
            },
            WireFx::Windup { by, weapon, a, ms } => WireFx::Windup {
                by,
                weapon,
                a: quant::dequantize_facing(quant::quantize_facing(a)),
                ms,
            },
            WireFx::WishStarted {
                event_time,
                caster,
                ability,
                duration,
                target,
                anchor,
            } => WireFx::WishStarted {
                event_time,
                caster,
                ability,
                duration,
                target,
                anchor: quantized_point(anchor),
            },
            WireFx::WishLanded {
                event_time,
                ability,
                caster,
                target,
                anchor,
                destination,
            } => WireFx::WishLanded {
                event_time,
                ability,
                caster,
                target,
                anchor: quantized_point(anchor),
                destination: quantized_point(destination),
            },
            hit => hit,
        }
    }
}

impl ServerMsg {
    /// P-TICK-3: decodes `frame` on a connection whose Welcome said `tick_rate`: a Snapshot or SnapshotDelta without a
    /// source-time override stands for `tick * (1000 / tick_rate)` ms. A rate other than 20 or 40 is refused.
    // STUB (P-TICK-3): the implementer replaces this body.
    pub fn decode_at(frame: &[u8], tick_rate: u8) -> Result<Self, DecodeError> {
        let _ = tick_rate;
        Self::decode(frame)
    }

    pub fn decode(frame: &[u8]) -> Result<Self, DecodeError> {
        if frame.len() > MAX_SERVER_FRAME {
            return Err(DecodeError::TooLarge(frame.len()));
        }
        let mut r = Reader::new(frame);
        let msg = match r.u8()? {
            T_WELCOME => {
                let protocol = r.u16()?;
                let content_hash = r.u64()?;
                let match_id = r.array16()?;
                let player_id = r.u32()?;
                let session = r.array16()?;
                let tick_rate = r.u8()?;
                let tick = r.u32()?;
                let server_ms = r.f64()?;
                let archetypes = archetype::read_table(&mut r)?;
                let vision = vision::WireVision::read(&mut r)?;
                let weapons = weapon::read_table(&mut r)?;
                let hud = hud_content::read_content(&mut r)?;
                let map = map::MapBlob::read(&mut r)?;
                if !vision.fits(map.w, map.h) {
                    return Err(DecodeError::BadField("vision"));
                }
                ServerMsg::Welcome {
                    protocol,
                    content_hash,
                    match_id,
                    player_id,
                    session,
                    tick_rate,
                    tick,
                    server_ms,
                    archetypes,
                    vision,
                    weapons,
                    hud,
                    map,
                }
            }
            T_REJECT => {
                // Any code above 0 decodes (unnamed ones as `Unknown`); 0 is not a code.
                let code = r.u16()?;
                if code == 0 {
                    return Err(DecodeError::BadTag(0));
                }
                ServerMsg::Reject {
                    code: RejectCode::from_u16(code),
                    reason: r.str()?,
                }
            }
            T_SNAPSHOT => {
                let tick = r.varint()?;
                let ack_input = r.varint()?;
                let head = r.varint()?;
                if head & !HM_KNOWN != 0 || (head & HM_PRESENT == 0 && head & HM_SELF_BITS != 0) {
                    return Err(DecodeError::BadField("head"));
                }
                let me = if head & HM_PRESENT != 0 {
                    Some(read_self(&mut r, head)?)
                } else {
                    None
                };
                let phase = if head & HM_PHASE != 0 {
                    code(&mut r, MAX_PHASE_CODE, "phase")?
                } else {
                    DEFAULT_PHASE
                };
                let server_time = if head & HM_SERVER_TIME != 0 {
                    let time = r.f64()?;
                    if !time.is_finite()
                        || time < 0.0
                        || time.to_bits() == snapshot_server_ms(tick).to_bits()
                    {
                        return Err(DecodeError::BadField("server time"));
                    }
                    time
                } else {
                    snapshot_server_ms(tick)
                };
                let training = head & HM_TRAINING != 0;
                let n = r.varint()? as usize;
                if n > MAX_ACTORS {
                    return Err(DecodeError::TooMany(n));
                }
                let mut actors = Vec::with_capacity(n);
                let mut prev = None;
                for _ in 0..n {
                    let a = read_actor(&mut r, prev)?;
                    prev = Some(a.id);
                    actors.push(a);
                }
                let owner_combat = if head & HM_OWNER_COMBAT != 0 {
                    let owner = read_owner_combat(&mut r)?;
                    if owner_combat_fault(Some(&owner), me.is_some(), |id| {
                        actors.binary_search_by_key(&id, |a| a.id).is_ok()
                    })
                    .is_some()
                    {
                        return Err(DecodeError::BadField("owner combat"));
                    }
                    Some(owner)
                } else {
                    None
                };
                if actors
                    .iter()
                    .any(|actor| encode::giver_actor_fault(actor).is_some())
                {
                    return Err(DecodeError::BadField("giver actor"));
                }
                ServerMsg::Snapshot {
                    tick,
                    server_time,
                    phase,
                    ack_input,
                    me,
                    actors,
                    training,
                    owner_combat,
                }
            }
            T_SNAPSHOT_DELTA => ServerMsg::SnapshotDelta(delta::read_snapshot_delta(&mut r)?),
            T_STATE => {
                let tick = r.varint()?;
                ServerMsg::State {
                    tick,
                    items: state::read_items(&mut r)?,
                }
            }
            T_EVENTS => {
                let tick = r.u32()?;
                let n = r.u16()? as usize;
                let mut events = Vec::with_capacity(n.min(1024));
                for _ in 0..n {
                    events.push(match r.u8()? {
                        8 => WireEvent::WindEmblem,
                        10 => WireEvent::Toast { text: r.str()? },
                        11 => WireEvent::CampCleared { camp: r.u16()? },
                        7 => {
                            let notice = r.u8()?;
                            if !(1..=10).contains(&notice) {
                                return Err(DecodeError::BadField("potion notice"));
                            }
                            WireEvent::PotionNotice { notice }
                        }
                        9 => {
                            let code = r.u8()?;
                            let seconds = match code {
                                1..=3 => None,
                                4 => {
                                    let s = r.u16()?;
                                    if !(1..=75).contains(&s) {
                                        return Err(DecodeError::BadField("cannon notice"));
                                    }
                                    Some(s)
                                }
                                _ => return Err(DecodeError::BadField("cannon notice")),
                            };
                            WireEvent::CannonNotice { code, seconds }
                        }
                        6 => {
                            let reason = r.u8()?;
                            if !(1..=9).contains(&reason) {
                                return Err(DecodeError::BadField("chest reason"));
                            }
                            WireEvent::ChestRefused { reason }
                        }
                        5 => {
                            let reason = r.u8()?;
                            if !(1..=5).contains(&reason) {
                                return Err(DecodeError::BadField("combat"));
                            }
                            WireEvent::PickupRefused { reason }
                        }
                        1 => WireEvent::Death {
                            id: r.u32()?,
                            by: r.opt(|r| r.u32())?,
                        },
                        2 => WireEvent::Bounty {
                            id: r.u32()?,
                            bounty: r.u16()?,
                        },
                        3 => WireEvent::MatchEnded {
                            winner: r.opt(|r| r.u32())?,
                            draw: r.u8()? != 0,
                        },
                        4 => WireEvent::Result(WireResult {
                            outcome: r.u8()?,
                            place: r.u8()?,
                            players: r.u8()?,
                            kills: r.u16()?,
                            killed_by: r.opt(|r| r.str())?,
                            gv_total: r.u32()?,
                            gv_win: r.u32()?,
                            duration_sec: r.u32()?,
                            win_bonus_withheld: r.u8()? != 0,
                        }),
                        t => return Err(DecodeError::BadTag(t)),
                    });
                }
                ServerMsg::Events { tick, events }
            }
            T_FX => {
                let tick = r.varint()?;
                let n = r.varint()? as usize;
                if n > u16::MAX as usize {
                    return Err(DecodeError::TooMany(n));
                }
                let mut fx = Vec::with_capacity(n.min(1024));
                for _ in 0..n {
                    fx.push(match r.u8()? {
                        3 => WireFx::Block {
                            id: r.varint()?,
                            by: r.varint()?,
                            weapon: r.u8()?,
                        },
                        4 => WireFx::Stagger { id: r.varint()? },
                        5 => WireFx::Teleport,
                        6 => WireFx::WishStarted {
                            event_time: r.f64()?,
                            caster: nonzero_varint(&mut r, "wish caster")?,
                            ability: code(&mut r, 4, "wish ability")?,
                            duration: r.f64()?,
                            target: r.opt(|r| nonzero_varint(r, "wish target"))?,
                            anchor: read_point_opt(&mut r)?,
                        },
                        7 => WireFx::WishLanded {
                            event_time: r.f64()?,
                            ability: code(&mut r, 4, "wish ability")?,
                            caster: r.opt(|r| nonzero_varint(r, "wish caster"))?,
                            target: r.opt(|r| nonzero_varint(r, "wish target"))?,
                            anchor: read_point_opt(&mut r)?,
                            destination: read_point_opt(&mut r)?,
                        },
                        8 => WireFx::WishEnded {
                            event_time: r.f64()?,
                            caster: nonzero_varint(&mut r, "wish caster")?,
                            ability: code(&mut r, 4, "wish ability")?,
                            reason: code(&mut r, 2, "wish reason")?,
                        },
                        1 => WireFx::Swing {
                            by: r.varint()?,
                            weapon: r.u8()?,
                            x: quant::dequantize_pos(r.u16()?),
                            y: quant::dequantize_pos(r.u16()?),
                            a: quant::dequantize_facing(r.u16()?),
                        },
                        2 => {
                            let victim = r.varint()?;
                            let by = r.varint()?;
                            let amount = r.f32()?;
                            let flags = code(&mut r, 3, "hit flags")?;
                            WireFx::Hit {
                                victim,
                                by,
                                amount,
                                passive: flags & 1 != 0,
                                critical: flags & 2 != 0,
                            }
                        }
                        9 => WireFx::Windup {
                            by: r.varint()?,
                            weapon: code(&mut r, MAX_KIT_CODE, "windup weapon")?,
                            a: quant::dequantize_facing(r.u16()?),
                            ms: r.u16()?,
                        },
                        10 => WireFx::Cancel { id: r.varint()? },
                        11 => {
                            let victim = r.varint()?;
                            let by = r.varint()?;
                            let kind = r.u8()?;
                            if !hit_kind_valid(kind) {
                                return Err(DecodeError::BadField("hit kind"));
                            }
                            WireFx::SigHit {
                                victim,
                                by,
                                kind,
                                amount: r.f32()?,
                            }
                        }
                        12 => WireFx::Heal {
                            id: r.varint()?,
                            amount: r.f32()?,
                        },
                        t => return Err(DecodeError::BadTag(t)),
                    });
                }
                for f in &fx {
                    let valid = match *f {
                        WireFx::WishStarted {
                            event_time,
                            duration,
                            target,
                            anchor,
                            ..
                        } => {
                            event_time.is_finite()
                                && event_time >= 0.0
                                && duration.is_finite()
                                && duration >= 0.0
                                && target.is_none_or(|id| id != 0)
                                && point_valid(anchor)
                        }
                        WireFx::WishLanded {
                            event_time,
                            ability,
                            caster,
                            target,
                            anchor,
                            destination,
                        } => {
                            event_time.is_finite()
                                && event_time >= 0.0
                                && caster.is_none_or(|id| id != 0)
                                && target.is_none_or(|id| id != 0)
                                && point_valid(anchor)
                                && point_valid(destination)
                                && ((ability == 3 && anchor.is_some() == destination.is_some())
                                    || (ability != 3 && destination.is_none()))
                                && (caster.is_some() || anchor.is_some())
                        }
                        WireFx::WishEnded { event_time, .. } => {
                            event_time.is_finite() && event_time >= 0.0
                        }
                        _ => true,
                    };
                    if !valid {
                        return Err(DecodeError::BadField("wish fx"));
                    }
                }
                ServerMsg::Fx { tick, fx }
            }
            T_PONG => ServerMsg::Pong {
                nonce: r.u32()?,
                client_ms: r.f64()?,
                server_ms: r.f64()?,
            },
            t => return Err(DecodeError::BadTag(t)),
        };
        r.end()?;
        Ok(msg)
    }
}
