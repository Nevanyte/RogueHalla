# P-NET-POLL-ALLOC-1: a change of the standing-wall set no longer allocates the walk map and the pathfinder tables inside `Session.Poll`

- **Serves:** not in ORDER_OF_OPERATIONS. The client input lane the owner started from the 2026-11-22 audit (earlier packets
  P-NET-ARRIVAL-CLOCK-1, P-NET-STEER-1). Evidence from the UI rework lane's perf A/B, 2026-11-23.
- **Implementer tier:** the coordinator decided both halves are in scope (wall-map clone and pathfinder tables and node array); the pathfinder half touches mirrored
  prediction code that must keep the Rust vectors, so the tier is the coordinator's call (proposal in section 5).
- **Reviewer:** reviewer-opus if `Pathfinder*.cs` or `LocalMotion*.cs` change (AGENTS.md routing: prediction math), else reviewer
- **Integration base:** `feature/unity-rework` @ `c9629669f`
- **D-rules touched:** D1 loose proof (allocation budgets, no capture) · D2 n/a · D5/D6 none · D9 unchanged (no prediction change)

## 1. ID and outcome

The Unity `netPoll` section wraps exactly `session.Poll()` (`UnityProject/Assets/Scripts/Net/MatchClient.cs:309`). In 3 of 10
Web-build sessions it allocated 17 to 114 KB per frame for whole windows of 5 to 8 seconds (table:
`.agent-runs/lane-ui-rework/understand/perf-ab-1-windows.txt` in the main checkout), against 0.1 to 3 KB otherwise.

**Measured here, the engine-free library has a cause that fits.** Driving a real `Session` (160 x 160 tile map, 40 fighters, 60
polls and 20 snapshots a second) through a fake transport, a poll allocates 0 to about 100 B in every ordinary case (idle, walking,
steering, follow, five snapshots in one poll, an unreachable goal, a resume after the first 2 s). The one thing that allocates
tens to hundreds of KB inside `Poll` is a **change of the set of standing walls** (Frost Walls rising and falling), carried by a
State frame:

| Poll that applies a State whose wall set differs | bytes, one poll | where |
|---|---|---|
| standing still | about 26.5 KB | `LocalMotion.SetWalls` -> `RebuildWalkMap`: `(byte[])_map.Tiles.Clone()` (25,600 B) and a `new MapBlob` (`LocalMotion.Walls.cs:85-100`) |
| walking (a path remains, `WalkBlocked` runs) | 259 KB, or 669 KB on the poll after a map change that also re-plans | the pathfinder sees a new `MapBlob` object, so `Prepare` rebuilds its tables: `_comp` int[n], `queue` int[n], `_nav` byte[n] = 230 KB (`Pathfinder.Tables.cs:17-60`), and `Reset` allocates `new Node[n]` = 410 KB (`Pathfinder.Search.cs:27-34`, forced by `_n = 0` in `Prepare`) |

A fight with several Frost Walls changes the set several times a second (a rise, then a fall each); each change also makes the
next pathfinder query rebuild again when the set returns to the base map. At four changes a second while walking that is about
2.7 MB a second, the size of the observed 3.6 to 6.9 MB a second. This is a fit, **not a proof**: the lane's builds did not
record whether walls stood in the three bad windows (see "Not established"). The packet is worded so that whatever else is found,
the measured defect is fixed and pinned.

When this lands: a poll that applies a changed wall set allocates at most 2 KB, standing or walking, and every ordinary poll
stays at 512 B or less.

## 2. Dependencies

None. (`P-FROST-WALL-PREDICT-1` introduced `SetWalls`; it is merged.)

## 3. Relevant legacy paths

