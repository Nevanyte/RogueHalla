using System;
using System.Threading;
using NUnit.Framework;

namespace Roguehalla.Client.Tests
{
    /// <summary>P-UI-ALLOC-METER-3: background noise cannot fail an allocation test. The meter measures up to three
    /// times and reports the smallest count, never lets another thread's samples in by reading the recorder after
    /// CollectFromAllThreads, warms up once, and names the quietest count when it fails. Most tests drive the rule
    /// through SamplesOverride with a scripted source; two use the real meter.</summary>
    public sealed class AllocationMeterNoiseEditModeTests
    {
        // The probe is never run: the override replaces the measurement window.
        static readonly Action Probe = () => { };

        const int Alloc = 0, Clean = 1, Counting = 2, ProbeIx = 3;

        /// <summary>A scripted SamplesOverride: per target, the reads in order (the last repeats) and the number of
        /// measurements made. Build a fresh one for every call under test.</summary>
        sealed class Source
        {
            readonly int[][] _reads;
            public readonly int[] Measured = new int[4];

            public Source(int[] alloc = null, int[] clean = null, int[] counting = null, int[] probe = null)
            {
                _reads = new[] { alloc ?? new[] { 20 }, clean ?? new[] { 0 }, counting ?? new[] { 30 }, probe ?? new[] { 0 } };
            }

            public int Total => Measured[0] + Measured[1] + Measured[2] + Measured[3];

            public int Read(Action a, int n)
            {
                int ix = ReferenceEquals(a, AllocationMeter.AllocatingControl) ? Alloc
                    : ReferenceEquals(a, AllocationMeter.CleanControl) ? Clean
                    : ReferenceEquals(a, AllocationMeter.CountingControl) ? Counting
                    : ProbeIx;
                int[] reads = _reads[ix];
                int k = Measured[ix]++;
                return reads[Math.Min(k, reads.Length - 1)];
            }

            public Source Install()
            {
                AllocationMeter.SamplesOverride = Read;
                return this;
            }
        }

        static int[] R(params int[] reads) { return reads; }

        [TearDown]
        public void TearDown()
        {
            AllocationMeter.MeterOverride = null;
            AllocationMeter.SamplesOverride = null;
        }

        [Test]
        public void AOneOffSampleThatDoesNotRepeatIsDropped()
        {
            var s = new Source(probe: R(1, 0)).Install();
            Assert.That(AllocationMeter.Samples(Probe, 100), Is.EqualTo(0), "a sample that does not repeat is dropped");
            Assert.That(s.Measured[ProbeIx], Is.EqualTo(2), "the probe was measured twice");

            s = new Source(probe: R(1, 0)).Install();
            Assert.That(AllocationMeter.Allocates(Probe, 100), Is.False, "Allocates is false");

            s = new Source(probe: R(1, 0)).Install();
            Assert.DoesNotThrow(() => AllocationMeter.AssertNone(Probe, 100, "x"), "AssertNone passes");
            Assert.That(s.Measured[ProbeIx], Is.EqualTo(2), "AssertNone: probe measured twice");
            Assert.That(s.Measured[Clean], Is.EqualTo(1), "AssertNone: clean control measured once");
            Assert.That(s.Measured[Alloc], Is.EqualTo(3), "AssertNone: allocating control measured three times");
            Assert.That(s.Measured[Counting], Is.EqualTo(0), "AssertNone never asks about the counting control");
        }

        [Test]
        public void AnAllocationInEveryMeasurementGivesTheSmallestCount()
        {
            var s = new Source(probe: R(2, 3, 1)).Install();
            Assert.That(AllocationMeter.Samples(Probe, 100), Is.EqualTo(1), "the smallest of 2, 3, 1");
            Assert.That(s.Measured[ProbeIx], Is.EqualTo(3), "measured three times");

            s = new Source(probe: R(2, 3, 1)).Install();
            Assert.That(AllocationMeter.Allocates(Probe, 100), Is.True, "an allocation in every measurement is reported");

            s = new Source(probe: R(2, 3, 1)).Install();
            var ex = Assert.Throws<AssertionException>(() => AllocationMeter.AssertNone(Probe, 100, "the probe"), "AssertNone fails");
            Assert.That(ex.Message, Does.Contain("the probe"));
            Assert.That(ex.Message, Does.Contain("allocated managed memory in 100 calls"));
            Assert.That(ex.Message, Does.Contain("1 GC.Alloc samples in its quietest measurement"));
            Assert.That(s.Measured[ProbeIx], Is.EqualTo(3), "the probe was measured exactly three times in that one call, not six");
        }

