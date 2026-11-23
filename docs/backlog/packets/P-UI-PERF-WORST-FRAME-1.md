# P-UI-PERF-WORST-FRAME-1: the recorder keeps the worst frame of a window and says if a collection fell in it

- **Serves:** not in the plan: owner direction 2026-11-22, chat of the UI rework lane ("make the front end as fluid as
  possible", "Please do a UI optimization sweep"). The lane's Web perf A/B of 2026-11-23 found single frames of 24 to
  64 ms in recorder section `actors` and 12 to 30 ms in `render`, old and new build alike; the records cannot say what
  they were.
- **Implementer tier:** implementer-sonnet
- **Reviewer:** reviewer
- **Integration base:** lane branch `lane/ui-rework/base-3` (stacked on the integration branch `feature/unity-rework`);
  gate with `--base lane/ui-rework/base-3`. Worktree of the lane: `/Users/nevinmckinnon/Documents/Projects/RH-ui-r`.
- **D-rules touched:** D1: measurement only; legacy (a DOM page) has no frame recorder and nothing a player sees
  changes (everything runs only with `?perf=1`), so no DEVIATIONS row. D2 n/a. D5/D6 none. D9 untouched: no rule, wire
  or `crates/`.

Coordinator's conditions (2026-11-23), cited as C1 to C5: write scope PerfMarks.cs, PerfRecorder.cs and
Tools/perf-matrix.cjs only; measurement only, nothing when the recorder is off [C1]; a fixed-size ring, no per-frame
allocation, proved by the meter [C2]; per window the worst frame's index, its section times, and whether the
collection count stepped in that frame or the one before [C3]; the same for the worst actors and render frames if they
are not the window's worst [C4]; settle the premise at `PerfMarks.cs:38`: record whether the count ever steps between
the frame bracket's start and end, and say what was found [C5]. C6 (coordinator's addition): each frame row also
carries the session's state.

## 1. ID and outcome

