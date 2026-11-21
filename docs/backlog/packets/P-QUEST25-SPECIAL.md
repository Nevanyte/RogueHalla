# P-QUEST25-SPECIAL — WallBuster and Rebirth Egg

## ID and outcome

Implement the Top 25 WallBuster and Rebirth Egg reward effects, validation, physical state, observation/wire contract and Unity presentation. WallBuster destroys only eligible ordinary terrain within 300 units and keeps collision, navigation and visibility coherent. Rebirth Egg is a planted, publicly counterable object that delays final death settlement for three seconds and revives at 40% HP only if intact and more than three fighters remain alive.

## Dependencies

P-QUEST25-ATTEMPTS and the Top 25 reward-slot caller on `codex/top25-quests`.

## Relevant legacy paths

`packages/` contains no WallBuster or planted Rebirth Egg implementation. This packet uses the explicitly approved workbook spell rework and quest brief as the new behavior source. Workbook: `docs/quests/FUTURE_ONLY_Quest_Design_Discussion.xlsx`, sheets `Top_25` and `Spell_Reworks`.

## New paths

- `crates/rh-sim/src/quests/board/special.rs` (or `special/`)
- this packet

## Exclusive write scope

- `crates/rh-sim/src/quests/board/special.rs` and private children/tests
- precise integration callsites only after checking for concurrent edits and coordinating their ownership
- separate Unity dynamic-terrain and egg observation/wire/render region, excluding `QuestView`
- this packet

## Contracts and references

C-AUTH, C-INPUT, C-OBS, C-RNG, C-NUM, C-TICK, checkpoint/hash coverage, `docs/ORDER_OF_OPERATIONS.md` quest objective/reward item, workbook `Top_25` #21/#7 and `Spell_Reworks` Rebirth Egg row. User brief's 300u destruction radius and protected terrain rules govern WallBuster. Workbook spell row governs egg placement at least 600u from coliseum, 200u public reveal radius, 3s hatch, 40% HP and disabled at <=3 alive.

## Behaviour to preserve

A qualifying death still receives normal kill credit/bounty, leaves the Plague corpse, resets patience, reopens gates, and is not erased while an egg hatch is pending. Destroyed eggs and low-alive restriction resolve the pending death exactly once. MIA and removal are not qualifying deaths. WallBuster cannot remove the outer rim, coliseum enclosure/gates or quest anchors. Effective movement, A*, collision and LOS use updated geometry; checkpoint restore recreates the same geometry and egg state.

## Acceptance checks

- WallBuster accepts a real spell aim at eligible terrain <=300u and refuses invalid owner, out-of-range/empty aim, protected rim/enclosure/gate/quest anchor, and non-destructible terrain.
- Destroyed cells immediately agree across collision, pathfinding and LOS; checkpoint restore yields the same effective geometry.
- Egg placement obeys owner/one-at-a-time, active-match, map, 600u coliseum and alive-count rules. Public observation reveals it at <=200u and supports counterplay (destroying it).
- Integration exercises the real lethal-damage/death/settlement path: credit, bounty, corpse, patience reset and gate reopening happen once; intact egg hatches at three seconds at 40% HP when >3 remain; destroyed egg or <=3-alive resolves death once.
- Rust, C# and JS decoders consume any wire extension; Unity invalidates dynamic map state and presents terrain removal and egg reveal/destruction from server observations.
- Existing parity fixtures stay green; new behavior gets a loose numeric sanity proof.

## Validation commands

`cargo test -p rh-sim special` (to be created by this packet); `cargo test -p rh-sim`; `dotnet test client-unity/RoguehallaNet.Tests`; Unity EditMode terrain/egg presentation tests; `bash client-unity/sync-net.sh --check`; `scripts/gate.sh --quick` then full gate with `--oracle packet/P-QUEST25-SPECIAL`.

### WallBuster geometry and witnesses

A cast aims at a destructible cell within 300 units of the caster. The blast opens eligible tile centres in that radius, including part of a standing Frost barrier when the rest falls outside it. Permanent terrain protects the outer rim, coliseum walls and gates, and one tile around the selected quest's interaction points. Lava and crevasses remain intact. The saved cell set is ordered and the effective map is rebuilt for both gate states. Reliable State key 14 carries the complete permanent overlay after a change, reconnect, or resync. Temporary barriers still use the Fields collection.

- `cargo test -p rh-sim --lib special_wall_buster` covers validated slot usage, radius and protections, real Frost barrier clipping, A*, LOS, checkpoint round-trip, and refusal of invalid saved terrain.
- `cargo test -p rh-protocol --test destroyed_terrain --test vectors` and `node tools/protocol-js/check-vectors.mjs` exercise the overlay bytes.
- `DestroyedTerrainTests` verifies the Rust vector, effective-map replacement, and prediction invalidation for a partially removed barrier. `WallBusterTerrainEditModeTests` verifies the arena wall mesh changes when an opening arrives.

## Risk and suggested model tier

High: dynamic collision/navigation/LOS must be authoritative and deterministic; delayed death settlement intersects same-tick credit, coliseum and corpse rules. Requires implementer-opus and reviewer-opus for Rust state/settlement; Unity protocol/view integration follows client local rules.

## Known traps

`TileMap` caches path masks/components and is immutable after build; every effective geometry consumer must use the same overlay. Death settlement can happen within a tick or between ticks. Do not put hidden egg coordinates into public views outside reveal rules. Never let an egg revive a terminal/end-run result or double-resolve it. Keep shared quest view and reward integration files outside this packet's exclusive scope.
