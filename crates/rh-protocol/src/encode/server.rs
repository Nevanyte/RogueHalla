//! Server-to-client frames: the [`Frame`] impl for every [`ServerMsg`] variant, plus the Events and Fx record lengths.

use super::*;

impl Frame for ServerMsg {
    const MAX_FRAME: usize = MAX_SERVER_FRAME;

    fn encoded_len(&self) -> usize {
        self.len_ms(TICK_MS)
    }

    fn check(&self, len: usize) -> Result<(), EncodeError> {
        self.check_ms(len, TICK_MS)
    }

    /// A Snapshot is written once into a reused per-thread scratch buffer and copied to its exact size: every actor's mask
    /// and field lengths are computed once (by the writer), not again by a sizing pass. The bytes are those of the sized
    /// path (debug builds assert it). Every other message is sized first.
    fn exact_vec(&self) -> Vec<u8> {
        if !matches!(self, ServerMsg::Snapshot { .. }) {
            return sized_then_written(self);
        }
        #[cfg(debug_assertions)]
        if let Err(e) = self.check(self.encoded_len()) {
            panic!("encoding a frame every decoder rejects: {e} (use try_encode or encode_into)");
        }
        thread_local! {
            static SCRATCH: std::cell::RefCell<Vec<u8>> = const { std::cell::RefCell::new(Vec::new()) };
        }
        SCRATCH.with(|cell| {
            let mut buf = std::mem::take(&mut *cell.borrow_mut());
            buf.clear();
            let mut w = Writer::from_vec(buf);
            self.write(&mut w);
            let buf = w.finish();
            debug_assert_eq!(
                buf.len(),
                self.encoded_len(),
                "encoded_len disagrees with the writer"
            );
            let out = buf.clone();
            *cell.borrow_mut() = buf;
            out
        })
    }

    fn write(&self, w: &mut Writer) {
        self.write_ms(w, TICK_MS)
    }
}

/// The writers behind the [`Frame`] impl, with snapshot time and delta limits at `tick_ms` per tick (P-TICK-3-fix): the
/// plain encoders pass [`TICK_MS`], [`ServerMsg::try_encode_at`] the connection's rate.
impl ServerMsg {
    fn len_ms(&self, tick_ms: u32) -> usize {
        match self {
            ServerMsg::Welcome {
                archetypes,
                vision,
                weapons,
                hud,
                map,
                ..
            } => {
                WELCOME_HEADER_LEN
                    + crate::archetype::table_len(archetypes)
                    + vision.encoded_len()
                    + crate::weapon::table_len(weapons)
                    + crate::hud_content::content_len(hud)
                    + map.encoded_len()
            }
            ServerMsg::Reject { reason, .. } => 1 + 2 + str_len(reason),
            ServerMsg::Snapshot {
                tick,
                server_time,
                phase,
                ack_input,
                me,
                training,
                actors,
                owner_combat,
            } => snapshot_len(
                *tick,
                tick_ms,
                *server_time,
                *phase,
                *ack_input,
                me.as_ref(),
                *training,
                actors,
                owner_combat.as_ref(),
            ),
            ServerMsg::SnapshotDelta(d) => delta::message_len(d, tick_ms),
            ServerMsg::Events { events, .. } => {
                1 + 4 + 2 + events.iter().map(event_len).sum::<usize>()
            }
            ServerMsg::Fx { tick, fx } => {
                1 + varint_len(*tick)
                    + varint_len(fx.len() as u32)
                    + fx.iter().map(fx_len).sum::<usize>()
            }
            ServerMsg::Pong { .. } => 1 + 4 + 8 + 8,
            ServerMsg::State { tick, items } => {
                1 + varint_len(*tick)
                    + varint_len(items.len() as u32)
                    + items.iter().map(StateItem::encoded_len).sum::<usize>()
            }
        }
    }

