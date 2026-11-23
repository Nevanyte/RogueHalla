# P-BAL-ELITE-BOTS-1: the bots' estimates of an elite read the per-kind numbers, empty table, no decision moves

- **Serves:** not in the plan: owner approval 2026-11-22 (balance patch, "Yes, per-elite numbers"; `docs/OWNER_DECISIONS.md`
  top row; plan of record `docs/backlog/BALANCE_LAB_PATCH.md` section 3 "Per-elite numbers" and the wave row for
  P-BAL-ELITE-BOTS-1)
- **Implementer tier:** implementer-sonnet (write scope is `rh-policy` alone; `EliteKindRow`, `EliteTuning.per_kind`,
  `hp_mult_of` and `dmg_mult_of` are already `pub`, `MobTypeId` and `mob_catalog` are `pub`)
- **Reviewer:** reviewer
- **Integration base:** `feature/unity-rework` @ c0ef232a1
- **D-rules touched:** D1 loose proof (owner direction 2026-11-10); no legacy counterpart (Rework-only seam) · D2 n/a ·
  D5/D6/D9 none

## 1. ID and outcome

P-BAL-ELITE-SEAM-1 (merged, integration 83) gave `EliteTuning` an empty `per_kind` table with `hp_mult_of(ty)` /
`dmg_mult_of(ty)`; the sim reads them. The bots' own estimates of an elite still read the global `hp_mult` / `dmg_mult`
fields, so once a number packet fills a row a bot would misjudge that elite. This packet moves every bot estimate that
knows the elite's kind onto the accessors. With the table empty the accessors return the global field itself
(bit-identical, `crates/rh-sim/src/mob_runtime.rs:521-533`), so no bot decision, pinned witness, fixture or content hash
moves.

## 2. Dependencies

P-BAL-ELITE-SEAM-1 (in the base). Disjoint from every `rh-sim` packet (nothing under `crates/rh-sim` changes).

## 3. Relevant legacy paths

None: legacy has one global elite pair (`packages/shared/src/mobs.ts:37-41`), and the bots' estimate of it is already
ported (`planning.ts` `mobFaction`, `huntAdapter.ts` `mobMaxHp`). Per-kind rows are a Rework-only difference (the number
packets propose the DEVIATIONS rows).

## Established facts (verified on c0ef232a1)

### Fact 1: every reader of an elite multiplier outside `mob_runtime.rs`

`grep -rnE 'hp_mult|dmg_mult|ELITE_(HP|DMG)_MULT|EliteTuning' crates tools --include='*.rs'`, outside `crates/rh-sim`:

| Site | Reads | Kind in hand? |
|---|---|---|
| `rh-policy/src/bots/planning.rs:182-186` `mob_faction(region, elite: bool, rule)` | `elites.hp_mult`, `elites.dmg_mult` (and `elites.k`, not per-kind) | NO: the signature has a zone and an `elite` flag only |
| `rh-policy/src/bots/temporal_percept/strategy/hunt_adapter.rs:294-301` `mob_max_hp(zone, elite, name, rule)` | `elites.hp_mult` (no damage) | YES when `name` resolves: it already looks up `typed` (a `MobTypeDef`, whose `.id` is a `MobTypeId`) from `name` at `:286-292`; `typed` is `None` when `name` is `None` |
| tests `planning/elite_tests.rs:15-23`, `hunt_adapter/elite_tests.rs:10-11` | the global fields, for the real tables | n/a (fact 3) |

Nothing else. `rh-headless` (`elite_bench.rs` lists kinds and labels the rule; hp and damage come from the sim),
`rh-parity`, `rh-load`, `rh-server`, `bots/raid/` and `tools/` read no elite multiplier (the grep is empty for them).
The `k` factor (`ELITE_K`) is not per-kind and stays on the field.

### Fact 2: who calls the two readers, and what they know

- `mob_max_hp` is called once, `hunt_adapter.rs:316` (`mob_like_of`, a remembered mob; `m.name` is the percept name).
  Kind known when the name resolves.
- `mob_faction` is called once, `planning.rs:441` (`matchup_share_under(me, region, elite, rule)`), whose callers are:
  - `strategy/mob_hunt.rs:515` (a visible mob `m`: has `m.type_: Option<String>` and `m.faction`, no `MobTypeId` yet; the
    implementer resolves it with `mob_catalog::mob_type_by_id` and checks `elite` and the zone; verify in
    `hunt_adapter.rs` `mob_like_of` what `type_` carries before relying on it);
  - `knowledge.rs:164` `Knowledge::mob_share(me, region, elite)` (camp level: `engine.rs:1109`, `:1225`,
    `brain/adapters.rs:1709`, `types.rs:471`, `source_trace.rs:223`): a camp is `(zone, elite)` only; the elite's kind is
    rolled at spawn (`rh-sim/src/systems/mobs.rs:144`) and is not in the bot's camp view. The kind is unknown here; the
    share-error table is keyed by `(me, region, elite)` (`knowledge.rs:71`, `:106`) and must not change;
  - `planning.rs:430-431` `matchup_share` (Legacy rule) and `:450-451` `camp_matchup`: the pinned planning witness.
