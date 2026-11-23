using System;
using System.Collections.Generic;
using NUnit.Framework;
using Roguehalla.Net.Ui;
using TMPro;
using UnityEngine;

namespace Roguehalla.Client.Tests
{
    /// <summary>
    /// P-UI-CAMP-LABEL-STABLE-1: the labels above and below a camp (reward badge, "RESPAWN Ns", ward text) glide clear of fighters instead of hopping
    /// as a fighter walks under them, the badge lingers and fades at the vision edge, is not drawn while the Tab map is open, and the ward text of a
    /// camp that also has a badge rides above the plaque. Frames are driven through the four-argument <see cref="CampRespawnLabels.Draw"/> at
    /// 1/60 s; obstacle rects are set by the test (the one-frame-old rects of the real wiring). Presentation only: the numbers are the packet's.
    /// </summary>
    public sealed class CampLabelStableEditModeTests
    {
        const float Dt = 1f / 60f, CampX = 640f, CampY = 640f;
        GameObject _arena; UiTheme _savedTheme;

        [SetUp]
        public void SetUp()
        {
            _savedTheme = UiTheme.Current; UiTheme.Current = UiTheme.Placeholder();
            ShowcaseSprites.Initialize();
            WorldLabelVisibility.Hidden = false;
            _arena = new GameObject("camp label stable arena");
        }

        [TearDown]
        public void TearDown()
        {
            if (_arena != null) UnityEngine.Object.DestroyImmediate(_arena);
            UiTheme.Current = _savedTheme;
            WorldLabelVisibility.Hidden = false;
        }

        /// <summary>One <see cref="CampRespawnLabels"/> with ready rows and a settable obstacle list; allocates nothing per frame.</summary>
        sealed class Rig
        {
            public readonly GameObject Root; public readonly CampRespawnLabels Labels;
            public readonly List<CampWorldLabel> Rows = new List<CampWorldLabel>();
            public readonly List<CampWardRow> Wards = new List<CampWardRow>();
            public readonly List<CampBadgeRow> Badges = new List<CampBadgeRow>();
            public Rect[] Obstacles = new Rect[0];
            public bool Reduced;

            public Rig(GameObject parent)
            {
                Root = new GameObject("camp labels"); Root.transform.SetParent(parent.transform, false);
                Labels = new CampRespawnLabels(Root.transform);
                Labels.NameRectSource = AddObstacles;
                Labels.ReducedMotionSource = () => Reduced;
            }

            void AddObstacles(List<Rect> into) { into.AddRange(Obstacles); }

            public void Frame() { Labels.Draw(Rows, Wards, Badges, Dt); }
            public void Frames(int n) { for (int i = 0; i < n; i++) Frame(); }

            public SpriteRenderer Plaque() { return Active<SpriteRenderer>("Camp reward plaque"); }
            public SpriteRenderer WardPlate() { var l = Active<TextMeshPro>("Camp ward label"); return l == null ? null : l.GetComponentInChildren<SpriteRenderer>(false); }
            public SpriteRenderer RespawnPlate() { var l = Active<TextMeshPro>("Camp respawn label"); return l == null ? null : l.GetComponentInChildren<SpriteRenderer>(false); }
            public TextMeshPro Label(string name) { return Active<TextMeshPro>(name); }

            public List<SpriteRenderer> Icons()
            {
                var list = new List<SpriteRenderer>();
                foreach (var r in Root.GetComponentsInChildren<SpriteRenderer>(true)) if (r.name == "Camp reward badge" && r.gameObject.activeSelf) list.Add(r);
                return list;
            }

            public int ActivePlaques()
            {
                int n = 0;
                foreach (var r in Root.GetComponentsInChildren<SpriteRenderer>(true)) if (r.name == "Camp reward plaque" && r.gameObject.activeSelf) n++;
                return n;
            }

