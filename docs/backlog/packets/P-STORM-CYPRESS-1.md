# P-STORM-CYPRESS-1 — Plague cypress and marsh contact

## Outcome and order of operations

Phase 3, item 16a. The approved Storm Ink cypress reads as a forked landmark in a live match. A fighter can cross behind the fork and remain legible. The surrounding marsh uses a painted ground wash, with distinct grass, mud, shallow water and bog using the delivered map.

## Dependencies and references

The church pilot, public BogTree anchor, R-AUTH, C-OBS, D9 and ADR 0008. The baseline is the church pilot at eafb6af3c. The legacy Warren bog crescents and dry crossings are described in packages/shared/src/zoneStyle/warren.ts. The delivered blueprint is docs/landmarks/blueprints/bog.txt.

## Exclusive write scope

In client-unity/UnityProject/Assets the scope includes Resources/StormInk/Plague with importer metadata, Resources/Regions/{RegionGround.shader,RegionSpriteLit.shader,StormInkMarsh.hlsl}, Scripts/View/Atmosphere/RegionShadows.cs, Scripts/View/Regions/{RegionGround,RegionWalls,PlagueGround,StormCypressGeometry}.cs and Scripts/View/Regions/Landmarks/{Landmark,BogTreeLandmark,BogTreeLandmark.StormInk,StormInkLandmarkSprite}.cs. Include Unity metadata for new files, the oracle files below and this packet.

## Behaviour to preserve

Read wall groups, hazard tiles, variants and actors from public data. Preserve walkability, surface effects, network identities, movement and simulation. Unmatched footprints and absent artwork use the existing rendering path. Detached knees follow the separate wall islands. The shadow under the painted tree is stationary. Other wall casters and the local-light occlusion map retain their normal behaviour.

## Oracle files

- client-unity/UnityProject/Assets/Tests/Editor/StormInkCypressEditModeTests.cs
- client-unity/UnityProject/Assets/Tests/Editor/StormInkMarshPigmentTests.cs
- client-unity/UnityProject/Assets/Tests/Editor/StormInkCypressMaterialFallbackTests.cs

Against the pilot the eight positive landmark cases failed and the five fallback cases passed. Render all eight map orientations upright and preserve the actor row order. Check the water and mud pigments on a delivered dressed map without changing the surface used for footsteps.

## Verification

Run Unity EditMode, including StormInkCypressEditModeTests, StormInkMarshPigmentTests, StormInkCypressMaterialFallbackTests, BogTreeLandmark, BogTreeCrown and RegionShadow. Run scripts/gate.sh. The normal-camera bog-close and bog-under views check scale and readability. The crossing regression uses a real actor at tile (31,24). In a 2560 by 1440 Web match on seed 52666345, use the west approach, gate at (4102.6,3511.8) and causeway at (4343.2,3511.8). Inspect the fork, contact wash, corner shadows and the player's silhouette at those positions.

## Risk and implementation tier

Presentation only. Sol implementation with a separate visual review. Source images are original generated watercolor art. Avoid high-frequency ground detail that obscures the character at normal zoom. A dressed map supplies variants 7 and 8 for decorative water and mud, independently of the bog hazard.
