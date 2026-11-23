using System;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;

namespace Roguehalla.Client.Tests
{
    /// <summary>P-UI-ALLOC-METER-2: the meter counts GC.Alloc samples. A clean delegate counts zero, every allocation is
    /// counted at least once, the count agrees with the yes-or-no answer and with the Unity constraint itself, and
    /// AssertAtMost allows a budget, fails one above it, and fails as "not working" when a control is wrong.</summary>
    public sealed class AllocationMeterCountEditModeTests
    {
        // Non-const so the compiler cannot fold the concatenation.
        static string s_a = "alpha", s_b = "beta";
        static int s_x = 3, s_y = 4, s_sinkInt, s_calls;
        static object s_sink1, s_sink2, s_sink3, s_sink4, s_sink5;
        static string s_sinkStr;

        struct Holder { public int V; }
        static Holder s_holder;

        // Delegates are built once, as static fields: a closure allocates when it is created.
        static readonly Action AddLocals = () => { int a = s_x; int b = s_y; s_sinkInt = a + b; };
        static readonly Action StructField = () => { s_holder.V++; };
        static readonly Action ReadFrameCount = () => { s_sinkInt = Time.frameCount; };
        static readonly Action Concat = () => { s_sinkStr = s_a + s_b; };
        static readonly Action OneObject = () => { s_sink1 = new object(); };
        static readonly Action TwoObjects = () => { s_sink1 = new object(); s_sink2 = new object(); };
        static readonly Action FiveObjects = () => { s_sink1 = new object(); s_sink2 = new object(); s_sink3 = new object(); s_sink4 = new object(); s_sink5 = new object(); };
        static readonly Action CountCalls = () => { s_calls++; };

        [TearDown]
        public void TearDown()
        {
            AllocationMeter.MeterOverride = null;
            AllocationMeter.SamplesOverride = null;
        }

        [Test]
        public void ACleanDelegateCountsZero()
        {
            // A zero means nothing from a meter that sees nothing: the same meter must count the allocating control.
            Assert.That(AllocationMeter.Samples(Concat, 100), Is.GreaterThan(0), "the meter counts string concatenation");
            var cases = new (string name, Action action)[]
            {
                ("adding two locals", AddLocals), ("struct field write", StructField), ("Time.frameCount read", ReadFrameCount),
            };
            foreach (var (name, action) in cases)
            {
                foreach (int calls in new[] { 1, 100, 10000 })
                {
                    Assert.That(AllocationMeter.Samples(action, calls), Is.EqualTo(0), name + " x" + calls + " counts no sample");
                }
            }
        }

        [Test]
        public void EveryAllocationIsCountedAtLeastOnce()
        {
            var cases = new (string name, Action action, int k)[] { ("one object", OneObject, 1), ("two objects", TwoObjects, 2), ("five objects", FiveObjects, 5) };
            foreach (var (name, action, k) in cases)
            {
                foreach (int n in new[] { 10, 100 })
                {
                    Assert.That(AllocationMeter.Samples(action, n), Is.GreaterThanOrEqualTo(k * n), name + " x" + n + " counts at least k*n samples");
                }
            }
            Assert.That(AllocationMeter.Samples(Concat, 100), Is.GreaterThanOrEqualTo(100), "a string concatenation counts at least once per call");
            Assert.That(AllocationMeter.Samples(Concat, 0), Is.EqualTo(0), "no calls, no samples");
            Assert.That(AllocationMeter.Samples(AllocationMeter.CountingControl, AllocationMeter.ControlCalls),
                Is.GreaterThanOrEqualTo(AllocationMeter.ControlCalls * AllocationMeter.CountingAllocationsPerCall),
                "the counting control counts at least the bound of self-check (c)");
        }

