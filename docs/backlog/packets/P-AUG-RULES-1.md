# P-AUG-RULES-1: the elite and augment rework seam (two live-only rules, a rule-aware card table, an elite table; no behaviour change)

- **Serves:** ORDER_OF_OPERATIONS phase 2, item 13 (P-CARDS-2/3, P-MOBS-2), under the owner's 2026-11-18 approval
  of the elite and augment rework (`docs/OWNER_DECISIONS.md` row "Elite and augment rework", 2026-11-18; plan of record
  `docs/backlog/AUGMENT_ELITE_REWORK.md`, "Decisions taken" 1 and "Architecture").
- **Implementer tier:** implementer-opus (rh-sim state, statics digest, save/restore checks; cross-crate: rh-sim,
  rh-server, rh-headless, rh-policy)
- **Reviewer:** reviewer-opus
- **Integration base:** feature/unity-rework
- **D-rules touched:** D1 loose proof (owner direction 2026-11-10); this packet changes no outcome, so its proof is
  equivalence (Legacy and Rework with empty tables play the same match) and every existing parity fixture stays
  byte-identical · D2 n/a · D5/D6/D9 none

## 1. ID and outcome
Every later rework packet changes numbers and behaviour only in the live world. This packet builds the switch and the
seams, and changes nothing a player can see:
- Two `SimConfig` rules, `augments: AugmentRule { Legacy (default), Rework }` and `elites: EliteRule { Legacy (default),
  Rework }`, exactly like `CampBuffRule` / `FrostWish` (`crates/rh-sim/src/statics.rs:276-360`): `Legacy` serializes as
  nothing and folds no hash word, `Rework` is `"rework"` in JSON and a tag of its own in the state hasher.
  `SimConfig::live_content()` (`statics.rs:435-460`) selects `Rework` for both; `SimConfig::default()` keeps `Legacy`.
- A card tuning table, `cards::Tuning`, derived on `MatchStatic` (`#[serde(skip)] cards: &'static Tuning`, built from
  the config like `hazards` / `blink_ring`, `statics.rs:14-96`), in the manner of `abilities::definition_for`
  (`abilities/mod.rs:390-398`). It answers `def(id)`, the named numbers (`CardNumbers`) and the pools. Under `Rework` it
  reads a hand-written override table that is EMPTY in this packet, so it equals legacy.
- Every reader of card rows and of the named numbers goes through the table, so a later override row is applied
  everywhere at once (fold, restore check, ledger, hooks, pools, envelope).
- An override-shape guard: an override may differ from its legacy definition only in row numbers and the `text`
  string (coordinator amendment, 2026-11-19); `id`, `title`, `kind`, `tier`, `base`, `requires`, `unique`, `ready`,
  `tags`, every row's variant and key (flags included) and the selections' factions and row shapes must be equal.
- The override table is ONE hand-written file, `crates/rh-sim/src/cards/rework_overrides.rs`, holding
  `pub(crate) static REWORK_OVERRIDES: &[CardDef] = &[ ... ];` whose entries are written exactly in the
  `catalog_data.rs` layout (`CardDef { id: CardId::<Id>, kind: ..., tier: ..., base: ..., requires: ..., unique: ...,
  ready: ..., title: "...", text: "...", tags: &[...], rows: &[...], selections: &[...] }`, one field per line, in
  that order), so the C# mirror test's regex (`BuildDrawerEditModeTests.cs:28`) reads the live text from it unchanged.
- An elite table, `mob_runtime::EliteTuning { k, hp_mult, dmg_mult }`, read wherever `ELITE_K` / `ELITE_HP_MULT` /
  `ELITE_DMG_MULT` are read today; legacy values under both rules.
- A guide, `docs/agent/AUGMENT_REWORK.md`: how a later packet adds a rework override row or a rework number.

When this lands: no state hash, content hash, checkpoint, replay or parity fixture of a `Legacy` world moves; a
`Rework` world differs from the same `Legacy` world only by its statics digest (so its state hashes), and plays the same
match.

