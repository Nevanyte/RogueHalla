using System;
using NUnit.Framework;
using Roguehalla.Net.Ui;

namespace Roguehalla.Net.Tests
{
    /// <summary>
    /// P-UI-ABILITY-ROW-1: the pure half of the action row. What a quest spell tile shows (AbilityFaces.Spell), how long a
    /// cooldown was when first seen (CooldownSpan), the scale of the row on a narrow stage (HudRegions.AbilityRowScale) and the
    /// two-pill prompt lane (HudRegions.PromptPair).
    /// </summary>
    public sealed class AbilityFaceTests
    {
        const float Eps = .001f;

        // ---- AbilityFaces.Spell ----------------------------------------------------------------------------------------

        [Test]
        public void NoSpellIsEmptyWithNoBadgeAndNoCastBar()
        {
            var face = AbilityFaces.Spell(false, 3, 9000, 1000, 5000);
            Assert.That(face.Phase, Is.EqualTo(AbilityPhase.Empty));
            Assert.That(face.Badge, Is.EqualTo(-1), "no badge");
            Assert.That(face.Cast, Is.EqualTo(-1f), "no cast bar");
            Assert.That(face.Sweep, Is.EqualTo(0f));
            Assert.That(face.Seconds, Is.EqualTo(0));
            Assert.That(face, Is.EqualTo(AbilityFace.Empty));
            Assert.That(AbilityFace.Empty.Badge, Is.EqualTo(-1));
            Assert.That(AbilityFace.Empty.Cast, Is.EqualTo(-1f));
        }

        [Test]
        public void ASpellWithNoTimeLeftIsReadyWithItsChargesAndNoNumber()
        {
            foreach (double readyAt in new[] { 0d, 1000d, 400d })   // at the time, long past, in the past
            {
                var face = AbilityFaces.Spell(true, 2, readyAt, 1000, 5000);
                Assert.That(face.Phase, Is.EqualTo(AbilityPhase.Ready), "readyAt " + readyAt);
                Assert.That(face.Sweep, Is.EqualTo(0f));
                Assert.That(face.Seconds, Is.EqualTo(0));
                Assert.That(face.Badge, Is.EqualTo(2));
                Assert.That(face.Cast, Is.EqualTo(-1f), "quest spells deliver no cast window");
            }
        }

        [Test]
        public void ACoolingSpellShowsWholeSecondsRoundedUpAndTheShareOfTheTotalLeft()
        {
            var face = AbilityFaces.Spell(true, 0, 5200, 1000, 10000);   // 4,200 ms left of 10,000
            Assert.That(face.Phase, Is.EqualTo(AbilityPhase.Cooling));
            Assert.That(face.Seconds, Is.EqualTo(5));
            Assert.That(face.Sweep, Is.EqualTo(.42f).Within(Eps));
            Assert.That(face.Badge, Is.EqualTo(0));
            Assert.That(face.Cast, Is.EqualTo(-1f));

            Assert.That(AbilityFaces.Spell(true, 0, 2000, 1000, 10000).Seconds, Is.EqualTo(1), "1,000 ms left is one second");
            Assert.That(AbilityFaces.Spell(true, 0, 2001, 1000, 10000).Seconds, Is.EqualTo(2), "1,001 ms left rounds up");
        }

        [Test]
        public void ACoolingSpellWithNoKnownTotalDrawsAFullWedgeAndALongerLeftThanTotalClampsToOne()
        {
            Assert.That(AbilityFaces.Spell(true, 1, 5200, 1000, 0).Sweep, Is.EqualTo(1f).Within(Eps), "total 0");
            Assert.That(AbilityFaces.Spell(true, 1, 5200, 1000, -3).Sweep, Is.EqualTo(1f).Within(Eps), "negative total");
            var face = AbilityFaces.Spell(true, 1, 13000, 1000, 10000);   // 12,000 left of 10,000
            Assert.That(face.Phase, Is.EqualTo(AbilityPhase.Cooling));
            Assert.That(face.Sweep, Is.EqualTo(1f).Within(Eps), "clamped");
            Assert.That(face.Seconds, Is.EqualTo(12));
        }

        [Test]
        public void NanTimesCountAsZero()
        {
            Assert.That(AbilityFaces.Spell(true, 1, double.NaN, 1000, 5000).Phase, Is.EqualTo(AbilityPhase.Ready), "NaN readyAt");
            var face = AbilityFaces.Spell(true, 1, 5000, double.NaN, 10000);   // now counts as 0: 5,000 left
            Assert.That(face.Phase, Is.EqualTo(AbilityPhase.Cooling));
            Assert.That(face.Seconds, Is.EqualTo(5));
            Assert.That(face.Sweep, Is.EqualTo(.5f).Within(Eps));
        }

