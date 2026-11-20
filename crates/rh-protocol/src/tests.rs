//! The mask bit numbers are pinned: they are append only.

use super::*;

/// The Full actor mask bit numbers are append only (docs/adr/0011-protocol-v4.md).
#[test]
fn full_actor_mask_bit_numbers_are_pinned() {
    assert_eq!(
        [AM_KIT, AM_HP, AM_STATUS, AM_BOUNTY, AM_KIND],
        [1 << 0, 1 << 1, 1 << 2, 1 << 3, 1 << 4]
    );
    // Protocol 19: the walk hint.
    assert_eq!(AM_WALK, 1 << 15);
}
