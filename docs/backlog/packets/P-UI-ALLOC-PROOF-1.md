# P-UI-ALLOC-PROOF-1: the lane's "allocates nothing" tests measure with a meter that works

- **Serves:** not in the plan: owner direction 2026-11-22 (UI rework lane chat: "make the front end as fluid as possible", "Please do a UI optimization sweep"). Follows P-UI-ALLOC-METER-1.
- **Implementer tier:** implementer-sonnet
- **Reviewer:** reviewer
- **Integration base:** `lane/ui-rework/base-2` (holds P-UI-ALLOC-METER-1: `client-unity/UnityProject/Assets/Tests/Editor/AllocationMeter.cs`); branches from `feature/unity-rework`
- **D-rules touched:** D1 n/a (tests, and allocation fixes that change nothing a player sees) · D2 n/a · D5/D6/D9 none

## 1. ID and outcome

P-UI-ALLOC-METER-1 measured that `GC.GetAllocatedBytesForCurrentThread()` reads 0 in this Unity editor (0 of 20 rounds
of 20,000 allocating iterations; table in `docs/backlog/packets/P-UI-ALLOC-METER-1.md`). Four EditMode tests of this lane
measure with it, so their "allocates nothing" claims pass on any code and have never been tested:

| Class | Test method | Old counter lines | Code under test |
|---|---|---|---|
| `CampLabelStableEditModeTests` | `ASteadyFrameAllocatesNothing` | `CampLabelStableEditModeTests.cs:476-479` | `Presentation/CampRespawnLabels.cs` (`Draw`, called by `rig.Frame()` at `CampLabelStableEditModeTests.cs:58`) |
| `UiMotionEditModeTests` | `AnIdleTickAllocatesNothing` | `UiMotionEditModeTests.cs:336-339` | `UI/UiMotion.cs` (`HudFade.Tick` :67, `HudPulses.Tick` :100) |
| `QuestTrackerEditModeTests` | `ASteadyFrameAllocatesNothing` | `QuestTrackerEditModeTests.cs:436-439` | `UI/QuestTrackerView.cs` (`Refresh` :62), `RoguehallaNet/Net/Ui/QuestTracker.cs` |
| `ToastLanesEditModeTests` | `ASteadyFrameWithToastsAllocatesNothing` | `ToastLanesEditModeTests.cs:333-336` | `UI/MatchHud.Notices.cs` (`TickNotices` :84, `NoticeDtOverride` :19), `UI/ToastLane.cs`, `RoguehallaNet/Net/Ui/ToastLaneModel.cs` |

When this packet lands: each of the four tests measures with `AllocationMeter.AssertNone(action, calls, what)`, and the
claim of each is either proven or the allocation is removed. In addition the dotnet tests that use the working counter
first prove it sees (item 4 below).

### Outcome items (from the lane spec)

1. **Each of the four tests measures with `AllocationMeter.AssertNone(action, calls, what)`.** The measured code is one
   `System.Action` built ONCE before the call (a lambda that captures locals allocates when created, not when called; a
   method group or a delegate stored in a local is fine), warmed up as the test warms it today, called at least as many
   times as the old loop ran (200). Everything else in the test stays: set-up, set-up assertions (the thing measured must
   be on screen and active), name, and the assertions after the measured part.
2. **The oracle author records what each converted test does on the base** (table below).
3. **The implementer makes every red one green by removing the allocation**, never by loosening the test: find what
   allocates in the steady call (a string built per frame, a boxed value, a lambda or enumerator created per call, a
   `GetComponent` of a missing component in the editor, a list that grows) and remove it. Nothing a player sees may
   change. If the source of an allocation is in a file outside the write scope: stop, report the file and the
   allocation, leave that test red, return blocked; do not widen the scope, do not weaken the test.
4. **The dotnet tests keep their counter and prove it sees.** Under dotnet the counter works. Every test of
   `client-unity/RoguehallaNet.Tests` that measures with `GC.GetAllocatedBytesForCurrentThread()` first asserts that a
   control (1,000 string concatenations kept alive) registers more than 0 bytes. One private helper per test class.

## 2. Dependencies

