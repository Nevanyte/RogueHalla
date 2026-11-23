using System;
using NUnit.Framework;
using Roguehalla.Net.Protocol;
using Roguehalla.Net.Ui;

namespace Roguehalla.Net.Tests
{
    /// <summary>P-UI-QUEST-OFFER-1 oracle (engine-free): which quest offer the player can take where they stand, the lookup of a
    /// handle in a view, and where the offer card's top goes so that it ends above the floor. Pure and presentation only.</summary>
    public sealed class QuestOfferTests
    {
        static WireQuestRow Row(uint handle, bool canAccept, double x = 0, double y = 0, string def = "barrow_run")
        {
            return new WireQuestRow
            {
                Handle = new QuestHandle(handle), Def = def, GiverX = x, GiverY = y, State = QuestRowState.Preview, ExpiresAt = 0,
                Name = "N", GiverLabel = "G", Brief = "B", Objective = "O", Reward = "R", CanAccept = canAccept,
                Participants = new[] { new WireQuestParticipant { Id = new RosterId(7) } },
            };
        }

        static QuestView Quests(bool available, params WireQuestRow[] rows)
        {
            var transport = new FakeTransport();
            var session = new Session(transport, new SessionOptions { Url = "ws://x/ws", Profile = "p", Ticket = Array.Empty<byte>(), Now = () => 0 });
            session.Connect(); transport.Open();
            transport.Deliver(new ServerMsg.Welcome { Protocol = Wire.ProtocolVersion, MatchId = new byte[16], Session = new byte[16],
                PlayerId = 1, TickRate = 20, Map = new MapBlob(4, 4, new byte[16]) });
            var view = new WireQuestView
            {
                OwnerRoster = new RosterId(7),
                Roster = new[] { new WireQuestRosterEntry { Id = new RosterId(7), Label = "owner" } },
                Rows = rows, Objectives = new WireQuestObjective[0],
            };
            transport.Deliver(new ServerMsg.State { Tick = 1, Items = new StateItem[] { available ? new QuestsState { Available = true, View = view } : new QuestsState { Available = false } } });
            return session.Collections.Quests;
        }

        static QuestView Quests(params WireQuestRow[] rows) { return Quests(true, rows); }

        [Test]
        public void NoAcceptableRowGivesNullAndZero()
        {
            var none = Quests(Row(1, false, 5, 5), Row(2, false, 6, 6));
            Assert.IsNotNull(none, "rig: the view was delivered");
            Assert.IsNull(QuestOffer.Pick(none, 0, 0, out int reach));
            Assert.AreEqual(0, reach, "no CanAccept row in reach");

            var empty = Quests();
            Assert.IsNull(QuestOffer.Pick(empty, 0, 0, out reach));
            Assert.AreEqual(0, reach, "an empty view");

            var unavailable = Quests(false);
            Assert.IsNotNull(unavailable, "rig: an unavailable view is still a view object");
            Assert.IsFalse(unavailable.Available, "rig: the view is unavailable");
            Assert.IsNull(QuestOffer.Pick(unavailable, 0, 0, out reach));
            Assert.AreEqual(0, reach, "an unavailable view");

            Assert.IsNull(QuestOffer.Pick(null, 0, 0, out reach));
            Assert.AreEqual(0, reach, "a null view");

            // the empty answers above are not a constant: the same rows with one acceptable give an offer
            var one = Quests(Row(1, false, 5, 5), Row(2, true, 6, 6));
            var pick = QuestOffer.Pick(one, 0, 0, out reach);
            Assert.IsNotNull(pick, "the same rows with one CanAccept row pick it");
            Assert.AreEqual(1, reach);
        }

        [Test]
        public void OneAcceptableRowAmongOthersIsPickedAndOnlyAcceptableRowsAreCounted()
        {
            var view = Quests(Row(1, false, 1, 1), Row(2, true, 100, 100), Row(3, false, 2, 2));
            var pick = QuestOffer.Pick(view, 0, 0, out int reach);
            Assert.IsNotNull(pick, "the one CanAccept row is picked however far it is");
            Assert.AreEqual(2u, pick.Handle.Value);
            Assert.AreEqual(1, reach, "inReach counts CanAccept rows only");
        }

