using System;
using System.Collections;
using System.IO;
using System.Reflection;
using System.Linq;
using System.Security.Cryptography;
using NUnit.Framework;
using Roguehalla.Net;
using Roguehalla.Net.Protocol;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Roguehalla.Client.Tests
{
    public sealed class StormInkFireAxeAuthoringEditModeTests
    {
        [TestCase(1, "9095583F08705435711ABBAC0F322980388DE3CB0B4533D94D5E5AFFE8C66478")]
        [TestCase(2, "A359251F4F92A9D85D94187A0BF2DE817C9321ED4A945E1197070D6F8DDA6861")]
        [TestCase(4, "AB3C4B085BBF63580B83C7AF8EEA5263AB0787BD848685CF5A957357B0890F0F")]
        public void ProductionRetainsTheExactApprovedStrikes(int row, string expected)
        {
            var texture = new Texture2D(2, 2);
            try
            {
                Assert.That(ImageConversion.LoadImage(texture, File.ReadAllBytes(Path.Combine(
                    Application.dataPath, "Resources/StormInk/Actors/FireAxe.png"))), Is.True);
                var pixels = texture.GetPixels(128, (7 - row) * 128, 128, 128);
                byte[] bytes = pixels.SelectMany(c =>
                {
                    Color32 rgba = c;
                    return new[] { rgba.r, rgba.g, rgba.b, rgba.a };
                }).ToArray();
                using var hash = SHA256.Create();
                Assert.That(BitConverter.ToString(hash.ComputeHash(bytes)).Replace("-", ""), Is.EqualTo(expected));
            }
            finally { Object.DestroyImmediate(texture); }
        }

        [TestCase(0)] [TestCase(1)] [TestCase(2)] [TestCase(3)]
        [TestCase(4)] [TestCase(5)] [TestCase(6)] [TestCase(7)]
        public void TwoDeliveredReleasesKeepSharedAxeAndPhysicalFoot(int row)
        {
            var root = new GameObject("authored Fire pose");
            try
            {
                var config = StormInkFireAxeAuthoring.Load();
                Assert.That(config, Is.Not.Null);
                foreach (StormInkFireAxePose phase in Enum.GetValues(typeof(StormInkFireAxePose)))
                {
                    var body = config.Frame(row, phase);
                    Assert.That(body, Is.Not.Null, phase.ToString());
                    Assert.That(config.TryHand(body, false, out var right), Is.True);
                    Assert.That(config.TryHand(body, true, out var left), Is.True);
                    Assert.That(Vector2.Distance(left, right), Is.GreaterThan(.08f));
                }

                var presenter = new SpriteActorPresenter(root.transform, Resources.Load<ShowcaseCatalog>("Showcase/Catalog"));
                float aim = (2 - row) * Mathf.PI / 4f;
                void Present()
                {
                    presenter.Begin();
                    presenter.Present(1, 100, 100, aim, 100, FactionId.Fire, ActorStatus.Alive,
                        WeaponId.Axe, ActorKind.Player, null, 0, 0, true);
                    presenter.End();
                }
                Present();
                var renderers = root.GetComponentsInChildren<SpriteRenderer>();
                var bodyRenderer = renderers.Single(r => r.name == "Authored character");
                var weapon = renderers.Single(r => r.name == "Equipped weapon cue");
                var halo = renderers.Single(r => r.name == "Faction footprint");
                Vector3 foot = halo.transform.position;
                var actors = (IDictionary)typeof(SpriteActorPresenter)
                    .GetField("_active", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(presenter);
                object actor = actors[1u];
                var attackAt = actor.GetType().GetField("AttackAt");
                foreach (bool alternate in new[] { false, true })
                {
                    presenter.ConfirmedCue(1, FxKind.Windup, WeaponId.Axe, aim, 250f);
                    Present();
                    Assert.That(bodyRenderer.sprite, Is.SameAs(config.Frame(row,
                        alternate ? StormInkFireAxePose.WindupB : StormInkFireAxePose.Windup)));
                    presenter.ConfirmedCue(1, FxKind.Swing, WeaponId.Axe, aim);
                    Present();
                    Assert.That(bodyRenderer.sprite, Is.SameAs(config.Frame(row,
                        alternate ? StormInkFireAxePose.ReleaseB : StormInkFireAxePose.ReleaseA)));
                    Assert.That(weapon.sprite, Is.SameAs(StormInkWeaponArt.Get(WeaponId.Axe, row)));
                    Assert.That(halo.transform.position, Is.EqualTo(foot));
                    Assert.That(config.TryHand(bodyRenderer.sprite, false, out var palm), Is.True);
                    Assert.That(Vector2.Distance(Socket(bodyRenderer, palm), Socket(weapon, StormInkWeaponArt.Grip(WeaponId.Axe, row))), Is.LessThan(.001f));
                    attackAt.SetValue(actor, Time.unscaledTime - .2f);
                    Present();
                    Assert.That(bodyRenderer.sprite, Is.SameAs(config.Frame(row,
                        alternate ? StormInkFireAxePose.RecoveryB : StormInkFireAxePose.Recovery)));
                }
                Assert.That(ShowcaseSprites.Get("weapon4"), Is.SameAs(StormInkWeaponArt.Get(WeaponId.Axe)));
                Assert.That(StormInkWeaponArt.GroundCentre(WeaponId.Axe), Is.EqualTo(new Vector2(0, .39f)));
            }
            finally { Object.DestroyImmediate(root); }
        }

        [TestCase(0)] [TestCase(1)] [TestCase(2)] [TestCase(3)]
        [TestCase(4)] [TestCase(5)] [TestCase(6)] [TestCase(7)]
        public void AuthoredGuardBearingsUseFourRegisteredDrawings(int row)
        {
            var config = StormInkFireAxeAuthoring.Load();
            Assert.That(config.GuardStagesFor(row), Is.EqualTo(4));
            var poses = Enumerable.Range(0, 4)
                .Select(stage => config.Frame(row, StormInkFireAxePose.Guard, stage)).ToArray();
            Assert.That(poses.Distinct().Count(), Is.EqualTo(4));
            foreach (var pose in poses)
            {
                Assert.That(config.TryHand(pose, false, out var right), Is.True);
                Assert.That(config.TryHand(pose, true, out var left), Is.True);
                Assert.That(Vector2.Distance(right, left), Is.GreaterThan(.1f));
                Assert.That(pose.pivot.y / pose.rect.height, Is.EqualTo(config.FootPivot.y));
            }
        }

        [TestCase(0)] [TestCase(1)] [TestCase(2)] [TestCase(3)]
        [TestCase(4)] [TestCase(5)] [TestCase(6)] [TestCase(7)]
        public void AuthoredWalkBearingsUseEightRegisteredDrawingsThroughTheGaitCycle(int row)
        {
            var config = StormInkFireAxeAuthoring.Load();
            Assert.That(config.StrideStagesFor(row), Is.EqualTo(8));
            var poses = Enumerable.Range(0, 8).Select(stage => config.Frame(row, StormInkFireAxePose.Stride,
                Gait.FrameIndex(stage + .25f, config.StrideStagesFor(row)))).ToArray();
            Assert.That(poses.Distinct().Count(), Is.EqualTo(8));
            foreach (var pose in poses)
            {
                Assert.That(config.TryHand(pose, false, out var right), Is.True);
                Assert.That(config.TryHand(pose, true, out var left), Is.True);
                Assert.That(Vector2.Distance(right, left), Is.GreaterThan(.1f));
                Assert.That(pose.pivot.y / pose.rect.height, Is.EqualTo(config.FootPivot.y));
            }
        }

        [Test]
        public void EditedHeightAndAxeSocketMoveTheArtAroundTheSameDeliveredFoot()
        {
            var root = new GameObject("Fire artist controls");
            var fighter = StormInkFireAxeAuthoring.Load();
            var axe = StormInkAxeArtAuthoring.Load();
            const BindingFlags PrivateInstance = BindingFlags.NonPublic | BindingFlags.Instance;
            var height = fighter.GetType().GetField("worldHeight", PrivateInstance);
            var grips = (Vector2[])axe.GetType().GetField("grips", PrivateInstance).GetValue(axe);
            const int row = 1;
            var oldGrip = grips[row];
            float oldHeight = fighter.WorldHeight;
            try
            {
                var presenter = new SpriteActorPresenter(root.transform, Resources.Load<ShowcaseCatalog>("Showcase/Catalog"));
                void Present(WeaponId weapon)
                {
                    presenter.Begin();
                    presenter.Present(1, 100, 100, Mathf.PI / 4f, 100, FactionId.Fire, ActorStatus.Alive,
                        weapon, ActorKind.Player, null, 0, 0, true);
                    presenter.End();
                }
                Present(WeaponId.Axe);
                var actor = root.transform.Find("Observed actor");
                var body = actor.Find("Authored character").GetComponent<SpriteRenderer>();
                var held = actor.Find("Equipped weapon cue").GetComponent<SpriteRenderer>();
                var footprint = actor.Find("Faction footprint").position;

                height.SetValue(fighter, oldHeight * 1.20f);
                grips[row] = oldGrip + new Vector2(.025f, -.035f);
                Present(WeaponId.Axe);
                Assert.That(actor.Find("Faction footprint").position, Is.EqualTo(footprint));
                Assert.That(body.transform.localScale.y, Is.EqualTo(oldHeight * 1.20f));
                Assert.That(held.sprite, Is.SameAs(StormInkWeaponArt.Get(WeaponId.Axe, row)));
                Assert.That(fighter.TryHand(body.sprite, false, out var right), Is.True);
                Assert.That(Vector2.Distance(Socket(body, right), Socket(held, grips[row])), Is.LessThan(.001f));

                foreach (var weapon in new[] { WeaponId.Fists, WeaponId.Sword, WeaponId.Bow, WeaponId.Shield, WeaponId.Wand })
                {
                    Present(weapon);
                    Assert.That(body.sprite.name, Does.StartWith(weapon == WeaponId.Sword ? "FireAxe/" : "FireChampion/"));
                    Assert.That(body.transform.localScale.y,
                        Is.EqualTo(weapon == WeaponId.Sword ? oldHeight * 1.20f : oldHeight));
                }
            }
            finally
            {
                height.SetValue(fighter, oldHeight);
                grips[row] = oldGrip;
                Object.DestroyImmediate(root);
            }
        }

        static Vector3 Socket(SpriteRenderer renderer, Vector2 uv) =>
            renderer.transform.TransformPoint((Vector2.Scale(uv, renderer.sprite.rect.size) - renderer.sprite.pivot) / renderer.sprite.pixelsPerUnit);
    }
}
