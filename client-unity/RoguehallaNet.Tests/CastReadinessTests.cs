using System;
using NUnit.Framework;
using Roguehalla.Net;
using Roguehalla.Net.Ui;

namespace Roguehalla.Net.Tests
{
    /// <summary>
    /// P-UI-ABILITY-FEEDBACK-1: the pure half of a cast key's refusal line. <see cref="CastReadiness"/> says from delivered facts why a
    /// cast key does nothing now, in legacy's words (abilitySlot.ts castDenial :140-160, formatCooldown :128), by the server's rule for
    /// ready (crates/rh-sim/src/systems/abilities/mod.rs :96-127). Time is the nowMs argument; nothing here reads a clock.
    /// </summary>
    public sealed class CastReadinessTests
    {
        const double Now = 5000d;
        const ushort Stun = 1, Root = 2, Silence = 4, Slow = 8, CastingBit = 4096;

        static WishView Wish(byte charges, byte max, double readyAt, double recharge = 10000d, WishCastView? cast = null) =>
            new WishView { Ability = AbilityId.Blaze, Charges = charges, MaxCharges = max, ReadyAt = readyAt, RechargeMs = recharge, Cast = cast };

        static WishCastView Cast(double started, double lands) => new WishCastView { Ability = AbilityId.Blaze, StartedAt = started, LandsAt = lands };

        static AbilityFace Ready => new AbilityFace(AbilityPhase.Ready, 0f, 0, -1, -1f);
        static AbilityFace Cooling(int seconds) => new AbilityFace(AbilityPhase.Cooling, 1f, seconds, -1, -1f);
        static AbilityFace CastingFace => new AbilityFace(AbilityPhase.Ready, 0f, 0, -1, .5f);

        static void Says(CastReading r, string line, string what)
        {
            Assert.That(r.Empty, Is.False, what + ": the slot is not empty");
            Assert.That(r.Line, Is.EqualTo(line), what);
            Assert.That(r.Pops, Is.False, what + ": no pop");
        }

        static void Goes(CastReading r, string what)
        {
            Assert.That(r.Empty, Is.False, what + ": the slot is not empty");
            Assert.That(r.Line, Is.Null, what + ": no line");
            Assert.That(r.Pops, Is.True, what + ": it pops");
        }

        [Test]
        public void SilentForAnEmptyOrReadySlot()
        {
            var none = CastReadiness.Wish(null, Now, 0);
            Assert.That(none.Empty, Is.True, "no Wish: empty");
            Assert.That(none.Line, Is.Null, "no Wish: no line");
            Assert.That(none.Pops, Is.False, "no Wish: no pop");
            var noSpell = CastReadiness.Spell(false, 1, 9000d, Now, 0);
            Assert.That(noSpell.Empty, Is.True, "no spell: empty");
            Assert.That(noSpell.Line, Is.Null, "no spell: no line");
            Assert.That(noSpell.Pops, Is.False, "no spell: no pop");

            foreach (double readyAt in new[] { 0d, 4000d, Now })
            {
                Goes(CastReadiness.Wish(Wish(1, 1, readyAt), Now, 0), "Wish ready at " + readyAt);
                Goes(CastReadiness.Spell(true, 1, readyAt, Now, 0), "spell ready at " + readyAt);
                Goes(CastReadiness.Spell(true, 0, readyAt == 0d ? 1d : readyAt, Now, 0), "spell with no charge, recharged at " + readyAt);
            }
        }

