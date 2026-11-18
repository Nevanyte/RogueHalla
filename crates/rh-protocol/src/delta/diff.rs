//! The decoded delta types ([`SelfDelta`], [`ActorDelta`], [`SnapshotDelta`]) and [`diff`], which computes one.

use super::*;

/// The changed self fields of a delta: `mask` has the head's self field bits (never the present, phase or departed bits); only
/// the fields whose bit is set mean anything (the others are zero).
#[derive(Clone, Copy, Debug, PartialEq, Default)]
pub struct SelfDelta {
    pub mask: u32,
    pub x: f32,
    pub y: f32,
    pub facing: f32,
    pub hp: f32,
    pub max_hp: f32,
    /// Faction in the low nibble, weapon in the high one.
    pub kit: u8,
    pub ready_in_ms: u16,
    pub kills: u16,
    pub bounty: u16,
    pub gv: u16,
    pub move_seq: u8,
    pub status: u8,
    pub effects: u16,
    pub displace_seq: u8,
    pub sig: u8,
    pub hunt_target: Option<u32>,
    pub hunt_stacks: u16,
    /// `Some(None)` clears the owner's Wish record; `None` means this group did not change.
    pub wish: Option<Option<WireWishOwner>>,
    pub barrier: f32,
    pub barrier_ring_step: u8,
    pub archetype: u8,
    pub flags: u8,
    pub build_marks: u8,
    /// Tier, brand, tax.
    pub mark: [u8; 3],
}

impl SelfDelta {
    /// The self after applying this delta to `base`.
    pub fn apply(&self, base: &WireSelf) -> WireSelf {
        let mut s = *base;
        let m = self.mask;
        if m & DH_X != 0 {
            s.x = self.x;
        }
        if m & DH_Y != 0 {
            s.y = self.y;
        }
        if m & DH_FACING != 0 {
            s.facing = self.facing;
        }
        if m & DH_HP != 0 {
            s.hp = self.hp;
        }
        if m & DH_MAX_HP != 0 {
            s.max_hp = self.max_hp;
        }
        if m & DH_KIT != 0 {
            s.faction = self.kit & 0x0f;
            s.weapon = self.kit >> 4;
        }
        if m & DH_READY != 0 {
            s.ready_in_ms = self.ready_in_ms;
        }
        if m & DH_KILLS != 0 {
            s.kills = self.kills;
        }
        if m & DH_BOUNTY != 0 {
            s.bounty = self.bounty;
        }
        if m & DH_GV != 0 {
            s.gv = self.gv;
        }
        if m & DH_MOVE_SEQ != 0 {
            s.move_seq = self.move_seq;
        }
        if m & DH_STATUS != 0 {
            s.status = self.status;
        }
        if m & DH_EFFECTS != 0 {
            s.effects = self.effects;
        }
        if m & DH_DISPLACE != 0 {
            s.displace_seq = self.displace_seq;
        }
        if m & DH_SIG != 0 {
            s.sig = self.sig;
        }
        if m & DH_HUNT_TARGET != 0 {
            s.hunt_target = self.hunt_target;
        }
        if m & DH_HUNT_STACKS != 0 {
            s.hunt_stacks = self.hunt_stacks;
        }
        if m & DH_WISH != 0 {
            if let Some(wish) = self.wish {
                s.wish = wish;
            }
        }
        if m & DH_BARRIER != 0 {
            s.barrier = self.barrier;
            s.barrier_ring_step = self.barrier_ring_step;
        }
        if m & DH_ARCHETYPE != 0 {
            s.archetype = self.archetype;
        }
        if m & DH_FLAGS != 0 {
            s.flags = self.flags;
        }
        if m & DH_BUILD_MARKS != 0 {
            s.build_marks = self.build_marks;
        }
        if m & DH_MARK != 0 {
            [s.mark_tier, s.brand, s.mark_tax] = self.mark;
        }
        s
    }

