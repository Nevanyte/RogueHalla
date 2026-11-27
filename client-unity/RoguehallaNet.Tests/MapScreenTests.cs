using System;
using NUnit.Framework;
using Roguehalla.Net.Ui;

namespace Roguehalla.Net.Tests
{
    public sealed class MapProjectionTests
    {
        [Test]
        public void PixelMapsLinearlyToWorldAndBack()
        {
            Assert.IsTrue(MapProjection.PixelToWorld(135, 67.5, 270, 270, 160, 160, out var w));
            Assert.AreEqual(160 * 32 / 2.0, w.X, 1e-9);
            Assert.AreEqual(160 * 32 / 4.0, w.Y, 1e-9);
            Assert.IsTrue(MapProjection.WorldToPixel(w.X, w.Y, 270, 270, 160, 160, out var px, out var py));
            Assert.AreEqual(135, px, 1e-9);
            Assert.AreEqual(67.5, py, 1e-9);
        }

        [Test]
        public void CornersAndOutsidePointsClampToTheMap()
        {
            MapProjection.PixelToWorld(-30, -1, 270, 270, 100, 50, out var a);
            Assert.AreEqual(0, a.X); Assert.AreEqual(0, a.Y);
            MapProjection.PixelToWorld(9999, 9999, 270, 270, 100, 50, out var b);
            Assert.AreEqual(100 * 32, b.X); Assert.AreEqual(50 * 32, b.Y);
            MapProjection.PixelToWorld(270, 0, 270, 270, 100, 50, out var c);
            Assert.AreEqual(3200, c.X); Assert.AreEqual(0, c.Y);
        }

        [Test]
        public void RectangularMapsStretchEachAxisIndependently()
        {
            MapProjection.PixelToWorld(100, 25, 200, 50, 10, 4, out var p);
            Assert.AreEqual(160, p.X, 1e-9); Assert.AreEqual(64, p.Y, 1e-9);
        }

        [Test]
        public void DegenerateInputsAreRefused()
        {
            Assert.IsFalse(MapProjection.PixelToWorld(1, 1, 0, 10, 10, 10, out _));
            Assert.IsFalse(MapProjection.PixelToWorld(1, 1, 10, 10, 0, 10, out _));
            Assert.IsFalse(MapProjection.PixelToWorld(double.NaN, 1, 10, 10, 10, 10, out _));
            Assert.IsFalse(MapProjection.PixelToWorld(1, double.PositiveInfinity, 10, 10, 10, 10, out _));
            Assert.IsFalse(MapProjection.WorldToPixel(1, 1, -5, 10, 10, 10, out _, out _));
        }

        [Test]
        public void PingLivesNinetyHundredMsFadesAndGrows()
        {
            var ping = new MapPing();
            Assert.AreEqual(-1, ping.Progress(0), "inactive");
            ping.Start(10, 20, 1000);
            Assert.AreEqual(0, ping.Progress(1000), 1e-9);
            Assert.AreEqual(.5, ping.Progress(1450), 1e-9);
            Assert.AreEqual(-1, ping.Progress(1900), "expired exactly at the lifetime");
            Assert.AreEqual(0, ping.Progress(900), 1e-9, "a clock that runs backwards never makes a negative age");
            Assert.AreEqual(1, ping.Alpha(0), 1e-9); Assert.AreEqual(0, ping.Alpha(1), 1e-9);
            Assert.Less(ping.Scale(0), ping.Scale(.5)); Assert.AreEqual(1, ping.Scale(1), 1e-9);
        }
    }

    public sealed class MapZonesTests
    {
        [Test]
        public void ZoneNamesShapesAndFactionColoursFollowTheWireIds()
        {
            string[] names = { "Fire", "Haunted", "Plague", "Frost", "Holy", "Coliseum" };
            for (int r = 1; r <= 6; r++) Assert.AreEqual(names[r - 1], MapZones.Name(r));
            for (int r = 1; r <= 5; r++) Assert.AreEqual(r - 1, MapZones.FactionIndex(r));
            Assert.AreEqual(-1, MapZones.FactionIndex(MapZones.Coliseum));
            Assert.AreEqual(-1, MapZones.FactionIndex(0));
            Assert.AreEqual("", MapZones.Name(0)); Assert.AreEqual("", MapZones.Name(7));
            Assert.AreEqual(ZoneShape.None, MapZones.Shape(0));
        }

