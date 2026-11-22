# P-STORM-CONCEALMENT-CANOPY-1 — Readable concealment at fighter scale

## Outcome and dependencies

Phase 3 item 16a. Build on the accepted Fire thorn density and the Plague bog willow. Increase the size of real hiding places at the normal camera distance. Keep a visible difference between a decorative plant and a cover patch using the same painting.

## Ownership and contract

This packet owns the shared canopy proportions asset, the cover-cell presentation in RegionProps, PlagueWillowArt's use of that placement identity, the corresponding regional foliage Editor checks and the environment tracking entries. D1 compares the art against the public map and the gameplay camera. D2 is n/a. C-OBS, R-AUTH and D9 apply. Rust supplies the bush ID and gameplay visibility. The renderer draws a rooted regional plant per delivered cover cell.

## Visual oracle

At 9f0d8f982 the Fire fighter appears to stand in front of a short hedge and the marsh fighter's torso clears the willow crown. Record the same walkable approach, inside position and exit on the same public map. Rooted cover should rise around the legs and torso, keep the local fighter locatable and clear the lane at exit. Keep the open Fire painting and the lower decorative willow. Compare single and adjoining cells and check other regions using the shared cover rule.

## Implementation and checks

Expose canopy width, height and near-branch sort depth in a version-controlled ScriptableObject. Apply these only to the placement rows created from the public bush plane. Compare map bytes, tile IDs, sprite pivots, draw order, and neighbouring decorative plants. Use focused Unity tests, matched native images, joined Web traversal, and the full integrated gate. Track each region's visual result independently.

## Haunted correction from the first joined loop

On public bush #39, sizing the procedural cluster made glossy green domes obscure the entire Haunted body. Replace that regional paint with an editable blackthorn cutout. Its branches have open gaps and purple and sage washes suited to the graveyard. The public placement name, roots and cell IDs stay the same. Use the same shrub at ordinary scale on delivered Haunted decorative rows. Retain the original neutral fallback and keep each region independent if a source is missing. This correction also owns the blackthorn pigment and import, art definition, atlas registration, regional paint lookup and mixed-region tests. Review the same Haunted approach, inside and exit in both players.

## Frost correction from the second joined loop

Two real Frost patches, at the east border of seed 52666345 and by the inland lake on certified seed 185, exposed the same silhouette. The enlarged circles with scalloped white caps looked like stacked stones. Fit an editable, original snow-dusted juniper to the existing Frost bush entry, including decorative shrubs on older maps and delivered concealment cells. The old procedural drawing remains available when the new pigment is unavailable. Review both locations at the same normal-zoom gameplay camera, including the return to open ice, atlas margins, root registration, Low and Reduced Motion.

## Integrated visual result

At gameplay revision `1dc224c41`, all five joined 2560 × 1440 routes entered a delivered bush from open ground and returned to open ground. The patch IDs were Fire 23, Plague 59, Haunted 39, Frost 68 and Holy 73. Every build and faction used identical public map bytes on seed 52666345. A second certified map, seed 185, verified the Frost juniper at the inland lake on patch 67. The Haunted blackthorn was additionally checked across both halves of patch 39 and two outward crossings. The current source imports, authored material, margins, placement and fallback suite passed 35 focused Unity tests. The native Low/Reduced Motion captures use the same art and public positions. Matched images, public positions and frame hashes are in `world/concealment-canopies/final/index.html`, with separate Haunted and Frost galleries for the painting revisions. The full integrated gate and matched performance runs are tracked separately from these visual results.

## Opponent view

A second Web player watched Fire approach, enter and leave thicket 23 on revision `a7ae7cbe2`. Frost held the same position throughout. Fire was visible to Frost in the open, disappeared in the thicket and reappeared on exit. Fire’s own clothing remained visible between the branches and her health bar stayed readable. Matching the public snapshots by server tick confirmed that both players were alive and that Frost received Fire’s actor only on approach and exit. The paired Web frames, full-resolution captures and public observations are in `world/concealment-canopies/opponent-view/index.html`.

## 1440p performance sample

Three interleaved runs per revision compared `9f0d8f982` with `1dc224c41` on an M5 Pro. The 95th-percentile frame interval in Fire cover was 8.2 ms on the earlier build and 8.3 ms on the larger canopy. In nearby combat it was 8.0 ms and 8.3 ms. Both builds measured a median frame interval of 6.9 ms. The final build transferred 376,349 additional bytes. Four runs overlapped a separate workspace test job, mob counts varied, and each browser graphics audit recorded 0x500. Use these as descriptive measurements. The six-run details, timestamps, inputs and outliers are in `world/concealment-canopies/final/performance-three-pairs.json`.
