//! Checkpoint validation for regional camp-buff assignments.
use super::{MatchStatic, World};

/// P-CAMP-BUFF-THEMES-1: the camp buff row's shape is tied to the hashed config (`MobRuntime::buffs` folds no word
/// when empty, see `hash.rs`): empty under `CampBuffRule::Legacy`, exactly one entry per camp under `Themed`. A buff,
/// where set, is another zone's normal type paying a stat off the camp's zone's theme (legacy a7b5c4ce `spillType`).
pub(super) fn check_camp_buffs(k: &MatchStatic, w: &World) -> Result<(), String> {
    use crate::mob_catalog::camp_buffs::{stat_index, zone_stat_types};
    let Some(rt) = w.mob_runtime.as_ref() else {
        return Ok(());
    };
    let buffs = &rt.buffs.0;
    match k.config.camp_buffs {
        crate::CampBuffRule::Legacy if buffs.is_empty() => Ok(()),
        crate::CampBuffRule::Legacy => Err("a Legacy camp buff rule carries no buff row".into()),
        crate::CampBuffRule::Themed | crate::CampBuffRule::ThemedFocus => {
            if buffs.len() != rt.camps.len() {
                return Err(format!(
                    "{} camp buffs for {} camps: one per camp under Themed",
                    buffs.len(),
                    rt.camps.len()
                ));
            }
            for (i, buff) in buffs.iter().enumerate() {
                let Some(t) = *buff else { continue };
                let zone = k
                    .arena
                    .map
                    .layers()
                    .and_then(|l| l.data().camps.get(i))
                    .and_then(|c| crate::FactionId::parse(c.zone.as_str()));
                let def = crate::mob_catalog::mob_type_by_id(t.as_str());
                let off_theme = match (zone, def, stat_index(t)) {
                    (Some(zone), Some(def), Some(b)) => {
                        def.zone != zone && zone_stat_types(zone)[b].is_none()
                    }
                    _ => false,
                };
                if !off_theme {
                    return Err(format!(
                        "camp {i}: buff {} is not another zone's normal type paying a stat off the camp's theme",
                        t.as_str()
                    ));
                }
            }
            Ok(())
        }
    }
}
