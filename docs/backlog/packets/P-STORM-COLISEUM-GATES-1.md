# P-STORM-COLISEUM-GATES-1 — Limestone gateposts and iron closures

## ID and outcome

Phase 3 item 16a. Give the four Coliseum openings a recognizable silhouette from both approaches. Place the pier artwork on existing solid cells and display ironwork across a doorway when the observed arena state locks it.

## Dependencies

The region mesh renderer, public map gates and arena collections are already present. The Storm Ink editable prop definitions provide the painting, dimensions and foot registration.

## Relevant legacy paths

`packages/client/src/render/MapView.ts:190` toggles visibility of the gate layers. Its gate builder at line 574 paints at the actual gate tiles and orders the closed gate by map row.

## New paths

`Assets/Resources/StormInk/Props/ColiseumGatePier*` under the Unity project, the gate rendering code and its focused Editor tests.

## Exclusive write scope

Coliseum gate presentation, its artwork, definition and Unity metadata, region prop/wall and arena render entry points, focused tests, the visual tracker, this brief and its backlog entry.

## Contracts and references

R-AUTH, C-OBS, D9. Use the delivered collision grid, gate side and gate tiles. The observed arena lock flag is authoritative. All geometry is visual.

## Behaviour to preserve

The entrances retain their delivered span. Existing floor furniture, actor overlap, world lighting and ordinary wall runs retain their respective render paths. A missing gate record creates no invented entrance. A missing pier painting retains the original solid wall appearance.

## Acceptance checks

Capture all four gates in a joined Web match, including the open approach and crossing. Inspect the front and receding closures and the return to the open state. Verify gate endpoint placement, row sorting, missing-art fallback and editable pier sizing in Unity. Keep the wider Coliseum floor, wall finish and dressing checks open until each has its own supporting evidence.

## Validation commands

Run the focused Unity EditMode gate tests, the normal joined browser proof on the delivered map and `scripts/gate.sh` before handoff.

## Risk and suggested model tier

Gate width and depth are easy to confuse on the east and west sides. Review both orientations beside a standing fighter and a wall run. Standard Unity implementer tier.

## Known traps

The square gate footprint can include several consecutive floor tiles. The gateposts belong on the neighboring solid cells. Measurements must use the joins and actual player-foot locations from the captured map.
