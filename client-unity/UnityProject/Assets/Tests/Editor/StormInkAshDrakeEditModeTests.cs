using System.Collections.Generic;
using NUnit.Framework;
using Roguehalla.Net;
using UnityEngine;

namespace Roguehalla.Client.Tests
{
    public sealed class StormInkAshDrakeEditModeTests
    {
        [Test]
        public void EachCastPoseHasItsOwnDrawingAndAnAttachedFocus()
        {
            var look = Resources.Load<ShowcaseCatalog>("Showcase/Catalog").Find(
                ShowcaseActorLook.AppearanceKey(ActorKind.RegionalMob, FactionId.Fire, MobTypeId.AshDrake));
            var grip = typeof(StormInkActors).GetMethod("TryCreatureFocus");
            var poses = new HashSet<Rect>();
            for (int row = 0; row < 8; row++)
            for (int col = 0; col < 4; col++)
            {
                var clip = col < 2 ? look.Walk : look.Cast.Length > 0 ? look.Cast : look.Attack;
                int frame = col == 1 ? 5 : col == 3 ? 3 : 0;
                var body = StormInkActors.Frame(ActorKind.RegionalMob, FactionId.Fire, MobTypeId.AshDrake,
                    look, clip, 7 - row, frame, 8, out bool mirror);
                Assert.IsNotNull(body, $"Ash Drake pose {row}/{col}");
                Assert.AreEqual("AshDrake", body.texture.name);
                Assert.IsTrue(poses.Add(body.rect));
                Assert.IsFalse(mirror);
                Assert.IsNotNull(grip);
                var args = new object[] { MobTypeId.AshDrake, body, null };
                Assert.AreEqual(true, grip.Invoke(null, args));
                var point = (Vector2)args[2];
                Assert.That(point.x, Is.InRange(.015f, .985f));
                Assert.That(point.y, Is.InRange(.1f, .98f));
            }
        }
    }
}
