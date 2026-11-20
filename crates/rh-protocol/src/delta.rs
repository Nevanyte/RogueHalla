//! Per-connection snapshot deltas (protocol v3, docs/adr/0010-snapshot-delta.md) with the v4 append-only varint masks
//! (docs/adr/0011-protocol-v4.md). Layout: the crate docs ([`crate`]).
//!
//! Three pieces, one per side of the socket:
//!
//! * [`SnapshotData`]: one viewer's snapshot as the room hands it to a connection: remote actors already quantized
//!   ([`QActor`]), the self at full precision, and a lower bound of its Full frame size (the exact size is computed only when
//!   needed). The room builds it once per tick from the observation ([`SnapshotData::build`], which refuses what the encoders always refused) and does not encode it.
//! * [`ConnEncoder`]: the connection's writer state. It remembers the last snapshot it wrote and encodes the next one as a
//!   Full frame (the v2 layout) or as a Delta against that base, whichever is smaller; a snapshot a slow client skipped never
//!   reaches it. Its memory is bounded by [`MAX_ACTORS`] (one base, two scratch lists).
//! * [`SnapshotTracker`]: a client's decoder. It holds the last snapshot it applied and REFUSES, with a [`DeltaError`], a delta
//!   that does not apply to it. It never clamps or repairs, and a refused delta changes nothing.
//!
//! Everything is lossless: the actors a client holds after any chain of frames equal the actors of a Full frame of the same
//! tick (quantized as always), and the self block is exact (bitwise, so -0.0 and NaN payloads survive).

use super::{
    actor_looks_bad, giver_actor_fault, head_len, head_word, id_field, kit, list, owner_combat_len,
    self_len, snapshot_fault, write_owner_combat, write_snapshot_head, Fixed, SnapshotHeader,
    MAX_ACTOR_LEN,
};
use crate::codec::{varint_len, DecodeError, EncodeError, Reader, Writer};
use crate::*;

#[path = "delta/actor.rs"]
mod actor;
#[path = "delta/diff.rs"]
mod diff;
#[path = "delta/encoder.rs"]
mod encoder;
#[path = "delta/read.rs"]
mod read;
#[path = "delta/snapshot_data.rs"]
mod snapshot_data;
#[path = "delta/tracker.rs"]
mod tracker;
#[path = "delta/write.rs"]
mod write;

pub use self::actor::QActor;
pub use self::diff::{diff, ActorDelta, SelfDelta, SnapshotDelta};
pub use self::encoder::{ConnEncoder, FrameKind};
pub(crate) use self::read::read_snapshot_delta;
pub use self::snapshot_data::{SnapshotData, SnapshotInner};
pub use self::tracker::{DeltaError, SnapshotTracker, TrackError};
pub(crate) use self::write::{message_check, message_len, write_message};
use self::{actor::*, diff::*, encoder::*, read::*};

/// Most ticks a delta's base may be behind it (a writer that skipped more sends a Full frame instead).
pub const MAX_DELTA_AGE: u32 = 64;

/// Fewest ticks between two Full frames that a resync request forced (20 ticks = 1 s at 20 Hz). A request that arrives sooner
/// is not dropped: it stays pending and the Full frame goes out when the interval ends, so a client that asks once per burst
/// of refusals always gets its Full frame, and a client that asks in a flood costs the writer one Full per interval.
pub const MIN_FORCED_FULL_TICKS: u32 = 20;

/// Delta head bits (protocol v4, append only). A varint: bits below 7 take one byte, so the fields that change nearly every
/// tick of a moving player sit there. Bit 0: a self follows (its fields are bits 1..=6 and 9..=14, data in bit order); bit 7: a
/// departed list follows the actors; bit 8: the phase changed (its byte follows the self).
pub const DH_PRESENT: u32 = 1 << 0;
pub const DH_X: u32 = 1 << 1;
pub const DH_Y: u32 = 1 << 2;
pub const DH_FACING: u32 = 1 << 3;
pub const DH_MOVE_SEQ: u32 = 1 << 4;
pub const DH_HP: u32 = 1 << 5;
pub const DH_READY: u32 = 1 << 6;
pub const DH_DEPARTED: u32 = 1 << 7;
pub const DH_PHASE: u32 = 1 << 8;
pub const DH_MAX_HP: u32 = 1 << 9;
pub const DH_KIT: u32 = 1 << 10;
pub const DH_KILLS: u32 = 1 << 11;
pub const DH_BOUNTY: u32 = 1 << 12;
pub const DH_GV: u32 = 1 << 13;
pub const DH_STATUS: u32 = 1 << 14;
/// The self field bits (a head without a self must have none of them).
pub const DH_EFFECTS: u32 = 1 << 15;
pub const DH_DISPLACE: u32 = 1 << 16;
pub const DH_SIG: u32 = 1 << 17;
pub const DH_HUNT_TARGET: u32 = 1 << 18;
pub const DH_HUNT_STACKS: u32 = 1 << 19;
pub const DH_WISH: u32 = 1 << 21;
pub const DH_BARRIER: u32 = 1 << 22;
pub const DH_SERVER_TIME: u32 = 1 << 23;
/// Owner-combat state changed; the field has a strict 0/1 availability tag.
pub const DH_OWNER_COMBAT: u32 = 1 << 24;
/// The self's hitbox archetype id changed (protocol 16): one byte after the barrier pair.
pub const DH_ARCHETYPE: u32 = 1 << 25;
/// The self's indicator facts changed (protocol 16, ADR0026): flags u8, build marks u8, mark plate (tier, brand, tax).
pub const DH_FLAGS: u32 = 1 << 26;
pub const DH_BUILD_MARKS: u32 = 1 << 27;
pub const DH_MARK: u32 = 1 << 28;
pub const DH_FIELDS: u32 = 0x7e7e
    | DH_FLAGS
    | DH_BUILD_MARKS
    | DH_MARK
    | DH_EFFECTS
    | DH_DISPLACE
    | DH_SIG
    | DH_HUNT_TARGET
    | DH_HUNT_STACKS
    | DH_WISH
    | DH_BARRIER
    | DH_ARCHETYPE;
