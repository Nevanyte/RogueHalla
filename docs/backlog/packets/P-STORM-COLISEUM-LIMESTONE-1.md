# P-STORM-COLISEUM-LIMESTONE-1 — Worn arena paving

## Outcome and dependencies

Phase 3 item 16a. The joined gate review exposed a bright diagonal grid over glossy rectangles. Lay worn limestone at the four thresholds and around the fighting floor, keeping the central dust and rings readable. Compare against the admitted gatepost treatment.

## Scope and implementation

This packet owns the Coliseum floor art definition and pigment, the optional ground-array packing and material binding, the Coliseum section of the region ground shader, focused editor tests and tracker entries. A designer can change the course width, depth, rotation and wash. The painting follows the map coordinates around the arena centre and uses the existing ground quad, region mask and sampler. The painted contribution receives the existing scene shadows and diffuse light. At zero wash or with missing art the original detail and relief remain available.

## Oracle and review

On joined Web revision cb11c36e5 the north threshold and east inner approach show highlights and a rotated joint pattern almost as strong as the fighter's outline. Inspect those spots, the other two gates, the central floor and an actual fight at normal 2560 × 1440 zoom. Compare texture joins, contact shadows, silhouettes and ground marks. Keep open/locked gate authority and terrain data on their current paths. Check that Holy sand and the ice/lava pigments keep their established slots with the optional art present or absent, then run the integrated gate.

## Authority and risks

D1 compares against the public region and gate footprint. D2 is n/a. R-AUTH, C-OBS and D9 apply. No map cells, outcomes, traversal, hitboxes or server visibility are changed. Repeated large slabs could appear mirrored or become too bright near the ring. Confirm at entrances and around moving actors in both Unity and Web.
