using NUnit.Framework;
using UnityEngine;

namespace Roguehalla.Client.Tests
{
    /// <summary>Texture profile and quality guards for the pooled snow/ember brush marks.</summary>
    public sealed class AtmosphereBrushProfileEditModeTests
    {
        static Texture2D Readback(Texture2D source)
        {
            var previous = RenderTexture.active;
            var target = RenderTexture.GetTemporary(source.width, source.height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
            var copy = new Texture2D(source.width, source.height, TextureFormat.RGBA32, false, true);
            try
            {
                Graphics.Blit(source, target);
                RenderTexture.active = target;
                copy.ReadPixels(new Rect(0, 0, source.width, source.height), 0, 0, false);
                copy.Apply(false, false);
                return copy;
            }
            catch
            {
                Object.DestroyImmediate(copy);
                throw;
            }
            finally
            {
                RenderTexture.active = previous;
                RenderTexture.ReleaseTemporary(target);
            }
        }

        static RectInt AlphaBounds(Texture2D texture, float threshold, out Vector2 centroid, out int count)
        {
            var pixels = texture.GetPixels32();
            int x0 = texture.width, y0 = texture.height, x1 = -1, y1 = -1;
            long sx = 0, sy = 0; count = 0;
            for (int y = 0; y < texture.height; y++) for (int x = 0; x < texture.width; x++)
            {
                if (pixels[y * texture.width + x].a / 255f < threshold) continue;
                x0 = Mathf.Min(x0, x); y0 = Mathf.Min(y0, y); x1 = Mathf.Max(x1, x); y1 = Mathf.Max(y1, y);
                sx += x; sy += y; count++;
            }
            Assert.Greater(count, 0, texture.name + " has a visible alpha mask");
            centroid = new Vector2((float)sx / count, (float)sy / count);
            return new RectInt(x0, y0, x1 - x0 + 1, y1 - y0 + 1);
        }

        [TestCase("flake")]
        [TestCase("ember")]
        public void SnowAndEmberTexturesReadAsLongAsymmetricBrushMarks(string key)
        {
            var readable = Readback(AtmosphereTextures.Get(key));
            try
            {
                RectInt bounds = AlphaBounds(readable, .12f, out Vector2 centroid, out _);
                float aspect = bounds.height / (float)bounds.width;
                Assert.Greater(aspect, 1.25f, key + " should read as an upright wind-drawn stroke, not a round mote");
                Assert.Greater(Mathf.Abs(centroid.x - (readable.width - 1) * .5f), 1.5f,
                    key + " should have an asymmetric swept silhouette");

                int y = Mathf.RoundToInt(centroid.y);
                int variedRows = 0;
                for (int x = bounds.xMin + 2; x < bounds.xMax - 2; x++)
                {
                    int a = readable.GetPixel(x, y).a > .18f ? 1 : 0;
                    int b = readable.GetPixel(x + 1, y).a > .18f ? 1 : 0;
                    if (a != b) variedRows++;
                }
                Assert.Greater(variedRows, 1, key + " retains dry-brush edge variation");
            }
            finally { Object.DestroyImmediate(readable); }
        }

        [Test]
        public void BrushPassKeepsExistingPooledLayersAndQualityBudget()
        {
            var snow = ParticlePresets.Get(ParticlePresetId.Snow);
            var fire = ParticlePresets.Get(ParticlePresetId.Embers);
            Assert.AreEqual(4, snow.Length, "snow still uses its existing four pooled layers");
            Assert.AreEqual(3, fire.Length, "embers still use their existing three pooled layers");
            Assert.AreEqual("flake", snow[0].Texture); Assert.AreEqual("flake", snow[1].Texture); Assert.AreEqual("flake", snow[2].Texture);
            Assert.AreEqual("ember", fire[0].Texture); Assert.AreEqual("ember", fire[1].Texture);
            Assert.AreEqual(14, LifeLights.Cap, "living glow sprites keep their fixed pool cap");
            Assert.AreEqual(9, LifeLights.MaxFireflies); Assert.AreEqual(6, LifeLights.MaxEmbers);
            Assert.IsTrue(fire[0].FixedRotation && fire[1].FixedRotation, "loft strokes stay upright as their particles rise");
            Assert.LessOrEqual(ParticlePresets.Budget(ParticlePresetId.Snow, 1f), ParticlePresets.HardBudget);
            Assert.Less(ParticlePresets.Budget(ParticlePresetId.Snow, .55f), ParticlePresets.Budget(ParticlePresetId.Snow, 1f));
            Assert.Less(ParticlePresets.Budget(ParticlePresetId.Embers, .55f), ParticlePresets.Budget(ParticlePresetId.Embers, 1f));
            Assert.LessOrEqual(snow[2].Tint.a, .8f, "above-actor snow strokes remain translucent");
            Assert.LessOrEqual(snow[2].SizeMax, .5f, "above-actor snow strokes stay small");
        }

        [Test]
        public void ExistingParticleAndLivingLightPoolsRespectReducedMotion()
        {
            AtmosphereQuality.ApplyPreset(ShowcaseQuality.High, false);
            var owner = new GameObject("brush pool oracle");
            var screen = new AtmosphereParticles(); var lights = new LifeLights();
            var cameraGo = new GameObject("brush pool camera"); var camera = cameraGo.AddComponent<Camera>();
            try
            {
                screen.Initialize(owner.transform); lights.Initialize(owner.transform);
                Assert.AreEqual(16, owner.transform.GetChild(0).childCount, "two banks retain four pooled layers and four anchors apiece");
                Assert.AreEqual(LifeLights.Cap, owner.transform.GetChild(1).childCount, "living lights retain their fixed sprite pool");
                screen.SetPreset(ParticlePresetId.Snow, true);
                Assert.AreEqual(ParticlePresetId.Snow, screen.Playing);
                Assert.Greater(screen.ParticleCount, 0);

                AtmosphereQuality.ApplyPreset(ShowcaseQuality.High, true);
                screen.RefreshQuality(); screen.Tick(camera, AtmosphereParticles.FadeSeconds);
                Assert.AreEqual(ParticlePresetId.None, screen.Playing, "reduced motion disables screen particles through the existing switchboard");
                Assert.AreEqual(0, screen.ParticleCount);
                Assert.AreEqual(LifeLights.Cap, owner.transform.GetChild(1).childCount, "turning particles off does not grow or rebuild living-light objects");
            }
            finally
            {
                screen.Dispose(); lights.Dispose(); Object.DestroyImmediate(cameraGo); Object.DestroyImmediate(owner);
                AtmosphereQuality.ApplyPreset(ShowcaseQuality.High, false);
            }
        }
    }
}
