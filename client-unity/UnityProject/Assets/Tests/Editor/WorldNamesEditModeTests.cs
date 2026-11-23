using System.Collections.Generic;
using NUnit.Framework;
using Roguehalla.Net;
using Roguehalla.Net.Protocol;
using Roguehalla.Net.Ui;
using TMPro;
using UnityEngine;
using static Roguehalla.Client.Tests.WorldNamesRig;

namespace Roguehalla.Client.Tests
{
    /// <summary>
    /// P-UI-WORLD-NAMES-2, the layout half: every drawn name is one rigid box of its actor's cluster that keeps clear of the boxes before it
    /// (class, then key) and of other actors' health bars, the cluster moves as one, and the camp labels read the result. Time (memory, glide,
    /// hold, forget) is in <see cref="WorldNamesTimeEditModeTests"/>. Each test opens with a set-up that holds on the base (the alone boxes,
    /// grown by <see cref="OverheadBoxLayout.PadEnter"/>, overlap) and then asks for what the layout gives (<see cref="SpriteActorPresenter.TryNameLift"/>
    /// true with a lift). Spacing is stated through the library's constants, never as a number a tuning would move.
    /// </summary>
    public sealed class WorldNamesEditModeTests
    {
        Stage _stage; GameObject _labels;
        UiTheme _savedTheme; bool? _savedReduced; bool _savedHidden; System.Func<float> _savedClock; bool _savedContinuity;

        SpriteActorPresenter P { get { return _stage.P; } }
        GameObject Arena { get { return _stage.Arena; } }

        [SetUp]
        public void SetUp()
        {
            _savedTheme = UiTheme.Current; _savedReduced = UiMotion.ReducedOverride; _savedHidden = WorldLabelVisibility.Hidden;
            _savedClock = SpriteActorPresenter.FadeClock; _savedContinuity = SpriteActorPresenter.ContinuityEnabled;
            UiTheme.Current = UiTheme.Skin();
            ShowcaseSprites.Initialize();
            WorldLabelVisibility.Hidden = false; UiMotion.ReducedOverride = false; SpriteActorPresenter.FadeClock = null;
            _stage = new Stage("world names arena");
        }

        [TearDown]
        public void TearDown()
        {
            try
            {
                if (_labels != null) Object.DestroyImmediate(_labels);
                if (_stage != null) _stage.Dispose();
            }
            finally
            {
                UiMotion.ReducedOverride = _savedReduced; WorldLabelVisibility.Hidden = _savedHidden;
                SpriteActorPresenter.FadeClock = _savedClock; SpriteActorPresenter.ContinuityEnabled = _savedContinuity;
                UiTheme.Current = _savedTheme;
            }
        }

        static Rect Union(IEnumerable<Rect> rects)
        {
            bool any = false; Rect u = default;
            foreach (var r in rects) { if (!any) { u = r; any = true; } else u = Rect.MinMaxRect(Mathf.Min(u.xMin, r.xMin), Mathf.Min(u.yMin, r.yMin), Mathf.Max(u.xMax, r.xMax), Mathf.Max(u.yMax, r.yMax)); }
            return u;
        }

        // ---- 1. the picture ----

