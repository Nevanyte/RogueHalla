using NUnit.Framework;
using Roguehalla.Net.Protocol;
using UnityEngine;

namespace Roguehalla.Client.Tests
{
    /// <summary>Exercise Coliseum surface authoring on the same ground material and map that gameplay uses.</summary>
    public sealed class StormInkColiseumFloorEditModeTests
    {
        static MapBlob Floor()
        {
            const int side = 12;
            var regions = new byte[side * side];
            for (int y = 2; y < 10; y++)
                for (int x = 2; x < 10; x++)
                    regions[y * side + x] = (byte)RegionId.Coliseum;
            return new MapBlob(side, side, new byte[regions.Length], new byte[regions.Length], regions, MapBlob.GroundTerrainAll);
        }

        [Test]
        public void OrientedPaintingUsesTheExistingQuadAndItsOwnLinearAtlasSlice()
        {
            var root = new GameObject("Worn limestone floor");
            var art = Resources.Load<ColiseumFloorArt>("StormInk/Coliseum/ColiseumFloorArt");
            Assert.That(art, Is.Not.Null);
            var previous = (art.RepeatSpan, art.RotationDegrees);
            try
            {
                art.RepeatSpan = new Vector2(4f, 8f);
                art.RotationDegrees = 90f;
                using var ground = RegionGround.Create(root.transform, Floor(), .85f);
                var material = ground.Material;
                var atlas = (Texture2DArray)material.GetTexture("_GroundTex");
                Assert.That(material.GetFloat("_ColiseumPavingSlice"), Is.EqualTo(atlas.depth - 1));
                Assert.That(material.GetFloat("_HolyDrySandSlice"), Is.EqualTo(RegionGroundTextures.HolyDrySandSlice));
                Assert.That(atlas.depth - 1, Is.GreaterThan(RegionGroundTextures.HolyDrySandSlice));
                Assert.That(material.GetFloat("_ColiseumPavingWash"), Is.EqualTo(art.WashStrength));
                Assert.That(material.GetVector("_Coliseum"), Is.EqualTo(new Vector4(6, 6, 4, 1)));
                var uv = material.GetVector("_ColiseumPavingUV");
                Assert.That(Vector4.Distance(uv, new Vector4(0f, .25f, -.125f, 0f)), Is.LessThan(.00001f));
                Assert.That(ground.Root.GetComponent<MeshFilter>().sharedMesh.vertexCount, Is.EqualTo(4));
                Assert.That(((Texture2DArray)material.GetTexture("_GroundNrm")).depth, Is.EqualTo(RegionGroundTextures.Layers));
            }
            finally
            {
                (art.RepeatSpan, art.RotationDegrees) = previous;
                Object.DestroyImmediate(root);
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public void MissingOrDisabledPaintUsesTheProceduralFloorAndOriginalAtlas(bool missing)
        {
            var root = new GameObject("Procedural Coliseum floor");
            var art = Resources.Load<ColiseumFloorArt>("StormInk/Coliseum/ColiseumFloorArt");
            var previous = (art.Pigment, art.WashStrength);
            try
            {
                if (missing) art.Pigment = null;
                else art.WashStrength = 0;
                using var ground = RegionGround.Create(root.transform, Floor(), 1f);
                Assert.That(ground.Material.GetFloat("_ColiseumPavingWash"), Is.Zero);
                Assert.That(ground.Material.GetFloat("_ColiseumPavingSlice"), Is.EqualTo(-1f));
                var atlas = (Texture2DArray)ground.Material.GetTexture("_GroundTex");
                Assert.That(atlas.depth, Is.EqualTo(RegionGroundTextures.HolyDrySandSlice + 1));
            }
            finally
            {
                (art.Pigment, art.WashStrength) = previous;
                Object.DestroyImmediate(root);
            }
        }
    }
}
