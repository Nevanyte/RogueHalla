using System.Reflection;
using NUnit.Framework;
using Roguehalla.Net.Protocol;
using UnityEngine;

namespace Roguehalla.Client.Tests
{
    public sealed class WallBusterTerrainEditModeTests
    {
        [Test]
        public void ArenaReplacesWallMeshesWhenTwoObstructionsAreOpened()
        {
            var root = new GameObject("WallBuster test");
            try
            {
                var arena = root.AddComponent<ArenaView>();
                typeof(ArenaView).GetMethod("Awake", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(arena, null);
                var tiles = new byte[] { 0, 1, 2, 0, 1, 0, 0, 0, 0 };
                var surfaces = new byte[] { 0, 0, 2, 0, 0, 0, 0, 0, 0 };
                var regions = new byte[] { 2, 2, 2, 2, 2, 2, 2, 2, 2 };
                var map = new MapBlob(3, 3, tiles, surfaces, regions, MapBlob.GroundTerrainMap);
                var wallsField = typeof(ArenaView).GetField("_regionWalls", BindingFlags.Instance | BindingFlags.NonPublic);
                arena.Build(map);
                var before = (RegionWalls)wallsField.GetValue(arena);
                Assert.That(before.TileCount, Is.EqualTo(3));
                var opened = map.WithDestroyedTerrain(new uint[] { 1, 2 });
                arena.Build(opened);
                var after = (RegionWalls)wallsField.GetValue(arena);
                Assert.That(after, Is.Not.SameAs(before));
                Assert.That(after.TileCount, Is.EqualTo(1));
                Assert.That(opened.Terrain[2], Is.Zero);
                arena.Build(opened);
                Assert.That(wallsField.GetValue(arena), Is.SameAs(after));
            }
            finally { Object.DestroyImmediate(root); }
        }
    }
}
