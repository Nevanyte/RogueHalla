//! [`ConnEncoder`]: the connection's writer state, and the Full/Delta frame writers it uses.

use super::*;

/// Which kind of snapshot frame [`ConnEncoder::encode`] wrote.
#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub enum FrameKind {
    Full,
    Delta,
}

/// A connection's writer state for snapshots (one per connection, owned by its writer task). See the module docs.
#[derive(Debug, Default)]
pub struct ConnEncoder {
    base: Option<SnapshotData>,
    /// A resync was requested and not yet answered: the next snapshot is Full once [`MIN_FORCED_FULL_TICKS`] have passed since
    /// the last forced one (until then deltas continue and the request stays pending).
    force_full: bool,
    /// The tick of the last Full frame that answered a resync request.
    last_forced: Option<u32>,
    departed: Vec<u32>,
    /// The base before the current one, free to build the next snapshot in ([`ConnEncoder::take_spare`]).
    spare: Option<SnapshotData>,
    /// The buffer [`ConnEncoder::encode_frame`] assembles a frame in before copying it to its exact size.
    scratch: Vec<u8>,
}

/// Largest scratch buffer a [`ConnEncoder`] keeps between frames: a bigger one (a Full frame of a crowd) is freed once it has
/// been copied out, so a connection holds no more than this beyond its snapshots.
pub(super) const SCRATCH_KEEP: usize = 2048;

impl ConnEncoder {
    pub fn new() -> Self {
        Self::default()
    }

    /// A client asked to resync: the next snapshot is a Full frame, or the first one at least [`MIN_FORCED_FULL_TICKS`] after the
    /// last forced Full (the request is kept, never dropped). Asking again while it is pending changes nothing.
    pub fn request_full(&mut self) {
        self.force_full = true;
    }

    /// The tick of the last snapshot written (the base of the next delta), if any.
    pub fn base_tick(&self) -> Option<u32> {
        self.base.as_ref().map(SnapshotData::tick)
    }

    /// Bytes this connection's snapshot state holds (base, spare list, scratch list): bounded by [`MAX_ACTORS`] whatever the
    /// traffic.
    pub fn memory_bytes(&self) -> usize {
        std::mem::size_of::<Self>()
            + self.base.as_ref().map_or(0, |b| b.memory_bytes())
            + self.spare.as_ref().map_or(0, SnapshotData::memory_bytes)
            + self.departed.capacity() * 4
            + self.scratch.capacity()
    }

    /// The snapshot the base replaced, finished with: pass it to [`SnapshotData::build_with`] for the next snapshot, so the
    /// steady state never allocates one. `None` until the base has been replaced once.
    pub fn take_spare(&mut self) -> Option<SnapshotData> {
        self.spare.take()
    }

    /// Whether a pending resync request is to be answered with `cur` (it is pending and the interval since the last forced Full
    /// has passed).
    fn forced_full_due(&self, cur: &SnapshotData) -> bool {
        self.force_full
            && self
                .last_forced
                .is_none_or(|t| cur.tick.wrapping_sub(t) >= MIN_FORCED_FULL_TICKS)
    }

    /// Whether `cur` may be sent as a delta against the base; `forced` is [`ConnEncoder::forced_full_due`].
    fn delta_possible(&self, cur: &SnapshotData, forced: bool) -> bool {
        let Some(b) = &self.base else { return false };
        if forced || cur.tick <= b.tick || cur.tick - b.tick > MAX_DELTA_AGE {
            return false;
        }
        match (&b.me, &cur.me) {
            (_, None) => true,
            (Some(bm), Some(cm)) => bm.id == cm.id,
            (None, Some(_)) => false,
        }
    }

    /// The frame for `snap` in one allocation of exactly its size (what the socket writer owns until it is sent), and its kind.
    /// The frame is assembled in a buffer this encoder keeps between calls and copied out, so the only allocation per frame is
    /// the frame itself: assembled in a fresh buffer, a 13-byte delta would sit in a block sized for the worst case (the stack
    /// records are copied whole before they are trimmed). Otherwise exactly [`ConnEncoder::encode`].
    pub fn encode_frame(&mut self, snap: SnapshotData) -> (Vec<u8>, FrameKind) {
        let mut scratch = std::mem::take(&mut self.scratch);
        scratch.clear();
        scratch.reserve(snap.frame_capacity());
        let kind = self.encode(snap, &mut scratch);
        let frame = scratch.clone();
        if scratch.capacity() <= SCRATCH_KEEP {
            self.scratch = scratch;
        }
        (frame, kind)
    }

