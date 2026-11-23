using NUnit.Framework;
using Roguehalla.Net.Ui;

namespace Roguehalla.Net.Tests
{
    /// <summary>P-UI-VITALS-EASE-1 oracle: the pure state machine of an eased bar with a ghost of lost value.</summary>
    public sealed class BarEaseTests
    {
        const float Dt = .05f, Tol = .001f;

        static void Near(float actual, float expected, string what)
        {
            Assert.That(actual, Is.EqualTo(expected).Within(Tol), what);
        }

        static BarEase Fall()
        {
            var e = default(BarEase);
            e.Snap(1f);
            e.Step(.7f, 0f, false);
            return e;
        }

        [Test]
        public void FirstStepSnaps()
        {
            var e = default(BarEase);
            e.Step(.5f, Dt, false);
            Near(e.Shown, .5f, "shown after the first step");
            Near(e.Ghost, .5f, "ghost after the first step");
            Assert.That(e.Has, Is.True);
            e.Snap(1f);
            e.Clear();
            Assert.That(e.Has, Is.False);
            e.Step(.5f, Dt, false);
            Near(e.Shown, .5f, "shown after Clear and a step");
            Near(e.Ghost, .5f, "ghost after Clear and a step");
        }

        [Test]
        public void FallsAtSixPerSecondAndRisesAtThree()
        {
            var e = Fall();
            Near(e.Shown, 1f, "a delivery with dt 0 moves nothing");
            e.Step(.7f, Dt, false);
            Near(e.Shown, .70f, "fall step 1: 1 - 6 * .05");

            var r = default(BarEase);
            r.Snap(.7f);
            r.Step(1f, 0f, false);
            r.Step(1f, Dt, false);
            Near(r.Shown, .85f, "rise step 1: .70 + 3 * .05");
            Near(r.Ghost, .85f, "a rise puts the ghost on the shown fill");
            r.Step(1f, Dt, false);
            Near(r.Shown, 1f, "rise step 2");
            Near(r.Ghost, 1f, "rise ghost 2");

            var big = Fall();
            big.Step(.5f, 10f, false);
            Assert.That(big.Shown, Is.EqualTo(.5f), "a step larger than the gap lands exactly");
        }

        [Test]
        public void GhostHoldsThenFalls()
        {
            var e = Fall();
            Near(e.Ghost, 1f, "ghost at the delivery");
            Near(e.GhostHold, .25f, "hold at the delivery");
            float[] ghost = { 1f, 1f, 1f, 1f, 1f, .9f, .8f, .7f };
            for (int i = 0; i < 8; i++)
            {
                e.Step(.7f, Dt, false);
                Near(e.Shown, .7f, "shown at step " + (i + 1));
                Near(e.Ghost, ghost[i], "ghost at step " + (i + 1));
                if (i == 0) Near(e.GhostHold, .20f, "hold after step 1");
            }
            Near(e.GhostHold, 0f, "hold spent");

            // Restart: a further fall during the hold leaves the ghost and restarts the hold.
            var r = Fall();
            for (int i = 0; i < 3; i++) r.Step(.7f, Dt, false);
            Near(r.GhostHold, .10f, "hold after step 3");
            r.Step(.5f, 0f, false);
            Near(r.Ghost, 1f, "the ghost stays where it is");
            Near(r.GhostHold, .25f, "the hold restarts");
        }

        [Test]
        public void ARiseKeepsARunningGhost()
        {
            var e = Fall();
            e.Step(.7f, Dt, false); e.Step(.7f, Dt, false);
            e.Step(.8f, Dt, false);
            Near(e.Shown, .80f, "the rise lands on the target");
            Near(e.Ghost, 1f, "the ghost survives the rise");
            Near(e.GhostHold, .10f, "the hold keeps counting");
            e.Step(.8f, Dt, false); e.Step(.8f, Dt, false);
            Near(e.Ghost, 1f, "ghost still held");
            e.Step(.8f, Dt, false);
            Near(e.Ghost, .9f, "ghost falls");
            e.Step(.8f, Dt, false);
            Near(e.Ghost, .8f, "ghost reaches the shown fill");

            var lift = Fall();
            lift.Step(.7f, Dt, false); lift.Step(.7f, Dt, false);
            lift.Step(1f, Dt, false);
            Near(lift.Shown, .85f, "rise from .70");
            Near(lift.Ghost, 1f, "ghost above the shown fill stays");

            var up = default(BarEase);
            up.Snap(.7f);
            up.Step(1f, 0f, false);
            up.Step(1f, Dt, false);
            Near(up.Shown, .85f, "plain rise");
            Near(up.Ghost, .85f, "ghost follows a plain rise");
            up.Step(1f, Dt, false);
            Near(up.Ghost, 1f, "ghost follows a plain rise to the end");
        }

