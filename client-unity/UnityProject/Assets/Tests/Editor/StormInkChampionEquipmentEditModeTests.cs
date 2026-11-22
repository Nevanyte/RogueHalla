using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using Roguehalla.Net;
using UnityEngine;

namespace Roguehalla.Client.Tests
{
    public sealed class StormInkChampionEquipmentEditModeTests
    {
        [Test]
        public void EveryFactionUsesTheSameEightViewsOfEveryEquippedItem()
        {
            var root = new GameObject("Shared equipment proof");
            try
            {
                var presenter = new SpriteActorPresenter(root.transform, Resources.Load<ShowcaseCatalog>("Showcase/Catalog"));
                var art = typeof(ShowcaseSprites).Assembly.GetType("Roguehalla.Client.StormInkWeaponArt");
                Assert.That(art, Is.Not.Null, "all 200 held views come from shared canonical equipment");
                var byFacing = art.GetMethod("Get", BindingFlags.Public | BindingFlags.Static, null,
                    new[] { typeof(WeaponId), typeof(int) }, null);
                Sprite Equipment(WeaponId weapon, int row) => (Sprite)byFacing.Invoke(null, new object[] { weapon, row });
                for (int item = 0; item < 5; item++)
                {
                    var weapon = (WeaponId)item;
                    var distinct = new HashSet<Sprite>();
                    for (int row = 0; row < 8; row++)
                    {
                        float heading = (2 - row) * Mathf.PI / 4f;
                        var canonical = Equipment(weapon, row);
                        Assert.IsNotNull(canonical, $"{weapon} view {row}");
                        Assert.IsTrue(distinct.Add(canonical), $"{weapon} repeats view {row}");
                        Assert.AreEqual(weapon + "Views", canonical.texture.name);
                        if (row == 0) Assert.AreSame(ShowcaseSprites.Get("weapon" + item), canonical);
                        for (int house = 0; house < 5; house++)
                        {
                            presenter.Begin();
                            presenter.Present(1, 160, 160, heading, 100, (FactionId)house, ActorStatus.Alive,
                                weapon, ActorKind.Player, null, 0, 0, true);
                            presenter.End();
                            var held = root.transform.Find("Observed actor/Equipped weapon cue").GetComponent<SpriteRenderer>();
                            Assert.AreSame(canonical, held.sprite, $"{(FactionId)house} {weapon} view {row}");
                            Assert.AreEqual(Color.white, held.color);
                            Assert.IsFalse(held.flipX);
                        }
                    }
                }
                Assert.IsNull(Equipment(WeaponId.Fists, 0));
                Assert.IsNull(Equipment((WeaponId)250, 0));
            }
            finally { Object.DestroyImmediate(root); }
        }

        [TestCase(ActorKind.Player, FactionId.Fire, -1, "hero")]
        [TestCase(ActorKind.Player, FactionId.Sentenced, -1, "hero")]
        [TestCase(ActorKind.Player, FactionId.Plague, -1, "hero")]
        [TestCase(ActorKind.Player, FactionId.Frost, -1, "hero")]
        [TestCase(ActorKind.Player, FactionId.Holy, -1, "hero")]
        [TestCase(ActorKind.RegionalMob, FactionId.Sentenced, (int)MobTypeId.Executioner, "mob-goblin-elite")]
        public void EachFacingHasItsOwnReadyStrideWindupAndStrike(ActorKind kind, FactionId faction, int type, string key)
        {
            var catalog = Resources.Load<ShowcaseCatalog>("Showcase/Catalog");
            var look = catalog.Find(key);
            var poses = new HashSet<Rect>();
            MobTypeId? mob = type < 0 ? (MobTypeId?)null : (MobTypeId)type;
            // Catalog directions are SW, W, NW, N, NE, E, SE, S.
            int[] directions = { 7, 6, 5, 4, 3, 2, 1, 0 };
            for (int row = 0; row < directions.Length; row++)
            {
                void Check(Sprite[] clip, int frame, int column)
                {
                    var ink = StormInkActors.Frame(kind, faction, mob, look, clip,
                        directions[row], frame, 8, out bool mirrored);
                    Assert.IsNotNull(ink, $"row {row} pose {column}");
                    Assert.IsFalse(mirrored, $"row {row} must preserve the anatomical hands");
                    Assert.AreEqual(new Rect(column * 128, (7 - row) * 128, 128, 128), ink.rect,
                        $"row {row} pose {column}");
                    Assert.IsTrue(poses.Add(ink.rect), $"repeated drawing in row {row} pose {column}");
                }
                Check(look.Walk, 0, 0);
                Check(look.Walk, 5, 1);
                Check(look.Attack, 0, 2);
                Check(look.Attack, 3, 3);
            }
            Assert.AreEqual(32, poses.Count);
        }
    }
}
