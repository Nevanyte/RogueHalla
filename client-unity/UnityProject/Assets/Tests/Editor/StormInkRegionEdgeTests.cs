using NUnit.Framework;
using Roguehalla.Net.Protocol;
using UnityEngine;

namespace Roguehalla.Client.Tests
{
    public sealed class StormInkRegionEdgeTests
    {
        [Test]
        public void VergePigmentFollowsTheSelectedPropFoot()
        {
            var art = RegionTransitionArt.Load();
            var field = new RegionField();
            var weights = new float[RegionField.Regions];
            field.Build(new byte[] { 4, 5 }, 2, 1);
            var tile = new Vector2(1f, .5f);
            float footY = -3.75f;
            var grass = art.ContactAt("holy.grass", field, weights, tile, footY, .4f);
            var pine = art.ContactAt("frost.pine", field, weights, tile, footY, 4f);
            Assert.That(grass.x, Is.EqualTo(footY));
            Assert.That(grass.y, Is.GreaterThan(pine.y), "The wash rises less on grass than on a tree");
            Assert.That(grass.z, Is.EqualTo(art.FootWash).Within(.001f));
            Assert.That(art.ContactAt("sent.lantern", field, weights, tile, footY, 2f), Is.EqualTo(Vector4.zero));
            field.Build(new byte[] { 4 }, 1, 1);
            Assert.That(art.ContactAt("frost.pine", field, weights, tile, footY, 4f), Is.EqualTo(Vector4.zero));
        }

        [Test]
        public void ArtistCanTuneEachMaterialIndependently()
        {
            var art = RegionTransitionArt.Load();
            var before = (art.GrassWeathering, art.RockBreakup, art.PavingBreakup, art.SnowEdge);
            var root = new GameObject("Edited regional edge treatments");
            try
            {
                art.GrassWeathering = .22f;
                art.RockBreakup = .43f;
                art.PavingBreakup = 0f;
                art.SnowEdge = .83f;
                var regions = new byte[] { 2, 4, 5, 3 };
                var map = new MapBlob(2, 2, new byte[4], new byte[4], regions, MapBlob.GroundTerrainAll);
                using var ground = RegionGround.Create(root.transform, map, 1f);
                Assert.That(ground.Material.GetVector("_RegionEdgeArt"), Is.EqualTo(new Vector4(.22f, .43f, 0f, .83f)));
            }
            finally
            {
                (art.GrassWeathering, art.RockBreakup, art.PavingBreakup, art.SnowEdge) = before;
                Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void GroundMaterialBindsTheFourRegionalEdgeTreatments()
        {
            var regions = new byte[] { 1, 2, 3, 4, 5, 4, 3, 2, 1 };
            var map = new MapBlob(3, 3, new byte[9], new byte[9], regions, MapBlob.GroundTerrainAll);
            var root = new GameObject("Painted border material");
            try
            {
                using var ground = RegionGround.Create(root.transform, map, 1f);
                Assert.That(ground.Material.HasProperty("_RegionEdgeArt"), Is.True);
                Assert.That(ground.Material.GetVector("_RegionEdgeArt"), Is.EqualTo(Vector4.one));
                Assert.That(ground.Root.GetComponent<MeshFilter>().sharedMesh.vertexCount, Is.EqualTo(4));
            }
            finally { Object.DestroyImmediate(root); }
        }
    }
}
