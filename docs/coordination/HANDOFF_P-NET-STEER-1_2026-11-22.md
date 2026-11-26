# Hand-off: P-NET-STEER-1 (2026-11-22)

PACKET: P-NET-STEER-1: keyboard steering and aim stop spending the server's order and frame budgets (client only)
BRANCH: agent/sonnet/P-NET-STEER-1 (3534bba5e, plus this hand-off commit)
ORACLE: packet/P-NET-STEER-1 (9e92bfa7c; brief 1b9c5bfd2, stub 6ef16d747, oracle 1cdf3b3c7, oracle-amend 9e92bfa7c)
BASE: feature/unity-rework 83edb687f, not stacked. No file in common with P-NET-ARRIVAL-CLOCK-1 or P-UI-INPUT-CUES-1.
SERVES: not in ORDER_OF_OPERATIONS: owner direction 2026-11-22 (the Sonnet audit he asked for, finding 1 in
`docs/backlog/BALANCE_LAB_PATCH.md`), run by the client-input lane at the coordinator's request.
LANE: client-input lane (worktree claims under session `lane-client-input`).

GATE:
- `scripts/gate.sh --quick --oracle packet/P-NET-STEER-1` → GATE PASS (quick) on attempt 2/2; C# 2355/2355; Unity
  EditMode 2000/2000; `sync-net.sh --check` clean.
- Attempt 1 was red in `client-unity/UnityProject/Assets/Tests/Editor/PresentationTimelineEditModeTests.cs:117`, which
  sets `MatchClient._lastDir` by reflection: the implementer had removed the field (the brief said to). It is kept, written
  on each send (see Minor).
- No full gate run by the lane; the coordinator's merge gate is the one that counts.

ORACLE (`client-unity/RoguehallaNet.Tests/SteerPacingTests.cs`; 30 s keyboard kite, 8 headings each held 600 ms, 4
clicks/s = Attack + Release, walker at 170 u/s, every frame's commands through a copy of the server's order budget):

| | before (today's 100 ms repeat) | after |
|---|---|---|
| Orders per second | 18.0 | 13.0 |
| Orders the server drops in 30 s | 62, the first at 9.5 s | 0 |
| Peak WebSocket frames in 1 s, 60 / 144 / 240 fps | 39 / 49 / 55 | 37 / 40 / 45 |

Plus a rooted walker: 20 Moves in 2 s before, at most 5 after (safety repeat only); a key change still sends on its frame.
Oracle amendment (lane, before the implementer started): the peak-frames bound moved 45 → 48, because the brief's own
design peaked at exactly 45 at 240 fps in the oracle author's check; 48 keeps today's 49 and 55 red and leaves room under
the server's 60/s for pings and pongs.

CHANGED: `client-unity/RoguehallaNet/Net/OrderBudget.cs` (`SteerDecision`, `SteerPacer`),
`client-unity/RoguehallaNet/Net/InputSender.cs` (frame meter: a 64-entry ring; an aim-only frame waits 50 ms instead of
25 ms once 36 frames left in the last second), their synced copies,
`client-unity/UnityProject/Assets/Scripts/Net/MatchClient.cs` (steering block uses the pacer: a Turn always goes, a
Refresh asks the client budget; the pacer is reset at all six places steering stops; `SteerRepeatMs`, `_lastSteer`
removed). No existing test changed.

WHAT CHANGES FOR A PLAYER: holding a direction re-sends its order about 5 times a second instead of 10, so kiting while
attacking no longer loses attacks, casts or locks to the server's order budget (15/s). Key presses, direction changes and
releases still leave on their frame. When many frames have gone out in the last second, an aim-only update waits 50 ms
instead of 25 ms; aim riding with any other command is unchanged. Server constants are unchanged.

D-RULES: D1 loose proof (the kite oracle) · D2 n/a · D5/D6 none · D9 unchanged (no prediction change; the server plans
every order).

REVIEW (reviewer, Sonnet): LAND, no notes. The reviewer checked that the >20° heading Turn (which bypasses the client
budget) cannot bring back 10/s in a realistic case: after a refused Refresh it fires once the walker passes the old goal,
about one send per 0.25 to 0.4 s; an 8 u wall slide could reach 10/s, no worse than today.
Minor, not notes:
- `MatchClient.cs:37` `_lastDir` is write-only state kept for the reflection test; its assertion at
  `PresentationTimelineEditModeTests.cs:140` is now vacuous as a steering check (`_steering` at `:139` still proves that
  steering survives a clock cut). A later cleanup can drop the field and the two test lines.
- `InputSender.FramesInWindow` scans up to 64 entries on each Flush with aim pending (per render frame).
- No unit test covers the refused-Refresh path (the budget-bypassing Turn after an overshoot): correct by reading.
- The 24 u margin and the 500 ms safety repeat rest on the walk prediction being close; feel is not measured.

PINNED VALUES TO RE-TAKE: none.
DEVIATIONS (proposed row, not applied; the owner signs): "Keyboard steering re-sends its order on goal consumption (the
last goal within 24 u), a clear-goal heading change over 20°, or a 500 ms safety repeat, instead of every 100 ms (legacy
`packages/client/src/net/keyboardMovement.ts:21` `STEER_REPEAT_MS`); aim-only frames stand back from 25 ms to 50 ms once
36 frames left in the last second (legacy `uplinkPace.ts` had a similar stand-back). Client pacing only: same goals, same
server rules. Reversible: restore the 100 ms rule in `SteerPacer.Decide`."
SHARED RECORDS: the DEVIATIONS row above; the backlog row and the MIGRATION checkpoint entry are the coordinator's.
NOT ESTABLISHED: feel on a real client (a late steer making the walker pause at its goal), behaviour against a live server
and real latency; nothing was played or run in a browser.
RISKS: each EditMode run rewrites `ColiseumTorchArt.asset` and `ColiseumWeaponRackArt.asset`; the implementer restored
them and committed neither.