        [Test]
        public void ThePictureNamesNoLongerTouch()
        {
            var pic = MeasurePicture(P, OverheadBoxLayout.PadEnter / 4f);
            Assert.That(Grown(pic.GaolerBox, OverheadBoxLayout.PadEnter).Overlaps(pic.SlidPackBox), Is.True, "set-up: the alone boxes, grown by PadEnter, overlap");
            Settle(P, pic.All);
            Assert.That(P.TryNameLift(pic.Carrier, out float packLift, out _), Is.True, "set-up: the pack's name takes part in the layout");
            Assert.That(packLift, Is.GreaterThan(0f), "set-up: the pack yields to the Gaoler");

            Assert.That(P.TryNameLift(13, out float gLift, out _), Is.True, "the Gaoler takes part");
            Assert.That(gLift, Is.EqualTo(0f).Within(1e-5f), "the elite is visited first and does not move");
            Assert.That(NameOf(P, 13).transform.position.x, Is.EqualTo(pic.GaolerNameAt.x).Within(1e-3f), "the Gaoler's name stays (x)");
            Assert.That(NameOf(P, 13).transform.position.y, Is.EqualTo(pic.GaolerNameAt.y).Within(1e-3f), "the Gaoler's name stays (y)");
            var star = SpriteOf(P, 13, "Elite star").transform.position;
            Assert.That(star.x, Is.EqualTo(pic.GaolerStarAt.x).Within(1e-3f), "the star stays (x)");
            Assert.That(star.y, Is.EqualTo(pic.GaolerStarAt.y).Within(1e-3f), "the star stays (y)");

            Assert.That(packLift, Is.GreaterThanOrEqualTo((pic.GaolerBox.height + pic.PackBox.height) / 2f + OverheadBoxLayout.Gap - 1e-3f), "the pack rises over the Gaoler's line");
            Assert.That(packLift, Is.LessThanOrEqualTo(OverheadBoxLayout.MaxLift), "and no further than the cap");
            var g = LineBoxOf(P, 13); var pack = LineBoxOf(P, pic.Carrier);
            var plate = XY(SpriteOf(P, 13, "Elite star plate").bounds);
            Assert.That(Clearance(g, pack), Is.GreaterThanOrEqualTo(OverheadBoxLayout.Gap - .01f), "the pack's line box clears the Gaoler's by the gap");
            Assert.That(pack.Overlaps(plate), Is.False, "the pack's line box " + pack + " clears the star plate " + plate);
            Assert.That(star.x, Is.EqualTo(NameOf(P, 13).transform.position.x).Within(1e-3f), "the star is centred on the Gaoler's name");
            Assert.That(star.y - NameOf(P, 13).transform.position.y, Is.EqualTo(pic.GaolerStarAt.y - pic.GaolerNameAt.y).Within(1e-3f), "at its alone offset");

            // A camp badge row on the Gaoler's name (the wiring ArenaView does), drawn after the frame's names are placed.
            _labels = new GameObject("camp labels"); _labels.transform.SetParent(Arena.transform, false);
            var camp = new CampRespawnLabels(_labels.transform);
            camp.NameRectSource = into => P.FighterNameRects(into);
            double campX = g.center.x / ArenaView.Scale, campY = -g.center.y / (ArenaView.Scale * ArenaView.GroundSquash) + 44.0;   // the badge sits 44 px above the camp
            camp.Draw(new List<CampWorldLabel>(), new List<CampWardRow>(),
                new List<CampBadgeRow> { new CampBadgeRow { Index = 0, WorldX = campX, WorldY = campY, IconKey = "ad", IconRgb = 0xff8a3d, EliteIconKey = "ap", EliteIconRgb = 0xb39ddb } });
            var badgeRects = new List<Rect>();
            foreach (var r in _labels.GetComponentsInChildren<SpriteRenderer>(false))
                if (r.gameObject.activeInHierarchy && (r.name == "Camp reward plaque" || r.name == "Camp reward badge")) badgeRects.Add(XY(r.bounds));
            Assert.That(badgeRects.Count, Is.GreaterThanOrEqualTo(2), "set-up: the badge (plaque and icons) is drawn");
            var badge = Union(badgeRects);
            Assert.That(badge.Overlaps(g), Is.False, "the badge " + badge + " clears the Gaoler's line box " + g);
            Assert.That(badge.Overlaps(plate), Is.False, "the badge clears the star plate " + plate);
            Assert.That(badge.Overlaps(pack), Is.False, "the badge clears the pack's line box " + pack);
        }

        // ---- 2. adjacent elites ----

