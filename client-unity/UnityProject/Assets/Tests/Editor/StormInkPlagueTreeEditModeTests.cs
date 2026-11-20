using System.Linq;
using NUnit.Framework;
using Roguehalla.Net.Protocol;
using Roguehalla.Net.Ui;
using UnityEngine;

namespace Roguehalla.Client.Tests
{
    public sealed class StormInkPlagueTreeEditModeTests
    {
        static MapBlob GateMap()
        {
            const int size = 160;
            var regions = Enumerable.Repeat((byte)RegionId.Plague, size * size).ToArray();
            var bushes = new byte[size * size];
            bushes[125 * size + 125] = 1;
            var props = new[] { new MapProp(23, 4010, 3798, 0, MapBlob.PropFlagTall) };
            return new MapBlob(size, size, new byte[size * size], null, regions).WithPublic(null, null, bushes, props, null);
        }

        [Test]
        public void DeliveredGateTreeUsesTheRootedPaintedAtlasAndKeepsIndependentConcealment()
        {
            var map = GateMap();
            Assert.That(map.Bushes[(3798 / 32) * map.W + (4010 / 32)], Is.Zero);
            var placed = RegionProps.Scatter(map);
            var tree = placed.Single(p => p.Shipped);
            Assert.That(tree.Art, Is.EqualTo("plague.tree"));
            Assert.That(tree.Flags, Is.EqualTo(MapBlob.PropFlagTall));
            Assert.That(placed.Any(p => !p.Shipped && p.Art == "plague.bush"), Is.True);
            var root = new GameObject("Gate tree witness");
            try
            {
                using var scene = RegionProps.Build(root.transform, map);
                var renderer = root.GetComponentsInChildren<SpriteRenderer>().Single(r => r.name == tree.Art);
                Assert.That(renderer.sprite.name, Is.EqualTo("plague.tree.ink"));
                Assert.That(renderer.sprite.texture, Is.SameAs(RegionPropArt.Atlas));
                Assert.That(RegionPropArt.Sways(renderer.sprite.name), Is.True);
                Assert.That(renderer.transform.position, Is.EqualTo(ArenaView.WorldPoint(4010, 3798)));
                Assert.That(renderer.sortingOrder, Is.EqualTo(2000 + Mathf.RoundToInt(3798 * .5f)));
                Assert.That(renderer.color.a, Is.EqualTo(1f));
            }
            finally { Object.DestroyImmediate(root); }
        }

        [Test]
        public void VisibleFighterStillFadesOrdinaryWalkThroughTree()
        {
            var root = new GameObject("Gate tree actor overlap");
            try
            {
                using var scene = RegionProps.Build(root.transform, GateMap());
                var tree = root.GetComponentsInChildren<SpriteRenderer>().Single(r => r.name == "plague.tree");
                scene.NoteActor(ArenaView.WorldPoint(4010, 3765));
                scene.Tick(1f);
                Assert.That(tree.color.a, Is.EqualTo(PropFade.FadedAlpha).Within(.001f));
            }
            finally { Object.DestroyImmediate(root); }
        }
    }
}