Today a recorder window holds a mean and ONE maximum per section (`PerfMarks.EndFrame`, `Presentation/PerfMarks.cs:52`;
`PerfRecorder.Snapshot`, `Presentation/PerfRecorder.cs:231`) and one `gcCollections` (:246): the frame that made a
maximum is lost, so a 64 ms `actors` frame cannot be set beside what `render` or the heap did in it, its neighbours, or
a collection. `PerfMarks.End` (:43) keeps heap growth (:47-48) only as a window sum. The comment at :38 asserts that a
GC never runs mid-frame on WebGL; nothing tests it. The driver (`Tools/perf-matrix.cjs`, `measure`, :153-181) turns the
page's rAF timestamps `data.t` into deltas (:173), hands them to `budget()` and drops them: `stats.maxMs` is the page's
worst interval, not which one. It stores the whole recorder object (`unity`, :172; `summarise` reads named fields
only), so new fields reach `records.json`. The page's interval is not the recorder's frame (5.3 to 12.2 ms off in six
of eight windows with a 12 ms frame, in both directions). `libil2cpp` (`gc/BoehmGC.cpp`; Boehm): `GetCollectionCount`
ignores its argument and returns `GC_get_gc_no()`, so `GC.CollectionCount(0)` counts every collection and, an icall
returning an int, allocates nothing managed. On the JavaScript target `IL2CPP_ENABLE_DEFERRED_GC` is set: `GC.Collect`
while the collector is disabled only sets a pending flag that `CollectALittle` ("called at top of each frame") runs;
that is shown only for an EXPLICIT collect. Where the engine runs that call relative to the first player-loop system
(the recorder's `OnFrameStart` is first in `Initialization`), and whether allocation-triggered or incremental steps are
deferred, is unknown (`ProjectSettings/ProjectSettings.asset:602`: `gcIncremental: 1`). The count rises when a cycle's
marking completes, not on an incremental slice. The game never calls `GC.Collect`.

When this lands, a perf window (`?perf=1`) keeps, per window, the worst `frame`, `actors` and `render` frames as full
rows (every section's ms and heap growth, five collection-count reads, the session's context) with the frame before and
after, counts collections by phase, and publishes it as a `worstFrames` member of the `Snapshot` JSON. The driver prints
it beside the page's own worst interval, matched by time, and a clock check. Nothing a player sees changes.

## 2. Dependencies

The base `lane/ui-rework/base-3` carries `AllocationMeter` (`Assets/Tests/Editor/AllocationMeter.cs`, packets
P-UI-ALLOC-METER-1 and -2). No other packet.

## 3. Relevant legacy paths

None: the legacy game is a DOM page with no frame recorder (verified: no `perf` recorder in `packages/`).

## 4. New paths

- `client-unity/UnityProject/Assets/Tests/Editor/PerfWorstFrameEditModeTests.cs` (+ `.meta`): oracle.
- `client-unity/UnityProject/Tools/perf-matrix.test.cjs`: oracle.
- This packet.

## 5. Exclusive write scope

- `client-unity/UnityProject/Assets/Scripts/Presentation/PerfMarks.cs` and `PerfRecorder.cs` (the Outcome below);
- `client-unity/UnityProject/Tools/perf-matrix.cjs` (`measure`'s record and print, the functions, the exports).
- Reading public state of other classes from `PerfRecorder.cs` is inside the scope; changing any other file is not.
- Not in scope: `Assets/Plugins/WebGL/RhPerf.jslib`, `View/ArenaView.cs`, every presenter and HUD file,
  `Tools/perf-run.cjs`, `client-unity/RoguehallaNet`. No existing test names `PerfMarks`, `PerfRecorder` or
  `perf-matrix` (grepped by the oracle author, whole tree): no `oracle-amend:` commit.
- Existing users of the changed API (compile only; none may need an edit): `MatchHud.cs`, `MatchHud.Features.cs`,
  `MatchClient.cs` (:309), `ArenaView.cs`, `ArenaView.Camera.cs`, `RegionAtmosphere.cs`, `ShowcaseSoundscape.cs`,
  `ShowcaseSettings.cs` call only `PerfMarks.Begin` and `PerfMarks.End`/`Sec`, which keep their signatures.
  `perf-run.cjs`, `adaptive-check.cjs` (reads `adaptive` only; imports `readyPoint`, which stays exported) and
  `webgl-pass-capture.js` do not read the new members.

## 6. Contracts and references

R-AUTH (Unity holds no rules; this is measurement only). No wire or protocol change. Motion/readability rules of
`.agent-runs/lane-ui-rework/specs/COMMON.md`: new non-test C# files need a `/// <summary>` (no new product file here);
both product files stay far below 800 lines.

## 7. Behaviour to preserve

Existing `PerfMarks` members keep names and meaning (`On`, `Mark`, `Begin`, `End`, `Frames`, `MeanMs`, `MaxMs`, `Calls`,
`AllocPerFrame`, `Names`, `Sec`); `Snapshot`'s existing members keep names and meaning (it adds `gcIncremental` and
`worstFrames`). `perf-matrix.cjs`: `unity`, `stats` and the five old exports (`budget`, `summarise`, `readyPoint`,
`minimapRect`, `legacyScale`) stay. With the recorder off nothing is allocated and no reader throws.

## Outcome (the design, all of it)

All of it runs only while `PerfMarks.On` is true, which only `PerfRecorder.Hook` sets (`?perf=1`) (C1). The recorder's
callbacks exist only after `Hook`, and every new block in them begins with the test that the recorder is on. The fixed
rows (about 5 KB) are created at the first `Reset` (`Hook` calls it; so do tests; `Add` or `EndFrame` create them if
`On` was set without one), so with the recorder off nothing is allocated at all, and every reader is null-safe until
then. While on, no per-frame allocation (C2): no boxing, closure, enumerator over an interface, `ValueType.GetHashCode`
on a struct, or string. No read the recorder makes may throw: a throw would skip the frame's end.

1. **Frame rows.** At each `EndFrame` a row is formed: `Index` (0 for the first frame after a reset), `Seconds` (given
   by the caller: window start to the frame's end), `Ms[Sec.Count]`, `Bytes[Sec.Count]` (the positive heap growth
   `_alloc` sums, kept per frame in a new `_frameAlloc`), `Gc[5]` (the collection count at the frame bracket's start,
   the start of scripts, the end of scripts, the start of render, the frame bracket's end) and `Ctx`, a copy of the
   frame's `FrameContext`. Rows are fixed arrays created once; two are held (slots copy rows).
2. **FrameContext (C6)**: ten plain ints the recorder fills and tests feed (API below); the five flags (the three
   change flags, `Dead`, `OrderPending`) are 0 or 1.
3. **Three slots (C3, C4)** for the sections `Frame`, `Actors`, `Render`, each with copies of a `before`, `at` and
   `after` row and its section's value at `at`. At `EndFrame` of frame k, per slot, in this order: (a) the slot is empty
   or the frame's ms in its section is STRICTLY larger than the slot's value: `at` = row k, `before` = row k-1 (none
   when k = 0), `after` cleared and waiting (the first frame fills an empty slot at any value, even 0); (b) else a
   waiting slot takes row k as `after`. A tie keeps the earlier frame; the newest frame has no `after`; slots may hold
   the same frame. Worked, values 10, 20, 20, 15, 30: `at` is 0, then 1 (`before`=0), still 1 with `after`=2, still 1,
   then 4 (`before`=3, no `after`). On the Web player `Reset` and `Snapshot` come from page JavaScript, between frames.
4. **Window counters (C5, C6).** Collections: with `d(a,b) = max(0, b-a)`, per `EndFrame`, all skipped when the row's
   `FrameStart` read is -1 (`OnFrameStart` did not run for that frame, as in the one where `Hook` ran; its `FrameEnd`
   still counts as the next frame's previous end): `collectionsInFrames += d(FrameStart, FrameEnd)`;
   `framesWithCollection` +1 when that is above 0; four phase counters +1 when their span stepped:
   `framesStepBeforeScripts` (FrameStart to ScriptsStart), `framesStepInScripts` (ScriptsStart to ScriptsEnd),
   `framesStepBetweenScriptsAndRender` (ScriptsEnd to RenderStart), `framesStepInRender` (RenderStart to FrameEnd); a
   frame that stepped in two phases adds to both. `collectionsBetweenFrames += d(previous FrameEnd, this FrameStart)`
   once a previous end of at least 0 is known: after `Reset(n)` it is `n`, after `Reset()` the first frame has none.
   Context, over every recorded frame: `wallsMin`, `wallsMax` (0, 0 with no frame), `stateFrames` and `snapshots`
   (sums), `mapTiles` (the last frame's), and `framesWallsChanged`, `framesWalkMapChanged`,
   `framesTerrainMapChanged`, `framesDead`, `framesOrderPending`, each the number of frames whose flag is 1.
5. **The premise (C5).** The counters settle only this: no collection COMPLETED inside a bracket, when
   `framesStepInScripts`, `...BetweenScriptsAndRender` and `framesStepInRender` are 0. A step in the first phase is
   ambiguous (it may be the engine's top-of-frame call): read it apart from the other three. An unchanged count says
   nothing about an incremental slice, so `Snapshot` also publishes `gcIncremental`
   (`UnityEngine.Scripting.GarbageCollector.isIncremental`, present in 6000.0.85f2). The comment at `PerfMarks.cs:38`
   says only: bytes are heap growth in whole collector blocks, a lower bound (a collection or lazy sweeping lowers it).
   C5's "say what you found" is met by the lane's report of the Web run after the merge, not by this packet.
6. **Seam (C2).** `End` computes its two numbers as today, then calls `Add(s, ms, grewBytes)`, which does the rest of
   what `End` did (and `_frameAlloc`). `PerfMarks` stays static and engine-free.
7. **JSON.** `AppendWorstFrames(StringBuilder sb)` appends exactly one object member, no leading or trailing comma,
   numbers by `CultureInfo.InvariantCulture` (ms and seconds `"0.###"`, bytes and counts `"0"`):
   `"worstFrames":{"sections":[<Names>],"slots":{"frame":SLOT,"actors":SLOT,"render":SLOT},"gc":{<the 7 collection
   counters>},"context":{<the 10 context counters>}}`. SLOT = `{"section":"actors","maxMs":64.4,"before":ROW,"at":ROW,
   "after":ROW}`; ROW = `null` or `{"i":1523,"t":7.512,"ms":[26 numbers],"bytes":[26 numbers],"gc":[5 integers],
   "ctx":{"actors":31,"walls":2,...}}` (the ten `FrameContext` fields, camelCase: `actors, walls, wallsChanged,
   walkMapChanged, terrainMapChanged, stateFrames, snapshots, dead, orderPending, mapTiles`). The `gc` object holds
   `collectionsInFrames, framesWithCollection, framesStepBeforeScripts, framesStepInScripts,
   framesStepBetweenScriptsAndRender, framesStepInRender, collectionsBetweenFrames`; the `context` object holds `wallsMin,
   wallsMax, stateFrames, snapshots, mapTiles, framesWallsChanged, framesWalkMapChanged, framesTerrainMapChanged,
   framesDead, framesOrderPending`. `maxMs` is the slot's value and equals the existing `cpu.<section>.max`; with no
   frame (also before the first `Reset`) `at`, `before`, `after` are `null`, `maxMs` 0. Existing members keep names and
   meaning; `Snapshot` adds `"gcIncremental":true|false` beside `gcCollections`.
8. **Recorder wiring** (`PerfRecorder.cs`, about 100 added lines).
   - Gc reads: `PerfMarks.GcReads _gc`, every field -1 at first; `System.GC.CollectionCount(0)`, each read after the On
     test. `OnFrameStart` reads before `Begin`; `OnScriptsStart` after the `PreUpdate` `End`, before `Begin`;
     `OnScriptsEnd` after its `End`; `OnRenderStart` before `Begin`; `OnFrameEnd` after both `End` calls. No read lies
     inside the bracket it delimits; the three between phases add a few ns to `frame` and to no other section.
     `OnFrameEnd` ends by setting `_gc.FrameStart = -1`. `ResetCounters` sets `_gcBase` first, then
     `PerfMarks.Reset(_gcBase)`.
   - Context, read in `OnFrameEnd` after the frame bracket closed, before `PerfMarks.EndFrame(_gc, ctx,
     Time.realtimeSinceStartup - _windowStart)`: no section is charged for it. The recorder holds a `MatchClient` as it
     holds the `ArenaView` (`_client`, found by `FindFirstObjectByType<MatchClient>()` in the existing 0.25 s poll of
     `Update` while the Unity-null test fails; never per frame). Per frame `Session s = _client != null ?
     _client.Session : null`, so a rebind is followed. `Actors` is `_arena.ObservedActorCount` (0 without an arena;
     `ArenaView.cs:121`).
   - NULL-SAFE: no context read can throw, so the frame's end always runs. The null cases: no `_client`, or `s` null
     (`MatchClient.CloseCurrentSession` sets `Session = null`, `Net/MatchClient.cs:184`): every field but `Actors` is 0.
     A session before its Welcome (it exists from creation; `Welcome` is set at `RoguehallaNet/Net/Session.cs:741`,
     `Motion` at :763): `s.TerrainMap` is null (`Session.cs:91`: `Collections.TerrainMap ?? Map`, `Map` null without
     `Welcome`), so `MapTiles` is 0, and `s.Motion` and `s.Me` are null. `Collections` is a readonly field
     (`Session.cs:166`), never null; its `Fields` defaults to empty (read null as empty). The three change flags compare
     references only (`ReferenceEquals`, no member read on a null): null to null is same, null to non-null is changed.
   - SESSION BINDING: the recorder holds the session it subscribed to. `s` null is a rebind to none: unsubscribe from
     the held session, drop the reference, take no baselines. An `s` that is not the held session (none to a session
     included) is first sight: unsubscribe from the held one, subscribe (only when `_instance == this`), hold it, and
     take every baseline again: `SnapshotsReceived`, the State-frame count (set to 0), the walls signature, the walk
     map and the terrain map. In that frame the three change flags, `StateFrames` and `Snapshots` read 0. The first
     frame after `Hook` is a first sight. `OnDestroy` unsubscribes.
   - `Walls`: rows of `s.Collections.Fields` (an `IReadOnlyList<AreaView>`, read by index) with `Kind == AreaKind.Wall`
     and `Tiles != null`: the rows `LocalMotion.SetWalls` takes (`RoguehallaNet/Net/LocalMotion.Walls.cs:19-33`).
     `WallsChanged`: a `long` signature over them differs from last frame's; per row `h = h * 31 +
     BitConverter.DoubleToInt64Bits(Key.StartedAt)`, then `+ Key.FirstTile`, then `+ Tiles.Length`, unchecked (plain
     `AreaKey` fields, `Net/Collections.cs:78`, no `GetHashCode`). `WalkMapChanged`: `s.Motion.WalkMap`
     (`LocalMotion.Walls.cs:65`) is not the same object as last frame's (null without `Motion`). `TerrainMapChanged`:
     `s.TerrainMap` likewise. `MapTiles`: `TerrainMap.W * TerrainMap.H` (`ushort`; 0 when `s.TerrainMap` is null). The
     last signature and objects persist across windows.
   - `Dead`: `s.Me != null && s.Me.Status == ActorStatus.Dead` (`Me` is null before the first snapshot and while
     spectating; `Session.cs:116`). `OrderPending`: `s.Motion != null && s.Motion.OrderPending`
     (`LocalMotion.cs:196`).
   - `Session` exposes `SnapshotsReceived` (`Session.cs:152`) and no count of State frames (`Collections.Tick` is a
     tick). `Snapshots` is the change in `s.SnapshotsReceived` since the last read: the count the session exposes, which
     includes a stale snapshot the session did not apply (`Session.cs:508`, :591); the exact applied count is not
     wanted. `StateFrames` comes from a static handler `OnCollections(CollectionsView)` on `s.CollectionsChanged`
     (`Session.cs:179`, raised at :579) that adds to a static int, held in a static readonly delegate field so
     subscribing allocates once; its body begins with the On test. It runs inside the `netPoll` section
     (`Session.Poll`, `MatchClient.cs:309`): a few ns, no allocation.
   - `Snapshot` uses `StringBuilder(12288)` and closes with `},` + `PerfMarks.AppendWorstFrames(sb)` + `}`.
   - Left out, for the coordinator: a non-empty predicted walk path. `LocalMotion` has no public member for it (`_path`,
     `_pathIndex` are private); a `public bool` there changes `client-unity/RoguehallaNet`, outside scope.
9. **Driver** (`perf-matrix.cjs`; the new functions are pure and exported).
   - TIME ALIGNMENT. `measure`'s first page call begins by setting `window.__rhPerf.unity = null` (a stale object of an
     earlier window can never be matched) and returns `performance.now()`, read right after
     `window.__rhPerf.reset()`, stored as `resetAt`. `pageIntervalAt(stamps, resetAt, seconds)` returns `null` or
     `{index, ms, startSeconds, endSeconds}` for the interval `i` (`stamps[i]` to `stamps[i+1]`) holding `seconds` (a
     row's `t`): with `start = (stamps[i]-resetAt)/1000` and `end` likewise for `i+1` (no multiplication), `start <
     seconds <= end`; a boundary belongs to the interval that ends there; a time at or before the first stamp, after
     the last, or non-finite, or fewer than 2 stamps, give `null`; an interval with a non-finite end is never chosen;
     `ms = stamps[i+1]-stamps[i]`. `slotPageIntervals(unity, stamps, resetAt)` gives `{frame, actors, render}`. The
     row's `t` is the frame's END by the recorder's clock; a page stamp is a frame START; the lane's records show the
     two differ by 5 to 12 ms. So each slot reports the LONGEST page interval that overlaps the frame's own span
     `[at.t - at.ms[Frame]/1000, at.t]`: interval `i` overlaps when its `start` is below the span's end and its `end`
     above the span's start (touching does not count); finite ends only; a tie keeps the earlier; the result has the
     shape of `pageIntervalAt`'s. A frame ms that is not finite or not above 0 makes the span the instant `at.t`, and
     the result is `pageIntervalAt`'s. `null` where `at` is null, `at.t` is not finite, no interval overlaps, or `unity`
     has no `worstFrames`. The window record gets it as `pageByTime`. A collection between frames shows as a short
     recorder frame inside a long page interval.
   - `worstPageFrame(stamps, resetAt)` gives `null` below 2 stamps or with no finite delta, else `{deltaIndex, ms,
     endSeconds, beforeMs, afterMs}`: `deltaIndex` indexes the deltas list `budget` receives; `ms` is the largest finite
     delta, a tie keeping the earlier (`=== budget(deltas).maxMs`); `endSeconds = (stamps[deltaIndex+1] - resetAt) /
     1000`, `null` if `resetAt` is not finite; `beforeMs`, `afterMs` the up to two deltas each side by position, in time
     order, a non-finite delta kept in its place as `null` (printed `n/a`). The record gets `pageWorst:
     worstPageFrame(data.t, resetAt)` beside `stats`.
   - CLOCK CHECK. The page call that sends `Snapshot` also returns `performance.now()`, read right after that
     `SendMessage`. The record gets `clockCheck: {pageSeconds: (now - resetAt) / 1000, unitySeconds: unity.seconds}`
     (`null` without `unity`). `clockCheckLine(cc)` returns `clock check: page minus recorder +100.0 ms` (signed, one
     decimal) only when the two differ by more than 5 ms, else `null` (also for `null` or a non-finite value).
   - `worstFrameLines(unity, pageByTime)` returns the lines to print (the slot lines only); `describeWorstFrame(unity,
     slot, page)` makes one (`slot` is `'frame'`, `'actors'` or `'render'`, `page` that slot's `pageByTime` entry,
     whose `ms` is printed, or null); `describePageWorst(pw)` is the page line. `measure` prints them after the window's
     line, indented six spaces: the `frame` slot's, then `actors` and `render` only when their `at.i` differs from it,
     then the page line, then the clock line when it is not `null`. `unity`, `stats` and every old export stay; the
     seven new functions are exported: `worstPageFrame, pageIntervalAt, slotPageIntervals, clockCheckLine,
     describeWorstFrame, worstFrameLines, describePageWorst`.
10. So a long actors frame comes with every section's ms, its heap growth (a lower bound), its two neighbours, the
    collection reads and the context; not the call inside actor presentation (Not established).

Line formats (exact; ms one decimal, `t` two decimals, `kB = Math.round(bytes/1024)`; one line, wrapped here):
`worst frame #1523 at 7.51 s: 69.4 ms; actors 54.8 ms 1953 kB, render 8.0 ms 0 kB, preUpdate 1.6 ms 0 kB; gc in
frame +0, just before +1; actors 31 (before 30); walls 2 same; walk map same; terrain same; state frames 1; snapshots
2; dead no; order pending yes; page interval 57.2 ms by time`. The label is `worst <slot> frame` for slots other than
`frame`; the length is the row's `frame` ms. The sections are the up to three largest row ms above 0, descending, ties
by section order, after skipping `frame` and the containers `scripts`, `present`, `hud` and `camera`; `render`,
`uiRebuild`, `netPoll`, `actors` and the other rows stay (none left: `no section time`). They may overlap (Traps).
`gc in frame` = `Gc[4]-Gc[0]` of `at`; `just before` = `Gc[0]` of `at` minus `Gc[0]` of `before` (the frame before or
the gap before); both `n/a` when `Gc[0]` is -1, `just before` also without `before`. `actors N (before M)` takes
`ctx.actors` of `at` and `before` (`n/a` without). Then `walls N same|changed`, `walk map` and `terrain`
`same|changed`, `dead yes|no`, `order pending yes|no`, then `page interval X ms by time`, or exactly `page interval
n/a` when `page` is null. Page line: `page worst interval #1980 ends 8.05 s after the window began: 57.2 ms (before 4.2,
4.1; after 4.0, 4.3)`, a `null` delta printing `n/a`; for a `null` argument (no usable stamps) exactly `page worst
interval none`, for `undefined` (an old record) `null`. The functions that take `unity` give `null` or `[]` for an old
build's `unity`, `null`, `{error}`.

### New public API (`Presentation/PerfMarks.cs`; a member a test touches is `public`: no InternalsVisibleTo)

```csharp
public struct GcReads { public int FrameStart, ScriptsStart, ScriptsEnd, RenderStart, FrameEnd; }
public struct FrameContext { public int Actors, Walls, WallsChanged, WalkMapChanged, TerrainMapChanged,
    StateFrames, Snapshots, Dead, OrderPending, MapTiles; }
public static void Add(Sec s, double ms, long grewBytes);          // no-op while !On
public static void EndFrame(GcReads gc, FrameContext ctx, double seconds);   // replaces EndFrame(); no-op while !On
public static void Reset();                      // = Reset(-1): no previous end known
public static void Reset(int gcCount);           // clears window, rows, slots, counters, index
public static void AppendWorstFrames(System.Text.StringBuilder sb);
```

The stub commit keeps the old `EndFrame()` (so `PerfRecorder` compiles) and gives `Reset(int)` the old `Reset()` body;
the implementer deletes the old `EndFrame()`, makes `Reset()` = `Reset(-1)` and wires the recorder.

## 8. Acceptance checks

- `scripts/gate.sh --quick --oracle packet/P-UI-PERF-WORST-FRAME-1 --base lane/ui-rework/base-3` passes.
- `node --test client-unity/UnityProject/Tools/perf-matrix.test.cjs` green (the gate's step "Unity tool tests (node
  --test)" runs it when something under `Tools/` changed).
- `PerfWorstFrameEditModeTests` green in Unity (`unity-class.sh <worktree> PerfWorstFrameEditModeTests`).
- The oracle author did NOT run the whole suite or the gate.

## Oracle files (read-only for implementers)

- `client-unity/UnityProject/Assets/Tests/Editor/PerfWorstFrameEditModeTests.cs`
- `client-unity/UnityProject/Assets/Tests/Editor/PerfWorstFrameEditModeTests.cs.meta`
- `client-unity/UnityProject/Tools/perf-matrix.test.cjs`
- `docs/backlog/packets/P-UI-PERF-WORST-FRAME-1.md`

## 9. Validation commands

- `.agent-runs/lane-ui-rework/bin/in.sh <worktree> node --test client-unity/UnityProject/Tools/perf-matrix.test.cjs`
- `.agent-runs/lane-ui-rework/bin/unity-class.sh <worktree> PerfWorstFrameEditModeTests`
- `.agent-runs/lane-ui-rework/bin/in.sh <worktree> scripts/gate.sh --quick --oracle packet/P-UI-PERF-WORST-FRAME-1 --base lane/ui-rework/base-3`

## 10. Risk and suggested model tier

implementer-sonnet; reviewer. Hard parts: the slot rules and the per-phase counters (all pinned by tests), the
no-allocation steady frame (the meter test), and the recorder wiring, which NO test reaches (it needs a PlayerLoop, a
live `MatchClient` or the Web player): read it twice, keep every read null-safe. Reviewer: check the wiring against
section 8 line by line.

## 11. Known traps

- `"collections"` (`Sec.Collections`) is ArenaView's section (`ArenaView.cs:629`), not the GC: no new key uses it.
- The between-scripts-and-render phase is near-empty by construction; keep it, a nonzero count is news.
- Nested sections repeat bytes as they repeat ms (`scripts` contains `present` contains `actors`; `hud` the `hud*`):
  that is why the printed line skips the containers `frame`, `scripts`, `present`, `hud` and `camera`. The printed
  sections are the three largest rows after the skips and may overlap: `render` contains `uiRebuild`, `netPoll`
  contains `fxCues` (`Session` raises `FxReceived` inside `Poll`, `RoguehallaNet/Net/Session.cs:653`) and `camera`
  contains `atmosphere` (`View/ArenaView.Camera.cs:44-46`, inside `Follow`). A printed pair is not a partition and not
  additive; say so in a code comment at the printing function.
- `Roguehalla.Net` also has an `ArenaView` struct; `MatchClient.cs` has the same `using Roguehalla.Net;` and still
  resolves the name to the `MonoBehaviour`, as `PerfRecorder` must.
- `perf-matrix.cjs` guards `main()` with `require.main` and loads Playwright inside `session` only: requiring it starts
  nothing. The consumers of `window.__rhPerf.unity` are `perf-matrix.cjs` and `adaptive-check.cjs`; both read named
  fields: new members are safe.
- JsonUtility (used by the tests) turns `null` into a default object: the tests count `"before":null` on the text.
- Not established (do not claim it in code comments): which call inside actor presentation the hitch is (needs marks
  in `ArenaView.cs`, `SpriteActorPresenter*.cs`, outside scope); whether a collection completes inside a bracket on the
  Web player; that the recorder wiring is right (no test reaches it); that the page's interval for a frame is the right
  one (`pageByTime` is a match by overlap, never an identity; `clockCheck` shows a gap above 5 ms and corrects nothing;
  `Snapshots` counts snapshots received, not applied); that `Time.realtimeSinceStartup` is not latched per frame on the
  Web player; the cost of the `Fields` loop under IL2CPP.

## Existing tests that pin this code

None. Searched the whole tree for `PerfMarks`, `PerfRecorder` and `perf-matrix` (files: `perf-run.cjs`,
`adaptive-check.cjs`, `webgl-pass-capture.js`, `RhPerf.jslib`, `link.xml`, the product callers listed in section 5 and
some docs): no test file mentions them. `Tools/perf-run.test.cjs` tests `perf-run.cjs` only; `perf-matrix.cjs` gets no
test before this packet. `Tools/hud-seams.test.cjs` does not name them. No `oracle-amend:` commit. The new node test
carries one standing guard that is green on the base: "the five old exports exist".

## Notes from the oracle author

What the oracle holds: `PerfWorstFrameEditModeTests` (18 tests, JsonUtility DTOs; every `Assert` line reads a helper that
returns a sentinel instead of throwing when a row is missing) and `perf-matrix.test.cjs` (21 tests, the standing guard
included). `AllocationMeter.AssertNone` is used for the no-garbage test.

**Red state on the stub (commit `stub:`), checked by running them.** EditMode: all 18 red, each on an assertion about
rows, JSON text or counters (examples: `frame.at is a row of 26 sections` Expected True But was False;
`sections are PerfMarks.Names` actual is an empty array; `Calls after frame 1` Expected 2 But was 0;
`wallsMax` Expected 3 But was 0; `three frames before the reset` Expected 3 But was 0; for
`ASteadyFrameAllocatesNothing` the first assertion, `Frames` Expected 1 But was 0). Node: 20 red on assertions, the one
standing guard ("the five old exports exist") green; no test is red on a TypeError (a property read on a result goes
through a null-safe `P(...)`).

**Scratch runs (not committed).** (1) EditMode, on the stub, with a catch-and-collect copy of the class (`Assert.` replaced
by a collector, the meter wrapped, a sample document of the exact JSON shape parsed into the DTOs): every collected entry was an
assertion about missing behaviour except one defect, `EachSlotKeepsItsWorstFrameWithNeighbours` read `.at.t` on a null row
(NullReferenceException): fixed with a null-safe `RowT`. The sample document parsed (JsonUtility reads `double[]`, the
6-deep nesting and `null` rows; the de-DE culture constructs). On the stub the meter's own control checks passed with
`Begin`/`End` (so `Stopwatch.GetTimestamp` and `GC.GetTotalMemory(false)` read no sample: test 8's second delegate is
sound). (2) A throwaway reference implementation of `PerfMarks` (the section 8 rules for the marks, rows, slots, counters
and JSON, about 120 lines; NOT committed, `git checkout` restored the stub) made all 18 EditMode tests green, the meter
tests included; a throwaway reference of the seven node functions made all 21 node tests green. So every expectation is
reachable and consistent with this packet's rules. The recorder wiring (section 8, `PerfRecorder.cs`) is NOT covered by
any test.

**Where the oracle differs from the lane spec.**
1. `ExistingStatisticsKeepTheirMeaning`: the spec says `MeanMs` 8 after the second frame. `MeanMs` is the window sum over
   `Frames` (`PerfMarks.cs:69`), so after frames of 8 and 4 ms it is `(8 + 4) / 2 = 6`. The test asserts 6 (and `Frames` 2).
   Calls 3, `MaxMs` 8 and `AllocPerFrame` 100 are as in the spec.
2. `TheJsonHoldsTheNamedFields` and its de-DE half are two tests (`TheJsonIsTheSameUnderAGermanCulture`); same checks.
3. Extra tests, each a rule the spec states in prose: `AFirstFrameOfZeroStillFillsTheSlots`,
   `WithNoFrameEveryRowIsNullAndTheMaximumIsZero` (spec test 2's last sentence), `TheFrameEndOfASkippedFrameStillCountsAsThePreviousEnd`
   (Outcome 4's "its FrameEnd still counts"). `NothingIsRecordedWhileOff` also asserts `Calls` 1, `AllocPerFrame` 0 and the
   collection counters 0.
4. Node: extra cases for rules in prose: a `resetAt` that is not finite gives `endSeconds` null; the frame-ms-not-above-0 /
   non-finite / negative span rule of `slotPageIntervals` (the span becomes the instant, a boundary goes to the interval
   that ends there); a section list with ties, the container skips and the three-section limit; `just before`/`gc in frame`
   `n/a` for a -1 start read; `no section time`; the `actors`/`render` label and own row; two lines when one slot differs;
   the slot independence of `slotPageIntervals`.
5. The stub gives `Reset(int)` the old `Reset()` body (the stub cannot make `Reset()` call `Reset(-1)` without recursion);
   the old `EndFrame()` stays in the stub only so `PerfRecorder` compiles.

**Not tested (so the reviewer reads it):** `PerfRecorder.cs` wiring (gc reads' positions, context reads, session binding,
`gcIncremental`, the handler), `Snapshot`'s JSON assembly with `AppendWorstFrames`, and `measure`'s calls in
`perf-matrix.cjs` (alignment call, `pageByTime`, `pageWorst`, `clockCheck`, the printing). The scratch references above were
not kept.

**Existing test classes run on the stub:** none pins this code (see the section above), so none was run.

## Notes from previous attempts
