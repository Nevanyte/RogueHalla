# Pilot 2: the same 40 matches on integration 88, with a time budget (2026-11-23)

The second measurement of the balance patch ([plan](../../../backlog/BALANCE_LAB_PATCH.md), "Pilot"). It reruns the
[first pilot](../pilot/README.md) on a test bot that fights from where a camp can reach it, casts four quest spells and
prices a quest walk once, and it adds what the first could not show: where a seat's time goes, and the same numbers by
faction. It is still a table to learn from, not one to retune from. Read "What this does not establish" before using any
figure.

## What was run

- **Tree:** integration 88, `coord/int-88` @ `3618186b1` (merged as `feature/unity-rework` @ `35d15b0b2`). Content hash
  `8a4bf0ce37b406f2`, the same as the first pilot: no game number differs, only the bot and the lab.
- **Matches:** seeds 1 to 40, up to 24,000 ticks (20 minutes), eight `RaidBot` seats, every seat a human seat
  (`bot: false`), one generated map per seed. Every seat starts with fists; a seat's faction rotates with its seat
  number and the seed (64 seats of each faction).
- **Command** (release; `report.json` here is its standard output):
  `balance_lab --human-seats --seeds 1..41 --max-ticks 24000 --jobs 6 --seat 0=raid … --seat 7=raid`
- **Cost:** 347 s at six jobs; peak memory 330 MB.
- **Determinism:** the same 40 seeds run one report each give the same state hash per seed, and their camps and deaths
  sum exactly to the 40-seed report.
- **Evaluator:** `eval.json` is `balance_eval` on that report with `tools/balance-lab/inputs.json`, from the same tree.
- The report is byte-identical to a run on `35ed9690d`, the tree before the last packet (P-RAID-Q3b-fix2): its cap
  changed no match in these seeds.
- **Known defect on this tree:** Smite and Turret can be cast before the seat stands where the elite can reach it
  (the merge review's note; fixed by P-RAID-S-fix2, not in this tree). The run has 25 Turret casts and no Smite.

## Against the first pilot

| | First pilot (integration 87) | This run |
|---|---|---|
| Camps cleared per player-minute | 0.63 | 0.68 |
| Stat stacks per living seat at minutes 5, 8, 12 | 34.5, 75.2, 124.5 | 57.1, 103.8, 163.8 |
| First hit to camp cleared, seconds | 29.9 | 24.3 |
| Quests played; quest accepts | 14; 1,059 | 15; 1,218 |
| Let Him Cook, completed of attempts | 73 of 189 | 120 of 121 |
| Elite attempts; kills; attempts left | 1,133; 539; 338 | 1,281; 766; 177 |
| Wand in hand: elite kills of attempts | 181 of 475 | 414 of 624 |
| Chests offered; offers of the 14 new augments | 904; 138 | 1,290; 216 |
| Matches with a winner; mean length, minutes | 28; 17.4 | 34; 16.3 |
| Deaths (by a player; by the closing hazard) | 265 (138; 102) | 276 (172; 89) |

## What it shows

1. **One faction farms at twice the rate of the others.** A Fire seat clears 1.37 camps per living minute; Plague
   0.66, Frost 0.54, Holy 0.46, Sentenced 0.40. Fire holds 100 stat stacks at minute 5 against 40 to 56, takes 473 of
   the 775 elite kills, and is 22 of the 32 seats alive when a match is decided. It is not where a seat spawns: at
   seven of the eight seat numbers Fire clears 1.8 to 2.3 times what the best other faction does, and at the eighth
   they are level (eight seats a cell). What the code offers as causes, none of them measured apart: Fire's speed out
   of combat ramps to 1.5 times over 20 s and its next hit after a full ramp adds 50 plus 0.4 of bonus Offense
   (`systems/fire_roam.rs`); its attacks burn (`systems/fire.rs`); and the bot's own fight prices read a faction
   match-up table (`raid/exec/farm.rs`, `exec/elite.rs`). In the first five minutes, on six seeds of an earlier tree
   of this integration, a Fire seat's normal camp took a median 8.4 s against 12.8 to 20.0 s for the others.
2. **The workbook's farm rate is out of reach for four factions even with no walking.** The average seat clears
   0.68 camps per living minute, 0.83 per minute that is not spent on a quest or a player, and 1.67 per minute
   actually spent fighting camps. The workbook assumes 2.9: a camp every 21 s, walk included. Here a normal camp
   takes 23.7 s from first hit to cleared, the walk 8.9 s and the heal-up 5.0 s. So the gap to 2.9 is not mainly
   idleness: only 3.6% of living time is standing at full health.
3. **Where the time goes.** Of a living seat's time: fighting normal camps 28.6%, elite camps 11.9%, moving with no
   fight and no quest 24.2%, quests 16.6%, standing below full health 13.4%, standing at full health 3.6%, fighting
   players 1.7%. Quests take 37% of the first five minutes and almost nothing after. From minute 11 a living seat
   stands below full health 30% of the time, and 63% of all seat time is dead or eliminated. The camp bucket holds
   36 s per normal camp credited against 23.7 s from first hit to cleared: about a third of it is not explained by
   credited clears (fights broken off, fights another seat got the credit for, and the 3 s tail).
4. **Quests:** 15 of 25 are played, nearly all to completion. Two Banners is accepted 104 times of 136 dealt and
   Trade Up 144 of 144, both always completed. Hold the Fort is accepted 10 times of 152 (32 in the first pilot):
   the bot now prices its measured death row (9 of 32) and mostly declines. Watchtowers is no longer accepted (8
   before) and Letters falls to 8 (21). By a reading of the code, not a measurement: a spell reward is refused once
   two spell slots are spoken for, and more quests now compete for the two. Ten quests have no executor or are never taken (four kill quests, three PvP or
   event quests, Whispers, Feed or Hunt, Watchtowers).
5. **Elites:** 766 kills in 1,281 attempts, 3 deaths, 177 attempts left (338 before). The ranged-reach fix is most
   of the change: a wand seat kills 414 of 624 (181 of 475 before). The credited killer picked from its chest in 719
   of 766 kills.
6. **New augments:** offered 0 to 27 times each in 1,290 chests (Glacier and Trophy Hunter never), 216 offers in
   all. Still almost no data; the evaluator's rows for them are model estimates. Measuring them needs granted cards
   (P-BAL-LAB-2).
7. **Evaluator, five quests with a measured value.** At the measured rate all five read above the band (1.3 to 7.7
   times their price); at the workbook's 2.9 Let Him Cook is inside it (1.18) and the other four above (1.7 to
   2.2). Both columns move with the farm rate, and finding 1 says the rate depends on the faction.
