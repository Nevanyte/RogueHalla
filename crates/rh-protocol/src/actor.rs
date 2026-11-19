//! The Snapshot's public records: the owner's self, combat and Wish groups, actors, points, and the indicator and kind limits.

use super::*;

#[derive(Clone, Copy, Debug, PartialEq, Default)]
pub struct WireSelf {
    pub id: u32,
    pub faction: u8,
    pub x: f32,
    pub y: f32,
    pub facing: f32,
    pub hp: f32,
    pub max_hp: f32,
    pub status: u8,
    pub weapon: u8,
    pub ready_in_ms: u16,
    pub kills: u16,
    pub bounty: u16,
    pub gv: u16,
    pub move_seq: u8,
    pub effects: u16,
    pub displace_seq: u8,
    pub sig: u8,
    /// The selected Sentenced rival; absent is `None` (never actor 0).
    pub hunt_target: Option<u32>,
    pub hunt_stacks: u16,
    /// The owner-only Wish display record. Full snapshots omit it when absent; deltas can explicitly clear it.
    pub wish: Option<WireWishOwner>,
    /// The owner's source-f32 barrier value and its source-derived eight-step ring.
    pub barrier: f32,
    pub barrier_ring_step: u8,
    /// The own hitbox archetype id (protocol 16, ADR0024); the row is in the Welcome's archetype table.
    pub archetype: u8,
    /// The own indicator flags (protocol 16, ADR0026): [`ACTOR_FLAG_WISH_READY`], [`ACTOR_FLAG_TELEPORT_IMMUNE`],
    /// [`ACTOR_FLAG_ROAM_READY`] (a self is never [`ACTOR_FLAG_ENGAGED`]).
    pub flags: u8,
    /// The own overhead build marks, packed as [`WireActor::build_marks`].
    pub build_marks: u8,
    /// The own Sentenced mark plate: tier 0..=4, brand 0..=100, mark tax percent 0..=100 (as on an actor).
    pub mark_tier: u8,
    pub brand: u8,
    pub mark_tax: u8,
}

/// Indicator flag bits (protocol 16, ADR0026), on actors and the self. Verdicts only, never a clock.
/// The player's Wish is ready (legacy `wishReadyAt` passed, outside the lobby).
pub const ACTOR_FLAG_WISH_READY: u8 = 1 << 0;
/// A regional mob is fighting: its attack cooldown runs or its bow is drawn (legacy MobView `fighting`).
pub const ACTOR_FLAG_ENGAGED: u8 = 1 << 1;
/// The teleport-immunity ring is up (at most 1.5 s of immunity left, legacy `immuneUntil`).
pub const ACTOR_FLAG_TELEPORT_IMMUNE: u8 = 1 << 2;
/// Fire's charged roaming burst is armed (legacy `roamReady`).
pub const ACTOR_FLAG_ROAM_READY: u8 = 1 << 3;
/// Every flag bit a decoder accepts; any other bit is refused.
pub const ACTOR_FLAGS_KNOWN: u8 = 0x0f;
/// Largest off-hand code: 0 an empty second slot, else the weapon code + 1.
pub const MAX_OFFHAND_CODE: u8 = MAX_KIT_CODE + 1;
/// Largest build-mark stat key: 0 none, 1..=9 = ad, ap, damage, health, attack speed, armor, mr, speed, leech.
pub const MAX_STAT_KEY: u8 = 9;
/// Largest Sentenced mark tier.
pub const MAX_MARK_TIER: u8 = 4;

/// A packed build-mark byte is valid: two keys 0..=9 (first in the low nibble), the second only after a first, never equal.
pub fn build_marks_valid(v: u8) -> bool {
    let (first, second) = (v & 0x0f, v >> 4);
    first <= MAX_STAT_KEY
        && second <= MAX_STAT_KEY
        && (second == 0 || (first != 0 && first != second))
}

