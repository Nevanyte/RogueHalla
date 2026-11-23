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
    /// P-UI-WORLD-NAMES-2, the time half: a name's lift is remembered per key, rises at the rise rate, holds, falls slowly, follows reduced motion,
    /// is forgotten with the Tab map or after more than a second unseen, survives a renewed observer handle, and comes from the presenter's own clock
    /// when End has no dt. Every frame ends <c>End(1f / 60f)</c> except in test 13. Frame counts and bounds are computed from the library's constants.
    /// </summary>
    public sealed class WorldNamesTimeEditModeTests
    {
        readonly List<Stage> _stages = new List<Stage>();
        UiTheme _savedTheme; bool? _savedReduced; bool _savedHidden; Func<float> _savedClock; bool _savedContinuity; float _now;

        Stage Fresh(string name) { var s = new Stage(name); _stages.Add(s); return s; }
        SpriteActorPresenter P { get { return _stages[0].P; } }
        GameObject Arena { get { return _stages[0].Arena; } }

        [SetUp]
        public void SetUp()
        {
            _savedTheme = UiTheme.Current; _savedReduced = UiMotion.ReducedOverride; _savedHidden = WorldLabelVisibility.Hidden;
            _savedClock = SpriteActorPresenter.FadeClock; _savedContinuity = SpriteActorPresenter.ContinuityEnabled;
            UiTheme.Current = UiTheme.Skin();
            ShowcaseSprites.Initialize();
            WorldLabelVisibility.Hidden = false; UiMotion.ReducedOverride = false; SpriteActorPresenter.FadeClock = null;
            Fresh("world names time arena");
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

        // ---- 8. a renewed handle keeps its lift and its place ----

        void Step(Scene scene) { scene.Present(P); P.End(Dt); _now += Dt; P.StepFades(); }

        [Test]
        public void ARenewedHandleKeepsItsLiftAndItsPlace()
        {
            _now = 50f; SpriteActorPresenter.FadeClock = () => _now;
            SpriteActorPresenter.ContinuityEnabled = true; P.ExitFadesAllowed = true; SpriteActorPresenter.MobArt.Reset();
            var elite = Actor.Mob(1, 640f, 640f, MobTypeId.Wyrmling);
            var pardoner = Actor.Mob(2, 640f, 640f, MobTypeId.Pardoner);
            Step(Scene.Of(elite, pardoner));
            Assert.That(P.TryNameLift(2, out float t, out _), Is.True, "set-up: the lone Pardoner takes part");
            Assert.That(t, Is.GreaterThan(0f), "set-up: the Pardoner, on the elite's spot, yields");

            int frames = Mathf.FloorToInt(LabelLift.HoldSeconds * 60f / 2f);
            for (int n = 0; n < frames; n++) Step(Scene.Of(pardoner));   // the elite is dropped
            Assert.That(LiftAndTarget(P, 2).lift, Is.EqualTo(t).Within(1e-4f), "half the hold time after the elite left, the Pardoner's lift is held");

            var renewed = new HashSet<uint> { 3 }; P.Delivered = renewed; P.Retain(renewed);   // the server renewed the handle: id 2 is gone, id 3 appears
            Step(Scene.Of(Actor.Mob(3, 648f, 640f, MobTypeId.Pardoner)));   // 8 px away
            Assert.That(SpriteActorPresenter.MobArt.Continued, Is.EqualTo(1), "the art followed the renewed handle");
            var (lift, target) = LiftAndTarget(P, 3);
            Assert.That(lift, Is.EqualTo(t).Within(1e-4f), "the renewed handle keeps the lift");
            Assert.That(target, Is.EqualTo(t).Within(1e-4f), "and the target");
        }

        // ---- 9. reduced motion ----

        static Scene Two(bool contact) { return Scene.Of(Actor.Fighter(1, 640f, "Mara", true), Actor.Fighter(2, contact ? 646f : 1040f, "Rowan")); }

        [Test]
        public void ReducedMotionTakesTheTargetAtOnceAndKeepsTheHold()
        {
            // Reduced: the target at once, held for the hold time (less two frames), then 0 in one step.
            UiMotion.ReducedOverride = true;
            var reduced = Fresh("reduced motion arena").P;
            Frame(reduced, Two(false)); Frame(reduced, Two(false)); Frame(reduced, Two(true));
            Assert.That(reduced.TryNameLift(2, out float lift, out float target), Is.True, "set-up: the second fighter takes part");
            Assert.That(lift, Is.GreaterThan(0f), "set-up: it yields at the contact");
            Assert.That(lift, Is.EqualTo(target).Within(1e-5f), "reduced motion: the lift is the target at once");
            float t = target;
            int held = Mathf.FloorToInt(LabelLift.HoldSeconds * 60f) - 2, zeroBy = Mathf.CeilToInt(LabelLift.HoldSeconds * 60f) + 2;
            for (int n = 1; n <= zeroBy; n++)
            {
                Frame(reduced, Two(false));
                float now = LiftAndTarget(reduced, 2).lift;
                if (n <= held) Assert.That(now, Is.EqualTo(t).Within(1e-4f), "contact gone, frame " + n + ": held");
                else Assert.That(Mathf.Abs(now - t) < 1e-4f || Mathf.Abs(now) < 1e-5f, Is.True, "contact gone, frame " + n + ": the lift " + now + " is the held " + t + " or 0, nothing between (one step)");
            }
            Assert.That(LiftAndTarget(reduced, 2).lift, Is.EqualTo(0f).Within(1e-5f), "0 by frame " + zeroBy);

            // Not reduced: the first frame of a contact climbs at the rise rate, and the camp labels get the name where it is going.
            UiMotion.ReducedOverride = false;
            var moving = Fresh("moving arena").P;
            Frame(moving, Two(false)); Frame(moving, Two(false)); Frame(moving, Two(true));
            var (l, tg) = LiftAndTarget(moving, 2);
            Assert.That(l, Is.EqualTo(LabelLift.RiseUnitsPerSecond / 60f).Within(1e-4f), "the first frame climbs at the rise rate");
            Assert.That(tg, Is.GreaterThan(l + 1e-3f), "the target is above the drawn lift");
            var box = LineBoxOf(moving, 2);
            var expected = new Rect(box.xMin - SpriteActorPresenter.NameRectPad, box.yMin + (tg - l), box.width + 2f * SpriteActorPresenter.NameRectPad, box.height);
            var rects = new List<Rect>(); moving.FighterNameRects(rects);
            Assert.That(AnyEquals(rects, expected, 1e-3f), Is.True, "FighterNameRects holds the line box " + box + " moved up by " + (tg - l) + " and grown in x");
        }

        // ---- 10. the Tab map forgets ----

        [Test]
        public void TheTabMapForgetsAndNamesReturnAtTheirLift()
        {
            var pic = MeasurePicture(P, OverheadBoxLayout.PadEnter / 4f);
            Settle(P, pic.All);
            Assert.That(P.TryNameLift(pic.Carrier, out float t, out float target), Is.True, "set-up: the pack's name takes part");
            Assert.That(t, Is.GreaterThan(0f), "set-up: the pack yields to the Gaoler");
            Assert.That(target, Is.EqualTo(t).Within(1e-5f), "set-up: the lift has settled");

            WorldLabelVisibility.Hidden = true;   // the map opens on a running scene
            Frame(P, pic.All);
            Assert.That(ActiveNames(Arena).Count, Is.EqualTo(0), "no name is drawn while the map is open");
            foreach (uint id in new uint[] { 11, 12, 13 }) Assert.That(P.TryNameLift(id, out _, out _), Is.False, "id " + id + " takes no part while the map is open");
            var rects = new List<Rect>(); P.FighterNameRects(rects);
            Assert.That(rects.Count, Is.EqualTo(0), "nothing is handed to the camp labels while the map is open");

            WorldLabelVisibility.Hidden = false;
            Frame(P, pic.All);
            var (lift, back) = LiftAndTarget(P, pic.Carrier);
            Assert.That(lift, Is.EqualTo(t).Within(1e-3f), "the name is back at its lift on the first frame (the memory was cleared: a first-sight answer)");
            Assert.That(back, Is.EqualTo(t).Within(1e-3f), "and its target");
        }

        // ---- 11. a hidden name keeps its lift for half a second and forgets after more than a second ----

        static Scene Pair(float localX, bool hidden)
        {
            var two = Actor.Fighter(2, 646f, "Rowan");
            return Scene.Of(Actor.Fighter(1, localX, "Mara", true), hidden ? two.Hidden() : two);
        }

        float HiddenThenVisible(Stage stage, int hiddenFrames, out float lift0)
        {
            var p = stage.P;
            Frame(p, Pair(640f, false));
            Assert.That(p.TryNameLift(2, out float t, out _), Is.True, "set-up: the second fighter takes part");
            Assert.That(t, Is.GreaterThan(0f), "set-up: it yields at the contact");
            lift0 = t;
            for (int n = 0; n < hiddenFrames; n++)
            {
                Frame(p, Pair(1640f, true));
                Assert.That(p.TryNameLift(2, out _, out _), Is.False, "hidden frame " + n + ": a hidden fighter has no box (its root is inactive)");
            }
            Frame(p, Pair(1640f, false));   // visible again, no contact
            return LiftAndTarget(p, 2).lift;
        }

        [Test]
        public void AHiddenNameKeepsItsLiftForAHalfSecondAndForgetsAfterMoreThanASecond()
        {
            var kept = Fresh("kept arena");
            float shortLift = HiddenThenVisible(kept, Mathf.CeilToInt(OverheadBoxLayout.ForgetSeconds * 30f), out float t);
            var (l, tg) = LiftAndTarget(kept.P, 2);
            Assert.That(l, Is.EqualTo(t).Within(1e-3f), "hidden for half the forget time: the lift is there at once");
            Assert.That(tg, Is.EqualTo(t).Within(1e-3f), "and the target");
            Assert.That(shortLift, Is.EqualTo(l).Within(1e-6f), "(the same read)");

            var forgotten = Fresh("forgotten arena");
            float longLift = HiddenThenVisible(forgotten, Mathf.CeilToInt(OverheadBoxLayout.ForgetSeconds * 60f) + 1, out _);
            Assert.That(longLift, Is.EqualTo(0f).Within(1e-5f), "hidden for more than the forget time: the memory is gone, no contact, no lift");
        }

        [Test]
        public void ANameFirstSeenHiddenIsMeasuredWhenItAppears()
        {
            var p = P;
            for (int n = 0; n < 3; n++) Frame(p, Pair(640f, true));   // fighter 2 is first presented hidden, beside the local fighter
            Frame(p, Pair(640f, false));
            Assert.That(p.TryNameLift(2, out float lift, out float target), Is.True, "set-up: the revealed fighter takes part");
            Assert.That(lift, Is.GreaterThan(0f), "the revealed name yields to the local fighter's");
            Assert.That(lift, Is.EqualTo(target).Within(1e-5f), "a first sight: the lift is the target");
            var mine = LineBoxOf(p, 2); var local = LineBoxOf(p, 1);
            Assert.That(Clearance(local, mine), Is.GreaterThanOrEqualTo(OverheadBoxLayout.Gap - .01f), "its line box " + mine + " clears the local fighter's " + local);
            var rects = new List<Rect>(); p.FighterNameRects(rects);
            Assert.That(AnyContains(rects, mine, 1e-3f), Is.True, "FighterNameRects holds a rect containing its line box " + mine);
        }

        // ---- 13. End without a dt ----

        void ThreeFrames(Stage stage, Func<float> clock, Action advance, out float lift, out float target)
        {
            SpriteActorPresenter.FadeClock = clock;
            var p = stage.P;
            Two(false).Present(p); p.End(); advance();
            Two(false).Present(p); p.End(); advance();
            Two(true).Present(p); p.End();
            Assert.That(p.TryNameLift(2, out lift, out target), Is.True, "set-up: the second fighter takes part");
        }

        [Test]
        public void EndWithoutADtDerivesItFromThePresenterClock()
        {
            // No clock, then a constant one: every dt is 0, every frame a first sight, the lift is the target at once.
            ThreeFrames(Fresh("no clock arena"), null, () => { }, out float lift, out float target);
            Assert.That(lift, Is.GreaterThan(0f), "set-up: the contact makes the name yield");
            Assert.That(lift, Is.EqualTo(target).Within(1e-5f), "no presenter clock: the lift is the target at once");
            ThreeFrames(Fresh("constant clock arena"), () => 5f, () => { }, out lift, out target);
            Assert.That(lift, Is.GreaterThan(0f), "set-up: the contact makes the name yield (constant clock)");
            Assert.That(lift, Is.EqualTo(target).Within(1e-5f), "a constant clock: the lift is the target at once");

            // A clock of a sixtieth of a second a frame: frame 1 has dt 0, frame 2 records entries, frame 3 climbs at the rise rate.
            float time = 0f;
            ThreeFrames(Fresh("moving clock arena"), () => time, () => time += Dt, out lift, out target);
            Assert.That(lift, Is.EqualTo(LabelLift.RiseUnitsPerSecond / 60f).Within(1e-4f), "a moving clock: the first frame of the contact climbs at the rise rate");
            Assert.That(target, Is.GreaterThan(lift + 1e-3f), "the target is above the drawn lift");
        }

        // ---- 14. a steady frame allocates nothing ----

        [Test]
        public void LayoutNamesAllocatesNothing()
        {
            var actors = new List<Actor>();
            for (uint i = 0; i < 10; i++) actors.Add(Actor.Fighter(i + 1, 640f + 6f * i, "fighter " + (i + 1), i == 0));   // ten names on one another
            for (uint i = 0; i < 10; i++) actors.Add(Actor.Mob(100 + i, 1400f + 400f * i, 640f, MobTypeId.MagmaBrute));   // ten more, far apart
            Frame(P, Scene.Of(actors.ToArray()));
            for (uint id = 2; id <= 10; id++)
            {
                Assert.That(P.TryNameLift(id, out float lift, out _), Is.True, "set-up: fighter " + id + " takes part");
                Assert.That(lift, Is.GreaterThan(0f), "set-up: fighter " + id + " yields");
            }
            var method = typeof(SpriteActorPresenter).GetMethod("LayoutNames", BindingFlags.NonPublic | BindingFlags.Instance, null, new[] { typeof(float) }, null);
            Assert.That(method, Is.Not.Null, "the presenter has a private instance method LayoutNames(float)");
            var layout = (Action<float>)Delegate.CreateDelegate(typeof(Action<float>), P, method);
            Action step = () => layout(Dt);
            AllocationMeter.AssertNone(step, 100, "the name pass of a steady frame");
        }
    }
}