        [Test]
        public void ThereAreNeverMoreThanThreeMeasurements()
        {
            Assert.That(AllocationMeter.MaxMeasurements, Is.EqualTo(3));

            var s = new Source(probe: R(5, 5, 5, 5, 5)).Install();
            Assert.That(AllocationMeter.Samples(Probe, 100), Is.EqualTo(5), "five every time");
            Assert.That(s.Measured[ProbeIx], Is.EqualTo(3), "measured exactly three times");

            s = new Source(probe: R(1, 1, 0)).Install();
            Assert.That(AllocationMeter.Samples(Probe, 100), Is.EqualTo(0), "the third measurement reads 0");
            Assert.That(s.Measured[ProbeIx], Is.EqualTo(3), "measured exactly three times");

            s = new Source(probe: R(0)).Install();
            Assert.That(AllocationMeter.Samples(Probe, 100), Is.EqualTo(0), "clean code reads 0");
            Assert.That(s.Measured[ProbeIx], Is.EqualTo(1), "and is measured once");

            s = new Source(probe: R(0)).Install();
            Assert.DoesNotThrow(() => AllocationMeter.AssertNone(Probe, 100, "x"), "AssertNone passes clean code");
            Assert.That(s.Measured[ProbeIx], Is.EqualTo(1), "with one probe measurement");
        }

        [Test]
        public void AssertAtMostComparesTheSmallestCount()
        {
            var s = new Source(probe: R(2, 3, 1)).Install();
            Assert.DoesNotThrow(() => AllocationMeter.AssertAtMost(Probe, 100, 1, "the probe"), "the smallest count, 1, is at the limit");
            Assert.That(s.Measured[ProbeIx], Is.EqualTo(3), "the probe was measured three times");
            Assert.That(s.Measured[Counting], Is.EqualTo(3), "the counting control was measured three times");

            s = new Source(probe: R(2, 3, 2)).Install();
            var ex = Assert.Throws<AssertionException>(() => AllocationMeter.AssertAtMost(Probe, 100, 1, "the probe"), "the smallest count, 2, is above 1");
            Assert.That(ex.Message, Does.Contain("made 2 GC.Alloc samples in 100 calls"));
            Assert.That(ex.Message, Does.Contain("limit of 1"));

            s = new Source(probe: R(0)).Install();
            Assert.DoesNotThrow(() => AllocationMeter.AssertAtMost(Probe, 100, 0, "the probe"), "0 at a limit of 0");
            Assert.That(s.Measured[ProbeIx], Is.EqualTo(1), "measured once");
        }

        [Test]
        public void AnAllocatingOrCleanControlThatFailsInAnyMeasurementIsNotWorking()
        {
            // The allocating control reads 0 in its second measurement: the smallest count is 0, so it is blind.
            new Source(alloc: R(20, 0, 20), probe: R(0)).Install();
            var ex = Assert.Throws<AssertionException>(() => AllocationMeter.AssertNone(Probe, 100, "the probe"), "AssertNone: allocating control {20,0,20}");
            Assert.That(ex.Message, Does.Contain("the meter is not working"));
            Assert.That(ex.Message, Does.Contain("the probe"));

            new Source(alloc: R(20, 0, 20), probe: R(0)).Install();
            var ex2 = Assert.Throws<AssertionException>(() => AllocationMeter.AssertAtMost(Probe, 100, 5, "the probe"), "AssertAtMost: allocating control {20,0,20}");
            Assert.That(ex2.Message, Does.Contain("the meter is not working"));
            Assert.That(ex2.Message, Does.Contain("the probe"));

            // The clean control reads 2 every time: it is never 0, so it is wrong.
            new Source(clean: R(2), probe: R(0)).Install();
            var ex3 = Assert.Throws<AssertionException>(() => AllocationMeter.AssertNone(Probe, 100, "the probe"), "AssertNone: clean control {2}");
            Assert.That(ex3.Message, Does.Contain("the meter is not working"));
            Assert.That(ex3.Message, Does.Contain("the probe"));

            new Source(clean: R(2), probe: R(0)).Install();
            var ex4 = Assert.Throws<AssertionException>(() => AllocationMeter.AssertAtMost(Probe, 100, 5, "the probe"), "AssertAtMost: clean control {2}");
            Assert.That(ex4.Message, Does.Contain("the meter is not working"));
            Assert.That(ex4.Message, Does.Contain("the probe"));
        }

        [Test]
        public void ACountingControlThatReadsLowInAnyMeasurementIsNotWorking()
        {
            foreach (var script in new[] { R(30, 29, 30), R(29, 30, 30) })
            {
                string name = "counting " + string.Join(",", script);
                new Source(counting: script, probe: R(0)).Install();
                var ex = Assert.Throws<AssertionException>(() => AllocationMeter.AssertAtMost(Probe, 100, 5, "the probe"), name + ": AssertAtMost");
                Assert.That(ex.Message, Does.Contain("the meter is not working"), name);
                Assert.That(ex.Message, Does.Contain("the probe"), name);

                var s = new Source(counting: script, probe: R(0)).Install();
                Assert.DoesNotThrow(() => AllocationMeter.AssertNone(Probe, 100, "the probe"), name + ": AssertNone does not ask");
                Assert.That(s.Measured[Counting], Is.EqualTo(0), name + ": AssertNone never measures the counting control");
            }
        }

