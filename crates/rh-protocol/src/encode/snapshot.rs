//! Snapshot content checks and writers: the fault test, actor and owner-combat records, and the Full head.

use super::*;

/// The first reason a decoder would reject this Snapshot's content (None when it is encodable).
pub(super) fn snapshot_fault(
    phase: u8,
    me: Option<&WireSelf>,
    actors: &[WireActor],
) -> Option<&'static str> {
    if phase > MAX_PHASE_CODE {
        return Some("phase");
    }
    if let Some(s) = me {
        if let Some(f) = kit_status_fault(s.faction, s.weapon, s.status) {
            return Some(f);
        }
        if s.hunt_target == Some(0) {
            return Some("hunt target");
        }
        if !s.barrier.is_finite() || s.barrier < 0.0 || s.barrier_ring_step > 8 {
            return Some("barrier");
        }
        if s.wish.is_some_and(|wish| !wish_owner_valid(&wish)) {
            return Some("wish");
        }
        if let Some(f) = indicator_fault(
            0,
            s.build_marks,
            [s.mark_tier, s.brand, s.mark_tax],
            s.flags,
        ) {
            return Some(f);
        }
    }
    let mut prev: Option<u32> = None;
    for a in actors {
        if giver_actor_fault(a).is_some() {
            return Some("giver actor");
        }
        if let Some(f) = kit_status_fault(a.faction, a.weapon, a.status) {
            return Some(f);
        }
        if a.mob_type_code > 30 {
            return Some("mob type");
        }
        if a.hp_pct > 100 {
            return Some("hp_pct");
        }
        if a.barrier_pct > 100 || a.barrier_ring_step > 8 {
            return Some("barrier");
        }
        if let Some(f) = actor_indicator_fault(a) {
            return Some(f);
        }
        if !quant::pos_in_range(a.x) || !quant::pos_in_range(a.y) {
            return Some("position");
        }
        if prev.is_some_and(|p| a.id <= p) {
            return Some("actor order");
        }
        prev = Some(a.id);
    }
    None
}

/// Whether `snapshot_fault` could report `a` (or its order after `prev`): a cheap superset test, exact when it says no.
#[inline(always)]
pub(super) fn actor_looks_bad(a: &WireActor, prev: Option<u32>) -> bool {
    giver_actor_fault(a).is_some()
        || (a.faction | a.weapon) > MAX_KIT_CODE
        || a.status > MAX_STATUS_CODE
        || a.mob_type_code > 30
        || a.hp_pct > 100
        || a.barrier_pct > 100
        || a.barrier_ring_step > 8
        || actor_indicator_fault(a).is_some()
        || !(quant::pos_in_range(a.x) && quant::pos_in_range(a.y))
        || prev.is_some_and(|p| a.id <= p)
}

/// The actor's indicator facts as a decoder checks them (ADR0026).
#[inline(always)]
pub(super) fn actor_indicator_fault(a: &WireActor) -> Option<&'static str> {
    indicator_fault(
        a.offhand,
        a.build_marks,
        [a.mark_tier, a.brand, a.mark_tax],
        a.flags,
    )
}

/// A QuestGiver actor has one contextual sentinel tuple. Other actor kinds and Self retain the full four-bit domains.
#[inline(always)]
pub(crate) fn giver_actor_fault(a: &WireActor) -> Option<&'static str> {
    (a.kind == 4 && (a.faction != 15 || a.weapon != 15 || a.mob_type_code != 0))
        .then_some("giver actor")
}

pub(super) fn kit_status_fault(faction: u8, weapon: u8, status: u8) -> Option<&'static str> {
    if faction > MAX_KIT_CODE {
        Some("faction")
    } else if weapon > MAX_KIT_CODE {
        Some("weapon")
    } else if status > MAX_STATUS_CODE {
        Some("status")
    } else {
        None
    }
}

