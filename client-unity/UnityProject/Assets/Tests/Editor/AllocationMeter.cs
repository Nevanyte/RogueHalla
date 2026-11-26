using System;
using NUnit.Framework;
using UnityEngine.Profiling;

namespace Roguehalla.Client.Tests
{
    /// <summary>Says whether a piece of code allocates managed memory, with a meter that this editor is proven to
    /// honour (GC.GetAllocatedBytesForCurrentThread reads 0 here). It counts GC.Alloc profiler samples through
    /// the Recorder, so a collection during the loop cannot hide growth. Warm the code up before
    /// you measure it, and build each delegate once outside the measured call: a closure allocates when it is created,
    /// not when it is called.</summary>
    public static class AllocationMeter
    {
        /// <summary>Test seam, null in normal use. When set it replaces the meter: it gets the action and the call count and answers "allocated".</summary>
        public static Func<Action, int, bool> MeterOverride;

        static int s_sinkInt;
        static object s_sinkObj;
        static string s_a = "alpha", s_b = "beta";
        static string s_sinkStr;

        // Controls are static delegates, built once: a closure allocates when it is created.
        public const int ControlCalls = 10, CountingAllocationsPerCall = 3;
        public const int MaxMeasurements = 3;
        public static readonly Action AllocatingControl = () => { s_sinkObj = new object(); s_sinkStr = s_a + s_b; };
        public static readonly Action CleanControl = () => { s_sinkInt += 3; };
        static object s_sinkObj2, s_sinkObj3;
        public static readonly Action CountingControl = () => { s_sinkObj = new object(); s_sinkObj2 = new object(); s_sinkObj3 = new object(); };

        /// <summary>Test seam, null in normal use. When set, Samples returns its answer and runs nothing.</summary>
        public static Func<Action, int, int> SamplesOverride;

        /// <summary>Counts the GC.Alloc profiler samples recorded on the calling thread (by design: other threads are
        /// never counted) while action runs `calls` times, and returns the SMALLEST count of up to MaxMeasurements
        /// measurements, stopping at the first 0. An allocation is therefore reported only if it shows in every
        /// measurement. The unit is a sample: one per allocation (measured on 6000.0.85f2); never assert equality,
        /// and a byte budget has no conversion. The action runs `calls` times unmeasured once (warm-up, before the
        /// first measurement only), then `calls` times per measurement: 2 * calls runs when the first window reads 0,
        /// at most 4 * calls. The recorder is read without CollectFromAllThreads(), which would let other threads'
        /// samples in, and stays filtered to the calling thread afterwards: every other user of this recorder
        /// disables, filters and then enables before it measures (as AllocatingGCMemoryConstraint does).
        /// The limit: code that allocates at least once in every `calls` consecutive calls is reported every time;
        /// code that allocates once per many calls, once per second, or only in some windows is not. Random
        /// allocations are reported less often: with probability 1/100 per call and 200 calls, one window reports
        /// 87 runs in 100 and three windows about 65.</summary>
        public static int Samples(Action action, int calls)
        {
            int s = Measure(action, calls, true);
            for (int m = 1; m < MaxMeasurements && s > 0; m++)
                s = Math.Min(s, Measure(action, calls, false));
            return s;
        }

        static int Measure(Action action, int calls, bool warmUp)
        {
            var over = SamplesOverride;
            if (over != null) return over(action, calls);
            if (action == null) throw new ArgumentNullException(nameof(action));
            Action loop = () => { for (int i = 0; i < calls; i++) action(); };
            if (warmUp) loop();
            var recorder = Recorder.Get("GC.Alloc");
            recorder.enabled = false;
            recorder.FilterToCurrentThread();
            recorder.enabled = true;
            try { loop(); }
            finally { recorder.enabled = false; }
            return recorder.sampleBlockCount;
        }

        /// <summary>Fails the running test when action makes more than maxSamples GC.Alloc samples in `calls`
        /// calls (one sample per allocation, measured on 6000.0.85f2; samples are allocations, not bytes, and a
        /// byte budget has no conversion). Before it measures, it checks itself with Samples: the allocating
        /// control must register, the clean control must not, and the counting control must give at least
        /// ControlCalls * CountingAllocationsPerCall, each in every measurement (see Samples); otherwise the test
        /// fails as "the meter is not working". The action runs 2 to 4 times `calls`; the smallest count is compared.</summary>
        public static void AssertAtMost(Action action, int calls, int maxSamples, string what)
        {
            if (action == null) throw new ArgumentNullException(nameof(action));
            if (calls < 1) throw new ArgumentOutOfRangeException(nameof(calls));
            if (maxSamples < 0) throw new ArgumentOutOfRangeException(nameof(maxSamples));
            if (Samples(AllocatingControl, ControlCalls) <= 0)
            {
                Assert.Fail("the meter is not working (" + Meter + "): it did not see a known allocating control, so '" + what + "' proves nothing");
            }
            if (Samples(CleanControl, ControlCalls) != 0)
            {
                Assert.Fail("the meter is not working (" + Meter + "): it reported allocation for a known clean control, so '" + what + "' proves nothing");
            }
            int counted = Samples(CountingControl, ControlCalls);
            if (counted < ControlCalls * CountingAllocationsPerCall)
            {
                Assert.Fail("the meter is not working (" + Meter + "): it counted " + counted + " samples for a control that makes " + (ControlCalls * CountingAllocationsPerCall) + " allocations, so '" + what + "' proves nothing");
            }
            int n = Samples(action, calls);
            if (n > maxSamples)
            {
                Assert.Fail(what + " made " + n + " GC.Alloc samples in " + calls + " calls, above the limit of " + maxSamples + " (meter " + Meter + ")");
            }
        }

        /// <summary>True when calling action `calls` times allocated managed memory.</summary>
        public static bool Allocates(Action action, int calls)
        {
            var over = MeterOverride;
            return over != null ? over(action, calls) : Samples(action, calls) > 0;
        }

        /// <summary>Fails the running test, naming `what`, when action allocates in `calls` calls. Before it
        /// measures, it checks itself: a known allocating control must register and a known clean control must
        /// not; if either check fails the test is failed as "the meter is not working", never passed.</summary>
        public static void AssertNone(Action action, int calls, string what)
        {
            if (!Allocates(AllocatingControl, 10))
            {
                Assert.Fail("the meter is not working (" + Meter + "): it did not see a known allocating control, so '" + what + "' proves nothing");
            }
            if (Allocates(CleanControl, 10))
            {
                Assert.Fail("the meter is not working (" + Meter + "): it reported allocation for a known clean control, so '" + what + "' proves nothing");
            }
            if (MeterOverride != null)
            {
                if (Allocates(action, calls))
                    Assert.Fail(what + " allocated managed memory in " + calls + " calls (meter " + Meter + ")");
                return;
            }
            int n = Samples(action, calls);
            if (n > 0)
            {
                Assert.Fail(what + " allocated managed memory in " + calls + " calls (" + n + " GC.Alloc samples in its quietest measurement; meter " + Meter + ")");
            }
        }

        /// <summary>The name of the meter in use, for failure messages and the packet's table.</summary>
        public static string Meter => "GC.Alloc profiler samples (UnityEngine.Profiling.Recorder, calling thread only)";
    }
}