    fn check_ms(&self, len: usize, tick_ms: u32) -> Result<(), EncodeError> {
        match self {
            ServerMsg::Welcome {
                archetypes,
                vision,
                weapons,
                hud,
                map,
                ..
            } => {
                if let Some(why) = crate::archetype::table_fault(archetypes) {
                    return Err(EncodeError::BadValue(why));
                }
                if let Some(why) = vision.fault() {
                    return Err(EncodeError::BadValue(why));
                }
                if !vision.fits(map.w, map.h) {
                    return Err(EncodeError::BadValue("vision"));
                }
                if let Some(why) = crate::weapon::table_fault(weapons) {
                    return Err(EncodeError::BadValue(why));
                }
                if let Some(why) = crate::hud_content::content_fault(hud) {
                    return Err(EncodeError::BadValue(why));
                }
                map.check()?
            }
            ServerMsg::Snapshot {
                server_time,
                phase,
                me,
                actors,
                owner_combat,
                ..
            } => {
                if !server_time.is_finite() || *server_time < 0.0 {
                    return Err(EncodeError::BadValue("server time"));
                }
                list("actors", actors.len(), MAX_ACTORS)?;
                if let Some(why) = snapshot_fault(*phase, me.as_ref(), actors) {
                    return Err(EncodeError::BadValue(why));
                }
                if owner_combat_fault(owner_combat.as_ref(), me.is_some(), |id| {
                    actors.binary_search_by_key(&id, |a| a.id).is_ok()
                })
                .is_some()
                {
                    return Err(EncodeError::BadValue("owner combat"));
                }
            }
            ServerMsg::SnapshotDelta(d) => delta::message_check(d, tick_ms)?,
            ServerMsg::Events { events, .. } => {
                list("events", events.len(), u16::MAX as usize)?;
                for e in events {
                    if matches!(e, WireEvent::PotionNotice { notice } if !(1..=10).contains(notice))
                    {
                        return Err(EncodeError::BadValue("potion notice"));
                    }
                    if matches!(e, WireEvent::CannonNotice { code, seconds }
                        if !(1..=4).contains(code)
                            || (*code == 4)
                                != matches!(seconds, Some(s) if (1..=75).contains(s)))
                    {
                        return Err(EncodeError::BadValue("cannon notice"));
                    }
                    if matches!(e, WireEvent::ChestRefused { reason } if !(1..=9).contains(reason))
                    {
                        return Err(EncodeError::BadValue("chest reason"));
                    }
                    if matches!(e, WireEvent::PickupRefused { reason } if !(1..=5).contains(reason))
                    {
                        return Err(EncodeError::BadValue("pickup reason"));
                    }
                    if let WireEvent::Result(r) = e {
                        if let Some(k) = &r.killed_by {
                            str_fits("killed_by", k)?;
                        }
                    }
                    if let WireEvent::Toast { text } = e {
                        str_fits("toast", text)?;
                    }
                }
            }
            ServerMsg::Fx { fx, .. } => {
                list("fx", fx.len(), u16::MAX as usize)?;
                let swing_off_grid = |f: &WireFx| {
                    matches!(*f, WireFx::Swing { x, y, .. }
                        if !quant::pos_in_range(x) || !quant::pos_in_range(y))
                };
                if fx.iter().any(swing_off_grid) {
                    return Err(EncodeError::BadValue("position"));
                }
                for f in fx {
                    let good = match *f {
                        WireFx::WishStarted {
                            event_time,
                            caster,
                            ability,
                            duration,
                            target,
                            anchor,
                        } => {
                            event_time.is_finite()
                                && event_time >= 0.0
                                && caster != 0
                                && ability <= 4
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
                                && ability <= 4
                                && caster.is_none_or(|id| id != 0)
                                && target.is_none_or(|id| id != 0)
                                && point_valid(anchor)
                                && point_valid(destination)
                                && ((ability == 3 && anchor.is_some() == destination.is_some())
                                    || (ability != 3 && destination.is_none()))
                                && (caster.is_some() || anchor.is_some())
                        }
                        WireFx::WishEnded {
                            event_time,
                            caster,
                            ability,
                            reason,
                        } => {
                            event_time.is_finite()
                                && event_time >= 0.0
                                && caster != 0
                                && ability <= 4
                                && reason <= 2
                        }
                        WireFx::Windup { weapon, .. } if weapon > MAX_KIT_CODE => {
                            return Err(EncodeError::BadValue("windup weapon"));
                        }
                        WireFx::SigHit { kind, .. } if !hit_kind_valid(kind) => {
                            return Err(EncodeError::BadValue("hit kind"));
                        }
                        _ => true,
                    };
                    if !good {
                        return Err(EncodeError::BadValue("wish fx"));
                    }
                }
            }
            ServerMsg::Reject { code, reason } => {
                // Code 0 is not a code (every decoder refuses it), and a named code must be built as its variant.
                let c = code.code();
                if c == 0
                    || (matches!(code, RejectCode::Unknown(_)) && RejectCode::from_u16(c) != *code)
                {
                    return Err(EncodeError::BadValue("reject code"));
                }
                str_fits("reason", reason)?
            }
            ServerMsg::Pong { .. } => {}
            ServerMsg::State { items, .. } => {
                crate::state::check_items(items)?;
            }
        }
        frame_size(len, Self::MAX_FRAME)
    }

