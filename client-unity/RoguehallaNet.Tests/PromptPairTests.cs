using System;
using NUnit.Framework;
using Roguehalla.Net.Ui;

namespace Roguehalla.Net.Tests
{
    /// <summary>
    /// P-UI-PAIR-LANE-NARROW-1: the prompt lane scaled with the action row. The pickup pill and the quest launcher share one lane;
    /// on a narrow stage the row shrinks (HudRegions.AbilityRowScale) and the lane shrinks with it as one layout:
    /// HudRegions.PromptPair(index, scale) and HudRegions.PromptLone(scale) are the stage rectangles after scaling.
    /// </summary>
    public sealed class PromptPairTests
    {
        const float Eps = .001f, Worked = .05f, H = 800f;

        static float RowScale(float w, float minimap) => HudRegions.AbilityRowScale(w, 262f, w - 12f - minimap);

        // stage width, minimap, row scale, pill left and right, launcher left and right, lone left and right (all worked numbers of the packet)
        [TestCase(1422f, 270f, 1f, 487f, 707f, 715f, 935f, 561f, 861f)]
        [TestCase(1280f, 270f, 1f, 416f, 636f, 644f, 864f, 490f, 790f)]
        [TestCase(1040f, 270f, 1f, 296f, 516f, 524f, 744f, 370f, 670f)]
        [TestCase(900f, 270f, .6987f, 293.5f, 447.2f, 452.8f, 606.5f, 345.2f, 554.8f)]
        [TestCase(800f, 236f, .6f, 265.6f, 397.6f, 402.4f, 534.4f, 310f, 490f)]
        public void ThePairIsAdjacentCentredAndInsideAtFiveWidths(float w, float minimap, float expectedScale,
            float pillLeft, float pillRight, float launcherLeft, float launcherRight, float loneLeft, float loneRight)
        {
            float s = RowScale(w, minimap);
            Assert.That(s, Is.EqualTo(expectedScale).Within(Eps), "set-up: the row scale at stage " + w);
            var left = HudRegions.PromptPair(0, s).On(w, H);
            var right = HudRegions.PromptPair(1, s).On(w, H);
            Assert.That(left.Overlaps(right), Is.False, "the pair does not overlap");
            Assert.That(right.Left - left.Right, Is.EqualTo(8f * s).Within(Eps), "the gap is 8 s");
            Assert.That(left.Top, Is.EqualTo(right.Top).Within(Eps), "equal top");
            Assert.That(left.Bottom, Is.EqualTo(right.Bottom).Within(Eps), "equal bottom");
            Assert.That((left.Left + right.Right) / 2f, Is.EqualTo(w / 2f).Within(Eps), "the union is centred");
            Assert.That(left.Inside(w, H), Is.True, "pill inside the stage");
            Assert.That(right.Inside(w, H), Is.True, "launcher inside the stage");
            Assert.That(left.Width / 220f, Is.EqualTo(s).Within(Eps), "pill width over 220 is s");
            Assert.That(right.Width / 220f, Is.EqualTo(s).Within(Eps), "launcher width over 220 is s");
            Assert.That(left.Height / 26f, Is.EqualTo(s).Within(Eps), "height over 26 is s");
            Assert.That(right.Height / 26f, Is.EqualTo(s).Within(Eps));
            Assert.That(H - left.Bottom, Is.EqualTo(14f + 116f * s).Within(Eps), "the bottom edge is 14 + 116 s above the stage bottom");
            Assert.That(H - right.Bottom, Is.EqualTo(14f + 116f * s).Within(Eps));
            Assert.That(left.Left, Is.EqualTo(pillLeft).Within(Worked), "pill left");
            Assert.That(left.Right, Is.EqualTo(pillRight).Within(Worked), "pill right");
            Assert.That(right.Left, Is.EqualTo(launcherLeft).Within(Worked), "launcher left");
            Assert.That(right.Right, Is.EqualTo(launcherRight).Within(Worked), "launcher right");
            // the lone prompt of the same table row is pinned in its own test below, with these numbers
            var lone = HudRegions.PromptLone(s).On(w, H);
            Assert.That(lone.Left, Is.EqualTo(loneLeft).Within(Worked), "lone left");
            Assert.That(lone.Right, Is.EqualTo(loneRight).Within(Worked), "lone right");
        }

        [Test]
        public void AtScaleOneItIsTheOldLane()
        {
            for (int i = 0; i < 2; i++)
            {
                var old = HudRegions.PromptPair(i); var now = HudRegions.PromptPair(i, 1f);
                Assert.That(now.Anchor, Is.EqualTo(old.Anchor), "anchor " + i);
                Assert.That(now.X, Is.EqualTo(old.X).Within(Eps), "x " + i);
                Assert.That(now.Y, Is.EqualTo(old.Y).Within(Eps), "y " + i);
                Assert.That(now.Width, Is.EqualTo(old.Width).Within(Eps), "width " + i);
                Assert.That(now.Height, Is.EqualTo(old.Height).Within(Eps), "height " + i);
            }
            var lone = HudRegions.PromptLone(1f); var prompt = HudRegions.Prompt();
            Assert.That(lone.Anchor, Is.EqualTo(prompt.Anchor), "lone anchor");
            Assert.That(lone.X, Is.EqualTo(prompt.X).Within(Eps), "lone x");
            Assert.That(lone.Y, Is.EqualTo(prompt.Y).Within(Eps), "lone y");
            Assert.That(lone.Width, Is.EqualTo(prompt.Width).Within(Eps), "lone width");
            Assert.That(lone.Height, Is.EqualTo(prompt.Height).Within(Eps), "lone height");
        }

