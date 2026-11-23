using NUnit.Framework;
using Roguehalla.Net.Ui;

namespace Roguehalla.Net.Tests
{
    /// <summary>P-UI-MOTION-1 oracle: the pure easing and stepping functions of HUD motion.</summary>
    public sealed class UiEaseTests
    {
        const float Dt = 1f / 60f;
        delegate float Curve(float t);

        static void Monotone(Curve c)
        {
            float last = c(0f);
            for (int i = 1; i <= 100; i++)
            {
                float v = c(i / 100f);
                Assert.That(v, Is.GreaterThan(last), "strictly rising at " + i / 100f);
                last = v;
            }
        }

        [Test]
        public void Clamp01KeepsTheUnitRange()
        {
            Assert.That(UiEase.Clamp01(-3f), Is.EqualTo(0f));
            Assert.That(UiEase.Clamp01(.25f), Is.EqualTo(.25f));
            Assert.That(UiEase.Clamp01(7f), Is.EqualTo(1f));
        }

        [Test]
        public void OutCubicRunsFromZeroToOneAndRisesAndClamps()
        {
            Assert.That(UiEase.OutCubic(0f), Is.EqualTo(0f).Within(1e-6));
            Assert.That(UiEase.OutCubic(1f), Is.EqualTo(1f).Within(1e-6));
            Assert.That(UiEase.OutCubic(.5f), Is.EqualTo(.875f).Within(1e-5));
            Assert.That(UiEase.OutCubic(-1f), Is.EqualTo(0f).Within(1e-6));
            Assert.That(UiEase.OutCubic(2f), Is.EqualTo(1f).Within(1e-6));
            Monotone(UiEase.OutCubic);
        }

        [Test]
        public void InOutSineRunsFromZeroToOneAndRisesAndClamps()
        {
            Assert.That(UiEase.InOutSine(0f), Is.EqualTo(0f).Within(1e-6));
            Assert.That(UiEase.InOutSine(1f), Is.EqualTo(1f).Within(1e-6));
            Assert.That(UiEase.InOutSine(.5f), Is.EqualTo(.5f).Within(1e-5));
            Assert.That(UiEase.InOutSine(.25f), Is.EqualTo(.146447f).Within(1e-5));
            Assert.That(UiEase.InOutSine(-1f), Is.EqualTo(0f).Within(1e-6));
            Assert.That(UiEase.InOutSine(2f), Is.EqualTo(1f).Within(1e-6));
            Monotone(UiEase.InOutSine);
        }

        [Test]
        public void PopIsZeroAtBothEndsOneInTheMiddleAndZeroOutside()
        {
            Assert.That(UiEase.Pop(0f), Is.EqualTo(0f).Within(1e-6));
            Assert.That(UiEase.Pop(1f), Is.EqualTo(0f).Within(1e-6));
            Assert.That(UiEase.Pop(.5f), Is.EqualTo(1f).Within(1e-6));
            Assert.That(UiEase.Pop(.25f), Is.EqualTo(.707107f).Within(1e-5));
            Assert.That(UiEase.Pop(-.5f), Is.EqualTo(0f));
            Assert.That(UiEase.Pop(1.5f), Is.EqualTo(0f));
        }

        [Test]
        public void TowardMovesByAtMostMaxDeltaAndNeverOvershoots()
        {
            Assert.That(UiEase.Toward(0f, 1f, .3f), Is.EqualTo(.3f).Within(1e-6));
            Assert.That(UiEase.Toward(1f, 0f, .3f), Is.EqualTo(.7f).Within(1e-6));
            Assert.That(UiEase.Toward(.9f, 1f, .3f), Is.EqualTo(1f));
            Assert.That(UiEase.Toward(.1f, 0f, .5f), Is.EqualTo(0f));
            Assert.That(UiEase.Toward(.5f, .5f, .3f), Is.EqualTo(.5f));
            Assert.That(UiEase.Toward(.2f, 1f, -1f), Is.EqualTo(.2f), "a negative step counts as 0");
        }

        [Test]
        public void AFadeInReachesOneInInSecondsWithinOneStepAndNeverFalls()
        {
            float a = 0f, last = 0f; int steps = 0;
            while (a < 1f && steps < 1000)
            {
                a = UiEase.FadeStep(a, true, Dt, .12f, .25f, false); steps++;
                Assert.That(a, Is.GreaterThan(last), "strictly rising on step " + steps);
                Assert.That(a, Is.LessThanOrEqualTo(1f));
                last = a;
            }
            Assert.That(a, Is.EqualTo(1f));
            Assert.That(steps * Dt, Is.InRange(.12f - Dt * 1.01f, .12f + Dt * 1.01f), "seconds to full");
            Assert.That(UiEase.FadeStep(1f, true, Dt, .12f, .25f, false), Is.EqualTo(1f));
        }

