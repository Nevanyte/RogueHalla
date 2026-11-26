using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using Roguehalla.Net;
using Roguehalla.Net.Protocol;
using Roguehalla.Net.Ui;
using UnityEngine;
using static Roguehalla.Client.Tests.WorldNamesRig;

namespace Roguehalla.Client.Tests
{
    /// <summary>
    /// P-UI-WORLD-NAMES-3, the glide half: a pack's one name glides to its new place when the pack's carrier, the carrier's singled-out state
    /// (hover or lock) or its member count changes, instead of stepping; it snaps when there is no real dt, under reduced motion, and after the
    /// names were hidden. The pair: ids 1 and 2, 64 px apart (one pack by the 128 px chain). Unhovered, the name is over the middle of the extent
    /// (672 px); hovering id 2 moves it to 704 px (the carrier switches), hovering id 1 (the default carrier: carrier and count stay) to 640 px:
    /// a step of 1.0 world unit either way. Each test opens with a "not yet there" assertion: it is the red one (the base steps at once).
    /// Time is always a dt argument: the EditMode clock does not advance.
    /// </summary>
    public sealed class WorldNamesGlideEditModeTests
    {
        readonly List<Stage> _stages = new List<Stage>();
        UiTheme _savedTheme; bool? _savedReduced; bool _savedHidden; Func<float> _savedClock; bool _savedContinuity;

        Stage Fresh(string name) { var s = new Stage(name); _stages.Add(s); return s; }

        [SetUp]
        public void SetUp()
        {
            _savedTheme = UiTheme.Current; _savedReduced = UiMotion.ReducedOverride; _savedHidden = WorldLabelVisibility.Hidden;
            _savedClock = SpriteActorPresenter.FadeClock; _savedContinuity = SpriteActorPresenter.ContinuityEnabled;
            UiTheme.Current = UiTheme.Skin();
            ShowcaseSprites.Initialize();
            WorldLabelVisibility.Hidden = false; UiMotion.ReducedOverride = false; SpriteActorPresenter.FadeClock = null;
        }

        [TearDown]
        public void TearDown()
        {
            try { foreach (var s in _stages) s.Dispose(); _stages.Clear(); }
            finally
            {
                UiMotion.ReducedOverride = _savedReduced; WorldLabelVisibility.Hidden = _savedHidden;
                SpriteActorPresenter.FadeClock = _savedClock; SpriteActorPresenter.ContinuityEnabled = _savedContinuity;
                UiTheme.Current = _savedTheme;
            }
        }

        struct M
        {
            public uint Id; public float X, Y; public bool Locked, Hovered;
            public static M At(uint id, float x, float y) => new M { Id = id, X = x, Y = y };
        }

        // The frame of NamePackGroup2EditModeTests (Magma Brutes), ended with a dt.
        static void Frame(Stage s, float dt, params M[] mobs)
        {
            var p = s.P;
            p.Begin();
            foreach (var m in mobs)
            {
                p.Present(m.Id, m.X, m.Y, 0, 100, null, ActorStatus.Alive, null, ActorKind.RegionalMob, MobTypeId.MagmaBrute, 0, 0, false, 0, 0, "grunt");
                p.PresentIndicators(m.Id, 0, false, WeaponId.Fists, m.Locked, -1, m.Hovered);
            }
            p.End(dt);
        }

        static M[] Pair(bool hover1 = false, bool hover2 = false)
        {
            var one = M.At(1, 640f, 640f); one.Hovered = hover1;
            var two = M.At(2, 704f, 640f); two.Hovered = hover2;
            return new[] { one, two };
        }

        static M[] Row(int count, uint hovered = 0)
        {
            var row = new M[count];
            for (int i = 0; i < count; i++) { row[i] = M.At((uint)(i + 1), 640f + 64f * i, 640f); row[i].Hovered = row[i].Id == hovered; }
            return row;
        }

        // The one drawn name of the pack (the carrier may switch objects).
        static TMPro.TextMeshPro TheName(Stage s)
        {
            var names = ActiveNames(s.Arena);
            Assert.That(names.Count, Is.EqualTo(1), "set-up: the pack shows exactly one name");
            return names[0];
        }

        static float NameX(Stage s) { return TheName(s).transform.position.x; }
        static float NameY(Stage s) { return TheName(s).transform.position.y; }
        static bool NameShown(Stage s, uint id) { return NameOf(s.P, id).gameObject.activeSelf; }

        const float X0 = 672f * ArenaView.Scale;   // 21.0: the middle of the pair

        // A stage with the pair stood still for three frames; the name rests over the middle.
        Stage Rested(M[] scene)
        {
            var s = Fresh("world names glide arena");
            for (int n = 0; n < 3; n++) Frame(s, Dt, scene);
            return s;
        }

        static int Bound { get { return Mathf.CeilToInt(NameAnchorGlide.GlideSeconds / Dt) + 2; } }

