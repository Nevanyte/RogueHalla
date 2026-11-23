# Pilot: 40 full-length matches of the test bot (2026-11-23)

The first measurement of the balance patch ([plan](../../../backlog/BALANCE_LAB_PATCH.md), "Pilot"). It is a first table
and a check that the lab can measure, not a set of numbers to retune from. Read "What this does not establish" before
using any figure.

## What was run

- **Tree:** `coord/int-87` @ `70c7932f1` (integration 87). Content hash `8a4bf0ce37b406f2`.
- **Matches:** seeds 1 to 40, up to 24,000 ticks (20 minutes), eight `RaidBot` seats, every seat a human seat
  (`bot: false`), one generated map per seed.
- **Command** (release; `report.json` here is its standard output):
  `balance_lab --human-seats --seeds 1..41 --max-ticks 24000 --jobs 6 --seat 0=raid … --seat 7=raid`
- **Cost:** 529 s at six jobs on a busy machine; peak memory 283 MB.
- **Determinism:** the same 40 seeds run one report each give the same state hash per seed, and their camps, deaths and
  pick events sum exactly to the 40-seed report.
- **Evaluator:** `eval.json` is `balance_eval` on that report with `tools/balance-lab/inputs.json`, built from
  P-BAL-EVAL-2 (`agent/sonnet/P-BAL-EVAL-2` @ `2022b8604`, in the integration 88 preview). It prints every verdict at
  the measured farm rate and at the workbook's 2.9 camps a minute; the second is a design assumption, not a measurement.
- No quest spell is cast on this tree (P-RAID-S is in integration 88).

## What it shows

1. **The test bot farms at about a fifth of the workbook's rate**: 0.63 camps per player-minute against 2.9, and 34
   stat stacks per seat at minute 5 against 144. Every price in the value model is time at the farm rate, so every quest
   verdict moves with this number: Even Skeletons Get Cold reads 7.8 times its price at the bot's rate and 1.7 at the
   workbook's; Let Him Cook goes from above the band (1.50) to below it (0.40). **No quest number should move until
   the farm rate is understood.** What is known about it:
   - The lab's 29.9 s from first hit to camp cleared includes time away healing and 483 elite camps of 2,414.
   - The existing camp bench (an unbuilt scripted fighter that never retreats or heals, 600 normal-camp fights on the
     integration 86 tree) cleared 86% and died in 14%, with a median clear of 9 to 25 s by zone including a 10-tile
     approach. So 9.5 to 16.5 s per kill with no heal-up, the workbook's line, is the optimistic end.
   - Ranged seats can park outside a camp's leash, so the target walks home and heals (P-RAID-REACH-1). Whether that
     slows normal camps too is being measured.
2. **Quests:** 14 of 25 are played. Eleven are never accepted: four kill quests, three PvP or event quests and Whispers
   have no executor yet; Feed or Hunt has none either; Two Banners and Trade Up are P-RAID-Q3b. Most played quests
   complete nearly every time. Two stand out: Let Him Cook ran out of time in 108 of 189 attempts, and Hold the Fort
   killed the seat in 9 of 32. Watchtowers (8 accepts of 112 deals) and Letters (21 of 120) are low. By a reading of the
   code, not a measurement: their reward is a spell and the bot refuses a spell quest once two slots are spoken for; it
   never casts on this tree, so the first two spell rewards hold the slots all match; their price rows are also low.
3. **Elites:** 1,133 attempts, 539 kills, 7 deaths. The bot leaves a fight it is losing, so its death rate says nothing
   about the 10 to 20% band, which is defined for the bench's fighter that does not retreat. The credited killer picked
   from its chest in 520 of 539 kills.
