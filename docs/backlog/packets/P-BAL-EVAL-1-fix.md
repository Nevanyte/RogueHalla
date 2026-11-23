# P-BAL-EVAL-1-fix: the balance evaluator invents no model number and prints no non-finite ratio

- **Serves:** not in the plan: owner approval 2026-11-22 (follows P-BAL-EVAL-1, `BALANCE_LAB_PATCH.md` section 2); two review notes
- **Implementer tier:** implementer-sonnet
- **Reviewer:** reviewer
- **Integration base:** stacked on `agent/sonnet/P-BAL-EVAL-1` @ `f78ef6694` (reviewed LAND WITH NOTES, not merged). Gates take
  `--base agent/sonnet/P-BAL-EVAL-1` until it merges.
- **D-rules touched:** D1 loose proof; D2 n/a; D5/D6/D9 none (an analysis tool in `rh-headless`, no sim, wire or hash change)

## 1. ID and outcome

P-BAL-EVAL-1's rule: every model number comes from the lab report or `inputs.json`; a missing input is "not measured" with a
reason, never a guess, never a silent zero. Two places break it, and a third (reward descriptors defaulting to 0, section 3) was added by the coordinator (line numbers at `f78ef6694`, all in
`crates/rh-headless/src/balance_model.rs`):

1. **Invented numbers.** `#[serde(default)]` and `impl Default` supply `Inputs` (`:114-150`: band 0.85/1.25, premium 1.15,
   stacks_per_camp 12, coliseum_start_minute 12, targets 0.95/0.85/0.70), `Failure` (`:34-48`: 15 s, share 1.0), and
   `EliteAssumptions`/`PerHelpers` (`:51-66`, default 0, so a missing block gives `needed = Some(0.0)`). `Band` and `Targets`
   derive `Default` (0). An unknown `growth` string is treated as zero growth (`:587-596`).
2. **Non-finite beside a verdict.** `ratio = val / pr` (`:477`) with price 0 (measured `c_eff` 0, i.e. `camps_credited` 0) is
   infinite (verdict `AboveBand`) or NaN (falls through to `InBand`, `:479-485`); serde_json writes both as `null`. The
   augment path has the same hole: `kg` (`:608-610`) is infinite when `1 - kappa * pct / (100 + pct)` is 0 (kappa 0.5,
   `farm_speed_pct` -200), giving `value`, ratios `Some(inf)` and `meets_rule: Some(false)`. `to_json` (`:672`) uses
   `unwrap_or_default()`, an empty string on failure.

Checked and NOT reachable: `rate_gain` with `frac = -1` (`:381`) is only called when the fraction is `> 0` (`:444`, `:449`);
`farm_speed_pct = -100` gives `kg = 0`, a finite value. Neither needs a guard (a guard is harmless).

## 2. Dependencies

P-BAL-EVAL-1 (above). **P-BAL-INPUTS-1** (being written) fills `inputs.json` with reward and augment descriptors on the same
base. **This packet adds no descriptor.** The two touch `inputs.json` at different places (this one: at most one note).

## 3. Facts and decisions

- The shipped `tools/balance-lab/inputs.json` already carries every block made required here; no number needs adding.
- The five tests in `tests/balance_eval.rs` build inputs with every block (`inputs()` at `:57-73`) and every augment field
  (`:179-181`, `:204-205`); none relies on a default being made required. They stay green.
- **Decision: the per-augment multipliers `income_mult` and `hazard_mult` default to 1 and stay optional.** A multiplier of 1
  means "no effect": the identity of the arithmetic, not a design number. (The additive fields default to 0 on the same
  reasoning, and `growth` to `"none"`.) Required are the design numbers: `band` (and `low`, `high`), `premium`,
  `stacks_per_camp`, `coliseum_start_minute`, `difficulty_targets` (all three), `failure` (both fields), `elite_assumptions`
  (`pick_minute`, `time_ce`, `death_chance` and each helper count). 
- **Reward descriptors (`Reward`, `:68-77`) default their numbers to 0 today; in this packet each kind requires what
  `quest_verdict` reads (`:439-472`).** `permanent_buff` reads `direct_stacks`, `damage_fraction`, `move_fraction`; `spell`
  reads only the optional `duel_equivalent_ce` (absent stays "not measured", `:470`); any other kind is "not priced" and
  reads nothing. Every descriptor needs `kind`. A missing number is an `Err` naming the quest and the field, never a 0. A
  quest with no descriptor stays valid (the shipped `inputs.json` has none). P-BAL-INPUTS-1 is data only and fills them.
  Existing `tests/balance_eval.rs` builds descriptors only through `buff10()` (`:81-83`: kind, direct_stacks,
  damage_fraction, move_fraction), so none becomes an `Err`; checked.
