# P-EDITMODE-STORMINK-1: Unity EditMode green again after the Storm Ink integration

## ID and outcome

Serves ORDER_OF_OPERATIONS phase 3 item 16a (the D9 play loop's client presentation), as a repair of P-STORM-BIOME-EDGES-1 and
P-STORM-PROP-ATLAS-LIFETIME-1, which arrived squashed in PR 83 (`3c47f0bff`, "Integrate the saved Storm Ink visual
overhaul", merged as `37a2200d4`). On origin's `feature/unity-rework` @ `64ca1230c` (PR 89),
`client-unity/UnityProject/Tools/editmode.sh` reports 1997/2000. CI does not run EditMode, so nothing caught it. Three tests
fail:

1. `DressedMapEditModeTests.ABlobWithPropsDrawsExactlyThosePropsAtTheirPositionsAndRollsNoScatter`: "RegionProps.Count is
   the number of props drawn", expected 5, was 22.
2. `DressedMapEditModeTests.ALegacyPropThatArrivesIsDrawnAsThePlaceholderAndDoesNotThrow`: expected 1, was 13.
3. `StormInkFireGeologyEditModeTests.EditorCleanupBetweenAtlasLookupAndSceneBuildPreservesRegisteredSprites`: expected the
   same `fire.scree` Sprite instance, got a different one with the same name.

When this lands the suite passes in full. Test 3 is a code defect and the code is fixed. Tests 1 and 2 test a premise that
the art change intentionally changed, so the tests are amended, keeping their guarantee that no client scatter is drawn
on a dressed map.

### Where each one turned red

The bisect over origin's first-parent merges from PR 82 (`38e18aa8e`) to PR 89 (`64ca1230c`) puts all three at PR 83
(`37a2200d4`). PR 84 to PR 89 touch no file under `Scripts/View/Regions/`, nor any of the art those tests read. PR 83
is a squash of `codex/storm-ink` from `10001a404` to `03220a892`, so the commits that caused each one come from that
branch's history:

- Tests 1 and 2: `ae92042b4` "Add cosmetic regional edge dressing". `RegionProps.Create` appends
  `RegionEdgeProps.Generate(...)`, small grass tufts and scree pebbles at region seams (`Placement.TransitionDetail`), to
  the placements it draws, on every map, dressed or not. The tests' `Open()` map is half Plague and half Frost, so
  Plague-side tufts appear along the seam: 17 with the five known props and 12 with the one legacy prop. The count differs
  because the seam roll is seeded from `MapBlob.LayerHash`, which covers the prop list. `RegionProps.Scatter` is
  unchanged, so `ScatterOfABlobWithPropsIsTheBlobsPropsAndNothingRolled` still passes.
- Test 3: `c1f7e63ab` "Replace the flat winter verge grass with wind-combed painted sedge". `RegionTransitionArt`
  (`Resources/StormInk/Surfaces/RegionTransitions.asset`) became an atlas source (its `WinterSedge` is baked as
  `edge.grass`), and it got an Editor `OnValidate` that calls `RegionPropArt.MarkAuthoredDefinitionsDirty()`. The
  keep-alive that P-STORM-PROP-ATLAS-LIFETIME-1 added (`254b20dae`, the commit that also added test 3) retains only the
  `StormInkPropArtDefinition`s under `StormInk/Props`. Editor cleanup therefore unloads the transitions asset. The next
  `RegionProps.Build` calls `RegionTransitionArt.Load()`, the reload runs `OnValidate`, and the following
  `RegionPropArt.TryGet` rebuilds the atlas with new Sprite instances. This is the failure P-STORM-PROP-ATLAS-LIFETIME-1
  fixed, back again through a source it did not retain.

### Code or premise

- **Test 3: the code is wrong.** The test pins exactly what P-STORM-PROP-ATLAS-LIFETIME-1 promised: an atlas is not
  invalidated by Editor cleanup when no source was edited. Fix: `RegionPropArt.Ensure` retains the `RegionTransitionArt`
  together with the prop definitions, and loads it before it clears the dirty flag, so validation on that load cannot
  invalidate the atlas it is about to bake. Built players are unchanged (Editor-only field, as before).
- **Tests 1 and 2: the premise changed on purpose.** Evidence that the art change meant seam details to draw on a dressed
  map:
  - Every served map is dressed (`crates/rh-server/src/runtime.rs` `arena()`: "every served map is dressed"; P-DRESS-BAKE-1),
    so a seam detail suppressed on dressed maps would never appear in a real match.
  - P-STORM-BIOME-EDGES-1 asks for the foreground seam dressing to be shown "in real gameplay", and its checkpoint
    records a joined Web pass on `cef989406`, which comes after `ae92042b4`. That was a live server, so a dressed map.
  - `RegionEdgeProps.Generate` takes the drawn placements as `existing` and keeps clear of them (`TooClose`). On a dressed
    map those are the server's props.

  The seam details are cosmetic ground-transition detail: deterministic from public map data (region grid, tiles,
  terrain, bushes, anchors, the map hashes), identical on every client, within two tiles of a region boundary only, at
  0.12 to 0.39 scale. The theme scatter the tests guard against (one roll per floor tile) stays off on a dressed map. The
  amended tests compute the seam details with the same public call `Create` makes, then assert that what is drawn is
  exactly the blob's props plus those seam details, each at its own position, and nothing else.

  **Owner flag (resolved 2026-11-22):** owner decision 5 (2026-11-10) and DEV-021 say decoration is baked server-side;
  the `docs/OWNER_DECISIONS.md` row glosses this as "no client-rolled decoration look on a dressed map". Storm Ink's seam
  tufts and pebbles are client-generated, deterministic, and drawn on dressed maps. Asked whether to keep them, the owner
  answered "Keep them" (2026-11-22, in the coordinator's chat). The coordinator recorded it as its own row in
  `docs/OWNER_DECISIONS.md`. If it is ever reversed: wrap the `RegionEdgeProps.Generate` call in `RegionProps.Create` in
  `if (!IsDressed(map))` and revert the two test amendments.

## Dependencies

- PR 83 (`37a2200d4`) merged into `feature/unity-rework`. Base: `origin/feature/unity-rework` @ `64ca1230c`.

## Relevant legacy paths (verified with `rg`)

None. Unity presentation only, with no legacy counterpart (DEV-021).

## New paths

- `docs/backlog/packets/P-EDITMODE-STORMINK-1.md` (this file).

## Exclusive write scope

- `client-unity/UnityProject/Assets/Scripts/View/Regions/RegionPropArt.cs` (the Editor keep-alive).
- `client-unity/UnityProject/Assets/Scripts/View/Regions/RegionProps.cs` (one comment at the seam pass, no behaviour).
- `client-unity/UnityProject/Assets/Tests/Editor/DressedMapEditModeTests.cs` (oracle amendment: tests 1 and 2 and the class
  summary).
- This packet file.

Nothing under `crates/`, `packages/`, `protocol/`, `parity/`, `hosting/`, `.github/` or `.claude/`.

## Contracts and references

- R-AUTH / ADR 0008: presentation only, with no game rule in Unity. Seam details stay walk-through, outside the content
  hash, and read only public map data (C-OBS unchanged).
- Owner decision 5 (2026-11-10), DEV-021, P-UNITY-DRESS-1, P-UNITY-DRESS-2: decoration is baked server-side; the client
  draws it as sent. See the owner flag above.
- P-STORM-BIOME-EDGES-1 (seam dressing), P-STORM-PROP-ATLAS-LIFETIME-1 (atlas lifetime in the Editor).

## Behaviour to preserve

- `RegionProps.Scatter` of a dressed blob stays the blob's props plus fixed furniture and real bushes
  (`ScatterOfABlobWithPropsIsTheBlobsPropsAndNothingRolled`, `ADressedMapKeepsRealBushCellsAndOnlyThose`).
- An undressed blob keeps its scatter (`ABlobWithoutPropsKeepsTodaysScatterUnchanged`).
- An edit to a prop definition or to `RegionTransitions.asset` in the Inspector still rebuilds the atlas (`OnValidate` is
  unchanged; `StormInkArtistSourceEditModeTests` and the definition-edit tests stay green).
- Built players: lazy loading as before; the retained field exists only under `UNITY_EDITOR`.

## Acceptance checks

- `bash client-unity/UnityProject/Tools/editmode.sh` passes every test (2000/2000 on this base).
- `scripts/gate.sh --quick --base origin/feature/unity-rework` prints GATE PASS.
- No file outside the write scope changes.

## Validation commands

```bash
bash client-unity/UnityProject/Tools/editmode.sh
```

```bash
scripts/gate.sh --quick --base origin/feature/unity-rework
```

## Risk and suggested model tier

Low: Unity presentation, one Editor-only field, two test amendments. `implementer-sonnet` tier, `reviewer` review.
Diagnosis and fix were done in the side session that wrote this brief (the fix is a few lines).

## Known traps

- Run Unity only in this worktree and never while the project is open in an Editor (`editmode.sh` exits 3 then).
- The seam count depends on `MapBlob.LayerHash`, which covers the prop list, so it differs between two blobs with the same
  grid. Compute the expected seam details from the blob under test; never pin a number.
- Loading a `ScriptableObject` that Editor cleanup unloaded runs its `OnValidate`. Any new atlas source whose
  `OnValidate` dirties the atlas must be retained in `RegionPropArt.Ensure` and loaded before the dirty flag is cleared.

## Evidence

`client-unity/UnityProject/Tools/editmode.sh` at each point (Unity 6000.0.85f2, this Mac, 2026-11-22):

| Commit | What | EditMode | The three tests |
|---|---|---|---|
| `38e18aa8e` | PR 82 merge, before Storm Ink | 1602/1602 | 1 and 2 pass; 3 does not exist yet |
| `37a2200d4` | PR 83 merge (Storm Ink squash `3c47f0bff`) | 1988/1991 | all three fail, same messages as the tip |
| `64ca1230c` | origin's tip, PR 89 | 1997/2000 | all three fail (the reported baseline) |
| `82bb9514c` | `packet/P-EDITMODE-STORMINK-1` (amended oracle, no code change) | 1999/2000 | 1 and 2 pass; only 3 fails |
| `0d3463bc4` | `agent/opus/P-EDITMODE-STORMINK-1` (the fix) | 2000/2000 | all pass |

The boundary was checked directly, not found by a `git bisect run` search: the good parent (PR 82) and the first bad merge
(PR 83) were both run. PR 84 to 88 were not run one by one; `git diff --name-only <merge>^1 <merge>` shows none of them
touches `Scripts/View/Regions/`, `Resources/StormInk/Props` or `Surfaces`, or the two test files. Inside the squash, the
causing commits come from reading `codex/storm-ink` (`git log -S`), not from Unity runs.

## Hand-off

```
PACKET:          P-EDITMODE-STORMINK-1 — serves ORDER_OF_OPERATIONS phase 3 item 16a (client presentation; repair of
                 P-STORM-BIOME-EDGES-1 and P-STORM-PROP-ATLAS-LIFETIME-1 as squashed into PR 83)
BRANCH:          agent/opus/P-EDITMODE-STORMINK-1 @ (this commit; fix 0d3463bc4), oracle packet/P-EDITMODE-STORMINK-1 @
                 82bb9514c, base origin/feature/unity-rework @ 64ca1230c. Local only, nothing pushed.
GATE:            PASS (quick) on 0d3463bc4 with --base origin/feature/unity-rework --oracle packet/P-EDITMODE-STORMINK-1
                 (path checks, rustfmt, card provenance, staging/NPC/determinism pins, readability, Unity EditMode
                 2000/2000); PASS (quick) on this commit without --oracle (this commit appends this report to the
                 brief, which is an oracle file). [skipped: the full gate, the coordinator's on the merged tree; Rust
                 suites, since no crate changed]
CHANGED:         client-unity/UnityProject/Assets/Scripts/View/Regions/RegionPropArt.cs (Editor keep-alive),
                 client-unity/UnityProject/Assets/Scripts/View/Regions/RegionProps.cs (one comment),
                 client-unity/UnityProject/Assets/Tests/Editor/DressedMapEditModeTests.cs (oracle amendment),
                 docs/backlog/packets/P-EDITMODE-STORMINK-1.md
D-RULES:         D1 n/a (presentation, no legacy counterpart, DEV-021); D2 n/a; D5, D6, D9 untouched (no sim, wire,
                 prediction or observation change)
DEVIATIONS:      none. Owner flag resolved (see "Code or premise"): Storm Ink's client-generated seam details draw on
                 dressed maps, against the OWNER_DECISIONS gloss of decision 5; the owner said "Keep them" (2026-11-22,
                 coordinator's chat). The one-line reversal stays named in the brief.
SHARED RECORDS:  proposed edits:
                 docs/backlog/README.md, after the P-STORM-PROP-ATLAS-LIFETIME-1 row: `| P-EDITMODE-STORMINK-1 | Unity
                 EditMode green again after the Storm Ink integration (atlas keeps the region transitions through Editor
                 cleanup; dressed-map drawing tests account for seam details) | P-STORM-BIOME-EDGES-1,
                 P-STORM-PROP-ATLAS-LIFETIME-1 | Ready packet (oracle + fix on agent/opus; EditMode 2000/2000)
                 ([spec](packets/P-EDITMODE-STORMINK-1.md)) | none (Unity only) |`;
                 (the OWNER_QUEUE entry proposed earlier is no longer needed: the owner ruled, and the coordinator added
                 the docs/OWNER_DECISIONS.md row);
                 .agent-runs/log.csv: `2026-11-22,P-EDITMODE-STORMINK-1,side session (diagnosis + fix),opus,1,PASS (quick),110,oracle by the same session; 5 EditMode runs`
LEGACY DOUBTS:   none
NOT ESTABLISHED: no Web build or live look check (the fix is Editor-only and the amendment changes no behaviour, so the
                 player draws as it did on 64ca1230c); the keep-alive is proven by the Editor cleanup test on this
                 Unity version only.
RISKS:           any future atlas source
                 whose OnValidate dirties the atlas needs the same keep-alive (recorded under Known traps).
```

Follow-ups seen, not done here:
- `editmode.sh` leaves `Resources/StormInk/Props/ColiseumTorchArt.asset` and `ColiseumWeaponRackArt.asset` modified after
  every run: Unity rewrites `m_EditorClassIdentifier:` with a trailing space, and the files were committed without it.
  Either commit the rewritten form or add them to the script's restore list (`Tools/editmode.sh`, beside the Showcase
  fonts).
- CI runs no Unity EditMode, which is how these three reached `feature/unity-rework`; `.github/` needs owner approval.
