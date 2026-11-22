# P-NET-STEER-1: keyboard steering and aim stop spending the server's order and frame budgets

- **Serves:** not in ORDER_OF_OPERATIONS. Owner direction 2026-11-22 (MIGRATION checkpoint "Balance patch and test bot":
  the Sonnet audit he asked for, finding 1 in `docs/backlog/BALANCE_LAB_PATCH.md` "System improvements"). Run by the
  client-input lane at the coordinator's request.
- **Implementer tier:** implementer-sonnet
- **Reviewer:** reviewer
- **Integration base:** `feature/unity-rework` @ `83edb687f`
- **D-rules touched:** D1 loose proof (one scripted-kite test; no capture) · D2 n/a · D5 none · D6 none · D9 unchanged
  (no prediction change; the server still plans every order)

## 1. ID and outcome

`P-NET-STEER-1`. Holding a direction key re-sends a Move on a fixed 100 ms timer (`MatchClient.cs:36` `SteerRepeatMs`,
`:500-506`): 10 orders a second. The server charges one token per non-Aim command at 15/s with a burst of 30 on sim time
(`crates/rh-sim/src/input.rs:1146-1162` `OrderBudget::allow`, `crates/rh-sim/src/constants.rs:182-183`) and drops the
rest silently (`Reject::OverBudget`, `input.rs:999`). Kiting while clicking runs the budget dry: a scripted model of this
code (30 s, 8 headings each held 600 ms, 4 clicks a second = Attack + Release, walker at 170 u/s) spends 18 orders/s and
starts losing orders, attacks included, at about 9.7 s (62 lost in 30 s). Aim leaves on its own frame every 25 ms
(`InputSender.cs:14`), so the same kite sends up to 53 WebSocket frames in one second at 144 fps and 56 at 240 fps,
against the server's 60 frames/s connection limit (`crates/rh-server/src/conn.rs:33-34`), which disconnects.

When this lands: a held direction is re-sent only when it matters (a new direction, the goal nearly walked, or a safety
repeat), about 5 a second instead of 10; repeats are optional orders that back off when the client's budget is low; and
aim-only frames stand back from 25 ms to 50 ms while the client has already sent many frames in the last second. The same
model then spends 13 orders/s, loses none, and peaks at 38 to 43 frames per second. A key press still sends its order in
the same frame. Server constants do not change: the order budget is hashed sim state.

## 2. Dependencies

None.

## 3. Relevant legacy paths

- `packages/client/src/net/keyboardMovement.ts:21` `STEER_REPEAT_MS = 100` and `:33` `steerDue`: the legacy client had the
  same fixed repeat (and the same budget problem). This packet deviates from it on purpose (see DEVIATIONS below).
- `packages/client/src/net/uplinkPace.ts:1-40`: legacy already made the aim stand back when the client's own frame rate
  neared the server's message cap (`AIM_BUDGET_SLOW`); the frame meter here is the same idea, simpler.
- `packages/client/src/net/keyboardMovement.ts:6` `keyboardMoveGoal`: the 64 u lookahead goal, ported as
  `client-unity/RoguehallaNet/Net/KeyboardSteer.cs`. Unchanged.

## 4. New paths