        [Test]
        public void NotDamageMovesAtTheRiseRateAndKeepsTheSum()
        {
            var e = default(BarEase);
            e.Snap(1f);
            e.Step(.5f, Dt, false, false);
            Near(e.Shown, .85f, "rate 3, not 6");
            Near(e.Ghost, .85f, "no ghost");
            Near(e.GhostHold, 0f, "no hold");

            var health = default(BarEase); var barrier = default(BarEase);
            health.Snap(1f); barrier.Snap(0f);
            health.Step(2f / 3f, 0f, false, false); barrier.Step(1f / 3f, 0f, false, false);
            for (int i = 0; i < 3; i++)
            {
                health.Step(2f / 3f, Dt, false, false);
                barrier.Step(1f / 3f, Dt, false, false);
                Near(health.Shown + barrier.Shown, 1f, "sum at step " + (i + 1));
                Near(health.Ghost, health.Shown, "health ghost equals shown at step " + (i + 1));
            }
            Near(health.Shown, 2f / 3f, "health landed");
            Near(barrier.Shown, 1f / 3f, "barrier landed");
        }

        [Test]
        public void LargeRisesSnapAndLargeFallsEase()
        {
            var e = default(BarEase);
            e.Snap(.3f);
            e.Step(.95f, Dt, false);
            Near(e.Shown, .95f, "a rise of .65 snaps");
            Near(e.Ghost, .95f, "and the ghost with it");

            e.Snap(.3f);
            e.Step(.85f, Dt, false);
            Near(e.Shown, .45f, "a rise of .55 eases");
            Near(e.Target, .85f, "target");

            e.Snap(0f);
            e.Step(.61f, Dt, false);
            Near(e.Shown, .61f, "0 to .61 snaps");
            e.Snap(0f);
            e.Step(.59f, Dt, false);
            Near(e.Shown, .15f, "0 to .59 eases");

            e.Snap(1f);
            e.Step(.2f, Dt, false);
            Near(e.Shown, .70f, "a big fall eases");
            Near(e.Ghost, 1f, "and keeps its ghost");

            e.Snap(0f);
            e.Step(.5f, 0f, false);
            e.Step(1f, 0f, false);
            Near(e.Shown, 0f, "each rise is measured from the previous target (.5)");
            Near(e.Target, 1f, "target moved");
        }

        [Test]
        public void ReducedSnapsWithoutAGhost()
        {
            var e = default(BarEase);
            e.Snap(1f);
            e.Step(.4f, Dt, true);
            Near(e.Shown, .4f, "reduced fall");
            Near(e.Ghost, .4f, "reduced fall ghost");
            e.Step(.9f, Dt, true);
            Near(e.Shown, .9f, "reduced rise");
            Near(e.Ghost, .9f, "reduced rise ghost");
            e.Step(.1f, 0f, true, false);
            Near(e.Shown, .1f, "reduced not damage");
            Near(e.Ghost, .1f, "reduced not damage ghost");
        }

        [Test]
        public void BadInputsAreSafeAndConstantsMatch()
        {
            var e = default(BarEase);
            e.Snap(.2f);
            e.Step(3f, 0f, false);
            Near(e.Shown, 1f, "a rise over .6 snaps to the clamped target");

            e.Snap(.8f);
            e.Step(-1f, 10f, false);
            Near(e.Shown, 0f, "negative target");
            e.Snap(.8f);
            e.Step(float.NaN, 10f, false);
            Near(e.Shown, 0f, "NaN target");

            e.Snap(1f);
            e.Step(.7f, float.NaN, false);
            Near(e.Shown, 1f, "NaN dt");
            e.Snap(1f);
            e.Step(.7f, -1f, false);
            Near(e.Shown, 1f, "negative dt");

            Assert.That(BarEase.FallPerSecond, Is.EqualTo(6f));
            Assert.That(BarEase.RisePerSecond, Is.EqualTo(3f));
            Assert.That(BarEase.GhostHoldSeconds, Is.EqualTo(.25f));
            Assert.That(BarEase.GhostFallPerSecond, Is.EqualTo(2f));
            Assert.That(BarEase.SnapJump, Is.EqualTo(.6f));
        }
    }
}
