# P-BAL-EVAL-1: `balance_model` and `balance_eval`, a lab report turned into the value model's verdicts

- **Serves:** not in the plan: owner approval 2026-11-22 (plan of record `docs/backlog/BALANCE_LAB_PATCH.md` section 2
  "Evaluator" and the rules table in section 4; owner decision row "Balance patch, test bot, per-elite numbers" at the top of
  `docs/OWNER_DECISIONS.md`)
- **Implementer tier:** implementer-sonnet
- **Reviewer:** reviewer
- **Integration base:** `feature/unity-rework` @ `c0ef232a1`
- **D-rules touched:** D1 loose proof (AGENTS.md, owner direction 2026-11-10) · D2 n/a · D5/D6/D9 none (a pure function in
  `rh-headless`; no sim, wire, policy, hash or client change)

## 1. ID and outcome

A pure evaluator: `balance_eval --report lab.json --inputs tools/balance-lab/inputs.json` reads a `balance_lab` report and a
small design-intent file and prints, as JSON on stdout and tables on stderr (as `balance_lab` does):

- **Per quest:** value, price, ratio = value / price, a verdict against the band (0.85 to 1.25), the timer that meets the
  quest's difficulty target (the measured completion-time quantile), the reward value that would give ratio 1.0, and the
  quest duration that would give ratio 1.0.
- **Per augment:** value over what an elite costs (`needed`), solo, with one helper and with two, from measured growth,
  death rate and the elite's cost.
- **Anything the report does not measure is "not measured"**, listed with its reason, never zero and never a verdict.

The later number packets (P-BAL-NUM-*) become "change one constant until this tool's ratio is inside the band". The tool
changes no table and writes no file (see "Not built": no tool rewrites `raid_values.json` or any table from measurements;
REQUIREMENTS R-ML, plan "What this will not establish").

## 2. Dependencies

- P-BAL-LAB-1: Done (`2fe5634f4`), the report schema `balance-lab/1` is on the integration base.
- Not a dependency but the reason for the "not measured" rule: the lab's quest completions are unmeasured today (the board
  never sets `owner_completed`; P-BAL-LAB-1-fix reads them from the sim's attempt state after P-BAL-PROBE-1). The evaluator
  must work on today's report (completions zero) and on the fixed one.
- The evaluator needs one report field the lab does not emit (completion-time samples, section 3 "Gaps"). Until
  P-BAL-LAB-1-fix (or a field added beside it) emits it, the timer is "not measured": that is the specified behaviour, not
  a defect of this packet.

## 3. Relevant legacy paths

No legacy counterpart: this is a Rust-side analysis tool over the Rust port's own measurements. Nothing under `packages/` is
read. The formulas are `docs/QUEST_VALUE_MODEL.md` (marked FUTURE ONLY, "a rough pricing model, not a contract") and the
workbook `docs/quests/FUTURE_ONLY_Quest_Design_Discussion.xlsx` (sheets `Inputs`, `Augment_Value`; not edited).

### Established facts (verified at `c0ef232a1`; the implementer re-verifies a line before relying on it)

**The report today** (`crates/rh-headless/src/balance_lab.rs`, `balance_lab/acc.rs`):
- Schema `balance-lab/1`; top-level keys in declaration order `schema, content_hash, seeds, seating, maps, world, elites,
  quests, rewards, augments, notes` (`balance_lab.rs:286-299`). The lab's types derive `Serialize` only; `Report` and the
  accumulators are private, or `pub` inside the private `mod acc` (`balance_lab.rs:4`); none derives `Deserialize`.
- `quests.by_quest[]`: `{quest (the def id, e.g. "watchtowers"), dealt, accepted, attempts, completed, ended_timer,
  ended_death, abandoned, complete_secs: {sum, n}}` (`acc.rs:185-207`, the flatten in `acc.rs:291-296`). `complete_secs`
  is a sum and a count: **a mean, no distribution**, so no quantile can be read from it.
- `quests.accepted_by_minute`: map minute (tick / 1200) to first acceptances, all quests together (`acc.rs:214`); JSON keys
  are strings (a `BTreeMap<u32, _>`).