P-UI-ALLOC-METER-1 (in the base: `AllocationMeter.cs`, `AllocationMeter.AssertNone`, which checks itself on every
call: an allocating control must register and a clean control must not, else the test fails as "the meter is not working").

## 3. Relevant legacy paths

None: presentation-only tests; no legacy behaviour is involved.

## 4. New paths

None. No stub (the oracle IS the amended tests).

## 5. Exclusive write scope

- Amended tests, each test METHOD in its own `oracle-amend:` commit (the meter only changes): the four allocation tests
  above under `client-unity/UnityProject/Assets/Tests/Editor/`, and the dotnet tests of item 4 under
  `client-unity/RoguehallaNet.Tests/` (listed under "Oracle files").
- Product files, only where a converted test is red and only to remove the allocation it reports:
  `client-unity/UnityProject/Assets/Scripts/Presentation/CampRespawnLabels.cs`,
  `client-unity/UnityProject/Assets/Scripts/UI/UiMotion.cs`, `UI/QuestTrackerView.cs`, `UI/ToastLane.cs`,
  `UI/MatchHud.Notices.cs`, `client-unity/RoguehallaNet/Net/Ui/QuestTracker.cs`, `ToastLaneModel.cs`, `LabelLift.cs`
  and their synced copies under `client-unity/UnityProject/Assets/RoguehallaNet/Net/Ui/` (run `bash client-unity/sync-net.sh`
  through the wrapper after a library edit).
- `docs/backlog/packets/P-UI-ALLOC-PROOF-1.md`.
- Not in scope: `AllocationMeter.cs` and its tests; the thirteen older classes that use the same counter
  (`ChampionPanelNoGarbageEditModeTests`, `FrostWallPresenterEditModeTests`, `DressedMapEditModeTests`,
  `MatchHudStatusTextsEditModeTests` and the rest: the coordinator decides); `AbilityRowEditModeTests` and
  `TabMapHeaderNoGarbageEditModeTests` (their own packets); every other product file.

## 6. Contracts and references

- Lane rules `COMMON.md`: motion allocates nothing in a steady frame; presentation only; tests are a separate assembly
  (no `InternalsVisibleTo`).
- `docs/backlog/packets/P-UI-ALLOC-METER-1.md` (the measurement of the broken counter).

## 7. Behaviour to preserve

- Everything a player sees: any allocation fix must leave texts, positions, fades and pulses exactly as they are.
- Library edits keep `RoguehallaNet.Tests` green and the synced copy identical.
- `client-unity/UnityProject/Tools/hud-seams.test.cjs` pins names and files of `MatchHud.*.cs` members: if a `MatchHud` member is renamed or moved, run `node --test client-unity/UnityProject/Tools/hud-seams.test.cjs` through the wrapper.

## 8. Acceptance checks

- `scripts/gate.sh --quick --oracle packet/P-UI-ALLOC-PROOF-1 --base lane/ui-rework/base-2` prints `GATE PASS`.
- The four converted EditMode tests are green (`unity-class.sh <worktree> CampLabelStableEditModeTests,UiMotionEditModeTests,QuestTrackerEditModeTests,ToastLanesEditModeTests`).
- The dotnet tests of item 4 are green.

## Oracle files (read-only for implementers)

Unity EditMode (each method in its own `oracle-amend:` commit, only the meter changed):
- `client-unity/UnityProject/Assets/Tests/Editor/CampLabelStableEditModeTests.cs` (`ASteadyFrameAllocatesNothing`)
- `client-unity/UnityProject/Assets/Tests/Editor/UiMotionEditModeTests.cs` (`AnIdleTickAllocatesNothing`)
- `client-unity/UnityProject/Assets/Tests/Editor/QuestTrackerEditModeTests.cs` (`ASteadyFrameAllocatesNothing`)
- `client-unity/UnityProject/Assets/Tests/Editor/ToastLanesEditModeTests.cs` (`ASteadyFrameWithToastsAllocatesNothing`)

