# P-STORM-COLISEUM-PARAPET-1 — Painted arena parapets

## ID and outcome

Phase 3 item 16a. The joined three-player crowd and north rack frames show identical square grey blocks with regular grid lines. Dress the solid Coliseum wall cells in worn limestone with a painted frieze beside the existing gate piers.

## Dependencies

Regional row meshes and lighting, the delivered Coliseum footprint, accepted arena gateposts, paving, braziers and weapon rack.

## Relevant legacy paths

`packages/client/src/render/MapView.ts` draws exposed wall faces and sorts wall runs by tile row in `drawWallRow`.

## New paths

`ColiseumParapetArt.cs`, its Inspector refresh, art and definition under `Resources/StormInk/Coliseum/`, and `ColiseumParapetEditModeTests.cs` under the Unity project.

## Exclusive write scope

This packet, its backlog and visual tracker entries, the new paths above with their import metadata, and the existing `RegionWalls` (including its parapet partial), `RegionWallAtlas` and `RegionCanvas` rendering code.

## Contracts and references

R-AUTH, C-OBS and D9. Use the delivered wall plane. Location, traversal and shape-light occlusion continue to come from the existing map.

## Behaviour to preserve

Preserve every wall footprint, exposed south face, ground contact, row depth and real gate opening. Keep low railings, regional boundaries, the church and Holy pier treatments distinct. Use the existing wall lighting and batching.

## Acceptance checks

Check joined cap and face paintings on horizontal, receding, diagonal and isolated cells. Review a matched exterior, entrance, rack and fighting-floor approach at normal 2560 × 1440 zoom, including actor traversal and bright and dark stone. Inspect a saved and reimported designer edit and the source-missing fallback. Record which crops demonstrate the remaining crowd-label issue separately.

## Validation commands

Run `ColiseumParapetEditModeTests`, the adjacent wall/gate EditMode suites, the normal Web player and `scripts/gate.sh` on the integrated revision.

## Risk and suggested model tier

A repetitive or overly dark band can overpower fighters. A broken line at a mesh tile seam exposes the original layout under the painting. Standard Unity implementer tier.

## Known traps

Gate endpoint piers replace specific wall tiles. They own their existing artwork and must continue to be excluded from the row mesh. A terrain fence or a low obstacle inside C4 must keep its proper treatment.
