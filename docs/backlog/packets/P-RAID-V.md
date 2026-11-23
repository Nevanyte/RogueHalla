# P-RAID-V: the value table (a row per quest reward and per augment)

- **Serves:** not in the plan: owner approval 2026-11-22 (plan of record `docs/backlog/BALANCE_LAB_PATCH.md` section 1,
  wave row "3 | P-RAID-V"; `docs/OWNER_DECISIONS.md` row "Balance patch, test bot, per-elite numbers")
- **Implementer tier:** implementer-sonnet
- **Reviewer:** reviewer
- **Integration base:** `coord/int-83` @ `84d666014`
- **D-rules touched:** D1 loose proof (owner direction 2026-11-10): the numbers are design-workbook estimates, not
  legacy; the oracle pins them once · D2 n/a · D5/D6/D9 none (rh-policy only; no sim, wire or observation change)

## 1. ID and outcome
`RaidBot`'s price table gets a row per quest reward and per augment, in camp-equivalents (one normal camp cleared = 12
stat stacks = 1), behind the two accessors that exist: `Prices::reward_value(def)` and `Prices::augment_value(card)`.
Today both return one flat placeholder (1.0). After this packet a Trade Up (5.6) is valued above Watchtowers (0.9) and
Pathfinder (3.2) above Pocket Sand (0.3), and the balance lab can read the same table, as `data/raid_values.json`, as
"what the bot believed" beside what it measured. No executor reads the rows yet (the quest walker, in another packet,
calls `reward_value`), so today's play does not change.

## 2. Dependencies
`P-RAID-0` and `P-RAID-0-fix` (merged; they created `Prices`). None else.

## 3. Relevant legacy paths
None: RaidBot has no legacy counterpart. Sources of the numbers: the design workbook (Top_25 sheet, "Value V (CE)";
Augment_Value sheet, "Total value V (CE)", scaled by the coordinator for the live Augment_Tuning numbers), and
`docs/QUEST_VALUE_MODEL.md` sections 1-2. They are estimates of estimates, to be replaced by lab measurements.

### Established facts (verified in code on the base)
1. **Accessors** (`crates/rh-policy/src/bots/raid/values.rs`):
   `pub fn reward_value(&self, _def: &str) -> f64 { self.reward_default }` (:81, key = the board def id, as
   `goal::Target::Quest::def`) and `pub fn augment_value(&self, _card: &str) -> f64 { self.augment_default }` (:89;
   key is a `&str` card id). `Prices` is `#[derive(Clone, Copy, Debug, PartialEq)]`, `Prices::LOCAL` is a `const`,
   `farm_only()` is a `const fn` zeroing `reward_default` and `augment_default` (its doc says a table packet must clear
   rows too). Callers outside `values.rs`: none in non-test code (only doc text in `raid/mod.rs:19-20`). Test callers:
   `raid/fix_tests.rs:120-138` (`prices_answer_for_quest_rewards_and_augments_from_their_table`). `Prices { .. }` struct
   literals outside `values.rs`: `fix_tests.rs:126` (`..Prices::LOCAL`) and `fix_tests.rs:415` (`..Prices::LOCAL`), and
   `Prices::LOCAL` / `farm_only()` uses elsewhere (27 occurrences under `crates/`, `tests/raid_bot.rs` uses
   `farm_only()`). All of them must keep compiling without an edit.
2. **Enumeration from public rh-sim API.** Quests: `rh_sim::quests::board::catalog::DEFINITIONS` (`[BoardDef; 25]`,
   `pub const`, `BoardDef.id: &'static str`, catalog.rs:172, :200); `QuestId::ALL` (25) and
   `QuestId::definition()` also public (`rh-server/src/quest_bridge_tests.rs:9` reaches `QuestId::ALL` from outside
   rh-sim). The 25 ids in the brief all equal the code's ids (checked against catalog.rs :202-466). Augments:
   `rh_sim::reward_chests::REWORK_POOL: &[CardId]` (19, `reward_chests/pools.rs:30`) and `rh_sim::cards::CardId::NATIVE`
   (14, `cards/catalog.rs:191`; `choice.rs:10,46` already reaches natives this way); the key is `CardId::as_str()`
   (camelCase, e.g. `"markedMan"`, `"hereComesTheBoom"`; `choice.rs:47` matches `def.id.as_str() == id`). All of it is
   publicly reachable, so the oracle enumerates from the code, not from a checked-in list. The coordinator's 25 + 19 +
   14 names all match the code's titles (`CardDef.title`, e.g. "Third Time's the Charm", "A Race to 0" =
   `CardId::RaceToZero`, "Executioner's Ledger"); nothing is missing or misnamed.
3. **`Copy` and `const`.** Rows are `&'static [(&'static str, f64)]` fields: a shared slice reference is `Copy`,
   `PartialEq` and `Debug` derive over it, and a `&[(..)]` literal is a valid `const` initializer, and `..Prices::LOCAL`
   struct-update copies the fields. So `Copy`, `const LOCAL`, `const fn farm_only` (set the two rows to `&[]`) and the
   `fix_tests.rs` literals all hold.
4. **Precedent for a checked-in data file.** `crates/rh-policy/data/brain_tuning.json` (format `rh-brain-tuning/1`) is
   checked against the compiled table by `crates/rh-headless/tests/tune.rs:344` (`the_checked_in_table_is_the_compiled_tables`,
   reads it with `env!("CARGO_MANIFEST_DIR")` and `serde_json`; `rh-headless/src/tune.rs:7`). `serde_json` is already a
   dependency of rh-policy (`Cargo.toml:18`).

