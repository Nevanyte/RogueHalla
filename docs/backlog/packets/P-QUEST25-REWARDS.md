# P-QUEST25-REWARDS — named Top25 awards

## ID and outcome

Deliver the Top25 named reward pool through per-player inventory, full-slot choices, claimable loot, active casts, and match-long permanent effects. WallBuster and Rebirth Egg delegate their internal effects to the dedicated special lane.

## Dependencies

P-QUEST25-ATTEMPTS (`attempts::pending` and exactly-once `attempts::finish`), the Top25 catalog and giver action routing, and the special runtime API for WallBuster and Rebirth Egg.

## Relevant legacy paths

`packages/server/src/sim/quests.ts` supplies legacy turn-in and payout. `packages/server/src/sim/abilities.ts` provides the reference combat and cast lifecycle. The approved workbook supplies the new spell behaviors.

## New paths

`crates/rh-sim/src/quests/board/rewards/` contains spell ownership, drops, permanent payouts, effect simulation, validation and acceptance tests.

## Exclusive write scope

- `crates/rh-sim/src/quests/board/rewards.rs`
- `crates/rh-sim/src/quests/board/rewards/**`
- narrow owned reward-state integration in `crates/rh-sim/src/quests/live.rs`
- `crates/rh-sim/src/systems/weapons.rs` BlindRage attack-speed hook
- `crates/rh-sim/src/systems/frost.rs` and `crates/rh-sim/src/systems/terrain/mod.rs` Warm Coat hazard immunity hooks
- `crates/rh-sim/src/systems/ledger.rs` Feed or Hunt damage bonus and Breaker's Helm armor bypass hooks
- `crates/rh-sim/src/quests/board/attempts/tests.rs` reward-state fixture initialization
- `crates/rh-sim/src/quests/board/mod.rs` module declaration
- `crates/rh-sim/src/quests/board/special/wall.rs` test-only map-borrow correction required to compile the focused suite
- narrow ability, status, movement, hit settlement, item and vision consumers for active spells, including the shared low-ground barrier, private scene observation, and the existing combat stat fold
- this packet

## Contracts and references

DEV-039, C-DET, C-INPUT, C-OBS, C-RNG and C-PARITY. The normal-raid design uses the November 18 owner-approved Top 25 plan and the workbook’s Top_25 and Spell_Reworks sheets.

## Behaviour to preserve

These named rewards implement the approved Top25 design note and `Top_25` / `Spell_Reworks` workbook values. They are a deliberate departure from the legacy quest payout pool; existing legacy quest captures remain unchanged. Workbook values called out as playtest defaults are encoded as tunable constants. Quest turn-ins grant their named reward. Smite’s credited mob kill grants its spell bonus through ordinary mob GV. WallBuster and Rebirth Egg internals are excluded and delegated to the dedicated special lane.

## Acceptance checks

Focused `cargo test -p rh-sim quests::board::rewards::tests` covers one-time payout, independent owners, replacement/decline, loot pickup, cooldown and charges, permanent effects, and save/restore validation. Initial red: tests reference the absent `board::rewards` module and runtime.

Active gameplay oracle: `cargo test --locked -p rh-sim --lib quests::board::rewards::effects::tests`.
It runs ordinary validated inputs, verifies actual Stasis movement and damage, and checks spell effects on the collision
map, combat ledger, statuses, visibility, and cooldowns. Deployment and effect rows are checked on restore.

## Validation commands

`cargo test --locked -p rh-sim --lib quests::board::rewards`, `cargo test --locked -p rh-sim --lib attempts::scenarios::full_slots`, and the integrated protocol and Unity suites.

## Risk and suggested model tier

Luna and Sol implement and exercise the new rules. The coordinator owns integration, cross-language checks and the full gate.

## Known traps

An outstanding replacement decision must survive reconnect and leave both equipped spells intact until the player chooses. Ground spells keep their charge count and recharge deadline. Actual movement, targeting, damage and stat changes establish an effective reward.