/// The first reason a decoder refuses these indicator facts (ADR0026), or None. `mark` is (tier, brand, tax).
pub fn indicator_fault(
    offhand: u8,
    build_marks: u8,
    mark: [u8; 3],
    flags: u8,
) -> Option<&'static str> {
    if offhand > MAX_OFFHAND_CODE {
        Some("offhand")
    } else if !build_marks_valid(build_marks) {
        Some("build marks")
    } else if mark[0] > MAX_MARK_TIER || mark[1] > 100 || mark[2] > 100 {
        Some("mark")
    } else if flags & !ACTOR_FLAGS_KNOWN != 0 {
        Some("flags")
    } else {
        None
    }
}

/// Signature and ability hit kinds of [`WireFx::SigHit`] (protocol 16, ADR0026): the signature kinds, the bleed (reserved:
/// the sim emits no bleed chunks yet), and `HIT_KIND_ABILITY + ability code` (Freeze 0 .. Last Stand 4).
pub const HIT_KIND_BURN: u8 = 1;
pub const HIT_KIND_DISEASE: u8 = 2;
pub const HIT_KIND_BACKLASH: u8 = 3;
pub const HIT_KIND_MARK: u8 = 4;
pub const HIT_KIND_BLEED: u8 = 5;
pub const HIT_KIND_ABILITY: u8 = 16;

/// A [`WireFx::SigHit`] kind a decoder accepts.
pub fn hit_kind_valid(kind: u8) -> bool {
    (HIT_KIND_BURN..=HIT_KIND_BLEED).contains(&kind)
        || (HIT_KIND_ABILITY..=HIT_KIND_ABILITY + 4).contains(&kind)
}

/// Owner-only action lock state. This is display data, not a capability to target or act.
#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub enum WireOwnerLock {
    Clear,
    Held { target: Option<u32> },
}

/// Owner-only canonical combat clock and modifier mirrors.
#[derive(Clone, Copy, Debug, PartialEq)]
pub struct WireOwnerCombat {
    pub lock: WireOwnerLock,
    pub draw_started_at: u32,
    pub ready_at: u32,
    pub as_milli: f64,
    pub windup_milli: f64,
    pub speed_milli: f64,
}

impl WireOwnerCombat {
    pub(crate) fn valid(&self) -> bool {
        [self.as_milli, self.windup_milli, self.speed_milli]
            .into_iter()
            .all(|v| v.is_finite() && v >= 50.0 && v.fract() == 0.0)
            && match self.lock {
                WireOwnerLock::Clear | WireOwnerLock::Held { target: None } => true,
                WireOwnerLock::Held { target: Some(id) } => id != 0,
            }
    }
}

/// Shared owner-combat structural check for snapshot builders and connection adapters.
/// `target_present` is consulted only for a known lock target.
pub fn owner_combat_fault(
    owner: Option<&WireOwnerCombat>,
    owner_present: bool,
    target_present: impl FnOnce(u32) -> bool,
) -> Option<&'static str> {
    let owner = owner?;
    if !owner_present || !owner.valid() {
        return Some("owner combat");
    }
    if let WireOwnerLock::Held { target: Some(id) } = owner.lock {
        if !target_present(id) {
            return Some("owner combat");
        }
    }
    None
}

/// Owner-only, already-authoritative Wish display facts (ADR0022). This carries no target, cursor, rank, or outcome.
#[derive(Clone, Copy, Debug, PartialEq)]
pub struct WireWishOwner {
    pub ability: u8,
    pub ready_at: f64,
    pub full_at: Option<f64>,
    pub max_charges: u8,
    pub recharge_ms: f64,
    pub charges: u8,
    pub next_charge_at: Option<f64>,
    pub cast: Option<WireWishCast>,
}

/// The owner's current cast cue deadlines.
#[derive(Clone, Copy, Debug, PartialEq)]
pub struct WireWishCast {
    pub ability: u8,
    pub started_at: f64,
    pub lands_at: f64,
}

/// A public, quantized point carried by a Wish cue.
#[derive(Clone, Copy, Debug, PartialEq)]
pub struct WirePoint {
    pub x: f32,
    pub y: f32,
}

