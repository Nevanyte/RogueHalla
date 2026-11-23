# P-UI-ALLOC-METER-2: the allocation meter can count allocations, for a test that allows a few

- **Serves:** a request of the coordinator (its message of 2026-11-23 after reviewing wave 2): its conversion of thirteen older test classes to the lane's meter needs a budget form, because `RenderPassEditModeTests.cs:379` asserts a byte budget (`Assert.LessOrEqual(grown, 4096, ...)`, reading `GC.GetAllocatedBytesForCurrentThread()` at `:378`, which reads 0 in this editor) that a yes-or-no meter cannot express. Owner direction 2026-11-22 (UI optimization sweep). Not in ORDER_OF_OPERATIONS.
- **Implementer tier:** implementer-sonnet
- **Reviewer:** reviewer
- **Integration base:** the lane branch `lane/ui-rework/base-3` (head 632ecb349, which already holds P-UI-QUEST-OFFER-1; nothing of it touches `AllocationMeter.cs`). Quick gate: `--oracle packet/P-UI-ALLOC-METER-2 --base lane/ui-rework/base-3`.
- **D-rules touched:** D1: test support only; legacy has no counterpart and no player sees a change: no DEVIATIONS row. D2 n/a. D5/D6 none. D9 none (no product code, no game rule).

## 1. ID and outcome

`AllocationMeter.Allocates` answers `Raw(...)` (`Assets/Tests/Editor/AllocationMeter.cs:55-62`): it warms the loop up once, then returns `new AllocatingGCMemoryConstraint().ApplyTo(loop).IsSuccess`, which is "the recorder saw more than 0 samples". The constraint reads `recorder.sampleBlockCount` into a private result class (`AllocatingGCMemoryResult.diff`); the number is public only as message text ("made N GC allocation(s)"). So the only forms are `AssertNone` and `Allocates`: a test of code that may allocate a little (one string a frame, a pooled list that regrows once) cannot say so. It must choose zero, which is false, or the blind `GC.GetAllocatedBytesForCurrentThread()` with a byte budget, which proves nothing.

What one sample is, from the source of Unity Test Framework 1.6.0 (`UnityEngine.TestRunner/Assertions/AllocatingGCMemoryConstraint.cs`): the constraint disables the recorder (which flushes it), filters it to the current thread, enables it, runs the delegate, disables it, calls `CollectFromAllThreads`, and reads `sampleBlockCount`. Unity's documentation calls that "number of Begin/End pairs". A `GC.Alloc` marker is written once per managed allocation, so one sample should be one allocation. That is the expectation, not a fact: P-UI-ALLOC-METER-1 measured only "more than 0" and "0". The measured table in section 3 is that measurement.

