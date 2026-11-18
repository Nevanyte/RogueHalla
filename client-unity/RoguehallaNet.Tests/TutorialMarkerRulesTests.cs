using System;
using NUnit.Framework;
using Roguehalla.Net.Ui;

namespace Roguehalla.Net.Tests
{
    public sealed class TutorialMarkerRulesTests
    {
        const float W = 1000f, H = 600f, M = 40f;

        [Test]
        public void CardNeedsEveryConditionAndNoHigherPanel()
        {
            Assert.IsTrue(TutorialMarkerRules.CardVisible(true, true, false, true));
            Assert.IsFalse(TutorialMarkerRules.CardVisible(false, true, false, true), "not confirmed practice");
            Assert.IsFalse(TutorialMarkerRules.CardVisible(true, false, false, true), "window unfocused");
            Assert.IsFalse(TutorialMarkerRules.CardVisible(true, true, true, true), "a higher panel is open");
            Assert.IsFalse(TutorialMarkerRules.CardVisible(true, true, false, false), "skipped or finished");
        }

        [Test]
        public void WelcomeShowsOnlyWhenDueOnTheMenuNotDismissedAndOutOfMatch()
        {
            Assert.IsTrue(TutorialMarkerRules.WelcomeVisible(true, true, false, false));
            Assert.IsFalse(TutorialMarkerRules.WelcomeVisible(false, true, false, false), "not due");
            Assert.IsFalse(TutorialMarkerRules.WelcomeVisible(true, false, false, false), "menu hidden");
            Assert.IsFalse(TutorialMarkerRules.WelcomeVisible(true, true, true, false), "dismissed this session");
            Assert.IsFalse(TutorialMarkerRules.WelcomeVisible(true, true, false, true), "in a match");
        }

        [Test]
        public void WelcomeResumesOnlyAStoredStepOfTheCurrentVersion()
        {
            int v = TutorialFlow.Version;
            Assert.AreEqual("Resume", TutorialMarkerRules.WelcomeAction(new AccountTutorialState(v, 3, "step"), v));
            Assert.AreEqual("Start", TutorialMarkerRules.WelcomeAction(null, v), "no stored progress");
            Assert.AreEqual("Start", TutorialMarkerRules.WelcomeAction(new AccountTutorialState(v, 0, "step"), v), "step 0");
            Assert.AreEqual("Start", TutorialMarkerRules.WelcomeAction(new AccountTutorialState(v - 1, 3, "step"), v), "older version");
            Assert.AreEqual("Start", TutorialMarkerRules.WelcomeAction(new AccountTutorialState(v, 3, "skipped"), v));
            Assert.AreEqual("Start", TutorialMarkerRules.WelcomeAction(new AccountTutorialState(v, 3, "done"), v));
            Assert.AreEqual("Start", TutorialMarkerRules.WelcomeAction(new AccountTutorialState(v, 3, "started"), v), "legacy started");
        }

        [Test]
        public void OnScreenPointIsUnchanged()
        {
            foreach (var p in new[] { new[] { 500f, 300f }, new[] { 0f, 0f }, new[] { W, H }, new[] { 12f, 590f } })
            {
                var m = TutorialMarkerRules.Place(p[0], p[1], true, W, H, M);
                Assert.IsTrue(m.OnScreen, p[0] + "," + p[1]);
                Assert.AreEqual(p[0], m.X, 1e-3f); Assert.AreEqual(p[1], m.Y, 1e-3f);
            }
        }

        [Test]
        public void OffScreenPointClampsToTheInsetRectangleAlongTheCentreRay()
        {
            var right = TutorialMarkerRules.Place(2500f, 300f, true, W, H, M);
            Assert.IsFalse(right.OnScreen);
            Assert.AreEqual(W - M, right.X, 1e-2f); Assert.AreEqual(300f, right.Y, 1e-2f); Assert.AreEqual(0f, right.Angle, 1e-3f);

            var up = TutorialMarkerRules.Place(500f, -900f, true, W, H, M);
            Assert.AreEqual(500f, up.X, 1e-2f); Assert.AreEqual(M, up.Y, 1e-2f); Assert.AreEqual(-Math.PI / 2, up.Angle, 1e-3);

            // Diagonal: bearing (+1000,+1000) from the centre hits the bottom edge first (half height 260 after inset < half width 460).
            var diag = TutorialMarkerRules.Place(1500f, 1300f, true, W, H, M);
            Assert.IsFalse(diag.OnScreen);
            Assert.AreEqual(H - M, diag.Y, 1e-2f); Assert.AreEqual(500f + 260f * 1000f / 1000f, diag.X, 1e-2f);
            Assert.AreEqual(Math.Atan2(1000, 1000), diag.Angle, 1e-3);
        }

        [Test]
        public void BehindTheCameraFlipsAndIsAlwaysOffScreen()
        {
            // A projected point inside the rectangle but behind the camera mirrors through the centre: (600,300) -> (400,300), points left.
            var m = TutorialMarkerRules.Place(600f, 300f, false, W, H, M);
            Assert.IsFalse(m.OnScreen);
            Assert.AreEqual(M, m.X, 1e-2f); Assert.AreEqual(300f, m.Y, 1e-2f);
            Assert.AreEqual(Math.PI, Math.Abs(m.Angle), 1e-3);

            var centre = TutorialMarkerRules.Place(500f, 300f, false, W, H, M);
            Assert.IsFalse(centre.OnScreen);
            Assert.AreEqual(500f, centre.X, 1e-2f); Assert.AreEqual(H - M, centre.Y, 1e-2f); Assert.AreEqual(Math.PI / 2, centre.Angle, 1e-3);
        }

        [Test]
        public void OffScreenResultsAlwaysLieInsideTheInsetRectangle()
        {
            var rng = new Random(7);
            int off = 0;
            for (int i = 0; i < 500; i++)
            {
                float x = (float)(rng.NextDouble() * 6000 - 3000), y = (float)(rng.NextDouble() * 4000 - 2000);
                bool front = rng.Next(2) == 0;
                var m = TutorialMarkerRules.Place(x, y, front, W, H, M);
                if (m.OnScreen) { Assert.IsTrue(front); continue; }
                off++;
                Assert.GreaterOrEqual(m.X, M - 1e-2f); Assert.LessOrEqual(m.X, W - M + 1e-2f);
                Assert.GreaterOrEqual(m.Y, M - 1e-2f); Assert.LessOrEqual(m.Y, H - M + 1e-2f);
            }
            Assert.GreaterOrEqual(off, 400, "the sweep must exercise the clamp");
        }

        [Test]
        public void StepLabelNumbersStepsAndFillsTheWishKey()
        {
            Assert.AreEqual("Step 1/5 · Right-click to walk.", TutorialMarkerRules.StepLabel(1, 5, "Right-click to walk.", "R"));
            Assert.AreEqual("Step 5/5 · Press R to cast your Wish", TutorialMarkerRules.StepLabel(5, 5, "Press {wish} to cast your Wish", "R"));
            Assert.AreEqual("Welcome to the Training grounds.", TutorialMarkerRules.StepLabel(0, 5, "Welcome to the Training grounds.", "R"), "welcome has no prefix");
            Assert.AreEqual("Tutorial complete.", TutorialMarkerRules.StepLabel(6, 5, "Tutorial complete.", "R"), "done has no prefix");
        }
    }
}
