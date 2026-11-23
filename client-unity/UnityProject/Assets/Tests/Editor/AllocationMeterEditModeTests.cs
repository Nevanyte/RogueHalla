using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace Roguehalla.Client.Tests
{
    /// <summary>P-UI-ALLOC-METER-1: the allocation meter sees allocations, does not invent them, answers the same every time, and fails a test when it goes blind.</summary>
    public sealed class AllocationMeterEditModeTests
    {
        struct Holder { public int V; }

        // Non-const so the compiler cannot fold the concatenation.
        static string s_a = "alpha", s_b = "beta";
        static int s_x = 3, s_y = 4, s_sinkInt;
        static object s_sinkObj;
        static string s_sinkStr;
        static List<int> s_sinkList;
        static Holder s_holder;

        // Delegates are built once, as static fields: a closure allocates when it is created.
        static readonly Action Concat = () => { s_sinkStr = s_a + s_b; };
        static readonly Action NewObject = () => { s_sinkObj = new object(); };
        static readonly Action Boxing = () => { s_sinkObj = s_x; };
        static readonly Action ListGrowth = () => { s_sinkList = new List<int>(0); s_sinkList.Add(1); };

        static readonly Action AddLocals = () => { int a = s_x; int b = s_y; s_sinkInt = a + b; };
        static readonly Action StructField = () => { s_holder.V++; };
        static readonly Action ReadFrameCount = () => { s_sinkInt = Time.frameCount; };

        static readonly int[] Counts = { 1, 100, 10000 };

        [TearDown]
        public void TearDown()
        {
            AllocationMeter.MeterOverride = null;
        }

        [Test]
        public void AnAllocatingDelegateIsSeen()
        {
            var cases = new (string name, Action action)[]
            {
                ("string concatenation", Concat), ("new object", NewObject), ("boxing", Boxing), ("list growth", ListGrowth),
            };
            foreach (var (name, action) in cases)
            {
                action();
                foreach (int calls in Counts)
                {
                    Assert.That(AllocationMeter.Allocates(action, calls), Is.True, name + " x" + calls + " allocates and must be seen");
                }
            }
        }

        [Test]
        public void ACleanDelegateIsNotSeen()
        {
            // A "clean" answer means nothing from a meter that sees nothing: the same meter must see an allocating control.
            Assert.That(AllocationMeter.Allocates(Concat, 100), Is.True, "the meter sees the allocating control");
            var cases = new (string name, Action action)[]
            {
                ("adding two locals", AddLocals), ("struct field write", StructField), ("Time.frameCount read", ReadFrameCount),
            };
            foreach (var (name, action) in cases)
            {
                action();
                foreach (int calls in Counts)
                {
                    Assert.That(AllocationMeter.Allocates(action, calls), Is.False, name + " x" + calls + " allocates nothing");
                }
            }
        }

        [Test]
        public void ItGivesTheSameAnswerTwentyTimes()
        {
            Concat();
            AddLocals();
            for (int i = 0; i < 20; i++)
            {
                Assert.That(AllocationMeter.Allocates(Concat, 100), Is.True, "round " + i + ": string concatenation is seen");
                if (i % 3 == 0) GC.Collect();
                Assert.That(AllocationMeter.Allocates(AddLocals, 100), Is.False, "round " + i + ": adding locals is not seen");
                if (i % 4 == 1) GC.Collect();
            }
        }

        [Test]
        public void AssertNoneFailsOnAnAllocatingDelegate()
        {
            Concat();
            var ex = Assert.Throws<AssertionException>(() => AllocationMeter.AssertNone(Concat, 100, "the concat probe"));
            Assert.That(ex.Message, Does.Contain("the concat probe"), "the failure names what was measured");

            AddLocals();
            Assert.DoesNotThrow(() => AllocationMeter.AssertNone(AddLocals, 100, "the clean probe"));
        }

        [Test]
        public void ABlindMeterFailsTheTest()
        {
            AddLocals();
            AllocationMeter.MeterOverride = (action, calls) => false;
            var ex = Assert.Throws<AssertionException>(() => AllocationMeter.AssertNone(AddLocals, 100, "blind probe"));
            Assert.That(ex.Message, Does.Contain("the meter is not working"));
        }

        [Test]
        public void TheMeterHasAName()
        {
            Assert.That(AllocationMeter.Meter, Is.Not.Null.And.Not.Empty);
            Assert.That(AllocationMeter.Meter, Is.Not.EqualTo("none"));
        }
    }
}
