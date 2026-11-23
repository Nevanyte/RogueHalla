using System;
using NUnit.Framework;
using Roguehalla.Net.Ui;

namespace Roguehalla.Net.Tests
{
    /// <summary>P-UI-TABMAP-MARKS-1: the scale of the Tab map's marks layer, from the map's width and the stage's pixels per HUD unit.</summary>
    public sealed class MapMarkScaleTests
    {
        static readonly float[] Scales = { .8f, .9f, 1f, 1.2f, 1.35f, 1.5f };
        static readonly float[] Sides = { 240f, 400f, 624f, 700f, 784f, 820f };

        [Test]
        public void TheScaleAtFiveStages()
        {
            Assert.AreEqual(1.300f, MapMarkScale.Layer(624f, .9f), .001f, "1280 x 720");
            Assert.AreEqual(1.300f, MapMarkScale.Layer(624f, 1.35f), .001f, "1920 x 1080");
            Assert.AreEqual(1.400f, MapMarkScale.Layer(700f, .8f), .001f, "1024 x 768");
            Assert.AreEqual(1.400f, MapMarkScale.Layer(784f, 1.5f), .001f, "2560 x 1440");
            Assert.AreEqual(1.389f, MapMarkScale.Layer(384f, .8f), .001f, "640 x 480 stacked: the pixel floor wins");
        }

        [Test]
        public void AMarkLabelIsNeverUnderTenPixels()
        {
            foreach (var scale in Scales)
                foreach (var side in Sides)
                    Assert.GreaterOrEqual(MapMarkScale.LabelUnits * MapMarkScale.Layer(side, scale) * scale, MapMarkScale.MinLabelPx - .001f, "side " + side + " scale " + scale);
        }

        [Test]
        public void NeverSmallerThanTheMinimapAndNeverPastTheGuard()
        {
            var all = new System.Collections.Generic.List<float>(Scales) { .1f };
            foreach (var scale in all)
                foreach (var side in Sides)
                {
                    float k = MapMarkScale.Layer(side, scale);
                    Assert.GreaterOrEqual(k, 1f, "side " + side + " scale " + scale);
                    Assert.LessOrEqual(k, MapMarkScale.Max, "side " + side + " scale " + scale);
                }
            Assert.AreEqual(MapMarkScale.Max, MapMarkScale.Layer(784f, .1f), .0001f, "a tiny stage scale wants more than the guard allows: it gets the guard");
            var odd = new[] { 0f, -1f, float.NaN, float.PositiveInfinity };
            foreach (var bad in odd)
            {
                Assert.AreEqual(1f, MapMarkScale.Layer(bad, 1f), "side " + bad);
                Assert.AreEqual(1f, MapMarkScale.Layer(624f, bad), "scale " + bad);
            }
        }

        [Test]
        public void ItGrowsWithTheMapAndStops()
        {
            var sides = new[] { 240f, 480f, 560f, 624f, 672f, 784f, 820f };
            float last = 0f;
            foreach (var side in sides)
            {
                float k = MapMarkScale.Layer(side, 1.35f);
                Assert.GreaterOrEqual(k, last, "does not decrease at " + side);
                last = k;
            }
            Assert.AreEqual(1f, MapMarkScale.Layer(480f, 1.35f), .0001f, "base size at the reference side");
            foreach (var side in new[] { 672f, 784f, 820f })
                Assert.AreEqual(MapMarkScale.MaxGrowth, MapMarkScale.Layer(side, 1.35f), .0001f, "full growth from 672 on, at " + side);
        }
    }
}