        [Test]
        public void EveryZoneHasItsOwnShapeSoColourIsNeverTheOnlyCue()
        {
            var seen = new System.Collections.Generic.HashSet<ZoneShape>();
            for (int r = 1; r <= 6; r++) Assert.IsTrue(seen.Add(MapZones.Shape(r)), "shape of region " + r + " repeats");
            Assert.IsFalse(seen.Contains(ZoneShape.None));
        }

        [Test]
        public void TintIsSubtleAndAbsentOutsideZones()
        {
            Assert.AreEqual(0f, MapZones.TintAlpha(0, true)); Assert.AreEqual(0f, MapZones.TintAlpha(9, false));
            for (int r = 1; r <= 6; r++)
            {
                Assert.That(MapZones.TintAlpha(r, true), Is.InRange(.1f, .4f), "large map tint " + r);
                Assert.LessOrEqual(MapZones.TintAlpha(r, false), MapZones.TintAlpha(r, true), "the minimap is never stronger than the Tab map");
            }
        }

        [Test]
        public void LabelTilesLandInsideTheirOwnRegionEvenForACrescent()
        {
            // Region 1 is a C shape whose centroid is in the hole; region 2 a plain block.
            int w = 12, h = 12; var grid = new byte[w * h];
            for (int y = 2; y < 10; y++) for (int x = 2; x < 10; x++) grid[y * w + x] = 1;
            for (int y = 4; y < 8; y++) for (int x = 4; x < 10; x++) grid[y * w + x] = 0;
            for (int y = 10; y < 12; y++) for (int x = 0; x < 3; x++) grid[y * w + x] = 2;
            MapZones.LabelTiles(grid, w, h, out var found, out var tx, out var ty);
            Assert.IsTrue(found[1]); Assert.IsTrue(found[2]); Assert.IsFalse(found[3]);
            Assert.AreEqual(1, grid[(int)ty[1] * w + (int)tx[1]], "crescent label is on its own tiles");
            Assert.AreEqual(2, grid[(int)ty[2] * w + (int)tx[2]]);
        }

        [Test]
        public void LabelTilesToleratesMissingOrWrongSizedPlanes()
        {
            MapZones.LabelTiles(null, 4, 4, out var f1, out _, out _);
            Assert.IsFalse(Array.Exists(f1, v => v));
            MapZones.LabelTiles(new byte[5], 4, 4, out var f2, out _, out _);
            Assert.IsFalse(Array.Exists(f2, v => v));
        }

        [Test]
        public void SmoothCoverageHugsTilesAndKeepsAOneTileWallOneTileWide()
        {
            // A one tile wide vertical wall in a 5x5 grid.
            Func<int, int, float> tile = (x, y) => x == 2 ? 1f : 0f;
            Assert.AreEqual(1f, MapSmoothing.Coverage(tile, 5, 5, 2.5, 2.5), 1e-6);
            Assert.AreEqual(0f, MapSmoothing.Coverage(tile, 5, 5, .5, 2.5), 1e-6);
            Assert.AreEqual(.5f, MapSmoothing.Coverage(tile, 5, 5, 2.0, 2.5), 1e-6, "the contour sits on the tile edge");
            Assert.AreEqual(.5f, MapSmoothing.Coverage(tile, 5, 5, 3.0, 2.5), 1e-6);
            Assert.AreEqual(1f, MapSmoothing.Inside(1f, .1f)); Assert.AreEqual(0f, MapSmoothing.Inside(0f, .1f));
            Assert.AreEqual(.5f, MapSmoothing.Inside(.5f, .1f), 1e-6);
            Assert.AreEqual(1f, MapSmoothing.Stroke(.5f, .1f, .05f), 1e-6); Assert.AreEqual(0f, MapSmoothing.Stroke(1f, .1f, .05f));
        }
    }

    public sealed class MapScreenLayoutTests
    {
        // (uiW, uiH) of the legacy stage at the four capture sizes: 1280x800, 1920x1080 (scale 1.35), 2560x1440 (1.5), plus a narrow window.
        static readonly float[][] Stages = { new[] { 1280f, 800f }, new[] { 1422f, 800f }, new[] { 1707f, 960f }, new[] { 800f, 700f }, new[] { 420f, 700f }, new[] { 2400f, 800f } };

