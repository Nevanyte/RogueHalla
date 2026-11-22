using NUnit.Framework;
using Roguehalla.Net.Protocol;
using UnityEngine;

namespace Roguehalla.Client.Tests
{
    public sealed class StormInkMarshContactEditModeTests
    {
        [TestCase(true, SurfaceKind.ShallowWater, SurfaceKind.Mud)]
        [TestCase(false, SurfaceKind.Grass, SurfaceKind.Grass)]
        public void PaintedVariantContactsUseTheSameEligibilityAsMarshPigment(
            bool usableTrunk, SurfaceKind expectedWater, SurfaceKind expectedMud)
        {
            const int width = 32, y = 5;
            var tiles = new byte[width * width];
            var terrain = new byte[tiles.Length];
            var regions = new byte[tiles.Length];
            var variants = new byte[tiles.Length];
            for (int i = 0; i < regions.Length; i++) regions[i] = (byte)RegionId.Plague;
            for (int ty = 12; ty < (usableTrunk ? 21 : 13); ty++)
            for (int x = 7; x < 16; x++) tiles[ty * width + x] = MapBlob.Wall;

            variants[y * width + 10] = 7;
            variants[y * width + 11] = 8;
            variants[y * width + 12] = 7;
            terrain[y * width + 12] = (byte)TerrainKind.Bog;
            variants[y * width + 13] = 7;
            tiles[y * width + 13] = MapBlob.Wall;
            variants[y * width + 14] = 8;
            regions[y * width + 14] = (byte)RegionId.Fire;

            var anchor = new MapAnchor((byte)RegionId.Plague, MapBlob.AnchorKindBogTree,
                new MapTile(11, 12), new MapRect(1, 1, 29, 29), 4,
                new[] { new MapTile(11, 29) });
            var map = new MapBlob(width, width, tiles, terrain, regions, MapBlob.GroundTerrainAll)
                .WithPublic(null, null, null, new[] { new MapProp(25, 16, 16, 0, 0) },
                    variants, null, new[] { anchor });
            Assert.That(Resources.Load<Texture2D>("StormInk/Plague/MarshWash"), Is.Not.Null);
            var region = new RegionMap();
            region.Load(map);
            var layer = PlagueGround.Build(map);
            var surfaces = new SurfaceMap(region);
            surfaces.AddLayer(layer);
            SurfaceKind At(int x) => surfaces.SurfaceAtServer(x * 32 + 16, y * 32 + 16);
            Assert.That(At(10), Is.EqualTo(expectedWater), "variant 7 must match the painted water");
            Assert.That(At(11), Is.EqualTo(expectedMud), "variant 8 must match the painted mud");
            Assert.That(At(12), Is.EqualTo(SurfaceKind.Bog), "effective terrain wins over cosmetic water");
            Assert.That(layer.TryGet(13, y, RegionId.Plague, out _), Is.False, "walls never inherit painted contacts");
            Assert.That(At(14), Is.EqualTo(SurfaceKind.Basalt), "variant 8 in another region stays ignored");
            Assert.That(At(15), Is.EqualTo(SurfaceKind.Grass), "no synthetic noise water or mud returns");
            Assert.That(layer.WaterTiles, Is.Zero);
            Assert.That(layer.MudTiles, Is.Zero);
        }
    }
}
