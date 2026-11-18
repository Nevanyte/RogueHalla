//! The Snapshot's self and actor records: lengths, masks, the stack-built [`Fixed`] record and the Wish owner group.

use super::*;

pub(super) fn point_opt_len(point: &Option<WirePoint>) -> usize {
    1 + if point.is_some() { 4 } else { 0 }
}
pub(super) fn point_valid(point: Option<WirePoint>) -> bool {
    point.is_none_or(|p| quant::pos_in_range(p.x) && quant::pos_in_range(p.y))
}

/// `max_hp` goes out as a varint when it is an integer in 0..=65535 (the bit pattern survives the round trip, so -0.0, NaN
/// and fractions take the f32 path).
#[inline]
pub(super) fn max_hp_is_varint(v: f32) -> bool {
    (v as u16 as f32).to_bits() == v.to_bits()
}

#[inline]
pub(super) fn kit(faction: u8, weapon: u8) -> u8 {
    (faction & 0x0f) | (weapon << 4)
}

/// The head of a Snapshot: for a self its present bit and the bits of every field that differs from its default, and the phase
/// bit when the phase is not the default.
pub(super) fn head_word(
    phase: u8,
    tick: u32,
    server_time: f64,
    me: Option<&WireSelf>,
    training: bool,
    owner_combat: bool,
) -> u32 {
    let mut m = if phase != DEFAULT_PHASE { HM_PHASE } else { 0 };
    if training {
        m |= HM_TRAINING;
    }
    if owner_combat {
        m |= HM_OWNER_COMBAT;
    }
    if server_time.to_bits() != snapshot_server_ms(tick).to_bits() {
        m |= HM_SERVER_TIME;
    }
    let Some(s) = me else { return m };
    m |= HM_PRESENT;
    if kit(s.faction, s.weapon) != 0 {
        m |= HM_KIT;
    }
    // Bitwise: -0.0 and NaN payloads must survive.
    if s.hp.to_bits() != s.max_hp.to_bits() {
        m |= HM_HP;
    }
    if s.ready_in_ms != 0 {
        m |= HM_READY;
    }
    if s.kills != 0 {
        m |= HM_KILLS;
    }
    if s.bounty != 0 {
        m |= HM_BOUNTY;
    }
    if s.gv != 0 {
        m |= HM_GV;
    }
    if s.move_seq != 0 {
        m |= HM_MOVE_SEQ;
    }
    if !max_hp_is_varint(s.max_hp) {
        m |= HM_MAXHP_F32;
    }
    if s.status != DEFAULT_STATUS {
        m |= HM_STATUS;
    }
    if s.effects != 0 {
        m |= HM_EFFECTS;
    }
    if s.displace_seq != 0 {
        m |= HM_DISPLACE;
    }
    if s.sig != 0 {
        m |= HM_SIG;
    }
    if s.hunt_target.is_some() {
        m |= HM_HUNT_TARGET;
    }
    if s.hunt_stacks != 0 {
        m |= HM_HUNT_STACKS;
    }
    if s.wish.is_some() {
        m |= HM_WISH;
    }
    if s.barrier.to_bits() != 0.0f32.to_bits() || s.barrier_ring_step != 0 {
        m |= HM_BARRIER;
    }
    if s.archetype != 0 {
        m |= HM_ARCHETYPE;
    }
    if s.flags != 0 {
        m |= HM_FLAGS;
    }
    if s.build_marks != 0 {
        m |= HM_BUILD_MARKS;
    }
    if s.mark_tier != 0 || s.brand != 0 || s.mark_tax != 0 {
        m |= HM_MARK;
    }
    m
}

pub(super) fn wish_owner_len(wish: &WireWishOwner) -> usize {
    1 + 8
        + opt_len(wish.full_at.is_some(), 8)
        + 1
        + 8
        + 1
        + opt_len(wish.next_charge_at.is_some(), 8)
        + opt_len(wish.cast.is_some(), 17)
}