8. **Spread:** a match's mean stacks per seat at minute 12 has a standard deviation of 55 across seeds (44 before):
   an unpaired comparison of one seat would need about 2,680 seeds per arm for one camp-equivalent. Comparisons must
   be made inside one match (P-BAL-LAB-2).

## Corrections to the first pilot's page

- **Let Him Cook's 108 timer ends of 189 attempts were the test bot, not the quest.** The bot accepted the quest at
  one price for the walk's danger and priced the same walk as deadly on its next think, so it stood still until the
  timer ran out (P-RAID-Q3b-fix). With one price it completes 120 of 121. The first page's evaluator row for it (1.50
  at the measured rate, 0.40 at 2.9) was measured under that error; this run reads 4.15 and 1.18.
- **Hold the Fort's 9 deaths in 32** stand as measured there, and are now an input: the bot's price for a hold reads
  that row. The 2 deaths in 10 here are partly a consequence of it and must not be read as a fresh measurement of the
  quest.
- The first page's item 1 lists the ranged leash as a possible cause of the low farm rate. It was not: normal camps
  did not show the trap (P-RAID-REACH-1's measurement). On this tree elite kills rose by 227 and the farm rate moved
  from 0.63 to 0.68, for several reasons at once.

## How to read the tables

- **Time buckets.** A seat is "fighting" for 3 s after the last hit it dealt or took; a turret's hits count as its
  owner's for the turret's 25 s; a blocked attack opens no window; "moving" does not tell a walk to a fight from
  roaming; a seat that cannot act for any reason reads as standing. The elite bucket includes the adds after the
  elite is dead.
- **By faction.** Each faction has 64 seats over 40 maps; one seat's result depends on the seven others in its
  match. There is no table by weapon: a seat's row carries its last weapon, which is fists for every seat that died.
- **Elite rows.** `Left` is the lab's rule (no hit dealt or taken against that elite's camp for 10 s), not the bot
  giving up. 775 elite kills were credited by the sim and 766 are booked to an attempt: nine went to a seat whose
  attempt had already closed.
