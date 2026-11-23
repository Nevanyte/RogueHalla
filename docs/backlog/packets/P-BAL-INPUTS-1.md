# P-BAL-INPUTS-1: the evaluator's inputs file gets every Top 25 reward and every new native augment

- **Serves:** not in the plan: owner approval 2026-11-22 (owner decision row "Balance patch, test bot, per-elite numbers",
  `docs/OWNER_DECISIONS.md` line 10; plan of record `docs/backlog/BALANCE_LAB_PATCH.md` sections 2 and 4)
- **Implementer tier:** implementer-sonnet (the Haiku `implementer` could do it: pure transcription, and the oracle checks
  every number; the one judgement, the `source` strings, is spelled out below, so Sonnet is chosen only for the care a
  36-entry JSON edit needs)
- **Reviewer:** reviewer
- **Integration base:** stacked on `agent/sonnet/P-BAL-EVAL-1` @ `f78ef6694` (not merged yet)
- **D-rules touched:** D1 loose proof (AGENTS.md, owner direction 2026-11-10) · D2 n/a · D5/D6/D9 none (a design-intent
  JSON file; no sim, wire, policy, hash or client change)

## 1. ID and outcome

`balance_eval` prices a quest reward and an augment only from `tools/balance-lab/inputs.json`. That file has the global
blocks and a difficulty class for the 15 timed quests, but no `reward` and `"augments": {}`: on any real report every quest
value is "not measured (no reward descriptor in inputs)" and no augment is evaluated. This packet fills it, from the design
workbook, so that:

- all 25 Top 25 quests have a `reward` descriptor. The seven permanent rewards carry their design numbers and are priced;
  the 18 spell rewards carry kind `spell` and stay "not measured" until P-BAL-LAB-2 supplies a duel equivalence (gap 3 in
  `P-BAL-EVAL-1.md`; **no `duel_equivalent_ce` is invented**, although the workbook has guesses for Δ, see section 3);
- all 14 native augments have an entry, priced against the elite from the workbook's `Augment_Tuning` numbers;
- every reward and augment names its workbook source in a `source` string (the evaluator ignores unknown fields; the
  oracle checks the string exists).

## 2. Dependencies

- P-BAL-EVAL-1 (`agent/sonnet/P-BAL-EVAL-1` @ `f78ef6694`, reviewed LAND WITH NOTES, not merged): the shape this file
  follows (`balance_model.rs` `Reward`, `QuestInput`, `AugmentInput`, lines 69-110 on that branch). Gate:
  `scripts/gate.sh --quick --base agent/sonnet/P-BAL-EVAL-1 --oracle packet/P-BAL-INPUTS-1` until it merges.
- Sibling P-BAL-EVAL-1-fix (same base) makes the global blocks required and may add a one-line note beside Rime Hunt in
  `inputs.json`. This packet edits only the `quests` and `augments` objects (add `reward` keys, add the 10 untimed quest
  entries, fill `augments`); leave every existing `difficulty` and the global blocks as they are.

## 3. Relevant legacy paths

No legacy counterpart (no `packages/` file is read). Sources, all read at `f78ef6694`:

- Workbook `docs/quests/FUTURE_ONLY_Quest_Design_Discussion.xlsx` (FUTURE ONLY; an .xlsx is a zip of XML): sheets `Top_25`
  (rows 5-29, one per quest), `Augment_Tuning` (rows 5-54), `Augment_Value`, `Inputs`, `Formulas`, `Time_Limits`.
  Cell references below are in that file.
- `docs/QUEST_VALUE_MODEL.md` sections 4.1 (permanent buff), 8 (worked example), and the workbook's `Formulas` sheet.
- Shipped numbers: `crates/rh-sim/src/quests/board/tuning.rs` `TOP25` (lines 119-208), `catalog.rs` (`DEFINITIONS`,
  `RewardId::kind`, lines 118-165 and 198+), `crates/rh-sim/src/cards/native/table.rs` (card text, lines 36-105).
