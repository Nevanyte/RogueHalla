# P-STORM-PLAGUE-BOG-WILLOW-1 — Low willow scrub in the marsh

## ID, outcome and dependencies

Phase 3 item 16a. The joined marsh dressing review found round glossy domes beneath the split-crown trees. Give decorative Plague bushes and actual Plague cover patches one editable low willow painting. The existing Holy myrtle and Fire scrub established the shared atlas and art definition workflow.

## Placement and contract

The legacy decoration map and the public Rust map supply prop feet, region IDs and the separate bush grid. Map row kind 24 is a decorative bush. A nonzero bush grid cell is authoritative cover. Render the new painting at the existing placement and draw order. The opening between the stems should show the player's feet. Preserve the earlier fallback painting and the renderer choices of neighbouring regions. D1 is a visual check against the delivered footprint. D2 is n/a. R-AUTH, C-OBS and D9 apply.

## Scope and ownership

This packet owns the bog willow source import and art definition, the corresponding atlas registration and presentation selection in RegionPropArt and RegionProps, dedicated Editor tests and metadata, and its visual tracker and backlog entries. Keep all map, simulation, materials and gameplay data on their existing paths. Source pigment is original Storm Ink art.

## Acceptance

The failing visual oracle is the joined 2560 × 1440 run at `5babbe104`: on the (4050,3863) decorative bush tile, bush ID is zero, despite the conspicuous shiny green pile. On the same map the ten-cell marsh patch #59 starts at tile (133,126). Compare matched approaches on native Unity and the normal Web player. Walk from zero into #59 and out to zero, check the boots and targeting arc, inspect alignment of adjacent cells and check the authored pigment with the usual surface lighting. Use the focused foliage and atlas tests and the full integrated project gate.

## Risk

The canopy can look convincing alone and still be too dense or too repetitive across a real cover patch. Review a continuous bank and a fully visible decorative bush before acceptance.

## Ordinary decorative bush follow-up (2026-11-21)

The old joined Web baseline `5babbe104` directly shows public kind 24, variant 3 at (4050,3863), tile (126,120), with floor 0 / terrain 0 / Plague region 3 and bush-grid cover ID 0. Its glossy overlapping domes fail against the matte marsh. The revised decorative clump has a favorable native capture (`willow/revised-decorative-detail.webp`, native revisions `cb11c36e5` / `f13b98139`), but the available `1dc224c41` joined Web willow route is specifically the ten-cell kind-59 cover bank and does not unambiguously frame this delivered kind-24 foot. Keep the decorative bush at `Implemented, awaiting visual check` until the revised ordinary prop passes a matched normal-zoom Web review. This is separate from cover acceptance. Evidence and map receipt: `world/plague-marsh-dressing/audit.html` and `receipts/props-audit.json`.

## Ordinary prop Web review completed (2026-11-21)

The archived joined player `ce7f87418` was served locally against the private Rust source-arena server on seed 52666345. Four 2560 × 1440 gameplay captures and traces show approach, beside, the public kind 24 prop foot, and exit. The actual map row is kind 24 / variant 3 / flags 0 at (4050,3863), tile (126,120): floor 0, terrain 0, Plague region 3, and bush-grid cover ID 0. At the foot capture the live fighter was at (4047.9,3872.8), 16.6 world units from the prop. The new paint reads as a low muted willow on marsh mud beside the actor. The matched 5babbe104 baseline shows the glossy green globes at the same footprint; the revised archived player shows the painterly willow. This closes ordinary kind 24 visual review; it is distinct from the ten-cell kind 59 cover bank. Gallery and exact receipts: `world/plague-marsh-dressing/willow/web-kind24-ce7f87418/index.html`.