    /// The delta that turns `b` into `c` (their ids must be equal; the id is not carried).
    pub fn between(b: &WireSelf, c: &WireSelf) -> SelfDelta {
        let mut f = Fixed::<MAX_DELTA_HEAD>::new();
        let mask = write_self_delta(&mut f, b, c);
        // Only the fields in the mask mean anything (the others are zero, as a decoder reads them back).
        let keep = |bit: u32| mask & bit != 0;
        SelfDelta {
            mask,
            x: if keep(DH_X) { c.x } else { 0.0 },
            y: if keep(DH_Y) { c.y } else { 0.0 },
            facing: if keep(DH_FACING) { c.facing } else { 0.0 },
            hp: if keep(DH_HP) { c.hp } else { 0.0 },
            max_hp: if keep(DH_MAX_HP) { c.max_hp } else { 0.0 },
            kit: if keep(DH_KIT) {
                kit(c.faction, c.weapon)
            } else {
                0
            },
            ready_in_ms: if keep(DH_READY) { c.ready_in_ms } else { 0 },
            kills: if keep(DH_KILLS) { c.kills } else { 0 },
            bounty: if keep(DH_BOUNTY) { c.bounty } else { 0 },
            gv: if keep(DH_GV) { c.gv } else { 0 },
            move_seq: if keep(DH_MOVE_SEQ) { c.move_seq } else { 0 },
            status: if keep(DH_STATUS) { c.status } else { 0 },
            effects: if keep(DH_EFFECTS) { c.effects } else { 0 },
            displace_seq: if keep(DH_DISPLACE) { c.displace_seq } else { 0 },
            sig: if keep(DH_SIG) { c.sig } else { 0 },
            hunt_target: if keep(DH_HUNT_TARGET) {
                c.hunt_target
            } else {
                None
            },
            hunt_stacks: if keep(DH_HUNT_STACKS) {
                c.hunt_stacks
            } else {
                0
            },
            wish: if keep(DH_WISH) { Some(c.wish) } else { None },
            barrier: if keep(DH_BARRIER) { c.barrier } else { 0.0 },
            barrier_ring_step: if keep(DH_BARRIER) {
                c.barrier_ring_step
            } else {
                0
            },
            archetype: if keep(DH_ARCHETYPE) { c.archetype } else { 0 },
            flags: if keep(DH_FLAGS) { c.flags } else { 0 },
            build_marks: if keep(DH_BUILD_MARKS) {
                c.build_marks
            } else {
                0
            },
            mark: if keep(DH_MARK) {
                [c.mark_tier, c.brand, c.mark_tax]
            } else {
                [0; 3]
            },
        }
    }
}

/// One changed remote actor of a delta: `mask` (bits [`AD_X`]..[`AD_KIND`]) says which fields of `q` are carried; the others
/// are zero and mean nothing.
#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub struct ActorDelta {
    pub mask: u32,
    pub q: QActor,
}