- Cross-check only, never read by the evaluator: `raid_values.json` is on commit `7b4d96056` (P-RAID-V), not on this base;
  its 25 reward ids and 14 native augment ids equal the ids below.

### Owner decisions

`docs/OWNER_DECISIONS.md` line 10 (2026-11-22): "Numbers land as measured, with the before/after table"; this packet
changes no game number, only the evaluator's design inputs. No other decided row concerns these inputs (searched
"inputs", "workbook", "quest value", "augment").

### How the model reads a reward (what the shape can say)

- `permanent_buff`: `V = b + (g - 1) * c_eff * (R - tau)` with `b = direct_stacks / 12` (workbook `Top_25` column S, in
  CE, times `s = 12`) and `g = 1 / (1 - kappa * d / (1 + d))` for `damage_fraction d` (column U).
- `spell`: needs `duel_equivalent_ce`, which this packet leaves out.
- Augment: `growth: "none"` with `fight_stacks_now` (F), `one_shot_stacks`, `farm_speed_pct` (J), `income_mult` (K),
  `hazard_mult` (Lh) and `cap_stacks` (informational while growth is `none`). `growth: "measured"` reads a growth rate from
  the lab's native readouts, which are in the card's own units (percent, armor points), not stacks, and the shape has no
  conversion factor. That is why **no augment uses `"measured"`** (decision 3).

## 4. The mapping (workbook row, quest id, reward id, kind, numbers, source)

All rows are `Top_25` (header row 4). `Kind` is the catalog's `RewardId::kind()`; the oracle checks it. `Timed` is
`route_timer_ms.is_some()` in `catalog.rs`. "Δ" is column T (a design guess; not entered). "d" is column U.

### Permanent rewards (kind `permanent_buff`): entered with numbers

| Row | Quest id | Reward id | `direct_stacks` (S x 12) | `damage_fraction` (U) | Workbook V (AF) | Shipped number |
|---|---|---|---|---|---|---|
| 6 | `the_burning_crown` | CrownPower | 48 (S6 = 4) | 0.10 (U6) | 5.246 | `crown_power_cap` 48: same |
| 15 | `the_bargain` | TheBargain | 24 (S15 = 2) | 0.05 (U15) | 2.697 | `bargain_stacks` 24: same |
| 16 | `trade_up` | TradeUp | 54 (S16 = 4.5) | 0.08 (U16) | 5.581 | `trade_up_stacks[2]` 54: same |
| 19 | `let_him_cook` | ChefsKiss | 36 (S19 = 3) | 0.05 (U19) | 3.684 | a 20% share of the lowest stat: see differences |
| 27 | `feed_or_hunt` | FeedOrHuntDamage | 6 (S27 = 0.5) | 0.06 (U27) | 1.502 | `feed_damage_stacks` 6: same |
| 28 | `even_skeletons_get_cold` | WarmCoat | 18 (S28 = 1.5) | 0 (U28) | 1.5 | immunity, no number: see differences |
| 29 | `tuber_eats` | BreakersHelm | 18 (S29 = 1.5) | 0.05 (U29) | 2.153 | `helm_bypass` 0.20: no stack count |

`move_fraction` is 0 for all seven (the workbook has no move column). Entry shape:

```json
"trade_up": { "difficulty": "medium",
  "reward": { "kind": "permanent_buff", "direct_stacks": 54, "damage_fraction": 0.08, "move_fraction": 0,
              "source": "Top_25 row 16: b = S16 4.5 CE x s 12; d = U16" } }
```

### Spell rewards (kind `spell`): entered without a value

Shape: `"reward": { "kind": "spell", "spell_type": "single_use" | "reusable", "source": "Top_25 row N: Δ = T<N> (a design guess, not entered)" }`.
`spell_type` follows the catalog (`RewardKind::SingleUse` or `Reusable`); the evaluator ignores it, P-BAL-LAB-2 will use it
(single-use is an option, section 4.2; reusable is an annuity, 4.3). Add no `duel_equivalent_ce`, no `damage_fraction`.

