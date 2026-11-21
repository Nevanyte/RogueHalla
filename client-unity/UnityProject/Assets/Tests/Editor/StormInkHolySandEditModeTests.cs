using NUnit.Framework;
using Roguehalla.Net.Protocol;
using UnityEngine;

namespace Roguehalla.Client.Tests
{
    /// <summary>Checks the painted exterior sand through the same ground material built for a match.</summary>
    public sealed class StormInkHolySandEditModeTests
    {
        [Test]
        public void OrdinaryHolyFloorUsesTheArtistWashOnTheExistingGroundQuad()
        {
            const int side = 12;
            var zones = new byte[side * side];
            for (int i = 0; i < zones.Length; i++) zones[i] = (byte)RegionId.Holy;
            var map = new MapBlob(side, side, new byte[zones.Length], new byte[zones.Length],
                zones, MapBlob.GroundTerrainAll);
            var root = new GameObject("Holy sand material test");
            try
            {
                using var ground = RegionGround.Create(root.transform, map, .83f);
                var material = ground.Material;
                Assert.That(material.HasProperty("_HolyDrySandTex"), Is.True, "The gameplay ground needs an authored dry sand input");
                var wash = Resources.Load<Texture2D>("StormInk/Holy/OasisSand");
                Assert.That(wash, Is.Not.Null);
                Assert.That(material.GetTexture("_HolyDrySandTex"), Is.SameAs(wash));
                Assert.That(material.GetFloat("_HolyDrySandRepeatSpan"), Is.GreaterThan(0f));
                Assert.That(material.GetFloat("_HolyDrySandWash"), Is.InRange(.01f, 1f));
                Assert.That(material.GetFloat("_Exposure"), Is.EqualTo(.83f).Within(.0001f));
                Assert.That(ground.Root.GetComponent<MeshFilter>().sharedMesh.vertexCount, Is.EqualTo(4));
                Assert.That(wash.mipmapCount, Is.GreaterThan(1));
                Assert.That(wash.wrapMode, Is.EqualTo(TextureWrapMode.Mirror));
            }
            finally { Object.DestroyImmediate(root); }
        }
    }
}
