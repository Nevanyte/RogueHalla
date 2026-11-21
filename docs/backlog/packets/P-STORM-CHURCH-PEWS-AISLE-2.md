# P-STORM-CHURCH-PEWS-AISLE-2

## ID and outcome

Phase 3 item 16a. Bring the Haunted church pews off the end walls and frame both doorways with four banks of painted oak. Carry a worn band of the approved slate through the nave and reserve the central crossing for combat.

## Dependencies

P-STORM-CHURCH-WALL-FINISH-1 and the editable church preview.

## Relevant legacy paths

The delivered church uses the geometry in docs/landmarks/blueprints/church.txt.

## New paths

ChurchNaveArt.cs, NaveSeating.asset, ProcessionalSlate.png and ChurchAislePaintingEditModeTests.cs with their Unity metadata. The source painting replaces OakPew.png in place.

## Exclusive write scope

The new paths, OakPew.png and its existing furnishing definition, seating and floor assembly in ChurchLandmark.cs, the Church Art Studio layout control and ChurchArtEditorRefresh.cs, church editor tests, this packet, the backlog row and the ten reopened entries in the visual tracker.

## Contracts and references

R-AUTH, C-OBS and D9. Owner decisions of 2026-11-20 approve the carved limestone and reopen the pew and aisle composition. The subsequent owner review reopens the aisle on its own. Use the approved overhead church study for the paving and compare against the paired-pew revision at 9fb7029e4.

## Behaviour to preserve

All delivered walls, two open doors, roof masking, the carved limestone, side pulpit, lamps and windows. Retain the shared pew definition, eight named slot overrides, planted projection and the underlying slate wash. All seating is walk-through.

## Acceptance checks

In all four orientations and their reflections, verify the inner seat ends frame the three-tile doorway width, every seat clears solid cells, the end banks stand away from the glass, and the central crossing stays clear. Inspect the seat, end cheeks and back rail at gameplay scale. Verify the roof states and a joined 1440p Web traversal through both doors and the new pew positions. Check a shared layout edit in the ordinary preview. Follow the staggered stone courses from both doors, including the quiet carving at the crossing. Confirm the paving turns and reflects with the building and that it preserves the slate beyond the aisle.

## Validation commands

Run ChurchLandmarkEditModeTests and the church authoring tests, the 16-view Unity capture, a joined Web traversal and scripts/gate.sh.

## Risk and suggested model tier

Ground squash shortens the projected seat depth. Inspect the narrow edge and side-facing views. Unity presentation implementer.

## Known traps

Do not use the old bank coordinates as Web traversal evidence. Sample the actor at the new benches. The room contains eight solid groups; the two nearer the aisle on each side constrain the seat length.