        [Test]
        public void ANoisyCleanControlThatReadsZeroOnceIsSound()
        {
            foreach (var script in new[] { R(1, 0), R(4, 0) })
            {
                string name = "clean " + string.Join(",", script);
                var s = new Source(clean: script, probe: R(0)).Install();
                Assert.DoesNotThrow(() => AllocationMeter.AssertNone(Probe, 100, "the probe"), name + ": AssertNone");
                Assert.That(s.Measured[Clean], Is.EqualTo(2), name + ": AssertNone measured the clean control twice");

                s = new Source(clean: script, probe: R(0)).Install();
                Assert.DoesNotThrow(() => AllocationMeter.AssertAtMost(Probe, 100, 5, "the probe"), name + ": AssertAtMost");
                Assert.That(s.Measured[Clean], Is.EqualTo(2), name + ": AssertAtMost measured the clean control twice");
            }
        }

        [Test]
        public void TheYesOrNoSeamIsAskedOncePerQuestion()
        {
            var s = new Source(probe: R(1, 0)).Install();
            int asked = 0;
            bool probeAnswer = false;
            AllocationMeter.MeterOverride = (a, n) =>
            {
                asked++;
                if (ReferenceEquals(a, AllocationMeter.AllocatingControl)) return true;
                if (ReferenceEquals(a, AllocationMeter.CleanControl)) return false;
                return probeAnswer;
            };

            Assert.That(AllocationMeter.Samples(Probe, 100), Is.EqualTo(0), "Samples does not see the yes-or-no seam and drops the lone 1");
            Assert.That(s.Measured[ProbeIx], Is.EqualTo(2), "two probe measurements");

            int before = s.Total;
            asked = 0;
            Assert.DoesNotThrow(() => AllocationMeter.AssertNone(Probe, 100, "x"), "the seam says no for the probe");
            Assert.That(asked, Is.EqualTo(3), "asked exactly three times: the two controls and the probe, none retried");
            Assert.That(s.Total, Is.EqualTo(before), "no measurement of the scripted source happened during that call");

            probeAnswer = true;
            asked = 0;
            before = s.Total;
            var ex = Assert.Throws<AssertionException>(() => AllocationMeter.AssertNone(Probe, 100, "x"), "the seam says yes for the probe");
            Assert.That(ex.Message, Does.Contain("allocated managed memory in 100 calls"));
            Assert.That(ex.Message, Does.Not.Contain("GC.Alloc samples in its quietest"), "only the yes-or-no is known");
            Assert.That(asked, Is.EqualTo(3), "still asked three times");
            Assert.That(s.Total, Is.EqualTo(before), "and nothing measured");
        }

        static volatile bool s_noiseStop;
        static volatile int s_noiseCount;
        static object s_noiseObj;

        static void NoiseBody()
        {
            while (!s_noiseStop) { s_noiseObj = new object(); s_noiseCount++; }
        }

        [Test]
        public void ABusyThreadNextDoorCannotFoolTheMeter()
        {
            s_noiseStop = false;
            var thread = new Thread(NoiseBody) { IsBackground = true };
            try
            {
                thread.Start();
                for (int round = 0; round < 100; round++)
                {
                    // Wait until the noise thread has made progress, so it is running when the window opens.
                    int start = s_noiseCount;
                    int spins = 0;
                    while (s_noiseCount == start && spins < 100000000) spins++;
                    if (s_noiseCount == start) Assert.Fail("the noise thread is not running (set-up fault)");

                    int clean = AllocationMeter.Samples(AllocationMeter.CleanControl, 1000);
                    if (clean != 0) Assert.Fail("round " + round + ": clean code read " + clean + " samples under a busy thread");

                    int alloc = AllocationMeter.Samples(AllocationMeter.AllocatingControl, 10);
                    if (alloc != 20) Assert.Fail("round " + round + ": the allocating control read " + alloc + " samples, expected exactly 20");
                }
            }
            finally
            {
                s_noiseStop = true;
                thread.Join();
            }
        }

        static int s_calls;
        static object s_keep;
        static readonly Action CountAndAllocate = () => { s_calls++; s_keep = new object(); };

        [Test]
        public void TheWarmUpRunsOnceAndEachMeasurementRunsTheLoop()
        {
            s_calls = 0;
            int samples = AllocationMeter.Samples(CountAndAllocate, 7);
            Assert.That(samples, Is.GreaterThanOrEqualTo(7), "an allocating action reads at least one sample a call");
            Assert.That(s_calls, Is.EqualTo(28), "7 warm-up calls plus three windows of 7");
        }
    }
}
