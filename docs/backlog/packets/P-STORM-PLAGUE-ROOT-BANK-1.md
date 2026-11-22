# P-STORM-PLAGUE-ROOT-BANK-1 — Distinct exposed roots for the marsh bank

## ID and outcome

Phase 3 item 16a. The joined marsh route shows the same triangular cavity along nearly every metre of shoreline. Keep some original root arches and break the repeated shape with a flat, winding exposed root and a short splayed root plate.

## Dependencies

The regional prop atlas, editable prop definitions, the existing rubble fallback and the map dressing kind registry.

## Relevant legacy paths

`packages/client/src/render/MapView.ts` draws the regional grounds. Current public placement and classification are specified in `client-unity/RoguehallaNet/Net/Ui/DecorationKinds.cs`. Plague roots are kind 27, distinct from ordinary rubble at kind 22.

## New paths

Two original root paintings, their editable definitions and a rubble definition under `Resources/StormInk/Props`, plus `PlagueRootBankEditModeTests.cs`.

## Exclusive write scope

The new art, definitions, focused test and their Unity metadata, `RegionPropArt.cs`, `RegionProps.cs`, this packet, its backlog entry and the root bank visual tracker row.

## Contracts and references

R-AUTH, C-OBS, D9. Each public root keeps its foot, fade flag, terrain, view depth and collision status.

## Behaviour to preserve

Keep the original rubble on ordinary rubble entries and the established procedural fallback if a family painting is missing. Use the same matte lighting as other regional plants at the marsh edge. Normal art edits go through the prop definition in the Inspector.

## Acceptance checks

Examine the actual clustered kind-27 feet around (4106,3734), (4113,3691), (4115,3758) and (4140,3696). Compare the same bank at 2560 × 1440 with the fighter approaching, amid the roots and leaving. Check transparent edges, plant scale, spot shadows, variation, and water flowing through its original channel. Verify the source-missing path and an isolated designer edit. Update the tracker after inspecting the images.

## Validation commands

Run the new Unity EditMode tests with the existing rubble and nearby marsh prop suites. Use the normal joined Web player for the route. Run the full project gate on the integrated source.

## Risk and suggested model tier

A winding root can look like an animal at gameplay scale. Keep dark bark breaks, a grounded silhouette and no independent animation. Standard Unity implementer tier.

## Known traps

`plague.root` and ordinary rubble share a legacy art lookup. Select the complete visual entry only for the delivered root kind. The four public variation values remain intact.
