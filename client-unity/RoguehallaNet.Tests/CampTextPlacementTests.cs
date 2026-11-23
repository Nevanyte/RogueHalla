using System;
using System.Collections.Generic;
using NUnit.Framework;
using Roguehalla.Net.Ui;

namespace Roguehalla.Net.Tests
{
    /// <summary>P-UI-TABMAP-TEXT-1: where a camp's text block goes on the Tab map: Right, Below, Above, Left of the camp, the first place inside
    /// the map (inset 2) that meets no obstacle and no block placed earlier; elites first. Pure; layer units, y up; no time. The packet
    /// (docs/backlog/packets/P-UI-TABMAP-TEXT-1.md, rules 2 to 4 and 8) has the rules and the worked numbers.</summary>
    public sealed class CampTextPlacementTests
    {
        const float LeftGap = 38.1f, BelowGap = 24f, W = 15.02f, LineH = 12.14f;
        static readonly MapRect Bounds = new MapRect(0f, 0f, 480f, 480f);
        const int Sentinel = -7;

        static CampTextBlock Block(float x, float y, int lines = 1, bool elite = false, float w = W, float lineH = LineH)
        {
            return new CampTextBlock { X = x, Y = y, W = w, LineH = lineH, Lines = lines, Elite = elite };
        }

        static MapRect Square(float cx, float cy, float side) { return new MapRect(cx - side / 2f, cy - side / 2f, side, side); }

        static void Run(IReadOnlyList<CampTextBlock> blocks, IReadOnlyList<MapRect> obstacles, MapRect bounds, out int[] places, out MapRect[] rects,
            float leftGap = LeftGap, float belowGap = BelowGap)
        {
            places = new int[blocks.Count]; rects = new MapRect[blocks.Count];
            for (int i = 0; i < places.Length; i++) { places[i] = Sentinel; rects[i] = new MapRect(Sentinel, Sentinel, Sentinel, Sentinel); }
            CampTextPlacement.Place(blocks, obstacles, bounds, leftGap, belowGap, places, rects);
        }

        static void AssertRect(MapRect r, float x, float y, float w, float h, string what)
        {
            Assert.That(r.X, Is.EqualTo(x).Within(.001f), what + " X");
            Assert.That(r.Y, Is.EqualTo(y).Within(.001f), what + " Y");
            Assert.That(r.W, Is.EqualTo(w).Within(.001f), what + " W");
            Assert.That(r.H, Is.EqualTo(h).Within(.001f), what + " H");
        }

        static void AssertSameRect(MapRect a, MapRect b, string what)
        {
            Assert.That(a.X, Is.EqualTo(b.X).Within(.0001f), what + " X");
            Assert.That(a.Y, Is.EqualTo(b.Y).Within(.0001f), what + " Y");
            Assert.That(a.W, Is.EqualTo(b.W).Within(.0001f), what + " W");
            Assert.That(a.H, Is.EqualTo(b.H).Within(.0001f), what + " H");
        }

        static float OverlapW(MapRect a, MapRect b) { return Math.Min(a.Right, b.Right) - Math.Max(a.X, b.X); }
        static float OverlapH(MapRect a, MapRect b) { return Math.Min(a.Bottom, b.Bottom) - Math.Max(a.Y, b.Y); }
        static bool Meets(MapRect a, MapRect b) { return OverlapW(a, b) > .001f && OverlapH(a, b) > .001f; }

        static void AssertInside(MapRect r, string what)
        {
            Assert.That(r.X, Is.GreaterThanOrEqualTo(2f - .001f), what + " left");
            Assert.That(r.Y, Is.GreaterThanOrEqualTo(2f - .001f), what + " bottom");
            Assert.That(r.Right, Is.LessThanOrEqualTo(478f + .001f), what + " right");
            Assert.That(r.Bottom, Is.LessThanOrEqualTo(478f + .001f), what + " top");
        }