impl ActorDelta {
    /// This delta applied to the actor `base` (a decoded actor).
    pub fn apply(&self, base: &WireActor) -> WireActor {
        let mut a = *base;
        if self.mask & AD_X != 0 {
            a.x = quant::dequantize_pos(self.q.x);
        }
        if self.mask & AD_Y != 0 {
            a.y = quant::dequantize_pos(self.q.y);
        }
        if self.mask & AD_FACING != 0 {
            a.facing = quant::dequantize_facing(self.q.facing);
        }
        if self.mask & AD_KIT != 0 {
            a.faction = self.q.kit & 0x0f;
            a.weapon = self.q.kit >> 4;
        }
        if self.mask & AD_HP != 0 {
            a.hp_pct = self.q.hp_pct;
        }
        if self.mask & AD_BOUNTY != 0 {
            a.bounty = self.q.bounty;
        }
        if self.mask & AD_STATUS != 0 {
            a.status = self.q.status;
        }
        if self.mask & AD_KIND != 0 {
            a.kind = self.q.kind;
        }
        if self.mask & AD_EFFECTS != 0 {
            a.effects = self.q.effects;
        }
        if self.mask & AD_DISPLACE != 0 {
            a.displace_seq = self.q.displace_seq;
        }
        if self.mask & AD_MOB_TYPE != 0 {
            a.mob_type_code = self.q.mob_type_code;
        }
        if self.mask & AD_SIG != 0 {
            a.sig = self.q.sig;
        }
        if self.mask & AD_BARRIER != 0 {
            a.barrier_pct = self.q.barrier_pct;
            a.barrier_ring_step = self.q.barrier_ring_step;
        }
        if self.mask & AD_ARCHETYPE != 0 {
            a.archetype = self.q.archetype;
        }
        if self.mask & AD_FLAGS != 0 {
            a.flags = self.q.flags;
        }
        if self.mask & AD_OFFHAND != 0 {
            a.offhand = self.q.offhand;
        }
        if self.mask & AD_BUILD_MARKS != 0 {
            a.build_marks = self.q.build_marks;
        }
        if self.mask & AD_MARK != 0 {
            [a.mark_tier, a.brand, a.mark_tax] = self.q.mark;
        }
        a
    }
}

/// A snapshot delta frame, as decoded (syntax checked; whether it applies is [`SnapshotTracker`]'s question).
#[derive(Clone, Debug, PartialEq)]
pub struct SnapshotDelta {
    pub tick: u32,
    /// This frame's absolute source clock; absent wire override decodes to `tick * 50`, never the base clock.
    pub server_time: f64,
    /// Protocol 11: the source's public training fact (delta head bit 20; static per connection).
    pub training: bool,
    /// The tick of the snapshot this one is relative to (`tick - base_age`, 1..=[`MAX_DELTA_AGE`] behind).
    pub base_tick: u32,
    /// `Some` when the phase differs from the base's.
    pub phase: Option<u8>,
    pub ack_input: u32,
    /// `Some` when the frame has a self (the base must have one too); the id is the base's.
    pub me: Option<SelfDelta>,
    /// Ascending by id; a new actor carries at least its position ([`AD_NEW`]), the rest starts at the defaults.
    pub changed: Vec<ActorDelta>,
    /// Ascending ids that were in the base and are gone.
    pub departed: Vec<u32>,
    /// `None` retains the group for a continuing owner; `Some(None)` explicitly marks it unavailable.
    pub owner_combat: Option<Option<WireOwnerCombat>>,
}

/// The delta that turns `base` into `cur`, as a message (a reference implementation: [`ConnEncoder`] writes the same bytes
/// without building it). `None` when `cur` cannot be a delta of `base` (the same conditions [`ConnEncoder`] sends a Full
/// frame for, except that it may be larger than the Full frame).
pub fn diff(base: &SnapshotData, cur: &SnapshotData) -> Option<SnapshotDelta> {
    let me = match (&base.me, &cur.me) {
        (_, None) => None,
        (Some(b), Some(c)) if b.id == c.id => Some(SelfDelta::between(b, c)),
        _ => return None,
    };
    if cur.tick <= base.tick || cur.tick - base.tick > MAX_DELTA_AGE {
        return None;
    }
    let (b, c) = (&base.actors, &cur.actors);
    let mut changed = Vec::new();
    for q in c {
        match b.binary_search_by_key(&q.id, |p| p.id) {
            Ok(i) => {
                let mask = actor_delta_mask(&b[i], q);
                if mask != 0 {
                    changed.push(ActorDelta {
                        mask,
                        q: mask_fields(q, mask),
                    });
                }
            }
            Err(_) => {
                let mask = new_actor_mask(q);
                changed.push(ActorDelta {
                    mask,
                    q: mask_fields(q, mask),
                })
            }
        }
    }
    let departed = b
        .iter()
        .filter(|p| c.binary_search_by_key(&p.id, |q| q.id).is_err())
        .map(|p| p.id)
        .collect();
    let owner_combat = owner_combat_delta(base, cur);
    Some(SnapshotDelta {
        tick: cur.tick,
        server_time: cur.server_time,
        training: cur.training,
        base_tick: base.tick,
        phase: (cur.phase != base.phase).then_some(cur.phase),
        ack_input: cur.ack_input,
        me,
        changed,
        departed,
        owner_combat,
    })
}