        // ---- CooldownSpan ----------------------------------------------------------------------------------------------

        [Test]
        public void ACooldownSpanKeepsTheLengthSeenFirstUntilTheReadyTimeChanges()
        {
            var span = new CooldownSpan();
            Assert.That(span.Total(6000, 1000), Is.EqualTo(5000d).Within(Eps), "first sight of a 5 s cooldown");
            Assert.That(span.Total(6000, 3000), Is.EqualTo(5000d).Within(Eps), "2 s later still 5 s");
            Assert.That(span.Total(6000, 5900), Is.EqualTo(5000d).Within(Eps), "almost over, still 5 s");
            Assert.That(span.Total(9000, 5900), Is.EqualTo(3100d).Within(Eps), "a new readyAt starts a new span at the time now left");
            Assert.That(span.Total(9000, 6000), Is.EqualTo(3100d).Within(Eps));
        }

        [Test]
        public void ACooldownSpanAnswersZeroWhenNothingIsLeftAndStartsFreshAfterwards()
        {
            var span = new CooldownSpan();
            Assert.That(span.Total(6000, 1000), Is.EqualTo(5000d).Within(Eps));
            Assert.That(span.Total(6000, 6000), Is.EqualTo(0d), "nothing left at the ready time");
            Assert.That(span.Total(6000, 7000), Is.EqualTo(0d), "nothing left after it");
            Assert.That(span.Total(15000, 9500), Is.EqualTo(5500d).Within(Eps), "the next cooldown starts fresh");
            Assert.That(span.Total(15000, 15000), Is.EqualTo(0d));
            // The same readyAt after the span was forgotten is a new span, not the old length.
            Assert.That(span.Total(15000, 14000), Is.EqualTo(1000d).Within(Eps), "forgotten span is not remembered");
        }

        [Test]
        public void ACooldownSpanNeverShrinksInsideASpanWhenTimeStepsBackwards()
        {
            var span = new CooldownSpan();
            Assert.That(span.Total(15000, 9500), Is.EqualTo(5500d).Within(Eps));
            Assert.That(span.Total(15000, 12000), Is.EqualTo(5500d).Within(Eps), "time forwards: unchanged");
            Assert.That(span.Total(15000, 9000), Is.EqualTo(6000d).Within(Eps), "time backwards: the largest left seen so far");
            Assert.That(span.Total(15000, 12000), Is.EqualTo(6000d).Within(Eps), "forwards again: never shrinks");
            Assert.That(span.Total(15000, 9400), Is.EqualTo(6000d).Within(Eps));
        }

        [Test]
        public void ASpellFaceFromASpanDrawsAWedgeThatShrinksFromOneToZero()
        {
            var span = new CooldownSpan();
            double ready = 6000, last = 1f;
            for (double now = 1000; now < ready; now += 500)
            {
                var face = AbilityFaces.Spell(true, 1, ready, now, span.Total(ready, now));
                Assert.That(face.Phase, Is.EqualTo(AbilityPhase.Cooling));
                Assert.That(face.Sweep, Is.LessThanOrEqualTo(last + Eps), "the wedge never grows");
                last = face.Sweep;
            }
            Assert.That(AbilityFaces.Spell(true, 1, ready, 1000, span.Total(ready, 1000)).Sweep, Is.EqualTo(1f).Within(Eps), "full at first sight");
            Assert.That(AbilityFaces.Spell(true, 1, ready, ready, span.Total(ready, ready)).Phase, Is.EqualTo(AbilityPhase.Ready));
        }

        // ---- HudRegions.AbilityRowScale --------------------------------------------------------------------------------

        [Test]
        public void TheRowScaleIsOneWhereItClearsAndTheBindingSideSetsItWhereItDoesNot()
        {
            Assert.That(HudRegions.AbilityRowScale(1422, 262, 1140), Is.EqualTo(1f).Within(Eps));
            Assert.That(HudRegions.AbilityRowScale(1000, 262, 684), Is.EqualTo(352f / 458f).Within(Eps), "the minimap side binds");
            Assert.That(HudRegions.AbilityRowScale(875, 262, 700), Is.EqualTo(335f / 458f).Within(Eps), "the champion side binds");
            Assert.That(HudRegions.AbilityRowScale(600, 262, 478), Is.EqualTo(.6f).Within(Eps), "never under .6");
        }

