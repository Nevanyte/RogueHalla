using System;
using NUnit.Framework;
using UnityEngine;

namespace Roguehalla.Client.Tests
{
    /// <summary>P-UI-MOTION-1 oracle: the HUD's shared motion (a panel fade through a CanvasGroup, press pulses about the centre).</summary>
    public sealed class UiMotionEditModeTests
    {
        const float Dt = 1f / 60f;
        GameObject _parent;
        RectTransform _rect;
        bool? _savedOverride;

        [SetUp]
        public void SetUp()
        {
            _savedOverride = UiMotion.ReducedOverride;
            UiMotion.ReducedOverride = false;
            _parent = new GameObject("ui motion parent", typeof(RectTransform));
            ((RectTransform)_parent.transform).sizeDelta = new Vector2(800f, 600f);
            _rect = NewRect("ui motion child", new Vector2(.5f, .5f), new Vector2(30f, 20f));
        }

        [TearDown]
        public void TearDown()
        {
            UiMotion.ReducedOverride = _savedOverride;
            if (_parent != null) UnityEngine.Object.DestroyImmediate(_parent);
        }

        RectTransform NewRect(string name, Vector2 pivot, Vector2 position)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rect = (RectTransform)go.transform;
            rect.SetParent(_parent.transform, false);
            rect.anchorMin = rect.anchorMax = new Vector2(.5f, .5f);   // a point anchor, as HudUi.Place makes
            rect.sizeDelta = new Vector2(100f, 60f);
            rect.pivot = pivot;
            rect.anchoredPosition = position;
            return rect;
        }

        static Vector3 Centre(RectTransform rect) { return rect.TransformPoint(rect.rect.center); }

        static CanvasGroup Group(RectTransform rect)
        {
            var g = rect.GetComponent<CanvasGroup>();
            Assert.That(g, Is.Not.Null, "HudFade adds a CanvasGroup to its root");
            return g;
        }

        // ---- fades ----

        [Test]
        public void AFadeInRisesToOneAndIsActiveFromTheFirstFrame()
        {
            var fade = new HudFade(_rect, true);
            fade.Snap(false);
            Assert.That(fade.Active, Is.False, "a hidden panel is deactivated");
            Assert.That(fade.Alpha, Is.EqualTo(0f));
            fade.Set(true);
            Assert.That(fade.Shown, Is.True);
            Assert.That(fade.Active, Is.True, "activated at once, before any tick");
            float last = fade.Alpha;
            int frames = Mathf.CeilToInt(HudFade.InSeconds / Dt) + 1;
            for (int i = 0; i < frames; i++)
            {
                fade.Tick(Dt);
                if (last < 1f) Assert.That(fade.Alpha, Is.GreaterThan(last), "strictly rising on frame " + i);
                Assert.That(fade.Alpha, Is.LessThanOrEqualTo(1f));
                Assert.That(Group(_rect).alpha, Is.EqualTo(fade.Alpha), "the CanvasGroup shows Alpha");
                last = fade.Alpha;
            }
            Assert.That(fade.Alpha, Is.EqualTo(1f), "full after InSeconds plus one frame");
            Assert.That(Group(_rect).blocksRaycasts && Group(_rect).interactable, Is.True, "an interactive panel takes the pointer once shown");
        }

        [Test]
        public void AFadeOutStopsTakingThePointerAtOnceAndDeactivatesAtZero()
        {
            var fade = new HudFade(_rect, true);
            fade.Snap(true);
            Assert.That(fade.Alpha, Is.EqualTo(1f));
            Assert.That(fade.Active, Is.True);
            Assert.That(Group(_rect).blocksRaycasts, Is.True);
            fade.Set(false);
            Assert.That(fade.Shown, Is.False);
            Assert.That(Group(_rect).blocksRaycasts, Is.False, "no pointer on a panel that is leaving, before any tick");
            Assert.That(Group(_rect).interactable, Is.False);
            Assert.That(fade.Active, Is.True, "still drawn while it fades");
            float last = fade.Alpha;
            int frames = 0;
            while (fade.Alpha > 0f && frames < 100)
            {
                fade.Tick(Dt); frames++;
                Assert.That(fade.Alpha, Is.LessThan(last), "strictly falling on frame " + frames);
                Assert.That(Group(_rect).blocksRaycasts, Is.False);
                if (fade.Alpha > 0f) Assert.That(fade.Active, Is.True, "active while any alpha is left");
                last = fade.Alpha;
            }
            Assert.That(frames, Is.InRange(Mathf.RoundToInt(HudFade.OutSeconds / Dt) - 1, Mathf.RoundToInt(HudFade.OutSeconds / Dt) + 1));
            Assert.That(fade.Alpha, Is.EqualTo(0f));
            Assert.That(fade.Active, Is.False, "deactivated when alpha reaches 0");
        }