dotnet (control assertion added; the counter and the rest of each test unchanged):
- `client-unity/RoguehallaNet.Tests/ToastLaneModelTests.cs` (`ATickThatChangesNothingAllocatesNothing`)
- `client-unity/RoguehallaNet.Tests/GarbageTests.cs` (`SessionMeMakesNoGarbageOnTheSteadyPath`, class `GarbageTests`)
- `client-unity/RoguehallaNet.Tests/NetTests.cs` (`IdleFlushAllocatesNothing`, class `InputSenderTests`)
- `client-unity/RoguehallaNet.Tests/OwnerCombatProtocolTests.cs` (`RetainingOwnerCombatAcrossPooledDeltasDoesNotAllocateSteadily`)
- `client-unity/RoguehallaNet.Tests/NameMergeTests.cs` (`DecidingAllocatesNothing`)

(`AllocationProbe.cs` and the tests that use it are not in this list: they measure through the probe, which retires the
allocation block and is proven by `AllocationProbeTests`; the spec's item 4 targets direct uses of the counter.)

## 9. Validation commands

- `.agent-runs/lane-ui-rework/bin/in.sh <worktree> dotnet test client-unity/RoguehallaNet.Tests --filter "FullyQualifiedName~ToastLaneModelTests|FullyQualifiedName~GarbageTests|FullyQualifiedName~InputSenderTests|FullyQualifiedName~OwnerCombatProtocolTests|FullyQualifiedName~NameMergeTests"`
- `.agent-runs/lane-ui-rework/bin/unity-class.sh <worktree> CampLabelStableEditModeTests,UiMotionEditModeTests,QuestTrackerEditModeTests,ToastLanesEditModeTests`
- Finish with the quick gate above.

## 10. Risk and suggested model tier

Sonnet. The hard part is finding which line of a steady call allocates (use the editor's profiler markers or bisect the
call by hand); the tests give the answer in seconds per run, one to three minutes per Unity run.

## 11. Known traps

- `AllocationMeter.AssertNone` warms the action by running the loop once before it measures, so a stateful action gets
  twice the calls: give time-driven code a `dt` small enough that nothing finishes or starts during the run.
- A lambda capturing `this` or a local passed to `AssertNone` allocates the closure before the call: fine. A NEW lambda
  created inside the measured action is not.
- Struct returned through an interface boxes; `foreach` over a `List<T>` held as `IEnumerable<T>` allocates an
  enumerator; string interpolation and `+` allocate; `GetComponent` of a missing component allocates in the editor only.
- Never change what a test asserts about behaviour; never raise an allowance: the assertion is "none".
- A packet branch stays linear; the test assembly cannot see `internal` members; static state tests set is restored in TearDown.
- The Camp test keeps its second half (the badge does not follow a flickering fighter); the UiMotion pulses take an
  absolute `now` (`pulses.Tick(1f + i * Dt)`), so the converted action keeps a counter.

## Existing tests that pin this code (verified by reading)

- `CampLabelStableEditModeTests` (whole class): camp badge, respawn and ward texts stable, plaque gaps; also `CampLabelsHiddenFix4EditModeTests`, `CampWardTextGlideEditModeTests`, `NameLegibleEditModeTests`, `NamePackGroup2EditModeTests` (world labels through `CampRespawnLabels.Draw`): positions and text of the labels.
- `UiMotionEditModeTests` (fade alpha and pulses scale over time, reduced motion instant, destroyed rect dropped), `HudFadeConstructionEditModeTests` (first state from the root's `activeSelf`).
- `QuestTrackerEditModeTests` (rows, clocks, text cuts and fit, floor above the readout strip); `RoguehallaNet.Tests/QuestTrackerTests.cs` (pure model).
- `ToastLanesEditModeTests` (lanes, slots, history of 200, long toast fit); `RoguehallaNet.Tests/ToastLaneModelTests.cs` (pure model); `HudLegacyLayoutEditModeTests`, `StormInkCombatHudEditModeTests` (HUD layout built from pieces).
- `RoguehallaNet.Tests/LabelLiftTests.cs` (label lift arithmetic).
- `client-unity/UnityProject/Tools/hud-seams.test.cjs` (names and files of `MatchHud.*.cs` members).
- None of them asserts an allocation count except the four converted tests and the dotnet ones; none conflicts with this packet.

## Notes from the oracle author

(see the section below, filled in after the runs)
