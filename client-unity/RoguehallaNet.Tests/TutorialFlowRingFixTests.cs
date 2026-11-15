using System;
using NUnit.Framework;

namespace Roguehalla.Net.Tests
{
    /// <summary>P-TUTORIAL-2-fix: the step 1 ring must never contain the spawn, whatever the rack distance.</summary>
    public sealed class TutorialFlowRingFixTests
    {
        static TutorialObservation At(float x, float y, float rx, float ry) =>
            new TutorialObservation { HasSelf = true, X = x, Y = y, Weapon = WeaponId.Fists, HasRack = true, RackId = 9, RackX = rx, RackY = ry };

        static TutorialFlow Move() { var f = new TutorialFlow(); f.ResumeAt((int)TutorialStep.Move); return f; }

        static float Dist(float ax, float ay, float bx, float by) => (float)Math.Sqrt((ax - bx) * (ax - bx) + (ay - by) * (ay - by));

        [Test]
        public void TrainingGeometryFirstObservationDoesNotAdvanceAndRingIsClearOfSpawn()
        {
            var f = Move();
            Assert.IsFalse(f.Evaluate(At(784, 144, 880, 144)), "the spawn must not already be inside the ring");
            Assert.AreEqual(TutorialStep.Move, f.Step);
            Assert.AreEqual(TutorialTargetKind.Point, f.Target.Kind);
            float d = Dist(784, 144, f.Target.X, f.Target.Y);
            Assert.GreaterOrEqual(d, 2 * TutorialFlow.RingRadius);
            Assert.AreEqual(TutorialFlow.RingDistance, d, .01f);
            Assert.AreEqual(904f, f.Target.X, .01f); Assert.AreEqual(144f, f.Target.Y, .01f);
        }

        [Test]
        public void SweepOfRackDistancesAndDirectionsNeverAdvancesOnFirstObservation()
        {
            float[] dirs = { 0f, 45f, 90f, 135f, 180f, 225f, 270f, 315f, 17f };
            int cases = 0;
            foreach (float deg in dirs)
            {
                float ux = (float)Math.Cos(deg * Math.PI / 180), uy = (float)Math.Sin(deg * Math.PI / 180);
                for (float r = 1f; r <= 400f; r += 3f)
                {
                    var f = Move();
                    var o = At(784, 144, 784 + ux * r, 144 + uy * r);
                    Assert.IsFalse(f.Evaluate(o), $"advanced on first observation: dir {deg} rack {r}");
                    Assert.AreEqual(TutorialStep.Move, f.Step);
                    Assert.AreEqual(TutorialFlow.RingDistance, Dist(784, 144, f.Target.X, f.Target.Y), .01f, $"dir {deg} rack {r}");
                    Assert.AreEqual(TutorialFlow.RingRadius, f.Target.Radius, .001f);
                    cases++;
                }
            }
            Assert.GreaterOrEqual(cases, 1000);
        }

        [Test]
        public void WalkingIntoTheRingStillAdvancesAfterAFirstNonAdvancingObservation()
        {
            foreach (float r in new[] { 5f, 96f, 300f })
            {
                var f = Move();
                Assert.IsFalse(f.Evaluate(At(784, 144, 784 + r, 144)));
                Assert.IsFalse(f.Evaluate(At(784 + 30, 144, 784 + r, 144)), "still outside the ring");
                Assert.IsTrue(f.Evaluate(At(784 + 100, 144, 784 + r, 144)), $"rack {r}");
                Assert.AreEqual(TutorialStep.Pickup, f.Step);
            }
        }
    }
}
