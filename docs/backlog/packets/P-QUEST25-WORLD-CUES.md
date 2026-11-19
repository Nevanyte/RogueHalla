# P-QUEST25-WORLD-CUES

## ID and outcome

Top 25 deployables, spells and public quest activity have visible world cues. The tactical map shows the server's coordinates for the Crown, Smuggler, Fort, meditation and quest bounty even after a viewer's own turn-in.

## Dependencies

P-QUEST25-BOARD-1, P-QUEST25-ATTEMPTS, P-QUEST25-REWARDS, P-QUEST25-SPECIAL and P-QUEST25-WIRE.

## Relevant legacy paths

packages/shared/src/quests.ts:302 defines the preserved legacy quest roster. These new cues follow the approved November 18 Top 25 design.

## New paths

crates/rh-policy/src/delivery/collections/field_projection.rs owns projection of observation fields, projectile rows and Top 25 cues. This packet records the protocol and presentation work.

## Exclusive write scope

The State field kinds and codec, the official v19 fixture, the policy field projection, the C# field codec and its Unity mirror, the JS probe decoder, the Unity arena and tactical map cue renderers, and the field regressions.

## Contracts and references

C-NET, C-OBS, C-DET and D9. New State key 3 kinds 9–20 carry geometry with anonymous owner and subject fields. The simulation selects the source observations. The client draws the received coordinates.

## Behaviour to preserve

Field kinds 0–3 retain their established wire layout. Codes 4–8 are reserved. The Cage uses the existing tile-backed wall row, Rebirth Egg uses its dedicated prop row, and permanent destruction uses the terrain replacement.

## Acceptance checks

The v19 world-cue vector round-trips in Rust, C# and JS, with the full set of new field kinds and coordinates. An anonymous cue carrying an actor reference is rejected. Reliable field replacement clears cues once their source observation disappears. Public marks and transient effects use separate authored glyphs or pooled world shapes.

Dropped spells also appear on the arena floor at the coordinates in the viewer’s reward collection. The Unity EditMode test `WorldQuestRewardUsesDeliveredGroundPointAndClearsOnPickup` checks placement and removal after pickup.

## Validation commands

cargo run --locked -p rh-protocol --bin protocol-fixtures

cargo test --locked -p rh-policy --lib visible_cues_are_anonymous_reliable_replacements_and_clear_when_no_longer_observed

node tools/protocol-js/check-vectors.mjs

dotnet test client-unity/RoguehallaNet.Tests

bash client-unity/sync-net.sh --check

## Risk and suggested model tier

Cross-language codec and visibility work needs independent review. Reliable collections keep a per-viewer baseline, and a normal map marker can outlive the completion row it originally accompanied.

## Known traps

Opaque world cues reuse reliable field geometry without actor handles. A caster's private seed is owner-scoped. Public quest marks appear in their own coordinates collection, alongside each viewer's personal objective list.