        [Test]
        public void AdjacentElitesDoNotLandOnEachOthersStars()
        {
            Frame(P, Scene.Of(Actor.Mob(21, 640f, 640f, MobTypeId.Wyrmling), Actor.Mob(22, 646f, 640f, MobTypeId.Wyrmling)));
            Assert.That(P.TryNameLift(22, out float lift22, out _), Is.True, "set-up: the second elite takes part");
            Assert.That(lift22, Is.GreaterThan(0f), "set-up: the second elite, 6 px beside the first, yields");

            Assert.That(LiftOf(P, 21), Is.EqualTo(0f).Within(1e-5f), "the first elite (the older art) does not move");
            var n21 = LineBoxOf(P, 21); var n22 = LineBoxOf(P, 22);
            var p21 = XY(SpriteOf(P, 21, "Elite star plate").bounds); var p22 = XY(SpriteOf(P, 22, "Elite star plate").bounds);
            Assert.That(n22.Overlaps(n21), Is.False, "22's name clears 21's name");
            Assert.That(n22.Overlaps(p21), Is.False, "22's name " + n22 + " clears 21's star plate " + p21);
            Assert.That(p22.Overlaps(n21), Is.False, "22's star plate clears 21's name");
            Assert.That(p22.Overlaps(p21), Is.False, "22's star plate clears 21's star plate");
            foreach (uint id in new uint[] { 21, 22 })
                Assert.That(SpriteOf(P, id, "Elite star").transform.position.x, Is.EqualTo(NameOf(P, id).transform.position.x).Within(1e-3f), "the star of " + id + " is centred on its name");
        }

        // ---- 3. every kind of name takes part ----

        [Test]
        public void EveryKindOfNameTakesPart()
        {
            float far = 2400f;
            var a = Scene.Of(Actor.Fighter(1, 640f, "Aria", true), Actor.Fighter(2, 646f, "Bram"), Actor.Mob(3, 652f, 640f, MobTypeId.Wyrmling));
            var b = Scene.Of(Actor.Dummy(4, far, 640f), Actor.Mob(5, far + 6f, 640f, MobTypeId.Pardoner));
            Frame(P, Scene.Join(a, b));
            Assert.That(P.TryNameLift(2, out float lift2, out _), Is.True, "set-up: the second fighter takes part");
            Assert.That(lift2, Is.GreaterThan(0f), "set-up: it yields to the local fighter");

            var rects = new List<Rect>(); P.FighterNameRects(rects);
            foreach (var ids in new[] { new uint[] { 1, 2, 3 }, new uint[] { 4, 5 } })
            {
                string scene = "the scene of " + ids[0];
                float previous = -1f, top = 0f; var boxes = new List<Rect>();
                foreach (uint id in ids)
                {
                    Assert.That(P.TryNameLift(id, out float lift, out _), Is.True, scene + ": id " + id + " takes part");
                    if (previous < 0f) Assert.That(lift, Is.EqualTo(0f).Within(1e-5f), scene + ": the first in order does not move");
                    else Assert.That(lift, Is.GreaterThan(previous + 1e-3f), scene + ": id " + id + " rises over the one before it");
                    previous = lift; top = lift;
                    var box = LineBoxOf(P, id);
                    foreach (var other in boxes) Assert.That(box.Overlaps(other), Is.False, scene + ": the name of " + id + " " + box + " overlaps " + other);
                    boxes.Add(box);
                    Assert.That(AnyContains(rects, box, 1e-3f), Is.True, scene + ": FighterNameRects holds a rect containing the name of " + id + " " + box);
                }
                Assert.That(top, Is.LessThan(.95f * OverheadBoxLayout.MaxLift), scene + ": the cap does not bite (a tuning that makes it bite fails here)");
            }
        }

        // ---- 4. a small pace moves nothing; a release waits, then glides ----

