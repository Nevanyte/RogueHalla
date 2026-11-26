//! Player damage pace is a before-mitigation multiplier. It leaves the hit and DoT caps in the ledger.
use super::Entry;
use crate::constants::*;
use crate::fighter::FighterIx;
use crate::state::{Player, World};
use crate::statics::{CombatPace, MatchStatic};
use crate::FactionId;

// Both players in a matchup use the same product. The weights calibrate duel length while preserving the ratio
// between their outgoing factors. Each mirror fight uses the square of that faction's weight.
fn faction_weight(faction: FactionId) -> f64 {
    match faction {
        FactionId::Fire => RAID_PVP_FIRE_WEIGHT,
        FactionId::Sentenced => RAID_PVP_SENTENCED_WEIGHT,
        FactionId::Plague => RAID_PVP_PLAGUE_WEIGHT,
        FactionId::Frost => RAID_PVP_FROST_WEIGHT,
        FactionId::Holy => RAID_PVP_HOLY_WEIGHT,
    }
}

fn raid_factor(attacker: &Player) -> f64 {
    let farmed_share = attacker.farm.normal_stacks().min(RAID_PVP_FARMED_STACKS) as f64
        / RAID_PVP_FARMED_STACKS as f64;
    let power = attacker.build.power();
    let augment_bonus =
        RAID_PVP_CARD_BONUS_LIMIT * power / (power + RAID_PVP_CARD_POWER_HALF_BONUS);
    (RAID_PVP_STARTER_FACTOR + (RAID_PVP_FARMED_FACTOR - RAID_PVP_STARTER_FACTOR) * farmed_share)
        * (1. + augment_bonus)
}

pub(super) fn factor(
    k: &MatchStatic,
    w: &World,
    entry: &Entry,
    victim: Option<FighterIx>,
    raid_cache: &mut [Option<f64>],
) -> f64 {
    // Ability power is fixed in the source, and the arena and NPCs have their own damage rules.
    if !k.config.pvp_rules || entry.arena || entry.ability.is_some() {
        return 1.;
    }
    let Some(victim) = victim.filter(|&ix| w.is_player(ix)) else {
        return 1.;
    };
    let Some(attacker) = entry
        .by
        .and_then(|id| w.credited_player(id))
        .and_then(|id| w.idx(id))
    else {
        return 1.;
    };
    let player = &w.players[attacker];
    match k.config.combat_pace {
        CombatPace::Legacy => crate::content::pvp_factor(player.build.power()),
        CombatPace::Raid => {
            let build = *raid_cache[attacker].get_or_insert_with(|| raid_factor(player));
            let matchup = faction_weight(player.faction())
                * faction_weight(w.players[victim.slot()].faction());
            build * matchup
        }
    }
}
