using NUnit.Framework;

namespace Roguehalla.Net.Tests
{
    public sealed class TutorialFlowTests
    {
        static TutorialObservation Obs(float x = 0, float y = 0, WeaponId weapon = WeaponId.Fists) =>
            new TutorialObservation { HasSelf = true, X = x, Y = y, Weapon = weapon, HasRack = true, RackId = 9, RackX = 400, RackY = 0, HasDummy = true, DummyId = 7, DummyX = 500, DummyY = 0 };
        static TutorialObservation Armed2(byte active) { var o = Obs(weapon: WeaponId.Sword); o.HasInventory = true; o.Slot0 = WeaponId.Sword; o.Slot1 = WeaponId.Bow; o.ActiveSlot = active; return o; }

        static TutorialFlow At(TutorialStep step) { var f = new TutorialFlow(); f.ResumeAt((int)step); return f; }

        [Test]
        public void WelcomeAdvancesOnlyOnExplicitStartNeverOnObservations()
        {
            var f = new TutorialFlow();
            Assert.IsFalse(f.Evaluate(Obs(500, 0, WeaponId.Sword))); Assert.AreEqual(TutorialStep.Welcome, f.Step);
            Assert.IsTrue(f.Start()); Assert.AreEqual(TutorialStep.Move, f.Step); Assert.IsFalse(f.Start());
        }

        [Test]
        public void MoveNeedsTheOwnPositionInsideTheRingAimedAtTheRack()
        {
            var f = At(TutorialStep.Move);
            Assert.IsFalse(f.Evaluate(Obs(0, 0)));
            Assert.AreEqual(TutorialTargetKind.Point, f.Target.Kind);
            Assert.AreEqual(120f, f.Target.X, .01f); Assert.AreEqual(0f, f.Target.Y, .01f);
            Assert.IsFalse(f.Evaluate(Obs(30, 0)), "still outside the ring");
            Assert.IsTrue(f.Evaluate(Obs(100, 10))); Assert.AreEqual(TutorialStep.Pickup, f.Step);
            Assert.AreEqual(TutorialTargetKind.Container, f.Target.Kind); Assert.AreEqual(9u, f.Target.Id);
        }

        [Test]
        public void PickupDoesNotAdvanceWithoutAWeaponChange()
        {
            var f = At(TutorialStep.Pickup);
            for (int i = 0; i < 5; i++) Assert.IsFalse(f.Evaluate(Obs(390, 0, WeaponId.Fists)));
            var fistsOnly = Obs(); fistsOnly.HasInventory = true; fistsOnly.Slot0 = WeaponId.Fists;
            Assert.IsFalse(f.Evaluate(fistsOnly)); Assert.AreEqual(TutorialStep.Pickup, f.Step);
            Assert.IsTrue(f.Evaluate(Obs(390, 0, WeaponId.Axe))); Assert.AreEqual(TutorialStep.Hit, f.Step);
            Assert.AreEqual(TutorialTargetKind.Actor, f.Target.Kind); Assert.AreEqual(7u, f.Target.Id);
        }

        [Test]
        public void HitNeedsALandedHitOnTheDummy()
        {
            var f = At(TutorialStep.Hit);
            Assert.IsFalse(f.Evaluate(Obs(weapon: WeaponId.Sword)));
            var hit = Obs(weapon: WeaponId.Sword); hit.HitDummy = true;
            Assert.IsTrue(f.Evaluate(hit)); Assert.AreEqual(TutorialStep.Swap, f.Step);
        }

        [Test]
        public void SwapNeedsTwoWeaponsAndAnActiveSlotChange()
        {
            var f = At(TutorialStep.Swap);
            var one = Obs(weapon: WeaponId.Sword); one.HasInventory = true; one.Slot0 = WeaponId.Sword;
            Assert.IsFalse(f.Evaluate(one)); one.ActiveSlot = 1; Assert.IsFalse(f.Evaluate(one), "slot change with one weapon is not a swap");
            Assert.IsFalse(f.Evaluate(Armed2(0))); Assert.IsFalse(f.Evaluate(Armed2(0)));
            Assert.IsTrue(f.Evaluate(Armed2(1))); Assert.AreEqual(TutorialStep.Wish, f.Step);
        }

        [Test]
        public void WishNeedsANewCastNotAWishAlreadyOnCooldown()
        {
            var f = At(TutorialStep.Wish);
            var busy = Obs(); busy.WishBusy = true;
            Assert.IsFalse(f.Evaluate(busy), "already recharging at entry");
            Assert.IsFalse(f.Evaluate(Obs()));
            Assert.IsTrue(f.Evaluate(busy)); Assert.IsTrue(f.Done); Assert.IsFalse(f.Running);
        }

        [Test]
        public void FullPlaythroughReachesDone()
        {
            var f = new TutorialFlow(); f.Start();
            Assert.IsTrue(f.Evaluate(Obs(0, 0)) == false); Assert.IsTrue(f.Evaluate(Obs(110, 0)));
            Assert.IsTrue(f.Evaluate(Obs(390, 0, WeaponId.Sword)));
            var hit = Obs(weapon: WeaponId.Sword); hit.HitDummy = true; Assert.IsTrue(f.Evaluate(hit));
            Assert.IsFalse(f.Evaluate(Armed2(0))); Assert.IsTrue(f.Evaluate(Armed2(1)));
            Assert.IsFalse(f.Evaluate(Obs())); var busy = Obs(); busy.WishBusy = true; Assert.IsTrue(f.Evaluate(busy));
            Assert.AreEqual(TutorialStep.Done, f.Step); Assert.IsTrue(f.Done);
        }

        [Test]
        public void ResumeClampsAndRestartReturnsToWelcome()
        {
            var f = new TutorialFlow();
            f.ResumeAt(3); Assert.AreEqual(TutorialStep.Hit, f.Step); Assert.IsTrue(f.Running);
            f.ResumeAt(99); Assert.AreEqual(TutorialStep.Done, f.Step); Assert.IsTrue(f.Done);
            f.ResumeAt(-4); Assert.AreEqual(TutorialStep.Welcome, f.Step);
            f.ResumeAt(2); f.Restart(); Assert.AreEqual(TutorialStep.Welcome, f.Step); Assert.IsTrue(f.Running);
        }

        [Test]
        public void SkipStopsEvaluationAndRestartRevivesIt()
        {
            var f = At(TutorialStep.Pickup);
            f.Skip(); Assert.IsTrue(f.Skipped); Assert.IsFalse(f.Running); Assert.AreEqual(TutorialTargetKind.None, f.Target.Kind);
            Assert.IsFalse(f.Evaluate(Obs(0, 0, WeaponId.Sword)), "skipped flows never advance"); Assert.AreEqual(TutorialStep.Pickup, f.Step);
            f.Restart(); Assert.IsFalse(f.Skipped); Assert.IsTrue(f.Running);
            var done = At(TutorialStep.Done); done.Skip(); Assert.IsFalse(done.Skipped, "a finished tutorial is not retroactively skipped");
        }

        [Test]
        public void CopyKeepsKeyPlaceholderForTheUiToFill()
        {
            Assert.AreEqual("Press {wish} to cast your Wish (click to place it if it aims)", At(TutorialStep.Wish).Copy);
            for (int i = 0; i < TutorialFlow.StepCount; i++) { var f = new TutorialFlow(); f.ResumeAt(i); Assert.IsNotEmpty(f.Copy); }
        }
    }
}
