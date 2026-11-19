//! P-COLISEUM-HEALS-1 STUB: the four heal pickups at the centre of the grand coliseum (owner request 2026-11-19). On a
//! map whose coliseum is the grand one, four `ContainerKind::Heal` containers stand on the four floor tiles around the
//! coliseum centre; a standing, damaged player with no potion heal running who touches an available one starts the
//! Large potion's heal over time (`potions::DEFINITION`) and the pickup returns after [`DEFINITION`]`.respawn_ms`.
//! Legacy has no such pickup (packet brief, proposed DEVIATIONS row).

// STUB: read by the implementation (P-COLISEUM-HEALS-1); until then only by its tests.
#[cfg_attr(not(test), allow(dead_code))]
/// The pickups' rule numbers. Not folded into `content_hash` until the owner decides (packet brief).
pub(crate) struct Definition {
    /// How long a taken pickup stays unavailable (the owner's dial).
    pub(crate) respawn_ms: f64,
    /// Centre-to-pickup distance at which a body touches it (the 12 u legacy body plus 8 u).
    pub(crate) reach: f64,
}

#[cfg_attr(not(test), allow(dead_code))]
pub(crate) const DEFINITION: Definition = Definition {
    respawn_ms: 30_000.,
    reach: 20.,
};

/// The four pickup positions (NW, NE, SW, SE: the coliseum centre plus or minus half a tile) when the arena's coliseum is
/// the grand one, else `None`. STUB: always `None`.
#[cfg_attr(not(test), allow(dead_code))]
pub(crate) fn spots(_arena: &crate::Arena) -> Option<[crate::Vec2; 4]> {
    None
}

#[cfg(test)]
#[path = "heal_pickups_tests.rs"]
mod tests;