pub(super) fn write_wish_owner<const N: usize>(w: &mut Fixed<N>, wish: &WireWishOwner) {
    w.u8(wish.ability);
    w.bytes(wish.ready_at.to_le_bytes());
    if let Some(full_at) = wish.full_at {
        w.u8(1);
        w.bytes(full_at.to_le_bytes());
    } else {
        w.u8(0);
    }
    w.u8(wish.max_charges);
    w.bytes(wish.recharge_ms.to_le_bytes());
    w.u8(wish.charges);
    if let Some(next_charge_at) = wish.next_charge_at {
        w.u8(1);
        w.bytes(next_charge_at.to_le_bytes());
    } else {
        w.u8(0);
    }
    if let Some(cast) = wish.cast {
        w.u8(1);
        w.u8(cast.ability);
        w.bytes(cast.started_at.to_le_bytes());
        w.bytes(cast.lands_at.to_le_bytes());
    } else {
        w.u8(0);
    }
}

pub(crate) fn write_wish_owner_writer(w: &mut Writer, wish: &WireWishOwner) {
    w.u8(wish.ability);
    w.f64(wish.ready_at);
    w.opt(wish.full_at.as_ref(), |w, v| w.f64(*v));
    w.u8(wish.max_charges);
    w.f64(wish.recharge_ms);
    w.u8(wish.charges);
    w.opt(wish.next_charge_at.as_ref(), |w, v| w.f64(*v));
    w.opt(wish.cast.as_ref(), |w, cast| {
        w.u8(cast.ability);
        w.f64(cast.started_at);
        w.f64(cast.lands_at);
    });
}

pub(crate) fn wish_owner_valid(wish: &WireWishOwner) -> bool {
    let clock = |v: f64| v.is_finite() && v >= 0.0;
    wish.ability <= 4
        && clock(wish.ready_at)
        && wish.full_at.is_none_or(clock)
        && (1..=2).contains(&wish.max_charges)
        && wish.recharge_ms.is_finite()
        && wish.recharge_ms >= 0.0
        && wish.charges <= wish.max_charges
        && wish.next_charge_at.is_none_or(clock)
        && wish.cast.is_none_or(|cast| {
            cast.ability <= 4
                && clock(cast.started_at)
                && clock(cast.lands_at)
                && cast.started_at <= cast.lands_at
        })
}

/// Bytes of the self record after its head (`mask` is [`head_word`]).
pub(super) fn self_len(s: &WireSelf, mask: u32) -> usize {
    let mut n = varint_len(s.id) + 12;
    n += if mask & HM_MAXHP_F32 != 0 {
        4
    } else {
        varint_len(s.max_hp as u32)
    };
    if mask & HM_KIT != 0 {
        n += 1;
    }
    if mask & HM_HP != 0 {
        n += 4;
    }
    if mask & HM_READY != 0 {
        n += 2;
    }
    if mask & HM_KILLS != 0 {
        n += varint_len(s.kills as u32);
    }
    if mask & HM_BOUNTY != 0 {
        n += varint_len(s.bounty as u32);
    }
    if mask & HM_GV != 0 {
        n += varint_len(s.gv as u32);
    }
    if mask & HM_MOVE_SEQ != 0 {
        n += 1;
    }
    if mask & HM_STATUS != 0 {
        n += 1;
    }
    if mask & HM_EFFECTS != 0 {
        n += 2;
    }
    if mask & HM_DISPLACE != 0 {
        n += 1;
    }
    if mask & HM_SIG != 0 {
        n += 1;
    }
    if let Some(target) = s.hunt_target {
        n += varint_len(target);
    }
    if mask & HM_HUNT_STACKS != 0 {
        n += varint_len(s.hunt_stacks as u32);
    }
    if mask & HM_WISH != 0 {
        n += wish_owner_len(s.wish.as_ref().expect("Wish bit requires a record"));
    }
    if mask & HM_BARRIER != 0 {
        n += 5;
    }
    if mask & HM_ARCHETYPE != 0 {
        n += 1;
    }
    n += (mask & HM_FLAGS != 0) as usize
        + (mask & HM_BUILD_MARKS != 0) as usize
        + 3 * (mask & HM_MARK != 0) as usize;
    n
}

/// Bytes of the head itself, with the phase byte that follows it when `mask` has the phase bit.
#[inline]
pub(super) fn head_len(mask: u32) -> usize {
    varint_len(mask) + (mask & HM_PHASE != 0) as usize
}

/// A record built on the stack (no capacity checks per byte), then appended to the frame with one copy: the byte-wise
/// `Vec::push` path of [`Writer`] dominated the cost of a Snapshot's header and actors. `N` is the record's maximum size
/// (a longer write panics, which the size constants below make unreachable).
pub(super) struct Fixed<const N: usize> {
    pub(super) b: [u8; N],
    pub(super) n: usize,
}

