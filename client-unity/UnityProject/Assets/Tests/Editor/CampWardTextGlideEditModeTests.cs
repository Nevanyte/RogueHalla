using System.Collections.Generic;
using NUnit.Framework;
using Roguehalla.Net.Ui;
using TMPro;
using UnityEngine;

namespace Roguehalla.Client.Tests
{
    /// <summary>
    /// P-UI-CAMP-LABEL-STABLE-1-fix: the ward text of a camp that also has a reward badge rides a gap above the badge's plaque, and it
    /// glides there (rises, holds, falls through <see cref="LabelLift"/>) when the badge comes and goes at the vision edge, instead of
    /// hopping by the plaque's height. Frames are driven through the four-argument <see cref="CampRespawnLabels.Draw"/> at 1/60 s.
    /// Presentation only: the numbers are the packet's.
    /// </summary>
    public sealed class CampWardTextGlideEditModeTests
    {
        const float Dt = 1f / 60f, CampX = 640f, CampY = 640f, Gap = .08f;
        const int CampIndex = 3;
        const string WardLabel = "Camp ward label";
        GameObject _arena; UiTheme _savedTheme;

        [SetUp]
        public void SetUp()
        {
            _savedTheme = UiTheme.Current; UiTheme.Current = UiTheme.Placeholder();
            ShowcaseSprites.Initialize();
            WorldLabelVisibility.Hidden = false;
            _arena = new GameObject("camp ward glide arena");
        }

        [TearDown]
        public void TearDown()
        {
            if (_arena != null) UnityEngine.Object.DestroyImmediate(_arena);
            UiTheme.Current = _savedTheme;
            WorldLabelVisibility.Hidden = false;
        }

        /// <summary>One <see cref="CampRespawnLabels"/> with a warded camp, an optional badge row, and no fighters near it.</summary>
        sealed class Rig
        {
            public readonly GameObject Root; public readonly CampRespawnLabels Labels;
            public readonly List<CampWorldLabel> Rows = new List<CampWorldLabel>();
            public readonly List<CampWardRow> Wards = new List<CampWardRow>();
            public readonly List<CampBadgeRow> Badges = new List<CampBadgeRow>();
            public bool Reduced;

            public Rig(GameObject parent)
            {
                Root = new GameObject("camp labels"); Root.transform.SetParent(parent.transform, false);
                Labels = new CampRespawnLabels(Root.transform);
                Labels.ReducedMotionSource = () => Reduced;
                Wards.Add(new CampWardRow { Index = CampIndex, WorldX = CampX, WorldY = CampY, BeaconRgb = 0x66ccff, TextRgb = 0x66ccff, WorldText = "YOUR WARD 107s", Countdown = "107s" });
            }

            public void Frame() { Labels.Draw(Rows, Wards, Badges, Dt); }
            public void Frames(int n) { for (int i = 0; i < n; i++) Frame(); }
            public void BadgeOn() { Badges.Clear(); Badges.Add(new CampBadgeRow { Index = CampIndex, WorldX = CampX, WorldY = CampY, IconKey = "ad", IconRgb = 0xff8a3d }); }
            public void BadgeOff() { Badges.Clear(); }

            /// <summary>The ward text's y (its transform; the lift is added to it).</summary>
            public float TextY() { return Active<TextMeshPro>(WardLabel).transform.position.y; }
            public Rect Plate() { var l = Active<TextMeshPro>(WardLabel); return XY(l.GetComponentInChildren<SpriteRenderer>(false).bounds); }
            public SpriteRenderer Plaque() { return Active<SpriteRenderer>("Camp reward plaque"); }

            T Active<T>(string name) where T : Component
            {
                foreach (var c in Root.GetComponentsInChildren<T>(true)) if (c.name == name && c.gameObject.activeSelf) return c;
                return null;
            }
        }

        Rig NewRig() { return new Rig(_arena); }

        static Rect XY(Bounds b) { return new Rect(b.min.x, b.min.y, b.size.x, b.size.y); }

        /// <summary>The plaque's drawn rect from its position and its sliced size (never <c>bounds</c>, which lags a changed size in EditMode).</summary>
        static Rect PlaqueRect(SpriteRenderer p) { var at = p.transform.position; return new Rect(at.x - p.size.x * .5f, at.y - p.size.y * .5f, p.size.x, p.size.y); }

        /// <summary>How far the ward plate's lower edge is above the drawn plaque's upper edge.</summary>
        static float GapAbove(Rig rig) { return rig.Plate().yMin - PlaqueRect(rig.Plaque()).yMax; }

        /// <summary>Where the ward text rests with no badge and nothing near it.</summary>
        float RestY()
        {
            var rig = NewRig(); rig.Frames(3);
            return rig.TextY();
        }

