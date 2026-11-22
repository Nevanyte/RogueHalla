using System.Linq;
using NUnit.Framework;
using Roguehalla.Net.Protocol;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace Roguehalla.Client.Tests
{
    public sealed class ColiseumTorchArtEditModeTests
    {
        [Test]
        public void DeliveredTorchUsesItsAuthoredBodyOneFlameAndTheSameMirroredSocketForPooledSpill()
        {
            var art = ColiseumTorchArt.Load();
            Assert.That(art, Is.Not.Null);
            Assert.That(art.IsReady, Is.True, "the authored body and separate flame are both required");
            Assert.That(art.Pigment.isReadable, Is.True);
            Assert.That(art.Flame, Is.Not.Null);

            var map = ColiseumMapWithTorch();
            var placements = RegionProps.Scatter(map).Where(p => p.Art == "col.torch").ToArray();
            Assert.That(placements.Length, Is.GreaterThan(0), "the map must place at least one real Coliseum torch");
            Assert.That(art.TryGetPlacedSocketOffset(1f, false, out var unmirrored), Is.True);
            Assert.That(art.TryGetPlacedSocketOffset(1f, true, out var mirrored), Is.True);
            Assert.That(mirrored.x, Is.EqualTo(-unmirrored.x).Within(.0001f));
            Assert.That(mirrored.y, Is.EqualTo(unmirrored.y).Within(.0001f));

            var root = new GameObject("Coliseum authored torch placement test");
            try
            {
                using var props = RegionProps.Build(root.transform, map);
                var bodies = root.GetComponentsInChildren<SpriteRenderer>(true).Where(r => r.gameObject.name == "col.torch").ToArray();
                var flames = root.GetComponentsInChildren<ChurchCandleFlameLayer>(true);
                Assert.That(bodies.Length, Is.EqualTo(placements.Length));
                Assert.That(flames.Length, Is.EqualTo(placements.Length), "one overlay flame per authored torch");
                Assert.That(root.GetComponentsInChildren<Light2D>(true), Is.Empty, "torch props do not own per-prop Unity lights");

                Assert.That(RegionPropArt.TryGet("col.torch", out var torchSprite), Is.True);
                Assert.That(torchSprite.UsesAuthoredTexture, Is.True);
                var expectedOffset = new Vector2[placements.Length];
                for (int i = 0; i < placements.Length; i++)
                {
                    Assert.That(bodies[i].sprite, Is.SameAs(torchSprite.Sprite));
                    Assert.That(bodies[i].transform.localScale.x, Is.EqualTo(placements[i].Flip ? -1f : 1f).Within(.001f));
                    Assert.That(art.TryGetPlacedSocketOffset(placements[i].Scale, placements[i].Flip, out var reflected), Is.True);
                    expectedOffset[i] = reflected;
                    var layer = bodies[i].GetComponentInChildren<ChurchCandleFlameLayer>(true);
                    Assert.That(layer, Is.Not.Null);
                    Assert.That(layer.Renderer.sprite, Is.SameAs(art.Flame));
                    Assert.That(layer.Renderer.sharedMaterial, Is.SameAs(ShowcaseSprites.Unlit), "flame is bright at Low and avoids atlas-specific wind/ink UV semantics");
                    Assert.That(layer.Renderer.sortingOrder, Is.EqualTo(bodies[i].sortingOrder + 1));
                    Assert.That(art.TryGetFittedSocketOffset(out var local), Is.True);
                    Vector3 actualWorld = layer.transform.position;
                    Vector3 expectedWorld = bodies[i].transform.TransformPoint(new Vector3(local.x, local.y, -.01f));
                    Assert.That(Vector3.Distance(actualWorld, expectedWorld), Is.LessThan(.0001f));
                }

                var lights = new AccentLights();
                lights.Load(map);
                var authored = lights.Planner.Clusters.Where(c => c.AuthoredLamp).ToArray();
                Assert.That(authored.Length, Is.EqualTo(placements.Length), "each real torch joins the existing paged lamp planner");
                for (int i = 0; i < placements.Length; i++)
                {
                    Assert.That(authored[i].X, Is.EqualTo(placements[i].X + expectedOffset[i].x).Within(.0001f));
                    Assert.That(authored[i].Y, Is.EqualTo(placements[i].Y).Within(.0001f));
                    Assert.That(authored[i].Lift, Is.EqualTo(expectedOffset[i].y).Within(.0001f));
                    Assert.That(authored[i].LightColor, Is.EqualTo(art.LightColor));
                    Assert.That(authored[i].LightIntensity, Is.EqualTo(art.LightIntensity));
                    Assert.That(authored[i].LightRadius, Is.EqualTo(art.LightRadius));
                    Assert.That(AccentLights.GainFor(authored[i]), Is.EqualTo(art.SpillIntensity));
                    AccentLights.Shape(authored[i], out float height, out int cookie, out bool shadow);
                    Assert.That(height, Is.EqualTo(authored[i].Lift));
                    Assert.That(cookie, Is.EqualTo(LocalLightField.CookieGlass));
                    Assert.That(shadow, Is.True);
                }
            }
            finally { Object.DestroyImmediate(root); }
        }

        [Test]
        public void MissingBodyOrFlameKeepsTheOriginalBakedTorchAndItsLegacySocket()
        {
            var art = ColiseumTorchArt.Load();
            Assert.That(art, Is.Not.Null);
            var serialized = new SerializedObject(art);
            var pigment = serialized.FindProperty("Pigment");
            var flame = serialized.FindProperty("Flame");
            var originalPigment = pigment.objectReferenceValue;
            var originalFlame = flame.objectReferenceValue;
            var map = ColiseumMapWithTorch();
            try
            {
                flame.objectReferenceValue = null;
                serialized.ApplyModifiedPropertiesWithoutUndo();
                Assert.That(art.IsReady, Is.False);
                Assert.That(RegionPropArt.TryGet("col.torch", out var noFlame), Is.True);
                Assert.That(noFlame.UsesAuthoredTexture, Is.False);
                var fallbackLights = new AccentLights();
                fallbackLights.Load(map);
                var fallbackTorchLights = fallbackLights.Planner.Clusters.Where(c => c.TempleLamp && !c.AuthoredLamp).ToArray();
                var torchPlacements = RegionProps.Scatter(map).Where(p => p.Art == "col.torch").ToArray();
                Assert.That(fallbackTorchLights.Length, Is.EqualTo(torchPlacements.Length));
                Assert.That(fallbackTorchLights.All(c => Mathf.Approximately(c.Lift, 82f / RegionPropArt.PixelsPerUnit)), Is.True,
                    "missing authored art retains the original baked flame and its legacy light height");

                serialized.Update();
                serialized.FindProperty("Flame").objectReferenceValue = originalFlame;
                serialized.FindProperty("Pigment").objectReferenceValue = null;
                serialized.ApplyModifiedPropertiesWithoutUndo();
                Assert.That(art.IsReady, Is.False);
                Assert.That(RegionPropArt.TryGet("col.torch", out var noBody), Is.True);
                Assert.That(noBody.UsesAuthoredTexture, Is.False);
            }
            finally
            {
                serialized.Update();
                serialized.FindProperty("Pigment").objectReferenceValue = originalPigment;
                serialized.FindProperty("Flame").objectReferenceValue = originalFlame;
                serialized.ApplyModifiedPropertiesWithoutUndo();
            }
        }

        [Test]
        public void ReducedMotionReturnsTorchFlameToItsStillBasePose()
        {
            var art = ColiseumTorchArt.Load();
            Assert.That(art, Is.Not.Null);
            Assert.That(art.Flame, Is.Not.Null);
            var root = new GameObject("reduced motion torch");
            var body = root.AddComponent<SpriteRenderer>();
            body.sprite = art.Flame;
            var flameObject = new GameObject("flame");
            flameObject.transform.SetParent(root.transform, false);
            var layer = flameObject.AddComponent<ChurchCandleFlameLayer>();
            layer.ConfigureAtLocalOffset(body, art.Flame, new Vector2(.1f, .8f),
                new Vector2(art.FlameWidthWorldUnits, art.FlameHeightWorldUnits), art.FlameTint,
                null, .7f, 5);
            Vector3 baseScale = flameObject.transform.localScale;
            var reduced = typeof(AtmosphereQuality).GetField("<ReducedMotion>k__BackingField", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
            bool previous = (bool)reduced.GetValue(null);
            try
            {
                reduced.SetValue(null, true);
                flameObject.transform.localScale = baseScale * 1.3f;
                flameObject.transform.localRotation = Quaternion.Euler(0f, 0f, 12f);
                typeof(ChurchCandleFlameLayer).GetMethod("LateUpdate", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                    .Invoke(layer, null);
                Assert.That(flameObject.transform.localScale, Is.EqualTo(baseScale));
                Assert.That(Quaternion.Angle(flameObject.transform.localRotation, Quaternion.identity), Is.LessThan(.001f));
            }
            finally
            {
                reduced.SetValue(null, previous);
                Object.DestroyImmediate(root);
            }
        }

        static MapBlob ColiseumMapWithTorch()
        {
            const int size = 40;
            var regions = Enumerable.Repeat((byte)RegionId.Coliseum, size * size).ToArray();
            var tiles = new byte[size * size];
            for (int y = 0; y < size; y++) for (int x = 0; x < size; x++)
                if (x == 0 || y == 0 || x == size - 1 || y == size - 1) tiles[y * size + x] = MapBlob.Wall;
            // A few inset wall blocks produce the same kinds of side-mounted placements as the live arena.
            for (int y = 8; y < size - 8; y += 8)
            for (int x = 8; x < size - 8; x += 8)
                tiles[y * size + x] = MapBlob.Wall;
            return new MapBlob(size, size, tiles, null, regions);
        }
    }
}
