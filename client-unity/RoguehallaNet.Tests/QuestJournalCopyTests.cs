using System;
using NUnit.Framework;
using Roguehalla.Net.Protocol;
using Roguehalla.Net.Ui;

namespace Roguehalla.Net.Tests
{
    /// <summary>P-UI-JOURNAL-POLISH-1 oracle (engine-free): the journal's words for a quest, the state on its list row,
    /// the status line, the reason an offer cannot be accepted, and whether the brief is worth printing.
    /// Pure and presentation only: every input is a delivered fact.</summary>
    public sealed class QuestJournalCopyTests
    {
        static WireQuestRow Row(uint handle, bool accepted = false, bool completed = false, bool ready = false, QuestRowState state = QuestRowState.Open,
            double expiresAt = 0, string def = "barrow_run", uint progress = 0, string objective = "O", string brief = "B",
            string giver = "Gallows quest giver", bool canAccept = false, double giverX = 1, double giverY = 2)
        {
            return new WireQuestRow
            {
                Handle = new QuestHandle(handle), Def = def, GiverX = giverX, GiverY = giverY, State = state, ExpiresAt = expiresAt,
                Name = "N", GiverLabel = giver, Brief = brief, Objective = objective, Reward = "R",
                Participants = new[] { new WireQuestParticipant { Id = new RosterId(7), Accepted = accepted } },
                CanAccept = canAccept, OwnerAccepted = accepted, OwnerCompleted = completed, OwnerReady = ready, OwnerProgress = progress,
            };
        }

