# P-UI-ALLOC-METER-1: a meter for "allocates nothing" tests that is proven to see allocations

- **Serves:** not in the plan: owner direction 2026-11-22 (chat of the UI rework lane: "make the front end as fluid as possible", "Please do a UI optimization sweep"); the lane rule that a steady frame allocates nothing must be provable. Found 2026-11-23 by the oracle author of P-UI-TABMAP-SHEET-1-fix.
- **Implementer tier:** implementer-sonnet
- **Reviewer:** reviewer
- **Integration base:** `lane/ui-rework/base-2` (lane branch of `feature/unity-rework`)
- **D-rules touched:** D1 n/a (test support only) · D2 n/a · D5/D6/D9 none. No product code changes.

## 1. ID and outcome

Every "allocates nothing" EditMode test measures with `GC.GetAllocatedBytesForCurrentThread()` before and after a loop
(about twenty places, see section 7). The oracle author of P-UI-TABMAP-SHEET-1-fix measured that in this editor (Unity
6000.0.85f2, Mono, batch mode) that counter reads 0 even across 20,000 string concatenations: a test built on it passes
on any code. That author switched to `Profiler.GetMonoUsedSizeLong()` after `GC.Collect()`: it saw allocations when the
class ran alone and read 0 inside the whole suite (a collection during the loop hides the growth; the meter moves in
16 KB steps), so it is flaky.

When this packet lands there is one helper, `AllocationMeter`, in the test assembly, built on the first candidate meter
that passes all four cells of the measurement table (section 3) every time, with a self-check inside `AssertNone`: a
meter that goes blind turns the test red, never green. This packet converts no existing test; a later packet
(P-UI-ALLOC-PROOF-1) moves the lane's allocation tests onto the helper.

### Step 1 for the implementer: measure and write down what is true

In section "Measurement table" below, fill for each candidate what it reads for (a) a control that allocates (20,000
`string` concatenations whose results are kept alive in a list, and separately 20,000 `new object()`), (b) a control
that does not (20,000 additions on a local `int`), each with the class run alone AND inside a run of several heavy
classes (`PlayerFlowsEditModeTests`, `HudLegacyLayoutEditModeTests`, `TabMapSheetEditModeTests` together with
`AllocationMeterEditModeTests`: `unity-class.sh` takes a comma list). Run the controls at least 20 times in a row.
Candidates, in this order:

1. `GC.GetAllocatedBytesForCurrentThread()` (confirm or correct the finding above: say exactly what it reads);
2. `UnityEngine.TestTools.Constraints`: `Assert.That(() => ..., Is.Not.AllocatingGCMemory())` and its positive form
   (counts `GC.Alloc` profiler samples; needs `using UnityEngine.TestTools.Constraints;` and
   `using Is = UnityEngine.TestTools.Constraints.Is;`; may need the profiler enabled: measure, do not assume);
3. `Unity.Profiling.ProfilerRecorder` on the memory counters ("GC Allocated In Frame", "GC Alloc" or the names this
   version has);
4. `GC.GetTotalAllocatedBytes(false)` and `(true)`, and `GC.CollectionCount(0)` as a side signal;
5. `Profiler.GetMonoUsedSizeLong()` with collections held off (`UnityEngine.Scripting.GarbageCollector.GCMode` set to
   Disabled if the editor allows it, or `GC.TryStartNoGCRegion`), or taking the largest growth of several repeats.

Build the helper on the first candidate that passes all four cells every time. **If no candidate passes**, do not ship
a helper that guesses: report `blocked` with the table, saying which candidate came closest and what broke it.

## 2. Dependencies

none (base `lane/ui-rework/base-2`).

## 3. Relevant legacy paths

none (test support in the Unity client; no legacy behaviour).

### Measurement table (implementer fills; the oracle author measured nothing)

| Candidate | alloc control alone (string / object) | clean control alone | alloc in heavy run | clean in heavy run | verdict |
|---|---|---|---|---|---|
| `GC.GetAllocatedBytesForCurrentThread` | | | | | |
| `Is.Not.AllocatingGCMemory` | | | | | |
| `ProfilerRecorder` | | | | | |
| `GC.GetTotalAllocatedBytes` / `CollectionCount` | | | | | |
| `GetMonoUsedSizeLong` held off | | | | | |

