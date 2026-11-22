using System.Linq;
using NUnit.Framework;
using Roguehalla.Client;
using Roguehalla.Net.Protocol;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace Roguehalla.Client.Tests
{
    public sealed class ColiseumWeaponRackPresentationEditModeTests
    {
        const int MapSize = 40;

        static MapBlob MapWithRack(out RegionProps.Placement rack)
        {
            var tiles = new byte[MapSize * MapSize];
            var regions = Enumerable.Repeat((byte)RegionId.Coliseum, tiles.Length).ToArray();
            for (int y = 0; y < MapSize; y++)
            for (int x = 0; x < MapSize; x++)
                if (x == 0 || y == 0 || x == MapSize - 1 || y == MapSize - 1)
                    tiles[y * MapSize + x] = MapBlob.Wall;

            // Vary an interior region bit to vary the deterministic placement seed without removing any wall
            // geometry or shrinking the Coliseum. Return a fixture that drives the real scatter/render path.
            var seedCells = new[]
            {
                12 * MapSize + 12, 12 * MapSize + 27,
                18 * MapSize + 12, 18 * MapSize + 27,
                24 * MapSize + 12, 24 * MapSize + 27,
                28 * MapSize + 15, 28 * MapSize + 24,
            };
            for (int variation = 0; variation < 1 << seedCells.Length; variation++)
            {
                for (int bit = 0; bit < seedCells.Length; bit++)
                    regions[seedCells[bit]] = (byte)((variation >> bit) & 1);
                var map = new MapBlob(MapSize, MapSize, tiles, null, regions);
                var placements = RegionProps.Scatter(map);
                var found = placements.FirstOrDefault(p => p.Art == "col.rack");
                if (found.Art != null)
                {
                    rack = found;
                    return map;
                }
            }

            Assert.Fail("A deterministic wall-adjacent Coliseum rack fixture could not be found.");
            rack = default;
            return null;
        }

        static SpriteRenderer At(Transform rack, string childName) =>
            rack.Find(childName).GetComponent<SpriteRenderer>();

        [Test]
        public void AuthoredRackUsesSharedRigidWeaponsAndOnePlantedSortGroup()
        {
            var definition = ColiseumWeaponRackArtDefinition.Load();
            Assert.That(definition, Is.Not.Null);
            Assert.That(definition.HasAuthoredRackAndSlots, Is.True);
            Assert.That(RegionPropArt.TryGet("col.rack", out var rackArt), Is.True);
            Assert.That(rackArt.UsesAuthoredTexture, Is.True);

            var map = MapWithRack(out var placement);
            var root = new GameObject("Coliseum rack presentation test");
            RegionProps scene = null;
            try
            {
                scene = RegionProps.Build(root.transform, map);
                var rackFoot = ArenaView.WorldPoint(placement.X * 32f, placement.Y * 32f);
                var rack = root.GetComponentsInChildren<Transform>().FirstOrDefault(t =>
                    t.name == "col.rack" && t.localPosition == rackFoot);
                Assert.That(rack, Is.Not.Null);
                var body = rack.GetComponent<SpriteRenderer>();
                var group = rack.GetComponent<SortingGroup>();
                Assert.That(group, Is.Not.Null, "Body and display items must sort as one planted prop relative to actors.");
                Assert.That(group.sortingLayerID, Is.EqualTo(body.sortingLayerID));
                Assert.That(group.sortingOrder, Is.EqualTo(2000 + Mathf.RoundToInt(placement.Y * 16f) + placement.Bias));
                Assert.That(body.sortingOrder, Is.Zero);
                Assert.That(body.sprite, Is.SameAs(rackArt.Sprite));

                var slots = definition.WeaponSlots;
                Assert.That(slots.Count, Is.EqualTo(3));
                for (int i = 0; i < slots.Count; i++)
                {
                    var slot = slots[i];
                    var item = At(rack, "Rack item " + slot.weapon);
                    Assert.That(item, Is.Not.Null);
                    Assert.That(item.sprite, Is.SameAs(StormInkWeaponArt.Get(slot.weapon, slot.viewRow)),
                        "The rack uses the same transferable sprite as the player's weapon.");
                    Assert.That(item.sharedMaterial, Is.SameAs(body.sharedMaterial), "Weapon pigment stays in the normal lit material path.");
                    Assert.That(item.sortingOrder, Is.EqualTo(i + 1));
                    Assert.That(item.transform.localPosition, Is.EqualTo(definition.TryGetFittedOffset(slot.normalizedAnchor, out var expected) ? expected : Vector2.zero));
                    Assert.That(item.transform.localScale.x, Is.EqualTo(slot.scale).Within(.001f));

                    var block = new MaterialPropertyBlock();
                    item.GetPropertyBlock(block);
                    Assert.That(block.GetFloat(Shader.PropertyToID("_StormInkResponse")), Is.EqualTo(1f), "Equipment uses matte painted response.");
                    Assert.That(block.GetFloat(Shader.PropertyToID("_RhSwayV")), Is.Zero, "Shared equipment atlas UVs must not inherit cloth/foliage wind.");
                }

                Assert.That(rack.GetComponentsInChildren<Collider2D>(true), Is.Empty, "The rack remains decorative and walk-through.");
                Assert.That(rack.GetComponentsInChildren<Collider>(true), Is.Empty, "The rack remains decorative and walk-through.");
            }
            finally
            {
                scene?.Dispose();
                Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void MissingRackPigmentKeepsWholeProceduralFallbackWithoutEquipmentOverlays()
        {
            var definition = ColiseumWeaponRackArtDefinition.Load();
            Assert.That(definition, Is.Not.Null);
            var serialized = new SerializedObject(definition);
            var pigment = serialized.FindProperty("Pigment");
            var original = pigment.objectReferenceValue;
            var root = new GameObject("Coliseum rack fallback test");
            RegionProps scene = null;
            try
            {
                pigment.objectReferenceValue = null;
                serialized.ApplyModifiedPropertiesWithoutUndo();
                Assert.That(RegionPropArt.TryGet("col.rack", out var art), Is.True);
                Assert.That(art.UsesAuthoredTexture, Is.False);

                var map = MapWithRack(out var placement);
                scene = RegionProps.Build(root.transform, map);
                var rackFoot = ArenaView.WorldPoint(placement.X * 32f, placement.Y * 32f);
                var rack = root.GetComponentsInChildren<Transform>().FirstOrDefault(t =>
                    t.name == "col.rack" && t.localPosition == rackFoot);
                Assert.That(rack, Is.Not.Null);
                Assert.That(rack.GetComponentsInChildren<SpriteRenderer>(true).Length, Is.EqualTo(1),
                    "Do not layer shared items on the old procedural rack, which already paints its weapon silhouettes.");
                Assert.That(rack.GetComponent<SortingGroup>(), Is.Null);
            }
            finally
            {
                scene?.Dispose();
                Object.DestroyImmediate(root);
                serialized.Update();
                serialized.FindProperty("Pigment").objectReferenceValue = original;
                serialized.ApplyModifiedPropertiesWithoutUndo();
            }
        }

        [Test]
        public void EditableRackSlotAnchorsAreFootRelativeAndCanBeMovedIndependently()
        {
            var definition = ColiseumWeaponRackArtDefinition.Load();
            Assert.That(definition, Is.Not.Null);
            Assert.That(definition.TryGetFittedOffset(definition.TextureFoot, out var footOffset), Is.True);
            Assert.That(footOffset.magnitude, Is.LessThan(.001f));

            var serialized = new SerializedObject(definition);
            var anchor = serialized.FindProperty("weaponSlots.Array.data[0].normalizedAnchor");
            Assert.That(anchor, Is.Not.Null);
            var original = anchor.vector2Value;
            try
            {
                Assert.That(definition.TryGetFittedOffset(original, out var before), Is.True);
                anchor.vector2Value = original + new Vector2(.05f, .08f);
                serialized.ApplyModifiedPropertiesWithoutUndo();
                var moved = definition.WeaponSlots[0].normalizedAnchor;
                Assert.That(definition.TryGetFittedOffset(moved, out var after), Is.True);
                Assert.That((after - before).magnitude, Is.GreaterThan(.05f),
                    "A designer can move one shared-weapon mount without repainting or changing other slots.");
            }
            finally
            {
                serialized.Update();
                serialized.FindProperty("weaponSlots.Array.data[0].normalizedAnchor").vector2Value = original;
                serialized.ApplyModifiedPropertiesWithoutUndo();
            }
        }
    }
}
