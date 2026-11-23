using System;
using NUnit.Framework;
using Roguehalla.Client;
using Roguehalla.Net;
using Roguehalla.Net.Ui;

namespace Roguehalla.Net.Tests
{
    /// <summary>
    /// P-UI-CHARGE-TILES-1: a quest spell that holds a charge reads Ready, because the server refuses a quest spell for its cooldown
    /// only at 0 charges (crates/rh-sim/src/quests/board/rewards/casts.rs:64-65) and its ready_at is the NEXT charge's arrival. The
    /// pure half: AbilityFaces.Spell, the CooldownSpan cycle of Lantern Ward, the one difference from the Wish face, and the Wish tile
    /// motion that must not pulse for a second charge.
    /// </summary>
    public sealed class ChargeFaceTests
    {
        const float Eps = .001f;

        [Test]
        public void AChargeInHandReadsReadyWhileTheNextOneRecharges()
        {
            var face = AbilityFaces.Spell(true, 1, 5200, 1000, 10000);
            Assert.That(face.Phase, Is.EqualTo(AbilityPhase.Ready), "one charge with the next 4.2 s away");
            Assert.That(face.Sweep, Is.EqualTo(0f), "no wedge");
            Assert.That(face.Seconds, Is.EqualTo(0), "no number");
            Assert.That(face.Badge, Is.EqualTo(1), "the badge is the count");
            Assert.That(face.Cast, Is.EqualTo(-1f), "no cast bar");

            face = AbilityFaces.Spell(true, 2, 305000, 5000, 300000);
            Assert.That(face.Phase, Is.EqualTo(AbilityPhase.Ready), "two charges, the next far away");
            Assert.That(face.Badge, Is.EqualTo(2));
            Assert.That(face.Sweep, Is.EqualTo(0f));
            Assert.That(face.Seconds, Is.EqualTo(0));

            face = AbilityFaces.Spell(true, 2, 0, 1000, 60000);
            Assert.That(face.Phase, Is.EqualTo(AbilityPhase.Ready), "full stock, nothing recharging");
            Assert.That(face.Badge, Is.EqualTo(2));

            Assert.That(AbilityFaces.Spell(true, 1, 5000, double.NaN, 10000).Phase, Is.EqualTo(AbilityPhase.Ready),
                "a NaN now counts 0, so 5 s are left, but a charge is in hand");
        }

        // Standing guard: green on the base, guards the other half of the rule.
        [Test]
        public void NoChargeStillCools()
        {
            var face = AbilityFaces.Spell(true, 0, 5200, 1000, 10000);
            Assert.That(face.Phase, Is.EqualTo(AbilityPhase.Cooling));
            Assert.That(face.Seconds, Is.EqualTo(5));
            Assert.That(face.Sweep, Is.EqualTo(.42f).Within(Eps));
            Assert.That(face.Badge, Is.EqualTo(0));
            Assert.That(face.Cast, Is.EqualTo(-1f));

            Assert.That(AbilityFaces.Spell(true, 0, 5200, 1000, 0).Sweep, Is.EqualTo(1f).Within(Eps), "total 0 draws a full wedge");
            face = AbilityFaces.Spell(true, -1, 5200, 1000, 10000);
            Assert.That(face.Phase, Is.EqualTo(AbilityPhase.Cooling), "a negative count is none");
            Assert.That(face.Seconds, Is.EqualTo(5));
            Assert.That(face.Badge, Is.EqualTo(-1), "and the badge is the count as given");
        }

        // Standing guard: green on the base. A maximum of one charge behaves as before in every reachable state.
        [Test]
        public void AMaximumOfOneChargeReadsAsBefore()
        {
            var face = AbilityFaces.Spell(true, 1, 0, 1000, 0);
            Assert.That(face.Phase, Is.EqualTo(AbilityPhase.Ready), "(1, past)");
            Assert.That(face.Badge, Is.EqualTo(1));
            Assert.That(face.Seconds, Is.EqualTo(0));

            face = AbilityFaces.Spell(true, 0, 61000, 1000, 60000);
            Assert.That(face.Phase, Is.EqualTo(AbilityPhase.Cooling), "(0, ahead)");
            Assert.That(face.Seconds, Is.EqualTo(60));
            Assert.That(face.Sweep, Is.EqualTo(1f).Within(Eps));
            Assert.That(face.Badge, Is.EqualTo(0));

            face = AbilityFaces.Spell(true, 0, 900, 1000, 60000);
            Assert.That(face.Phase, Is.EqualTo(AbilityPhase.Ready), "(0, past): the instant before the tick banks the charge");
            Assert.That(face.Badge, Is.EqualTo(0));
            Assert.That(face.Seconds, Is.EqualTo(0));
        }

