# P-SYNC-ORIGIN-3: merge origin's Top 25 quests (PR 81) into the rework line, as protocol 24

- **Serves:** PR 80, the augment and elite rework (owner, 2026-11-20: "Please push and merge the new augments"). While
  PR 80's CI ran, origin merged PR 81 (the Top 25 quests) into `feature/unity-rework`. PR 80 cannot merge until the two
  lines are one again.
- **Tier:** `implementer-opus` (a cross-crate merge, a wire renumber, checkpoint and replay format moves). Reviewer:
  `reviewer-opus`.
- **Base:** `feature/unity-rework` at the commit that adds this brief. Merge `origin/feature/unity-rework` @ `ef8d5e9ce`
  (fetched 2026-11-21; merge base `5567458fb`, 29 commits, PR 81). Do not fetch a newer origin.
- **Precedent:** P-SYNC-ORIGIN-2 (`docs/backlog/packets/P-SYNC-ORIGIN-2.md`, integration 76 in `docs/MIGRATION.md`).
  Same rules unless this brief says otherwise. Origin's own account of PR 81: its `docs/MIGRATION.md` top section,
  `docs/backlog/packets/P-QUEST25-INTEGRATE.md` and `P-QUEST25-WIRE.md` on `ef8d5e9ce`.
- **Oracle (coordinator decision):** the union of both lines' existing suites. Every test either line has must pass on
  the merged tree; no test may be weakened or deleted. Version-stamp and key-number moves are the only edits allowed in
  an existing test, and each is listed in the hand-off.
- **Trial merge (coordinator):** 89 conflicted files: rh-sim 21, tools 14, rh-protocol 12, rh-policy 12, parity 6,
  Unity project 6, C# library 5 and its tests 5, rh-server 5, docs 2, protocol fixtures 1.

## What origin added since `5567458fb`

- 25 ranked quests in normal raids, behind `SimConfig::top25_quests` (on in `live_content()`), with quest spells,
  rewards (catalog stat cards, Bargain exchanges, Rebirth Eggs), objectives and destroyed terrain.
- Protocol **23** (origin skipped 22 because this line holds it): Rebirth Eggs as prop kind 9, opened base-map tiles as
  **State key 14**, the quest wire.
- Observation 36, policy contract 32, `rh-checkpoint/38`, `rh-replay/27`, content hash `7639672bbb0933c9`.
- Its own card provenance refresh, and `top25_quests` in `tools/determinism/compare.py`.

| | Local (`feature/unity-rework`) | Origin (`ef8d5e9ce`) | Merged |
|---|---|---|---|
| Protocol | 22 | 23 | **24** |
| Observation | 35 | 36 | **37** |
| Policy contract | 31 | 32 | **33** |
| Checkpoint | `rh-checkpoint/37` | `rh-checkpoint/38` | **`rh-checkpoint/39`** |
| Replay | `rh-replay/25` | `rh-replay/27` | **`rh-replay/28`** |
| Content hash | `d43155e2ed7d1c68` | `7639672bbb0933c9` | the regolden script's |

## Decisions

1. **Wire: protocol 24** carries origin's 23 and the local 22.
   - **Origin's numbers stand.** State key 14 is origin's (opened base-map tiles), and so is prop kind 9 (Rebirth Eggs).
     Each local State key, prop kind or other wire number that collides with origin's moves to the next free number:
     the local own-readout rows (local State key 14) move first, then the public Marked Man rows (local key 15) only if
     origin also uses 15. List every move.
   - The server refuses 23 and below.
   - `protocol/fixtures/v24.json` comes only from `cargo run -p rh-protocol --bin protocol-fixtures`. `v23.json` takes
     origin's file. The local `v22.json` stays as it is (never deployed); local tests that read it read v24.
   - The Rust, C# and JS decoders carry both lines' changes. The Unity copy of the net library moves only through
     `bash client-unity/sync-net.sh`.