impl<const N: usize> Fixed<N> {
    #[inline(always)]
    pub(super) fn new() -> Self {
        Self { b: [0; N], n: 0 }
    }
    #[inline(always)]
    pub(super) fn u8(&mut self, v: u8) {
        self.b[self.n] = v;
        self.n += 1;
    }
    #[inline(always)]
    pub(super) fn bytes<const M: usize>(&mut self, v: [u8; M]) {
        if let Some(dst) = self.b[self.n..].first_chunk_mut::<M>() {
            *dst = v;
            self.n += M;
        } else {
            unreachable!("Fixed record overflow");
        }
    }
    #[inline(always)]
    pub(super) fn u16(&mut self, v: u16) {
        self.bytes(v.to_le_bytes());
    }
    #[inline(always)]
    pub(super) fn f32(&mut self, v: f32) {
        self.bytes(v.to_le_bytes());
    }
    /// Unsigned LEB128, one bounds check per length class instead of one per byte.
    #[inline(always)]
    pub(super) fn varint(&mut self, v: u32) {
        if v < 0x80 {
            self.u8(v as u8);
        } else if v < 0x4000 {
            self.bytes([(v as u8) | 0x80, (v >> 7) as u8]);
        } else if v < 0x20_0000 {
            self.bytes([(v as u8) | 0x80, ((v >> 7) as u8) | 0x80, (v >> 14) as u8]);
        } else if v < 0x1000_0000 {
            self.bytes([
                (v as u8) | 0x80,
                ((v >> 7) as u8) | 0x80,
                ((v >> 14) as u8) | 0x80,
                (v >> 21) as u8,
            ]);
        } else {
            self.bytes([
                (v as u8) | 0x80,
                ((v >> 7) as u8) | 0x80,
                ((v >> 14) as u8) | 0x80,
                ((v >> 21) as u8) | 0x80,
                (v >> 28) as u8,
            ]);
        }
    }
    #[inline(always)]
    pub(super) fn put(&self, w: &mut Writer) {
        w.append_record(&self.b, self.n);
    }
}

/// Largest Snapshot head including protocol-12 Wish, barrier, and source-clock override groups.
pub(super) const MAX_HEAD_LEN: usize = 1
    + 5
    + 5
    + 3
    + 5
    + 12
    + 4
    + 1
    + 4
    + 2
    + 9
    + 1
    + 1
    + 2
    + 1
    + 1
    + 1
    + 5
    + 5
    + 3
    + 55
    + 5
    + 8
    + 1
    + 1
    + 1
    + 3;
/// Largest actor: id 5, mask 3, x/y/facing 6, kit/hp/status, bounty 3, kind, effects 2, displacement, mob type, sig, barrier pair,
/// archetype, flags, off-hand, build marks, mark plate 3.
pub(crate) const MAX_ACTOR_LEN: usize =
    5 + 3 + 6 + 1 + 1 + 1 + 3 + 1 + 2 + 1 + 1 + 1 + 2 + 1 + 1 + 1 + 1 + 3;

/// Writes the self record for the head word `m` ([`head_word`] of `s`), fields in bit order.
#[inline]
pub(super) fn write_self(w: &mut Fixed<MAX_HEAD_LEN>, m: u32, s: &WireSelf) {
    w.varint(s.id);
    let [x0, x1, x2, x3] = s.x.to_le_bytes();
    let [y0, y1, y2, y3] = s.y.to_le_bytes();
    let [f0, f1, f2, f3] = s.facing.to_le_bytes();
    w.bytes([x0, x1, x2, x3, y0, y1, y2, y3, f0, f1, f2, f3]);
    if m & HM_MAXHP_F32 != 0 {
        w.f32(s.max_hp);
    } else {
        w.varint(s.max_hp as u32);
    }
    if m & HM_KIT != 0 {
        w.u8(kit(s.faction, s.weapon));
    }
    if m & HM_HP != 0 {
        w.f32(s.hp);
    }
    if m & HM_READY != 0 {
        w.u16(s.ready_in_ms);
    }
    if m & HM_KILLS != 0 {
        w.varint(s.kills as u32);
    }
    if m & HM_BOUNTY != 0 {
        w.varint(s.bounty as u32);
    }
    if m & HM_MOVE_SEQ != 0 {
        w.u8(s.move_seq);
    }
    if m & HM_GV != 0 {
        w.varint(s.gv as u32);
    }
    if m & HM_STATUS != 0 {
        w.u8(s.status);
    }
    if m & HM_EFFECTS != 0 {
        w.u16(s.effects);
    }
    if m & HM_DISPLACE != 0 {
        w.u8(s.displace_seq);
    }
    if m & HM_SIG != 0 {
        w.u8(s.sig);
    }
    if let Some(target) = s.hunt_target {
        w.varint(target);
    }
    if m & HM_HUNT_STACKS != 0 {
        w.varint(s.hunt_stacks as u32);
    }
    if m & HM_WISH != 0 {
        write_wish_owner(w, s.wish.as_ref().expect("Wish bit requires a record"));
    }
    if m & HM_BARRIER != 0 {
        w.f32(s.barrier);
        w.u8(s.barrier_ring_step);
    }
    if m & HM_ARCHETYPE != 0 {
        w.u8(s.archetype);
    }
    if m & HM_FLAGS != 0 {
        w.u8(s.flags);
    }
    if m & HM_BUILD_MARKS != 0 {
        w.u8(s.build_marks);
    }
    if m & HM_MARK != 0 {
        w.bytes([s.mark_tier, s.brand, s.mark_tax]);
    }
}

