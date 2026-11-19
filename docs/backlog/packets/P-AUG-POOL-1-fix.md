# P-AUG-POOL-1-fix: fold both card tables' chest-pool inputs (pool ids, faction-only pairs, Eye for an Eye flag) into the content hash

- **Serves:** ORDER_OF_OPERATIONS phase 2, item 13 (P-CARDS-2/3), under the owner's 2026-11-18 approval of the elite
  and augment rework (`docs/OWNER_DECISIONS.md` row "Elite and augment rework", 2026-11-18). Follow-up of P-AUG-POOL-1
  (reviewer-opus, integration 62 review): a broken guarantee, the rules identity (`content_hash`) does not cover the
  chest pool.
- **Implementer tier:** implementer-opus (`content/fold.rs` is a shared content-identity file; the content hash moves)
- **Reviewer:** reviewer-opus
- **Integration base:** `feature/unity-rework` @ 7673f8a02 (content hash `c890f52fd7ab4546`)
- **D-rules touched:** D1 n/a (no gameplay change: identity only) · D2 n/a · D5/D6/D9 none. Moves `content_hash`
  (Welcome, `/info`, replays: older replays are refused with ContentMismatch, as every regold).

## 1. ID and outcome
A Rework chest's offers are decided by three table inputs that `content_hash` does not fold today:
- the pool ids (`Tuning::pool()`: `reward_chests::REWORK_POOL`, and under Legacy `reward_chests::INTERACTIVE`),
- the table's `faction_only` `(CardId, FactionId)` pairs (`Offer::Faction`),
- the table's `weight_eye_for_an_eye` flag.

So adding a pool card, adding a faction pair (P-AUG-NATIVE-4 will) or flipping the flag changes chest offers and reward
draws in the live world without moving the rules identity. When this packet lands, `content_hash` folds those three
inputs for both real tables (Legacy and Rework), each perturbation of them moves the hash, the hash is regolded through
`scripts/regolden-content.sh`, and the `NON_GAMEPLAY` reasons that claimed "identifiers only" / "no number of its own"
are corrected. No gameplay changes: no draw, no offer, no checkpoint or wire byte.

