# P-QUEST25-BOARD-1

## ID and outcome

P-QUEST25-BOARD-1 introduces the owner-approved November 18 Top 25 quest board. Each normal raid deals two different quests per home zone, publishes all ten to every human at planning and reproduces the same board after restore. This is the first packet in the approved new-design extension of phase 2, item 13. Activation follows personal completion, rewards and Unity verification.

## Dependencies

The existing K1 quest hooks, generated map, reliable viewer-scoped quest collection and private quest stream.

## Relevant legacy paths

`packages/shared/src/quests.ts:302` selects the old roster, line 660 places it. `packages/server/src/sim/quests.ts:80` starts the private draw. The first global winner at line 306 is the legacy fixture rule.

## New paths

`crates/rh-sim/src/quests/board/` and the independent board acceptance test alongside the existing quest tests.

## Exclusive write scope

The quest board, placement, lifecycle dispatch, giver validation, quest observation, sim configuration and constructors, map admission, checkpoint and content identity wiring, the affected config literals and named quest tests. Documentation scope is this packet, the backlog row, and the quest deviation ledger row. Protocol, rewards and Unity are subsequent packets.

## Contracts and references

C-DET, C-RNG, C-OBS, C-INPUT, C-PARITY and ADR 0008. The accepted Top 25 workbook and owner rulings assign five quests to each home zone. D5/D10 owner exception is limited to this quest program. Legacy K1 remains reproducible for existing fixtures.

## Behaviour to preserve

Use the private quest stream. Public map randomness alone must never reveal the draw. Keep training and disabled quest configurations silent. Published givers are actual neutral actors. Player input continues through validation and submission.

## Acceptance checks

The roster has ten different stable definitions with two per actual giver region. Planning and active views agree, two fresh observers see the same board, and restore keeps the selected identities and locations. Seed sweeps cover all 25 quests, deterministic placements, reachable sites and required landmarks. A map that cannot support its drawn quests requires repair or regeneration before admission.

## Validation commands

`cargo test --locked -p rh-sim --lib quests::`, `cargo test --locked -p rh-server --test live_content`, `scripts/gate.sh --quick --oracle packet/P-QUEST25-BOARD-1`, and the integrated full gate.

## Risk and suggested model tier

Sol for deterministic placement and checkpoint review, Luna for the catalog. Coordinator owns admission, integration and hand-off.

## Known traps

The legacy roster has twenty offers, ten initially visible. Counting visible offers alone misses a stale legacy implementation. The legacy placer can silently drop an offer, and its target region differs from the new starting-giver region. A restored board must use the same map, feature reservations and private RNG state as its initial draw.
