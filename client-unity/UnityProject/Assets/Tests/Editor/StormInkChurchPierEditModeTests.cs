using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Roguehalla.Net.Protocol;
using UnityEngine;

namespace Roguehalla.Client.Tests
{
    public sealed class StormInkChurchPierEditModeTests
    {
        [TestCase(0)] [TestCase(1)] [TestCase(2)] [TestCase(3)]
        [TestCase(4)] [TestCase(5)] [TestCase(6)] [TestCase(7)]
        public void TheEightDeliveredPierGroupsReplaceOnlyTheirGenericWallCaps(int orientation)
        {
            const BindingFlags fixture = BindingFlags.NonPublic | BindingFlags.Static;
            var blueprint = typeof(ChurchLandmarkEditModeTests);
            var site = blueprint.GetMethod("Church", fixture).Invoke(null, new object[] { orientation });
            var map = (MapBlob)site.GetType().GetField("Map").GetValue(site);
            var anchor = (MapAnchor)site.GetType().GetField("Anchor").GetValue(site);
            var delivered = map.WithPublic(null, null, null, null, null, null, new[] { anchor });
            var rows = (string[])blueprint.GetMethod("Orient", fixture).Invoke(null,
                new[] { blueprint.GetMethod("ChurchRows", fixture).Invoke(null, null), (object)orientation });
            var root = new GameObject("Church pier test");
            var baseline = new GameObject("Unanchored wall test");
            try
            {
                using (var landmarks = LandmarkView.Build(root.transform, delivered))
                using (var walls = RegionWalls.Build(root.transform, delivered, 1f))
                using (var unanchored = RegionWalls.Build(baseline.transform, map, 1f))
                {
                    var piers = root.GetComponentsInChildren<SpriteRenderer>()
                        .Where(r => r.name == "Church nave pier").ToArray();
                    Assert.That(piers.Length, Is.EqualTo(8));
                    Assert.That(walls.TileCount, Is.EqualTo(unanchored.TileCount));
                    Assert.That(landmarks.Get(0).WallCount, Is.EqualTo(136));
                    var vertices = WallVertices(root);
                    var original = WallVertices(baseline);
                    for (int y = 0; y < rows.Length; y++)
                    for (int x = 0; x < rows[y].Length; x++)
                    {
                        char kind = rows[y][x];
                        if (kind != 'P' && kind != '#' && kind != 'G') continue;
                        int tx = x + anchor.Footprint.X, ty = y + anchor.Footprint.Y;
                        Assert.That(HasCap(original, tx, ty), Is.True, "walls draw without a delivered church");
                        Assert.That(HasCap(vertices, tx, ty), Is.EqualTo(kind != 'P'),
                            $"{kind} cap at ({tx},{ty})");
                    }
                }
            }
            finally
            {
                Object.DestroyImmediate(root);
                Object.DestroyImmediate(baseline);
            }
        }

        static Vector3[] WallVertices(GameObject root) => root.GetComponentsInChildren<MeshFilter>()
            .Where(m => m.name.StartsWith("Walls row ")).SelectMany(m => m.sharedMesh.vertices).ToArray();

        static bool HasCap(Vector3[] vertices, int x, int y)
        {
            float bottom = -(y + 1) * ArenaView.GroundSquash + RegionWalls.WallHeight;
            for (int i = 0; i < vertices.Length; i += 4)
            {
                if (Mathf.Abs(vertices[i].x - (x - .003f)) < .0002f &&
                    Mathf.Abs(vertices[i].y - bottom) < .0002f &&
                    Mathf.Abs(vertices[i + 2].y - (bottom + .78f)) < .0002f) return true;
            }
            return false;
        }
    }
}
