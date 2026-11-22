using NUnit.Framework;
using Roguehalla.Net.Protocol;
using UnityEngine;

namespace Roguehalla.Client.Tests
{
    /// <summary>Checks the painted exterior sand through the same ground material built for a match.</summary>
    public sealed class StormInkHolySandEditModeTests
    {
        static MapBlob HolyFloor()
        {
            const int side = 12;
            var zones = new byte[side * side];
            for (int i = 0; i < zones.Length; i++) zones[i] = (byte)RegionId.Holy;
            return new MapBlob(side, side, new byte[zones.Length], new byte[zones.Length],
                zones, MapBlob.GroundTerrainAll);
        }

        [Test]
        public void OrdinaryHolyFloorUsesTheArtistWashOnTheExistingGroundQuad()
        {
            var root = new GameObject("Holy sand material test");
            try
            {
                using var ground = RegionGround.Create(root.transform, HolyFloor(), .83f);
                var material = ground.Material;
                Assert.That(material.HasProperty("_HolyDrySandWash"), Is.True, "The gameplay ground needs an authored dry sand wash");
                var wash = Resources.Load<Texture2D>("StormInk/Holy/OasisSand");
                Assert.That(wash, Is.Not.Null);
                Assert.That(wash.isReadable, Is.True);
                var atlas = (Texture2DArray)material.GetTexture("_GroundTex");
                Assert.That(atlas.depth, Is.EqualTo(RegionGroundTextures.Layers + 3),
                    "Sand shares the existing array with region details and the ice and lava washes");
                Assert.That(material.GetFloat("_HolyDrySandSlice"), Is.EqualTo(atlas.depth - 1));
                Assert.That(material.GetFloat("_HolyDrySandRepeatSpan"), Is.GreaterThan(0f));
                Assert.That(material.GetFloat("_HolyDrySandWash"), Is.InRange(.01f, 1f));
                Assert.That(material.GetFloat("_Exposure"), Is.EqualTo(.83f).Within(.0001f));
                Assert.That(ground.Root.GetComponent<MeshFilter>().sharedMesh.vertexCount, Is.EqualTo(4));
                Assert.That(wash.mipmapCount, Is.GreaterThan(1));
                Assert.That(wash.wrapMode, Is.EqualTo(TextureWrapMode.Mirror));
            }
            finally { Object.DestroyImmediate(root); }
        }

        [TestCase(true, .6f)]
        [TestCase(true, 0f)]
        [TestCase(false, .6f)]
        public void RebuildingTheViewHonoursArtistEditsAndTheProceduralFallback(bool assigned, float strength)
        {
            var definition = HolyDrySandArt.Load();
            var original = (definition.Pigment, definition.RepeatSpan, definition.WashStrength);
            var root = new GameObject("Edited Holy sand material");
            try
            {
                definition.Pigment = assigned ? original.Pigment : null;
                definition.RepeatSpan = 7.5f;
                definition.WashStrength = strength;
                using var ground = RegionGround.Create(root.transform, HolyFloor(), 1f);
                bool paintsSand = assigned && strength > 0f;
                var atlas = (Texture2DArray)ground.Material.GetTexture("_GroundTex");
                Assert.That(atlas.depth, Is.EqualTo(RegionGroundTextures.Layers + (paintsSand ? 3 : 2)));
                Assert.That(ground.Material.GetFloat("_HolyDrySandWash"),
                    Is.EqualTo(paintsSand ? strength : 0f).Within(.001f));
                if (paintsSand)
                    Assert.That(ground.Material.GetFloat("_HolyDrySandRepeatSpan"), Is.EqualTo(7.5f));
            }
            finally
            {
                (definition.Pigment, definition.RepeatSpan, definition.WashStrength) = original;
                Object.DestroyImmediate(root);
            }
        }
    }
}
