using NUnit.Framework;
using Roguehalla.Net.Ui;

namespace Roguehalla.Net.Tests
{
    /// <summary>
    /// P-UI-HUD-REGIONS-1: the table of top-level HUD regions (HudRegions) and its collision proof. Rects are in HUD units on
    /// the stage (1 unit = 1 legacy CSS px, y from the top). Every layout check runs at 16:9, 4:3, 21:9 and 1600x900 stages.
    /// </summary>
    public sealed class HudRegionsTests
    {
        static readonly float[][] Stages =
        {
            new[] { 1422f, 800f }, new[] { 1280f, 960f }, new[] { 1866f, 800f }, new[] { 1600f, 900f },
        };

        static System.Collections.Generic.IEnumerable<TestCaseData> EachStage()
        {
            foreach (var s in Stages) yield return new TestCaseData(s[0], s[1]).SetName("Stage_" + s[0] + "x" + s[1]);
        }

        const float Eps = .01f;

        static void AssertSame(HudRegion a, HudRegion b, string what)
        {
            Assert.That(a.Anchor, Is.EqualTo(b.Anchor), what);
            Assert.That(a.X, Is.EqualTo(b.X).Within(Eps), what + " X");
            Assert.That(a.Y, Is.EqualTo(b.Y).Within(Eps), what + " Y");
            Assert.That(a.Width, Is.EqualTo(b.Width).Within(Eps), what + " Width");
            Assert.That(a.Height, Is.EqualTo(b.Height).Within(Eps), what + " Height");
        }

        // ---- geometry of the rect and the region -----------------------------------------------------------------------

        [Test]
        public void StageRectEdgesOverlapAndInsideFollowTheStrictRule()
        {
            var r = new HudStageRect(10, 20, 100, 50);
            Assert.That(r.Right, Is.EqualTo(110f).Within(Eps));
            Assert.That(r.Bottom, Is.EqualTo(70f).Within(Eps));
            Assert.That(r.Overlaps(new HudStageRect(100, 60, 50, 50)), Is.True, "corners intersect");
            Assert.That(r.Overlaps(new HudStageRect(110, 20, 50, 50)), Is.False, "touching right edge");
            Assert.That(r.Overlaps(new HudStageRect(10, 70, 100, 50)), Is.False, "touching bottom edge");
            Assert.That(r.Overlaps(new HudStageRect(30, 30, 10, 10)), Is.True, "contained");
            Assert.That(new HudStageRect(30, 30, 10, 10).Overlaps(r), Is.True, "symmetric");
            Assert.That(r.Inside(110, 70), Is.True, "exactly the stage");
            Assert.That(r.Inside(109, 70), Is.False, "too narrow");
            Assert.That(r.Inside(110, 69), Is.False, "too short");
            Assert.That(new HudStageRect(-1, 0, 5, 5).Inside(100, 100), Is.False, "left of the stage");
            Assert.That(new HudStageRect(0, -1, 5, 5).Inside(100, 100), Is.False, "above the stage");
        }

        [Test]
        public void OnAgreesWithTheHudUiBoxRuleForEachOfTheNineAnchors()
        {
            // stage 1000 x 600, region 100 x 50 with X = 10, Y = 20. Edge axes inset from their edge; centred axes are signed
            // offsets (right and up positive).
            const float W = 1000f, H = 600f;
            var cases = new[]
            {
                (HudRegionAnchor.TopLeft, 10f, 20f),
                (HudRegionAnchor.TopCenter, 460f, 20f),
                (HudRegionAnchor.TopRight, 890f, 20f),
                (HudRegionAnchor.MiddleLeft, 10f, 255f),
                (HudRegionAnchor.Center, 460f, 255f),
                (HudRegionAnchor.MiddleRight, 890f, 255f),
                (HudRegionAnchor.BottomLeft, 10f, 530f),
                (HudRegionAnchor.BottomCenter, 460f, 530f),
                (HudRegionAnchor.BottomRight, 890f, 530f),
            };
            foreach (var (anchor, left, top) in cases)
            {
                var r = new HudRegion(anchor, 10f, 20f, 100f, 50f).On(W, H);
                Assert.That(r.Left, Is.EqualTo(left).Within(Eps), anchor + " Left");
                Assert.That(r.Top, Is.EqualTo(top).Within(Eps), anchor + " Top");
                Assert.That(r.Width, Is.EqualTo(100f).Within(Eps), anchor + " Width");
                Assert.That(r.Height, Is.EqualTo(50f).Within(Eps), anchor + " Height");
            }
        }

