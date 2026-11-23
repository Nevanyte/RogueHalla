using System;
using NUnit.Framework;
using UnityEngine.Profiling;

namespace Roguehalla.Client.Tests
{
    /// <summary>Says whether a piece of code allocates managed memory, with a meter that this editor is proven to
    /// honour (GC.GetAllocatedBytesForCurrentThread reads 0 here). It counts GC.Alloc profiler samples through
    /// UnityEngine.TestTools.Constraints, so a collection during the loop cannot hide growth. Warm the code up before
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
        public static readonly Action AllocatingControl = () => { s_sinkObj = new object(); s_sinkStr = s_a + s_b; };
        public static readonly Action CleanControl = () => { s_sinkInt += 3; };
        static object s_sinkObj2, s_sinkObj3;
        public static readonly Action CountingControl = () => { s_sinkObj = new object(); s_sinkObj2 = new object(); s_sinkObj3 = new object(); };

        /// <summary>Test seam, null in normal use. When set, Samples returns its answer and runs nothing.</summary>
        public static Func<Action, int, int> SamplesOverride;

        /// <summary>Counts the GC.Alloc profiler samples recorded on the current thread while action runs `calls`
        /// times. The unit is a sample: one sample per allocation (measured on 6000.0.85f2); the table of
        /// P-UI-ALLOC-METER-2 supports no more than "at least k * n for k allocations in n calls", so never assert
        /// equality. The action runs 2 * calls times in all: once unmeasured as a warm-up, once measured. It makes
        /// the recorder calls of AllocatingGCMemoryConstraint, in its order. A count says nothing about size, and
        /// a byte budget has no conversion: sizes cannot be measured in this editor. To convert a test that had a
        /// byte budget, run Samples once on the accepted code, use that count as the limit and name in a comment
        /// the allocations it stands for. A test whose point is a size keeps AssertNone on the buffer.</summary>
        public static int Samples(Action action, int calls)
        {
            var over = SamplesOverride;
            if (over != null) return over(action, calls);
            if (action == null) throw new ArgumentNullException(nameof(action));
            Action loop = () => { for (int i = 0; i < calls; i++) action(); };
            loop(); // warm-up, unmeasured
            var recorder = Recorder.Get("GC.Alloc");
            recorder.enabled = false;
            recorder.FilterToCurrentThread();
            recorder.enabled = true;
            try { loop(); }
            finally { recorder.enabled = false; recorder.CollectFromAllThreads(); }
            return recorder.sampleBlockCount;
        }

        /// <summary>Fails the running test when action makes more than maxSamples GC.Alloc samples in `calls`
        /// calls (one sample per allocation, measured on 6000.0.85f2; samples are allocations, not bytes, and a
        /// byte budget has no conversion). Before it measures, it checks itself with Samples: the allocating
        /// control must register, the clean control must not, and the counting control must give at least
        /// ControlCalls * CountingAllocationsPerCall; otherwise the test fails as "the meter is not working". The
        /// action runs 2 * calls times in all (warm-up and measurement).</summary>
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
            if (Allocates(action, calls))
            {
                Assert.Fail(what + " allocated managed memory in " + calls + " calls (meter " + Meter + ")");
            }
        }

        /// <summary>The name of the meter in use, for failure messages and the packet's table.</summary>
        public static string Meter => "GC.Alloc profiler samples (UnityEngine.Profiling.Recorder, the calls of AllocatingGCMemoryConstraint)";
    }
}
