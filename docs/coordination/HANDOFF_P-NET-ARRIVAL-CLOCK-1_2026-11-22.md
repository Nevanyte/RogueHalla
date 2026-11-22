# Hand-off: P-NET-ARRIVAL-CLOCK-1 (2026-11-22)

PACKET: P-NET-ARRIVAL-CLOCK-1: snapshots timed at socket arrival; a 4 s jitter window at any tick rate (client only)
BRANCH: agent/sonnet/P-NET-ARRIVAL-CLOCK-1 (bc32dadbb, plus this hand-off commit)
ORACLE: packet/P-NET-ARRIVAL-CLOCK-1 (ab66cd8ff; brief 40622c89a, stub 2e8ababee)
BASE: feature/unity-rework 83edb687f, not stacked.
SERVES: not in ORDER_OF_OPERATIONS: owner direction 2026-11-22 (the Sonnet audit he asked for, finding 4 in
`docs/backlog/BALANCE_LAB_PATCH.md`), run by the client-input lane at the coordinator's request.
LANE: client-input lane (worktree claims under session `lane-client-input`).

GATE:
- `scripts/gate.sh --quick --oracle packet/P-NET-ARRIVAL-CLOCK-1` → GATE PASS (quick), first attempt; Unity EditMode
  2000 passed, 0 failed; `dotnet test client-unity/RoguehallaNet.Tests` 2353 passed; `sync-net.sh --check` clean.
- No full gate run by the lane; the coordinator's merge gate is the one that counts.

ORACLE (before → after, `client-unity/RoguehallaNet.Tests/ArrivalClockTests.cs`):
- `JitterWindowCoversFourSecondsAt40Hz`: late arrivals 2.5 to 4 s ago at 40 Hz. Target delay 30 ms before (the 80-sample
  window held 2 s, none late) → ≥ 54 ms after (the 4 s window still holds them, as at 20 Hz).
- `JitterWindowAt20HzIsUnchanged`: control, green before and after.
- `SnapshotsAreTimedAtSocketArrivalNotAtTheFrameDrain`: regular 40 Hz arrivals drained at 60 fps. Target delay 8.33 ms
  before (drain noise read as jitter) → the 5 ms floor after.

CHANGED: `client-unity/RoguehallaNet/Net/Session.cs` (`IArrivalTimedTransport`; `OnReceived` gives the clock
`now - clamp(age, 0, 1000)`; `Motion` keeps the drain-time `now`), `client-unity/RoguehallaNet/Net/RenderClock.cs`
(`WindowMs = 4000`, capacity `max(8, round(WindowMs / TickMs))`, reallocated only on a capacity change), their synced
copies, `UnityProject/Assets/Plugins/WebGL/RhWebSocket.jslib` (queue `{d, t: performance.now()}`, new `RhWs_NextAge`),
`UnityProject/Assets/Scripts/Net/WebGLTransport.cs` (implements the interface; age read before the pop),
`UnityProject/Assets/Scripts/Net/NativeTransport.cs` (Stopwatch stamp on the receive thread). No existing test changed.

WHAT CHANGES FOR A PLAYER: on a calm link, remote players, mobs and projectiles are drawn up to one display frame closer
to the present (the frame-drain noise is no longer counted as network jitter). At the live 40 Hz tick a late-snapshot
pattern is remembered for 4 s, as designed for 20 Hz, instead of 2 s. Nothing on the wire changes.

D-RULES: D1 loose proof (the oracle) · D2 n/a · D5/D6 none · D9 unchanged (`LocalMotion` and `Motion` timing untouched).

REVIEW (reviewer, Sonnet): LAND, no notes.
Minor, not notes:
- The 1000 ms age clamp could in theory place a very old frame's arrival after a newer one; needs frames older than 1 s,
  where the clock resyncs or snaps anyway.
- `using System.Diagnostics;` in `NativeTransport.cs` is out of alphabetical order.
- The `RenderClock` comment says the arrays are reallocated "only when the tick changed"; it also happens on the first
  snapshot after construction at a non-20 Hz tick (harmless: the history is empty then).

PINNED VALUES TO RE-TAKE: none.
DEVIATIONS: none (legacy measured arrival in its WebSocket handler too).
SHARED RECORDS: none beyond the coordinator's own backlog row and MIGRATION checkpoint entry.
MERGE ORDER: P-UI-INPUT-CUES-1 also edits `Session.cs` (only `FlushInput` and the event list); the lane checks that it
merges cleanly with this branch before handing it over. Either order works.
NOT ESTABLISHED: the jslib and the WebGL branch of `WebGLTransport` were not run in a browser (the gate cannot run a
WebGL player); `NativeTransport`'s real receive path is not exercised by the tests; the delay saved in a real session was
not measured. A one-time browser proof would show the ages are small and non-negative.
RISKS: the EditMode run dirtied two StormInk assets (`ColiseumTorchArt.asset`, `ColiseumWeaponRackArt.asset`); the
implementer restored them and committed neither.
