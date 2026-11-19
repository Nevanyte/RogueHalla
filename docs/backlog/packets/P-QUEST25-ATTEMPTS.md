# P-QUEST25-ATTEMPTS — personal quest attempts

## ID and outcome

Implement the approved Top 25 plan's personal quest lifecycle and objective progression in the authoritative simulation. Each owner can accept, retry, and complete a shared offer independently.

## Dependencies

Top 25 catalog and placement, quest action routing and owner observation, and named reward delivery.

## Relevant legacy paths

`packages/server/src/sim/quests.ts` provides the acceptance, credited-kill, collection and turn-in hooks. The ordinary mob runtime supplies kill credit and respawns.

## New paths

`crates/rh-sim/src/quests/board/attempts.rs` and its children implement personal attempts, objective interactions, world-event subscriptions and generated-raid scenarios.

## Exclusive write scope

- `crates/rh-sim/src/quests/board/attempts.rs`
- `crates/rh-sim/src/quests/board/attempts/**`
- The ranked-camp bindings in `systems/mobs.rs`, `systems/mob_life.rs`, and `systems/mob_checkpoint.rs`.
- This packet.

## Contracts and references

DEV-039, C-DET, C-INPUT, C-OBS, C-RNG and C-PARITY. The normal-raid design uses the November 18 owner-approved Top 25 plan and the workbook’s Top_25 and Spell_Reworks sheets.

## Behaviour to preserve

The approved plan introduces new quest behavior and supersedes the workbook's shared retirement rules. The legacy path and its fixtures remain available. The board packet records the coordinated deviation.

The legacy quest system distinguishes pickup, credited mob kills, and camp clears
(`packages/server/src/sim/quests.ts:147`, `:221`, `:352`). Its expiry/turn-in
loop (`:412`) establishes the legacy behavior preserved by the old runtime.
This packet applies the owner-approved per-player departure only to the ranked
board. The regional target bridge uses the normal authoritative mob payout and
respawn paths, with the authored encounter's shorter respawn window.

## Acceptance checks

`cargo test -p rh-sim --lib quests::board::attempts` exercises independent same-offer claims, isolated retry, duplicate tokens, actual regional targets, solo bot bounty, route lock behavior, crown transfer, and restoring a pending claim before exactly-once finalization. The catalog scenario exercises all 25 objectives and named effects with generated boards, committed owner views, and validated commands. For each quest it checks that a second player can accept after the first completes it and that both views stay correct after a checkpoint restore and reconnect.

The catalog scenario runs two human seats through each of the 25 quests in the same generated match. It verifies independent named rewards, both completion records, and both owner views after a checkpoint round trip. Positions and target encounters are controlled in this simulation test. The separate room playthrough uses ordinary movement for both seats.

## Validation commands

`cargo test --locked -p rh-sim --lib quests::board::attempts` and `cargo test --locked -p rh-server --test top25_playthrough`.

## Risk and suggested model tier

Luna and Sol implement and exercise the new rules. The coordinator owns integration, cross-language checks and the full gate.

## Known traps

Lock entry expires Letters and starts Gatecrasher tracking. Other route clocks pause. A player’s turn-in can remove their giver and still leave physical activity visible to another seat. Returning targets require fresh, authoritative kill credit.
