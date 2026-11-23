using NUnit.Framework;
using Roguehalla.Net;
using Roguehalla.Net.Protocol;
using Roguehalla.Net.Ui;

namespace Roguehalla.Net.Tests
{
    /// <summary>
    /// P-UI-TOAST-LANES-1: which toast lane, tone, words and family each delivered server event gets (one row of the packet's
    /// table per case), the hold formula and the shortening of a long text.
    /// </summary>
    public sealed class NoticeRouterTests
    {
        static WireEvent Toast(string text) => new WireEvent { Kind = EventKind.Toast, ToastText = text };
        static WireEvent Potion(PotionNotice value) => new WireEvent { Kind = EventKind.PotionNotice, PotionNotice = (byte)value };

        static void AssertRoute(WireEvent item, NoticeLane lane, NoticeTone tone, string text, string key)
        {
            var r = NoticeRouter.Route(item);
            Assert.AreEqual(lane, r.Lane, "lane");
            if (lane == NoticeLane.None) return;
            Assert.AreEqual(tone, r.Tone, "tone");
            Assert.AreEqual(text, r.Text, "text");
            Assert.AreEqual(key, r.Key, "key");
        }

        [Test]
        public void AToastWithNoTextShowsNothing()
        {
            Assert.AreEqual(NoticeLane.Event, NoticeRouter.Route(Toast("Something")).Lane, "a toast with text does show");
            AssertRoute(Toast(null), NoticeLane.None, 0, null, null);
            AssertRoute(Toast(""), NoticeLane.None, 0, null, null);
        }

        [TestCase("Quest complete: Letters", NoticeTone.Good)]
        [TestCase("Quest accepted: Letters", NoticeTone.Info)]
        [TestCase("Accepted: Wolf hunt", NoticeTone.Info)]
        [TestCase("Stat trade: +2 Offense for -1 Defense", NoticeTone.Good)]
        [TestCase("Slow down", NoticeTone.Info)]
        [TestCase("quest complete: lower case is another family", NoticeTone.Info)]
        public void AToastIsAnEventLineWithItsOwnWordsAndTone(string text, NoticeTone tone) =>
            AssertRoute(Toast(text), NoticeLane.Event, tone, text, null);

        [TestCase("Ember Imp: +1 kill stack · +1 Offense total")]
        [TestCase("Ember Imp: +2 kill stacks · +2 Offense total")]
        public void KillStackGrantsAreOneGoodFamily(string text) =>
            AssertRoute(Toast(text), NoticeLane.Event, NoticeTone.Good, text, NoticeRouter.KillStackKey);

        [Test]
        public void ThePrefixRowsComeBeforeTheKillStackRow() =>
            AssertRoute(Toast("Quest complete: one kill stack"), NoticeLane.Event, NoticeTone.Good, "Quest complete: one kill stack", null);

        [Test]
        public void ALongToastTextIsShortenedBeforeItIsShown()
        {
            string text = "Quest accepted: " + new string('x', 100);
            var r = NoticeRouter.Route(Toast(text));
            Assert.AreEqual(NoticeLane.Event, r.Lane);
            Assert.AreEqual(NoticeRouter.MaxChars, r.Text.Length);
            Assert.IsTrue(r.Text.EndsWith("…"));
        }

        [TestCase(PotionNotice.Stored, NoticeLane.Event, NoticeTone.Good)]
        [TestCase(PotionNotice.LargeStored, NoticeLane.Event, NoticeTone.Good)]
        [TestCase(PotionNotice.Used, NoticeLane.Feedback, NoticeTone.Good)]
        [TestCase(PotionNotice.LargeUsed, NoticeLane.Feedback, NoticeTone.Good)]
        [TestCase(PotionNotice.BagFull, NoticeLane.Feedback, NoticeTone.Warn)]
        [TestCase(PotionNotice.CampBagFull, NoticeLane.Feedback, NoticeTone.Warn)]
        [TestCase(PotionNotice.Blocked, NoticeLane.Feedback, NoticeTone.Warn)]
        [TestCase(PotionNotice.Busy, NoticeLane.Feedback, NoticeTone.Warn)]
        [TestCase(PotionNotice.Full, NoticeLane.Feedback, NoticeTone.Warn)]
        [TestCase(PotionNotice.Empty, NoticeLane.Feedback, NoticeTone.Warn)]
        public void APotionNoticeGivesLaneAndToneAndLeavesTheWordsToUnity(PotionNotice value, NoticeLane lane, NoticeTone tone) =>
            AssertRoute(Potion(value), lane, tone, null, null);