        [Test]
        public void ANonInteractiveFadeNeverTakesThePointer()
        {
            var fade = new HudFade(_rect, false);
            fade.Snap(false);
            fade.Set(true);
            Assert.That(fade.Active, Is.True);
            for (int i = 0; i < 20; i++)
            {
                fade.Tick(Dt);
                Assert.That(Group(_rect).blocksRaycasts, Is.False, "fade in, frame " + i);
                Assert.That(Group(_rect).interactable, Is.False, "fade in, frame " + i);
            }
            Assert.That(fade.Alpha, Is.EqualTo(1f));
            fade.Set(false);
            for (int i = 0; i < 30; i++)
            {
                fade.Tick(Dt);
                Assert.That(Group(_rect).blocksRaycasts, Is.False, "fade out, frame " + i);
            }
            Assert.That(fade.Alpha, Is.EqualTo(0f));
            Assert.That(fade.Active, Is.False);
        }

        [Test]
        public void ReversingAFadeContinuesFromTheCurrentAlpha()
        {
            var fade = new HudFade(_rect, true);
            fade.Snap(false);
            fade.Set(true);
            for (int i = 0; i < 4; i++) fade.Tick(Dt);
            float mid = fade.Alpha;
            Assert.That(mid, Is.GreaterThan(0f).And.LessThan(1f), "half way in");
            fade.Set(false);
            Assert.That(fade.Alpha, Is.EqualTo(mid), "no jump when the direction changes");
            float oneStep = Mathf.Max(Dt / HudFade.InSeconds, Dt / HudFade.OutSeconds) + 1e-3f;
            fade.Tick(Dt);
            Assert.That(fade.Alpha, Is.LessThan(mid));
            Assert.That(mid - fade.Alpha, Is.LessThanOrEqualTo(oneStep), "at most one step of the fade out");
            float low = fade.Alpha;
            fade.Set(true);
            Assert.That(fade.Alpha, Is.EqualTo(low), "no jump when it turns back");
            Assert.That(fade.Active, Is.True);
            fade.Tick(Dt);
            Assert.That(fade.Alpha, Is.GreaterThan(low));
            Assert.That(fade.Alpha - low, Is.LessThanOrEqualTo(oneStep));
        }

        [Test]
        public void SettingTheWantedStateAgainDoesNothing()
        {
            var fade = new HudFade(_rect, true);
            fade.Snap(true);
            Assert.That(fade.Alpha, Is.EqualTo(1f));
            fade.Set(true);
            fade.Tick(Dt);
            Assert.That(fade.Alpha, Is.EqualTo(1f));
            Assert.That(fade.Shown, Is.True);
            Assert.That(fade.Active, Is.True);
            fade.Snap(false);
            fade.Set(false);
            Assert.That(fade.Alpha, Is.EqualTo(0f));
            Assert.That(fade.Active, Is.False, "Set(false) on a hidden panel does not wake it");
        }

        [Test]
        public void ReducedMotionSnaps()
        {
            UiMotion.ReducedOverride = true;
            var fade = new HudFade(_rect, true);
            fade.Snap(false);
            Assert.That(fade.Alpha, Is.EqualTo(0f));
            fade.Set(true);
            Assert.That(fade.Alpha, Is.EqualTo(1f), "in at once");
            Assert.That(fade.Active, Is.True);
            for (int i = 0; i < 5; i++)
            {
                fade.Tick(Dt);
                Assert.That(fade.Alpha == 0f || fade.Alpha == 1f, Is.True, "only 0 or 1");
            }
            fade.Set(false);
            Assert.That(fade.Alpha, Is.EqualTo(0f), "out at once");
            Assert.That(fade.Active, Is.False);
            Assert.That(Group(_rect).blocksRaycasts, Is.False);
            fade.Tick(Dt);
            Assert.That(fade.Alpha, Is.EqualTo(0f));
        }

