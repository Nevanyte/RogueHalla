using System.Reflection;
using NUnit.Framework;
using Roguehalla.Net.Protocol;
using Roguehalla.Net.Ui;
using UnityEngine;

namespace Roguehalla.Client.Tests
{
    public sealed class StormInkFrostPineEditModeTests
    {
        [Test]
        public void PineAAndWindPineUseTheirOwnEditableSourcesInTheSharedAtlas()
        {
            var a = Resources.Load<StormInkPropArtDefinition>("StormInk/Props/FrostPineArt");
            var b = Resources.Load<StormInkPropArtDefinition>("StormInk/Props/FrostWindPineArt");
            Assert.That(a, Is.Not.Null);
            Assert.That(b, Is.Not.Null);
            Assert.That(a.Pigment, Is.Not.Null);
            Assert.That(a.Pigment.isReadable, Is.True);
            Assert.That(b.Pigment, Is.Not.Null);
            Assert.That(b.Pigment.isReadable, Is.True);

            Assert.That(RegionPropArt.TryGet("frost.pine", out var pineA), Is.True);
            Assert.That(RegionPropArt.TryGet("frost.pine2", out var pineB), Is.True);
            Assert.That(pineA.UsesAuthoredTexture, Is.True);
            Assert.That(pineB.UsesAuthoredTexture, Is.True);
            Assert.That(pineA.Sprite.texture, Is.SameAs(RegionPropArt.Atlas));
            Assert.That(pineB.Sprite.texture, Is.SameAs(RegionPropArt.Atlas));
            Assert.That(pineA.Sprite, Is.Not.SameAs(pineB.Sprite));
            Assert.That(pineA.Tall, Is.True);
            Assert.That(pineB.Tall, Is.True);
            Assert.That(RegionPropArt.Sways("frost.pine"), Is.True);
            Assert.That(RegionPropArt.Sways("frost.pine2"), Is.True);
            Assert.That(pineA.Width, Is.EqualTo(a.WidthWorldUnits).Within(.02f));
            Assert.That(pineB.Width, Is.EqualTo(b.WidthWorldUnits).Within(.02f));
            Assert.That(pineA.Height, Is.EqualTo(a.HeightWorldUnits - a.GroundWorldUnits).Within(.02f));
            Assert.That(pineB.Height, Is.EqualTo(b.HeightWorldUnits - b.GroundWorldUnits).Within(.02f));
            Assert.That(pineA.MinMargin, Is.GreaterThanOrEqualTo(RegionPropArt.RequiredMargin));
            Assert.That(pineB.MinMargin, Is.GreaterThanOrEqualTo(RegionPropArt.RequiredMargin));
        }

        [Test]
        public void ArtistSizeEditChangesPaintedBoundsOnNextLookupAndLeavesOldSpriteAlive()
        {
            var definition = Resources.Load<StormInkPropArtDefinition>("StormInk/Props/FrostPineArt");
            Assert.That(RegionPropArt.TryGet("frost.pine", out var original), Is.True);
            Assert.That(original.UsesAuthoredTexture, Is.True);
            var oldWidth = definition.WidthWorldUnits;
            var oldHeight = definition.HeightWorldUnits;
            float width = Mathf.Clamp(oldWidth * .8f, .5f, 3f);
            float height = Mathf.Clamp(oldHeight * .8f, .35f, 4f);
            var onValidate = typeof(StormInkPropArtDefinition).GetMethod("OnValidate", BindingFlags.Instance | BindingFlags.NonPublic);
            try
            {
                definition.WidthWorldUnits = width;
                definition.HeightWorldUnits = height;
                onValidate.Invoke(definition, null);
                Assert.That(RegionPropArt.TryGet("frost.pine", out var edited), Is.True);
                Assert.That(edited.Width, Is.EqualTo(width).Within(.02f));
                Assert.That(edited.Height, Is.EqualTo(height - definition.GroundWorldUnits).Within(.02f));
                Assert.That(edited.PixelWidth, Is.LessThan(original.PixelWidth));
                Assert.That(edited.PixelHeight, Is.LessThan(original.PixelHeight));
                Assert.That(edited.Sprite, Is.Not.SameAs(original.Sprite));
                Assert.That(original.Sprite, Is.Not.Null, "live views may still hold the preceding atlas sprite");
            }
            finally
            {
                definition.WidthWorldUnits = oldWidth;
                definition.HeightWorldUnits = oldHeight;
                onValidate.Invoke(definition, null);
                RegionPropArt.TryGet("frost.pine", out _);
            }
        }

        [Test]
        public void MissingPineSourceRetainsProceduralArtAndFrostSpireDelivery()
        {
            Assert.That(DecorationKinds.ArtKey(81, 0, 0), Is.EqualTo("frost.pine2"));
            Assert.That(RegionPropArt.TryGet("frost.spire.fallback", out var fallback), Is.True);
            Assert.That(fallback.UsesAuthoredTexture, Is.False);
            Assert.That(fallback.Tall, Is.True);
            Assert.That(RegionPropArt.Sways("frost.spire.fallback"), Is.True);

            var pine = Resources.Load<StormInkPropArtDefinition>("StormInk/Props/FrostPineArt");
            var source = pine.Pigment;
            var onValidate = typeof(StormInkPropArtDefinition).GetMethod("OnValidate", BindingFlags.Instance | BindingFlags.NonPublic);
            try
            {
                pine.Pigment = null;
                onValidate.Invoke(pine, null);
                Assert.That(RegionPropArt.TryGet("frost.pine", out var procedural), Is.True);
                Assert.That(procedural.UsesAuthoredTexture, Is.False);
                Assert.That(procedural.Width, Is.EqualTo(112f / RegionPropArt.PixelsPerUnit).Within(.02f));
            }
            finally
            {
                pine.Pigment = source;
                onValidate.Invoke(pine, null);
                RegionPropArt.TryGet("frost.pine", out _);
            }
            Assert.That(RegionPropArt.TryGet("frost.spire.fallback", out fallback), Is.True);

            var props = new[] { new MapProp(81, 320, 320, 0, 1) };
            var map = new MapBlob(20, 20, new byte[400]).WithPublic(null, null, null, props, null);
            var root = new GameObject("Frost spire compatibility fixture");
            RegionProps scene = null;
            try
            {
                scene = RegionProps.Build(root.transform, map);
                var renderers = root.GetComponentsInChildren<SpriteRenderer>();
                Assert.That(renderers.Length, Is.EqualTo(1));
                Assert.That(renderers[0].sprite, Is.SameAs(fallback.Sprite));
                Assert.That(RegionProps.Scatter(map)[0].Art, Is.EqualTo("frost.pine2"));
            }
            finally
            {
                scene?.Dispose();
                Object.DestroyImmediate(root);
            }
        }
    }
}
