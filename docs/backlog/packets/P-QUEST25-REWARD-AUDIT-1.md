# P-QUEST25-REWARD-AUDIT-1 — earned reward effects

## ID and outcome

Phase 2 item 13, quest completion. Allow StillMind to break an active stun and exercise each reward from its
actual Top 25 quest and turn-in at the legacy and live server step rates.

## Dependencies

P-QUEST25-REWARDS, P-QUEST25-SPECIAL and the generated scenarios under `quests::board::attempts::scenarios`.

## Relevant legacy paths

`packages/server/src/sim/quests.ts:306` completes the legacy attempt and applies its payout.
`packages/server/src/sim/abilities.ts:235` handles casting interruptions from statuses. The legacy catalog pays
different rewards. DEV-040 and the approved Top 25 workbook govern named awards.

## New paths

This packet brief.

## Exclusive write scope

- `crates/rh-sim/src/quests/board/rewards/casts.rs`
- `crates/rh-sim/src/quests/board/rewards/effects/tests.rs`
- `crates/rh-sim/src/quests/board/attempts/scenarios.rs`
- `crates/rh-sim/src/quests/board/attempts/scenarios/spells.rs`
- This packet brief.

## Contracts and references

C-DET, C-INPUT, C-OBS, C-PARITY and DEV-040. Award values and the effect of StillMind are defined in the existing
Top 25 tuning and effects modules. D9 applies to all reward inputs and outcomes.

## Behaviour to preserve

Keep the existing quest conditions, turn-in rules, inventory, timers and reward amounts. StillMind can be used during
stun because its effect clears stun and root and protects against reapplication. Taunt, pull, silence, active casts
and Stasis retain their existing cast restrictions. Rebirth Egg still needs more than three survivors.

## Acceptance checks

Both generated board sweeps earn the full catalog through validated inputs, verify its corresponding powers and
restore checkpoints. Lost Brother applies stun before the earned StillMind cast and checks that stun disappears and the
draught is consumed. The test fails on the base because the cast is rejected. Hold the Fort checks that the earned
turret damages a player. Whispers in the Brush and Watchtowers check private vision from the earned deployments.
Gatecrasher sends a lethal weapon input against the player with the earned, planted egg and checks for a hatch and
weapon retention above the survivor threshold and final death at the threshold. Existing focused tests cover charges,
replacement, decline, loot and additional reward interactions.

The generated scenarios are the common receipt proof for every row below. Focused tests isolate effects that need
particular targets, terrain, equipment, or timing. The active spell numbers are tuning values in
`quests/board/tuning.rs` and the behavior advertised in `quests/board/catalog.rs`.

