using System;
using NUnit.Framework;
using Roguehalla.Net.Ui;

namespace Roguehalla.Net.Tests
{
    /// <summary>
    /// P-UI-CAMP-LABEL-STABLE-1: the lift of a camp world label above its base. It rises fast to what it needs to clear fighters, holds
    /// when the need drops, falls slowly, ignores jitter inside the deadband and never leaves [0, the largest wanted value seen].
    /// Presentation only: the numbers are the packet's, not a legacy rule.
    /// </summary>
    public sealed class LabelLiftTests
    {
        const float Dt = 1f / 60f;

        [Test]
        public void StartTakesTheWantedValueAtOnceAndNeverNegative()
        {
            var a = LabelLift.Start(.7f);
            Assert.That(a.Value, Is.EqualTo(.7f).Within(1e-6f), "Value");
            Assert.That(a.Target, Is.EqualTo(.7f).Within(1e-6f), "Target");
            var b = LabelLift.Start(-2f);
            Assert.That(b.Value, Is.EqualTo(0f).Within(1e-6f), "a negative wanted value starts at 0");
            Assert.That(b.Target, Is.EqualTo(0f).Within(1e-6f));
        }

        [Test]
        public void ARiseMovesNoMoreThanTheRiseSpeedAStepAndReachesTheTarget()
        {
            var l = LabelLift.Start(0f);
            float prev = l.Value;
            for (int i = 0; i < 30; i++)
            {
                l = l.Step(1f, Dt, false);
                Assert.That(l.Value - prev, Is.LessThanOrEqualTo(LabelLift.RiseUnitsPerSecond * Dt + 1e-5f), "step " + i);
                Assert.That(l.Value, Is.GreaterThanOrEqualTo(prev), "never falls while rising, step " + i);
                prev = l.Value;
            }
            Assert.That(l.Value, Is.EqualTo(1f).Within(1e-4f), "reaches the target (1 unit takes about 8 frames)");
            Assert.That(l.Target, Is.EqualTo(1f).Within(1e-6f));
            Assert.That(LabelLift.Start(0f).Step(1f, Dt, false).Value, Is.GreaterThan(0f), "it does move on the first step");
        }

        [Test]
        public void ADropHoldsForTheHoldTimeThenFallsNoFasterThanTheFallSpeed()
        {
            var l = LabelLift.Start(1f);
            for (int i = 0; i < 24; i++)   // 0.40 s, the hold is 0.45 s
            {
                l = l.Step(0f, Dt, false);
                Assert.That(l.Value, Is.EqualTo(1f).Within(1e-5f), "held, step " + i);
            }
            float prev = l.Value;
            for (int i = 0; i < 90; i++)
            {
                l = l.Step(0f, Dt, false);
                Assert.That(l.Value, Is.LessThanOrEqualTo(prev + 1e-6f), "never rises, step " + i);
                Assert.That(prev - l.Value, Is.LessThanOrEqualTo(LabelLift.FallUnitsPerSecond * Dt + 1e-5f), "falls slowly, step " + i);
                prev = l.Value;
            }
            Assert.That(l.Value, Is.EqualTo(0f).Within(1e-4f), "back at the base after the hold and 1 / 3 s of falling");
            Assert.That(l.Target, Is.EqualTo(0f).Within(1e-6f));
        }

        [Test]
        public void AWantedValueThatAlternatesEveryFrameLeavesTheHigherValue()
        {
            var l = LabelLift.Start(0f);
            for (int i = 0; i < 200; i++)
            {
                l = l.Step(i % 2 == 0 ? .5f : 0f, Dt, false);
                if (i >= 20) Assert.That(l.Value, Is.EqualTo(.5f).Within(1e-5f), "settled at the higher value, frame " + i);
            }
        }

        [Test]
        public void JitterInsideTheDeadbandMovesNothing()
        {
            var l = LabelLift.Start(.5f);
            for (int i = 0; i < 120; i++)
            {
                l = l.Step(i % 2 == 0 ? .52f : .48f, Dt, false);
                Assert.That(l.Value, Is.EqualTo(.5f).Within(1e-6f), "value, frame " + i);
                Assert.That(l.Target, Is.EqualTo(.5f).Within(1e-6f), "target, frame " + i);
            }
            // The jitter keeps the hold fresh: a long time later a real drop still waits the whole hold.
            for (int i = 0; i < 24; i++) l = l.Step(0f, Dt, false);
            Assert.That(l.Value, Is.EqualTo(.5f).Within(1e-5f), "the hold restarted with the last jitter frame");
        }

