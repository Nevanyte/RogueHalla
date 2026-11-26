using System;
using System.Collections.Generic;
using NUnit.Framework;
using Roguehalla.Client;
using Roguehalla.Net;
using Roguehalla.Net.Protocol;
using TMPro;
using UnityEngine;

namespace Roguehalla.Client.Tests
{
    /// <summary>P-UI-JOURNAL-POLISH-1 oracle: the journal leads with the live facts in the game's words, shows the objective before
    /// the brief and hides a brief that repeats it, cannot send Accept twice, says where to go for an offer out of reach, keeps the
    /// selected row marked and names the state on each list row. Presentation only: every fact is a delivered one.</summary>
    public sealed class QuestJournalPolishEditModeTests
    {
        sealed class TestTransport : ITransport
        {
            public event Action Opened;
            public event Action<byte[]> Received;
            public event Action<string> Closed;
            public readonly List<byte[]> Sent = new List<byte[]>();
            public void Connect(string url) { }
            public void Send(byte[] frame) => Sent.Add(frame);
            public void Close() => Closed?.Invoke("closed");
            public void Poll() { }
            public void Open() => Opened?.Invoke();
            public void Deliver(ServerMsg message) => Received?.Invoke(message.Encode());
        }

        const string Giver = "Wanderer & Co.";
        GameObject _canvas, _host;
        QuestJournalView _view;
        Session _session;
        TestTransport _transport;
        readonly QuestHandle _first = new QuestHandle(71), _second = new QuestHandle(72), _third = new QuestHandle(73);
        int _tick;

        [SetUp]
        public void SetUp()
        {
            _canvas = new GameObject("Quest journal polish canvas", typeof(RectTransform), typeof(Canvas), typeof(UnityEngine.UI.GraphicRaycaster));
            _host = new GameObject("Quest journal polish host");
            _view = _host.AddComponent<QuestJournalView>();
            _view.Initialize(_canvas.transform);
            _view.SetVisible(true);
            _transport = new TestTransport(); _session = NewSession(_transport, 1);
            _view.Bind(_session);
            _tick = 20;
        }

        [TearDown]
        public void TearDown()
        {
            if (_session != null) _session.Leave();
            if (_host != null) UnityEngine.Object.DestroyImmediate(_host);
            if (_canvas != null) UnityEngine.Object.DestroyImmediate(_canvas);
        }

        [Test]
        public void AcceptCannotBeSentTwice()
        {
            Show(Offer(_first));
            var accept = Button("QuestDetail/AcceptQuest");
            accept.onClick.Invoke();
            Assert.IsTrue(accept.gameObject.activeSelf, "the button stays visible while the accept is pending");
            Assert.IsFalse(accept.interactable, "the button cannot be pressed again");
            Assert.AreEqual("ACCEPTING…", ButtonLabel("AcceptQuest"));
            Assert.AreEqual("Accepting…", Status());

            accept.onClick.Invoke();
            Assert.IsFalse(_view.TryAccept(_first), "a second accept of the same quest is refused locally");
            Assert.AreEqual("Accepting…", Status());
            _session.FlushInput();
            Assert.AreEqual(1, AcceptCommands(), "exactly one QuestAccept reaches the transport");
        }

        [Test]
        public void ANewQuestViewEndsThePendingState()
        {
            Show(Offer(_first));
            var accept = Button("QuestDetail/AcceptQuest");
            accept.onClick.Invoke();
            Assert.AreEqual("ACCEPTING…", ButtonLabel("AcceptQuest"), "rig: the accept is pending");
            Assert.IsFalse(accept.interactable, "rig: the accept is pending");

            var changed = Offer(_first); changed.State = QuestRowState.Preview;
            Deliver(changed); _view.Refresh();
            Assert.IsTrue(accept.gameObject.activeSelf);
            Assert.AreEqual("ACCEPT QUEST", ButtonLabel("AcceptQuest"));
            Assert.IsTrue(accept.interactable, "a new quest view clears the pending state");

            accept.onClick.Invoke();
            Assert.AreEqual("ACCEPTING…", ButtonLabel("AcceptQuest"));
            _session.FlushInput();
            Assert.AreEqual(2, AcceptCommands(), "after the view arrived a second command is legitimate");

            var accepted = Offer(_first); accepted.CanAccept = false; accepted.OwnerAccepted = true; accepted.OwnerReady = false;
            Deliver(accepted); _view.Refresh();
            Assert.IsFalse(accept.gameObject.activeSelf, "an accepted quest has no accept button");
            StringAssert.StartsWith("Accepted", Status());
        }

