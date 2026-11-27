//! Offline receipt reader for a settled Bargain. It never submits a command.
use rh_policy::bots::raid::{percept::Me, quest::decide::stat_worth_at, values::Prices};
use rh_sim::mob_catalog::MobTypeId;
use rh_sim::modifiers::{farm::DEFINITION, Farm};
use rh_sim::state::Player;
use rh_sim::{FactionId, Sim, Vec2, WeaponId};
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
pub struct NearbyCamp {
    start_s: f64,
    camp: usize,
    distance: f64,
    paid_stat: &'static str,
    alive: u32,
}

pub fn nearby_camp(sim: &Sim, player: &Player, start_s: f64) -> Option<NearbyCamp> {
    let definitions = &sim.arena().map.layers()?.data().camps;
    let (camp, distance, roster) = sim
        .camp_rosters()
        .iter()
        .zip(definitions)
        .enumerate()
        .filter(|(_, (live, _))| live.elite.is_none() && live.alive > 0)
        .map(|(index, (live, sample))| {
            let distance = (player.body.x - sample.x).hypot(player.body.y - sample.y);
            (index, distance, live)
        })
        .min_by(|a, b| a.1.total_cmp(&b.1))?;
    let paid_mob = sim
        .camp_buffs()
        .get(camp)
        .copied()
        .flatten()
        .unwrap_or(roster.mob_type);
    let family = DEFINITION.rows.iter().find(|row| row.mob == paid_mob)?.stat;
    let (_, paid_stat) = FAMILIES.iter().find(|(representative, _)| {
        DEFINITION
            .rows
            .iter()
            .any(|row| row.mob == *representative && row.stat == family)
    })?;
    Some(NearbyCamp {
        start_s,
        camp,
        distance,
        paid_stat,
        alive: roster.alive,
    })
}

#[derive(Serialize)]
pub struct Receipt {
    age_s: f64,
    offer: u32,
    held_before: [Option<WeaponId>; 2],
    held_after: [Option<WeaponId>; 2],
    active_before: u8,
    changes: Vec<StatChange>,
    per_stack_policy_weights: [f64; 9],
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
    let per_stack_policy_weights = std::array::from_fn(|i| stat_worth_at(prices, i as u8, &me));
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
                per_stack_policy_weight: per_stack_policy_weights[index],
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
        per_stack_policy_weights,
        neutral_policy_weight: prices.neutral_stat,
        spare_weapon_policy_cost_ce: prices.spare_weapon,
    }
}