        [Test]
        public void CooldownFormatsLegacyWay()
        {
            Assert.That(CastReadiness.Cooldown(0d), Is.EqualTo("0"));
            Assert.That(CastReadiness.Cooldown(59d), Is.EqualTo("59"));
            Assert.That(CastReadiness.Cooldown(59.001d), Is.EqualTo("1:00"));
            Assert.That(CastReadiness.Cooldown(60d), Is.EqualTo("1:00"));
            Assert.That(CastReadiness.Cooldown(299d), Is.EqualTo("4:59"));
            Assert.That(CastReadiness.Cooldown(3600d), Is.EqualTo("60:00"));
            Assert.That(CastReadiness.Cooldown(2147483647d), Is.EqualTo("35791394:07"));
            Assert.That(CastReadiness.Cooldown(double.NaN), Is.EqualTo("0"));
            Assert.That(CastReadiness.Cooldown(-5d), Is.EqualTo("0"));
            Assert.That(CastReadiness.Cooldown(double.NegativeInfinity), Is.EqualTo("0"));
            Assert.That(CastReadiness.Cooldown(double.PositiveInfinity), Is.EqualTo("35791394:07"));
            Assert.That(CastReadiness.Cooldown(.5d), Is.EqualTo("1"), "rounded up");
        }

        [Test]
        public void CooldownAgreesWithSlotText()
        {
            foreach (double ms in new[] { 1d, 999d, 1000d, 1001d, 59000d, 59001d, 60000d, 299000d, 300000d, 86400000d, 2147483647000d })
                Assert.That(CastReadiness.Cooldown(ms / 1000d), Is.EqualTo(SlotText.Cooldown(ms)), "for " + ms + " ms");
        }

        [Test]
        public void ACoolingSlotSaysWhen()
        {
            foreach (var c in new[] { (1d, "1"), (59000d, "59"), (59001d, "1:00"), (60000d, "1:00"), (299000d, "4:59"), (300000d, "5:00") })
            {
                Says(CastReadiness.Wish(Wish(0, 1, Now + c.Item1), Now, 0), "Ready in " + c.Item2, "Wish with " + c.Item1 + " ms left");
                Says(CastReadiness.Spell(true, 0, Now + c.Item1, Now, 0), "Ready in " + c.Item2, "spell with " + c.Item1 + " ms left");
            }
            Says(CastReadiness.Wish(Wish(0, 1, 52100d), 50000d, 0), "Ready in 3", "52100 at 50000");

            Goes(CastReadiness.Wish(Wish(1, 1, double.NaN), Now, 0), "a NaN Wish ReadyAt reads ready");
            Goes(CastReadiness.Wish(Wish(1, 1, double.PositiveInfinity), Now, 0), "an infinite Wish ReadyAt reads ready");
            Goes(CastReadiness.Spell(true, 0, double.NaN, Now, 0), "a NaN spell ReadyAt reads ready");
            Goes(CastReadiness.Spell(true, 0, double.PositiveInfinity, Now, 0), "an infinite spell ReadyAt reads ready");
            Says(CastReadiness.Wish(Wish(0, 1, 5000d), double.NaN, 0), "Ready in 5", "a NaN now counts 0 (Wish)");
            Says(CastReadiness.Spell(true, 0, 5000d, double.NaN, 0), "Ready in 5", "a NaN now counts 0 (spell)");
        }

        [Test]
        public void CastingBeatsEverythingButEmpty()
        {
            var cast = Cast(4000d, 6000d);
            Says(CastReadiness.Wish(Wish(0, 1, 9000d, 10000d, cast), Now, 0), "Casting", "a cast on a cooling Wish");
            Says(CastReadiness.Wish(Wish(1, 1, 1000d, 10000d, cast), Now, 0), "Casting", "a cast on a ready Wish");
            Says(CastReadiness.Wish(Wish(1, 1, 1000d, 10000d, cast), Now, Stun), "Casting", "a cast on a stunned Wish");
            Says(CastReadiness.Spell(true, 1, 0d, Now, CastingBit), "Casting", "bit 4096 on a ready spell");
            Says(CastReadiness.Spell(true, 0, 9000d, Now, CastingBit), "Casting", "bit 4096 on a cooling spell");
            Says(CastReadiness.Wish(Wish(1, 1, 0d), Now, CastingBit), "Casting", "bit 4096 on a ready Wish");

            var emptySpell = CastReadiness.Spell(false, 0, 0d, Now, CastingBit);
            Assert.That(emptySpell.Empty, Is.True, "an empty spell slot with the bit stays empty");
            Assert.That(emptySpell.Line, Is.Null, "and has no line");
            var emptyWish = CastReadiness.Wish(null, Now, CastingBit);
            Assert.That(emptyWish.Empty, Is.True, "an empty Wish slot with the bit stays empty");
            Assert.That(emptyWish.Line, Is.Null, "and has no line");
        }