        [Test]
        public void AFadeUsesAnExistingCanvasGroupAndAddsOnlyOne()
        {
            var own = _rect.gameObject.AddComponent<CanvasGroup>();
            own.alpha = 1f;
            var fade = new HudFade(_rect, true);
            fade.Snap(false);
            Assert.That(own.alpha, Is.EqualTo(0f), "the group that was already there is the one driven");
            Assert.That(_rect.GetComponents<CanvasGroup>().Length, Is.EqualTo(1));
        }

        // ---- pulses ----

        [Test]
        public void APulseScalesAboutTheCentreWhateverThePivot()
        {
            foreach (var pivot in new[] { new Vector2(0f, 0f), new Vector2(1f, 1f), new Vector2(.5f, 0f) })
            {
                var rect = NewRect("pivot " + pivot, pivot, new Vector2(30f, 20f));
                var original = rect.anchoredPosition;
                var centre = Centre(rect);
                var pulses = new HudPulses();
                pulses.Play(rect, 10f);
                Assert.That(pulses.Count, Is.EqualTo(1), "pivot " + pivot);
                int sawScale = 0;
                for (int i = 1; i < 15; i++)
                {
                    float t = i / 15f;
                    pulses.Tick(10f + t * HudPulses.Seconds);
                    Assert.That(Vector3.Distance(Centre(rect), centre), Is.LessThan(.01f), "centre stays, pivot " + pivot + " t " + t);
                    float want = 1f + HudPulses.Pop * Mathf.Sin(Mathf.PI * t);
                    Assert.That(rect.localScale.x, Is.EqualTo(want).Within(1e-3f), "scale at t " + t + ", pivot " + pivot);
                    Assert.That(rect.localScale.y, Is.EqualTo(want).Within(1e-3f));
                    if (rect.localScale.x > 1f) sawScale++;
                }
                Assert.That(sawScale, Is.GreaterThan(10), "it grows during the pulse");
                pulses.Tick(10f + HudPulses.Seconds * .5f);
                Assert.That(rect.localScale.x, Is.GreaterThan(1f), "larger than 1 at half time, pivot " + pivot);
                Assert.That(Vector3.Distance(Centre(rect), centre), Is.LessThan(.01f));
                pulses.Tick(10f + HudPulses.Seconds + .01f);
                Assert.That(rect.localScale, Is.EqualTo(Vector3.one), "scale exactly 1 at the end, pivot " + pivot);
                Assert.That(rect.anchoredPosition.x, Is.EqualTo(original.x), "position exactly as before, pivot " + pivot);
                Assert.That(rect.anchoredPosition.y, Is.EqualTo(original.y));
                Assert.That(pulses.Count, Is.EqualTo(0), "a finished pulse is dropped");
            }
        }

        [Test]
        public void ARestartedPulseRestartsTheClockAndKeepsTheFirstOriginal()
        {
            var rect = NewRect("restart", new Vector2(0f, 1f), new Vector2(-40f, 15f));
            var original = rect.anchoredPosition;
            var pulses = new HudPulses();
            pulses.Play(rect, 0f);
            pulses.Tick(.05f);
            Assert.That(rect.localScale.x, Is.GreaterThan(1f), "pulsing");
            pulses.Play(rect, .05f);
            Assert.That(pulses.Count, Is.EqualTo(1), "one pulse per rect");
            pulses.Tick(.05f + HudPulses.Seconds * .5f);
            Assert.That(rect.localScale.x, Is.EqualTo(1f + HudPulses.Pop).Within(1e-3f), "the new pulse is at its peak half a pulse later");
            pulses.Tick(.05f + HudPulses.Seconds + .01f);
            Assert.That(rect.localScale, Is.EqualTo(Vector3.one));
            Assert.That(rect.anchoredPosition.x, Is.EqualTo(original.x));
            Assert.That(rect.anchoredPosition.y, Is.EqualTo(original.y), "shifts never accumulate");
        }