        [Test]
        public void TheRowScaleStaysBetweenPointSixAndOneAndTreatsBadInputAsOne()
        {
            for (float w = 300; w <= 2400; w += 50)
                foreach (float left in new[] { 100f, 262f, 400f })
                    foreach (float right in new[] { w / 2f - 40f, w / 2f + 160f, w - 100f })
                    {
                        float s = HudRegions.AbilityRowScale(w, left, right);
                        Assert.That(s, Is.InRange(.6f - Eps, 1f + Eps), "w " + w + " left " + left + " right " + right);
                    }
            Assert.That(HudRegions.AbilityRowScale(1000, 262, 684), Is.LessThan(1f), "set-up: a narrow stage really scales (the bad inputs below fall back to 1)");
            Assert.That(HudRegions.AbilityRowScale(float.NaN, 262, 700), Is.EqualTo(1f));
            Assert.That(HudRegions.AbilityRowScale(1000, float.NaN, 700), Is.EqualTo(1f));
            Assert.That(HudRegions.AbilityRowScale(1000, 262, float.NaN), Is.EqualTo(1f));
            Assert.That(HudRegions.AbilityRowScale(float.PositiveInfinity, 262, 700), Is.EqualTo(1f));
            Assert.That(HudRegions.AbilityRowScale(1000, 262, float.NegativeInfinity), Is.EqualTo(1f));
            Assert.That(HudRegions.AbilityRowScale(0, 262, 700), Is.EqualTo(1f), "stage width 0");
            Assert.That(HudRegions.AbilityRowScale(-5, 262, 700), Is.EqualTo(1f), "negative stage width");
        }

        [Test]
        public void TheScaledRowStaysEightShortOfTheLeastRoomTheMinimapFormulaLeaves()
        {
            // HudLayout.MinimapSize keeps the minimap's left edge at or beyond the centre line plus 152.
            const float champion = 12f + 250f;
            for (float w = 560; w <= 1500; w += 20)
            {
                float minimapLeft = w / 2f + 152f;
                float scale = HudRegions.AbilityRowScale(w, champion, minimapLeft);
                float rightEdge = w / 2f + scale * HudRegions.AbilityWidth / 2f;
                Assert.That(rightEdge, Is.LessThanOrEqualTo(minimapLeft - 8f + .01f), "width " + w + ": the row's right edge clears the minimap by 8");
            }
        }

        // ---- HudRegions.PromptPair -------------------------------------------------------------------------------------

        [TestCase(1280f, 800f)]
        [TestCase(1422f, 800f)]
        [TestCase(1707f, 800f)]
        public void TheTwoPromptPillsSitSideBySideFourFromTheCentreInsideThePromptLane(float w, float h)
        {
            Assert.That(HudRegions.PromptPairWidth, Is.EqualTo(220f));
            var lane = HudRegions.Prompt().On(w, h);
            var left = HudRegions.PromptPair(0).On(w, h);
            var right = HudRegions.PromptPair(1).On(w, h);
            Assert.That(HudRegions.PromptPair(0).Anchor, Is.EqualTo(HudRegionAnchor.BottomCenter));
            Assert.That(left.Width, Is.EqualTo(220f).Within(Eps)); Assert.That(right.Width, Is.EqualTo(220f).Within(Eps));
            Assert.That(left.Height, Is.EqualTo(HudRegions.PromptHeight).Within(Eps)); Assert.That(right.Height, Is.EqualTo(HudRegions.PromptHeight).Within(Eps));
            Assert.That(left.Right, Is.EqualTo(w / 2f - 4f).Within(Eps), "the left pill ends 4 left of the centre line");
            Assert.That(right.Left, Is.EqualTo(w / 2f + 4f).Within(Eps), "the right pill starts 4 right of the centre line");
            Assert.That(left.Overlaps(right), Is.False);
            foreach (var r in new[] { left, right })
            {
                Assert.That(r.Inside(w, h), Is.True, "inside the stage");
                Assert.That(r.Top, Is.EqualTo(lane.Top).Within(Eps), "same height as the prompt lane");
                Assert.That(r.Bottom, Is.EqualTo(lane.Bottom).Within(Eps));
            }
            Assert.That(HudRegions.PromptPair(0).Y, Is.EqualTo(HudRegions.PromptBottom).Within(Eps));
        }
    }
}