| Row | Quest id | Reward id | `spell_type` | Timed | Δ (T) | d (U) |
|---|---|---|---|---|---|---|
| 5 | `the_warden` | WardensCage | single_use | no | 2 | 0 |
| 7 | `bounty_hunter` | Hook | reusable | no | 3.5 | 0 |
| 8 | `smugglers_run` | ShoulderCharge | reusable | yes | 2.5 | 0 |
| 9 | `rime_hunt` | Tornado | reusable | yes | 2 | 0.12 |
| 10 | `hold_the_fort` | Turret | reusable | no | 1.5 | 0.15 |
| 11 | `gatecrasher` | RebirthEgg | single_use | no | 2.5 | 0 |
| 12 | `two_banners` | Warhorn | single_use | no | 3 | 0 |
| 13 | `elite_slayer` | Smite | reusable | no | 0.5 | 0.1 |
| 14 | `ember_walk` | SpellShield | reusable | yes | 2 | 0 |
| 17 | `letters` | ForgetMeNotSeed | single_use | no | 2 | 0 |
| 18 | `a_cruel_joke` | SwapStick | single_use | yes | 2 | 0 |
| 20 | `whispers_in_the_brush` | LanternWard | reusable | yes | 0.8 | 0 |
| 21 | `pilgrims_path` | Cleanse | reusable | yes | 2 | 0 |
| 22 | `watchtowers` | ScoutFalcon | reusable | yes | 0.9 | 0 |
| 23 | `stay_a_while` | Stasis | single_use | no | 2 | 0 |
| 24 | `a_bone_to_pick` | Bolas | reusable | yes | 1.8 | 0 |
| 25 | `barrow_run` | WallBuster | single_use | yes | 1.5 | 0 |
| 26 | `lost_brother` | BlindRageAndStillMind | single_use | yes | 2.5 | 0 |

Counts: 7 permanent + 18 spells = 25; 8 single-use and 10 reusable, as the workbook says (`Top_25` A2). Every workbook
quest has a catalog quest and every catalog quest a row: **no open row** (names differ only in punctuation: "A Cruel Joke
(fixed)" is `a_cruel_joke`, "Stay A While (fixed)" is `stay_a_while`, "Feed or Hunt (feed path)" is `feed_or_hunt`).

### Native augments: entered from `Augment_Tuning` (the shipped numbers)

Entry shape (growth is always `none`, `one_shot_stacks` 0):

```json
"pathfinder": { "fight_stacks_now": 48, "one_shot_stacks": 0, "farm_speed_pct": 6, "income_mult": 1, "hazard_mult": 1,
                "cap_stacks": 48, "growth": "none",
                "source": "Augment_Tuning row 8: stacks by coliseum O8 = min(G8 7.2/min x R 7, cap J8 48); farm L8" }
```

`fight_stacks_now` is the workbook's "stacks by coliseum" (column O: `min(cap J, growth I x 7 min)`, 7 = coliseum 12 minus
pick minute 5) for the investment augments, and column H for the others. `cap_stacks` is column J. Columns: F = `fight_stacks_now`,
cap = `cap_stacks`, farm = L, income = M, hazard = N; "V" is column S, "New value (CE)".

