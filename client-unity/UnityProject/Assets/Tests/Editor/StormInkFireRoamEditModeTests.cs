using System.Collections.Generic;
using NUnit.Framework;
using Roguehalla.Net;
using Roguehalla.Net.Protocol;
using UnityEngine;

namespace Roguehalla.Client.Tests
{
    public sealed class StormInkFireRoamEditModeTests
    {
        [Test]
        public void TheOwnerOnlyShowsDeliveredPartialChargeAgainstTheWelcomeMaximum()
        {
            var me = new SelfView { Faction = FactionId.Fire, Status = ActorStatus.Alive };
            var panel = new WireBuildSummary { Panel = new[] { new WirePanelValue { Code = 8, Value = 3000000 } } };
            var hud = new WireHudContent
            {
                Terms = new[] { new WireHudTerm { Code = (byte)HudTerm.FireRoamChargeMax, Value = 600d } },
            };
            Assert.That(LegacyFacts.Of(me, null, panel, hud).RoamCharge, Is.EqualTo(.5f));
            Assert.That(LegacyFacts.Of(me, null, panel).RoamCharge, Is.Zero);
            Assert.That(LegacyFacts.Of(me, null, null, hud).RoamCharge, Is.Zero);
            var remote = new InterpActor { Faction = FactionId.Fire, Kind = ActorKind.Player, Status = ActorStatus.Alive };
            Assert.That(LegacyFacts.Of(remote, null).RoamCharge, Is.Zero);
            Assert.That(LegacyFacts.Of(remote, null).Roam, Is.False);
            remote.Flags = WireIndicators.FlagRoamReady;
            Assert.That(LegacyFacts.Of(remote, null).Roam, Is.True);
            panel.Panel[0].Value = 6000000;
            Assert.That(LegacyFacts.Of(me, null, panel, hud).Roam, Is.False);
            Assert.That(LegacyFacts.Of(me, null, panel, hud).RoamCharge, Is.Zero, "a retained full panel cannot re-arm the glow");
            me.Flags = WireIndicators.FlagRoamReady;
            Assert.That(LegacyFacts.Of(me, null).Roam, Is.True, "signed readiness works without a panel");
        }

        [Test]
        public void ReducedMotionKeepsTheMarkStaticAndPartialStrengthLocal()
        {
            var actor = new GameObject("Fire actor");
            try
            {
                var marks = new OverheadIndicators(actor.transform);
                var facts = new OverheadIndicators.Facts { HeadY = 1.5f, Reduced = true };
                var wash = actor.transform.Find("Fire Roam wash").GetComponent<SpriteRenderer>();
                var ember = actor.transform.Find("Fire Roam ember").GetComponent<SpriteRenderer>();
                facts.M.RoamCharge = .5f;
                marks.Update(facts);
                Assert.That(wash.enabled || ember.enabled, Is.False, "only the owner may display partial charge");
                facts.Local = true;
                marks.Update(facts);
                Assert.That(wash.enabled && ember.enabled, Is.True);
                float washAlpha = wash.color.a;
                float emberAlpha = ember.color.a;
                marks.Update(facts);
                Assert.That(wash.color.a, Is.EqualTo(washAlpha));
                Assert.That(ember.color.a, Is.EqualTo(emberAlpha));
                facts.M.Roam = true;
                marks.Update(facts);
                Assert.That(wash.color.a, Is.GreaterThan(washAlpha));
                Assert.That(ember.color.a, Is.GreaterThan(emberAlpha));
            }
            finally { Object.DestroyImmediate(actor); }
        }

        [Test]
        public void DepartureClearsReadinessImmediatelyWhileTheBodyCanFade()
        {
            var root = new GameObject("world");
            try
            {
                var presenter = new SpriteActorPresenter(root.transform, null) { ExitFadesAllowed = true };
                presenter.Begin();
                presenter.Present(1, 640, 640, 0f, 100f, FactionId.Fire, ActorStatus.Alive,
                    WeaponId.Fists, ActorKind.Player, null, 0, 0, false);
                presenter.PresentMarks(1, LegacyFacts.Of(new InterpActor
                {
                    Kind = ActorKind.Player, Faction = FactionId.Fire, Status = ActorStatus.Alive,
                    Flags = WireIndicators.FlagRoamReady,
                }, null));
                presenter.PresentIndicators(1, 0, false, WeaponId.Fists, false);
                presenter.End();
                presenter.CancelFades();

                var actor = root.transform.Find("Observed actor");
                var body = actor.Find("Authored character").GetComponent<SpriteRenderer>();
                var wash = actor.Find("Fire Roam wash").GetComponent<SpriteRenderer>();
                Assert.That(wash.enabled, Is.True);
                Assert.That(wash.sortingLayerID, Is.EqualTo(body.sortingLayerID));
                Assert.That(wash.sortingOrder, Is.LessThan(body.sortingOrder));

                presenter.Retain(new HashSet<uint>());
                Assert.That(presenter.Contains(1), Is.False);
                Assert.That(presenter.GhostCount, Is.EqualTo(1));
                Assert.That(body.gameObject.activeInHierarchy, Is.True);
                Assert.That(wash.enabled, Is.False);
            }
            finally { Object.DestroyImmediate(root); }
        }

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
