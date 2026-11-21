//! Which living actors can contribute Frost chill, and how strong their chill is.
use super::*;

pub(super) fn returning(w: &World, id: PlayerId) -> bool {
    w.mobs.get(id).is_some_and(|m| m.returning())
}
pub(super) fn structural_source(w: &World, ix: crate::fighter::FighterIx) -> bool {
    let core = w.core(ix);
    core.vitals.faction == FactionId::Frost
        && !returning(w, core.id)
        && (w.is_player(ix)
            || w.mobs.get(core.id).is_some_and(|m| {
                m.kind != crate::mob_runtime::MobKind::Dummy
                    && m.zombie.is_none()
                    && m.mob_type
                        .and_then(|t| crate::mob_catalog::mob_type_by_id(t.as_str()))
                        .is_some_and(|t| t.zone == FactionId::Frost)
            }))
}
pub(super) fn source_chill(w: &World, ix: crate::fighter::FighterIx) -> f64 {
    if w.is_player(ix) {
        crate::modifiers::frost_chill(w.core(ix))
    } else {
        1. - FROST_SLOW
    }
}
