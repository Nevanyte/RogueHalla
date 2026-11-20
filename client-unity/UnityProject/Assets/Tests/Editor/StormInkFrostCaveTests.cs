using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Roguehalla.Editor;
using Roguehalla.Net.Protocol;
using UnityEngine;

namespace Roguehalla.Client.Tests
{
    /// <summary>Source-driven oracle for the delivered Frost cave roof, mouth, and actor fade in all eight blueprint poses.</summary>
    public sealed class StormInkFrostCaveTests
    {
        static IEnumerable<int> Orientations() => Enumerable.Range(0, 8);

        [TestCaseSource(nameof(Orientations))]
        public void DeliveredIceRoofFollowsTheSignedFootprintAndKeepsEveryMouthOpen(int pose)
        {
            var map = CaveMap(pose, true, out var anchor);
            var root = new GameObject("Frost cave roof test");
            try
            {
                using var view = LandmarkView.Build(root.transform, map);
                Assert.That(view.Count, Is.EqualTo(1));
                var cave = view.Get(0);
                Assert.That(cave.Anchor, Is.SameAs(anchor));
                Assert.That(cave.Anchor.Zone, Is.EqualTo((byte)RegionId.Frost));
                Assert.That(cave.ArchTiles.Length, Is.EqualTo(3));
                var roof = cave.RoofRenderers.Single();
                Assert.That(roof.name, Is.EqualTo("Frost painted ice roof"));
                Assert.That(roof.sortingOrder, Is.EqualTo(cave.RoofOrder));
                Assert.That(roof.sprite.texture.name, Is.EqualTo("Frost ice roof and portal"));
                var asset = Resources.Load<Texture2D>("StormInk/Frost/IceRoof");
                Assert.That(asset, Is.Not.Null);
                Assert.That(asset.isReadable, Is.True);
                Assert.That(roof.sprite.texture.width, Is.EqualTo(asset.width));
                Assert.That(roof.sprite.texture.height, Is.EqualTo(asset.height));
                Assert.That(roof.sprite.texture.GetPixel(asset.width / 2, asset.height / 2),
                    Is.EqualTo(asset.GetPixel(asset.width / 2, asset.height / 2)), "painted pixels come from the delivered IceRoof resource");
                if (pose == 0) AssertPaintedWallCapsFollowRoofAlpha(cave, asset);

                // The artist's silhouette remains visible in the chamber, while all delivered threshold tiles are cut clear.
                var middle = Feet(anchor.Centre.X, anchor.Centre.Y);
                Assert.That(SampleAlpha(roof, middle), Is.GreaterThan(.5f), "the painted roof stays continuous over the inner chamber");
                foreach (var tile in cave.ArchTiles)
                {
                    Assert.That(map.At(tile.X, tile.Y), Is.EqualTo(MapBlob.Floor), "signed entrance stays walkable");
                    Assert.That(SampleAlpha(roof, Feet(tile.X, tile.Y)), Is.LessThan(.06f), "paint clears every delivered doorway tile");
                }

                // The normal actor path drives fade and restores opaque sorting without changing the footprint.
                view.NoteActor(Feet(1, 1));
                view.Tick(1f);
                Assert.That(roof.color.a, Is.EqualTo(LandmarkView.RoofOpaqueAlpha).Within(.001f), "the exterior approach does not fade the cave roof");
                view.NoteActor(middle);
                view.Tick(1f);
                Assert.That(roof.color.a, Is.EqualTo(LandmarkView.RoofFadedAlpha).Within(.001f), "the same roof reveals a fighter inside the chamber");
                view.NoteOtherActor(middle);
                view.Tick(1f);
                Assert.That(roof.color.a, Is.EqualTo(LandmarkView.RoofPeekAlpha).Within(.001f), "other fighters remain visible beneath the roof wash");
                view.NoteActor(Feet(1, 1));
                view.Tick(1f);
                Assert.That(roof.color.a, Is.EqualTo(LandmarkView.RoofOpaqueAlpha).Within(.001f));
                Assert.That(roof.sortingOrder, Is.EqualTo(cave.RoofOrder));
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }

        [Test]
        public void AnUndeliveredCaveKeepsItsExistingProceduralRoof()
        {
            var map = CaveMap(0, false, out var anchor);
            var root = new GameObject("Frost cave fallback test");
            try
            {
                using var view = LandmarkView.Build(root.transform, map, new[] { anchor });
                Assert.That(view.Count, Is.EqualTo(1));
                var cave = view.Get(0);
                Assert.That(cave.RoofRenderers.Length, Is.GreaterThan(0));
                Assert.That(cave.RoofRenderers.Any(r => r.name == "Frost painted ice roof"), Is.False,
                    "art replacement requires an anchor carried by the validated map");
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }

        [Test]
        public void PaintedCaveReplacesOnlyItsWallRenderQuads()
        {
            var map = CaveMap(0, true, out _);
            var baseline = new MapBlob(map.W, map.H, (byte[])map.Tiles.Clone(), (byte[])map.Terrain.Clone(),
                (byte[])map.Regions.Clone(), map.TerrainMode);
            var paintedRoot = new GameObject("painted cave wall caster test");
            var baselineRoot = new GameObject("fallback cave wall caster test");
            RegionWalls painted = null, fallback = null;
            try
            {
                painted = RegionWalls.Build(paintedRoot.transform, map, 1f);
                fallback = RegionWalls.Build(baselineRoot.transform, baseline, 1f);
                int paintedVertices = paintedRoot.GetComponentsInChildren<MeshFilter>(true).Sum(m => m.sharedMesh.vertexCount);
                int fallbackVertices = baselineRoot.GetComponentsInChildren<MeshFilter>(true).Sum(m => m.sharedMesh.vertexCount);
                Assert.That(painted.TileCount, Is.EqualTo(fallback.TileCount), "render replacement keeps tile accounting");
                Assert.That(paintedVertices, Is.LessThan(fallbackVertices), "only the painted signed cave omits its tile wall quads");
            }
            finally
            {
                painted?.Dispose(); fallback?.Dispose();
                UnityEngine.Object.DestroyImmediate(paintedRoot);
                UnityEngine.Object.DestroyImmediate(baselineRoot);
            }
        }

        [Test]
        public void PaintedCaveOmitsOnlyItsWallTileSunCasters()
        {
            var map = CaveMap(0, true, out _);
            var fallback = new MapBlob(map.W, map.H, (byte[])map.Tiles.Clone(), (byte[])map.Terrain.Clone(),
                (byte[])map.Regions.Clone(), map.TerrainMode);
            var paintedField = new RegionField(); paintedField.Build(map.Regions, map.W, map.H);
            var fallbackField = new RegionField(); fallbackField.Build(fallback.Regions, fallback.W, fallback.H);
            var painted = new RegionShadowMap(map, paintedField);
            var unpainted = new RegionShadowMap(fallback, fallbackField);
            try
            {
                Assert.That(painted.CasterCount, Is.GreaterThan(0));
                Assert.That(painted.CasterCount, Is.LessThan(unpainted.CasterCount),
                    "the painted IceRoof replaces its rectangular wall tile shadows while unrelated casters remain");
            }
            finally { painted.Dispose(); unpainted.Dispose(); }
        }

        static float SampleAlpha(SpriteRenderer renderer, Vector3 world)
        {
            var bounds = renderer.bounds;
            float u = Mathf.InverseLerp(bounds.min.x, bounds.max.x, world.x);
            float v = Mathf.InverseLerp(bounds.min.y, bounds.max.y, world.y);
            return renderer.sprite.texture.GetPixelBilinear(u, v).a;
        }

        static void AssertPaintedWallCapsFollowRoofAlpha(Landmark cave, Texture2D asset)
        {
            const int ppu = 12;
            const float face = .67f, over = .05f, lip = 1.25f;
            bool checkedTransparentEdge = false;
            foreach (var row in cave.Root.GetComponentsInChildren<SpriteRenderer>(true))
            {
                const string prefix = "Cave rock row ";
                if (!row.name.StartsWith(prefix, StringComparison.Ordinal) ||
                    !int.TryParse(row.name.Substring(prefix.Length), out int mapRow)) continue;
                var tex = row.sprite.texture;
                var output = tex.GetPixels32();
                for (int j = 0; j < tex.height && !checkedTransparentEdge; j++)
                for (int i = 0; i < tex.width; i++)
                {
                    float wx = -.5f + (i + .5f) / ppu;
                    float py = -(cave.FootY + mapRow + 1) * ArenaView.GroundSquash;
                    float wy = py - over + (j + .5f) / ppu;
                    if (wy < py + face) continue;
                    float v = mapRow + 1f - (wy - py - face) / ArenaView.GroundSquash;
                    float u = (wx + lip) / (cave.FootW + 2f * lip);
                    float roofV = (cave.FootH + lip - v) / (cave.FootH + 2f * lip);
                    if (u < 0f || u > 1f || roofV < 0f || roofV > 1f || asset.GetPixelBilinear(u, roofV).a > .015f) continue;
                    Assert.That(output[j * tex.width + i].a, Is.LessThan(5), "the painted roof's transparent edge masks the rectangular procedural wall cap");
                    checkedTransparentEdge = true;
                    break;
                }
            }
            Assert.That(checkedTransparentEdge, Is.True, "signed cave wall caps overlap the irregular transparent IceRoof edge");
        }

        static Vector3 Feet(int x, int y) => new Vector3(x + .5f, -(y + .5f) * ArenaView.GroundSquash, 0f);

        // Begin with the actual signed cave scene (loaded from docs/landmarks/blueprints/cave.txt and validated through MapBlob.WithPublic),
        // then place its complete tile/entrance/anchor data in one of the generator's eight mirrored/transposed poses.
        static MapBlob CaveMap(int pose, bool delivered, out MapAnchor anchor)
        {
            var source = LookCapture.BuildMap("cave-wide");
            var original = source.Anchors.Single(a => a.Kind == MapBlob.AnchorKindCave);
            int ox = original.Footprint.X, oy = original.Footprint.Y, size = original.Footprint.W;
            var tiles = (byte[])source.Tiles.Clone();
            var terrain = (byte[])source.Terrain.Clone();
            var regions = (byte[])source.Regions.Clone();
            for (int y = 0; y < size; y++) for (int x = 0; x < size; x++)
            {
                int i = (oy + y) * source.W + ox + x;
                tiles[i] = MapBlob.Floor; terrain[i] = 0; regions[i] = (byte)RegionId.Frost;
            }
            for (int y = 0; y < size; y++) for (int x = 0; x < size; x++)
            {
                var (dx, dy) = Orient(x, y, size, pose);
                int src = (oy + y) * source.W + ox + x, dst = (oy + dy) * source.W + ox + dx;
                tiles[dst] = source.Tiles[src]; terrain[dst] = source.Terrain[src]; regions[dst] = source.Regions[src];
            }
            var entrance = original.Entrance.Select(t =>
            {
                var (x, y) = Orient(t.X - ox, t.Y - oy, size, pose);
                return new MapTile((ushort)(ox + x), (ushort)(oy + y));
            }).ToArray();
            var centreLocal = Orient(original.Centre.X - ox, original.Centre.Y - oy, size, pose);
            double mx = entrance.Average(t => t.X) - (ox + (size - 1) / 2.0);
            double my = entrance.Average(t => t.Y) - (oy + (size - 1) / 2.0);
            byte front = Math.Abs(mx) > Math.Abs(my) ? (byte)(mx > 0 ? 0 : 8) : (byte)(my > 0 ? 4 : 12);
            anchor = new MapAnchor(original.Zone, original.Kind,
                new MapTile((ushort)(ox + centreLocal.x), (ushort)(oy + centreLocal.y)),
                original.Footprint, front, entrance);
            var map = new MapBlob(source.W, source.H, tiles, terrain, regions, source.TerrainMode);
            return delivered ? map.WithPublic(null, null, null, null, null, null, new[] { anchor }) : map;
        }

        static (int x, int y) Orient(int x, int y, int size, int pose)
        {
            int sx = (pose & 2) != 0 ? size - 1 - x : x;
            int sy = (pose & 4) != 0 ? size - 1 - y : y;
            return (pose & 1) != 0 ? (sy, sx) : (sx, sy);
        }
    }
}