        [Test]
        public void TabLayoutFitsTheStageWithoutOverlapAtEverySize()
        {
            foreach (var s in Stages)
            {
                var l = MapScreenLayout.Compute(s[0], s[1], MapScreenMode.Tab);
                string id = s[0] + "x" + s[1];
                foreach (var r in new[] { l.Title, l.Frame, l.Legend, l.Left, l.Right })
                    Assert.IsTrue(r.Within(s[0], s[1]), id + " rect inside the stage");
                Assert.IsFalse(l.Frame.Overlaps(l.Title), id); Assert.IsFalse(l.Frame.Overlaps(l.Legend), id);
                Assert.IsFalse(l.Left.Overlaps(l.Right), id);
                if (!l.Stacked)
                {
                    Assert.IsFalse(l.Left.Overlaps(l.Frame), id); Assert.IsFalse(l.Right.Overlaps(l.Frame), id);
                    Assert.IsFalse(l.Left.Overlaps(l.Title) || l.Right.Overlaps(l.Legend), id);
                }
            }
        }

        [Test]
        public void MapIsAnExactSquareInsideItsFrameWithNoLetterbox()
        {
            foreach (var s in Stages)
            foreach (var mode in new[] { MapScreenMode.Tab, MapScreenMode.Planning })
            {
                var l = MapScreenLayout.Compute(s[0], s[1], mode);
                Assert.AreEqual(l.Map.W, l.Map.H, .01f);
                Assert.AreEqual(l.Frame.W, l.Map.W + MapScreenLayout.FramePad * 2f, .01f, "the frame hugs the square: no empty bars beside it");
                Assert.AreEqual(l.Frame.H, l.Map.H + MapScreenLayout.FramePad * 2f, .01f);
            }
        }

        [Test]
        public void WideTabBodyIsCentredAndMapGrowsWithTheStage()
        {
            var a = MapScreenLayout.Compute(1280, 800, MapScreenMode.Tab);
            Assert.IsFalse(a.Stacked);
            float margin = a.Body.X, other = 1280 - a.Body.Right;
            Assert.AreEqual(margin, other, 1.01f, "centred horizontally");
            Assert.GreaterOrEqual(a.Body.Y, MapScreenLayout.ChromeClear, "clears the top-right chrome buttons and the phase pill");
            Assert.LessOrEqual(a.Body.Bottom, 800 - MapScreenLayout.Pad + .01f);
            var b = MapScreenLayout.Compute(1707, 960, MapScreenMode.Tab);
            Assert.Greater(b.Map.W, a.Map.W);
            Assert.LessOrEqual(b.Map.W, MapScreenLayout.MapMax);
            Assert.GreaterOrEqual(a.Map.W, MapScreenLayout.MapMin);
        }

        [Test]
        public void TabColumnsKeepLegacyWidthAndScaleWithMapTextSize()
        {
            var a = MapScreenLayout.Compute(1706, 960, MapScreenMode.Tab, 1f);
            Assert.AreEqual(250f, a.Left.W); Assert.AreEqual(250f, a.Right.W);
            var big = MapScreenLayout.Compute(1706, 960, MapScreenMode.Tab, 1.5f);
            Assert.AreEqual(290f, big.Left.W, "clamped to the legacy 190..290 range");
        }

        [Test]
        public void PlanningWideLayoutFlanksTheMapWithQuestAndDecisionColumns()
        {
            var l = MapScreenLayout.Compute(1422, 800, MapScreenMode.Planning);
            Assert.IsFalse(l.Stacked);
            Assert.AreEqual(MapScreenLayout.PlanningQuestColumn, l.Left.W);
            Assert.AreEqual(340f, l.Right.W);
            Assert.GreaterOrEqual(l.Title.Y, MapScreenLayout.ChromeClear, "clears the top-right chrome buttons");
            Assert.IsFalse(l.Left.Overlaps(l.Frame)); Assert.IsFalse(l.Right.Overlaps(l.Frame));
            Assert.IsFalse(l.Left.Overlaps(l.Right));
            Assert.IsTrue(l.Body.Within(1422, 800));
            Assert.GreaterOrEqual(l.Map.W, 260f);
        }

        [Test]
        public void PlanningQuestAndDecisionColumnsFitCommonDesktopStages()
        {
            var compact = MapScreenLayout.Compute(1280, 720, MapScreenMode.Planning);
            var large = MapScreenLayout.Compute(1440, 900, MapScreenMode.Planning);
            foreach (var layout in new[] { compact, large })
            {
                Assert.IsFalse(layout.Stacked);
                Assert.IsFalse(layout.Left.Overlaps(layout.Frame));
                Assert.IsFalse(layout.Right.Overlaps(layout.Frame));
                Assert.IsFalse(layout.Left.Overlaps(layout.Right));
            }
            Assert.IsTrue(compact.Left.Within(1280, 720)); Assert.IsTrue(compact.Right.Within(1280, 720));
            Assert.IsTrue(large.Left.Within(1440, 900)); Assert.IsTrue(large.Right.Within(1440, 900));
            Assert.AreEqual(260f, compact.Left.W); Assert.AreEqual(340f, compact.Right.W);
            Assert.AreEqual(260f, large.Left.W); Assert.AreEqual(340f, large.Right.W);
        }

