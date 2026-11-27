# Quest rewards on the selected local build

Eighty unselected generated rooms, seeds 601–680, played to match end with the 8-for-11 Bargain and content `cb811d19ebed6dfe`. Each room used the authoritative 40 Hz simulation with one account-eligible RaidBot and seven fill bots. The account-eligible seat supplied the reward observations. All 80 planning boards had five distinct quests, one per zone. All raid boards retained them and revealed five more, giving ten unique quests, two per zone.

## Claims in actual play

The focal bots received all **292 settled rewards in 297 attempts** across 15 quest definitions. 5 attempts expired. The timeouts were A Cruel Joke (1), Lost Brother (1), Pilgrim's Path (1), Trade Up (2). Of 143 spell rewards claimed, 108 belonged to spells without a dedicated RaidBot cast rule. The separate authority suite exercises all 25 definitions, including pending rewards and both Bargain payment branches. [Per-quest outcomes](quest-counts.csv) · [Spell uses and complete counts](quest-counts.json)

The median raid lasted 20.12 minutes. The shortest ran 11.14 minutes, and 0 ended inside one minute. These are full room durations, including fights after the focal seat was eliminated.

## Opportunity cost

An ordinary full camp supplies 12 stacks at the reference roster size. The cost includes time spent approaching the giver and completing the objective. It credits ordinary clears made along the way and retains time spent on failed routes. The time conversion uses the measured historic outside-quest clear rates for each faction and raid-age band. The Bargain separately values the paid and received stats at their actual build weights and charges the value of the nearby camp. [Route costs for all definitions](route-economy.csv)

| Quest | Claims | Value relative to ordinary farming |
|---|---:|---|
| Trade Up | 30 | Cashouts at stages one, two and three were 0, 0 and 30. Reward divided by actual route cost gives **1.36×** when the chosen stat matches the farming alternative. |
| Let Him Cook | 31 | If Chef’s Kiss delivers 33 equivalent useful stacks, the measured 2.112-clear mean route cost yields **1.30×**. A different weakest stat or the movement cap can lower that return. |
| The Bargain | 28/35 offers | Actual settlements returned **1.68×** the full nearby camp (95% map interval 1.18–2.53) and **1.70×** its remaining roster (1.10–3.01). |

At equal value for the two stat types, three net stacks returned 0.64× the neutral camp alternative on the average Bargain route. The full-near comparison put 8 paid routes below 1.25×, 2 inside 1.25–1.50× and 18 above 1.50×. 0 observed trades used a spare weapon. [Actual trades](bargain-trades.csv) · [Pooled figures and intervals](bargain-summary.json) · [Same 80 maps at the previous price](../field-lot14/README.md) · [Independent prospective price comparison](../price-confirmation/README.md)

## Reproduction

The observer binary SHA-256 is `4cb7f8f54c8d20af81a955560e87a2d7baddb0bea9cb0d78e249125273528ea8`. The observation archive omits terminal hashes. The historical rate archive retains all outside-quest ticks and clear counts used by the reports. [80 room traces](observations.jsonl.gz) · [Historical rate inputs](reference.jsonl.gz)

```sh
base=docs/evidence/alpha-balance-targets/quests/final-field
gzip -dc "$base/observations.jsonl.gz" > /tmp/quest-final-rooms.jsonl
gzip -dc "$base/reference.jsonl.gz" > /tmp/quest-final-rates.jsonl
python3 tools/balance-lab/quest_balance_report.py /tmp/quest-final-rooms.jsonl \
  --expect-seeds 601..681 --csv /tmp/quest-final-counts.csv
python3 tools/balance-lab/quest_economy.py /tmp/quest-final-rooms.jsonl \
  --rate-traces /tmp/quest-final-rates.jsonl --output /tmp/quest-final-costs.csv
python3 tools/balance-lab/bargain_receipt_report.py /tmp/quest-final-rooms.jsonl \
  --farm-reference /tmp/quest-final-rates.jsonl --fee 8 --payout 11 \
  --output /tmp/quest-final-trades.csv --summary /tmp/quest-final-bargain.json
```
