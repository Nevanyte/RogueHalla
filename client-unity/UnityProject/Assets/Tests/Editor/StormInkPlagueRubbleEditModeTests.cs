using System.Collections.Generic;
using NUnit.Framework;
using Roguehalla.Net.Protocol;
using Roguehalla.Net.Ui;
using UnityEngine;

namespace Roguehalla.Client.Tests
{
    public sealed class StormInkPlagueRubbleEditModeTests
    {
        [Test]
        public void PaintedRubbleTextureReachesAtlasAtTheOriginalGroundScale()
        {
            var source = Resources.Load<Texture2D>("StormInk/Props/PlagueRubble");
            Assert.That(source, Is.Not.Null, "the reviewed prop texture is required");
            Assert.That(source.isReadable, Is.True, "the atlas builder samples source pixels");
            Assert.That(RegionPropArt.TryGet("plague.rubble", out var rubble), Is.True);

            var sourceColors = new HashSet<Color32>();
            foreach (var pixel in source.GetPixels32()) if (pixel.a > 8) sourceColors.Add(pixel);
            var rect = rubble.Sprite.textureRect;
            var atlas = rubble.Sprite.texture.GetPixels32();
            int x0 = Mathf.RoundToInt(rect.x), y0 = Mathf.RoundToInt(rect.y);
            int width = Mathf.RoundToInt(rect.width), height = Mathf.RoundToInt(rect.height), matched = 0;
            var seen = new HashSet<Color32>();
            for (int y = 0; y < height; y++) for (int x = 0; x < width; x++)
            {
                var pixel = atlas[(y0 + y) * rubble.Sprite.texture.width + x0 + x];
                if (pixel.a > 8 && sourceColors.Contains(pixel) && seen.Add(pixel)) matched++;
            }

            Assert.That(matched, Is.GreaterThanOrEqualTo(12), "distinct authored pigment colors must reach the atlas crop");
            Assert.That(rubble.Width, Is.EqualTo(112f / RegionPropArt.PixelsPerUnit).Within(.001f));
            Assert.That(rubble.Height, Is.EqualTo(52f / RegionPropArt.PixelsPerUnit).Within(.001f));
            Assert.That(rubble.Sprite.pivot.x / rubble.Sprite.rect.width, Is.EqualTo(.5f).Within(.01f));
            Assert.That(rubble.Sprite.pivot.y, Is.InRange(1f, 5f), "the sprite pivot stays at the old ground contact");
            Assert.That(rubble.MinMargin, Is.GreaterThanOrEqualTo(RegionPropArt.RequiredMargin));
            Assert.That(RegionPropArt.Sways("plague.rubble"), Is.False, "the replacement remains in the rigid atlas band");
            Assert.That(rubble.UsesAuthoredTexture, Is.True);
            Assert.That(RegionPropArt.TryGet(DecorationKinds.Placeholder, out var fallback), Is.True);
            Assert.That(fallback.UsesAuthoredTexture, Is.False, "procedural props do not inherit authored-art variation");
        }

