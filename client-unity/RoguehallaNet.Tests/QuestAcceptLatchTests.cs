using System.Collections.Generic;
using NUnit.Framework;
using Roguehalla.Net.Protocol;
using Roguehalla.Net.Ui;

namespace Roguehalla.Net.Tests
{
    /// <summary>P-UI-QUEST-OFFER-1-fix oracle: the pure latch behind the quest journal's ACCEPT and the offer card's Enter.
    /// One hold per handle for 2.5 s of ticks, a signature of the row that ends it when the row changes, a press stamp that
    /// outlives the answer, four slots that replace the oldest press, and no allocation after construction.</summary>
    public sealed class QuestAcceptLatchTests
    {
        static readonly QuestHandle H1 = new QuestHandle(1), H2 = new QuestHandle(2), H3 = new QuestHandle(3), H4 = new QuestHandle(4),
            H5 = new QuestHandle(5), H7 = new QuestHandle(7), H9 = new QuestHandle(9);

        static int S() { return QuestAcceptLatch.Signature(true, false, QuestRowState.Preview); }

        [Test]
        public void BeginHoldsAndASecondBeginIsRefused()
        {
            var latch = new QuestAcceptLatch();
            int s = S();
            Assert.That(latch.TryBegin(H7, s), Is.True, "a first begin is allowed");
            Assert.That(latch.IsHeld(H7), Is.True, "the handle is held");
            Assert.That(latch.PressedRecently(H7), Is.True, "and stamped");
            Assert.That(latch.Held, Is.EqualTo(1));
            Assert.That(latch.IsHeld(H9), Is.False, "another handle is not held");

            latch.Tick(1f);
            Assert.That(latch.TryBegin(H7, s + 5), Is.False, "a second begin of a held handle is refused");
            Assert.That(latch.Held, Is.EqualTo(1), "and starts nothing");
            latch.Answer(H7, s);
            Assert.That(latch.IsHeld(H7), Is.True, "the first signature stays: an answer equal to it changes nothing");
            latch.Tick(1.5f);
            Assert.That(latch.IsHeld(H7), Is.False, "the refused begin did not restart the hold: 1 + 1.5 s ends it");
        }

        [Test]
        public void TheHoldEndsAtExactlyHoldSeconds()
        {
            Assert.That(QuestAcceptLatch.HoldSeconds, Is.EqualTo(2.5f), "the hold is 2.5 s");
            Assert.That(QuestAcceptLatch.Capacity, Is.EqualTo(4), "four slots");
            Assert.That(QuestAcceptLatch.Gone, Is.EqualTo(-1));

            var latch = new QuestAcceptLatch();
            int s = S();
            Assert.That(latch.TryBegin(H7, s), Is.True);
            latch.Tick(2.25f);
            Assert.That(latch.IsHeld(H7), Is.True, ".25 s left");
            latch.Tick(.25f);
            Assert.That(latch.IsHeld(H7), Is.False, "ended exactly at 2.5 s");

            latch = new QuestAcceptLatch();
            Assert.That(latch.TryBegin(H7, s), Is.True);
            Assert.That(latch.IsHeld(H7), Is.True);
            latch.Tick(2.5f);
            Assert.That(latch.IsHeld(H7), Is.False, "one Tick(2.5f) ends it");

            latch = new QuestAcceptLatch();
            Assert.That(latch.TryBegin(H7, s), Is.True);
            Assert.That(latch.IsHeld(H7), Is.True);
            latch.Tick(1e30f);
            Assert.That(latch.IsHeld(H7), Is.False, "a huge dt ends it");
            Assert.That(latch.PressedRecently(H7), Is.False, "and the stamp");
            Assert.That(latch.Held, Is.EqualTo(0));
        }

