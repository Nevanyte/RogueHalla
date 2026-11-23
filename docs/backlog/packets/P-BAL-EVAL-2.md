# P-BAL-EVAL-2: the balance evaluator closes its known model gaps (reference farm rate, upfront cost, spell dividend, native unit, typed rewards)

- **Serves:** not in the plan: owner approval 2026-11-22 (plan of record `docs/backlog/BALANCE_LAB_PATCH.md` section 2
  "Evaluator"; owner decision row "Balance patch, test bot, per-elite numbers" at the top of `docs/OWNER_DECISIONS.md`)
- **Implementer tier:** implementer-sonnet (one crate, a pure analysis tool; no sim, wire, hash or RNG)
- **Reviewer:** reviewer
- **Integration base:** `coord/int-87` @ `d52f8a769`
- **D-rules touched:** D1 loose proof (AGENTS.md, owner direction 2026-11-10) · D2 n/a · D5/D6/D9 none

## 1. ID and outcome

The coordinator's dry run (40 seeds, eight test bots in human seats, full-length matches; report, `eval.txt` and `eval.err` in
the scratchpad `pilot-dry/`, reduced to `crates/rh-headless/tests/fixtures/balance_pilot_dry_report.json`) showed four gaps in
`balance_eval` (`crates/rh-headless/src/balance_model.rs`):

- The test bots clear 0.618 camps per player-minute (`measured.c_eff`); the workbook assumes 2.9
  (`docs/QUEST_VALUE_MODEL.md:28`, row `c`: "use 2.9"; section 8 uses c_eff = 2.9). Every price is time at the measured rate, so
  the short permanent-reward quests read 7 to 8 times above the band (`even_skeletons_get_cold` 7.99, `tuber_eats` 7.39) and
  nobody can tell the quest from the slow bot.
- `the_bargain` reads 109.4: its reward (24 stacks) is gross of the 20 stacks it costs, and its accept-to-trade time is
  0.029 min (173.75 s over 101 completions, so 1.7 s each).
- All 18 spell rewards are "not measured".
- All 14 native augments say `growth: "none"`, and a `"measured"` growth in the card's own readout unit would be read as stat
  stacks (`native_growth`, `balance_model.rs:563-581`, feeds `a.cap_stacks.min(g * ra)` at `:614` as stacks).

After this packet the evaluator prints every quest and augment row at two farm rates, prices `the_bargain` net of its cost,
values a reusable spell by section 4.3 when the numbers exist, converts a measured native growth to stacks, and carries a
reward as an enum by kind.

## 2. Dependencies

None beyond the base (P-BAL-EVAL-1 and -fix, P-BAL-INPUTS-1 are merged on `coord/int-87`).

## 3. Relevant legacy paths

None: a Rust-side tool; nothing under `packages/` is read. Formulas: `docs/QUEST_VALUE_MODEL.md` sections 4.1 (`:61-78`), 4.2
(`:80-86`), 4.3 (`:88-98`) and the workbook (not edited).

### Established facts (verified at `d52f8a769`)

- Pricing today (`balance_model.rs:396-534`): price form `a + b*tau` with `a = pi*retries*L`, `b = pi*c*(1+retries*share)`,
  `L = c*downtime/60`; every term is linear in `c = c_eff`, so price at another rate `c'` is the measured price times `c'/c`
  exactly. Permanent value form `a = b0 + k*r`, `b = -k` with `b0 = direct_stacks/stacks_per_camp`, `k = (g-1)*c`,
  `r = coliseum_start_minute - pickup_minute` (`:446-472`); `g` from `kappa` and `walk_share` (`rate_gain`, `:385`). So the
  dividend also scales with `c`, and a reward with no dividend (`damage_fraction` 0, `move_fraction` 0, e.g.
  `even_skeletons_get_cold`) has a value independent of the rate.
