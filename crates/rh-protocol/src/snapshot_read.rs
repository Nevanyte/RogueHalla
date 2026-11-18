//! The Snapshot readers: the self record and each actor, with the Wish and owner-combat groups they carry.

use super::*;

pub(crate) fn varint_u16(r: &mut Reader, what: &'static str) -> Result<u16, DecodeError> {
    u16::try_from(r.varint()?).map_err(|_| DecodeError::BadField(what))
}

/// The self record after its head (`mask` has HM_PRESENT set). The phase, if the head has one, is read by the caller.
pub(super) fn read_self(r: &mut Reader, mask: u32) -> Result<WireSelf, DecodeError> {
    let id = r.varint()?;
    let x = r.f32()?;
    let y = r.f32()?;
    let facing = r.f32()?;
    let max_hp = if mask & HM_MAXHP_F32 != 0 {
        r.f32()?
    } else {
        varint_u16(r, "max_hp")? as f32
    };
    let (faction, weapon) = if mask & HM_KIT != 0 {
        let k = r.u8()?;
        (k & 0x0f, k >> 4)
    } else {
        (0, 0)
    };
    let hp = if mask & HM_HP != 0 { r.f32()? } else { max_hp };
    let ready_in_ms = if mask & HM_READY != 0 { r.u16()? } else { 0 };
    let kills = if mask & HM_KILLS != 0 {
        varint_u16(r, "kills")?
    } else {
        0
    };
    let bounty = if mask & HM_BOUNTY != 0 {
        varint_u16(r, "bounty")?
    } else {
        0
    };
    let move_seq = if mask & HM_MOVE_SEQ != 0 { r.u8()? } else { 0 };
    let gv = if mask & HM_GV != 0 {
        varint_u16(r, "gv")?
    } else {
        0
    };
    let status = if mask & HM_STATUS != 0 {
        code(r, MAX_STATUS_CODE, "status")?
    } else {
        DEFAULT_STATUS
    };
    let effects = if mask & HM_EFFECTS != 0 { r.u16()? } else { 0 };
    let displace_seq = if mask & HM_DISPLACE != 0 { r.u8()? } else { 0 };
    let sig = if mask & HM_SIG != 0 { r.u8()? } else { 0 };
    let hunt_target = if mask & HM_HUNT_TARGET != 0 {
        let target = r.varint()?;
        if target == 0 {
            return Err(DecodeError::BadField("hunt target"));
        }
        Some(target)
    } else {
        None
    };
    let hunt_stacks = if mask & HM_HUNT_STACKS != 0 {
        varint_u16(r, "hunt_stacks")?
    } else {
        0
    };
    let wish = if mask & HM_WISH != 0 {
        let wish = read_wish_owner(r)?;
        if !encode::wish_owner_valid(&wish) {
            return Err(DecodeError::BadField("wish"));
        }
        Some(wish)
    } else {
        None
    };
    let (barrier, barrier_ring_step) = if mask & HM_BARRIER != 0 {
        let barrier = r.f32()?;
        let ring = r.u8()?;
        if !barrier.is_finite() || barrier < 0.0 || ring > 8 {
            return Err(DecodeError::BadField("barrier"));
        }
        (barrier, ring)
    } else {
        (0.0, 0)
    };
    let archetype = if mask & HM_ARCHETYPE != 0 { r.u8()? } else { 0 };
    let flags = if mask & HM_FLAGS != 0 { r.u8()? } else { 0 };
    let build_marks = if mask & HM_BUILD_MARKS != 0 {
        r.u8()?
    } else {
        0
    };
    let mark = if mask & HM_MARK != 0 {
        [r.u8()?, r.u8()?, r.u8()?]
    } else {
        [0; 3]
    };
    if let Some(why) = indicator_fault(0, build_marks, mark, flags) {
        return Err(DecodeError::BadField(why));
    }
    Ok(WireSelf {
        archetype,
        flags,
        build_marks,
        mark_tier: mark[0],
        brand: mark[1],
        mark_tax: mark[2],
        effects,
        displace_seq,
        sig,
        id,
        faction,
        x,
        y,
        facing,
        hp,
        max_hp,
        status,
        weapon,
        ready_in_ms,
        kills,
        bounty,
        gv,
        move_seq,
        hunt_target,
        hunt_stacks,
        wish,
        barrier,
        barrier_ring_step,
    })
}

pub(crate) fn read_wish_owner(r: &mut Reader) -> Result<WireWishOwner, DecodeError> {
    let ability = code(r, 4, "wish ability")?;
    let ready_at = r.f64()?;
    let full_at = r.opt(|r| r.f64())?;
    let max_charges = r.u8()?;
    let recharge_ms = r.f64()?;
    let charges = r.u8()?;
    let next_charge_at = r.opt(|r| r.f64())?;
    let cast = r.opt(|r| {
        Ok(WireWishCast {
            ability: code(r, 4, "wish cast ability")?,
            started_at: r.f64()?,
            lands_at: r.f64()?,
        })
    })?;
    let wish = WireWishOwner {
        ability,
        ready_at,
        full_at,
        max_charges,
        recharge_ms,
        charges,
        next_charge_at,
        cast,
    };
    if !encode::wish_owner_valid(&wish) {
        return Err(DecodeError::BadField("wish"));
    }
    Ok(wish)
}