        [Test]
        public void PlagueRubbleKeepsWireFeetButGetsDeterministicLowArtVariation()
        {
            const int count = 24, yTile = 24;
            var props = new MapProp[count + 1];
            for (int i = 0; i < count; i++)
            {
                ushort x = (ushort)((8 + i * 2) * 32);
                props[i] = new MapProp((byte)(i % 2 == 0 ? 22 : 27), x, yTile * 32, (byte)(i % 3), 0);
            }
            // Legacy-flagged decoration deliberately uses the procedural fallback.
            props[count] = new MapProp(26, 56 * 32, yTile * 32, 0, MapBlob.PropFlagLegacy);
            var map = new MapBlob(64, 64, new byte[64 * 64]).WithPublic(null, null, null, props, null);

            var scattered = RegionProps.Scatter(map);
            Assert.That(scattered.Count, Is.EqualTo(props.Length));
            Assert.That(scattered[count].Art, Is.EqualTo(DecorationKinds.Placeholder));
            for (int i = 0; i < count; i++)
            {
                Assert.That(scattered[i].Art, Is.EqualTo("plague.rubble"));
                Assert.That(scattered[i].X, Is.EqualTo(props[i].X / 32f));
                Assert.That(scattered[i].Y, Is.EqualTo(props[i].Y / 32f));
                Assert.That(scattered[i].Kind, Is.EqualTo(props[i].Kind));
                Assert.That(scattered[i].Variant, Is.EqualTo(props[i].Variant));
            }

            var parentA = new GameObject("plague rubble variation A");
            var parentB = new GameObject("plague rubble variation B");
            RegionProps first = null, second = null;
            try
            {
                first = RegionProps.Build(parentA.transform, map);
                second = RegionProps.Build(parentB.transform, map);
                var a = parentA.GetComponentsInChildren<SpriteRenderer>();
                var b = parentB.GetComponentsInChildren<SpriteRenderer>();
                Assert.That(a.Length, Is.EqualTo(props.Length)); Assert.That(b.Length, Is.EqualTo(props.Length));
                var tints = new HashSet<Color32>(); int flipped = 0, unflipped = 0;
                float minWidth = float.MaxValue, maxWidth = float.MinValue, minHeight = float.MaxValue, maxHeight = float.MinValue;
                for (int i = 0; i < count; i++)
                {
                    float width = Mathf.Abs(a[i].transform.localScale.x), height = a[i].transform.localScale.y;
                    Assert.That(width, Is.InRange(.84f, .98f)); Assert.That(height, Is.InRange(.66f, .82f));
                    Assert.That(height, Is.LessThan(width), "the new low prop is flatter than its width");
                    minWidth = Mathf.Min(minWidth, width); maxWidth = Mathf.Max(maxWidth, width);
                    minHeight = Mathf.Min(minHeight, height); maxHeight = Mathf.Max(maxHeight, height);
                    if (a[i].transform.localScale.x < 0) flipped++; else unflipped++;
                    Assert.That(a[i].color.r, Is.LessThan(a[i].color.g)); Assert.That(a[i].color.b, Is.LessThan(a[i].color.g));
                    tints.Add((Color32)a[i].color);
                    Assert.That(Vector3.Distance(a[i].transform.localPosition, ArenaView.WorldPoint(scattered[i].X * 32f, scattered[i].Y * 32f)), Is.LessThan(.001f));
                    Assert.That(a[i].transform.localRotation, Is.EqualTo(Quaternion.identity));
                    Assert.That(a[i].sortingOrder, Is.EqualTo(2000 + Mathf.RoundToInt(scattered[i].Y * 16f) + scattered[i].Bias));
                    Assert.That(b[i].transform.localScale, Is.EqualTo(a[i].transform.localScale), "variation repeats for the same map");
                    Assert.That(b[i].color, Is.EqualTo(a[i].color));
                }
                Assert.That(flipped, Is.GreaterThan(0)); Assert.That(unflipped, Is.GreaterThan(0));
                Assert.That(maxWidth - minWidth, Is.GreaterThan(.04f));
                Assert.That(maxHeight - minHeight, Is.GreaterThan(.04f));
                Assert.That(tints.Count, Is.GreaterThan(6));
                Assert.That(RegionPropArt.TryGet(DecorationKinds.Placeholder, out var fallback), Is.True);
                Assert.That(a[count].sprite, Is.SameAs(fallback.Sprite));
                Assert.That(b[count].sprite, Is.SameAs(fallback.Sprite));
                Assert.That(a[count].transform.localScale, Is.EqualTo(Vector3.one), "procedural fallback art stays untouched");
                Assert.That(a[count].color, Is.EqualTo(Color.white));
            }
            finally
            {
                first?.Dispose(); second?.Dispose();
                Object.DestroyImmediate(parentA); Object.DestroyImmediate(parentB);
            }
        }
    }
}
