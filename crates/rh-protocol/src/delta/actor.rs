//! [`QActor`]: a remote actor as a connection remembers and sends it, and its full-record writer.

use super::*;

/// A remote actor as a connection remembers and sends it: exactly what the wire carries.
#[derive(Clone, Copy, Debug, PartialEq, Eq, Default)]
pub struct QActor {
    pub id: u32,
    /// Position on the 1/8 u grid ([`quant::quantize_pos`]).
    pub x: u16,
    pub y: u16,
    /// Angle code ([`quant::quantize_facing`]).
    pub facing: u16,
    /// Faction in the low nibble, weapon in the high one.
    pub kit: u8,
    pub hp_pct: u8,
    pub status: u8,
    pub bounty: u16,
    /// What the entity is (0 = player).
    pub kind: u8,
    pub effects: u16,
    pub displace_seq: u8,
    pub sig: u8,
    pub mob_type_code: u8,
    pub barrier_pct: u8,
    pub barrier_ring_step: u8,
    pub archetype: u8,
    /// Indicator facts (ADR0026): `ACTOR_FLAG_*` bits, the off-hand code, the packed build marks and the mark plate
    /// (tier, brand, tax).
    pub flags: u8,
    pub offhand: u8,
    pub build_marks: u8,
    pub mark: [u8; 3],
    /// The walk hint (protocol 19): `[walk_dir, walk_ticks]`.
    pub walk: [u8; 2],
}

impl QActor {
    /// The actor a delta's new entry starts from: every optional field at its default (position zero; a new entry carries it).
    pub const NEW: QActor = QActor {
        id: 0,
        x: 0,
        y: 0,
        facing: 0,
        kit: 0,
        hp_pct: DEFAULT_HP_PCT,
        status: DEFAULT_STATUS,
        bounty: 0,
        kind: 0,
        effects: 0,
        displace_seq: 0,
        mob_type_code: 0,
        sig: 0,
        barrier_pct: 0,
        barrier_ring_step: 0,
        archetype: 0,
        flags: 0,
        offhand: 0,
        build_marks: 0,
        mark: [0; 3],
        walk: [0; 2],
    };

