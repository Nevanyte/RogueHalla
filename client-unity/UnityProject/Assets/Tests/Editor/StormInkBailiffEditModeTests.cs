using System.Collections.Generic;
using NUnit.Framework;
using Roguehalla.Net;
using UnityEngine;

namespace Roguehalla.Client.Tests
{
    public sealed class StormInkBailiffEditModeTests
    {
        [Test]
        public void TheBailiffHasDistinctBodyDrawingsAndTwoRegisteredHandsThroughEveryFacing()
        {
            var catalog = Resources.Load<ShowcaseCatalog>("Showcase/Catalog");
            var look = catalog.Find(ShowcaseActorLook.AppearanceKey(ActorKind.RegionalMob, FactionId.Sentenced, MobTypeId.Bailiff));
            var registration = typeof(StormInkActors).GetMethod("TryMobHand");
            var visited = new HashSet<Rect>();
            for (int row = 0; row < 8; row++)
            for (int col = 0; col < 4; col++)
            {
                var clip = col < 2 ? look.Walk : look.Attack;
                int frame = col == 1 ? 5 : col == 3 ? 3 : 0;
                var art = StormInkActors.Frame(ActorKind.RegionalMob, FactionId.Sentenced, MobTypeId.Bailiff,
                    look, clip, 7 - row, frame, 8, out var mirror);
                Assert.IsNotNull(art, $"Bailiff facing {row}, pose {col}");
                Assert.AreEqual("Bailiff", art.texture.name);
                Assert.IsTrue(visited.Add(art.rect));
                Assert.IsFalse(mirror);
                Assert.IsNotNull(registration);
                var right = new object[] { MobTypeId.Bailiff, art, false, null };
                var left = new object[] { MobTypeId.Bailiff, art, true, null };
                Assert.AreEqual(true, registration.Invoke(null, right));
                Assert.AreEqual(true, registration.Invoke(null, left));
                Assert.That(Vector2.Distance((Vector2)right[3], (Vector2)left[3]), Is.GreaterThan(.03f));
            }
        }
    }
}
