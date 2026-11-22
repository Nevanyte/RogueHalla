using NUnit.Framework;
using Roguehalla.Net.Protocol;
using UnityEngine;

namespace Roguehalla.Client.Tests
{
    public sealed class StormInkRegionalFloorTests
    {
        [Test]
        public void EditedFootprintAndOptionalPigmentSurviveAdjacentLayers()
        {
            var fire = Resources.Load<RegionalFloorArt>("StormInk/Surfaces/FireAshFloor");
            var heath = Resources.Load<RegionalFloorArt>("StormInk/Surfaces/HauntedHeathFloor");
            var original = (fire.Pigment, heath.RotationDegrees, heath.RepeatSpan, heath.WashStrength);
            var root = new GameObject("Edited regional floor");
            try
            {
                fire.Pigment = null;
                heath.RotationDegrees = 90f;
                heath.RepeatSpan = new Vector2(5, 10);
                heath.WashStrength = .65f;
                var cells = new byte[] { 1, 2, 4, 6 };
                var map = new MapBlob(2, 2, new byte[4], new byte[4], cells, MapBlob.GroundTerrainAll);
                using var ground = RegionGround.Create(root.transform, map, 1f);
                var material = ground.Material;
                var paint = material.GetVectorArray("_DryPaint");
                var axes = material.GetVectorArray("_DryPaintUV");
                Assert.That(paint[0].y, Is.Zero);
                Assert.That(paint[1].y, Is.EqualTo(.65f).Within(.001f));
                Assert.That(Vector4.Distance(axes[1], new Vector4(0, .2f, -.1f, 0)), Is.LessThan(.00001f));
                Assert.That(paint[3].x, Is.EqualTo(paint[1].x + 1f));
                Assert.That(material.GetFloat("_ColiseumPavingSlice"), Is.EqualTo(paint[3].x + 1f));
            }
            finally
            {
                (fire.Pigment, heath.RotationDegrees, heath.RepeatSpan, heath.WashStrength) = original;
                Object.DestroyImmediate(root);
            }
        }

        [TestCase(true)]
        [TestCase(false)]
        public void JoinedGroundPacksOnlyThePresentDryPaints(bool allRegions)
        {
            const int side = 12;
            var cells = new byte[side * side];
            for (int i = 0; i < cells.Length; i++) cells[i] = allRegions ? (byte)(1 + i % 5) : (byte)RegionId.Holy;
            var map = new MapBlob(side, side, new byte[cells.Length], new byte[cells.Length], cells, MapBlob.GroundTerrainAll);
            var root = new GameObject("Regional ground review");
            try
            {
                using var ground = RegionGround.Create(root.transform, map, .83f);
                var material = ground.Material;
                var paramsByLayer = material.GetVectorArray("_DryPaint");
                Assert.That(paramsByLayer, Has.Length.EqualTo(RegionGroundTextures.Layers));
                var atlas = (Texture2DArray)material.GetTexture("_GroundTex");
                int first = RegionGroundTextures.HolyDrySandSlice + 1;
                Assert.That(atlas.depth, Is.EqualTo(first + (allRegions ? 3 : 0)));
                int slice = first;
                foreach (int layer in new[] { 0, 1, 3 })
                {
                    Assert.That(paramsByLayer[layer].y, Is.EqualTo(allRegions ? 1f : 0f));
                    if (allRegions) Assert.That(paramsByLayer[layer].x, Is.EqualTo(slice++));
                }
                Assert.That(paramsByLayer[2].y, Is.Zero);
                Assert.That(paramsByLayer[4].y, Is.Zero);
                Assert.That(material.GetFloat("_HolyDrySandWash"), Is.GreaterThan(0f));
                Assert.That(material.GetFloat("_Exposure"), Is.EqualTo(.83f).Within(.001f));
            }
            finally { Object.DestroyImmediate(root); }
        }
    }
}
