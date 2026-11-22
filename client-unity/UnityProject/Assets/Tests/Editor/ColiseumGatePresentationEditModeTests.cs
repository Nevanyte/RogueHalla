using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Roguehalla.Client;
using Roguehalla.Net.Protocol;
using UnityEngine;

namespace Roguehalla.Client.Tests
{
    public sealed class ColiseumGatePresentationEditModeTests
    {
        const int Width = 24, Height = 24, ColiseumX = 5, ColiseumY = 5, ColiseumW = 14, ColiseumH = 14;

        [TestCase(0)] [TestCase(1)] [TestCase(2)] [TestCase(3)]
        public void DeliveredGatePlacesOnlyItsTwoSolidEndpointPiers(int side)
        {
            var map = MapWithGate(side, out var expected);
            var gate = ColiseumGateLayout.DeliveredGates(map).Single();
            Assert.That(gate.Side, Is.EqualTo(side));
            Assert.That(gate.RunsAlongScreen, Is.EqualTo(side == 0 || side == 2));
            Assert.That(ColiseumGateLayout.PierCells(map, true).Select(p => p.x + "," + p.y),
                Is.EquivalentTo(expected.Select(p => p.x + "," + p.y)));
            Assert.That(ColiseumGateLayout.PierCells(map, false), Is.Empty,
                "An absent or unreadable painted pier must leave the existing wall boxes in place.");
            foreach (var tile in gate.Tiles) Assert.That(map.At(tile.X, tile.Y), Is.EqualTo(MapBlob.Floor));
            foreach (var cell in expected) Assert.That(ColiseumGateLayout.IsSolid(map, cell.x, cell.y), Is.True);
        }

        [Test]
        public void MissingColiseumAndEmptyGateInputProduceNoGuessedArchitecture()
        {
            var plain = BaseMap();
            Assert.That(ColiseumGateLayout.DeliveredGates(plain), Is.Empty);
            Assert.That(ColiseumGateLayout.PierCells(plain, true), Is.Empty);
            var empty = plain.WithPublic(null, new MapColiseum(ColiseumX, ColiseumY, ColiseumW, ColiseumH, new MapGate[0]), null, null, null);
            Assert.That(ColiseumGateLayout.DeliveredGates(empty), Is.Empty);
            Assert.That(ColiseumGateLayout.PierCells(null, true), Is.Empty);
        }

        [Test]
        public void AuthoredPiersReplaceOnlyTheTwoWallBoxesBesideEachValidGate()
        {
            Assume.That(ColiseumGateLayout.IsArtReady(), "The Coliseum gate pier needs a readable authored texture.");
            var map = MapWithGate(0, out var endpoints);
            var root = new GameObject("Coliseum pier wall test");
            try
            {
                using (var walls = RegionWalls.Build(root.transform, map, 1f))
                {
                    var vertices = root.GetComponentsInChildren<MeshFilter>().Where(m => m.name.StartsWith("Walls row "))
                        .SelectMany(m => m.sharedMesh.vertices).ToArray();
                    foreach (var endpoint in endpoints) Assert.That(HasCap(vertices, endpoint.x, endpoint.y), Is.False,
                        $"authored pier should cover the box at ({endpoint.x},{endpoint.y})");
                    Assert.That(HasCap(vertices, endpoints[0].x - 1, endpoints[0].y), Is.True,
                        "the next wall cell remains the ordinary box and closes the masonry run");
                    Assert.That(ColiseumGateLayout.PierCells(map, true).Count, Is.EqualTo(2));
                    Assert.That(walls.TileCount, Is.GreaterThan(0));
                }
            }
            finally { Object.DestroyImmediate(root); }
        }

