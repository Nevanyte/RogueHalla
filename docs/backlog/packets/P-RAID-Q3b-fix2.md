# P-RAID-Q3b-fix2: a hurt seat keeps the fort it holds; the death term cannot panic; Trade Up reads one walk measure

- **Serves:** not in the plan: owner approval 2026-11-22 (plan of record `docs/backlog/BALANCE_LAB_PATCH.md` section 1).
  Closes the note of reviewer-opus on P-RAID-Q3b-fix and three small items.
- **Implementer tier:** implementer-sonnet (single crate, `rh-policy`, bot pricing only; no `rh-sim`, no wire)
- **Reviewer:** reviewer
- **Integration base:** `coord/int-88` @ `58e36bb96` (gate and oracle check with `--base coord/int-88`)
- **D-rules touched:** D1 loose proof (RaidBot has no legacy counterpart) · D2 n/a · D5/D6/D9 none

## 1. ID and outcome

The shared quest death term (`walk_death`, `quest/mod.rs:69`) stops pricing a hold out of existence for a hurt seat, cannot
panic, and Trade Up and Two Banners read the helper and one walk measure like every other quest.

## 2. Facts (verified on `coord/int-88`; paths in `crates/rh-policy/src/bots/raid/`)

- The note holds. `quest/mod.rs:71-73`: `survive = 1 - (row / hp.max(0.05)).clamp(row, 0.98)`; Hold the Fort takes it through
  `quest/dwell.rs:316` (`death: walk_death(cx, def, &[spot])`). Hold's row is 9/32 = 0.281, `death_loss` 6, value 3.6. The
  arbiter's score is `value * success - death * loss` (`arbiter.rs:26`), a committed goal is kept only while above 0. Measured
  on the base by the oracle: uncontested Hold at hp 1.0 term 0.281 score 1.91; at 0.45 term 0.625 score -0.15; one contestant
  (success 0.8) at hp 0.5 term 0.5625 score -0.495. The dwell executor's own rule lets go only below `LOW_HP` = 1/3 while
  hit (`dwell.rs:86`, `:509`), and Sustain pre-empts; the price is not meant to do either.
- Panic: `f64::clamp(row, 0.98)` panics when `row > 0.98`; measured on the base with a Hold row of 0.99 ("min > max") and with
  `death_floor` 0.99 on a quest with no row. The range test (`quest/death_tests.rs:7`) allows `death_floor..=0.98`, and a row
  of exactly 0.98 does not panic.
- Item 3: the Trade Up cash-out candidate uses `straight_minutes(me, last_trader)` (the line x 1.3, `decide.rs` about 892) and
  the give-up in `act_trade` uses `Self::walk_minutes` (path, `decide.rs:444`, `eta_minutes` = the path length; the straight x
  1.3 only when the service has no answer). Where the path is over 1.3 x the line and the time left is between the two, the
  candidate says reachable, the step fails, and the goal is picked again.
- Item 4: `banners_terms` (`decide.rs:671`) and `trade_read` (`decide.rs:800`) return the flat `death_floor` when no outpost
  guess / no trader is shown.

## 3. The coordinator's decisions (2026-11-23)

1. The health factor is capped: the share is `row * min(1 / hp_share, CAP)` with a named constant beside the helper,
   `CAP = 1.5`, and its reason in a comment (a measured row already holds the lab's mix of health; a held dwell is let go by the
   dwell executor's rule and Sustain, not by this price). Still: strictly higher for a hurt seat than a healthy one, never
   below the row, never above 0.98. Accepted uncontested Hold: 0.281 x 1.5 = 0.42, 3.6 - 0.42 x 6 = 1.07 > 0. One contestant
   at 50 %: 3.6 x 0.8 - 2.53 = 0.35 > 0. Two contestants (0.64): 2.3 - 2.53 < 0 is not pinned.
2. No panic: `walk_death` is total for any row and any `death_floor` (the range of the field, per `death_tests.rs`, stays
   `death_floor..=0.98`; the oracle also feeds 0.99 and 1.0 and a `death_floor` of 0.99, and expects a term in 0..=1). Narrowing
   the range test alone does not satisfy the oracle.
3. One walk measure for Trade Up: the cash-out candidate and the give-up read the path walk (`Self::walk_minutes`), because the
   step walks the path and a price must not promise what the step refuses. (Either measure is accepted by the oracle's idea;
   this one is the decision.)
4. The two fallbacks call the helper's default (`walk_death` with no quest points of its own, or the `Prices::quest_death_share`
   default through the same hp factor) instead of the flat `death_floor`.