        [Test]
        public void BottomAnchorInsetFortyEightWithHeightSeventySixIsTopSixSevenSixOnAnEightHundredStage()
        {
            var r = new HudRegion(HudRegionAnchor.BottomCenter, 0, 48, 458, 76).On(1422, 800);
            Assert.That(r.Top, Is.EqualTo(676f).Within(Eps));
            Assert.That(r.Left, Is.EqualTo((1422f - 458f) / 2f).Within(Eps));
        }

        // ---- 1. the ability row ----------------------------------------------------------------------------------------

        [Test]
        public void AbilityWidthIs458AndTheRowIsCentredAtBottomFortyEight()
        {
            Assert.That(HudRegions.AbilityWidth, Is.EqualTo(458f));
            var row = HudRegions.AbilityRow();
            Assert.That(row.Anchor, Is.EqualTo(HudRegionAnchor.BottomCenter));
            Assert.That(row.X, Is.EqualTo(0f));
            Assert.That(row.Y, Is.EqualTo(48f));
            Assert.That(row.Width, Is.EqualTo(458f));
            Assert.That(row.Height, Is.EqualTo(76f));
        }

        [Test]
        public void TheSixAbilityElementsTileTheRowLeftToRightWithGapsOfSix()
        {
            var row = HudRegions.AbilityRow();
            float[] widths = { 104, 28, 104, 64, 64, 64 };
            // Element rects are relative to the row: measure them on a stage the size of the row.
            HudStageRect prev = default;
            for (int i = 0; i < 6; i++)
            {
                var e = HudRegions.AbilityElement(i);
                Assert.That(e.Anchor, Is.EqualTo(HudRegionAnchor.BottomLeft), "element " + i);
                Assert.That(e.Width, Is.EqualTo(widths[i]).Within(Eps), "element " + i + " width");
                Assert.That(e.Y, Is.EqualTo(0f).Within(Eps), "element " + i + " sits on the row's bottom edge");
                var r = e.On(row.Width, row.Height);
                Assert.That(r.Inside(row.Width, row.Height), Is.True, "element " + i + " inside the row");
                Assert.That(r.Bottom, Is.EqualTo(row.Height).Within(Eps), "element " + i + " bottom on the row's bottom edge");
                if (i == 1) Assert.That(r.Height, Is.InRange(1f, 76f), "the swap chip is shorter than the row");
                else Assert.That(r.Height, Is.EqualTo(i == 0 || i == 2 ? 76f : 64f).Within(Eps), "element " + i + " height");
                if (i > 0) Assert.That(r.Left - prev.Right, Is.EqualTo(6f).Within(Eps), "gap before element " + i);
                else Assert.That(r.Left, Is.EqualTo(0f).Within(Eps));
                prev = r;
            }
            Assert.That(prev.Right, Is.EqualTo(row.Width).Within(Eps), "the last tile ends at the row's right edge");
        }

        [Test]
        public void ConsumablesAndPromptAreCentredBottomRegionsOfTheFixedSizes()
        {
            var c = HudRegions.Consumables();
            Assert.That(c.Anchor, Is.EqualTo(HudRegionAnchor.BottomCenter));
            Assert.That(c.X, Is.EqualTo(0f)); Assert.That(c.Y, Is.EqualTo(14f));
            Assert.That(c.Width, Is.EqualTo(352f)); Assert.That(c.Height, Is.EqualTo(28f));
            var p = HudRegions.Prompt();
            Assert.That(p.Anchor, Is.EqualTo(HudRegionAnchor.BottomCenter));
            Assert.That(p.X, Is.EqualTo(0f)); Assert.That(p.Y, Is.EqualTo(130f));
            Assert.That(p.Width, Is.EqualTo(300f)); Assert.That(p.Height, Is.EqualTo(26f));
            Assert.That(HudRegions.Prompt(200f).Width, Is.EqualTo(200f));
        }

        // ---- 2. the centre-bottom stack --------------------------------------------------------------------------------

