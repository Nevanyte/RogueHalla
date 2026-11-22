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