        [Test]
        public void CountAndAnswerAgreeTwentyTimes()
        {
            var controls = new (string name, Action action)[] { ("allocating control", AllocationMeter.AllocatingControl), ("clean control", AllocationMeter.CleanControl) };
            for (int round = 0; round < 20; round++)
            {
                if (round % 3 == 0) GC.Collect();
                foreach (var (name, action) in controls)
                {
                    int samples = AllocationMeter.Samples(action, 10);
                    bool answer = AllocationMeter.Allocates(action, 10);
                    TestDelegate loop = () => { for (int i = 0; i < 10; i++) action(); };
                    loop();
                    var result = new UnityEngine.TestTools.Constraints.AllocatingGCMemoryConstraint().ApplyTo(loop);
                    var writer = new NUnit.Framework.Internal.TextMessageWriter("");
                    result.WriteMessageTo(writer);
                    string message = writer.ToString();
                    var m = Regex.Match(message, @"made (\d+) GC allocation");
                    int witnessCount = m.Success ? int.Parse(m.Groups[1].Value) : 0;

                    string where = "round " + round + ", " + name + ": ";
                    Assert.That(samples > 0, Is.EqualTo(result.IsSuccess), where + "the count is above 0 exactly when the constraint succeeds");
                    Assert.That(answer, Is.EqualTo(result.IsSuccess), where + "Allocates agrees with the constraint");
                    Assert.That(samples, Is.EqualTo(witnessCount), where + "the count equals the constraint's own count (" + message.Trim() + ")");
                    Assert.That(samples > 0, Is.EqualTo(action == AllocationMeter.AllocatingControl), where + "the allocating control counts, the clean one does not");
                }
            }
        }

        // One override that tells the controls from the measured action by reference.
        static Func<Action, int, int> Overrides(int alloc, int clean, int counting, int other, Action<Action> seen = null)
        {
            return (a, n) =>
            {
                if (ReferenceEquals(a, AllocationMeter.AllocatingControl)) return alloc;
                if (ReferenceEquals(a, AllocationMeter.CleanControl)) return clean;
                if (ReferenceEquals(a, AllocationMeter.CountingControl)) return counting;
                if (seen != null) seen(a);
                return other;
            };
        }

        [Test]
        public void AssertAtMostPassesAtTheLimitAndFailsOneAbove()
        {
            int measuredSeen = 0;
            AllocationMeter.SamplesOverride = Overrides(20, 0, 30, 137, a => { if (ReferenceEquals(a, OneObject)) measuredSeen++; });

            Assert.DoesNotThrow(() => AllocationMeter.AssertAtMost(OneObject, 250, 137, "the limit probe"), "at the limit it returns");
            Assert.That(measuredSeen, Is.EqualTo(3), "the override saw the measured action once per measurement, three (at the limit)");

            measuredSeen = 0;
            var ex = Assert.Throws<AssertionException>(() => AllocationMeter.AssertAtMost(OneObject, 250, 136, "the limit probe"), "one above the limit fails");
            Assert.That(ex.Message, Does.Contain("the limit probe"));
            Assert.That(ex.Message, Does.Contain("made 137 GC.Alloc samples in 250 calls"));
            Assert.That(ex.Message, Does.Contain("limit of 136"));
            Assert.That(ex.Message, Does.Contain(AllocationMeter.Meter));
            Assert.That(measuredSeen, Is.EqualTo(3), "the override saw the measured action once per measurement, three (above the limit)");
        }

        [Test]
        public void AMeterThatIsBlindOrWrongFailsAsNotWorking()
        {
            var wrong = new (string name, int alloc, int clean, int counting)[]
            {
                ("blind to the allocating control", 0, 0, 30),
                ("counts the clean control", 20, 4, 30),
                ("under-counts the counting control", 20, 0, 29),
                ("blind to all three", 0, 0, 0),
            };
            foreach (var (name, alloc, clean, counting) in wrong)
            {
                AllocationMeter.SamplesOverride = Overrides(alloc, clean, counting, 0);
                var ex = Assert.Throws<AssertionException>(() => AllocationMeter.AssertAtMost(OneObject, 100, 1000, "the wrong-meter probe"), name);
                Assert.That(ex.Message, Does.Contain("the meter is not working"), name);
                Assert.That(ex.Message, Does.Contain("the wrong-meter probe"), name);
            }

            foreach (int counting in new[] { 30, 45 })
            {
                AllocationMeter.SamplesOverride = Overrides(20, 0, counting, 0);
                Assert.DoesNotThrow(() => AllocationMeter.AssertAtMost(OneObject, 100, 1000, "the sound-meter probe"), "counting control " + counting + " is sound");
            }
        }