        // The first frame moves the name toward its new place by more than 0 and at most a third of the step; no frame moves it back or past the
        // place; y never changes; it is there within the bound; the pack's name is shown on every frame by `shown`.
        static void AssertGlide(Stage s, M[] scene, float from, float to, uint shown, string what)
        {
            float step = to - from, sign = Mathf.Sign(step), size = Mathf.Abs(step), y0 = NameY(s), prev = 0f;
            for (int n = 1; n <= Bound; n++)
            {
                Frame(s, Dt, scene);
                float p = (NameX(s) - from) * sign;
                if (n == 1) Assert.That(p, Is.GreaterThan(0f).And.LessThanOrEqualTo(size / 3f + 1e-4f), what + ": the first frame moves toward the place by at most a third of the step (the base steps at once)");
                Assert.That(p, Is.GreaterThanOrEqualTo(prev - 1e-4f), what + ": frame " + n + " does not move it back");
                Assert.That(p, Is.LessThanOrEqualTo(size + 1e-4f), what + ": frame " + n + " does not go past the place");
                Assert.That(NameY(s), Is.EqualTo(y0).Within(1e-4f), what + ": y never changes, frame " + n);
                Assert.That(NameShown(s, shown), Is.True, what + ": the name is shown by id " + shown + ", frame " + n);
                prev = p;
            }
            Assert.That(NameX(s), Is.EqualTo(to).Within(1e-3f), what + ": at the place within " + Bound + " frames");
        }

        // The hover frame alone, for the tests that cut the glide: asserts the name is mid-glide (moved, but at most a third of the way).
        static void HoverFrameIsMidGlide(Stage s, float dt, M[] hovered, float from, float to, string what)
        {
            Frame(s, dt, hovered);
            float p = (NameX(s) - from) * Mathf.Sign(to - from) / Mathf.Abs(to - from);
            Assert.That(p, Is.GreaterThan(0f).And.LessThanOrEqualTo(1f / 3f + 1e-4f), what + ": the hover frame is mid-glide");
        }

        // ---- 1 ----

        [TestCase(2u)]
        [TestCase(1u)]
        public void AHoveredMemberCarriesTheNameByAGlide(uint hovered)
        {
            var s = Rested(Pair());
            Assert.That(NameX(s), Is.EqualTo(X0).Within(1e-3f), "set-up: the name rests over the middle of the pair (21.0)");
            float place = hovered == 2 ? X0 + 1f : X0 - 1f;
            var hover = Pair(hovered == 1, hovered == 2);
            AssertGlide(s, hover, X0, place, hovered, "hover on id " + hovered);
            AssertGlide(s, Pair(), place, X0, 1u, "the hover leaves id " + hovered);
        }

        // ---- 2 ----

        [Test]
        public void NoGlideWithoutARealDtOrUnderReducedMotion()
        {
            float place = X0 + 1f;
            var control = Rested(Pair());
            HoverFrameIsMidGlide(control, Dt, Pair(false, true), X0, place, "the control (dt " + Dt + ")");

            foreach (float dt in new[] { 0f, 1.5f })
            {
                var s = Rested(Pair());
                Frame(s, dt, Pair(false, true));
                Assert.That(NameX(s), Is.EqualTo(place).Within(1e-3f), "dt " + dt + ": the name is at the member's place at once");
            }
            var reduced = Rested(Pair());
            UiMotion.ReducedOverride = true;
            Frame(reduced, Dt, Pair(false, true));
            Assert.That(NameX(reduced), Is.EqualTo(place).Within(1e-3f), "reduced motion: the name is at the member's place at once");
        }

        // ---- 3 ----

        [TestCase("hidden")]
        [TestCase("dt0")]
        [TestCase("reduced")]
        public void AMidGlideCutSnapsAndTheTabMapForgets(string cut)
        {
            float place = X0 + 1f;
            var hover = Pair(false, true);
            var s = Rested(Pair());
            HoverFrameIsMidGlide(s, Dt, hover, X0, place, cut);
            if (cut == "hidden")
            {
                WorldLabelVisibility.Hidden = true;
                Frame(s, Dt, hover);
                Assert.That(ActiveNames(s.Arena).Count, Is.EqualTo(0), "the Tab map is open: no name is drawn");
                WorldLabelVisibility.Hidden = false;
                Frame(s, Dt, hover);
            }
            else if (cut == "dt0") Frame(s, 0f, hover);
            else { UiMotion.ReducedOverride = true; Frame(s, Dt, hover); }
            Assert.That(NameX(s), Is.EqualTo(place).Within(1e-3f), cut + ": the next frame is at the member's place at once");
        }

        // ---- 4 ----

