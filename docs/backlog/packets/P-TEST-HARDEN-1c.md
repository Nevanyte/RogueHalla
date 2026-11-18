# P-TEST-HARDEN-1c: exact decode allocation expectations are met by any one run of a bounded number

**Write scope:** `client-unity/RoguehallaNet.Tests/` (`AllocationProbe.cs`, `AllocationProbeTests.cs`, `GarbageTests.cs`), this brief. No library code.

**What flaked.** P-TEST-HARDEN-1b measured the minimum of five runs. At machine load about 35 a one-off charge (the
collector's block-voiding charge, see `AllocationProbe.cs`) landed in all five runs: `Expected 2896.0, but was 2896.028`
(56 bytes over 2,000 calls). Noise only ever adds bytes and is occasional; a real regression is present in every run.

**Fix.** `AllocationProbe.PerCallExact(body, expectedBytesPerCall, iterations, maxRuns = 25)` measures one run at a
time and returns at once when a run equals the expectation exactly, or is below it (below is a real change and fails the
assertion). Otherwise it returns the minimum after `maxRuns`. Exact equality stays; no tolerance, retry attribute or skip.
Used by the two exact decode assertions in `AllocationProbeTests` and by `GarbageTests.DecodeGarbage`.
"Allocates nothing" tests stay on `PerCall` with `AreEqual(0.0)`: they were not seen to flake, and their cost on a
failure would be 25 runs for no gain.

**What this can hide.** It cannot hide a steady per-call allocation, or one every Nth call with N up to the iteration
count: those are in every run. It can hide an allocation that happens in fewer than all of `maxRuns` runs, a genuinely
occasional one, which the old single-mean test would have flagged as a flake rather than a finding.

**Proof.** `dotnet test ... --filter PlainDecode` in a loop of 20 under load, before and after (see the hand-off).

**Result.** The flake did not reproduce on the unfixed tree: 0 failures in 20 `PlainDecode` runs beside 4 loops, 0 of 20
beside 12 loops (load average up to 67, the 12 loops themselves 0 of 480), 0 of 20 with `DOTNET_GCHighMemPercent=0x0A`.
After the fix: 0 of 20 beside 8 loops. Runtime about 5 s idle (unfixed); about 2.8 s per run loaded (fixed). The fix rests
on the stated reasoning, not on a reproduced before/after.
