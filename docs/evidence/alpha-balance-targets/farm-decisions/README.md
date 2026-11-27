# What one trip to a stat camp buys

Using ordinary camp payouts, the player who planned the next camp for a specific build earned **78% more useful stats per second** across 200 decisions on 20 previously unused maps. Raw stacks per second were 0.39 versus 0.38. Choosing the stat changed most of the value. In 73 decisions both policies chose the same camp, and their outcomes matched exactly.

| Faction | Paired decisions | Targeted / nearest value per second | 95% map interval | Targeted / nearest clears |
| --- | ---: | ---: | ---: | ---: |
| Fire | 40 | 1.38× | 1.17–1.63 | 37 / 38 |
| Sentenced | 40 | 1.30× | 0.98–1.73 | 32 / 35 |
| Plague | 40 | 2.56× | 1.87–3.61 | 39 / 34 |
| Frost | 40 | 2.02× | 1.59–2.64 | 37 / 35 |
| Holy | 40 | 1.99× | 1.46–2.91 | 33 / 33 |
| All | 200 | 1.78× | 1.52–2.09 | 178 / 175 |

Fire and Sentenced landed inside the owner's 25–50% target by point estimate. Plague, Frost and Holy had a larger premium in this solo setting. Across all pairs the targeted arm had 17 deaths and the nearest arm had 19. When the two destinations differed, the targeted leg took a median 28.73 seconds and the nearest leg 24.38 seconds. The result includes both walking and fighting. The camps were uncontested. [Both arms and the distinct-destination subset](returns.csv)

## Does the resulting inventory win fights?

Each completed pair replayed both fighters’ actual end inventories and credited clears in two full-health, same-faction, same-weapon duels with faction Wishes. The targeted inventory scored **70.1%** over 164 pairs that cleared both legs, including cases where the policies chose the same camp. On the 98 completed pairs with different destinations it scored **83.7%**. A win scores one point, a draw or timeout half. This check measures the composition that two completed legs bought. The time-adjusted table above includes unsuccessful legs. [All 328 seat exchanges](kit_duels.csv)

## How the comparison works

The fixed seeds were 421–440. Each generated map supplied both authored builds for each of the five factions. Each pair shared its spawn, map, camp roster, representative inventory of 15 banked normal camps, raid age (minute ten), weapon, and input policy. Both paths used the ordinary three-stack rule. The map exposed the actual themed stat at each reachable normal camp. We priced walk time using path length and base speed, and clear time using the legacy planner's four seconds per living mob. Before combat, the target policy searched within ten added seconds of walking and diverted only for a predicted value-rate gain of at least 25%. Otherwise it kept the nearest camp. Both actions started with the same position and gear.

The authoritative simulator records elapsed time from departure to whole clear, death, or 90-second cutoff. The numerator counts all farm stacks actually earned on the way, including partial income. Utility uses the authored weight of the received stat for that build, with a floor of 0.25. The reported rate is total weighted income divided by total elapsed time. The comparison uses the ratio of those two pooled rates. The 95% interval resamples the 20 maps, retaining all builds on each selected map. Natural regeneration, terrain damage, other players, and pre-raised zombies were disabled. The [eight-seat route trials](../farming/README.md) account for contests, later survival and changing map position. [Predeclared sampling rule](../../../backlog/packets/P-ALPHA-BALANCE-TARGETS-1.md) · [Raw attempts](attempts.json.gz)

## Effect of the experimental focus bonus on stock rooms

On twelve other matched generated maps (1–12), all eight stock RaidBots played with the current quest and elite rules. The only change was the optional focus reward that pays three extra stacks for qualifying kills after a different normal camp. Opening ordinary clears took 13.50 s with ordinary payouts and 13.41 s with the bonus. Across the raid the corresponding medians were 14.71 and 14.81 s for ordinary camps, 22.51 and 22.20 s for elite camps, and 9.85 and 9.75 s for field PvP final blows. Room duration stayed at 20:08.5 and 20:09. All 12 rooms finished in each cohort. The groups recorded 4 and 6 deaths before five minutes, and 20 and 16 before ten minutes. [All timing bands](focus-field/timings.csv) · [Outcomes](focus-field/outcomes.csv) · [Room results](focus-field/matches.csv) · [Raw field comparison](focus-field/raw.json.gz)

## Reproduction

Build the solo and mirrored combat tools with `cargo build --locked --release -p rh-headless --features elite-bench --bin camp_bench`. Run `target/release/camp_bench --elite --farm-legs SEED` once per map, directing each JSON result to its own file. Run `python3 tools/balance-lab/farm_leg_report.py OUTPUT MAP_FILES... --duel target/release/camp_bench`. The same reporter accepts the committed `attempts.json.gz` as its only input. The focus field comparison used the two [stock raid observers](../field/README.md) from `ab9979970`, setting only `cfg.sim.camp_buffs` to the experimental mode after ordinary match setup. Use `field_pacing_compare.py OUTPUT --seeds 1..13 --read-archive focus-field/raw.json.gz` to regenerate its tables.