- Spell: `Some(rw) if rw.kind == "spell"` uses `duel_equivalent_ce` alone and otherwise says "value: spell reward has no duel
  equivalent" (`:473-476`). `spell_type` is in the shipped file (`inputs.json`: 10 reusable, 8 single_use) but
  `Reward` has no such field (`:58-66`), so it is ignored. `source` strings are on every descriptor and must stay accepted.
- Augment: `Needed` (the elite cost) and the augment value both use `c_eff` (`:545-560`, `:620`), `banked_ce` is measured from
  stacks and is not a rate (`:363-366`).
- `parse_inputs` checks descriptor fields by hand on a second parse (`:136-157`); `Reward` is `#[serde(default)]` without
  `deny_unknown_fields`, so a stray number is silently ignored today.
- Dry-run numbers (`eval.txt`): `c_eff` 0.618, `kappa` 0.697, `walk_share` 0.187, `pickup_minute` 1.158.

### Existing tests that pin what this packet changes (all listed; the first group must stay green untouched)

Stay green unchanged, and constrain the design:
- `crates/rh-headless/tests/balance_eval_fix.rs:246-270` (`a_reward_descriptor_lacks_no_number_its_kind_reads`): a `{"kind":"spell"}`
  with no `spell_type` parses; `{"kind":"spell","duel_equivalent_ce":2.0}` parses; an unknown kind (`"something_unpriced"`) parses
  with no number; a permanent buff missing `kind`, `direct_stacks`, `damage_fraction` or `move_fraction` is an error naming the
  quest and the field. So the enum keeps: a spell without `spell_type` is single-use; an unknown kind is an unpriced variant.
- `balance_eval_fix.rs:171-203` (`a_zero_price_gives_no_ratio_and_no_verdict`): a spell without `spell_type` and with
  `duel_equivalent_ce` 2.0 is judged (ratio and verdict) when the price is above 0: single-use by default.
- `balance_eval_fix.rs:129-140`: schema match is exact; `balance-inputs/1` is accepted, `balance-inputs/10` refused.
- `balance_inputs.rs:166-203`: a spell with no duel equivalent has a `not_measured` reason containing "duel"; every permanent
  quest gets a value above 0 and a verdict. `:122`: no shipped spell carries `duel_equivalent_ce`.
- `balance_inputs.rs:224-231`: `the_bargain` keeps `direct_stacks` 24 (gross), damage 0.05, move 0.
- `balance_eval.rs:88-125` (the section 8 worked example), `:128-170` (timers), `:175-198` (augment prices); `balance_inputs.rs:272-`
  (augments against the workbook, all `growth: none`).

