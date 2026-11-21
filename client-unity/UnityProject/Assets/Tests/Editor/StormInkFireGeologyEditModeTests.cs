using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Roguehalla.Net.Protocol;
using Roguehalla.Net.Ui;
using UnityEngine;

namespace Roguehalla.Client.Tests
{
    public sealed class StormInkFireGeologyEditModeTests
    {
        [Test]
        public void DeliveredFireMaterialsResolveSeparatelyAndKeepLegacyAliasesAndDrawOrder()
        {
            var rows = new[]
            {
                new MapProp(3, 96, 96, 0, 0),   // Loose scree.
                new MapProp(7, 96, 128, 0, 0),  // Fractured basalt ledge.
                new MapProp(5, 96, 160, 0, 0),  // Ash alias.
                new MapProp(6, 96, 192, 0, 0),  // Crust alias.
                new MapProp(9, 96, 224, 0, 0),  // Boulder alias.
                new MapProp(10, 96, 256, 0, 0), // Ashheap alias.
                new MapProp(3, 96, 288, 0, MapBlob.PropFlagLegacy),
            };
            var map = new MapBlob(20, 20, new byte[400]).WithPublic(null, null, null, rows, null);
            var placements = RegionProps.Scatter(map);
            Assert.That(placements.Count, Is.EqualTo(rows.Length));

            for (int i = 0; i < rows.Length; i++)
            {
                Assert.That(placements[i].Art, Is.EqualTo(DecorationKinds.ArtKey(rows[i].Kind, rows[i].Variant, rows[i].Flags)));
                Assert.That(placements[i].Kind, Is.EqualTo(rows[i].Kind));
                Assert.That(placements[i].Flags, Is.EqualTo(rows[i].Flags));
                Assert.That(placements[i].Variant, Is.EqualTo(rows[i].Variant));
            }
            Assert.That(placements[0].Art, Is.EqualTo("fire.rocks"));
            Assert.That(placements[1].Art, Is.EqualTo("fire.rocks"));
            foreach (int i in new[] { 2, 3, 4, 5 }) Assert.That(placements[i].Art, Is.EqualTo("fire.rocks"));
            Assert.That(placements[6].Art, Is.EqualTo(DecorationKinds.Placeholder));

            Assert.That(RegionPropArt.TryGet("fire.scree", out var scree), Is.True);
            Assert.That(RegionPropArt.TryGet("fire.basalt.ledge", out var basalt), Is.True);
            Assert.That(RegionPropArt.TryGet("fire.rocks", out var oldPile), Is.True);
            Assert.That(scree.UsesAuthoredTexture, Is.True);
            Assert.That(basalt.UsesAuthoredTexture, Is.True);
            Assert.That(oldPile.UsesAuthoredTexture, Is.False);
            Assert.That(scree.Tall, Is.False);
            Assert.That(basalt.Tall, Is.False);
            Assert.That(scree.Width, Is.EqualTo(oldPile.Width).Within(.02f));
            Assert.That(basalt.Width, Is.EqualTo(oldPile.Width).Within(.02f));
            Assert.That(scree.Height, Is.EqualTo(oldPile.Height).Within(.02f));
            Assert.That(basalt.Height, Is.EqualTo(oldPile.Height).Within(.02f));
            Assert.That(RegionPropArt.TryGet(DecorationKinds.Placeholder, out var placeholder), Is.True);

            var expectedSprites = new[] { scree.Sprite, basalt.Sprite, oldPile.Sprite, oldPile.Sprite, oldPile.Sprite, oldPile.Sprite, placeholder.Sprite };
            var root = new GameObject("Delivered Fire geology fixture");
            RegionProps scene = null;
            try
            {
                scene = RegionProps.Build(root.transform, map);
                var renderers = root.GetComponentsInChildren<SpriteRenderer>();
                Assert.That(renderers.Length, Is.EqualTo(rows.Length));
                for (int i = 0; i < renderers.Length; i++)
                {
                    Assert.That(renderers[i].sprite, Is.SameAs(expectedSprites[i]));
                    Assert.That(renderers[i].sortingOrder, Is.EqualTo(2000 + rows[i].Y / 2));
                    Assert.That(renderers[i].transform.localPosition, Is.EqualTo(ArenaView.WorldPoint(rows[i].X, rows[i].Y)));
                    Assert.That(renderers[i].sprite.texture, Is.SameAs(RegionPropArt.Atlas));
                }
            }
            finally
            {
                scene?.Dispose();
                Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void UndressedFireRockRulesUseTheLooseScreePainting()
        {
            const int width = 48, height = 48;
            var tiles = new byte[width * height];
            var regions = Enumerable.Repeat((byte)1, width * height).ToArray();
            var map = new MapBlob(width, height, tiles, null, regions).WithPublic(null, null, null, null, null);
            var placements = RegionProps.Scatter(map);
            var looseRock = placements.FirstOrDefault(p => !p.Shipped && p.Art == "fire.rocks");
            Assert.That(looseRock.Art, Is.EqualTo("fire.rocks"), "the deterministic open Fire fixture must exercise the existing undressed rock rule");
            Assert.That(RegionPropArt.TryGet("fire.scree", out var scree), Is.True);
            Assert.That(scree.UsesAuthoredTexture, Is.True);

            var root = new GameObject("Undressed Fire scree fixture");
            RegionProps scene = null;
            try
            {
                scene = RegionProps.Build(root.transform, map);
                var renderers = root.GetComponentsInChildren<SpriteRenderer>();
                Assert.That(renderers.Any(r => r.gameObject.name == "fire.rocks" && r.sprite == scree.Sprite), Is.True);
            }
            finally
            {
                scene?.Dispose();
                Object.DestroyImmediate(root);
            }
        }
    }
}