- Valid `growth` values are `"none"` and `"measured"`; anything else is an `Err` naming `growth`.
- Missing field errors must name the field (serde's `missing field \`x\`` does; keep it).
- A quest ratio and verdict exist only when price `> 0` and the ratio is finite; otherwise both are `None` and a
  `not_measured` reason says why (`price` may stay `Some(0.0)`). The same for an augment: a non-finite `value` makes `value`,
  the three ratios and `meets_rule` all `None` with a reason.
- `to_json` keeps its signature and `.expect("...")`s the serialisation (it cannot fail once nothing is non-finite).

## 4. Exclusive write scope

`crates/rh-headless/src/balance_model.rs`, `crates/rh-headless/src/bin/balance_eval.rs` (only if needed),
`tools/balance-lab/README.md`, `tools/balance-lab/inputs.json` (only the note below; it is JSON, so put it in the README if
no comment key fits the parser: unknown fields are ignored by serde, so `"_note"` beside the `rime_hunt` row works).

Scope items not pinned by the test or only partly pinned:

- Schema checks exact: `balance-lab/1` and `balance-inputs/1` exactly (or the prefix followed by a non-digit). Pinned.
- Duplicate quest id in `by_quest` (`:641-644` keeps the first): an `Err`. Pinned.
- `tools/balance-lab/README.md`: add an "Evaluating a report" section (command, the two files, the output JSON and stderr table,
  "not measured" rule, required inputs) that P-BAL-EVAL-1 asked for. Not pinned.
- A one-line note that Rime Hunt's class is `medium` in the workbook's `Time_Limits` sheet and `hard` in `Top_25`, and the
  shipped choice is medium. Not pinned.
- Run `python3 scripts/readability_check.py` (no arguments): it must pass.

## 5. Acceptance

`scripts/gate.sh --quick --base agent/sonnet/P-BAL-EVAL-1 --oracle packet/P-BAL-EVAL-1-fix`

## 6. Oracle files (read-only for implementers)

- `crates/rh-headless/tests/balance_eval_fix.rs` (new, 7 tests): each design block and nested number removed in turn is an
  `Err` naming it (via `parse_inputs` and `evaluate_json`); unknown growth `Err`, valid growths and omitted multipliers `Ok`;
  `balance-lab/10`, `balance-inputs/10` and a duplicate quest id are `Err`; `camps_credited` 0 gives no ratio or verdict (an
  infinite and a NaN row), with a positive control; a non-finite augment value is all `None` with a reason; a generic walk of
  `to_json` output finds no null `value`/`price`/`ratio*` beside a non-null `verdict`/`meets_rule`; the shipped inputs parse; a reward descriptor
  missing `kind` or a number its kind reads is an `Err` naming quest and field, a spell or unpriced kind needs none, and no
  descriptor is valid.
- Existing tests that pin what this changes: `tests/balance_eval.rs` (five tests, unchanged, stay green; its
  `balance-lab/2` is `Err` check holds).
- No `oracle-amend`.

## 7. Tier and risk

implementer-sonnet; one file of logic, no `rh-sim` contact. Low risk. Trap: do not add a descriptor or number to
`inputs.json`; do not edit `tests/balance_eval.rs`.

## Coordinator's answers (2026-11-23)

1. Reward descriptors: the numbers a reward's kind needs are required in this packet (model code); P-BAL-INPUTS-1 is data only
   and fills them. A descriptor lacking a number its kind reads is an `Err` naming quest and field, never a 0; `spell` needs
   none of the value numbers; no descriptor stays valid. Implement per section 3, reading only what `quest_verdict` uses.
2. Rime Hunt stays `medium` (the `Time_Limits` sheet is the timer's own sheet); the implementer records the disagreement with
   `Top_25` (hard) in the README or a `_note` key.

## NOT ESTABLISHED by this packet

That any evaluator number is right (the oracle checks refusals and absence, not values); that reward descriptor numbers are
right (only that the ones a kind reads are present); that the NaN/inf guard covers every future formula (only the paths above
are tested); `rate_gain` and the `balance_eval` binary error output are not pinned; the README section and Rime Hunt note are not
tested.
