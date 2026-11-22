using System.Linq;
using NUnit.Framework;
using Roguehalla.Net.Protocol;
using UnityEngine;

namespace Roguehalla.Client.Tests
{
    public sealed class RegionEdgePropsEditModeTests
    {
        [Test]
        public void FrostEdgeAddsSparseDeterministicGrassOnlyOnWalkableGreenSide()
        {
            var map = EdgeMap(5, 4);
            var field = Field(map);
            var settings = ScriptableObject.CreateInstance<RegionTransitionArt>();
            try
            {
                var first = RegionEdgeProps.Generate(map, field, settings, null);
                var second = RegionEdgeProps.Generate(map, field, settings, null);
                Assert.That(first.Count, Is.GreaterThan(0));
                Assert.That(first.Select(p => (p.Art, p.Tx, p.Ty, p.Scale, p.Flip)), Is.EqualTo(
                    second.Select(p => (p.Art, p.Tx, p.Ty, p.Scale, p.Flip))));
                Assert.That(first.All(p => p.TransitionDetail), Is.True);
                Assert.That(first.All(p => p.Art == "holy.grass" || p.Art == "sent.deadgrass"), Is.True);
                Assert.That(first.All(p => map.Regions[p.Ty * map.W + p.Tx] == (byte)RegionId.Holy));
                Assert.That(first.All(p => field.SampleIsFrostBand(p.X, p.Y)));
                Assert.That(first.All(p => p.Scale >= .27f && p.Scale <= .39f));
                Assert.That(first.All(p => p.TransitionWidth < 1f && p.TransitionHeight < 1f));
            }
            finally { Object.DestroyImmediate(settings); }
        }

        [Test]
        public void FireEdgeBreaksRockIntoSmallWalkablePebbleDetails()
        {
            var map = EdgeMap(1, 4);
            var field = Field(map);
            var settings = ScriptableObject.CreateInstance<RegionTransitionArt>();
            try
            {
                var placements = RegionEdgeProps.Generate(map, field, settings, null);
                var pebbles = placements.Where(p => p.Art == "fire.scree.cobbles").ToArray();
                Assert.That(pebbles, Is.Not.Empty);
                Assert.That(placements.All(p => p.TransitionDetail), Is.True);
                Assert.That(pebbles.All(p => p.Scale >= .12f && p.Scale <= .36f));
                Assert.That(pebbles.All(p => map.Tiles[p.Ty * map.W + p.Tx] == MapBlob.Floor));
                Assert.That(pebbles.All(p => map.Terrain[p.Ty * map.W + p.Tx] == 0));
            }
            finally { Object.DestroyImmediate(settings); }
        }

        [Test]
        public void DressingSkipsTerrainWallsBushesAndExistingPropFeet()
        {
            var map = EdgeMap(5, 4);
            var tiles = map.Tiles.ToArray();
            var terrain = map.Terrain.ToArray();
            var bushes = new byte[map.W * map.H];
            int wall = 22 + 10 * map.W, hazard = 23 + 10 * map.W, bush = 22 + 11 * map.W;
            tiles[wall] = MapBlob.Wall; terrain[hazard] = (byte)TerrainKind.Ice; bushes[bush] = 1;
            map = new MapBlob(map.W, map.H, tiles, terrain, map.Regions, MapBlob.GroundTerrainAll)
                .WithPublic(null, null, bushes, null, null);
            var settings = ScriptableObject.CreateInstance<RegionTransitionArt>();
            try
            {
                var occupied = new[] { new RegionProps.Placement { X = 23.5f, Y = 12.5f, Art = "shipped.test" } };
                var placements = RegionEdgeProps.Generate(map, Field(map), settings, occupied);
                Assert.That(placements.Any(p => p.Tx == 22 && p.Ty == 10), Is.False);
                Assert.That(placements.Any(p => p.Tx == 23 && p.Ty == 10), Is.False);
                Assert.That(placements.Any(p => p.Tx == 22 && p.Ty == 11), Is.False);
                Assert.That(placements.All(p => Vector2.Distance(new Vector2(p.X, p.Y), new Vector2(23.5f, 12.5f)) >= .31f));
            }
            finally { Object.DestroyImmediate(settings); }
        }

        [Test]
        public void MissingOrDisabledTransitionDataLeavesPropsUnweathered()
        {
            var map = EdgeMap(5, 4);
            var field = Field(map);
            Assert.That(RegionEdgeProps.Generate(map, field, null, null), Is.Empty);
            var settings = ScriptableObject.CreateInstance<RegionTransitionArt>();
            settings.GrassWeathering = 0f;
            settings.RockBreakup = 0f;
            try
            {
                Assert.That(RegionEdgeProps.Generate(map, field, settings, null), Is.Empty);
                float sx = 1f, sy = 1f; var tint = Color.white;
                var existing = new RegionProps.Placement { Art = "holy.grass", X = 23.5f, Y = 10.5f, Tx = 23, Ty = 10 };
                Assert.That(RegionEdgeProps.WeatherExisting(map, field, settings, existing, "holy.grass", new float[6],
                    ref sx, ref sy, ref tint), Is.False);
                Assert.That(sx, Is.EqualTo(1f)); Assert.That(sy, Is.EqualTo(1f));
                Assert.That(tint, Is.EqualTo(Color.white));
            }
            finally { Object.DestroyImmediate(settings); }
        }

        [Test]
        public void AHalfStrengthRockWashInterpolatesBothSizeAndPigment()
        {
            var map = EdgeMap(1, 4);
            var field = Field(map);
            var settings = ScriptableObject.CreateInstance<RegionTransitionArt>();
            try
            {
                var rock = new RegionProps.Placement { X = 23.5f, Y = 10.5f, Tx = 23, Ty = 10 };
                var weights = new float[RegionField.Regions];
                float sx = 1f, sy = 1f;
                var tint = Color.white;
                Assert.That(RegionEdgeProps.WeatherExisting(map, field, settings, rock, "fire.scree", weights, ref sx, ref sy, ref tint), Is.True);
                float fullWidth = sx;
                var fullTint = tint;
                settings.RockBreakup = .5f;
                sx = sy = 1f;
                tint = Color.white;
                Assert.That(RegionEdgeProps.WeatherExisting(map, field, settings, rock, "fire.scree", weights, ref sx, ref sy, ref tint), Is.True);
                Assert.That(Mathf.Abs(sx - Mathf.Lerp(1f, fullWidth, .5f)), Is.LessThan(.0001f));
                Assert.That(tint, Is.EqualTo(Color.Lerp(Color.white, fullTint, .5f)));
            }
            finally { Object.DestroyImmediate(settings); }
        }

        static MapBlob EdgeMap(byte westRegion, byte eastRegion)
        {
            const int width = 48, height = 32;
            var tiles = new byte[width * height];
            var terrain = new byte[tiles.Length];
            var regions = new byte[tiles.Length];
            for (int y = 0; y < height; y++) for (int x = 0; x < width; x++)
                regions[y * width + x] = x < width / 2 ? westRegion : eastRegion;
            return new MapBlob(width, height, tiles, terrain, regions, MapBlob.GroundTerrainAll);
        }

        static RegionField Field(MapBlob map)
        {
            var field = new RegionField(); field.Build(map.Regions, map.W, map.H); return field;
        }
    }

    static class RegionEdgePropsTestExtensions
    {
        internal static bool SampleIsFrostBand(this RegionField field, float x, float y)
        {
            var weights = new float[RegionField.Regions]; field.Sample(x, y, weights);
            return weights[(int)RegionId.Frost - 1] >= .035f && weights[(int)RegionId.Frost - 1] < .88f;
        }
    }
}
