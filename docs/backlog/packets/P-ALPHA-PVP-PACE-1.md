# P-ALPHA-PVP-PACE-1

## ID and outcome

P-ALPHA-PVP-PACE-1 serves the duel timing portion of ORDER_OF_OPERATIONS Phase 2 item 11 and the owner's alpha
restoration. Equal-build PvP targets a median of about 25 seconds on starter gear and about 10 seconds on developed gear.
Raid mode grows the PvP multiplier linearly from 0.425 at zero earned normal-mob stacks to 0.95 at 288 stacks. A
symmetric faction-pair weight calibrates fight duration while applying the same matchup adjustment to both participants. Augments retain their own damage, defense
and combat effects, with card power contributing up to 20% additional Raid PvP scaling.

## Dependencies

The alpha restoration's shared `SimConfig.combat_pace` selects Raid in the live profile and Legacy in default and
historical configurations. The coordinator registers the Raid constants in the content identity. The headless control
uses the elite bench's banked camp builds and the balance lab's match setup.

## Relevant legacy paths

- [packages/shared/src/constants.ts](../../../packages/shared/src/constants.ts#L225) describes the legacy card-power
  curve, 35% per-hit cap and 15%-of-max-health per-source DoT cap per second.
- [packages/server/src/sim/ledger.ts](../../../packages/server/src/sim/ledger.ts#L173) defines credited PvP sources and
  applies card-power scaling. The outgoing ledger pass at line 219 excludes abilities from that scaling.

## New paths

- `crates/rh-sim/src/systems/ledger/pvp.rs`
- `crates/rh-headless/src/pvp_bench.rs`
- `docs/backlog/packets/P-ALPHA-PVP-PACE-1.md`

## Exclusive write scope

The new paths plus `crates/rh-sim/src/systems/ledger.rs`, `crates/rh-sim/src/scratch.rs`,
`crates/rh-sim/src/modifiers/farm.rs` and the `camp_bench` CLI in `crates/rh-headless/src/bin/camp_bench.rs`.
Configuration, the constants registry and the deviation record belong to the alpha coordinator.

## Contracts and references

C-INPUT uses validated held attacks and actual casts, C-TICK retains the ledger phase, and C-PARITY retains the legacy
profile for captures. Legacy and Raid run in the same timing oracle through the ordinary sim. D2's full action/reply
matrix remains covered by the separate P-BALANCE-1 scope.

## Behaviour to preserve

PvP still settles simultaneous damage before death payouts. Damage shares retain armor, shield and true-damage
mitigation. Signature burn and mark still enter the normal settlement path. Wish damage uses its own fixed numbers.
Arena, unowned NPC and player-to-mob damage keep their existing settlement rules. The existing player hit and DoT
limits still use the victim's max HP and the sim tick duration.

## Acceptance checks

- Run all 15 unordered faction pairings, five equipped weapons and six deterministic draws at 20 and 40 Hz. Measure
  first down from validated attack start. Both fighters begin in range with the same build and weapon. The Raid
  starter median must be 22–28 seconds and both developed medians 8–12 seconds, with every duel decided.
- The developed stage banks 24 camps, or 288 normal-mob stacks. Pilot 2 measured about 164 stacks at minute 12 and a gain
  of about 15 stacks per minute from minute 8 to 12. The late reference extrapolates that rate. Repeat with real named
  pick sets from that pilot transplanted onto the same banked builds.
- Observe real signature burn and mark damage, a damaging Blaze trail with the same damage under both pace profiles,
  and a post-charge Fire opening strike with a positive burst.
- Sample actual late balance-lab survivors using the current rules, with their normal-mob stack mix, augments, boosts
  and weapon. Use mirror duels to report their timings as an independent distribution.

The reference runs decided all 450 duels per profile, build and tick rate. Times below are seconds from attack start.

| Build | Legacy 20 Hz median | Raid 20 Hz median | Raid 20 Hz p10–p90 | Raid 40 Hz median |
| --- | ---: | ---: | ---: | ---: |
| Starter | 15.15 | 25.25 | 22.45–29.00 | 25.18 |
| 288 stacks | 11.50 | 9.25 | 6.75–12.80 | 9.22 |
| 288 stacks and pilot augment picks | 4.70 | 10.60 | 6.00–16.95 | 10.55 |

## Validation commands

```sh
cargo run --release -p rh-headless --features elite-bench --bin camp_bench -- --pvp --check --sampled
cargo test -p rh-sim --test no_alloc
scripts/gate.sh
```

## Risk and suggested model tier

High. This is an authoritative balance change. Review combat identity, old configurations, and the range of timings for
unusual loadouts in addition to cohort medians.

## Known traps

The bench gives full attack uptime and excludes movement, dodges and opponent policy. First down can be faster than
both mirror fights in a mixed matchup. The balance-lab snapshot transplants static equipment and progression and starts
each duel with fresh combat state. It excludes a sampled player's temporary effects, current health, quest inventory and
accumulated combat-trigger meters. A live fight can include any of those.
