using NUnit.Framework;
using Roguehalla.Net.Protocol;
using UnityEngine;

namespace Roguehalla.Client.Tests
{
    public sealed class StormInkCypressMaterialFallbackTests
    {
        const int Width = 32, Height = 32;

        [TestCase((ushort)17, 9, false, TestName = "UndersizedFootprintKeepsLegacyGround")]
        [TestCase((ushort)29, 1, false, TestName = "ThinTrunkKeepsLegacyGround")]
        [TestCase((ushort)29, 9, true, TestName = "UsableDeliveredCypressEnablesPaintedGround")]
        public void MaterialOptInMatchesDeliveredCypressGeometry(ushort footprintWidth, int trunkDepth, bool expected)
        {
            var tiles = new byte[Width * Height];
            var terrain = new byte[tiles.Length];
            var zones = new byte[tiles.Length];
            for (int i = 0; i < zones.Length; i++) zones[i] = (byte)RegionId.Plague;
            for (int y = 12; y < 12 + trunkDepth; y++)
            for (int x = 7; x < 16; x++) tiles[y * Width + x] = MapBlob.Wall;

            var anchor = new MapAnchor((byte)RegionId.Plague, MapBlob.AnchorKindBogTree,
                new MapTile(11, 12), new MapRect(1, 1, footprintWidth, 29), 4,
                new[] { new MapTile(11, 29) });
            var map = new MapBlob(Width, Height, tiles, terrain, zones, MapBlob.GroundTerrainAll)
                .WithPublic(null, null, null, null, null, null, new[] { anchor });
            var root = new GameObject("cypress ground eligibility oracle");
            try
            {
                Assert.That(Resources.Load<Texture2D>("StormInk/Plague/MarshWash"), Is.Not.Null, "paint asset is available in all cases");
                using var ground = RegionGround.Create(root.transform, map, 1f);
                Assert.That(ground.Material.GetFloat("_StormMarshOn"), Is.EqualTo(expected ? 1f : 0f));
            }
            finally { Object.DestroyImmediate(root); }
        }
    }
}
