using System;
using NUnit.Framework;
using Roguehalla.Net;
using Roguehalla.Net.Ui;

namespace Roguehalla.Net.Tests
{
    /// <summary>
    /// P-UI-WISH-TILE-1: the pure half of the Wish tile. What the tile shows (AbilityFaces.Wish) follows the server's own test for a
    /// cast: refused only while now is before ReadyAt (crates/rh-sim/src/systems/abilities/mod.rs:117), and the label it carries
    /// (AbilityFaces.WishLabel).
    /// </summary>
    public sealed class WishFaceTests
    {
        const float Eps = .001f;
        const double Now = 5000d;

        static WishView Wish(byte charges, byte max, double readyAt, double recharge = 10000d, WishCastView? cast = null) =>
            new WishView { Ability = AbilityId.Blaze, Charges = charges, MaxCharges = max, ReadyAt = readyAt, RechargeMs = recharge, Cast = cast };

        [Test]
        public void NoWishIsEmptyAndReadsADash()
        {
            Assert.That(AbilityFaces.Wish(null, Now), Is.EqualTo(AbilityFace.Empty));
            Assert.That(AbilityFaces.NoWishLabel, Is.EqualTo("—"));
            Assert.That(AbilityFaces.WishLabel(null, "Blaze"), Is.EqualTo("—"));
            Assert.That(AbilityFaces.WishLabel(Wish(1, 1, 0), "Blaze"), Is.EqualTo("Blaze"));
            Assert.That(AbilityFaces.WishLabel(Wish(1, 1, 0), ""), Is.EqualTo("—"));
            Assert.That(AbilityFaces.WishLabel(Wish(1, 1, 0), null), Is.EqualTo("—"));
        }

        [Test]
        public void AWishTheServerWouldAcceptReadsReady()
        {
            // 1 of 2 charges with the second recharging: the server accepts it (ready_at is behind us), so Ready with its badge.
            var twoStock = new WishView { Ability = AbilityId.Blaze, Charges = 1, MaxCharges = 2, ReadyAt = 4000, FullAt = 14000,
                NextChargeAt = 14000, RechargeMs = 10000 };
            var face = AbilityFaces.Wish(twoStock, Now);
            Assert.That(face.Phase, Is.EqualTo(AbilityPhase.Ready), "a charge in hand and the next recharging");
            Assert.That(face.Seconds, Is.EqualTo(0), "no countdown for the charge still recharging");
            Assert.That(face.Sweep, Is.EqualTo(0f));
            Assert.That(face.Badge, Is.EqualTo(1));

            face = AbilityFaces.Wish(Wish(1, 1, 0), Now);
            Assert.That(face.Phase, Is.EqualTo(AbilityPhase.Ready), "1 of 1 at rest");
            Assert.That(face.Badge, Is.EqualTo(-1), "one charge shows no badge");

            face = AbilityFaces.Wish(Wish(0, 2, 4000), Now);
            Assert.That(face.Phase, Is.EqualTo(AbilityPhase.Ready), "0 charges with ReadyAt passed: the server would accept it");
            Assert.That(face.Badge, Is.EqualTo(0), "and the badge says 0");

            foreach (double readyAt in new[] { 5000d, 4000d })
            {
                face = AbilityFaces.Wish(Wish(1, 2, readyAt), Now);
                Assert.That(face.Phase, Is.EqualTo(AbilityPhase.Ready), "ReadyAt " + readyAt + " is not after now");
                Assert.That(face.Cast, Is.EqualTo(-1f), "no cast, no cast bar");
                Assert.That(face.Seconds, Is.EqualTo(0));
            }

            Assert.That(AbilityFaces.Wish(Wish(0, 0, 0), Now).Badge, Is.EqualTo(-1), "max 0");
            Assert.That(AbilityFaces.Wish(Wish(1, 1, 0), Now).Badge, Is.EqualTo(-1), "max 1");
            Assert.That(AbilityFaces.Wish(Wish(3, 3, 0), Now).Badge, Is.EqualTo(3), "max 3 with 3");
            Assert.That(AbilityFaces.Wish(Wish(1, 2, double.NaN), Now).Phase, Is.EqualTo(AbilityPhase.Ready), "a NaN ReadyAt counts 0");
        }