        [Test]
        public void NarrowWindowsStackTheColumnsUnderTheMap()
        {
            foreach (var mode in new[] { MapScreenMode.Tab, MapScreenMode.Planning })
            {
                var l = MapScreenLayout.Compute(420, 700, mode);
                Assert.IsTrue(l.Stacked, mode.ToString());
                Assert.GreaterOrEqual(l.Right.Y, l.Legend.Bottom, "columns sit below the map strip");
                Assert.LessOrEqual(l.Frame.W, 420f - 2f * MapScreenLayout.Pad + .01f);
            }
        }

        [Test]
        public void DegenerateStagesNeverProduceNaN()
        {
            foreach (var l in new[] { MapScreenLayout.Compute(float.NaN, 0, MapScreenMode.Tab), MapScreenLayout.Compute(1, 1, MapScreenMode.Planning) })
            {
                Assert.IsFalse(float.IsNaN(l.Map.W) || float.IsNaN(l.Map.X) || float.IsNaN(l.Right.Y));
                Assert.Greater(l.Map.W, 0f);
            }
        }

        // ---- P-UI-TABMAP-SHEET-1: the Tab map is one sheet with a header ----------------------------------------------------------

        static readonly float[][] SheetStages = { new[] { 1280f, 800f }, new[] { 1422f, 800f }, new[] { 1707f, 960f }, new[] { 1280f, 960f }, new[] { 1866f, 800f }, new[] { 2400f, 800f } };

        static void AssertRect(float x, float y, float w, float h, MapRect r, string what)
        {
            Assert.AreEqual(x, r.X, .01f, what + " x"); Assert.AreEqual(y, r.Y, .01f, what + " y");
            Assert.AreEqual(w, r.W, .01f, what + " w"); Assert.AreEqual(h, r.H, .01f, what + " h");
        }

        [Test]
        public void TheTabSheetWrapsHeaderMapAndRails()
        {
            foreach (var s in SheetStages)
            {
                var l = MapScreenLayout.Compute(s[0], s[1], MapScreenMode.Tab);
                string id = s[0] + "x" + s[1];
                float pad = MapScreenLayout.SheetPad;
                Assert.IsFalse(l.Stacked, id);
                Assert.AreEqual(12f, pad, id + " SheetPad");
                Assert.IsTrue(l.Sheet.Within(s[0], s[1]), id + " the sheet is inside the stage");
                Assert.GreaterOrEqual(l.Sheet.Y, MapScreenLayout.ChromeClear - .01f, id + " the sheet starts below the chrome");
                Assert.LessOrEqual(l.Sheet.Bottom, s[1] - 12f + .01f, id + " the sheet ends 12 above the stage bottom");
                Assert.Greater(l.Sheet.W, 0f, id + " the sheet has a size");
                foreach (var r in new[] { l.Header, l.Left, l.Frame, l.Legend, l.Right })
                    Assert.IsTrue(r.X >= l.Sheet.X + pad - .01f && r.Y >= l.Sheet.Y + pad - .01f && r.Right <= l.Sheet.Right - pad + .01f && r.Bottom <= l.Sheet.Bottom - pad + .01f,
                        id + " every part keeps 12 from each sheet edge");
                Assert.AreEqual(l.Left.X, l.Header.X, .01f, id + " the header starts at the left rail");
                Assert.AreEqual(l.Right.Right, l.Header.Right, .01f, id + " the header ends at the right rail");
                Assert.AreEqual(l.Title.X, l.Header.X, .01f, id + " Title equals Header"); Assert.AreEqual(l.Title.W, l.Header.W, .01f, id);
                Assert.AreEqual(l.Title.Y, l.Header.Y, .01f, id); Assert.AreEqual(l.Title.H, l.Header.H, .01f, id);
                Assert.AreEqual(l.Header.Bottom + 12f, l.Left.Y, .01f, id + " the rails start 12 under the header");
                Assert.AreEqual(l.Left.Y, l.Frame.Y, .01f, id); Assert.AreEqual(l.Left.Y, l.Right.Y, .01f, id);
                var parts = new[] { l.Header, l.Left, l.Frame, l.Legend, l.Right };
                for (int i = 0; i < parts.Length; i++) for (int j = i + 1; j < parts.Length; j++) Assert.IsFalse(parts[i].Overlaps(parts[j]), id + " parts " + i + " and " + j + " overlap");
                AssertRect(l.Frame.X + 4f, l.Frame.Y + 4f, l.Frame.W - 8f, l.Frame.H - 8f, l.Map, id + " Map is Frame inset by 4");
            }
        }

