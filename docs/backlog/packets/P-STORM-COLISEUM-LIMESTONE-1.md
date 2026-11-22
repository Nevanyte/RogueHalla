# P-STORM-COLISEUM-LIMESTONE-1 — Worn arena paving

## Outcome and dependencies

Phase 3 item 16a. The joined gate review exposed a bright diagonal grid over glossy rectangles. Lay worn limestone at the four thresholds and around the fighting floor, keeping the central dust and rings readable. Compare against the admitted gatepost treatment.

## Scope and implementation

This packet owns the Coliseum floor art definition and pigment, the optional ground-array packing and material binding, the Coliseum section of the region ground shader, focused editor tests and tracker entries. A designer can change the course width, depth, rotation and wash. The painting follows the map coordinates around the arena centre and uses the existing ground quad, region mask and sampler. The painted contribution receives the existing scene shadows and diffuse light. At zero wash or with missing art the original detail and relief remain available.

## Oracle and review

On joined Web revision cb11c36e5 the north threshold and east inner approach show highlights and a rotated joint pattern almost as strong as the fighter's outline. Inspect those spots, the other two gates, the central floor and an actual fight at normal 2560 × 1440 zoom. Compare texture joins, contact shadows, silhouettes and ground marks. Keep open/locked gate authority and terrain data on their current paths. Check that Holy sand and the ice/lava pigments keep their established slots with the optional art present or absent, then run the integrated gate.

## Authority and risks

D1 compares against the public region and gate footprint. D2 is n/a. R-AUTH, C-OBS and D9 apply. No map cells, outcomes, traversal, hitboxes or server visibility are changed. Repeated large slabs could appear mirrored or become too bright near the ring. Confirm at entrances and around moving actors in both Unity and Web.

## Visual result

Revision `a7ae7cbe2` passed 21 focused Unity checks. In the native replay, a quarter turn rotated the courses and zero wash restored the original floor pattern. The joined 2560 × 1440 Web route crossed all four gate thresholds on the same public map as the earlier build. The stone picks up the neighbouring marsh, Fire and Frost colours at the entrances. Its narrower joints leave the aim arc and foot marker visible against both stone and sand.

Two joined players repeated the centre fight at the same server positions in both builds. On the painted floor Fire struck Frost at tick 851, 43.8 game units apart. Both actors were inside the delivered Coliseum. The public hit event reported 14.58 damage and Frost's own health fell from 985 to 969.82. The gallery at `world/coliseum-limestone/index.html` has the original and painted Web frames, full-resolution thresholds, the centre fight, inspector-control comparisons and the focused test report. Standards, torch spill and the mandatory wall blocks have separate outstanding visual work. The integrated gate is tracked separately.

### Crowded encounter supplement — scene review remains open

The archived `ce7f87418` Web client was joined to a private local source-arena room with three normal player sessions and zero fill bots. All three reached the interior of certified C4 on map hash `1025431840`. At server tick 866, Fire, Frost and Holy were alive at `(2538.1,2549.1)`, `(2570.9,2549.1)` and `(2538.1,2581.9)`, 32.8–46.4 units apart. Their server snapshot HP was `898.19/970.42/1091.66` from `950/985/1145`; the same tick records public windup, swing and hit FX. Tick 867 records another exchange. The active-onset 2560×1440 views, map receipt, positions and compact FX sample are in `world/coliseum-crowd/index.html`.

This confirms the new worn limestone remains visible beneath a close three-player fight and its target, damage and Frost Wall placement marks. Overhead names and health bars stack in the crowded view. The fight later ended naturally with two players eliminated, so the screenshots document the live three-player onset rather than a sustained three-survivor encounter. Keep the combined Coliseum scene row at **Needs visual rework**: the square wall blocks still need their visual redesign, and the overlapping labels remain a crowd readability observation.
