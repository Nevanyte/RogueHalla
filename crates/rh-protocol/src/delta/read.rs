//! The delta message reader.

use super::*;

pub(super) fn read_self_delta(r: &mut Reader, mask: u32) -> Result<SelfDelta, DecodeError> {
    let mut s = SelfDelta {
        mask,
        ..SelfDelta::default()
    };
    if mask & DH_X != 0 {
        s.x = r.f32()?;
    }
    if mask & DH_Y != 0 {
        s.y = r.f32()?;
    }
    if mask & DH_FACING != 0 {
        s.facing = r.f32()?;
    }
    if mask & DH_MOVE_SEQ != 0 {
        s.move_seq = r.u8()?;
    }
    if mask & DH_HP != 0 {
        s.hp = r.f32()?;
    }
    if mask & DH_READY != 0 {
        s.ready_in_ms = r.u16()?;
    }
    if mask & DH_MAX_HP != 0 {
        s.max_hp = r.f32()?;
    }
    if mask & DH_KIT != 0 {
        s.kit = r.u8()?;
    }
    if mask & DH_KILLS != 0 {
        s.kills = crate::varint_u16(r, "kills")?;
    }
    if mask & DH_BOUNTY != 0 {
        s.bounty = crate::varint_u16(r, "bounty")?;
    }
    if mask & DH_GV != 0 {
        s.gv = crate::varint_u16(r, "gv")?;
    }
    if mask & DH_STATUS != 0 {
        s.status = crate::code(r, MAX_STATUS_CODE, "status")?;
    }
    if mask & DH_EFFECTS != 0 {
        s.effects = r.u16()?;
    }
    if mask & DH_DISPLACE != 0 {
        s.displace_seq = r.u8()?;
    }
    if mask & DH_SIG != 0 {
        s.sig = r.u8()?;
    }
    if mask & DH_HUNT_TARGET != 0 {
        let target = r.varint()?;
        s.hunt_target = (target != 0).then_some(target);
    }
    if mask & DH_HUNT_STACKS != 0 {
        s.hunt_stacks = crate::varint_u16(r, "hunt_stacks")?;
    }
    if mask & DH_WISH != 0 {
        s.wish = Some(r.opt(crate::read_wish_owner)?);
    }
    if mask & DH_BARRIER != 0 {
        s.barrier = r.f32()?;
        s.barrier_ring_step = r.u8()?;
        if !s.barrier.is_finite() || s.barrier < 0.0 || s.barrier_ring_step > 8 {
            return Err(DecodeError::BadField("barrier"));
        }
    }
    if mask & DH_ARCHETYPE != 0 {
        s.archetype = r.u8()?;
    }
    if mask & DH_FLAGS != 0 {
        s.flags = r.u8()?;
    }
    if mask & DH_BUILD_MARKS != 0 {
        s.build_marks = r.u8()?;
    }
    if mask & DH_MARK != 0 {
        s.mark = [r.u8()?, r.u8()?, r.u8()?];
    }
    if let Some(why) = self_indicator_fault(&s) {
        return Err(DecodeError::BadField(why));
    }
    Ok(s)
}

/// The carried indicator facts of a self delta as a decoder checks them (ADR0026); fields outside the mask are zero.
pub(super) fn self_indicator_fault(s: &SelfDelta) -> Option<&'static str> {
    crate::indicator_fault(0, s.build_marks, s.mark, s.flags)
}

/// The carried indicator facts of an actor delta as a decoder checks them (ADR0026); fields outside the mask are zero.
pub(super) fn actor_delta_indicator_fault(a: &ActorDelta) -> Option<&'static str> {
    let q = mask_fields(&a.q, a.mask);
    crate::indicator_fault(q.offhand, q.build_marks, q.mark, q.flags)
}

/// An id that is absolute for the first of a list and a non-zero gap after (so ids strictly ascend).
pub(super) fn read_id(
    r: &mut Reader,
    prev: Option<u32>,
    what: &'static str,
) -> Result<u32, DecodeError> {
    let field = r.varint()?;
    match prev {
        None => Ok(field),
        Some(p) => match p.checked_add(field) {
            Some(id) if field != 0 => Ok(id),
            _ => Err(DecodeError::BadField(what)),
        },
    }
}