        [TestCase((byte)0)]
        [TestCase((byte)11)]
        [TestCase((byte)200)]
        public void AnUnknownPotionNoticeShowsNothing(byte value)
        {
            Assert.AreEqual(NoticeLane.Event, NoticeRouter.Route(Potion(PotionNotice.Stored)).Lane, "a known value does show");
            AssertRoute(new WireEvent { Kind = EventKind.PotionNotice, PotionNotice = value }, NoticeLane.None, 0, null, null);
        }

        [Test]
        public void RefusalsAnswerInTheFeedbackLaneInPlayerWords()
        {
            AssertRoute(new WireEvent { Kind = EventKind.PickupRefused, PickupReason = 3 }, NoticeLane.Feedback, NoticeTone.Warn, "Can't pick that up", null);
            AssertRoute(new WireEvent { Kind = EventKind.ChestRefused, ChestReason = 2 }, NoticeLane.Feedback, NoticeTone.Warn, "That choice was refused", null);
        }

        [Test]
        public void TheWindEmblemIsAnInfoEvent() =>
            AssertRoute(new WireEvent { Kind = EventKind.WindEmblem }, NoticeLane.Event, NoticeTone.Info, "Wind emblem", null);

        [Test]
        public void CannonNoticesFollowTheirCodes()
        {
            AssertRoute(new WireEvent { Kind = EventKind.CannonNotice, CannonCode = 1 }, NoticeLane.Feedback, NoticeTone.Warn, "Cannon sealed", null);
            AssertRoute(new WireEvent { Kind = EventKind.CannonNotice, CannonCode = 2 }, NoticeLane.Feedback, NoticeTone.Warn, "Cannot enter cannon", null);
            AssertRoute(new WireEvent { Kind = EventKind.CannonNotice, CannonCode = 3 }, NoticeLane.Event, NoticeTone.Info, "Cannon blast", null);
            AssertRoute(new WireEvent { Kind = EventKind.CannonNotice, CannonCode = 4, CannonSeconds = 7 }, NoticeLane.Feedback, NoticeTone.Info, "Cannon reloading · 7s", null);
            AssertRoute(new WireEvent { Kind = EventKind.CannonNotice, CannonCode = 0, CannonSeconds = 12 }, NoticeLane.Feedback, NoticeTone.Info, "Cannon reloading · 12s", null);
        }

        [Test]
        public void ACannonReloadWithoutSecondsHasNoNumberAndNoStrayLetter() =>
            AssertRoute(new WireEvent { Kind = EventKind.CannonNotice, CannonCode = 4 }, NoticeLane.Feedback, NoticeTone.Info, "Cannon reloading", null);

        [TestCase(EventKind.Death)]
        [TestCase(EventKind.Bounty)]
        [TestCase(EventKind.MatchEnded)]
        [TestCase(EventKind.Result)]
        [TestCase(EventKind.CampCleared)]
        public void OtherKindsShowNoToast(EventKind kind)
        {
            Assert.AreEqual(NoticeLane.Event, NoticeRouter.Route(new WireEvent { Kind = EventKind.WindEmblem }).Lane, "a toast kind does show");
            AssertRoute(new WireEvent { Kind = kind }, NoticeLane.None, 0, null, null);
        }

        [TestCase(0, 2.2f)]
        [TestCase(20, 2.2f)]
        [TestCase(40, 3.1f)]
        [TestCase(200, 4.5f)]
        public void HoldGrowsWithTheTextAndIsClamped(int characters, float seconds) =>
            Assert.AreEqual(seconds, NoticeRouter.HoldSeconds(new string('a', characters)), .001f);

        [Test]
        public void HoldOfNullTextIsTheShortest() => Assert.AreEqual(2.2f, NoticeRouter.HoldSeconds(null), .001f);

        [Test]
        public void ShortKeepsEightyCharactersAndCutsEightyOne()
        {
            string eighty = new string('a', 80);
            Assert.AreEqual(eighty, NoticeRouter.Short(eighty));
            string cut = NoticeRouter.Short(new string('a', 81));
            Assert.AreEqual(80, cut.Length);
            Assert.IsTrue(cut.EndsWith("…"));
            Assert.AreEqual(new string('a', 79), cut.Substring(0, 79));
        }

        [Test]
        public void ShortLeavesNullAndShortTextAlone()
        {
            Assert.IsNull(NoticeRouter.Short(null));
            Assert.AreEqual("hi", NoticeRouter.Short("hi"));
            Assert.AreEqual(80, NoticeRouter.Short(new string('b', 500)).Length);
        }
    }
}