            T Active<T>(string name) where T : Component
            {
                foreach (var c in Root.GetComponentsInChildren<T>(true)) if (c.name == name && c.gameObject.activeSelf) return c;
                return null;
            }
        }

        Rig NewRig() { return new Rig(_arena); }

        static CampBadgeRow Badge(int index, double x = CampX, double y = CampY)
        {
            return new CampBadgeRow { Index = index, WorldX = x, WorldY = y, IconKey = "ad", IconRgb = 0xff8a3d };
        }

        static Rect XY(Bounds b) { return new Rect(b.min.x, b.min.y, b.size.x, b.size.y); }

        /// <summary>The plaque's drawn rect from its position and its sliced size: what is drawn, not the renderer's bounds (which lag a changed size in EditMode).</summary>
        static Rect PlaqueRect(SpriteRenderer p) { var at = p.transform.position; return new Rect(at.x - p.size.x * .5f, at.y - p.size.y * .5f, p.size.x, p.size.y); }

        static float Alpha(SpriteRenderer r) { return r == null || !r.gameObject.activeSelf ? 0f : r.color.a; }

        /// <summary>An obstacle 1.2 wide and .5 tall centred at x whose top is at yMax.</summary>
        static Rect Obstacle(float centreX, float yMax) { return new Rect(centreX - .6f, yMax - .5f, 1.2f, .5f); }

        /// <summary>Where the plaque of the standard badge sits with nothing near it.</summary>
        Rect BaseBadge()
        {
            // Two frames: a pooled plaque is drawn at its sprite's native size in its very first frame (the sprite is assigned after the size).
            var rig = NewRig(); rig.Badges.Add(Badge(1)); rig.Frames(2);
            Assert.That(rig.Plaque(), Is.Not.Null, "set-up: the badge is drawn");
            return PlaqueRect(rig.Plaque());
        }

        /// <summary>Where the plate of the respawn label (kind 0) or the ward label (kind 1) sits with nothing near it.</summary>
        Rect BasePlate(bool ward)
        {
            var rig = NewRig(); AddText(rig, ward); rig.Frame();
            var plate = ward ? rig.WardPlate() : rig.RespawnPlate();
            Assert.That(plate, Is.Not.Null, "set-up: the label is drawn");
            return XY(plate.bounds);
        }

        static void AddText(Rig rig, bool ward)
        {
            if (ward) rig.Wards.Add(new CampWardRow { Index = 2, WorldX = CampX, WorldY = CampY, BeaconRgb = 0x66ccff, TextRgb = 0x66ccff, WorldText = "YOUR WARD 107s", Countdown = "107s" });
            else rig.Rows.Add(new CampWorldLabel { Index = 1, WorldX = CampX, WorldY = CampY, Text = "RESPAWN 167s" });
        }

        // ---- 1, 2, 3, 4, 5: the badge's lift ----

        [Test]
        public void ABadgeGlidesWhenAFighterWalksUnderIt()
        {
            var home = BaseBadge();
            var rig = NewRig(); rig.Badges.Add(Badge(1));
            rig.Frame();
            var plaque = rig.Plaque(); var icons = rig.Icons();
            Assert.That(icons.Count, Is.EqualTo(1), "set-up: one icon");
            float iconOffset = icons[0].transform.position.y - plaque.transform.position.y;
            float top = home.yMin + .4f, from = home.center.x - 3f, to = home.center.x + 3f;
            float prev = plaque.transform.position.y, maxStep = 0f, clearedBy = float.NegativeInfinity;
            for (int f = 1; f <= 120; f++)
            {
                var obstacle = Obstacle(Mathf.Lerp(from, to, f / 120f), top);
                rig.Obstacles = new[] { obstacle };
                rig.Frame();
                plaque = rig.Plaque(); icons = rig.Icons();
                Assert.That(plaque, Is.Not.Null, "plaque drawn, frame " + f); Assert.That(icons.Count, Is.EqualTo(1), "icon drawn, frame " + f);
                float y = plaque.transform.position.y;
                maxStep = Mathf.Max(maxStep, Mathf.Abs(y - prev));
                Assert.That(Mathf.Abs(y - prev), Is.LessThanOrEqualTo(LabelLift.RiseUnitsPerSecond * Dt + .001f), "the plaque hopped at frame " + f);
                Assert.That(icons[0].transform.position.y - y, Is.EqualTo(iconOffset).Within(1e-4f), "the icon keeps its offset from the plaque, frame " + f);
                prev = y;
                if (f == 60) clearedBy = PlaqueRect(plaque).yMin - obstacle.yMax;   // the fighter is centred under the plaque
            }
            Assert.That(clearedBy, Is.GreaterThanOrEqualTo(.06f - .001f), "with the fighter right under it the plaque clears its top by the clearance");
            Assert.That(maxStep, Is.GreaterThan(.05f), "set-up: the plaque really did move as the fighter passed");
        }

