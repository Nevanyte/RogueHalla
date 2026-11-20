using NUnit.Framework;
using UnityEngine;

namespace Roguehalla.Client.Tests
{
    public sealed class StormInkFireRoamEditModeTests
    {
        [Test]
        public void SignedReadyUsesPaintedPigmentBehindTheBodyAndClearsOnSpendOrDeath()
        {
            var actor = new GameObject("Fire actor");
            try
            {
                var marks = new OverheadIndicators(actor.transform);
                var facts = new OverheadIndicators.Facts { Order = 2000, HeadY = 1.5f, Reduced = true };
                facts.M.Roam = true;
                marks.Update(facts);
                var wash = actor.transform.Find("Fire Roam wash")?.GetComponent<SpriteRenderer>();
                var ember = actor.transform.Find("Fire Roam ember")?.GetComponent<SpriteRenderer>();
                Assert.That(wash, Is.Not.Null, "readiness uses a painted wash");
                Assert.That(ember, Is.Not.Null);
                Assert.That(wash.sprite, Is.SameAs(WishFxArt.Get(WishFxArt.FireWash)));
                Assert.That(ember.sprite, Is.SameAs(WishFxArt.Get(WishFxArt.FireFleck)));
                Assert.That(actor.transform.Find("Roam ready ring outer"), Is.Null);
                foreach (var r in new[] { wash, ember })
                {
                    Assert.That(r.enabled, Is.True);
                    Assert.That(r.sortingOrder, Is.LessThan(facts.Order));
                    Assert.That(r.transform.localPosition.y, Is.InRange(0f, facts.HeadY * .5f));
                }
                facts.Dead = true;
                marks.Update(facts);
                Assert.That(wash.enabled || ember.enabled, Is.False);
                facts.Dead = false;
                facts.M.Roam = false;
                marks.Update(facts);
                Assert.That(wash.enabled || ember.enabled, Is.False);
            }
            finally { Object.DestroyImmediate(actor); }
        }
    }
}