        static int PlaceOne(CampTextBlock block, params MapRect[] obstacles)
        {
            Run(new[] { block }, obstacles, Bounds, out var places, out var rects);
            Assert.That(places[0], Is.Not.EqualTo(CampTextPlacement.None), "a block with a line has a place");
            Assert.That(places[0], Is.Not.EqualTo(Sentinel), "the place was written");
            return places[0];
        }

        // ---- 1. the four places -------------------------------------------------------------------------------------------------------

        [Test]
        public void AWorkedBlockHasTheFourPlaces()
        {
            var b = Block(240f, 240f);
            var right = CampTextPlacement.RectOf(CampTextPlacement.Right, b, LeftGap, BelowGap);
            var below = CampTextPlacement.RectOf(CampTextPlacement.Below, b, LeftGap, BelowGap);
            var above = CampTextPlacement.RectOf(CampTextPlacement.Above, b, LeftGap, BelowGap);
            var left = CampTextPlacement.RectOf(CampTextPlacement.Left, b, LeftGap, BelowGap);
            Assert.That(right.W, Is.GreaterThan(0f), "Right is not the empty rect");
            AssertRect(right, 261.2f, 235.93f, 15.02f, 12.14f, "Right");
            AssertRect(below, 232.49f, 203.86f, 15.02f, 12.14f, "Below");
            AssertRect(above, 232.49f, 261f, 15.02f, 12.14f, "Above");
            AssertRect(left, 186.88f, 235.93f, 15.02f, 12.14f, "Left");
            AssertRect(CampTextPlacement.RectOf(CampTextPlacement.Below, b, LeftGap, 27.1f), 232.49f, 200.76f, 15.02f, 12.14f, "Below, belowGap 27.1");

            var two = Block(240f, 240f, 2);
            AssertRect(CampTextPlacement.RectOf(CampTextPlacement.Right, two, LeftGap, BelowGap), 261.2f, 235.93f, 15.02f, 26.54f, "Lines 2 Right");
            AssertRect(CampTextPlacement.RectOf(CampTextPlacement.Below, two, LeftGap, BelowGap), 232.49f, 189.46f, 15.02f, 26.54f, "Lines 2 Below");
            AssertRect(CampTextPlacement.RectOf(CampTextPlacement.Above, two, LeftGap, BelowGap), 232.49f, 261f, 15.02f, 26.54f, "Lines 2 Above");
            AssertRect(CampTextPlacement.RectOf(CampTextPlacement.Left, two, LeftGap, BelowGap), 186.88f, 235.93f, 15.02f, 26.54f, "Lines 2 Left");

            foreach (int bad in new[] { -1, 4 })
                AssertRect(CampTextPlacement.RectOf(bad, b, LeftGap, BelowGap), 0f, 0f, 0f, 0f, "place " + bad);
        }

        // ---- 2. edges -----------------------------------------------------------------------------------------------------------------

        [Test]
        public void ABlockNearAnEdgeEndsInsideTheMap()
        {
            var cases = new[]
            {
                new { X = 470f, Y = 240f, Place = CampTextPlacement.Below },
                new { X = 240f, Y = 470f, Place = CampTextPlacement.Below },
                new { X = 470f, Y = 470f, Place = CampTextPlacement.Below },
                new { X = 10f, Y = 470f, Place = CampTextPlacement.Below },
                new { X = 10f, Y = 10f, Place = CampTextPlacement.Right },
                new { X = 470f, Y = 10f, Place = CampTextPlacement.Above },
                new { X = 10f, Y = 240f, Place = CampTextPlacement.Right },
            };
            foreach (var c in cases)
            {
                var one = new[] { Block(c.X, c.Y) };
                Run(one, new MapRect[0], Bounds, out var places, out var rects);
                Assert.That(places[0], Is.Not.EqualTo(CampTextPlacement.None), "(" + c.X + ", " + c.Y + ") has a place");
                Assert.That(places[0], Is.EqualTo(c.Place), "(" + c.X + ", " + c.Y + ") place");
                AssertSameRect(rects[0], CampTextPlacement.RectOf(c.Place, one[0], LeftGap, BelowGap), "(" + c.X + ", " + c.Y + ") rect is its place's");
                AssertInside(rects[0], "(" + c.X + ", " + c.Y + ")");
            }
            // the Above place of (470, 10) is taken: Left is the next
            var corner = Block(470f, 10f);
            var above = CampTextPlacement.RectOf(CampTextPlacement.Above, corner, LeftGap, BelowGap);
            var obstacle = new MapRect(460f, 30f, 18f, 20f);
            Assert.That(Meets(above, obstacle), Is.True, "set-up: the obstacle is on the Above place");
            Run(new[] { corner }, new[] { obstacle }, Bounds, out var p2, out var r2);
            Assert.That(p2[0], Is.Not.EqualTo(CampTextPlacement.None), "the corner camp has a place");
            Assert.That(p2[0], Is.EqualTo(CampTextPlacement.Left), "the corner camp goes Left");
            AssertInside(r2[0], "Left of the corner camp");
        }