#[inline(always)]
pub(super) fn write_actor(out: &mut Writer, a: &WireActor, prev: Option<u32>) {
    let m = actor_mask(a);
    let mut w = Fixed::<MAX_ACTOR_LEN>::new();
    w.varint(id_field(a.id, prev));
    w.varint(m);
    w.u16(quant::quantize_pos(a.x));
    w.u16(quant::quantize_pos(a.y));
    w.u16(quant::quantize_facing(a.facing));
    if m & AM_KIT != 0 {
        w.u8(kit(a.faction, a.weapon));
    }
    if m & AM_HP != 0 {
        w.u8(a.hp_pct);
    }
    if m & AM_STATUS != 0 {
        w.u8(a.status);
    }
    if m & AM_BOUNTY != 0 {
        w.varint(a.bounty as u32);
    }
    if m & AM_KIND != 0 {
        w.u8(a.kind);
    }
    if m & AM_EFFECTS != 0 {
        w.u16(a.effects);
    }
    if m & AM_DISPLACE != 0 {
        w.u8(a.displace_seq);
    }
    if m & AM_MOB_TYPE != 0 {
        w.u8(a.mob_type_code);
    }
    if m & AM_SIG != 0 {
        w.u8(a.sig);
    }
    if m & AM_BARRIER != 0 {
        w.u8(a.barrier_pct);
        w.u8(a.barrier_ring_step);
    }
    if m & AM_ARCHETYPE != 0 {
        w.u8(a.archetype);
    }
    if m & AM_FLAGS != 0 {
        w.u8(a.flags);
    }
    if m & AM_OFFHAND != 0 {
        w.u8(a.offhand);
    }
    if m & AM_BUILD_MARKS != 0 {
        w.u8(a.build_marks);
    }
    if m & AM_MARK != 0 {
        w.bytes([a.mark_tier, a.brand, a.mark_tax]);
    }
    w.put(out);
}

/// Exact size of a Snapshot frame.
#[allow(clippy::too_many_arguments)] // Mirrors the complete snapshot wire header and body inputs.
pub(super) fn snapshot_len(
    tick: u32,
    server_time: f64,
    phase: u8,
    ack_input: u32,
    me: Option<&WireSelf>,
    training: bool,
    actors: &[WireActor],
    owner_combat: Option<&WireOwnerCombat>,
) -> usize {
    let head = head_word(
        phase,
        tick,
        server_time,
        me,
        training,
        owner_combat.is_some(),
    );
    1 + varint_len(tick)
        + varint_len(ack_input)
        + head_len(head)
        + me.map_or(0, |s| self_len(s, head))
        + (head & HM_SERVER_TIME != 0) as usize * 8
        + varint_len(actors.len() as u32)
        + actors
            .iter()
            .enumerate()
            .map(|(i, a)| actor_len(a, i.checked_sub(1).map(|j| actors[j].id)))
            .sum::<usize>()
        + owner_combat.map_or(0, owner_combat_len)
}

pub(crate) fn owner_combat_len(owner: &WireOwnerCombat) -> usize {
    let lock_len = match owner.lock {
        WireOwnerLock::Clear | WireOwnerLock::Held { target: None } => 1,
        WireOwnerLock::Held { target: Some(id) } => 1 + varint_len(id),
    };
    lock_len + 4 + 4 + 8 + 8 + 8
}

pub(crate) fn write_owner_combat(w: &mut Writer, owner: &WireOwnerCombat) {
    match owner.lock {
        WireOwnerLock::Clear => w.u8(0),
        WireOwnerLock::Held { target: None } => w.u8(1),
        WireOwnerLock::Held { target: Some(id) } => {
            w.u8(2);
            w.varint(id);
        }
    }
    w.u32(owner.draw_started_at);
    w.u32(owner.ready_at);
    w.f64(owner.as_milli);
    w.f64(owner.windup_milli);
    w.f64(owner.speed_milli);
}

pub(super) struct SnapshotHeader<'a> {
    pub(super) tick: u32,
    pub(super) server_time: f64,
    pub(super) phase: u8,
    pub(super) ack_input: u32,
    pub(super) me: Option<&'a WireSelf>,
    pub(super) training: bool,
    pub(super) actors: usize,
    pub(super) owner_combat: bool,
}

/// Everything of a Snapshot before the actors: header, self and the actor count.
pub(super) fn write_snapshot_head(w: &mut Writer, header: SnapshotHeader<'_>) {
    let SnapshotHeader {
        tick,
        server_time,
        phase,
        ack_input,
        me,
        training,
        actors,
        owner_combat,
    } = header;
    let mut f = Fixed::<MAX_HEAD_LEN>::new();
    f.u8(T_SNAPSHOT);
    f.varint(tick);
    f.varint(ack_input);
    let head = head_word(phase, tick, server_time, me, training, owner_combat);
    f.varint(head);
    if let Some(s) = me {
        write_self(&mut f, head, s);
    }
    if head & HM_PHASE != 0 {
        f.u8(phase);
    }
    if head & HM_SERVER_TIME != 0 {
        f.bytes(server_time.to_le_bytes());
    }
    f.varint(actors as u32);
    f.put(w);
}