        [Test]
        public void AnObstacleThatFlickersLeavesTheBadgeStill()
        {
            var home = BaseBadge();
            var rig = NewRig(); rig.Badges.Add(Badge(1));
            var present = new[] { Obstacle(home.center.x, home.yMin + .4f) };
            rig.Obstacles = present;
            rig.Frames(30);
            float settled = rig.Plaque().transform.position.y;
            Assert.That(PlaqueRect(rig.Plaque()).yMin, Is.GreaterThanOrEqualTo(present[0].yMax + .06f - .001f), "set-up: the plaque is lifted over the fighter");
            for (int f = 0; f < 60; f++)
            {
                rig.Obstacles = f % 2 == 0 ? present : new Rect[0];
                rig.Frame();
                Assert.That(rig.Plaque().transform.position.y, Is.EqualTo(settled).Within(1e-4f), "frame " + f + ": the plaque follows the flicker");
            }
        }

        [Test]
        public void TheBadgeComesDownOnlyAfterTheHold()
        {
            var home = BaseBadge();
            var rig = NewRig(); rig.Badges.Add(Badge(1));
            rig.Obstacles = new[] { Obstacle(home.center.x, home.yMin + .4f) };
            rig.Frames(30);
            float high = rig.Plaque().transform.position.y;
            rig.Obstacles = new Rect[0];
            int heldFrames = Mathf.RoundToInt(.40f / Dt);
            for (int f = 1; f <= heldFrames; f++)
            {
                rig.Frame();
                Assert.That(rig.Plaque().transform.position.y, Is.EqualTo(high).Within(1e-4f), "held, " + f * Dt + " s after the fighter left");
            }
            float prev = high;
            for (int f = heldFrames + 1; f <= 60; f++)
            {
                rig.Frame();
                float y = rig.Plaque().transform.position.y;
                Assert.That(y, Is.LessThanOrEqualTo(prev + 1e-4f), "never rises again, frame " + f);
                Assert.That(prev - y, Is.LessThanOrEqualTo(LabelLift.FallUnitsPerSecond * Dt + .001f), "falls slowly, frame " + f);
                prev = y;
            }
            Assert.That(PlaqueRect(rig.Plaque()).yMin, Is.EqualTo(home.yMin).Within(1e-3f), "one second after the fighter left the plaque is back at its base");
        }

