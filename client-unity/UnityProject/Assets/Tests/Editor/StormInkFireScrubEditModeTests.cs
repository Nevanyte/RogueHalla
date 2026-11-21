using NUnit.Framework;
using Roguehalla.Net.Protocol;
using UnityEngine;

namespace Roguehalla.Client.Tests
{
    public sealed class StormInkFireScrubEditModeTests
    {
        [Test]
        public void FireBushCellsUseFireArtAndKeepTheirDeliveredCellCenters()
        {
            const int width = 20, height = 20;
            var regions = new byte[width * height];
            var bushes = new byte[width * height];
            regions[5 * width + 6] = 1;
            regions[7 * width + 8] = 1;
            regions[9 * width + 10] = 4;
            regions[11 * width + 12] = 5;
            bushes[5 * width + 6] = 1;
            bushes[7 * width + 8] = 1;
            bushes[9 * width + 10] = 1;
            bushes[11 * width + 12] = 1;
            var tiles = new byte[width * height];
            for (int i = 0; i < tiles.Length; i++) tiles[i] = MapBlob.Wall;
            var map = new MapBlob(width, height, tiles, null, regions)
                .WithPublic(null, null, bushes, null, null);

            var placements = RegionProps.Scatter(map);

            Assert.That(placements.Count, Is.EqualTo(4));
            Assert.That(placements[0].Art, Is.EqualTo("fire.bush"));
            Assert.That(placements[0].X, Is.EqualTo(6.5f));
            Assert.That(placements[0].Y, Is.EqualTo(5.5f));
            Assert.That(placements[0].Scale, Is.EqualTo(1f));
            Assert.That(placements[1].Art, Is.EqualTo("fire.bush"));
            Assert.That(placements[1].X, Is.EqualTo(8.5f));
            Assert.That(placements[1].Y, Is.EqualTo(7.5f));
            Assert.That(placements[2].Art, Is.EqualTo("frost.bush"));
            Assert.That(placements[3].Art, Is.EqualTo("holy.shrub"));
            Assert.That(placements[0].Shipped, Is.False);
            Assert.That(placements[1].Shipped, Is.False);
            Assert.That(placements[2].Scale, Is.EqualTo(1f));
            Assert.That(placements[3].Scale, Is.EqualTo(1f));
        }

        [Test]
        public void FireScrubUsesReadableAtlasArtAndKeepsBushCellGroundedAndUnfaded()
        {
            Assert.That(RegionPropArt.TryGet("fire.bush", out var scrub), Is.True);
            Assert.That(scrub.UsesAuthoredTexture, Is.True);
            Assert.That(scrub.Sprite.texture, Is.SameAs(RegionPropArt.Atlas));
            Assert.That(scrub.Tall, Is.False);
            Assert.That(RegionPropArt.Sways("fire.bush"), Is.True);
            Assert.That(scrub.MinMargin, Is.GreaterThanOrEqualTo(RegionPropArt.RequiredMargin));

            const int width = 20, height = 20, bushX = 8, bushY = 11;
            var tiles = new byte[width * height];
            for (int i = 0; i < tiles.Length; i++) tiles[i] = MapBlob.Wall;
            var regions = new byte[width * height];
            var bushes = new byte[width * height];
            regions[bushY * width + bushX] = 1;
            bushes[bushY * width + bushX] = 1;
            var map = new MapBlob(width, height, tiles, null, regions)
                .WithPublic(null, null, bushes, null, null);
            var root = new GameObject("Fire cover art");
            RegionProps scene = null;
            try
            {
                scene = RegionProps.Build(root.transform, map);
                var renderers = root.GetComponentsInChildren<SpriteRenderer>();
                Assert.That(renderers.Length, Is.EqualTo(1));
                Assert.That(renderers[0].sprite, Is.SameAs(scrub.Sprite));
                Assert.That(renderers[0].transform.localPosition, Is.EqualTo(ArenaView.WorldPoint((bushX + .5f) * 32f, (bushY + .5f) * 32f)));
                Assert.That(Mathf.Abs(renderers[0].transform.localScale.x), Is.InRange(.94f, 1.06f));
            }
            finally
            {
                scene?.Dispose();
                Object.DestroyImmediate(root);
            }

            var placement = RegionProps.Scatter(map)[0];
            Assert.That(RegionProps.IsTall(placement, scrub), Is.False);
        }

        [Test]
        public void ExistingBushArtRemainsAssignedToOtherZones()
        {
            const int width = 12, height = 12;
            var regions = new byte[width * height];
            var bushes = new byte[width * height];
            regions[3 * width + 3] = 2;
            regions[4 * width + 4] = 3;
            regions[5 * width + 5] = 4;
            regions[6 * width + 6] = 5;
            for (int i = 0; i < regions.Length; i++) bushes[i] = regions[i] == 0 ? (byte)0 : (byte)1;
            var tiles = new byte[width * height];
            for (int i = 0; i < tiles.Length; i++) tiles[i] = MapBlob.Wall;
            var map = new MapBlob(width, height, tiles, null, regions)
                .WithPublic(null, null, bushes, null, null);

            var placements = RegionProps.Scatter(map);

            Assert.That(placements[0].Art, Is.EqualTo("plague.bush"));
            Assert.That(placements[1].Art, Is.EqualTo("plague.bush"));
            Assert.That(placements[2].Art, Is.EqualTo("frost.bush"));
            Assert.That(placements[3].Art, Is.EqualTo("holy.shrub"));
        }
    }
}
