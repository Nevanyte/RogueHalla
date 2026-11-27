# The Bargain against the closest normal camp

This check joins real 20-stack or spare-weapon payments to the stat that actually landed, then values the paid and received stats with the same faction and current weapon weights used to price nearby farming. Those existing RaidBot weights are a transparent design premise. They do not measure how a human values a stat or the change in duel win probability.

## Method

- The offline observer snapshots the focal player's normal farm counts and both weapon slots before each step on maps with The Bargain. A same-tick completed attempt and specific owner reward notice identify settlement. The debit, credit and weapon change must form an exact valid offer. Any incompatible change is recorded as ambiguous.
- Each selected quest leg captures the geometrically nearest open, non-elite camp from its starting point. The alternative pays that camp's actual Themed stat and its living roster gives the base remaining stacks at three per normal kill. A sensitivity assigns a full 12-stack clear of the same stat. Each candidate is valued with the settled trade's active weapon and faction. The route clock still uses the frozen observed faction/raid-age rate, including travel, downtime and normal non-quest work. The alternative is an estimate using observed throughput and the nearby roster. Available pulls vary in obstacles, remaining health and competition. The model excludes incremental card payouts such as Marked Man.
- The awarded stack family and amount are observed. Timing, alternative roster and closest distance are observed. Fit weights and replacement cost of a spare weapon are authored. All priced receipts and neutral abort spend enter each cohort ratio. Failed legs without settlement use neutral weight in the headline, with a 0.6–1.4 range in the summary. A change in payout can affect acceptance, choice, travel and exposure.

## Calibration

The 27 previously specified offer-selected maps were replayed on the later ordinary Themed build at content hash `bc50899e49b08197`. All 27 terminal state hashes matched the first receipt observer. Separate same-binary tests confirmed receipt capture preserves state hashes with capture off for seeds 2, 27 and 50.

On these calibration maps, 22 offers paid and all 22 consumed 20 stacks. Two paid an armor output, one shield, eight ability power and eleven attack damage. No receipt used a spare weapon. The 22 routes cost **12.440 neutral camp-equivalents**, and the equal-stat return totaled **16.500**, or **1.33×**. The same trades under the authored fit weights yielded **35.833 CE**. Closest-normal farm cost was **11.608 CE** on the remaining rosters and **12.923 CE** when each is priced at 12 same-stat stacks. The fit return is **3.09×** and **2.77×**, respectively.

Changing the reward while holding the 22 trades and their routes fixed prices 23 at 1.80× versus remaining rosters or 1.61× versus full rosters. The 22-stack price gives 1.58× and 1.42×. These are extrapolations. The independently selected boards test actual policy choices at each price.

[Calibration receipts](bargain_calibration_receipts.csv) · [Calibration totals](bargain_calibration_summary.json)

## Held-out payout comparisons

The same 14 of 19 offered maps settled at all three payouts. Each settled payment consumed 20 stacks, with no observed spare-weapon exchange. The remaining five never accepted, and there was zero measurable failed goal time in this sample. The test priced whichever route and output the policy actually chose in each build.

| Payout | Mean route cost, neutral CE | Fitted net return per payout, CE | Remaining nearest roster | Full nearest 12-stack camp | Full-near below / within / above requested band |
|---:|---:|---:|---:|---:|---:|
| 29 | 0.620 | 1.419 | 2.224× | 2.126× | 1 / 1 / 12 |
| 23 | 0.543 | 0.955 | 1.543× | 1.618× | 3 / 0 / 11 |
| 22 | 0.505 | 0.893 | 1.574× | 1.648× | 2 / 1 / 11 |

At 29 the bot received 12 AP, one AD and one armor reward. At 23 it took nine AP, four AD and one armor reward. At 22 it took eight AP, five AD and one armor reward. Median selected time fell from 23.025 seconds to 22.100 and 21.200 seconds, respectively. Lower payouts reduced the net return in the same units. The lower time and changed alternative camp pay diluted that effect. Both cheaper quotes still exceeded the 25–50% cohort band under the authored closest-camp valuation.