pub(crate) fn read_owner_combat(r: &mut Reader) -> Result<WireOwnerCombat, DecodeError> {
    let lock = match r.u8()? {
        0 => WireOwnerLock::Clear,
        1 => WireOwnerLock::Held { target: None },
        2 => {
            let target = r.varint()?;
            if target == 0 {
                return Err(DecodeError::BadField("owner combat"));
            }
            WireOwnerLock::Held {
                target: Some(target),
            }
        }
        _ => return Err(DecodeError::BadField("owner combat")),
    };
    let owner = WireOwnerCombat {
        lock,
        draw_started_at: r.u32()?,
        ready_at: r.u32()?,
        as_milli: r.f64()?,
        windup_milli: r.f64()?,
        speed_milli: r.f64()?,
    };
    if !owner.valid() {
        return Err(DecodeError::BadField("owner combat"));
    }
    Ok(owner)
}

/// A byte that must be at most `max`.
pub(crate) fn code(r: &mut Reader, max: u8, what: &'static str) -> Result<u8, DecodeError> {
    match r.u8()? {
        v if v <= max => Ok(v),
        _ => Err(DecodeError::BadField(what)),
    }
}

/// One actor; `prev` is the previous actor's id (ids strictly ascend: the first is absolute, the rest are gaps).
pub(super) fn read_actor(r: &mut Reader, prev: Option<u32>) -> Result<WireActor, DecodeError> {
    let field = r.varint()?;
    let id = match prev {
        None => field,
        Some(p) => match p.checked_add(field) {
            Some(id) if field != 0 => id,
            _ => return Err(DecodeError::BadField("actor order")),
        },
    };
    let mask = r.varint()?;
    if mask & !AM_KNOWN != 0 {
        return Err(DecodeError::BadField("actor mask"));
    }
    let x = quant::dequantize_pos(r.u16()?);
    let y = quant::dequantize_pos(r.u16()?);
    let facing = quant::dequantize_facing(r.u16()?);
    let (faction, weapon) = if mask & AM_KIT != 0 {
        let k = r.u8()?;
        (k & 0x0f, k >> 4)
    } else {
        (0, 0)
    };
    let hp_pct = if mask & AM_HP != 0 {
        r.u8()?
    } else {
        DEFAULT_HP_PCT
    };
    if hp_pct > 100 {
        return Err(DecodeError::BadField("hp_pct"));
    }
    let status = if mask & AM_STATUS != 0 {
        code(r, MAX_STATUS_CODE, "status")?
    } else {
        DEFAULT_STATUS
    };
    let bounty = if mask & AM_BOUNTY != 0 {
        varint_u16(r, "bounty")?
    } else {
        0
    };
    let kind = if mask & AM_KIND != 0 { r.u8()? } else { 0 };
    let effects = if mask & AM_EFFECTS != 0 { r.u16()? } else { 0 };
    let displace_seq = if mask & AM_DISPLACE != 0 { r.u8()? } else { 0 };
    let mob_type_code = if mask & AM_MOB_TYPE != 0 {
        code(r, 30, "mob type")?
    } else {
        0
    };
    let sig = if mask & AM_SIG != 0 { r.u8()? } else { 0 };
    let (barrier_pct, barrier_ring_step) = if mask & AM_BARRIER != 0 {
        let pct = r.u8()?;
        let ring = r.u8()?;
        if pct > 100 || ring > 8 {
            return Err(DecodeError::BadField("barrier"));
        }
        (pct, ring)
    } else {
        (DEFAULT_BARRIER_PCT, DEFAULT_BARRIER_RING_STEP)
    };
    let archetype = if mask & AM_ARCHETYPE != 0 { r.u8()? } else { 0 };
    let flags = if mask & AM_FLAGS != 0 { r.u8()? } else { 0 };
    let offhand = if mask & AM_OFFHAND != 0 { r.u8()? } else { 0 };
    let build_marks = if mask & AM_BUILD_MARKS != 0 {
        r.u8()?
    } else {
        0
    };
    let mark = if mask & AM_MARK != 0 {
        [r.u8()?, r.u8()?, r.u8()?]
    } else {
        [0; 3]
    };
    if let Some(why) = indicator_fault(offhand, build_marks, mark, flags) {
        return Err(DecodeError::BadField(why));
    }
    Ok(WireActor {
        archetype,
        flags,
        offhand,
        build_marks,
        mark_tier: mark[0],
        brand: mark[1],
        mark_tax: mark[2],
        mob_type_code,
        effects,
        displace_seq,
        sig,
        id,
        faction,
        x,
        y,
        facing,
        hp_pct,
        status,
        weapon,
        bounty,
        kind,
        barrier_pct,
        barrier_ring_step,
    })
}
