using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Roguehalla.Net.Protocol;
using UnityEngine;

namespace Roguehalla.Client.Tests
{
    public sealed class StormInkHolyPierFootingEditModeTests
    {
        [TestCase(0), TestCase(1), TestCase(2), TestCase(3)]
        [TestCase(4), TestCase(5), TestCase(6), TestCase(7)]
        public void DeliveredPiersDrawOnlyTheirOriginalPaintedBaseAtTheOriginalContact(int pose)
        {
            var fixture = typeof(StormInkHolyHallEditModeTests).GetMethod("DeliveredHall", BindingFlags.Static | BindingFlags.NonPublic);
            var map = (MapBlob)fixture.Invoke(null, new object[] { pose, true });
            var layout = TempleLayout.For(map);
            var root = new GameObject("Holy pier footing proof");
            try
            {
                using var view = LandmarkView.Build(root.transform, map);
                var columns = root.GetComponentsInChildren<Transform>().Where(t => t.name == "Hall column").ToArray();
                Assert.That(columns.Length, Is.EqualTo(32));
                var pier = Resources.Load<Sprite>("StormInk/Holy/SunPier");
                Assert.That(pier, Is.Not.Null);
                foreach (var column in columns)
                {
                    var surfaces = column.GetComponentsInChildren<Renderer>();
                    Assert.That(surfaces.Length, Is.EqualTo(1), "Only SunPier, including its original stepped base.");
                    Assert.That(surfaces[0], Is.TypeOf<SpriteRenderer>());
                    var sprite = (SpriteRenderer)surfaces[0];
                    Assert.That(sprite.sprite, Is.SameAs(pier));
                    var bounds = sprite.bounds;
                    var footprint = layout.Pillars.Single(p =>
                        Mathf.Abs(p.x + 1.5f - bounds.center.x) < .01f &&
                        Mathf.Abs(-(p.y + 3) * ArenaView.GroundSquash + .18f - 4.38f * .06f - bounds.min.y) < .01f);
                    Assert.That(sprite.sortingOrder, Is.EqualTo(2000 + 16 * (footprint.y + 2) + 2));
                    Assert.That(bounds.size.x, Is.EqualTo(3f).Within(.01f));
                    Assert.That(bounds.size.y, Is.EqualTo(4.38f).Within(.01f));
                }
            }
            finally { Object.DestroyImmediate(root); }
        }
    }
}