        [Test]
        public void ReducedMotionPulseDoesNotScale()
        {
            var pulses = new HudPulses();
            var original = _rect.anchoredPosition;
            UiMotion.ReducedOverride = true;
            pulses.Play(_rect, 0f);
            pulses.Tick(HudPulses.Seconds * .5f);
            Assert.That(_rect.localScale, Is.EqualTo(Vector3.one), "reduced: no pop");
            Assert.That(_rect.anchoredPosition.x, Is.EqualTo(original.x));
            Assert.That(_rect.anchoredPosition.y, Is.EqualTo(original.y));
            pulses.Tick(HudPulses.Seconds);
            Assert.That(_rect.localScale, Is.EqualTo(Vector3.one));
            // the same helper pops once motion is back, so the quiet above was the setting and not a dead pulse
            UiMotion.ReducedOverride = false;
            pulses.Play(_rect, 1f);
            pulses.Tick(1f + HudPulses.Seconds * .5f);
            Assert.That(_rect.localScale.x, Is.GreaterThan(1f));
            pulses.Tick(1f + HudPulses.Seconds + .01f);
            Assert.That(_rect.localScale, Is.EqualTo(Vector3.one));
        }

        [Test]
        public void ClearRestoresEveryRect()
        {
            var a = NewRect("a", new Vector2(0f, 0f), new Vector2(10f, 10f));
            var b = NewRect("b", new Vector2(1f, .5f), new Vector2(-25f, 5f));
            var pa = a.anchoredPosition; var pb = b.anchoredPosition;
            var pulses = new HudPulses();
            pulses.Play(a, 0f); pulses.Play(b, 0f);
            Assert.That(pulses.Count, Is.EqualTo(2));
            pulses.Tick(HudPulses.Seconds * .5f);
            Assert.That(a.localScale.x, Is.GreaterThan(1f), "both are mid pulse");
            Assert.That(b.localScale.x, Is.GreaterThan(1f));
            pulses.Clear();
            Assert.That(pulses.Count, Is.EqualTo(0));
            Assert.That(a.localScale, Is.EqualTo(Vector3.one)); Assert.That(b.localScale, Is.EqualTo(Vector3.one));
            Assert.That(a.anchoredPosition.x, Is.EqualTo(pa.x)); Assert.That(a.anchoredPosition.y, Is.EqualTo(pa.y));
            Assert.That(b.anchoredPosition.x, Is.EqualTo(pb.x)); Assert.That(b.anchoredPosition.y, Is.EqualTo(pb.y));
            pulses.Tick(HudPulses.Seconds * .75f);
            Assert.That(a.localScale, Is.EqualTo(Vector3.one), "nothing left to move");
        }

        [Test]
        public void ADestroyedRectIsDroppedAndNullIsIgnored()
        {
            var gone = NewRect("gone", new Vector2(.5f, .5f), Vector2.zero);
            var pulses = new HudPulses();
            pulses.Play(null, 0f);
            Assert.That(pulses.Count, Is.EqualTo(0), "null is ignored");
            pulses.Play(gone, 0f);
            pulses.Play(_rect, 0f);
            Assert.That(pulses.Count, Is.EqualTo(2));
            UnityEngine.Object.DestroyImmediate(gone.gameObject);
            Assert.DoesNotThrow(() => pulses.Tick(HudPulses.Seconds * .5f));
            Assert.That(pulses.Count, Is.EqualTo(1), "the destroyed rect is dropped");
            Assert.That(_rect.localScale.x, Is.GreaterThan(1f), "the live one keeps pulsing");
            Assert.DoesNotThrow(() => pulses.Clear());
            Assert.That(pulses.Count, Is.EqualTo(0));
        }

        // ---- allocation ----

        [Test]
        public void AnIdleTickAllocatesNothing()
        {
            var fade = new HudFade(_rect, true);
            fade.Snap(true);
            Assert.That(fade.Alpha, Is.EqualTo(1f), "settled and shown");
            var hidden = new HudFade(NewRect("hidden", new Vector2(.5f, .5f), Vector2.zero), true);
            hidden.Snap(false);
            Assert.That(hidden.Active, Is.False, "settled and hidden");
            var pulses = new HudPulses();
            Assert.That(pulses.Count, Is.EqualTo(0));
            for (int i = 0; i < 5; i++) { fade.Tick(Dt); hidden.Tick(Dt); pulses.Tick(i * Dt); }
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 200; i++) { fade.Tick(Dt); hidden.Tick(Dt); pulses.Tick(1f + i * Dt); }
            long after = GC.GetAllocatedBytesForCurrentThread();
            Assert.That(after - before, Is.LessThanOrEqualTo(0), "idle ticks allocate nothing");
            Assert.That(fade.Alpha, Is.EqualTo(1f));
            Assert.That(hidden.Active, Is.False, "an idle tick does not wake a hidden panel");
        }
    }
}
