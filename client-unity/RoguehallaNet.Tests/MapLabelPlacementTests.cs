using System;
using System.Collections.Generic;
using NUnit.Framework;
using Roguehalla.Net.Ui;

namespace Roguehalla.Net.Tests
{
    /// <summary>P-UI-ZONE-NAMES-1: where a zone's name goes on the map: six fixed spots around the icon, the first free one wins, a free
    /// current spot is kept. Pure; layer units, y up; no time. The packet (docs/backlog/packets/P-UI-ZONE-NAMES-1.md, 1.1 to 1.3) has the rules.</summary>
    public sealed class MapLabelPlacementTests
    {
        const float IconX = 100f, IconY = 100f, W = 58f, H = 14f;

        static List<MapRect> Spots(float w = W, float h = H)
        {
            var list = new List<MapRect>();
            for (int i = 0; i < MapLabelPlacement.Spots; i++) list.Add(MapLabelPlacement.Spot(i, IconX, IconY, w, h));
            return list;
        }

        static void AssertRect(MapRect r, float x, float y, float w, float h, string what)
        {
            Assert.That(r.X, Is.EqualTo(x).Within(.001f), what + " X");
            Assert.That(r.Y, Is.EqualTo(y).Within(.001f), what + " Y");
            Assert.That(r.W, Is.EqualTo(w).Within(.001f), what + " W");
            Assert.That(r.H, Is.EqualTo(h).Within(.001f), what + " H");
        }

        static readonly MapRect Over0 = new MapRect(90, 66, 20, 20);

        [Test]
        public void SpotsAreTheWorkedTable()
        {
            var spots = Spots();
            AssertRect(spots[0], 71, 69, 58, 14, "spot 0");
            AssertRect(spots[1], 71, 117, 58, 14, "spot 1");
            AssertRect(spots[2], 117, 93, 58, 14, "spot 2");
            AssertRect(spots[3], 25, 93, 58, 14, "spot 3");
            AssertRect(spots[4], 117, 73, 58, 14, "spot 4");
            AssertRect(spots[5], 25, 73, 58, 14, "spot 5");
            var tall = Spots(W, 30f);
            AssertRect(tall[0], 71, 53, 58, 30, "h 30 spot 0");
            AssertRect(tall[1], 71, 117, 58, 30, "h 30 spot 1");
            var icon = new MapRect(IconX - MapLabelPlacement.IconHalf, IconY - MapLabelPlacement.IconHalf, 26f, 26f);
            foreach (var set in new[] { spots, tall })
                for (int i = 0; i < set.Count; i++)
                {
                    var grown = new MapRect(icon.X - 4f + .01f, icon.Y - 4f + .01f, icon.W + 8f - .02f, icon.H + 8f - .02f);
                    Assert.That(grown.Overlaps(set[i]), Is.False, "spot " + i + " is 4 or more clear of the 26 x 26 icon box (h " + set[i].H + ")");
                    Assert.That(set[i].W, Is.EqualTo(58f).Within(.001f), "spot " + i + " has the name's width");
                }
            foreach (int bad in new[] { 6, -1, 99 })
            {
                var r = MapLabelPlacement.Spot(bad, IconX, IconY, W, H);
                Assert.That(r.W, Is.EqualTo(0f), "spot " + bad + " W"); Assert.That(r.H, Is.EqualTo(0f), "spot " + bad + " H");
            }
            foreach (float w in new[] { 0f, float.NaN, -3f, float.PositiveInfinity })
            {
                var r = MapLabelPlacement.Spot(0, IconX, IconY, w, H);
                Assert.That(r.W == 0f && r.H == 0f, Is.True, "w " + w + " gives an empty rect");
            }
            foreach (float h in new[] { 0f, float.NaN, -3f, float.PositiveInfinity })
            {
                var r = MapLabelPlacement.Spot(0, IconX, IconY, W, h);
                Assert.That(r.W == 0f && r.H == 0f, Is.True, "h " + h + " gives an empty rect");
            }
        }

