using NUnit.Framework;
using Roguehalla.Net.Ui;

namespace Roguehalla.Net.Tests
{
    /// <summary>P-UI-MOTION-1 oracle: server time for HUD countdowns, advanced between snapshots, never going back.</summary>
    public sealed class HudClockTests
    {
        static HudClock Started() { var c = new HudClock(); c.OnServerTime(10000, 5000); return c; }

        [Test]
        public void NotReadyGivesZeroUntilTheFirstServerTime()
        {
            var c = new HudClock();
            Assert.That(c.Ready, Is.False); Assert.That(c.Now(1234), Is.EqualTo(0));
            c.OnServerTime(10000, 5000);
            Assert.That(c.Ready, Is.True); Assert.That(c.Now(5000), Is.EqualTo(10000));
        }

        [Test]
        public void BetweenSnapshotsNowRisesWithLocalTime()
        {
            var c = Started();
            Assert.That(c.Now(5050), Is.EqualTo(10050));
            c.OnServerTime(10050, 5050);
            Assert.That(c.Now(5075), Is.EqualTo(10075));
            Assert.That(c.Now(5100), Is.EqualTo(10100));
        }

        [Test]
        public void NowStopsAtMaxAheadPastTheLastSnapshot()
        {
            var c = Started();
            Assert.That(HudClock.MaxAheadMs, Is.EqualTo(150));
            Assert.That(c.Now(5150), Is.EqualTo(10150));
            Assert.That(c.Now(6000), Is.EqualTo(10150));
            Assert.That(c.Now(60000), Is.EqualTo(10150));
        }

        [Test]
        public void ASnapshotBehindTheExtrapolatedAnswerDoesNotLowerNow()
        {
            var c = Started();
            Assert.That(c.Now(5100), Is.EqualTo(10100));
            c.OnServerTime(10050, 5100);                     // base 10050 is behind the 10100 already shown
            Assert.That(c.Now(5100), Is.EqualTo(10100));
            Assert.That(c.Now(5120), Is.EqualTo(10100), "waits until the new base passes it");
            Assert.That(c.Now(5200), Is.EqualTo(10150));
            Assert.That(c.Now(5300), Is.EqualTo(10200).Within(0), "capped 150 ms past the new base");
        }

        [Test]
        public void ALowerServerTimeIsANewTimelineAndResets()
        {
            var c = Started();
            Assert.That(c.Now(5100), Is.EqualTo(10100));
            c.OnServerTime(2000, 5200);
            Assert.That(c.Now(5200), Is.EqualTo(2000));
            Assert.That(c.Now(5250), Is.EqualTo(2050));
        }

        [Test]
        public void TheSameServerTimeTwiceKeepsTheFirstArrivalStamp()
        {
            var c = Started();
            c.OnServerTime(10000, 5100);
            Assert.That(c.Now(5100), Is.EqualTo(10100));
            Assert.That(c.Now(5200), Is.EqualTo(10150), "150 ms past the first arrival, capped");
        }

        [Test]
        public void LocalTimeGoingBackDoesNotLowerNow()
        {
            var c = Started();
            Assert.That(c.Now(5100), Is.EqualTo(10100));
            Assert.That(c.Now(5000), Is.EqualTo(10100));
            Assert.That(c.Now(1000), Is.EqualTo(10100));
            var d = Started();
            Assert.That(d.Now(4000), Is.EqualTo(10000), "before the arrival counts as no time passed");
        }

        [Test]
        public void ResetForgetsEverything()
        {
            var c = Started();
            Assert.That(c.Now(5100), Is.EqualTo(10100));
            c.Reset();
            Assert.That(c.Ready, Is.False); Assert.That(c.Now(5100), Is.EqualTo(0));
            c.OnServerTime(3000, 6000);
            Assert.That(c.Now(6010), Is.EqualTo(3010));
        }
    }
}
