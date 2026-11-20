using System.Collections.Generic;
using NUnit.Framework;
using Roguehalla.Net;
using UnityEngine;

namespace Roguehalla.Client.Tests
{
    /// <summary>Checks the painted wand elite at the same clock positions used by other regional attackers.</summary>
    public sealed class StormInkBloodSaintEditModeTests
    {
        [Test]
        public void HerEightDirectionsRetainFiveDistinctPoses()
        {
            var look = Resources.Load<ShowcaseCatalog>("Showcase/Catalog").Find("mage-female");
            var drawings = new HashSet<Rect>();
            for (int row = 0; row < 8; row++)
            for (int column = 0; column < 5; column++)
            {
                var clip = column < 2 ? look.Walk : look.Cast;
                int frame = new[] {0, 5, 0, 3, 7}[column];
                var sprite = StormInkActors.Frame(ActorKind.RegionalMob, FactionId.Holy, MobTypeId.BloodSaint,
                    look, clip, 7 - row, frame, 8, out bool mirror);
                Assert.IsNotNull(sprite, $"Missing facing {row}, pose {column}");
                Assert.AreEqual($"BloodSaint/{row}/{column}", sprite.name);
                Assert.IsTrue(drawings.Add(sprite.rect));
                Assert.IsFalse(mirror);
            }
        }

        [Test]
        public void SharingHerArchetypeDoesNotExposeTheEliteLook()
        {
            var look = Resources.Load<ShowcaseCatalog>("Showcase/Catalog").Find("mage-female");
            Assert.IsNull(StormInkActors.Frame(ActorKind.RegionalMob, null, MobTypeId.BloodSaint,
                look, look.Idle, 0, 0, 1, out _));
            var champion = StormInkActors.Frame(ActorKind.Player, FactionId.Holy, null,
                look, look.Idle, 0, 0, 1, out _);
            Assert.AreEqual("HolyChampion", champion.texture.name);
        }
    }
}
