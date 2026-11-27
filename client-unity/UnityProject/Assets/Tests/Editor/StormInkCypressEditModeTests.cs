using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using Roguehalla.Net.Protocol;
using UnityEngine;

namespace Roguehalla.Client.Tests
{
    public sealed class StormInkCypressEditModeTests
    {
        const int MapSize = 48, Size = 29, Left = 9, Top = 7;
        const int OutsideX = 1, OutsideY = 1;
        const byte Plague = 3;
        const string TrunkName = "Bog tree trunk";

        static IEnumerable<int> Orientations() => Enumerable.Range(0, 8);

        [TestCaseSource(nameof(Orientations))]
        public void DeliveredCypressOwnsOnlyItsFootprintAndRevealsActors(int orientation)
        {
            var map = Map(orientation, AnchorCase.Valid, out _);
            var root = new GameObject("painted cypress oracle");
            try
            {
                using var walls = RegionWalls.Build(root.transform, map, 1f);
                using var landmarks = LandmarkView.Build(root.transform, map);

                Assert.That(walls.RowMeshCount, Is.EqualTo(1), "only the unrelated outside wall remains a regular wall mesh");
                Assert.That(root.GetComponentsInChildren<MeshFilter>().Any(r => r.name == "Walls row 1 x0"), Is.True);
                Assert.That(landmarks.Count, Is.EqualTo(1));
                var cypress = landmarks.Get(0);
                var renderers = cypress.Root.GetComponentsInChildren<SpriteRenderer>();
                var trunk = renderers.Single(r => r.name == TrunkName);
                var crown = renderers.Single(r => r.name == BogTreeLandmark.CrownName);
                var trunkAsset = Resources.Load<Sprite>("StormInk/Plague/CypressTrunk");
                var crownAsset = Resources.Load<Sprite>("StormInk/Plague/CypressCrown");
                Assert.That(trunkAsset, Is.Not.Null, "canonical trunk imported");
                Assert.That(crownAsset, Is.Not.Null, "canonical crown imported");
                Assert.That(trunk.sprite, Is.SameAs(trunkAsset));
                Assert.That(crown.sprite, Is.SameAs(crownAsset));
                foreach (var layer in new[] { trunk, crown })
                {
                    Assert.That(layer.bounds.min.x, Is.GreaterThanOrEqualTo(Left));
                    Assert.That(layer.bounds.max.x, Is.LessThanOrEqualTo(Left + Size));
                    Assert.That(layer.transform.rotation, Is.EqualTo(Quaternion.identity), "screen-upright in every map orientation");
                }
                Assert.That(trunk.sortingOrder, Is.LessThan(crown.sortingOrder), "trunk keeps its actor-row sorting");
                int trunkOrder = trunk.sortingOrder;
                int crownOrder = crown.sortingOrder;

                var inside = (from y in Enumerable.Range(Top, Size)
                              from x in Enumerable.Range(Left, Size)
                              where cypress.RoofCovers(x, y) && map.Tiles[y * MapSize + x] == MapBlob.Floor
                              where map.Terrain[y * MapSize + x] != (byte)TerrainKind.Bog
                              orderby Mathf.Abs(x - Left - 14) + Mathf.Abs(y - Top - 14)
                              select new Vector2Int(x, y)).First();
                landmarks.NoteActor(Feet(OutsideX, OutsideY));
                landmarks.Tick(10f);
                AssertAlpha(trunk, crown, LandmarkView.RoofOpaqueAlpha);

                landmarks.NoteActor(Feet(inside.x, inside.y));
                landmarks.Tick(10f);
                AssertAlpha(trunk, crown, LandmarkView.RoofFadedAlpha);

                landmarks.NoteOtherActor(Feet(inside.x, inside.y));
                landmarks.Tick(10f);
                AssertAlpha(trunk, crown, LandmarkView.RoofPeekAlpha);

                landmarks.Tick(10f);
                AssertAlpha(trunk, crown, LandmarkView.RoofOpaqueAlpha);
                Assert.That(trunk.sortingOrder, Is.EqualTo(trunkOrder));
                Assert.That(crown.sortingOrder, Is.EqualTo(crownOrder));
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }

        [Test]
        public void PlagueAnchorActivatesItsAuthoredGroundWashAcrossTheRegionGround()
        {
            var map = Map(0, AnchorCase.Valid, out _);
            var root = new GameObject("Plague ground wash witness");
            try
            {
                var plague = PlagueGround.Build(map);
                Assert.That(plague.MudTiles + plague.GrassTiles + plague.WaterTiles, Is.GreaterThan(0), "Plague floor has its distinct mud, grass and shallow-water classification");
                using var ground = RegionGround.Create(root.transform, map, 1f, plague: plague);
                Assert.That(ground.Material.GetFloat("_StormMarshOn"), Is.EqualTo(1f), "the delivered cypress activates the authored watercolor marsh path");
                Assert.That(ground.Material.GetTexture("_StormMarshWash"), Is.Not.Null, "active Plague ground must bind its ink wash");
                Assert.That(ground.Material.GetTexture("_PlgTex"), Is.Not.Null, "the full map carries the Plague coverage plane");
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }

        [Test]
        public void PlagueWithoutDeliveredAnchorKeepsTheGenericGroundFallback()
        {
            var map = Map(0, AnchorCase.Undelivered, out _);
            var root = new GameObject("Plague fallback ground witness");
            try
            {
                var plague = PlagueGround.Build(map);
                using var ground = RegionGround.Create(root.transform, map, 1f, plague: plague);
                Assert.That(ground.Material.GetFloat("_StormMarshOn"), Is.EqualTo(0f), "the authored cypress wash requires its delivered signature anchor");
                Assert.That(ground.Material.GetTexture("_GroundTex"), Is.Not.Null, "the existing generic region ground remains available without the anchor");
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }

        public enum AnchorCase { Valid, Undelivered, WrongZone, OffMap, SmallFootprint, ThinTrunk }

        [TestCase(AnchorCase.Undelivered)]
        [TestCase(AnchorCase.WrongZone)]
        [TestCase(AnchorCase.OffMap)]
        [TestCase(AnchorCase.SmallFootprint)]
        [TestCase(AnchorCase.ThinTrunk)]
        public void WithoutAUsableDeliveredCypressWallGeometryRemains(AnchorCase kind)
        {
            var map = Map(0, kind, out var anchor);
            var root = new GameObject("unmatched cypress oracle");
            try
            {
                using var walls = RegionWalls.Build(root.transform, map, 1f);
                Assert.That(walls.RowMeshCount, Is.GreaterThan(1), "walls still own the in-footprint geometry");
                using var landmarks = kind == AnchorCase.Undelivered || kind == AnchorCase.OffMap
                    ? LandmarkView.Build(root.transform, map, new[] { anchor })
                    : LandmarkView.Build(root.transform, map);
                Assert.That(root.GetComponentsInChildren<SpriteRenderer>()
                    .Any(r => r.sprite != null && r.sprite.name.StartsWith("Cypress", StringComparison.Ordinal)), Is.False,
                    "unmatched or malformed footprints cannot claim the replacement sprites");
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }

        static Vector3 Feet(int x, int y) => new Vector3(x + .5f, -(y + .5f) * ArenaView.GroundSquash, 0f);

        static void AssertAlpha(SpriteRenderer trunk, SpriteRenderer crown, float alpha)
        {
            Assert.That(trunk.color.a, Is.EqualTo(alpha).Within(.001f), "the lower silhouette reveals the actor too");
            Assert.That(crown.color.a, Is.EqualTo(alpha).Within(.001f), "the crown reveals the actor");
        }

        static MapBlob Map(int pose, AnchorCase kind, out MapAnchor anchor)
        {
            string source = Path.GetFullPath(Path.Combine(Application.dataPath, "../../../docs/landmarks/blueprints/bog.txt"));
            var rows = File.ReadAllLines(source).Where(s => s.Length > 0 && s[0] != ';').ToArray();
            var tiles = new byte[MapSize * MapSize];
            var terrain = new byte[tiles.Length];
            var regions = new byte[tiles.Length];
            var entrances = new List<MapTile>();
            tiles[OutsideY * MapSize + OutsideX] = MapBlob.Wall;

            for (int y = 0; y < Size; y++)
            for (int x = 0; x < Size; x++)
            {
                int sx = (pose & 2) != 0 ? Size - 1 - x : x;
                int sy = (pose & 4) != 0 ? Size - 1 - y : y;
                if ((pose & 1) != 0) (sx, sy) = (sy, sx);
                char glyph = rows[sy][sx];
                int index = (Top + y) * MapSize + Left + x;
                bool treeWall = glyph == 'T' || glyph == 'r';
                if (kind == AnchorCase.ThinTrunk) treeWall = y == 14 && x >= 9 && x <= 18;
                if (treeWall) tiles[index] = MapBlob.Wall;
                if (glyph == '%') terrain[index] = (byte)TerrainKind.Bog;
                if (glyph == ':' && (x == 0 || x == Size - 1 || y == 0 || y == Size - 1))
                    entrances.Add(new MapTile((ushort)(Left + x), (ushort)(Top + y)));
                regions[index] = Plague;
            }
            int width = kind == AnchorCase.SmallFootprint ? 17 : Size;
            var rect = new MapRect((ushort)(kind == AnchorCase.OffMap ? MapSize - 3 : Left), Top, (ushort)width, Size);
            if (kind == AnchorCase.SmallFootprint) entrances.RemoveAll(t => t.X >= Left + width);
            anchor = new MapAnchor(kind == AnchorCase.WrongZone ? (byte)4 : Plague, MapBlob.AnchorKindBogTree,
                new MapTile(Left + 14, Top + 14), rect, 4, entrances.ToArray());
            var map = new MapBlob(MapSize, MapSize, tiles, terrain, regions, MapBlob.GroundTerrainAll);
            return kind == AnchorCase.Undelivered || kind == AnchorCase.OffMap ? map : map.WithPublic(null, null, null, null, null, null, new[] { anchor });
        }
    }
}