- Rule for an unknown kind: an elite with no kind uses the global pair, `hp_mult` / `dmg_mult` themselves (what
  `Option<MobTypeId>::None` means). The camp-level share therefore stays on the global pair after this packet. That is a
  known limit; see "Question for the coordinator".

### Fact 3: existing tests that pin what this packet changes

| Test | Asserts | Under the empty table |
|---|---|---|
| `planning/elite_tests.rs` `an_elite_is_scaled_by_the_rules_hp_and_damage` | `mob_faction(f, true, rule)` hp and hit ratios Rework/Legacy equal `r.hp_mult/l.hp_mult`, `r.dmg_mult/l.dmg_mult`; absolute hp = faction hp x k x MOB_HP_MULT x `r.hp_mult`; Fire, Frost, Plague | unchanged: `mob_faction(region, elite, rule)` keeps its signature and means "kind unknown" |
| `planning/elite_tests.rs` `a_regional_mob_ignores_the_rule` | normal mob hp/hit equal under both rules | unchanged |
| `hunt_adapter/elite_tests.rs` `a_remembered_elite_has_the_rules_hp` | `mob_max_hp(zone, true, None, rule)` = faction hp x k x MOB_HP_MULT x `t.hp_mult` (name `None`: kind unknown); regional equals faction hp x MOB_K x MOB_HP_MULT | unchanged: keeps its 4-argument signature |
| `rh-policy/tests/elite_rule_start.rs` | `MatchStart::elites` carries the room's rule | unchanged |
| `rh-policy/tests/first_pick_bots.rs`, `modules/choice/native_tests.rs` | mention `Rework` for the augment pick, not the elite estimate | unchanged |

Both existing elite test files stay as they are and keep compiling: the packet adds sibling functions and does not
change the two reader signatures. No existing expectation changes, so no `oracle-amend:` commit.

### Fact 4: which tests prove "nothing moves"

Every pinned bot witness runs the Legacy elite rule, so they read the global pair and the (empty) table does not matter
to them; a green run proves the refactor is behaviour-neutral:
- `MatchStart` literals in the policy tests all carry `elites: rh_sim::EliteRule::Legacy` (`bots/bot.rs:947`,
  `planning_parity_tests.rs:448`, `frost_wall_bots_tests.rs:585,825,889`, `wish_module_tests.rs:41`,
  `bots/bot/reset_stream_tests.rs:25`); `Knowledge::new` starts Legacy (`knowledge.rs:114`), `MobHunter` starts Legacy
  (`mob_hunt.rs:230`), the `KnowledgeApi` default is Legacy (`types.rs:455-457`); a live `Bot` applies the room's rule
  at `bot.rs:386` (`.with_elite_rule(m.elites)`).
- Planning: `bots/planning_parity_tests.rs:136` (`matchup_share`) and `:152` (`camp_matchup`) against
  `parity/policy/bot-planning.json`; knowledge: `knowledge_parity_tests.rs:90`, `knowledge_repair_tests.rs:127`
  against `parity/policy/bot-knowledge*.json`; hunt: `mob_hunt_parity_tests.rs` against `parity/policy/bot-mob-hunt.json`;
  Brain prefix, fight and tactics witnesses (`parity/policy/bot-brain-prefix`, `bot-fight`, `bot-tactics.json`,
  `bot-private-*`). All run in `cargo test -p rh-policy`. The captures are legacy-source: under Legacy the table is
  empty by definition.
- A Rework elite with the empty table: `planning/elite_tests.rs` and `hunt_adapter/elite_tests.rs` (fact 3).
- Not covered by a pinned witness: a Rework room end to end with bots (no policy test runs one; `rh-headless`
  `rework_at_25ms` exercises the sim, not these estimates). "Nothing moves under Rework with the empty table" rests on
  `hp_mult_of` / `dmg_mult_of` returning the field itself with no row, guarded in rh-sim by
  `elite_kind_tests::with_the_real_tables_every_elite_kind_plays_todays_numbers_bit_for_bit`.

## 4. New paths

- `crates/rh-policy/src/bots/planning/elite_kind_tests.rs` (oracle)
- `crates/rh-policy/src/bots/temporal_percept/strategy/hunt_adapter/elite_kind_tests.rs` (oracle)

## 5. Exclusive write scope

- `crates/rh-policy/src/bots/planning.rs` (`mob_faction_under`; a kind-taking sibling of `matchup_share_under`; keep the
  `#[cfg(test)] mod elite_kind_tests;` line)
- `crates/rh-policy/src/bots/temporal_percept/strategy/hunt_adapter.rs` (`mob_max_hp_under`; keep the `mod` line)
- `crates/rh-policy/src/bots/temporal_percept/strategy/mob_hunt.rs` (`:515` and `matchup_share` at `:628`: pass the mob's
  kind when it resolves)

Not in scope: anything in `rh-sim`, `Knowledge::mob_share` and its callers (camp level, kind unknown), `share_key`, the
bot `Command` enum, the existing `elite_tests.rs` files, `parity/**`.

