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

## Evidence and completion

- Joined public Web candidate `8a38edfc1`, protocol 18, content hash `10dfa3b399dc43a1`, certified seed `52666345` (map-pool hash `3ccddedb`; C4 rectangle tiles 71,71 through 88,88).
- At server tick 60 / 2,000 ms, arena authority reported gates open. At tick 12,060 / 602,350 ms, the normal timer reported `gatesLocked=true` and `inColiseum=true`. Frost player id 1 remained at `(2560,2746)` while ordinary outward movement samples were blocked.
- After the Fire player id 3 left through the normal browser session and its reconnect grace expired, public authority reopened the arena at tick 12,795 / 639,450 ms (`gatesLocked=false`, `inColiseum=false`). Frost id 1 crossed the same south gate and reached `(2560,2933)` outside C4 at full 985 HP. Three joined seats kept the match active through the departure.
- The published gallery at `world/coliseum-gates/index.html` links normal-zoom open, locked and server-reopened Web captures plus compact authority, route, public-map and input-hash receipts. Earlier joined Web evidence checks four open approaches and four locked interiors; the native evidence checks all four orientations, open/closed/reopened presentation and 19 focused Editor tests.
- At gameplay scale the opening, iron closure and reopened passage read clearly. Delivered square wall cells remain visually prominent; this belongs to the separate Coliseum walls/floor/standards review and does not block this gate-presentation packet.

The gateposts and closures visual checks are complete. This does not close the broader Coliseum environment review or establish the full project gate.