4. **New augments:** the 14 were offered 0 to 18 times each in 904 chests seen (Glacier never), 138 offers in all. The
   lab has almost no data on them, and by the evaluator one of the 14 is worth what an elite costs with one helper at the
   measured rate (two at the workbook's rate). Measuring them needs granted cards (P-BAL-LAB-2).
5. **Matches:** 28 of 40 end with a winner; the other 12 run to the cap. 102 of 265 deaths are to the closing hazard
   after minute 11: the bot has no coliseum goal yet (P-RAID-P).
6. **Spread:** a match's mean stacks per seat at minute 12 has a standard deviation of 44 across seeds, about 3.6 camps'
   worth. Taking a seat's as that times the square root of eight, an unpaired comparison of one seat would need about
   1,660 seeds per arm to see one camp-equivalent, 415 for two and 185 for three: far more than the plan's guess of 140,
   35 and 16, which assumed pairing. A match diverges as soon as one seat acts differently, so pairing across matches
   may not recover that. P-BAL-LAB-2 should compare granted and ungranted seats inside the same match and report
   per-seat rows; its own pilot sets the count.

## How to read the elite rows

From the Opus merge review of integration 87:

- `Left` is the lab's rule (no hit dealt or taken against that elite's camp for 10 s), not the bot giving up. For wand
  and bow seats it is largely time spent leashed while the bot still holds the fight.
- For ranged seats, attempts are inflated by re-engaging after a leash, and kill seconds cover the last streak only.
- `Killed` and the chest columns miss a kill credited to a seat whose attempt had already closed. How many is not
  measured: the report has no count of elite deaths to compare with the 539 kills booked. (483 elite camps were fully
  cleared, fewer than the kills, because a camp counts as cleared only once its adds are dead too.)
- Reports from integration 86 or earlier are not comparable: `died` and `taken by another` are narrower now, and chest
  offers and picks are counted once per chest (they were both inflated and swallowed before).

## What this does not establish

- **Anything about players.** Bots in human seats are not players, the bot does not dodge, and human-seat rules change
  kill accounting.
- **Card strength.** Pick rates follow the bot's own price table (`raid/values.rs`), which was written by hand. "Held by
  the winner" is weak evidence: most winners are the last seat alive after the hazard closes.
- **The value of any spell reward** (no cast on this tree, and no duel equivalent in the inputs), and so the verdict of
  the ten played quests whose reward is a spell.
- **Elite difficulty by kind or weapon.** Wand and bow rows carry the leash effect; per-kind cells are small.
- **The late game.** No seat goes to the coliseum on purpose.
- **Whether the bot's farm rate is the bot or the game.** That is the next thing to settle.

## Tables

Generated from `report.json`, `eval.json` and the 40 one-seed reports (not committed; reproduce them with the command
above and `--seeds k..k+1 --jobs 1`).

- Matches: 40; ended with a winner 28, cut at the tick cap 12; length 11.3 to 20.0 minutes, mean 17.4.
- Player-minutes alive: 3819. Content hash 8a4bf0ce37b406f2.

### Farming

