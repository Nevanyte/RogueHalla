//! Every Top 25 quest number in one table, `TOP25`: objectives, reward cooldowns and sizes, spell effects, deployments,
//! Rebirth Egg and WallBuster. Each `visit_rules` folds its fields in the word order of the consts they replaced, so
//! moving a number here does not move the content hash; changing a folded one does. Not every field is folded today:
//! 39 of the 91 fields (the inline literals they replaced were never folded: the route deadline, most spell ranges,
//! widths, pulls and durations, the deployment hp, caps, reveals and turret share; the "not folded" rows of
//! `docs/backlog/packets/P-BAL-Q25-SEAM-1.md`) reach no `visit_rules`, so retuning one of them moves no content hash
//! until a later packet folds them. Route timers and objective counts stay in the `def!` rows of `catalog.rs`.
use crate::constants::{QUEST_TURN_IN_RANGE, TILE};

/// The Top 25 tuning table. Times in ms, distances in world units, shares as fractions.
pub(crate) struct Top25Tuning {
    // Objectives and attempts.
    pub(crate) fort_ms: f64,
    pub(crate) meditate_ms: f64,
    /// The Burning Crown's burn: share of the carrier's max hp dealt as true terrain damage once per whole second
    /// carried (paused while the crown is turned in and waiting for its claim). Folded.
    pub(crate) crown_burn_rate: f64,
    /// Most Crown Power stacks (EmberImp): one per whole second the crown is carried, capped here. Also the restore
    /// bound on a turned-in crown's points. Folded.
    pub(crate) crown_power_cap: u8,
    pub(crate) contact_range: f64,
    pub(crate) fort_range: f64,
    pub(crate) brush_reveal_range: f64,
    pub(crate) giver_flee_ms: f64,
    pub(crate) quest_respawn_ms: f64,
    pub(crate) warden_hp_multiplier: f64,
    pub(crate) bargain_cost: u64,
    /// Route deadline floor: `length * route_pace / PLAYER_SPEED + route_slack_ms` (1.5x the walk plus slack): ms of
    /// deadline per second the route takes at `PLAYER_SPEED` (u/s), so 1000 is exactly the walk. Not folded.
    pub(crate) route_pace: f64,
    pub(crate) route_slack_ms: f64,
    // Reusable spell cooldowns and charges.
    pub(crate) hook_cooldown_ms: f64,
    pub(crate) shoulder_charge_cooldown_ms: f64,
    pub(crate) tornado_cooldown_ms: f64,
    pub(crate) turret_cooldown_ms: f64,
    pub(crate) smite_cooldown_ms: f64,
    pub(crate) spell_shield_cooldown_ms: f64,
    pub(crate) lantern_ward_cooldown_ms: f64,
    pub(crate) cleanse_cooldown_ms: f64,
    pub(crate) scout_falcon_cooldown_ms: f64,
    pub(crate) bolas_cooldown_ms: f64,
    pub(crate) lantern_ward_charges: u8,
    // Permanent rewards.
    pub(crate) trade_up_stacks: [u64; 3],
    pub(crate) bargain_stacks: u64,
    pub(crate) feed_damage_stacks: u64,
    pub(crate) chef_bonus_share: f64,
    pub(crate) helm_bypass: f64,
    // Spell effects.
    pub(crate) stasis_ms: f64,
    pub(crate) shield_ms: f64,
    pub(crate) warhorn_ms: f64,
    /// Tornado: ms from the cast until it lands. Also how long the Cleansed buff of a Cleanse lasts (a Cleansed fighter
    /// ignores Tornado pulls) and that buff's restore bound. Folded.
    pub(crate) tornado_windup_ms: f64,
    pub(crate) tornado_radius: f64,
    /// Hook and Tornado pulls: world units a pulled fighter moves toward its pull point per legacy 50 ms tick (scaled
    /// by `per_step` at other step rates). Folded.
    pub(crate) pull_tick_distance: f64,
    pub(crate) smite_range: f64,
    pub(crate) swap_range: f64,
    pub(crate) hook_range: f64,
    pub(crate) hook_width: f64,
    pub(crate) bolas_range: f64,
    pub(crate) bolas_width: f64,
    pub(crate) blind_rage_attack_rate: f64,
    pub(crate) warhorn_damage_per_taunt: f64,
    pub(crate) warhorn_guard_per_taunt: f64,
    pub(crate) warhorn_guard_floor: f64,
    pub(crate) tornado_cast_range: f64,
    pub(crate) warhorn_radius: f64,
    pub(crate) smite_damage: f64,
    pub(crate) swap_ms: f64,
    pub(crate) blind_rage_ms: f64,
    pub(crate) still_mind_ms: f64,
    pub(crate) charge_range: f64,
    pub(crate) charge_width: f64,
    pub(crate) charge_knockback: f64,
    pub(crate) charge_stun_ms: f64,
    /// Hook and Bolas flight, per legacy 50 ms tick (scaled by `per_step`).
    pub(crate) shot_speed: f64,
    pub(crate) hook_stop_distance: f64,
    pub(crate) hook_pull_ms: f64,
    pub(crate) bolas_root_ms: f64,
    /// Tornado: the longest pull, in world units toward its centre, scaled by
    /// `clamp(caster max hp / target max hp - 1, 0, 1)` (full at twice the target's max hp; a turret's max hp is
    /// `turret_hp`). Not folded.
    pub(crate) tornado_pull: f64,
    pub(crate) tornado_pull_ms: f64,
    pub(crate) taunt_stop_distance: f64,
    // Cage and deployments.
    pub(crate) cage_radius: f64,
    pub(crate) cage_ms: f64,
    pub(crate) falcon_speed_per_ms: f64,
    /// Radians either side of the caster's facing left open in the Warden's Cage ring.
    pub(crate) cage_gap_half_angle: f64,
    pub(crate) turret_ms: f64,
    pub(crate) turret_interval_ms: f64,
    pub(crate) turret_range: f64,
    pub(crate) turret_reward_range: f64,
    pub(crate) ward_ms: f64,
    pub(crate) falcon_ms: f64,
    pub(crate) cage_cast_range: f64,
    pub(crate) deploy_range: f64,
    /// Turret: hit points at placement and their restore bound; also the turret's max hp in the Tornado pull scale.
    /// Not folded.
    pub(crate) turret_hp: f64,
    pub(crate) ward_hp: f64,
    pub(crate) turret_cap: usize,
    pub(crate) ward_cap: usize,
    /// Scout Falcons per owner: a restore bound only (the deployment validator). A cast always replaces the owner's
    /// previous falcon, so play keeps at most one, and a value above 1 would admit restores play cannot produce.
    /// Not folded.
    pub(crate) falcon_cap: usize,
    pub(crate) falcon_range: f64,
    pub(crate) turret_reveal: f64,
    pub(crate) ward_reveal: f64,
    pub(crate) falcon_reveal: f64,
    /// Turret: share of the owner's own weapon damage per second that the turret deals (each shot is this share of one
    /// weapon hit's raw damage, times the shot interval over the owner's attack interval). Not folded.
    pub(crate) turret_damage_share: f64,
    // Rebirth Egg.
    pub(crate) egg_safe_distance: f64,
    pub(crate) egg_sight_radius: f64,
    pub(crate) egg_hatch_ms: f64,
    pub(crate) egg_hatch_hp_share: f64,
    pub(crate) egg_min_standing: usize,
    // WallBuster.
    pub(crate) wall_radius: f64,
    pub(crate) wall_anchor_margin: f64,
}