Candidate 23 was predeclared from the initial calibration, and 22 was added before reading the held-out outcomes when the completed calibration put 22 closer to 1.375× for a full near camp. The checkout maintained its 29-stack runtime during these alternative replays. The confirmation board outcomes remained untouched until the next price was fixed.

[29-stack receipts](bargain_heldout_29_receipts.csv) · [23-stack receipts](bargain_heldout_23_receipts.csv) · [22-stack receipts](bargain_heldout_22_receipts.csv)

## Confirmation price fixed before the second holdout

At fixed observed choices and routes, a 20-stack reward leaves the neutral stack count unchanged. A 21-stack reward adds one nominal stack. Either can improve the build by exchanging a weaker stat for a used stat. The native valuation prices the swap itself, as well as the additional nominal stacks.

| Previously observed choices | Candidate payout | Remaining closest roster | Full closest 12-stack camp |
|---|---:|---:|---:|
| 27-map calibration, observed at 29 | 20 | 1.149× | 1.032× |
| 27-map calibration, observed at 29 | 21 | 1.364× | 1.225× |
| First holdout, pooled actual 22/23 payments | 20 | 1.084× | 1.136× |
| First holdout, pooled actual 22/23 payments | 21 | 1.274× | 1.335× |

The confirmation compares **21 vs 29** on all 17 preselected offer boards in the untouched 121–160 range. The 21-stack candidate is closer to the target under both nearest-camp assumptions in both inputs above. The checkout kept its 29-stack runtime during the independent replays.

## Confirmation on the untouched offer boards

All 17 offers were replayed to the normal endpoint. The 21-stack case paid on 10 maps, each also paying at 29. Both prices used only stack payments. The 21-stack case spent 0.146 neutral CE on routes that yielded no payout. The 29-stack case had zero such cost. The table includes that time at neutral weight and gives 95% percentile intervals from 2,000 resamples of map seeds.

| Measure | 29-stack control | 21-stack candidate |
|---|---:|---:|
| Claims / offers | 14/17 | 10/17 |
| Mean route cost including failed spend, neutral CE | 0.518 | 0.317 |
| Median selected route time, seconds | 23.600 | 19.200 |
| Equal-stat payout / neutral route spend | 1.447× | 0.262× |
| Fit payout / remaining nearby roster | 2.798× (2.042–4.300) | 2.386× (1.343–4.565) |
| Fit payout / full nearby 12-stack camp | 2.905× (2.214–4.057) | 2.595× (1.548–4.336) |
| Full-near paid routes below / within / above 25–50% | 1 / 0 / 13 | 1 / 1 / 8 |
| Remaining-near paid routes below / within / above 25–50% | 1 / 2 / 11 | 2 / 0 / 8 |

Weighting failed routes at 0.6–1.4 of neutral changes the 21-stack full-near return to 2.543–2.649×, and the remaining-near return to 2.343–2.432×. The 21-stack candidate reduced the aggregate fit return and still exceeded the target. The policy selected cheaper routes and concentrated on swaps it valued more. At 21 the median fitted return for completed full-near routes was 3.694×. A flat payout reduction alone failed to produce a 25–50% premium in this test. The checkpoint kept the 29-stack rule through that test.

[29-stack confirmation receipts](bargain_confirmation_29_receipts.csv) · [21-stack confirmation receipts](bargain_confirmation_21_receipts.csv) · [29-stack summary](bargain_confirmation_29_summary.json) · [21-stack summary](bargain_confirmation_21_summary.json)

## Smaller lot fixed before the third holdout

Reducing the stack fee and the reward limits the amount of stat a player can redirect in one trade. Candidate **10→14** keeps four net stacks and had projected full-near multiples of 1.290×, 1.223× and 1.726× in the three preceding independent cohorts when their observed swaps and timing stay fixed. The arithmetic mean is 1.413×. Observed price variants within each cohort contribute to a pooled denominator for that cohort. Other fixed-choice means were 1.301× for 8→12, 1.626× for 10→15 and 1.738× for 12→17. An otherwise unchanged 20→29 replay provides the control.

