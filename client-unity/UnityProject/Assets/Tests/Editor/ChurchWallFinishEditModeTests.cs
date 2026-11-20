using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Roguehalla.Net.Protocol;
using UnityEngine;

namespace Roguehalla.Client.Tests
{
    /// <summary>Checks the visible surface against the independent wall symbols from the delivered church blueprint.</summary>
    public sealed class ChurchWallFinishEditModeTests
    {
        [TestCase(0)] [TestCase(1)] [TestCase(2)] [TestCase(3)]
        [TestCase(4)] [TestCase(5)] [TestCase(6)] [TestCase(7)]
        public void OnlyNaveBoundaryCellsUseTheDecoratedCap(int orientation)
        {
            const BindingFlags fixture = BindingFlags.Static | BindingFlags.NonPublic;
            var blueprint = typeof(ChurchLandmarkEditModeTests);
            var site = blueprint.GetMethod("Church", fixture).Invoke(null, new object[] { orientation });
            var anchor = (MapAnchor)site.GetType().GetField("Anchor").GetValue(site);
            var map = ((MapBlob)site.GetType().GetField("Map").GetValue(site)).WithPublic(null, null, null, null, null, null, new[] { anchor });
            var rows = (string[])blueprint.GetMethod("Orient", fixture).Invoke(null,
                new[] { blueprint.GetMethod("ChurchRows", fixture).Invoke(null, null), (object)orientation });
            var root = new GameObject("Nave wall finish test");
            try
            {
                var property = typeof(RegionWallAtlas).GetProperty("Nave");
                Assert.That(property, Is.Not.Null, "The nave needs a separately authored finish on the existing wall mesh");
                var finish = (RegionWallAtlas.Set)property.GetValue(null);
                var expected = rows.Sum(row => row.Count(cell => cell == '#'));
                int matched = 0;
                using (RegionWalls.Build(root.transform, map, 1f))
                foreach (var filter in root.GetComponentsInChildren<MeshFilter>())
                {
                    var mesh = filter.sharedMesh;
                    var uv = mesh.uv;
                    var vertices = mesh.vertices;
                    for (int i = 0; i < uv.Length; i += 4)
                    {
                        if (!finish.Top.Take(RegionWallAtlas.TopVariants).Any(cell => cell.Min == uv[i])) continue;
                        int x = Mathf.RoundToInt(vertices[i].x) - anchor.Footprint.X;
                        int y = Mathf.RoundToInt((RegionWalls.WallHeight - vertices[i].y) / ArenaView.GroundSquash) - 1 - anchor.Footprint.Y;
                        Assert.That(rows[y][x], Is.EqualTo('#'), $"cap on non-wall at {x},{y}");
                        matched++;
                    }
                }
                Assert.That(matched, Is.EqualTo(expected));
            }
            finally { Object.DestroyImmediate(root); }
        }
    }
}
