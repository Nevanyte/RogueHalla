# P-STORM-REGIONAL-GROUND-1 — Five faction grounds at gameplay scale

## Purpose and dependencies

Phase 3 item 16a, client presentation. The owner approved Plague as the painting reference and the Holy oasis sand as a second anchor. Real joined Fire views show an orange network on ordinary walkable stone. Haunted and Frost also need a surface distinct from gridded masonry and from frozen water.

## Scope

Three ground paintings and editable definitions under StormInk/Surfaces, RegionGround and its texture array and shader, focused EditMode coverage, this packet, its backlog and visual tracker entries. Review all five ordinary surfaces and the joins present on the delivered public map. Maintain Plague's peat, sedge and marsh channels, Holy's sand and gardens, landmark floors, independent terrain masks, region light, shadows and contact effects.

## Oracle

On the starting revision the shared ground material lacks the Fire ash, Haunted earth and Frost snow pigments. Build it with all five regions, then without the new regions. Check packing, exposure, material settings and editability. Compare the same map and camera scale on real Web walks across adjoining regions. Look for checkerboard edges, repeat seams, active terrain clarity, planted feet and contrast against actors. Keep composite landmark or effect checks independent of ordinary ground. Run the full project gate.

## Authority and handoff

D1 uses the delivered region and terrain data. D2 is inapplicable. Rust retains visibility, collision, traversal and damage. The main risk is flattening distinctive ground into similarly grey material or obscuring dry-to-hazard boundaries.

## Gameplay review

The before player tiled Fire with red outlined cells, Frost with a blue slab pattern and Haunted with bevelled squares. Three unlit painted assets now share the same 256-pixel ground texture array as the other regions. The individual FireAshFloor, HauntedHeathFloor and FrostSnowFloor definitions own the pigment, two repeat dimensions, angle and wash strength. Full wash removes the old dry tile relief. The existing terrain pass still renders bog, ice, embers and lava over the independently blended regional ground. The five-region map still uses one ground quad.

The first Web build, `c4478ec11`, passed 34 focused tests, including material edits, optional paints, both established floor systems and the new region indices. The original binding checks failed on `991864383`. The first art pass left a smooth straight band where bright snow met dark ash. Revision `99843b92c` introduces irregular deposits only within the existing regional overlap. Adjacent pigments still use the same normalized map weights and world-space coordinates.

Reviewed at normal zoom in the joined 2560 × 1440 Web player, with byte-identical public maps and identical server-reported player positions in every before/after image pair. Ordinary public cells included Fire (57,102), Haunted (45,40), Plague (128,115), Frost (128,20) and open Holy sand (85,50) outside its hall. The six crossings were Fire/Frost, Haunted/Holy, Plague/Holy, Fire/Haunted, Frost/Plague and Fire/Holy. An actual Fire player crossed from ash to snow and back on video. The separate Low Web route crossed from dry snow onto the lake and reached the lava bank. Native Ultra and Low/Reduced Motion replayed the same public map and tested actor contrast, foot contact and materials under local illumination.

The local review gallery is `world/regional-ground-study/index.html`. It contains original Web frames, the intermediate edge, the final 5.38-second traversal, the two map blob identities, exact positions and Low controls. All baseline and final Web sessions logged an existing Chromium internal-format capability warning. This review covers ordinary ground and the six walked pairings. Landmark architecture, mandatory wall pieces and upright props have their own tracker rows.