    fn write_ms(&self, w: &mut Writer, tick_ms: u32) {
        match self {
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
            } => {
                WelcomeHeader {
                    protocol: *protocol,
                    content_hash: *content_hash,
                    match_id: *match_id,
                    player_id: *player_id,
                    session: *session,
                    tick_rate: *tick_rate,
                    tick: *tick,
                    server_ms: *server_ms,
                }
                .write(w);
                crate::archetype::write_table(w, archetypes);
                vision.write(w);
                crate::weapon::write_table(w, weapons);
                crate::hud_content::write_content(w, hud);
                map.write(w);
            }
            ServerMsg::Reject { code, reason } => {
                w.u8(T_REJECT);
                w.u16(code.code());
                w.str(reason);
            }
            ServerMsg::Snapshot {
                tick,
                server_time,
                phase,
                ack_input,
                me,
                training,
                actors,
                owner_combat,
            } => {
                write_snapshot_head(
                    w,
                    SnapshotHeader {
                        tick: *tick,
                        server_time: *server_time,
                        tick_ms,
                        phase: *phase,
                        ack_input: *ack_input,
                        me: me.as_ref(),
                        training: *training,
                        actors: actors.len(),
                        owner_combat: owner_combat.is_some(),
                    },
                );
                let mut prev = None;
                for a in actors {
                    write_actor(w, a, prev);
                    prev = Some(a.id);
                }
                if let Some(owner) = owner_combat {
                    write_owner_combat(w, owner);
                }
            }
            ServerMsg::SnapshotDelta(d) => delta::write_message(w, d, tick_ms),
            ServerMsg::Events { tick, events } => {
                w.u8(T_EVENTS);
                w.u32(*tick);
                w.u16(events.len() as u16);
                for e in events {
                    match e {
                        WireEvent::PotionNotice { notice } => {
                            w.u8(7);
                            w.u8(*notice);
                        }
                        WireEvent::CannonNotice { code, seconds } => {
                            w.u8(9);
                            w.u8(*code);
                            if let Some(seconds) = seconds {
                                w.u16(*seconds);
                            }
                        }
                        WireEvent::ChestRefused { reason } => {
                            w.u8(6);
                            w.u8(*reason);
                        }
                        WireEvent::WindEmblem => {
                            w.u8(8);
                        }
                        WireEvent::Toast { text } => {
                            w.u8(10);
                            w.str(text);
                        }
                        WireEvent::CampCleared { camp } => {
                            w.u8(11);
                            w.u16(*camp);
                        }
                        WireEvent::PickupRefused { reason } => {
                            w.u8(5);
                            w.u8(*reason);
                        }
                        WireEvent::Death { id, by } => {
                            w.u8(1);
                            w.u32(*id);
                            w.opt(by.as_ref(), |w, b| w.u32(*b));
                        }
                        WireEvent::Bounty { id, bounty } => {
                            w.u8(2);
                            w.u32(*id);
                            w.u16(*bounty);
                        }
                        WireEvent::MatchEnded { winner, draw } => {
                            w.u8(3);
                            w.opt(winner.as_ref(), |w, b| w.u32(*b));
                            w.u8(*draw as u8);
                        }
                        WireEvent::Result(r) => {
                            w.u8(4);
                            w.u8(r.outcome);
                            w.u8(r.place);
                            w.u8(r.players);
                            w.u16(r.kills);
                            w.opt(r.killed_by.as_ref(), |w, s| w.str(s));
                            w.u32(r.gv_total);
                            w.u32(r.gv_win);
                            w.u32(r.duration_sec);
                            w.u8(r.win_bonus_withheld as u8);
                        }
                    }
                }
            }
            ServerMsg::Fx { tick, fx } => {
                w.u8(T_FX);
                w.varint(*tick);
                w.varint(fx.len() as u32);
                for f in fx {
                    match *f {
                        WireFx::Block { id, by, weapon } => {
                            w.u8(3);
                            w.varint(id);
                            w.varint(by);
                            w.u8(weapon);
                        }
                        WireFx::Stagger { id } => {
                            w.u8(4);
                            w.varint(id);
                        }
                        WireFx::Swing {
                            by,
                            weapon,
                            x,
                            y,
                            a,
                        } => {
                            w.u8(1);
                            w.varint(by);
                            w.u8(weapon);
                            w.u16(quant::quantize_pos(x));
                            w.u16(quant::quantize_pos(y));
                            w.u16(quant::quantize_facing(a));
                        }
                        WireFx::Teleport => w.u8(5),
                        WireFx::WishStarted {
                            event_time,
                            caster,
                            ability,
                            duration,
                            target,
                            anchor,
                        } => {
                            w.u8(6);
                            w.f64(event_time);
                            w.varint(caster);
                            w.u8(ability);
                            w.f64(duration);
                            w.opt(target.as_ref(), |w, id| w.varint(*id));
                            write_point_opt(w, anchor);
                        }
                        WireFx::WishLanded {
                            event_time,
                            ability,
                            caster,
                            target,
                            anchor,
                            destination,
                        } => {
                            w.u8(7);
                            w.f64(event_time);
                            w.u8(ability);
                            w.opt(caster.as_ref(), |w, id| w.varint(*id));
                            w.opt(target.as_ref(), |w, id| w.varint(*id));
                            write_point_opt(w, anchor);
                            write_point_opt(w, destination);
                        }
                        WireFx::WishEnded {
                            event_time,
                            caster,
                            ability,
                            reason,
                        } => {
                            w.u8(8);
                            w.f64(event_time);
                            w.varint(caster);
                            w.u8(ability);
                            w.u8(reason);
                        }
                        WireFx::Hit {
                            victim,
                            by,
                            amount,
                            passive,
                            critical,
                        } => {
                            w.u8(2);
                            w.varint(victim);
                            w.varint(by);
                            w.f32(amount);
                            w.u8(passive as u8 | (critical as u8) << 1);
                        }
                        WireFx::Windup { by, weapon, a, ms } => {
                            w.u8(9);
                            w.varint(by);
                            w.u8(weapon);
                            w.u16(quant::quantize_facing(a));
                            w.u16(ms);
                        }
                        WireFx::Cancel { id } => {
                            w.u8(10);
                            w.varint(id);
                        }
                        WireFx::SigHit {
                            victim,
                            by,
                            kind,
                            amount,
                        } => {
                            w.u8(11);
                            w.varint(victim);
                            w.varint(by);
                            w.u8(kind);
                            w.f32(amount);
                        }
                        WireFx::Heal { id, amount } => {
                            w.u8(12);
                            w.varint(id);
                            w.f32(amount);
                        }
                    }
                }
            }
            ServerMsg::State { tick, items } => {
                w.u8(T_STATE);
                w.varint(*tick);
                w.varint(items.len() as u32);
                for item in items {
                    item.write(w);
                }
            }
            ServerMsg::Pong {
                nonce,
                client_ms,
                server_ms,
            } => {
                w.u8(T_PONG);
                w.u32(*nonce);
                w.f64(*client_ms);
                w.f64(*server_ms);
            }
        }
    }
}