        // ---- 3. two near camps ----------------------------------------------------------------------------------------------------------

        [Test]
        public void TwoNearCampsGetBlocksThatDoNotOverlap()
        {
            var blocks = new[] { Block(100f, 100f), Block(100f, 108f) };
            var plates = new[] { Square(100f, 100f, 20.4f), Square(100f, 108f, 20.4f) };
            var secondRight = CampTextPlacement.RectOf(CampTextPlacement.Right, blocks[1], LeftGap, BelowGap);
            var firstRight = CampTextPlacement.RectOf(CampTextPlacement.Right, blocks[0], LeftGap, BelowGap);
            Assert.That(OverlapH(firstRight, secondRight), Is.EqualTo(4.14f).Within(.001f), "set-up: the second's Right overlaps the first's by 4.14 high");
            Run(blocks, plates, Bounds, out var places, out var rects);
            Assert.That(places[0], Is.Not.EqualTo(CampTextPlacement.None), "first has a place");
            Assert.That(places[1], Is.Not.EqualTo(CampTextPlacement.None), "second has a place");
            Assert.That(places[0], Is.EqualTo(CampTextPlacement.Right), "first Right");
            Assert.That(places[1], Is.EqualTo(CampTextPlacement.Below), "second Below");
            Assert.That(Meets(rects[0], rects[1]), Is.False, "the two blocks do not overlap");
            for (int i = 0; i < 2; i++)
                foreach (var plate in plates)
                    Assert.That(Meets(rects[i], plate), Is.False, "block " + i + " meets no plate");
        }

        // ---- 4. elites first ----------------------------------------------------------------------------------------------------------

        [Test]
        public void AnEliteIsPlacedBeforeItsNeighbours()
        {
            Run(new[] { Block(100f, 100f), Block(100f, 104f, 1, true) }, new MapRect[0], Bounds, out var places, out var rects);
            Assert.That(places[0], Is.Not.EqualTo(CampTextPlacement.None), "A has a place");
            Assert.That(places[1], Is.Not.EqualTo(CampTextPlacement.None), "B has a place");
            Assert.That(places[1], Is.EqualTo(CampTextPlacement.Right), "the elite B (index 1) is placed first: Right");
            Assert.That(places[0], Is.EqualTo(CampTextPlacement.Below), "the plain A (index 0) gives way: Below");
            Assert.That(Meets(rects[0], rects[1]), Is.False, "the two blocks do not overlap");

            Run(new[] { Block(100f, 100f), Block(100f, 104f) }, new MapRect[0], Bounds, out var p2, out var r2);
            Assert.That(p2[0], Is.Not.EqualTo(CampTextPlacement.None), "plain A has a place");
            Assert.That(p2[0], Is.EqualTo(CampTextPlacement.Right), "both plain: A (index order) Right");
            Assert.That(p2[1], Is.EqualTo(CampTextPlacement.Below), "both plain: B Below");
        }

