# P-BAL-ELITE-SEAM-1: a per-elite-kind multiplier seam under Rework, empty table, no number moves

- **Serves:** not in the plan: owner approval 2026-11-22 (balance patch, "Yes, per-elite numbers"; `docs/OWNER_DECISIONS.md`
  top row; plan of record `docs/backlog/BALANCE_LAB_PATCH.md` section 3 "Per-elite numbers" and wave 2 row
  P-BAL-ELITE-SEAM-1)
- **Implementer tier:** implementer-opus (rh-sim state readers, restore check, content hash)
- **Reviewer:** reviewer-opus
- **Integration base:** `feature/unity-rework` @ 83edb687f
- **D-rules touched:** D1 loose proof (owner direction 2026-11-10); no legacy counterpart (Rework-only seam, legacy has
  one global pair) · D2 n/a · D5/D6/D9 none

## 1. ID and outcome

Today the Rework elite numbers are one global pair (`REWORK_ELITE_HP_MULT` 1.5, `REWORK_ELITE_DMG_MULT` 1.0, owner
decision 2026-11-19). Measured after that tune the reference Duelist dies 17.4% overall but 46.7% at the Avalanche Troll
and 2.2% at the Executioner (`tools/balance-camps/README.md`, "Elite numbers: before and after P-ELITE-TUNE-1"). The
owner wants each elite moved toward 10-20%. This packet builds the seam only: `EliteTuning` gains a per-kind table
(`per_kind: &'static [EliteKindRow { ty, hp, dmg }]`), EMPTY in both `LEGACY_ELITES` and `REWORK_ELITES`, and two
accessors `hp_mult_of(ty)` / `dmg_mult_of(ty)`: with no row for `ty` they return the global field itself (bit-identical),
with a row they return `global * row`. Every rh-sim reader of an elite multiplier goes through them. When it lands no
number, hash, checkpoint or event changes; a later number packet fills the Rework table with measured rows.

## 2. Dependencies

None. Runs beside P-BAL-Q25-SEAM-1 (disjoint scope: nothing under `quests/board/**`). P-BAL-ELITE-BOTS-1 (rh-policy)
follows this one.

## 3. Relevant legacy paths

- `packages/shared/src/mobs.ts:37-41` (`mobFaction`): `hpMult = MOB_HP_MULT * (elite ? ELITE_HP_MULT : 1)`,
  `d = MOB_DMG_MULT * (elite ? ELITE_DMG_MULT : 1)`: one global pair, no per-kind number. `mobs.ts:211`
  (`mobTypeFaction`) then applies the type's role. Legacy has no per-kind elite number, so the `Legacy` table stays
  empty forever; the per-kind rows are a Rework-only difference (each later number packet proposes its DEVIATIONS row).

## Established facts (verified on 83edb687f)

### Fact 1: every read of an elite multiplier in `crates/rh-sim/src`

`rg '\.(hp_mult|dmg_mult)\b' crates/rh-sim/src` gives 14 hits; 4 are elite reads, the rest are the role multiplier
(`role_def().hp_mult` / `.dmg_mult`) or the mob-catalog role row folded into the content hash.

| Site | Reads | Computes | Mob type in hand? |
|---|---|---|---|
| `mob_runtime.rs:497` (`spawn_hp`) | `elites.hp_mult` | an elite's spawn max hp: `(faction.hp * k) * (MOB_HP_MULT * hp_mult) * role.hp_mult` | yes: parameter `ty: MobTypeId` |
| `state.rs:1155` (`World::faction_spec`) | `elites.hp_mult` | the mob's faction sheet hp (fed to `defense`, `paced_faction_spec` and every sheet reader) | yes: `m.mob_type` (`ty` resolved at `state.rs:1141`) |
| `state.rs:1189` (`World::point_damage`) | `elites.dmg_mult` | a mob's outgoing point damage `(((raw*k)*d)*exec)*(role.dmg_mult*zone*reach)`; feeds `faction_spec().hit` (`state.rs:1157`), `signature_damage` (`state.rs:1207`) and `paced_faction_spec` | yes: `m.mob_type` (`state.rs:1176`) |
| `systems/sentenced.rs:543` (`npc_pulse`) | `elites.dmg_mult` | the Sentenced Mark pulse of an NPC owner: `((MARK_BASE_PCT*(k*d))*role)*DUEL_PACE` | yes: `mob.mob_type` (`sentenced.rs:538`) |

