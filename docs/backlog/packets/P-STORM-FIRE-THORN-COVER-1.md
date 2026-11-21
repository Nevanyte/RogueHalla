# P-STORM-FIRE-THORN-COVER-1 — Volcanic thorn cover

## ID and outcome

Phase 3, item 16a. Replace the round green marsh shrub on Fire concealment cells with dry matte thorn growth. At gameplay scale the player can identify the strip of cover and its edge while surrounded by ember ground, basalt and lava.

## Dependencies

P-STORM-FIRE-ROCK-FIELD-1 supplies the accepted volcanic field. Use the existing Storm Ink editable prop definition, lit atlas and wind shader.

## Relevant legacy paths

`packages/client/src/render/RaidVision.ts` uses shared bush concealment when displaying actors. `packages/client/src/render/zoneArt/featureStyle.ts` supplies the legacy bush treatment.

## New paths

`client-unity/UnityProject/Assets/Resources/StormInk/Props/FireThornScrub*` and `client-unity/UnityProject/Assets/Tests/Editor/StormInkFireScrubEditModeTests.cs` with Unity metadata.

## Exclusive write scope

The painting, importer, editable definition and metadata above, the Fire cover lookup in RegionProps and the Fire section of RegionPropArt (extracted to RegionPropArt.Fire.cs with Unity metadata), focused editor tests, this brief, its backlog row and the Fire concealment scrub visual check.

## Contracts and references

R-AUTH, C-OBS and D9. The delivered public bush plane and region IDs select the positions. Use the approved map with seed 52666345 and public map hash 1025431840 for matched comparisons.

## Behaviour to preserve

Every nonzero Fire bush cell has one visual at its original contact point. The walkable tile, cover identity, visibility and movement still come from Rust. Maintain the existing non-Fire brush bindings and old missing-art fallback. Apply ordinary vegetation lighting and wind.

## Acceptance checks

The former Fire cell selects `plague.bush` and fails a check requiring a volcanic painting. Check the same approach, inside and exit in native Unity and joined 2560 × 1440 Web gameplay. Compare the player legs, edge of cover, surface lighting and continuity between neighboring plants. Verify editable scale and foot, fallback, wind, Low quality and region isolation.

## Validation commands

Run the targeted Unity EditMode checks, `python3 scripts/readability_check.py`, `scripts/gate.sh --quick` and `scripts/gate.sh`. Capture normal gameplay against the fixed pilot with its protocol-18 content identity.

## Risk and suggested model tier

Moderate visual risk. The scrub can read as isolated decorative plants if neighboring crowns leave too much exposed ground. Renderer work fits the standard Unity implementer tier.

## Known traps

The public bush value identifies the cover patch and can exceed one. Decode the actual tile under the player's foot when checking entry and exit. Ordinary regional scatter and delivered concealment cells use different placement paths.