| Quest | Payout and evidence |
|---|---|
| The Warden | Warden's Cage lasts 6 s. `cage_blocks_melee_and_motion_but_keeps_sight_and_its_directed_gap` tests the collision masks, escape gap and removal. |
| Hold the Fort | Turret shoots players and mobs. The catalog sweeps now check HP loss on a player and `smite_targets_a_mob_and_turret_shoots_mobs` checks mob HP. Remote turret credit has separate coverage in `turret_share_tests.rs`. |
| Bounty Hunter | Hook pulls the first enemy hit. `hook_pulls_first_contact_and_charge_moves_and_staggers` checks contact and displacement. The attempt suite covers both player bounty and fallback fill-bot marks. |
| Smuggler's Run | Shoulder Charge moves the caster and knocks back and staggers the first target. The same focused Hook/Charge test checks both bodies and Stun. |
| Two Banners | Warhorn taunts enemies and buffs the caster per successful taunt. `opposing_banners_pay_only_at_the_destination_of_each_delivery` covers both branches. `warhorn_taunted_enemies_walk_to_and_attack_the_caster_who_gets_a_large_buff` checks forced approach and attack and the buff. |
| Ember Walk | Spell Shield blocks one incoming spell. `shield_blocks_one_bolas_and_refunds_then_cleanse_removes_second_root` verifies block, consumed shield, refunded charge and that the second shot roots. The room cooldown test covers the spent reward through replacement, pickup and recharge. |
| The Burning Crown | Ember Imp damage grows at one stack a second to 48. `crown_damage_follows_the_crown_and_caps_at_48_without_double_payment` uses Holy, verifies the carrier's HP loss and landed gain, transfers the provisional bonus on drop, and proves banked damage is paid once. |
| Let Him Cook | Chef's Kiss increases the weakest eligible stat by 20%. `chef_increases_the_lowest_percentage_growth_by_twenty_percent` verifies the folded attack-speed multiplier changes from 1.20 to 1.44 while HP stays fixed. Eligibility is the six positive starting attributes in `rewards/permanent.rs`. |
| Elite Slayer | Smite deals 220 true damage to a valid mob. The earned spell takes credit from a rival with more threat and pays the +15 GV bonus alongside normal mob GV. The focused spell test also checks nonlethal mob HP loss. |
| Tuber Eats | Breaker's Helm moves 20% of bow and axe physical damage to true damage. `breakers_helm_bypasses_physical_armor_on_bow_and_axe_only` tests the actual loss of armored target HP and the Sword control. |
| Barrow Run | WallBuster removes eligible walls within 300 u. The sweep checks a real terrain edit. The `special::wall` tests check range, protected terrain, movement and visibility. |
| The Bargain | Twenty equivalent stacks or the inactive weapon buys 24 selected stacks. `bargain_spends_equivalent_stacks_or_the_inactive_weapon_for_real_max_hp` checks both payments, the active Bow survives, and +24% max HP folds in. The generated case and published lifesteal trade cover other receiving stats. |
| A Cruel Joke | Swap-Stick exchanges the active weapons for 10 s. `swap_returns_both_original_slots_and_seed_recalls_to_authoritative_feet` checks slot restoration. A separate test covers a fill-bot target. |
| A Bone to Pick | Reusable Bolas applies a 1.5 s root. The earned spell roots a live enemy in the sweeps. The shield interaction and tick-rate suites check tagged projectile contact and time scaling. |
| Feed or Hunt | Six Ember Imp stacks give +6% damage. The sweeps pay six and `feed_or_hunt_boosts_the_owners_landed_damage` independently checks 106 damage versus 100. |
| Rime Hunt | Tornado pulls eligible targets based on max HP. `tornado_waits_and_pulls_by_max_hp` checks the wind-up and displacement. |
| Watchtowers | Scout Falcon reveals along its flight. The sweeps check a point visible only to the owner outside both players' ordinary vision. |
| Whispers in the Brush | Lantern Ward gives two reusable charges and a private radius. `lantern_ward_has_two_independent_spends_and_private_sight` checks charges, recharge and sight. |
| Even Skeletons Get Cold | Warm Coat blocks bog slow and chill. `warm_coat_stops_bog_movement_slow_and_both_kinds_of_frost_chill` checks the terrain and both chill sources. |
| Lost Brother | One BlindRage and one StillMind. The sweeps check the 1.20 attack rate and that StillMind clears stun and adds immunity. The `warhorn_forces_walk_and_buffs_then_draughts_apply_and_expire` test checks both buffs and expiry. |
| Pilgrim's Path | Cleanse removes root, stun and pull. The sweeps clear an applied root. `cleanse_breaks_a_forced_pull_before_the_next_displacement` checks that the same forced pull moves the unprotected control and leaves the casting player in place. |
| Gatecrasher | Rebirth Egg hatches after 3 s at 40% HP. The new sweep checks real death, hatch position and retained gear above the cutoff. `rebirth_egg_real_death_opens_gates_and_hatches_once_after_three_seconds` checks the timing and HP. |
| Letters | Forget-Me-Not Seed records the server position and teleports back on the second cast. The sweep and Swap-Stick/Seed focused test both observe the player's return. |
| Stay A While | Stasis lasts 2.5 s and blocks action and damage. `stasis_stops_real_movement_attacks_casting_and_damage_then_releases` checks the sim effects. The encoded room test earns and spends Stasis through normal client inputs. |
| Trade Up | Cashing out at stages 1, 2 or 3 gives 12, 30 or 54 Penitent stacks. `trade_up_all_three_cash_outs_raise_max_hp` checks the actual +12%, +30% and +54% maximum HP changes. |

## Validation commands

`cargo test --locked -p rh-sim --lib quests::board` and the coordinator's full gate.

## Risk and suggested model tier

This extends an existing defensive cast exception. Review the cast gate and both step-rate sweeps together. Sol.

## Known traps

Use validated cast input on the reward earned by the tested seat. Ordinary vision persists for 30 seconds and can
mask a failed deployment reveal. Completing Gatecrasher as the second owner kills a second opponent and can place the
match at the egg cutoff after the owner dies.
