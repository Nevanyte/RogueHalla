using System.Linq;
using NUnit.Framework;
using Roguehalla.Net.Protocol;
using UnityEngine;

namespace Roguehalla.Client.Tests
{
    public sealed class StormInkMushroomVariationEditModeTests
    {
        [Test]
        public void DeliveredGateUsesThreeSilhouettesWithoutRepeatingItsAdjacentRows()
        {
            const int size = 160;
            var props = new[]
            {
                new MapProp(29, 3889, 3368, 0, 0),
                new MapProp(29, 3734, 3369, 0, 0),
                new MapProp(29, 3860, 3476, 0, 0),
                new MapProp(29, 3825, 3504, 0, 0),
                new MapProp(29, 3849, 3510, 0, 0),
                new MapProp(29, 3828, 3537, 0, 0),
            };
            var map = new MapBlob(size, size, new byte[size * size]).WithPublic(null, null, null, props, null);
            var placements = RegionProps.Scatter(map);
            Assert.That(placements.Select(p => p.Art), Is.All.EqualTo("plague.mushroom"));
            var first = ReadScene(map);
            var second = ReadScene(map);
            Assert.That(first.Select(p => p.sprite), Is.EqualTo(second.Select(p => p.sprite)));
            Assert.That(first.Skip(2).Select(p => p.sprite).Distinct().Count(), Is.EqualTo(3));
            for (int i = 2; i < props.Length - 1; i++)
                Assert.That(first[i].sprite, Is.Not.SameAs(first[i + 1].sprite));
            for (int i = 0; i < props.Length; i++)
            {
                Assert.That(first[i].foot, Is.EqualTo(ArenaView.WorldPoint(props[i].X, props[i].Y)));
                Assert.That(first[i].scale, Is.EqualTo(Vector3.one));
                Assert.That(first[i].tint, Is.EqualTo(Color.white));
                Assert.That(first[i].sort, Is.EqualTo(2000 + Mathf.RoundToInt(props[i].Y * .5f)));
                Assert.That(first[i].sprite.texture, Is.SameAs(RegionPropArt.Atlas));
            }
            Assert.That(RegionPropArt.TryGet("plague.mushroom", out var original), Is.True);
            Assert.That(first[0].sprite, Is.SameAs(original.Sprite));
            Assert.That(first[1].sprite, Is.SameAs(original.Sprite));
        }

        [Test]
        public void LocalFallbackScatterAlsoUsesDeterministicMushroomSilhouettes()
        {
            const int size = 160;
            var regions = Enumerable.Repeat((byte)RegionId.Plague, size * size).ToArray();
            var map = new MapBlob(size, size, new byte[size * size], null, regions);
            var first = ReadScene(map);
            var second = ReadScene(map);
            Assert.That(first.Length, Is.GreaterThan(10));
            Assert.That(first.Select(i => i.sprite).Distinct().Count(), Is.GreaterThan(1));
            Assert.That(first.Select(i => i.sprite), Is.EqualTo(second.Select(i => i.sprite)));
        }

        static (Sprite sprite, Vector3 foot, Vector3 scale, Color tint, int sort)[] ReadScene(MapBlob map)
        {
            var root = new GameObject("Mushroom variant proof");
            try
            {
                using var scene = RegionProps.Build(root.transform, map);
                return root.GetComponentsInChildren<SpriteRenderer>()
                    .Where(r => r.name == "plague.mushroom")
                    .Select(r => (r.sprite, r.transform.position, r.transform.localScale, r.color, r.sortingOrder)).ToArray();
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }
    }
}
