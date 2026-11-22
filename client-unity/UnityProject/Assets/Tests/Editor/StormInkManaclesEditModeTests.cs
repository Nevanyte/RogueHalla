using NUnit.Framework;
using Roguehalla.Net;
using Roguehalla.Net.Protocol;
using UnityEngine;

namespace Roguehalla.Client.Tests
{
    public sealed class StormInkManaclesEditModeTests
    {
        [Test]
        public void AConfirmedWardenLashMovesItsCuffAndLeavesTheSharedSwordAtTheHip()
        {
            var root = new GameObject("Warden test");
            try
            {
                var presenter = new SpriteActorPresenter(root.transform, Resources.Load<ShowcaseCatalog>("Showcase/Catalog"));
                void Show(MobTypeId type, ActorStatus state) => presenter.Present(17, 32, 32, 0, 100, FactionId.Sentenced,
                    state, WeaponId.Sword, ActorKind.RegionalMob, type, 0, 0, false);
                presenter.Begin();
                Show(MobTypeId.ChainWarden, ActorStatus.Alive);
                var actor = root.transform.GetChild(0);
                var chain = actor.Find("Warden manacle lash");
                Assert.IsNotNull(chain);
                var cuff = chain.Find("Weighted cuff");
                Assert.AreEqual(9, chain.childCount);
                var rest = cuff.position;
                presenter.ConfirmedCue(17, FxKind.Swing, WeaponId.Sword, 0);
                Show(MobTypeId.ChainWarden, ActorStatus.Alive);
                Assert.Greater(cuff.position.x - rest.x, .5f, "Facing east releases the weighted cuff into the melee arc");
                var sword = actor.Find("Equipped weapon cue").GetComponent<SpriteRenderer>();
                Assert.AreSame(StormInkWeaponArt.Get(WeaponId.Sword, 2), sword.sprite);
                Assert.Less(sword.sortingOrder, actor.Find("Authored character").GetComponent<SpriteRenderer>().sortingOrder);
                Show(MobTypeId.Bailiff, ActorStatus.Alive);
                Assert.IsFalse(chain.gameObject.activeSelf, "A reused actor cannot retain the Warden chain");
            }
            finally { Object.DestroyImmediate(root); }
        }
    }
}
