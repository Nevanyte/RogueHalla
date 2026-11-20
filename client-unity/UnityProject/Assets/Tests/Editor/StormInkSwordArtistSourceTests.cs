using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Roguehalla.Net;
using Roguehalla.Net.Protocol;
using UnityEngine;

namespace Roguehalla.Client.Tests
{
    public sealed class StormInkSwordArtistSourceTests
    {
        [Test]
        public void SelectingFireDuringSwordReleaseExposesTheCurrentPalmAndSharedSizing()
        {
            var root = new GameObject("Fire sword source selection");
            try
            {
                var presenter = new SpriteActorPresenter(root.transform, Resources.Load<ShowcaseCatalog>("Showcase/Catalog"));
                void Present(WeaponId weapon)
                {
                    presenter.Begin();
                    presenter.Present(1, 100, 100, 0f, 100, FactionId.Fire, ActorStatus.Alive,
                        weapon, ActorKind.Player, null, 0, 0, true);
                    presenter.End();
                }
                Present(WeaponId.Sword);
                presenter.ConfirmedCue(1, FxKind.Swing, WeaponId.Sword, -Mathf.PI / 2f);
                Present(WeaponId.Sword);
                var bindings = root.GetComponentsInChildren<StormInkArtistSource>();
                var body = bindings.Single(s => s.Kind == StormInkArtistSourceKind.FireSwordBody);
                var sword = bindings.Single(s => s.Kind == StormInkArtistSourceKind.EquippedSword);
                Assert.That(body.FireBody, Is.SameAs(StormInkFireAxeAuthoring.Load()));
                Assert.That(body.BodyPainting, Is.SameAs(StormInkFireSwordAuthoring.Load()));
                Assert.That(body.TryBodyPalm(false, out var palm), Is.True);
                Assert.That(body.SwordBody.TryHand(body.Sprite, false, out var paintedPalm), Is.True);
                Assert.That(palm, Is.EqualTo(paintedPalm));
                Assert.That(sword.WeaponSource, Is.SameAs(StormInkSwordArtAuthoring.Load()));
                Assert.That(sword.HeldGripUv, Is.EqualTo(StormInkWeaponArt.Grip(WeaponId.Sword, 4)));
                Assert.That(sword.Sprite, Is.SameAs(StormInkWeaponArt.Get(WeaponId.Sword, 4)));

                Present(WeaponId.Axe);
                Assert.That(sword.Kind, Is.EqualTo(StormInkArtistSourceKind.EquippedAxe));
                Assert.That(body.SwordBody, Is.Null);
            }
            finally { Object.DestroyImmediate(root); }
        }

        [Test]
        public void DroppedSwordNamesTheSameGripAssetAndFollowsPublicPickupPosition()
        {
            var root = new GameObject("Ground sword source selection");
            const BindingFlags hidden = BindingFlags.Instance | BindingFlags.NonPublic;
            try
            {
                var arena = root.AddComponent<ArenaView>();
                typeof(ArenaView).GetMethod("Awake", hidden).Invoke(arena, null);
                var collection = new CollectionsView();
                var state = new ServerMsg.State { Tick = 7, Items = new StateItem[] {
                    new PropsState { Rows = new[] {
                        new WireProp { Handle = new PickupHandle(2), Kind = PropKind.Ground, X = 400, Y = 200,
                            GroundWeapon = (byte)WeaponId.Sword }
                    } }
                } };
                typeof(CollectionsView).GetMethod("Apply", hidden).Invoke(collection, new object[] { state });
                arena.PresentCollections(collection);
                var source = root.GetComponentsInChildren<StormInkArtistSource>().Single();
                Assert.That(source.Kind, Is.EqualTo(StormInkArtistSourceKind.DroppedSword));
                Assert.That(source.WeaponSource, Is.SameAs(StormInkSwordArtAuthoring.Load()));
                Assert.That(source.Renderer.transform.position, Is.EqualTo(ArenaView.WorldPoint(50, 25) + (Vector3)source.GroundCentre));
                Assert.That(source.Sprite, Is.SameAs(StormInkWeaponArt.Get(WeaponId.Sword, 0)));
            }
            finally { Object.DestroyImmediate(root); }
        }
    }
}