/// The actor mask of `a`: a bit for every field that differs from its default.
#[inline]
pub(super) fn actor_mask(a: &WireActor) -> u32 {
    let mut m = 0;
    if kit(a.faction, a.weapon) != 0 {
        m |= AM_KIT;
    }
    if a.hp_pct != DEFAULT_HP_PCT {
        m |= AM_HP;
    }
    if a.status != DEFAULT_STATUS {
        m |= AM_STATUS;
    }
    if a.bounty != 0 {
        m |= AM_BOUNTY;
    }
    if a.kind != 0 {
        m |= AM_KIND;
    }
    if a.effects != 0 {
        m |= AM_EFFECTS;
    }
    if a.displace_seq != 0 {
        m |= AM_DISPLACE;
    }
    if a.mob_type_code != 0 {
        m |= AM_MOB_TYPE;
    }
    if a.sig != 0 {
        m |= AM_SIG;
    }
    if a.barrier_pct != DEFAULT_BARRIER_PCT || a.barrier_ring_step != DEFAULT_BARRIER_RING_STEP {
        m |= AM_BARRIER;
    }
    if a.archetype != 0 {
        m |= AM_ARCHETYPE;
    }
    if a.flags != 0 {
        m |= AM_FLAGS;
    }
    if a.offhand != 0 {
        m |= AM_OFFHAND;
    }
    if a.build_marks != 0 {
        m |= AM_BUILD_MARKS;
    }
    if a.mark_tier != 0 || a.brand != 0 || a.mark_tax != 0 {
        m |= AM_MARK;
    }
    m
}

/// Bytes one actor takes on the wire.
#[inline]
pub(super) fn actor_len(a: &WireActor, prev: Option<u32>) -> usize {
    let m = actor_mask(a);
    varint_len(id_field(a.id, prev))
        + varint_len(m)
        + 6
        + (m & AM_KIT != 0) as usize
        + (m & AM_HP != 0) as usize
        + (m & AM_STATUS != 0) as usize
        + if m & AM_BOUNTY != 0 {
            varint_len(a.bounty as u32)
        } else {
            0
        }
        + (m & AM_KIND != 0) as usize
        + 2 * (m & AM_EFFECTS != 0) as usize
        + (m & AM_DISPLACE != 0) as usize
        + (m & AM_MOB_TYPE != 0) as usize
        + (m & AM_SIG != 0) as usize
        + 2 * (m & AM_BARRIER != 0) as usize
        + (m & AM_ARCHETYPE != 0) as usize
        + (m & AM_FLAGS != 0) as usize
        + (m & AM_OFFHAND != 0) as usize
        + (m & AM_BUILD_MARKS != 0) as usize
        + 3 * (m & AM_MARK != 0) as usize
}

/// The id as written: the first actor's absolute id, then the gap to the previous one (the observation sorts by id, so a
/// gap is small and a varint of it is mostly one byte).
#[inline]
pub(super) fn id_field(id: u32, prev: Option<u32>) -> u32 {
    match prev {
        None => id,
        Some(p) => id.wrapping_sub(p),
    }
}
