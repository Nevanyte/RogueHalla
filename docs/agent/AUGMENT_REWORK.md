# The augment and elite rework: how to change a number

The owner approved the elite and augment rework on 2026-11-18 (plan of record:
[docs/backlog/AUGMENT_ELITE_REWORK.md](../backlog/AUGMENT_ELITE_REWORK.md)). Every rework change is **live-only**: it
applies when `SimConfig::augments` or `SimConfig::elites` is `Rework` (`SimConfig::live_content()`), and never in the
default world (`Legacy`) that parity, headless runs and most tests play. P-AUG-RULES-1 built the switch and the seams;
this guide says how a later packet uses them.

## The two rules

- `SimConfig::augments: AugmentRule { Legacy, Rework }` and `SimConfig::elites: EliteRule { Legacy, Rework }`
  (`crates/rh-sim/src/statics.rs`). `Legacy` writes nothing (JSON `null`, no word in the state hash), so a `Legacy`
  world's hashes, checkpoints and replays are the ones it had before the fields existed. `Rework` is `"rework"` in
  JSON and `"augments=rework"` / `"elites=rework"` in the state hasher.
- Do not branch on the rule in game code. Read the rule's **table** instead (below): a reader that asks
  `config.augments == Rework` is a reader the tables cannot retune.

## The card table: `cards::Tuning`

`MatchStatic::cards: &'static cards::Tuning` (`crates/rh-sim/src/cards/tuning.rs`) is the table of the world's
`augments` rule, picked by `Tuning::of(&config)` when the statics are built (a static, never built per step). It
answers:

