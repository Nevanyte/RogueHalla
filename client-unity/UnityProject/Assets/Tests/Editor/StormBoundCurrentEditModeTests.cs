using System.Reflection;
using NUnit.Framework;
using Roguehalla.Net.Protocol;
using UnityEngine;

namespace Roguehalla.Client.Tests
{
    public sealed class StormBoundCurrentEditModeTests
    {
        const int Width = 24, Height = 20;
        readonly byte[] _terrain = new byte[Width * Height];
        readonly byte[] _tiles = new byte[Width * Height];
        readonly byte[] _regions = new byte[Width * Height];

        [SetUp]
        public void Reset()
        {
            System.Array.Clear(_terrain, 0, _terrain.Length);
            System.Array.Clear(_tiles, 0, _tiles.Length);
            for (int i = 0; i < _regions.Length; i++) _regions[i] = (byte)RegionId.Fire;
        }

        [Test]
        public void FlowFollowsAnLBendAndEndsInAQuietPool()
        {
            for (int x = 3; x <= 12; x++) Lava(x, 4);
            for (int y = 4; y <= 12; y++) Lava(12, y);
            for (int y = 12; y <= 17; y++)
            for (int x = 9; x <= 15; x++) Lava(x, y);

            var field = Bake();
            Color At(int x, int y) => field[y * Width + x];
            Assert.That(At(7, 4).r, Is.GreaterThan(At(4, 4).r + 2f));
            Assert.That(At(12, 9).r, Is.GreaterThan(At(12, 5).r + 2f));
            Assert.That(At(12, 9).a, Is.GreaterThan(.6f));
            Assert.That(At(12, 14).a, Is.LessThan(.45f));
            Assert.That(At(12, 14).b, Is.GreaterThan(At(12, 9).b));
            Assert.That(Bake(), Is.EqualTo(field), "repeated builds pick the same path and projection");
        }

        [Test]
        public void DryIslandAndCornerOnlyNeighbourNeverBecomeAConnectedStream()
        {
            for (int x = 3; x <= 10; x++) Lava(x, 4);
            for (int x = 11; x <= 18; x++) Lava(x, 5);
            Lava(8, 8);
            _tiles[8 * Width + 8] = MapBlob.Wall;
            _terrain[7 * Width + 8] = (byte)TerrainKind.Bog;
            var field = Bake();
            Color At(int x, int y) => field[y * Width + x];
            Assert.That(At(10, 4).r, Is.GreaterThan(At(11, 5).r + 3f), "corner does not bridge the two ranges");
            Assert.That(At(10, 5).b, Is.Zero, "guard texels carry no shore depth");
            Assert.That(At(10, 5).a, Is.Zero);
            Assert.That(At(8, 8).a, Is.Zero, "walls reject even a lava code");
            Assert.That(At(8, 7).a, Is.Zero, "bog cannot feed the flow field");
        }

        void Lava(int x, int y)
        {
            int i = y * Width + x;
            _tiles[i] = MapBlob.Low;
            _terrain[i] = (byte)TerrainKind.Lava;
        }

        Color[] Bake()
        {
            var map = new MapBlob(Width, Height, _tiles, _terrain, _regions, MapBlob.GroundTerrainAll);
            var type = typeof(RegionGround).Assembly.GetType("Roguehalla.Client.StormBoundCurrent");
            Assert.That(type, Is.Not.Null, "painted streams need a connected map-space coordinate field");
            var bake = type.GetMethod("Bake", BindingFlags.Static | BindingFlags.NonPublic);
            return (Color[])bake.Invoke(null, new object[] { map, PlagueGround.Build(map), _terrain });
        }
    }
}