        [Test]
        public void IrregularGateRunOrMissingEndpointWallKeepsGenericWallFallback()
        {
            var brokenRun = MapWithGate(0, out _);
            byte[] tiles = (byte[])brokenRun.Tiles.Clone();
            // Replace the second delivered gate floor with a wall: the run is no longer a valid consecutive opening.
            tiles[ColiseumY * Width + 10] = MapBlob.Wall;
            brokenRun = new MapBlob(Width, Height, tiles, Regions(), null, MapBlob.GroundTerrainMap)
                .WithPublic(null, brokenRun.Coliseum, null, null, null);
            Assert.That(ColiseumGateLayout.DeliveredGates(brokenRun), Is.Empty);
            Assert.That(ColiseumGateLayout.PierCells(brokenRun, true), Is.Empty);

            var missingEnd = MapWithGate(1, out _);
            var noWall = (byte[])missingEnd.Tiles.Clone();
            noWall[(8 * Width) + 18] = MapBlob.Floor;
            missingEnd = new MapBlob(Width, Height, noWall, Regions(), null, MapBlob.GroundTerrainMap)
                .WithPublic(null, missingEnd.Coliseum, null, null, null);
            Assert.That(ColiseumGateLayout.DeliveredGates(missingEnd), Is.Empty);
            Assert.That(ColiseumGateLayout.PierCells(missingEnd, true), Is.Empty);
        }

        [Test]
        public void SingleTileAndUnsortedContiguousGateRowsAreResolvedByTheirDeliveredCells()
        {
            var four = MapWithGate(0, out var expected);
            var reversed = (MapTile[])four.Coliseum.Gates[0].Tiles.Clone();
            System.Array.Reverse(reversed);
            var unsorted = four.WithPublic(null, new MapColiseum(ColiseumX, ColiseumY, ColiseumW, ColiseumH,
                new[] { new MapGate(0, reversed) }), null, null, null);
            Assert.That(ColiseumGateLayout.PierCells(unsorted, true).Select(p => p.x + "," + p.y),
                Is.EquivalentTo(expected.Select(p => p.x + "," + p.y)));

            var singleTiles = (byte[])four.Tiles.Clone();
            singleTiles[ColiseumY * Width + 10] = MapBlob.Wall;
            singleTiles[ColiseumY * Width + 11] = MapBlob.Wall;
            singleTiles[ColiseumY * Width + 12] = MapBlob.Wall;
            var singleBase = new MapBlob(Width, Height, singleTiles, null, Regions(), MapBlob.GroundTerrainMap);
            var single = singleBase.WithPublic(null, new MapColiseum(ColiseumX, ColiseumY, ColiseumW, ColiseumH,
                new[] { new MapGate(0, new[] { new MapTile(9, 5) }) }), null, null, null);
            Assert.That(ColiseumGateLayout.DeliveredGates(single).Single().Tiles.Count, Is.EqualTo(1));
            Assert.That(ColiseumGateLayout.PierCells(single, true).Select(p => p.x + "," + p.y),
                Is.EquivalentTo(new[] { "8,5", "10,5" }));
        }

        [TestCase(0)] [TestCase(1)]
        public void LockedStateUsesCachedLitBarsAndCorrectDepthForBothGateProjections(int side)
        {
            var map = MapWithGate(side, out _);
            var root = new GameObject("Coliseum gate test");
            try
            {
                var presenter = ColiseumGatePresenter.Build(root.transform, map);
                var renderers = root.GetComponentsInChildren<SpriteRenderer>(true);
                Assert.That(renderers.Length, Is.GreaterThan(0));
                Assert.That(renderers.All(r => r.sharedMaterial == ShowcaseSprites.Lit), Is.True,
                    "The gate bars must receive the game's dynamic 2D light.");
                Assert.That(renderers.Any(r => r.name == "Portcullis upright"), Is.True);
                Assert.That(renderers.Any(r => r.name == "Portcullis cross rail"), Is.True);
                var gateRoot = root.GetComponentsInChildren<Transform>(true).Single(t => t.name == "North portcullis" || t.name == "East portcullis");
                Assert.That(gateRoot.gameObject.activeSelf, Is.False);
                int count = renderers.Length;
                presenter.SetLocked(true);
                Assert.That(gateRoot.gameObject.activeSelf, Is.True);
                Assert.That(root.GetComponentsInChildren<SpriteRenderer>(true).Length, Is.EqualTo(count), "State updates must only toggle cached renderers.");
                presenter.SetLocked(true);
                presenter.SetLocked(false);
                Assert.That(gateRoot.gameObject.activeSelf, Is.False);
                var posts = root.GetComponentsInChildren<SpriteRenderer>(true).Where(r => r.name == "Portcullis upright").ToArray();
                Assert.That(posts.Select(r => r.sortingOrder).Distinct().Count(), Is.EqualTo(side == 0 ? 1 : 4),
                    "Along-screen gates share one wall row; receding gates depth-sort each delivered row.");
                if (side == 0)
                    Assert.That(posts.Max(p => p.transform.localPosition.x) - posts.Min(p => p.transform.localPosition.x), Is.GreaterThan(2f));
                else
                {
                    Assert.That(posts.Max(p => p.transform.localPosition.y) - posts.Min(p => p.transform.localPosition.y), Is.GreaterThan(.7f));
                    var rails = root.GetComponentsInChildren<SpriteRenderer>(true).Where(r => r.name == "Portcullis cross rail").ToArray();
                    Assert.That(rails.All(r => Mathf.Abs(Mathf.Abs(Mathf.DeltaAngle(0, r.transform.eulerAngles.z)) - 90f) < .01f), Is.True,
                        "receding rails follow compressed projected map-Y rather than forming horizontal rungs");
                }
            }
            finally { Object.DestroyImmediate(root); }
        }

