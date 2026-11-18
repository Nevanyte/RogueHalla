//! The delta message writer, its length and its pre-encode check.

use super::*;

/// The delta message as bytes (the reference writer behind `ServerMsg::SnapshotDelta`).
pub(crate) fn write_message(w: &mut Writer, d: &SnapshotDelta) {
    w.u8(T_SNAPSHOT_DELTA);
    w.varint(d.tick);
    w.varint(d.tick.wrapping_sub(d.base_tick));
    w.varint(d.ack_input);
    let mut head = 0u32;
    if d.training {
        head |= DH_TRAINING;
    }
    if let Some(s) = &d.me {
        head |= DH_PRESENT | (s.mask & DH_FIELDS);
    }
    if d.phase.is_some() {
        head |= DH_PHASE;
    }
    if !d.departed.is_empty() {
        head |= DH_DEPARTED;
    }
    if d.owner_combat.is_some() {
        head |= DH_OWNER_COMBAT;
    }
    if d.server_time.to_bits() != snapshot_server_ms(d.tick).to_bits() {
        head |= DH_SERVER_TIME;
    }
    w.varint(head);
    if let Some(s) = &d.me {
        let m = s.mask;
        if m & DH_X != 0 {
            w.f32(s.x);
        }
        if m & DH_Y != 0 {
            w.f32(s.y);
        }
        if m & DH_FACING != 0 {
            w.f32(s.facing);
        }
        if m & DH_MOVE_SEQ != 0 {
            w.u8(s.move_seq);
        }
        if m & DH_HP != 0 {
            w.f32(s.hp);
        }
        if m & DH_READY != 0 {
            w.u16(s.ready_in_ms);
        }
        if m & DH_MAX_HP != 0 {
            w.f32(s.max_hp);
        }
        if m & DH_KIT != 0 {
            w.u8(s.kit);
        }
        if m & DH_KILLS != 0 {
            w.varint(s.kills as u32);
        }
        if m & DH_BOUNTY != 0 {
            w.varint(s.bounty as u32);
        }
        if m & DH_GV != 0 {
            w.varint(s.gv as u32);
        }
        if m & DH_STATUS != 0 {
            w.u8(s.status);
        }
        if m & DH_EFFECTS != 0 {
            w.u16(s.effects);
        }
        if m & DH_DISPLACE != 0 {
            w.u8(s.displace_seq);
        }
        if m & DH_SIG != 0 {
            w.u8(s.sig);
        }
        if m & DH_HUNT_TARGET != 0 {
            w.varint(s.hunt_target.unwrap_or(0));
        }
        if m & DH_HUNT_STACKS != 0 {
            w.varint(s.hunt_stacks as u32);
        }
        if m & DH_WISH != 0 {
            match s.wish {
                Some(Some(wish)) => {
                    w.u8(1);
                    super::super::write_wish_owner_writer(w, &wish);
                }
                Some(None) => w.u8(0),
                None => unreachable!("Wish bit has an explicit option"),
            }
        }
        if m & DH_BARRIER != 0 {
            w.f32(s.barrier);
            w.u8(s.barrier_ring_step);
        }
        if m & DH_ARCHETYPE != 0 {
            w.u8(s.archetype);
        }
        if m & DH_FLAGS != 0 {
            w.u8(s.flags);
        }
        if m & DH_BUILD_MARKS != 0 {
            w.u8(s.build_marks);
        }
        if m & DH_MARK != 0 {
            w.bytes(&s.mark);
        }
    }
    if let Some(p) = d.phase {
        w.u8(p);
    }
    if head & DH_SERVER_TIME != 0 {
        w.f64(d.server_time);
    }
    w.varint(d.changed.len() as u32);
    let mut prev: Option<u32> = None;
    for a in &d.changed {
        w.varint(id_field(a.q.id, prev));
        prev = Some(a.q.id);
        w.varint(a.mask & AD_ALL);
        if a.mask & AD_X != 0 {
            w.u16(a.q.x);
        }
        if a.mask & AD_Y != 0 {
            w.u16(a.q.y);
        }
        if a.mask & AD_FACING != 0 {
            w.u16(a.q.facing);
        }
        if a.mask & AD_KIT != 0 {
            w.u8(a.q.kit);
        }
        if a.mask & AD_HP != 0 {
            w.u8(a.q.hp_pct);
        }
        if a.mask & AD_BOUNTY != 0 {
            w.varint(a.q.bounty as u32);
        }
        if a.mask & AD_STATUS != 0 {
            w.u8(a.q.status);
        }
        if a.mask & AD_KIND != 0 {
            w.u8(a.q.kind);
        }
        if a.mask & AD_EFFECTS != 0 {
            w.u16(a.q.effects);
        }
        if a.mask & AD_DISPLACE != 0 {
            w.u8(a.q.displace_seq);
        }
        if a.mask & AD_MOB_TYPE != 0 {
            w.u8(a.q.mob_type_code);
        }
        if a.mask & AD_SIG != 0 {
            w.u8(a.q.sig);
        }
        if a.mask & AD_BARRIER != 0 {
            w.u8(a.q.barrier_pct);
            w.u8(a.q.barrier_ring_step);
        }
        if a.mask & AD_ARCHETYPE != 0 {
            w.u8(a.q.archetype);
        }
        if a.mask & AD_FLAGS != 0 {
            w.u8(a.q.flags);
        }
        if a.mask & AD_OFFHAND != 0 {
            w.u8(a.q.offhand);
        }
        if a.mask & AD_BUILD_MARKS != 0 {
            w.u8(a.q.build_marks);
        }
        if a.mask & AD_MARK != 0 {
            w.bytes(&a.q.mark);
        }
    }
    if !d.departed.is_empty() {
        write_departed(w, &d.departed);
    }
    if let Some(owner) = d.owner_combat {
        match owner {
            None => w.u8(0),
            Some(owner) => {
                w.u8(1);
                write_owner_combat(w, &owner);
            }
        }
    }
}

