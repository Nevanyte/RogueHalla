# Raid balance verification

The requested targets are about twice the ordinary solo clear time for an elite after minute five, and a 25–50% return above nearby farming for a well-chosen quest or faction route. Time in combat and time spent walking both use the authoritative 40 Hz raid clock. A player who dies, abandons a quest or fails a clear has spent time and earned a different reward. Each comparison retains its unsuccessful attempts.

## Quest rewards

All 25 quests have an authoritative completion and reward test. In 80 stock raids, a focal bot claimed 309 rewards in 310 attempts across 16 quest definitions. Nine quests still lack a stock bot objective executor. The [25-quest valuation](quests/README.md) includes real approach and objective time, direct payment, concurrent farming and failed attempts where observed. It prices situational benefits only for the stated use case.

At the revised 29-stack top payout, Trade Up returned 46% above farming on ten faction-stratified maps. Four additional claims took shorter routes and returned 66% above farming. Let Him Cook returned 41% in the measured route-cost scenario where Chef’s Kiss increased a useful stat by the equivalent of 33 normal stacks. The 12-stack Feed or Hunt reward returns 33% on a 30-second route at 1.5 normal clears per minute. The last figure is a design calculation for a player who can collect all three items in that time. [Quest prices and conditions](quests/README.md)

For all 25 quests the [required-value table](quests/required_value.csv) also gives the payoff needed to earn the specified premium at either end of the [baseline cost range](quests/route_baseline.csv). Multiply the net route cost by 1.25 or 1.50. For example, The Warden’s low design cost is 1.625 ordinary clears. Its cage must deliver 2.03–2.44 clears of value there, equivalent to 81–98 seconds saved at 1.5 clears per minute. A single 40-second escape on that route would underpay. Repeated escapes, a decisive kill or preventing an elimination can change that result. Each unplayed quest is labeled as a design scenario in the table.

## Faction farming

In a same-origin solo comparison using ordinary payouts, stat-aware farming earned 78% more build-valued stats per second than choosing the nearest camp. Forty opportunities per faction yielded 38% for Fire, 30% for Sentenced, 156% for Plague, 102% for Frost and 99% for Holy. Earned kits scored 70.1% in full-health, seat-swapped duels across 164 pairs that completed both legs. [Measured travel, camp income and duel outcomes](farm-decisions/README.md)

A route must buy a more useful stat quickly enough to repay the walk. For two choices with expected useful payouts F and N and total travel-plus-clear times t and u, the targeted route’s premium is (F / N) × (u / t) − 1. At the same stack count, if the nearest stat has 60% of the preferred stat’s value, a targeted route lasting 1.11–1.33 times as long earns 25–50% more value per second. If the nearest camp already pays the preferred stat, taking it is an appropriate result. Full-match comparisons additionally include deaths, choice of subsequent camps and the damage the earned inventory can actually deal. [Route experiments](farming/README.md)

## Camps, player fights and raid duration

The earlier farm change raised normal-mob rewards from one stack to three and doubled normal mob health. The subsequent alpha added a defense curve for ordinary camps after minute five. Elite camps originally missed that curve. The new elite rule treats the boss and its escorts together and reduces the damage they deal during a longer pull. [Solo camp comparison](elites/README.md) · [Earlier farming change](../quest-elite-balance/README.md#elite-camp-difficulty)

In the final same-kit test, elite camps took 2.15×, 2.02×, 2.00× and 1.93× their ordinary controls at minutes 6, 10, 15 and 20. Each age supplied 200 attempts on ten independent maps. The corresponding elite clear medians were 30.85, 41.80, 22.25 and 21.05 seconds. [Absolute times, completions, deaths and per-boss ratios](elites/README.md#independent-test-of-the-final-rule)

In twelve paired eight-seat raids with the ordinary farming rules, normal clear time stayed close to its previous value (14.50 s to 14.71 s), elite clear time increased (13.83 s to 22.51 s), and median room duration stayed near 20 minutes (20:09.5 to 20:08.5). Single-player matched camp comparisons isolate the extra time for an elite. Groups, damaged rosters, weapons, and distinct start and end points affect the field medians. [Encounter times by raid age, outcomes and room durations](field/README.md)

Fixed-loadout full-health duels measured 25.20 s early, 9.43 s with a developed farm kit and 11.38 s with developed farm and augments. With the farm kits actually earned at minutes 5, 10, 15 and 20, medians were 23.48, 15.29, 13.00 and 13.45 seconds. Only eight surviving pairs supplied the last figure. The twelve tuned stock matches yielded 27 credited PvP final blows with a 9.85 s median. Twenty-five involved other damage sources or multiple attackers. This sample contained zero healthy, clean duels before the gates locked. [Matched farm-kit duels](../raid-routing-ttk/README.md#camp-clears-and-player-kills-over-raid-time) · [Field fight definitions](field/README.md#timing-definitions)

The region boss also pays 12 camp-stat stacks to a qualifying contributor. In developed full-health duels, a freshly acquired Machine Gun won 80% against two preferred normal camps and scored 50% against four. Committed scored 80% and 30% on those comparisons. In symmetric fights, adding both cards to 180-stack kits reduced median duel time from 14.98 to 9.80 seconds. The card’s value, contributor reward and chance of surviving the extra elite exposure all affect the choice. [All 33 augment comparisons](../quest-elite-balance/README.md#elite-augments)

## Planning, maps and presentation

Planning reveals one named quest per zone. At raid start each zone reveals its second distinct quest. The placement check spreads the givers across the map and each uses the same painted archivist sprite. The Unity browser check saw five distinct names during planning and ten after the start. Map admission keeps the volcano, tree, cave, church and Holy temple landmarks, including on fallback maps. The zone art fixes cover Sentenced fence direction and the older colored props and Plague ground. [Quest restoration](../../backlog/packets/P-ALPHA-QUEST-RESTORE-1.md) · [Zone art](../../backlog/packets/P-ALPHA-ZONE-ART-1.md)

## Bot play and remaining coverage

The real-room test seated one human-controlled connection and seven RaidBots on each of three maps. At 150 active seconds 7, 6 and 7 bots remained standing, and the human survived all three. A bot killed an elite at 145.6 seconds and took a chest upgrade at 147.5 seconds on the first map. In the broader stock runs, all 33 elite augments were selected. Wish Engine and the Shield/Fists growth augments still need deliberate bot input and weapon choices for representative field data. [Production room checks](../../../crates/rh-server/src/room/tests/bot_fill.rs) · [Augment field measurements and direct effect checks](../quest-elite-balance/README.md#elite-augments)

The next bot quality tests are human kiting and disengages, shield and fists use when a card pays for them, and target selection at the Coliseum lock. Sudden death accounted for 49 of 84 deaths on the twelve tuned maps. These gaps matter when interpreting bot results as a forecast of human play.