- **Evaluator.** "At 2.9" is the workbook's design assumption, not a measurement. A spell reward has no measured
  value, so ten played quests read "not measured".

## What this does not establish

- **Anything about players.** Bots in human seats are not players; the bot does not dodge, kite or use its Wish in a
  camp fight, and human-seat rules change kill accounting.
- **Why Fire farms faster**, and whether a person playing another faction farms faster than this bot does. The three
  candidates above are read from the code. Until that is known, the farm rate that should price a quest's time is
  open, and no quest number should move.
- **Card strength.** Pick rates follow the bot's own price table, written by hand. "Held by the winner" mostly says
  which faction won.
- **The value of any spell reward.** Four spells are cast (92 dashes and 25 turrets here; Smite and Cleanse were never
  cast in 40 matches), fifteen have no rule yet.
- **Elite difficulty by kind or weapon.** The bot leaves a fight it is losing, so its death rate says nothing about
  the 10 to 20% band, which is defined for the bench's fighter that does not retreat.
- **The late game.** No seat goes to the coliseum on purpose; 89 of 276 deaths are to the closing hazard.

## Tables

Generated from `report.json`, `eval.json` and the 40 one-seed reports (not committed; reproduce them with the command
above and `--seeds k..k+1 --jobs 1`).

- Matches: 40; ended with a winner 34, cut at the tick cap 6; length 11.2 to 20.0 minutes, mean 16.3.
- Player-minutes alive: 3447. Content hash 8a4bf0ce37b406f2.

### Farming

