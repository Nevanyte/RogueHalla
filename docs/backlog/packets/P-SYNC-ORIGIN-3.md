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