        [Test]
        public void TheStatusLineLeadsWithTheLiveFacts()
        {
            _transport.Deliver(new ServerMsg.Snapshot { Tick = 20, Phase = 1 }); _tick = 21;
            var dwell = Row(_first, "Hold", "b", "o", "r", false, true);
            dwell.Def = "hold_the_fort"; dwell.OwnerProgress = 12; dwell.OwnerReady = false; dwell.State = QuestRowState.Open; dwell.ExpiresAt = 135000;
            Show(dwell);
            Assert.AreEqual("Accepted · 12 / 45 s · 2:14 left", Status());
            var label = TextAt("QuestDetail/QuestStatus");
            Assert.AreEqual((Color)ShowcasePalette.Text, label.color, "a quest the player holds is bright");
            Assert.AreEqual(22.5f, label.GetComponent<MythicTextScale>().BaseSize, 0.001f, "the status line is 15 units (22.5 base size), was 13");

            var offer = Offer(_first); offer.ExpiresAt = 135000;
            Deliver(offer); _view.Refresh();
            Assert.AreEqual("Offer · 2:14 left", Status());
            Assert.AreEqual((Color)ShowcasePalette.MutedText, label.color, "an offer is muted");

            var expired = Offer(_first); expired.State = QuestRowState.Expired; expired.CanAccept = false; expired.ExpiresAt = 135000;
            Deliver(expired); _view.Refresh();
            Assert.AreEqual("Expired", Status());
        }

        [Test]
        public void TheObjectiveComesFirstAndTheRepeatedBriefIsHidden()
        {
            var same = Offer(_first); same.Brief = "Hold the ground."; same.Objective = "Hold the ground.";
            Show(same);
            const string content = "QuestDetail/QuestTextScroll/Viewport/Content/";
            var objective = Find(content + "QuestObjective"); var giver = Find(content + "QuestGiver"); var brief = Find(content + "QuestBrief");
            var name = Find(content + "QuestName");
            Assert.Less(name.GetSiblingIndex(), objective.GetSiblingIndex(), "the name leads");
            Assert.Less(objective.GetSiblingIndex(), giver.GetSiblingIndex(), "the objective is above the giver");
            Assert.Less(objective.GetSiblingIndex(), brief.GetSiblingIndex(), "the objective is above the brief");
            Assert.Less(giver.GetSiblingIndex(), brief.GetSiblingIndex(), "the giver is above the brief");
            Assert.AreEqual((Color)ShowcasePalette.Text, TextAt(content + "QuestObjective/Value").color, "the objective is bright");
            Assert.IsFalse(brief.gameObject.activeSelf, "a brief that repeats the objective is hidden");
            Assert.AreEqual("Hold the ground.", TextAt(content + "QuestBrief/Value").text, "its text is still written verbatim");

            var different = Offer(_first); different.Brief = "A long story about the ground."; different.Objective = "Hold the ground.";
            Deliver(different); _view.Refresh();
            Assert.IsTrue(brief.gameObject.activeSelf, "a different brief is shown");
            Assert.AreEqual((Color)ShowcasePalette.MutedText, TextAt(content + "QuestBrief/Value").color, "the brief is muted");
            Assert.AreEqual("A long story about the ground.", TextAt(content + "QuestBrief/Value").text);
        }

        [Test]
        public void AnOfferOutOfReachSaysWhereToGo()
        {
            var far = Offer(_first); far.CanAccept = false;
            DeliverMe(201); Show(far);
            var accept = Find("QuestDetail/AcceptQuest").gameObject;
            var hint = Find("QuestDetail/AcceptHint");
            Assert.IsFalse(accept.activeSelf, "no accept button for an offer that cannot be accepted");
            Assert.IsTrue(hint.gameObject.activeSelf, "the hint takes its place");
            Assert.AreEqual("Go to Wanderer & Co. to accept", hint.GetComponent<TMP_Text>().text);

            DeliverMe(1); _view.Refresh();
            Assert.IsFalse(accept.activeSelf);
            Assert.IsTrue(hint.gameObject.activeSelf);
            Assert.AreEqual("Cannot be accepted right now", hint.GetComponent<TMP_Text>().text);

            Deliver(Offer(_first)); _view.Refresh();
            Assert.IsTrue(accept.activeSelf, "an offer that can be accepted shows the button");
            Assert.IsFalse(hint.gameObject.activeSelf, "and no hint");
        }

        [Test]
        public void TheSelectedRowStaysMarked()
        {
            Show(Row(_first, "One", "b", "o", "r", false), Row(_second, "Two", "b", "o", "r", false), Row(_third, "Three", "b", "o", "r", false));
            _view.SelectQuest(_second);
            AssertMarked(1, "after SelectQuest of the second");

            Deliver(Row(_first, "One", "b", "o", "r", false), Row(_second, "Two", "b", "o", "r", false), Row(_third, "Three", "b", "o", "r", false));
            _view.Refresh();
            AssertMarked(1, "after a new quest view");

            _view.SelectQuest(_first);
            AssertMarked(0, "after SelectQuest of the first");
        }

