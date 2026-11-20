# P-STORM-CHURCH-AUTHORING-1: data-driven Sentenced church placements

## ID and outcome

Move the church's existing pew, pulpit, stained-glass and candelabrum placements onto artist-owned family definitions and stable semantic slots. Let a church layout override individual fields without copying family defaults, resolve a stable cosmetic reflection from public anchor data, and keep flame lighting pooled and editable.

## Dependencies

- `P-STORM-CHURCH-ART-1` (church footprint, roof, doors and original painted pieces).
- `P-STORM-CHURCH-PIERS-1` (accepted eight-orientation hall footprint and pier replacements).

## Relevant legacy paths

None. This is presentation and authoring data. Map occupancy and combat remain authoritative in Rust.

## New paths

- `client-unity/UnityProject/Assets/Scripts/View/Regions/Landmarks/ChurchArtValues.cs` and `.meta`
- `client-unity/UnityProject/Assets/Scripts/View/Regions/Landmarks/ChurchArtDefinition.cs` and `.meta`
- `client-unity/UnityProject/Assets/Scripts/View/Regions/Landmarks/ChurchInteriorDefinition.cs` and `.meta`
- `client-unity/UnityProject/Assets/Scripts/View/Regions/Landmarks/ChurchArtOrientation.cs` and `.meta`
- `client-unity/UnityProject/Assets/Scripts/View/Regions/Landmarks/ChurchArtPlacement.cs` and `.meta`
- `client-unity/UnityProject/Assets/Resources/StormInk/Church/Definitions/`
- `client-unity/UnityProject/Assets/Tests/Editor/ChurchArtAuthoringEditModeTests.cs` and `.meta`
- `docs/backlog/packets/P-STORM-CHURCH-AUTHORING-1.md`

## Exclusive write scope

- The five new church authoring/runtime scripts above.
- `client-unity/UnityProject/Assets/Scripts/View/Regions/Landmarks/ChurchLandmark.cs`.
- `client-unity/UnityProject/Assets/Scripts/View/Regions/Landmarks/Landmark.cs`.
- `client-unity/UnityProject/Assets/Scripts/View/Atmosphere/AccentLights.cs`.
- The new `StormInk/Church/Definitions` assets and their metadata.
- `client-unity/UnityProject/Assets/Tests/Editor/ChurchArtAuthoringEditModeTests.cs` and its metadata.
- This packet and its row in `docs/backlog/README.md`.

Do not edit `Assets/Editor`, shared visual ledgers, other zones, gameplay rules or the older Frost worktree.

## Contracts and references

- `client-unity/AGENTS.md`: Unity is presentation only; map collision and gameplay stay data-driven by the server.
- `docs/backlog/packets/P-STORM-CHURCH-ART-1.md` and `P-STORM-CHURCH-PIERS-1.md`.
- Public church anchor data has no blueprint mirror flag. The deterministic reflection is a cosmetic choice derived from public anchor fields; it does not claim to recover generator-private orientation. The optional preview override is per call and has no shared static state.

## Behaviour to preserve

- The existing church stays one roofed building with two open doorways, eight walk-through pews, eight stained-glass slots, up to eight matching candelabra and a raised side pulpit.
- Slot identifiers are stable and semantic: `pew.row0.left` through `pew.row3.right`, `window.front.bay0`/`window.rear.bay0` through bay3, matching `candle.front.bay0`/`candle.rear.bay0` slots, and `pulpit.left`.
- Each slot inherits its family's artist-authored defaults; its field mask overrides only named fields. Clearing a flag restores the current family default, including sprite or material.
- Placement transforms use the church's map-facing axes and contact anchors; component refresh is idempotent and re-registers its light socket without adding another pooled light.
- Candle color, intensity, spill radius and spill gain flow through the existing pooled light planner. A placement refresh updates those planner entries without rebuilding the map or creating one light object per candle.
- Props add no colliders and do not change delivered tile occupancy.
- No private anchor fields, hash-order collections, random choices or per-frame allocations are introduced by this path.

## Acceptance checks

- EditMode tests load all four family definition assets and verify actual sprites and world-unit defaults.
- EditMode tests prove field-level inheritance/reset, stable reflection and per-call preview override.
- Runtime-generated church objects expose eight pew, eight window and eight candle slots, plus the semantic pulpit slot, for all eight map orientations.
- Contact-point to UV and world-drag to local-offset handles round-trip; refresh preserves transform and sort order.
- Refreshing a candle updates the existing planner's authored light color, intensity, radius and spill intensity and restoring its material override returns to the shared flame material.
- Run the full Unity EditMode suite. Browser/native appearance and Inspector interaction are reviewed by the owner after integration; these checks do not establish the art's final visual quality.

## Validation commands

- `RH_UNITY_EDITOR=/Applications/Unity/Hub/Editor/6000.0.85f2/Unity.app/Contents/MacOS/Unity bash client-unity/UnityProject/Tools/editmode.sh`
- The packet's focused test can be run by Unity EditMode with filter `Roguehalla.Client.Tests.ChurchArtAuthoringEditModeTests`.

## Risk and suggested model tier

The main risk is a mismatch between serialized world-unit values and the 2.5D contact projection. The eight-orientation runtime oracle and owner Inspector preview are the checks for that boundary.

## Known traps

- `MapAnchor.Front` can describe an oblique arena bearing; derive the church-facing axis from its delivered entrance and footprint when entrance data exists.
- Local Y is map Y projected by `ArenaView.GroundSquash`; upright sprite height and floor depth use different scales.
- A missing material override must return to the shared church lit/flame material, and field masks must not copy a family definition into every slot.
- `Tools/editmode.sh` restores generated Showcase font changes; do not stage generated font assets.
