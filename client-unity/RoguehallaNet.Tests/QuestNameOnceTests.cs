using NUnit.Framework;
using Roguehalla.Net.Ui;

namespace Roguehalla.Net.Tests
{
    /// <summary>
    /// P-UI-WORLD-LABELS-1: a quest's name stands on one point only. Per frame: <see cref="QuestNameOnce.Begin"/>, then every DRAWN objective point is
    /// <see cref="QuestNameOnce.Offer"/>ed in delivered order, then <see cref="QuestNameOnce.Carries"/> is asked per point. The point that carried last frame
    /// keeps the name while it is among the offers; else the first offer takes it; a quest with no offer in a frame forgets its carrier; two
    /// offers of one quest within .01 of each other both carry. The packet (docs/backlog/packets/P-UI-WORLD-LABELS-1.md, "Rules") has the rules.
    /// </summary>
    public sealed class QuestNameOnceTests
    {
        const float AX = 100f, BX = 200f, CX = 300f, Y = 50f;

        static void Frame(QuestNameOnce q, uint quest, params float[] xs)
        {
            q.Begin();
            foreach (float x in xs) q.Offer(quest, x, Y);
        }

        /// <summary>Which of the points the quest's name stands on, as a string of the x values that carry ("100", "200 300", "" when none).</summary>
        static string Carriers(QuestNameOnce q, uint quest, params float[] xs)
        {
            string s = "";
            foreach (float x in xs) if (q.Carries(quest, x, Y)) s += (s.Length == 0 ? "" : " ") + x;
            return s;
        }

        [Test]
        public void FirstDrawnCarries()
        {
            var q = new QuestNameOnce();
            Frame(q, 7, AX, BX, CX);
            Assert.That(Carriers(q, 7, AX, BX, CX), Is.EqualTo("100"), "only the first offer carries the name");
        }

        [Test]
        public void ACarrierKeepsTheNameWhileDrawn()
        {
            var q = new QuestNameOnce();
            Frame(q, 7, AX, BX, CX);
            Assert.That(Carriers(q, 7, AX, BX, CX), Is.EqualTo("100"), "frame 1: A");
            Frame(q, 7, BX, CX);
            Assert.That(Carriers(q, 7, BX, CX), Is.EqualTo("200"), "frame 2: A is not drawn, the first drawn (B) takes it");
            Frame(q, 7, AX, BX, CX);
            Assert.That(Carriers(q, 7, AX, BX, CX), Is.EqualTo("200"), "frame 3: A is back, B keeps it (sticky)");
            Frame(q, 7, AX, BX, CX);
            Assert.That(Carriers(q, 7, AX, BX, CX), Is.EqualTo("200"), "frame 4: still B");
        }

        [Test]
        public void ALaterCarrierIsFoundBeforeTheFirstAnswers()
        {
            var q = new QuestNameOnce();
            Frame(q, 7, CX);
            Assert.That(Carriers(q, 7, CX), Is.EqualTo("300"), "frame 1: C alone carries");
            Frame(q, 7, AX, BX, CX);
            Assert.That(q.Carries(7, AX, Y), Is.False, "frame 2: A, asked first, does not carry (C did)");
            Assert.That(q.Carries(7, BX, Y), Is.False, "B does not");
            Assert.That(q.Carries(7, CX, Y), Is.True, "C keeps it");
        }

        [Test]
        public void ANotDrawnCarrierPassesItToTheFirstDrawn()
        {
            var q = new QuestNameOnce();
            Frame(q, 7, AX, BX, CX);
            Assert.That(Carriers(q, 7, AX, BX, CX), Is.EqualTo("100"), "frame 1: A");
            Frame(q, 7, CX);
            Assert.That(Carriers(q, 7, CX), Is.EqualTo("300"), "frame 2: only C is drawn, it takes the name");
            Frame(q, 7, BX, CX);
            Assert.That(Carriers(q, 7, BX, CX), Is.EqualTo("300"), "frame 3: C is sticky even though B comes first");
        }

        [Test]
        public void QuestsAreToldApart()
        {
            var q = new QuestNameOnce();
            q.Begin();
            q.Offer(1, AX, Y); q.Offer(2, AX, Y); q.Offer(1, BX, Y); q.Offer(2, BX, Y);
            Assert.That(q.Count, Is.EqualTo(2), "two quests offered");
            Assert.That(Carriers(q, 1, AX, BX), Is.EqualTo("100"), "quest 1 has its own carrier");
            Assert.That(Carriers(q, 2, AX, BX), Is.EqualTo("100"), "quest 2 has its own carrier, at the same places");
            q.Begin();
            q.Offer(1, BX, Y); q.Offer(1, AX, Y); q.Offer(2, BX, Y); q.Offer(2, AX, Y);
            Assert.That(Carriers(q, 1, AX, BX), Is.EqualTo("100"), "quest 1 keeps A");
            Assert.That(Carriers(q, 2, AX, BX), Is.EqualTo("100"), "quest 2 keeps A");
            q.Begin();
            q.Offer(1, BX, Y); q.Offer(2, BX, Y); q.Offer(2, AX, Y);
            Assert.That(Carriers(q, 1, AX, BX), Is.EqualTo("200"), "quest 1 lost A: B takes it");
            Assert.That(Carriers(q, 2, AX, BX), Is.EqualTo("100"), "quest 2 still has A");
        }