pub(crate) static TOP25: Top25Tuning = Top25Tuning {
    fort_ms: 35_000.,
    meditate_ms: 30_000.,
    crown_burn_rate: 0.01,
    crown_power_cap: 48,
    contact_range: QUEST_TURN_IN_RANGE,
    fort_range: 72.,
    brush_reveal_range: 112.,
    giver_flee_ms: 60_000.,
    quest_respawn_ms: 25_000.,
    warden_hp_multiplier: 3.,
    bargain_cost: 20,
    route_pace: 1500.,
    route_slack_ms: 15_000.,
    hook_cooldown_ms: 45_000.,
    shoulder_charge_cooldown_ms: 12_000.,
    tornado_cooldown_ms: 14_000.,
    turret_cooldown_ms: 30_000.,
    smite_cooldown_ms: 20_000.,
    spell_shield_cooldown_ms: 25_000.,
    lantern_ward_cooldown_ms: 60_000.,
    cleanse_cooldown_ms: 30_000.,
    scout_falcon_cooldown_ms: 35_000.,
    bolas_cooldown_ms: 18_000.,
    lantern_ward_charges: 2,
    trade_up_stacks: [10, 22, 34],
    bargain_stacks: 30,
    feed_damage_stacks: 6,
    chef_bonus_share: 0.20,
    helm_bypass: 0.20,
    stasis_ms: 2500.,
    shield_ms: 1500.,
    warhorn_ms: 2500.,
    tornado_windup_ms: 600.,
    tornado_radius: 240.,
    pull_tick_distance: 80.,
    smite_range: 450.,
    swap_range: 400.,
    hook_range: 700.,
    hook_width: 14.,
    bolas_range: 550.,
    bolas_width: 12.,
    blind_rage_attack_rate: 1.20,
    warhorn_damage_per_taunt: 0.2,
    warhorn_guard_per_taunt: 0.15,
    warhorn_guard_floor: 0.4,
    tornado_cast_range: 500.,
    warhorn_radius: 280.,
    smite_damage: 220.,
    swap_ms: 10_000.,
    blind_rage_ms: 6000.,
    still_mind_ms: 3000.,
    charge_range: 220.,
    charge_width: 21.,
    charge_knockback: 120.,
    charge_stun_ms: 500.,
    shot_speed: 175.,
    hook_stop_distance: 42.,
    hook_pull_ms: 600.,
    bolas_root_ms: 1500.,
    tornado_pull: 180.,
    tornado_pull_ms: 300.,
    taunt_stop_distance: 42.,
    cage_radius: 160.,
    cage_ms: 6000.,
    falcon_speed_per_ms: 6.,
    cage_gap_half_angle: 0.48,
    turret_ms: 25_000.,
    turret_interval_ms: 850.,
    turret_range: 340.,
    turret_reward_range: 400.,
    ward_ms: 75_000.,
    falcon_ms: 4000.,
    cage_cast_range: 400.,
    deploy_range: 400.,
    turret_hp: 100.,
    ward_hp: 35.,
    turret_cap: 1,
    ward_cap: 2,
    falcon_cap: 1,
    falcon_range: 1200.,
    turret_reveal: 220.,
    ward_reveal: 365.,
    falcon_reveal: 165.,
    turret_damage_share: 0.3,
    egg_safe_distance: 600.,
    egg_sight_radius: 200.,
    egg_hatch_ms: 3_000.,
    egg_hatch_hp_share: 0.4,
    egg_min_standing: 4,
    wall_radius: 300.,
    wall_anchor_margin: TILE,
};