pub const DH_KNOWN: u32 = DH_PRESENT
    | DH_FIELDS
    | DH_PHASE
    | DH_DEPARTED
    | DH_TRAINING
    | DH_SERVER_TIME
    | DH_OWNER_COMBAT;
/// Public training fact, independent of the self; present on every training delta.
pub const DH_TRAINING: u32 = 1 << 20;

/// Actor delta mask bits (append only): the fields that follow, in bit order.
pub const AD_X: u32 = 1 << 0;
pub const AD_Y: u32 = 1 << 1;
pub const AD_FACING: u32 = 1 << 2;
pub const AD_KIT: u32 = 1 << 3;
pub const AD_HP: u32 = 1 << 4;
pub const AD_BOUNTY: u32 = 1 << 5;
pub const AD_STATUS: u32 = 1 << 6;
pub const AD_KIND: u32 = 1 << 7;
/// Every field.
pub const AD_EFFECTS: u32 = 1 << 8;
pub const AD_DISPLACE: u32 = 1 << 9;
pub const AD_MOB_TYPE: u32 = 1 << 10;
pub const AD_SIG: u32 = 1 << 11;
/// Source barrier share and ring pair (protocol 12).
pub const AD_BARRIER: u32 = 1 << 12;
/// Hitbox archetype id (protocol 16). An entity's archetype never changes, so only a new actor carries it.
pub const AD_ARCHETYPE: u32 = 1 << 13;
/// Indicator facts (protocol 16, ADR0026): flags u8, off-hand u8, build marks u8, mark plate (tier, brand, tax) u8 x3.
pub const AD_FLAGS: u32 = 1 << 14;
pub const AD_OFFHAND: u32 = 1 << 15;
pub const AD_BUILD_MARKS: u32 = 1 << 16;
pub const AD_MARK: u32 = 1 << 17;
/// A remote player's walk hint (protocol 19): `walk_dir` u8, `walk_ticks` u8, sent when the pair differs from the base
/// (`(0, 0)` clears it).
pub const AD_WALK: u32 = 1 << 18;
pub const AD_ALL: u32 = 0x7_ffff;
/// What a new actor (one the base does not hold) must carry: its position. Every other field starts at its default.
pub const AD_NEW: u32 = AD_X | AD_Y | AD_FACING;

/// Fewest bytes a self takes after the head word (id, x y facing, a one-byte max_hp) and an actor takes in a Full frame (id, mask,
/// x y facing).
const MIN_SELF: usize = 1 + 12 + 1;
const MIN_ACTOR: usize = 1 + 1 + 6;

/// Largest self delta after the head: x, y, facing, hp, max_hp (4 each), kit, ready (2), kills, bounty, gv (3 each),
/// move_seq, status, phase, effects (2), displacement, sig.
const MAX_SELF_DELTA: usize =
    20 + 1 + 2 + 9 + 1 + 1 + 1 + 2 + 1 + 1 + 5 + 3 + 56 + 5 + 1 + 1 + 1 + 3;
/// Largest delta head: tag, tick (5), age, ack (5), head (1 placeholder), the self delta, the changed count (3).
const MAX_DELTA_HEAD: usize =
    1 + 5 + 1 + 5 + 5 + MAX_SELF_DELTA + 8 + 3 + 1 + 1 + 5 + 8 + 8 + 8 + 8;
/// Largest changed-actor record: id 5, mask 3, x/y/facing 6, kit/hp, bounty 3, status/kind, effects 2, displacement/mob type/sig,
/// barrier pair, archetype, flags, off-hand, build marks, mark plate 3, walk hint 2.
const MAX_ACTOR_DELTA: usize =
    5 + 3 + 6 + 1 + 1 + 3 + 1 + 1 + 2 + 1 + 1 + 1 + 2 + 1 + 1 + 1 + 1 + 3 + 2;

fn optional_f64_bits_equal(a: Option<f64>, b: Option<f64>) -> bool {
    match (a, b) {
        (None, None) => true,
        (Some(a), Some(b)) => a.to_bits() == b.to_bits(),
        _ => false,
    }
}

fn wish_bits_equal(a: Option<WireWishOwner>, b: Option<WireWishOwner>) -> bool {
    match (a, b) {
        (None, None) => true,
        (Some(a), Some(b)) => {
            a.ability == b.ability
                && a.ready_at.to_bits() == b.ready_at.to_bits()
                && optional_f64_bits_equal(a.full_at, b.full_at)
                && a.max_charges == b.max_charges
                && a.recharge_ms.to_bits() == b.recharge_ms.to_bits()
                && a.charges == b.charges
                && optional_f64_bits_equal(a.next_charge_at, b.next_charge_at)
                && match (a.cast, b.cast) {
                    (None, None) => true,
                    (Some(a), Some(b)) => {
                        a.ability == b.ability
                            && a.started_at.to_bits() == b.started_at.to_bits()
                            && a.lands_at.to_bits() == b.lands_at.to_bits()
                    }
                    _ => false,
                }
        }
        _ => false,
    }
}