/// Reads the body of a delta frame (after the tag) on a connection of `tick_ms` per tick (its Welcome's rate): syntax and
/// ranges only. The base age is bounded by [`max_delta_age`] and an absent source time is `tick * tick_ms`.
pub(crate) fn read_snapshot_delta(
    r: &mut Reader,
    tick_ms: u32,
) -> Result<SnapshotDelta, DecodeError> {
    let tick = r.varint()?;
    let age = r.varint()?;
    if !(1..=max_delta_age(tick_ms)).contains(&age) || age > tick {
        return Err(DecodeError::BadField("base age"));
    }
    let ack_input = r.varint()?;
    let head = r.varint()?;
    if head & !DH_KNOWN != 0 || (head & DH_PRESENT == 0 && head & DH_FIELDS != 0) {
        return Err(DecodeError::BadField("head"));
    }
    let training = head & DH_TRAINING != 0;
    let me = if head & DH_PRESENT != 0 {
        Some(read_self_delta(r, head & DH_FIELDS)?)
    } else {
        None
    };
    let phase = if head & DH_PHASE != 0 {
        Some(crate::code(r, MAX_PHASE_CODE, "phase")?)
    } else {
        None
    };
    let server_time = if head & DH_SERVER_TIME != 0 {
        let time = r.f64()?;
        if !time.is_finite()
            || time < 0.0
            || time.to_bits() == snapshot_server_ms_at(tick, tick_ms).to_bits()
        {
            return Err(DecodeError::BadField("server time"));
        }
        time
    } else {
        snapshot_server_ms_at(tick, tick_ms)
    };
    let n = r.varint()? as usize;
    if n > MAX_ACTORS {
        return Err(DecodeError::TooMany(n));
    }
    let mut changed = Vec::with_capacity(n);
    let mut prev = None;
    for _ in 0..n {
        let id = read_id(r, prev, "actor order")?;
        prev = Some(id);
        let mask = r.varint()?;
        if mask & !AD_ALL != 0 {
            return Err(DecodeError::BadField("actor mask"));
        }
        let mut q = QActor {
            id,
            ..QActor::default()
        };
        if mask & AD_X != 0 {
            q.x = r.u16()?;
        }
        if mask & AD_Y != 0 {
            q.y = r.u16()?;
        }
        if mask & AD_FACING != 0 {
            q.facing = r.u16()?;
        }
        if mask & AD_KIT != 0 {
            q.kit = r.u8()?;
        }
        if mask & AD_HP != 0 {
            q.hp_pct = r.u8()?;
            if q.hp_pct > 100 {
                return Err(DecodeError::BadField("hp_pct"));
            }
        }
        if mask & AD_BOUNTY != 0 {
            q.bounty = crate::varint_u16(r, "bounty")?;
        }
        if mask & AD_STATUS != 0 {
            q.status = crate::code(r, MAX_STATUS_CODE, "status")?;
        }
        if mask & AD_KIND != 0 {
            q.kind = r.u8()?;
        }
        if mask & AD_EFFECTS != 0 {
            q.effects = r.u16()?;
        }
        if mask & AD_DISPLACE != 0 {
            q.displace_seq = r.u8()?;
        }
        if mask & AD_MOB_TYPE != 0 {
            q.mob_type_code = crate::code(r, 30, "mob type")?;
        }
        if mask & AD_SIG != 0 {
            q.sig = r.u8()?;
        }
        if mask & AD_BARRIER != 0 {
            q.barrier_pct = r.u8()?;
            q.barrier_ring_step = r.u8()?;
            if q.barrier_pct > 100 || q.barrier_ring_step > 8 {
                return Err(DecodeError::BadField("barrier"));
            }
        }
        if mask & AD_ARCHETYPE != 0 {
            q.archetype = r.u8()?;
        }
        if mask & AD_FLAGS != 0 {
            q.flags = r.u8()?;
        }
        if mask & AD_OFFHAND != 0 {
            q.offhand = r.u8()?;
        }
        if mask & AD_BUILD_MARKS != 0 {
            q.build_marks = r.u8()?;
        }
        if mask & AD_MARK != 0 {
            q.mark = [r.u8()?, r.u8()?, r.u8()?];
        }
        if mask & AD_WALK != 0 {
            q.walk = [r.u8()?, r.u8()?];
        }
        if let Some(why) = crate::indicator_fault(q.offhand, q.build_marks, q.mark, q.flags) {
            return Err(DecodeError::BadField(why));
        }
        changed.push(ActorDelta { mask, q });
    }
    let mut departed = Vec::new();
    if head & DH_DEPARTED != 0 {
        let n = r.varint()? as usize;
        if n == 0 {
            return Err(DecodeError::BadField("departed"));
        }
        if n > MAX_ACTORS {
            return Err(DecodeError::TooMany(n));
        }
        departed.reserve(n);
        let mut prev = None;
        for _ in 0..n {
            let id = read_id(r, prev, "departed order")?;
            prev = Some(id);
            departed.push(id);
        }
    }
    let owner_combat = if head & DH_OWNER_COMBAT != 0 {
        Some(match r.u8()? {
            0 => None,
            1 => Some(read_owner_combat(r)?),
            _ => return Err(DecodeError::BadField("owner combat")),
        })
    } else {
        None
    };
    if matches!(owner_combat, Some(Some(_))) && me.is_none() {
        return Err(DecodeError::BadField("owner combat"));
    }
    Ok(SnapshotDelta {
        tick,
        server_time,
        training,
        base_tick: tick - age,
        phase,
        ack_input,
        me,
        changed,
        departed,
        owner_combat,
    })
}
