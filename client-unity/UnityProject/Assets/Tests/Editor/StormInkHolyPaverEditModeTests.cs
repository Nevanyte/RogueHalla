using NUnit.Framework;
using Roguehalla.Net.Protocol;
using Roguehalla.Net.Ui;
using UnityEngine;

namespace Roguehalla.Client.Tests
{
    public sealed class StormInkHolyPaverEditModeTests
    {
        [Test]
        public void ShippedPaverUsesItsOwnLowArtInTheExistingAtlas()
        {
            Assert.That(DecorationKinds.ArtKey(47, 0, 0), Is.EqualTo("holy.paver"));
            Assert.That(DecorationKinds.ArtKey(42, 0, 0), Is.EqualTo("holy.rock"));
            Assert.That(DecorationKinds.ArtKey(1, 0, MapBlob.PropFlagLegacy), Is.EqualTo(DecorationKinds.Placeholder));

            var source = Resources.Load<Texture2D>("StormInk/Props/HolyPaver");
            Assert.That(source, Is.Not.Null);
            Assert.That(source.isReadable, Is.True);
            Assert.That(RegionPropArt.TryGet("holy.paver", out var paver), Is.True);
            Assert.That(RegionPropArt.TryGet("holy.rock", out var rock), Is.True);
            Assert.That(paver.UsesAuthoredTexture, Is.True);
            Assert.That(rock.UsesAuthoredTexture, Is.False);
            Assert.That(paver.Sprite, Is.Not.SameAs(rock.Sprite));
            Assert.That(paver.Sprite.texture, Is.SameAs(RegionPropArt.Atlas));
            Assert.That(rock.Sprite.texture, Is.SameAs(RegionPropArt.Atlas));
            Assert.That(paver.Tall, Is.False);
            Assert.That(RegionPropArt.Sways("holy.paver"), Is.False);
            Assert.That(paver.MinMargin, Is.GreaterThanOrEqualTo(RegionPropArt.RequiredMargin));
            Assert.That(paver.Sprite.pivot.x / paver.Sprite.rect.width, Is.EqualTo(.5f).Within(.01f));
            Assert.That(paver.Sprite.pivot.y / paver.Sprite.rect.height, Is.LessThan(.4f));

            var rect = paver.Sprite.textureRect;
            int width = Mathf.RoundToInt(rect.width), height = Mathf.RoundToInt(rect.height);
            int x0 = Mathf.RoundToInt(rect.x), y0 = Mathf.RoundToInt(rect.y);
            int minX = width, minY = height, maxX = -1, maxY = -1, visible = 0;
            for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                if (RegionPropArt.Atlas.GetPixel(x0 + x, y0 + y).a <= .08f) continue;
                minX = Mathf.Min(minX, x);
                minY = Mathf.Min(minY, y);
                maxX = Mathf.Max(maxX, x);
                maxY = Mathf.Max(maxY, y);
                visible++;
            }
            Assert.That(visible, Is.GreaterThan(100));
            Assert.That((maxY - minY + 1f) / (maxX - minX + 1f), Is.LessThan(.55f),
                "the visible paver reads as a low ground prop");
        }

        [Test]
        public void PaverPreservesShippedFeetAndDepthAlongsideRockAndLegacyRows()
        {
            var rows = new[] {
                new MapProp(47, 304, 368, 0, 0),
                new MapProp(42, 336, 368, 0, 0),
                new MapProp(1, 368, 368, 0, MapBlob.PropFlagLegacy),
            };
            var map = new MapBlob(20, 20, new byte[400]).WithPublic(null, null, null, rows, null);
            var placements = RegionProps.Scatter(map);
            Assert.That(placements.Count, Is.EqualTo(3));
            Assert.That(placements[0].Art, Is.EqualTo("holy.paver"));
            Assert.That(placements[1].Art, Is.EqualTo("holy.rock"));
            Assert.That(placements[2].Art, Is.EqualTo(DecorationKinds.Placeholder));

            var parent = new GameObject("delivered Holy paver fixture");
            RegionProps props = null;
            try
            {
                props = RegionProps.Build(parent.transform, map);
                var renderers = parent.GetComponentsInChildren<SpriteRenderer>();
                Assert.That(renderers.Length, Is.EqualTo(3));
                for (int i = 0; i < rows.Length; i++)
                {
                    var row = rows[i];
                    var placement = placements[i];
                    var renderer = renderers[i];
                    Assert.That(placement.Kind, Is.EqualTo(row.Kind));
                    Assert.That(placement.Flags, Is.EqualTo(row.Flags));
                    Assert.That(placement.Variant, Is.EqualTo(row.Variant));
                    Assert.That(renderer.transform.localPosition, Is.EqualTo(ArenaView.WorldPoint(row.X, row.Y)));
                    Assert.That(renderer.sortingOrder, Is.EqualTo(2000 + row.Y / 2));
                    Assert.That(renderer.transform.parent, Is.SameAs(renderers[0].transform.parent));
                    Assert.That(renderer.sharedMaterial, Is.SameAs(renderers[0].sharedMaterial));
                    Assert.That(RegionPropArt.TryGet(placement.Art, out var art), Is.True);
                    Assert.That(renderer.sprite, Is.SameAs(art.Sprite));
                }
                Assert.That(renderers[1].transform.localScale, Is.EqualTo(Vector3.one));
                Assert.That(renderers[1].color, Is.EqualTo(Color.white));
            }
            finally
            {
                props?.Dispose();
                Object.DestroyImmediate(parent);
            }
        }
    }
}
