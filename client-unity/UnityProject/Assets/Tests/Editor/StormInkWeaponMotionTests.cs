using System.Collections.Generic;
using NUnit.Framework;
using Roguehalla.Net;
using Roguehalla.Net.Protocol;
using UnityEngine;

namespace Roguehalla.Client.Tests
{
    public sealed class StormInkWeaponMotionTests
    {
        [Test]
        public void EveryChampionHasAFactionAndWeaponSpecificConfirmedStrikeAndCancellation()
        {
            var root = new GameObject("weapon motion oracle");
            try
            {
                var presenter = new SpriteActorPresenter(root.transform, Resources.Load<ShowcaseCatalog>("Showcase/Catalog"));
                var signatures = new HashSet<(Vector3, Quaternion)>();
                for (byte i = 0; i < 5; i++)
                for (byte j = 0; j < 5; j++)
                {
                    var faction = (FactionId)i;
                    var weapon = (WeaponId)j;
                    void Present() => presenter.Present(1, 0, 0, Mathf.PI / 2f, 100, faction, ActorStatus.Alive, weapon,
                        ActorKind.Player, null, 0, 0, true);
                    Present();
                    var art = root.transform.Find("Observed actor/Equipped weapon cue");
                    var rest = art.localPosition;
                    var restRotation = art.localRotation;
                    presenter.ConfirmedCue(1, FxKind.Swing);
                    Present();
                    Assert.That((art.localPosition, art.localRotation), Is.Not.EqualTo((rest, restRotation)), faction + " " + weapon);
                    Assert.That(signatures.Add((art.localPosition, art.localRotation)), Is.True, faction + " " + weapon);
                    presenter.ConfirmedCue(1, FxKind.Cancel);
                    Present();
                    Assert.That((art.localPosition, art.localRotation), Is.EqualTo((rest, restRotation)), faction + " " + weapon);
                    var sprite = art.GetComponent<SpriteRenderer>();
                    Assert.That(sprite.sprite, Is.SameAs(ShowcaseSprites.Get("weapon" + j)));
                    Assert.That(sprite.color, Is.EqualTo(Color.white));
                }
            }
            finally { Object.DestroyImmediate(root); }
        }
    }
}