pub(super) fn write_point_opt(w: &mut Writer, point: Option<WirePoint>) {
    w.opt(point.as_ref(), |w, p| {
        w.u16(quant::quantize_pos(p.x));
        w.u16(quant::quantize_pos(p.y));
    });
}

impl ServerMsg {
    /// The frame, in one allocation of exactly [`ServerMsg::encoded_len`] bytes. A message decoders would reject (more
    /// than [`MAX_ACTORS`] actors, an oversized frame, a bad map) panics in debug builds and is NOT repaired in release
    /// builds, except that a string over 255 bytes panics in release too (the writer never truncates); use [`ServerMsg::try_encode`] or [`ServerMsg::encode_into`] where that can happen.
    pub fn encode(&self) -> Vec<u8> {
        encode_exact(self)
    }

    /// Like [`ServerMsg::encode`] (one allocation of the exact size), but a message decoders would reject is an error. The
    /// server encodes every Events and Fx frame through this.
    pub fn try_encode(&self) -> Result<Vec<u8>, EncodeError> {
        checked_exact(self)
    }

    /// Appends the frame to `out` (a buffer the caller reuses) and returns its length; on error `out` is unchanged.
    pub fn encode_into(&self, out: &mut Vec<u8>) -> Result<usize, EncodeError> {
        encode_into(self, out)
    }