/// A remote actor as the observation chose it. On the wire its position and facing are quantized ([`quant`]): a decoded
/// actor equals [`WireActor::quantized`] of what was encoded. `hp_pct` is 0..=100 (the observation clamps it).
#[derive(Clone, Copy, Debug, PartialEq, Default)]
pub struct WireActor {
    pub id: u32,
    pub faction: u8,
    pub x: f32,
    pub y: f32,
    pub facing: f32,
    /// hp fraction in hundredths (0..=100), what an hp bar shows.
    pub hp_pct: u8,
    pub status: u8,
    pub weapon: u8,
    pub bounty: u16,
    /// What the entity is: 0 = player (the default, which costs no byte). Kind 4 is a QuestGiver and requires the actor-only
    /// packed kit 0xff (Neutral/None) with mob_type_code 0. Other kinds remain opaque/extensible and keep their full kit range;
    /// the id namespace is shared by every kind.
    pub kind: u8,
    pub effects: u16,
    pub displace_seq: u8,
    pub sig: u8,
    pub mob_type_code: u8,
    /// Source-derived twentieth share rounded to an integer percent.
    pub barrier_pct: u8,
    /// Source-derived barrier ring step in 0..=8.
    pub barrier_ring_step: u8,
    /// Hitbox archetype id (protocol 16, ADR0024); 0 is the legacy circle. The row is in the Welcome's archetype table.
    pub archetype: u8,
    /// Indicator flags (protocol 16, ADR0026): `ACTOR_FLAG_*` bits.
    pub flags: u8,
    /// The weapon in a player's second slot, the one not in hand: 0 empty (legacy draws fists), else weapon code + 1.
    pub offhand: u8,
    /// A player's top two invested stats (legacy build marks): first key in the low nibble, second in the high one, each
    /// 0 none or 1..=9 (ad, ap, damage, health, attack speed, armor, mr, speed, leech). Keys only, never scores.
    pub build_marks: u8,
    /// Sentenced mark plate: tier 0..=4, brand 0..=100, mark tax percent 0..=100 (all zero when unmarked).
    pub mark_tier: u8,
    pub brand: u8,
    pub mark_tax: u8,
}

impl WireActor {
    /// STUB (P-NET-DRAWAHEAD-1a replaces it with the fields `walk_dir`, `walk_ticks`): a remote player's walk hint (protocol
    /// 19), the heading of the walk the server will continue in 256ths of a turn (0 whenever the ticks are 0) and the whole
    /// ticks it is committed to. Never set on a mob.
    pub fn walk_hint(&self) -> (u8, u8) {
        (0, 0)
    }

    /// What a client decodes after this actor went over the wire (positions to 1/8 u, facing to 2 pi / 65536).
    pub fn quantized(&self) -> WireActor {
        WireActor {
            x: quant::dequantize_pos(quant::quantize_pos(self.x)),
            y: quant::dequantize_pos(quant::quantize_pos(self.y)),
            facing: quant::dequantize_facing(quant::quantize_facing(self.facing)),
            ..*self
        }
    }
}

pub(super) fn quantized_point(point: Option<WirePoint>) -> Option<WirePoint> {
    point.map(|p| WirePoint {
        x: quant::dequantize_pos(quant::quantize_pos(p.x)),
        y: quant::dequantize_pos(quant::quantize_pos(p.y)),
    })
}

pub(super) fn read_point_opt(r: &mut Reader) -> Result<Option<WirePoint>, DecodeError> {
    r.opt(|r| {
        Ok(WirePoint {
            x: quant::dequantize_pos(r.u16()?),
            y: quant::dequantize_pos(r.u16()?),
        })
    })
}

pub(super) fn point_valid(point: Option<WirePoint>) -> bool {
    point.is_none_or(|p| quant::pos_in_range(p.x) && quant::pos_in_range(p.y))
}

pub(super) fn nonzero_varint(r: &mut Reader, what: &'static str) -> Result<u32, DecodeError> {
    match r.varint()? {
        0 => Err(DecodeError::BadField(what)),
        id => Ok(id),
    }
}