        [Test]
        public void EachBlockerHasItsWord()
        {
            Says(CastReadiness.Judge(Ready, false, Stun, 7), "Stunned", "stun");
            Says(CastReadiness.Judge(Ready, false, Silence, 7), "Silenced", "silence");
            Says(CastReadiness.Judge(Ready, false, Root, 7), "Rooted", "root");
            Says(CastReadiness.Judge(Ready, false, 7, 7), "Stunned", "stun beats silence and root");
            Says(CastReadiness.Judge(Ready, false, Silence | Root, 7), "Silenced", "silence beats root");
            Says(CastReadiness.Judge(Ready, false, Stun | Root, 7), "Stunned", "stun beats root");

            Goes(CastReadiness.Judge(Ready, false, Root, Stun | Silence), "a bit outside the mask is ignored (root)");
            Goes(CastReadiness.Judge(Ready, false, Slow, 0xFF), "a bit that is no blocker is ignored");
            Goes(CastReadiness.Judge(Ready, false, Stun, 0), "an empty mask ignores everything");
            Says(CastReadiness.Judge(Ready, false, Stun | Root, Silence | Root), "Rooted", "only bits in both masks count");

            Says(CastReadiness.Judge(Cooling(3), false, Stun, 7), "Ready in 3", "a cooling face with a blocker says when");
            Says(CastReadiness.Judge(Cooling(75), false, 0, 0), "Ready in 1:15", "a cooling face reads minutes");
            Goes(CastReadiness.Judge(Cooling(3), true, 0, 0), "a cooling face with a charge in hand goes");
            Says(CastReadiness.Judge(Cooling(3), true, Stun, 7), "Stunned", "a charge in hand falls through to the blocker");
            Says(CastReadiness.Judge(CastingFace, false, Stun, 7), "Casting", "a cast bar beats a blocker");
            Says(CastReadiness.Judge(Ready, false, CastingBit, 0), "Casting", "bit 4096 says casting");

            var empty = CastReadiness.Judge(AbilityFace.Empty, false, Stun | CastingBit, 7);
            Assert.That(empty.Empty, Is.True, "an empty face is empty");
            Assert.That(empty.Line, Is.Null, "with no line");
        }

        [Test]
        public void WishFollowsTheServer()
        {
            Says(CastReadiness.Wish(Wish(1, 1, 0d), Now, Stun), "Stunned", "a stun blocks the Wish");
            Says(CastReadiness.Wish(Wish(1, 1, 0d), Now, Silence), "Silenced", "a silence blocks the Wish");
            Says(CastReadiness.Wish(Wish(1, 1, 0d), Now, Stun | Silence), "Stunned", "stun first");
            Goes(CastReadiness.Wish(Wish(1, 1, 0d), Now, Root), "a root does not stop the Wish (CANCELLED_BY.root is false)");
            Goes(CastReadiness.Wish(Wish(1, 1, 0d), Now, Slow), "a slow does not");
            Goes(CastReadiness.Wish(Wish(1, 2, 4000d, 10000d), Now, 0), "1 of 2 charges with ReadyAt behind: not refused");
            Assert.That(CastReadiness.WishBlockers, Is.EqualTo((ushort)5), "the Wish blocks on stun and silence");
        }