        [Test]
        public void AWishTheServerWouldRefuseReadsCoolingAndShrinksTheWedge()
        {
            var face = AbilityFaces.Wish(Wish(0, 1, 9000), Now);
            Assert.That(face.Phase, Is.EqualTo(AbilityPhase.Cooling), "0 of 1 recharging");
            Assert.That(face.Seconds, Is.EqualTo(4));
            Assert.That(face.Sweep, Is.EqualTo(.4f).Within(Eps));
            Assert.That(face.Badge, Is.EqualTo(-1));

            face = AbilityFaces.Wish(Wish(0, 2, 9000), Now);
            Assert.That(face.Phase, Is.EqualTo(AbilityPhase.Cooling), "0 of 2 recharging");
            Assert.That(face.Badge, Is.EqualTo(0));

            face = AbilityFaces.Wish(Wish(1, 2, 9000), Now);
            Assert.That(face.Phase, Is.EqualTo(AbilityPhase.Cooling), "a charge in hand but a lock the server refuses");
            Assert.That(face.Seconds, Is.EqualTo(4));
            Assert.That(face.Badge, Is.EqualTo(1));

            Assert.That(AbilityFaces.Wish(Wish(0, 1, 5001), Now).Seconds, Is.EqualTo(1), "5001");
            Assert.That(AbilityFaces.Wish(Wish(0, 1, 6000), Now).Seconds, Is.EqualTo(1), "6000");
            Assert.That(AbilityFaces.Wish(Wish(0, 1, 6001), Now).Seconds, Is.EqualTo(2), "6001");
            Assert.That(AbilityFaces.Wish(Wish(0, 1, 25000), Now).Sweep, Is.EqualTo(1f), "the wedge clamps at 1");

            face = AbilityFaces.Wish(Wish(0, 1, 9000), double.NaN);
            Assert.That(face.Phase, Is.EqualTo(AbilityPhase.Cooling), "a NaN now counts 0");
            Assert.That(face.Seconds, Is.EqualTo(9));

            face = AbilityFaces.Wish(Wish(0, 1, 1e300), Now);
            Assert.That(face.Phase, Is.EqualTo(AbilityPhase.Cooling));
            Assert.That(face.Seconds, Is.EqualTo(int.MaxValue), "a huge ReadyAt caps at int.MaxValue");

            // The same Phase, Seconds and Sweep as a quest spell with the same numbers.
            foreach (byte charges in new byte[] { 0, 1, 2 })
                foreach (double readyAt in new[] { 0d, 4000d, 5000d, 5001d, 5999d, 6000d, 6001d, 9000d, 25000d })
                    foreach (double now in new[] { 0d, 1000d, 5000d, 5500d })
                    {
                        var wish = AbilityFaces.Wish(Wish(charges, 2, readyAt), now);
                        var spell = AbilityFaces.Spell(true, charges, readyAt, now, 10000d);
                        string at = "charges " + charges + " readyAt " + readyAt + " now " + now;
                        Assert.That(wish.Phase, Is.EqualTo(spell.Phase), "phase, " + at);
                        Assert.That(wish.Seconds, Is.EqualTo(spell.Seconds), "seconds, " + at);
                        Assert.That(wish.Sweep, Is.EqualTo(spell.Sweep).Within(1e-6f), "sweep, " + at);
                    }
        }

        [Test]
        public void TheCastBarFollowsTheCastWindowInAnyPhase()
        {
            var cast = new WishCastView { Ability = AbilityId.Blaze, StartedAt = 4500, LandsAt = 6500 };
            Assert.That(AbilityFaces.Wish(Wish(1, 2, 4000, 10000, cast), 5000).Cast, Is.EqualTo(.25f).Within(Eps), "a quarter in");
            Assert.That(AbilityFaces.Wish(Wish(1, 2, 4000, 10000, cast), 4500).Cast, Is.EqualTo(0f).Within(Eps), "at the start");
            Assert.That(AbilityFaces.Wish(Wish(1, 2, 4000, 10000, cast), 6500).Cast, Is.EqualTo(1f).Within(Eps), "at the landing");
            Assert.That(AbilityFaces.Wish(Wish(1, 2, 4000, 10000, cast), 4000).Cast, Is.EqualTo(0f).Within(Eps), "before the start clamps to 0");
            Assert.That(AbilityFaces.Wish(Wish(1, 2, 4000, 10000, cast), 9000).Cast, Is.EqualTo(1f).Within(Eps), "after the landing clamps to 1");

            var instant = new WishCastView { Ability = AbilityId.Blaze, StartedAt = 5000, LandsAt = 5000 };
            Assert.That(AbilityFaces.Wish(Wish(1, 2, 4000, 10000, instant), 5000).Cast, Is.EqualTo(0f).Within(Eps), "an empty window at its start reads 0");
            Assert.That(AbilityFaces.Wish(Wish(1, 2, 4000, 10000, instant), 5001).Cast, Is.EqualTo(1f).Within(Eps), "and 1 a millisecond later");

            var cooling = AbilityFaces.Wish(Wish(0, 2, 9000, 10000, cast), 5000);
            Assert.That(cooling.Phase, Is.EqualTo(AbilityPhase.Cooling));
            Assert.That(cooling.Cast, Is.EqualTo(.25f).Within(Eps), "a Cooling face carries the cast");
            var ready = AbilityFaces.Wish(Wish(1, 2, 4000, 10000, cast), 5000);
            Assert.That(ready.Phase, Is.EqualTo(AbilityPhase.Ready));
            Assert.That(ready.Cast, Is.EqualTo(.25f).Within(Eps), "and a Ready face too");
            Assert.That(AbilityFaces.Wish(Wish(1, 2, 4000), 5000).Cast, Is.EqualTo(-1f), "no cast gives -1");

            var nan = new WishCastView { Ability = AbilityId.Blaze, StartedAt = double.NaN, LandsAt = double.NaN };
            Assert.DoesNotThrow(() => AbilityFaces.Wish(Wish(1, 2, 4000, 10000, nan), 5000), "NaN cast times");
            var inf = new WishCastView { Ability = AbilityId.Blaze, StartedAt = double.NegativeInfinity, LandsAt = double.PositiveInfinity };
            Assert.DoesNotThrow(() => AbilityFaces.Wish(Wish(1, 2, 4000, 10000, inf), 5000), "infinite cast times");
        }

        [Test]
        public void AnUnknownRechargeDrawsAFullWedgeAndKeepsTheNumber()
        {
            foreach (double recharge in new[] { 0d, -5d, double.NaN, double.PositiveInfinity })
            {
                var face = AbilityFaces.Wish(Wish(0, 1, 9000, recharge), Now);
                Assert.That(face.Phase, Is.EqualTo(AbilityPhase.Cooling), "RechargeMs " + recharge);
                Assert.That(face.Sweep, Is.EqualTo(1f), "a full wedge, RechargeMs " + recharge);
                Assert.That(face.Seconds, Is.EqualTo(4), "the number stays, RechargeMs " + recharge);
            }
        }
    }
}