    /// Appends the frame for `snap` to `out` and makes `snap` the base of the next one: a Delta when that is possible and
    /// smaller than the Full frame, else a Full frame.
    pub fn encode(&mut self, snap: SnapshotData, out: &mut Vec<u8>) -> FrameKind {
        let start = out.len();
        let mut kind = FrameKind::Full;
        let forced = self.forced_full_due(&snap);
        if self.delta_possible(&snap, forced) {
            let base = self.base.as_ref().expect("checked by delta_possible");
            write_delta(base, &snap, &mut self.departed, out);
            // Smaller than the Full frame? Below the Full frame's least size it certainly is; the exact size (a pass over the
            // actors) is asked for only when the delta is that large.
            let len = out.len() - start;
            if len < snap.min_len() || len < snap.full_len() {
                kind = FrameKind::Delta;
            } else {
                out.truncate(start);
            }
        }
        if kind == FrameKind::Full {
            snap.encode_full_into(out);
        }
        if kind == FrameKind::Full {
            // Any Full frame answers a pending request (the client holds a snapshot it can apply deltas to again).
            self.force_full = false;
            if forced {
                self.last_forced = Some(snap.tick);
            }
        }
        self.spare = self.base.replace(snap);
        kind
    }
}

/// Appends a stack record to the frame with one constant-size copy (inline moves, no `memcpy` call for a length only known at
/// run time), then drops the unused tail: what `Fixed::put` does for a [`Writer`].
#[inline(always)]
pub(super) fn put<const N: usize>(out: &mut Vec<u8>, f: &Fixed<N>) {
    let len = out.len();
    out.extend_from_slice(&f.b);
    out.truncate(len + f.n);
}

/// The mask of the fields of `q` that differ from `p` (the same actor in the base), bits as [`AD_X`]...
#[inline]
pub(super) fn actor_delta_mask(p: &QActor, q: &QActor) -> u32 {
    (p.x != q.x) as u32
        | ((p.y != q.y) as u32) << 1
        | ((p.facing != q.facing) as u32) << 2
        | ((p.kit != q.kit) as u32) << 3
        | ((p.hp_pct != q.hp_pct) as u32) << 4
        | ((p.bounty != q.bounty) as u32) << 5
        | ((p.status != q.status) as u32) << 6
        | ((p.kind != q.kind) as u32) << 7
        | ((p.effects != q.effects) as u32) << 8
        | ((p.displace_seq != q.displace_seq) as u32) << 9
        | ((p.mob_type_code != q.mob_type_code) as u32) << 10
        | ((p.sig != q.sig) as u32) << 11
        | ((p.barrier_pct != q.barrier_pct || p.barrier_ring_step != q.barrier_ring_step) as u32)
            << 12
        | ((p.archetype != q.archetype) as u32) << 13
        | ((p.flags != q.flags) as u32) << 14
        | ((p.offhand != q.offhand) as u32) << 15
        | ((p.build_marks != q.build_marks) as u32) << 16
        | ((p.mark != q.mark) as u32) << 17
        | ((p.walk != q.walk) as u32) << 18
}

/// The mask of a NEW actor: its position, and every other field that is not the default.
#[inline]
pub(super) fn new_actor_mask(q: &QActor) -> u32 {
    AD_NEW | actor_delta_mask(&QActor::NEW, q)
}

