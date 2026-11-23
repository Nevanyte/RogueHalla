using System;
using NUnit.Framework;
using Roguehalla.Net.Ui;
using UnityEngine;

namespace Roguehalla.Client.Tests
{
    /// <summary>
    /// P-UI-HUD-REGIONS-1: the Unity adapter. A rect made by <c>HudUi.Box(stage, name, region)</c> (or placed by
    /// <c>HudUi.Place(rect, region)</c>) measures, in stage units from the top-left, the same as <c>region.On(w, h)</c>.
    /// </summary>
    public sealed class HudRegionsEditModeTests
    {
        GameObject _owner;
        RectTransform _stage;

        [TearDown]
        public void TearDown()
        {
            if (_owner != null) UnityEngine.Object.DestroyImmediate(_owner);
        }

        void BuildStage(float w, float h)
        {
            _owner = new GameObject("HudRegions test stage", typeof(RectTransform));
            _stage = (RectTransform)_owner.transform;
            _stage.anchorMin = _stage.anchorMax = _stage.pivot = new Vector2(.5f, .5f);
            _stage.sizeDelta = new Vector2(w, h);
        }

        /// <summary>Left, top, width, height of a rect inside the stage, in stage units from the top-left.</summary>
        static HudStageRect Measure(RectTransform stage, RectTransform r)
        {
            var corners = new Vector3[4]; r.GetWorldCorners(corners);
            Vector3 min = stage.InverseTransformPoint(corners[0]), max = stage.InverseTransformPoint(corners[2]);
            float w = stage.rect.width, h = stage.rect.height;
            return new HudStageRect(min.x + w / 2f, h / 2f - max.y, max.x - min.x, max.y - min.y);
        }

        static readonly Vector2[] Stages = { new Vector2(1422, 800), new Vector2(1280, 960) };

        static HudRegion[] Regions() => new[]
        {
            HudRegions.AbilityRow(), HudRegions.FeedbackPill(1, 300f), HudRegions.EventPill(2, 300f), HudRegions.TrackerDock(200f, 2),
        };

        static void AssertAnchorsMatch()
        {
            Assert.That(Enum.GetNames(typeof(HudAnchor)), Is.EqualTo(Enum.GetNames(typeof(HudRegionAnchor))));
            foreach (HudRegionAnchor a in Enum.GetValues(typeof(HudRegionAnchor)))
                Assert.That((int)Enum.Parse(typeof(HudAnchor), a.ToString()), Is.EqualTo((int)a), a.ToString());
        }

        [TestCaseSource(nameof(Stages))]
        public void BoxFromARegionMeasuresTheSameAsOn(Vector2 stage)
        {
            AssertAnchorsMatch();
            BuildStage(stage.x, stage.y);
            var regions = Regions();
            for (int i = 0; i < regions.Length; i++)
            {
                var rect = HudUi.Box(_stage, "Region" + i, regions[i]);
                var expected = regions[i].On(stage.x, stage.y);
                var m = Measure(_stage, rect);
                string tag = "region " + i + " (" + regions[i].Anchor + ") on " + stage;
                Assert.That(expected.Width, Is.GreaterThan(0f), tag + ": the region has a size");
                Assert.That(m.Left, Is.EqualTo(expected.Left).Within(.01f), tag + " left");
                Assert.That(m.Top, Is.EqualTo(expected.Top).Within(.01f), tag + " top");
                Assert.That(m.Width, Is.EqualTo(expected.Width).Within(.01f), tag + " width");
                Assert.That(m.Height, Is.EqualTo(expected.Height).Within(.01f), tag + " height");
            }
        }

        [TestCaseSource(nameof(Stages))]
        public void PlaceFromARegionMovesAnExistingRectTheSameWay(Vector2 stage)
        {
            AssertAnchorsMatch();
            BuildStage(stage.x, stage.y);
            var regions = Regions();
            for (int i = 0; i < regions.Length; i++)
            {
                var rect = (RectTransform)new GameObject("Existing" + i, typeof(RectTransform)).transform;
                rect.SetParent(_stage, false);
                HudUi.Place(rect, regions[i]);
                var expected = regions[i].On(stage.x, stage.y);
                var m = Measure(_stage, rect);
                string tag = "region " + i + " (" + regions[i].Anchor + ") on " + stage;
                Assert.That(m.Left, Is.EqualTo(expected.Left).Within(.01f), tag + " left");
                Assert.That(m.Top, Is.EqualTo(expected.Top).Within(.01f), tag + " top");
                Assert.That(m.Width, Is.EqualTo(expected.Width).Within(.01f), tag + " width");
                Assert.That(m.Height, Is.EqualTo(expected.Height).Within(.01f), tag + " height");
            }
        }
    }
}
