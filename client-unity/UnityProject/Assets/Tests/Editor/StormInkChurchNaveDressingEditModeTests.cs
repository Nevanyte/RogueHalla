using System.Linq;
using NUnit.Framework;
using Roguehalla.Net.Protocol;
using Roguehalla.Net.Ui;
using UnityEngine;

namespace Roguehalla.Client.Tests
{
    public sealed class StormInkChurchNaveDressingEditModeTests
    {
        const int Size = 29;

        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        [TestCase(3)]
        [TestCase(4)]
        [TestCase(5)]
        [TestCase(6)]
        [TestCase(7)]
        public void OutdoorDressingClearsOnlyTheSignedNaveWhileRealConcealmentStays(int pose)
        {
            var map = Fixture(pose, true, out var expected);
            var placed = RegionProps.Scatter(map);
            Assert.That(placed.Where(p => p.Shipped).Select(p => p.Art), Is.EqualTo(expected.Select(p => DecorationKinds.ArtKey(p.Kind, p.Variant, p.Flags))));
            Assert.That(placed.Single(p => !p.Shipped).Art, Is.EqualTo("plague.bush"));
            for (int i = 0; i < 4; i++)
            {
                var wire = expected[i];
                Assert.That(placed[i].X, Is.EqualTo(wire.X / 32f));
                Assert.That(placed[i].Y, Is.EqualTo(wire.Y / 32f));
            }

            var parent = new GameObject("Signed church decor");
            RegionProps scene = null;
            try
            {
                scene = RegionProps.Build(parent.transform, map);
                var names = parent.GetComponentsInChildren<SpriteRenderer>().Select(r => r.name);
                Assert.That(names, Is.EquivalentTo(placed.Select(p => p.Art)));
            }
            finally
            {
                scene?.Dispose();
                Object.DestroyImmediate(parent);
            }
        }

        [Test]
        public void UndeliveredChurchPreservesOrdinaryDressing()
        {
            var map = Fixture(0, false, out _);
            Assert.That(RegionProps.Scatter(map).Count(p => p.Shipped), Is.EqualTo(map.Props.Length));
        }

        static MapBlob Fixture(int pose, bool hasChurch, out MapProp[] expected)
        {
            var tiles = new byte[Size * Size];
            var bushes = new byte[tiles.Length];
            MapTile At(int x, int y)
            {
                if ((pose & 1) != 0) (x, y) = (y, x);
                if ((pose & 2) != 0) x = Size - 1 - x;
                if ((pose & 4) != 0) y = Size - 1 - y;
                return new MapTile((ushort)x, (ushort)y);
            }
            void Ring(int x0, int y0, int x1, int y1, bool doors)
            {
                for (int y = y0; y <= y1; y++)
                for (int x = x0; x <= x1; x++)
                {
                    if (x != x0 && x != x1 && y != y0 && y != y1) continue;
                    if (doors && x >= 8 && x <= 10) continue;
                    var tile = At(x, y);
                    tiles[tile.Y * Size + tile.X] = MapBlob.Wall;
                }
            }
            MapProp Prop(byte kind, int x, int y, byte flags = 0)
            {
                var tile = At(x, y);
                return new MapProp(kind, (ushort)(tile.X * 32 + 16), (ushort)(tile.Y * 32 + 16), 0, flags);
            }

            Ring(3, 3, 15, 20, true);
            Ring(20, 6, 25, 12, false);
            var bush = At(8, 10);
            bushes[bush.Y * Size + bush.X] = 1;
            var props = new[]
            {
                Prop(15, 8, 7), Prop(16, 9, 10), Prop(17, 10, 13),
                Prop(15, 17, 13), Prop(16, 22, 9), Prop(17, 9, 20),
                Prop(16, 8, 11, MapBlob.PropFlagLegacy),
            };
            var anchors = hasChurch
                ? new[] { new MapAnchor(2, MapBlob.AnchorKindChurch, At(8, 12), new Roguehalla.Net.Protocol.MapRect(0, 0, Size, Size), 0,
                    new[] { At(8, 28), At(9, 28), At(10, 28) }) }
                : null;
            MapProp[] Ordered(System.Collections.Generic.IEnumerable<MapProp> rows) =>
                rows.OrderBy(p => p.Y).ThenBy(p => p.X).ThenBy(p => p.Kind).ToArray();
            expected = Ordered(props.Skip(3));
            return new MapBlob(Size, Size, tiles).WithPublic(null, null, bushes, Ordered(props), null, null, anchors);
        }
    }
}