        [Test]
        public void ASmallPaceMovesNothingAndAReleaseWaitsThenGlides()
        {
            const float rise = LabelLift.RiseUnitsPerSecond / 60f, fall = LabelLift.FallUnitsPerSecond / 60f;
            var pic = MeasurePicture(P, OverheadBoxLayout.PadEnter / 2f);
            Assert.That(Grown(pic.GaolerBox, OverheadBoxLayout.PadEnter).Overlaps(pic.SlidPackBox), Is.True, "set-up: the alone boxes, grown by PadEnter, overlap");
            var enter = pic.At(OverheadBoxLayout.PadEnter / 2f);
            var keep = pic.At((OverheadBoxLayout.PadEnter + OverheadBoxLayout.PadKeep) / 2f);
            var release = pic.At(OverheadBoxLayout.PadKeep + .2f);
            Settle(P, enter);
            Assert.That(P.TryNameLift(pic.Carrier, out float t0, out float target0), Is.True, "set-up: the pack's name takes part");
            Assert.That(t0, Is.GreaterThan(0f), "set-up: the pack yields to the Gaoler");
            Assert.That(target0, Is.EqualTo(t0).Within(1e-5f), "set-up: the lift has settled");

            // A pace between the two paddings: the pack keeps its lift and its target.
            for (int n = 0; n < 2 * FrameBound; n++)
            {
                Frame(P, n % 2 == 0 ? keep : enter);
                var (lift, target) = LiftAndTarget(P, pic.Carrier);
                Assert.That(lift, Is.EqualTo(t0).Within(1e-4f), "frame " + n + ": the lift of a small pace");
                Assert.That(target, Is.EqualTo(t0).Within(1e-4f), "frame " + n + ": the target of a small pace");
            }

            // A release: held for the hold time less two frames, then a fall no faster than the fall rate.
            int held = Mathf.FloorToInt(LabelLift.HoldSeconds * 60f) - 2;
            for (int n = 1; n <= held; n++)
            {
                Frame(P, release);
                Assert.That(LiftAndTarget(P, pic.Carrier).lift, Is.EqualTo(t0).Within(1e-4f), "release frame " + n + " of " + held + ": still held");
            }
            float previous = t0; int fallBound = Mathf.CeilToInt(OverheadBoxLayout.MaxLift / fall) + 6, frames = 0;
            while (previous > 1e-5f && frames < fallBound)
            {
                Frame(P, release); frames++;
                float lift = LiftAndTarget(P, pic.Carrier).lift;
                Assert.That(lift, Is.LessThanOrEqualTo(previous + 1e-5f), "fall frame " + frames + ": the lift never rises on a release");
                Assert.That(previous - lift, Is.LessThanOrEqualTo(fall + 1e-4f), "fall frame " + frames + ": it falls no faster than the fall rate");
                previous = lift;
            }
            Assert.That(previous, Is.EqualTo(0f).Within(1e-5f), "the lift is 0 after " + frames + " frames of release");

            // Back in contact: it rises no faster than the rise rate.
            Frame(P, enter);
            var (up, upTarget) = LiftAndTarget(P, pic.Carrier);
            Assert.That(up, Is.GreaterThan(0f), "contact again: the lift starts to climb");
            Assert.That(up, Is.LessThanOrEqualTo(rise + 1e-4f), "contact again: the first frame climbs no more than the rise rate");
            Assert.That(upTarget, Is.GreaterThan(up + 1e-3f), "contact again: the target is above the drawn lift");
            previous = up;
            for (int n = 0; n < FrameBound && previous < upTarget - 1e-5f; n++)
            {
                Frame(P, enter);
                float lift = LiftAndTarget(P, pic.Carrier).lift;
                Assert.That(lift - previous, Is.LessThanOrEqualTo(rise + 1e-4f), "climb frame " + n + ": no faster than the rise rate");
                previous = lift;
            }
            Assert.That(previous, Is.EqualTo(upTarget).Within(1e-4f), "the lift reaches its target");
        }

        // ---- 5. the order does not follow the player ----