        [Test]
        public void TheRealMeterBacksAssertAtMost()
        {
            Assert.DoesNotThrow(() => AllocationMeter.AssertAtMost(AddLocals, 100, 0, "adding locals"), "a clean action passes a limit of 0");

            var ex = Assert.Throws<AssertionException>(() => AllocationMeter.AssertAtMost(Concat, 100, 0, "the concat probe"), "an allocating action fails a limit of 0");
            Assert.That(ex.Message, Does.Contain("the concat probe"));

            Assert.DoesNotThrow(() => AllocationMeter.AssertAtMost(OneObject, 100, 1000, "one object a call"), "100 allocations pass a limit of 1000");
            var ex2 = Assert.Throws<AssertionException>(() => AllocationMeter.AssertAtMost(OneObject, 100, 99, "one object a call"), "100 allocations fail a limit of 99");
            Assert.That(ex2.Message, Does.Contain("one object a call"));

            AllocationMeter.MeterOverride = (a, n) => false;
            var ex3 = Assert.Throws<AssertionException>(() => AllocationMeter.AssertAtMost(Concat, 100, 0, "the concat probe"), "the yes-or-no seam does not reach AssertAtMost");
            Assert.That(ex3.Message, Does.Contain("the concat probe"));
        }

        [Test]
        public void TheSeamsAreSeparate()
        {
            AllocationMeter.SamplesOverride = (a, n) => 5;
            s_calls = 0;
            Assert.That(AllocationMeter.Samples(CountCalls, 3), Is.EqualTo(5), "Samples returns the override's answer");
            Assert.That(s_calls, Is.EqualTo(0), "the action did not run");
            Assert.That(AllocationMeter.Allocates(AddLocals, 3), Is.True, "Allocates sees the samples override");

            AllocationMeter.MeterOverride = (a, n) => false;
            Assert.That(AllocationMeter.Allocates(AddLocals, 3), Is.False, "the yes-or-no override wins in Allocates");
            Assert.That(AllocationMeter.Samples(CountCalls, 3), Is.EqualTo(5), "and does not reach Samples");
        }

        [Test]
        public void TheWarmUpRunsTheActionOnceMoreFirst()
        {
            s_calls = 0;
            AllocationMeter.Samples(CountCalls, 7);
            Assert.That(s_calls, Is.EqualTo(14), "one unmeasured pass and one measured pass of 7 calls");
        }

        [Test]
        public void BadArgumentsThrowBeforeMeasuring()
        {
            int invocations = 0;
            AllocationMeter.SamplesOverride = (a, n) => { invocations++; return 0; };

            Assert.Throws<ArgumentNullException>(() => AllocationMeter.AssertAtMost(null, 10, 5, "x"), "a null action");
            Assert.Throws<ArgumentOutOfRangeException>(() => AllocationMeter.AssertAtMost(AddLocals, 0, 5, "x"), "calls 0");
            Assert.Throws<ArgumentOutOfRangeException>(() => AllocationMeter.AssertAtMost(AddLocals, -1, 5, "x"), "calls -1");
            Assert.Throws<ArgumentOutOfRangeException>(() => AllocationMeter.AssertAtMost(AddLocals, 10, -1, "x"), "maxSamples -1");
            Assert.That(invocations, Is.EqualTo(0), "nothing was measured");
        }
    }
}
