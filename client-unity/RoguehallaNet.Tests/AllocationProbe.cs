using System;
using System.Runtime.ExceptionServices;
using System.Diagnostics;
using System.Threading;

namespace Roguehalla.Net.Tests
{
    /// <summary>
    /// Allocation measurement for tests (P-TEST-HARDEN-1). Counts the calling worker thread only
    /// (<c>GC.GetAllocatedBytesForCurrentThread</c>), so other threads cannot add to a reading.
    ///
    /// How to write an allocation test: put the steady-state work in a body <c>i =&gt; ...</c> (keep results in a field or
    /// local that the body writes, so the work is not elided), call <see cref="PerCall"/> for an exact figure or
    /// <see cref="PerCallMin"/> when asserting a bound, and assert on the result (<c>Assert.AreEqual(0.0, ...)</c> for
    /// "allocates nothing"). Warm-up (JIT, first-use statics) is done here; do not allocate setup data inside the body.
    /// </summary>
    public static class AllocationProbe
    {
        public const int DefaultIterations = 20000;

        /// <summary>Leaves the calling thread holding no allocation block, so a measured window counts only what its body allocates.
        /// The per-thread counter is "bytes handed to the thread minus the unused rest of its current block". When the collector
        /// considers memory load high it can void a thread's block between collections, and the unused rest is then charged to the
        /// thread: one jump of a few kilobytes at a random call, with nothing allocated by the body. After a collection the
        /// thread holds no block, so there is no rest to charge; a real allocation is still counted in full.</summary>
        static void RetireAllocationBlock() => GC.Collect();

        /// <summary>Bytes allocated per call of <paramref name="body"/>, after 200 warm-up calls, on one dedicated worker thread.</summary>
        public static double PerCall(Action<int> body, int iterations = DefaultIterations) => PerCallWindow(body, iterations, out _);

        /// <summary>One measured window; <paramref name="window"/> receives collections of generations 0, 1, 2 across it and the measuring thread id.</summary>
        static double PerCallWindow(Action<int> body, int iterations, out int[] window)
        {
            double result = 0;
            int[] w = new int[4];
            window = w;
            ExceptionDispatchInfo failure = null;
            var worker = new Thread(() =>
            {
                try
                {
                    // Counter first-use and caller/NUnit execution context are outside the measured work.
                    GC.GetAllocatedBytesForCurrentThread();
                    for (int i = 0; i < 200; i++) body(i);
                    RetireAllocationBlock();
                    int c0 = GC.CollectionCount(0), c1 = GC.CollectionCount(1), c2 = GC.CollectionCount(2);
                    long before = GC.GetAllocatedBytesForCurrentThread();
                    for (int i = 200; i < 200 + iterations; i++) body(i);
                    long after = GC.GetAllocatedBytesForCurrentThread();
                    w[0] = GC.CollectionCount(0) - c0; w[1] = GC.CollectionCount(1) - c1; w[2] = GC.CollectionCount(2) - c2;
                    w[3] = Thread.CurrentThread.ManagedThreadId;
                    result = (after - before) / (double)iterations;
                }
                catch (Exception error)
                {
                    failure = ExceptionDispatchInfo.Capture(error);
                }
            });
            if (ExecutionContext.IsFlowSuppressed()) worker.Start();
            else
            {
                using (ExecutionContext.SuppressFlow()) worker.Start();
            }
            worker.Join();
            failure?.Throw();
            return result;
        }

        /// <summary>For an exact expectation: measures one run at a time and returns as soon as a run equals
        /// <paramref name="expectedBytesPerCall"/> exactly (or is below it, which is a real change and fails the caller's
        /// assertion); after <paramref name="maxRuns"/> runs returns the minimum seen. Collector noise only adds bytes and is
        /// occasional, a real allocation is in every run. Cannot hide a steady per-call allocation, or one every Nth call
        /// with N up to the iteration count (present in every run). Can hide an allocation that occurs in fewer than all of
        /// <paramref name="maxRuns"/> runs, a genuinely occasional one, which a single-mean test would report as a flake.</summary>
        public static double PerCallExact(Action<int> body, double expectedBytesPerCall, int iterations = DefaultIterations, int maxRuns = 25)
        {
            double best = double.MaxValue;
            // Recorded as plain numbers while measuring and formatted once at the end, so the diagnostic adds no allocation or
            // finalizable object between attempts.
            var bytes = new double[maxRuns];
            var windows = new int[maxRuns][];
            var ages = new long[maxRuns];
            long start = Stopwatch.GetTimestamp();
            int done = 0;
            try
            {
                for (int r = 0; r < maxRuns; r++)
                {
                    double got = PerCallWindow(body, iterations, out windows[r]);
                    bytes[r] = got;
                    ages[r] = Stopwatch.GetTimestamp();
                    done = r + 1;
                    if (got <= expectedBytesPerCall) return got;
                    best = Math.Min(best, got);
                }
                return best;
            }
            finally
            {
                var info = GC.GetGCMemoryInfo();
                var log = new System.Text.StringBuilder("attempts (expected " + expectedBytesPerCall + "; collector memory load " + (info.MemoryLoadBytes >> 20) + " of "
                    + (info.HighMemoryLoadThresholdBytes >> 20) + " MB high-load threshold, process age " + (long)(DateTime.Now - Process.GetCurrentProcess().StartTime).TotalMilliseconds + " ms at the end): ");
                for (int r = 0; r < done; r++)
                    log.Append(string.Format(System.Globalization.CultureInfo.InvariantCulture, "#{0} {1:R} B/call gc0/1/2 {2}/{3}/{4} thread {5} t+{6} ms; ", r, bytes[r],
                        windows[r][0], windows[r][1], windows[r][2], windows[r][3], (ages[r] - start) * 1000 / Stopwatch.Frequency));
                lastAttempts.Value = log.ToString();
            }
        }

        static readonly ThreadLocal<string> lastAttempts = new ThreadLocal<string>(() => "");
        /// <summary>For the calling thread's most recent <see cref="PerCallExact"/>: every attempt's bytes per call, collection counts of
        /// generations 0, 1 and 2 across its window, the measuring thread id and its time since the first attempt, and at the end the
        /// collector's memory load against its high-load threshold and the process age. Append it to the assertion message so a failure
        /// explains itself.</summary>
        public static string LastAttempts => lastAttempts.Value;

        /// <summary>The minimum of <paramref name="runs"/> measurements.</summary>
        public static double PerCallMin(Action<int> body, int runs = 5, int iterations = DefaultIterations)
        {
            double best = double.MaxValue;
            for (int r = 0; r < runs; r++) best = Math.Min(best, PerCall(body, iterations));
            return best;
        }
    }
}