        [Test]
        public void AWalkingPackKeepsPaceWhileTheStepGlides()
        {
            Func<int, bool, M[]> walk = (f, hover2) =>
            {
                var one = M.At(1, 640f + 4f * f, 640f); var two = M.At(2, 704f + 4f * f, 640f); two.Hovered = hover2;
                return new[] { one, two };
            };
            var plain = Rested(walk(0, false));
            for (int f = 1; f <= 30; f++)
            {
                Frame(plain, Dt, walk(f, false));
                Assert.That(NameX(plain), Is.EqualTo((672f + 4f * f) * ArenaView.Scale).Within(1e-3f), "unhovered, the name stays over the middle of the walking pair, frame " + f);
            }

            var s = Rested(walk(0, false));
            for (int f = 1; f <= 30; f++)
            {
                Frame(s, Dt, walk(f, true));
                float d = NameX(s) - (704f + 4f * f) * ArenaView.Scale;   // against id 2's own place
                if (f <= 2) Assert.That(d, Is.LessThan(-1e-3f), "hover on id 2: the name still lags behind the walker, frame " + f + " (the base is on it at once)");
                Assert.That(d, Is.LessThanOrEqualTo(1e-4f), "the name never gets ahead of the walker, frame " + f);
                if (f >= Bound) Assert.That(d, Is.EqualTo(0f).Within(1e-3f), "the name has caught up with the walker, frame " + f);
            }
        }

        // ---- 5 ----

        [Test]
        public void ASecondEndOnTheSameFrameChangesNothingMidGlide()
        {
            float place = X0 + 1f;
            var hover = Pair(false, true);
            var s = Rested(Pair());
            HoverFrameIsMidGlide(s, Dt, hover, X0, place, "the hover frame");
            float x = NameX(s);
            s.P.End(Dt);
            Assert.That(NameX(s), Is.EqualTo(x).Within(1e-5f), "a second End on the same stamp leaves the name where it is");
            foreach (var m in hover) s.P.PresentIndicators(m.Id, 0, false, WeaponId.Fists, m.Locked, -1, m.Hovered);   // the second half of a frame presented twice
            s.P.End(Dt);
            Assert.That(NameX(s), Is.EqualTo(x).Within(1e-4f), "the indicators presented again and End on the same stamp: the same place");
        }

        // ---- 6 ----

        [Test]
        public void ADroppedMemberGlidesTheAnchor()
        {
            var s = Rested(Row(3));
            Assert.That(NameX(s), Is.EqualTo(704f * ArenaView.Scale).Within(1e-3f), "set-up: the name is over the middle of three (22.0)");
            Assert.That(NameShown(s, 1), Is.True, "set-up: id 1 carries it");
            AssertGlide(s, Row(2), 704f * ArenaView.Scale, 672f * ArenaView.Scale, 1u, "id 3 drops out of sight");
        }

        // ---- 7 ----

        [Test]
        public void ASteadyHoveredPackMergeAllocatesNothing()
        {
            var s = Rested(Pair());
            HoverFrameIsMidGlide(s, Dt, Pair(false, true), X0, X0 + 1f, "the hover frame");
            var p = s.P;
            var flags = BindingFlags.NonPublic | BindingFlags.Instance;
            var field = typeof(SpriteActorPresenter).GetField("_glideStamp", flags);
            Assert.That(field, Is.Not.Null, "the presenter has a private instance int field _glideStamp");
            var method = typeof(SpriteActorPresenter).GetMethod("MergeMobNames", flags, null, new[] { typeof(float) }, null);
            Assert.That(method, Is.Not.Null, "the presenter has a private instance method MergeMobNames(float)");
            var merge = (Action<float>)Delegate.CreateDelegate(typeof(Action<float>), p, method);
            object minusOne = -1;   // boxed once, outside the delegates
            // A real frame's indicators, hover kept (the rig's Reindicate passes no hover), with no Begin and no Present.
            Action indicators = () =>
            {
                p.PresentIndicators(1, 0, false, WeaponId.Fists, false, -1, false);
                p.PresentIndicators(2, 0, false, WeaponId.Fists, false, -1, true);
            };
            Action reset = () => field.SetValue(p, minusOne);
            Action step = () => { indicators(); reset(); merge(Dt); };
            Action stepSameStamp = () => { indicators(); merge(Dt); };

            indicators();
            Assert.That(ActiveNames(s.Arena).Count, Is.EqualTo(2), "set-up: the indicators bring the second name back");
            reset(); merge(Dt);
            Assert.That(ActiveNames(s.Arena).Count, Is.EqualTo(1), "set-up: the merge shows one name again");

            int indicatorSamples = AllocationMeter.Samples(indicators, 100);
            AllocationMeter.AssertAtMost(step, 100, indicatorSamples, "the merge of a hovered pack adds no allocation to the indicators part (" + indicatorSamples + " samples in 100 calls)");
            AllocationMeter.AssertAtMost(stepSameStamp, 100, indicatorSamples, "a merge on an already merged stamp adds none either");
        }
    }
}
