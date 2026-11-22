# P-STORM-APPROVED-MARSH-1

## Outcome and dependencies

Phase 3 item 16a. Carry the owner-approved marsh and cypress paintings into the delivered Plague biome. Scope is the three existing Plague textures for floor, trunk and crown, StormInkMarsh.hlsl, the Plague light-shaft entry in ParticlePresets.cs, BogTreeLandmark.StormInk.cs, and this packet. The existing Storm Ink marsh, labelled map anchors, crown fade and bound currents are dependencies.

## Baseline and oracle

The prior two tree sprites use different horizontal scales. In the approved drawing the same fork continues from one layer into the other. The two layers must meet at the same world-space row and have the same horizontal origin and scale in all eight orientations. Direct repetition of the approved ground creates an edge seam and repeats the diagonal grass patch. Inspect the textures in the west approach, gate and causeway of the certified 1440p match. Confirm dry crossings, water flow and the character under the crown.

## Contracts and verification

R-AUTH, C-OBS, D9 and ADR 0008 apply. Keep the existing surface, wave, wet-root, local light and occlusion classifications. The legacy map in packages/shared/src/zoneStyle/warren.ts remains the gameplay reference. Run the existing cypress, roof, marsh and shadow EditMode cases, the project gate and the normal-zoom Web route. Compare against a same-camera native baseline.

The first render put the grass strokes into the water. The next preserved the classes but mirrored an entire grass clump into a chevron. The final three-channel texture holds sampled, periodically blended peat, sedge and water pigment in linear RGB. The ground shader reads it once and uses the delivered surface and water masks to place the materials. Both crown and trunk strips keep one pixels-to-world scale. The quieter Plague sun shafts preserve the light direction and leave the fork visible. The 93 focused Unity cases passed, the complete tree renders upright in the eight rotated/reflected landmark fixtures and the player stays visible on the certified causeway.
