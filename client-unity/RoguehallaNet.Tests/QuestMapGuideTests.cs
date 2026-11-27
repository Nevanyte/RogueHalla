using System;
using NUnit.Framework;
using Roguehalla.Net.Protocol;
using Roguehalla.Net.Ui;

namespace Roguehalla.Net.Tests
{
    /// <summary>Quest map directions use only facts delivered in the viewer's quest state.</summary>
    public sealed class QuestMapGuideTests
    {
        static QuestView View(params WireQuestRow[] rows)
        {
            var transport = new FakeTransport();
            var session = new Session(transport, new SessionOptions { Url = "ws://x/ws", Profile = "p", Ticket = Array.Empty<byte>(), Now = () => 0 });
            session.Connect(); transport.Open();
            transport.Deliver(new ServerMsg.Welcome { Protocol = Wire.ProtocolVersion, MatchId = new byte[16], Session = new byte[16],
                PlayerId = 1, TickRate = 20, Map = new MapBlob(4, 4, new byte[16]) });
            transport.Deliver(new ServerMsg.State { Tick = 1, Items = new StateItem[]
            {
                new QuestsState { Available = true, View = new WireQuestView
                {
                    OwnerRoster = new RosterId(7),
                    Roster = new[] { new WireQuestRosterEntry { Id = new RosterId(7), Label = "owner" } },
                    Rows = rows,
                    Objectives = rows.Length == 0 ? Array.Empty<WireQuestObjective>() :
                        new[] { new WireQuestObjective { Quest = rows[0].Handle, X = 64, Y = 96 } },
                } },
            } });
            var result = session.Collections.Quests;
            session.Leave();
            return result;
        }

        static WireQuestRow Row(uint handle, string name, bool accepted, bool ready = false, string objective = "") => new WireQuestRow
        {
            Handle = new QuestHandle(handle), Def = "map_quest", GiverX = 1, GiverY = 2, State = QuestRowState.Open,
            Name = name, GiverLabel = "Mira", Brief = "", Objective = objective, Reward = "", OwnerAccepted = accepted, OwnerReady = ready,
        };

        [Test]
        public void DirectionsCoverOffersObjectivesAndTurnInsWithoutInventingUndeliveredMarks()
        {
            var text = QuestMapGuide.Copy(View(
                Row(1, "Gather", true, objective: "Collect five herbs"),
                Row(2, "Escort", true, ready: true),
                Row(3, "New contract", false, objective: "secret objective")));

            StringAssert.Contains("NEXT · Collect five herbs", text);
            StringAssert.Contains("MAP · 1 quest mark", text);
            StringAssert.Contains("TURN IN · Return to Mira", text);
            StringAssert.Contains("ACCEPT · Visit Mira", text);
            StringAssert.DoesNotContain("secret objective", text, "unaccepted quests do not expose their objective copy");
        }

        [Test]
        public void NoOpenQuestsAndUnavailableStateAreClear()
        {
            Assert.AreEqual("No open quests delivered.", QuestMapGuide.Copy(View(withState(Row(4, "Done", true, ready: true), QuestRowState.Done))));
            Assert.AreEqual("Quest directions arrive with the next server update.", QuestMapGuide.Copy(null));
        }

        static WireQuestRow withState(WireQuestRow row, QuestRowState state)
        {
            row.State = state;
            return row;
        }
    }
}
