# P-ALPHA-ZONE-ART-1: restore distinct alpha zone art and align directional fence rails

- **Serves:** open alpha gameplay restoration, visual issues 3–6; Unity presentation only
- **Implementer tier:** implementer-sonnet
- **Reviewer:** reviewer
- **Integration base:** `codex/alpha-gameplay-restoration` @ `dbaa701be`
- **D-rules touched:** D1 n/a (new presentation has no legacy behavior); D2 n/a; D5/D6/D9 none

## 1. ID and outcome
Normal alpha mode must show one distinct landmark for each zone and its matching ground treatment. The server owns map
features and sends anchors/tiles; the client must draw the delivered data through its active Storm Ink path. Sentenced
fence rails must follow their actual connected tile runs. Older procedural fallbacks must not replace available approved
Storm Ink assets. A Plague anchor's authored marsh wash must be visible with its unique ground, without painting hazards
onto non-Plague or non-floor tiles.

## 2. Dependencies
- The alpha server recipe enables `--map-pool --signature-landmarks` (owned by the alpha recipe packet).
- Existing map anchors, landmark art, Storm Ink surfaces and Plague wash are in the integration base.

## 3. Relevant legacy paths
- `client-unity/UnityProject/Assets/Scripts/View/Regions/RegionWalls.cs:122-151` draws every delivered `TerrainKind.Fence` as the same tile-wide low wall.
- `client-unity/UnityProject/Assets/Scripts/View/Regions/RegionWallAtlas.cs:451-480` paints the Timber fence texture with horizontal rails.
- `client-unity/UnityProject/Assets/Scripts/View/Regions/PlagueGround.cs:42-73` binds `StormInk/Plague/MarshWash` only for a valid delivered cypress anchor, and classifies only Plague floor tiles.
- `client-unity/UnityProject/Assets/Scripts/View/Regions/RegionGround.cs:35-48` binds the authored wash and loads each region's ground art.
- `client-unity/UnityProject/Assets/Scripts/View/Regions/Landmarks/LandmarkView.cs:31-58` consumes delivered anchors; `LandmarkArts.cs:26-40` registers all five kinds.

## 4. New paths
- This packet brief; targeted Unity EditMode regression(s).

## 5. Exclusive write scope
- `client-unity/UnityProject/Assets/Scripts/View/Regions/RegionWalls.cs`
- `client-unity/UnityProject/Assets/Scripts/View/Regions/RegionWallAtlas.cs`
- `client-unity/UnityProject/Assets/Scripts/View/Regions/PlagueGround.cs` (only if test demonstrates a live alpha path defect)
- targeted Editor tests under `client-unity/UnityProject/Assets/Tests/Editor/`
- no third-party art imports, map generation, wire, hosting, actor sprites, or palette-wide recoloring

## 6. Contracts and references
- `docs/DEVIATIONS.md` DEV-020 describes signature landmarks as server-delivered map features.
- `docs/backlog/packets/P-UNITY-LANDMARK-1.md` and `P-UNITY-LANDMARK-2` through `-5` define the five landmark renderers.
- Unity client rules: presentation only; collision, terrain, and feature placement remain server-owned.

## 7. Behaviour to preserve
- Fence collision and terrain are unchanged; only texture orientation may change.
- Landmark geometry remains derived from delivered anchors/tiles; keep roof fade and row sorting.
- Ground wash and surface classification stay within the delivered Plague region and floor mask.
- Approved Storm Ink resources remain primary, existing procedural fallbacks remain available if an asset is missing.

## 8. Acceptance checks
- A regression built from delivered map tile neighbors verifies horizontal and vertical fence runs receive matching artwork orientation, including a turn and an isolated tile.
- A Unity EditMode test verifies an eligible delivered Plague anchor binds the Storm Ink wash and produces non-empty unique ground coverage only on Plague floor tiles; a map without an anchor keeps existing fallback behavior.
- Build each of the five delivered landmark kinds through the active `LandmarkView.Build` path and assert the matching builder produces visible renderers.
- Inspect a real alpha Web frame after server recipe integration; confirm the five landmarks, aligned fence runs, and Plague ground are visible.
- Run targeted EditMode tests and `scripts/gate.sh --quick --oracle packet/P-ALPHA-ZONE-ART-1`.

## 9. Validation commands
- `bash client-unity/UnityProject/Tools/editmode.sh`
- `scripts/gate.sh --quick --oracle packet/P-ALPHA-ZONE-ART-1`

## 10. Risk and suggested model tier
Sonnet. The bitmap shaders and Unity Web view are the final authority for appearance; mesh-level tests alone do not prove
the lit browser result.

## 11. Known traps
- Do not infer server feature placement from client visuals or add client-generated map landmarks.
- Do not recolor all Plague/Storm Ink assets: only fix identified legacy fallback selection and measured ground/wall rendering defects.
- The alpha host recipe is protected and belongs to its own packet.