    /// Exact size of the frame [`ServerMsg::encode`] writes (a Welcome counts its map's runs: one pass, no allocation).
    pub fn encoded_len(&self) -> usize {
        Frame::encoded_len(self)
    }

    /// P-TICK-3-fix: [`ServerMsg::try_encode`] on a connection whose Welcome said `tick_rate`, the encoding side of
    /// [`ServerMsg::decode_at`]: a Snapshot or SnapshotDelta writes a source-time override only when its time is not
    /// `tick * (1000 / tick_rate)`, and a delta's base age is bounded by [`delta::max_delta_age`] at that rate. At 20 Hz it is
    /// [`ServerMsg::try_encode`]; a rate other than 20 or 40 is refused (`BadValue("tick rate")`), whatever the message.
    pub fn try_encode_at(&self, tick_rate: u8) -> Result<Vec<u8>, EncodeError> {
        let tick_ms = tick_ms_for_rate(tick_rate).ok_or(EncodeError::BadValue("tick rate"))?;
        let len = self.len_ms(tick_ms);
        self.check_ms(len, tick_ms)?;
        let mut w = Writer::with_capacity(len);
        self.write_ms(&mut w, tick_ms);
        debug_assert_eq!(w.len(), len, "encoded_len disagrees with the writer");
        Ok(w.finish())
    }

    /// Exact size of the frame [`ServerMsg::try_encode_at`] writes at `tick_rate` (refused like it for another rate).
    pub fn encoded_len_at(&self, tick_rate: u8) -> Result<usize, EncodeError> {
        let tick_ms = tick_ms_for_rate(tick_rate).ok_or(EncodeError::BadValue("tick rate"))?;
        Ok(self.len_ms(tick_ms))
    }
}

pub(super) fn event_len(e: &WireEvent) -> usize {
    match e {
        WireEvent::WindEmblem => 1,
        WireEvent::PickupRefused { .. } => 2,
        WireEvent::ChestRefused { .. } | WireEvent::PotionNotice { .. } => 2,
        WireEvent::CannonNotice { seconds, .. } => 2 + seconds.map_or(0, |_| 2),
        WireEvent::Death { by, .. } => 1 + 4 + opt_len(by.is_some(), 4),
        WireEvent::Bounty { .. } => 1 + 4 + 2,
        WireEvent::MatchEnded { winner, .. } => 1 + opt_len(winner.is_some(), 4) + 1,
        WireEvent::Result(r) => {
            1 + 3 + 2 + 1 + r.killed_by.as_deref().map_or(0, str_len) + 4 + 4 + 4 + 1
        }
        WireEvent::Toast { text } => 1 + str_len(text),
        WireEvent::CampCleared { .. } => 1 + 2,
    }
}

pub(super) fn fx_len(f: &WireFx) -> usize {
    match f {
        WireFx::Block { id, by, .. } => 2 + varint_len(*id) + varint_len(*by),
        WireFx::Stagger { id } => 1 + varint_len(*id),
        WireFx::Swing { by, .. } => 1 + varint_len(*by) + 1 + 6,
        WireFx::Hit { victim, by, .. } => 1 + varint_len(*victim) + varint_len(*by) + 5,
        WireFx::Windup { by, .. } => 1 + varint_len(*by) + 1 + 2 + 2,
        WireFx::Cancel { id } => 1 + varint_len(*id),
        WireFx::SigHit { victim, by, .. } => 1 + varint_len(*victim) + varint_len(*by) + 1 + 4,
        WireFx::Heal { id, .. } => 1 + varint_len(*id) + 4,
        WireFx::Teleport => 1,
        WireFx::WishStarted {
            caster,
            target,
            anchor,
            ..
        } => {
            1 + 8
                + varint_len(*caster)
                + 1
                + 8
                + opt_len(target.is_some(), target.map_or(0, varint_len))
                + point_opt_len(anchor)
        }
        WireFx::WishLanded {
            caster,
            target,
            anchor,
            destination,
            ..
        } => {
            1 + 8
                + 1
                + opt_len(caster.is_some(), caster.map_or(0, varint_len))
                + opt_len(target.is_some(), target.map_or(0, varint_len))
                + point_opt_len(anchor)
                + point_opt_len(destination)
        }
        WireFx::WishEnded { caster, .. } => 1 + 8 + varint_len(*caster) + 1 + 1,
    }
}