        static MapBlob MapWithGate(int side, out Vector2Int[] endpoints)
        {
            var tiles = new byte[Width * Height]; var regions = Regions();
            for (int y = ColiseumY; y < ColiseumY + ColiseumH; y++)
            for (int x = ColiseumX; x < ColiseumX + ColiseumW; x++)
                if (x == ColiseumX || y == ColiseumY || x == ColiseumX + ColiseumW - 1 || y == ColiseumY + ColiseumH - 1)
                    tiles[y * Width + x] = MapBlob.Wall;
            var gateTiles = new MapTile[4];
            for (int i = 0; i < gateTiles.Length; i++)
            {
                int x = side == 1 ? ColiseumX + ColiseumW - 1 : side == 3 ? ColiseumX : ColiseumX + 4 + i;
                int y = side == 0 ? ColiseumY : side == 2 ? ColiseumY + ColiseumH - 1 : ColiseumY + 4 + i;
                tiles[y * Width + x] = MapBlob.Floor;
                gateTiles[i] = new MapTile((ushort)x, (ushort)y);
            }
            endpoints = side == 0 || side == 2
                ? new[] { new Vector2Int(gateTiles[0].X - 1, gateTiles[0].Y), new Vector2Int(gateTiles[3].X + 1, gateTiles[3].Y) }
                : new[] { new Vector2Int(gateTiles[0].X, gateTiles[0].Y - 1), new Vector2Int(gateTiles[3].X, gateTiles[3].Y + 1) };
            var map = new MapBlob(Width, Height, tiles, null, regions, MapBlob.GroundTerrainMap);
            return map.WithPublic(null, new MapColiseum(ColiseumX, ColiseumY, ColiseumW, ColiseumH,
                new[] { new MapGate((byte)side, gateTiles) }), null, null, null);
        }

        static MapBlob BaseMap() => new MapBlob(Width, Height, new byte[Width * Height], null, Regions(), MapBlob.GroundTerrainMap);

        static byte[] Regions()
        {
            var regions = new byte[Width * Height];
            for (int y = ColiseumY; y < ColiseumY + ColiseumH; y++)
            for (int x = ColiseumX; x < ColiseumX + ColiseumW; x++) regions[y * Width + x] = (byte)RegionId.Coliseum;
            return regions;
        }

        static bool HasCap(Vector3[] vertices, int x, int y)
        {
            float bottom = -(y + 1) * ArenaView.GroundSquash + RegionWalls.WallHeight;
            for (int i = 0; i + 2 < vertices.Length; i += 4)
                if (Mathf.Abs(vertices[i].x - (x - .003f)) < .0002f &&
                    Mathf.Abs(vertices[i].y - bottom) < .0002f &&
                    Mathf.Abs(vertices[i + 2].y - (bottom + .78f)) < .0002f) return true;
            return false;
        }
    }
}
