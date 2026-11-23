# P-TEST-HARDEN-1d: why `PlainDecodeAllocatesExactly...(68)` failed once under load (cause NOT established)

**ID and outcome.** Find the cause of two whole-assembly failures of `AllocationProbeTests.PlainDecodeAllocatesExactlyTheSnapshotItsSelfAndOneActorArrayInEveryRun(68)`
and harden the test without loosening it. Outcome so far: one hypothesis refuted with numbers; no fix shipped (a fix for an unshown cause is not wanted).

**Dependencies.** P-TEST-HARDEN-1, -1b, -1c (`AllocationProbe.PerCall`, `PerCallMin`, `PerCallExact`, `Noise`).

**Relevant legacy paths.** None (test infrastructure).

**New paths.** None. Write scope if a cause is found: `client-unity/RoguehallaNet.Tests/AllocationProbe.cs`, `AllocationProbeTests.cs`, this brief. No library code.

**The failures.** Run 0, spectator measurement, 68 actors: `Expected 2808, was 2808.084` (168 B over 2000 calls) in the gate of 2026-11-23 on `1bd9cc1dd` (2507 tests, load 10-20), and `2808.032` (64 B) in another session's gate (2543 tests, load 9-25). `PerCallExact` returned the minimum of 25 attempts, so every one of the 25 attempts of run 0 was above 2808.

**Evidence gathered (all with a scratch edit that recorded per-attempt bytes and `GC.CollectionCount(0/1/2)` around the window; reverted, not committed).**
- Class alone, 100 runs beside 24 `yes` processes (load 12-59): 0 failures.
- Whole assembly beside up to 24 `yes`: the loop was stopped before it produced a result. Whole assembly, no extra load, 3 traced runs: run 0 exact on the first attempt, with 111-186 gen0 collections inside a window.
- Hypothesis "each collection inside the window charges the measuring thread a few bytes" is REFUTED. Forced collections (`GC.Collect(0/1/2)` loops on 3 noise threads, 15 attempts each): windows saw 233 to 6413 collections (up to 308 to 5110 gen1 and 233 to 3244 gen2) and the excess was 0 B in every attempt. Ordinary noise: 29 to 58 collections per window, excess 0.
- `DOTNET_GCHighMemPercent=0x0A` (the setting that reproduced the block-voiding charge in `GarbageTests`): 3 runs of the (1)/(68) cases and 3 of a 60-attempt trace, 0 B excess in all 60 attempts of each.
- Only non-zero excess seen: the first window of the first measurement in a fresh process (`ScratchEarly` as the first test, 6 of 6 processes, exactly +32 B over 2000 calls, then 0 for the next 59 attempts), with default tiering, `TieredPGO=0` and `TieredCompilation=0` alike, so it is not JIT tiering. It did not appear when the decode tests ran first under `GCHighMemPercent`, so it is an early-process one-off, not the failures' signature (the failures had 25 consecutive attempts above, after tests had already run in the process).

**Cause.** Not established. Unverified candidates, none shown: a charge tied to process age or heap state in the full assembly (not reproduced in 3 whole-assembly runs); `GC.Collect` retiring a block under real memory pressure from other processes (the earlier P-TEST-HARDEN-1c explanation; not reproduced here either).

**Change (diagnostic only).** `AllocationProbe.PerCallExact` behaves and returns exactly as before, and now also records, per attempt, bytes per call, collections of generations 0/1/2 across the window, the measuring thread id and time since the first attempt, and at the end the collector memory load against its high-load threshold and the process age. `AllocationProbe.LastAttempts` (thread-local) returns it as one string; the exact assertions in `AllocationProbeTests` (both) and `GarbageTests.PlainDecodeAllocatesOnlyTheSnapshotItsSelfAndOneActorArray` append it to their message. No expectation, tolerance or retry changed (`oracle-amend:` commit). Example, from a scratch mutation that raised the asserted number by 24 (reverted): `run 0, no self; attempts (expected 2808; collector memory load 53739 of 58982 MB high-load threshold, process age 454 ms at the end): #0 2808 B/call gc0/1/2 51/0/0 thread 25 t+33 ms;`. The cause is still not established; the next real failure will list all 25 attempts.

**Trap found while building it.** A first version that called `Process.GetCurrentProcess()` and `GC.GetGCMemoryInfo()` and formatted a string between attempts failed the 68-actor test in 6 of about 19 whole-assembly runs on an idle-ish machine (every attempt 64 to 2600 B over per window, 38 gen0 / 3 gen1 / 1 gen2 per window, memory load 55 to 58 of 59 GB), where the unchanged probe failed 0 of 12 and the final version 0 of 8 plus 3 green. The final version records plain numbers and formats once at the end. This shows that allocation or finalizable objects created between attempts can produce exactly the failure signature (excess in every attempt, run 0, 68 actors) and so is a lead for the original failure: something in the process that churns the heap between or during attempts. Unproven: the first version's per-failure logs are the only data (kept in this paragraph's numbers, not as files).

**Previously: Change.** None committed. Not shipping a collection-count filter: the data show collections do not charge the thread.

**Before and after rates.** Before: 2 failures seen in the gate, 0 in 100 class runs under load, 0 in 3 whole-assembly runs. After: n/a. Mutation check: not applicable to a cause fix; the diagnostic was proven by the scratch mutation above.

**Other exact-allocation tests in the assembly with the same shape.** `GarbageTests.PlainDecodeAllocatesOnlyTheSnapshotItsSelfAndOneActorArray` (via `DecodeGarbage`, `PerCallExact`, 1/14/68 actors); the two `PerCall` exact tests in `AllocationProbeTests` (`ABodyThatAllocatesOneObject...`, `AnEmptyBody...`); `RemoteDrawTests` (line ~241) and `FrostWallWalkTests` (line ~249) use single-run `PerCall`. Unchanged.

**Next step suggested.** Capture rather than guess: make `PerCallExact`'s failure message carry the per-attempt values and collection counts so the next gate failure explains itself (a test-side diagnostic only, loosening nothing).

**Validation commands.** `dotnet test client-unity/RoguehallaNet.Tests --filter FullyQualifiedName~AllocationProbeTests`.

**Risk and tier.** Low; test side only; Sonnet.

**Known traps.** Machine load from other sessions reached 40-60 during this work; whole-assembly loops took minutes each.