pub(super) fn owner_combat_bits_equal(
    a: Option<WireOwnerCombat>,
    b: Option<WireOwnerCombat>,
) -> bool {
    match (a, b) {
        (None, None) => true,
        (Some(a), Some(b)) => {
            a.lock == b.lock
                && a.draw_started_at == b.draw_started_at
                && a.ready_at == b.ready_at
                && a.as_milli.to_bits() == b.as_milli.to_bits()
                && a.windup_milli.to_bits() == b.windup_milli.to_bits()
                && a.speed_milli.to_bits() == b.speed_milli.to_bits()
        }
        _ => false,
    }
}

pub(super) fn owner_combat_delta(
    base: &SnapshotData,
    cur: &SnapshotData,
) -> Option<Option<WireOwnerCombat>> {
    // Owner loss clears retained owner-only state without spending a redundant option byte.
    cur.me.as_ref()?;
    if !owner_combat_bits_equal(base.owner_combat, cur.owner_combat) {
        return Some(cur.owner_combat);
    }
    let npc_refs_changed = base
        .actors
        .iter()
        .filter(|actor| actor.kind != 0)
        .map(|actor| actor.id)
        .ne(cur
            .actors
            .iter()
            .filter(|actor| actor.kind != 0)
            .map(|actor| actor.id));
    (npc_refs_changed && cur.owner_combat.is_some()).then_some(cur.owner_combat)
}

/// `q` with the fields outside `mask` zeroed (what a decoder reads back).
pub(super) fn mask_fields(q: &QActor, mask: u32) -> QActor {
    QActor {
        id: q.id,
        x: if mask & AD_X != 0 { q.x } else { 0 },
        y: if mask & AD_Y != 0 { q.y } else { 0 },
        facing: if mask & AD_FACING != 0 { q.facing } else { 0 },
        kit: if mask & AD_KIT != 0 { q.kit } else { 0 },
        hp_pct: if mask & AD_HP != 0 { q.hp_pct } else { 0 },
        status: if mask & AD_STATUS != 0 { q.status } else { 0 },
        bounty: if mask & AD_BOUNTY != 0 { q.bounty } else { 0 },
        kind: if mask & AD_KIND != 0 { q.kind } else { 0 },
        effects: if mask & AD_EFFECTS != 0 { q.effects } else { 0 },
        displace_seq: if mask & AD_DISPLACE != 0 {
            q.displace_seq
        } else {
            0
        },
        mob_type_code: if mask & AD_MOB_TYPE != 0 {
            q.mob_type_code
        } else {
            0
        },
        sig: if mask & AD_SIG != 0 { q.sig } else { 0 },
        barrier_pct: if mask & AD_BARRIER != 0 {
            q.barrier_pct
        } else {
            0
        },
        barrier_ring_step: if mask & AD_BARRIER != 0 {
            q.barrier_ring_step
        } else {
            0
        },
        archetype: if mask & AD_ARCHETYPE != 0 {
            q.archetype
        } else {
            0
        },
        flags: if mask & AD_FLAGS != 0 { q.flags } else { 0 },
        offhand: if mask & AD_OFFHAND != 0 { q.offhand } else { 0 },
        build_marks: if mask & AD_BUILD_MARKS != 0 {
            q.build_marks
        } else {
            0
        },
        mark: if mask & AD_MARK != 0 { q.mark } else { [0; 3] },
    }
}