| Row | Card id (`CardId::as_str`) | Workbook name | F | cap | farm % | income | hazard | V (S) |
|---|---|---|---|---|---|---|---|---|
| 5 | `markedMan` | Marked Man | 0 | 0 | 0 | 1.5 | 1.5 | 6.2228 |
| 8 | `pathfinder` | Pathfinder | 48 (O8) | 48 | 6 | 1 | 1 | 3.2310 |
| 11 | `marksmansTally` | Marksman's Tally | 30 (O11) | 30 | 6 | 1 | 1 | 2.2455 |
| 16 | `eighthStrike` | Tenth Strike (suggested "every 8th hit") | 12.5 | 0 | 12.5 | 1 | 1 | 1.9093 |
| 26 | `calluses` | Calluses | 23.8 (O26) | 24 | 0 | 1 | 1 | 1.3031 |
| 27 | `bastion` | Bastion | 14 | 0 | 4 | 1 | 1 | 1.1716 |
| 29 | `hazardWalker` | Hazard Walker | 15 (O29) | 15 | 2 | 1 | 1 | 1.0254 |
| 30 | `glacier` | Glacier | 16.8 (O30) | 24 | 0 | 1 | 1 | 0.9199 |
| 34 | `stoked` | Stoked | 14 (O34) | 30 | 1 | 1 | 1 | 0.8690 |
| 37 | `corpseHoarder` | Corpse Hoarder | 12.6 (O37) | 24 | 0 | 1 | 1 | 0.6899 |
| 38 | `overflow` | Overflow | 6 | 0 | 3 | 1 | 1 | 0.6335 |
| 46 | `wishEngine` | Wish Engine (percent) | 8 (O46) | 8 | 1 | 1 | 1 | 0.5405 |
| 49 | `executionersLedger` | Executioner's Ledger | 8.4 (O49) | 30 | 0 | 1 | 1 | 0.4599 |
| 52 | `trophyHunter` | Trophy Hunter | 7 (O52) | 15 | 0 | 1 | 1 | 0.3833 |

All 14 native ids (`CardId::NATIVE`) have a row; none is unmatched. Each `source` string names the sheet, the row and the
columns used (as in the example). The oracle recomputes V from these numbers and the evaluator and compares to column S
within 10%.

## 5. Decisions (each with its reason)

1. **The 10 untimed quests get no `difficulty`.** The timer verdict is the measured completion-time quantile that meets the
   difficulty target, and only route quests have a timer (`route_timer_ms`). Race, event, dwell and decision quests have
   other clocks (`Top_25` K column), and a class on them would make `balance_eval` print a timer verdict for a quest with
   no timer. Their entries hold a `reward` only. (`Top_25` column L does classify them Hard/Medium/Easy; that column sets
   the chance of beating a race or event, not a timer. The oracle pins "difficulty present exactly when timed".)
   Effect to know: if a future lab report carries `complete_secs.samples` for an untimed quest, the evaluator says
   "timer: unknown difficulty" for it. That is accurate.
2. **Inputs describe the reward as shipped, with the workbook's target where they differ** (the coordinator's default).
   For the seven permanents the shipped and workbook sizes agree wherever the shipped game has a stack count
   (Crown 48, Bargain 24, Trade Up 54, Feed or Hunt 6). Differences, all listed in section 6.
3. **Augments use design stacks, not measured growth.** The shape's `growth` is `none` or `measured`; there is no field
   for a growth rate, and a measured one would be in native units (percent, armor, AD) while `cap_stacks` and the model are
   in stacks. So the 14 augments use `growth: "none"` with `fight_stacks_now` set to the workbook's own stacks at
   coliseum, which reproduces the workbook value exactly (the oracle shows it). Consequence: the lab's native readouts are
   not yet compared with the design growth; that needs a model change (a `stacks_per_unit` factor per card), a separate
   packet. **Coordinator's question 1.** Default in force: as above.
4. **Augment numbers come from `Augment_Tuning`, not `Augment_Value`.** `Augment_Value` is the older, pre-tuning sheet;
   `Augment_Tuning` columns H to N hold the "suggested" parameters, and they equal what the native table ships (verified
   card by card against `table.rs` text: Marksman 0.3% cap 30%, Bastion 35%, Calluses 0.07 cap 30, Ledger +3 cap 30,
   Pathfinder 1.2% cap 24%, Hazard Walker cap 15, Wish Engine cap -25%, Overflow 8 s, Corpse Hoarder 3% cap 24%, Stoked +2 cap
   30, Glacier 0.15 cap 30, Trophy Hunter 2% cap 15%, Marked Man +50%, Eighth Strike every 8th hit).