pub(crate) fn message_len(d: &SnapshotDelta) -> usize {
    let mut w = Writer::default();
    write_message(&mut w, d);
    w.len()
}

/// Why a decoder would reject this delta message (the encoder refuses it too: [`EncodeError`]).
pub(crate) fn message_check(d: &SnapshotDelta) -> Result<(), EncodeError> {
    list("actors", d.changed.len(), MAX_ACTORS)?;
    list("departed", d.departed.len(), MAX_ACTORS)?;
    let bad = |why| Err(EncodeError::BadValue(why));
    if !d.server_time.is_finite() || d.server_time < 0.0 {
        return bad("server time");
    }
    if d.phase.is_some_and(|p| p > MAX_PHASE_CODE) {
        return bad("phase");
    }
    match d.tick.checked_sub(d.base_tick) {
        Some(age) if (1..=MAX_DELTA_AGE).contains(&age) => {}
        _ => return bad("base age"),
    }
    if let Some(s) = &d.me {
        if s.mask & !DH_FIELDS != 0 {
            return bad("head");
        }
        if s.mask & DH_HUNT_TARGET != 0 && s.hunt_target == Some(0) {
            return bad("hunt target");
        }
        if s.mask & DH_STATUS != 0 && s.status > MAX_STATUS_CODE {
            return bad("status");
        }
        if (s.mask & DH_WISH != 0) != s.wish.is_some() {
            return bad("wish");
        }
        if s.wish
            .flatten()
            .is_some_and(|wish| !super::super::wish_owner_valid(&wish))
        {
            return bad("wish");
        }
        if s.mask & DH_BARRIER != 0
            && (!s.barrier.is_finite() || s.barrier < 0.0 || s.barrier_ring_step > 8)
        {
            return bad("barrier");
        }
        if let Some(why) = self_indicator_fault(s) {
            return bad(why);
        }
    }
    if let Some(Some(owner)) = d.owner_combat {
        if d.me.is_none() || !owner.valid() {
            return bad("owner combat");
        }
    }
    let mut prev: Option<u32> = None;
    for a in &d.changed {
        if a.mask & !AD_ALL != 0 {
            return bad("actor mask");
        }
        if a.mask & AD_STATUS != 0 && a.q.status > MAX_STATUS_CODE {
            return bad("status");
        }
        if a.mask & AD_MOB_TYPE != 0 && a.q.mob_type_code > 30 {
            return bad("mob type");
        }
        if a.mask & AD_HP != 0 && a.q.hp_pct > 100 {
            return bad("hp_pct");
        }
        if a.mask & AD_BARRIER != 0 && (a.q.barrier_pct > 100 || a.q.barrier_ring_step > 8) {
            return bad("barrier");
        }
        if let Some(why) = actor_delta_indicator_fault(a) {
            return bad(why);
        }
        if prev.is_some_and(|p| a.q.id <= p) {
            return bad("actor order");
        }
        prev = Some(a.q.id);
    }
    if d.departed.windows(2).any(|w| w[1] <= w[0]) {
        return bad("departed order");
    }
    Ok(())
}