## 4. New paths

- `client-unity/UnityProject/Assets/Tests/Editor/AllocationMeter.cs` (+ `.meta`): the helper (stubbed by the oracle author; the implementer replaces the body).
- `client-unity/UnityProject/Assets/Tests/Editor/AllocationMeterEditModeTests.cs` (+ `.meta`): the oracle.

API (namespace `Roguehalla.Client.Tests`, in the test assembly `Roguehalla.Client.EditModeTests`; the stub has these exact signatures):

```csharp
public static class AllocationMeter
{
    public static bool Allocates(System.Action action, int calls);
    public static void AssertNone(System.Action action, int calls, string what);
    public static string Meter { get; }
    // Test seam, public, null in normal use. When set it REPLACES the raw measurement (it is called with the action
    // and the call count and returns "allocated"); the self-check in AssertNone runs through it too.
    public static System.Func<System.Action, int, bool> MeterOverride;
}
```

- `Allocates(action, calls)`: true when calling `action` `calls` times allocated managed memory.
- `AssertNone(action, calls, what)`: fails the running test (NUnit `AssertionException`), naming `what` in the message, when `action` allocates. Before it measures it checks itself: a known allocating control must register and a known clean control must not; if either check fails the test is failed with a message containing "the meter is not working", never passed. Keep its cost small (it runs in every allocation test).
- `Meter`: the name of the meter in use (never empty, never "none").
- `MeterOverride`: when non-null, replaces the meter (the self-check sees it too). Tests set it to `(a, n) => false` ("always no allocation") and remove it in TearDown.
- The helper's summary tells users: warm the code up before measuring, and build each delegate once outside the measured call (a closure allocates when it is created, not when called).

## 5. Exclusive write scope

- new `client-unity/UnityProject/Assets/Tests/Editor/AllocationMeter.cs` (+ `.meta`)
- new `client-unity/UnityProject/Assets/Tests/Editor/AllocationMeterEditModeTests.cs` (+ `.meta`) (oracle: read-only for the implementer)
- `docs/backlog/packets/P-UI-ALLOC-METER-1.md` (the implementer fills the measurement table only)
- Not in scope: every existing test, everything under `Assets/Scripts`, the assembly definition files. The test assembly `Roguehalla.Client.EditModeTests.asmdef` already has `optionalUnityReferences: TestAssemblies` (so `UnityEngine.TestTools.Constraints` resolves) and Unity.Profiling lives in `UnityEngine.CoreModule`; if the chosen meter needs an assembly reference the test assembly lacks, stop and report which.

## 6. Contracts and references

- Lane rules (`COMMON.md` of the lane): motion and presentation allocate nothing in a steady frame; EditMode time does not advance; the test assembly cannot see `internal` members.
- `client-unity/UnityProject/Assets/Tests/Editor/Roguehalla.Client.EditModeTests.asmdef` (test assembly, Editor only).

## 7. Behaviour to preserve (existing tests that use the old meter: none is changed by this packet)

No existing test pins the new file. The tests below use `GC.GetAllocatedBytesForCurrentThread()` and stay as they are
(they are the coordinator's / P-UI-ALLOC-PROOF-1's). Each asserts that a warmed loop grows the counter by 0 (or at most
a small bound):

- `ChampionPanelNoGarbageEditModeTests.cs:90` (Wish row and weapon slots, unchanged inputs)
- `FrostWallPresenterEditModeTests.cs:213` (200 warmed frames, `<= 0`)
- `DressedMapEditModeTests.cs:260` (NoteActor and Tick once built, `== 0`)
- `CampLabelStableEditModeTests.cs:476`, `UiMotionEditModeTests.cs:336`, `QuestTrackerEditModeTests.cs:436`, `ToastLanesEditModeTests.cs:333`
- `RenderPassEditModeTests.cs:368`, `WorldMarks2EditModeTests.cs:118,231`, `BogTreeLandmarkEditModeTests.cs:424,430`, `SrgbMeshColorsEditModeTests.cs:37`, `LandmarkViewEditModeTests.cs:331,337`, `AtmosphereEditModeTests.cs:187,332`, `VolcanoLandmarkEditModeTests.cs:304`, `MatchHudStatusTextsEditModeTests.cs:155`, `HallLandmarkEditModeTests.cs:361,367`, `ChurchLandmarkEditModeTests.cs:485,491`