        [Test]
        public void ABadgeDrawnForTheFirstTimeOverAHeadIsAlreadyClear()
        {
            var home = BaseBadge();
            var obstacle = Obstacle(home.center.x, home.yMin + .4f);
            // A single fresh frame: clear at once and fully drawn.
            var one = NewRig(); one.Badges.Add(Badge(1)); one.Obstacles = new[] { obstacle };
            one.Frame();
            Assert.That(PlaqueRect(one.Plaque()).yMin, Is.GreaterThanOrEqualTo(obstacle.yMax + .06f - .001f), "a fresh badge never appears on top of a head");
            Assert.That(Alpha(one.Plaque()), Is.EqualTo(1f).Within(1e-4f), "and a fresh frame is fully drawn");
            // A badge that enters while the labels are running: no lift state yet, so it is clear in its very first frame, and it fades in.
            var late = NewRig(); late.Obstacles = new[] { obstacle };
            late.Frames(5);
            late.Badges.Add(Badge(1));
            late.Frame();
            var plaque = late.Plaque();
            Assert.That(plaque, Is.Not.Null, "the badge is active on the frame its row appears");
            Assert.That(Alpha(plaque), Is.LessThan(1f), "set-up: it fades in rather than popping");
            Assert.That(PlaqueRect(plaque).yMin, Is.GreaterThanOrEqualTo(obstacle.yMax + .06f - .001f), "but its position is clear at once, from the first frame");
        }

        [Test]
        public void SmallChangesInTheObstacleDoNotMoveTheBadge()
        {
            var home = BaseBadge();
            var rig = NewRig(); rig.Badges.Add(Badge(1));
            float top = home.yMin + .4f;
            rig.Obstacles = new[] { Obstacle(home.center.x, top) };
            rig.Frames(30);
            float settled = rig.Plaque().transform.position.y;
            for (int f = 0; f < 60; f++)
            {
                rig.Obstacles = new[] { Obstacle(home.center.x, top + (f % 2 == 0 ? .02f : -.02f)) };   // a sprite's bounds change between animation frames
                rig.Frame();
                Assert.That(rig.Plaque().transform.position.y, Is.EqualTo(settled).Within(1e-4f), "frame " + f);
            }
        }

        // ---- 6: respawn and ward text ----

        [TestCase(false)]
        [TestCase(true)]
        public void RespawnAndWardTextGlideTheSameWay(bool ward)
        {
            var home = BasePlate(ward);
            string which = ward ? "ward" : "respawn";
            var rig = NewRig(); AddText(rig, ward);
            rig.Frame();
            var label = rig.Label(ward ? "Camp ward label" : "Camp respawn label");
            float top = home.yMin + .3f, from = home.center.x - 4f, to = home.center.x + 4f;
            float prev = label.transform.position.y, maxStep = 0f;
            bool clearAtCentre = false;
            for (int f = 1; f <= 120; f++)
            {
                var obstacle = Obstacle(Mathf.Lerp(from, to, f / 120f), top);
                rig.Obstacles = new[] { obstacle };
                rig.Frame();
                label = rig.Label(ward ? "Camp ward label" : "Camp respawn label");
                float y = label.transform.position.y;
                maxStep = Mathf.Max(maxStep, Mathf.Abs(y - prev));
                Assert.That(Mathf.Abs(y - prev), Is.LessThanOrEqualTo(LabelLift.RiseUnitsPerSecond * Dt + .001f), which + " text hopped at frame " + f);
                prev = y;
                if (f == 60) clearAtCentre = !XY((ward ? rig.WardPlate() : rig.RespawnPlate()).bounds).Overlaps(obstacle);
            }
            Assert.That(clearAtCentre, Is.True, which + " text is clear of the fighter standing under it");
            Assert.That(maxStep, Is.GreaterThan(.05f), "set-up: the text really did move as the fighter passed");

            // The flicker case: settled over the fighter, then the fighter is present on even frames only.
            var flick = NewRig(); AddText(flick, ward);
            var present = new[] { Obstacle(home.center.x, top) };
            flick.Obstacles = present; flick.Frames(30);
            float settled = flick.Label(ward ? "Camp ward label" : "Camp respawn label").transform.position.y;
            for (int f = 0; f < 60; f++)
            {
                flick.Obstacles = f % 2 == 0 ? present : new Rect[0];
                flick.Frame();
                Assert.That(flick.Label(ward ? "Camp ward label" : "Camp respawn label").transform.position.y, Is.EqualTo(settled).Within(1e-4f), which + " text follows the flicker, frame " + f);
            }
        }