## 4. New paths
- `crates/rh-policy/data/raid_values.json`
- `crates/rh-policy/src/bots/raid/values_tests.rs` (oracle; its `#[cfg(test)] mod values_tests;` line is in `raid/mod.rs`
  from the stub)

## 5. Exclusive write scope
- `crates/rh-policy/src/bots/raid/values.rs`
- `crates/rh-policy/data/raid_values.json` (replace the empty sections with the rows)

Nothing else. (`raid/mod.rs` already carries the `mod` line from the stub commit.)

## 6. Contracts and references
- R-ML: no tuning pipeline writes these numbers; a person edits `values.rs` and the JSON together in one reviewed packet,
  and the JSON says so in its `_comment`.
- `rh-policy` clippy bans hash-ordered collections: the rows are slices, lookups are linear.

## 7. Behaviour to preserve
- `Prices` stays `Copy`; `Prices::LOCAL` stays a `const`; `farm_only` stays a `const fn`.
- Every non-row number of `Prices` is unchanged; `reward_default` and `augment_default` stay 1.0.
- A def or card with no row returns the flat default, as today.
- `crates/rh-policy/tests/raid_bot.rs` (built with `farm_only()`), `raid/fix_tests.rs` (incl. the test at :111, which
  asserts defaults for the unknown keys `kill_slimes`, `anything_else`, `any_card`, `other_card`, `another` under a
  doctored default) and `raid/review_tests.rs` stay green and unedited. No conflict: none of them names a real def or
  card key.

## Design (what the implementer builds)
- Add `pub reward_rows: &'static [(&'static str, f64)]` and `pub augment_rows: &'static [(&'static str, f64)]` to
  `Prices`, filled in `LOCAL` with the rows below (reward key = board def id; augment key = `CardId::as_str()`).
  Accessors: first row whose key equals the argument, else the flat default.
- `farm_only()` sets both row fields to `&[]` as well as zeroing the defaults (update its doc).
- `data/raid_values.json`: `{"schema": "raid-values/1", "_comment": "<R-ML note: hand-edited with values.rs in one
  reviewed packet; no tool rewrites it>", "rewards": {"source": "<...>", "rows": {"the_warden": 2.0, ...}},
  "augments": {"source": "<...>", "rows": {"markedMan": 7.0, ...}}}`, the same numbers as the slices.
- Update the module and struct docs that say "until P-RAID-V".

### The rows (camp-equivalents; estimates of 2026-11-22, to be replaced by lab measurements)
Quest rewards (workbook Top_25 "Value V (CE)", 0.1 rounding): the_warden 2.0, hold_the_fort 3.6, bounty_hunter 3.5,
smugglers_run 2.5, two_banners 3.0, ember_walk 2.0, the_burning_crown 5.2, let_him_cook 3.7, elite_slayer 2.1,
tuber_eats 2.2, barrow_run 1.5, the_bargain 2.7, a_cruel_joke 2.0, a_bone_to_pick 1.8, feed_or_hunt 1.5, rime_hunt 3.5,
watchtowers 0.9, whispers_in_the_brush 0.8, even_skeletons_get_cold 1.5, lost_brother 2.5, pilgrims_path 2.0,
gatecrasher 2.5, letters 2.0, stay_a_while 2.0, trade_up 5.6.

Augments (workbook Augment_Value "Total value V (CE)", scaled for the live Augment_Tuning): natives markedMan 7.0,
pathfinder 3.2, marksmansTally 2.4, eighthStrike 1.9, calluses 1.3, bastion 1.2, hazardWalker 1.1, glacier 0.9,
stoked 0.9, overflow 0.8, corpseHoarder 0.6, wishEngine 0.5, trophyHunter 0.5, executionersLedger 0.45; catalog pool
committed 5.4, machineGun 2.5, plantYourFeet 2.3, fistsOfFury 1.9, tankEngine 1.9, gambler 1.9, hereComesTheBoom 1.7,
hemorrhage 1.5, overkill 1.4, thirdTimesTheCharm 0.9, spikedShell 0.9, secondWind 0.8, slowBurn 0.6, bestOffense 0.6,
eyeForAnEye 0.5, siphon 0.5, wishfulThinking 0.4, pocketSand 0.3, raceToZero 0.0.

## 8. Acceptance checks
- `scripts/gate.sh --quick --oracle packet/P-RAID-V` passes.
- `cargo test -p rh-policy values_tests` green (4 tests).

## Oracle files (read-only for implementers)
- `crates/rh-policy/src/bots/raid/values_tests.rs`
- `docs/backlog/packets/P-RAID-V.md`

The stub commit (not oracle) adds the `mod values_tests;` line in `raid/mod.rs`, the two empty row fields in
`values.rs`, and `crates/rh-policy/data/raid_values.json` with empty sections.

## 9. Validation commands
- `export PATH="$HOME/.cargo/bin:$PATH"; cargo test -p rh-policy values_tests`
- `cargo test -p rh-policy raid` (fix_tests, review_tests, tests/raid_bot.rs unchanged and green)

## 10. Risk and suggested model tier
Low: one file of data plus two lookups. Sonnet. Hand-typing 58 numbers twice is the risk; the oracle pins both copies.

## 11. Known traps
- Card keys are camelCase `as_str()` ids, not titles.
- `raceToZero` is a real row at 0.0, distinct from "no row" (which gives 1.0).
- `farm_only()` must clear the rows, or `raid_bot.rs` stops being a farm-only test once the quest walker lands.
- JSON object key order is not significant; the test compares by key and count.
- Numbers are workbook estimates; `values_tests.rs` is the one place that pins them, and a later number packet amends it.

## Notes from previous attempts