        [Test]
        public void TheFirstFreeSpotWinsAndAFreeCurrentIsKept()
        {
            var spots = Spots();
            var none = new List<MapRect>();
            Assert.That(MapLabelPlacement.Choose(spots, none, -1), Is.EqualTo(0), "no marks: spot 0");
            var marks = new List<MapRect> { Over0 };
            Assert.That(MapLabelPlacement.Choose(spots, marks, -1), Is.EqualTo(1), "a mark over spot 0 only: spot 1");
            Assert.That(MapLabelPlacement.Choose(spots, marks, 1), Is.EqualTo(1), "current 1 is free: kept");
            Assert.That(MapLabelPlacement.Choose(spots, marks, 4), Is.EqualTo(4), "current 4 is free: kept, no hop back to the first free spot");
            Assert.That(MapLabelPlacement.Choose(spots, marks, 0), Is.EqualTo(1), "current 0 is overlapped: the first free spot");
            var two = new List<MapRect> { Over0, new MapRect(90, 114, 20, 20) };
            Assert.That(MapLabelPlacement.Choose(spots, two, -1), Is.EqualTo(2), "marks on spots 0 and 1: spot 2");
            Assert.That(MapLabelPlacement.Choose(spots, two, 1), Is.EqualTo(2), "current 1 is overlapped: spot 2");
            var touching = new List<MapRect> { new MapRect(129, 69, 10, 14) };
            Assert.That(MapLabelPlacement.Choose(spots, touching, -1), Is.EqualTo(0), "a mark that only touches spot 0 (and overlaps spot 4): spot 0");
            Assert.That(MapLabelPlacement.Choose(spots, touching, 4), Is.EqualTo(0), "current 4 is overlapped, spot 0 is free");
        }

        [Test]
        public void NoFreeSpotTakesTheSmallestOverlap()
        {
            var spots = Spots();
            var marks = new List<MapRect>();
            for (int i = 0; i < spots.Count; i++) marks.Add(new MapRect(spots[i].X + spots[i].W / 2f - 5f, spots[i].Y + spots[i].H / 2f - 5f, 10f, 10f));
            marks.Add(new MapRect(90, 70, 5, 5));
            Assert.That(MapLabelPlacement.Choose(spots, marks, -1), Is.EqualTo(1), "none yet: the lowest spot with the least overlap");
            Assert.That(MapLabelPlacement.Choose(spots, marks, 0), Is.EqualTo(1), "current 0 (125) moves to a strictly smaller overlap (100)");
            Assert.That(MapLabelPlacement.Choose(spots, marks, 3), Is.EqualTo(3), "current 3 (100) is not worse than the best: kept");
            Assert.That(MapLabelPlacement.Choose(spots, marks, 2), Is.EqualTo(2), "current 2 (100) is kept");
        }

        [Test]
        public void BadInputsAreSafe()
        {
            var spots = Spots();
            Assert.That(MapLabelPlacement.Choose(new List<MapRect>(), new List<MapRect>(), -1), Is.EqualTo(-1), "no spots: -1");
            var none = new List<MapRect>();
            Assert.That(MapLabelPlacement.Choose(spots, none, 9), Is.EqualTo(0), "current above the range means none yet");
            Assert.That(MapLabelPlacement.Choose(spots, none, -5), Is.EqualTo(0), "current below the range means none yet");
            var junk = new List<MapRect>
            {
                new MapRect(float.NaN, 66, 20, 20), new MapRect(90, float.PositiveInfinity, 20, 20), new MapRect(90, 66, 0, 20),
                new MapRect(90, 66, 20, -4), new MapRect(90, 66, float.NaN, 20), new MapRect(90, 66, 20, float.PositiveInfinity), Over0,
            };
            Assert.That(MapLabelPlacement.Choose(spots, junk, -1), Is.EqualTo(1), "invalid marks are ignored, the valid one over spot 0 counts");
            var wrongFirst = new List<MapRect> { new MapRect(float.NaN, 0, 10, 10), MapLabelPlacement.Spot(1, IconX, IconY, W, H) };
            Assert.That(MapLabelPlacement.Choose(wrongFirst, none, -1), Is.EqualTo(1), "an invalid spot is never returned");
            var allBad = new List<MapRect> { new MapRect(0, 0, 0, 0), new MapRect(float.NaN, 0, 10, 10) };
            Assert.That(MapLabelPlacement.Choose(allBad, none, -1), Is.EqualTo(-1), "no valid spot: -1");
            var into = new List<MapRect>();
            MapLabelPlacement.AddBox(into, float.NaN, 0, 4, 2); MapLabelPlacement.AddBox(into, 0, 0, 0, 2); MapLabelPlacement.AddBox(into, 0, 0, 4, -1);
            MapLabelPlacement.AddBox(into, 0, float.PositiveInfinity, 4, 2);
            MapLabelPlacement.AddText(into, float.NaN, 0, 4, 2, 0); MapLabelPlacement.AddText(into, 0, 0, float.NaN, 2, 1); MapLabelPlacement.AddText(into, 0, 0, 4, 0, -1);
            MapLabelPlacement.AddEdges(into, 0, 0, float.NaN, 50); MapLabelPlacement.AddEdges(into, 0, 0, 100, float.PositiveInfinity); MapLabelPlacement.AddEdges(into, 0, 0, -1, 50);
            Assert.That(into.Count, Is.EqualTo(0), "builders add nothing for a size that is not finite and above 0");
            Assert.That(MapLabelPlacement.Choose(spots, new List<MapRect> { Over0 }, -1), Is.EqualTo(1), "set-up: a valid mark still works after the bad ones");
        }

