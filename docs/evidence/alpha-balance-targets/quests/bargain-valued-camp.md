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

## Reproduction

`quest_balance START END --board-only` records the five planning and ten raid offers and exits at raid setup. The first test cohort is every seed in 81–120 with The Bargain on the raid board, fixed before reading outcomes. Its 19 seeds are 81, 82, 83, 84, 85, 88, 89, 94, 95, 98, 101, 103, 105, 107, 108, 109, 111, 115 and 117. The additional confirmation board range is 121–160, with 17 eligible seeds (121, 123, 126, 127, 129, 131, 138, 139, 142, 144, 145, 146, 149, 151, 154, 155 and 156). Neither selection uses quote, travel, completion, payout or later raid outcomes.

Replay each seed to the ordinary 40 Hz end using `quest_balance SEED SEED_PLUS_ONE --bargain-receipts`. The alternative replay builds each public payout and catalog text in its own Source snapshot. Generate tables with the frozen 80-map normal-rate cohort.

```sh
python3 tools/balance-lab/bargain_receipt_report.py traces/receipt-*.jsonl \
  --payout 29 --farm-reference stock-routes.jsonl \
  --output settlements.csv --summary summary.json
```