        [Test]
        public void TheListRowSaysOfferHereOnlyInReach()
        {
            var here = Offer(_first);
            var offer = Offer(_second); offer.CanAccept = false;
            var accepted = Row(_third, "Three", "b", "o", "r", false, true); accepted.OwnerReady = false; accepted.State = QuestRowState.Open;
            var turnIn = Row(new QuestHandle(74), "Four", "b", "o", "r", false, true); turnIn.State = QuestRowState.Open;
            var complete = Row(new QuestHandle(75), "Five", "b", "o", "r", false, true); complete.State = QuestRowState.Open; complete.OwnerCompleted = true;
            Show(here, offer, accepted, turnIn, complete);
            Assert.AreEqual("OFFER · HERE  ·  " + Giver, RowSecondLine(0));
            Assert.AreEqual("OFFER  ·  " + Giver, RowSecondLine(1));
            Assert.AreEqual("ACCEPTED  ·  " + Giver, RowSecondLine(2));
            Assert.AreEqual("TURN IN  ·  " + Giver, RowSecondLine(3));
            Assert.AreEqual("COMPLETE  ·  " + Giver, RowSecondLine(4));
        }

        void AssertMarked(int selected, string when)
        {
            for (int i = 0; i < 3; i++)
            {
                var button = Button("QuestRows/Viewport/Content/QuestRow" + i);
                Color expected = i == selected ? (Color)UiTheme.Current.SurfaceSelected : (Color)UiTheme.Current.SurfaceRaised;
                Assert.AreEqual(expected, button.colors.normalColor, "row " + i + " " + when);
            }
        }

        // ---- helpers (the pattern of QuestJournalViewEditModeTests) ----

        static Session NewSession(TestTransport transport, byte id)
        {
            var session = new Session(transport, new SessionOptions { Url = "ws://quest-test/ws", Profile = "quest-test", Now = () => 1000 });
            session.Connect(); transport.Open();
            var match = new byte[16]; match[0] = id;
            transport.Deliver(new ServerMsg.Welcome { Protocol = Wire.ProtocolVersion, MatchId = match, Session = new byte[16],
                PlayerId = 1, TickRate = 20, Map = new MapBlob(2, 2, new byte[4]) });
            return session;
        }

        /// <summary>Delivers the rows, refreshes and selects the first.</summary>
        void Show(params WireQuestRow[] rows)
        {
            Deliver(rows); _view.Refresh(); _view.SelectQuest(rows[0].Handle);
        }

        void Deliver(params WireQuestRow[] rows)
        {
            _transport.Deliver(new ServerMsg.State { Tick = 1, Items = new StateItem[] { new QuestsState { Available = true, View = new WireQuestView { Rows = rows } } } });
        }

        void DeliverMe(float x)
        {
            _transport.Deliver(new ServerMsg.Snapshot { Tick = (uint)(++_tick), Phase = 1, Me = new WireSelf { Id = 1, X = x, Y = 2, Hp = 100, MaxHp = 100, Status = 1, Weapon = 0 } });
        }

        /// <summary>An open offer the player can accept (a pending Preview row is the helper's default).</summary>
        static WireQuestRow Offer(QuestHandle handle)
        {
            var row = Row(handle, "Offer", "brief", "objective", "reward", true);
            row.State = QuestRowState.Open;
            return row;
        }

        static WireQuestRow Row(QuestHandle handle, string name, string brief, string objective, string reward, bool canAccept, bool accepted = false)
        {
            return new WireQuestRow { Handle = handle, Def = "test", GiverX = 1, GiverY = 2, State = accepted ? QuestRowState.Open : QuestRowState.Preview,
                ExpiresAt = 12345.5, Participants = Array.Empty<WireQuestParticipant>(), DoneBy = null,
                CanAccept = canAccept, OwnerAccepted = accepted, OwnerCompleted = false, OwnerReady = accepted, OwnerProgress = accepted ? 2u : 0u,
                OwnerVisited = 0, Name = name, GiverLabel = Giver, Brief = brief, Objective = objective, Reward = reward };
        }

        int AcceptCommands()
        {
            int count = 0;
            foreach (byte[] frame in _transport.Sent)
                if (ClientMsg.Decode(frame) is ClientMsg.Input input)
                    foreach (var command in input.Commands) if (command.Kind == CommandKind.QuestAccept) count++;
            return count;
        }

        Transform Find(string path)
        {
            var found = _canvas.transform.Find("QuestJournal/" + path);
            Assert.IsNotNull(found, "the journal has " + path);
            return found;
        }

        UnityEngine.UI.Button Button(string path) => Find(path).GetComponent<UnityEngine.UI.Button>();
        TMP_Text TextAt(string path) => Find(path).GetComponent<TMP_Text>();
        string Status() => TextAt("QuestDetail/QuestStatus").text;
        string ButtonLabel(string name) => TextAt("QuestDetail/" + name + "/Label").text;
        string RowSecondLine(int index) => TextAt("QuestRows/Viewport/Content/QuestRow" + index + "/QuestRowLabel").text.Split('\n')[1];
    }
}