        // ---- 5. the tag travels with the amount -----------------------------------------------------------------------------------------

        [Test]
        public void TheTagTravelsWithTheAmount()
        {
            var obstacle = new MapRect(261f, 249f, 20f, 10f);
            var one = Block(240f, 240f, 1);
            var rightOne = CampTextPlacement.RectOf(CampTextPlacement.Right, one, LeftGap, BelowGap);
            Assert.That(rightOne.Bottom, Is.EqualTo(248.07f).Within(.001f), "set-up: one line's Right top is 248.07");
            Assert.That(PlaceOne(one, obstacle), Is.EqualTo(CampTextPlacement.Right), "one line fits Right under the obstacle");

            var two = Block(240f, 240f, 2);
            var rightTwo = CampTextPlacement.RectOf(CampTextPlacement.Right, two, LeftGap, BelowGap);
            Assert.That(rightTwo.Bottom, Is.EqualTo(262.47f).Within(.001f), "set-up: two lines' Right top is 262.47");
            Assert.That(PlaceOne(two, obstacle), Is.EqualTo(CampTextPlacement.Below), "the tag makes the block meet the obstacle: Below, tag and amount together");
        }

        // ---- 6. no free place ---------------------------------------------------------------------------------------------------------

        [Test]
        public void NoFreePlaceTakesTheLeastOverlapAndIsRepeatable()
        {
            var b = Block(240f, 240f);
            var rs = new MapRect[4];
            for (int p = 0; p < 4; p++) rs[p] = CampTextPlacement.RectOf(p, b, LeftGap, BelowGap);
            for (int p = 0; p < 4; p++) for (int q = p + 1; q < 4; q++) Assert.That(Meets(rs[p], rs[q]), Is.False, "set-up: the places " + p + " and " + q + " are disjoint");
            var half = new MapRect(rs[3].X, rs[3].Y, rs[3].W / 2f, rs[3].H);
            var obstacles = new[] { rs[0], rs[1], rs[2], half };
            Run(new[] { b }, obstacles, Bounds, out var places, out var rects);
            Assert.That(places[0], Is.Not.EqualTo(CampTextPlacement.None), "a place is chosen");
            Assert.That(places[0], Is.EqualTo(CampTextPlacement.Left), "Left is covered by half as much (91.17 against 182.34)");
            AssertInside(rects[0], "the least-overlap place");
            Run(new[] { b }, obstacles, Bounds, out var again, out var rects2);
            Assert.That(again[0], Is.EqualTo(places[0]), "a second call gives the same place");
            AssertSameRect(rects2[0], rects[0], "a second call gives the same rect");

            Run(new[] { b }, new[] { rs[0], rs[1], rs[2], rs[3] }, Bounds, out var tie, out var tieRects);
            Assert.That(tie[0], Is.Not.EqualTo(CampTextPlacement.None), "a place is chosen when all four are whole");
            Assert.That(tie[0], Is.EqualTo(CampTextPlacement.Right), "a tie goes to the earlier place");
            AssertInside(tieRects[0], "the tie's place");
            Run(new[] { b }, new[] { rs[0], rs[1], rs[2], rs[3] }, Bounds, out var tie2, out var tieRects2);
            Assert.That(tie2[0], Is.EqualTo(tie[0]), "a repeat of the tie is the same");
            AssertSameRect(tieRects2[0], tieRects[0], "a repeat of the tie is the same rect");
        }

        // ---- 7. obstacle order ----------------------------------------------------------------------------------------------------------

