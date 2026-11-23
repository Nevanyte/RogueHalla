using System;
using System.Collections.Generic;
using NUnit.Framework;
using Roguehalla.Net.Ui;

namespace Roguehalla.Net.Tests
{
    /// <summary>
    /// P-UI-TOAST-LANES-1: the pure state machine of one toast lane. Time is passed in (steps of 10 ms unless a test says
    /// otherwise); nothing here reads a clock. A short text holds 2.2 s (NoticeRouter.HoldSeconds).
    /// </summary>
    public sealed class ToastLaneModelTests
    {
        const float Dt = .01f, Hold = 2.2f, FadeIn = ToastLaneModel.FadeInSeconds, FadeOut = ToastLaneModel.FadeOutSeconds;

        static void Run(ToastLaneModel m, float seconds, float dt = Dt, bool visible = true, bool reduced = false)
        {
            int steps = (int)Math.Round(seconds / dt);
            for (int i = 0; i < steps; i++) m.Tick(dt, visible, reduced);
        }

        static int IndexOf(ToastLaneModel m, string text)
        {
            for (int i = 0; i < m.Count; i++) if (m.Line(i).Text == text) return i;
            return -1;
        }

        static string[] Texts(ToastLaneModel m)
        {
            var list = new List<string>();
            for (int i = 0; i < m.Count; i++) list.Add(m.Line(i).Text);
            return list.ToArray();
        }

        [Test]
        public void AEmptyOrNullTextIsIgnored()
        {
            var m = new ToastLaneModel(3);
            m.Push(null, NoticeTone.Info); m.Push("", NoticeTone.Info); m.Push("ok", NoticeTone.Warn);
            Run(m, .05f);
            Assert.AreEqual(1, m.Count);
            Assert.AreEqual("ok", m.Line(0).Text);
            Assert.AreEqual(NoticeTone.Warn, m.Line(0).Tone);
            Assert.AreEqual(1, m.Line(0).Count);
            Assert.IsFalse(m.Line(0).Sticky);
            Assert.AreEqual(0, m.Waiting);
        }

        [Test]
        public void ALineFadesInHoldsFadesOutAndIsGone()
        {
            var m = new ToastLaneModel(3);
            m.Push("hello", NoticeTone.Info);
            float t = 0, previous = -1f;
            bool sawPartial = false;
            var startedAt = new Dictionary<string, float>();
            for (int step = 0; step < 400; step++)
            {
                m.Tick(Dt, true, false); t += Dt;
                if (m.Count == 0) { Assert.Greater(t, FadeIn + Hold + FadeOut - .05f, "gone no earlier than its life"); break; }
                Assert.AreEqual(1, m.Count);
                float a = m.Line(0).Alpha;
                Assert.That(a, Is.InRange(0f, 1f));
                if (t < FadeIn - .02f)
                {
                    Assert.GreaterOrEqual(a, previous - 1e-5f, "alpha only rises while fading in");
                    if (a > .02f && a < .98f) sawPartial = true;
                }
                else if (t > FadeIn + .02f && t < FadeIn + Hold - .02f) Assert.AreEqual(1f, a, 1e-4f, "held fully opaque at " + t);
                else if (t > FadeIn + Hold + .02f)
                {
                    Assert.LessOrEqual(a, previous + 1e-5f, "alpha only falls while fading out");
                    if (a < .98f) sawPartial = true;
                }
                previous = a;
            }
            Assert.IsTrue(sawPartial, "the fade is a ramp, not a jump");
            Assert.AreEqual(0, m.Count, "the line is gone after fade in, hold and fade out");
            Assert.Less(t, FadeIn + Hold + FadeOut + .1f, "and not much later");
        }

        [Test]
        public void ALineIsFullyOpaqueForItsWholeHoldAndFadingAfterIt()
        {
            var m = new ToastLaneModel(3);
            m.Push("hello", NoticeTone.Info);
            Run(m, FadeIn + Hold - .05f);
            Assert.AreEqual(1f, m.Line(0).Alpha, 1e-4f);
            Run(m, .05f + FadeOut * .5f);
            Assert.AreEqual(1, m.Count);
            Assert.Less(m.Line(0).Alpha, .9f);
            Assert.Greater(m.Line(0).Alpha, .1f);
            Run(m, FadeOut * .5f + .05f);
            Assert.AreEqual(0, m.Count);
        }