        // ---- 7: the Tab map ----

        [Test]
        public void BadgesAreNotDrawnWhileTheTabMapIsOpen()
        {
            try
            {
                var home = BaseBadge();
                var rig = NewRig(); rig.Badges.Add(Badge(1));
                rig.Frames(3);
                Assert.That(rig.Labels.BadgesUsed, Is.EqualTo(1), "set-up: drawn while the map is closed");
                WorldLabelVisibility.Hidden = true;
                rig.Frames(2);
                Assert.That(rig.Labels.BadgesUsed, Is.EqualTo(0), "BadgesUsed while the Tab map is open");
                Assert.That(rig.ActivePlaques(), Is.EqualTo(0), "no active plaque");
                Assert.That(rig.Icons().Count, Is.EqualTo(0), "no active icon");

                // The map closes: the labels start fresh, whatever fading or lifting went on before.
                WorldLabelVisibility.Hidden = false;
                var obstacle = Obstacle(home.center.x, home.yMin + .4f);
                rig.Obstacles = new[] { obstacle };
                rig.Frame();
                Assert.That(rig.Plaque(), Is.Not.Null, "drawn again");
                Assert.That(Alpha(rig.Plaque()), Is.EqualTo(1f).Within(1e-4f), "the first draw after the map closes is fully drawn");
                Assert.That(PlaqueRect(rig.Plaque()).yMin, Is.GreaterThanOrEqualTo(obstacle.yMax + .06f - .001f), "and clear of a fighter at once");
            }
            finally { WorldLabelVisibility.Hidden = false; }
        }

        // ---- 8, 9, 10: the vision edge ----

        [Test]
        public void ABadgeThatLeavesTheRowsLingersThenFades()
        {
            var rig = NewRig(); rig.Badges.Add(Badge(1));
            rig.Frames(10);
            Assert.That(Alpha(rig.Plaque()), Is.EqualTo(1f).Within(1e-4f), "set-up: fully drawn");
            var at = rig.Plaque().transform.position;
            rig.Badges.Clear();
            for (int k = 1; k <= 11; k++)   // 0.183 s: inside the 0.2 s linger
            {
                rig.Frame();
                Assert.That(rig.Plaque(), Is.Not.Null, "still drawn " + k * Dt + " s after the row left");
                Assert.That(Alpha(rig.Plaque()), Is.EqualTo(1f).Within(1e-4f), "fully drawn during the linger, frame " + k);
                Assert.That(rig.Icons().Count, Is.EqualTo(1), "its icon too, frame " + k);
                Assert.That(Alpha(rig.Icons()[0]), Is.EqualTo(1f).Within(1e-4f), "icon alpha, frame " + k);
                Assert.That((rig.Plaque().transform.position - at).magnitude, Is.LessThan(1e-4f), "and it stays where it was, frame " + k);
            }
            float prev = 1f; bool faded = false;
            for (int k = 12; k <= 23; k++)   // up to 0.383 s: 0.2 + 0.15 + a frame
            {
                rig.Frame();
                float a = Alpha(rig.Plaque());
                Assert.That(a, Is.LessThanOrEqualTo(prev + 1e-4f), "alpha only decreases, frame " + k);
                if (k == 20) { Assert.That(a, Is.LessThan(1f), "fading at 0.33 s"); faded = true; }
                prev = a;
            }
            Assert.That(faded, Is.True);
            Assert.That(rig.Plaque(), Is.Null, "switched off by 0.2 + 0.15 s plus a frame");
            Assert.That(rig.Icons().Count, Is.EqualTo(0), "icons off too");

            // A row that returns during the linger shows no change at all.
            var back = NewRig(); back.Badges.Add(Badge(1));
            back.Frames(10);
            var where = back.Plaque().transform.position;
            back.Badges.Clear();
            back.Frames(6);
            back.Badges.Add(Badge(1));
            for (int k = 0; k < 12; k++)
            {
                back.Frame();
                Assert.That(Alpha(back.Plaque()), Is.EqualTo(1f).Within(1e-4f), "alpha 1 on every frame of the return, frame " + k);
                Assert.That((back.Plaque().transform.position - where).magnitude, Is.LessThan(1e-4f), "no movement, frame " + k);
            }
        }