        [Test]
        public void TheOrderDoesNotFollowThePlayer()
        {
            Scene Row(float x1, float x2, float x3) =>
                Scene.Of(Actor.Fighter(1, x1, "aaa1", true), Actor.Fighter(2, x2, "bbb2"), Actor.Fighter(3, x3, "ccc3"));
            var first = Row(640f, 646f, 652f);
            Settle(P, first);
            Assert.That(P.TryNameLift(2, out float l2a, out _), Is.True, "set-up: the second fighter takes part");
            Assert.That(l2a, Is.GreaterThan(0f), "set-up: it yields to the local fighter, 6 px away");
            float l1a = LiftOf(P, 1), l3a = LiftOf(P, 3);
            P.TryPosition(1, out var root1a);
            float dyA = NameOf(P, 1).transform.position.y - root1a.y;

            Settle(P, Row(652f, 646f, 640f));   // the local fighter at the other end
            float l1b = LiftOf(P, 1), l2b = LiftOf(P, 2), l3b = LiftOf(P, 3);
            P.TryPosition(1, out var root1b);
            float dyB = NameOf(P, 1).transform.position.y - root1b.y;
            Assert.That(l1a, Is.EqualTo(0f).Within(1e-5f), "the local fighter does not move (left end)");
            Assert.That(l1b, Is.EqualTo(0f).Within(1e-5f), "the local fighter does not move (right end)");
            Assert.That(dyB, Is.EqualTo(dyA).Within(1e-4f), "its name keeps its place over its head");
            Assert.That(l2b, Is.EqualTo(l2a).Within(1e-3f), "the second fighter's lift does not follow the player");
            Assert.That(l3b, Is.EqualTo(l3a).Within(1e-3f), "nor does the third's");
            Assert.That(l2a, Is.LessThan(l3a), "the lower id stays lower");
        }

        // ---- 6. a cluster moves as one ----

        static readonly string[] ClusterParts =
        {
            "Name", "Bounty crown", "Bounty number", "Secondary weapon backer", "Mark pip 1", "Mark pip 2", "Mark pip 3", "Mark pip 4", "Stat mark backer 1", "Stat mark backer 2",
        };

        [Test]
        public void AClusterMovesAsOneAndTheStarGoesWithItsName()
        {
            var crowned = Actor.Fighter(2, 640f, "Rowan").Marks(40, true, 1, 2, 2);
            Frame(P, Scene.Of(crowned));
            var alone = new Dictionary<string, float>();
            foreach (string part in ClusterParts)
            {
                var t = Child(RootOf(P, 2), part);
                Assert.That(t.gameObject.activeInHierarchy, Is.True, "set-up: " + part + " is drawn on the lone fighter");
                var sr = t.GetComponent<SpriteRenderer>(); if (sr != null) Assert.That(sr.enabled, Is.True, "set-up: " + part + " is enabled");
                alone[part] = t.position.y;
            }
            var near = Scene.Of(Actor.Fighter(1, 646f, "Mara", true), crowned);
            Settle(P, near);
            Assert.That(P.TryNameLift(2, out float lift, out _), Is.True, "set-up: the crowned fighter takes part");
            Assert.That(lift, Is.GreaterThan(0f), "set-up: it yields to the local fighter 6 px away");
            float dy = NameOf(P, 2).transform.position.y - alone["Name"];
            Assert.That(dy, Is.EqualTo(lift).Within(1e-3f), "the name rose by its lift");
            foreach (string part in ClusterParts)
                Assert.That(Child(RootOf(P, 2), part).position.y - alone[part], Is.EqualTo(dy).Within(1e-4f), part + " rose with the name");

            // An elite yielding to a fighter takes its star and plate along.
            var elite = Actor.Mob(3, 2400f, 640f, MobTypeId.Wyrmling);
            Frame(P, Scene.Of(elite));
            string[] eliteParts = { "Name", "Elite star", "Elite star plate" };
            var eliteAlone = new Dictionary<string, float>();
            foreach (string part in eliteParts) eliteAlone[part] = Child(RootOf(P, 3), part).position.y;
            Settle(P, Scene.Of(Actor.Fighter(1, 2406f, "Mara", true), elite));
            float eliteLift = LiftOf(P, 3);
            Assert.That(eliteLift, Is.GreaterThan(0f), "set-up: the elite yields to the fighter");
            float eliteDy = NameOf(P, 3).transform.position.y - eliteAlone["Name"];
            Assert.That(eliteDy, Is.EqualTo(eliteLift).Within(1e-3f), "the elite's name rose by its lift");
            foreach (string part in eliteParts)
                Assert.That(Child(RootOf(P, 3), part).position.y - eliteAlone[part], Is.EqualTo(eliteDy).Within(1e-4f), part + " rose with the elite's name");
        }