        [Test]
        public void ALongerTextHoldsLonger()
        {
            var m = new ToastLaneModel(3);
            string text = new string('a', 40);       // 3.1 s
            m.Push(text, NoticeTone.Info);
            Run(m, FadeIn + 2.9f);
            Assert.AreEqual(1, m.Count);
            Assert.AreEqual(1f, m.Line(0).Alpha, 1e-4f, "still held at 3.0 s");
            Run(m, .2f + FadeOut + .1f);
            Assert.AreEqual(0, m.Count);
        }

        [Test]
        public void TheSameTextAgainIsOneLineWithACounterAndAFreshHold()
        {
            var m = new ToastLaneModel(3);
            m.Push("Wolf down", NoticeTone.Good);
            Run(m, .5f);
            m.Push("Wolf down", NoticeTone.Good);
            Run(m, .01f);
            Assert.AreEqual(1, m.Count);
            Assert.AreEqual("Wolf down x2", m.Line(0).Text);
            Assert.AreEqual(2, m.Line(0).Count);
            Assert.AreEqual(1f, m.Line(0).Alpha, 1e-4f, "no flicker when the counter changes");
            Run(m, 1.5f);
            m.Push("Wolf down", NoticeTone.Good);
            Run(m, .01f);
            Assert.AreEqual("Wolf down x3", m.Line(0).Text);
            // the hold restarted at the last push: opaque 2.0 s later (the first push alone would have faded at 2.32 s)
            Run(m, 2.0f);
            Assert.AreEqual(1, m.Count);
            Assert.AreEqual(1f, m.Line(0).Alpha, 1e-4f);
            Run(m, .3f + FadeOut + .1f);
            Assert.AreEqual(0, m.Count);
        }

        [Test]
        public void TheSameTextWhileItWaitsAlsoCounts()
        {
            var m = new ToastLaneModel(1);
            m.Push("first", NoticeTone.Info);
            m.Push("second", NoticeTone.Info); m.Push("second", NoticeTone.Info);
            Run(m, .05f);
            Assert.AreEqual(1, m.Waiting);
            Run(m, 3f);
            Assert.AreEqual("second x2", m.Line(0).Text);
        }

        [Test]
        public void AKeyedPushReplacesTheTextInPlaceWithoutACounterAndRestartsTheHold()
        {
            var m = new ToastLaneModel(3);
            m.Push("Imp: +1 kill stack", NoticeTone.Good, "k");
            Run(m, .5f);
            m.Push("Imp: +2 kill stacks", NoticeTone.Good, "k");
            Run(m, .01f);
            Assert.AreEqual(1, m.Count);
            Assert.AreEqual("Imp: +2 kill stacks", m.Line(0).Text);
            Assert.AreEqual(1, m.Line(0).Count);
            Assert.AreEqual(1f, m.Line(0).Alpha, 1e-4f);
            Run(m, 1.8f);
            m.Push("Imp: +3 kill stacks", NoticeTone.Good, "k");
            Run(m, 2.0f);
            Assert.AreEqual(1, m.Count);
            Assert.AreEqual("Imp: +3 kill stacks", m.Line(0).Text);
            Assert.AreEqual(1f, m.Line(0).Alpha, 1e-4f, "the hold restarted with the last push");
        }

        [Test]
        public void AKeyedPushMakesNoSecondLineBesideOtherLines()
        {
            var m = new ToastLaneModel(3);
            m.Push("Quest accepted: Letters", NoticeTone.Info);
            m.Push("Imp: +1 kill stack", NoticeTone.Good, "k");
            Run(m, .3f);
            m.Push("Imp: +2 kill stacks", NoticeTone.Good, "k");
            Run(m, .05f);
            CollectionAssert.AreEqual(new[] { "Quest accepted: Letters", "Imp: +2 kill stacks" }, Texts(m));
        }

        [Test]
        public void AKeyedPushReplacesTheTextOfAWaitingLine()
        {
            var m = new ToastLaneModel(1);
            m.Push("first", NoticeTone.Info);
            m.Push("Imp: +1 kill stack", NoticeTone.Good, "k");
            Run(m, .05f);
            Assert.AreEqual(1, m.Waiting);
            m.Push("Imp: +2 kill stacks", NoticeTone.Good, "k");
            Run(m, .05f);
            Assert.AreEqual(1, m.Waiting, "replaced, not added");
            Run(m, 3f);
            Assert.AreEqual(1, m.Count);
            Assert.AreEqual("Imp: +2 kill stacks", m.Line(0).Text);
            Assert.AreEqual(1, m.Line(0).Count);
        }