        [Test]
        public void PairScaleClampsAndFallsBack()
        {
            Assert.That(HudRegions.PairScale(float.NaN), Is.EqualTo(1f), "NaN");
            Assert.That(HudRegions.PairScale(float.PositiveInfinity), Is.EqualTo(1f), "+inf");
            Assert.That(HudRegions.PairScale(float.NegativeInfinity), Is.EqualTo(1f), "-inf");
            Assert.That(HudRegions.PairScale(.3f), Is.EqualTo(.6f).Within(Eps), "under the floor");
            Assert.That(HudRegions.PairScale(1.4f), Is.EqualTo(1f).Within(Eps), "over one");
            Assert.That(HudRegions.PairScale(.8f), Is.EqualTo(.8f).Within(Eps), "inside the range");
            Assert.That(HudRegions.PairScaleMin, Is.EqualTo(HudRegions.AbilityRowScale(600f, 262f, 478f)).Within(Eps), "the floor is the row's floor");
            var nan = HudRegions.PromptLone(float.NaN); var prompt = HudRegions.Prompt();
            Assert.That(nan.Width, Is.EqualTo(prompt.Width).Within(Eps), "NaN lone width");
            Assert.That(nan.Y, Is.EqualTo(prompt.Y).Within(Eps), "NaN lone y");
            var low = HudRegions.PromptLone(.3f); var floor = HudRegions.PromptLone(.6f);
            Assert.That(low.Width, Is.EqualTo(floor.Width).Within(Eps), "a scale under the floor is the floor");
            Assert.That(low.Height, Is.EqualTo(floor.Height).Within(Eps));
            Assert.That(floor.Width, Is.EqualTo(180f).Within(Eps), "300 x .6");
        }

        [Test]
        public void ThePairStaysInsideTheRowFootprint()
        {
            for (float w = 800f; w <= 1500f; w += 20f)
            {
                float minimap = Math.Min(270f, (float)Math.Floor(w / 2f - 164f));
                float s = RowScale(w, minimap);
                var left = HudRegions.PromptPair(0, s).On(w, H);
                var right = HudRegions.PromptPair(1, s).On(w, H);
                Assert.That(left.Width, Is.GreaterThan(0f), "w " + w + ": the pill has a size");
                Assert.That(left.Left, Is.GreaterThanOrEqualTo(w / 2f - HudRegions.AbilityWidth * s / 2f - Eps), "w " + w + ": pill inside the row's left edge");
                Assert.That(right.Right, Is.LessThanOrEqualTo(w / 2f + HudRegions.AbilityWidth * s / 2f + Eps), "w " + w + ": launcher inside the row's right edge");
            }
        }

        [TestCase(1422f, 270f)]
        [TestCase(1280f, 270f)]
        [TestCase(1040f, 270f)]
        [TestCase(900f, 270f)]
        [TestCase(800f, 236f)]
        public void TheLonePromptIsTheScaledLane(float w, float minimap)
        {
            float s = RowScale(w, minimap);
            var lone = HudRegions.PromptLone(s).On(w, H);
            var pill = HudRegions.PromptPair(0, s).On(w, H);
            Assert.That(lone.Left, Is.EqualTo(w - lone.Right).Within(Eps), "centred");
            Assert.That(lone.Inside(w, H), Is.True, "inside the stage");
            Assert.That(lone.Width, Is.EqualTo(300f * s).Within(Eps), "300 s wide");
            Assert.That(lone.Height, Is.EqualTo(26f * s).Within(Eps), "26 s high");
            Assert.That(H - lone.Bottom, Is.EqualTo(14f + 116f * s).Within(Eps), "the bottom edge is 14 + 116 s above the stage bottom");
            Assert.That(lone.Top, Is.EqualTo(pill.Top).Within(Eps), "the pair's top");
            Assert.That(lone.Bottom, Is.EqualTo(pill.Bottom).Within(Eps), "the pair's bottom");
            Assert.That(lone.Height, Is.EqualTo(pill.Height).Within(Eps), "the pair's height");
            if (w == 900f)
            {
                Assert.That(lone.Left, Is.EqualTo(345.2f).Within(Worked), "900 left");
                Assert.That(lone.Right, Is.EqualTo(554.8f).Within(Worked), "900 right");
                Assert.That(lone.Width, Is.EqualTo(209.6f).Within(Worked), "900 width");
                Assert.That(H - lone.Bottom, Is.EqualTo(95.05f).Within(Worked), "900 bottom");
                Assert.That(lone.Height, Is.EqualTo(18.17f).Within(Worked), "900 height");
            }
            if (w == 800f)
            {
                Assert.That(lone.Left, Is.EqualTo(310f).Within(Worked), "800 left");
                Assert.That(lone.Right, Is.EqualTo(490f).Within(Worked), "800 right");
                Assert.That(H - lone.Bottom, Is.EqualTo(83.6f).Within(Worked), "800 bottom");
                Assert.That(lone.Height, Is.EqualTo(15.6f).Within(Worked), "800 height");
            }
        }
    }
}