        [Test]
        public void TheWardTextGlidesUpWhenItsCampsBadgeAppears()
        {
            float rest = RestY();
            var rig = NewRig(); rig.Frames(60);
            Assert.That(rig.TextY(), Is.EqualTo(rest).Within(1e-4f), "set-up: the ward text rests at home with no badge");
            rig.BadgeOn();
            float prev = rest, maxStep = 0f;
            for (int f = 0; f < 60; f++)
            {
                rig.Frame();
                Assert.That(rig.Plaque(), Is.Not.Null, "plaque drawn, frame " + f);
                float y = rig.TextY();
                maxStep = Mathf.Max(maxStep, Mathf.Abs(y - prev));
                Assert.That(Mathf.Abs(y - prev), Is.LessThanOrEqualTo(LabelLift.RiseUnitsPerSecond * Dt + .001f), "the ward text hopped " + (y - prev) + " at frame " + f + " after the badge appeared");
                prev = y;
                if (f >= 19) Assert.That(GapAbove(rig), Is.GreaterThanOrEqualTo(Gap - .001f), "the ward text is a gap above the plaque by frame " + f);
            }
            Assert.That(prev - rest, Is.GreaterThan(.3f), "set-up: the text really rose to clear the plaque");
            Assert.That(maxStep, Is.GreaterThan(.05f), "and it moved, not jumped nowhere");
        }

        [Test]
        public void TheWardTextHoldsWhileTheBadgeFadesAndThenGlidesDown()
        {
            float rest = RestY();
            var rig = NewRig(); rig.BadgeOn(); rig.Frames(30);
            float high = rig.TextY();
            Assert.That(high - rest, Is.GreaterThan(.3f), "set-up: the text sits above the plaque");
            rig.BadgeOff();
            float prev = high; int plaqueGoneFrames = 0, fell = 0;
            for (int f = 0; f < 150; f++)
            {
                rig.Frame();
                float y = rig.TextY();
                if (rig.Plaque() != null) Assert.That(y, Is.EqualTo(high).Within(1e-4f), "the text moved while the plaque is still drawn, frame " + f);
                else plaqueGoneFrames++;
                Assert.That(prev - y, Is.LessThanOrEqualTo(LabelLift.FallUnitsPerSecond * Dt + .001f), "the text dropped " + (prev - y) + " in one frame at frame " + f + " (plaque gone for " + plaqueGoneFrames + " frames)");
                Assert.That(y, Is.LessThanOrEqualTo(prev + 1e-4f), "never rises again, frame " + f);
                if (y < prev - 1e-5f) fell++;
                prev = y;
            }
            Assert.That(plaqueGoneFrames, Is.GreaterThan(0), "set-up: the plaque left");
            Assert.That(fell, Is.GreaterThan(3), "the text comes down over several frames");
            Assert.That(prev, Is.EqualTo(rest).Within(1e-3f), "and rests at its height without a plaque");
        }

        [Test]
        public void ABadgeThatFlickersLeavesTheWardTextStill()
        {
            var rig = NewRig(); rig.BadgeOn(); rig.Frames(60);
            float settled = rig.TextY();
            for (int f = 0; f < 60; f++)
            {
                if (f % 2 == 0) rig.BadgeOn(); else rig.BadgeOff();
                rig.Frame();
                Assert.That(rig.TextY(), Is.EqualTo(settled).Within(1e-4f), "frame " + f + ": the ward text follows the badge flicker");
            }
        }

        [Test]
        public void ReducedMotionJumpsButStillHolds()
        {
            float rest = RestY();
            var rig = NewRig(); rig.Reduced = true; rig.Frames(5);
            rig.BadgeOn();
            rig.Frame();
            Assert.That(rig.Plaque(), Is.Not.Null, "plaque drawn on the appear frame");
            Assert.That(GapAbove(rig), Is.GreaterThanOrEqualTo(Gap - .001f), "reduced motion: the text is at the gap on the frame the badge appears");
            rig.Frames(5);
            float high = rig.TextY();
            Assert.That(high - rest, Is.GreaterThan(.3f), "set-up: lifted by about the plaque's height");
            rig.BadgeOff();
            int heldFrames = Mathf.RoundToInt(.40f / Dt), goneFor = 0; bool dropped = false; float before = high, biggest = 0f;
            for (int f = 0; f < 120; f++)
            {
                rig.Frame();
                bool plaque = rig.Plaque() != null;
                if (!plaque) goneFor++;
                float y = rig.TextY();
                if (plaque) Assert.That(y, Is.EqualTo(high).Within(1e-4f), "reduced motion: the text moved while the plaque is drawn, frame " + f);
                if (!dropped && y < high - 1e-4f)
                {
                    dropped = true;
                    Assert.That(goneFor, Is.GreaterThanOrEqualTo(heldFrames), "reduced motion still holds: the text dropped only " + goneFor + " frames after the plaque left");
                }
                biggest = Mathf.Max(biggest, before - y); before = y;
            }
            Assert.That(dropped, Is.True, "the text comes down after the hold");
            Assert.That(before, Is.EqualTo(rest).Within(1e-3f), "back at its height without a plaque");
            Assert.That(biggest, Is.GreaterThanOrEqualTo(high - rest - .001f), "and it came down in one frame");
        }

        /// <summary>Green on the base: it guards the first-frame snap the fix must keep.</summary>
        [Test]
        public void AFreshWardTextOverAPlaqueIsAlreadyClear()
        {
            var rig = NewRig(); rig.BadgeOn();
            rig.Frame();
            Assert.That(rig.Plaque(), Is.Not.Null, "plaque drawn on frame 0");
            Assert.That(GapAbove(rig), Is.GreaterThanOrEqualTo(Gap - .001f), "frame 0: the ward text is a gap above the plaque at once");
        }
    }
}
