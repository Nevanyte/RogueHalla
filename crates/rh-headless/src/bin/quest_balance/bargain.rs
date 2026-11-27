//! Offline receipt reader for a settled Bargain. It never submits a command.
use rh_policy::bots::raid::{percept::Me, quest::decide::stat_worth_at, values::Prices};
use rh_sim::mob_catalog::MobTypeId;
use rh_sim::modifiers::{farm::DEFINITION, Farm};
use rh_sim::state::Player;
use rh_sim::{FactionId, Vec2, WeaponId};
use serde::Serialize;

const FAMILIES: [(MobTypeId, &str); 9] = [
    (MobTypeId::EmberImp, "damage"),
    (MobTypeId::CinderHound, "movement"),
    (MobTypeId::MagmaBrute, "max_hp"),
    (MobTypeId::FlameCaller, "ability_power"),
    (MobTypeId::Bailiff, "armor"),
    (MobTypeId::ChainWarden, "shield"),
    (MobTypeId::Pardoner, "attack_damage"),
    (MobTypeId::PlagueRat, "attack_speed"),
    (MobTypeId::RotMonk, "lifesteal"),
];

#[derive(Clone, Copy)]
pub struct Snapshot {
    farm: Farm,
    faction: Option<FactionId>,
    weapons: [Option<WeaponId>; 2],
    slot: u8,
}

impl Snapshot {
    pub fn read(player: &Player) -> Self {
        Self {
            farm: player.farm,
            faction: player.vitals.faction.faction_id(),
            weapons: [player.weapon.weapon0, player.weapon.weapon1],
            slot: player.weapon.slot,
        }
    }
}

#[derive(Serialize)]
pub struct StatChange {
    stat: &'static str,
    stack_change: i64,
    per_stack_policy_weight: f64,
}

#[derive(Serialize)]
pub struct Receipt {
    age_s: f64,
    offer: u32,
    held_before: [Option<WeaponId>; 2],
    held_after: [Option<WeaponId>; 2],
    active_before: u8,
    changes: Vec<StatChange>,
    neutral_policy_weight: f64,
    spare_weapon_policy_cost_ce: f64,
}

pub fn receipt(before: Snapshot, after: Snapshot, age_s: f64, offer: u32) -> Receipt {
    // Only faction, active slot and held weapon are consulted by the shared stat-fit function.
    let me = Me {
        at: Vec2::new(0., 0.),
        hp: 0.,
        max_hp: 0.,
        alive: true,
        faction: before.faction,
        weapons: before.weapons,
        slot: before.slot,
        effects: 0,
        ready_at: 0.,
        potion_small: 0,
        potion_large: 0,
        potions_healing: 0,
        healing_until: None,
    };
    let prices = &Prices::LOCAL;
    let changes = FAMILIES
        .into_iter()
        .enumerate()
        .filter_map(|(index, (representative, stat))| {
            let family = DEFINITION
                .rows
                .iter()
                .find(|row| row.mob == representative)?
                .stat;
            let stack_change: i64 = DEFINITION
                .rows
                .iter()
                .filter(|row| row.stat == family)
                .map(|row| after.farm.count(row.mob) as i64 - before.farm.count(row.mob) as i64)
                .sum();
            (stack_change != 0).then(|| StatChange {
                stat,
                stack_change,
                per_stack_policy_weight: stat_worth_at(prices, index as u8, &me),
            })
        })
        .collect();
    Receipt {
        age_s,
        offer,
        held_before: before.weapons,
        held_after: after.weapons,
        active_before: before.slot,
        changes,
        neutral_policy_weight: prices.neutral_stat,
        spare_weapon_policy_cost_ce: prices.spare_weapon,
    }
}