Not readers of a multiplier, but carriers of the table (verified: each passes `&EliteTuning` to `Mob::born_with`, which
calls `spawn_hp` with the mob's type, `mob_runtime.rs:353` and `:371`):
- `systems/mobs.rs:213` (camp spawn, `EliteTuning::of(k.config.elites)`), then `targets::configure` (Warden x
  `WARDEN_HP_MULTIPLIER` on top, `quests/board/attempts/targets.rs:244-249`).
- `systems/mobs.rs:475` (the training-world mob restore check: `expected = Mob::born_with(..)`, compares
  `c.vitals != expected.core.vitals`).
- `systems/mobs.rs:555-556` (practice birth, `EliteTuning::of_world(w)`).
- `systems/mob_checkpoint.rs:363-371` (the arena mob restore check: `born_with` then `targets::configure`, compares
  `p.vitals.max_hp != expected.core.vitals.max_hp`).
- `mob_runtime.rs:309-310` (`Mob::born`: legacy table, never-elite births and tests).

So once `spawn_hp` reads `hp_mult_of(ty)`, spawn and both restore checks agree by construction: neither `mobs.rs` nor
`mob_checkpoint.rs` needs a change unless the implementer changes how the table is passed. The brief's outline listed
them as readers to change; the code says they are carriers only.

Direct constant reads: `ELITE_HP_MULT` / `ELITE_DMG_MULT` / `REWORK_ELITE_HP_MULT` / `REWORK_ELITE_DMG_MULT` are read
only in `mob_runtime.rs:450-451, 455-456` (the two statics). `ELITE_K` likewise (`k` is not per-kind in this packet).
`REWORK_ELITE_SHARE_STACKS` and `REWORK_ELITE_FIRST_PICK_MS` are other mechanics.

Outside rh-sim (NOT in this packet): `rh-policy/src/bots/planning.rs:182-186` (`mob_faction`) and
`temporal_percept/strategy/hunt_adapter.rs:294-301` (`mob_max_hp`) read `EliteTuning::of(rule).hp_mult / .dmg_mult`
directly; their tests `bots/planning/elite_tests.rs:15-23` and `hunt_adapter/elite_tests.rs:10-11` too. The global
fields therefore stay `pub` and keep their meaning (the global multiplier); P-BAL-ELITE-BOTS-1 moves the bots to the
accessors.

### Fact 2: the ten elite kinds (`mob_catalog/mod.rs:8-39`, rows `:255-465`, `elite: true`)

| Zone | Elite kinds (catalog position = wire code) |
|---|---|
| Fire | Wyrmling (4), AshDrake (5) |
| Sentenced | Executioner (10), Gaoler (11) |
| Plague | RatOgre (16), PoxBehemoth (17) |
| Frost | GlacialWarden (22), AvalancheTroll (23) |
| Holy | BloodSaint (28), Archon (29) |

Quest targets (`quests/board/attempts/targets.rs`): The Warden is a Gaoler (`:227`, the Warden camp's elite spot 0,
`:46`) and gets `max_hp *= WARDEN_HP_MULTIPLIER` after `born_with` (`:244-249`); A Bone to Pick forces the camp's elite
to RatOgre (`:226`); Elite Slayer targets the camp's own elite, any kind (`:185-186`). Order stays: Rework elite numbers
(now per kind) first, quest multiplier after. Nothing under `quests/board/**` changes.

### Fact 3: existing tests that pin elite numbers

| Test | Asserts | With an empty table | When a row lands later |
|---|---|---|---|
| `rh-sim/tests/elite_tune.rs` `the_rework_elite_table_is_hp_1_5_dmg_1_0_and_k_stays` | global Rework fields 0.65 / 1.5 / 1.0 | unchanged | unchanged (globals stay) |
| `elite_tune.rs` `the_legacy_elite_table_is_unchanged` | legacy fields = constants | unchanged | unchanged |
| `elite_tune.rs` `the_rework_elite_numbers_are_registry_rows_that_the_table_reads` | `REWORK_ELITES` body names the registry constants | passes (the body gains `per_kind: &[]`) | passes |
| `elite_tune.rs` `a_rework_elite_spawns_with_legacy_hp_times_1_5_over_2_and_normals_do_not_move` | every seed1 elite's max hp = `spawn_hp(of(rule),..)`, and Rework/Legacy ratio = 0.75 | unchanged | **needs a per-kind expectation** (ratio becomes `0.75 * row.hp`) |
| `elite_tune.rs` `a_rework_elite_hits_for_legacy_damage_times_1_0_over_1_5` | first elite hit ratio 2/3 per zone | unchanged | **needs a per-kind expectation** |
| `elite_tune.rs` checkpoint tests (two) | stored rule `"rework"`, restore, hash equal, max hp = `spawn_hp(of(Rework))` | unchanged | pass if `spawn_hp` applies the row |
| `rh-sim/tests/augment_rules.rs:333` `the_elite_table_is_the_legacy_constants_under_legacy_and_the_owners_numbers_under_rework` | global fields of both tables | unchanged | unchanged |
| `systems/mobs.rs` `elite_rule_tests::a_mob_runtime_elite_rule_the_config_does_not_store_is_refused` | `check_elite_rule` refusal | unchanged | unchanged |
| `rh-sim/tests/content.rs` `GOLDEN_CONTENT_HASH`, `every_const_in_the_source_tree_is_registered_or_explained` (NON_GAMEPLAY rows `LEGACY_ELITES`, `REWORK_ELITES`, `:83-84`) | the hash; every named const/static explained | unchanged (no new named static outside the test hook, see fact 4) | the number packet regolds and adds a `TABLES_FOLDED_BY_HASH` row if it names a table |
| `tests/hash_coverage.rs`, `tests/no_alloc.rs`, `rh-headless/tests/rework_at_25ms.rs` | state hash leaves, zero allocation per step, Rework smoke | unchanged | unchanged |
| rh-policy `planning/elite_tests.rs`, `hunt_adapter/elite_tests.rs` | bot estimates from the global fields | unchanged | P-BAL-ELITE-BOTS-1 |

No existing expectation changes in this packet.

### Fact 4: running a world under a doctored elite table

The card precedent (`cards/tuning_tests.rs:1-5`, "readers through a doctored table") puts a doctored `Tuning` on
`sim.k.cards`, because every card reader has the statics. The elite table cannot go that way: it is a `&'static` chosen
by `EliteRule` (`EliteTuning::of`), and three of the four readers have no statics (`of_world(w)` reads
`MobRuntime::elites`, an `EliteRule`). The cheapest hook: a `#[cfg(test)]` thread-local stand-in for the Rework table
that `EliteTuning::of(Rework)` returns while it is set, `mob_runtime::with_rework_table(&'static EliteTuning, f)`. It
reaches every reader (spawn, both restore checks, `faction_spec`, `point_damage`, `npc_pulse`, `stored_rule`) because
they all resolve the table through `of`. It is compiled out of the library outside `cfg(test)` (so `no_alloc` and the
release build are untouched), and thread-local, so parallel test threads do not see each other's table. Its static
(`TEST_REWORK_ELITES`) is a named `static` in `src`, which `tests/content.rs`
`every_const_in_the_source_tree_is_registered_or_explained` requires to be explained: one NON_GAMEPLAY row, committed
on its own as `oracle-amend:` (an added exemption, no changed expectation; precedent: `cards/native/hooks.rs` `TRACE`,
`tests/content.rs:75`). The in-crate oracle therefore lives in `src/mob_runtime/elite_kind_tests.rs`.

### Fact 5: the elite bench

`crates/rh-headless/src/elite_bench.rs` reads no elite number: it lists elite kinds (`mob_catalog::elites_of`, `:802`,
`:919`) and labels the rule (`elites: "rework"`, `:1162`); hp and damage come from the sim. A per-kind row reaches the
bench with no change there.

### State hash, `stored_rule`, `check_elite_rule`

`MobRuntime::elites` (`mob_runtime.rs:520`) stores an `EliteRule`, set to `EliteTuning::stored_rule(config.elites)`
(`systems/mobs.rs:75`, `:167`) and checked by `check_elite_rule` (`systems/mobs.rs:643-658`, refusal test `:671`);
`hash.rs:45` documents it. The state hash folds only the rule, never the numbers. A per-kind table does not change that:
the table is content (a static, like the global pair), not state. `stored_rule` compares the whole `EliteTuning` with
`LEGACY_ELITES` (derived `PartialEq`, which must include `per_kind`): with empty tables Rework already differs from
legacy (hp 1.5, dmg 1.0) and is stored as `"rework"`, exactly as today; a later non-empty Rework table differs too.
The `Legacy` table's `per_kind` stays empty (legacy has no per-kind number), so `Legacy` keeps writing nothing.
`CHECKPOINT_FORMAT` does not move.

### Content hash: rule for the number packet (designed here, not built)

Today the elite numbers reach `content_hash` only as registry rows (`constants.rs:600-602`, name and value bits); the
two statics are NON_GAMEPLAY (`tests/content.rs:83-84`, "registered constants by name"). With an empty table nothing new
is folded and `GOLDEN_CONTENT_HASH` must not move (`scripts/regolden-content.sh --check` clean).

The number packet that fills the Rework table follows this rule:
1. Each per-kind value is a registered constant, appended to `content!` in `constants.rs`:
   `REWORK_ELITE_<KIND>_HP_MULT` / `REWORK_ELITE_<KIND>_DMG_MULT` (for example `REWORK_ELITE_AVALANCHE_TROLL_HP_MULT`),
   and the row reads it by name (`EliteKindRow { ty: MobTypeId::AvalancheTroll, hp: crate::constants::REWORK_ELITE_..., .. }`),
   inline in `REWORK_ELITES` (no new named static) or in one named table listed in `TABLES_FOLDED_BY_HASH`.
2. The pairing (which type reads which value, and the row order) is folded by an elite content visitor, beside the
   mob-catalog fold (`content/fold.rs` `fold_mob_catalog`): for each rule whose `per_kind` is non-empty, the tag
   `"elite-kinds/1"`, the rule name, the row count, then per row `ty.as_str()`, `hp.to_bits()`, `dmg.to_bits()`. With
   every table empty it writes nothing, so this packet's hash is unchanged and the number packet's regold is the only
   move. Registry rows alone would not catch two kinds swapping values.
3. `scripts/regolden-content.sh`, never a hand edit; client `BakedContent.cs` and server ship together.

This packet does not add the visitor (nothing to fold); if the implementer adds it anyway, it must write nothing for an
empty table, and `content.rs` / `content/fold.rs` are shared files (merge one at a time).

## 4. New paths

- `crates/rh-sim/src/mob_runtime/elite_kind_tests.rs` (oracle, in-crate)
- `crates/rh-sim/tests/elite_seam.rs` (oracle, source scan)

## 5. Exclusive write scope

- `crates/rh-sim/src/mob_runtime.rs` (the table, accessors, `spawn_hp`; keep the test hook and the `mod` line)
- `crates/rh-sim/src/state.rs` (`faction_spec`, `point_damage`)
- `crates/rh-sim/src/systems/sentenced.rs` (`npc_pulse`)
- `crates/rh-sim/src/systems/mobs.rs`, `crates/rh-sim/src/systems/mob_checkpoint.rs` (only if the way the table is passed
  changes; fact 1 says no change is needed)
- `crates/rh-sim/src/content.rs`, `crates/rh-sim/src/content/fold.rs` (shared files; only an elite visitor that writes
  nothing for an empty table, and only if the implementer builds it now)

Not in scope: anything under `quests/board/**` (P-BAL-Q25-SEAM-1 is moving it), anything in `rh-policy`, `constants.rs`
(no new number), `tests/content.rs` (the one exemption row is an oracle-amend of this packet).

## 6. Contracts and references

- C-NUM: f64 order. With no row, the accessor returns the global field itself; a reader substitutes `x.hp_mult_of(ty)`
  for `x.hp_mult` in place, so every expression is bit-identical. With a row the value is `global * row` (one multiply,
  global first).
- C-DET / ADR 0007: the per-kind lookup is a linear scan of a `&'static` slice (no hash collection, no allocation).
- `docs/agent/AUGMENT_REWORK.md` "The elite table" (update its reader list in the hand-off as a proposed doc edit).
- OWNER_DECISIONS 2026-11-22 "Yes, per-elite numbers"; 2026-11-19 "dmg 1.0, hp 1.5" (the globals stay).

## 7. Behaviour to preserve

- `Legacy` worlds byte-identical (state hash, checkpoint, events); `Rework` worlds' state hash, checkpoint format,
  events and every existing test (`tests/elite_tune.rs`, `tests/augment_rules.rs`, `tests/hash_coverage.rs`,
  `tests/no_alloc.rs` and the other `*_no_alloc.rs`, `rh-headless` `rework_at_25ms`).
- `GOLDEN_CONTENT_HASH` unchanged; `scripts/regolden-content.sh --check` clean.
- No allocation per step. Quest target order: per-kind elite numbers, then the quest multiplier.
- The global fields `k`, `hp_mult`, `dmg_mult` stay `pub` with today's meaning (rh-policy reads them).

## 8. Acceptance checks

- `scripts/gate.sh --quick --oracle packet/P-BAL-ELITE-SEAM-1` passes; the coordinator's full gate on the merge.
- `scripts/regolden-content.sh --check` prints no `STALE`.
- `cargo test -p rh-sim --test elite_seam --test elite_tune --test augment_rules --test content` green;
  `cargo test -p rh-sim --lib mob_runtime::elite_kind_tests` green.

## Oracle files (read-only for implementers)

- `crates/rh-sim/tests/elite_seam.rs`: `no_file_outside_mob_runtime_reads_an_elite_multiplier_field` (red on the stub
  at `state.rs:1155`, `state.rs:1189`, `systems/sentenced.rs:543`).
- `crates/rh-sim/src/mob_runtime/elite_kind_tests.rs`: behaviour through a doctored one-row table (red on the stub,
  assertion failures), and the empty-table guard (green on the stub, must stay green).
- `crates/rh-sim/tests/content.rs`: the one NON_GAMEPLAY row for `TEST_REWORK_ELITES` (oracle-amend, see below).
- `docs/backlog/packets/P-BAL-ELITE-SEAM-1.md` (this brief).

### Oracle-amend for the owner

- `crates/rh-sim/tests/content.rs`: one added NON_GAMEPLAY row `("mob_runtime.rs", "TEST_REWORK_ELITES", ..)` for the
  cfg(test)-only thread-local stand-in table. No expectation changes. Needed because the content scan explains every
  named static in `src`; precedent `cards/native/hooks.rs` `TRACE` (`tests/content.rs:75`).

## 9. Validation commands

- `cargo test -p rh-sim --test elite_seam`
- `cargo test -p rh-sim --lib mob_runtime::elite_kind_tests`
- `scripts/regolden-content.sh --check`
- `scripts/gate.sh --quick --oracle packet/P-BAL-ELITE-SEAM-1`

## 10. Risk and suggested model tier

implementer-opus, reviewer-opus. The change is small (four reader lines and two accessors) but sits on the restore
checks and the content hash: a reader left on the global field desyncs a later number packet silently (the scan
catches field reads outside `mob_runtime.rs`; the behaviour test catches `spawn_hp` inside it), and a visitor that
writes words for an empty table moves the golden hash.

## 11. Known traps

- `role_def().hp_mult` / `.dmg_mult` (the mob role) and `content/fold.rs:17-18` (the role row) share the field names;
  they are not elite numbers. The scan allows exactly those receivers.
- `max_hp_mult` (`modifiers/mod.rs`) is an unrelated field.
- Do not destructure or build `EliteTuning { .. }` outside `mob_runtime.rs` (the scan refuses it).
- The Warden: `born_with` then `WARDEN_HP_MULTIPLIER`; the restore check repeats the same order
  (`mob_checkpoint.rs:363-377`). Keep both paths on one `spawn_hp`.
- `stored_rule` uses `PartialEq` on the whole table: keep `per_kind` inside the derived comparison.
- `EliteKindRow` lookups: at most one row per type (the guard asserts it), and only elite kinds.
- `k` (`ELITE_K`) is not per-kind in this packet.
- A per-weapon factor (bow and wand die more) is NOT in this packet: `point_damage` has no victim, so it would be a
  mechanic change (`BALANCE_LAB_PATCH.md` section 4).

## Notes from previous attempts
<!-- Coordinator appends on escalation: tier, branch, the gate's failure block. Nothing else. -->
