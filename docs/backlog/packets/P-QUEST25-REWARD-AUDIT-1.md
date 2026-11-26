# P-QUEST25-REWARD-AUDIT-1 — earned reward effects

## ID and outcome

Phase 2 item 13, quest completion. Allow StillMind to break an active stun and exercise each reward from its
actual Top 25 quest and turn-in at the legacy and live server step rates.

## Dependencies

P-QUEST25-REWARDS, P-QUEST25-SPECIAL and the generated scenarios under `quests::board::attempts::scenarios`.

## Relevant legacy paths

`packages/server/src/sim/quests.ts:306` completes the legacy attempt and applies its payout.
`packages/server/src/sim/abilities.ts:235` handles casting interruptions from statuses. The legacy catalog pays
different rewards. DEV-040 and the approved Top 25 workbook govern named awards.

## New paths

This packet brief.

## Exclusive write scope

- `crates/rh-sim/src/quests/board/rewards/casts.rs`
- `crates/rh-sim/src/quests/board/attempts/scenarios.rs`
- `crates/rh-sim/src/quests/board/attempts/scenarios/spells.rs`
- This packet brief.

## Contracts and references

C-DET, C-INPUT, C-OBS, C-PARITY and DEV-040. Award values and the effect of StillMind are defined in the existing
Top 25 tuning and effects modules. D9 applies to all reward inputs and outcomes.

## Behaviour to preserve

Keep the existing quest conditions, turn-in rules, inventory, timers and reward amounts. StillMind can be used during
stun because its effect clears stun and root and protects against reapplication. Taunt, pull, silence, active casts
and Stasis retain their existing cast restrictions. Rebirth Egg still needs more than three survivors.

## Acceptance checks

Both generated board sweeps earn the full catalog through validated inputs, verify its corresponding powers and
restore checkpoints. Lost Brother applies stun before the earned StillMind cast and checks that stun disappears and the
draught is consumed. The test fails on the base because the cast is rejected. Hold the Fort checks that the earned
turret damages a player. Whispers in the Brush and Watchtowers check private vision from the earned deployments.
Gatecrasher sends a lethal weapon input against the player with the earned, planted egg and checks for a hatch and
weapon retention above the survivor threshold and final death at the threshold. Existing focused tests cover charges,
replacement, decline, loot and additional reward interactions.

## Validation commands

`cargo test --locked -p rh-sim --lib quests::board` and the coordinator's full gate.

## Risk and suggested model tier

This extends an existing defensive cast exception. Review the cast gate and both step-rate sweeps together. Sol.

## Known traps

Use validated cast input on the reward earned by the tested seat. Ordinary vision persists for 30 seconds and can
mask a failed deployment reveal. Completing Gatecrasher as the second owner kills a second opponent and can place the
match at the egg cutoff after the owner dies.
