# P-STORM-HAUNTED-FLAGSTONES-1 — Watercolour flagstones

## Purpose and dependencies

Phase 3 item 16a, following P-STORM-REGIONAL-GROUND-1. The owner reopened Haunted ground after the five-region comparison. Its square stone courses are part of the faction identity. Restore the tile scale using dark charcoal, umber and muted plum, with soft pigment blooms and dry-brush edges.

## Scope

The Haunted exterior ground source, designer art definition, normal material binding and ground shader, focused EditMode checks, tracker entries and backlog. Preserve the church's accepted nave and aisle and the existing map masks for outdoor contacts and neighbouring regions.

## Failing oracle and verification

On 99843b92c, the Haunted surface paints continuous earth. Compare the same Haunted exterior at public cell (45,40) in the original tiled player, the earth iteration and the repainted player. Walk across (60,40) and (61,40) into Holy to check the last stones, inspect the graveyard by the church, and review the Low and normal gameplay views. Test the editable sheet, course spacing, alignment and procedural fallback in the live ground material.

## Constraints

Rust retains collision, terrain authority and visibility. In D1 the delivered cells are the placement reference. D2 is inapplicable. Preserve D9 and the game lighting. Avoid directional bevels in the painted source, a recurring four-tile wallpaper block, and bright mortar that competes with combat.