No conflict: this packet touches none of them.

## 8. Acceptance checks

- `scripts/gate.sh --quick --oracle packet/P-UI-ALLOC-METER-1 --base lane/ui-rework/base-2` prints `GATE PASS`.
- `AllocationMeterEditModeTests` green alone (`unity-class.sh <worktree> AllocationMeterEditModeTests`) and green when run together with `PlayerFlowsEditModeTests,HudLegacyLayoutEditModeTests,TabMapSheetEditModeTests`; `ItGivesTheSameAnswerTwentyTimes` must be green in the whole-suite run of the gate.
- The measurement table of section 3 is filled.

## Oracle files (read-only for implementers)

- `client-unity/UnityProject/Assets/Tests/Editor/AllocationMeterEditModeTests.cs`
- `client-unity/UnityProject/Assets/Tests/Editor/AllocationMeterEditModeTests.cs.meta`

## 9. Validation commands

- `.agent-runs/lane-ui-rework/bin/unity-class.sh /Users/nevinmckinnon/Documents/Projects/RH-ui-a AllocationMeterEditModeTests`
- same with `AllocationMeterEditModeTests,PlayerFlowsEditModeTests,HudLegacyLayoutEditModeTests,TabMapSheetEditModeTests`

## 10. Risk and suggested model tier

Sonnet. The risk is the meter, not the code: Mono in batch mode hides allocations from some counters, and the whole
suite leaves a warm, collecting heap. The self-check is what keeps a blind meter from passing.

## 11. Known traps

- A delegate with a closure allocates when it is CREATED: build each delegate once outside the measured call (the oracle's delegates are static fields).
- `GC.GetAllocatedBytesForCurrentThread()` reads 0 here (per the earlier finding); `GetMonoUsedSizeLong` moves in 16 KB steps and a collection during the loop hides growth.
- The gate runs about 2,060 EditMode tests in one editor process: the meter must not depend on a quiet heap.
- Never sleep, never read the wall clock.

## Notes from the oracle author

- Test 2 (`ACleanDelegateIsNotSeen`) would pass on the stub (stub answers false), so it also asserts that the meter sees an allocating control in the same test (a "clean" answer from a blind meter means nothing). Red on the stub on that assertion.
- The spec lists `Meter` "is not empty and not none": the stub returns "none".
- Seam: `AllocationMeter.MeterOverride` (see section 4).
- Red on the stub (all six, each on an assertion): `AnAllocatingDelegateIsSeen` (`Allocates` expected True, was False, "string concatenation x1 ..."); `ACleanDelegateIsNotSeen` ("the meter sees the allocating control" expected True); `ItGivesTheSameAnswerTwentyTimes` ("round 0: string concatenation is seen"); `AssertNoneFailsOnAnAllocatingDelegate` (expected AssertionException, was null); `ABlindMeterFailsTheTest` (expected AssertionException, was null); `TheMeterHasAName` (expected not "none").
- Scratch run: this NUnit has no `Assert.Multiple`, so I ran one scratch pass per class with a soft-assert wrapper (each assertion caught and listed, bodies otherwise intact). Every line ran; every listed failure was an assertion on the missing behaviour (the clean-delegate assertions passed on the stub, as they must). Not committed.
- The clean-delegate cases of test 2 and the clean `AssertNone` line of test 4 pass on the stub by nature; the red assertions come first/alongside them.
- No existing class pins this code (no product or existing test file changes), so no existing class was run.
- The seam `MeterOverride` replaces the raw measurement used by both the self-check and the real measurement; the blind test sets `(a, n) => false`.
