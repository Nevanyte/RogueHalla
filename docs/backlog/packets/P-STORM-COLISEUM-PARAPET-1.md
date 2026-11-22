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

## Visual verification update

The visual checks passed on `6465b287d`; the integrated project gate remains pending owner confirmation. The matched 2560 × 1440 joined Web route compares `ed680e11d` and `6465b287d` on the same certified public map (seed `52666345`, map hash `1025431840`, public map SHA-256 `f38bffe743bfaea721905ba8898df5166279478bafa638c49342348e662d66fd`). It covers the exterior run, both gate approaches, the north rack wall, the arena centre, an isolated cell, wall-shouldered movement and a normal movement attempt into a blocked wall tile. The actor stopped at the same delivered boundary before and after the visual change. The gallery records the renderer (`RegionWalls.Build` using public `MapBlob.Tiles` and the Coliseum `WallStyle.Limestone` atlas), exact captures and movement receipt at `world/coliseum-parapet/index.html`.

The native supplement at `world/coliseum-parapet/native/index.html` records the saved/reimported Inspector edit, fallback and Low/Reduced Motion views. Its public-wall adjacency histogram covers 72 cells: 36 corners, 20 isolated cells, eight run interiors and eight ends; a controlled native study shows the three-way join. The same Web candidate was also inspected in a real three-player C4 fight onset (`world/coliseum-parapet/crowd-6465b287d/index.html`). The limestone wall, gate surroundings and floor read around the fighters and combat marks. Name/health overlap remains an open HUD readability check, tracked separately; the encounter ended before a sustained three-survivor review. No runtime behavior or Rust wall data changed. This evidence does not replace the integrated project gate.

## Risk and suggested model tier

A repetitive or overly dark band can overpower fighters. A broken line at a mesh tile seam exposes the original layout under the painting. Standard Unity implementer tier.

## Known traps

Gate endpoint piers replace specific wall tiles. They own their existing artwork and must continue to be excluded from the row mesh. A terrain fence or a low obstacle inside C4 must keep its proper treatment.