        [Test]
        public void ObstacleOrderChangesNothing()
        {
            var blocks = new List<CampTextBlock>(); var obstacles = new List<MapRect>();
            for (int r = 0; r < 5; r++)
                for (int c = 0; c < 8; c++)
                {
                    float x = 40f + 27f * c, y = 40f + 27f * r;
                    blocks.Add(Block(x, y));
                    obstacles.Add(Square(x, y, 20.4f));
                    if ((c + r) % 2 == 0) obstacles.Add(Square(x - 13.2f, y + 2f, 13.6f));
                }
            Assert.That(blocks.Count, Is.EqualTo(40), "set-up: 40 camps");
            Assert.That(obstacles.Count, Is.EqualTo(60), "set-up: 40 plates and 20 ward rings");
            Run(blocks, obstacles, Bounds, out var places, out var rects);
            var seen = new HashSet<int>();
            for (int i = 0; i < places.Length; i++)
            {
                Assert.That(places[i], Is.Not.EqualTo(CampTextPlacement.None), "camp " + i + " has a place");
                seen.Add(places[i]);
                // the last column has nothing to its right: Right is free. The others are jammed (the next plate is 27 away); what they take is
                // the least-overlap fallback, which the order test below holds equal in any obstacle order
                if (i % 8 == 7) Assert.That(places[i], Is.EqualTo(CampTextPlacement.Right), "camp " + i + " (last column) is Right");
                AssertSameRect(rects[i], CampTextPlacement.RectOf(places[i], blocks[i], LeftGap, BelowGap), "camp " + i + " rect is its place's");
            }
            Assert.That(seen.Count, Is.GreaterThanOrEqualTo(2), "two different places occur");

            var reversed = new List<MapRect>(obstacles); reversed.Reverse();
            var rotated = new List<MapRect>(); for (int i = 0; i < obstacles.Count; i++) rotated.Add(obstacles[(i + 17) % obstacles.Count]);
            foreach (var order in new[] { reversed, rotated })
            {
                Run(blocks, order, Bounds, out var p2, out var r2);
                for (int i = 0; i < places.Length; i++)
                {
                    Assert.That(p2[i], Is.EqualTo(places[i]), "camp " + i + " place with the obstacles in another order");
                    AssertSameRect(r2[i], rects[i], "camp " + i + " rect with the obstacles in another order");
                }
            }
        }

        // ---- 8. bad inputs --------------------------------------------------------------------------------------------------------------

