using NUnit.Framework;
using UnityEngine;

namespace Roguehalla.Client.Tests
{
    public sealed class StormInkElementalPigmentTests
    {
        [TestCase("MineralIce")]
        [TestCase("MoltenInk")]
        public void PaintingsProvideFullColourForTheGroundArray(string name)
        {
            var source = Resources.Load<Texture2D>("StormInk/Surfaces/" + name);
            Assert.That(source, Is.Not.Null);
            Assert.That(source.width, Is.EqualTo(RegionGroundTextures.Size));
            Assert.That(source.height, Is.EqualTo(source.width));
            Assert.That(source.isReadable, Is.True);
            int chromatic = 0;
            foreach (var p in source.GetPixels32())
                if (Mathf.Max(p.r, Mathf.Max(p.g, p.b)) - Mathf.Min(p.r, Mathf.Min(p.g, p.b)) > 44) chromatic++;
            Assert.That(chromatic, Is.GreaterThan(source.width * source.height / 5));
        }

        [Test]
        public void ElementalPigmentsShareTheSixRegionArrayBinding()
        {
            var ground = RegionGroundTextures.Build(out var normals);
            try
            {
                Assert.That(ground.depth, Is.EqualTo(8));
                Assert.That(normals.depth, Is.EqualTo(6));
            }
            finally { Object.DestroyImmediate(ground); Object.DestroyImmediate(normals); }
        }
    }
}
