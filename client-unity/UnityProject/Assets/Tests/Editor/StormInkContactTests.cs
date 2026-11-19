using NUnit.Framework;
using UnityEngine;

namespace Roguehalla.Client.Tests
{
    public sealed class StormInkContactTests
    {
        [Test]
        public void ConfirmedContactDrawsOneBriefBrushMarkAndClearRemovesIt()
        {
            var root = new GameObject("Contact proof");
            try
            {
                AtmosphereQuality.ApplyPreset(ShowcaseQuality.Ultra, false);
                var sparks = new CombatSparks(root.transform);
                uint rng = 7;
                sparks.Hit(Vector3.zero, false, ref rng);
                var child = root.transform.Find("Contact ink");
                Assert.IsNotNull(child);
                var pool = child.GetComponent<ParticleSystem>();
                Assert.AreEqual("ContactInk", child.GetComponent<ParticleSystemRenderer>().sharedMaterial.mainTexture.name);
                Assert.AreEqual(1, pool.particleCount);
                var particles = new ParticleSystem.Particle[1];
                pool.GetParticles(particles);
                Assert.LessOrEqual(particles[0].startLifetime, .09f);
                sparks.Clear();
                sparks.Hit(Vector3.zero, true, ref rng);
                Assert.AreEqual(0, pool.particleCount);
            }
            finally { Object.DestroyImmediate(root); }
        }
    }
}