        [Test]
        public void AfterTheHoldABeginIsAllowedAgain()
        {
            var latch = new QuestAcceptLatch();
            int s = S();
            Assert.That(latch.TryBegin(H7, s), Is.True);
            Assert.That(latch.IsHeld(H7), Is.True);
            latch.Tick(2.5f);
            Assert.That(latch.IsHeld(H7), Is.False);
            Assert.That(latch.TryBegin(H7, s), Is.True, "begin 7 is true again");
            Assert.That(latch.IsHeld(H7), Is.True, "and holds again");
            latch.Tick(2.25f);
            Assert.That(latch.IsHeld(H7), Is.True, "for a whole new hold");
        }

        [Test]
        public void HoldsArePerHandle()
        {
            var latch = new QuestAcceptLatch();
            int s = S();
            Assert.That(latch.TryBegin(H1, s), Is.True);
            Assert.That(latch.IsHeld(H1), Is.True);
            latch.Tick(1f);
            Assert.That(latch.TryBegin(H2, s), Is.True, "a different handle is not refused");
            latch.Tick(1.5f);
            Assert.That(latch.IsHeld(H1), Is.False, "1 ended (2.5 s old)");
            Assert.That(latch.IsHeld(H2), Is.True, "2 is held (1 s left)");
            Assert.That(latch.Held, Is.EqualTo(1));
        }

        [Test]
        public void TheFullTableReplacesTheOldest()
        {
            var latch = new QuestAcceptLatch();
            int s = S();
            foreach (var h in new[] { H1, H2, H3, H4 })
            {
                Assert.That(latch.TryBegin(h, s), Is.True);
                latch.Tick(.25f);
            }
            Assert.That(latch.IsHeld(H1), Is.True, "four holds fit");
            Assert.That(latch.Held, Is.EqualTo(4));
            Assert.That(latch.TryBegin(H2, s), Is.False, "a refused begin of a held handle");
            Assert.That(latch.IsHeld(H1), Is.True, "evicts nothing");
            Assert.That(latch.Held, Is.EqualTo(4));

            Assert.That(latch.TryBegin(H5, s), Is.True, "a fifth handle is allowed");
            Assert.That(latch.IsHeld(H1), Is.False, "and takes the slot of the oldest");
            Assert.That(latch.IsHeld(H2), Is.True);
            Assert.That(latch.IsHeld(H3), Is.True);
            Assert.That(latch.IsHeld(H4), Is.True);
            Assert.That(latch.IsHeld(H5), Is.True);
            Assert.That(latch.Held, Is.EqualTo(4));
        }

        [Test]
        public void ClearEndsAll()
        {
            var latch = new QuestAcceptLatch();
            int s = S();
            Assert.That(latch.TryBegin(H1, s), Is.True);
            Assert.That(latch.TryBegin(H2, s), Is.True);
            Assert.That(latch.IsHeld(H1), Is.True);
            latch.Answer(H2, s + 1);
            Assert.That(latch.PressedRecently(H2), Is.True, "2 keeps its stamp after its answer");
            latch.Clear();
            Assert.That(latch.Held, Is.EqualTo(0), "no hold");
            Assert.That(latch.IsHeld(H1), Is.False);
            Assert.That(latch.PressedRecently(H1), Is.False, "no stamp of 1");
            Assert.That(latch.PressedRecently(H2), Is.False, "no stamp of 2: a held and a stamped-only slot both clear");
            Assert.That(latch.TryBegin(H1, s), Is.True, "a clear table begins again");
        }

        [Test]
        public void BadInputsAreSafe()
        {
            var latch = new QuestAcceptLatch();
            int s = S();
            Assert.That(latch.TryBegin(default(QuestHandle), s), Is.False, "the default handle never begins");
            Assert.That(latch.IsHeld(default(QuestHandle)), Is.False);
            Assert.That(latch.PressedRecently(default(QuestHandle)), Is.False);
            Assert.That(latch.Held, Is.EqualTo(0));

            Assert.That(latch.TryBegin(H7, s), Is.True);
            Assert.That(latch.IsHeld(H7), Is.True);
            latch.Tick(float.NaN);
            latch.Tick(-1f);
            latch.Tick(0f);
            Assert.That(latch.IsHeld(H7), Is.True, "NaN, a negative dt and zero change nothing");
            latch.Tick(2.25f);
            Assert.That(latch.IsHeld(H7), Is.True, "still .25 left: none of them aged it");
            latch.Tick(.25f);
            Assert.That(latch.IsHeld(H7), Is.False);
        }