        [TestCaseSource(nameof(EachStage))]
        public void TheCentreBottomStackIsInsideDisjointAndOrderedBottomUpWithGaps(float w, float h)
        {
            var regions = new[]
            {
                HudRegions.Consumables(), HudRegions.AbilityRow(), HudRegions.Prompt(),
                HudRegions.FeedbackPill(0, HudRegions.LaneWidth(w)), HudRegions.FeedbackPill(1, HudRegions.LaneWidth(w)),
            };
            var rects = new HudStageRect[regions.Length];
            for (int i = 0; i < regions.Length; i++)
            {
                rects[i] = regions[i].On(w, h);
                Assert.That(rects[i].Width, Is.GreaterThan(0f), "region " + i + " has a size");
                Assert.That(rects[i].Inside(w, h), Is.True, "region " + i + " inside the stage");
                for (int j = 0; j < i; j++) Assert.That(rects[i].Overlaps(rects[j]), Is.False, "regions " + i + " and " + j);
                if (i > 0) Assert.That(rects[i - 1].Top - rects[i].Bottom, Is.GreaterThanOrEqualTo(4f - Eps), "gap between " + (i - 1) + " and " + i);
            }
        }

        // ---- 3. feedback below, events above the combat zone -----------------------------------------------------------

        [Test]
        public void TheCombatZoneIsTheCentredShareOfTheStage()
        {
            var z = HudRegions.CombatZone(1422, 800);
            Assert.That(z.Width, Is.EqualTo(1422f * .46f).Within(Eps));
            Assert.That(z.Height, Is.EqualTo(352f).Within(Eps));
            Assert.That(z.Left, Is.EqualTo((1422f - z.Width) / 2f).Within(Eps));
            Assert.That(z.Top, Is.EqualTo(224f).Within(Eps));
        }

        [TestCaseSource(nameof(EachStage))]
        public void FeedbackPillsSitBelowAndEventPillsAboveTheCombatZone(float w, float h)
        {
            var zone = HudRegions.CombatZone(w, h);
            float lane = HudRegions.LaneWidth(w);
            for (int i = 0; i < HudRegions.FeedbackMax; i++)
            {
                var r = HudRegions.FeedbackPill(i, lane).On(w, h);
                Assert.That(r.Inside(w, h), Is.True, "feedback " + i + " inside");
                Assert.That(r.Overlaps(zone), Is.False, "feedback " + i + " overlaps the combat zone");
                Assert.That(r.Top, Is.GreaterThanOrEqualTo(zone.Bottom - Eps), "feedback " + i + " is below the zone");
            }
            for (int i = 0; i < HudRegions.EventMax; i++)
            {
                var r = HudRegions.EventPill(i, lane).On(w, h);
                Assert.That(r.Inside(w, h), Is.True, "event " + i + " inside");
                Assert.That(r.Overlaps(zone), Is.False, "event " + i + " overlaps the combat zone");
                Assert.That(r.Bottom, Is.LessThanOrEqualTo(zone.Top + Eps), "event " + i + " is above the zone");
            }
        }

        [Test]
        public void FeedbackPillsStackUpFromTheFeedbackBottomTwentyEightApart()
        {
            var a = HudRegions.FeedbackPill(0, 300);
            var b = HudRegions.FeedbackPill(1, 300);
            Assert.That(a.Anchor, Is.EqualTo(HudRegionAnchor.BottomCenter));
            Assert.That(a.X, Is.EqualTo(0f));
            Assert.That(a.Y, Is.EqualTo(162f).Within(Eps));
            Assert.That(b.Y, Is.EqualTo(190f).Within(Eps));
            Assert.That(a.Width, Is.EqualTo(300f)); Assert.That(a.Height, Is.EqualTo(24f));
            Assert.That(b.Height, Is.EqualTo(24f));
        }

        // ---- 4. the event lane -----------------------------------------------------------------------------------------

        [Test]
        public void EventPillsStartAtFortySixAndStepTwentyEightOrStartBelowTheTopCentreStack()
        {
            Assert.That(HudRegions.EventPill(0, 300).Anchor, Is.EqualTo(HudRegionAnchor.TopCenter));
            Assert.That(HudRegions.EventPill(0, 300).X, Is.EqualTo(0f));
            for (int i = 0; i < 3; i++)
            {
                var p = HudRegions.EventPill(i, 300);
                Assert.That(p.Y, Is.EqualTo(46f + 28f * i).Within(Eps), "default pill " + i);
                Assert.That(p.Height, Is.EqualTo(24f).Within(Eps));
                Assert.That(p.Width, Is.EqualTo(300f).Within(Eps));
                var q = HudRegions.EventPill(i, 300, 74f);
                Assert.That(q.Y, Is.EqualTo(82f + 28f * i).Within(Eps), "pill " + i + " under two top-centre pills");
            }
            Assert.That(HudRegions.EventPill(0, 300, 10f).Y, Is.EqualTo(46f).Within(Eps), "never above EventTop");
            Assert.That(HudRegions.EventPill(0, 300, 38f).Y, Is.EqualTo(46f).Within(Eps), "timer only");
        }

