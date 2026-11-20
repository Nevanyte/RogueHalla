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
