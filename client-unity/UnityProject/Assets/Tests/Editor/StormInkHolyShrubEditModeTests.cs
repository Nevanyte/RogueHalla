using NUnit.Framework;
using System.Reflection;
using Roguehalla.Net.Protocol;
using Roguehalla.Net.Ui;
using UnityEngine;

namespace Roguehalla.Client.Tests
{
    public sealed class StormInkHolyShrubEditModeTests
    {
        [Test]
        public void AuthoredShrubUsesItsEditablePaintingInTheSharedAtlas()
        {
            Assert.That(RegionPropArt.TryGet("holy.shrub", out var shrub), Is.True);
            Assert.That(RegionPropArt.TryGet("holy.flowers", out var flowers), Is.True);
            Assert.That(shrub.UsesAuthoredTexture, Is.True);
            Assert.That(flowers.UsesAuthoredTexture, Is.False);
            Assert.That(shrub.Sprite.texture, Is.SameAs(RegionPropArt.Atlas));
            Assert.That(shrub.Sprite, Is.Not.SameAs(flowers.Sprite));
            Assert.That(shrub.Tall, Is.False);
            Assert.That(RegionPropArt.Sways("holy.shrub"), Is.True);
            Assert.That(shrub.MinMargin, Is.GreaterThanOrEqualTo(RegionPropArt.RequiredMargin));
        }

        [Test]
        public void DesignerSizeEditInvalidatesForNextLookupAndKeepsOldViewSpriteAlive()
        {
            var definition = Resources.Load<HolyShrubArt>("StormInk/Props/HolyShrubArt");
            Assert.That(definition, Is.Not.Null);
            Assert.That(RegionPropArt.TryGet("holy.shrub", out var original), Is.True);
            Assert.That(original.UsesAuthoredTexture, Is.True);

            float previous = definition.WidthWorldUnits;
            float changed = Mathf.Clamp(previous * .8f, .5f, 3f);
            try
            {
                definition.WidthWorldUnits = changed;
                typeof(HolyShrubArt).GetMethod("OnValidate", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(definition, null);
                Assert.That(RegionPropArt.TryGet("holy.shrub", out var edited), Is.True);
                Assert.That(edited.Width, Is.EqualTo(changed).Within(.02f));
                Assert.That(edited.PixelWidth, Is.LessThan(original.PixelWidth), "the authored painting itself is refit to the edited width");
                Assert.That(edited.PixelHeight, Is.LessThan(original.PixelHeight), "the painted bounds retain their source aspect ratio");
                Assert.That(edited.Sprite, Is.Not.SameAs(original.Sprite));
                Assert.That(original.Sprite, Is.Not.Null, "a currently rendered view can finish using the preceding sprite");
            }
            finally
            {
                definition.WidthWorldUnits = previous;
                typeof(HolyShrubArt).GetMethod("OnValidate", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(definition, null);
                RegionPropArt.TryGet("holy.shrub", out _);
            }
        }

        [Test]
        public void ShippedShrubKeepsItsDeliveredPositionFlagsAndArtKey()
        {
            Assert.That(DecorationKinds.ArtKey(44, 0, 0), Is.EqualTo("holy.shrub"));
            var rows = new[] { new MapProp(44, 304, 368, 0, 0) };
            var map = new MapBlob(20, 20, new byte[400]).WithPublic(null, null, null, rows, null);
            var placements = RegionProps.Scatter(map);
            Assert.That(placements.Count, Is.EqualTo(1));
            Assert.That(placements[0].Art, Is.EqualTo("holy.shrub"));
            Assert.That(placements[0].Kind, Is.EqualTo(rows[0].Kind));
            Assert.That(placements[0].Flags, Is.EqualTo(rows[0].Flags));
            Assert.That(placements[0].Variant, Is.EqualTo(rows[0].Variant));
            Assert.That(placements[0].X, Is.EqualTo(rows[0].X / 32f));
            Assert.That(placements[0].Y, Is.EqualTo(rows[0].Y / 32f));
            Assert.That(RegionPropArt.TryGet(placements[0].Art, out var art), Is.True);
            Assert.That(RegionProps.IsTall(placements[0], art), Is.False,
                "the new low foliage art must not opt into tall-prop fading or change server concealment data");
        }

        [Test]
        public void ConcealmentBushPlaneStillPlacesTheHolyShrubAtItsCell()
        {
            const int width = 20, height = 20, bushX = 8, bushY = 11;
            var tiles = new byte[width * height];
            for (int i = 0; i < tiles.Length; i++) tiles[i] = MapBlob.Wall;
            var regions = new byte[width * height];
            regions[bushY * width + bushX] = 5;
            var bushes = new byte[width * height];
            bushes[bushY * width + bushX] = 1;
            var map = new MapBlob(width, height, tiles, null, regions).WithPublic(null, null, bushes, null, null);

            var placements = RegionProps.Scatter(map);
            Assert.That(placements.Count, Is.EqualTo(1));
            var shrub = placements[0];
            Assert.That(shrub.Art, Is.EqualTo("holy.shrub"));
            Assert.That(shrub.Shipped, Is.False);
            Assert.That(shrub.X, Is.EqualTo(bushX + .5f));
            Assert.That(shrub.Y, Is.EqualTo(bushY + .5f));
            Assert.That(RegionPropArt.TryGet(shrub.Art, out var art), Is.True);
            Assert.That(RegionProps.IsTall(shrub, art), Is.False);
        }

    }
}
