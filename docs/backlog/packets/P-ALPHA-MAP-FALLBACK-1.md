# P-ALPHA-MAP-FALLBACK-1 — Preserve signature landmarks on cold starts

Serves the map pool and phase 2 first playable raid. Base `dbaa701be`.

## 1. ID and outcome
Keep the five requested landmarks when the pool returns a configured fallback which already fits the quest board.

## 2. Dependencies
The five accepted zone generators and the map blob's validated anchor section.

## 3. Relevant legacy paths
`packages/shared/src/zoneStyle/landmarks.ts` and the faction style modules supply the original regional landmarks. The live signature art and geometry are the approved ported versions.

## 4. New paths
This packet brief.

## 5. Exclusive write scope
`crates/rh-protocol/src/map.rs`, `crates/rh-protocol/src/map/encoded.rs`, `crates/rh-server/src/map_pool.rs`, its internal tests and this brief.

## 6. Contracts and references
C-NET, C-OBS and P-WIRE17-MAP-3. The cached count is derived from the validated public anchors in either encoding constructor. Wire bytes and versions stay identical.

## 7. Behaviour to preserve
A board-ready map with the requested landmarks reuses its cached Welcome. The private draw survives map regeneration.

## 8. Acceptance checks
A board-ready unsigned fallback regenerates with all five zone anchors. The second admission of the resulting map reuses the same Welcome allocation.

## 9. Validation commands
`cargo test --locked -p rh-server --lib a_board_ready_pool_fallback_still_gets_the_requested_landmarks`, the protocol fixtures and the full gate.

## 10. Risk and review
The cold fallback may require one generated board before the first room can open.

## 11. Known traps
The simulation arena retains geometry, while the Welcome retains the visual anchors. The existence of a volcano feature alone does not establish that the client received its anchor.
