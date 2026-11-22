using System.Linq;
using NUnit.Framework;
using Roguehalla.Editor;
using Roguehalla.Net.Protocol;
using UnityEngine;

namespace Roguehalla.Client.Tests
{
    public sealed class StormInkLightMotionTests
    {
        [TestCase("hall-wide")]
        [TestCase("hall-rot-wide")]
        public void EveryPaintedLampHasOneEmitterAtItsFlame(string scene)
        {
            var map = LookCapture.BuildMap(scene);
            var root = new GameObject("lamp alignment test");
            var lighting = new AccentLights();
            try
            {
                using var landmarks = LandmarkView.Build(root.transform, map);
                lighting.Initialize(root.transform);
                lighting.Load(map);
                var lamps = landmarks.Get(0).Root.GetComponentsInChildren<SpriteRenderer>()
                    .Where(r => r.sprite.name == "AisleLamp").ToArray();
                var sites = lighting.Planner.Clusters.Where(c => c.TempleLamp).ToList();
                Assert.That(lamps.Length, Is.EqualTo(8));
                Assert.That(sites.Count, Is.EqualTo(lamps.Length), "each aisle and shrine lamp illuminates its surroundings");
                foreach (var lamp in lamps)
                {
                    Vector2 flame = new Vector2(lamp.bounds.center.x, lamp.bounds.min.y + .84f * lamp.bounds.size.y);
                    int match = sites.FindIndex(c => Vector2.Distance(new Vector2(c.X,
                        -c.Y * ArenaView.GroundSquash + c.Lift), flame) < .08f);
                    Assert.That(match, Is.GreaterThanOrEqualTo(0), lamp.name + " flame matches its pooled light");
                    sites.RemoveAt(match);
                }
            }
            finally { lighting.Dispose(); Object.DestroyImmediate(root); }
        }

        [Test]
        public void CurvedLavaChannelsCastFromMoltenTiles()
        {
            const int size = AccentLightPlanner.CellTiles;
            var terrain = new byte[size * size];
            for (int i = 1; i <= 6; i++)
            {
                terrain[size + i] = (byte)TerrainKind.Lava;
                terrain[i * size + 1] = (byte)TerrainKind.Lava;
            }
            var plan = new AccentLightPlanner();
            plan.Build(terrain, size, size);
            var lamp = plan.Clusters.Single(c => c.Kind == TerrainKind.Lava);
            Assert.That(terrain[Mathf.FloorToInt(lamp.Y) * size + Mathf.FloorToInt(lamp.X)],
                Is.EqualTo((byte)TerrainKind.Lava), "a bent channel lights the rock from its molten edge");
            Assert.That(lamp.Count, Is.EqualTo(11));
        }

        [Test]
        public void PaintedStoneAndLampUseDifferentLightResponses()
        {
            var root = new GameObject("pigment material test");
            try
            {
                using var landmarks = LandmarkView.Build(root.transform, LookCapture.BuildMap("hall-wide"));
                var renderers = landmarks.Get(0).Root.GetComponentsInChildren<SpriteRenderer>();
                var stone = renderers.First(r => r.name == "Holy sun pier").sharedMaterial;
                var lamp = renderers.First(r => r.name == "Holy aisle lamp").sharedMaterial;
                Assert.That(stone.HasProperty("_StormInkResponse"), Is.True);
                Assert.That(stone.GetFloat("_StormInkResponse"), Is.EqualTo(1f), "matte mineral pigment");
                Assert.That(lamp.GetFloat("_StormInkResponse"), Is.EqualTo(2f), "flame above matte bronze");
            }
            finally { Object.DestroyImmediate(root); }
        }
    }
}
