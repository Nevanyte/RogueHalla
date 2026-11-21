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
        static List<(Vector3 scale, Color tint, Vector3 foot, int order)> Capture(MapBlob map)
        {
            var root = new GameObject("Painted geology variation fixture");
            RegionProps scene = null;
            try
            {
                scene = RegionProps.Build(root.transform, map);
                return root.GetComponentsInChildren<SpriteRenderer>()
                    .Select(r => (r.transform.localScale, r.color, r.transform.localPosition, r.sortingOrder)).ToList();
            }
            finally
            {
                scene?.Dispose();
                Object.DestroyImmediate(root);
            }
        }

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

        [Test]
        public void PaintedGeologyVariationIsRepeatableAndLeavesLegacyAliasesAlone()
        {
            var rows = new[]
            {
                new MapProp(3, 96, 96, 1, 0),
                new MapProp(7, 96, 128, 2, 0),
                new MapProp(81, 96, 160, 0, MapBlob.PropFlagTall),
                new MapProp(5, 96, 192, 0, 0),
            };
            var map = new MapBlob(20, 20, new byte[400]).WithPublic(null, null, null, rows, null);
            var placements = RegionProps.Scatter(map);
            var first = Capture(map);
            var second = Capture(map);
            Assert.That(first.Count, Is.EqualTo(rows.Length));
            Assert.That(second.Count, Is.EqualTo(rows.Length));

            Assert.That(Mathf.Abs(first[0].scale.x), Is.InRange(.79f, 1.03f));
            Assert.That(first[0].scale.y, Is.InRange(.79f, 1.03f));
            Assert.That(Mathf.Abs(first[1].scale.x), Is.InRange(.79f, 1.03f));
            Assert.That(first[1].scale.y, Is.InRange(.79f, 1.03f));
            Assert.That(Mathf.Abs(first[2].scale.x), Is.InRange(.76f, 1f));
            Assert.That(first[2].scale.y, Is.InRange(.76f, 1f));
            Assert.That(first[1].tint.r, Is.InRange(.78f, .90f));
            Assert.That(first[1].tint.g, Is.InRange(.78f, .90f));
            Assert.That(first[1].tint.b, Is.InRange(.78f, .90f));
            Assert.That(first[0].tint, Is.EqualTo(Color.white));
            Assert.That(first[2].tint, Is.EqualTo(Color.white));

            Assert.That(first[3].scale, Is.EqualTo(Vector3.one), "unmigrated ash alias keeps its original sizing and facing");
            Assert.That(first[3].tint, Is.EqualTo(Color.white));
            for (int i = 0; i < first.Count; i++)
            {
                Assert.That(first[i].scale, Is.EqualTo(second[i].scale));
                Assert.That(first[i].tint, Is.EqualTo(second[i].tint));
                Assert.That(first[i].foot, Is.EqualTo(ArenaView.WorldPoint(rows[i].X, rows[i].Y)), "variation is anchored at the delivered foot");
                Assert.That(first[i].foot, Is.EqualTo(second[i].foot));
                Assert.That(first[i].order, Is.EqualTo(2000 + rows[i].Y / 2));
                Assert.That(first[i].order, Is.EqualTo(second[i].order));
            }

            Assert.That(RegionPropArt.TryGet("frost.spire", out var spire), Is.True);
            Assert.That(RegionProps.IsTall(placements[2], spire), Is.True, "the delivered tall flag still controls the spire's fade footprint");
        }
    }
}