Must change (the coordinator's `oracle-amend:` commit; the implementer does not touch them; each keeps its other assertions):
1. `crates/rh-headless/tests/balance_inputs.rs:209`: `("the_bargain", 4, 0.1, 24., 0.05, 2.697)` becomes `... 1.030)`.
   Decision 2 puts `upfront_stacks` 20 on `the_bargain`, so its value falls by `20/12 = 1.667` (2.697 - 1.667 = 1.030). The
   test's own check is within 10%, tau 0.1 min, pick-up minute 4.
2. `crates/rh-headless/tests/balance_eval.rs:204-205` (`unmeasured_is_not_a_verdict`, augment `grower`, `"growth": "measured"`):
   add `"readout_per_stack": 1` to it. Decision 4 makes `"measured"` without it an error, and this test calls
   `evaluate_json(...).expect("evaluates")` on that file. Expectations unchanged (no growth is measured, so `ratio_duo` and
   `meets_rule` stay `None` with a reason).
3. `crates/rh-headless/tests/balance_eval_fix.rs:116-119` (`an_unknown_growth_is_an_error`, the loop over
   `["none", "measured"]` through `augment(g)` at `:64-67`): the `"measured"` case must carry `"readout_per_stack": 1`
   (put it in `augment()` so both cases carry it; `none` with the field must stay valid, see the decision below).

## 4. Decisions (coordinator, 2026-11-23)

Names are mine where the coordinator said "for example"; adjusted names are marked.

1. **Reference farm rate.** `inputs.json` gains the optional top-level object
   `"reference_farm_rate": { "camps_per_minute": 2.9, "source": "docs/QUEST_VALUE_MODEL.md:28 (c, 'use 2.9') and section 8" }`
   (adjusted: an object, so the source line travels with the number; `camps_per_minute` must be above 0, else an error naming
   it). Present: every quest row and every augment row is computed a second time with the report's `c_eff` replaced by the
   reference rate **everywhere it enters** (price, `L`, the dividend `k`, the augment's `pp` and `Needed`'s `risk`), and
   everything else unchanged (`kappa`, `walk_share`, `mu`, `pickup_minute`, `tau`, `p`, `banked_ce`). Extra output fields, all
   absent from the JSON (not `null`) and `None` in Rust when there is no reference: `Evaluation.reference_farm_rate`,
   `Evaluation.needed_at_reference`; per quest `value_at_reference`, `price_at_reference`, `ratio_at_reference`,
   `verdict_at_reference`; per augment `value_at_reference`, `ratio_solo_at_reference`, `ratio_duo_at_reference`,
   `ratio_trio_at_reference`, `meets_rule_at_reference`. The table's header carries one line naming both rates and which
   columns used which; the rows carry both ratios and both verdicts. Absent: JSON and table exactly today's. No code default.
   Consequence to know (not a bug): a reward with a dividend gains value at the reference rate too, so its ratio falls by less
   than `2.9 / 0.618`; a reward with no dividend falls by exactly that factor.
2. **Upfront cost.** A permanent reward may carry `upfront_stacks` (default 0, never negative): value minus
   `upfront_stacks / stacks_per_camp`, at both rates (stacks, not time, so not scaled by the rate). `the_bargain` gets 20
   (its `source` string already says so). On the dry-run report, by hand with the model's own formulas:
   measured rate: value 2.230 - 1.667 = 0.563, price 0.0204, ratio about 27.6 (was 109.4); at 2.9: value about 1.41, price
   about 0.0956, ratio about 14.7. Both still `AboveBand`. **Remaining gap, not papered over:** its completion time is 1.7 s
   (accept-to-trade is one interaction, not a task), so price is about 0 at either rate and the ratio measures the size of a
   rounding, not the quest's balance. The evaluator cannot say what a fair time is for it; it needs a quest-design number
   (a minimum honest duration, or pricing the 20-stack cost as the time to farm it, which is what `upfront_stacks` now
   does on the value side only). Name it in the README as a known gap.
3. **Reusable spell dividend.** `V_CD = Delta_CD + (min(g_CD*c, cap) - c_eff)*(R - tau)` (`QUEST_VALUE_MODEL.md:92`). A
   reusable spell reward may carry `duel_equivalent_ce` (Delta_CD), `farm_rate_gain` (`g_CD`, a multiplier) and
   `cap_camps_per_minute` (`cap`, per player). When all three are present the value is computed with `c = c_eff` (the model
   has one measured rate; `c` and `c_eff` of the formula are the same number here; at the reference rate both become the
   reference rate), `R = coliseum_start_minute - pickup_minute`, `tau` the measured completion minutes. When any is missing the
   value stays "not measured" and the reason names each missing field by its JSON name (it still contains "duel" when
   `duel_equivalent_ce` is the missing one, as `balance_inputs.rs:183` requires). A single-use spell is its
   `duel_equivalent_ce` alone (4.2), as today; a single-use spell carrying `farm_rate_gain` or `cap_camps_per_minute` is an
   error. No spell gets a number in `inputs.json` in this packet (`balance_inputs.rs:122` stays true).
4. **Native's own unit.** `augments.<card>.readout_per_stack` (above 0). `growth: "measured"` requires it (error
   `inputs: augment <card>: growth "measured" needs readout_per_stack`, naming the card); the measured growth from
   `world.minutes[m].native[card]` (`value_sum / holders` per minute, slope) is divided by it before `a.cap_stacks.min(g*ra)`.
   `growth: "none"` with the field present is valid and ignored. No card flips to `"measured"`.
