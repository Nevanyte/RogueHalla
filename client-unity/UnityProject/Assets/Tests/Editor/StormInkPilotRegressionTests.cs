using NUnit.Framework;
using UnityEngine;

namespace Roguehalla.Client.Tests
{
    public sealed class StormInkPilotRegressionTests
    {
        [Test]
        public void ContactUsesTheSameAppliedQualityPolicyAsItsOtherParticles()
        {
            var root = new GameObject("Contact graphics settings proof");
            var previous = Quality.Preset;
            string overrides = AtmosphereQuality.LastUrl;
            try
            {
                Quality.Set(ShowcaseQuality.Low, default);
                AtmosphereQuality.SetUrl("");
                AtmosphereQuality.ApplyPreset(ShowcaseQuality.Ultra, false);
                var sparks = new CombatSparks(root.transform);
                var ink = root.transform.Find("Contact ink").GetComponent<ParticleSystem>();
                uint rng = 7;
                sparks.Hit(Vector3.zero, false, ref rng);
                Assert.That(ink.particleCount, Is.EqualTo(1));
                sparks.Clear();
                AtmosphereQuality.ApplyPreset(ShowcaseQuality.Ultra, true);
                sparks.Hit(Vector3.zero, false, ref rng);
                Assert.That(ink.particleCount, Is.Zero);
            }
            finally
            {
                Quality.Set(previous, default);
                AtmosphereQuality.SetUrl(overrides);
                AtmosphereQuality.RefreshFromSettings();
                Object.DestroyImmediate(root);
            }
        }
    }
}
