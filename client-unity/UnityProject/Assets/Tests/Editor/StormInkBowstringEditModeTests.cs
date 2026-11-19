using NUnit.Framework;
using Roguehalla.Net;
using Roguehalla.Net.Protocol;
using UnityEngine;

namespace Roguehalla.Client.Tests
{
    public sealed class StormInkBowstringEditModeTests
    {
        [Test]
        public void PaintedRightPalmPullsTheCanonicalBowAndReleaseRestoresItsRestMaterial()
        {
            var root = new GameObject("bowstring oracle");
            try
            {
                var presenter = new SpriteActorPresenter(root.transform, Resources.Load<ShowcaseCatalog>("Showcase/Catalog"));
                for (int row = 0; row < 8; row++)
                {
                    float facing = (2 - row) * Mathf.PI / 4f;
                    void Present(uint drawStart = 0) => presenter.Present(1, 0, 0, facing, 100, FactionId.Sentenced,
                        ActorStatus.Alive, WeaponId.Bow, ActorKind.Player, null, 0, 0, true,
                        drawStartedAt: drawStart, sampleTickMs: 980);
                    Present();
                    var actor = root.transform.Find("Observed actor");
                    var bow = actor.Find("Equipped weapon cue").GetComponent<SpriteRenderer>();
                    var body = actor.Find("Authored character").GetComponent<SpriteRenderer>();
                    var canonical = StormInkWeaponArt.Get(WeaponId.Bow, row);
                    Assert.That(bow.sprite, Is.SameAs(canonical));
                    Assert.That(bow.sharedMaterial, Is.SameAs(ShowcaseSprites.Unlit));
                    Vector3 scale = bow.transform.localScale;

                    Present(800);
                    var upper = actor.Find("Bowstring upper");
                    var lower = actor.Find("Bowstring lower");
                    Assert.That(upper, Is.Not.Null);
                    Assert.That(lower, Is.Not.Null);
                    Assert.That(upper.GetComponent<SpriteRenderer>().enabled && lower.GetComponent<SpriteRenderer>().enabled, Is.True);
                    Assert.That(bow.sprite, Is.SameAs(canonical));
                    Assert.That(bow.transform.localScale, Is.EqualTo(scale));
                    Assert.That(StormInkActors.TryHand(FactionId.Sentenced, body.sprite, false, out var hand), Is.True);
                    var localHand = Vector2.Scale(hand, body.sprite.rect.size) - body.sprite.pivot;
                    var rightPalm = body.transform.TransformPoint(localHand / body.sprite.pixelsPerUnit);
                    Assert.That(Vector3.Distance(upper.TransformPoint(Vector3.right * .5f), rightPalm), Is.LessThan(.0001f));
                    Assert.That(Vector3.Distance(lower.TransformPoint(Vector3.right * .5f), rightPalm), Is.LessThan(.0001f));
                    Assert.That(actor.Find("Supporting hand on weapon").GetComponent<SpriteRenderer>().enabled, Is.False);

                    presenter.ConfirmedCue(1, FxKind.Swing, WeaponId.Bow, facing);
                    Present();
                    Assert.That(bow.sprite, Is.SameAs(canonical));
                    Assert.That(bow.sharedMaterial, Is.SameAs(ShowcaseSprites.Unlit));
                    Assert.That(upper.GetComponent<SpriteRenderer>().enabled || lower.GetComponent<SpriteRenderer>().enabled, Is.False);
                    presenter.ConfirmedCue(1, FxKind.Cancel);
                }
            }
            finally { Object.DestroyImmediate(root); }
        }
    }
}
