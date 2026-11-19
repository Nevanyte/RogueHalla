# P-QUEST25-DEEP-PLAYTEST

## ID and outcome

P-QUEST25-DEEP-PLAYTEST serves Phase 2 item 13 and Phase 3 item 16. The owner requested an independent playtest of every Top 25 objective, including the controls and feedback a player actually receives.

## Dependencies

P-QUEST25-ATTEMPTS, P-QUEST25-JOURNAL and P-QUEST25-PLAYTHROUGH.

## Relevant legacy paths

`packages/server/src/sim/quests.ts` handles the original giver and turn-in loop. `packages/client/src/ui/questMap.ts` presents the legacy map. DEV-039 documents the owner-approved Top 25 design. Two Banners uses the two opposing deliveries specified in that design.

## New paths and exclusive write scope

This packet, its report in `docs/evidence/quest25-deep-playtest-2026-11-19/`, the banner destination in `crates/rh-sim/src/quests/board/attempts.rs`, the corresponding `attempts/tests.rs`, Unity's quest presentation in `MatchHud.QuestRewards.cs`, and the regression in `PlayerFlowsEditModeTests.cs`.

The playtest also covers the Rust simulation input and The Bargain stat catalog in `crates/rh-sim/src/{input.rs,quests/board/attempts/}`, Rust protocol command and quest payload validation and samples, the C# codec and its Unity mirror, and the browser decoder in `crates/rh-server/web/probe.html`. Protocol 20 includes the current client/server version selectors, their handshake and decoder assertions, browser verification tools and their tests. Generate `protocol/fixtures/v20.json` with `cargo run --locked -p rh-protocol --bin protocol-fixtures`. The coordinator updates the active migration checkpoint after verification.

## Contracts and behavior to preserve

C-INPUT, C-OBS, C-DET and D9. Server observations determine the current quest, actionable commands, inventory and rewards. Each player can complete an offer once per raid. Legacy journal rows retain their existing presentation.

## Acceptance checks

- Both banner carriers start at the Gallows. Picking up the rival banner leaves a return delivery outstanding. Arriving at each branch's correct destination awards only that carrier.
- The minimap, tactical map and world show delivered personal objectives for single-word Top 25 definitions. The completing player's personal markers clear.
- An equipped or dropped reusable spell can publish zero charges and a positive recharge timestamp. Zero charges without a recharge clock stays invalid. All 72 ordered exchanges between two distinct broker stats reach the simulation through the matching clients.
- Check the complete catalog with two human seats and their own rewards. Record which interactions ran in a rendered player and which used controlled geometry and encounters. Retain reproducible results for failures.

## Validation commands

Run `cargo test --locked -p rh-sim --lib opposing_banners_pay_only_at_the_destination_of_each_delivery` and Unity EditMode's `PlayerFlowsEditModeTests.MapsAndWorldUseDeliveredPersonalAndPublicPointsAndRemoveCompletedGivers`. Also run the all-quest scenarios, the two-seat native room test, and the repository gate.
