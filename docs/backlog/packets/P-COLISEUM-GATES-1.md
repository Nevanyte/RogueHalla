# P-COLISEUM-GATES-1: locked coliseum gates are drawn closed, and the self walk keeps predicting on the closed-gate map

- **Serves:** owner report 2026-11-19 (relayed by the lane): in the coliseum "the walls don't visually close and the players
  jitter all about". Phase 3 item 16 (Unity client feel, D10). No `docs/OWNER_DECISIONS.md` row covers coliseum gates or
  their prediction (searched "coliseum", "gate").
- **Implementer tier:** implementer-opus (`client-unity/AGENTS.md`: a change to walk prediction is Opus).
- **Reviewer:** reviewer-opus (walk prediction is the one gameplay mirror, ADR 0008, D9).
- **Integration base:** `feature/unity-rework` @ `24a2ce99`
- **D-rules touched:** D1 loose proof (Rust's own closed-gate map, already pinned against a Rust vector by
  `FrostWallAimVectorTests.ThePlanMapOfTheOpenGateMapWithTheGatesLockedIsRustsClosedGateMap`) · D2 n/a · D9: prediction
  stays the local player's walk only, always corrected by the server; no new rule on the client.

## 1. ID and outcome
When the arena reports `GatesLocked`, the server's body map is the Welcome map with every coliseum gate tile WALL
(`crates/rh-sim/src/statics.rs:64` `locked_map = arena.map.closed_gates()`; `crates/rh-sim/src/map.rs:168-194`; with a
standing Frost Wall, `barriers.rs:337-358`: `closed_gates().with_low(wall cells)`). Today the client (a) draws nothing on
the gate tiles, so the arena looks open, and (b) suspends walk prediction for the whole locked fight
(`client-unity/RoguehallaNet/Net/Session.cs:462-470` -> `LocalMotion.SuspendPrediction`), so the self steps at the server
rate (jitter). The comment there ("Welcome contains the open map, not gate geometry") is wrong: `MapBlob.Coliseum.Gates`
carries the gate tiles, and `FrostWallAim.PlanMap` (`Net/Ui/FrostWallAim.cs:80-95`) already builds the locked map from them.

When this lands:
- **A. Prediction.** On a lock, if the Welcome map has `Coliseum` with at least one gate tile, the session gives
  `Motion` a collision map equal to the Welcome map with every `Coliseum.Gates[*].Tiles` tile WALL
  (`Motion.SetCollisionMap(locked)`); standing Frost walls stay LOW on top through the existing `SetWalls` /
  `RebuildWalkMap` path (FLOOR only, as Rust `with_low`). On unlock, `SetCollisionMap(Map)` (the open map) as today.
  Prediction is not suspended merely because gates locked. A Welcome map with no `Coliseum` (or no gate tile) keeps
  today's behaviour: suspend. The Welcome map object itself is never edited.
- **B. Drawing (presentation only).** While `GatesLocked`, every gate tile draws as a closed gate in the coliseum wall
  style (portcullis / bars or a wall block from `RegionWallAtlas`, sorted against actors as a wall row of that tile is);
  the closed gates disappear when unlocked. The tile list comes from a new engine-free helper
  `Roguehalla.Net.Ui.ColiseumGates.ClosedTiles(MapBlob map, bool gatesLocked, List<MapTile> into)` (clears `into`; adds
  every gate's tiles, gate by gate, in delivered order, when locked; nothing when open, when `map` is null or has no
  coliseum). Legacy also shows a faint open-gate floor marking while unlocked (`MapView.ts:603-617`): optional here.

## 2. Dependencies
None (P-FROST-WALL-PREDICT-1 `SetWalls` is in the base).

## 3. Relevant legacy paths
- `packages/shared/src/map.ts:318-336` `withGates`: a copy with every gate tile TILE_WALL.
- `packages/client/src/scenes/RaidScene.ts:985-989` `planMap()` = `gatesLocked ? withGates(map) : map`; `:1045` the local
  motion frame predicts on `planMap()` (legacy predicts on the closed map; it never suspends).
- `packages/client/src/render/MapView.ts:160-163, 191-196, 572-618` `drawGates` / `setGatesLocked`: closed-gate wall rows
  (top on each gate tile, front face where the tile below is not WALL in the closed map; same depth as a wall row),
  visibility flipped by the lock; open-gate floor marking otherwise.
- Rust (reference for the client mirror): `crates/rh-sim/src/map.rs:154-165` `with_low` (FLOOR -> LOW only),
  `:168-194` `closed_gates` (None when no gate tile: Rust then uses the open map); `crates/rh-sim/src/barriers.rs:300-358`.

## 4. New paths
- `client-unity/RoguehallaNet/Net/Ui/ColiseumGates.cs` (the helper above; may also hold the locked-map builder the
  session uses).
- Its synced copy under `client-unity/UnityProject/Assets/RoguehallaNet/` (by `bash client-unity/sync-net.sh`), with `.meta`.
- Optionally one new Unity script under `client-unity/UnityProject/Assets/Scripts/View/` for the gate visual.

