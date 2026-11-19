# P-AUG-TUNE-1: Wishful Thinking's Rework rule (1.5% of the full cooldown a hit, at most 20% a window), live world only

- **Serves:** the owner's elite and augment rework approval of 2026-11-18 (`docs/backlog/AUGMENT_ELITE_REWORK.md`, the
  P-AUG-TUNE-1 row and Appendix A's Wishful Thinking row; content-hash move 4, accepted by the coordinator).
- **Implementer tier:** implementer-sonnet (Opus if the restore bound needs new serialized state; see section 10)
- **Reviewer:** reviewer-opus
- **Integration base:** `feature/unity-rework` as `coord/int-61` @ `3c530434f`
- **D-rules touched:** D1 loose proof (owner direction 2026-11-10); D2 n/a; D5/D6/D9 none (no wire; text-only client
  change). Legacy is unchanged and byte-identical.

## 1. ID and outcome

Under `AugmentRule::Rework` each hit you land shortens your Wish's cooldown by 1.5% of its full length, and a rolling
window may cut at most 20% of the full length. Legacy keeps 1 s a hit and 30 s a window. "Full length" is
`WishSpend::cooldown_ms`: the cooldown the Wish was cast with, after every cooldown multiplier (Arcane Overload's
included). It is the same value `abilities/checkpoint.rs` already validates against `cooldown(wish, training, mult)`, so
the restore bound can read it without new state. Window and floor keep their legacy meaning (below).

## 2. Dependencies

`coord/int-61` @ `3c530434f` (P-AUG-RULES-1, P-AUG-CONTENT-1, P-AUG-UI-1 and Plant merged). If the coordinator adds fix
commits, merge them in.

## 3. Relevant legacy paths

- `packages/server/src/sim/cardHooks.ts:367-388` `wishfulThinking`: sums the owner's `hit` `cooldown` trigger rows
  (`:371`); returns without a cast or a running clock (`:379`); drops trims with `now - t.at >= WISHFUL_WINDOW_MS`
  (`:380`, a ROLLING window, exclusive: a trim exactly 60 s old is gone); `used` is the sum of the trims left (`:381`);
  `floorAt = max(now, cast.at + WISHFUL_FLOOR_SHARE * cast.cooldownMs)` (`:382`); `cut = min(n*ms, BUDGET - used,
  clockEnd - floorAt)` (`:383`); a cut cuts `wishReadyAt` only while on cooldown, and always `wishFullAt` (`:385-386`);
  records `{at: now, ms: cut}` (`:387`). `n` is the hits of the tick (`:329`).
- `packages/shared/src/constants.ts:278-280` (`WISHFUL_BUDGET_MS` 30000, `WISHFUL_WINDOW_MS` 60000,
  `WISHFUL_FLOOR_SHARE` 0.5).
- Rust: `cards/mod.rs:229-269` `wishful_thinking`; `cards/after_damage.rs:62-101` (caller; `wish_ms` is the sum of the
  card's trigger rows, 1000 for the flag row); `abilities/checkpoint.rs:19-46` (`trim_trace_coherent`, `max_trim_cut`,
  legacy `WISHFUL_BUDGET_MS` / `WISHFUL_WINDOW_MS`) and `:127-182` (where they gate the restore).
- Owner decisions: `docs/OWNER_DECISIONS.md` row 2026-11-18 (the rework, live world only); nothing narrower on Wishful.

## 4. New paths

None outside the oracle files. Registry rows `REWORK_WISHFUL_HIT_SHARE` and `REWORK_WISHFUL_BUDGET_SHARE`.

## 5. Exclusive write scope

- `crates/rh-sim/src/constants.rs` (append after `REWORK_ELITE_SHARE_STACKS`, in this order:
  `REWORK_WISHFUL_HIT_SHARE: f64 = 0.015, "share"; REWORK_WISHFUL_BUDGET_SHARE: f64 = 0.20, "share";`)
- `crates/rh-sim/src/cards/tuning.rs` (REWORK table points `wishful_hit_share` / `wishful_budget_share` at them;
  LEGACY keeps `0.` = the millisecond rule, so its arithmetic is untouched)
- `crates/rh-sim/src/cards/rework_overrides.rs` (Wishful's text-only row, in `CardId` order: copy the catalog block
  unchanged, change only `text`)
- `crates/rh-sim/src/cards/mod.rs` (`wishful_thinking`, the non-test code only) and `cards/after_damage.rs` if the call
  needs it
