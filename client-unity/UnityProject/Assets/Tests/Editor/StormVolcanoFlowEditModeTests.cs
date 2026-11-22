using System.Reflection;
using NUnit.Framework;
using Roguehalla.Net.Protocol;
using UnityEngine;

namespace Roguehalla.Client.Tests
{
    public sealed class StormVolcanoFlowEditModeTests
    {
        [Test]
        public void EveryOutletFlowsAwayFromTheInteriorVent()
        {
            const int size = 32;
            var tiles = new byte[size * size];
            var terrain = new byte[tiles.Length];
            var regions = new byte[tiles.Length];
            void Add(int x, int y) { int n = y * size + x; tiles[n] = MapBlob.Low; terrain[n] = (byte)TerrainKind.Lava; regions[n] = (byte)RegionId.Fire; }
            for (int x = 3; x <= 26; x++) Add(x, 16);
            for (int y = 4; y <= 16; y++) Add(14, y);
            var map = new MapBlob(size, size, tiles, terrain, regions, MapBlob.GroundTerrainAll);
            var vent = new MapAnchor((byte)RegionId.Fire, MapBlob.AnchorKindVolcano, new MapTile(14, 16), new MapRect(13, 15, 3, 3), 4, new MapTile[0]);
            typeof(MapBlob).GetProperty("Anchors").SetValue(map, new[] { vent });
            var type = typeof(RegionGround).Assembly.GetType("Roguehalla.Client.StormBoundCurrent");
            var bake = type.GetMethod("Bake", BindingFlags.Static | BindingFlags.NonPublic);
            var f = (Color[])bake.Invoke(null, new object[] { map, PlagueGround.Build(map), terrain });
            foreach (var axis in new[] { Vector2Int.left, Vector2Int.right, Vector2Int.down })
            {
                Color At(int distance) => f[(16 + axis.y * distance) * size + 14 + axis.x * distance];
                Assert.That(At(8).r - At(3).r, Is.GreaterThan(4), $"five tiles downstream in outlet {axis}");
                Assert.That(At(6).a, Is.GreaterThan(.7f));
            }
            Assert.That(f[15 * size + 20].g, Is.LessThan(f[17 * size + 20].g));
            Assert.That(f[15 * size + 20].b, Is.Zero);
        }
    }
}
