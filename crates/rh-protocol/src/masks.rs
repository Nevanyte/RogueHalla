//! Field-mask bits (append only, ADR 0011), the default values a mask leaves out, and the code limits.

/// Full head bits (protocol v4; append only, see the crate docs). A head is a varint: bits below 7 take one byte.
pub(crate) const HM_PRESENT: u32 = 1 << 0;
pub(crate) const HM_KIT: u32 = 1 << 1;
pub(crate) const HM_HP: u32 = 1 << 2;
pub(crate) const HM_READY: u32 = 1 << 3;
pub(crate) const HM_KILLS: u32 = 1 << 4;
pub(crate) const HM_BOUNTY: u32 = 1 << 5;
pub(crate) const HM_MOVE_SEQ: u32 = 1 << 6;
pub(crate) const HM_GV: u32 = 1 << 7;
/// `max_hp` is written as an f32 (it is not an integer in 0..=65535); otherwise as a varint.
pub(crate) const HM_MAXHP_F32: u32 = 1 << 8;
pub(crate) const HM_STATUS: u32 = 1 << 9;
pub(crate) const HM_PHASE: u32 = 1 << 10;
/// Bits that belong to the self; a head without a self must have none of them set.
pub(crate) const HM_EFFECTS: u32 = 1 << 11;
pub(crate) const HM_DISPLACE: u32 = 1 << 12;
pub(crate) const HM_SIG: u32 = 1 << 13;
pub(crate) const HM_HUNT_TARGET: u32 = 1 << 14;
pub(crate) const HM_HUNT_STACKS: u32 = 1 << 15;
pub(crate) const HM_WISH: u32 = 1 << 17;
pub(crate) const HM_BARRIER: u32 = 1 << 18;
pub(crate) const HM_SERVER_TIME: u32 = 1 << 19;
/// Available owner-combat record follows the actor section of a Full snapshot.
pub(crate) const HM_OWNER_COMBAT: u32 = 1 << 20;
/// The self's hitbox archetype id (protocol 16): a byte after the barrier pair.
pub(crate) const HM_ARCHETYPE: u32 = 1 << 21;
/// Indicator facts of the self (protocol 16, ADR0026): flags u8, build marks u8, mark plate (tier, brand, tax) u8 x3.
pub(crate) const HM_FLAGS: u32 = 1 << 22;
pub(crate) const HM_BUILD_MARKS: u32 = 1 << 23;
pub(crate) const HM_MARK: u32 = 1 << 24;
pub(crate) const HM_SELF_BITS: u32 = 0x03ff
    | HM_FLAGS
    | HM_BUILD_MARKS
    | HM_MARK
    | HM_EFFECTS
    | HM_DISPLACE
    | HM_SIG
    | HM_HUNT_TARGET
    | HM_HUNT_STACKS
    | HM_WISH
    | HM_BARRIER
    | HM_ARCHETYPE;
pub(crate) const HM_KNOWN: u32 =
    HM_SELF_BITS | HM_PHASE | HM_TRAINING | HM_SERVER_TIME | HM_OWNER_COMBAT;
pub(crate) const HM_TRAINING: u32 = 1 << 16;
/// Full actor mask bits (append only): 0 kit, 1 hp, 2 status, 3 bounty, 4 kind.
pub(crate) const AM_KIT: u32 = 1 << 0;
pub(crate) const AM_HP: u32 = 1 << 1;
pub(crate) const AM_STATUS: u32 = 1 << 2;
pub(crate) const AM_BOUNTY: u32 = 1 << 3;
pub(crate) const AM_KIND: u32 = 1 << 4;
pub(crate) const AM_EFFECTS: u32 = 1 << 5;
pub(crate) const AM_DISPLACE: u32 = 1 << 6;
pub(crate) const AM_MOB_TYPE: u32 = 1 << 7;
pub(crate) const AM_SIG: u32 = 1 << 8;
pub(crate) const AM_BARRIER: u32 = 1 << 9;
/// Hitbox archetype id (protocol 16): one byte after the barrier pair.
pub(crate) const AM_ARCHETYPE: u32 = 1 << 10;
/// Indicator facts (protocol 16, ADR0026): flags u8, off-hand u8, build marks u8, mark plate (tier, brand, tax) u8 x3.
pub(crate) const AM_FLAGS: u32 = 1 << 11;
pub(crate) const AM_OFFHAND: u32 = 1 << 12;
pub(crate) const AM_BUILD_MARKS: u32 = 1 << 13;
pub(crate) const AM_MARK: u32 = 1 << 14;
/// A remote player's walk hint (protocol 19): `walk_dir` u8, `walk_ticks` u8 after the mark plate. Set only while
/// `walk_ticks != 0`; absent decodes to `(0, 0)`.
pub(crate) const AM_WALK: u32 = 1 << 15;
pub(crate) const AM_KNOWN: u32 = AM_ARCHETYPE
    | AM_WALK
    | AM_FLAGS
    | AM_OFFHAND
    | AM_BUILD_MARKS
    | AM_MARK
    | AM_KIT
    | AM_HP
    | AM_STATUS
    | AM_BOUNTY
    | AM_KIND
    | AM_EFFECTS
    | AM_DISPLACE
    | AM_MOB_TYPE
    | AM_SIG
    | AM_BARRIER;
/// The hp a clear hp bit stands for.
pub(crate) const DEFAULT_HP_PCT: u8 = 100;
pub(crate) const DEFAULT_BARRIER_PCT: u8 = 0;
pub(crate) const DEFAULT_BARRIER_RING_STEP: u8 = 0;
/// The status a clear status bit stands for: alive (0 lobby, 1 alive, 2 dead, 3 mia: docs/CONTRACTS.md C-NET).
pub const DEFAULT_STATUS: u8 = 1;
/// The phase a clear phase bit stands for: active (0 lobby, 1 active, 2 ended).
pub const DEFAULT_PHASE: u8 = 1;
/// Largest faction, weapon (4 bits each in the kit byte), status and phase (2 bits each) the wire can carry.
pub const MAX_KIT_CODE: u8 = 15;
pub const MAX_STATUS_CODE: u8 = 3;
pub const MAX_PHASE_CODE: u8 = 3;