        [Test]
        public void AFullLaneMakesTheNewLineWaitUntilTheOldestHasBeenSeenForASecond()
        {
            var m = new ToastLaneModel(3);
            m.Push("A", NoticeTone.Info); m.Push("B", NoticeTone.Info); m.Push("C", NoticeTone.Info);
            Run(m, .2f);
            m.Push("D", NoticeTone.Info);
            Run(m, .01f);
            Assert.AreEqual(3, m.Count);
            Assert.AreEqual(1, m.Waiting);
            Assert.AreEqual(-1, IndexOf(m, "D"));
            // A has been fully visible since 0.12 s: 1 s later is 1.12 s.
            Run(m, .8f);                                    // t = 1.01
            Assert.AreEqual(1f, m.Line(IndexOf(m, "A")).Alpha, 1e-4f, "A is not asked to leave before it was seen for a second");
            Assert.AreEqual(1, m.Waiting);
            Run(m, .19f);                                   // t = 1.20: A has begun to fade
            int a = IndexOf(m, "A");
            Assert.GreaterOrEqual(a, 0);
            Assert.Less(m.Line(a).Alpha, 1f, "A starts fading once it has been fully visible for MinShowSeconds");
            Run(m, FadeOut + .1f);
            Assert.AreEqual(-1, IndexOf(m, "A"));
            Assert.GreaterOrEqual(IndexOf(m, "D"), 0, "D takes the freed slot");
            Assert.AreEqual(0, m.Waiting);
            Assert.AreEqual(3, m.Count);
            Assert.GreaterOrEqual(IndexOf(m, "B"), 0);
            Assert.GreaterOrEqual(IndexOf(m, "C"), 0);
        }

        [Test]
        public void FivePushesInOneFrameShowThreeKeepTwoWaitingAndAllShowInOrder()
        {
            var m = new ToastLaneModel(3);
            for (int i = 1; i <= 5; i++) m.Push("n" + i, NoticeTone.Info);
            Run(m, .01f);
            Assert.AreEqual(3, m.Count);
            Assert.AreEqual(2, m.Waiting);
            CollectionAssert.AreEqual(new[] { "n1", "n2", "n3" }, Texts(m));
            var seen = new List<string>();
            for (int step = 0; step < 3000; step++)
            {
                m.Tick(Dt, true, false);
                Assert.LessOrEqual(m.Count, 3);
                for (int i = 0; i < m.Count; i++) if (!seen.Contains(m.Line(i).Text)) seen.Add(m.Line(i).Text);
            }
            CollectionAssert.AreEqual(new[] { "n1", "n2", "n3", "n4", "n5" }, seen);
            Assert.AreEqual(0, m.Count);
            Assert.AreEqual(0, m.Waiting);
        }

        [Test]
        public void MoreThanSixWaitingDropsTheOldestWaitingOnes()
        {
            var m = new ToastLaneModel(3);
            for (int i = 1; i <= 3; i++) m.Push("n" + i, NoticeTone.Info);
            Run(m, .01f);
            for (int i = 4; i <= 11; i++) m.Push("n" + i, NoticeTone.Info);     // 8 queued behind three shown: n4 and n5 are dropped
            Run(m, .01f);
            Assert.AreEqual(3, m.Count);
            Assert.AreEqual(ToastLaneModel.MaxWaiting, m.Waiting);
            var seen = new List<string>();
            for (int step = 0; step < 6000; step++)
            {
                m.Tick(Dt, true, false);
                for (int i = 0; i < m.Count; i++) if (!seen.Contains(m.Line(i).Text)) seen.Add(m.Line(i).Text);
            }
            CollectionAssert.AreEqual(new[] { "n1", "n2", "n3", "n6", "n7", "n8", "n9", "n10", "n11" }, seen);
        }