        static QuestRowView Quest(WireQuestRow row)
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
                Rows = new[] { row }, Objectives = new WireQuestObjective[0],
            };
            transport.Deliver(new ServerMsg.State { Tick = 1, Items = new StateItem[] { new QuestsState { Available = true, View = view } } });
            return session.Collections.Quests.Rows[0];
        }

        /// <summary>A row the way the game builds it from a wire row, but without the encoder: it refuses a null string, which
        /// the journal must still survive.</summary>
        static QuestRowView Raw(WireQuestRow row)
        {
            return (QuestRowView)Activator.CreateInstance(typeof(QuestRowView), System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic,
                null, new object[] { row }, null);
        }

        [Test]
        public void TheRowStateOfEachKindOfQuest()
        {
            Assert.AreEqual("COMPLETE", QuestJournalCopy.RowState(Quest(Row(1, accepted: true, completed: true, ready: true))));
            Assert.AreEqual("EXPIRED", QuestJournalCopy.RowState(Quest(Row(2, accepted: true, ready: true, state: QuestRowState.Expired))),
                "an accepted ready quest that expired is not a turn-in");
            Assert.AreEqual("DONE", QuestJournalCopy.RowState(Quest(Row(3, accepted: true, state: QuestRowState.Done))));
            Assert.AreEqual("TURN IN", QuestJournalCopy.RowState(Quest(Row(4, accepted: true, ready: true))));
            Assert.AreEqual("ACCEPTED", QuestJournalCopy.RowState(Quest(Row(5, accepted: true))));
            Assert.AreEqual("PREVIEW", QuestJournalCopy.RowState(Quest(Row(6, state: QuestRowState.Preview, canAccept: true))));
            Assert.AreEqual("OFFER · HERE", QuestJournalCopy.RowState(Quest(Row(7, canAccept: true))));
            Assert.AreEqual("OFFER", QuestJournalCopy.RowState(Quest(Row(8))));
        }

        [Test]
        public void TheStatusLineOfEachCase()
        {
            // the ten rows of the table, no deadline
            Assert.AreEqual("Accepting…", QuestJournalCopy.Status(Quest(Row(1, accepted: true)), true, false, -1));
            Assert.AreEqual("Choose your reward", QuestJournalCopy.Status(Quest(Row(2, accepted: true)), false, true, -1));
            Assert.AreEqual("Completed", QuestJournalCopy.Status(Quest(Row(3, accepted: true, completed: true)), false, false, 134));
            Assert.AreEqual("Expired", QuestJournalCopy.Status(Quest(Row(4, accepted: true, state: QuestRowState.Expired)), false, false, 134));
            Assert.AreEqual("Finished", QuestJournalCopy.Status(Quest(Row(5, state: QuestRowState.Done)), false, false, 134));
            Assert.AreEqual("Ready to turn in", QuestJournalCopy.Status(Quest(Row(6, accepted: true, ready: true)), false, false, -1));
            Assert.AreEqual("Accepted · 2 progress", QuestJournalCopy.Status(Quest(Row(7, accepted: true, progress: 2)), false, false, -1));
            Assert.AreEqual("Preview", QuestJournalCopy.Status(Quest(Row(8, state: QuestRowState.Preview)), false, false, -1));
            Assert.AreEqual("Offer", QuestJournalCopy.Status(Quest(Row(9, canAccept: true)), false, false, -1));
            Assert.AreEqual("Offer · not in reach", QuestJournalCopy.Status(Quest(Row(10)), false, false, -1));

            // progress in seconds for a dwell quest, the time left after it
            Assert.AreEqual("Accepted · 12 / 35 s · 2:14 left",
                QuestJournalCopy.Status(Quest(Row(11, accepted: true, def: "hold_the_fort", progress: 12)), false, false, 134));
            Assert.AreEqual("Accepted · 5 / 30 s", QuestJournalCopy.Status(Quest(Row(12, accepted: true, def: "stay_a_while", progress: 5)), false, false, -1));
            Assert.AreEqual("Accepted · 2 progress · 0:09 left", QuestJournalCopy.Status(Quest(Row(13, accepted: true, progress: 2)), false, false, 9));

            // the time: none, up, running, on every case that takes it
            Assert.AreEqual("Offer", QuestJournalCopy.Status(Quest(Row(14, canAccept: true)), false, false, -1), "no deadline adds nothing");
            Assert.AreEqual("Offer · time is up", QuestJournalCopy.Status(Quest(Row(15, canAccept: true)), false, false, 0), "a deadline of 0 never prints 0:00");
            Assert.AreEqual("Offer · 2:14 left", QuestJournalCopy.Status(Quest(Row(16, canAccept: true)), false, false, 134));
            Assert.AreEqual("Preview · 1:00 left", QuestJournalCopy.Status(Quest(Row(17, state: QuestRowState.Preview)), false, false, 60));
            Assert.AreEqual("Offer · not in reach · time is up", QuestJournalCopy.Status(Quest(Row(18)), false, false, 0));
            Assert.AreEqual("Ready to turn in · 0:30 left", QuestJournalCopy.Status(Quest(Row(19, accepted: true, ready: true)), false, false, 30));

            // the order of the cases
            Assert.AreEqual("Accepting…", QuestJournalCopy.Status(Quest(Row(20, accepted: true, ready: true)), true, true, 134), "acceptPending wins over everything");
            Assert.AreEqual("Choose your reward", QuestJournalCopy.Status(Quest(Row(21, accepted: true, completed: true)), false, true, 134),
                "rewardPending wins over Completed");
        }

        [Test]
        public void TheHintNamesTheGiverOnlyWhenTheQuestIsOutOfReach()
        {
            var offer = Quest(Row(1));
            Assert.AreEqual("Go to Gallows quest giver to accept", QuestJournalCopy.Hint(offer, 500));
            Assert.AreEqual("Go to Gallows quest giver to accept", QuestJournalCopy.Hint(offer, double.NaN));
            Assert.AreEqual("Go to Gallows quest giver to accept", QuestJournalCopy.Hint(offer, double.PositiveInfinity));
            Assert.AreEqual("Go to Gallows quest giver to accept", QuestJournalCopy.Hint(offer, 64.5));
            Assert.AreEqual("Cannot be accepted right now", QuestJournalCopy.Hint(offer, 64), "64 is within reach");
            Assert.AreEqual("Cannot be accepted right now", QuestJournalCopy.Hint(offer, 0));
            Assert.AreEqual(64d, QuestJournalCopy.GiverReach);

            Assert.AreEqual("Go to the quest giver to accept", QuestJournalCopy.Hint(Quest(Row(2, giver: "  ")), 500));
            Assert.AreEqual("Go to the quest giver to accept", QuestJournalCopy.Hint(Raw(Row(3, giver: null)), 500));

            Assert.AreEqual("", QuestJournalCopy.Hint(Quest(Row(4, canAccept: true)), 500), "an offer that can be accepted needs no reason");
            Assert.AreEqual("", QuestJournalCopy.Hint(Quest(Row(5, accepted: true)), 500), "an accepted quest");
            Assert.AreEqual("", QuestJournalCopy.Hint(Quest(Row(6, completed: true)), 500), "a completed quest");
            Assert.AreEqual("", QuestJournalCopy.Hint(Quest(Row(7, state: QuestRowState.Preview)), 500), "a preview");
            Assert.AreEqual("", QuestJournalCopy.Hint(Quest(Row(8, state: QuestRowState.Expired)), 500), "an expired quest");
            Assert.AreEqual("", QuestJournalCopy.Hint(Quest(Row(9, state: QuestRowState.Done)), 500), "a finished quest");
        }

        [Test]
        public void ABriefThatRepeatsTheObjectiveIsNotShown()
        {
            Assert.IsFalse(QuestJournalCopy.ShowsBrief(Quest(Row(1, brief: "Kill the Warden", objective: "Kill the Warden"))), "equal");
            Assert.IsFalse(QuestJournalCopy.ShowsBrief(Quest(Row(2, brief: "  Kill the Warden ", objective: "Kill the Warden  "))), "equal but for spaces");
            Assert.IsTrue(QuestJournalCopy.ShowsBrief(Quest(Row(3, brief: "The Warden troubles the road.", objective: "Kill the Warden"))), "different");
            Assert.IsTrue(QuestJournalCopy.ShowsBrief(Quest(Row(4, brief: "kill the warden", objective: "Kill the Warden"))), "ordinal: case matters");
            Assert.IsFalse(QuestJournalCopy.ShowsBrief(Raw(Row(5, brief: null, objective: "Kill the Warden"))), "null");
            Assert.IsFalse(QuestJournalCopy.ShowsBrief(Quest(Row(7, brief: "", objective: "Kill the Warden"))), "empty");
            Assert.IsFalse(QuestJournalCopy.ShowsBrief(Quest(Row(6, brief: "   ", objective: "Kill the Warden"))), "blank");
        }
    }
}
