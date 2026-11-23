using System;
using System.Collections.Generic;
using NUnit.Framework;
using Roguehalla.Net.Protocol;
using Roguehalla.Net.Ui;

namespace Roguehalla.Net.Tests
{
    /// <summary>P-UI-QUEST-TRACKER-1 oracle (engine-free): which accepted quests the HUD quest tracker lists, in which order, the seconds
    /// left, and the texts of a row. Pure and presentation only: every input is a delivered fact.</summary>
    public sealed class QuestTrackerTests
    {
        static WireQuestRow Row(uint handle, bool accepted = true, bool completed = false, bool ready = false, QuestRowState state = QuestRowState.Open,
            double expiresAt = 0, string def = "barrow_run", uint progress = 0, string name = "N", string objective = "O")
        {
            return new WireQuestRow
            {
                Handle = new QuestHandle(handle), Def = def, GiverX = 1, GiverY = 2, State = state, ExpiresAt = expiresAt,
                Name = name, GiverLabel = "G", Brief = "B", Objective = objective, Reward = "R",
                Participants = new[] { new WireQuestParticipant { Id = new RosterId(7), Accepted = accepted } },
                OwnerAccepted = accepted, OwnerCompleted = completed, OwnerReady = ready, OwnerProgress = progress,
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

        static List<uint> Handles(List<QuestTrackerRow> rows)
        {
            var list = new List<uint>();
            foreach (var r in rows) list.Add(r.Quest.Handle.Value);
            return list;
        }

        [Test]
        public void OnlyRunningAcceptedQuestsAreListed()
        {
            var view = Quests(
                Row(1, accepted: false),                        // offered
                Row(2, completed: true),                        // completed by the owner
                Row(3, state: QuestRowState.Expired),           // expired by the server
                Row(4, state: QuestRowState.Done),              // done
                Row(5, state: QuestRowState.Preview),           // preview
                Row(6, expiresAt: 900),                         // accepted, Open, deadline passed: stays with 0 s
                Row(7, expiresAt: 61000));                      // accepted, Open, running
            var rows = new List<QuestTrackerRow> { new QuestTrackerRow(null, 99) };
            int running = QuestTracker.Build(view, 1000, rows);
            Assert.AreEqual(2, running, "two accepted quests are running");
            CollectionAssert.AreEqual(new uint[] { 6, 7 }, Handles(rows), "the old row was cleared; soonest deadline first");
            Assert.AreEqual(0, rows[0].SecondsLeft, "a passed deadline stays listed with 0 seconds");
            Assert.AreEqual(60, rows[1].SecondsLeft);
        }

        [Test]
        public void ReadyFirstThenSoonestDeadlineThenUntimed()
        {
            var view = Quests(
                Row(1, expiresAt: 20000),
                Row(2, ready: true),                       // ready, untimed
                Row(3),                                    // untimed
                Row(4, ready: true, expiresAt: 50000),
                Row(5, ready: true, expiresAt: 30000),
                Row(6, expiresAt: 20000));
            var rows = new List<QuestTrackerRow>();
            int running = QuestTracker.Build(view, 1000, rows);
            Assert.AreEqual(6, running, "the count is every running quest, not the rows shown");
            CollectionAssert.AreEqual(new uint[] { 5, 4, 2 }, Handles(rows), "ready by deadline, a ready untimed row last of the ready; only three rows");

            var timed = Quests(Row(1, expiresAt: 20000), Row(2, expiresAt: 20000), Row(3), Row(4, expiresAt: 10000));
            QuestTracker.Build(timed, 1000, rows);
            CollectionAssert.AreEqual(new uint[] { 4, 1, 2 }, Handles(rows), "soonest first, ties in view order");

            var untimed = Quests(Row(1), Row(2, ready: true), Row(3, ready: true), Row(4));
            QuestTracker.Build(untimed, 1000, rows);
            CollectionAssert.AreEqual(new uint[] { 2, 3, 1 }, Handles(rows), "ready untimed before untimed, ties in view order");
        }

        [Test]
        public void SecondsLeft()
        {
            var view = Quests(Row(1, expiresAt: 135000), Row(2, expiresAt: 1100), Row(3, expiresAt: 900), Row(4, expiresAt: 0));
            var rows = new List<QuestTrackerRow>();
            QuestTracker.Build(view, 1000, rows);
            var byHandle = new Dictionary<uint, int>();
            foreach (var r in rows) byHandle[r.Quest.Handle.Value] = r.SecondsLeft;
            Assert.AreEqual(3, byHandle.Count, "three rows");
            Assert.AreEqual(134, byHandle[1], "134,000 ms left");
            Assert.AreEqual(1, byHandle[2], "100 ms left rounds up");
            Assert.AreEqual(0, byHandle[3], "a passed deadline");
            QuestTracker.Build(Quests(Row(4, expiresAt: 0)), 1000, rows);
            Assert.AreEqual(1, rows.Count);
            Assert.AreEqual(-1, rows[0].SecondsLeft, "no deadline");
        }

        [Test]
        public void Clock()
        {
            Assert.AreEqual("", QuestTracker.Clock(-1));
            Assert.AreEqual("2:14", QuestTracker.Clock(134));
            Assert.AreEqual("0:09", QuestTracker.Clock(9));
            Assert.AreEqual("0:00", QuestTracker.Clock(0));
            Assert.AreEqual("62:05", QuestTracker.Clock(3725));
        }

        [Test]
        public void Progress()
        {
            var view = Quests(Row(1, ready: true, def: "hold_the_fort", progress: 45), Row(2, def: "hold_the_fort", progress: 12),
                Row(3, def: "stay_a_while", progress: 7), Row(4, def: "barrow_run", progress: 3), Row(5, def: "barrow_run", progress: 0));
            var rows = new Dictionary<uint, QuestRowView>();
            foreach (var r in view.Rows) rows[r.Handle.Value] = r;
            Assert.AreEqual("Turn in", QuestTracker.Progress(rows[1]));
            Assert.AreEqual("12 / 45 s", QuestTracker.Progress(rows[2]));
            Assert.AreEqual("7 / 30 s", QuestTracker.Progress(rows[3]));
            Assert.AreEqual("3", QuestTracker.Progress(rows[4]));
            Assert.AreEqual("", QuestTracker.Progress(rows[5]));
            Assert.AreEqual(45, QuestTracker.DwellSeconds("hold_the_fort"));
            Assert.AreEqual(30, QuestTracker.DwellSeconds("stay_a_while"));
            Assert.AreEqual(0, QuestTracker.DwellSeconds("barrow_run"));
            Assert.AreEqual(0, QuestTracker.DwellSeconds(null));
        }

        [Test]
        public void Header()
        {
            Assert.AreEqual("J · log", QuestTracker.Header(2, 2));
            Assert.AreEqual("+2 more · J", QuestTracker.Header(5, 3));
        }

        [Test]
        public void SameFace()
        {
            var view = Quests(Row(1, expiresAt: 100000), Row(2, expiresAt: 100000));
            var a = view.Rows[0]; var b = view.Rows[1];
            Assert.IsTrue(new QuestTrackerRow(a, 30).SameFace(new QuestTrackerRow(a, 30)));
            Assert.IsFalse(new QuestTrackerRow(a, 30).SameFace(new QuestTrackerRow(a, 29)), "the seconds differ");
            Assert.IsFalse(new QuestTrackerRow(a, 30).SameFace(new QuestTrackerRow(b, 30)), "the quest object differs");
        }

        [Test]
        public void ANullOrUnavailableViewGivesNothing()
        {
            var rows = new List<QuestTrackerRow> { new QuestTrackerRow(null, 5) };
            Assert.AreEqual(0, QuestTracker.Build(null, 1000, rows));
            Assert.AreEqual(0, rows.Count, "null view: the list is cleared");
            rows.Add(new QuestTrackerRow(null, 5));
            var unavailable = Quests(false);
            Assert.IsNotNull(unavailable);
            Assert.IsFalse(unavailable.Available);
            Assert.AreEqual(0, QuestTracker.Build(unavailable, 1000, rows));
            Assert.AreEqual(0, rows.Count, "unavailable view: the list is cleared");
        }

        [Test]
        public void Short()
        {
            string atLimit = new string('a', 10);
            Assert.AreSame(atLimit, QuestTracker.Short(atLimit, 10), "nothing cut: the same string object");
            Assert.AreEqual("abcd…", QuestTracker.Short("abcdefghij", 5), "maxChars characters in all, ending in the ellipsis");
            Assert.AreEqual("ab…", QuestTracker.Short("ab cdefgh", 4), "no space before the ellipsis");
            Assert.AreEqual(26, QuestTracker.Short("The Long Road To The Northern Watchtowers", QuestTracker.NameChars).Length);
            Assert.AreEqual("", QuestTracker.Short(null, 10));
        }

        [Test]
        public void RowsThatFit()
        {
            Assert.AreEqual(3, QuestTracker.RowsThatFit(166, 344));
            Assert.AreEqual(2, QuestTracker.RowsThatFit(232, 344));
            Assert.AreEqual(0, QuestTracker.RowsThatFit(467, 344));
            Assert.AreEqual(3, QuestTracker.RowsThatFit(467, 618));
            Assert.AreEqual(2, QuestTracker.RowsThatFit(467, 580));
            Assert.AreEqual(0, QuestTracker.RowsThatFit(float.NaN, 344));
            Assert.AreEqual(0, QuestTracker.RowsThatFit(166, float.NaN));
            Assert.AreEqual(HudRegions.TrackerMaxRows, QuestTracker.MaxRows);
        }
    }
}