        [Test]
        public void TheNearerGiverWinsAndEqualDistancesKeepTheFirstRow()
        {
            var view = Quests(Row(1, true, 50, 0), Row(2, true, 10, 0), Row(3, true, 30, 0));
            var pick = QuestOffer.Pick(view, 0, 0, out int reach);
            Assert.AreEqual(3, reach);
            Assert.IsNotNull(pick);
            Assert.AreEqual(2u, pick.Handle.Value, "squared distance 100 beats 900 and 2500");

            var tie = Quests(Row(4, true, 10, 0), Row(5, true, 0, 10), Row(6, true, 6, 8));
            pick = QuestOffer.Pick(tie, 0, 0, out reach);
            Assert.AreEqual(3, reach);
            Assert.IsNotNull(pick);
            Assert.AreEqual(4u, pick.Handle.Value, "equal distances: the first of Rows stays");

            var laterNearer = Quests(Row(7, true, 10, 0), Row(8, true, 9, 0));
            pick = QuestOffer.Pick(laterNearer, 0, 0, out reach);
            Assert.IsNotNull(pick);
            Assert.AreEqual(8u, pick.Handle.Value, "a strictly nearer later row replaces the first");

            var away = Quests(Row(9, true, 100, 100), Row(10, true, 101, 100));
            pick = QuestOffer.Pick(away, 100, 100, out reach);
            Assert.IsNotNull(pick);
            Assert.AreEqual(9u, pick.Handle.Value, "distance is measured from the given point, not the origin");
        }

        [Test]
        public void NaNPositionsNeverThrowAndPickTheFirstAcceptableRow()
        {
            var view = Quests(Row(1, false, 0, 0), Row(2, true, 10, 0), Row(3, true, 5, 0));
            QuestRowView pick = null;
            int reach = -1;
            Assert.DoesNotThrow(() => pick = QuestOffer.Pick(view, double.NaN, double.NaN, out reach), "a NaN position");
            Assert.IsNotNull(pick, "a NaN position still picks");
            Assert.AreEqual(2u, pick.Handle.Value, "every distance is NaN: the first acceptable row stays");
            Assert.AreEqual(2, reach);

            Assert.DoesNotThrow(() => pick = QuestOffer.Pick(view, double.NaN, 0, out reach), "a NaN x");
            Assert.IsNotNull(pick);
            Assert.AreEqual(2u, pick.Handle.Value, "a NaN x gives NaN distances too: the first acceptable row stays");
            Assert.AreEqual(2, reach);
        }

        [Test]
        public void FindReturnsTheRowOfAHandleAndNullOtherwise()
        {
            var view = Quests(Row(1, false), Row(2, true), Row(3, false));
            var row = QuestOffer.Find(view, new QuestHandle(3));
            Assert.IsNotNull(row, "a row that cannot be accepted is still found");
            Assert.AreEqual(3u, row.Handle.Value);
            Assert.AreSame(view.Rows[1], QuestOffer.Find(view, new QuestHandle(2)));
            Assert.IsNull(QuestOffer.Find(view, new QuestHandle(99)), "an unknown handle");
            Assert.IsNull(QuestOffer.Find(null, new QuestHandle(2)), "a null view");
            var unavailable = Quests(false);
            Assert.IsFalse(unavailable.Available, "rig: the view is unavailable");
            Assert.IsNull(QuestOffer.Find(unavailable, new QuestHandle(2)), "an unavailable view");
        }

        [Test]
        public void LiftKeepsTheCardAboveTheFloorAndBelowTheTracker()
        {
            Assert.AreEqual(200f, QuestOffer.Lift(200f, 100f, 150f, 400f), 1e-4, "a region ending above the floor is unchanged");
            Assert.AreEqual(300f, QuestOffer.Lift(300f, 100f, 150f, 400f), 1e-4, "ending exactly at the floor is unchanged");
            Assert.AreEqual(300f, QuestOffer.Lift(330f, 100f, 150f, 400f), 1e-4, "overrunning by 30 with room ends at the floor");
            Assert.AreEqual(350f, QuestOffer.Lift(330f, 100f, 350f, 400f), 1e-4, "less room than its height: the top is clear");
            Assert.AreEqual(350f, QuestOffer.Lift(330f, 100f, 350f, 420f), 1e-4, "floor - height 320 is above clear 350: clear");
            Assert.AreEqual(77f, QuestOffer.Lift(77f, 100f, float.NaN, 400f), 1e-4, "NaN clear gives the region top");
            Assert.AreEqual(77f, QuestOffer.Lift(77f, float.NaN, 50f, 400f), 1e-4, "NaN height gives the region top");
            Assert.AreEqual(77f, QuestOffer.Lift(77f, 100f, 50f, float.NaN), 1e-4, "NaN floor gives the region top");
            Assert.AreEqual(float.NaN, QuestOffer.Lift(float.NaN, 100f, 50f, 400f), "NaN top gives the region top (NaN)");
        }
    }
}