None: the walk mirror's wall handling is a Rust-side mirror (`rh-sim` `barriers::body_map`, `raise_wall`), not legacy TypeScript. No
change to what the mirror computes. Legacy has no equivalent client allocation concern.
Owner decisions: `docs/OWNER_DECISIONS.md` has no row on this subject. Its 2026-11-13 row (GarbageTests: "warm up ... rather than
raising the budget ... or tolerating recurring garbage") applies in spirit: do not loosen a budget here.

## 4. New paths

- `client-unity/RoguehallaNet.Tests/PollAllocationTests.cs` (the oracle).

## 5. Exclusive write scope (proposal; the coordinator decides)

- `client-unity/RoguehallaNet/Net/LocalMotion.Walls.cs`: build the walk map without a fresh 25 KB array and `MapBlob` per change
  (a retained scratch tile array, or mutate and invalidate), keeping the object-identity contract below.
- `client-unity/RoguehallaNet/Net/Pathfinder.Tables.cs`, `Pathfinder.Search.cs`, `Pathfinder.cs`: tables and node array rebuilt into
  retained arrays when the map size is unchanged (no `new` per map object); the node array must not be reallocated because `_n`
  was zeroed.
- `client-unity/RoguehallaNet/Protocol/MapBlob.cs` only if the `MapBlob` constructor's hashing is the remaining allocation.
- `client-unity/UnityProject/Assets/RoguehallaNet/**` copies, through `bash client-unity/sync-net.sh`.
- Not in scope: `Session.cs` State handling, `Collections.cs` (its per-frame rows measure 440 B and are within budget).

## 6. Contracts and references

- ADR 0008 / D9: prediction mirrors Rust and is always corrected; nothing here may change a path or a position.
- `client-unity/AGENTS.md`: edit the library, then `sync-net.sh`; the pathfinder passes `protocol/fixtures` `pathfinding_*`,
  the walk mirror `server_walk_*`, `frost_wall_walk_v1.json`.

## 7. Behaviour to preserve (existing tests that pin what this changes)

- `FrostWallWalkTests.cs:218-232` (`WalkMap` is the Welcome map object when no wall stands, a different object while one does, the
  Welcome map again once it falls): keep identity semantics. A fix that keeps one reused walk map object while walls stand and
  returns the base map when none do is compatible.
- `FrostWallWalkTests.cs:235-261` `ASteadyFrameWithAStandingWallAllocatesNothingInTheWalkMirror`: same wall set again, in a fresh
  array, allocates exactly 0.
- `FrostWallWalkTests.cs`, `ColiseumGatePredictionTests.cs`, `DestroyedTerrainTests.cs`: the walk, re-plan and gate maps
  (`SetCollisionMap`) results stay byte for byte.
- `GarbageTests.cs`: steady decode 0 B, actor-count-varying decode at most `40 * (actors + 1) + 64`.
- `PathfinderTests`-style vectors (`pathfinding_v1.json`, `server_walk_v1.json`) stay green.
- No conflict was found between these tests and the oracle: the oracle only adds bounds. A fix that returned the SAME `MapBlob` object
  after a wall change would break `AreNotSame` at `FrostWallWalkTests.cs:223`; do not.

## 8. Acceptance checks

- `dotnet test client-unity/RoguehallaNet.Tests --filter PollAllocationTests` fully green (21 tests: the five red ones pass, and
  `GuardAWallSetThatChangesAgainAndAgainNeverServesAnAnswerFromAnEarlierSet` stays green).
- Both halves are in scope and both must be done: the wall-map clone (standing: 27 KB to at most 2 KB) and the pathfinder tables and
  node array (walking: 259 to 669 KB to at most 2 KB).
- `dotnet test client-unity/RoguehallaNet.Tests` green; `bash client-unity/sync-net.sh --check` clean.
- `scripts/gate.sh --quick --oracle packet/P-NET-POLL-ALLOC-1`.

## Oracle files (read-only for implementers)

- `client-unity/RoguehallaNet.Tests/PollAllocationTests.cs`

Red today (5 of 21 tests in `PollAllocationTests`, all by assertion):
- `TheWallSetChangingEveryTickWhileStandingAllocatesWithinTheBudget` (27,104 B on a poll),
- `TheWallSetChangingEveryTickWhileWalkingAllocatesWithinTheBudget` (666,912 B on a poll),
- `FrostWallsRisingAndFallingEveryHalfSecondAllocateWithinTheBudget(false)` (28,440 B) and `(true)` (668,608 B): the realistic shape,
  one wall every 500 ms standing 4 s,
- `GatesTogglingWithAWallStandingAllocateWithinTheBudget` (26,856 B per toggle poll): a coliseum gate closing or opening with a
  wall standing goes through `SetCollisionMap` -> `RebuildWalkMap`, the same 25.6 KB clone, so the same 2,048 B budget.

The other sixteen tests pass today and stay as guards (see the table in the hand-off for their bytes). The measurement method is in
the file header: each scenario is replayed 3 times from scratch on one thread and each poll takes its minimum over the replays
(the probe's best-of defence against collector noise, which only adds bytes); budgets are "at most", never exact.

### Budget, how chosen

- `WarmBudget = 512` B per poll, after 180 polls (3 s: the snapshot spare exists after 40 snapshots = 2 s). Clean scenarios measure
  at most 104 B (the once-a-second Ping: `Sync.MakePing`, `Encode`, `Send`) and 1.5 B mean on a quiet poll.
- `StateBudget = 2048` B for polls that also apply a State frame (440 B measured with a standing wall, 544 B with the Ping), re-plan
  after a snap (504 B, a 21-waypoint path array; 608 B with the Ping) or change the fighter count on every snapshot (1,728 B,
  `40 B * 41 + header`, the same bound `GarbageTests` pins).
- After a Welcome or resume: a bounded burst of at most 8,192 B per poll for the first 132 polls (40 snapshots = 2 s = 120 polls plus
  12 margin); measured 1,776 B per snapshot poll (40 fighters, no spare yet: `Session.cs:474-513` / `SnapshotBuffer.TakeSpare` null),
  then the warm budget. Five snapshots drained in one poll measured 8,880 B only inside that first 2 s, and 0 afterwards.
- **Destroyed terrain** does NOT go through `RebuildWalkMap`, so it is not red and its allocation is not removed by this scope.
  The 28 to 34 KB is made in `Collections.Apply` (`Collections.cs:461-470`) by `MapBlob.WithDestroyedTerrain` (`MapBlob.cs:127-144`:
  a `Tiles.Clone()`, a new `MapBlob` and `WithPublic`): a NEW terrain map object per distinct destroyed-cell set. `Session.cs:554-566`
  detects the change by `ReferenceEquals(previousTerrain, TerrainMap)` and renderers hold that object, so reusing one buffer is a
  design change, not a cleanup. It happens once per change of the cell set (a wall-buster), not per frame, and the same cell list
  again allocates 440 B (guard `GuardTheSameDestroyedTerrainOnEveryStateAllocatesWithinTheBudget`, at the 2,048 B budget). The
  per-change cost stays pinned at 64 KB (`GuardDestroyedTerrainChangingEveryTickAllocatesWithinTheBudget`, measures 34 KB). The Motion
  side of a terrain change (`SetCollisionMap(Map)`, `Session.cs:566-575`) allocates nothing in the poll (no wall standing); its
  pathfinder rebuild is deferred to the next `Order` (outside `netPoll`).
- **Coliseum gates**: with no wall standing a toggle allocates 400 B in the poll after the first closing (the first closing builds
  and caches `ClosedGateMap`, 28 KB once per Welcome, `Session.cs:717-727`): guard
  `GuardGatesTogglingWithoutWallsAllocateWithinTheBudget`. The pathfinder's rebuild (`InvalidateMap()`, `LocalMotion.cs:SetCollisionMap`) is
  again deferred to the next `Order`; measured about 0.6 MB there, outside `netPoll`, and the fix in scope for the pathfinder removes it too.
- The stale-state guard `GuardAWallSetThatChangesAgainAndAgainNeverServesAnAnswerFromAnEarlierSet` is green on the base by construction:
  60 rounds on ONE long-lived `LocalMotion` and ONE long-lived `Pathfinder` (reading `motion.WalkMap`, as Unity steering does) over
  seven wall sets (none, a column across the route, another column, two walls, a row, none, a short column), each round compared with
  FRESH instances EXACTLY (no tolerance): the walk map tiles against an independent build, every waypoint of three planned paths,
  five `SegmentClear` probes, and the pose after a second of walking with a wall set changing mid-walk (a rising wall that blocks
  the rest of the walk makes `SetWalls` re-plan). `WalkBlocked` and the motion's own path are private, so they are compared through
  what they decide (the pose after the re-plan). Because the long-lived `Pathfinder` is the test's own, a fix that mutates the
  walk-map tile array IN PLACE and keeps the same `MapBlob` object while the set changes between two standing sets fails this test (the
  Unity steering pathfinder would be stale too): hand out a new object, or make the pathfinder detect the change itself.

## 9. Validation commands

- `dotnet test client-unity/RoguehallaNet.Tests --filter PollAllocationTests` (add `--logger "console;verbosity=detailed"` for the
  `POLLALLOC` lines: per scenario warm mean, warm max, polls over budget).

## 10. Risk and suggested model tier

Sonnet for the `LocalMotion.Walls.cs` half (reuse a scratch tile array). The pathfinder retained-table half is mirrored Rust logic;
a mistake changes paths. If the coordinator wants only the cheap half, keep the map object stable
so `ReferenceEquals` in `Pathfinder.Prepare` holds and call `InvalidateMap()` only when the tiles really changed. That also kills
the 410 KB `Node` array churn when the node array is kept across `Prepare`.

## 11. Known traps

- `Pathfinder.Prepare` keys its tables on `MapBlob` reference identity; a retained, mutated array without `InvalidateMap()` serves
  stale tables (wrong paths, silently). The existing `FrostWallWalkTests` replay catches it only if the wall set changes between
  samples.
- `_walkMap` is also read by keyboard steering (`MatchClient`, Unity side: `motion.WalkMap`, its own `Pathfinder`): that pathfinder
  rebuilds its tables when `WalkMap` changes object. A fix that keeps one object and mutates it must also let that caller see the
  change (it is outside `netPoll` but shares the cost).
- `SetWalls` also re-plans from the model when a new wall blocks the remaining walk; that path must keep its result.

## Not established (tell the owner)

- That walls stood in the three bad windows of the lane's Web builds: no build recorded wall counts. The fit is arithmetic and a
  measured per-event cost, not an observation. Cheapest confirmation: the lane's recorder counts `SetWalls` rebuilds
  (`RebuildWalkMap` calls) per second and the pathfinder's `Searches`/table builds per second beside `netPoll`, in one more session.
- Whether Unity-side subscribers of `SnapshotReceived` / `CollectionsChanged` (inside `Poll`) also allocate: not measurable engine-free.
  If the cause is not walls the sub-sections of `netPoll` in `PerfMarks` remain the diagnostic to add.
- Two other library costs measured and NOT fixed here, both under budget: an actor count that changes on every snapshot costs a new
  array, about `40 B * actors` (1.7 KB at 40 fighters, 20 times a second); `Collections.Apply` rebuilds its rows on every State
  (440 B with one wall).
- The oracle's server is a simulation (it walks the same A* path, echoes sequences); real traffic carries effects, events and
  larger deltas, which this does not exercise.

## Notes from previous attempts