## 6. Contracts and references

- C-NUM: with no row the accessor returns the global field; an estimate substitutes `elites.hp_mult_of(ty)` for
  `elites.hp_mult` in place, so each expression is bit-identical under the empty table. With a row it is `global * row`.
- C-DET: no new draw, no new collection; `Knowledge::share_error` draws untouched.
- C-POLICY: `POLICY_CONTRACT_VERSION` does not move (no `MatchStart` or `Command` change).
- `docs/backlog/BALANCE_LAB_PATCH.md` section 3; `docs/agent/AUGMENT_REWORK.md` (reader list: propose the doc edit in the
  hand-off, do not edit it).

## 7. Behaviour to preserve

- `BrainBot` / `HumanBot` decisions, the `Command` enum and every capture, with the empty table, under both rules:
  `cargo test -p rh-policy` (all of fact 4) green with no oracle file touched.
- `mob_faction`, `mob_max_hp`, `matchup_share`, `matchup_share_under`, `Knowledge::mob_share`, `camp_matchup` keep their
  signatures and results (the new functions are siblings; the old ones delegate with "kind unknown").
- Unknown kind (name `None`, unresolved `type_`, camp level) reads the global fields exactly as today.

## 8. Acceptance checks

- `scripts/gate.sh --quick --oracle packet/P-BAL-ELITE-BOTS-1` passes.
- `cargo test -p rh-policy` green (fact 4), including the two new tests.
- `git diff --stat` shows no file outside the write scope: no `parity/`, `protocol/`, `Cargo.lock` or `rh-sim` line.

## Oracle files (read-only for implementers)

- `crates/rh-policy/src/bots/temporal_percept/strategy/hunt_adapter/elite_kind_tests.rs`:
  `a_row_moves_the_hp_estimate_of_its_kind_only`: with the real Rework table plus one doctored row (Wyrmling, hp x1.25,
  dmg x0.8), `mob_max_hp_under` for the named Wyrmling is the real estimate x1.25; AshDrake (the other Fire elite), an
  elite with no name, and a regional mob equal the real estimate bit for bit.
- `crates/rh-policy/src/bots/planning/elite_kind_tests.rs`: `a_row_moves_the_planning_sheet_of_its_kind_only`:
  `mob_faction_under(Fire, true, Some(Wyrmling), doctored)` has hp x1.25 and hit x0.8 of the real sheet; AshDrake, `None`
  and a regional mob bit for bit.
- This brief.

Both fail on the stub with assertion messages (the stubs take the table and the kind and read the global fields).

The seam for a test is a function taking `&EliteTuning`: the table is a `&'static` chosen by rule inside `rh-sim`, and its
test hook `with_rework_table` is `cfg(test)` there, invisible to `rh-policy`. The stub commit adds `mob_max_hp_under` and
`mob_faction_under`, and the old functions delegate to them (behaviour unchanged). Two tests rather than one: the two
readers sit in different files, and one test would let a half-done implementer pass.

Satisfiable by: in `mob_max_hp_under`, `if elite { typed.map_or(elites.hp_mult, |t| elites.hp_mult_of(t.id)) } else { 1. }`;
in `mob_faction_under`, `elites.hp_mult_of(ty)` / `elites.dmg_mult_of(ty)` when `ty` is `Some`, else the fields.

## 9. Validation commands

- `export PATH="$HOME/.cargo/bin:$PATH"; cargo test -p rh-policy`
- `scripts/gate.sh --quick --oracle packet/P-BAL-ELITE-BOTS-1`

## 10. Risk and suggested model tier

implementer-sonnet, reviewer: one crate, three files, no draw, state or wire. Risk: a reader switched to
`hp_mult_of(ty)` with `ty` fixed to a placeholder kind instead of the real one would pass the empty table and fail
silently later; the oracle names the kind in both tests.

## 11. Known traps

- `mob_faction` has no kind today and `Knowledge::mob_share` is `(zone, elite)` keyed through a trait (`KnowledgeApi`)
  with witnessed callers: do not widen that trait in this packet.
- `typed_mult` in `mob_max_hp` is the mob ROLE hp multiplier (`role_def().hp_mult`), not an elite number.
- Keep float order: `faction.hp * k * MOB_HP_MULT * hp_mult` in that order (the existing tests compare to 1e-9, the pinned
  witnesses are bit-compared).
- The two `mod elite_kind_tests;` lines are in the stub commit; keep them.

## Question for the coordinator (before an implementer starts)

The camp-level share (`Knowledge::mob_share`, which decides which camps the Brain and `HumanBot` approach) cannot read a
per-kind row because the camp view has no elite kind. After a number packet, a Troll camp is estimated with the global
pair. Options: accept (this packet: every estimate that knows the kind), or a follow-up that estimates a camp as the mean
of its zone's two elites' rows (a `KnowledgeApi` change on witnessed callers, larger). Recommendation: accept; the lab's
per-kind numbers are about fights, and live bots are Rework-only behind a flag.

## Notes from previous attempts
<!-- Coordinator appends on escalation: tier, branch, the gate's failure block. Nothing else. -->