        [Test]
        public void AQuestWithNoOfferForgets()
        {
            var q = new QuestNameOnce();
            Frame(q, 7, AX, BX);
            Assert.That(Carriers(q, 7, AX, BX), Is.EqualTo("100"), "frame 1: A");
            Frame(q, 7, BX);
            Assert.That(Carriers(q, 7, BX), Is.EqualTo("200"), "frame 2: B takes it");
            Frame(q, 8, AX);
            Assert.That(q.Count, Is.EqualTo(1), "frame 3: only quest 8 is offered");
            Assert.That(Carriers(q, 8, AX), Is.EqualTo("100"), "quest 8 carries");
            Frame(q, 7, AX, BX);
            Assert.That(Carriers(q, 7, AX, BX), Is.EqualTo("100"), "frame 4: quest 7 forgot B while it had no offer: the first (A) takes it");
        }

        [Test]
        public void ZeroIsAHandle()
        {
            var q = new QuestNameOnce();
            Frame(q, 0, AX, BX);
            Assert.That(q.Count, Is.EqualTo(1), "quest 0 is counted");
            Assert.That(Carriers(q, 0, AX, BX), Is.EqualTo("100"), "quest 0 carries on its first point only");
            Frame(q, 0, BX, AX);
            Assert.That(Carriers(q, 0, AX, BX), Is.EqualTo("100"), "and keeps it");
        }

        [Test]
        public void AMovedByAHairIsTheSameCarrier()
        {
            var q = new QuestNameOnce();
            Frame(q, 7, AX, BX);
            Assert.That(Carriers(q, 7, AX, BX), Is.EqualTo("100"), "frame 1: A");
            q.Begin();
            q.Offer(7, BX, Y); q.Offer(7, AX + .005f, Y);
            Assert.That(q.Carries(7, BX, Y), Is.False, "frame 2: B does not take the name from A delivered .005 away");
            Assert.That(q.Carries(7, AX + .005f, Y), Is.True, "A, delivered .005 away, is the same point");
        }

        [Test]
        public void TwoOffersWithinAHairBothCarry()
        {
            var q = new QuestNameOnce();
            q.Begin();
            q.Offer(7, AX, Y); q.Offer(7, AX + .005f, Y); q.Offer(7, BX, Y);
            Assert.That(q.Carries(7, AX, Y), Is.True, "the first");
            Assert.That(q.Carries(7, AX + .005f, Y), Is.True, "a second offer within .01 of it carries too (accepted)");
            Assert.That(q.Carries(7, BX, Y), Is.False, "a point further away does not");
        }

        [Test]
        public void GrowsPast()
        {
            var q = new QuestNameOnce();
            const int quests = 100;
            q.Begin();
            for (uint i = 0; i < quests; i++) { q.Offer(i + 1, i * 10f, 0f); q.Offer(i + 1, i * 10f, 50f); }
            Assert.That(q.Count, Is.EqualTo(quests), "100 quests offered");
            for (uint i = 0; i < quests; i++)
            {
                Assert.That(q.Carries(i + 1, i * 10f, 0f), Is.True, "quest " + (i + 1) + ": the first point carries");
                Assert.That(q.Carries(i + 1, i * 10f, 50f), Is.False, "quest " + (i + 1) + ": the second does not");
            }
            q.Begin();
            for (uint i = 0; i < quests; i++) { q.Offer(i + 1, i * 10f, 50f); q.Offer(i + 1, i * 10f, 0f); }
            for (uint i = 0; i < quests; i++)
            {
                Assert.That(q.Carries(i + 1, i * 10f, 0f), Is.True, "quest " + (i + 1) + " keeps its carrier a frame later");
                Assert.That(q.Carries(i + 1, i * 10f, 50f), Is.False, "quest " + (i + 1) + ": the other still does not");
            }
        }

        [Test]
        public void ASteadyFrameAllocatesNothing()
        {
            var q = new QuestNameOnce();
            q.Begin();
            q.Offer(1, AX, Y); q.Offer(1, BX, Y); q.Offer(2, AX, Y); q.Offer(2, BX, Y);
            Assert.That(q.Carries(2, AX, Y), Is.True, "set-up: the first point of the second quest carries");
            Assert.That(q.Carries(2, BX, Y), Is.False, "set-up: the second quest's other point does not");
            int hits = 0;
            double bytes = AllocationProbe.PerCall(i =>
            {
                q.Begin();
                q.Offer(1, AX, Y); q.Offer(1, BX, Y); q.Offer(2, AX, Y); q.Offer(2, BX, Y);
                if (q.Carries(1, AX, Y)) hits++;
                if (q.Carries(1, BX, Y)) hits++;
                if (q.Carries(2, AX, Y)) hits++;
                if (q.Carries(2, BX, Y)) hits++;
            }, 2000);
            Assert.That(hits, Is.GreaterThan(0));
            Assert.That(bytes, Is.EqualTo(0.0), "bytes per frame");
        }
    }
}
