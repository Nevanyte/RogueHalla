# P-STORM-HOLY-HALL-1: Storm Ink Holy Hall alignment

- **Serves:** Not in the frozen plan; owner approved the visual overhaul on 2026-11-19.
- **Implementer tier:** implementer-sonnet
- **Reviewer:** reviewer
- **Integration base:** `codex/storm-cypress-ground-fallback`
- **D-rules touched:** D1 presentation-only; D2 n/a; D5/D6/D9 none

## 1. ID and outcome

Recognize the delivered Holy Hall anchor as one coherent architectural axis. Align its ground wash, 32 sandstone piers, paired lamps and sun shrine to the map footprint, with an open shrine doorway and wall cover that fades correctly. Present the shrine enclosure in the Temple wall style. Keep the existing inferred temple path available when the delivered Hall anchor or art resources are absent. The V4 real-Hall review at `/private/tmp/rh-storm-proof/holy-live-v4/shrine.png` is the accepted visual reference.

## 2. Dependencies

The public Hall map anchor and map data from the existing signature-map work. The Storm Ink landmark sprite placement utilities and roof/fade framework are already in the integration base.

## 3. Relevant legacy paths

None. This is presentation-only and reads public map geometry; it changes no gameplay data or legacy behavior.

## 4. New paths

- `client-unity/UnityProject/Assets/Resources/Regions/StormInkHall.hlsl` and `.meta`
- `client-unity/UnityProject/Assets/Resources/StormInk/Holy.meta`
- `client-unity/UnityProject/Assets/Resources/StormInk/Holy/{AisleLamp,SunPier,SunShrine,TempleStone}.png` and importer metadata
- `client-unity/UnityProject/Assets/Scripts/View/Regions/Landmarks/HallLandmark.StormInk.cs` and `.meta`
- `client-unity/UnityProject/Assets/Scripts/View/Regions/TempleLayout.Signature.cs` and `.meta`
- `client-unity/UnityProject/Assets/Tests/Editor/StormInkHolyHallEditModeTests.cs` and `.meta`
- `docs/backlog/packets/P-STORM-HOLY-HALL-1.md`

## 5. Exclusive write scope

- `client-unity/UnityProject/Assets/Resources/Regions/RegionGround.shader`
- `client-unity/UnityProject/Assets/Scripts/View/ArenaView.Build.cs`
- `client-unity/UnityProject/Assets/Scripts/View/Regions/Landmarks/HallLandmark.cs`
- `client-unity/UnityProject/Assets/Scripts/View/Regions/RegionGround.cs`
- `client-unity/UnityProject/Assets/Scripts/View/Regions/RegionWalls.cs`
- `client-unity/UnityProject/Assets/Scripts/View/Regions/TempleLayout.cs`
- New paths listed above

## 6. Contracts and references

- Unity remains presentation-only; map tiles and anchors remain the source for geometry and collision.
- Preserve roof visibility/fade behavior and the existing `LandmarkView` interior cover contract.
- `client-unity/AGENTS.md`, `docs/landmarks/blueprints/hall.txt`, and the existing landmark roof/fade implementation.

## 7. Behaviour to preserve

- No map, network, simulation, movement, collision, or identity changes.
- The delivered shrine entrance remains open, the altar and enclosure remain roof-covered, and actors inside retain normal roof fade.
- The inferred option-off temple and all missing-resource fallbacks remain supported.
- Walls retain their actual geometry; only the delivered Hall’s pier groups use the authored sprite treatment, and the shrine enclosure receives Temple wall dressing.

## 8. Acceptance checks

- EditMode oracle passes all eight rotations of the delivered 37x37 Hall, checking the nave axis, 32 piers, lamp pairs, shrine entrance and roof cover.
- Existing option-off fixture still finds its inferred temple and procedural props.
- Owner-reviewed V4 screenshot establishes normal-gameplay visual acceptance; structural tests alone do not prove the final rendered look.

## Oracle files (read-only for implementers)

- `client-unity/UnityProject/Assets/Tests/Editor/StormInkHolyHallEditModeTests.cs`

## 9. Validation commands

- `unity test client-unity/UnityProject --mode EditMode --filter 'Roguehalla.Client.Tests.StormInkHolyHallEditModeTests' --timeout 600 --format json`
- Full project gate is coordinator-owned and was not run for this packet.

## 10. Risk and suggested model tier

Presentation-only. Main risks are axis orientation, doorway masking, and sprite placement across rotated map footprints. A normal-camera in-game review is required in addition to the geometry oracle.

## 11. Known traps

- The map’s `MapAnchor` and `TerrainKind.Light` patch determine Hall orientation and shrine depth; do not infer these from world coordinates.
- The Editor may modify generated project settings during tests; exclude unrelated generated changes.
- Keep the extracted `StormHallSurface` in a Holy-only shader include so the existing Plague shader path is unchanged.
