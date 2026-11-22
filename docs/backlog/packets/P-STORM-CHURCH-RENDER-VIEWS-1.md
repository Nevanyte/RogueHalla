# P-STORM-CHURCH-RENDER-VIEWS-1: directional church art rendering

## ID and outcome

Render church furniture from both projected map-plane axes, select supported upright paintings for cardinal approaches, give window panes explicit full/jamb/concealed modes, and detach the main painted candle flame from its wick so flame edits move both visible art and pooled spill.

## Dependencies

- `P-STORM-CHURCH-AUTHORING-1` (family definitions, semantic slots and placement handles).

## Relevant legacy paths

None. This packet changes Unity presentation only; the delivered church map and collision stay unchanged.

## New paths

- `client-unity/UnityProject/Assets/Scripts/View/Regions/Landmarks/ChurchCandleFlameLayer.cs` and `.meta`
- `client-unity/UnityProject/Assets/Resources/StormInk/Church/CandelabrumBase.png` and `.meta`
- `client-unity/UnityProject/Assets/Resources/StormInk/Church/CandelabrumMainFlameTight.png` and `.meta`
- `client-unity/UnityProject/Assets/Resources/StormInk/Church/GarnetLancetJamb.png` and `.meta`
- `client-unity/UnityProject/Assets/Resources/StormInk/Church/GothicPulpitViews.png` and `.meta`
- `client-unity/UnityProject/Assets/Tests/Editor/ChurchArtAuthoringEditModeTests.cs` (direction, view, pane and flame regression cases)
- `docs/backlog/packets/P-STORM-CHURCH-RENDER-VIEWS-1.md`

## Exclusive write scope

- `client-unity/UnityProject/Assets/Scripts/View/Regions/Landmarks/ChurchArtPlacement.cs` and `ChurchArtValues.cs`.
- `client-unity/UnityProject/Assets/Scripts/View/Regions/Landmarks/ChurchCandleFlameLayer.cs` and metadata.
- `client-unity/UnityProject/Assets/Scripts/View/Regions/Landmarks/ChurchLandmark.cs`.
- The four church family definitions under `StormInk/Church/Definitions/` as needed for directional defaults; in this packet `GothicPulpit.asset`, `GarnetLancet.asset`, and `Candelabrum.asset` change.
- The authored candle, jamb, and pulpit sprites above and their metadata.
- `client-unity/UnityProject/Assets/Tests/Editor/ChurchArtAuthoringEditModeTests.cs`.
- This packet and its row in `docs/backlog/README.md`.

Do not edit `Assets/Editor`, map data, collision, other zones, shared visual ledgers or Frost worktrees.

## Contracts and references

- `client-unity/AGENTS.md`: Unity is presentation only; delivered collision and outcomes remain authoritative elsewhere.
- `P-STORM-CHURCH-AUTHORING-1.md`: semantic slots, contact handles, field masks and pooled candle lighting.
- `ChurchArtPlacement.ProjectFloorSize` is the shared continuous map-plane projection; `ResolveSupportedView` is the shared upright cardinal view resolver for runtime and editor preview.

## Behaviour to preserve

- Floor art follows both map-plane axes continuously through `diag(1, GroundSquash) * Rotation`; width and depth are physical map-plane lengths and intermediate rotations may shear in screen space.
- Upright and wall art stays screen-flat. Cardinal facing chooses Front, Side or Back art instead of rotating an asymmetric painting.
- Window Auto uses the absolute camera-facing wall normal: inward front face shows the pane, an edge-on wall uses the stone jamb, and the back face is concealed. Explicit Full/Jamb/Concealed modes remain available.
- Pulpit uses distinct frontal, side, and rear paintings with stairs and follows the resolved reflected approach; mirrored bay offsets carry matching windows and candles together.
- The candle's wick/base and two smaller side flames remain anchored. Its main flame is a separate transparent layer whose socket and pooled spill move together; reduced-motion mode keeps a stable flame shape.
- Candle light sockets store the ground contact row separately from actual vertical lift, including authored mount height, so `AccentLights` reconstructs the emission point without applying squash or elevation twice.
- Moving art across a tile updates its sorting row by the existing 16-order row step; height-only edits preserve the ground row and resetting remains idempotent.
- The pulpit remains on its semantic side facing the aisle, has a ground contact, and clears adjacent candle footprints. No art adds collision or changes the eight delivered orientations.

## Acceptance checks

- Focused EditMode tests verify continuous affine projection at 0°, 37° and 90°, physical width/depth at quarter turns, and nearest-cardinal upright view resolution.
- Runtime tests verify absolute wall-face pane resolution, all eight church orientations, pulpit-to-candle clearance, reflected off-centre wick placement, row sorting, and exact pooled-light reconstruction from ground row plus lift.
- Full Unity EditMode suite passes; browser/native visual quality remains subject to owner review after integration.

## Validation commands

- `RH_UNITY_EDITOR=/Applications/Unity/Hub/Editor/6000.0.85f2/Unity.app/Contents/MacOS/Unity bash client-unity/UnityProject/Tools/editmode.sh`
- Focused test filter: `Roguehalla.Client.Tests.ChurchArtAuthoringEditModeTests`.

## Risk and suggested model tier

Projection and sprite contact conventions can make visually correct art drift from its map footprint. The math tests and eight-orientation scene fixture guard the runtime boundary; normal-zoom owner review is still needed for final visual approval.

## Known traps

- Do not rotate wall/upright sprites in screen space when the requested facing is not authored; select a supported view.
- Keep floor width in author units when turning 90°; only map-down depth receives the ground squash.
- The candelabrum's original art has baked flame pixels. Moving only a mask or light leaves a duplicate flame at the old socket, so use a base painting without the central flame plus a separate visible flame layer.
- Do not stage Unity-generated Showcase fonts after EditMode runs.
