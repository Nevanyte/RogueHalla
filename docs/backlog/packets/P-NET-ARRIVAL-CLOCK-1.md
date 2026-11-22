# P-NET-ARRIVAL-CLOCK-1: time snapshots at socket arrival, and keep a 4 s jitter window at any tick rate

- **Serves:** not in ORDER_OF_OPERATIONS. Owner direction 2026-11-22 (MIGRATION checkpoint "Balance patch and test bot":
  the Sonnet audit he asked for, finding 4 in `docs/backlog/BALANCE_LAB_PATCH.md` "System improvements"). Run by the
  client-input lane at the coordinator's request.
- **Implementer tier:** implementer-sonnet
- **Reviewer:** reviewer
- **Integration base:** `feature/unity-rework` @ `83edb687f`
- **D-rules touched:** D1 loose proof (no capture) · D2 n/a · D5 none · D6 none · D9 unchanged: remote actors stay
  interpolated; the own walk prediction (`LocalMotion`) is not touched

## 1. ID and outcome

`P-NET-ARRIVAL-CLOCK-1`. The render clock sizes the remote-draw delay from how late snapshots arrive
(`client-unity/RoguehallaNet/Net/RenderClock.cs:76-105`, `TickMs + 98th percentile of (local - server) excess`). In the
browser, a frame is queued without a time (`client-unity/UnityProject/Assets/Plugins/WebGL/RhWebSocket.jslib:23`), drained
at the next Unity frame (`client-unity/UnityProject/Assets/Scripts/Net/WebGLTransport.cs:66-77`), and timed after the
drain (`client-unity/RoguehallaNet/Net/Session.cs:554` `double now = Now;` then `:567` `Clock.OnSnapshot(now, ...)`). Up
to one display frame (16.7 ms at 60 fps) of drain noise is measured as network jitter and becomes standing delay for every
remote player, mob and projectile. At the live 40 Hz tick (ADR 0029) that noise is most of a 25 ms tick.

Second, the jitter window is a sample count, `Window = 80` (`RenderClock.cs:17`, "about 4 s of snapshots at 20 Hz"):
at 40 Hz it covers only 2 s, so a late-snapshot pattern is forgotten twice as fast as it was designed to be.

When this lands: the browser stamps `performance.now()` when the socket delivers a frame, the transport reports how old
the frame is when it is raised, and the session gives the clock the arrival time (`now - age`). The window covers 4 s
whatever the tick (80 samples at 20 Hz, 160 at 40 Hz). Remote actors are drawn up to a frame closer to the present on a
calm link; nothing else changes.

## 2. Dependencies

None.

## 3. Relevant legacy paths

- `packages/shared/src/interp.ts` `RenderClock`: legacy measured arrival in the WebSocket message handler (JS), so its
  samples had no frame-drain noise. This packet restores that property; no deviation.

## 4. New paths

None. The new optional interface goes in `client-unity/RoguehallaNet/Net/Session.cs` beside `ITransport` (a new library
file would need a hand-made Unity `.meta`).

## 5. Exclusive write scope

- `client-unity/RoguehallaNet/Net/Session.cs`: replace the stub interface's comment if needed, and use it in
  `OnReceived` for `Clock.OnSnapshot` only
- `client-unity/RoguehallaNet/Net/RenderClock.cs`: the window
- `client-unity/UnityProject/Assets/Plugins/WebGL/RhWebSocket.jslib`
- `client-unity/UnityProject/Assets/Scripts/Net/WebGLTransport.cs`
- `client-unity/UnityProject/Assets/Scripts/Net/NativeTransport.cs` (Editor/standalone: stamp on the receive thread with
  `System.Diagnostics.Stopwatch` and report the age the same way)
- the synced copies under `client-unity/UnityProject/Assets/RoguehallaNet/Net/` (by `bash client-unity/sync-net.sh` only)
- existing tests under `client-unity/RoguehallaNet.Tests/` only if an assertion pins `Window = 80` at a non-20 Hz tick;
  name each change in the report

