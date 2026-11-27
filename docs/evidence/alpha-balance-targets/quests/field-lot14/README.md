# Quest claims and prices on the 10-for-14 preview

Every generated map from seed 601 through 680 ran to match end under protocol 24 and content `13a89cbaff80132f`. One account-eligible RaidBot and seven fill bots played in each authoritative 40 Hz room. The observer recorded the account-eligible seat. Every board showed five distinct quests in planning, one per zone. Each showed ten distinct quests after raid start, two per zone, including all five previously shown.

## Rewards in ordinary play

The focal bot settled **293 rewards in 298 attempts**. All 293 settled rewards were received. Five attempts timed out, including two Trade Up attempts and one each for A Cruel Joke, Lost Brother and Pilgrim’s Path. All 80 rooms included at least one claim. The median room supplied four, and the median grant arrived at 1:56. [All 25 quests and their outcomes](quest-counts.csv)

This cohort produced claims for 15 quest definitions. Hold the Fort appeared in 34 rooms and the focal bot never chose it. It did claim that quest on two previous maps. Nine other definitions lack a stock bot objective executor. Of 143 spell rewards claimed in this cohort, 108 belonged to spells without a dedicated stock bot cast rule. The authoritative scenarios separately test all 25 rewards. [All counts and notices](quest-counts.json) · [Quest design and direct effect checks](../README.md)

## The payoff for choosing a quest

A normal clear is the reference unit. A representative full ordinary roster yields 12 same-stat stacks. Route costs include actual observed approach and objective time, subtract ordinary clears completed during the route and include work spent on unsuccessful routes. The conversion from time to ordinary camps uses the earlier independently measured outside-quest farming rates by faction and raid age. [Per-quest route costs](route-economy.csv)

| Quest | Observed claims | Value compared with farming |
|---|---:|---|
| Trade Up | 30 | All 30 cashed out at the 29-stack top tier. At an average 1.781 normal clears of route cost, the same-stat return was **1.36×**. |
| Let Him Cook | 31 | At an average cost of 2.112 clears, a Chef’s Kiss worth 33 ordinary stacks to this build returns **1.30×**. Movement caps or an unwanted lowest stat change the payoff. |
| The Bargain | 29 of 35 offers | The 10-for-14 trade returned **1.68×** the build-valued full nearby alternative (95% map interval 1.24–2.41) or **1.64×** the remaining nearby roster (1.14–2.51). With equal value on the stats exchanged it returned **0.70×** the neutral farming alternative. |

All 29 Bargain claims exchanged stacks. The full-near comparison put nine below 1.25×, three within the requested 1.25–1.50× band and seventeen above it. The spare-weapon payment is covered by the authoritative scenario. Its return in an ordinary room depends on what weapon the player gives up. [Trade ledger](bargain-trades.csv) · [Cohort estimates and intervals](bargain-summary.json)

## Reproduction

The archived observations omit terminal state hashes. The second archive contains the historical reference cohort’s outside-quest tick and clear counts, the complete input needed to recompute the reference rates. The compiled `quest_balance` observer’s SHA-256 is `45f9a7deda8571a799be4b5488c2b491f44736660bb8b04535cf5f96705d96bf`. [Final rooms](observations.jsonl.gz) · [Historical rate inputs](reference.jsonl.gz)

From the repository root, run the following commands.

```sh
base=docs/evidence/alpha-balance-targets/quests/field-lot14
gzip -dc "$base/observations.jsonl.gz" > /tmp/quest-final-rooms.jsonl
gzip -dc "$base/reference.jsonl.gz" > /tmp/quest-final-rates.jsonl
python3 tools/balance-lab/quest_balance_report.py /tmp/quest-final-rooms.jsonl \
  --expect-seeds 601..681 --csv /tmp/quest-final-counts.csv
python3 tools/balance-lab/quest_economy.py /tmp/quest-final-rooms.jsonl \
  --rate-traces /tmp/quest-final-rates.jsonl --output /tmp/quest-final-costs.csv
python3 tools/balance-lab/bargain_receipt_report.py /tmp/quest-final-rooms.jsonl \
  --farm-reference /tmp/quest-final-rates.jsonl --fee 10 --payout 14 \
  --output /tmp/quest-final-trades.csv --summary /tmp/quest-final-bargain.json
```