- `crates/rh-sim/src/abilities/checkpoint.rs` (the trim bound; takes the rule's numbers from `k.cards`)
- the content-hash copies, by `scripts/regolden-content.sh` only (six copies; the `hash-tape.json` `contentHash` line
  trips the gate's oracle-capture stop and the coordinator waives that one line)
- `client-unity/UnityProject/Assets/Scripts/UI/CardDisplayCatalog.cs`, line 80 (the Wishful entry, index 64): back to
  UI-1's text, "Each hit you land shortens your Wish's cooldown by 1.5% of its full length (at most 20% a minute)."
- Gate flags: `--allow-artefacts`, and `--regen-fixtures` for the `contentHash` lines.

## 6. Contracts and references

C-TICK (no tick-order change), C-NUM, `docs/agent/AUGMENT_REWORK.md` ("Changing a named number", "Moving the content
hash"): register the number by name, point the `REWORK` field at it, never branch on the rule in game code, move the
checkpoint bound with it.

## 7. Behaviour to preserve

- Legacy: byte-identical (existing `cards::wishful_tests`, `on_hit_salvage2_*`, the frozen source corpus, hash tape).
- Draw order: no RNG involved. Stays: the window is rolling and exclusive (`now - at < window`), the floor is
  `max(now, cast.at + 0.5 * cast.cooldown_ms)` and means the same under Rework, a charged Wish's `wish_full_at` shifts
  with the cut, a refused cut records no trim. "Same for every faction": the share reads the cast's own cooldown, so a
  two-charge Wish is cut by the same percentage.
- Order of operations (answers "after the multipliers"): the share multiplies `cast.cooldown_ms`, which already has the
  multipliers (Arcane Overload's) in it. Flat 1 s `ms` of the card row is ignored under Rework (the row stays the flag
  row, like Gambler, Third Time and Plant).

## 8. Acceptance checks

- `scripts/gate.sh --quick --oracle packet/P-AUG-TUNE-1`; the six content-hash copies regenerated by
  `scripts/regolden-content.sh`.
- The Unity mirror test (`BuildDrawerEditModeTests.cs`) passes with no edit: it reads `rework_overrides.rs` and the C#
  catalog and needs the same text on both sides.

## Oracle files (read-only for implementers)

- `crates/rh-sim/src/cards/wishful_rework_tests.rs` (numbers, text-only override, cut share, budget, rolling window,
  floor and charge clock, Legacy)
- `crates/rh-sim/src/abilities/source_tests/wishful_rework_restore_tests.rs` (the restore bound, both rules, a played Rework
  world)
- `client-unity/RoguehallaNet.Tests/CardDisplayReworkTests.cs` (the Wishful row back in `Retuned`: "1.5%", "full",
  "20%" present, "1 s", "30 s" absent; rest count `80 - 22`; digest `5cc91496...56a4d0`). This reverts the test half of
  coordinator commit `08bbb1405`; it fails until the catalog line is restored.
- `crates/rh-sim/src/cards/rework_content_tests.rs`: `oracle-amend` commit, two edits to P-AUG-CONTENT-1's tests (for
  the owner): (a) `every_changed_number_is_a_hashed_rework_registry_row`'s end-of-registry list now ends with the two
  new rows after `REWORK_ELITE_SHARE_STACKS` (it said the registry ends there); (b)
  `legacy_is_unchanged_and_unlisted_cards_keep_their_catalog_rows` skips the Wishful row's `text` (its rows are still
  asserted equal to the catalog's). The existing assertion that Rework keeps `wishful_budget_ms`, `wishful_window_ms`
  and `wishful_floor_share` legacy still holds (those fields are untouched).

Stub commit (the implementer replaces the zeros): `CardNumbers::wishful_hit_share` and `wishful_budget_share`
(`tuning.rs`, both tables `0.` = legacy ms path), the two `mod` lines, `setup.rs` reading `config.augments: "rework"`
(test-only), two compile lines in `tuning_tests.rs`.

## Existing tests that pin what this changes

- `cards/mod.rs` `wishful_tests::wishful_thinking_trims_with_the_source_caps`: Legacy numbers, 1 s, 30 s budget, floor,
  rolling window. Keeps passing (signature unchanged; Legacy path unchanged).
- `abilities/source_tests/mod.rs` `card_producer_checkpoint_states_restore_and_reject_malformed_producers`: a Legacy
  trim of `WISHFUL_BUDGET_MS + 1` is refused. Keeps passing.
- `cards/on_hit_salvage2_tests.rs` (Wishful counts per hit, ripostes not): Legacy worlds, unchanged.
- `cards/rework_content_tests.rs`: the two edits above (oracle-amend).
- `tests/content.rs`: new registry rows are hashed; no `NON_GAMEPLAY` entry needed.
- `tests/hash_coverage.rs` (`card_trims`): no new serialized field is expected.

## 9. Validation commands

`cargo test -p rh-sim --lib wishful`; `scripts/gate.sh --quick --oracle packet/P-AUG-TUNE-1 --allow-artefacts
--regen-fixtures`.

## 10. Risk and suggested model tier

Sonnet, `reviewer-opus`. The restore bound is the risk (plan Risks: "TUNE-1 then escalates to Opus"). No new state is
needed for the cap the oracle pins: `cast.cooldown_ms` is in `card_runtime.wish_cast`, already validated against the
build. **Plan doubt for the implementer to resolve, not pinned by a test:** trims persist 60 s but `wish_cast` is
replaced at every cast, and the cooldown can change between casts (a card taken, Arcane Overload). A bound that reads
the CURRENT cast's cooldown can refuse a state the sim reached: trims cut at a 60 s cooldown (12 000 each window)
checked against a later 40 s cast (8 000). The runtime has the same shape (old trims count against the new cast's
budget), so such a world simply cuts nothing more, but its checkpoint then holds a sum over the current budget. If the
Sonnet attempt cannot make the bound safe without a per-trim record of the cooldown it was cut against (a serialized
field: format bump, hash, restore), it stops and escalates to Opus; do not weaken the cap tests. A safe bound that
needs no state is the largest full cooldown the build could have cast with.

## 11. Known traps

- `ms` passed to `wishful_thinking` is 1000 under both rules (flag row): under Rework do not use it.
- The shared `trims` Vec mixes cuts from earlier casts; the budget is shared (legacy `:381`).
- Legacy must keep its exact float arithmetic (`n * ms`, `BUDGET - used`); Rework's `0.015 * cd` times `n`: the oracle
  uses exact values with 40 000, 60 000 (600, 900, 1800, 8 000), so compute `n * (share * cd)` or `share * cd * n`
  and check it prints those exactly.
- `REGISTRY` tail order is asserted; the `scripts/regolden-content.sh` run moves content hash `10dfa3b399dc43a1`.
- `a_played_rework_world...` reads the Rework cooldown from the played state (45 000 there; Legacy plays 60 000 in the
  same case), so do not copy a constant into the test.

## Notes from previous attempts