## 2. Dependencies
None (wave 1). P-AUG-CONTENT-1, P-SIM-BENCH-SEAM-1, P-AUG-PLANT-1 and every later rework packet build on it.

## 3. Relevant legacy paths
The rework itself is not legacy (owner-approved new design); these are the legacy numbers the seam must reproduce
under both rules (verified with `rg`):
- `packages/shared/src/constants.ts:237` `HP_RACE_CAP = 2.5`, `:242` `OUTGOING_ROW_CAP = 2.5`, `:276`
  `PLANT_FEET_BONUS = 0.25`, `:278-280` `WISHFUL_BUDGET_MS` / `WISHFUL_WINDOW_MS` / `WISHFUL_FLOOR_SHARE`, `:286-288`
  `GAMBLER_CHANCE` / `GAMBLER_UP` / `GAMBLER_DOWN`, `:485` `ELITE_K = 0.65`, `:513-514` `ELITE_HP_MULT = 2.0`,
  `ELITE_DMG_MULT = 1.5`.
- `packages/shared/src/mods.ts:284` (G1: the fold clamps the `hit` mult at OUTGOING_ROW_CAP) and `:288` (G4: the
  `hpRace` mult at HP_RACE_CAP); `packages/server/src/sim/mods.ts:685` (the outgoing row ceiling `OUTGOING_ROW_CAP /
  own`).
- `packages/server/src/sim/cardState.ts:99-105` (Gambler: up below `GAMBLER_CHANCE`, down below `2 * GAMBLER_CHANCE`).
- `packages/server/src/sim/onHit.ts:39` (Third Time: every third hit in a row adds one on-hit application).
- `packages/server/src/sim/cardHooks.ts:364-383` (Wishful Thinking: budget, window, floor).
- `packages/server/src/sim/weapons.ts:333-339` (Plant Your Feet: `1 + PLANT_FEET_BONUS` on the packet's raw and mult,
  and as the range multiplier).
- `packages/shared/src/mobs.ts:37-41` (`mobFaction`: elite `k`, `hpMult`, `d`), `:211` (`mobTypeFaction`).
- `packages/shared/src/cards.ts:261` (Machine Gun), `:282` (Arcane Overload), `:287` (Committed), `:646-652`
  (`INTERACTIVE_AUGMENT_IDS`).

