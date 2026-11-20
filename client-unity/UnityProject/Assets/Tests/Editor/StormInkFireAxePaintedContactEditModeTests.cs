using System.IO;
using System.Linq;
using NUnit.Framework;
using Roguehalla.Net;
using Roguehalla.Net.Protocol;
using UnityEngine;

namespace Roguehalla.Client.Tests
{
    public sealed class StormInkFireAxePaintedContactEditModeTests
    {
        static readonly Vector2Int[,] ReviewedHands =
        {
            { new(35,18), new(108,52), new(33,59), new(33,57) },
            { new(28,42), new(107,50), new(28,59), new(10,59) },
            { new(72,42), new(115,49), new(111,48), new(120,46) },
            { new(87,18), new(13,56), new(109,48), new(119,58) },
            { new(99,15), new(104,68), new(104,67), new(104,68) },
            { new(70,11), new(109,65), new(106,64), new(106,64) },
            { new(66,5), new(20,72), new(20,58), new(19,68) },
            { new(40,16), new(10,75), new(18,61), new(118,64) },
        };

        [TestCase(0, StormInkFireAxePose.ReleaseA, 67, 103)]
        [TestCase(1, StormInkFireAxePose.ReleaseA, 110, 95)]
        [TestCase(2, StormInkFireAxePose.ReleaseA, 122, 71)]
        [TestCase(3, StormInkFireAxePose.ReleaseA, 105, 25)]
        [TestCase(4, StormInkFireAxePose.ReleaseA, 93, 27)]
        [TestCase(5, StormInkFireAxePose.ReleaseA, 38, 24)]
        [TestCase(6, StormInkFireAxePose.ReleaseA, 20, 65)]
        [TestCase(7, StormInkFireAxePose.ReleaseA, 28, 99)]
        [TestCase(0, StormInkFireAxePose.ReleaseB, 39, 84)]
        [TestCase(1, StormInkFireAxePose.ReleaseB, 105, 91)]
        [TestCase(2, StormInkFireAxePose.ReleaseB, 115, 72)]
        [TestCase(3, StormInkFireAxePose.ReleaseB, 106, 40)]
        [TestCase(4, StormInkFireAxePose.ReleaseB, 77, 24)]
        [TestCase(5, StormInkFireAxePose.ReleaseB, 60, 69)]
        [TestCase(6, StormInkFireAxePose.ReleaseB, 17, 52)]
        [TestCase(7, StormInkFireAxePose.ReleaseB, 22, 66)]
        [TestCase(5, StormInkFireAxePose.Windup, 70, 11)]
        public void ThePresentedShaftIntersectsThePaintedFist(int row, StormInkFireAxePose pose, int fistX, int fistY)
        {
            var root = new GameObject("Painted axe contact");
            var paintedBody = ReadPng("Actors/FireAxe.png");
            var paintedAxe = ReadPng("Weapons/AxeViews.png");
            try
            {
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
                if (pose == StormInkFireAxePose.ReleaseB) presenter.ConfirmedCue(1, FxKind.Swing, WeaponId.Axe, aim);
                if (pose == StormInkFireAxePose.Windup) presenter.LocalAttackCue(1);
                else presenter.ConfirmedCue(1, FxKind.Swing, WeaponId.Axe, aim);
                Present();
                var renderers = root.GetComponentsInChildren<SpriteRenderer>();
                var body = renderers.Single(renderer => renderer.name == "Authored character");
                var axe = renderers.Single(renderer => renderer.name == "Equipped weapon cue");
                Assert.That(body.sprite, Is.SameAs(StormInkFireAxeAuthoring.Load().Frame(row, pose)));
                Assert.That(axe.sprite, Is.SameAs(StormInkWeaponArt.Get(WeaponId.Axe, row)));

                // Sample the independent painted fist and map it through the presented body into the
                // actual weapon alpha. Matching two authored UVs alone can position both in empty air.
                Vector2 bodyPixel = new Vector2(fistX + .5f, body.sprite.rect.height - fistY - .5f);
                Assert.That(AlphaAt(paintedBody, body.sprite, bodyPixel), Is.GreaterThan(.8f), "painted fist");
                var palm = body.transform.TransformPoint((bodyPixel - body.sprite.pivot) / body.sprite.pixelsPerUnit);
                Vector2 axePixel = (Vector2)axe.transform.InverseTransformPoint(palm) * axe.sprite.pixelsPerUnit + axe.sprite.pivot;
                Vector2 gripPixel = Vector2.Scale(StormInkWeaponArt.Grip(WeaponId.Axe, row), axe.sprite.rect.size);
                Assert.That(Vector2.Distance(axePixel, gripPixel), Is.LessThan(7f), "fist must meet the haft's binding");
                Assert.That(AlphaAt(paintedAxe, axe.sprite, axePixel), Is.GreaterThan(.7f), "painted shaft");
            }
            finally
            {
                Object.DestroyImmediate(root);
                Object.DestroyImmediate(paintedBody);
                Object.DestroyImmediate(paintedAxe);
            }
        }