        [Test]
        public void ABadgeThatEntersLaterFadesIn()
        {
            var rig = NewRig();
            rig.Frames(5);   // the labels are running with no rows
            rig.Badges.Add(Badge(1));
            float prev = -1f; int full = -1;
            for (int k = 0; k <= 10; k++)
            {
                rig.Frame();
                var plaque = rig.Plaque();
                if (k == 0)
                {
                    Assert.That(plaque, Is.Not.Null, "the plaque is active on the frame its row appears");
                    Assert.That(Alpha(plaque), Is.LessThan(1f), "and it starts below full alpha");
                }
                float a = Alpha(plaque);
                Assert.That(a, Is.GreaterThanOrEqualTo(prev - 1e-4f), "alpha never decreases, frame " + k);
                var icons = rig.Icons();
                Assert.That(icons.Count, Is.EqualTo(1), "icon, frame " + k);
                Assert.That(icons[0].color.a, Is.EqualTo(a).Within(.01f), "the icon fades with the plaque, frame " + k);
                Assert.That(icons[0].color.r, Is.EqualTo(0xff / 255f).Within(.01f), "the reward colour is unchanged, red, frame " + k);
                Assert.That(icons[0].color.g, Is.EqualTo(0x8a / 255f).Within(.01f), "green, frame " + k);
                Assert.That(icons[0].color.b, Is.EqualTo(0x3d / 255f).Within(.01f), "blue, frame " + k);
                if (full < 0 && a >= .999f) full = k;
                prev = a;
            }
            Assert.That(full, Is.GreaterThan(0), "it does not appear fully drawn");
            Assert.That(full, Is.LessThanOrEqualTo(Mathf.CeilToInt(.15f / Dt) + 1), "and is fully drawn within the fade time plus a frame");
        }

        [Test]
        public void ReducedMotionJumpsButStillHolds()
        {
            var home = BaseBadge();
            var rig = NewRig(); rig.Reduced = true; rig.Badges.Add(Badge(1));
            rig.Frame();
            float y0 = rig.Plaque().transform.position.y;
            var obstacle = Obstacle(home.center.x, home.yMin + .4f);
            rig.Obstacles = new[] { obstacle };
            rig.Frame();
            Assert.That(PlaqueRect(rig.Plaque()).yMin, Is.GreaterThanOrEqualTo(obstacle.yMax + .06f - .001f), "a new fighter moves the plaque clear in one frame");
            rig.Frames(5);
            float high = rig.Plaque().transform.position.y;
            Assert.That(high - y0, Is.GreaterThan(.4f), "set-up: lifted by about the fighter's height");
            rig.Obstacles = new Rect[0];
            int heldFrames = Mathf.RoundToInt(.40f / Dt);
            for (int f = 1; f <= heldFrames; f++)
            {
                rig.Frame();
                Assert.That(rig.Plaque().transform.position.y, Is.EqualTo(high).Within(1e-4f), "reduced motion still holds, frame " + f);
            }
            float prev = high, biggest = 0f;
            for (int f = heldFrames + 1; f <= 40; f++)
            {
                rig.Frame();
                float y = rig.Plaque().transform.position.y;
                biggest = Mathf.Max(biggest, prev - y); prev = y;
            }
            Assert.That(prev, Is.EqualTo(y0).Within(1e-3f), "back at the base after the hold");
            Assert.That(biggest, Is.GreaterThanOrEqualTo(high - y0 - .001f), "and it came down in one frame");

            // No fade: alpha is 0 or 1 on every frame; the linger stays.
            rig.Badges.Clear();
            for (int k = 1; k <= 30; k++)
            {
                rig.Frame();
                float a = Alpha(rig.Plaque());
                Assert.That(a == 0f || a == 1f, Is.True, "alpha " + a + " while the row is gone, frame " + k);
                if (k <= 11) Assert.That(a, Is.EqualTo(1f), "the linger stays under reduced motion, frame " + k);
            }
            Assert.That(rig.Plaque(), Is.Null, "gone after the linger");
            rig.Badges.Add(Badge(1));
            for (int k = 0; k < 15; k++)
            {
                rig.Frame();
                float a = Alpha(rig.Plaque());
                Assert.That(a == 0f || a == 1f, Is.True, "alpha " + a + " when the row returns, frame " + k);
            }
            Assert.That(Alpha(rig.Plaque()), Is.EqualTo(1f), "drawn again");
        }

