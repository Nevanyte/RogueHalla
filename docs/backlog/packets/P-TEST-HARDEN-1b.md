# P-TEST-HARDEN-1b: plain-decode allocation tests measure the minimum of runs

**Write scope:** `client-unity/RoguehallaNet.Tests/` (`GarbageTests.cs`, `AllocationProbeTests.cs`), this brief. No library code.

**What flaked.** P-TEST-HARDEN-1 (`09c2ef05d`) pinned the plain-decode allocation tests to exact equality
(`40 * actors + 88` / `+ 176` bytes per call) on one mean over 20,000 calls. At 68 actors that window allocates about
56 MB and runs many gen0 collections; each is a chance for the block-voiding charge described in `AllocationProbe.cs`
(a one-off jump of a few KB). It only ever adds bytes. Under machine load the 68-actor case failed about one full run in five.

**Fix.** The exact-equality decode assertions measure with `AllocationProbe.PerCallMin` (minimum of several runs)
instead of one `PerCall`. A real per-call allocation is deterministic and is in every run, noise only adds, so the
minimum keeps the check exact. No tolerance, no widened expectation, no skip or retry. "Allocates nothing" tests stay
on `PerCall` with `AreEqual(0.0)`.

**Proof.** `dotnet test client-unity/RoguehallaNet.Tests --filter PlainDecode` in a doubled loop (the second copy is the
load), 15 iterations each, failures counted before and after (see the hand-off); full library suite; `gate.sh --quick`.
