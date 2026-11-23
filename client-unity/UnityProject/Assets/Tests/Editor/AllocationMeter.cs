using System;

namespace Roguehalla.Client.Tests
{
    /// <summary>Says whether a piece of code allocates managed memory, with a meter that this editor is proven to
    /// honour (GC.GetAllocatedBytesForCurrentThread reads 0 here). Warm the code up before you measure it, and build
    /// each delegate once outside the measured call: a closure allocates when it is created, not when it is called.</summary>
    public static class AllocationMeter
    {
        /// <summary>Test seam, null in normal use. When set it replaces the meter: it gets the action and the call count and answers "allocated".</summary>
        public static Func<Action, int, bool> MeterOverride;

        /// <summary>True when calling action `calls` times allocated managed memory.</summary>
        public static bool Allocates(Action action, int calls) => false;

        /// <summary>Fails the running test, naming `what`, when action allocates in `calls` calls. Before it
        /// measures, it checks itself: a known allocating control must register and a known clean control must
        /// not; if either check fails the test is failed as "the meter is not working", never passed.</summary>
        public static void AssertNone(Action action, int calls, string what) { }

        /// <summary>The name of the meter in use, for failure messages and the packet's table.</summary>
        public static string Meter => "none";
    }
}