        // ---- 5. lane width ---------------------------------------------------------------------------------------------

        [Test]
        public void LaneWidthIs460OnAWideStageAndTheStageMinusTwentyFourOnANarrowOne()
        {
            Assert.That(HudRegions.LaneWidth(1422f), Is.EqualTo(460f).Within(Eps));
            Assert.That(HudRegions.LaneWidth(1866f), Is.EqualTo(460f).Within(Eps));
            Assert.That(HudRegions.LaneWidth(484f), Is.EqualTo(460f).Within(Eps));
            Assert.That(HudRegions.LaneWidth(483f), Is.EqualTo(459f).Within(Eps));
            Assert.That(HudRegions.LaneWidth(400f), Is.EqualTo(376f).Within(Eps));
            Assert.That(HudRegions.LaneWidth(1422f, 300f), Is.EqualTo(300f).Within(Eps));
            Assert.That(HudRegions.LaneWidth(200f, 300f), Is.EqualTo(176f).Within(Eps));
        }

        // ---- 6. the left column ----------------------------------------------------------------------------------------

        [TestCaseSource(nameof(EachStage))]
        public void TrackerAndOfferCardAreInsideDisjointAndLeftOfTheCombatZone(float w, float h)
        {
            var zone = HudRegions.CombatZone(w, h);
            float championTop = h - 198f;
            foreach (float plate in new[] { 150f, 200f, 260f })
                for (int rows = 0; rows <= 3; rows++)
                    foreach (float wanted in new[] { 120f, 220f, 400f })
                    {
                        string tag = " (plate " + plate + ", rows " + rows + ", wanted " + wanted + ")";
                        var tracker = HudRegions.TrackerDock(plate, rows).On(w, h);
                        var offerRegion = HudRegions.OfferCard(h, plate, rows, wanted, championTop);
                        var offer = offerRegion.On(w, h);
                        Assert.That(tracker.Inside(w, h), Is.True, "tracker inside" + tag);
                        Assert.That(offer.Inside(w, h), Is.True, "offer inside" + tag);
                        Assert.That(tracker.Top, Is.GreaterThanOrEqualTo(plate - Eps), "tracker under the plate chain" + tag);
                        Assert.That(offer.Top, Is.GreaterThanOrEqualTo(plate - Eps), "offer under the plate chain" + tag);
                        Assert.That(tracker.Right, Is.LessThanOrEqualTo(zone.Left + Eps), "tracker left of the zone" + tag);
                        Assert.That(offer.Right, Is.LessThanOrEqualTo(zone.Left + Eps), "offer left of the zone" + tag);
                        Assert.That(offer.Width, Is.EqualTo(324f).Within(Eps), "offer width" + tag);
                        if (rows > 0)
                        {
                            Assert.That(tracker.Width, Is.EqualTo(304f).Within(Eps), "tracker width" + tag);
                            Assert.That(tracker.Overlaps(offer), Is.False, "tracker and offer disjoint" + tag);
                        }
                        Assert.That(offer.Bottom, Is.LessThanOrEqualTo(championTop - 8f + Eps), "offer ends 8 above the champion panel" + tag);
                        Assert.That(offer.Height, Is.InRange(96f - Eps, wanted + Eps), "offer height" + tag);
                    }
        }

        [Test]
        public void TrackerDockHasTheHeaderPlusRowsAndNoHeightWithoutRows()
        {
            Assert.That(HudRegions.TrackerDock(200f, 0).Height, Is.EqualTo(0f));
            for (int rows = 1; rows <= 3; rows++)
            {
                var t = HudRegions.TrackerDock(200f, rows);
                Assert.That(t.Anchor, Is.EqualTo(HudRegionAnchor.TopLeft));
                Assert.That(t.X, Is.EqualTo(12f).Within(Eps));
                Assert.That(t.Width, Is.EqualTo(304f).Within(Eps));
                Assert.That(t.Y, Is.InRange(200f - Eps, 208f + Eps), "starts at or just below the plate chain, within one TrackerGap");
                Assert.That(t.Height, Is.InRange(18f + rows * 38f - Eps, 18f + rows * 46f + Eps), "rows " + rows);
            }
        }

