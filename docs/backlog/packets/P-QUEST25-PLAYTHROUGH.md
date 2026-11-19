# P-QUEST25-PLAYTHROUGH

## ID and outcome

P-QUEST25-PLAYTHROUGH is the Rust server portion of the owner-approved Top 25 release check. Two normal client seats receive the same ten offers, independently complete one shared quest, spend their own rewards, and reconnect into their own inventory state.

## Dependencies

P-QUEST25-BOARD-1, P-QUEST25-ATTEMPTS, P-QUEST25-REWARDS, and P-QUEST25-WIRE.

## Relevant legacy paths

packages/server/src/sim/quests.ts:306 implements the preserved legacy path. Personal completion and Stasis are intentional departures.

## New paths

crates/rh-server/tests/top25_playthrough.rs contains the room integration oracle.

## Exclusive write scope

crates/rh-server/tests/top25_playthrough.rs and this packet. The board’s generated-map sweep covers all 25 definitions. The [attempt scenarios](../../../crates/rh-sim/src/quests/board/attempts/scenarios.rs) provide two independent completions and reconnect views for every definition.

## Contracts and references

C-INPUT, C-OBS, C-DET, C-RNG, C-PARITY. The Stay A While and Stasis rules come from the approved Top 25 workbook and November 18 owner direction.

## Behaviour to preserve

The private deal and release criteria remain in the board packet. The test does not install or modify a player, offer, item, or effect after ordinary room construction. The separate source parity branch retains its legacy witnesses.

## Acceptance checks

Both seats receive the same quest identifiers and giver coordinates during planning. They walk to Stay A While, accept via their own handles, channel for 30 seconds, then earn and cast a Stasis charge. The first completion retires only that owner's quest row. Each reconnects and recovers only their own retained spell. A normal move submitted after casting is blocked while Stasis is active. All inputs pass the actual Rust client encoder, decoder, and authoritative room command intake. The test drives time by room wake deadlines without altering player positions, quest state, or rewards.

## Validation commands

cargo test --locked -p rh-server --test top25_playthrough and the integrated full repository gate.

## Risk and suggested model tier

Sol for the room and codec regression. Clock stepping, owner-specific reliable publication, and the compatibility between projected reward slots and submitted cast slots need end-to-end coverage.

## Known traps

Quest collections send replacements when they change. Each seat must keep the latest replacement from its own outbox. A resumed seat begins with an empty client cache. A charge decrement alone does not establish the spell's actual effect.

## Local WebGL evidence

Two separately rendered Chrome players joined the same Rust room. The journal and tactical map delivered the shared board into the raid, and accepting Two Banners changed only the first seat. [Result and planning journal screenshot](../../evidence/quest25-playtest-2026-11-19/README.md)