The board-only selection found 19 Bargain offers from seeds 161–200. The replay includes all 19, selected before reading any ROI, acceptance or combat outcome. The seeds are 161, 166, 167, 170, 174, 175, 176, 177, 179, 180, 182, 184, 188, 189, 190, 192, 197, 198 and 199.

## Smaller-lot results

Both builds offered The Bargain on all 19 selected boards. The smaller lot retained all 15 control payments and added two on seeds 177 and 197. All payouts in both cohorts spent stacks. No policy used a spare weapon and no Bargain attempt was lost to death. Neither cohort logged failed route spend. There were 17 focal deaths at 20→29 and 16 at 10→14, with zero before minute five in either cohort.

| Measure | 20→29 control | 10→14 candidate |
|---|---:|---:|
| Claims / offers | 15/19 | 17/19 |
| Mean route cost, neutral CE | 0.460 | 0.389 |
| Median selected route, seconds | 18.175 | 11.775 |
| Equal-stack payout / neutral route cost | 1.632× | 0.856× |
| Fit payout / remaining nearest roster | 3.860× (2.341–6.976) | 1.974× (1.215–3.664) |
| Fit payout / full nearest 12-stack camp | 2.886× (1.906–5.038) | 1.920× (1.190–3.473) |
| Full-near paid routes below / within / above +25–50% | 2 / 1 / 12 | 3 / 0 / 14 |
| Remaining-near paid routes below / within / above +25–50% | 1 / 0 / 14 | 4 / 1 / 12 |

On the same 15 paid maps, full-near return fell from 2.886× to 1.861× and remaining-near return fell from 3.860× to 1.959×. A paired bootstrap draws 19 map seeds with replacement for each of 4,000 resamples (seed 205) and reuses the same draw for both payouts. The 95% intervals for the candidate minus control are −2.07 to −0.33 for full-near and −4.07 to −0.89 for remaining-near. Repricing outcomes by actual closest-camp fit confirms a substantial reduction while the pooled return still exceeds the target band.

The four net nominal stacks pay below the average nearby farm alternative when both stats have equal value. The smaller lot is chiefly attractive when it converts spare growth into a useful stat. If a player chooses the weapon branch, the same 14-stack grant is weighed against the actual value of that weapon. This cohort establishes no field return for that branch. The earlier local checkpoint adopted 10→14 after this comparison. The [paired confirmation](price-confirmation/README.md) informed the subsequent 8→11 rule. Both payment methods remain available.

[20→29 receipts](bargain_lot_29_receipts.csv) · [10→14 receipts](bargain_lot_14_receipts.csv) · [20→29 summary](bargain_lot_29_summary.json) · [10→14 summary](bargain_lot_14_summary.json)

## Reproduction

`quest_balance START END --board-only` records the five planning and ten raid offers and exits at raid setup. The first test cohort is every seed in 81–120 with The Bargain on the raid board, fixed before reading outcomes. Its 19 seeds are 81, 82, 83, 84, 85, 88, 89, 94, 95, 98, 101, 103, 105, 107, 108, 109, 111, 115 and 117. The additional confirmation board range is 121–160, with 17 eligible seeds (121, 123, 126, 127, 129, 131, 138, 139, 142, 144, 145, 146, 149, 151, 154, 155 and 156). Neither selection uses quote, travel, completion, payout or later raid outcomes.

Replay each seed to the ordinary 40 Hz end using `quest_balance SEED SEED_PLUS_ONE --bargain-receipts`. The alternative replay builds each public payout and catalog text in its own Source snapshot. Generate tables with the frozen 80-map normal-rate cohort.

```sh
python3 tools/balance-lab/bargain_receipt_report.py traces/receipt-*.jsonl \
  --payout 29 --farm-reference stock-routes.jsonl \
  --output settlements.csv --summary summary.json
```