- `def(id)`: the card definition this world plays (the override row if the table has one, else the generated
  catalog's `CardId::definition()`); `rows(id, faction)`: that definition's rows for a faction;
- `numbers()`: the named numbers that are not card rows (`CardNumbers`: Plant Your Feet, Gambler's two chances and
  two factors, Wishful Thinking's budget, window and floor, the G1 and G4 caps, Third Time's divisor and extra);
- `pool()`, `augment_pool(pickers)`, `conditional_pool(pickers)`: what a chest may offer;
- `fold_definition()` (crate-only): the stat fold's definition with the table's G1 (`hit`) and G4 (`hpRace`) clamps.

### How a reader reaches it

- With the match statics: `k.cards` (systems `cx.k.cards`, hooks `hx.k.cards`).
- A helper without `k` takes `tuning: &cards::Tuning` as a parameter, and its caller passes `k.cards`
  (`Build::rows`, `modifiers::outgoing_damage`, `execute_share`, `hp_race_mult`, `maximum_packet`,
  `checkpoint::valid_ranged_packet`, `Runtime::valid`, `kill_augments::roll_with`, `reward_chests::drop_augment_in`,
  `modifiers::maximum_mark_rate_in`, `cards::wishful_thinking`).
- The no-`k` public forms (`reward_chests::augment_pool`, `kill_augments::invested_stats` / `roll` / `build_marks`,
  `modifiers::maximum_mark_rate`) answer under `Tuning::legacy()`.
- Never read `CardId::definition()` for a card's rows or numbers in game code: an override would apply in one place
  and not another (the fold but not the restore check, the hook but not the fold). `CardId::definition()` stays
  right for what the shape guard keeps equal: `unique`, `tier`, `kind`, `base`, `requires`, flags, tags, title.

Readers still on the legacy numbers, and why (each is a later packet's to move if its numbers change):

| Reader | Why |
|---|---|
| `abilities/checkpoint.rs` Wishful trim bounds (`WISHFUL_BUDGET_MS`, `WISHFUL_WINDOW_MS`) | P-AUG-TUNE-1 retunes Wishful and moves the bound with it |
| `cards/fight_clock.rs` `watches` | reads only the `while` condition, a shape the guard keeps equal; its caller (`systems/items.rs`) has no `k` |
| `kill_augments::build_marks` (observation marks) | display only; its callers (`fx_report.rs`, `observe.rs`) hold no `k` |
| `quests/rules.rs` (card base and tier) | shape fields the guard keeps equal |
| `content_hash` (`maximum_mark_rate()`) | the content identity is the legacy one until P-AUG-CONTENT-1 folds the rework rows |

### Adding an override row (P-AUG-CONTENT-1 and later)

The override table is ONE hand-written file, `crates/rh-sim/src/cards/rework_overrides.rs`:
`pub(crate) static REWORK_OVERRIDES: &[CardDef] = &[ ... ];`.

1. Copy the card's `CardDef { ... }` block from `crates/rh-sim/src/cards/catalog_data.rs` **unchanged in layout**:
   one field per line, in the order `id`, `kind`, `tier`, `base`, `requires`, `unique`, `ready`, `title`, `text`,
   `tags`, `rows`, `selections`. Keep rows in `CardId` order across the table. The Unity mirror test
   (`client-unity/UnityProject/Assets/Tests/Editor/BuildDrawerEditModeTests.cs`) reads `id`, `title`, `text` and
   `tags` from this file with the same regex it uses on `catalog_data.rs`, so the layout is a contract.
2. Change only **row numbers** and the **`text`** string (a selection's `text` too). `cards::check_override` refuses
   anything else: a changed id, kind, tier, base, requires, unique, ready, title, tags, a row added or removed, a row's
   variant, key, flag, trigger event, `once`, action, which numbers are present (`None` vs `Some`), a selection's
   faction, and any non-finite number. `tuning_tests::the_real_override_table_passes_the_guard` runs the guard over
   the real table, and every debug build re-runs it when a `Rework` world is built.
3. A changed number is a **registered constant by name**, never a literal: add a row to the `content!` registry in
   `crates/rh-sim/src/constants.rs` (append; e.g. `REWORK_COMMITTED_DAMAGE: f64 = 0.22, "share";`) and write
   `v: crate::constants::REWORK_COMMITTED_DAMAGE` in the override row. Unchanged numbers are copied as written in
   `catalog_data.rs`. Registering moves the content hash: regenerate its pinned copies with the documented command
   (`scripts/regolden-content.sh`, P-CONTENT-REGOLDEN-1).
4. The packet that first fills the table moves `REWORK_OVERRIDES` from `NON_GAMEPLAY` to `TABLES_FOLDED_BY_HASH` in
   `crates/rh-sim/tests/content.rs` and folds the table into the content hash.
5. One override row reaches every reader at once (fold, cached-fold restore check, ledger, hooks, pools, the
   ranged-packet envelope, the training console). Nothing else is edited for a numbers-only change.

### Changing a named number

The `REWORK` table's `numbers` (`cards/tuning.rs`) are registered constants by name; under P-AUG-RULES-1 they are the
legacy ones. To retune one: register `REWORK_<NAME>` in `constants.rs` and point the `REWORK` field at it. Notes:

- `outgoing_row_cap` is both the stat fold's `hit` clamp and the outgoing row ceiling; `hp_race_cap` is both the
  `hpRace` clamp and the Race to 0 ceiling. Never edit `stat_fold/definition.rs` (it is the legacy content identity).
- Gambler reads `roll < up_chance` up, `roll < up_chance + down_chance` down.
- Third Time reads `(streak + 1) % third_time_every == 0` and adds `third_time_extra` applications. Its legacy `3`
  and `1.` are the two literal numbers in the `LEGACY` / `REWORK` tables (legacy wrote them inline); register them
  when the rework moves them.
- Wishful's budget, window and floor also bound the trims at restore (`abilities/checkpoint.rs`, still legacy):
  whoever retunes them moves that bound too.

## The elite table: `mob_runtime::EliteTuning`

`EliteTuning { k, hp_mult, dmg_mult }` (`crates/rh-sim/src/mob_runtime.rs`); `EliteTuning::of(rule)` returns the
static `LEGACY_ELITES` or `REWORK_ELITES` (both the legacy `ELITE_K` / `ELITE_HP_MULT` / `ELITE_DMG_MULT` here).

- Readers with `k` read `EliteTuning::of(k.config.elites)` (camp spawn, the mob restore checks).
- Readers without `k` (`World::faction_spec` and through it `defense`, `paced_faction_spec`, `signature_damage`;
  `point_damage`; `sentenced::npc_pulse`; the practice-mob birth) read `EliteTuning::of_world(w)`: the rule the world's
  mob runtime stores, `MobRuntime::elites`.
- `MobRuntime::elites` is set where the runtime is built to `EliteTuning::stored_rule(config.elites)`: the config's
  rule, except that a rule whose numbers equal the legacy ones is stored as `Legacy` (it writes nothing). So today a
  `Rework` world's world bytes equal the `Legacy` world's; once `REWORK_ELITES` differs, a `Rework` world's runtime
  writes `"rework"` and the state hash folds it. `check_world` (`systems/mobs.rs` `check_elite_rule`, run by `mobs::validate`) admits exactly
  that one value per statics digest.
- `Mob::born_with(elites, ...)` takes the table for an elite's spawn hp; `Mob::born` is the legacy form for births that
  are never elite and for tests.
- The bots (`rh-policy` `bots/planning.rs` `mob_faction`, `temporal_percept/strategy/hunt_adapter.rs` `mob_max_hp`)
  read `EliteTuning::of(EliteRule::Legacy)`: their maths has no match context. P-AUG-BOTS-1 (rh-policy only) threads
  the room's rule there: a `MatchStart` field beside `frost_wish` (`rh-policy/src/lib.rs`), and `&EliteTuning` into
  `matchup_share` / `mob_faction` / `mob_max_hp` (callers `knowledge.rs`, `planning.rs`, `mob_hunt.rs`). Until it lands,
  a retune does not reach the bots (coordinator decision on the P-AUG-RULES-1 review note, 2026-11-19).

To retune (P-ELITE-TUNE-1): register `REWORK_ELITE_K` / `_HP_MULT` / `_DMG_MULT` and point `REWORK_ELITES` at them.
The `Legacy`/`Rework` equivalence test (`tests/augment_rules.rs`) then fails by design for the elite numbers, and
`systems/mobs.rs` `elite_rule_tests` (which expects the stored rule to be null under both rules) changes too: amend both
as that packet's oracle.

## Checks that guard the seam

- `crates/rh-sim/tests/augment_rules.rs`: the rules' encoding, the 64 statics identities of the silent switches, the
  tables equal legacy, `Legacy` and empty `Rework` play the same match, a `Rework` checkpoint with fold cards and a
  packet in flight restores.
- `crates/rh-sim/src/cards/tuning_tests.rs`: the shape guard, the real table through it, readers through a doctored
  table (fold and restore check, G1 in both places, Gambler), and the `Rework` ranged envelope over the fold cards.
- `crates/rh-sim/tests/hash_coverage.rs`: the `Rework` scenarios and the `MobRuntime::elites` row.

## Moving the content hash: `scripts/regolden-content.sh`

A packet that changes content moves `content_hash`. Never hand-edit the copies. After the change, run
`scripts/regolden-content.sh` (it computes the hash with `cargo run -q -p rh-sim --example content_hash`). It rewrites
the four text copies (`crates/rh-sim/tests/content.rs` `GOLDEN_CONTENT_HASH`, the two `tools/unity-browser-reload-proof`
`.mjs` files, `BakedContent.cs`), then runs the documented commands for the two protected copies
(`cargo test -p rh-sim --lib record_ability_hash_tape -- --ignored` and `cargo run -p rh-parity --bin hitbox-fixtures`).

- `--check` lists stale copies as `STALE <path>` (exit 1) without running the two protected commands (it still runs `cargo run --example content_hash` unless `--hash HEX` is given).
- `--dry-run` prints `WOULD EDIT` / `WOULD RUN` lines; `--text-only` skips the protected commands.
- `BakedContent.cs` is a build artefact by name: a diff that changes it needs `scripts/gate.sh --allow-artefacts`.
- Copies of the hash exist only in those six files; a new value pin elsewhere fails `regolden_content`.