        [Test]
        public void TheSheetNumbersAtTwoStages()
        {
            var a = MapScreenLayout.Compute(1422, 800, MapScreenMode.Tab);
            AssertRect(121, 44, 1180, 744, a.Sheet, "1422x800 Sheet"); AssertRect(133, 56, 1156, 34, a.Header, "1422x800 Header");
            AssertRect(133, 102, 250, 674, a.Left, "1422x800 Left"); AssertRect(395, 102, 632, 632, a.Frame, "1422x800 Frame");
            AssertRect(395, 746, 632, 30, a.Legend, "1422x800 Legend"); AssertRect(1039, 102, 250, 674, a.Right, "1422x800 Right");
            var b = MapScreenLayout.Compute(1280, 960, MapScreenMode.Tab);
            AssertRect(12, 86, 1256, 820, b.Sheet, "1280x960 Sheet"); AssertRect(24, 98, 1232, 34, b.Header, "1280x960 Header");
            AssertRect(24, 144, 250, 750, b.Left, "1280x960 Left"); AssertRect(286, 144, 708, 708, b.Frame, "1280x960 Frame");
            AssertRect(286, 864, 708, 30, b.Legend, "1280x960 Legend"); AssertRect(1006, 144, 250, 750, b.Right, "1280x960 Right");
        }

        [Test]
        public void StackedAndPlanningKeepTheirRects()
        {
            // Literals read from the base (lane/ui-rework/base-2) by hand from the formulas of MapScreenLayout.Tab / Planning.
            var s = MapScreenLayout.Compute(800, 700, MapScreenMode.Tab);
            Assert.IsTrue(s.Stacked);
            AssertRect(172, 44, 456, 34, s.Title, "800x700 Title"); AssertRect(172, 84, 456, 456, s.Frame, "800x700 Frame");
            AssertRect(172, 546, 456, 30, s.Legend, "800x700 Legend"); AssertRect(12, 588, 382, 100, s.Left, "800x700 Left"); AssertRect(406, 588, 382, 100, s.Right, "800x700 Right");
            AssertRect(s.Body.X, s.Body.Y, s.Body.W, s.Body.H, s.Sheet, "800x700 Sheet is Body"); AssertRect(s.Title.X, s.Title.Y, s.Title.W, s.Title.H, s.Header, "800x700 Header is Title");
            var n = MapScreenLayout.Compute(420, 700, MapScreenMode.Tab);
            Assert.IsTrue(n.Stacked);
            AssertRect(12, 44, 396, 34, n.Title, "420x700 Title"); AssertRect(12, 84, 396, 396, n.Frame, "420x700 Frame");
            AssertRect(12, 486, 396, 30, n.Legend, "420x700 Legend"); AssertRect(12, 528, 192, 160, n.Left, "420x700 Left"); AssertRect(216, 528, 192, 160, n.Right, "420x700 Right");
            AssertRect(n.Body.X, n.Body.Y, n.Body.W, n.Body.H, n.Sheet, "420x700 Sheet is Body"); AssertRect(n.Title.X, n.Title.Y, n.Title.W, n.Title.H, n.Header, "420x700 Header is Title");
            var p = MapScreenLayout.Compute(1422, 800, MapScreenMode.Planning);
            Assert.IsFalse(p.Stacked);
            AssertRect(357, 44, 628, 40, p.Title, "planning Title"); AssertRect(357, 96, 628, 628, p.Frame, "planning Frame");
            AssertRect(357, 736, 628, 52, p.Legend, "planning Legend"); AssertRect(85, 44, 260, 744, p.Left, "planning quest rail"); AssertRect(997, 44, 340, 744, p.Right, "planning Right");
            AssertRect(p.Body.X, p.Body.Y, p.Body.W, p.Body.H, p.Sheet, "planning Sheet is Body"); AssertRect(p.Title.X, p.Title.Y, p.Title.W, p.Title.H, p.Header, "planning Header is Title");
        }
    }
}
