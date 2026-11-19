using System.Reflection;
using NUnit.Framework;
using Roguehalla.Net.Protocol;
using UnityEngine;

namespace Roguehalla.Client.Tests
{
    public sealed class StormMarshTributaryEditModeTests
    {
        [Test]
        public void AThirdNarrowMarshReachHasCurrentWhileAnIsolatedPocketStaysQuiet()
        {
            const int size = 32;
            var variant = new byte[size * size];
            var regions = new byte[variant.Length];
            var terrain = new byte[variant.Length];
            var tiles = new byte[variant.Length];
            for (int i = 0; i < variant.Length; i++)
            {
                regions[i] = (byte)RegionId.Plague;
                tiles[i] = MapBlob.Wall;
            }
            void Wet(int x, int y) { int i = y * size + x; variant[i] = 7; tiles[i] = MapBlob.Floor; }
            for (int x = 4; x <= 28; x++) Wet(x, 18);
            for (int y = 4; y <= 18; y++) Wet(16, y);
            Wet(4, 5); Wet(5, 5);
            var map = new MapBlob(size, size, tiles, terrain, regions, MapBlob.GroundTerrainAll);
            // Bake consumes an already classified surface. The fixture has no cypress
            // blueprint, so it supplies the same classification at that input boundary.
            var marsh = PlagueGround.Build(map);
            for (int i = 0; i < variant.Length; i++)
                if (variant[i] == 7) marsh.Kind[i] = PlagueGround.Water;
            var type = typeof(RegionGround).Assembly.GetType("Roguehalla.Client.StormBoundCurrent");
            var bake = type.GetMethod("Bake", BindingFlags.Static | BindingFlags.NonPublic);
            var field = (Color[])bake.Invoke(null,new object[] { map,marsh,terrain });
            foreach (var axis in new[] { Vector2Int.left, Vector2Int.right, Vector2Int.down })
            {
                int index = (18 + axis.y * 7) * size + 16 + axis.x * 7;
                Assert.That(field[index].a,Is.GreaterThan(.75f),$"connected branch {axis}");
            }
            Assert.That(field[5 * size + 4].a, Is.Zero);
        }
    }
}