        [Test]
        public void LinesKeepTheirOrderAndGlideToTheirNewSlotWhenOneLeaves()
        {
            var m = new ToastLaneModel(3);
            m.Push("A", NoticeTone.Info);
            Run(m, 1.0f);
            m.Push("B", NoticeTone.Info);
            Run(m, .3f);
            m.Push("C", NoticeTone.Info);
            Run(m, .3f);
            CollectionAssert.AreEqual(new[] { "A", "B", "C" }, Texts(m));
            Assert.AreEqual(0f, m.Line(0).Slot, 1e-4f); Assert.AreEqual(1f, m.Line(1).Slot, 1e-4f); Assert.AreEqual(2f, m.Line(2).Slot, 1e-4f);
            // A leaves by its own hold (gone at 2.57 s of its life; the loop runs from 1.6 s to 3.1 s): follow B and C until A is gone and they have settled
            float lastB = 1f, lastC = 2f;
            bool aGone = false, bMoved = false;
            for (int step = 0; step < 150; step++)
            {
                m.Tick(Dt, true, false);
                int b = IndexOf(m, "B"), c = IndexOf(m, "C");
                Assert.GreaterOrEqual(b, 0); Assert.GreaterOrEqual(c, 0);
                Assert.Less(b, c, "order kept");
                float sb = m.Line(b).Slot, sc = m.Line(c).Slot;
                Assert.LessOrEqual(Math.Abs(sb - lastB), Dt / ToastLaneModel.GlideSeconds + 1e-3f, "B glides, it does not jump");
                Assert.LessOrEqual(Math.Abs(sc - lastC), Dt / ToastLaneModel.GlideSeconds + 1e-3f, "C glides, it does not jump");
                Assert.LessOrEqual(sb, lastB + 1e-4f, "slots only fall"); Assert.LessOrEqual(sc, lastC + 1e-4f, "slots only fall");
                if (sb < .99f) bMoved = true;
                lastB = sb; lastC = sc;
                if (IndexOf(m, "A") < 0) aGone = true;
            }
            Assert.IsTrue(aGone, "A left");
            Assert.IsTrue(bMoved, "B moved up");
            Assert.AreEqual(0f, lastB, 1e-3f);
            Assert.AreEqual(1f, lastC, 1e-3f);
        }

        [Test]
        public void NothingAdvancesWhileHiddenAndPushesMadeMeanwhileAppearOldestFirstAfterwards()
        {
            var m = new ToastLaneModel(3);
            m.Push("A", NoticeTone.Info);
            Run(m, 1f);
            float alpha = m.Line(0).Alpha;
            Run(m, 30f, .1f, visible: false);
            Assert.AreEqual(1, m.Count);
            Assert.AreEqual(alpha, m.Line(0).Alpha, 1e-6f);
            m.Push("B", NoticeTone.Info); m.Push("C", NoticeTone.Info);
            Run(m, 30f, .1f, visible: false);
            int hiddenB = IndexOf(m, "B");
            if (hiddenB >= 0) Assert.AreEqual(0f, m.Line(hiddenB).Alpha, "a line that arrived while hidden has not started to appear");
            Run(m, .3f);
            CollectionAssert.AreEqual(new[] { "A", "B", "C" }, Texts(m));
            Assert.AreEqual(1f, m.Line(0).Alpha, 1e-4f, "A still has the hold it had when it was hidden");
            // A had 0.88 s of its 2.2 s hold behind it: it leaves 1.32 s after the HUD is back (t = 1.57 s with its fade);
            // B and C start their hold after 0.12 s of fade-in and live their full 2.2 s (gone at t = 2.57 s)
            Run(m, 1.0f);                                    // t = 1.3
            Assert.AreEqual(3, m.Count);
            Run(m, .4f);                                     // t = 1.7
            Assert.AreEqual(2, m.Count, "A is gone after its remaining hold");
            Run(m, .7f);                                     // t = 2.4
            Assert.AreEqual(2, m.Count, "B and C have their full hold");
            Run(m, .3f);                                     // t = 2.7
            Assert.AreEqual(0, m.Count);
        }

        [Test]
        public void AStickyLineStaysInSlotZeroPastAnyHoldAndChangesTextInPlace()
        {
            var m = new ToastLaneModel(2);
            m.SetSticky("Press WASD", NoticeTone.Info);
            Run(m, .3f);
            Assert.AreEqual(1, m.Count);
            Assert.IsTrue(m.Line(0).Sticky);
            Assert.AreEqual(0f, m.Line(0).Slot, 1e-4f);
            Assert.AreEqual("Press WASD", m.Line(0).Text);
            Run(m, 40f);
            Assert.AreEqual(1, m.Count);
            Assert.AreEqual(1f, m.Line(0).Alpha, 1e-4f);
            m.SetSticky("Press the mouse", NoticeTone.Warn);
            Run(m, .01f);
            Assert.AreEqual(1, m.Count);
            Assert.AreEqual("Press the mouse", m.Line(0).Text);
            Assert.AreEqual(NoticeTone.Warn, m.Line(0).Tone);
            Assert.AreEqual(1f, m.Line(0).Alpha, 1e-4f, "changed in place: no fade restart");
            Assert.AreEqual(0f, m.Line(0).Slot, 1e-4f);
        }

