# P-NET-DELAY-1: draw remote fighters closer to now on calm links

## ID and outcome

`P-NET-DELAY-1`. Lower the render clock's delay floor (`RenderClock.MinDelayMs`) from 70 ms to 55 ms, so remote players,
mobs and projectiles are drawn up to 15 ms closer to the server's present on a calm connection, without stalling.
Serves the owner's goal of removing felt input lag (project thread "Cut delay on other players", 2026-11-19).

## Dependencies

None.

## Relevant legacy paths (verified)

- `packages/shared/src/constants.ts:19` `MIN_INTERP_DELAY_MS = 70`; `:26` `EXTRAPOLATE_MS = TICK_MS`.
- `packages/shared/src/interp.ts` `RenderClock` (`aheadMs`): with legacy draw-ahead on, the floor drops by 50 ms to 20 ms and
  walking players are extrapolated (`packages/client/src/net/remoteDraw.ts`). Unity has no draw-ahead.

## New paths

None.

## Exclusive write scope

- `client-unity/RoguehallaNet/Net/RenderClock.cs` and its synced copy under `client-unity/UnityProject/Assets/RoguehallaNet/`.
- `client-unity/RoguehallaNet.Tests/NetTests.cs`.

## Contracts and references

R-AUTH unaffected: presentation only. The attack/release `t` stays the render time, so the server's rewind
(`crates/rh-sim/src/systems/weapons.rs` `note_lag`, clamped to [50, 300] ms) shrinks with the delay and still matches what the
player saw.

## Behaviour to preserve

- The adaptive target is still `TickMs + 98th-percentile arrival excess`, so a jittery link keeps its larger delay.
- Render time never runs backwards; the 150 ms ceiling, 100 ms start and slews are unchanged.

## Acceptance checks

`NetTests.CalmLinksDrawRemoteFightersCloserToNowWithoutStalling` (the oracle): snapshots stamped at the polling frame (60 and
144 fps, 0 to 30 ms jitter); render time never runs more than 5 ms past the newest received sample on more than two frames,
and a calm 144 fps link settles under 66 ms. Measured: 55.0 to 68.1 ms, zero stalled frames in every case.

## Validation commands

`dotnet test client-unity/RoguehallaNet.Tests`; `scripts/gate.sh --quick`.

## Risk and suggested model tier

Low; one constant. `implementer-sonnet` tier.

## Known traps

At 60 fps, frame quantization alone puts the measured excess near 17 ms, so the delay settles near 67 ms: the gain there is
small. The larger win (about 50 ms) is legacy's bounded draw-ahead, which needs the sim's walk hint on the wire: a cross-crate
wire packet of its own (proposed P-NET-DRAWAHEAD-1).

DEVIATIONS: proposed DEV row: Unity's floor is 55 ms where legacy (draw-ahead off) uses 70 ms.
