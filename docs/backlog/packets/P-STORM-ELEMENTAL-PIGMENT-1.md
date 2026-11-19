# P-STORM-ELEMENTAL-PIGMENT-1 — Restore the colour of ice and lava

## Outcome and order

Phase 3, item 16a. The owner found the first ink ice too grey and the new lava too flat. Make the frozen lake luminous blue with clear depth, and give the molten current a broken hot centre and dark crust. Keep normal-camera fighters legible.

## Dependencies and boundaries

Build on the Storm Ink surface, light and motion packet. Keep the public footprints, shared regional sun, lamp pools, Reduced Motion, contact effects and server outcomes. Legacy parity is unaffected by the painted pigments. Use the existing ground texture-array binding and existing lighting budget.

## Exclusive write scope

This packet, the elemental surface paragraph of docs/art/STORM_INK_LIGHT_AND_MOTION.md, the two Resources/StormInk/Surfaces textures and new folder metadata, RegionGroundTextures.cs, RegionGround.shader, StormInkLightMotion.hlsl and the oracle below.

## Oracle files

- client-unity/UnityProject/Assets/Tests/Editor/StormInkElementalPigmentTests.cs

## Verification and risk

Prove the paintings exist as original texture inputs and share the existing ground sampler. Review the previous and revised material under the same camera and grade at normal zoom with a fighter on actual ice and a fighter beside molten terrain. Compare a lava emitter on and off. Run the full gate, review the Web player at 1440p and check that detail does not obscure silhouettes or remove warm spill from nearby basalt.