2. **Observation 37 and policy contract 33.**
3. **Checkpoint 39 and replay 28.** Both lines added serialized state, so both formats move. Every serialized field of
   either line reaches save, restore and the state hash (`tests/hash_coverage.rs` holds both sides' rows); the refused
   format lists add 37 and 38, and replay 25 and 27.
4. **Content hash:** after the merge, `scripts/regolden-content.sh` only. On a conflict in any copy (`BakedContent.cs`,
   `tests/content.rs`'s golden line, the reload-proof `.mjs` files), take either side and rerun it. Merge
   `crates/rh-sim/tests/content.rs` tables by hand (both sides' rows). Hash tape: `observationVersion` only through its
   recorder (`cargo test -p rh-sim --lib record_ability_hash_tape -- --ignored`), `contentHash` and `checkpoint` only
   through the script; no case digest may move (if one does, stop and report). `protocol/fixtures/archetypes_v1.json`
   only through its documented command.
5. **Both lines live at once.** `SimConfig::live_content()` turns on the rework's `augments` and `elites` rules and
   origin's `top25_quests`. Keep every field of both.
   - `crates/rh-headless/tests/rework_at_25ms.rs` must pass on the merged tree with quests on (it builds from
     `live_content()`). If a quest reward, spell or Bargain touches cards, stat stacks or chests in a way the rework's
     rules change (for example a quest grant of a card the rework retunes, or a Bargain that removes a native card),
     do not choose a behaviour: stop and report it.
   - A behaviour conflict (both lines changing one rule differently) is not resolved here either: stop and report.
6. **Card provenance: take origin's side, then leave the chain to the coordinator.**
   - For `parity/card-catalog/source.json`, `parity/card-runtime{,-witness}/{source,INPUT_SHA256}.json`, the overlay
     lines of the two `capture.mts` files and `tools/provenance/check-card-chain.mjs`, take origin's file whole.
   - Merge every pinned file by content (both sides' lines): `tools/determinism/compare.py` and `test_compare.py` keep
     `top25_quests`, `augments` and `elites`; `tools/protocol-js/*.mjs` and the `tools/unity-browser-reload-proof/`
     files carry both wires.
   - After the merge, `node tools/provenance/check-card-chain.mjs` reports `catalog-protected-sha` failures for the
     pinned files whose merged content differs from origin's, and nothing else. List them; the coordinator runs one
     recapture (P-CI-GREEN-11) on your branch. Any other kind of failure: stop and report.
7. **`docs/MIGRATION.md`:** keep the local file. Put origin's current section "Top 25 quests on the 40 Hz integration
   branch (2026-11-21)" verbatim under the local heading `## From origin (merged by P-SYNC-ORIGIN-1 and
   P-SYNC-ORIGIN-2, 2026-11-20)`, before the text already there, and rename the heading to `## From origin (merged by
   P-SYNC-ORIGIN-1 to 3)`. `docs/quests/README.md` keeps both sides. Edit no other shared record.
8. **Every other conflict keeps both sides.**
   - Version-stamp tests take the new numbers.
   - A digest pin that hashes a whole observation moves only because of the version or a key move; prove it by forcing
     the old version, as P-WIRE19-A did.
9. **`Cargo.lock`:** only if the merge itself conflicts there; take the union and say so.

## Write scope

The merge commit and the commits after it on `agent/opus/P-SYNC-ORIGIN-3`:
- the conflicted files;
- the wire files of both lines (rh-protocol, the C# library and tests, the JS probe and checks, the rh-server version
  check, the rh-policy contract);
- the version-stamp tests;
- the fixtures, through their commands;
- the content-hash copies, through the script;
- the checkpoint and replay formats and their refused lists;
- `docs/MIGRATION.md` and `docs/quests/README.md`, as in decision 7.

Never: `packages/`, `hosting/`, `.github/`, `.claude/`, `scripts/gate.sh`, any `AGENTS.md`/`CLAUDE.md`, `parity/**`
outside decision 6 and the hash tape.

## Gate

`scripts/gate.sh --coordinator --keep-going --allow-artefacts --regen-fixtures --base feature/unity-rework`, with
Postgres. Start it with `RH_PGTEST_STATE=<a directory of your own> eval "$(tools/pgtest/start.sh)"` and stop it when
done. Run it in your worktree with no uncommitted changes (it counts working-tree changes). The only waived red is the
card provenance step of decision 6. Also run:
- `dotnet test client-unity/RoguehallaNet.Tests` (dotnet is at `~/.dotnet`)
- `node tools/protocol-js/check-vectors.mjs` and `node tools/protocol-js/check-map-anchors.mjs`
- `bash client-unity/sync-net.sh --check`
- `python3 -m unittest discover -s tools/determinism -p "test_*.py"`
- `cargo test --release -p rh-headless --test rework_at_25ms`

Prefix commands with `export PATH="$HOME/.cargo/bin:$HOME/.dotnet:$PATH"`.

## Done

A merge commit of `ef8d5e9ce` into the base, then the renumber and format moves, on `agent/opus/P-SYNC-ORIGIN-3`. The
gate above is green except the waived step. The hand-off (AGENTS.md report, in this file) lists:
- every conflict and how it was resolved;
- every wire number and version-stamp move;
- the new content hash;
- the pinned files the chain now reports (decision 6);
- anything stopped and reported.

## Hand-off (implementer-opus, 2026-11-21)

```
PACKET:          P-SYNC-ORIGIN-3 — serves PR 80 (the augment and elite rework), joining origin's PR 81 (Top 25 quests)
BRANCH:          agent/opus/P-SYNC-ORIGIN-3 @ 4fa0e59de (merge commit 5191d7cdc)
GATE:            PASS (full: --coordinator --keep-going --allow-artefacts --regen-fixtures --base feature/unity-rework, with
                 Postgres; EditMode 1602/1602) except the waived "card provenance chain" step.
                 Log .agent-runs/gate/20261121T170538-agent-opus-P-SYNC-ORIGIN-3-full.log.
                 Extra checks: dotnet 2328/2328; check-vectors, check-map-anchors and every tools/protocol-js check;
                 sync-net --check; determinism unittest 27; reload-proof node --test 51/51: all PASS.
                 rework_at_25ms (release): FAIL on this commit (seed 5 short, seed 7 refused); settled by
                 P-SYNC-ORIGIN-3-fix.
CHANGED:         the 89 conflicted files, plus the wire, version-stamp and fixture files listed below
D-RULES:         D1 loose proof: no new mechanic. Hash tape: all 63 case digests equal the local tape (the 15 that differ
                 from origin's were already the local values). No D5/D6/D9 change.
DEVIATIONS:      none
SHARED RECORDS:  docs/MIGRATION.md per decision 7 (origin's section, demoted to ###, under the renamed heading
                 "## From origin (merged by P-SYNC-ORIGIN-1 to 3)"). docs/quests/README.md keeps both sides; the status
                 line names both implemented items. Cargo.lock had no conflict and was not touched.
LEGACY DOUBTS:   packages/server/src/sim/cardHooks.ts:252: legacy splashOverkill skips quest givers, non-hostile and
                 evading targets; the Rust resolve_splashes mob loop skipped none of them (on both lines before the merge).
NOT ESTABLISHED: CI's Linux, capture, perfproof and browser steps.
RISKS:           the quest × rework interactions below are literal unions unless P-SYNC-ORIGIN-3-fix decided them.
```

**Wire number moves (protocol 24).**
- Origin's numbers stand: State key 14 (opened tiles), prop kind 9 (Rebirth Egg), command 19 (`QuestInteract`).
- The local own readouts move from State key 14 to **16**. Marked Man stays on 15 (origin does not use 15).
- `MAX_STATE_KEYS` is 16, `REQUIRED_STATE_KEYS` 13. The malformed vectors `state_unknown_key` and `state_too_many_keys`
  name 17; `state_readouts*` use key byte 0x10.
- Rust scene candidates admit 11, 13 or 14 items (keys 15 and 16 never part of a scene); the C# scene candidate list
  adds 24.
- Rust, C# (`StateKey`, `ReadoutsState.Key`, `MaxKeys`; copied by `sync-net.sh`) and the JS probe carry the same keys.
- The server refuses 23 and below. `v24.json` is generated, `v23.json` is origin's, `v22.json` is unchanged; local
  tests that read `v22.json` read `v24.json`.

**Version moves.** Protocol 24, observation 37, policy contract 33, `rh-checkpoint/39` (refused lists add 38 and 37),
`rh-replay/28` (refused list adds 26 and 27).

**Content hash.** `8a4bf0ce37b406f2`, written only by `scripts/regolden-content.sh`.

**Pinned files the card chain flags.** Exactly three `catalog-protected-sha` failures: `tools/determinism/compare.py`,
`tools/determinism/test_compare.py`, `tools/protocol-js/check-vectors.mjs`.

**Edits to existing tests beyond stamps and key numbers.**
- Constructors gain origin's new fields (`Award::skip_stat`, `Award::smite_bonus`, `PlagueCorpse::owner_hatched`).
- Digest re-pins, each proved: `native_cards.rs` world and statics, `hooks_tests.rs` world and `wish_engine_tests.rs`
  Legacy and Rework hash to the old pins with origin's empty `"destroyed_terrain":[]`, `"rebirth_eggs":[]` and
  `"top25_quests":false` removed (state hashes unchanged); `barriers_reach_tests.rs` takes origin's forced version 31,
  and the merged observation hashes exactly to origin's `0xe54a_e805_e3fe_02c8`; `map_dressing.rs` keeps origin's
  protocol-21 normalization with its protocol-21 digests.
- `hash_coverage.rs`: the statics.rs serde-skip count goes from 8 to 9 (one from each line).
- `frost_wall_content.rs`: the local file. Origin's rewritten `WALL_CONTENT_HASH` test is not carried: local
  P-CONTENT-REGOLDEN-1 retired that pin, and it cannot hold with the REWORK rows.

**Conflicts (89 files) and how each was resolved.**
- Taken from origin whole (decision 6): the card provenance files, both `capture.mts` overlay lines,
  `check-card-chain.mjs`.
- Taken from either side, then regenerated: `BakedContent.cs`, the hash tape, `archetypes_v1.json`.
- rh-sim, both sides kept: `credit.rs` (native credited-kill hook, then origin's bounty-kill hook); `modifiers/mod.rs`
  (fold rows 36 to 40, native rows before origin's quest row, on local's `fold_rows_with_definition`); `ledger.rs`
  (`outgoing` with `k.cards`); `reward_chests/observer.rs` (origin's `reach(&w)` plus local `first_pick`); `state.rs`
  (origin's `state_tests.rs` plus local `history_step_tests`, one test now runs twice); `mobs.rs` and
  `mob_checkpoint.rs` (`born_with` the elite rule, then origin's quest target `configure`); `mob_life.rs` (the rework
  share, then origin's `!a.elite && !a.skip_stat`); `tests/content.rs` (both tables); `checkpoint.rs` 39.
- rh-protocol: keys as above; samples move the unknown and too-many vectors to 17.
- rh-policy: collections and frame carry terrain, Marked and readouts in key order; `REQUIRED_STATE_KEYS` is the
  protocol's. Local's `collections/tests.rs` (it has `first_pick`) is kept; origin's `collections_tests.rs` is a one-line
  placeholder.
- C# library, its tests, the Unity copy (via `sync-net.sh`), `probe.html`, `check-tick-rate.mjs` and the reload-proof
  files: both wires, stamps 24. `protocol-18-stamp.test.mjs` keeps local's current-fixture checks and adds origin's v24
  pin and v21 history check.
- `compare.py` and `test_compare.py` keep `top25_quests`, `augments` and `elites`.
- Every other conflict: version stamps moved to the new numbers.

**Stopped and reported (settled by the coordinator in P-SYNC-ORIGIN-3-fix).**
1. `rework_at_25ms` seed 7, step 1200: a real checkpoint refused (`invalid neutral quest giver body/runtime`) because
   Overkill's splash picked a neutral quest giver.
2. `rework_at_25ms` seed 5: the match ends at step 1670, under the 2400-step floor (a quest trajectory).
3. Quest × rework unions: the rework elite share paid a turret-only killer; quest target elites take the Rework elite
   numbers, then their quest multiplier.