        // ---- 7. bars ----

        [Test]
        public void OtherActorsBarsLiftANameButNeverTheLocalFightersOwn()
        {
            const float xE = 640f, xF = 1920f, xL = 3200f, y = 640f, below = 940f;
            // Each alone, far apart in one first-sight frame, with its own bar shown: no lift (a name ends above its own bar).
            Frame(P, Scene.Of(Actor.Mob(31, xE, y, MobTypeId.Wyrmling), Actor.Fighter(32, xF, "Cora"), Actor.Dummy(33, xL, y)));
            foreach (uint id in new uint[] { 31, 32, 33 }) Assert.That(BarShown(P, id), Is.True, "set-up: the bar of " + id + " is shown");
            foreach (uint id in new uint[] { 31, 32, 33 })
            {
                Assert.That(P.TryNameLift(id, out float lift, out _), Is.True, "set-up: " + id + " takes part in the layout");
                Assert.That(lift, Is.EqualTo(0f).Within(1e-5f), "the name of " + id + " clears its own bar");
            }

            // A hurt Magma Brute slid so its bar is centred on each of an elite's, a fighter's and the local fighter's name.
            Scene Targets() => Scene.Of(Actor.Mob(41, xE, y, MobTypeId.Wyrmling), Actor.Fighter(42, xF, "Cora"), Actor.Fighter(43, xL, "Lena", true));
            var brutes = new[] { Actor.Mob(51, xE, below, MobTypeId.MagmaBrute, 60f), Actor.Mob(52, xF, below, MobTypeId.MagmaBrute, 60f), Actor.Mob(53, xL, below, MobTypeId.MagmaBrute, 60f) };
            Frame(P, Scene.Join(Targets(), Scene.Of(brutes)));
            uint[] targets = { 41, 42, 43 };
            var slid = new Actor[3]; var barAt = new Rect[3];
            for (int i = 0; i < 3; i++)
            {
                var name = LineBoxOf(P, targets[i]);
                var bar = XY(SpriteOf(P, brutes[i].Id, "Health and barrier track").bounds);
                Assert.That(BarShown(P, brutes[i].Id), Is.True, "set-up: the hurt brute " + brutes[i].Id + " draws a bar");
                var px = WorldToPx(name.center - bar.center);
                slid[i] = brutes[i].Moved(px.x, px.y);
                barAt[i] = new Rect(bar.x + (name.center.x - bar.center.x), bar.y + (name.center.y - bar.center.y), bar.width, bar.height);
                Assert.That(barAt[i].Overlaps(name), Is.True, "set-up: the brute's bar, so moved, is on the name of " + targets[i]);
            }
            var localAlone = NameOf(P, 43).transform.position;
            var scene = Scene.Join(Targets(), Scene.Of(slid));
            Settle(P, scene);

            foreach (uint id in new uint[] { 41, 42 })
            {
                int i = id == 41 ? 0 : 1;
                Assert.That(P.TryNameLift(id, out float lift, out _), Is.True, "set-up: " + id + " takes part");
                Assert.That(lift, Is.GreaterThan(0f), "the brute's bar lifts the name of " + id);
                var mesh = NameOf(P, id); mesh.ForceMeshUpdate();
                var drawn = XY(mesh.GetComponent<MeshRenderer>().bounds);
                var bar = XY(SpriteOf(P, brutes[i].Id, "Health and barrier track").bounds);
                Assert.That(drawn.Overlaps(bar), Is.False, "the name of " + id + " " + drawn + " clears the bar " + bar);
            }
            Assert.That(SpriteOf(P, 41, "Elite star").transform.position.x, Is.EqualTo(NameOf(P, 41).transform.position.x).Within(1e-3f), "the elite's star is still centred on its name");
            Assert.That(P.TryNameLift(43, out float localLift, out _), Is.True, "the local fighter takes part");
            Assert.That(localLift, Is.EqualTo(0f).Within(1e-5f), "a creature's bar never lifts the owner's plate");
            Assert.That(NameOf(P, 43).transform.position.x, Is.EqualTo(localAlone.x).Within(1e-3f), "the local name stays (x)");
            Assert.That(NameOf(P, 43).transform.position.y, Is.EqualTo(localAlone.y).Within(1e-3f), "the local name stays (y)");
        }

