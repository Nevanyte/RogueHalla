using NUnit.Framework;
using Roguehalla.Net;
using Roguehalla.Net.Protocol;
using UnityEngine;

namespace Roguehalla.Client.Tests
{
    public sealed class StormInkChurchCastTests
    {
        [TestCase(ActorKind.Player, -1, "hero", "SentencedChampion")]
        [TestCase(ActorKind.RegionalMob, (int)MobTypeId.Executioner, "mob-goblin-elite", "Executioner")]
        public void PublicIdentitySelectsPaintedBodyAndConfirmedSwing(ActorKind kind, int type, string archetype, string sheet)
        {
            var root = new GameObject("Church cast");
            try
            {
                var catalog = Resources.Load<ShowcaseCatalog>("Showcase/Catalog");
                var presenter = new SpriteActorPresenter(root.transform, catalog);
                void Present(float facing)
                {
                    presenter.Begin();
                    presenter.Present(7, 160, 160, facing, 100, FactionId.Sentenced, ActorStatus.Alive,
                        WeaponId.Sword, kind, type < 0 ? (MobTypeId?)null : (MobTypeId)type, 0, 0, false, archetypeKey: archetype);
                    presenter.End();
                }
                Present(0);
                var body = root.transform.Find("Observed actor/Authored character").GetComponent<SpriteRenderer>();
                Assert.AreEqual(sheet, body.sprite.texture.name);
                var east = body.sprite;
                Present(-Mathf.PI / 2);
                Assert.AreNotSame(east, body.sprite, "the painted fighter turns away from the viewer");
                var north = body.sprite;
                presenter.ConfirmedCue(7, FxKind.Swing);
                Present(-Mathf.PI / 2);
                Assert.AreNotSame(north, body.sprite, "the swing starts on its anticipation drawing");
            }
            finally { Object.DestroyImmediate(root); }
        }
    }
}
