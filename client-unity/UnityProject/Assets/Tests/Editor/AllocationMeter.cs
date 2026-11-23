using System;
using NUnit.Framework;
using UnityEngine.TestTools.Constraints;

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
        static readonly Action AllocatingControl = () => { s_sinkObj = new object(); s_sinkStr = s_a + s_b; };
        static readonly Action CleanControl = () => { s_sinkInt += 3; };

        /// <summary>True when calling action `calls` times allocated managed memory.</summary>
        public static bool Allocates(Action action, int calls)
        {
            var over = MeterOverride;
            return over != null ? over(action, calls) : Raw(action, calls);
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
        public static string Meter => "UnityEngine.TestTools.Constraints GC.Alloc samples";

        static bool Raw(Action action, int calls)
        {
            if (action == null) throw new ArgumentNullException(nameof(action));
            TestDelegate loop = () => { for (int i = 0; i < calls; i++) action(); };
            // Warm: the constraint's own first call and any lazy init in the action stay out of the measurement.
            loop();
            return new AllocatingGCMemoryConstraint().ApplyTo(loop).IsSuccess;
        }
    }
}