        [Test]
        public void TheBuildersAddTheRightBoxes()
        {
            var into = new List<MapRect>();
            MapLabelPlacement.AddBox(into, 0, 0, 4, 2);
            Assert.That(into.Count, Is.EqualTo(1), "AddBox adds one box");
            AssertRect(into[0], -2, -1, 4, 2, "AddBox(0, 0, 4, 2)");
            for (int align = -1; align <= 1; align++) MapLabelPlacement.AddText(into, 10f, 5f, 16.5f, 12f, align);
            Assert.That(into.Count, Is.EqualTo(4), "AddText adds one box per call");
            AssertRect(into[1], 10f, -1f, 16.5f, 12f, "align -1: left edge at x");
            AssertRect(into[2], 1.75f, -1f, 16.5f, 12f, "align 0: centred on x");
            AssertRect(into[3], -6.5f, -1f, 16.5f, 12f, "align 1: right edge at x");
            MapLabelPlacement.AddText(into, 10f, 5f, 0f, 12f, 0);
            Assert.That(into.Count, Is.EqualTo(4), "a width of 0 adds nothing");
            var edges = new List<MapRect>();
            MapLabelPlacement.AddEdges(edges, 0, 0, 100, 50);
            Assert.That(edges.Count, Is.EqualTo(4), "four slabs");
            var map = new MapRect(0, 0, 100, 50);
            foreach (var slab in edges) Assert.That(slab.Overlaps(map), Is.False, "a slab is outside the map");
            var probes = new[] { new MapRect(-1, 10, 2, 10), new MapRect(99, 10, 2, 10), new MapRect(10, -1, 10, 2), new MapRect(10, 49, 10, 2) };
            var names = new[] { "left", "right", "bottom", "top" };
            for (int p = 0; p < probes.Length; p++)
            {
                int hit = 0;
                foreach (var slab in edges) if (slab.Overlaps(probes[p])) hit++;
                Assert.That(hit, Is.EqualTo(1), "the " + names[p] + " probe overlaps exactly one slab");
            }
            var inside = new MapRect(10, 10, 10, 10); int inHits = 0;
            foreach (var slab in edges) if (slab.Overlaps(inside)) inHits++;
            Assert.That(inHits, Is.EqualTo(0), "a probe inside the map overlaps none");
            var empty = new List<MapRect>();
            MapLabelPlacement.AddEdges(empty, 0, 0, 0, 50); MapLabelPlacement.AddEdges(empty, 0, 0, 100, 0);
            Assert.That(empty.Count, Is.EqualTo(0), "a width or height of 0 adds nothing");
        }

        static MapRect sinkRect;
        static int sinkInt;

        [Test]
        public void ChoosingAllocatesNothing()
        {
            AssertRect(MapLabelPlacement.Spot(0, 100, 100, 58, 14), 71, 69, 58, 14, "set-up: Spot(0, 100, 100, 58, 14)");
            var spots = Spots();
            var marks = new List<MapRect>(64) { Over0 };
            Assert.That(MapLabelPlacement.Choose(spots, marks, -1), Is.EqualTo(1), "set-up: Choose over the six real spots with test 2's mark");
            for (int i = 0; i < 39; i++) marks.Add(new MapRect(300f + i * 25f, 300f, 20f, 20f));
            Assert.That(MapLabelPlacement.Choose(spots, marks, -1), Is.EqualTo(1), "set-up: far marks change nothing");
            Assert.That(marks.Count, Is.EqualTo(40));
            var into = new List<MapRect>(16);
            Assert.That(MapLabelPlacement.Spot(0, 100, 100, 58, 14).W, Is.EqualTo(58f), "set-up: Spot returns the rect to be measured");
            Assert.That(AllocationProbe.PerCall(i => { sinkRect = MapLabelPlacement.Spot(i % 6, 100f, 100f, 58f, 14f); }), Is.EqualTo(0.0), "Spot");
            Assert.That(AllocationProbe.PerCall(i => { sinkInt = MapLabelPlacement.Choose(spots, marks, i % 6 - 1); }), Is.EqualTo(0.0), "Choose");
            Assert.That(AllocationProbe.PerCall(i => { into.Clear(); MapLabelPlacement.AddBox(into, 1f, 2f, 3f, 4f); }), Is.EqualTo(0.0), "AddBox");
            Assert.That(AllocationProbe.PerCall(i => { into.Clear(); MapLabelPlacement.AddText(into, 1f, 2f, 3f, 4f, i % 3 - 1); }), Is.EqualTo(0.0), "AddText");
            Assert.That(AllocationProbe.PerCall(i => { into.Clear(); MapLabelPlacement.AddEdges(into, 0f, 0f, 100f, 50f); }), Is.EqualTo(0.0), "AddEdges");
            Assert.That(sinkInt, Is.GreaterThanOrEqualTo(0).Or.EqualTo(-1), "the measured work ran");
        }
    }
}