        [Test]
        public void UnmeasuredAndBadInputsAreSafe()
        {
            var blocks = new[]
            {
                Block(100f, 100f, 1, false, 0f, LineH),
                Block(150f, 100f, 1, false, float.NaN, LineH),
                Block(200f, 100f, 0),
                Block(250f, 100f, 1, false, W, 0f),
                Block(300f, 100f, 1, false, W, float.PositiveInfinity),
            };
            Run(blocks, new MapRect[0], Bounds, out var places, out var rects);
            for (int i = 0; i < blocks.Length; i++)
            {
                if (blocks[i].Lines == 0)
                {
                    Assert.That(places[i], Is.EqualTo(CampTextPlacement.None), "Lines 0 stays None");
                    AssertRect(rects[i], 0f, 0f, 0f, 0f, "Lines 0 zero rect");
                    continue;
                }
                Assert.That(places[i], Is.EqualTo(CampTextPlacement.Right), "unmeasured block " + i + " is Right");
                AssertRect(rects[i], blocks[i].X + 21.2f, blocks[i].Y + 2f, 0f, 0f, "unmeasured block " + i + " zero rect");
            }

            // an unmeasured block does not take room from a measured one beside it
            Run(new[] { Block(100f, 100f, 1, false, 0f, LineH), Block(100f, 100f) }, new MapRect[0], Bounds, out var p2, out var r2);
            Assert.That(p2[1], Is.EqualTo(CampTextPlacement.Right), "a zero rect is no obstacle for later blocks");

            foreach (var gaps in new[] { new[] { 0f, BelowGap }, new[] { LeftGap, 0f } })
            {
                Run(new[] { Block(100f, 100f) }, new MapRect[0], Bounds, out var p3, out var r3, gaps[0], gaps[1]);
                Assert.That(p3[0], Is.EqualTo(CampTextPlacement.Right), "a gap of " + gaps[0] + " / " + gaps[1] + " makes the block unmeasured: Right");
                AssertRect(r3[0], 121.2f, 102f, 0f, 0f, "a bad gap gives the zero rect");
            }

            var good = new[] { Block(100f, 100f), Block(300f, 300f) };
            foreach (var zero in new[] { new MapRect(0f, 0f, 0f, 0f), new MapRect(float.NaN, 0f, 480f, 480f), new MapRect(0f, 0f, -5f, 480f) })
            {
                Run(good, new MapRect[0], zero, out var p4, out var r4);
                for (int i = 0; i < good.Length; i++)
                {
                    Assert.That(p4[i], Is.EqualTo(CampTextPlacement.Right), "a layer not laid out: block " + i + " is Right");
                    AssertSameRect(r4[i], CampTextPlacement.RectOf(CampTextPlacement.Right, good[i], LeftGap, BelowGap), "a layer not laid out: block " + i + " is its Right place");
                }
            }

            // buffers: null, one null, shorter than the blocks (the least of the three lengths is written)
            Assert.DoesNotThrow(() => CampTextPlacement.Place(good, new MapRect[0], Bounds, LeftGap, BelowGap, null, null), "both buffers null");
            Assert.DoesNotThrow(() => CampTextPlacement.Place(good, null, Bounds, LeftGap, BelowGap, new int[2], null), "rects null, obstacles null");
            Assert.DoesNotThrow(() => CampTextPlacement.Place(null, null, Bounds, LeftGap, BelowGap, new int[2], new MapRect[2]), "blocks null");
            var shortPlaces = new[] { Sentinel, Sentinel, Sentinel, Sentinel }; var shortRects = new MapRect[3];
            for (int i = 0; i < 3; i++) shortRects[i] = new MapRect(Sentinel, Sentinel, Sentinel, Sentinel);
            var four = new[] { Block(100f, 100f), Block(200f, 100f), Block(300f, 100f), Block(400f, 100f) };
            Assert.DoesNotThrow(() => CampTextPlacement.Place(four, new MapRect[0], Bounds, LeftGap, BelowGap, shortPlaces, shortRects), "short buffers");
            Assert.That(shortPlaces[0], Is.Not.EqualTo(Sentinel), "the first place is written");
            Assert.That(shortPlaces[2], Is.Not.EqualTo(Sentinel), "the third place is written (the shortest buffer has 3)");
            Assert.That(shortPlaces[3], Is.EqualTo(Sentinel), "the fourth place is not written: the rects buffer has 3");
        }

        // ---- 9. no garbage --------------------------------------------------------------------------------------------------------------

        [Test]
        public void PlacingAllocatesNothing()
        {
            var blocks = new List<CampTextBlock>(); var obstacles = new List<MapRect>();
            for (int i = 0; i < 40; i++)
            {
                float x = 40f + 27f * (i % 8), y = 40f + 27f * (i / 8);
                blocks.Add(Block(x, y, 1 + i % 2, i % 5 == 0));
                obstacles.Add(Square(x, y, 20.4f));
            }
            for (int i = 0; i < 160; i++) obstacles.Add(Square(10f + 3f * i, 5f + 2.9f * i, 6f));
            var places = new int[40]; var rects = new MapRect[40];
            for (int i = 0; i < 40; i++) places[i] = Sentinel;
            CampTextPlacement.Place(blocks, obstacles, Bounds, LeftGap, BelowGap, places, rects);
            for (int i = 0; i < 40; i++) Assert.That(places[i], Is.Not.EqualTo(CampTextPlacement.None), "block " + i + " has a place");
            Assert.That(obstacles.Count, Is.EqualTo(200), "set-up: 200 obstacles");
            double bytes = AllocationProbe.PerCall(i => CampTextPlacement.Place(blocks, obstacles, Bounds, LeftGap, BelowGap, places, rects), 2000);
            Assert.That(bytes, Is.EqualTo(0.0), "40 blocks and 200 obstacles: bytes per call");
        }
    }
}