5. **Where the shape cannot say something the workbook prices, the note goes in `source` and the number stays out**
   (section 6, items 2 to 4); the evaluator is not widened.

## 6. Differences between the workbook and the shipped game, and gaps for the owner

| # | Item | Workbook | Shipped | What the inputs say |
|---|---|---|---|---|
| 1 | Marked Man | `Augment_Value` row 30: income x2, hazard 1.5 (value 14.0) | +50% (`table.rs`), `Augment_Tuning` row 5: x1.5 both (6.22) | the shipped x1.5 |
| 2 | Tenth Strike | `Augment_Value` row 42: every 10th hit | every 8th hit (`eighthStrike`) | `Augment_Tuning` row 16 (8th) |
| 3 | Other seven investment cards | `Augment_Value` rows 33-77: older rates and caps (Marksman 0.25%/25, Ledger +1 AD, Trophy 0.5%/10, Wish Engine -20%, Corpse 2%/20, Glacier 0.1/25, Calluses 0.05/25, Stoked +1/20) | the `Augment_Tuning` numbers | the `Augment_Tuning` numbers |
| 4 | The Bargain upfront cost | `Top_25` R15 = 1.67 CE (20 stacks paid) added to its expected cost | `bargain_cost` 20 | not expressible (no upfront field): the evaluator's value (2.70) matches the workbook V, but its price omits the 1.67 CE, so the Bargain's ratio reads about 1.67 CE too high. **Coordinator's question 2.** |
| 5 | Let Him Cook | b = 3 CE = 36 stacks | `chef_bonus_share` 0.20 of the lowest stat (no stack count) | 36 stacks, the workbook's, which assumes the lowest stat is about 180 stacks |
| 6 | Warm Coat, Breaker's Helm | b = 1.5 CE (18 stacks) each: design guesses for a non-stack effect | immunity; 20% armor bypass | the 18 stacks, a guess, stated in `source` |
| 7 | Trade Up | b = 4.5 CE (the top step) | steps 12, 30, 54 stacks, cash out at any step | the top step 54 |
| 8 | Reusable spells' dividend | V = Δ + dividend, d in column U (Rime Hunt 0.12, Hold the Fort 0.15, Elite Slayer 0.10) | | the `spell` shape carries no dividend: P-BAL-LAB-2 must add it; the d is in the table above, not in the JSON |
| 9 | Spell cooldowns and charges | `Spell_Pool`, `Spell_Reworks`: Shoulder Charge ~12 s, Tornado 12-15 s, Turret 20-30 s life, ward 60-90 s | `TOP25`: 12 s, 14 s, 25 s, 75 s; lantern charges 2 (workbook gives none) | none matter: spell entries hold no number. Listed for completeness; no disagreement found |
| 10 | Rime Hunt class | `Top_25` L9 Hard | `inputs.json` has medium (`Time_Limits` E5 Medium) | not touched here (sibling packet's note) |

## 7. New paths

- `crates/rh-headless/tests/balance_inputs.rs`: the oracle (this commit set).

## 8. Exclusive write scope

Implementer may edit exactly:

- `tools/balance-lab/inputs.json` (the `quests` and `augments` objects only)
- `tools/balance-lab/README.md` (one or two lines: "inputs.json: where the rewards and augment numbers come from, and that
  spell rewards stay not measured until paired duels exist")

Nothing in `crates/`; the oracle file is the author's. No `Cargo.lock`, no `docs/MIGRATION.md`, no protected path.
Keep keys sorted and the JSON valid. Do not add or change a number in the global blocks.

## 9. Contracts and references

- R-ML (`docs/REQUIREMENTS.md:99`): numbers are written by hand from the workbook in a reviewed packet; no tool rewrites
  this file.
- The evaluator shares nothing with `raid_values.json` (P-BAL-EVAL-1 section 3): do not copy a number from it or read it.
- C-RNG, C-INPUT, C-OBS: not touched.

## 10. Behaviour to preserve

- `cargo test -p rh-headless --test balance_eval` stays green (its test 5 reads the same file: band, targets, a class on
  every timed quest).
- Every existing `difficulty` value stays as it is.

## 11. Acceptance checks

- `scripts/gate.sh --quick --base agent/sonnet/P-BAL-EVAL-1 --oracle packet/P-BAL-INPUTS-1` passes (drop `--base` once
  P-BAL-EVAL-1 has merged).
- `cargo test -p rh-headless --test balance_inputs --test balance_eval` passes.
- `python3 scripts/readability_check.py` passes.
- `balance_eval` on a real lab report exits 0 with the 7 permanent quests priced once completions are measured.

## Oracle files (read-only for implementers)

- `crates/rh-headless/tests/balance_inputs.rs`

Four tests, all over the shipped `inputs.json` (red on the base by assertion; green when run against a scratch copy built
from the tables above, which the author checked):

1. `every_quest_and_native_augment_has_an_entry`: quest keys equal the 25 `DEFINITIONS` ids; each reward kind follows
   `RewardId::kind()` (Permanent gives `permanent_buff` with positive `direct_stacks`; SingleUse/Reusable give `spell` with
   the matching `spell_type` and no `duel_equivalent_ce`); each reward has a non-empty `source`; `difficulty` is present
   exactly when the quest has a route timer; augment keys equal `CardId::NATIVE` ids, each `growth: "none"` with a `source`.
2. `every_non_spell_quest_gets_a_value`: a hand-built report with completions for all 25 quests; the evaluator returns a
   finite positive value, ratio and verdict for each `permanent_buff` quest, and none with a "duel" reason for each spell.
3. `permanent_rewards_match_the_workbook_values`: for the seven permanents the numbers in section 4 are pinned exactly, and
   the evaluator's value (c_eff 3, kappa 0.6, coliseum minute 12, pick-up minute N and tau O from `Top_25`) is within 10% of
   column AF (5.246, 2.697, 5.581, 3.684, 1.502, 1.5, 2.153).
4. `native_augments_match_the_workbook_values`: for all 14 native augments the numbers in section 4 are pinned and the
   evaluator's value (mu 0.06, c_eff 3, kappa 0.6, pick-up minute 5) is within 10% of `Augment_Tuning` column S; each has a
   solo, duo and trio ratio; Marked Man meets 0.85 with one helper (`Augment_Tuning` V5).

## 12. Risk and suggested model tier

Low. A data file, one test per layer. implementer-sonnet; the Haiku `implementer` is enough in principle. The risks are a
transposed number (the oracle pins every one) and a `source` string that says nothing (the oracle checks only that it
exists: the reviewer reads them).

## 13. Known traps

- `direct_stacks` is the workbook's `b` in CE times 12, not `b`: Trade Up is 54, not 4.5.
- `damage_fraction` is the fraction `0.08`, not `8`.
- Card ids are camelCase (`eighthStrike`, `executionersLedger`, `markedMan`), the strings `CardId::as_str` gives and the
  lab report uses; not the workbook names.
- The Bargain's upfront cost and the reusable spells' dividend are not in the shape; do not squeeze them into
  `direct_stacks` or `damage_fraction` to make a ratio look right.
- `Augment_Tuning` numbers, not `Augment_Value`'s (decision 4); `Marked Man` income and hazard are 1.5.
- An untimed quest gets no `difficulty` key at all, not an empty string.

## NOT ESTABLISHED by this packet

- That the workbook's numbers describe the game: Δ, b of Warm Coat and Breaker's Helm, the augment effect sizes are
  design guesses (workbook READ_ME B9: "treat every number as an order of magnitude").
- Any value for the 18 spell rewards (needs paired duels, P-BAL-LAB-2).
- Any comparison of the lab's measured native growth with the design growth (decision 3).
- The Bargain's ratio is gross of its upfront cost (difference 4).

## Notes from previous attempts
