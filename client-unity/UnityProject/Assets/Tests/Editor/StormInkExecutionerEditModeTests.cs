using NUnit.Framework;
using Roguehalla.Net;
using UnityEngine;

namespace Roguehalla.Client.Tests
{
    public sealed class StormInkExecutionerEditModeTests
    {
        [Test]
        public void ExecutionerKeepsItsSwordInThePaintedRightFistThroughEveryFacing()
        {
            var look = Resources.Load<ShowcaseCatalog>("Showcase/Catalog").Find(
                ShowcaseActorLook.AppearanceKey(ActorKind.RegionalMob,FactionId.Sentenced,MobTypeId.Executioner));
            for (int row = 0; row < 8; row++)
            for (int col = 0; col < 4; col++)
            {
                var body = StormInkActors.Frame(ActorKind.RegionalMob,FactionId.Sentenced,MobTypeId.Executioner,look,
                    col < 2 ? look.Walk : look.Attack,7-row,col == 1 ? 5 : col == 3 ? 3 : 0,8,out bool mirror);
                Assert.IsFalse(mirror);
                Assert.IsTrue(StormInkActors.TryMobHand(MobTypeId.Executioner,body,false,out var right),$"Executioner {row}/{col}");
                Assert.IsTrue(StormInkActors.TryMobHand(MobTypeId.Executioner,body,true,out var left));
                Assert.That(Vector2.Distance(right,left),Is.GreaterThan(.025f));
            }
        }
    }
}