## 6. Contracts and references

- ADR 0008 / R-AUTH: presentation only. The attack and cast stamp (`RenderClock.RenderMs`) follows the same clock, so the
  server's rewind still matches what the player saw.
- ADR 0029: the live tick is 25 ms; `Session` sets `Clock.TickMs = Wire.TickMsOf(w.TickRate)` (`Session.cs:726`).

## 7. Behaviour to preserve

- `RenderClock` rules: floor `TickMs + 5`, ceiling 150, start 100, slews, snap, draw-ahead and the lead. Only which
  local time a sample carries, and how many samples the window keeps, change.
- At 20 Hz the window is exactly today's 80 samples.
- `Motion.OnSnapshot(..., now, ...)` and `Motion.RebasePresentationClock(now)` keep the drain-time `now` (prediction math
  is out of scope; changing it needs Rust vectors).
- A transport without the interface (every test transport today) behaves exactly as before (age 0).
- No per-snapshot allocation in steady play (`GarbageTests`, `AllocationMeasurementTests`): resize the window arrays
  only when the capacity changes (on the first snapshot after a `Reset` or a `TickMs` change), never per snapshot.

## 8. Acceptance checks

- `scripts/gate.sh --quick --oracle packet/P-NET-ARRIVAL-CLOCK-1` passes.

### Design the implementer builds

- `Session.cs`: `public interface IArrivalTimedTransport { double ReceivedAgeMs { get; } }`, "how long ago (ms) the socket
  delivered the frame that the current `Received` event carries; valid only inside that handler". In `OnReceived`, for
  the clock only: `arrived = now - clamp(age, 0, 1000)` when `_t` implements it, else `now`. Clamp because a backgrounded
  tab can hold frames for seconds; the clock still sees them as late, but a broken timer cannot push a sample into the
  future or minutes back.
- `RhWebSocket.jslib`: `onmessage` queues `{ d: Uint8Array, t: performance.now() }`; `RhWs_NextLength` and
  `RhWs_Receive` read `.d`; a new `RhWs_NextAge(id)` returns `performance.now() - queue[0].t` (or 0). Keep the "JS never
  calls back into C#" design.
- `WebGLTransport.cs`: implements the interface; reads `RhWs_NextAge` before `RhWs_Receive` and holds it in
  `ReceivedAgeMs` while raising `Received`. Add the `DllImport` and the non-WebGL fallback (returns 0).
- `NativeTransport.cs`: stamp `Stopwatch.GetTimestamp()` on the receive thread, compute the age on the main thread when
  the queued event runs.
- `RenderClock.cs`: `public const double WindowMs = 4000;` capacity `max(8, round(WindowMs / TickMs))`.

## Oracle files (read-only for implementers)

- `client-unity/RoguehallaNet.Tests/ArrivalClockTests.cs`

## 9. Validation commands

- `export PATH="$HOME/.cargo/bin:$HOME/.dotnet:$PATH"; dotnet test client-unity/RoguehallaNet.Tests --filter ArrivalClock`
- `bash client-unity/sync-net.sh` then `bash client-unity/sync-net.sh --check`
- `scripts/gate.sh --quick --oracle packet/P-NET-ARRIVAL-CLOCK-1` (C# suite and the Unity EditMode suite)

## 10. Risk and suggested model tier

Sonnet: one area, small. The jslib cannot be run by the gate (WebGL player only): review reads it line by line, and the
hand-off says it was not run in a browser.

## 11. Known traps

- `performance.now()` and Unity's `Time.realtimeSinceStartupAsDouble` have different epochs: carry an age (a
  difference), never a JS timestamp, into C#.
- Read the age before `RhWs_Receive` pops the frame.
- `RenderClock` arrays are `readonly` today and `TickMs` is a public field set after `Reset()` (`Session.cs:723-726`).