/// Appends the delta of `cur` against `base` to `out` (the caller has checked [`ConnEncoder::delta_possible`]'s conditions).
/// One pass: the head and the changed count are written as one-byte placeholders that are patched once the actors have been
/// walked (the head is one byte unless a bit from 7 up is set; the count is one byte unless 128 or more actors changed), and
/// each changed actor is appended as it is found.
pub(super) fn write_delta(
    base: &SnapshotData,
    cur: &SnapshotData,
    departed: &mut Vec<u32>,
    out: &mut Vec<u8>,
) {
    departed.clear();
    let start = out.len();
    let age = cur.tick - base.tick;
    let mut f = Fixed::<MAX_DELTA_HEAD>::new();
    f.u8(T_SNAPSHOT_DELTA);
    f.varint(cur.tick);
    f.varint(age);
    f.varint(cur.ack_input);
    let head_at = f.n;
    f.u8(0);
    let mut head = 0u32;
    if cur.training {
        head |= DH_TRAINING;
    }
    if let (Some(bm), Some(cm)) = (&base.me, &cur.me) {
        head |= DH_PRESENT | write_self_delta(&mut f, bm, cm);
    }
    if cur.phase != base.phase {
        head |= DH_PHASE;
        f.u8(cur.phase);
    }
    if cur.server_time.to_bits() != snapshot_server_ms(cur.tick).to_bits() {
        head |= DH_SERVER_TIME;
        f.bytes(cur.server_time.to_le_bytes());
    }
    let owner_combat = owner_combat_delta(base, cur);
    if owner_combat.is_some() {
        head |= DH_OWNER_COMBAT;
    }
    let count_at = f.n;
    f.u8(0);
    put(out, &f);

    let (b, c) = (base.actors.as_slice(), cur.actors.as_slice());
    let (mut i, mut last, mut changed) = (0usize, 0u32, 0u32);
    for q in c {
        while i < b.len() && b[i].id < q.id {
            departed.push(b[i].id);
            i += 1;
        }
        let mask = if i < b.len() && b[i].id == q.id {
            let m = actor_delta_mask(&b[i], q);
            i += 1;
            if m == 0 {
                continue;
            }
            m
        } else {
            new_actor_mask(q)
        };
        let mut w = Fixed::<MAX_ACTOR_DELTA>::new();
        w.varint(if changed == 0 { q.id } else { q.id - last });
        w.varint(mask);
        if mask & AD_X != 0 {
            w.u16(q.x);
        }
        if mask & AD_Y != 0 {
            w.u16(q.y);
        }
        if mask & AD_FACING != 0 {
            w.u16(q.facing);
        }
        if mask & AD_KIT != 0 {
            w.u8(q.kit);
        }
        if mask & AD_HP != 0 {
            w.u8(q.hp_pct);
        }
        if mask & AD_BOUNTY != 0 {
            w.varint(q.bounty as u32);
        }
        if mask & AD_STATUS != 0 {
            w.u8(q.status);
        }
        if mask & AD_KIND != 0 {
            w.u8(q.kind);
        }
        if mask & AD_EFFECTS != 0 {
            w.u16(q.effects);
        }
        if mask & AD_DISPLACE != 0 {
            w.u8(q.displace_seq);
        }
        if mask & AD_MOB_TYPE != 0 {
            w.u8(q.mob_type_code);
        }
        if mask & AD_SIG != 0 {
            w.u8(q.sig);
        }
        if mask & AD_BARRIER != 0 {
            w.u8(q.barrier_pct);
            w.u8(q.barrier_ring_step);
        }
        if mask & AD_ARCHETYPE != 0 {
            w.u8(q.archetype);
        }
        if mask & AD_FLAGS != 0 {
            w.u8(q.flags);
        }
        if mask & AD_OFFHAND != 0 {
            w.u8(q.offhand);
        }
        if mask & AD_BUILD_MARKS != 0 {
            w.u8(q.build_marks);
        }
        if mask & AD_MARK != 0 {
            w.bytes(q.mark);
        }
        if mask & AD_WALK != 0 {
            w.bytes(q.walk);
        }
        put(out, &w);
        last = q.id;
        changed += 1;
    }
    departed.extend(b[i..].iter().map(|p| p.id));
    if !departed.is_empty() {
        head |= DH_DEPARTED;
        let mut w = Writer::from_vec(std::mem::take(out));
        write_departed(&mut w, departed);
        *out = w.finish();
    }
    if let Some(owner) = owner_combat {
        let mut w = Writer::from_vec(std::mem::take(out));
        match owner {
            None => w.u8(0),
            Some(owner) => {
                w.u8(1);
                write_owner_combat(&mut w, &owner);
            }
        }
        *out = w.finish();
    }
    // The count sits after the head, so it is patched first (a widening there does not move the head).
    if changed < 0x80 {
        out[start + count_at] = changed as u8;
    } else {
        // 128 or more changed actors: the count is a two-byte varint (at most 512).
        let v = [(changed as u8) | 0x80, (changed >> 7) as u8];
        out.splice(start + count_at..start + count_at + 1, v);
    }
    if head < 0x80 {
        out[start + head_at] = head as u8;
    } else {
        // Protocol 16 adds head bits through 28 (ADR0026), so the canonical head can be five bytes.
        let mut v = [0u8; 5];
        let n = varint_len(head);
        for (k, b) in v[..n].iter_mut().enumerate() {
            *b = (head >> (7 * k)) as u8 & 0x7f | if k + 1 < n { 0x80 } else { 0 };
        }
        out.splice(start + head_at..start + head_at + 1, v[..n].iter().copied());
    }
}