    /// `a` checked and quantized: the first reason a decoder would reject the actor on its own (a faction, weapon or status over
    /// its bit width, an `hp_pct` over 100, a position off the wire grid), or the actor as it goes over the wire. The check is
    /// per actor, so a result can be reused wherever the same actor is shown; the ascending order of ids belongs to a list and
    /// is checked by [`SnapshotData::build_checked`].
    #[inline]
    pub fn checked(a: &WireActor) -> Result<Self, &'static str> {
        if actor_looks_bad(a, None) {
            return Err(snapshot_fault(0, None, std::slice::from_ref(a)).unwrap_or("actor"));
        }
        Ok(Self::from_wire(a))
    }

    /// `a` as it goes over the wire (the caller has checked it: [`QActor::checked`] does).
    #[inline]
    pub fn from_wire(a: &WireActor) -> Self {
        QActor {
            id: a.id,
            x: quant::quantize_pos(a.x),
            y: quant::quantize_pos(a.y),
            facing: quant::quantize_facing(a.facing),
            kit: kit(a.faction, a.weapon),
            hp_pct: a.hp_pct,
            status: a.status,
            bounty: a.bounty,
            kind: a.kind,
            effects: a.effects,
            displace_seq: a.displace_seq,
            mob_type_code: a.mob_type_code,
            sig: a.sig,
            barrier_pct: a.barrier_pct,
            barrier_ring_step: a.barrier_ring_step,
            archetype: a.archetype,
            flags: a.flags,
            offhand: a.offhand,
            build_marks: a.build_marks,
            mark: [a.mark_tier, a.brand, a.mark_tax],
            walk: [a.walk_dir, a.walk_ticks],
        }
    }

    /// What a client decodes this actor to.
    pub fn to_wire(&self) -> WireActor {
        WireActor {
            id: self.id,
            faction: self.kit & 0x0f,
            x: quant::dequantize_pos(self.x),
            y: quant::dequantize_pos(self.y),
            facing: quant::dequantize_facing(self.facing),
            hp_pct: self.hp_pct,
            status: self.status,
            weapon: self.kit >> 4,
            bounty: self.bounty,
            kind: self.kind,
            effects: self.effects,
            displace_seq: self.displace_seq,
            mob_type_code: self.mob_type_code,
            sig: self.sig,
            barrier_pct: self.barrier_pct,
            barrier_ring_step: self.barrier_ring_step,
            archetype: self.archetype,
            flags: self.flags,
            offhand: self.offhand,
            build_marks: self.build_marks,
            mark_tier: self.mark[0],
            brand: self.mark[1],
            mark_tax: self.mark[2],
            walk_dir: self.walk[0],
            walk_ticks: self.walk[1],
        }
    }

    /// The Full actor mask: a bit per field that differs from its default.
    #[inline]
    fn full_mask(&self) -> u32 {
        let mut m = 0;
        if self.kit != 0 {
            m |= AM_KIT;
        }
        if self.hp_pct != DEFAULT_HP_PCT {
            m |= AM_HP;
        }
        if self.status != DEFAULT_STATUS {
            m |= AM_STATUS;
        }
        if self.bounty != 0 {
            m |= AM_BOUNTY;
        }
        if self.kind != 0 {
            m |= AM_KIND;
        }
        if self.effects != 0 {
            m |= AM_EFFECTS;
        }
        if self.displace_seq != 0 {
            m |= AM_DISPLACE;
        }
        if self.mob_type_code != 0 {
            m |= AM_MOB_TYPE;
        }
        if self.sig != 0 {
            m |= AM_SIG;
        }
        if self.barrier_pct != 0 || self.barrier_ring_step != 0 {
            m |= AM_BARRIER;
        }
        if self.archetype != 0 {
            m |= AM_ARCHETYPE;
        }
        if self.flags != 0 {
            m |= AM_FLAGS;
        }
        if self.offhand != 0 {
            m |= AM_OFFHAND;
        }
        if self.build_marks != 0 {
            m |= AM_BUILD_MARKS;
        }
        if self.mark != [0; 3] {
            m |= AM_MARK;
        }
        if self.walk[1] != 0 {
            m |= AM_WALK;
        }
        m
    }

    /// Bytes this actor takes in a Full frame after `prev` (the previous actor's id).
    #[inline]
    pub(super) fn full_len(&self, prev: Option<u32>) -> usize {
        varint_len(id_field(self.id, prev))
            + 6
            + varint_len(self.full_mask())
            + (self.kit != 0) as usize
            + (self.hp_pct != DEFAULT_HP_PCT) as usize
            + (self.status != DEFAULT_STATUS) as usize
            + if self.bounty != 0 {
                varint_len(self.bounty as u32)
            } else {
                0
            }
            + (self.kind != 0) as usize
            + 2 * (self.effects != 0) as usize
            + (self.displace_seq != 0) as usize
            + (self.mob_type_code != 0) as usize
            + (self.sig != 0) as usize
            + 2 * (self.barrier_pct != 0 || self.barrier_ring_step != 0) as usize
            + (self.archetype != 0) as usize
            + (self.flags != 0) as usize
            + (self.offhand != 0) as usize
            + (self.build_marks != 0) as usize
            + 3 * (self.mark != [0; 3]) as usize
            + 2 * (self.walk[1] != 0) as usize
    }
}

/// One v2 actor record from a [`QActor`] (the same bytes as `write_actor` writes for the [`WireActor`] it came from).
#[inline(always)]
pub(super) fn write_full_actor(out: &mut Writer, q: &QActor, prev: Option<u32>) {
    let m = q.full_mask();
    let mut w = Fixed::<MAX_ACTOR_LEN>::new();
    w.varint(id_field(q.id, prev));
    w.varint(m);
    w.u16(q.x);
    w.u16(q.y);
    w.u16(q.facing);
    if m & AM_KIT != 0 {
        w.u8(q.kit);
    }
    if m & AM_HP != 0 {
        w.u8(q.hp_pct);
    }
    if m & AM_STATUS != 0 {
        w.u8(q.status);
    }
    if m & AM_BOUNTY != 0 {
        w.varint(q.bounty as u32);
    }
    if m & AM_KIND != 0 {
        w.u8(q.kind);
    }
    if m & AM_EFFECTS != 0 {
        w.u16(q.effects);
    }
    if m & AM_DISPLACE != 0 {
        w.u8(q.displace_seq);
    }
    if m & AM_MOB_TYPE != 0 {
        w.u8(q.mob_type_code);
    }
    if m & AM_SIG != 0 {
        w.u8(q.sig);
    }
    if m & AM_BARRIER != 0 {
        w.u8(q.barrier_pct);
        w.u8(q.barrier_ring_step);
    }
    if m & AM_ARCHETYPE != 0 {
        w.u8(q.archetype);
    }
    if m & AM_FLAGS != 0 {
        w.u8(q.flags);
    }
    if m & AM_OFFHAND != 0 {
        w.u8(q.offhand);
    }
    if m & AM_BUILD_MARKS != 0 {
        w.u8(q.build_marks);
    }
    if m & AM_MARK != 0 {
        w.bytes(q.mark);
    }
    if m & AM_WALK != 0 {
        w.bytes(q.walk);
    }
    w.put(out);
}