        [Test]
        public void AStickyLineCountsTowardCapacityAndIsNeverEvicted()
        {
            var m = new ToastLaneModel(2);
            m.SetSticky("Hint", NoticeTone.Info);
            m.Push("A", NoticeTone.Info); m.Push("B", NoticeTone.Info); m.Push("C", NoticeTone.Info);
            Run(m, .3f);
            CollectionAssert.AreEqual(new[] { "Hint", "A" }, Texts(m));
            Assert.AreEqual(0f, m.Line(0).Slot, 1e-4f);
            Assert.AreEqual(1f, m.Line(1).Slot, 1e-4f);
            Assert.AreEqual(2, m.Waiting);
            var seen = new List<string>();
            for (int step = 0; step < 3000; step++)
            {
                m.Tick(Dt, true, false);
                Assert.LessOrEqual(m.Count, 2);
                Assert.IsTrue(m.Line(0).Sticky && m.Line(0).Text == "Hint", "the sticky line keeps slot 0 at step " + step);
                for (int i = 0; i < m.Count; i++) if (!seen.Contains(m.Line(i).Text)) seen.Add(m.Line(i).Text);
            }
            CollectionAssert.AreEqual(new[] { "Hint", "A", "B", "C" }, seen);
        }

        [Test]
        public void ClearingTheStickyLineFadesItOut()
        {
            var m = new ToastLaneModel(2);
            m.SetSticky("Hint", NoticeTone.Info);
            Run(m, 1f);
            m.SetSticky(null, NoticeTone.Info);
            Run(m, FadeOut * .5f);
            Assert.AreEqual(1, m.Count);
            Assert.Less(m.Line(0).Alpha, 1f);
            Run(m, FadeOut * .5f + .05f);
            Assert.AreEqual(0, m.Count);
        }

        [Test]
        public void ReducedMotionHasNoFadeAndNoGlide()
        {
            var m = new ToastLaneModel(3);
            m.Push("A", NoticeTone.Info); m.Push("B", NoticeTone.Info); m.Push("C", NoticeTone.Info); m.Push("D", NoticeTone.Info);
            m.Tick(Dt, true, true);
            Assert.AreEqual(3, m.Count);
            for (int i = 0; i < 3; i++) Assert.AreEqual(1f, m.Line(i).Alpha, "visible at once");
            var seen = new List<string>();
            for (int step = 0; step < 1500; step++)
            {
                m.Tick(Dt, true, true);
                for (int i = 0; i < m.Count; i++)
                {
                    float alpha = m.Line(i).Alpha, slot = m.Line(i).Slot;
                    Assert.IsTrue(alpha == 0f || alpha == 1f, "alpha is " + alpha);
                    Assert.AreEqual(Math.Round(slot), slot, 1e-6f, "slot is " + slot);
                    if (!seen.Contains(m.Line(i).Text)) seen.Add(m.Line(i).Text);
                }
            }
            CollectionAssert.AreEqual(new[] { "A", "B", "C", "D" }, seen);
            Assert.AreEqual(0, m.Count);
        }

        [Test]
        public void ClearEmptiesLinesWaitingAndTheStickyLine()
        {
            var m = new ToastLaneModel(2);
            m.SetSticky("Hint", NoticeTone.Info);
            for (int i = 0; i < 5; i++) m.Push("n" + i, NoticeTone.Info);
            Run(m, .3f);
            Assert.AreEqual(2, m.Count);
            Assert.Greater(m.Waiting, 0);
            m.Clear();
            Assert.AreEqual(0, m.Count);
            Assert.AreEqual(0, m.Waiting);
            Run(m, 5f);
            Assert.AreEqual(0, m.Count, "nothing comes back");
            m.Push("again", NoticeTone.Info);
            Run(m, .3f);
            Assert.AreEqual("again", m.Line(0).Text);
        }

        [Test]
        public void ATickThatChangesNothingAllocatesNothing()
        {
            var m = new ToastLaneModel(3);
            m.SetSticky("Hint", NoticeTone.Info);
            m.Push("A line that holds", NoticeTone.Good);
            m.Push("A line that holds", NoticeTone.Good);
            Run(m, .3f, .001f);                                           // past the fade-ins
            Assert.AreEqual(2, m.Count, "set-up: two lines are held");
            for (int i = 0; i < 50; i++) m.Tick(.001f, true, false);      // warm up
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 500; i++) { m.Tick(.001f, true, false); var l = m.Line(0); var l2 = m.Line(1); if (l.Text == null || l2.Text == null) Assert.Fail("lost text"); }
            long after = GC.GetAllocatedBytesForCurrentThread();
            Assert.AreEqual(0, after - before, "a steady Tick allocates nothing");
        }
    }
}