pub(super) fn write_departed(w: &mut Writer, ids: &[u32]) {
    w.varint(ids.len() as u32);
    let mut last = 0u32;
    for (n, &id) in ids.iter().enumerate() {
        w.varint(if n == 0 { id } else { id - last });
        last = id;
    }
}

/// The self fields of `c` that differ from `b`, written in bit order; returns their head bits.
#[inline]
pub(super) fn write_self_delta(f: &mut Fixed<MAX_DELTA_HEAD>, b: &WireSelf, c: &WireSelf) -> u32 {
    let mut m = 0u32;
    // Bitwise comparisons: -0.0 and NaN payloads must survive.
    if c.x.to_bits() != b.x.to_bits() {
        m |= DH_X;
        f.f32(c.x);
    }
    if c.y.to_bits() != b.y.to_bits() {
        m |= DH_Y;
        f.f32(c.y);
    }
    if c.facing.to_bits() != b.facing.to_bits() {
        m |= DH_FACING;
        f.f32(c.facing);
    }
    if c.move_seq != b.move_seq {
        m |= DH_MOVE_SEQ;
        f.u8(c.move_seq);
    }
    if c.hp.to_bits() != b.hp.to_bits() {
        m |= DH_HP;
        f.f32(c.hp);
    }
    if c.ready_in_ms != b.ready_in_ms {
        m |= DH_READY;
        f.u16(c.ready_in_ms);
    }
    if c.max_hp.to_bits() != b.max_hp.to_bits() {
        m |= DH_MAX_HP;
        f.f32(c.max_hp);
    }
    let k = kit(c.faction, c.weapon);
    if k != kit(b.faction, b.weapon) {
        m |= DH_KIT;
        f.u8(k);
    }
    if c.kills != b.kills {
        m |= DH_KILLS;
        f.varint(c.kills as u32);
    }
    if c.bounty != b.bounty {
        m |= DH_BOUNTY;
        f.varint(c.bounty as u32);
    }
    if c.gv != b.gv {
        m |= DH_GV;
        f.varint(c.gv as u32);
    }
    if c.status != b.status {
        m |= DH_STATUS;
        f.u8(c.status);
    }
    if c.effects != b.effects {
        m |= DH_EFFECTS;
        f.u16(c.effects);
    }
    if c.displace_seq != b.displace_seq {
        m |= DH_DISPLACE;
        f.u8(c.displace_seq);
    }
    if c.sig != b.sig {
        m |= DH_SIG;
        f.u8(c.sig);
    }
    if c.hunt_target != b.hunt_target {
        m |= DH_HUNT_TARGET;
        f.varint(c.hunt_target.unwrap_or(0));
    }
    if c.hunt_stacks != b.hunt_stacks {
        m |= DH_HUNT_STACKS;
        f.varint(c.hunt_stacks as u32);
    }
    if !wish_bits_equal(b.wish, c.wish) {
        m |= DH_WISH;
        match c.wish {
            None => f.u8(0),
            Some(wish) => {
                f.u8(1);
                super::super::write_wish_owner(f, &wish);
            }
        }
    }
    if c.barrier.to_bits() != b.barrier.to_bits() || c.barrier_ring_step != b.barrier_ring_step {
        m |= DH_BARRIER;
        f.f32(c.barrier);
        f.u8(c.barrier_ring_step);
    }
    if c.archetype != b.archetype {
        m |= DH_ARCHETYPE;
        f.u8(c.archetype);
    }
    if c.flags != b.flags {
        m |= DH_FLAGS;
        f.u8(c.flags);
    }
    if c.build_marks != b.build_marks {
        m |= DH_BUILD_MARKS;
        f.u8(c.build_marks);
    }
    let mark = [c.mark_tier, c.brand, c.mark_tax];
    if mark != [b.mark_tier, b.brand, b.mark_tax] {
        m |= DH_MARK;
        f.bytes(mark);
    }
    m
}