        // ---- 12. one frame ended twice ----

        [Test]
        public void EndTwiceOnOneFrameChangesNothing()
        {
            float far = 2400f;
            var scene = Scene.Of(Gaoler.At(670f, 640f), Actor.Mob(11, 640f, 640f, MobTypeId.ChainWarden), Actor.Mob(12, 700f, 640f, MobTypeId.ChainWarden),
                Actor.Fighter(1, far, "Mara", true), Actor.Fighter(2, far + 6f, "Rowan").Marks(40, false, 1, 2, 2));
            Frame(P, scene);
            uint packCarrier = Carrier(P, 11, 12);
            Assert.That(P.TryNameLift(packCarrier, out float packLift, out _), Is.True, "set-up: the pack's name takes part");
            Assert.That(packLift, Is.GreaterThan(0f), "set-up: the pack yields to the elite");
            Assert.That(LiftOf(P, 2), Is.GreaterThan(0f), "set-up: the fighter's cluster yields to the local fighter");

            uint[] ids = { 1, 2, 11, 12, 13 };
            var lifts = new List<(bool, float, float)>();
            foreach (uint id in ids) { bool took = P.TryNameLift(id, out float l, out float t); lifts.Add((took, l, t)); }
            var rects = new List<Rect>(); P.FighterNameRects(rects);
            var positions = Positions(Arena);

            P.End(Dt);   // the same frame again: no Begin
            AssertSame(ids, lifts, rects, positions, 1e-5f, "a second End");

            scene.Reindicate(P); P.End(Dt);   // the indicators drawn again (they reset the cluster), then End on the same stamp
            AssertSame(ids, lifts, rects, positions, 1e-4f, "PresentIndicators and End again");
        }

        void AssertSame(uint[] ids, List<(bool, float, float)> lifts, List<Rect> rects, List<Vector3> positions, float tol, string what)
        {
            for (int i = 0; i < ids.Length; i++)
            {
                bool took = P.TryNameLift(ids[i], out float l, out float t);
                Assert.That(took, Is.EqualTo(lifts[i].Item1), what + ": id " + ids[i] + " takes part as before");
                Assert.That(l, Is.EqualTo(lifts[i].Item2).Within(tol), what + ": lift of " + ids[i]);
                Assert.That(t, Is.EqualTo(lifts[i].Item3).Within(tol), what + ": target of " + ids[i]);
            }
            var again = new List<Rect>(); P.FighterNameRects(again);
            Assert.That(again.Count, Is.EqualTo(rects.Count), what + ": the number of rectangles handed to the camp labels");
            for (int i = 0; i < Mathf.Min(again.Count, rects.Count); i++)
                Assert.That(AnyEquals(new List<Rect> { again[i] }, rects[i], tol), Is.True, what + ": rectangle " + i + " was " + rects[i] + " and is " + again[i]);
            var now = Positions(Arena);
            Assert.That(now.Count, Is.EqualTo(positions.Count), what + ": the same objects exist");
            for (int i = 0; i < Mathf.Min(now.Count, positions.Count); i++)
                Assert.That((now[i] - positions[i]).magnitude, Is.LessThanOrEqualTo(tol), what + ": object " + i + " moved from " + positions[i] + " to " + now[i]);
        }
    }
}