        [Test]
        public void ALanternWardRunsThroughItsRealStates()
        {
            var span = new CooldownSpan();
            double total;

            total = span.Total(0, 1000);
            var face = AbilityFaces.Spell(true, 2, 0, 1000, total);
            Assert.That(face.Phase, Is.EqualTo(AbilityPhase.Ready), "step 1: full stock");
            Assert.That(face.Badge, Is.EqualTo(2), "step 1");

            total = span.Total(62000, 2000);
            Assert.That(total, Is.EqualTo(60000d).Within(Eps), "step 2: the span is fed and starts at one recharge");
            face = AbilityFaces.Spell(true, 1, 62000, 2000, total);
            Assert.That(face.Phase, Is.EqualTo(AbilityPhase.Ready), "step 2: first cast, one charge left");
            Assert.That(face.Badge, Is.EqualTo(1), "step 2");
            Assert.That(face.Seconds, Is.EqualTo(0), "step 2: no number");
            Assert.That(face.Sweep, Is.EqualTo(0f), "step 2: no wedge");

            total = span.Total(62000, 5000);
            Assert.That(total, Is.EqualTo(60000d).Within(Eps), "step 3: the span did not restart");
            face = AbilityFaces.Spell(true, 0, 62000, 5000, total);
            Assert.That(face.Phase, Is.EqualTo(AbilityPhase.Cooling), "step 3: second cast, none left");
            Assert.That(face.Seconds, Is.EqualTo(57), "step 3");
            Assert.That(face.Sweep, Is.EqualTo(.95f).Within(Eps), "step 3: the wedge starts at its true share, not full");
            Assert.That(face.Badge, Is.EqualTo(0), "step 3");

            total = span.Total(122000, 62000);
            face = AbilityFaces.Spell(true, 1, 122000, 62000, total);
            Assert.That(face.Phase, Is.EqualTo(AbilityPhase.Ready), "step 4: a charge came back");
            Assert.That(face.Badge, Is.EqualTo(1), "step 4");
            Assert.That(face.Seconds, Is.EqualTo(0), "step 4");
            Assert.That(face.Sweep, Is.EqualTo(0f), "step 4");

            total = span.Total(122000, 122000);
            Assert.That(total, Is.EqualTo(0d), "step 5: nothing recharging, the span is forgotten");
            face = AbilityFaces.Spell(true, 2, 122000, 122000, total);
            Assert.That(face.Phase, Is.EqualTo(AbilityPhase.Ready), "step 5: full again");
            Assert.That(face.Badge, Is.EqualTo(2), "step 5");
        }

        [Test]
        public void TheWishAndASpellDifferOnlyByHoldingACharge()
        {
            int differing = 0;
            foreach (byte charges in new byte[] { 0, 1, 2 })
                foreach (double readyAt in new[] { 0d, 4000d, 5000d, 5001d, 5999d, 6000d, 6001d, 9000d, 25000d })
                    foreach (double now in new[] { 0d, 1000d, 5000d, 5500d })
                    {
                        var wish = AbilityFaces.Wish(new WishView { Ability = AbilityId.Blaze, Charges = charges, MaxCharges = 2, ReadyAt = readyAt, RechargeMs = 10000d }, now);
                        var spell = AbilityFaces.Spell(true, charges, readyAt, now, 10000d);
                        string at = "charges " + charges + " readyAt " + readyAt + " now " + now;
                        if (charges == 0)
                        {
                            Assert.That(spell.Phase, Is.EqualTo(wish.Phase), "phase, " + at);
                            Assert.That(spell.Seconds, Is.EqualTo(wish.Seconds), "seconds, " + at);
                            Assert.That(spell.Sweep, Is.EqualTo(wish.Sweep).Within(1e-6f), "sweep, " + at);
                        }
                        else if (readyAt - now > 0d)
                        {
                            differing++;
                            Assert.That(wish.Phase, Is.EqualTo(AbilityPhase.Cooling), "the Wish's ReadyAt is a lock the server refuses, " + at);
                            Assert.That(spell.Phase, Is.EqualTo(AbilityPhase.Ready), "a spell with a charge in hand casts, " + at);
                            Assert.That(spell.Badge, Is.EqualTo(charges), "badge, " + at);
                        }
                        else
                        {
                            Assert.That(spell.Phase, Is.EqualTo(wish.Phase), "no time left: both Ready, " + at);
                        }
                    }
            Assert.That(differing, Is.GreaterThanOrEqualTo(20), "set-up: the grid covers the charge-with-time-left cases");
        }

        // Standing guard: green on the base; nothing else pins one charge to two.
        [Test]
        public void AWishGainingASecondChargeDoesNotPulse()
        {
            var motion = new WishTileMotion();
            motion.Observe(Wish(40000, 1, 2), 60000, 10f);
            motion.Observe(Wish(40000, 2, 2), 60050, 10.1f);
            Assert.That(motion.Flash(10.1f), Is.EqualTo(0f), "a second charge is not an accepted cast");
            Assert.That(motion.ReadyPulse(10.1f + WishTileMotion.ReadyPulseSeconds / 2), Is.EqualTo(0f), "and the tile was ready already");
        }

        static WishView Wish(double readyAt, byte charges, byte max) =>
            new WishView { Ability = AbilityId.Freeze, ReadyAt = readyAt, Charges = charges, MaxCharges = max, RechargeMs = 45000 };
    }
}
