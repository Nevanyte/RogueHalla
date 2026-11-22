# P-STORM-FROST-CAVE-1

## ID and outcome

Use the existing painted `StormInk/Frost/IceRoof` resource for a Frost cave with a valid delivered cave anchor. Fit the cave mouth cut to the entrance tiles in all eight blueprint poses; keep the entrance walkable, the roof on the landmark's existing fade/sort path, and the current procedural cave roof as the nonmatching-anchor fallback. For only a qualifying painted cave, suppress the underlying wall mesh quads and tile sun casters, and mask procedural wall-cap pixels against the same IceRoof alpha so transparent brush edges do not expose rectangular tile bands; leave collision and the landmark's replacement rock mass intact.

## Dependencies

Owner-directed single-environment packet, 2026-11-19. The signed cave shape is `docs/landmarks/blueprints/cave.txt`; landmark presentation and fade already exist in `CaveLandmark.cs` and `LandmarkView`.

## Relevant legacy paths (verified with `rg`)

None. This changes only Frost environment presentation and its proof.

## New paths

- `client-unity/UnityProject/Assets/Resources/StormInk/Frost/IceRoof.png` and `.meta`
- `client-unity/UnityProject/Assets/Scripts/View/Regions/Landmarks/CaveLandmark.StormInk.cs` and `.meta`
- `client-unity/UnityProject/Assets/Scripts/View/Regions/StormFrostGeometry.cs` and `.meta`
- `client-unity/UnityProject/Assets/Scripts/View/Regions/RegionWalls.cs` (Frost painted-cave replacement mask branch only)
- `client-unity/UnityProject/Assets/Scripts/View/Atmosphere/RegionShadows.cs` (same validated mask skips only replaced Frost wall tile sun casters)
- `client-unity/UnityProject/Assets/Tests/Editor/StormInkFrostCaveTests.cs` and `.meta`
- `docs/backlog/packets/P-STORM-FROST-CAVE-1.md`

## Exclusive write scope

- `client-unity/UnityProject/Assets/Scripts/View/Regions/Landmarks/CaveLandmark.cs`
- `client-unity/UnityProject/Assets/Scripts/View/Regions/Landmarks/CaveLandmark.StormInk.cs`
- `client-unity/UnityProject/Assets/Scripts/View/Regions/StormFrostGeometry.cs`
- `client-unity/UnityProject/Assets/Scripts/View/Regions/RegionWalls.cs` (Frost mask integration only)
- `client-unity/UnityProject/Assets/Scripts/View/Atmosphere/RegionShadows.cs` (Frost mask integration only)
- the new Frost resource, partial source and focused test listed above
- this packet note

No other zone assets, map-generation rules, map/wire data, shaders, shared test oracles, capture helpers, legacy files, or `BakedContent.cs`.

## Contracts and references

- `client-unity/AGENTS.md`: Unity renders the server's map and holds no game rules.
- `Landmark` owns the signed anchor, covered footprint, roof sorting order, actor-driven fade and disposal.
- `MapBlob.WithPublic` validates the fixture anchor in the eight-pose oracle.

## Behaviour to preserve

The server-signed cave entrance and collision remain unchanged and walkable. The IceRoof texture is used only for a delivered Frost cave with a 12-or-larger footprint, nonempty classified interior and three floor entrance tiles forming a straight arch at a footprint edge. The texture stays upright; only the doorway cut follows entrance direction. The new roof uses the existing landmark roof renderer sorting/fade and same region sprite-light material as cave walls. The exact same qualification owns the RegionWalls render replacement mask; it omits only wall quads within the delivered cave footprint, never collision. When the anchor/resource/geometry does not qualify, use the existing procedural roof, rock and wall paths unchanged.

## Acceptance checks

- Eight pose cases start from the real cave blueprint via `LookCapture.BuildMap("cave-wide")`, transform the complete footprint/anchor, and pass `MapBlob.WithPublic`; each verifies the imported painted texture, open entrance tiles, roof layer order, exterior opacity, interior reveal, other-actor peek, and fade restoration. The source-driven cap check proves transparent IceRoof edge pixels mask the overlapping procedural cap.
- A RegionWalls comparison verifies that the qualifying delivered map omits replacement render quads while preserving wall tile accounting; the undelivered fixture retains the fallback wall mesh.
- A shadow-caster comparison verifies that only the qualifying painted cave omits the wall-tile sun casters while other map casters remain.
- A negative fallback case injects an otherwise valid but undelivered anchor and proves the old procedural roof remains selected.
- Normal-zoom Unity capture reviews include the cave mouth, entry approach and inside fade.
- Browser gameplay proof uses a real server-signed Frost map; inspect entry, mouth traversal and roof fade at normal zoom and record the map/server/build identity in the handoff.
- Preserve all pre-existing tests and run the Unity EditMode entry point.

## Validation commands

- `bash client-unity/UnityProject/Tools/editmode.sh`
- `bash client-unity/UnityProject/Tools/look-capture.sh cave-wide cave-close cave-inside cave-rot-wide --out <temp-dir> --quality high`
- `git diff --check`

## Risk and suggested model tier

Medium. The existing bitmap may not align with every footprint direction, and the roof alpha aperture must retain clear, walkable entrance tiles. Validate with both the eight-pose source proof and actual signed-map gameplay before handoff.

## Known traps

Do not adjust cave geometry, signed map data, collision, or unrelated Frost ground art to make the image fit. `LookCapture` uses the canonical cave blueprint, but its normal screenshot does not replace the actual server-signed browser replay. All rendering remains in the existing client landmark presenter.
