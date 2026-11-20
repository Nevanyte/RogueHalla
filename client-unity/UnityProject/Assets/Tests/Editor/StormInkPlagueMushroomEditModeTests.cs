using NUnit.Framework;
using Roguehalla.Net.Protocol;
using Roguehalla.Net.Ui;
using UnityEngine;

namespace Roguehalla.Client.Tests
{
    public sealed class StormInkPlagueMushroomEditModeTests
    {
        [Test]
        public void OrdinaryMushroomAndFungusKeepTheirFeetAndUseTheSameMatteAtlasSprite()
        {
            Assert.That(DecorationKinds.ArtKey(26, 0, 0), Is.EqualTo("plague.mushroom"));
            Assert.That(DecorationKinds.ArtKey(29, 0, 0), Is.EqualTo("plague.mushroom"));
            Assert.That(DecorationKinds.ArtKey(26, 0, MapBlob.PropFlagLegacy), Is.EqualTo(DecorationKinds.Placeholder));
            Assert.That(RegionPropArt.TryGet("plague.mushroom", out var art), Is.True);
            Assert.That(art.UsesAuthoredTexture, Is.True);
            Assert.That(art.Sprite.texture, Is.SameAs(RegionPropArt.Atlas));
            Assert.That(art.MinMargin, Is.GreaterThanOrEqualTo(RegionPropArt.RequiredMargin));
            Assert.That(art.Sprite.pivot.y, Is.InRange(1f, 5f));
            Assert.That(art.Tall, Is.False);
            Assert.That(RegionPropArt.Sways(art.Key), Is.False);
            var props = new[] { new MapProp(26, 8 * 32, 8 * 32, 0, 0), new MapProp(29, 12 * 32, 8 * 32, 0, 0) };
            var map = new MapBlob(24, 24, new byte[24 * 24]).WithPublic(null, null, null, props, null);
            var root = new GameObject("Ordinary marsh props");
            RegionProps scene = null;
            try
            {
                scene = RegionProps.Build(root.transform, map);
                var renderers = root.GetComponentsInChildren<SpriteRenderer>();
                var placements = RegionProps.Scatter(map);
                Assert.That(renderers.Length, Is.EqualTo(2));
                for (int i = 0; i < renderers.Length; i++)
                {
                    Assert.That(renderers[i].sprite, Is.SameAs(art.Sprite));
                    Assert.That(renderers[i].transform.position, Is.EqualTo(ArenaView.WorldPoint(props[i].X, props[i].Y)));
                    Assert.That(renderers[i].sortingOrder, Is.EqualTo(2000 + 8 * 16 + placements[i].Bias));
                }
            }
            finally
            {
                scene?.Dispose();
                Object.DestroyImmediate(root);
            }
        }
    }
}