When this packet lands, `AllocationMeter` also has (all in `AllocationMeter.cs`; `AssertNone`, `Allocates`'s answers and every caller are unchanged):

1. **`Samples(Action action, int calls)`** returns the `GC.Alloc` samples recorded on the current thread while `action` runs `calls` times, after the warm-up `Allocates` uses today: the loop runs once unmeasured, then once measured. So `action` runs `2 * calls` times in all (the summary says so). It makes the constraint's recorder calls itself, in the constraint's order (listing below). `Allocates` is rebuilt on it: `MeterOverride` if set, else `Samples(action, calls) > 0`. `Raw` is deleted, and so is `using UnityEngine.TestTools.Constraints;` from the file, and `using UnityEngine.Profiling;` (for `Recorder`) is added. Count and yes-or-no now share one code path; the new test class still checks both against the constraint itself, which is the independent witness.
2. **`AssertAtMost(Action action, int calls, int maxSamples, string what)`.** Arguments first: `action` null throws `ArgumentNullException`; `calls < 1` or `maxSamples < 0` throws `ArgumentOutOfRangeException`; nothing is measured before that. Then the self-check, which uses `Samples` only (the seam `MeterOverride` does not touch this method): (a) `Samples(AllocatingControl, ControlCalls)` is above 0; (b) `Samples(CleanControl, ControlCalls)` is 0; (c) `Samples(CountingControl, ControlCalls)` is at least `ControlCalls * CountingAllocationsPerCall` (30). Each failure is `Assert.Fail("the meter is not working (" + Meter + "): ... so '" + what + "' proves nothing")`, with (a) and (b) worded as in `AssertNone` (`AllocationMeter.cs:40` and `:44`) and (c) "it counted N samples for a control that makes 30 allocations". A failed self-check fails the test whatever the measured count. Then `Samples(action, calls)`; above `maxSamples` fails with exactly `what + " made " + n + " GC.Alloc samples in " + calls + " calls, above the limit of " + maxSamples + " (meter " + Meter + ")"`. At or below the limit returns.
3. **It counts samples, not bytes, and the contract is what the table supports** (section 3). Nobody asserts equality in a test.
4. **Doc comments.** `Samples` and `AssertAtMost` say: the unit is a sample; the contract is the table's reading; the action runs `2 * calls` times; a budget in samples says nothing about size, so a test whose point is a size keeps `AssertNone` on the buffer; and **a byte budget has no conversion**: this counts allocations, not bytes, and sizes cannot be measured in this editor. To convert a test that had a byte budget, run `Samples` once on the accepted code, put that count in as the limit and name in a comment the allocations it stands for. Which limit a converted test uses is that test's owner's decision. `Meter` becomes "GC.Alloc profiler samples (UnityEngine.Profiling.Recorder, the calls of AllocatingGCMemoryConstraint)".
5. **Seams.** `MeterOverride` (`Func<Action, int, bool>`) stays and still wins in `Allocates` (so in `AssertNone`). New public static `SamplesOverride` (`Func<Action, int, int>`): when set, `Samples` returns its answer and runs nothing (the null check on `action` comes after it, as `Raw`'s did after `MeterOverride`). `Allocates` and `AssertNone` therefore also see it when `MeterOverride` is null. Both are null in play and restored by every test that sets them.
6. **Controls are public** so a seam can tell them from the measured action by reference: `ControlCalls` (const 10), `CountingAllocationsPerCall` (const 3), `AllocatingControl` and `CleanControl` (as today, `AllocationMeter.cs:23-24`, now public), and `CountingControl` (three `new object()` into three static sinks). All are static readonly lambdas.
7. **Doc-comment wording from the table.** If every read in every cell of the table is at least `k * n`, the meter does not under-count on that editor and the check in (c) is sound; if every read equals `k * n`, the doc comment says "one sample per allocation (measured on <version>)", otherwise "at least one sample per allocation (measured on <version>)". The implementer takes the reading from section 3.

```csharp
public static class AllocationMeter   // namespace Roguehalla.Client.Tests, Assets/Tests/Editor/AllocationMeter.cs
{
    public static Func<Action, int, bool> MeterOverride;
    public static Func<Action, int, int> SamplesOverride;
    public const int ControlCalls = 10, CountingAllocationsPerCall = 3;
    public static readonly Action AllocatingControl, CleanControl, CountingControl;
    public static string Meter { get; }
    public static bool Allocates(Action action, int calls);              // Samples(...) > 0 unless MeterOverride
    public static int Samples(Action action, int calls);
    public static void AssertNone(Action action, int calls, string what);               // body unchanged
    public static void AssertAtMost(Action action, int calls, int maxSamples, string what);
}
// Samples, after the null check and the override: the constraint's sequence, in its order.
TestDelegate loop = () => { for (int i = 0; i < calls; i++) action(); };   // built before the window
loop();                                                                    // warm-up, unmeasured
var recorder = Recorder.Get("GC.Alloc");
recorder.enabled = false;  recorder.FilterToCurrentThread();  recorder.enabled = true;
try { loop(); } finally { recorder.enabled = false; recorder.CollectFromAllThreads(); }
return recorder.sampleBlockCount;
```

## 2. Dependencies

`lane/ui-rework/base-3` only. `AllocationMeter.cs` (P-UI-ALLOC-METER-1) is on it.

## 3. Relevant legacy paths

none (test support in the Unity client; no legacy behaviour).

### Measured table (oracle author, editor 6000.0.85f2, from `ProjectSettings/ProjectVersion.txt`, the editor `unity-class.sh` runs)

Method: an uncommitted scratch class held the listing of section 1 (the warm-up, the recorder sequence, `sampleBlockCount`) and the controls below; per cell 20 rounds with `GC.Collect()` before every third round; "low / high" are the lowest and highest read of the 20. Two runs: the class alone, and in one run with `PlayerFlowsEditModeTests`, `HudLegacyLayoutEditModeTests`, `TabMapSheetEditModeTests` and `AllocationMeterEditModeTests`. Both runs gave the same numbers.

| Control (per call) | calls n | k * n | alone low / high | heavy run low / high |
|---|---|---|---|---|
| 1 `new object()` | 10 | 10 | 10 / 10 | 10 / 10 |
| 1 `new object()` | 100 | 100 | 100 / 100 | 100 / 100 |
| 2 `new object()` | 10 | 20 | 20 / 20 | 20 / 20 |
| 2 `new object()` | 100 | 200 | 200 / 200 | 200 / 200 |
| 5 `new object()` | 10 | 50 | 50 / 50 | 50 / 50 |
| 5 `new object()` | 100 | 500 | 500 / 500 | 500 / 500 |
| `CountingControl` (3 `new object()`), the cell self-check (c) depends on | 10 (`ControlCalls`) | 30 | 30 / 30 | 30 / 30 |
| for information: one boxed `int` | 100 | 100 | 100 / 100 | 100 / 100 |
| for information: `"a" + int.ToString()` (2 allocations: the ToString string and the concatenation) | 100 | 200 | 200 / 200 | 200 / 200 |
| for information: `new int[8]` | 100 | 100 | 100 / 100 | 100 / 100 |

Reading: no read in any cell is below `k * n`, so the meter does not under-count on 6000.0.85f2 and self-check (c) is sound. Every read equals `k * n`, so the doc comments of `Samples` and `AssertAtMost` say **"one sample per allocation (measured on 6000.0.85f2)"**. Array size does not change the count (an 8-int array is one sample). The table claims nothing for another editor. A test asserts `>=` only, never equality.

## 4. New paths

- `client-unity/UnityProject/Assets/Tests/Editor/AllocationMeterCountEditModeTests.cs` (+ `.meta`): the oracle.

## 5. Exclusive write scope

- `client-unity/UnityProject/Assets/Tests/Editor/AllocationMeter.cs` (stubbed by the oracle author, replaced by the implementer)
- no other existing file. The packet document and the new test class are oracle files, read-only for the implementer.
- Not in scope: `AllocationMeterEditModeTests.cs` (the older packet's oracle, stays green and untouched), the nine classes that call `AssertNone` on the base (`AbilityRow`, `CampLabelStable`, `OwnerVitalEase`, `QuestOffer`, `QuestTracker`, `TabMapHeaderNoGarbage`, `TabMapMarks`, `ToastLanes` and `UiMotion`, each with the suffix `EditModeTests`; the calls are at `AbilityRowEditModeTests.cs:467,482`, `CampLabelStableEditModeTests.cs:477`, `OwnerVitalEaseEditModeTests.cs:236`, `QuestOfferEditModeTests.cs:974,987`, `QuestTrackerEditModeTests.cs:437`, `ToastLanesEditModeTests.cs:333`, `TabMapMarksEditModeTests.cs:421`, `UiMotionEditModeTests.cs:338`, `TabMapHeaderNoGarbageEditModeTests.cs:121`), the thirteen older classes (the coordinator converts them, `RenderPassEditModeTests.cs` included), `Assets/Scripts`, every asmdef, `COMMON.md`.

## 6. Contracts and references

- Lane rules `.agent-runs/lane-ui-rework/specs/COMMON.md` (allocation fact; a packet branch stays linear).
- `docs/backlog/packets/P-UI-ALLOC-METER-1.md` (the yes-or-no meter this extends).

## 7. Behaviour to preserve

- `AssertNone`'s body and messages; `Allocates(action, calls)` true or false exactly as before (same warm-up, same constraint sequence).
- Existing tests that pin this code, all of which stay green and untouched (no `oracle-amend:`):
  - `AllocationMeterEditModeTests` (six tests): `AnAllocatingDelegateIsSeen` (Concat, NewObject, Boxing, ListGrowth give `Allocates` true at 1, 100, 10000 calls); `ACleanDelegateIsNotSeen` (Concat seen, then AddLocals, StructField, ReadFrameCount give false); `ItGivesTheSameAnswerTwentyTimes` (20 rounds with `GC.Collect()`); `AssertNoneFailsOnAnAllocatingDelegate` (throws `AssertionException` naming `what`; clean passes); `ABlindMeterFailsTheTest` (`MeterOverride` returning false makes `AssertNone` fail with "the meter is not working"); `TheMeterHasAName` (`Meter` not null, not empty, not "none"). Its `TearDown` restores only `MeterOverride`.
  - The nine classes above call `AssertNone(action, 200, ...)` and nothing else of the meter (`Allocates`, `Raw` and the controls are used by nobody outside `AllocationMeter.cs` and `AllocationMeterEditModeTests.cs`).
  - `RenderPassEditModeTests.cs:379` (`Assert.LessOrEqual(grown, 4096, ...)`) is the byte budget that motivated this; it is not touched here.

## 8. Acceptance checks

- `scripts/gate.sh --quick --oracle packet/P-UI-ALLOC-METER-2 --base lane/ui-rework/base-3` prints `GATE PASS`.
- `AllocationMeterCountEditModeTests` green, and `AllocationMeterEditModeTests` green, alone and with `PlayerFlowsEditModeTests,HudLegacyLayoutEditModeTests,TabMapSheetEditModeTests`.
- Hand-off proposes one sentence for `COMMON.md` (the lane lead's file): `AssertAtMost` exists and counts samples (allocations), not bytes.

## Oracle files (read-only for implementers)
- `client-unity/UnityProject/Assets/Tests/Editor/AllocationMeterCountEditModeTests.cs`
- `client-unity/UnityProject/Assets/Tests/Editor/AllocationMeterCountEditModeTests.cs.meta`
- `docs/backlog/packets/P-UI-ALLOC-METER-2.md`

## 9. Validation commands

- `.agent-runs/lane-ui-rework/bin/unity-class.sh <worktree> AllocationMeterCountEditModeTests,AllocationMeterEditModeTests`
- `.agent-runs/lane-ui-rework/bin/unity-class.sh <worktree> AllocationMeterCountEditModeTests,AllocationMeterEditModeTests,PlayerFlowsEditModeTests,HudLegacyLayoutEditModeTests,TabMapSheetEditModeTests`

## 10. Risk and suggested model tier

Sonnet: one file, a short fixed sequence. The risk is in the window: any allocation inside the measured window (a string, closure, `nameof`, formatting) is counted and breaks the zero-sample claim for clean actions.

## 11. Known traps

- The measured window holds only `loop()`: build `loop` before `Recorder.Get`, and put no string, closure, `nameof` or formatting between `enabled = true` and the `finally`. Read `sampleBlockCount` after disabling, as the constraint does; the recorder is flushed by disabling. `CollectFromAllThreads()` after the window is part of the sequence (the next `FilterToCurrentThread` undoes it).
- Warm-up first, then `Recorder.Get`: the same order as `Raw`. Never call `Samples` inside a measured action.
- Do not change `AssertNone`'s body or messages, and keep `Allocates(action, calls)` true or false exactly as before.
- The test assembly cannot see `internal` members: every new member is public. Both overrides are statics: a test that leaves one set turns the rest of the editor run blind or blind-green; restore in `TearDown`.
- `Assert.Fail` throws NUnit's `AssertionException`; the argument errors are not assertion failures and are asserted with their exact types.
- A packet branch stays linear. A new Unity script gets its `.meta` on the first Unity run: commit it.
- P-UI-QUEST-OFFER-1 is on the base and only calls `AssertNone` (from `AbilityRowEditModeTests` and `QuestOfferEditModeTests`); `AssertNone` is unchanged, so no overlap.

## Notes from the oracle author

- **Scratch run done** (not committed): a copy of `AllocationMeterCountEditModeTests` with every `Assert.That`, `Assert.Throws<T>` and `Assert.DoesNotThrow` routed through a catch-and-collect helper, failing in `TearDown` with the list, run on the stub. Every entry was an assertion about the missing behaviour (counts of 0 against `>= k * n`, no throw from `AssertAtMost`, override not honoured, warm-up counter 0 against 14); no exception from set-up or deliveries. The witness parse works: on the stub it printed "The provided delegate made 20 GC allocation(s)." for the allocating control at 10 calls (2 allocations a call: the object and the string), and the parse takes "made N". The type is `NUnit.Framework.Internal.TextMessageWriter` (the test writes it fully qualified; `NUnit.Framework.Constraints` does not hold it in this NUnit), so the count comparison in `CountAndAnswerAgreeTwentyTimes` is kept.
- **Red on the stub**, each on an assertion: `ACleanDelegateCountsZero` (the meter counts string concatenation: expected > 0, was 0); `EveryAllocationIsCountedAtLeastOnce` (one object x10: expected >= 10, was 0); `CountAndAnswerAgreeTwentyTimes` (round 0, allocating control: count above 0 exactly when the constraint succeeds); `AssertAtMostPassesAtTheLimitAndFailsOneAbove` (the override saw the measured action once: expected 1, was 0); `AMeterThatIsBlindOrWrongFailsAsNotWorking` (blind to the allocating control: no `AssertionException`); `TheRealMeterBacksAssertAtMost` (an allocating action fails a limit of 0: no throw); `TheSeamsAreSeparate` (Samples returns the override's answer: expected 5, was 0); `TheWarmUpRunsTheActionOnceMoreFirst` (expected 14, was 0); `BadArgumentsThrowBeforeMeasuring` (a null action: no `ArgumentNullException`). Run: `AllocationMeterCountEditModeTests,AllocationMeterEditModeTests,PlayerFlowsEditModeTests,HudLegacyLayoutEditModeTests,TabMapSheetEditModeTests`: 87 tests, 78 passed, the 9 new ones red, the six `AllocationMeterEditModeTests` and the three heavy classes green.
- The test the spec calls item 10 (the old six stay green alone, with the heavy three, and in the gate's whole suite) is not a separate test: it is the existing class, run as above.
- **One flake seen on the unchanged `Raw` path (stub):** in one run of `ScratchSoftCountEditModeTests,AllocationMeterCountEditModeTests,AllocationMeterEditModeTests` together, `AllocationMeterEditModeTests.ACleanDelegateIsNotSeen` failed once on "Time.frameCount read x10000 allocates nothing" (expected false, was true). Run alone (6/6) and in the heavy run (above, and a second time) it passed. The stub did not change `Raw`, `Allocates` or the controls' behaviour, so this is an existing noise source of the older meter that the implementer should keep in mind when a 10,000-call clean cell fails once; it is not a defect in the new tests.
- Spec deviation: none in design. The commit order is brief, stub, oracle; the `.meta` of the new test file is in the oracle commit.

## Notes from previous attempts
<!-- Coordinator appends on escalation: tier, branch, the gate's failure block. Nothing else. -->