        // ---- 11: ward text over the plaque ----

        [Test]
        public void TheWardTextStaysAboveThePlaqueOfItsCamp()
        {
            var home = BaseBadge();
            var rig = NewRig();
            rig.Badges.Add(Badge(3));
            rig.Wards.Add(new CampWardRow { Index = 3, WorldX = CampX, WorldY = CampY, BeaconRgb = 0x66ccff, TextRgb = 0x66ccff, WorldText = "YOUR WARD 107s", Countdown = "107s" });
            float top = home.yMin + .4f, from = home.center.x - 3f, to = home.center.x + 3f;
            for (int f = 0; f <= 120; f++)
            {
                rig.Obstacles = f == 0 ? new Rect[0] : new[] { Obstacle(Mathf.Lerp(from, to, f / 120f), top) };
                rig.Frame();
                Assert.That(rig.Plaque(), Is.Not.Null, "plaque, frame " + f); Assert.That(rig.WardPlate(), Is.Not.Null, "ward plate, frame " + f);
                var plaque = PlaqueRect(rig.Plaque()); var plate = XY(rig.WardPlate().bounds);
                Assert.That(plate.Overlaps(plaque), Is.False, "the ward text " + plate + " sits on the plaque " + plaque + " at frame " + f);
                Assert.That(plate.yMin, Is.GreaterThanOrEqualTo(plaque.yMax + .08f - .001f), "the ward text is a gap above the plaque, frame " + f);
            }
        }

        // ---- 12: a steady frame ----

        [Test]
        public void ASteadyFrameAllocatesNothing()
        {
            var home = BaseBadge();
            var rig = NewRig();
            rig.Badges.Add(Badge(1, CampX, CampY));
            rig.Rows.Add(new CampWorldLabel { Index = 2, WorldX = 1500f, WorldY = CampY, Text = "RESPAWN 167s" });
            rig.Wards.Add(new CampWardRow { Index = 3, WorldX = 2400f, WorldY = CampY, BeaconRgb = 0x66ccff, TextRgb = 0x66ccff, WorldText = "YOUR WARD 107s", Countdown = "107s" });
            var present = new[] { Obstacle(home.center.x, home.yMin + .4f) };
            rig.Obstacles = present;
            rig.Frames(60);
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int f = 0; f < 200; f++) rig.Frame();
            long grown = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.That(grown, Is.LessThanOrEqualTo(0), "200 unchanged frames allocate nothing");

            // And the badge does not follow a flickering fighter (the new behaviour that keeps this packet's oracle red before it).
            float settled = rig.Plaque().transform.position.y;
            var none = new Rect[0];
            for (int f = 0; f < 60; f++)
            {
                rig.Obstacles = f % 2 == 0 ? present : none;
                rig.Frame();
                Assert.That(rig.Plaque().transform.position.y, Is.EqualTo(settled).Within(1e-4f), "flicker frame " + f);
            }
        }
    }
}