| Number | Workbook (assumed) | Measured |
|---|---|---|
| Camps cleared per player-minute | 2.9 | 0.63 (2414 camps, 483 of them elite camps) |
| First hit to camp cleared, seconds | 9.5 to 16.5 | 29.9 (includes time away healing, and elite camps) |
| Heal-up after a camp, seconds | 0 (unmeasured) | 5.0 |
| Walk to the next camp, seconds | 6 | 7.8 |
| Stat stacks per living seat at minute 5 | 144 (12 camps) | 34.5 (2.9 camps' worth; 283 seats alive) |
| Stat stacks per living seat at minute 8 | - | 75.2 (6.3 camps' worth; 240 seats alive) |
| Stat stacks per living seat at minute 12 | - | 124.5 (10.4 camps' worth; 148 seats alive) |

### Deaths

- 265 deaths in 3819 player-minutes (6.9 per 100): player 138, elite 3, mob 22, hazard 102.
- Minutes 0 to 5: 37 deaths in 1499 player-minutes (2.5 per 100), 0 of them to a hazard.
- Minutes 5 to 11: 83 deaths in 1435 player-minutes (5.8 per 100), 0 of them to a hazard.
- Minutes 11 to 20: 145 deaths in 886 player-minutes (16.4 per 100), 102 of them to a hazard.

### Quests

| Quest | Dealt | Accepted | Attempts | Completed | Timer ran out | Died | Other | Mean seconds |
|---|---|---|---|---|---|---|---|---|
| a_cruel_joke | 104 | 65 | 65 | 65 | 0 | 0 | 0 | 24.6 |
| barrow_run | 120 | 55 | 55 | 54 | 1 | 0 | 0 | 20.1 |
| ember_walk | 168 | 115 | 115 | 115 | 0 | 0 | 0 | 10.4 |
| even_skeletons_get_cold | 112 | 112 | 112 | 112 | 0 | 0 | 0 | 15.8 |
| hold_the_fort | 152 | 32 | 32 | 21 | 0 | 9 | 2 | 187.1 |
| let_him_cook | 120 | 110 | 189 | 73 | 108 | 7 | 1 | 59.2 |
| letters | 120 | 21 | 21 | 21 | 0 | 0 | 0 | 19.3 |
| lost_brother | 128 | 108 | 108 | 108 | 0 | 0 | 0 | 38.4 |
| pilgrims_path | 104 | 65 | 65 | 65 | 0 | 0 | 0 | 19.2 |
| smugglers_run | 120 | 90 | 90 | 90 | 0 | 0 | 0 | 38.9 |
| stay_a_while | 136 | 28 | 28 | 28 | 0 | 0 | 0 | 31.7 |
| the_bargain | 160 | 106 | 106 | 106 | 0 | 0 | 0 | 0.9 |
| tuber_eats | 144 | 144 | 144 | 144 | 0 | 0 | 0 | 19.7 |
| watchtowers | 112 | 8 | 8 | 8 | 0 | 0 | 0 | 14.3 |

Never accepted: a_bone_to_pick, bounty_hunter, elite_slayer, feed_or_hunt, gatecrasher, rime_hunt, the_burning_crown, the_warden, trade_up, two_banners, whispers_in_the_brush.
Accepts by minute: 1059 in all; minute 0: 548, minute 1: 316, later: 195. Spell casts: 0.

### Elites

| Elite | Attempts | Killed | Taken by another | Died | Died elsewhere | Left | Open | Kill seconds (median) | Killer picked |
|---|---|---|---|---|---|---|---|---|---|
| Archon | 226 | 71 | 59 | 1 | 16 | 79 | 0 | 13.1 | 66 of 71 |
| AshDrake | 57 | 45 | 8 | 0 | 0 | 4 | 0 | 7.5 | 41 of 45 |
| AvalancheTroll | 12 | 8 | 2 | 0 | 0 | 2 | 0 | 10.3 | 8 of 8 |
| BloodSaint | 1 | 0 | 0 | 0 | 0 | 1 | 0 | - | - |
| Executioner | 245 | 100 | 47 | 1 | 23 | 73 | 1 | 16.3 | 98 of 100 |
| Gaoler | 140 | 68 | 5 | 0 | 8 | 58 | 1 | 17.4 | 66 of 68 |
| GlacialWarden | 78 | 55 | 6 | 0 | 2 | 15 | 0 | 11.3 | 55 of 55 |
| PoxBehemoth | 156 | 64 | 22 | 3 | 6 | 61 | 0 | 17.3 | 61 of 64 |
| RatOgre | 162 | 97 | 25 | 2 | 8 | 30 | 0 | 16.1 | 94 of 97 |
| Wyrmling | 56 | 31 | 4 | 0 | 5 | 15 | 1 | 5.2 | 31 of 31 |

| Weapon in hand | Attempts | Killed | Taken by another | Died | Died elsewhere | Left | Open | Kill seconds (median) | Killer picked |
|---|---|---|---|---|---|---|---|---|---|
| axe | 67 | 25 | 13 | 3 | 5 | 21 | 0 | 27.9 | 24 of 25 |
| bow | 407 | 264 | 44 | 1 | 25 | 72 | 1 | 11.2 | 258 of 264 |
| shield | 58 | 25 | 10 | 0 | 4 | 19 | 0 | 25.5 | 23 of 25 |
| sword | 126 | 44 | 22 | 2 | 14 | 44 | 0 | 33.1 | 42 of 44 |
| wand | 475 | 181 | 89 | 1 | 20 | 182 | 2 | 14.7 | 173 of 181 |

### Chests and cards

- Chests offered (per seat): 904. Picks: 859 named (card 732, kill 127), 26 unnamed, 885 pick events in the sim.
- Winners: 28.

| Card | New native | Offered | Picked | Pick rate | Held by the winner |
|---|---|---|---|---|---|
| siphon |  | 232 | 46 | 20% | 11 |
| wishfulThinking |  | 223 | 20 | 9% | 5 |
| raceToZero |  | 211 | 3 | 1% | 0 |
| thirdTimesTheCharm |  | 191 | 57 | 30% | 11 |
| overkill |  | 176 | 67 | 38% | 13 |
| eyeForAnEye |  | 148 | 27 | 18% | 9 |
| secondWind |  | 143 | 33 | 23% | 13 |
| spikedShell |  | 121 | 41 | 34% | 16 |
| hereComesTheBoom |  | 110 | 50 | 45% | 13 |
| tankEngine |  | 108 | 56 | 52% | 13 |
| plantYourFeet |  | 103 | 65 | 63% | 18 |
| committed |  | 90 | 57 | 63% | 15 |
| machineGun |  | 86 | 46 | 53% | 13 |
| fistsOfFury |  | 81 | 47 | 58% | 11 |
| hemorrhage |  | 55 | 27 | 49% | 10 |
| pocketSand |  | 34 | 3 | 9% | 0 |
| slowBurn |  | 34 | 7 | 21% | 4 |
| bestOffense |  | 21 | 3 | 14% | 2 |
| executionersLedger | yes | 18 | 5 | 28% | 1 |
| markedMan | yes | 16 | 13 | 81% | 5 |
| marksmansTally | yes | 16 | 10 | 62% | 4 |
| eighthStrike | yes | 14 | 7 | 50% | 3 |
| wishEngine | yes | 14 | 5 | 36% | 1 |
| bastion | yes | 13 | 6 | 46% | 1 |
| overflow | yes | 11 | 6 | 55% | 3 |
| calluses | yes | 9 | 3 | 33% | 1 |
| pathfinder | yes | 9 | 6 | 67% | 3 |
| gambler |  | 8 | 6 | 75% | 2 |
| hazardWalker | yes | 8 | 4 | 50% | 0 |
| stoked | yes | 6 | 4 | 67% | 0 |
| corpseHoarder | yes | 3 | 1 | 33% | 0 |
| trophyHunter | yes | 1 | 1 | 100% | 0 |

### Evaluator

What a card from an elite must be worth, in camp-equivalents, at the measured farm rate: alone 3.63, with one helper 2.21, with two 1.76.
At the workbook's rate of 2.9 camps a minute: alone 5.80, with one helper 2.93, with two 2.05.

| Quest | Value | Price | Ratio | Verdict | Value at 2.9 | Price at 2.9 | Ratio at 2.9 | Verdict at 2.9 | Timer that meets the target, s |
|---|---|---|---|---|---|---|---|---|---|
| a_cruel_joke | - | 0.30 | - | not measured | - | 1.37 | - | not measured | 28.2 |
| barrow_run | - | 0.25 | - | not measured | - | 1.15 | - | not measured | 35.8 |
| ember_walk | - | 0.13 | - | not measured | - | 0.58 | - | not measured | 15.7 |
| even_skeletons_get_cold | 1.50 | 0.19 | 7.81 | AboveBand | 1.50 | 0.88 | 1.70 | AboveBand | 25.8 |
| hold_the_fort | - | 3.55 | - | not measured | - | 16.29 | - | not measured | - |
| let_him_cook | 3.21 | 2.15 | 1.50 | AboveBand | 3.98 | 9.84 | 0.40 | BelowBand | 64.5 |
| letters | - | 0.23 | - | not measured | - | 1.07 | - | not measured | - |
| lost_brother | - | 0.47 | - | not measured | - | 2.14 | - | not measured | 43.0 |
| pilgrims_path | - | 0.23 | - | not measured | - | 1.06 | - | not measured | 21.9 |
| smugglers_run | - | 0.47 | - | not measured | - | 2.16 | - | not measured | 41.4 |
| stay_a_while | - | 0.38 | - | not measured | - | 1.76 | - | not measured | - |
| the_bargain | 2.23 | 1.68 | 1.33 | AboveBand | 3.08 | 1.72 | 1.79 | AboveBand | - |
| tuber_eats | 1.73 | 0.24 | 7.25 | AboveBand | 2.55 | 1.09 | 2.33 | AboveBand | 22.4 |
| watchtowers | - | 0.17 | - | not measured | - | 0.79 | - | not measured | 18.9 |

| New native | Value | Value / needed, one helper | Meets the rule | Value at 2.9 | Value / needed at 2.9, one helper | Meets the rule at 2.9 |
|---|---|---|---|---|---|---|
| bastion | 0.81 | 0.37 | no | 1.16 | 0.40 | no |
| calluses | 1.22 | 0.55 | no | 1.22 | 0.42 | no |
| corpseHoarder | 0.65 | 0.29 | no | 0.65 | 0.22 | no |
| eighthStrike | 0.94 | 0.42 | no | 2.00 | 0.68 | no |
| executionersLedger | 0.43 | 0.20 | no | 0.43 | 0.15 | no |
| glacier | 0.86 | 0.39 | no | 0.86 | 0.29 | no |
| hazardWalker | 0.82 | 0.37 | no | 0.99 | 0.34 | no |
| markedMan | 1.21 | 0.55 | no | 5.54 | 1.89 | yes |
| marksmansTally | 1.68 | 0.76 | no | 2.20 | 0.75 | no |
| overflow | 0.38 | 0.17 | no | 0.64 | 0.22 | no |
| pathfinder | 2.61 | 1.18 | yes | 3.13 | 1.07 | yes |
| stoked | 0.74 | 0.34 | no | 0.83 | 0.28 | no |
| trophyHunter | 0.36 | 0.16 | no | 0.36 | 0.12 | no |
| wishEngine | 0.43 | 0.20 | no | 0.52 | 0.18 | no |

### Spread across seeds

One report per seed (40 seeds); each number is a match's mean over its seats.

| Per match | Mean | SD across seeds | Standard error of the mean |
|---|---|---|---|
| camps/min | 0.64 | 0.16 | 0.03 |
| stacks@5 | 34.77 | 8.41 | 1.33 |
| stacks@12 | 129.14 | 43.57 | 7.16 |
| deaths/100min | 7.35 | 2.22 | 0.35 |
| quests done | 25.25 | 6.22 | 0.98 |
| elite kills | 13.47 | 5.59 | 0.88 |

Seeds an unpaired comparison of one seat's stacks at minute 12 would need (80% power, 5% two-sided), taking a seat's SD as the match SD times the square root of eight (123 stacks; seats assumed independent):
- 1 camp-equivalent (12 stacks): 1657 seeds per arm.
- 2 camp-equivalent (24 stacks): 415 seeds per arm.
- 3 camp-equivalent (36 stacks): 185 seeds per arm.
