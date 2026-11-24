using System;
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
    /// P-UI-WORLD-NAMES-3, the carry and ground half. (1) A pack whose layout key changes (its mobs leave their camp's reach, or the lowest-serial
    /// member goes) keeps the lift of its name for the hold, copied from the key the pack's lowest-serial member had; a creature that leaves a
    /// pack and stands alone starts at lift 0, as on the base. (2) A name's colours read on the ground at the place the name is DRAWN (the base
    /// plus its lift), also while the lift climbs. The rig is <see cref="WorldNamesRig"/>: the picture of a Gaoler beside two Chain Wardens.
    /// EditMode time is frozen, so a creature's remembered last position never moves: a moved creature keeps its camp, only the camps change it.
    /// </summary>
    public sealed class WorldNamesCarryEditModeTests
    {
        Stage _stage;
        UiTheme _savedTheme; bool? _savedReduced; bool _savedHidden; Func<float> _savedClock; bool _savedContinuity;

        SpriteActorPresenter P { get { return _stage.P; } }
        static readonly int HoldFrames = Mathf.FloorToInt(LabelLift.HoldSeconds * 60f) - 3;

        [SetUp]
        public void SetUp()
        {
            _savedTheme = UiTheme.Current; _savedReduced = UiMotion.ReducedOverride; _savedHidden = WorldLabelVisibility.Hidden;
            _savedClock = SpriteActorPresenter.FadeClock; _savedContinuity = SpriteActorPresenter.ContinuityEnabled;
            UiTheme.Current = UiTheme.Skin();
            ShowcaseSprites.Initialize();
            WorldLabelVisibility.Hidden = false; UiMotion.ReducedOverride = false; SpriteActorPresenter.FadeClock = null;
            _stage = new Stage("world names carry arena");
        }

        [TearDown]
        public void TearDown()
        {
            try { if (_stage != null) _stage.Dispose(); }
            finally
            {
                UiMotion.ReducedOverride = _savedReduced; WorldLabelVisibility.Hidden = _savedHidden;
                SpriteActorPresenter.FadeClock = _savedClock; SpriteActorPresenter.ContinuityEnabled = _savedContinuity;
                UiTheme.Current = _savedTheme;
            }
        }

        static List<CampView> Rows() { return new List<CampView> { new CampView { MobType = MobTypeId.ChainWarden } }; }

        // The carrier's lift and target on every one of the next frames equal t (1e-3).
        void AssertHeld(Scene scene, uint carrier, float t, string what)
        {
            for (int n = 1; n <= HoldFrames; n++)
            {
                Frame(P, scene);
                var (lift, target) = LiftAndTarget(P, carrier);
                Assert.That(lift, Is.EqualTo(t).Within(1e-3f), what + ": the lift, frame " + n);
                Assert.That(target, Is.EqualTo(t).Within(1e-3f), what + ": the target, frame " + n);
            }
        }

        // ---- 1 ----

        [Test]
        public void APackThatLeavesItsCampsReachKeepsItsLift()
        {
            var rows = Rows();
            P.SetCamps(new[] { new MapCamp(660, 640, 96, 1) }, rows);
            var pic = MeasurePicture(P, OverheadBoxLayout.PadEnter / 4f);
            Settle(P, pic.All);
            float t = LiftOf(P, pic.Carrier);
            Assert.That(t, Is.GreaterThan(0f), "set-up: the pack's name is lifted over the Gaoler's");

            P.SetCamps(new[] { new MapCamp(9000, 9000, 96, 1) }, rows);   // no camp in reach: Camp 0, the key becomes the lowest serial
            var apart = Scene.Join(Scene.Of(Gaoler), pic.Slid.Moved(1000f, 0f));   // clear of the Gaoler's name; the wardens still make a pack of two
            AssertHeld(apart, pic.Carrier, t, "after the key changed");
        }

        // ---- 2 ----

        [Test]
        public void ThePacksNewKeyTakesTheLowestSerialMembersLift()
        {
            var pic = MeasurePicture(P, OverheadBoxLayout.PadEnter / 4f);
            var stray = Scene.Of(Actor.Mob(14, 3000f, 640f, MobTypeId.ChainWarden));   // presented after 12, so its serial is higher; its lift is 0
            Settle(P, Scene.Join(pic.All, stray));
            float t = LiftOf(P, pic.Carrier);
            Assert.That(t, Is.GreaterThan(0f), "set-up: the pack's name is lifted over the Gaoler's");
            Assert.That(LiftOf(P, 14), Is.EqualTo(0f).Within(1e-5f), "set-up: the stray's own name has no lift");

            // 11 leaves: the pack is {12, 14}, its key the serial of 12, a key neither old pack held. Its one source is 12's old key.
            var next = Scene.Of(Gaoler, Actor.Mob(12, 3000f, 640f, MobTypeId.ChainWarden), Actor.Mob(14, 3060f, 640f, MobTypeId.ChainWarden));
            AssertHeld(next, 12, t, "the new pack {12, 14}");
        }

        // ---- 3: a standing guard (green on the base) ----

        [Test]
        public void AStrayThatStandsAloneStartsAtLiftZero()
        {
            var pic = MeasurePicture(P, OverheadBoxLayout.PadEnter / 4f);
            Settle(P, pic.All);
            Assert.That(LiftOf(P, pic.Carrier), Is.GreaterThan(0f), "set-up: the pack's name is lifted over the Gaoler's");
            var alone = Scene.Of(pic.Slid.Actors.Find(a => a.Id == 12));   // 11 and the Gaoler gone: 12's pack is itself
            for (int n = 1; n <= 6; n++)
            {
                Frame(P, alone);
                var (lift, target) = LiftAndTarget(P, 12);
                Assert.That(lift, Is.EqualTo(0f).Within(1e-4f), "a creature alone is not carried a pack's lift, frame " + n);
                Assert.That(target, Is.EqualTo(0f).Within(1e-4f), "nor its target, frame " + n);
            }
        }

        // ---- 4: the ground colour is the colour at the drawn place ----

        static SurfaceMap RowMap(Func<int, SurfaceKind> kindOfRow)
        {
            const int w = 40, h = 40;
            var tiles = new byte[w * h]; var regions = new byte[w * h]; var terrain = new byte[w * h];
            for (int y = 0; y < h; y++) for (int x = 0; x < w; x++)
            {
                bool border = x == 0 || y == 0 || x == w - 1 || y == h - 1;
                var kind = kindOfRow(y);
                tiles[y * w + x] = border ? MapBlob.Wall : MapBlob.Floor;
                regions[y * w + x] = (byte)(border ? 0 : RegionOf(kind));
                terrain[y * w + x] = (byte)TerrainKind.None;
            }
            var rm = new RegionMap(); rm.Load(new MapBlob(w, h, tiles, terrain, regions, MapBlob.GroundTerrainAll));
            return new SurfaceMap(rm);
        }

        static RegionId RegionOf(SurfaceKind k) { return k == SurfaceKind.Basalt ? RegionId.Fire : RegionId.Frost; }

        static Color FaceOf(TextMeshPro t)
        {
            var m = t.fontSharedMaterial; var f = m.HasProperty(ShaderUtilities.ID_FaceColor) ? m.GetColor(ShaderUtilities.ID_FaceColor) : Color.white;
            return new Color(t.color.r * f.r, t.color.g * f.g, t.color.b * f.b, t.color.a * f.a);
        }

        static double LightFace { get { return NamePalette.Luminance(NamePalette.Colours(NameKind.Mob, false).Face); } }
        static double DarkFace { get { return NamePalette.Luminance(NamePalette.Colours(NameKind.Mob, true).Face); } }

        static string Which(Color face)
        {
            double l = NameLegibleFloors.Luminance(face);
            return Math.Abs(l - LightFace) < Math.Abs(l - DarkFace) ? "light" : "dark";
        }

        // 'A': rows from the base's row down are Basalt, those above Snow; 'B': the inverse; 'C': all Snow, the base in row 1; 'D': all Snow, the base in row 0.
        [TestCase("A")]
        [TestCase("B")]
        [TestCase("C")]
        [TestCase("D")]
        public void TheGroundColourIsTheColourAtTheDrawnPlace(string which)
        {
            // Measure on a throwaway stage with no Surfaces: the carrier's name at lift 0 stands at row r (a tile row is 32 legacy px in y).
            Picture pic; double r; float nameX;
            using (var measure = new Stage("world names carry measure"))
            {
                pic = MeasurePicture(measure.P, OverheadBoxLayout.PadEnter / 4f);
                Frame(measure.P, pic.Slid);
                var at = NameOf(measure.P, pic.Carrier).transform.position;
                r = -at.y / ArenaView.GroundSquash; nameX = at.x;
            }
            double frac = r - Math.Floor(r);
            double newRow = which == "A" || which == "B" ? r + ((.22 - frac + 1.0) % 1.0) : which == "C" ? 1.22 : .22;
            float dy = (float)((newRow - r) * 32.0);   // legacy px
            int baseRow = (int)Math.Floor(newRow);
            if (which == "A" || which == "B")
            {
                Assert.That(baseRow, Is.InRange(6, 33), "set-up: the base's row");
                Assert.That(Mathf.FloorToInt(nameX), Is.InRange(2, 37), "set-up: the base's column");
            }
            else Assert.That(baseRow, Is.EqualTo(which == "C" ? 1 : 0), "set-up: the name's row at lift 0");

            Func<int, SurfaceKind> rows =
                which == "A" ? (Func<int, SurfaceKind>)(y => y >= baseRow ? SurfaceKind.Basalt : SurfaceKind.Snow)
                : which == "B" ? (y => y >= baseRow ? SurfaceKind.Snow : SurfaceKind.Basalt)
                : (y => SurfaceKind.Snow);
            P.Surfaces = RowMap(rows);

            // The reference: the carrier alone at lift 0, on the ground of its base.
            Frame(P, pic.Slid.Moved(0f, dy));
            var carrierName = NameOf(P, pic.Carrier);
            Assert.That(LiftOf(P, pic.Carrier), Is.EqualTo(0f).Within(1e-5f), "set-up: alone, the name has no lift");
            double rowNow = Math.Floor(-carrierName.transform.position.y / ArenaView.GroundSquash);
            Assert.That(rowNow, Is.EqualTo(baseRow), "set-up: the name's row at lift 0");
            var reference = FaceOf(carrierName);
            string refWhich = which == "A" || which == "D" ? "light" : "dark";   // Basalt and the dark ground of None: light face; Snow: dark face
            Assert.That(Which(reference), Is.EqualTo(refWhich), "set-up: the reference face on the ground of the base");
            string otherWhich = refWhich == "light" ? "dark" : "light";
            string drawnWhich = which == "D" ? "light" : otherWhich;   // the face once the drawn place is on the other ground (D: never leaves the border)

            float edge = (float)(.22 * ArenaView.GroundSquash);   // the lift at which the drawn place crosses into the row above
            float lift = 0f; int before = 0, after = 0;
            var all = pic.All.Moved(0f, dy);
            for (int n = 1; n <= FrameBound; n++)
            {
                Frame(P, all);
                lift = LiftOf(P, pic.Carrier);
                var face = FaceOf(NameOf(P, pic.Carrier));
                if (which != "D" && Mathf.Abs(lift - edge) < 1e-3f) continue;
                bool past = which == "D" || lift > edge;
                if (past) after++; else before++;
                Assert.That(Which(face), Is.EqualTo(past ? drawnWhich : refWhich), "case " + which + ", frame " + n + ": the face at lift " + lift.ToString("F4") + " (the edge is at " + edge.ToString("F4") + ") is the " + (past ? drawnWhich : refWhich) + " one");
                if (!past) Assert.That(NameLegibleFloors.Luminance(face), Is.EqualTo(NameLegibleFloors.Luminance(reference)).Within(1e-3), "before the edge the face is the reference's, frame " + n);
                if (which == "A") if (past) Assert.That(NameLegibleFloors.Luminance(face), Is.LessThan(NameLegibleFloors.Luminance(reference)), "A: the lifted face is darker than the reference, frame " + n);
                if (which == "B" || which == "C") if (past) Assert.That(NameLegibleFloors.Luminance(face), Is.GreaterThan(NameLegibleFloors.Luminance(reference)), which + ": the lifted face is lighter than the reference, frame " + n);
            }
            Assert.That(lift, Is.GreaterThan(.5f), "set-up: the settled lift");
            if (which != "D")
            {
                Assert.That(before, Is.GreaterThan(0), "set-up: at least one climbing frame short of the edge");
                Assert.That(after, Is.GreaterThan(0), "at least one frame past the edge");
            }
        }
    }
}