        [Test]
        public void AWantedValueBackInsideTheDeadbandRestartsTheHold()
        {
            var l = LabelLift.Start(.5f);
            for (int i = 0; i < 20; i++) l = l.Step(0f, Dt, false);   // 0.33 s of the 0.45 s hold has run
            l = l.Step(.49f, Dt, false);                               // back inside the deadband: the hold restarts
            for (int i = 0; i < 24; i++) l = l.Step(0f, Dt, false);    // another 0.40 s
            Assert.That(l.Value, Is.EqualTo(.5f).Within(1e-5f), "still held");
        }

        [Test]
        public void ARiseAboveTheDeadbandRaisesTheTargetAndRestartsTheHold()
        {
            var l = LabelLift.Start(.5f).Step(.6f, Dt, false);
            Assert.That(l.Target, Is.EqualTo(.6f).Within(1e-6f), "0.1 above the target is a new target");
            for (int i = 0; i < 20; i++) l = l.Step(.6f, Dt, false);
            Assert.That(l.Value, Is.EqualTo(.6f).Within(1e-4f));
        }

        [Test]
        public void ReducedMotionJumpsToTheTargetButStillHolds()
        {
            var l = LabelLift.Start(0f).Step(.5f, Dt, true);
            Assert.That(l.Value, Is.EqualTo(.5f).Within(1e-6f), "no easing up: 0.5 in one frame (the speed allows 0.13)");
            for (int i = 0; i < 24; i++)
            {
                l = l.Step(0f, Dt, true);
                Assert.That(l.Value, Is.EqualTo(.5f).Within(1e-6f), "held, step " + i);
            }
            bool dropped = false;
            for (int i = 0; i < 12 && !dropped; i++)
            {
                l = l.Step(0f, Dt, true);
                if (l.Value < .5f) { dropped = true; Assert.That(l.Value, Is.EqualTo(0f).Within(1e-6f), "no easing down: the drop is whole"); }
            }
            Assert.That(dropped, Is.True, "the hold ends within 0.45 s plus a few frames");
        }

        [Test]
        public void ANegativeDtOrWantedIsClamped()
        {
            var l = LabelLift.Start(0f).Step(1f, -5f, false);
            Assert.That(l.Value, Is.EqualTo(0f).Within(1e-6f), "no time passed, no movement");
            Assert.That(l.Target, Is.EqualTo(1f).Within(1e-6f), "but the target is taken");
            var m = LabelLift.Start(.4f).Step(-3f, Dt, false);
            Assert.That(m.Value, Is.GreaterThanOrEqualTo(0f));
            Assert.That(m.Target, Is.EqualTo(.4f).Within(1e-6f), "a negative wanted counts as 0: it is a drop, so the old target holds");
        }

        [TestCase(false)]
        [TestCase(true)]
        public void ValueIsNeverNegativeAndNeverAboveTheLargestWantedSeen(bool reduced)
        {
            uint s = 12345u;
            Func<float> next = () => { s = s * 1664525u + 1013904223u; return (s >> 8) / (float)(1 << 24); };
            var l = LabelLift.Start(next());
            float max = l.Value; bool sawLift = l.Value > 0f;
            for (int i = 0; i < 2000; i++)
            {
                float wanted = next() * 1.4f - .2f;   // [-0.2, 1.2)
                float dt = .004f + next() * .05f;
                max = Math.Max(max, wanted);
                l = l.Step(wanted, dt, reduced);
                if (l.Value > 0f) sawLift = true;
                if (reduced) Assert.That(l.Value, Is.EqualTo(l.Target).Within(1e-6f), "reduced motion: Value is the Target, frame " + i);
                Assert.That(l.Value, Is.GreaterThanOrEqualTo(0f), "frame " + i);
                Assert.That(l.Value, Is.LessThanOrEqualTo(max + 1e-5f), "frame " + i);
                Assert.That(l.Target, Is.LessThanOrEqualTo(max + 1e-5f), "target, frame " + i);
            }
            Assert.That(max, Is.GreaterThan(1f), "set-up: the sweep did reach high values");
            Assert.That(sawLift, Is.True, "the lift followed the sweep (Value was above 0 at some frame)");
        }
    }
}