- `world`: `camps_credited` (`acc.rs:131`), `kill_secs`, `heal_secs`, `walk_secs` as `{sum, n}` (`:135-139`),
  `player_deaths {total, by_cause, by_minute{"<m>": {player_ticks, total, by_cause}}, ...}` (`:79-86`, `:63-69`;
  `player_ticks / 1200` is player-minutes), `minutes{"5"|"8"|"12": {samples, farm_stacks{mob_type: u64},
  boosts, native{card: {holders, value_sum, max_sum}}}}` (`:113-118`, `:98-102`).
- `elites.by_kind_weapon[]`: `{kind, weapon, attempts, killed, died, taken_by_other, open}` (`acc.rs:156-183`). **No seconds
  of elite fights and no helper count.**
- `augments.by_card[]`: `{card, offered, picked, held_by_winner}` (`acc.rs:243-263`).
- A lab report without `--human-seats` has an all-zero quests block by construction (`balance_lab.rs:301-302` note 1).

**How the evaluator reads the report (decision, with the reason): its own tolerant structs through serde, not the lab's
types.** Every field is `#[serde(default)]`, unknown fields are ignored, and `schema` must start with `balance-lab/1`
(anything else is an `Err`). Reasons: (1) the lab's types are `Serialize`-only and private, and adding `Deserialize` to them
is an edit to `balance_lab/*` (P-BAL-LAB-1-fix and P-BAL-LAB-2 are changing them; a shared edit would conflict and is
outside this write scope); (2) the evaluator must accept a report with fields absent (completions, the missing quantile
samples, minutes with no samples) and say "not measured", where the lab's strict types would turn a missing field into a
parse error or, worse, a defaulted zero; (3) a hand-built report in a test or a report from before a lab change both parse.
Counts are read as optional numbers; an absent number and a zero are different things to the evaluator (a counter of 0
completions is "no completions", the quantity derived from it is "not measured").