## 2. Dependencies
- P-AUG-POOL-1 (merged, integration 62).
- **Merges after P-ELITE-TUNE-1** (in flight, `agent/sonnet/P-ELITE-TUNE-1`): both regold the content hash (the same
  six copies: `tests/content.rs`, the two `tools/unity-browser-reload-proof/*.mjs`, `BakedContent.cs`,
  `parity/abilities/hash-tape.json`, `protocol/fixtures/archetypes_v1.json`). The implementer rebases on the integration
  head after ELITE-TUNE-1 lands and regolds once, there. The coordinator orders the merges (MIGRATION "P-AUG-POOL-1-fix
  (Opus): after P-ELITE-TUNE-1").

## 3. Relevant legacy paths
None: the identity fold is Rust-only. Legacy's pool is `packages/shared/src/cards.ts:646-652`
`INTERACTIVE_AUGMENT_IDS` (the 14, = `reward_chests::INTERACTIVE`); legacy has no faction-only offers and no weighting
(P-AUG-POOL-1 §3). `docs/OWNER_DECISIONS.md`: no row on the chest pool or on content-hash coverage; the elite and augment
rework row (2026-11-18) governs the Rework tables.

Rust today (integration head 7673f8a02):
- `crates/rh-sim/src/cards/tuning.rs:76-89` `Tuning` (fields `:82`, `:85`, `:88`) `pool`, `faction_only`, `weight_eye_for_an_eye`; `:102-124`
  `LEGACY` (`:121-123`: `INTERACTIVE`, `&[]`, `false`); `:125-147` `REWORK` (`:144-146`: `REWORK_POOL`, `&[]`, `true`). Readers: `:193`
  `pool`, `:197` `augment_pool` (pool membership), `:226` `offer` (`faction_only`), `:244` `offer_pool`
  (both, and the flag with `REWORK_EYE_FOR_AN_EYE_WEIGHT`).
- `crates/rh-sim/src/reward_chests/pools.rs:10-25` `INTERACTIVE`; `:30-50` `REWORK_POOL`.
- `crates/rh-sim/src/content/fold.rs:113-169` `visit_progression_content`: folds the card *names* (`CardId::ALL`,
  `:156-162`) and `cards::visit_content` (tiers and five numbers, `cards/rules.rs:19-32`), never a pool. The
  `reward-chests/1` stream (`reward_chests/rules.rs:29-44`) folds tier weights, range, lifetime, skip and themed-first
  ids, not the pool.
- `crates/rh-sim/src/content/definitions.rs:66-70` folds `visit_progression_content` under `mvp-progression/1`;
  `fold.rs:278` `content_hash_of_progression_words` is its word seam.
- `crates/rh-sim/tests/content.rs:66-67` `NON_GAMEPLAY` rows `LEGACY` / `REWORK` ("no number of its own"); `:73-74`
  `INTERACTIVE` / `REWORK_POOL` ("identifiers only"). `:574` `GOLDEN_CONTENT_HASH = 0xc890_f52f_d7ab_4546`.
- The weight value itself, `REWORK_EYE_FOR_AN_EYE_WEIGHT`, is a registry row (`constants.rs`) and already folded.

## 4. New paths
- `crates/rh-sim/tests/chest_pool_content.rs` (oracle; a new file, because the implementer must edit
  `tests/content.rs`).

## 5. Exclusive write scope
- `crates/rh-sim/src/content/fold.rs` (the fold, the seam body; `ChestPoolTable` and `chest_pool_tables` may be
  reworked but keep their signatures).
- `crates/rh-sim/src/cards/tuning.rs`: accessors only (`of_rule`, `faction_only`, `weights_eye_for_an_eye`, added by the
  stub); no table, number or behaviour change.
- `crates/rh-sim/src/content/definitions.rs`: only if the implementer chooses to fold the pools as their own stream
  rather than inside `visit_progression_content` (one `h.str(..)` plus a visitor call); otherwise untouched.
- `crates/rh-sim/tests/content.rs`: the `NON_GAMEPLAY` reasons of `cards/tuning.rs` `LEGACY` / `REWORK` and
  `reward_chests/pools.rs` `INTERACTIVE` / `REWORK_POOL` (they may move to `TABLES_FOLDED_BY_HASH` with a reason naming
  the fold); `GOLDEN_CONTENT_HASH` only through `scripts/regolden-content.sh`.
- The other regold copies, only through `scripts/regolden-content.sh` (it rewrites `BakedContent.cs`, a build artefact
  by name: run the gate with `--allow-artefacts` for that one file).
- `docs/agent/AUGMENT_REWORK.md` ("The chest pool" section: say the three inputs are in the content hash and that
  adding a pool card or a faction pair is a regold).

## 6. Contracts and references
- `crates/rh-sim/AGENTS.md`: content goes where `content_hash` folds it; `tests/content.rs` stays green.
- `docs/agent/AUGMENT_REWORK.md` "The chest pool (P-AUG-POOL-1)".
- C-PARITY: no parity fixture moves except the two protected hash copies the regold script rewrites
  (`parity/abilities/hash-tape.json`, `protocol/fixtures/archetypes_v1.json`), by their documented commands.
- P-AUG-CONTENT-1-fix closed the same class of hole for override numbers (`cards/rework_guard_tests.rs`).

## 7. Behaviour to preserve
- Every chest offer and reward draw, both rules: no file under `reward_chests/` or `systems/` changes.
  `reward_chests/pool_tests.rs` (incl. `legacy_offers_and_draws_are_the_integration_heads`) stays green.
- The Legacy table's chest inputs are exactly today's: pool `INTERACTIVE` in source order, no faction pair, no
  weighting; `Tuning::of(Legacy).offer(id)` is `Some(Offer::Any)` for the 14 and `None` for the other 66 ids. This is
  what "the Legacy pool fold equals today's behaviour" means here: the fold reads these inputs (pinned), the digest itself
  moves.
- No checkpoint, replay, observation or protocol version moves; only `content_hash` (and its six copies).

## 8. Acceptance checks
- `cargo test -p rh-sim --test chest_pool_content` green (the oracle).
- `cargo test -p rh-sim --test content --test progression_content` green after the regold.
- `scripts/regolden-content.sh --check` exits 0.
- `scripts/gate.sh --quick --allow-artefacts --oracle packet/P-AUG-POOL-1-fix` passes.

## Existing tests that pin what this packet changes
- `crates/rh-sim/tests/content.rs:566-574` `hash_golden` / `GOLDEN_CONTENT_HASH` (`0xc890_f52f_d7ab_4546`): moves, by
  the regold script only (no `oracle-amend:`: the script is the documented route, and P-ELITE-TUNE-1 moves it first).
- `crates/rh-sim/tests/content.rs:991` `every_const_in_the_source_tree_is_registered_or_explained` with `NON_GAMEPLAY`
  rows `:66-67`, `:73-74`: stays green; the four reasons are reworded (or the rows move to `TABLES_FOLDED_BY_HASH`, which
  that test also requires to name declared consts, `:1021`).
- `crates/rh-sim/tests/progression_content.rs:7-27`
  `actual_progression_words_and_order_participate_in_the_aggregate_identity`: asserts
  `content_hash_of_progression_words(visit_progression_content words) == content_hash()` and more than 200 words. If the
  pools are folded inside `visit_progression_content` it stays green unchanged (more words); if folded as a separate
  stream in `definitions.rs` it also stays green (that stream is not part of the progression words). No conflict.
- `crates/rh-sim/src/reward_chests/pool_tests.rs` (P-AUG-POOL-1's oracle): asserts the real tables' pools, that both
  real tables are all `Offer::Any`, and the Legacy draw golden. Not touched; must stay green.
- `crates/rh-sim/tests/augment_rules.rs` `the_card_table_is_the_legacy_catalog_under_legacy_and_for_unchanged_cards_under_rework`:
  `Tuning::of(Legacy).pool() == INTERACTIVE`, Rework `== REWORK_POOL`. Not touched; stays green.
- The regold copies (`tools/unity-browser-reload-proof/check.mjs`, `normal-companion.mjs`,
  `client-unity/UnityProject/Assets/Scripts/BakedContent.cs`, `parity/abilities/hash-tape.json`,
  `protocol/fixtures/archetypes_v1.json`): rewritten by the script only.

## Oracle files (read-only for implementers)
- `crates/rh-sim/tests/chest_pool_content.rs`
- `docs/backlog/packets/P-AUG-POOL-1-fix.md`

The seam (stub commit; the implementer replaces the body of `content_hash_of_chest_pools` and makes `content_hash`
fold `chest_pool_tables()`), in `crates/rh-sim/src/content/fold.rs`, re-exported as `rh_sim::content::*`:

```rust
#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub struct ChestPoolTable<'a> {
    pub rule: crate::AugmentRule,
    pub pool: &'a [crate::cards::CardId],
    pub faction_only: &'a [(crate::cards::CardId, FactionId)],
    pub weight_eye_for_an_eye: bool,
}
impl ChestPoolTable<'static> { pub fn of(t: &'static crate::cards::Tuning) -> Self }
pub fn chest_pool_tables() -> [ChestPoolTable<'static>; 2]          // [Legacy, Rework], read from the Tuning statics
pub fn content_hash_of_chest_pools(registry: &[Entry], stagger_weapons: &[WeaponId],
                                   tables: &[ChestPoolTable<'_>]) -> u64 // STUB: ignores `tables`
```

and in `crates/rh-sim/src/cards/tuning.rs` (`impl Tuning`, real accessors, no behaviour):
`pub fn of_rule(rule: AugmentRule) -> &'static Tuning`, `pub fn faction_only(&self) -> &'static [(CardId, FactionId)]`,
`pub fn weights_eye_for_an_eye(&self) -> bool`.

What each oracle test pins (`chest_pool_content.rs`):
- `the_folded_chest_inputs_are_the_real_tables_and_legacy_is_todays`: `chest_pool_tables()` is `[Legacy, Rework]`, each
  equal to `Tuning::of(config)`'s accessors (default config, `live_content()`); Legacy = `INTERACTIVE` in source order,
  no pair, flag off, and `offer` is `Any` for exactly the 14; Rework = `REWORK_POOL`, its pairs, flag on. Green at the
  stub (input pin).
- `the_seam_with_the_real_tables_is_the_content_hash`: `content_hash_of_chest_pools(REGISTRY, STAGGER_WEAPONS,
  &chest_pool_tables()) == content_hash()`. Green at the stub; ties the seam to the real identity, so a perturbation
  that moves the seam is a perturbation of `content_hash`.
- `adding_or_removing_one_pool_card_moves_the_content_hash` (**red at the stub**): for each table, removing each pool
  card, and adding each live augment outside it, moves the hash; all distinct.
- `adding_or_changing_a_faction_pair_moves_the_content_hash` (**red at the stub**): for each table, adding
  `(card, faction)` for its first pool card and each of the five factions, and a second pair, moves the hash; all
  distinct.
- `flipping_the_eye_for_an_eye_weight_flag_moves_the_content_hash` (**red at the stub**): flipping each table's flag
  moves the hash; the two flips differ.
- `which_rule_holds_which_table_is_folded` (**red at the stub**): swapping the two tables, or giving Legacy the Rework
  pool, moves the hash.

## 9. Validation commands
- `cargo test -p rh-sim --test chest_pool_content`
- `cargo test -p rh-sim --test content --test progression_content`
- `scripts/regolden-content.sh` then `scripts/regolden-content.sh --check`
- `scripts/gate.sh --quick --allow-artefacts --oracle packet/P-AUG-POOL-1-fix`

## 10. Risk and suggested model tier
Opus implementer, `reviewer-opus`: shared content-identity file and a hash move that every client, replay and fixture
copy must follow; a fold that reads copies instead of the `Tuning` statics would pass the seam tests and still miss a
later table edit.

## 11. Known traps
1. **Fold the real tables.** `content_hash` must fold `chest_pool_tables()` (which reads `Tuning::of_rule`), not a copy
   of the arrays; the seam and `content_hash` must share one code path (e.g. a `visit_chest_pool_content(tables, word)`
   called by both). The oracle checks the identity with the real tables; the reviewer checks there is no second list.
2. **Unambiguous framing.** Fold a tag, the rule, the pool length then ids, the pair count then (card, faction) pairs,
   then the flag, per table, so moving a card from the pool to a pair, or between tables, cannot collide (the oracle
   asserts the perturbations are distinct). Use `CardId::index()` and `FactionId::code()` (the wire identities).
3. **Pool order is not behaviour** (`augment_pool` walks `CardId::ALL`); the oracle does not pin whether reordering the
   pool array moves the hash. Folding in array order is fine.
4. **Where to fold.** Simplest inside `fold.rs` alone: fold the tables at the end of `visit_progression_content` through
   a shared visitor, and make the seam call `content_hash_of_progression_words` with the words of
   `visit_progression_content` over the given tables. `tests/progression_content.rs` stays green either way.
5. **Regold once, after P-ELITE-TUNE-1**, through `scripts/regolden-content.sh` (never hand-edit a copy); then
   `--check`. Protected copies (`parity/`, `protocol/fixtures/`) are written by the script's documented commands.
6. **`NON_GAMEPLAY` wording.** `every_const_in_the_source_tree_is_registered_or_explained` reads `(file, name)` only;
   the reasons are prose, but they are the record a later packet trusts. Name the fold that now covers each table.

## Notes from previous attempts
None.

Out of scope, flagged for the coordinator (not this packet): `LEGACY` / `REWORK` (`cards/tuning.rs`) also pick *which*
registered constant fills each `CardNumbers` field, and `REWORK_OVERRIDES` which constant fills each override row; the
registry folds the values by name, not the assignment, so swapping two equal-typed `REWORK_*` names between fields would
not move the hash. P-AUG-CONTENT-1-fix's guard checks only that each changed number is some `REWORK_*` value. The
`NON_GAMEPLAY` row of `REWORK_OVERRIDES` (`tests/content.rs:68`, "EMPTY here") is also stale.