        [Test]
        public void AFadeOutReachesZeroInOutSecondsWithinOneStepAndNeverRises()
        {
            float a = 1f, last = 1f; int steps = 0;
            while (a > 0f && steps < 1000)
            {
                a = UiEase.FadeStep(a, false, Dt, .12f, .25f, false); steps++;
                Assert.That(a, Is.LessThan(last), "strictly falling on step " + steps);
                Assert.That(a, Is.GreaterThanOrEqualTo(0f));
                last = a;
            }
            Assert.That(a, Is.EqualTo(0f));
            Assert.That(steps * Dt, Is.InRange(.25f - Dt * 1.01f, .25f + Dt * 1.01f), "seconds to empty");
        }

        [Test]
        public void AFadeWithNoTimePassingStaysWhereItIs()
        {
            Assert.That(UiEase.FadeStep(.3f, true, 0f, .12f, .25f, false), Is.EqualTo(.3f));
            Assert.That(UiEase.FadeStep(.3f, false, 0f, .12f, .25f, false), Is.EqualTo(.3f));
            Assert.That(UiEase.FadeStep(.3f, true, Dt, .12f, .25f, false), Is.GreaterThan(.3f));
        }

        [Test]
        public void ReducedMotionIsExactlyZeroOrOne()
        {
            Assert.That(UiEase.FadeStep(.3f, true, Dt, .12f, .25f, true), Is.EqualTo(1f));
            Assert.That(UiEase.FadeStep(.3f, false, Dt, .12f, .25f, true), Is.EqualTo(0f));
            Assert.That(UiEase.FadeStep(0f, true, 0f, .12f, .25f, true), Is.EqualTo(1f));
        }

        [Test]
        public void ANonPositiveDurationIsInstant()
        {
            Assert.That(UiEase.FadeStep(.2f, true, Dt, 0f, .25f, false), Is.EqualTo(1f));
            Assert.That(UiEase.FadeStep(.2f, true, Dt, -1f, .25f, false), Is.EqualTo(1f));
            Assert.That(UiEase.FadeStep(.8f, false, Dt, .12f, 0f, false), Is.EqualTo(0f));
            Assert.That(UiEase.FadeStep(.8f, false, Dt, .12f, -2f, false), Is.EqualTo(0f));
        }

        [Test]
        public void ApproachEasesWithoutOvershootAndLandsExactly()
        {
            float v = 0f, first = UiEase.Approach(0f, 1f, Dt, .5f, false);
            Assert.That(first, Is.GreaterThan(0f).And.LessThan(1f), "one frame moves part of the gap");
            for (int i = 0; i < 30; i++)
            {
                float next = UiEase.Approach(v, 1f, Dt, .5f, false);
                Assert.That(next, Is.GreaterThanOrEqualTo(v)); Assert.That(next, Is.LessThanOrEqualTo(1f));
                v = next;
            }
            Assert.That(1f - v, Is.LessThanOrEqualTo(.15f), "after `seconds` the gap is at most 15 percent of the start");
            for (int i = 0; i < 600; i++) v = UiEase.Approach(v, 1f, Dt, .5f, false);
            Assert.That(v, Is.EqualTo(1f), "lands exactly on the target");
        }

        [Test]
        public void ApproachWorksDownwardAndAHugeStepStopsAtTheTarget()
        {
            float v = 1f;
            for (int i = 0; i < 30; i++)
            {
                float next = UiEase.Approach(v, .2f, Dt, .5f, false);
                Assert.That(next, Is.LessThanOrEqualTo(v)); Assert.That(next, Is.GreaterThanOrEqualTo(.2f));
                v = next;
            }
            Assert.That(v, Is.LessThan(.9f));
            Assert.That(UiEase.Approach(0f, 1f, 10f, .2f, false), Is.EqualTo(1f));
            Assert.That(UiEase.Approach(1f, .2f, 10f, .2f, false), Is.EqualTo(.2f));
        }

        [Test]
        public void ApproachJumpsUnderEpsilonAndUnderReducedMotion()
        {
            Assert.That(UiEase.Approach(.9995f, 1f, Dt, .5f, false), Is.EqualTo(1f));
            Assert.That(UiEase.Approach(.99f, 1f, Dt, .5f, false, .05f), Is.EqualTo(1f));
            Assert.That(UiEase.Approach(0f, 1f, Dt, .5f, true), Is.EqualTo(1f));
            Assert.That(UiEase.Approach(.5f, .25f, Dt, .5f, true), Is.EqualTo(.25f));
            Assert.That(UiEase.Approach(0f, 1f, 0f, .5f, false), Is.EqualTo(0f), "no time, no move");
        }
    }
}