**The bot's price table and sharing.** `crates/rh-policy/src/bots/raid/values.rs` has `Prices` (`:26-42`), `reward_value`
and `augment_value` (`:87`, `:94`): at the base they return one flat `reward_default` / `augment_default` of 1.0
(placeholders until P-RAID-V), and `crates/rh-policy/data/raid_values.json` does not exist yet (P-RAID-V builds it; the
plan's section 1 says it is "written by hand from the QUEST_VALUE_MODEL formulas"). The currency is the same, the
camp-equivalent (`camp: 1.`, `stacks_per_camp: 12.`, `values.rs:53-54`). **The evaluator shares nothing with it**: no
`rh-policy` import, no constant, no file. `rh-headless` already depends on `rh-policy` (`Cargo.toml:14`); the evaluator must
not use that dependency. Reason: the bot's table is a judgement the lab tests; verdicts computed from the same table are
circular. The model's own numbers (`s = 12`, `π = 1.15`, band, targets) are written again in `inputs.json`, which is the
evaluator's only source of design numbers.

**Same-tick and boundary points that matter to the maths** (from the lab's notes, `balance_lab.rs:301-308`): kill, heal and
walk seconds are heuristics (the walk share includes the doc's overhead `o`); observations are not sampled on the match's
last step; later minutes have few or no samples (`minutes[m].samples`, `native.holders` give the count). The evaluator
divides by those counts, and a minute with `samples == 0` is "not measured", never a zero.

### Gaps between the plan and the report (each is a "not measured" in the evaluator, and a question below)

1. **No completion-time distribution** (`complete_secs` is `{sum, n}`). The timer wants a quantile. Contract the evaluator
   reads: an optional `complete_secs.samples: [seconds, ...]` (any order; the evaluator sorts). Absent, the timer is "not
   measured". A lab change to emit it belongs to P-BAL-LAB-1-fix (coordinator's call, question 1).
2. **No elite fight seconds** and no helper count. The plan says the augment price comes from "measured growth, death rate
   and elite time". Today the elite cost (time in camp-equivalents, death chance at the elite, solo / one helper / two) is
   read from `inputs.json` as labelled assumptions, and the output lists them under `assumed`. Measured elite time arrives
   with P-BAL-LAB-2 (paired mode); the evaluator then prefers a measured value when the report carries one (field name is
   P-BAL-LAB-2's; not built here).
3. **Quest reward spells have no value without a duel equivalence** (`Δ`, model 4.2 and 4.4), measured by paired duels
   (P-BAL-LAB-2). A quest whose reward `kind` is `spell` and which has no `duel_equivalent_ce` in the report or inputs has
   value "not measured".

## 4. New paths

- `crates/rh-headless/src/balance_model.rs`: the pure functions and the tolerant report structs (readability rule: 1,500
  lines at most, 200 to 600 aimed; split into `src/balance_model/*.rs` if it grows; every `src/**/*.rs` starts with a `//!`
  header).
- `crates/rh-headless/src/bin/balance_eval.rs`: thin, as `src/bin/balance_lab.rs`: arguments `--report PATH --inputs PATH`
  (both required; `--inputs` defaults to nothing), prints the JSON on stdout and the table on stderr, exit 2 on a usage
  error, exit 1 on an unreadable or unparseable file. No wall clock, no path in the output.
- `tools/balance-lab/inputs.json`: design intent only (section 5 shape).
- `crates/rh-headless/tests/balance_eval.rs`: the oracle.
- Doc lines: one line each in `crates/rh-headless/README.md` (beside `:23`) and `tools/balance-lab/README.md` (a short
  "Evaluating a report" section). Both are in the write scope as one-line edits.

## 5. Exclusive write scope

Implementer may edit exactly:

- `crates/rh-headless/src/balance_model.rs` (and `src/balance_model/*.rs` if split)
- `crates/rh-headless/src/bin/balance_eval.rs`
- `tools/balance-lab/inputs.json`
- `crates/rh-headless/src/lib.rs`: **one line**, `pub mod balance_model;` (the stub commit already adds it). **Shared file:
  merges one packet at a time** (plan, "Packets and waves").
- `crates/rh-headless/Cargo.toml`: **no edit needed.** `src/bin/*.rs` is auto-discovered (`balance_lab`, `camp_bench` are not
  listed; only `rh-headless` and `scalebench` are `[[bin]]`), and the evaluator needs no feature (no `rh_sim::trusted_build`).
  If the implementer finds a reason, it is a **shared file: merges one packet at a time**; say so in the hand-off.
- `crates/rh-headless/README.md` and `tools/balance-lab/README.md`: the doc lines above.

No change in `rh-sim`, `rh-policy`, `rh-protocol`, a client, `balance_lab*`, `scripts/gate.sh`, `Cargo.lock` (no new
dependency: `serde`, `serde_json` are already there) or any `AGENTS.md`. No tool that rewrites `raid_values.json` or any table.

### The inputs file (`tools/balance-lab/inputs.json`) and its shape (the oracle builds the same shape by hand)

Design intent only; no measured number lives there. Keys:

```json
{
  "schema": "balance-inputs/1",
  "band": { "low": 0.85, "high": 1.25 },
  "premium": 1.15,
  "stacks_per_camp": 12,
  "coliseum_start_minute": 12,
  "difficulty_targets": { "easy": 0.95, "medium": 0.85, "hard": 0.70 },
  "failure": { "downtime_secs": 15, "failed_attempt_share": 1.0 },
  "elite_assumptions": {
    "pick_minute": 5,
    "time_ce":      { "solo": 2.2,  "duo": 1.6,  "trio": 1.4  },
    "death_chance": { "solo": 0.15, "duo": 0.05, "trio": 0.02 }
  },
  "quests": {
    "<def id>": { "difficulty": "easy|medium|hard",
                  "reward": { "kind": "permanent_buff", "direct_stacks": 10, "damage_fraction": 0.10, "move_fraction": 0 } }
  },
  "augments": {
    "<card id>": { "fight_stacks_now": 28, "one_shot_stacks": 0, "farm_speed_pct": 38, "income_mult": 1, "hazard_mult": 1,
                   "cap_stacks": 0, "growth": "none|measured" }
  }
}
```

`elite_assumptions` are the workbook's `Augment_Value` assumptions B8 to B13 and are labelled as assumptions (the elite
cost is what the plan calls "how many camp-equivalents an elite costs"); a later lab measurement replaces them. The shipped
file lists every Route-clock quest of `rh_sim::quests::board::catalog::DEFINITIONS` with a `difficulty`, taken from the
workbook (`Top_25`, `Time_Limits`) by the implementer (the classes are design intent; a quest the workbook does not class is
a question for the coordinator, not a guess). Rewards and augments appear only where the workbook gives the design numbers.

### The model the evaluator computes (all from the cited sources; no number is chosen by the implementer)

Measured from the report (each "not measured" if its inputs are absent or its denominator is zero):

- player-minutes `pm = Σ world.player_deaths.by_minute[*].player_ticks / 1200`
- `c_eff = world.camps_credited / pm` (camps per player-minute; it already includes the supply cap, the model's `cap`, so
  the uncapped dividend form is used: `V = b + (g - 1) * c_eff * (R - τ)`, QUEST_VALUE_MODEL 4.1)
- `κ = mean(kill_secs) / (mean(kill_secs) + mean(heal_secs) + mean(walk_secs))`; `w/cyc = mean(walk) / same sum`
- `μ = world.player_deaths.total / pm` (deaths per player-minute; the workbook's hazard `μ`, `Augment_Value!B5`)
- `banked` (camp-equivalents) = at the elite pick minute, `Σ minutes[m].farm_stacks / samples / stacks_per_camp`
- quest: `τ = mean(complete_secs) / 60` minutes, `p = completed / attempts`, pickup minute = mean of
  `accepted_by_minute` keys weighted by count (bucket start), `R = coliseum_start_minute - pickup`

Quest (QUEST_VALUE_MODEL sections 3, 4.1, 6):
- `E[C] = c_eff·τ + (1-p)/p · (c_eff·f·τ + L)`, `L = c_eff · downtime_secs/60`, `f = failed_attempt_share`
- `price = π · E[C]`; `ratio = value / price`; verdict `below_band` (< low), `in_band`, `above_band` (> high)
- `permanent_buff` value: `V = b + (g - 1) · c_eff · (R - τ)`, `b = direct_stacks / s`,
  `g = 1 / (1 - κ·d/(1+d))` for `damage_fraction d`; a move bonus `m` uses `1 / (1 - (w/cyc)·m/(1+m))` (4.1); both are
  multiplied (rate multipliers) only if both are present, else the one given
- `reward_value_at_parity = price` (the reward value that gives ratio 1.0)
- `duration_secs_at_parity`: the τ at which `value(τ) = price(τ)`; both are linear in τ for a fixed `p`, so a closed form
  (the doc's "about a 37 s quest", section 8)
- timer: the smallest sample `x` of `complete_secs.samples` such that `(#samples ≤ x) / n ≥ target(difficulty)` (nearest
  rank; this equals the doc's "K = smallest k with CDF(k; μ) ≥ p_target", 11.1), reported in seconds

Augment (workbook `Augment_Value`, formulas in its rows 15-27 and 30+):
- `R_a = max(0, T - pick_minute)`; `ä = (1 - e^{-μ R_a}) / μ`; `sum_at_risk = banked + c_eff·ä`
- `price_x = time_ce_x + death_chance_x · sum_at_risk`; `needed_x = π · price_x`, for `x` in solo, duo, trio
- `M = F + (G > 0 ? min(H, G·R_a) : 0)` with `G` the measured native growth in stacks per minute (mean `value_sum / holders`
  change between the minute samples that have holders; `growth: "none"` means `G = 0` by design; `growth: "measured"`
  with no measured growth is "not measured")
- `N = (M + I)/s · e^{-μ·Lh·R_a}`, `g = 1/(1 - κ·J/(100+J))`,
  `P = c_eff · (K·g·(1 - e^{-μ·Lh·R_a})/(μ·Lh) - ä)`, `V = N + P` (`F`, `I`, `J`, `K`, `Lh`, `H` = `fight_stacks_now`,
  `one_shot_stacks`, `farm_speed_pct`, `income_mult`, `hazard_mult`, `cap_stacks`)
- ratios `V / needed_solo`, `V / needed_duo`, `V / needed_trio`; `meets_rule = ratio_duo ≥ band.low && ratio_solo ≤ band.high`
  (plan section 4, "New augments": at least 0.85 with one helper, nothing above 1.25 solo)

## 6. Contracts and references

- C-RNG, C-INPUT, C-OBS: not touched; no draw, no sim read, no submit.
- R-ML (`docs/REQUIREMENTS.md:99`, plan "What this will not establish"): the evaluator prints a report; **no code path
  writes `raid_values.json`, `inputs.json` or any content table**. A person edits numbers in a reviewed packet.
- Determinism of output: the evaluation is a pure function of (report bytes, inputs bytes). `BTreeMap` only; floats printed
  through `serde_json` (`float_roundtrip` is on); no clock, no thread, no path in the output.
- `docs/agent/READABILITY.md`: headers, README lines, size.

## 7. Behaviour to preserve

- `balance_lab`, `elite_bench`, `camp_bench` and every existing test untouched.
- A report that is today's real lab output (completions zero, no samples) evaluates without error and yields no verdict and
  no zero for a quest it did not measure.

## 8. Acceptance checks

- `cargo test -p rh-headless --test balance_eval` passes (the oracle, four tests).
- `scripts/gate.sh --quick --oracle packet/P-BAL-EVAL-1` passes.
- `python3 scripts/readability_check.py` clean.
- `cargo run -p rh-headless --bin balance_eval -- --report r.json --inputs tools/balance-lab/inputs.json` on a real lab
  report (`balance_lab --seeds 1..3 --human-seats`) exits 0, with every quest "not measured" while completions are unmeasured.

## 9. Validation commands

- `export PATH="$HOME/.cargo/bin:$PATH"; cargo test -p rh-headless --test balance_eval`
- `cargo run --release -p rh-headless --bin balance_lab -- --seeds 1..3 --jobs 2 --human-seats > /tmp/lab.json 2> /dev/null`
  then `cargo run -p rh-headless --bin balance_eval -- --report /tmp/lab.json --inputs tools/balance-lab/inputs.json`
- `scripts/gate.sh --quick --oracle packet/P-BAL-EVAL-1`

## 10. Risk and suggested model tier

implementer-sonnet: a leaf-crate, pure arithmetic with the formulas written out above and a numeric oracle. The hard parts
are the "not measured" discipline (an `Option` per quantity and a reason string, no `unwrap_or(0.0)` on a measured field)
and reading a report whose fields may be absent. Reviewer `reviewer`. No Opus: nothing in `rh-sim`, no state, hash or wire.

## 11. Known traps

- `unwrap_or_default()` on a count turns "absent" into "zero" and a zero completion count into `p = 0`: never. `completed == 0`
  or `attempts == 0` is "not measured" for `p`, `τ` and the ratio; do not report a ratio of 0 or infinity.
- The report's per-minute keys are JSON strings (`"5"`). Parse them as numbers.
- `complete_secs.sum / n` is seconds; the model's `τ` is minutes.
- The mean walk seconds include the doc's overhead `o` (heuristic); `cyc` is `kill + heal + walk`, and `c_eff` is **not**
  `60 / cyc` (it is measured directly; the two differ by contention and idle time).
- The quantile is of the completions that happened; a quest nobody completes has no timer, not "infinite".
- A quest in the report but not in `inputs.json` has no difficulty class: no timer, no verdict that needs a reward; list the
  reason. A quest in `inputs.json` and not in the report is not output at all.
- Do not import `rh_policy`; do not read `raid_values.json`.

## Choices to read, not guesses

1. **Own tolerant structs, not the lab's types** (section 3).
2. **`inputs.json` carries the reward descriptor and the elite assumptions**, beyond the four items in the brief (class, band,
   targets, elite cost). Without a reward descriptor a quest's value cannot be computed from design alone (the workbook's
   rewards are design); without the elite numbers the augment price is uncomputable from today's report. Both are design
   intent, not measurements, and are labelled so in the output (`assumed`).
3. **Price = π · E[C]** (the model's balance condition `V = π·E[C]`, section 6, and "needs V ≥ 2.50" in section 8), so ratio
   1.0 is the balanced point and the band 0.85 to 1.25 is around it (workbook `Inputs!B17:C18`).
4. **`c_eff` is the measured camps per player-minute and stands for `min(c, cap)`**, and the uncapped dividend is used
   (`g·c - c_eff` with `c = c_eff`); the cap is not separately measured.
5. **Pickup minute = bucket start** of `accepted_by_minute`, across all quests (the report has no per-quest pickup minute).
   The oracle uses a single bucket so this does not decide any asserted number except by choosing "start", not "start + 0.5".
6. **Nearest-rank quantile** over a `samples` array the lab does not yet emit (question 1).
7. **Tolerance 10%** on every asserted number (the brief's "within 10%"); the timer quantile is exact in the hand-built
   samples, so it is also asserted to 1%.
8. **Shared constants with the bot**: none, on purpose. If a shared constant is thought right (e.g. `stacks_per_camp`),
   it would be a `rh-sim` content constant read by both, not a copy from `Prices`; not built on here.
9. Spell rewards and `growth: "measured"` augments are "not measured" until P-BAL-LAB-2; the oracle pins the not-measured
   behaviour, not a number for them.

## Questions that need the coordinator's yes before an implementer starts

1. Does P-BAL-LAB-1-fix (or a field beside it) emit `quests.by_quest[].complete_secs.samples` (seconds of each completion)?
   Without it the timer is "not measured" on every real report. Default in force: the evaluator reads it when present;
   the oracle hand-builds it.
2. Elite seconds and helper count are not in the report (gap 2). Default in force: `inputs.json` `elite_assumptions`
   (workbook values), listed as `assumed` in the output.

## Existing tests that pin what this packet changes

None. The packet adds files and one `pub mod` line; it changes no constant and no existing test. Searched:
`rg "balance_model|balance_eval|balance-eval|balance-inputs" crates tools scripts docs` finds only the backlog rows and the
plan before this packet. The one existing check that reads these directories is `python3 scripts/readability_check.py`
(headers, file size; run it).

## Oracle files (read-only for implementers)

- `crates/rh-headless/tests/balance_eval.rs`

Four tests, each over hand-built reports through `balance_model::evaluate_json(report, inputs)`:

1. `quest_a_worked_example`: QUEST_VALUE_MODEL section 8, lines 147-165 ("`Quest A, time only: τ = 0.75 min, p = 1, E[C] =
   2.9·0.75 = 2.18` CE, so it needs `V ≥ 2.50`. → ... overpriced for its reward even at R = 8 (2.04 < 2.50) ... about a 37 s
   quest (... τ ≈ 0.62 min)"). With the permanent +10% damage reward (`b = 0.83`, `g = 1.0577`, line 150-153, table "R = 8:
   2.04"): asserts `value ≈ 2.04`, `price ≈ 2.50`, `ratio ≈ 0.818` (2.04 / 2.50), verdict `below_band`,
   `reward_value_at_parity ≈ 2.50`, `duration_secs_at_parity ≈ 37 s` (0.62 min), each within 10%. Measured side hand-built so
   `c_eff = 2.9` (290 credited camps in 100 player-minutes), `κ = 0.6` (kill 12 s, heal 4, walk 4), `τ = 45 s`, `p = 1`,
   pickup minute 4 of a coliseum at 12 (`R = 8`).
2. `timers_meet_the_difficulty_target`: QUEST_VALUE_MODEL 11.4 (lines 247-253) Watchtowers (easy, timer 50 s), Rime Hunt
   (medium, 65 s) and Smuggler's Run (hard, 100 s). The hand-built samples are the doc's own Poisson mixture (`τ + k·20 s`,
   `μ = 0.2, 0.3, 1.2`, counts out of 100 or 1,000); the 0.95, 0.85 and 0.70 quantiles must be 50, 65 and 100 s.
3. `augment_prices_and_a_value_match_the_workbook`: workbook `Augment_Value` (assumptions B5:B13 and the `Committed` row 31
   of the sheet): `needed` solo / duo / trio = 7.558 / 3.516 / 2.280, and for `Committed` (F = 28, J = 38 %, K = 1, L = 1,
   no growth) `V = 4.927`, `V / needed` = 0.652 / 1.401 / 2.161, `meets_rule`. The measured side is hand-built to
   `μ = 0.06` (6 deaths in 100 player-minutes), `c_eff = 3`, `κ = 0.6`, `banked = 12` camp-equivalents at minute 5.
4. `unmeasured_is_not_a_verdict`: (a) the real shape of today's lab output (completions 0, `complete_secs` `{0, 0}`, no
   samples) gives no value, ratio, verdict or timer for that quest and a non-empty reason, and no field is `0.0`; (b) a
   quest with completions but no `samples` has a ratio and no timer; (c) a quest absent from `inputs.json` has no timer; (d)
   a `growth: "measured"` augment with no native data has no ratio; (e) a report with schema `balance-lab/2` is an `Err`.
   Also: the shipped `tools/balance-lab/inputs.json` parses, holds the band 0.85 to 1.25 and the targets 0.95 / 0.85 / 0.70,
   and gives a difficulty class to every route-clock quest of the Top 25 catalog.

(The `stub:` commit holds `crates/rh-headless/src/balance_model.rs` (public types and signatures, bodies returning empty
results), `src/bin/balance_eval.rs`, the `pub mod balance_model;` line and an empty `tools/balance-lab/inputs.json`; the
implementer replaces the bodies. The public names below are the seam the oracle calls and are fixed.)

### The API the oracle calls (declared by the stub)

```rust
pub const EVAL_SCHEMA: &str = "balance-eval/1";
pub enum Verdict { BelowBand, InBand, AboveBand }
pub struct Band { pub low: f64, pub high: f64 }
pub struct Targets { pub easy: f64, pub medium: f64, pub hard: f64 }
pub struct Inputs { pub band: Band, pub difficulty_targets: Targets, /* + the rest of section 5 */ }
pub fn parse_inputs(json: &str) -> Result<Inputs, String>
pub struct QuestVerdict { pub quest: String, pub value: Option<f64>, pub price: Option<f64>, pub ratio: Option<f64>,
    pub verdict: Option<Verdict>, pub timer_secs: Option<f64>, pub reward_value_at_parity: Option<f64>,
    pub duration_secs_at_parity: Option<f64>, pub not_measured: Vec<String> }
pub struct Needed { pub solo: Option<f64>, pub duo: Option<f64>, pub trio: Option<f64> }
pub struct AugmentVerdict { pub card: String, pub value: Option<f64>, pub ratio_solo: Option<f64>,
    pub ratio_duo: Option<f64>, pub ratio_trio: Option<f64>, pub meets_rule: Option<bool>, pub not_measured: Vec<String> }
pub struct Evaluation { pub schema: String, pub needed: Needed, pub quests: Vec<QuestVerdict>,
    pub augments: Vec<AugmentVerdict> /* + assumed, measured: free for the implementer */ }
pub fn evaluate_json(report: &str, inputs: &str) -> Result<Evaluation, String>
pub fn to_json(e: &Evaluation) -> String          // compact, declaration-order keys
pub fn render_table(e: &Evaluation) -> String
```

`quests` and `augments` are in the order of `inputs.json` keys (sorted, `BTreeMap`). An `Option` is `None` exactly when the
quantity is not measured; `not_measured` carries one reason per missing quantity.

## NOT ESTABLISHED by this packet

- That the model's formulas are right: the oracle proves the code reproduces the document's and the workbook's own worked
  numbers, not that those prices describe the game. The model is marked FUTURE ONLY and "a rough pricing model".
- That a real report will carry what the model needs: completion quantiles and elite time are not emitted today (gaps).
- Quest spell rewards, native-growth augments and the cap: not pinned by a number (no measurement or paired duel exists yet).
- The elite assumptions (`inputs.json`) are the workbook's unmeasured placeholders.

## Notes from previous attempts
