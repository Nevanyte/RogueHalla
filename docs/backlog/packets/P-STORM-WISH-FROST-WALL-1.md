# P-STORM-WISH-FROST-WALL-1: Plant the Frost Wall in dry-brush ice

- **Serves:** not in the current backend operation plan: owner-approved visual work, 2026-11-19
- **Implementer tier:** implementer
- **Reviewer:** reviewer
- **Integration base:** Fire Wish packet `587b149f8ecd58c5ebc2d8199233eb7919dd6ff4`
- **D-rules touched:** D1 n/a (Unity presentation only) · D2 focused Editor oracle and look capture · D5/D6/D9 none

## 1. ID and outcome

Give the planted Frost Wall authored, brush-painted ice relief. Horizontal runs use a connected shelf of overlapping blades with asymmetric peaks, blue-black depth, broad turquoise mid-value planes, and pale glacial striations. Vertical/receding runs use a separate longitudinal painting with broken crests on both sides, so they do not read as a rotated horizontal wall. Slice each image only along contiguous delivered wall runs so it remains row-sorted; actual gaps remain gaps. Keep a minimal per-tile ink fracture for hit timing and a source-driven opaque mesh fallback if an art resource is missing. The cool scene light stays restrained. Wall tiles, collision footprint, cast duration, area lifetime and end fade remain exactly the delivered values. The cast gathers along its delivered telegraph segment; after the wall appears, a small fixed set of local lights samples only its delivered tiles.

## 2. Dependencies

The combined Storm Ink client snapshot and the delivered wall tiles, which supply the collision footprint and row sorting.

## 3. Relevant legacy paths

- None. This is presentation only; Rust still determines the wall and its lifetime.

## 4. New paths

- `client-unity/UnityProject/Assets/Tests/Editor/FrostWallInkEditModeTests.cs`
- `client-unity/UnityProject/Assets/Tests/Editor/FrostWallInkEditModeTests.cs.meta`
- `client-unity/UnityProject/Assets/Resources/StormInk/Surfaces/FrostWallShelf.png`
- `client-unity/UnityProject/Assets/Resources/StormInk/Surfaces/FrostWallShelf.png.meta`
- `client-unity/UnityProject/Assets/Resources/StormInk/Surfaces/FrostWallReceding.png`
- `client-unity/UnityProject/Assets/Resources/StormInk/Surfaces/FrostWallReceding.png.meta`
- `docs/backlog/packets/P-STORM-WISH-FROST-WALL-1.md`

## 5. Exclusive write scope

- `client-unity/UnityProject/Assets/Scripts/Presentation/FrostWallPresenter.cs`
- `client-unity/UnityProject/Assets/Editor/Look/LookCapture.cs` (the Frost Wall art capture only; its diagnostic fixture stays in `BuildWallScene`)
- `client-unity/UnityProject/Assets/Tests/Editor/FrostWallInkEditModeTests.cs`
- `client-unity/UnityProject/Assets/Tests/Editor/FrostWallInkEditModeTests.cs.meta`
- `docs/unity/DIALS.md` (the existing `frost-wall-ice` row only)
- `docs/backlog/packets/P-STORM-WISH-FROST-WALL-1.md`

## 6. Contracts and references

- The wall presenter consumes only `AreaKind.Wall` and its delivered `Tiles`, `StartedAt`, and `EndsAt` values. Contiguous tile runs join visually; actual gaps keep exposed end rims.
- Cast anticipation stays on the `FrostWallPresenter.Telegraph` segment and its delivered duration; the meshes and analytic lights are presentation only.
- `LocalLightField` supports bounded staged lights and is already quality-gated by `AtmosphereQuality.AnalyticLights` and `FxLights`.
- The existing six-row `frost-wall-*` dial index and its `FrostWallReadEditModeTests` remain intact; new look dials are added to the `frost-wall-ice` row.

## 7. Behaviour to preserve

- Preserve signed per-tile placement, row sorting, key-based pooling, crack timing, fade, preview and cast line. The authored slices follow contiguous delivered components, including vertical/receding orientation, and do not invent a tile across a signed gap.
- Preserve reduced-motion and lighting-quality controls; do not add per-tile light objects or allocate during a warmed frame.
- Never create, move, extend, or retain a wall based on local animation. A released wall still disappears when its area row does.
- No gameplay, protocol, map, actor, or other Wish changes.

## 8. Acceptance checks

- The focused source oracle proves nonblank pixels from `FrostWallShelf.png` reach horizontal sprites, vertical runs use the separate `FrostWallReceding.png` relief, painted feet remain anchored to delivered tiles, disconnected signed islands receive separate one-tile slices, polygon face panels are removed when art is present, and staged light centers stay within delivered telegraph or wall tiles.
- Existing `FrostWallPresenterEditModeTests` and `FrostWallReadEditModeTests` remain green.
- Review normal-scale Frost captures for horizontal, vertical and cracking walls; rerun `wall-read-check.py` on the four prescribed wall and preview captures.
- The owner-facing art plates omit the red placeholder block while `LookCaptureWallEditModeTests` continue to verify the stand-in diagnostic fixture.
- No oracle/capture files are changed.

## Oracle files (read-only for implementers)

- None. The new assertions are source-driven and use the existing Editor test assemblies.

## 9. Validation commands

- `/Applications/Unity/Hub/Editor/6000.0.85f2/Unity.app/Contents/MacOS/Unity -batchmode -projectPath client-unity/UnityProject -runTests -testPlatform EditMode -testFilter Roguehalla.Client.Tests.FrostWallInkEditModeTests -testResults <path> -logFile <path>`
- `/Applications/Unity/Hub/Editor/6000.0.85f2/Unity.app/Contents/MacOS/Unity -batchmode -projectPath client-unity/UnityProject -runTests -testPlatform EditMode -testFilter Roguehalla.Client.Tests.FrostWallPresenterEditModeTests -testResults <path> -logFile <path>`
- `/Applications/Unity/Hub/Editor/6000.0.85f2/Unity.app/Contents/MacOS/Unity -batchmode -projectPath client-unity/UnityProject -runTests -testPlatform EditMode -testFilter Roguehalla.Client.Tests.FrostWallReadEditModeTests -testResults <path> -logFile <path>`
- `client-unity/UnityProject/Tools/look-capture.sh frost-wall frost-wall-vertical frost-wall-cracking frost-wall-preview frost-wall-preview-invalid --out <dir>`
- `python3 client-unity/UnityProject/Tools/wall-read-check.py <dir> --check`
- `git diff --check`

## 10. Risk and suggested model tier

Medium: overlapping tile meshes must read as one jagged obstacle in both orientations while retaining row-based actor sorting. Normal-scale captures are the final visual check; presentation does not alter collision.

## 11. Known traps

- Keep authored slices connected across each contiguous run while preserving per-row depth sorting. Use the horizontal shelf for horizontal runs and the longitudinal relief for vertical/receding runs; the art supplies the silhouette and face. Avoid reconstructing either as repeated tile polygons. Retain only the independent hit-fracture overlay in the authored path.
- `LocalLightField` is a fixed staged-light pool. Sample a few positions across a wall instead of creating a light object for each tile.
- The five Editor look captures are not the browser combat proof. A Web Wish fight remains a parent-owned integration check.
