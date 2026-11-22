using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Roguehalla.Net.Protocol;
using UnityEngine;

namespace Roguehalla.Client.Tests
{
    public sealed class ColiseumParapetEditModeTests
    {
        [TestCase(true)]
        [TestCase(false)]
        public void ImportedStripContainsPaintAcrossEverySection(bool coping)
        {
            var atlas = RegionWallAtlas.Atlas;
            var set = RegionWallAtlas.Parapet;
            var sections = coping ? set.Top : set.Face;
            for (int i = 0; i < 3; i++)
            {
                var cell = sections[i];
                float darkest = 1f, palest = 0f;
                for (int y = 1; y < 16; y++)
                for (int x = 1; x < 16; x++)
                {
                    var uv = new Vector2(Mathf.Lerp(cell.Min.x, cell.Max.x, x / 16f),
                        Mathf.Lerp(cell.Min.y, cell.Max.y, y / 16f));
                    float pigment = atlas.GetPixel((int)(uv.x * atlas.width), (int)(uv.y * atlas.height)).grayscale;
                    darkest = Mathf.Min(darkest, pigment);
                    palest = Mathf.Max(palest, pigment);
                }
                Assert.That(palest - darkest, Is.GreaterThan(.07f), $"No visible stone washes in section {i}");
            }
        }

        [Test]
        public void PaintedCapsFollowActualArenaWallsAndTurnWithTheRun()
        {
            const int w = 14, h = 12;
            var tiles = new byte[w * h];
            var regions = Enumerable.Repeat((byte)RegionId.Coliseum, w * h).ToArray();
            foreach (var p in new[] { new Vector2Int(2, 3), new Vector2Int(3, 3),
                new Vector2Int(4, 3), new Vector2Int(4, 4), new Vector2Int(4, 5),
                new Vector2Int(8, 7), new Vector2Int(0, 0) }) tiles[p.y * w + p.x] = MapBlob.Wall;
            tiles[7 * w + 7] = MapBlob.Low;
            var map = new MapBlob(w, h, tiles, null, regions, MapBlob.GroundTerrainMap)
                .WithPublic(null, new MapColiseum(1, 1, 10, 9, new MapGate[0]), null, null, null);
            var property = typeof(RegionWallAtlas).GetProperty("Parapet");
            Assert.That(property, Is.Not.Null, "The flat grey arena grid still uses the generic wall painting");
            var set = (RegionWallAtlas.Set)property.GetValue(null);
            var root = new GameObject("Arena wall mesh oracle");
            try
            {
                var paintedCells = new HashSet<Vector2Int>();
                var isolated = new HashSet<Vector2>();
                bool across = false, receding = false;
                using (var walls = RegionWalls.Build(root.transform, map, 1f))
                {
                    foreach (var filter in root.GetComponentsInChildren<MeshFilter>())
                    {
                        var mesh = filter.sharedMesh;
                        var uv = mesh.uv;
                        var points = mesh.vertices;
                        for (int i = 0; i < uv.Length; i += 4)
                        {
                            Vector2 center = (uv[i] + uv[i + 2]) * .5f;
                            if (!set.Top.Take(RegionWallAtlas.TopVariants).Any(c =>
                                center.x > c.Min.x && center.x < c.Max.x && center.y > c.Min.y && center.y < c.Max.y)) continue;
                            Vector3 middle = (points[i] + points[i + 2]) * .5f;
                            int x = Mathf.FloorToInt(middle.x);
                            int y = Mathf.FloorToInt((RegionWalls.WallHeight - middle.y) / ArenaView.GroundSquash);
                            Assert.That(tiles[y * w + x], Is.EqualTo(MapBlob.Wall));
                            Assert.That(x, Is.GreaterThan(0), "The arena finish must stop at the delivered footprint");
                            if (x == 3 && y == 3) across |= Mathf.Abs(uv[i + 1].x - uv[i].x) > .001f;
                            if (x == 4 && y == 4) receding |= Mathf.Abs(uv[i + 1].y - uv[i].y) > .001f;
                            Assert.That(filter.GetComponent<MeshRenderer>().sortingOrder, Is.EqualTo(2000 + y * 16));
                            paintedCells.Add(new Vector2Int(x, y));
                            if (x == 8 && y == 7)
                                for (int j = 0; j < 4; j++) isolated.Add(points[i + j]);
                        }
                    }
                    Assert.That(paintedCells.Count, Is.EqualTo(6));
                    Assert.That(isolated.Count, Is.GreaterThan(4), "An exposed block needs a chipped silhouette");
                    Assert.That(isolated.Min(v => v.x), Is.EqualTo(8f).Within(.01f));
                    Assert.That(isolated.Max(v => v.x), Is.EqualTo(9f).Within(.01f));
                    Assert.That(across && receding, Is.True, "Coping must rotate on receding runs");
                    Assert.That(root.GetComponentsInChildren<Collider2D>(), Is.Empty);
                }
            }
            finally { Object.DestroyImmediate(root); }
        }
    }
}