| Number | Workbook (assumed) | Measured |
|---|---|---|
| Camps cleared per player-minute | 2.9 | 0.68 (2339 camps, 701 of them elite camps) |
| First hit to camp cleared, seconds | 9.5 to 16.5 | 24.3 (includes time away healing, and elite camps) |
| Heal-up after a camp, seconds | 0 (unmeasured) | 5.0 |
| Walk to the next camp, seconds | 6 | 8.9 |
| Stat stacks per living seat at minute 5 | 144 (12 camps) | 57.1 (4.8 camps' worth; 291 seats alive) |
| Stat stacks per living seat at minute 8 | - | 103.8 (8.7 camps' worth; 215 seats alive) |
| Stat stacks per living seat at minute 12 | - | 163.8 (13.6 camps' worth; 112 seats alive) |

### Where a seat's time goes

Share of a living seat's time (a fight bucket holds 3 s after the last hit dealt or taken). Dead or eliminated seats take 34% of all seat time: 6% in minutes 0 to 5, 32% in minutes 5 to 11, 63% from minute 11.

| Bucket | Whole match | Minutes 0 to 5 | Minutes 5 to 11 | Minute 11 on |
|---|---|---|---|---|
| Fighting a normal camp | 28.6% | 22.9% | 39.9% | 18.8% |
| Fighting an elite camp | 11.9% | 5.5% | 16.4% | 18.1% |
| On a quest, moving | 14.9% | 33.6% | 0.3% | 0.3% |
| On a quest, standing | 1.7% | 3.4% | 0.4% | 0.3% |
| Fighting a player | 1.7% | 1.7% | 2.1% | 0.9% |
| Moving, no fight and no quest | 24.2% | 26.6% | 21.9% | 22.9% |
| Standing below full health | 13.4% | 4.9% | 15.3% | 29.9% |
| Standing at full health | 3.6% | 1.3% | 3.7% | 8.8% |
| Living seat-minutes | 3447 | 1507 | 1313 | 626 |

- Normal camps: 1638 credited, 23.7 s from first hit to cleared; the camp bucket holds 36.1 s per camp credited.
- Elite camps: 701 cleared, 25.6 s from first hit to cleared; the elite bucket holds 35.2 s per camp cleared. Elite kills credited: 775.
- Camps per minute, three ways: per living minute 0.68; per minute not on a quest and not fighting a player 0.83; per minute in a camp or elite fight 1.67.

| Faction | Seats | Camps per living minute | Normal camps | Elite camps | Stacks at minute 5 | Fighting camps or elites | Standing below full health | Deaths per seat |
|---|---|---|---|---|---|---|---|---|
| Fire | 64 | 1.37 | 486 | 404 | 100.6 | 39% | 11% | 0.62 |
| Frost | 64 | 0.54 | 352 | 70 | 46.2 | 42% | 15% | 0.92 |
| Holy | 64 | 0.46 | 266 | 25 | 46.0 | 45% | 13% | 1.00 |
| Plague | 64 | 0.66 | 304 | 156 | 56.2 | 40% | 11% | 0.83 |
| Sentenced | 64 | 0.40 | 230 | 46 | 39.7 | 36% | 16% | 0.94 |

- Elite kills credited, by faction: Fire 473, Frost 62, Holy 14, Plague 201, Sentenced 25.
- Seats alive when a match was decided, by faction: Fire 22, Frost 1, Holy 0, Plague 6, Sentenced 3.

### Deaths

- 276 deaths in 3447 player-minutes (8.0 per 100): player 172, elite 1, mob 14, hazard 89.
- Minutes 0 to 5: 29 deaths in 1507 player-minutes (1.9 per 100), 0 of them to a hazard.
- Minutes 5 to 11: 128 deaths in 1313 player-minutes (9.7 per 100), 0 of them to a hazard.
- Minutes 11 to 20: 119 deaths in 626 player-minutes (19.0 per 100), 89 of them to a hazard.

### Quests

| Quest | Dealt | Accepted | Attempts | Completed | Timer ran out | Died | Other | Mean seconds |
|---|---|---|---|---|---|---|---|---|
| a_cruel_joke | 104 | 51 | 51 | 51 | 0 | 0 | 0 | 24.4 |
| barrow_run | 120 | 46 | 46 | 45 | 1 | 0 | 0 | 18.1 |
| ember_walk | 168 | 97 | 97 | 97 | 0 | 0 | 0 | 9.7 |
| even_skeletons_get_cold | 112 | 112 | 112 | 112 | 0 | 0 | 0 | 16.0 |
| hold_the_fort | 152 | 10 | 10 | 7 | 0 | 2 | 1 | 142.5 |
| let_him_cook | 120 | 120 | 121 | 120 | 1 | 0 | 0 | 58.8 |
| letters | 120 | 8 | 8 | 8 | 0 | 0 | 0 | 19.5 |
| lost_brother | 128 | 100 | 100 | 99 | 1 | 0 | 0 | 38.2 |
| pilgrims_path | 104 | 32 | 32 | 32 | 0 | 0 | 0 | 19.4 |
| smugglers_run | 120 | 75 | 76 | 75 | 1 | 0 | 0 | 38.6 |
| stay_a_while | 136 | 51 | 51 | 51 | 0 | 0 | 0 | 34.5 |
| the_bargain | 160 | 124 | 124 | 124 | 0 | 0 | 0 | 0.9 |
| trade_up | 144 | 144 | 144 | 144 | 0 | 0 | 0 | 48.3 |
| tuber_eats | 144 | 144 | 144 | 144 | 0 | 0 | 0 | 19.7 |
| two_banners | 136 | 104 | 104 | 104 | 0 | 0 | 0 | 22.5 |

Never accepted: a_bone_to_pick, bounty_hunter, elite_slayer, feed_or_hunt, gatecrasher, rime_hunt, the_burning_crown, the_warden, watchtowers, whispers_in_the_brush.
Accepts by minute: 1218 in all; minute 0: 458, minute 1: 408, later: 352. Spell casts: 117.

### Elites

| Elite | Attempts | Killed | Taken by another | Died | Died elsewhere | Left | Open | Kill seconds (median) | Killer picked |
|---|---|---|---|---|---|---|---|---|---|
| Archon | 193 | 87 | 72 | 0 | 9 | 25 | 0 | 14.2 | 83 of 87 |
| AshDrake | 61 | 55 | 2 | 0 | 0 | 4 | 0 | 4.5 | 53 of 55 |
| AvalancheTroll | 26 | 23 | 0 | 0 | 0 | 3 | 0 | 10.8 | 22 of 23 |
| BloodSaint | 2 | 0 | 0 | 0 | 0 | 2 | 0 | - | - |
| Executioner | 241 | 109 | 72 | 0 | 16 | 44 | 0 | 13.9 | 94 of 109 |
| Gaoler | 160 | 109 | 22 | 0 | 7 | 21 | 1 | 13.5 | 100 of 109 |
| GlacialWarden | 95 | 78 | 7 | 0 | 4 | 6 | 0 | 8.9 | 76 of 78 |
| PoxBehemoth | 153 | 80 | 30 | 3 | 6 | 34 | 0 | 16.6 | 78 of 80 |
| RatOgre | 259 | 148 | 54 | 0 | 26 | 30 | 1 | 13.9 | 139 of 148 |
| Wyrmling | 91 | 77 | 3 | 0 | 3 | 8 | 0 | 4.8 | 74 of 77 |

| Weapon in hand | Attempts | Killed | Taken by another | Died | Died elsewhere | Left | Open | Kill seconds (median) | Killer picked |
|---|---|---|---|---|---|---|---|---|---|
| axe | 45 | 12 | 10 | 1 | 7 | 15 | 0 | 27.6 | 10 of 12 |
| bow | 411 | 286 | 61 | 0 | 18 | 45 | 1 | 11.0 | 265 of 286 |
| fists | 44 | 6 | 18 | 0 | 2 | 18 | 0 | 31.7 | 5 of 6 |
| shield | 24 | 5 | 6 | 0 | 3 | 10 | 0 | 38.5 | 5 of 5 |
| sword | 133 | 43 | 37 | 2 | 19 | 32 | 0 | 26.6 | 40 of 43 |
| wand | 624 | 414 | 130 | 0 | 22 | 57 | 1 | 11.3 | 394 of 414 |

### Chests and cards

- Chests offered (per seat): 1290. Picks: 1189 named (card 1036, kill 153), 70 unnamed, 1259 pick events in the sim.
- Winners: 34.

| Card | New native | Offered | Picked | Pick rate | Held by the winner |
|---|---|---|---|---|---|
| wishfulThinking |  | 334 | 40 | 12% | 10 |
| raceToZero |  | 318 | 4 | 1% | 0 |
| siphon |  | 313 | 61 | 19% | 22 |
| eyeForAnEye |  | 239 | 42 | 18% | 19 |
| thirdTimesTheCharm |  | 230 | 80 | 35% | 30 |
| overkill |  | 228 | 85 | 37% | 29 |
| secondWind |  | 206 | 55 | 27% | 25 |
| spikedShell |  | 198 | 63 | 32% | 25 |
| hereComesTheBoom |  | 169 | 70 | 41% | 29 |
| plantYourFeet |  | 154 | 79 | 51% | 31 |
| committed |  | 129 | 78 | 60% | 31 |
| machineGun |  | 129 | 69 | 53% | 28 |
| tankEngine |  | 127 | 62 | 49% | 29 |
| fistsOfFury |  | 118 | 56 | 47% | 25 |
| hemorrhage |  | 88 | 43 | 49% | 23 |
| pocketSand |  | 68 | 3 | 4% | 1 |
| slowBurn |  | 53 | 17 | 32% | 12 |
| bestOffense |  | 46 | 15 | 33% | 11 |
| bastion | yes | 27 | 13 | 48% | 4 |
| eighthStrike | yes | 27 | 12 | 44% | 8 |
| markedMan | yes | 27 | 18 | 67% | 6 |
| wishEngine | yes | 22 | 5 | 23% | 4 |
| calluses | yes | 21 | 8 | 38% | 5 |
| hazardWalker | yes | 21 | 11 | 52% | 8 |
| gambler |  | 18 | 13 | 72% | 10 |
| overflow | yes | 15 | 6 | 40% | 3 |
| pathfinder | yes | 15 | 11 | 73% | 7 |
| marksmansTally | yes | 14 | 12 | 86% | 8 |
| stoked | yes | 13 | 5 | 38% | 3 |
| executionersLedger | yes | 12 | 0 | 0% | 0 |
| corpseHoarder | yes | 2 | 0 | 0% | 0 |

### Evaluator

What a card from an elite must be worth, in camp-equivalents, at the measured farm rate: alone 3.98, with one helper 2.32, with two 1.80.
At the workbook's rate of 2.9 camps a minute: alone 6.03, with one helper 3.01, with two 2.08.

| Quest | Value | Price | Ratio | Verdict | Value at 2.9 | Price at 2.9 | Ratio at 2.9 | Verdict at 2.9 | Timer that meets the target, s |
|---|---|---|---|---|---|---|---|---|---|
| a_cruel_joke | - | 0.32 | - | not measured | - | 1.36 | - | not measured | 27.3 |
| barrow_run | - | 0.25 | - | not measured | - | 1.05 | - | not measured | 35.8 |
| ember_walk | - | 0.13 | - | not measured | - | 0.54 | - | not measured | 14.4 |
| even_skeletons_get_cold | 1.50 | 0.21 | 7.22 | AboveBand | 1.50 | 0.89 | 1.69 | AboveBand | 26.1 |
| hold_the_fort | - | 2.73 | - | not measured | - | 11.67 | - | not measured | - |
| let_him_cook | 3.21 | 0.77 | 4.15 | AboveBand | 3.88 | 3.30 | 1.18 | InBand | 63.5 |
| letters | - | 0.25 | - | not measured | - | 1.09 | - | not measured | - |
| lost_brother | - | 0.50 | - | not measured | - | 2.15 | - | not measured | 43.2 |
| pilgrims_path | - | 0.25 | - | not measured | - | 1.08 | - | not measured | 21.1 |
| smugglers_run | - | 0.51 | - | not measured | - | 2.18 | - | not measured | 41.4 |
| stay_a_while | - | 0.45 | - | not measured | - | 1.92 | - | not measured | - |
| the_bargain | 2.23 | 1.68 | 1.33 | AboveBand | 2.97 | 1.72 | 1.73 | AboveBand | - |
| trade_up | 4.83 | 0.63 | 7.70 | AboveBand | 5.93 | 2.68 | 2.21 | AboveBand | 57.1 |
| tuber_eats | 1.72 | 0.26 | 6.72 | AboveBand | 2.44 | 1.09 | 2.23 | AboveBand | 22.6 |
| two_banners | - | 0.29 | - | not measured | - | 1.25 | - | not measured | - |

| New native | Value | Value / needed, one helper | Meets the rule | Value at 2.9 | Value / needed at 2.9, one helper | Meets the rule at 2.9 |
|---|---|---|---|---|---|---|
| bastion | 0.76 | 0.33 | no | 1.06 | 0.35 | no |
| calluses | 1.13 | 0.49 | no | 1.13 | 0.38 | no |
| corpseHoarder | 0.60 | 0.26 | no | 0.60 | 0.20 | no |
| eighthStrike | 0.87 | 0.38 | no | 1.78 | 0.59 | no |
| executionersLedger | 0.40 | 0.17 | no | 0.40 | 0.13 | no |
| glacier | 0.80 | 0.34 | no | 0.80 | 0.27 | no |
| hazardWalker | 0.76 | 0.33 | no | 0.91 | 0.30 | no |
| markedMan | 1.18 | 0.51 | no | 5.05 | 1.68 | yes |
| marksmansTally | 1.56 | 0.67 | no | 2.01 | 0.67 | no |
| overflow | 0.35 | 0.15 | no | 0.58 | 0.19 | no |
| pathfinder | 2.42 | 1.04 | yes | 2.86 | 0.95 | yes |
| stoked | 0.69 | 0.30 | no | 0.76 | 0.25 | no |
| trophyHunter | 0.33 | 0.14 | no | 0.33 | 0.11 | no |
| wishEngine | 0.40 | 0.17 | no | 0.48 | 0.16 | no |

### Spread across seeds

One report per seed (40 seeds); each number is a match's mean over its seats.

| Per match | Mean | SD across seeds | Standard error of the mean |
|---|---|---|---|
| camps/min | 0.70 | 0.20 | 0.03 |
| stacks@5 | 56.95 | 24.56 | 3.88 |
| stacks@12 | 174.44 | 55.43 | 9.37 |
| deaths/100min | 8.32 | 1.83 | 0.29 |
| quests done | 30.32 | 7.44 | 1.18 |
| elite kills | 19.15 | 4.72 | 0.75 |

Seeds an unpaired comparison of one seat's stacks at minute 12 would need (80% power, 5% two-sided), taking a seat's SD as the match SD times the square root of eight (157 stacks; seats assumed independent):
- 1 camp-equivalent (12 stacks): 2680 seeds per arm.
- 2 camp-equivalent (24 stacks): 670 seeds per arm.
- 3 camp-equivalent (36 stacks): 298 seeds per arm.
