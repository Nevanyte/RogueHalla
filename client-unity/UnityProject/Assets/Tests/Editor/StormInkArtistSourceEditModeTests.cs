using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Roguehalla.Net;
using Roguehalla.Net.Protocol;
using UnityEngine;

namespace Roguehalla.Client.Tests
{
    public sealed class StormInkArtistSourceEditModeTests
    {
        const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;

        [Test]
        public void FighterAndEquippedAxeExposeActualCueFacingAndClearOnReuse()
        {
            var root = new GameObject("Artist source proof");
            try
            {
                var presenter = new SpriteActorPresenter(root.transform, Resources.Load<ShowcaseCatalog>("Showcase/Catalog"));
                void Present(WeaponId weapon)
                {
                    presenter.Begin();
                    presenter.Present(1, 100, 100, Mathf.PI / 4f, 100, FactionId.Fire, ActorStatus.Alive, weapon,
                        ActorKind.Player, null, 0, 0, true);
                    presenter.End();
                }
                Present(WeaponId.Axe);
                presenter.ConfirmedCue(1, FxKind.Swing, WeaponId.Axe, -Mathf.PI / 2f);
                Present(WeaponId.Axe);
                var sources = root.GetComponentsInChildren<StormInkArtistSource>();
                var body = sources.Single(s => s.Kind == StormInkArtistSourceKind.FireAxeBody);
                var held = sources.Single(s => s.Kind == StormInkArtistSourceKind.EquippedAxe);
                Assert.That(body.FireBody, Is.SameAs(StormInkFireAxeAuthoring.Load()));
                Assert.That(body.PoseRow, Is.EqualTo(4));
                Assert.That(held.PoseRow, Is.EqualTo(4));
                Assert.That(held.SharedAxe, Is.SameAs(StormInkAxeArtAuthoring.Load()));
                Assert.That(held.Sprite, Is.SameAs(StormInkWeaponArt.Get(WeaponId.Axe, 4)));
                Assert.That(body.TryBodyPalm(false, out var palm), Is.True);
                Assert.That(Vector2.Distance(Socket(body, palm), Socket(held, held.HeldGripUv)), Is.LessThan(.001f));

                Present(WeaponId.Fists);
                Assert.That(body.Kind, Is.EqualTo(StormInkArtistSourceKind.None));
                Assert.That(held.Kind, Is.EqualTo(StormInkArtistSourceKind.None));
                Assert.That(body.FireBody, Is.Null);
                Assert.That(held.SharedAxe, Is.Null);
            }
            finally { Object.DestroyImmediate(root); }
        }

        [Test]
        public void GroundAxeNamesCanonicalArtistAssetAndDeliversNoStaleSourceOnOtherItems()
        {
            var root = new GameObject("Dropped artist source proof");
            try
            {
                var arena = root.AddComponent<ArenaView>();
                typeof(ArenaView).GetMethod("Awake", Hidden).Invoke(arena, null);
                void Present(WeaponId weapon)
                {
                    var state = new ServerMsg.State { Tick = 7, Items = new StateItem[]
                    {
                        new PropsState { Rows = new[] { new WireProp
                        {
                            Handle = new PickupHandle(2), Kind = PropKind.Ground,
                            X = 400, Y = 200, GroundWeapon = (byte)weapon,
                        } } },
                    } };
                    var props = new CollectionsView();
                    typeof(CollectionsView).GetMethod("Apply", Hidden).Invoke(props, new object[] { state });
                    arena.PresentCollections(props);
                }
                Present(WeaponId.Axe);
                var source = root.GetComponentsInChildren<StormInkArtistSource>().Single();
                Assert.That(source.Kind, Is.EqualTo(StormInkArtistSourceKind.DroppedAxe));
                Assert.That(source.PoseRow, Is.Zero);
                Assert.That(source.SharedAxe, Is.SameAs(StormInkAxeArtAuthoring.Load()));
                Assert.That(source.Sprite, Is.SameAs(StormInkWeaponArt.Get(WeaponId.Axe, 0)));
                Assert.That(source.GroundCentre, Is.EqualTo(new Vector2(0, .39f)));
                Assert.That(source.Renderer.transform.position, Is.EqualTo(ArenaView.WorldPoint(50, 25) + (Vector3)source.GroundCentre));

                Present(WeaponId.Shield);
                Assert.That(source.Kind, Is.EqualTo(StormInkArtistSourceKind.None));
                Assert.That(source.Sprite, Is.Null);
                Assert.That(source.SharedAxe, Is.Null);
            }
            finally { Object.DestroyImmediate(root); }
        }

        static Vector3 Socket(StormInkArtistSource source, Vector2 uv) => source.Renderer.transform.TransformPoint(
            (Vector2.Scale(uv, source.Sprite.rect.size) - source.Sprite.pivot) / source.Sprite.pixelsPerUnit);
    }
}