/// The published quest rules a bot may read (P-RAID-TOP25-READ-1): static design numbers, built from `TOP25`, like the
/// catalog. Not state, not hashed, not an observation, not reachable from a `Command`.
#[derive(Clone, Copy, Debug, PartialEq)]
pub struct QuestPublicRules {
    /// Hold the Fort: ms to dwell (the quest text says the seconds).
    pub fort_ms: f64,
    /// Stay a While: ms to meditate (the quest text says the seconds).
    pub meditate_ms: f64,
    /// Range of a giver contact, accept and turn-in (u).
    pub contact_range: f64,
    /// Range of the fort token that counts as inside (u).
    pub fort_range: f64,
    /// The Bargain: stacks given up (the reward text says it).
    pub bargain_cost: u64,
    /// The Bargain: stacks received (the reward text says it).
    pub bargain_stacks: u64,
    /// Route deadline: ms of deadline per second of walk at `PLAYER_SPEED`.
    pub route_pace: f64,
    /// Route deadline: ms added on top of the paced walk.
    pub route_slack_ms: f64,
}

/// The public rules, read from `TOP25` at each call.
#[must_use]
#[inline]
pub fn public_rules() -> QuestPublicRules {
    QuestPublicRules {
        fort_ms: TOP25.fort_ms,
        meditate_ms: TOP25.meditate_ms,
        contact_range: TOP25.contact_range,
        fort_range: TOP25.fort_range,
        bargain_cost: TOP25.bargain_cost,
        bargain_stacks: TOP25.bargain_stacks,
        route_pace: TOP25.route_pace,
        route_slack_ms: TOP25.route_slack_ms,
    }
}

#[cfg(test)]
#[path = "public_rules_tests.rs"]
mod public_rules_tests;

#[cfg(test)]
#[path = "tuning_tests.rs"]
mod tests;