Rust readers today (each must read the seam after this packet):
- Card rows, `CardId::definition()`: `cards/mod.rs:290` (`has_flag`), `:323` (`add`), `:336` (`valid`), `:353`
  (`Build::rows`), `:405` (`on_blocked`, Tank Engine's fortify numbers); `cards/on_hit.rs:40, :286`;
  `cards/after_damage.rs:74`; `cards/on_hurt.rs:40`; `cards/on_overkilled.rs:35`; `kill_augments.rs:312`
  (`invested_scores`); `modifiers/mod.rs:82` (the mark-rate restore bound over every card), `:683` (`execute_share`),
  `:910` (`outgoing_damage`); `reward_chests/pools.rs:39, :51, :67`; `reward_chests/mod.rs:303-309` (the `requires`
  refusal); `systems/training/console.rs:70-129, :426`; `systems/mob_checkpoint.rs:66` (Hemorrhage's bleed-duration
  restore bound); `quests/rules.rs:67, :77` (base and tier only).
- `Build::rows` callers: `modifiers/mod.rs:195` (`Modifiers::valid`, the cached-modifier re-fold at restore,
  `:185-200`), `:330` (`recompute_core`), `:789` (`fold_build`, used by `maximum_packet`); `build_summary.rs:100`;
  `cards/fight_clock.rs:12`.
- Named numbers: `constants.rs:100-115`; readers `cards/mod.rs:126-129` (Gambler), `:237-241` (Wishful),
  `systems/melee.rs:138` (Plant bonus), `cards/on_hit.rs:236-245` (Third Time `% 3` and `+1`),
  `modifiers/mod.rs:665` (`hp_race_mult`), `:899-905` (outgoing ceiling); and the STAT FOLD's own copies,
  `stat_fold/definition.rs:47` (`hp_race_cap`) and `:52` (`outgoing_row_cap`), applied at `stat_fold/fold.rs:205-207`
  and `:216-218` and folded into the content hash (`stat_fold/definition.rs:101-113`).
- The ranged-packet envelope: `modifiers/mod.rs:805-835` (`maximum_packet`, folds only `cards::SUPPORTED` =
  Slow Burn, Pocket Sand, Siphon, with max boosts and `Farm::maximum()`), used by `checkpoint.rs:273-279`
  (`valid_ranged_packet`) at `:644` (projectiles) and `:916` (pending actions).
- Pools: `reward_chests/pools.rs:47-74` (`conditional_pool`, `augment_pool`), callers `reward_chests/mod.rs:107`
  (`drop_augment`), `systems/training/console.rs:408`.
- Elites: `state.rs:1083-1093` (`World::faction_spec` hp) and `:1121-1130` (`point_damage`), `mob_runtime.rs:413-427`
  (`spawn_hp`, called from `Mob::born` at `:334`, `:352`; `Mob::born` callers all hold `k`), `systems/sentenced.rs:540-541`
  (`npc_pulse`), `rh-policy` `bots/planning.rs:180-183` (`mob_faction`) and
  `bots/temporal_percept/strategy/hunt_adapter.rs:286-291` (`mob_max_hp`).

## 4. New paths
- `crates/rh-sim/src/cards/tuning.rs` (stub on `packet/P-AUG-RULES-1`; the implementer replaces the bodies):
  `Tuning`, `CardNumbers`, `ShapeError`, `check_override`.
- `crates/rh-sim/src/cards/rework_overrides.rs` (stub: the empty table, in its fixed layout; later packets fill it).
- `crates/rh-sim/src/mob_runtime/elite_tuning.rs` is NOT required: `EliteTuning` is stubbed in `mob_runtime.rs`; the
  implementer may move it.
- `docs/agent/AUGMENT_REWORK.md` (the guide).
- Oracle files: see "Oracle files" below.

## 5. Exclusive write scope
- `crates/rh-sim/src/statics.rs`, `crates/rh-sim/src/lib.rs`, `crates/rh-sim/src/cards/tuning.rs`,
  `crates/rh-sim/src/cards/rework_overrides.rs` (stays an empty table here),
  `crates/rh-sim/src/cards/mod.rs`, `crates/rh-sim/src/cards/on_hit.rs`, `cards/after_damage.rs`, `cards/on_hurt.rs`,
  `cards/on_overkilled.rs`, `cards/fight_clock.rs`, `crates/rh-sim/src/kill_augments.rs`,
  `crates/rh-sim/src/build_summary.rs`, `crates/rh-sim/src/modifiers/mod.rs`, `crates/rh-sim/src/checkpoint.rs`,
  `crates/rh-sim/src/reward_chests/pools.rs`, `crates/rh-sim/src/reward_chests/mod.rs`,
  `crates/rh-sim/src/systems/training/console.rs`, `crates/rh-sim/src/systems/mob_checkpoint.rs`,
  `crates/rh-sim/src/systems/melee.rs`, `crates/rh-sim/src/systems/ledger.rs`, `crates/rh-sim/src/state.rs`,
  `crates/rh-sim/src/mob_runtime.rs`, `crates/rh-sim/src/systems/mobs.rs`, `crates/rh-sim/src/systems/sentenced.rs`,
  `crates/rh-sim/src/hash.rs` (only if the elite seam stores a field), `crates/rh-sim/src/stat_fold/fold.rs` (only to
  take the fold caps from the table; `stat_fold/definition.rs` stays as it is)
- `crates/rh-policy/src/lib.rs`, `crates/rh-policy/src/bots/planning.rs`,
  `crates/rh-policy/src/bots/temporal_percept/strategy/hunt_adapter.rs`
- The full `SimConfig` literals the stub commit already updated (compile-only `Legacy` lines; edit only if a
  signature forces it): see the stub commit's file list.
- `docs/agent/AUGMENT_REWORK.md` (new).
- Not in scope: `Cargo.lock`, any `Cargo.toml`, `parity/**`, `protocol/fixtures/**`, `packages/**`,
  `crates/rh-sim/src/cards/catalog.rs` / `catalog_data.rs` (generated), `stat_fold/definition.rs`, `constants.rs`.

## 6. Contracts and references
- C-PARITY (every parity fixture byte-identical), C-DET / ADR 0007 (the digest pins the config; the new tags must not
  collide with any other config value: `statics.rs:313-326`), C-RNG (no new draw), C-TICK (no schedule row),
  C-OBS (nothing new is observed).
- `crates/rh-sim/AGENTS.md`: no_alloc, hash_coverage, check_world.
- `docs/backlog/AUGMENT_ELITE_REWORK.md` "Live-only pattern" and "Architecture".

## 7. Behaviour to preserve
- `SimConfig::default()` and every `Legacy` world: same statics digest, state hashes, checkpoints, replays, RNG draws,
  events, observations and content hash (`tests/content.rs:568`). `CardId::definition()` and the generated catalog do
  not change (`tests/card_catalog.rs`). The public `reward_chests::augment_pool` / `conditional_pool` keep their
  signatures and answers (`tests/reward_pools.rs`).
- A `Rework` world with the empty tables: the same events, world state and observations as the same `Legacy` world;
  only `statics.config.augments` / `.elites` and the digest differ.
- The ranged-packet envelope under `Legacy` is not loosened (restore acceptance of `Legacy` worlds unchanged).
- No allocation per step (`tests/no_alloc.rs`, `tests/cards_no_alloc.rs`): `Tuning` lookups are static, not built
  per call.

## 8. Acceptance checks
- `scripts/gate.sh --quick --oracle packet/P-AUG-RULES-1` passes; the coordinator's full gate on the merge.
- Every oracle file below green; the parity report MATCH unchanged; `tests/content.rs` content hash unchanged.

## Oracle files (read-only for implementers)
- (filled in by the `oracle:` commits)

## 9. Validation commands
- `export PATH="$HOME/.cargo/bin:$PATH"; cargo test -p rh-sim --features trusted-status-probes --test augment_rules`
- `cargo test -p rh-sim --features trusted-status-probes --lib cards::tuning`
- `cargo test -p rh-sim --features trusted-status-probes --test hash_coverage`
- `cargo test -p rh-server --test live_content`
- `scripts/gate.sh --quick --oracle packet/P-AUG-RULES-1`

## 10. Risk and suggested model tier
Opus: the statics digest and config encoding (a tag collision silently merges two identities), the restore checks
(a wrong envelope refuses live checkpoints or admits forged packets), and a reader left on `CardId::definition()`
(an override then applies to the fold but not to the restore check, or to the hook but not to the fold) are each
invisible while the tables are empty. Cross-crate (rh-sim, rh-server, rh-headless, rh-policy).

## 11. Known traps
- Coordinator amendment (2026-11-19): override rows carry the live card TEXT. The Unity EditMode test
  `client-unity/UnityProject/Assets/Tests/Editor/BuildDrawerEditModeTests.cs:38`
  (`DisplayCatalogExactlyMirrorsRustCardIdsTitlesDescriptionsAndTags`) regex-parses `catalog_data.rs` and demands the
  client's `CardDisplayCatalog` text equal Rust's exactly; the live client will show Rework text, so Rust holds it in
  `rework_overrides.rs` in the same layout. The mirror test is left alone here (the table is empty, so it stays green);
  a later packet amends it to prefer an override's text over `catalog_data.rs`.
- (survey below)

## Notes from previous attempts