5. **Not here:** the rows. Let Him Cook's 7 of 189 was measured while the old pricing left seats holding the attempt without
   walking it; it stays until a pilot rerun gives a new number, and a later number packet refreshes both rows.

## 4. Existing tests that pin what this packet changes (none amended)

- `tests/raid_walk_risk.rs` `a_hurt_seat_is_likelier_to_die_to_the_same_walk` (hurt at 30 % strictly above healthy for Let Him
  Cook, Hold, Stay A While; at most 0.98; stays green: 0.281 -> 0.42, 0.037 -> 0.056, 0.02 -> 0.03),
  `hold_the_fort_is_priced_at_its_row_...` (full hp, unchanged), `the_rows_sit_above_the_default_...` (full hp).
- `quest/death_tests.rs` `the_quest_death_rows_are_in_range_...` (rows within `death_floor..=0.98`; unchanged unless item 2 tightens
  it, which the oracle does not ask for).
- `quest/decide_fix_tests.rs:562` `the_answer_minutes_and_the_death_floor_are_read_from_the_prices` (pick-up keeps the floor).
- Guards, named not copied: the eleven tests of `tests/raid_walk_risk.rs` and `tests/raid_dwell.rs` stay green.

## 5. Exclusive write scope

- `crates/rh-policy/src/bots/raid/quest/mod.rs` (the helper and its constant), `quest/decide.rs` (items 3 and 4),
  `quest/death_tests.rs` only if item 2 needs it, and one new unit test module for item 3 (see below).
- Nothing else.

## 6. Behaviour to preserve

No draw from `cx.rng`; full-health terms unchanged; awake-camp term unchanged; the rows unchanged.

## 7. Acceptance checks

- `scripts/gate.sh --quick --base coord/int-88 --oracle packet/P-RAID-Q3b-fix2`
- `cargo test --locked -p rh-policy --no-fail-fast`; `cargo test --locked -p rh-headless --test raid_seats`
- No lab run is asked for: twelve seeds cannot show this (Hold completions 2 to 3).

## 8. Oracle and what it does not cover

`crates/rh-policy/tests/raid_walk_risk_fix.rs` (helpers copied from `raid_walk_risk.rs`; hand-built frames through `Dwell` and
`Walker`). On the base: 4 RED by assertion or the clamp's panic, 1 guard green.

| Test | Base |
|---|---|
| `an_uncontested_hold_stays_worth_holding_at_any_health` | RED: score -0.15 at hp 0.45 (term 0.625) |
| `a_hold_with_one_contestant_stays_worth_holding_at_half_health` | RED: score -0.495 (term 0.5625) |
| `the_health_factor_is_capped_at_one_and_a_half` | RED: term 0.625 at hp 0.45 against 1.5 x 0.281 = 0.42 |
| `a_row_at_or_above_the_top_of_the_range_prices_without_a_panic` | RED: panics at a row of 0.99 (0.98 prices) |
| `guard_a_hurt_seat_is_priced_higher_than_a_healthy_one` | guard, green |

**Item 3 has no scene in the oracle.** The path service's real paths on the two fixture maps (`seed1`, `seed2`) are at most
1.10 x the straight line between any two camp or spawn points (measured), and the fallback is exactly 1.3 x, so the case
"path over 1.3 x the line" does not arise on a map the helpers load. The implementer writes a unit test inside
`quest/` (a new `*_tests.rs` module, `mod` line in `mod.rs`) with a hand-built walk measure showing the candidate's success
is above 0 exactly when `act_trade` does not give up; this is not oracle-protected. Item 4 likewise: a unit test that the two
fallbacks equal the helper's default at hp 1 and are higher at a lower hp.

## Oracle files (read-only for implementers)

- `crates/rh-policy/tests/raid_walk_risk_fix.rs`
- `docs/backlog/packets/P-RAID-Q3b-fix2.md`