## 5. Exclusive write scope
- `client-unity/RoguehallaNet/Net/**` (expected: `Session.cs`, `LocalMotion*.cs` doc comments if needed, the new helper).
- `client-unity/UnityProject/Assets/RoguehallaNet/**` only through `bash client-unity/sync-net.sh`.
- `client-unity/UnityProject/Assets/Scripts/View/**` (gate visual: e.g. `Regions/RegionWalls.cs`, `Regions/RegionWallAtlas.cs`,
  `ArenaView*.cs`) and the one place that feeds it the lock (`ArenaView` reading `Session.Collections.Arena`).
- New test files beside the oracle in `client-unity/RoguehallaNet.Tests/` (not the oracle file).
- No Rust, no wire change, no `protocol/fixtures` change, no existing test edited.

## 6. Contracts and references
- ADR 0008 / R-AUTH / D9: prediction mirrors Rust, local walk only, server always corrects.
- C-OBS: nothing new is observed; gate tiles are already public in the Welcome map.

## 7. Behaviour to preserve
- `PredictionPollTests.LockedGatesSuspendOnlyPredictionUntilOpenMapIsAuthoritativeAgain` (map without `Coliseum`: lock
  suspends, server positions draw, unlock resumes on the open map, 3 inputs sent) stays green unchanged.
- `PredictionPollTests.RealPollArrivalOrderMatchesEveryRustCornerVectorWithoutDoubleWalking`, `LocalMotionTests`
  (`SuspendPrediction` at :442), `FrostWallWalkTests`, `FrostWallAimVectorTests` (PlanMap vs Rust `gate_closed`) stay green.
- `Session.cs:610` resets the lock flag on a Welcome (resume): after a resume during a locked fight the next locked State
  must re-apply the closed map.
- No input is added or dropped by the change (prediction never adds commands).

## 8. Acceptance checks
- `dotnet test client-unity/RoguehallaNet.Tests` green, including every test in the oracle file.
- `bash client-unity/sync-net.sh --check` clean.
- `scripts/gate.sh --quick --oracle packet/P-COLISEUM-GATES-1` passes.
- Look check (coordinator/lane, browser or Editor): in a coliseum fight the gates show closed while locked and open
  after; the self walk is smooth while locked.

## Oracle files (read-only for implementers)
- `client-unity/RoguehallaNet.Tests/ColiseumGatePredictionTests.cs`
  - `LockedGatesMakeTheWalkMapTheServersClosedGateMap`: after a locked State, `Motion.WalkMap.Tiles ==
    FrostWallAim.PlanMap(map, true, [], null)`; the Welcome map is unedited.
  - `APredictedWalkThroughALockedGateDetoursAroundItBeforeAnySnapshot`: an order through the locked gate, 12 frames with no
    snapshot: the disc never enters the gate, and the drawn self moves north toward the ordinary gap (prediction is live).
  - `UnlockingReturnsTheWalkToTheOpenMap`: gate tile WALL while locked; open tiles after unlock; a fresh order walks
    straight through the open gate.
  - `FrostWallsStayLowOnTopOfTheLockedGatesAsRustsOverlay`: wall then lock = closed map with wall cells LOW on FLOOR only
    (a wall cell on a gate stays WALL); unlock = open map with both wall cells LOW.
  - `ClosedGateTilesAreEveryGateTileWhileLockedAndNoneWhenOpen`: the helper, reflection-located (8 gates, all sides).

## 9. Validation commands
- `dotnet test client-unity/RoguehallaNet.Tests --filter "FullyQualifiedName~ColiseumGatePredictionTests|FullyQualifiedName~PredictionPollTests"`
- `bash client-unity/sync-net.sh --check`
- `scripts/gate.sh --quick --oracle packet/P-COLISEUM-GATES-1`

## 10. Risk and suggested model tier
Opus: it changes when the prediction mirror runs and on which map; a wrong map desyncs the self walk for every coliseum
fight. The math is unchanged (existing pathfinder and collision on a different map).

## 11. Known traps
- `FrostWallAim.PlanMap` sets LOW over a gate tile under a wall; Rust's locked overlay keeps it WALL (`with_low` lowers
  FLOOR only). Do not build the walk map with `PlanMap(…, fields)`; use the closed base map + `SetWalls` (oracle test 4).
  PlanMap's own discrepancy is out of scope here (flag only).
- `SetCollisionMap` calls `SuspendPrediction` internally (drops the pending order and correction): a lock mid-walk
  restarts from the authoritative position, which is acceptable; do not call it on every State, only on a lock change.
- Build the locked map once per Welcome map, not per State message (allocation budget: `GarbageTests`).
- Rust `closed_gates` returns None when no gate has a tile (then the open map applies); the client may suspend in that
  case as today (MapBlob validation already refuses a gate with zero tiles).
- The visual reads `GatesLocked` from `Collections.Arena`; it must not change collision or any outcome.

## Notes from previous attempts
