using System.Collections.Generic;
using NUnit.Framework;
using Roguehalla.Net.Protocol;
using Roguehalla.Net.Ui;
using UnityEngine;

namespace Roguehalla.Client.Tests
{
    public sealed class StormInkPlagueFernEditModeTests
    {
        [Test]
        public void PaintedFernTextureResolvesWithGroundPivotAndSwayAtlasPlacement()
        {
            var source = Resources.Load<Texture2D>("StormInk/Props/PlagueFern");
            Assert.That(source, Is.Not.Null, "the curated Plague fern texture is required");
            Assert.That(source.isReadable, Is.True, "the shared atlas builder samples source pixels");
            int opaque = 0;
            var sourceColors = new HashSet<Color32>();
            foreach (var pixel in source.GetPixels32()) if (pixel.a > 8) { opaque++; sourceColors.Add(pixel); }
            Assert.That(opaque, Is.GreaterThan(500), "the source must contain visible painted pixels");
            Assert.That(sourceColors.Count, Is.GreaterThan(8), "the authored texture must carry painted colour variation");

            Assert.That(RegionPropArt.TryGet("plague.fern", out var fern), Is.True);
            Assert.That(fern.UsesAuthoredTexture, Is.True, "the actual source texture must reach the resolved atlas art");
            Assert.That(fern.Sprite.texture, Is.SameAs(RegionPropArt.Atlas));
            Assert.That(fern.Width, Is.EqualTo(96f / RegionPropArt.PixelsPerUnit).Within(.001f));
            Assert.That(fern.Height, Is.EqualTo(76f / RegionPropArt.PixelsPerUnit).Within(.001f));
            Assert.That(fern.Sprite.pivot.x / fern.Sprite.rect.width, Is.EqualTo(.5f).Within(.01f));
            Assert.That(fern.Sprite.pivot.y, Is.InRange(1f, 5f), "the crop pivot remains at the physical lower contact");
            Assert.That(fern.MinMargin, Is.GreaterThanOrEqualTo(RegionPropArt.RequiredMargin));
            Assert.That(RegionPropArt.Sways("plague.fern"), Is.True);
            Assert.That(RegionPropArt.Sways("plague.mushroom"), Is.False);
            var rect = fern.Sprite.textureRect;
            Assert.That((rect.y + rect.height) / RegionPropArt.Atlas.height, Is.LessThan(RegionPropArt.SwayV), "fern stays in the existing wind-capable atlas band");

            var atlasPixels = RegionPropArt.Atlas.GetPixels32();
            int x0 = Mathf.RoundToInt(rect.x), y0 = Mathf.RoundToInt(rect.y);
            int width = Mathf.RoundToInt(rect.width), height = Mathf.RoundToInt(rect.height), matched = 0;
            var seen = new HashSet<Color32>();
            for (int y = 0; y < height; y++) for (int x = 0; x < width; x++)
            {
                var pixel = atlasPixels[(y0 + y) * RegionPropArt.Atlas.width + x0 + x];
                if (pixel.a > 8 && sourceColors.Contains(pixel) && seen.Add(pixel)) matched++;
            }
            Assert.That(matched, Is.GreaterThanOrEqualTo(8), "multiple exact source pigment colours reach the atlas crop");

            Assert.That(DecorationKinds.ArtKey(25, 0, 0), Is.EqualTo("plague.fern"));
            Assert.That(DecorationKinds.ArtKey(28, 0, 0), Is.EqualTo("plague.fern"), "plague.reed remains the public fern alias");
            Assert.That(DecorationKinds.ArtKey(30, 0, 0), Is.EqualTo("plague.fern"), "plague.sedge remains the public fern alias");
            Assert.That(DecorationKinds.ArtKey(28, 0, MapBlob.PropFlagLegacy), Is.EqualTo(DecorationKinds.Placeholder));
        }

        [Test]
        public void AuthoredFernAliasesVaryWithoutMovingDeliveredFeetOrMapRows()
        {
            const int count = 24, yTile = 24;
            var props = new MapProp[count + 1];
            for (int i = 0; i < count; i++)
            {
                ushort x = (ushort)((8 + i * 2) * 32);
                byte kind = (byte)(i % 3 == 0 ? 25 : i % 3 == 1 ? 28 : 30);
                props[i] = new MapProp(kind, x, yTile * 32, (byte)(i % 3), 0);
            }
            props[count] = new MapProp(26, 56 * 32, yTile * 32, 0, 0);
            var map = new MapBlob(64, 64, new byte[64 * 64]).WithPublic(null, null, null, props, null);
            var placements = RegionProps.Scatter(map);
            Assert.That(placements.Count, Is.EqualTo(props.Length));
            for (int i = 0; i < count; i++)
            {
                Assert.That(placements[i].Art, Is.EqualTo("plague.fern"));
                Assert.That(placements[i].X, Is.EqualTo(props[i].X / 32f));
                Assert.That(placements[i].Y, Is.EqualTo(props[i].Y / 32f));
                Assert.That(placements[i].Kind, Is.EqualTo(props[i].Kind));
                Assert.That(placements[i].Variant, Is.EqualTo(props[i].Variant));
            }

            var parentA = new GameObject("plague fern variation A");
            var parentB = new GameObject("plague fern variation B");
            RegionProps first = null, second = null;
            try
            {
                first = RegionProps.Build(parentA.transform, map);
                second = RegionProps.Build(parentB.transform, map);
                var a = parentA.GetComponentsInChildren<SpriteRenderer>();
                var b = parentB.GetComponentsInChildren<SpriteRenderer>();
                Assert.That(a.Length, Is.EqualTo(props.Length)); Assert.That(b.Length, Is.EqualTo(props.Length));
                int flipped = 0, unflipped = 0; var tints = new HashSet<Color32>();
                float minHeight = float.MaxValue, maxHeight = float.MinValue;
                for (int i = 0; i < count; i++)
                {
                    Assert.That(a[i].transform.localScale.y, Is.InRange(.88f, 1f));
                    Assert.That(Mathf.Abs(a[i].transform.localScale.x), Is.InRange(.84f, .98f));
                    Assert.That(a[i].color.g, Is.GreaterThan(a[i].color.r), "fern pigment stays muted green");
                    minHeight = Mathf.Min(minHeight, a[i].transform.localScale.y); maxHeight = Mathf.Max(maxHeight, a[i].transform.localScale.y);
                    if (a[i].transform.localScale.x < 0) flipped++; else unflipped++;
                    tints.Add((Color32)a[i].color);
                    Assert.That(Vector3.Distance(a[i].transform.localPosition, ArenaView.WorldPoint(placements[i].X * 32f, placements[i].Y * 32f)), Is.LessThan(.001f));
                    Assert.That(a[i].transform.localRotation, Is.EqualTo(Quaternion.identity));
                    Assert.That(a[i].sortingOrder, Is.EqualTo(2000 + Mathf.RoundToInt(placements[i].Y * 16f) + placements[i].Bias));
                    Assert.That(b[i].transform.localScale, Is.EqualTo(a[i].transform.localScale));
                    Assert.That(b[i].color, Is.EqualTo(a[i].color));
                }
                Assert.That(flipped, Is.GreaterThan(0)); Assert.That(unflipped, Is.GreaterThan(0));
                Assert.That(maxHeight - minHeight, Is.GreaterThan(.02f));
                Assert.That(tints.Count, Is.GreaterThan(6));
                Assert.That(a[count].transform.localScale, Is.EqualTo(Vector3.one), "procedural fallback art stays unchanged");
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
