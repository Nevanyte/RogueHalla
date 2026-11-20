using System.Collections.Generic;
using NUnit.Framework;
using Roguehalla.Net;
using UnityEngine;

namespace Roguehalla.Client.Tests
{
    public sealed class StormInkChainWardenEditModeTests
    {
        [Test]
        public void EveryGripBelongsToItsOwnChainWardenFacingAndPose()
        {
            var look = Resources.Load<ShowcaseCatalog>("Showcase/Catalog").Find(
                ShowcaseActorLook.AppearanceKey(ActorKind.RegionalMob,FactionId.Sentenced,MobTypeId.ChainWarden));
            var seen = new HashSet<Sprite>();
            for (int row = 0; row < 8; row++)
            for (int col = 0; col < 4; col++)
            {
                var art = StormInkActors.Frame(ActorKind.RegionalMob,FactionId.Sentenced,MobTypeId.ChainWarden,
                    look,col < 2 ? look.Walk : look.Attack,7-row,col == 1 ? 5 : col == 3 ? 3 : 0,8,out bool mirror);
                Assert.IsNotNull(art,$"Chain Warden {row}/{col}");
                Assert.AreEqual("ChainWarden",art.texture.name);
                Assert.IsTrue(seen.Add(art));
                Assert.IsFalse(mirror);
                Assert.IsTrue(StormInkActors.TryMobHand(MobTypeId.ChainWarden,art,false,out var r));
                Assert.IsTrue(StormInkActors.TryMobHand(MobTypeId.ChainWarden,art,true,out var l));
                Assert.That(Vector2.Distance(r,l),Is.GreaterThan(.025f));
            }
        }
    }
}
