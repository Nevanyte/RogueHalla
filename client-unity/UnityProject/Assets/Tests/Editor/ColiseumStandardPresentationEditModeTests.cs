using System.Linq;
using NUnit.Framework;
using Roguehalla.Client;
using Roguehalla.Net.Protocol;
using UnityEditor;
using UnityEngine;

namespace Roguehalla.Client.Tests
{
    public sealed class ColiseumStandardPresentationEditModeTests
    {
        const int BannerMapSize = 5;

        static MapBlob HolyBannerMap(byte region)
        {
            var regions = Enumerable.Repeat(region, BannerMapSize * BannerMapSize).ToArray();
            var map = new MapBlob(BannerMapSize, BannerMapSize, new byte[BannerMapSize * BannerMapSize], null, regions);
            return map.WithPublic(null, null, null, new[] { new MapProp(48, 48, 48, 0, 0) }, null);
        }

        static string ShippedBannerSpriteKey(MapBlob map)
        {
            var root = new GameObject("Coliseum standard test");
            try
            {
                using (RegionProps.Build(root.transform, map))
                    return root.GetComponentsInChildren<SpriteRenderer>().Single(r => r.transform.localPosition ==
                        ArenaView.WorldPoint(48f, 48f)).sprite.name;
            }
            finally { Object.DestroyImmediate(root); }
        }

        [Test]
        public void ColiseumProceduralStandardsUseEditableWindArtAndShippedHolyBannerKeepsItsKey()
        {
            var definition = Resources.Load<StormInkPropArtDefinition>("StormInk/Props/ColiseumStandardArt");
            Assert.That(definition, Is.Not.Null);
            Assert.That(definition.Pigment != null && definition.Pigment.isReadable, Is.True);
            Assert.That(RegionPropArt.TryGet("col.standard", out var standard), Is.True);
            Assert.That(standard.UsesAuthoredTexture, Is.True);
            Assert.That(standard.Tall, Is.True);
            Assert.That(standard.Width, Is.EqualTo(definition.WidthWorldUnits).Within(.001f));
            Assert.That(standard.Height, Is.EqualTo(definition.HeightWorldUnits - definition.GroundWorldUnits).Within(.001f));
            Assert.That(RegionPropArt.Sways("col.standard"), Is.True);
            Assert.That(RegionPropArt.TryGet("col.banner", out _), Is.True,
                "The original procedural key remains available to Holy's shipped banner kind.");

            // A shipped Holy kind stays Holy even if malformed placement puts it inside a Coliseum region.
            Assert.That(ShippedBannerSpriteKey(HolyBannerMap((byte)RegionId.Holy)), Is.EqualTo("col.banner"));
            Assert.That(ShippedBannerSpriteKey(HolyBannerMap((byte)RegionId.Coliseum)), Is.EqualTo("col.banner"));

            const int n = 40;
            var tiles = new byte[n * n];
            var regions = Enumerable.Repeat((byte)RegionId.Coliseum, n * n).ToArray();
            for (int y = 0; y < n; y++) for (int x = 0; x < n; x++)
                if (x == 0 || y == 0 || x == n - 1 || y == n - 1) tiles[y * n + x] = MapBlob.Wall;
            var arena = new MapBlob(n, n, tiles, null, regions);
            var placements = new System.Collections.Generic.List<RegionProps.Placement>();
            bool foundStandard = false;
            for (int seed = 0; seed < 32 && !foundStandard; seed++)
            {
                placements.Clear();
                ColiseumProps.Add(arena, placements, seed);
                foundStandard = placements.Any(p => p.Art == "col.standard");
            }
            Assert.That(foundStandard, Is.True, "The actual Coliseum placement path emits the new standard key.");
        }

        [Test]
        public void ColiseumStandardFallsBackToProceduralTallPaintingWhenPigmentIsUnauthored()
        {
            var definition = Resources.Load<StormInkPropArtDefinition>("StormInk/Props/ColiseumStandardArt");
            Assert.That(definition, Is.Not.Null);
            var serialized = new SerializedObject(definition);
            var pigmentProperty = serialized.FindProperty("Pigment");
            var original = pigmentProperty.objectReferenceValue;
            try
            {
                pigmentProperty.objectReferenceValue = null;
                serialized.ApplyModifiedPropertiesWithoutUndo();
                Assert.That(RegionPropArt.TryGet("col.standard", out var fallback), Is.True);
                Assert.That(fallback.UsesAuthoredTexture, Is.False);
                Assert.That(fallback.Tall, Is.True, "The fallback retains the arena's actor-fade behavior.");
            }
            finally
            {
                serialized.Update();
                serialized.FindProperty("Pigment").objectReferenceValue = original;
                serialized.ApplyModifiedPropertiesWithoutUndo();
            }
        }

        [Test]
        public void ColiseumStandardEditableFootSettingMovesAtlasPivotAndRestoresAuthoredDefault()
        {
            var definition = Resources.Load<StormInkPropArtDefinition>("StormInk/Props/ColiseumStandardArt");
            Assert.That(definition, Is.Not.Null);
            var serialized = new SerializedObject(definition);
            var footProperty = serialized.FindProperty("TextureFoot");
            var original = footProperty.vector2Value;
            try
            {
                footProperty.vector2Value = new Vector2(original.x, .2f);
                serialized.ApplyModifiedPropertiesWithoutUndo();
                Assert.That(RegionPropArt.TryGet("col.standard", out var raisedFoot), Is.True);
                float raisedPivot = raisedFoot.Sprite.pivot.y;

                footProperty.vector2Value = new Vector2(original.x, .014f);
                serialized.ApplyModifiedPropertiesWithoutUndo();
                Assert.That(RegionPropArt.TryGet("col.standard", out var lowFoot), Is.True);
                Assert.That(Mathf.Abs(raisedPivot - lowFoot.Sprite.pivot.y), Is.GreaterThan(2f),
                    "The editable source foot point must re-anchor the painted image through the atlas pivot.");
            }
            finally
            {
                serialized.Update();
                serialized.FindProperty("TextureFoot").vector2Value = original;
                serialized.ApplyModifiedPropertiesWithoutUndo();
            }
        }
    }
}
