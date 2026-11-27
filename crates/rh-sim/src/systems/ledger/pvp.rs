//! Player damage pace is a before-mitigation multiplier. It leaves the hit and DoT caps in the ledger.
use super::{weapon_hit, Entry};
use crate::constants::*;
use crate::factions::{PerformedAttack, SignatureKind};
use crate::fighter::FighterIx;
use crate::state::{Player, World};
use crate::statics::{CombatPace, MatchStatic};
use crate::{FactionId, WeaponId};

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

fn interval(attacker: &Player, weapon: WeaponId) -> f64 {
    let haste = crate::stat_fold::decode_mult(attacker.modifiers.attack_rate_wire());
    crate::content::attack_interval(&crate::content::faction(attacker.faction()), weapon, haste)
}

/// Bound weapon hits and the damage riders that can repeat with them. Native bonuses still apply to the capped base
/// contact, and the general hit and DoT limits still cover every source they did before.
pub(super) fn hit_cap_share(w: &World, entry: &Entry, performed: &[PerformedAttack]) -> f64 {
    if entry.ability.is_some() || entry.arena {
        return f64::INFINITY;
    }
    let Some(attacker) = entry.by.and_then(|id| w.player(id)) else {
        return f64::INFINITY;
    };
    if let Some(weapon) = entry.weapon.filter(|_| weapon_hit(entry)) {
        return RAID_PVP_WEAPON_CAP_HP_PER_SEC * interval(attacker, weapon);
    }
    if let Some(weapon) = entry
        .weapon
        .filter(|_| entry.sig == Some(SignatureKind::Mark))
    {
        return RAID_PVP_MARK_CAP_HP_PER_SEC * interval(attacker, weapon);
    }
    if entry.sig == Some(SignatureKind::Backlash) {
        // Backlash reacts to the target's performed weapon, not Fire's display weapon or a reflected projectile.
        // An input swap cancels the previous action, so both attacks at the two possible land sites use one weapon.
        let trigger = performed.iter().find(|attack| attack.by == entry.victim);
        if let Some((victim, trigger)) = w.player(entry.victim).zip(trigger) {
            return RAID_PVP_BACKLASH_CAP_HP_PER_SEC * interval(victim, trigger.weapon);
        }
    }
    f64::INFINITY
}

#[cfg(test)]
mod tests {
    use super::*;
    use crate::{validate, Placement, RawCommand, Secrets, Sim, SimConfig, Vec2};

    #[test]
    fn cadence_caps_follow_attack_and_trigger_weapons() {
        let mut sim = Sim::reset_active(
            SimConfig::default(),
            crate::arenas::practice_arena(),
            Secrets::from_seed(3),
            &[
                Placement {
                    profile: "fire".into(),
                    faction: FactionId::Fire,
                    bot: false,
                    at: Vec2::new(400., 320.),
                    hp: None,
                    weapon0: Some(WeaponId::Wand),
                    weapon1: Some(WeaponId::Sword),
                },
                Placement {
                    profile: "opponent".into(),
                    faction: FactionId::Holy,
                    bot: false,
                    at: Vec2::new(440., 320.),
                    hp: None,
                    weapon0: Some(WeaponId::Sword),
                    weapon1: None,
                },
            ],
        );
        let attacker = sim.players()[0].id;
        let victim = sim.players()[1].id;
        sim.submit(
            attacker,
            validate(RawCommand::Swap { slot: Some(1) }).unwrap(),
        )
        .unwrap();
        sim.step();
        assert_eq!(sim.players()[0].active_weapon(), WeaponId::Sword);
        let mut entry = Entry {
            terrain: false,
            dot: false,
            ability: None,
            arena: false,
            source: None,
            critical: false,
            critical_mult: None,
            sig: None,
            execute: false,
            victim,
            by: Some(attacker),
            packet: crate::state::Packet {
                raw: 1.,
                phys: 1.,
                magic: 0.,
                true_: 0.,
            },
            weapon: Some(WeaponId::Wand),
            stagger: false,
            projectile: Some(1),
            scale: 1.,
            derived: false,
            passive: false,
        };
        // An in-flight ordinary projectile keeps the timing of its weapon of origin after an offhand swap.
        assert_eq!(
            hit_cap_share(&sim.w, &entry, &[]),
            RAID_PVP_WEAPON_CAP_HP_PER_SEC * interval(&sim.players()[0], WeaponId::Wand)
        );
        entry.passive = true;
        assert!(hit_cap_share(&sim.w, &entry, &[]).is_infinite());
        entry.passive = false;
        entry.sig = Some(SignatureKind::Backlash);
        let attack = PerformedAttack {
            by: victim,
            weapon: WeaponId::Axe,
        };
        assert_eq!(
            hit_cap_share(&sim.w, &entry, &[attack]),
            RAID_PVP_BACKLASH_CAP_HP_PER_SEC * interval(&sim.players()[1], WeaponId::Axe)
        );
        entry.sig = Some(SignatureKind::Mark);
        assert_eq!(
            hit_cap_share(&sim.w, &entry, &[attack]),
            RAID_PVP_MARK_CAP_HP_PER_SEC * interval(&sim.players()[0], WeaponId::Wand)
        );
        entry.ability = Some(crate::status_runtime::AbilityId::Blaze);
        assert!(hit_cap_share(&sim.w, &entry, &[attack]).is_infinite());
        entry.ability = None;
        entry.arena = true;
        assert!(hit_cap_share(&sim.w, &entry, &[attack]).is_infinite());
    }
}