        [TestCase(0)] [TestCase(1)] [TestCase(2)] [TestCase(3)]
        [TestCase(4)] [TestCase(5)] [TestCase(6)] [TestCase(7)]
        public void AllAnticipationAndRecoveryDrawingsKeepTheAxeAtTheReviewedFist(int row)
        {
            var root = new GameObject("Fire axe windup and recovery contact");
            var paintedBody = ReadPng("Actors/FireAxe.png");
            var paintedAxe = ReadPng("Weapons/AxeViews.png");
            try
            {
                var config = StormInkFireAxeAuthoring.Load();
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
                var body = renderers.Single(renderer => renderer.name == "Authored character");
                var axe = renderers.Single(renderer => renderer.name == "Equipped weapon cue");
                var active = (System.Collections.IDictionary)typeof(SpriteActorPresenter)
                    .GetField("_active", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                    .GetValue(presenter);
                object actor = active[1u];
                var releaseCount = actor.GetType().GetField("FireAxeReleaseCount");
                var attackAt = actor.GetType().GetField("AttackAt");

                var poses = new[]
                {
                    StormInkFireAxePose.Windup, StormInkFireAxePose.Recovery,
                    StormInkFireAxePose.WindupB, StormInkFireAxePose.RecoveryB
                };
                for (int slot = 0; slot < poses.Length; slot++)
                {
                    bool alternate = slot >= 2;
                    bool recovery = (slot & 1) != 0;
                    releaseCount.SetValue(actor, alternate ? 1 : 0);
                    presenter.ConfirmedCue(1, FxKind.Windup, WeaponId.Axe, aim, 250f);
                    if (recovery) presenter.ConfirmedCue(1, FxKind.Swing, WeaponId.Axe, aim);
                    if (recovery) attackAt.SetValue(actor, Time.unscaledTime - .2f);
                    Present();

                    var pose = poses[slot];
                    Assert.That(body.sprite, Is.SameAs(config.Frame(row, pose)), $"row {row} {pose} body");
                    Assert.That(axe.sprite, Is.SameAs(StormInkWeaponArt.Get(WeaponId.Axe, row)), $"row {row} {pose} shared axe");
                    Assert.That(config.TryHand(body.sprite, false, out var right), Is.True, $"row {row} {pose} registration");

                    Vector2Int landmark = ReviewedHands[row, slot];
                    var pixel = new Vector2(landmark.x + .5f, body.sprite.rect.height - landmark.y - .5f);
                    Assert.That(AlphaAt(paintedBody, body.sprite, pixel), Is.GreaterThan(.8f), $"row {row} {pose} reviewed hand pixel");
                    var registeredPixel = new Vector2(right.x * body.sprite.rect.width - .5f,
                        body.sprite.rect.height - right.y * body.sprite.rect.height - .5f);
                    Assert.That(Vector2.Distance(registeredPixel, landmark), Is.LessThan(.75f),
                        $"row {row} {pose} must register its reviewed anatomical hand");

                    var palm = body.transform.TransformPoint((pixel - body.sprite.pivot) / body.sprite.pixelsPerUnit);
                    Vector2 axePixel = (Vector2)axe.transform.InverseTransformPoint(palm) * axe.sprite.pixelsPerUnit + axe.sprite.pivot;
                    Vector2 gripPixel = Vector2.Scale(StormInkWeaponArt.Grip(WeaponId.Axe, row), axe.sprite.rect.size);
                    Assert.That(Vector2.Distance(axePixel, gripPixel), Is.LessThan(7f), $"row {row} {pose} haft reaches fist");
                    Assert.That(AlphaAt(paintedAxe, axe.sprite, axePixel), Is.GreaterThan(.7f), $"row {row} {pose} painted haft");
                }
            }
            finally
            {
                Object.DestroyImmediate(root);
                Object.DestroyImmediate(paintedBody);
                Object.DestroyImmediate(paintedAxe);
            }
        }

        static Texture2D ReadPng(string path)
        {
            var texture = new Texture2D(2, 2);
            Assert.That(ImageConversion.LoadImage(texture,
                File.ReadAllBytes(Path.Combine(Application.dataPath, "Resources/StormInk", path))), Is.True);
            return texture;
        }

        static float AlphaAt(Texture2D texture, Sprite sprite, Vector2 localPixel)
        {
            if (!new Rect(Vector2.zero, sprite.rect.size).Contains(localPixel)) return 0f;
            var pixel = sprite.rect.position + localPixel;
            return texture.GetPixel(Mathf.FloorToInt(pixel.x), Mathf.FloorToInt(pixel.y)).a;
        }
    }
}
