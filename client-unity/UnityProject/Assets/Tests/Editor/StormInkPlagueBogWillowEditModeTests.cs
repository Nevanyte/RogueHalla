using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Roguehalla.Client;
using Roguehalla.Net.Protocol;
using UnityEngine;

namespace Roguehalla.Client.Tests
{
    public sealed class StormInkPlagueBogWillowEditModeTests
    {
        const int Width = 24, Height = 20;

        [Test]
        public void MixedDeliveredCoverAndDecorativeBushesKeepTheirRowsAndOnlyPlagueGetsWillowArt()
        {
            var map = MixedMap(out var originalBushes);
            var placements = RegionProps.Scatter(map);

            var plagueCover = Cover(placements, 8, 8);
            var hauntedCover = Cover(placements, 15, 8);
            var fireCover = Cover(placements, 18, 8);
            var neutralCover = Cover(placements, 21, 8);
            var frostCover = Cover(placements, 20, 12);
            var plagueDecor = placements.Single(p => p.Shipped && p.Kind == 24 && p.Tx == 9 && p.Ty == 10);
            var hauntedDecor = placements.Single(p => p.Shipped && p.Kind == 24 && p.Tx == 16 && p.Ty == 10);

            AssertPlacement(plagueCover, "plague.bush", 8, 8, false, 0);
            AssertPlacement(hauntedCover, "plague.bush", 15, 8, false, 0);
            AssertPlacement(fireCover, "fire.bush", 18, 8, false, 0);
            AssertPlacement(neutralCover, "plague.bush", 21, 8, false, 0);
            AssertPlacement(plagueDecor, "plague.bush", 9, 10, true, 0);
            AssertPlacement(hauntedDecor, "plague.bush", 16, 10, true, 0);

            Assert.That(map.Bushes, Is.EqualTo(originalBushes), "placing the willow art must not change Rust's delivered concealment plane");
            Assert.That(map.Bushes[8 * Width + 8], Is.EqualTo(23), "the Plague cell remains the same cover id for movement and concealment");

            var willow = Willow();
            var root = new GameObject("Mixed Plague bog willow scene");
            RegionProps scene = null;
            try
            {
                scene = RegionProps.Build(root.transform, map);
                var sprites = root.GetComponentsInChildren<SpriteRenderer>();
                var plagueCoverSprite = At(root, plagueCover);
                var plagueDecorSprite = At(root, plagueDecor);
                var hauntedCoverSprite = At(root, hauntedCover);
                var hauntedDecorSprite = At(root, hauntedDecor);
                var fireSprite = At(root, fireCover);
                var neutralSprite = At(root, neutralCover);
                var frostSprite = At(root, frostCover);

                Assert.That(plagueCoverSprite.sprite, Is.SameAs(willow.Sprite));
                Assert.That(plagueDecorSprite.sprite, Is.SameAs(willow.Sprite));
                Assert.That(RegionPropArt.TryGet("sent.blackthorn.bush", out var blackthorn), Is.True);
                Assert.That(blackthorn.UsesAuthoredTexture, Is.True);
                Assert.That(hauntedCoverSprite.sprite, Is.SameAs(blackthorn.Sprite));
                Assert.That(hauntedDecorSprite.sprite, Is.SameAs(blackthorn.Sprite));
                Assert.That(neutralSprite.sprite.name, Is.EqualTo("plague.bush"));
                Assert.That(RegionPropArt.TryGet("frost.bush", out var juniper), Is.True);
                Assert.That(juniper.UsesAuthoredTexture, Is.True);
                Assert.That(frostSprite.sprite, Is.SameAs(juniper.Sprite));
                Assert.That(juniper.MinMargin, Is.GreaterThanOrEqualTo(RegionPropArt.RequiredMargin));
                Assert.That(fireSprite.sprite.name, Is.EqualTo("fire.bush"), "the prior Fire art/fallback binding remains independent");

                Vector3 plagueFoot = ArenaView.WorldPoint((plagueCover.X) * 32f, (plagueCover.Y) * 32f);
                Assert.That(plagueCoverSprite.transform.localPosition, Is.EqualTo(plagueFoot));
                var canopy = Resources.Load<ConcealmentCanopyArt>("StormInk/Props/ConcealmentCanopyArt");
                Assert.That(canopy.HeightScale, Is.GreaterThan(1f));
                Assert.That(canopy.WidthScale, Is.GreaterThan(1f));
                foreach (var cover in new[] { plagueCoverSprite, hauntedCoverSprite, neutralSprite, frostSprite })
                {
                    Assert.That(Mathf.Abs(cover.transform.localScale.x), Is.EqualTo(canopy.WidthScale));
                    Assert.That(cover.transform.localScale.y, Is.EqualTo(canopy.HeightScale));
                }
                Assert.That(plagueDecorSprite.transform.localScale, Is.EqualTo(Vector3.one));
                Assert.That(hauntedDecorSprite.transform.localScale, Is.EqualTo(Vector3.one));
                Assert.That(plagueCoverSprite.sortingOrder, Is.EqualTo(2000 + Mathf.RoundToInt((plagueCover.Y + canopy.ForegroundDepth) * 16f)));
                Assert.That(plagueDecorSprite.sortingOrder, Is.EqualTo(2000 + Mathf.RoundToInt(plagueDecor.Y * 16f)));
                Assert.That(plagueCoverSprite.color.a, Is.EqualTo(1f), "cover art remains a low shrub, not a tall actor-fading prop");
                Assert.That(sprites.Length, Is.EqualTo(placements.Count), "the new art adds no separate ground-shadow sprites");
                Assert.That(sprites.Select(s => s.sharedMaterial).Distinct().Count(), Is.EqualTo(1), "all foliage stays on the existing shared lit material");
                var response = new MaterialPropertyBlock(); plagueCoverSprite.GetPropertyBlock(response);
                Assert.That(response.GetFloat(Shader.PropertyToID("_StormInkResponse")), Is.EqualTo(1f), "authored willow uses the existing matte response with Unity lighting");
            }
            finally
            {
                scene?.Dispose();
                Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void WillowPigmentUsesEditableAtlasBoundsAndWindFamily()
        {
            var definition = Resources.Load<StormInkPropArtDefinition>("StormInk/Props/PlagueBogWillowArt");
            Assert.That(definition, Is.Not.Null);
            Assert.That(definition.Pigment, Is.Not.Null);
            Assert.That(definition.Pigment.isReadable, Is.True);
            var willow = Willow();
            Assert.That(willow.UsesAuthoredTexture, Is.True);
            Assert.That(willow.Sprite.texture, Is.SameAs(RegionPropArt.Atlas));
            Assert.That(willow.Width, Is.EqualTo(definition.WidthWorldUnits).Within(.01f));
            Assert.That(willow.Tall, Is.False);
            Assert.That(willow.MinMargin, Is.GreaterThanOrEqualTo(RegionPropArt.RequiredMargin));
            Assert.That(RegionPropArt.Sways("plague.willow.bush"), Is.True,
                "the art key stays in the existing vegetation wind family");
        }

        [Test]
        public void MissingWillowPigmentFallsBackToProceduralPlagueBushAndRestoresTheEditableSource()
        {
            var definition = Resources.Load<StormInkPropArtDefinition>("StormInk/Props/PlagueBogWillowArt");
            Assert.That(definition, Is.Not.Null);
            var source = definition.Pigment;
            var map = MixedMap(out _);
            var root = new GameObject("Missing willow source fallback");
            RegionProps scene = null;
            try
            {
                definition.Pigment = null;
                Validate(definition);
                Assert.That(RegionPropArt.TryGet("plague.willow.bush", out var fallback), Is.True);
                Assert.That(fallback.UsesAuthoredTexture, Is.False);
                scene = RegionProps.Build(root.transform, map);
                Assert.That(At(root, Cover(RegionProps.Scatter(map), 8, 8)).sprite.name, Is.EqualTo("plague.bush"));
                Assert.That(At(root, RegionProps.Scatter(map).Single(p => p.Shipped && p.Kind == 24 && p.Tx == 9 && p.Ty == 10)).sprite.name,
                    Is.EqualTo("plague.bush"));
                Assert.That(At(root, Cover(RegionProps.Scatter(map), 15, 8)).sprite.name, Is.EqualTo("sent.blackthorn.bush"),
                    "Haunted retains its own painting while Plague art is unavailable");
            }
            finally
            {
                scene?.Dispose();
                Object.DestroyImmediate(root);
                definition.Pigment = source;
                Validate(definition);
                RegionPropArt.TryGet("plague.willow.bush", out _);
            }
        }

        static MapBlob MixedMap(out byte[] originalBushes)
        {
            var tiles = new byte[Width * Height];
            var regions = new byte[Width * Height];
            var bushes = new byte[Width * Height];
            regions[8 * Width + 8] = (byte)RegionId.Plague;
            regions[10 * Width + 9] = (byte)RegionId.Plague;
            regions[8 * Width + 15] = (byte)RegionId.Sentenced;
            regions[10 * Width + 16] = (byte)RegionId.Sentenced;
            regions[8 * Width + 18] = (byte)RegionId.Fire;
            bushes[8 * Width + 8] = 23;
            bushes[8 * Width + 15] = 9;
            bushes[8 * Width + 18] = 3;
            regions[12 * Width + 20] = (byte)RegionId.Frost;
            bushes[12 * Width + 20] = 12;
            bushes[8 * Width + 21] = 1; // no region: preserve the old neutral fallback
            originalBushes = (byte[])bushes.Clone();
            var props = new[]
            {
                new MapProp(24, 9 * 32 + 16, 10 * 32 + 16, 0, 0),
                new MapProp(24, 16 * 32 + 16, 10 * 32 + 16, 0, 0),
            };
            return new MapBlob(Width, Height, tiles, null, regions)
                .WithPublic(null, null, bushes, props, null);
        }

        static RegionProps.Placement Cover(System.Collections.Generic.List<RegionProps.Placement> rows, int x, int y) =>
            rows.Single(p => !p.Shipped && p.Tx == x && p.Ty == y);

        static void AssertPlacement(RegionProps.Placement p, string art, int tx, int ty, bool shipped, byte flags)
        {
            Assert.That(p.Art, Is.EqualTo(art));
            Assert.That(p.Tx, Is.EqualTo(tx)); Assert.That(p.Ty, Is.EqualTo(ty));
            Assert.That(p.Shipped, Is.EqualTo(shipped)); Assert.That(p.Flags, Is.EqualTo(flags));
            Assert.That(p.X, Is.EqualTo(tx + .5f)); Assert.That(p.Y, Is.EqualTo(ty + .5f));
            Assert.That(p.Scale, Is.EqualTo(1f));
        }

        static SpriteRenderer At(GameObject root, RegionProps.Placement p)
        {
            Vector3 point = ArenaView.WorldPoint(p.X * 32f, p.Y * 32f);
            return root.GetComponentsInChildren<SpriteRenderer>().Single(r => r.name == p.Art &&
                (r.transform.localPosition - point).sqrMagnitude < .0001f);
        }

        static RegionPropSprite Willow()
        {
            Assert.That(RegionPropArt.TryGet("plague.willow.bush", out var willow), Is.True);
            return willow;
        }

        static void Validate(StormInkPropArtDefinition definition) =>
            typeof(StormInkPropArtDefinition).GetMethod("OnValidate", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(definition, null);
    }
}