5. **`Reward` is an enum**, internally tagged by `kind`: `permanent_buff` (`direct_stacks`, `damage_fraction`, `move_fraction`,
   `upfront_stacks`, `source`), `spell` with `spell_type` `single_use` (default when absent) or `reusable` (`duel_equivalent_ce`,
   `source`; reusable also `farm_rate_gain`, `cap_camps_per_minute`), and an unpriced variant for any other kind (kept for
   `balance_eval_fix.rs:255`, only `source` and `kind` read). Each variant refuses a field of another kind
   (`deny_unknown_fields`-style error naming the quest and the field; `source` is accepted everywhere). The hand-written
   second parse in `parse_inputs` (`:136-157`) goes: required numbers are required by the type, and the errors still name the
   quest and the field (`balance_eval_fix.rs:259-269`). **Schema stays `balance-inputs/1`**: every addition is optional or
   additive and the shipped file stays valid; a bump would force an amend of every inputs fixture in three test files for no
   reader's benefit. (The two new errors, `measured` without the readout and a cross-kind number, are refusals of files that
   were already wrong.)
6. Only the three amends in section 3 are needed.

## 5. Exclusive write scope (implementer)

- `crates/rh-headless/src/balance_model.rs`
- `crates/rh-headless/src/bin/balance_eval.rs`
- `tools/balance-lab/inputs.json`
- `tools/balance-lab/README.md` (the evaluator sections only: "Evaluating a report" and "inputs.json")

Nothing under `balance_lab/`, `crates/rh-sim`, `crates/rh-policy`, nothing else under `tests/`.

## 6. Contracts and references

R-ML (no model, no learning: this is arithmetic); `docs/QUEST_VALUE_MODEL.md` (marked FUTURE ONLY: the 4.3 formula is used as
a design instrument, not a contract); P-BAL-EVAL-1-fix's rule: no model number comes from code, a missing one is "not
measured" with a reason, never a zero.

## 7. Behaviour to preserve

With no `reference_farm_rate`, no `upfront_stacks` and no new field set, `to_json` and `render_table` are byte-for-byte
today's. The existing suites in section 3 (minus the three amends) pass as they are.

## 8. Acceptance checks

- `scripts/gate.sh --quick --oracle packet/P-BAL-EVAL-2` passes; `cargo test -p rh-headless --test balance_eval2 --test
  balance_eval --test balance_eval_fix --test balance_inputs` green after the amends.
- By hand on the dry-run fixture: `the_bargain` ratio about 27.6 measured and 14.7 at reference.

## Oracle files (read-only for implementers)

- `crates/rh-headless/tests/balance_eval2.rs`
- `crates/rh-headless/tests/fixtures/balance_pilot_dry_report.json`

Stub-only (the implementer replaces): the new output fields in `crates/rh-headless/src/balance_model.rs`.

## 9. Validation commands

`export PATH="$HOME/.cargo/bin:$PATH"; cargo test -p rh-headless --test balance_eval2`; the evaluator on the fixture:
`cargo run -p rh-headless --bin balance_eval -- --report crates/rh-headless/tests/fixtures/balance_pilot_dry_report.json --inputs tools/balance-lab/inputs.json`.

## 10. Risk and suggested model tier

Low: arithmetic and a type change in one file. The trap is the existing tests (section 3): the enum must keep the loose
shapes they use (no `spell_type`, unknown kind).

## 11. Known traps

- A reusable spell's `g_CD * c` uses the rate in force (measured, or the reference); computing the reference row with the
  measured `c` in the dividend would pass the price test and fail the dividend one.
- `deny_unknown_fields` does not work with internally tagged enums in serde; check fields by hand per variant or use a
  struct per variant with `deny_unknown_fields` and a `tag` pre-read.
- `source` and the `_note` key of the fixture are accepted and ignored.
- Float order: keep the measured row's arithmetic untouched so today's output is byte-identical.

## Notes from previous attempts