        [Test]
        public void OfferCardIsCentredWhenThatClearsTheTrackerAndSitsJustBelowItOtherwise()
        {
            // Nothing in the tracker: the card is exactly centred and takes its wanted height.
            var free = HudRegions.OfferCard(800f, 150f, 0, 220f, 602f);
            Assert.That(free.Anchor, Is.EqualTo(HudRegionAnchor.TopLeft));
            Assert.That(free.X, Is.EqualTo(12f).Within(Eps));
            Assert.That(free.Y, Is.EqualTo(290f).Within(Eps), "(800 - 220) / 2");
            Assert.That(free.Height, Is.EqualTo(220f).Within(Eps));

            // A wanted height that would run into the champion panel is cut so the card ends 8 above championTop.
            var cut = HudRegions.OfferCard(800f, 150f, 0, 400f, 602f);
            Assert.That(cut.Y, Is.EqualTo(200f).Within(Eps));
            Assert.That(cut.Height, Is.EqualTo(394f).Within(Eps), "602 - 8 - 200");

            // A full tracker reaches past the centre: the card moves to just below it.
            var tracker = HudRegions.TrackerDock(260f, 3).On(1422f, 800f);
            var below = HudRegions.OfferCard(800f, 260f, 3, 150f, 602f);
            Assert.That(below.Y, Is.InRange(tracker.Bottom - Eps, tracker.Bottom + 8f + Eps), "just below the tracker");
            Assert.That(below.Y, Is.GreaterThan(325f + Eps), "not centred ((800 - 150) / 2)");
            Assert.That(below.Height, Is.EqualTo(150f).Within(Eps));
        }

        [Test]
        public void OfferCardNeverShrinksBelowTheMinimumEvenWhenTheChampionPanelIsClose()
        {
            var squeezed = HudRegions.OfferCard(800f, 150f, 0, 220f, 300f);
            Assert.That(squeezed.Y, Is.EqualTo(290f).Within(Eps));
            Assert.That(squeezed.Height, Is.EqualTo(96f).Within(Eps), "the minimum wins over the champion limit");
        }

        // ---- 8. clamping -----------------------------------------------------------------------------------------------

        [Test]
        public void OutOfRangeIndexesAndRowCountsAreClampedToTheNearestValidOne()
        {
            AssertSame(HudRegions.AbilityElement(-1), HudRegions.AbilityElement(0), "element -1");
            AssertSame(HudRegions.AbilityElement(99), HudRegions.AbilityElement(5), "element 99");
            Assert.That(HudRegions.AbilityElement(99).X, Is.EqualTo(394f).Within(Eps), "element 5 is the last tile");
            AssertSame(HudRegions.FeedbackPill(-1, 300), HudRegions.FeedbackPill(0, 300), "feedback -1");
            AssertSame(HudRegions.FeedbackPill(99, 300), HudRegions.FeedbackPill(1, 300), "feedback 99");
            Assert.That(HudRegions.FeedbackPill(99, 300).Y, Is.EqualTo(190f).Within(Eps));
            AssertSame(HudRegions.EventPill(-1, 300), HudRegions.EventPill(0, 300), "event -1");
            AssertSame(HudRegions.EventPill(99, 300), HudRegions.EventPill(2, 300), "event 99");
            Assert.That(HudRegions.EventPill(99, 300).Y, Is.EqualTo(102f).Within(Eps));
            AssertSame(HudRegions.TrackerDock(200f, -1), HudRegions.TrackerDock(200f, 0), "rows -1");
            AssertSame(HudRegions.TrackerDock(200f, 9), HudRegions.TrackerDock(200f, 3), "rows 9");
            Assert.That(HudRegions.TrackerDock(200f, 9).Height, Is.GreaterThanOrEqualTo(18f + 3 * 38f - Eps));
            AssertSame(HudRegions.OfferCard(800f, 260f, 9, 220f, 602f), HudRegions.OfferCard(800f, 260f, 3, 220f, 602f), "offer rows 9");
            AssertSame(HudRegions.OfferCard(800f, 260f, -1, 220f, 602f), HudRegions.OfferCard(800f, 260f, 0, 220f, 602f), "offer rows -1");
            Assert.That(HudRegions.OfferCard(800f, 260f, 0, 220f, 602f).Width, Is.EqualTo(324f).Within(Eps));
        }
    }
}
