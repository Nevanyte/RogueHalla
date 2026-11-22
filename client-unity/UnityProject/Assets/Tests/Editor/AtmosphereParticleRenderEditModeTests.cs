using NUnit.Framework;
using UnityEngine;

namespace Roguehalla.Client.Tests
{
    /// <summary>Guards the world-space placement and Universal2D transparent material used by pooled atmospheric strokes.</summary>
    public sealed class AtmosphereParticleRenderEditModeTests
    {
        static void AssertVisibleMaterialAndCameraPlacement(ParticleSystemRenderer[] renderers, Camera camera)
        {
            int active = 0;
            var view = new Bounds(camera.transform.position, new Vector3(camera.orthographicSize * camera.aspect * 2f, camera.orthographicSize * 2f, 100f));
            foreach (var renderer in renderers)
            {
                if (!renderer.gameObject.activeInHierarchy) continue;
                active++;
                Assert.AreEqual("Roguehalla/Atmosphere/Particles2D", renderer.sharedMaterial.shader.name);
                Assert.AreEqual((int)UnityEngine.Rendering.RenderQueue.Transparent, renderer.sharedMaterial.renderQueue);
                Assert.IsNotNull(renderer.sharedMaterial.mainTexture);
                Assert.Less(Vector2.Distance(new Vector2(renderer.transform.position.x, renderer.transform.position.y),
                        new Vector2(camera.transform.position.x, camera.transform.position.y)), .01f,
                    "the emitter transform follows the current camera center");
                Assert.IsTrue(renderer.bounds.Intersects(view), "the live particle bounds overlap the camera frustum");
            }
            Assert.Greater(active, 0);
        }

        [Test]
        public void PooledBrushesUseTransparentUniversal2DAndResetAfterCameraMoves()
        {
            AtmosphereQuality.ApplyPreset(ShowcaseQuality.High, false);
            var owner = new GameObject("atmosphere renderer oracle");
            var cameraGo = new GameObject("atmosphere renderer camera");
            var camera = cameraGo.AddComponent<Camera>(); camera.orthographic = true; camera.orthographicSize = 9f;
            var particles = new AtmosphereParticles();
            try
            {
                ShowcaseSprites.Initialize();
                particles.Initialize(owner.transform);
                cameraGo.transform.position = new Vector3(37f, -52f, -20f);
                particles.SetPreset(ParticlePresetId.Snow, true);
                Assert.Greater(particles.ParticleCount, 0, "initial prewarm fills the pool before first camera tick");
                particles.Tick(camera, 0f);
                AssertVisibleMaterialAndCameraPlacement(owner.GetComponentsInChildren<ParticleSystemRenderer>(true), camera);

                particles.SetPreset(ParticlePresetId.Embers, true); // preset changes before the next camera follow, as on a region crossing
                cameraGo.transform.position = new Vector3(20f, -53.1f, -20f);
                particles.Tick(camera, 0f);
                particles.Tick(camera, AtmosphereParticles.FadeSeconds);
                Assert.AreEqual(ParticlePresetId.Embers, particles.Playing);
                AssertVisibleMaterialAndCameraPlacement(owner.GetComponentsInChildren<ParticleSystemRenderer>(true), camera);
            }
            finally
            {
                particles.Dispose(); Object.DestroyImmediate(cameraGo); Object.DestroyImmediate(owner);
                AtmosphereQuality.ApplyPreset(ShowcaseQuality.High, false);
            }
        }
    }
}