        [Test]
        public void AnAnswerEndsTheHoldOnlyWhenTheRowDiffers()
        {
            var latch = new QuestAcceptLatch();
            int s = S();
            Assert.That(latch.TryBegin(H7, s), Is.True);
            Assert.That(latch.IsHeld(H7), Is.True);
            latch.Answer(H7, s);
            Assert.That(latch.IsHeld(H7), Is.True, "an equal signature keeps the hold");
            latch.Answer(H9, s + 1);
            Assert.That(latch.IsHeld(H7), Is.True, "an answer for another handle changes nothing");
            latch.Answer(H7, s + 1);
            Assert.That(latch.IsHeld(H7), Is.False, "a different signature ends it");

            Assert.That(latch.TryBegin(H7, s), Is.True, "begin again");
            Assert.That(latch.IsHeld(H7), Is.True);
            latch.Answer(H7, QuestAcceptLatch.Gone);
            Assert.That(latch.IsHeld(H7), Is.False, "a row that is gone ends it");
            latch.Answer(H7, s + 1);
            Assert.That(latch.IsHeld(H7), Is.False, "an answer for a handle with no running hold changes nothing");
            Assert.That(latch.PressedRecently(H7), Is.True, "and no answer ends the stamp");
        }

        [Test]
        public void TheSignatureSeparatesTheFactsAnAcceptDependsOn()
        {
            var seen = new HashSet<int>();
            var states = new[] { QuestRowState.Preview, QuestRowState.Open, QuestRowState.Done, QuestRowState.Expired };
            foreach (bool canAccept in new[] { false, true })
                foreach (bool owner in new[] { false, true })
                    foreach (var state in states)
                    {
                        int sig = QuestAcceptLatch.Signature(canAccept, owner, state);
                        Assert.That(sig, Is.GreaterThanOrEqualTo(0), "never negative: " + canAccept + "/" + owner + "/" + state);
                        Assert.That(sig, Is.Not.EqualTo(QuestAcceptLatch.Gone));
                        Assert.That(seen.Add(sig), Is.True, "distinct: " + canAccept + "/" + owner + "/" + state + " gave " + sig);
                    }
            Assert.That(seen.Count, Is.EqualTo(16));
        }

        [Test]
        public void TryGetHeldListsTheHeldSlots()
        {
            var latch = new QuestAcceptLatch();
            int s = S();
            Assert.That(latch.TryBegin(H3, s), Is.True);
            Assert.That(latch.TryBegin(H5, s), Is.True);
            Assert.That(Listed(latch), Is.EquivalentTo(new uint[] { 3, 5 }), "two slots report, with handles 3 and 5");
            Assert.That(latch.TryGetHeld(QuestAcceptLatch.Capacity, out _), Is.False, "a slot past the table");
            Assert.That(latch.TryGetHeld(-1, out _), Is.False, "a negative slot");

            latch.Tick(2.5f);
            Assert.That(Listed(latch), Is.Empty, "none after the hold");

            Assert.That(latch.TryBegin(H3, s), Is.True);
            Assert.That(latch.TryBegin(H5, s), Is.True);
            latch.Answer(H3, s + 1);
            Assert.That(Listed(latch), Is.EquivalentTo(new uint[] { 5 }), "an answered slot reports false");
        }

        static List<uint> Listed(QuestAcceptLatch latch)
        {
            var list = new List<uint>();
            for (int i = 0; i < QuestAcceptLatch.Capacity; i++)
                if (latch.TryGetHeld(i, out var h)) list.Add(h.Value);
            return list;
        }

