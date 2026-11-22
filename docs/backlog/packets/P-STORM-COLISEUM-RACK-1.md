# P-STORM-COLISEUM-RACK-1 — An open oak rack at the arena wall

## Outcome and dependencies

Phase 3 item 16a, after the arena brazier. The rack on the northern approach reads as another closed gate in the joined 1440p baseline. Six identical vertical spear shafts line up with the pier behind them. Give the arrangement enough open space to read the wall, the stored items and the fighter crossing in front.

## Scope and authority

Own the regional rack pigment, import, editable art definition, rack presenter, tests and visual tracker. Use the same sword, bow and axe sprites as the transferable equipment. Keep the wood and items in the region's existing lighting and planted draw order. The rack is scenery at the map's normal decoration positions. Rust owns wall solids, pickup entities and movement. D1 compares the placement and materials to the existing renderer. D2 is n/a. C-OBS and D9 apply.

## Visual oracle

Compare the same approach, stand in front, cross past both ends, and return to open ground in joined Web builds on seed 52666345. Compare the top edges, gaps between items, ground contact, actor overlap and the view of the adjacent stone pier. Try a new size and an individual item angle in Unity. Check shared sprite identity, rebuild, missing-paint fallback, Low with Reduced Motion and normal lighting. Keep the broader arena wall and combat review independently tracked.

## Evidence

- Joined 1440p Web frames at `ce7f87418` and `ed680e11d` show the northern rack at tile (82.5,72.62). Frost walked up, stood at the rail, crossed to either side and returned to open floor. The new frame leaves the pier visible. Its sword, bow and axe use three existing equipment views and draw behind Frost when he crosses in front. Both public map streams have SHA-256 `f38bffe743bfaea721905ba8898df5166279478bafa638c49342348e662d66fd`.
- The first native pass showed the three weapon tops lining up at the stone lip. Staggered the lengths and angles. A controlled draw without the pier then exposed premature occlusion in immediate preview renders. Resolving the sorting groups once when placing the rack restored the tips before the first camera render. On the matched diagnostic 133 of 135 overlapping weapon pixels visibly affect the stone. The final Web route confirms the silhouettes at gameplay scale.
- Low with Reduced Motion retains all three items. In the isolated editor, saved a width of 1.203125 world units and a sword angle of 13 degrees, reimported the wood and reopened Unity. The new frame width and sword lean both appeared at the original planted position. The player defaults are 1.5625 and −19 degrees. Eighteen focused Editor checks passed on the final renderer, including shared sprite identity, missing paint, item grouping, wind and editable anchors.
- The gallery, full Web frames, native edit, receipts and tests are at `world/coliseum-rack/index.html`. No Rust or equipment art changed.