None. `SteerPacer` lives in `client-unity/RoguehallaNet/Net/OrderBudget.cs` beside `OrderBudget` and `StopRetry` (the
file is the client's order pacing; a new file would need a hand-made Unity `.meta`).

## 5. Exclusive write scope

- `client-unity/RoguehallaNet/Net/OrderBudget.cs` (replace the stub `SteerPacer`)
- `client-unity/RoguehallaNet/Net/InputSender.cs` (the aim frame meter, in `Flush`)
- `client-unity/UnityProject/Assets/Scripts/Net/MatchClient.cs` (the steering block, `:36-49` fields and `:493-514`)
- the synced copies under `client-unity/UnityProject/Assets/RoguehallaNet/Net/` (by `bash client-unity/sync-net.sh` only)
- `client-unity/RoguehallaNet.Tests/OrderBudgetTests.cs` and `client-unity/RoguehallaNet.Tests/NetTests.cs` only if an
  existing assertion conflicts; name each change in the report

## 6. Contracts and references

- C-INPUT (`docs/CONTRACTS.md`): every order still goes through `Session.OrderMove`/`InputSender`; the server validates
  and budgets. R-AUTH / ADR 0008: the client still sends destinations and never simulates movement.
- `crates/rh-server/src/conn.rs:33-34` `FRAMES_PER_SECOND = 60`, `FRAME_BURST = 120` (Pings and Pongs count too).

## 7. Behaviour to preserve

- A first press, a direction change, and a release (Stop) send on the frame they happen, as today.
- The goal itself: `KeyboardSteer.TryGoal` and its lookahead (64 down to 8 u, box foot 1 u steps) are unchanged.
- `StopRetry`, right-click hold (`HoldRepeatMs`, `HoldMovedU`) and `OrderMapMove` keep their behaviour.
- Aim: latest wins; a changed aim is never lost; an unchanged aim is re-sent every `AimRepeatMs` (100 ms; the server
  drops aim while stunned or target-locked); aim never waits when a frame leaves anyway (it rides along once
  `AimMinIntervalMs`, 25 ms, has passed). `IdleFlushAllocatesNothing` stays green: the meter allocates nothing per Flush.

## 8. Acceptance checks

- `scripts/gate.sh --quick --oracle packet/P-NET-STEER-1` passes.
- The oracle's kite prints orders/s, lost orders and peak frames/s before and after; the hand-off quotes them.

### Design the implementer builds

`SteerPacer` (engine-free, pure arithmetic; every time is an argument):

- `SteerDecision Decide(double nowMs, int keyX, int keyY, double posX, double posY, double goalX, double goalY)` returns
  `None`, `Turn` or `Refresh`. `pos` is the drawn (predicted) position; `goal` is this frame's `TryGoal` result.
  - `Turn`: not steering yet, or the held keys changed. Always sent.
  - `Turn` also when the clear goal's heading (from `pos`) differs from the last sent goal's heading by more than
    `TurnDegrees` (20) and at least `MinRepeatMs` (100) passed: a wall slide switched axis.
  - `Refresh`, only after `MinRepeatMs` (100 ms) since the last send:
    - the last sent goal is nearly walked: within `NearlyConsumedU` (24 u) of `pos`, and the new goal lies more than
      `SameGoalU` (2 u) from it (standing against a wall with the same goal sends nothing new);
    - or `SafetyRepeatMs` (500 ms) passed (heals an order the server dropped).
  - Otherwise `None`.
- `void Sent(double nowMs, int keyX, int keyY, double goalX, double goalY)` records a send; `void Reset()` when steering
  stops (keys released, no goal, keyboard blocked, session change). `bool Steering`.
- The constants are public consts on `SteerPacer`. 24 u is about 140 ms of walking at 170 u/s: with the walk predicted,
  the drawn position is roughly where the server will be when the order lands, so the next goal arrives before this one
  is reached.

`MatchClient` steering block: `Turn` calls `OrderMove` as today (essential, `Spend`); `Refresh` asks
`_orderBudget.TryTake(now)` and, if granted, calls `OrderMove(x, y, budgeted: true)`; when refused, `Sent` is not called,
so the pacer asks again next frame. Every place that sets `_steering = false` resets the pacer. Remove `SteerRepeatMs`,
`_lastSteer` and `_lastDir` once unused.

`InputSender.Flush` aim: keep the times of the last 64 frames Flush returned in a fixed ring (allocated once). A changed
aim goes out when the queue already holds a command and `AimMinIntervalMs` passed (as today); in an aim-only frame it
needs `AimMinIntervalMs` while fewer than `AimStandBackFrames` (36) frames left in the last `FrameMeterWindowMs` (1000 ms),
else `AimSoloSlowMs` (50 ms). New public consts beside `AimMinIntervalMs`. The keepalive is unchanged.

## Oracle files (read-only for implementers)

- `client-unity/RoguehallaNet.Tests/SteerPacingTests.cs`

## 9. Validation commands

- `export PATH="$HOME/.cargo/bin:$HOME/.dotnet:$PATH"; dotnet test client-unity/RoguehallaNet.Tests --filter SteerPacing`
- `bash client-unity/sync-net.sh` then `bash client-unity/sync-net.sh --check`
- `scripts/gate.sh --quick --oracle packet/P-NET-STEER-1` (runs the C# suite and the Unity EditMode suite)

## 10. Risk and suggested model tier

Sonnet: one area (client input pacing), engine-free logic plus one Unity call site. The risk is feel: a steer that
arrives late makes the walker stop for a frame at the goal. The 24 u margin and the 500 ms safety repeat cover a dropped or
late order; the oracle pins the rate, review checks the feel argument.

## 11. Known traps

- `myX/myY` in `MatchClient` are the drawn (predicted) position; use them, not the server's.
- Do not gate a `Turn` on the client budget: a key change is an order, not a repeat.
- `_steering` is reset in several places (`JoinCore`, the session state handler, `keyboardBlocked`): reset the pacer at
  each, or keep one helper.
- The Unity copy is generated: edit `client-unity/RoguehallaNet/`, then run `sync-net.sh`.

## DEVIATIONS (proposed in the hand-off)

Keyboard steering re-sends on goal consumption, a heading change or a 500 ms safety repeat instead of every 100 ms
(legacy `keyboardMovement.ts:21`); aim-only frames stand back to 50 ms above 36 frames in the last second. Client
pacing only: same goals, same server rules.

## Existing tests that touch steering or aim (survey by the oracle author)

No existing test pins the steering cadence (`SteerRepeatMs` lives only in `MatchClient.cs`, which has no C# test). Aim and
budget tests, each checked by running the whole suite against a throwaway implementation of the design (reverted): all
green, no `oracle-amend:` needed.

- `NetTests.AimIsRateLimitedToFortyPerSecond` (aim-only, 5 ms flush, 1 s, `InRange(38,40)`): the meter reaches 36 frames
  at about 0.9 s, so the stand-back to 50 ms only bites in the last ~100 ms; stays 38 to 40.
- `NetTests.UnchangedAimIsNotResentExceptByTheSlowRepeat` (`InRange(20,21)` in 2 s): 10 frames/s, under 36; unchanged.
- `NetTests.ChangedAimIsNeverLostWhileRateLimited`: few frames, gaps 25 ms as before; unchanged.
- `NetTests.IdleFlushAllocatesNothing` (< 1 KB): the ring is allocated once; green.
- `NetTests.LatestAimWinsAndNonAimLeavesImmediately`: a queued command keeps the 25 ms gap; green.
- `OrderBudgetTests` (all six): `OrderBudget` and `StopRetry` are not edited; green.
- `UnityCopyOfTheLibraryIsUpToDate`: needs `sync-net.sh` after the implementation.

Measured on the stub (today's rule), `dotnet test --filter SteerPacing`: 18.0 orders/s, 62 dropped, first drop 9500 ms,
peak frames 39 (60 fps), 49 (144), 55 (240); rooted walker: 20 Moves in 2 s. Throwaway design: 13.0 orders/s, 0 dropped,
peak 37 / 40 / 45.