        [Test]
        public void TheLatchAllocatesNothing()
        {
            var latch = new QuestAcceptLatch();
            int s = S();
            Assert.That(latch.TryBegin(H7, s), Is.True);
            Assert.That(latch.IsHeld(H7), Is.True, "set-up: a begun hold is held");
            bool sink = false; int count = 0;
            double bytes = AllocationProbe.PerCall(i =>
            {
                sink ^= latch.TryBegin(H7, s);
                sink ^= latch.IsHeld(H7);
                sink ^= latch.PressedRecently(H7);
                latch.Answer(H7, s);
                sink ^= latch.TryGetHeld(0, out var h);
                count += QuestAcceptLatch.Signature(true, false, QuestRowState.Open) + (int)h.Value;
                latch.Tick(.25f);
                latch.Clear();
                latch.TryBegin(H7, s);
            });
            Assert.That(bytes, Is.EqualTo(0.0), "no allocation after construction");
            Assert.That(count >= 0 || sink || !sink, Is.True);
        }

        [Test]
        public void TheStampOutlivesTheAnswer()
        {
            var latch = new QuestAcceptLatch();
            int s = S();
            Assert.That(latch.TryBegin(H7, s), Is.True);
            latch.Answer(H7, s + 1);
            Assert.That(latch.IsHeld(H7), Is.False, "the answer ended the hold");
            Assert.That(latch.PressedRecently(H7), Is.True, "and left the stamp");
            latch.Tick(2.25f);
            Assert.That(latch.PressedRecently(H7), Is.True, ".25 s left");
            latch.Tick(.25f);
            Assert.That(latch.PressedRecently(H7), Is.False, "the stamp ends at 2.5 s");

            Assert.That(latch.TryBegin(H7, s), Is.True);
            latch.Answer(H7, s + 1);
            latch.Tick(1f);
            Assert.That(latch.TryBegin(H7, s), Is.True, "a new press after an answer is allowed and reuses its slot");
            Assert.That(latch.IsHeld(H7), Is.True);
            latch.Tick(2.25f);
            Assert.That(latch.PressedRecently(H7), Is.True, "the new press restarted the stamp");
            latch.Tick(.25f);
            Assert.That(latch.PressedRecently(H7), Is.False);
        }

        [Test]
        public void AStampEndsByTimeAndByClearOnly()
        {
            var latch = new QuestAcceptLatch();
            int s = S();
            Assert.That(latch.TryBegin(H8(), s), Is.True);
            latch.Answer(H8(), s + 1);
            Assert.That(latch.Held, Is.EqualTo(0), "after an answer no hold runs");
            Assert.That(latch.PressedRecently(H8()), Is.True, "while the stamp does");

            Assert.That(latch.TryBegin(H7, s), Is.True);
            latch.Answer(H7, s + 1);
            Assert.That(latch.PressedRecently(H7), Is.True);
            latch.Clear();
            Assert.That(latch.PressedRecently(H7), Is.False, "Clear ends the stamp");
            Assert.That(latch.PressedRecently(H9), Is.False, "a handle never pressed is false");
        }

        static QuestHandle H8() { return new QuestHandle(8); }

        [Test]
        public void TheFullTableEvictsTheOldestPress()
        {
            var latch = new QuestAcceptLatch();
            int s = S();
            Assert.That(latch.TryBegin(H1, s), Is.True);
            latch.Tick(.25f);
            Assert.That(latch.TryBegin(H2, s), Is.True);
            latch.Answer(H2, s + 1);
            Assert.That(latch.TryBegin(H3, s), Is.True);
            Assert.That(latch.TryBegin(H4, s), Is.True);
            Assert.That(latch.TryBegin(H5, s), Is.True, "the fifth press is allowed");
            Assert.That(latch.PressedRecently(H1), Is.False, "the oldest press lost its stamp");
            Assert.That(latch.PressedRecently(H2), Is.True, "an answered press is newer than 1: it stays");
            Assert.That(latch.IsHeld(H3), Is.True);
            Assert.That(latch.IsHeld(H4), Is.True);
            Assert.That(latch.IsHeld(H5), Is.True);
            Assert.That(latch.IsHeld(H1), Is.False, "and its hold");
        }
    }
}
