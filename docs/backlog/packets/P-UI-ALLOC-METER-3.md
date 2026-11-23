# P-UI-ALLOC-METER-3: background noise cannot fail an allocation test

- **Serves:** five sightings on 2026-11-23 of the lane's allocation meter, or Unity's own constraint, reporting an allocation that does not happen, all on a loaded machine. (1) `AllocationMeterEditModeTests.ACleanDelegateIsNotSeen` failed once on its 10,000-call clean cell in a combined run, on the older `Raw` path (the constraint itself; `docs/backlog/packets/P-UI-ALLOC-METER-2.md`, notes of the oracle author). (2) `AllocationMeterEditModeTests.ItGivesTheSameAnswerTwentyTimes` failed once in a whole-suite gate run on "round 1: adding locals is not seen", on the base `Samples` (reported by P-UI-WISH-TILE-1's implementer; not recorded in the repo). (3 to 5) `AllocationMeterCountEditModeTests.CountAndAnswerAgreeTwentyTimes` failed in three whole-suite gate runs, each time "expected 21, was 20" for the allocating control in a round that begins with `GC.Collect()` (rounds 12, 3 and 6): there the EXTRA sample was in Unity's constraint, which uses the leaking read order of section 3, and the meter's 20 was right. Every allocation test of the lane, and the coordinator's planned conversion of thirteen older classes, rest on this meter. Owner direction 2026-11-22 (UI optimization sweep). Not in ORDER_OF_OPERATIONS.
- **Implementer tier:** implementer-sonnet
- **Reviewer:** reviewer
- **Integration base:** the lane branch `lane/ui-rework/base-3` (head `4102837d4`; a later lane merge touches no meter file). Quick gate: `--oracle packet/P-UI-ALLOC-METER-3 --base lane/ui-rework/base-3`.
- **D-rules touched:** D1: test support, legacy has no counterpart and no player sees a change, so no DEVIATIONS row. D2 n/a. D5/D6 none. D9 none (no product code, no game rule).
- **Worktree:** `/Users/nevinmckinnon/Documents/Projects/RH-ui-x`

## 1. ID and outcome

`AllocationMeter.Samples` (`Assets/Tests/Editor/AllocationMeter.cs:40-54`) runs one unmeasured warm-up loop, then one measured window: it disables the `GC.Alloc` recorder, `FilterToCurrentThread()`, enables it, runs the loop, disables it, calls `CollectFromAllThreads()` (`:52`) and only then reads `sampleBlockCount` (`:53`). That is the order of Unity's `AllocatingGCMemoryConstraint` (its lines 55 to 80, in `client-unity/UnityProject/Library/PackageCache/com.unity.test-framework@082c24152fd4/UnityEngine.TestRunner/Assertions/AllocatingGCMemoryConstraint.cs`). One sample in that one window is a verdict. `Allocates` (`:88-92`) is `Samples(...) > 0`. `AssertNone` (`:97-111`) asks `Allocates` three questions: the allocating control (must be yes), the clean control (must be no; else "the meter is not working") and the action. A stray sample in the clean control's window fails an innocent test; one in the action's window says that clean code allocates. The lane has 22 `AssertNone` calls in 14 classes besides the meter's own three (`AbilityRowNearbyNoGarbage` has 7; the others are `ToastLanes`, `QuestTracker`, `QuestOffer`, `WishTile`, `OwnerVitalEase`, `AbilityRow`, `UiMotion`, `TabMapHeaderNoGarbage`, `TabMapMarks`, `MapOpenWorld`, `ContactRailWords`, `CampLabelStable`, `ZoneNames`, each with the suffix `EditModeTests`).

When this packet lands, the meter reads the recorder in an order that does not let other threads' samples in, and it never reports an allocation that did not show in every one of up to three measurements. Numbered rules (all in `AllocationMeter.cs`):

1. **The read order (the cure, part one).** `Samples` never calls `CollectFromAllThreads()`. A measurement is: `Recorder.Get("GC.Alloc")`, `enabled = false`, `FilterToCurrentThread()`, `enabled = true`, the measured loop, `enabled = false`, read `sampleBlockCount`. The recorder stays filtered to the calling thread afterwards; the doc comment says so and why it is safe: every other user of this recorder sets its own filter before it measures. Read in `AllocatingGCMemoryConstraint.cs` lines 55 to 66: it disables, calls `FilterToCurrentThread()`, then enables, on every use. Nothing else under `Assets` touches this recorder (grep: only `AllocationMeter.cs`); the Collections package test that uses it filters before it enables; the Performance package uses `ProfilerRecorder`.
2. **The rule (the cure, part two).** `Samples(action, calls)` takes a first measurement; if it reads 0 it returns 0 at once. Otherwise it measures again, up to `MaxMeasurements = 3` measurements in all, stopping early at a 0, and returns the SMALLEST count read. The read order alone left about 1 window in 100 off by one under a hot noise thread; with both, all three measurements of clean code err together about once in a million there.
3. **Warm-up once.** The unmeasured loop runs once, before the first measurement only: `action` runs `2 * calls` times when the first window reads 0 (as today), else `calls * (1 + k)` for k windows (at most `4 * calls`).
4. **Callers follow from it.** `Allocates` is true when the smallest count is above 0 (so an allocation must show in every measurement to be reported). `AssertNone` fails when it is above 0. `AssertAtMost` compares the smallest count with `maxSamples`. The self-checks are unchanged in text and, through `Samples`, mean: the allocating control reads above 0 in every one of its measurements, the counting control reads at least `ControlCalls * CountingAllocationsPerCall` (30) in every one, the clean control reads 0 in at least one. Only `AssertAtMost` measures the counting control.
5. **`AssertNone` names the count.** When `MeterOverride` is null its failure for the action reads `<what> allocated managed memory in <calls> calls (<n> GC.Alloc samples in its quietest measurement; meter <Meter>)`, `n` being the smallest count, got from ONE call of `Samples` for the action (built by concatenation as today). When `MeterOverride` is set only the yes-or-no is known and today's message stays (`<what> allocated managed memory in <calls> calls (meter <Meter>)`). No test pins this message (grep). `AssertAtMost`'s messages are unchanged (the count tests pin substrings).
6. **Seams.** `SamplesOverride` is called once per measurement, with the action and `calls`, in place of the window (no loop runs, so no warm-up either); a test tells measurements apart by letting its override count its own calls, per action by `ReferenceEquals` to the public controls. `MeterOverride` keeps its meaning: it answers `Allocates` once per question, no retry, and never reaches `Samples` or `AssertAtMost`.
7. **The limit, in the doc comments and here.** An allocation that does not happen in every window of `calls` calls is not reported: code that allocates at least once in every `calls` consecutive calls is reported every time; code that allocates once per many calls, once per second, or only in some windows is not. Anything rarer than the window already slipped through; the rule widens it for random allocations. Worked, as the price: an allocation on each call with probability 1/100, `calls` 200: one window reports it in 87 runs of 100 (1 - .99^200), three windows in about 65 (.866^3). The doc comment adds: the meter counts the calling thread only, by design.
8. **The meter's name.** `Meter` reads "GC.Alloc profiler samples (UnityEngine.Profiling.Recorder, calling thread only)"; the old "the calls of AllocatingGCMemoryConstraint" is no longer true. Tests read the property.

## 2. Dependencies

`lane/ui-rework/base-3` only: `AllocationMeter.cs` (P-UI-ALLOC-METER-1 and -2) is on it.

## 3. Relevant legacy paths

none (test support in the Unity client; no legacy behaviour).

### Cause established

Reading `sampleBlockCount` after `CollectFromAllThreads()` lets other threads' samples in. Evidence: the lane lead's experiment of 2026-11-23 (editor 6000.0.85f2, 500 rounds per cell; files `.agent-runs/lane-ui-rework/understand/meter-experiment/` in the main checkout: `README.txt`, `table200.txt`, `run2.out`, `Scratch.cs.txt`; not in the repo, so the table is copied here). It compared four ways to read the recorder: A the order of today (`:46-53`); B as A but reading `sampleBlockCount` BEFORE `CollectFromAllThreads()`; C never calling `CollectFromAllThreads()` in the measurement; D no thread filter. The clean loop is 1000 calls that add to a static int (must read 0); the allocating loop is 10 calls of two allocations (must read 20). Rounds whose count was wrong, clean / allocating:

| condition (500 rounds) | A (today) | B | C (the cure) | D (no filter) |
|---|---|---|---|---|
| quiet machine | 0 / 0 | 0 / 0 | 0 / 0 | 2 / 1 |
| one background thread allocating all the time | 206 / 107 | 61 / 20 | 6 / 1 | 500 / 500 |
| 2000 finalizable objects dropped and `GC.Collect()` | 2 / 0 | 0 / 0 | 0 / 0 | 0 / 0 |

The thread filter works (D is wildly off). Under the hot thread 41 percent of A's clean windows read above 0 (up to 5 too many) and 21 percent of its allocating windows read 21 for 20. C leaves 6 of 500 and 1 of 500, at most 1 sample, and the windows that still erred were the first after a switch back from all-thread collection. On a quiet machine every variant but D is exact. The experiment did not test the first-ever `Recorder.Get` of an editor session under C. The oracle author did not repeat the experiment and ran no CPU burners or load.

## 4. New paths

- `client-unity/UnityProject/Assets/Tests/Editor/AllocationMeterNoiseEditModeTests.cs` (+ `.meta`): the oracle.

## 5. Exclusive write scope

- `client-unity/UnityProject/Assets/Tests/Editor/AllocationMeter.cs` (stubbed by the oracle author: only `MaxMeasurements`; the implementer does the rest). No product code, no other test helper.
- No other existing file. The packet document, the new test class and the two amended expectations (below) are oracle files, read-only for the implementer.
- Not in scope: `AllocationMeterEditModeTests.cs` (green, untouched), the 14 classes that call `AssertNone`, the thirteen older classes, `Assets/Scripts`, `COMMON.md` (the hand-off proposes for it: "the helper runs the action 2 to 4 times `calls`; a stateful rig advances up to four times as far").

## 6. Contracts and references

- Lane rules `.agent-runs/lane-ui-rework/specs/COMMON.md` (allocation fact; a packet branch stays linear).
- `docs/backlog/packets/P-UI-ALLOC-METER-1.md`, `P-UI-ALLOC-METER-2.md` (the meter this repairs).

## 7. Behaviour to preserve

- Public API after the change (everything public, no `InternalsVisibleTo`):

```csharp
public static class AllocationMeter   // namespace Roguehalla.Client.Tests
{
    public const int ControlCalls = 10, CountingAllocationsPerCall = 3;   // as today
    public const int MaxMeasurements = 3;                                 // new
    public static Func<Action, int, bool> MeterOverride;                  // as today
    public static Func<Action, int, int> SamplesOverride;                 // now called once per measurement
    public static readonly Action AllocatingControl, CleanControl, CountingControl;   // as today
    public static int Samples(Action action, int calls);   // the smallest of up to MaxMeasurements counts
    public static bool Allocates(Action action, int calls);   // MeterOverride, else Samples(...) > 0
    public static void AssertNone(Action action, int calls, string what);
    public static void AssertAtMost(Action action, int calls, int maxSamples, string what);   // body as today
    public static string Meter { get; }                                   // text changed (rule 8)
}
// private static int Measure(Action action, int calls, bool warmUp): the override if set (before the null check);
// else null check, build `loop` BEFORE Recorder.Get, `if (warmUp) loop();`, then the sequence of rule 1
// (`finally { recorder.enabled = false; }`, then `return recorder.sampleBlockCount;`).
// Samples: s = Measure(.., true); for (m = 1; m < MaxMeasurements && s > 0; m++)
//     s = Math.Min(s, Measure(.., false));
```

- `AssertAtMost`: argument checks first (null `action` throws `ArgumentNullException`; `calls < 1` or `maxSamples < 0` throws `ArgumentOutOfRangeException`), nothing measured before them; self-check texts and its failure text unchanged.
- `MeterOverride` wins in `Allocates` and never reaches `Samples`/`AssertAtMost`; both overrides are null in play.

## 8. Acceptance checks

- `scripts/gate.sh --quick --oracle packet/P-UI-ALLOC-METER-3 --base lane/ui-rework/base-3` passes.
- Unity classes `AllocationMeterNoiseEditModeTests`, `AllocationMeterCountEditModeTests`, `AllocationMeterEditModeTests` all green.
- Traps below are kept.

### Existing tests that pin this code (whole of both old classes read)

`AllocationMeterEditModeTests` (six tests): `AnAllocatingDelegateIsSeen` (Concat, NewObject, Boxing, ListGrowth give `Allocates` true at 1, 100, 10000 calls); `ACleanDelegateIsNotSeen` (Concat seen; AddLocals, StructField, ReadFrameCount not, at 1, 100, 10000); `ItGivesTheSameAnswerTwentyTimes` (20 rounds, with `GC.Collect()`); `AssertNoneFailsOnAnAllocatingDelegate` (throws naming the `what`; passes for AddLocals); `ABlindMeterFailsTheTest` (`MeterOverride` false: "the meter is not working"); `TheMeterHasAName` (not null, not empty, not "none"). All stay green and untouched.

`AllocationMeterCountEditModeTests` (nine tests):
- `ACleanDelegateCountsZero`, `EveryAllocationIsCountedAtLeastOnce` (k*n lower bounds, `Samples(Concat, 0)` is 0, counting control at least 30): untouched, green.
- `CountAndAnswerAgreeTwentyTimes` (`:76-102`): compares `Samples` with ONE run of the raw constraint, which reads in the leaking order, so its count can be one too high (sightings 3 to 5). **Amended** (see below).
- `AssertAtMostPassesAtTheLimitAndFailsOneAbove` (`:118-133`): asserts `measuredSeen == 1` at the limit (`:124`) and above it (`:132`). The override reads 137, above 0, so with the rule it is called three times. **Amended** (see below).
- `AMeterThatIsBlindOrWrongFailsAsNotWorking`, `TheRealMeterBacksAssertAtMost`, `TheSeamsAreSeparate` (override reads 5, the action never runs), `BadArgumentsThrowBeforeMeasuring`: untouched, green on the stub and on the cure.
- `TheWarmUpRunsTheActionOnceMoreFirst` (`:192-197`): the counter reads 14 for `Samples(CountCalls, 7)`; holds whenever a clean action's first window reads 0. Not amended.

### Amended expectations (for the owner)

Each in its own `oracle-amend:` commit, nothing else in the test changes.
1. `AssertAtMostPassesAtTheLimitAndFailsOneAbove`: lines 124 and 132 expect 3 instead of 1 (messages: "once per measurement, three"). Why: the override reads 137 for the action, so the rule measures it three times.
2. `CountAndAnswerAgreeTwentyTimes`: lines 88 to 93 (from `var result = ...` through `int witnessCount = ...`) become a loop of three raw-constraint runs, keeping the smallest count as witness, and the first two asserts (lines 96, 97) use `witnessSuccess` (true only if all three runs say "allocating") in place of `result.IsSuccess`. Why: the raw constraint reads in the leaking order, so one run can be one too high.

## Oracle files (read-only for implementers)

- `client-unity/UnityProject/Assets/Tests/Editor/AllocationMeterNoiseEditModeTests.cs`
- `client-unity/UnityProject/Assets/Tests/Editor/AllocationMeterNoiseEditModeTests.cs.meta`
- `client-unity/UnityProject/Assets/Tests/Editor/AllocationMeterCountEditModeTests.cs` (the two amendments only)
- `docs/backlog/packets/P-UI-ALLOC-METER-3.md`

## 9. Validation commands

- `.agent-runs/lane-ui-rework/bin/unity-class.sh /Users/nevinmckinnon/Documents/Projects/RH-ui-x AllocationMeterNoiseEditModeTests,AllocationMeterCountEditModeTests,AllocationMeterEditModeTests`
- `.agent-runs/lane-ui-rework/bin/in.sh /Users/nevinmckinnon/Documents/Projects/RH-ui-x scripts/gate.sh --quick --oracle packet/P-UI-ALLOC-METER-3 --base lane/ui-rework/base-3`

## 10. Risk and suggested model tier

implementer-sonnet; one test-support file, about 60 lines. Hard part: the recorder sequence must be exact (traps).

## 11. Known traps

- Each measurement is a whole recorder sequence: never keep the recorder enabled across windows, build `loop` before `Recorder.Get` in each, and put no string or closure between `enabled = true` and the `finally`. Never call `CollectFromAllThreads()` inside `Samples`.
- `AssertNone` keeps its self-checks on `Allocates` (so `MeterOverride` reaches them); only the action's question moves to ONE call of `Samples` when `MeterOverride` is null.
- Cost: `AssertNone` on clean code is five windows (allocating control 3, clean control 1, the action 1); an allocating action under `AssertAtMost` takes three: up to `4 * calls` runs.
- All new members are public; both overrides are statics: restore them in `TearDown`.
- Not established: `TheWarmUpRunsTheActionOnceMoreFirst` can read 21 or 28 if all else fails. The experiment ran variant A first in every round, so the first `Recorder.Get` of an editor session under the cure is unmeasured. Test 9 (real thread noise) has its own false-red chance of about one run in ten thousand with the cure.

## Notes from the oracle author

Tests of `AllocationMeterNoiseEditModeTests` (ten, as the lane spec lists them, names unchanged): all ten are red on the stub (today's single window), each on an assertion about the missing behaviour, measured by `unity-class.sh` on 6000.0.85f2:

| test | first red assertion on the stub |
|---|---|
| `AOneOffSampleThatDoesNotRepeatIsDropped` | `Samples(Probe, 100)` expected 0, was 1 |
| `AnAllocationInEveryMeasurementGivesTheSmallestCount` | smallest of 2,3,1: expected 1, was 2 |
| `ThereAreNeverMoreThanThreeMeasurements` | measured exactly three times: expected 3, was 1 (`MaxMeasurements` is in the stub, so its first assert is green) |
| `AssertAtMostComparesTheSmallestCount` | `{2,3,1}` at limit 1: threw "made 2 GC.Alloc samples ... above the limit of 1" |
| `AnAllocatingOrCleanControlThatFailsInAnyMeasurementIsNotWorking` | allocating `{20,0,20}` under `AssertNone`: expected `AssertionException`, was null (the clean `{2}` half is green on the stub, as the spec says) |
| `ACountingControlThatReadsLowInAnyMeasurementIsNotWorking` | `{30,29,30}` under `AssertAtMost`: expected `AssertionException`, was null (the `{29,30,30}` half is green on the stub) |
| `ANoisyCleanControlThatReadsZeroOnceIsSound` | `AssertNone` with clean `{1,0}` threw "it reported allocation for a known clean control" |
| `TheYesOrNoSeamIsAskedOncePerQuestion` | `Samples(Probe, 100)` expected 0, was 1 |
| `ABusyThreadNextDoorCannotFoolTheMeter` | on the first run "round 13: clean code read 1 samples under a busy thread" |
| `TheWarmUpRunsOnceAndEachMeasurementRunsTheLoop` | counter expected 28, was 14 |

Scratch run (catch-and-collect helper, not committed, deleted with its `.meta`): every assertion of the nine scripted and counting tests ran on the stub, none of the collected entries was anything but an assertion about the missing behaviour (counts, "measured three times", the quietest-count message, "not working" throws); lines that follow a failed `Assert.Throws` read an empty message and fail as derived entries, which is expected. In test 9 the stub read wrong in 9 of 100 rounds (8 clean windows with 1 extra sample, 1 allocating window with 21), no set-up fault. After the cure, the lines past the first red assertion are consistent with the packet's rules (worked through by hand: `AssertNone` on clean code = allocating control 3, clean 1, probe 1 measurements; `AssertAtMost` = 3, 1, 3, probe 1 to 3).

Existing classes on the stub: `AllocationMeterEditModeTests` six of six green; `AllocationMeterCountEditModeTests` eight of nine green and `CountAndAnswerAgreeTwentyTimes` failed once in this very run ("round 12, allocating control ... expected 21, was 20", the sighting of section 1, the constraint's extra sample): the amendment below is the cure for exactly that. `AssertAtMostPassesAtTheLimitAndFailsOneAbove` is green on the stub and turns red on the cure unless amended (it reads 1 only while one window is made).

Deviations from the spec: none. The scripted `Source` is a small private class of the test; the probe is an empty static `Action` that is never run.