        [Test]
        public void ASpellHoldingAChargeIsNotRefused()
        {
            Goes(CastReadiness.Spell(true, 1, 9000d, Now, 0), "Charges 1 with ReadyAt ahead (Lantern Ward)");
            Goes(CastReadiness.Spell(true, 2, 305000d, Now, 0), "Charges 2 with ReadyAt far ahead");
            Says(CastReadiness.Spell(true, 0, 9000d, Now, 0), "Ready in 4", "Charges 0 with ReadyAt ahead: the cooldown");
            Goes(CastReadiness.Spell(true, 0, 4000d, Now, 0), "Charges 0 with ReadyAt behind");
            Goes(CastReadiness.Spell(true, 0, Now, Now, 0), "Charges 0 with ReadyAt equal to now");
            Goes(CastReadiness.Spell(true, 1, 0d, Now, Stun), "a stun on a ready spell gives no line");
            Goes(CastReadiness.Spell(true, 1, 0d, Now, Silence), "a silence on a ready spell gives no line");
            Goes(CastReadiness.Spell(true, 1, 0d, Now, Root), "a root on a ready spell gives no line");
            Goes(CastReadiness.Spell(true, 1, 0d, Now, Stun | Silence | Root), "all three give no line");
        }

        [Test]
        public void TheLineAgreesWithTheFace()
        {
            int cooling = 0, ready = 0, cast = 0;
            foreach (double readyAt in new[] { 0d, 4999d, 5000d, 5001d, 5999d, 6000d, 304000d })
            foreach (byte charges in new byte[] { 0, 1 })
            foreach (bool withCast in new[] { false, true })
            {
                var w = Wish(charges, 1, readyAt, 10000d, withCast ? Cast(4000d, 6000d) : (WishCastView?)null);
                var face = AbilityFaces.Wish(w, Now);
                string what = "Wish ReadyAt " + readyAt + " charges " + charges + (withCast ? " casting" : "");
                var reading = CastReadiness.Wish(w, Now, 0);
                if (face.Cast >= 0f) { Says(reading, "Casting", what); cast++; }
                else if (face.Phase == AbilityPhase.Cooling) { Says(reading, "Ready in " + CastReadiness.Cooldown(face.Seconds), what); cooling++; }
                else { Assert.That(face.Phase, Is.EqualTo(AbilityPhase.Ready), what); Goes(reading, what); ready++; }
            }
            foreach (double readyAt in new[] { 0d, 4999d, 5000d, 5001d, 5999d, 6000d, 304000d })
            foreach (byte charges in new byte[] { 0, 1 })
            {
                var face = AbilityFaces.Spell(true, charges, readyAt, Now, 0d);
                string what = "spell ReadyAt " + readyAt + " charges " + charges;
                var reading = CastReadiness.Spell(true, charges, readyAt, Now, 0);
                if (face.Phase == AbilityPhase.Cooling && charges == 0) { Says(reading, "Ready in " + CastReadiness.Cooldown(face.Seconds), what); cooling++; }
                else { Goes(reading, what); ready++; }
            }
            var none = CastReadiness.Spell(false, 0, 9000d, Now, 0);
            Assert.That(none.Empty, Is.True, "an empty slot is empty");
            Assert.That(none.Line, Is.Null, "an empty slot has no line");
            Assert.That(cooling, Is.GreaterThanOrEqualTo(8), "set-up: the sweep covers cooling faces");
            Assert.That(ready, Is.GreaterThanOrEqualTo(8), "set-up: the sweep covers ready faces");
            Assert.That(cast, Is.GreaterThanOrEqualTo(7), "set-up: the sweep covers cast faces");
        }

        [Test]
        public void ConstantsAndWords()
        {
            Assert.That(CastReadiness.LineKey, Is.EqualTo("cast"));
            Assert.That(CastReadiness.WishBlockers, Is.EqualTo((ushort)5));
            Assert.That(CastReadiness.HoldSeconds, Is.EqualTo(1f));
            Assert.That(CastReadiness.Casting, Is.EqualTo("Casting"));
            Assert.That(CastReadiness.Stunned, Is.EqualTo("Stunned"));
            Assert.That(CastReadiness.Silenced, Is.EqualTo("Silenced"));
            Assert.That(CastReadiness.Rooted, Is.EqualTo("Rooted"));
        }
    }
}
