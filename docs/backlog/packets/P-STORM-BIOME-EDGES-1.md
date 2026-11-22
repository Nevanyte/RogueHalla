# P-STORM-BIOME-EDGES-1 — Painted materials and foreground at region boundaries

## Purpose and dependencies

Phase 3 item 16a, following the restored Haunted flagstones. Across adjacent regions, grass changes to smaller straw-coloured tufts toward Frost, Haunted courses break into separated jagged slabs and chips, drift snow and sand interlock, and Fire scree dwindles into pebbles. Plague and Holy interiors are the established reference paintings.

## Exclusive scope

Client ground material and regional art bindings, a small regional transition definition and shader include, selected foreground foliage and scree presentation in RegionProps and its presentation/shadow entry points, focused editor tests, tracker and packet records. Use public map data and existing lighting. Existing furnishing, walls, hitboxes, bush concealment and the server terrain plane retain their meaning.

## Oracle and checks

The starting Web player draws a continuous low-contrast dissolve over fully intact boundary flagstones. Turf and ordinary stones lack coordinated climate changes. The starting ground material also has no editable controls for regional transitions. Show the previous and proposed materials at the same coordinates in real gameplay, including both sides of the Haunted paving, Frost vegetation and Fire scree. Collect a real snow/sand adjacency if the map pool contains one. Test repeatability, pure interiors, three-way junctions, optional art and ordinary terrain versus hazard boundaries. Run the project gate plus 1440p matched Web timing checks.

## Authority and risks

D1 uses only public region, obstacle, dressing and terrain geometry. D2 does not apply. D9 walking and C-OBS visibility remain server-owned. Keep details at the material edge legible at gameplay zoom and avoid covering an actual terrain warning, foliage concealment cell or furnished interior.
