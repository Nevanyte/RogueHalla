using System.Linq;
using NUnit.Framework;
using Roguehalla.Net.Protocol;

namespace Roguehalla.Client.Tests
{
    public sealed class StormInkMarshPigmentTests
    {
        [Test]
        public void DressedWaterAndMudPaintDifferentlyWhileTheWalkingSurfaceStaysGrass()
        {
            const int w = 21, h = 9;
            var tiles = Enumerable.Repeat(MapBlob.Floor, w * h).ToArray();
            var regions = Enumerable.Repeat((byte)RegionId.Plague, w * h).ToArray();
            var variants = new byte[w * h];
            for (int y = 0; y < h; y++)
                for (int x = 7; x < w; x++)
                    variants[y * w + x] = (byte)(x < 14 ? 7 : 8);
            tiles[4 * w + 19] = MapBlob.Wall;
            var map = new MapBlob(w, h, tiles, new byte[w * h], regions, MapBlob.GroundTerrainAll)
                .WithPublic(null, null, null, new[] { new MapProp(1, 64, 64, 0, 0) }, variants);
            var ground = PlagueGround.Build(map);
            var original = ground.Bake(1);
            var painted = ground.Bake(1, map.GroundVariants);

            Assert.That(painted[4 * w + 3].g, Is.EqualTo(255), "grass");
            Assert.That(painted[4 * w + 10].r, Is.EqualTo(255), "decorative shallow water");
            Assert.That(painted[4 * w + 17].r, Is.EqualTo(0), "mud has no standing water");
            Assert.That(painted[4 * w + 17].g, Is.EqualTo(0), "exposed mud has no grass");
            Assert.That(painted[4 * w + 19].a, Is.Zero, "the wall has no walkable-surface pigment");
            Assert.That(original[4 * w + 10].g, Is.EqualTo(255), "legacy renderer");
            Assert.That(ground.KindAt(10, 4), Is.EqualTo(PlagueGround.Grass), "footstep surface");
        }
    }
}
