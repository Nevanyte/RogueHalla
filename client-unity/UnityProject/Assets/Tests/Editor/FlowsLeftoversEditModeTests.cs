using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using Roguehalla.Client;
using Roguehalla.Net;
using Roguehalla.Net.Protocol;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Roguehalla.Client.Tests
{
    /// <summary>P-UI-FLOWS-LEFTOVERS-1 oracle, the journal: a field of the details is shown only when it has a value (an empty
    /// PARTICIPANTS, OBJECTIVE, GIVER or REWARD leaves no heading over nothing), the QUEST NAME has no heading and leads the
    /// details, and the order and the stack are unchanged. The journal is built hidden, delivered while hidden and opened after, as
    /// a match builds it. The canvas is 1920 x 1080 world units.</summary>
    public sealed class JournalFieldsEditModeTests
    {
        sealed class TestTransport : ITransport
        {
            public event Action Opened;
            public event Action<byte[]> Received;
            public event Action<string> Closed;
            public void Connect(string url) { }
            public void Send(byte[] frame) { }
            public void Close() => Closed?.Invoke("closed");
            public void Poll() { }
            public void Open() => Opened?.Invoke();
            public void Deliver(ServerMsg message) => Received?.Invoke(message.Encode());
        }

        const string Detail = "QuestDetail";
        const string Content = "QuestDetail/QuestTextScroll/Viewport/Content/";
        static readonly string[] FieldNames = { "QuestName", "QuestObjective", "QuestGiver", "QuestBrief", "QuestReward", "QuestParticipants" };
        static readonly RosterId Alice = new RosterId(11);

        GameObject _canvas, _host;
        QuestJournalView _view;
        Session _session;
        TestTransport _transport;
        readonly QuestHandle _first = new QuestHandle(71);

        [SetUp]
        public void SetUp()
        {
            _canvas = new GameObject("Journal fields canvas", typeof(RectTransform), typeof(Canvas), typeof(GraphicRaycaster));
            _canvas.GetComponent<Canvas>().renderMode = RenderMode.WorldSpace;
            var canvasRect = (RectTransform)_canvas.transform;
            canvasRect.sizeDelta = new Vector2(1920, 1080); canvasRect.position = Vector3.zero; canvasRect.localScale = Vector3.one;
            _host = new GameObject("Journal fields host");
            _view = _host.AddComponent<QuestJournalView>();
            _view.Initialize(_canvas.transform);
            _transport = new TestTransport(); _session = NewSession(_transport, 1);
            _view.Bind(_session);
        }

        [TearDown]
        public void TearDown()
        {
            if (_session != null) _session.Leave();
            if (_host != null) UnityEngine.Object.DestroyImmediate(_host);
            if (_canvas != null) UnityEngine.Object.DestroyImmediate(_canvas);
        }

        // ---------------------------------------------------------------- 1

        [Test]
        public void AHeldQuestWithNoParticipantsShowsNeitherHeadingNorValue()
        {
            var held = Row(_first, "Held", "b", "o", "r");
            Deliver(new[] { held }); Deliver(new[] { held });
            Assert.IsFalse(_view.Root.gameObject.activeSelf, "rig: the journal is still hidden");
            Open(); Select(_first);
            Assert.IsFalse(Field("QuestParticipants").gameObject.activeSelf, "the participants field is hidden");
            Assert.IsFalse(FieldLabel("QuestParticipants").gameObject.activeInHierarchy, "its heading is not shown");
            Assert.AreEqual("", Value("QuestParticipants").text, "its value is empty");
            foreach (var name in new[] { "QuestObjective", "QuestGiver", "QuestBrief", "QuestReward" })
                Assert.IsTrue(Field(name).gameObject.activeInHierarchy, name + " is shown");
        }

        // ---------------------------------------------------------------- 2

        [Test]
        public void AParticipantShowsHeadingAndLines()
        {
            var held = Row(_first, "Held", "b", "o", "r");
            held.Participants = new[] { new WireQuestParticipant { Id = Alice, Accepted = true, Progress = 3 } };
            var roster = new[] { new WireQuestRosterEntry { Id = Alice, Label = "Alice" } };
            Deliver(new[] { held }, null, roster, Alice); Deliver(new[] { held }, null, roster, Alice);
            Open(); Select(_first);
            Assert.IsTrue(Field("QuestParticipants").gameObject.activeInHierarchy, "the participants field is shown");
            Assert.IsTrue(FieldLabel("QuestParticipants").gameObject.activeInHierarchy, "its heading is shown");
            Assert.AreEqual("Alice · You · Accepted · Progress 3", Value("QuestParticipants").text);

            Deliver(new[] { Row(_first, "Held", "b", "o", "r") }, null, roster, Alice);
            Assert.IsFalse(Field("QuestParticipants").gameObject.activeSelf, "a delivery without participants hides the field");
            Assert.IsFalse(FieldLabel("QuestParticipants").gameObject.activeInHierarchy, "and its heading");
            Assert.AreEqual("", Value("QuestParticipants").text, "and its text is empty");

            Deliver(new[] { held }, null, roster, Alice);
            Assert.IsTrue(Field("QuestParticipants").gameObject.activeInHierarchy, "a delivery with a participant shows it again");
            Assert.IsTrue(FieldLabel("QuestParticipants").gameObject.activeInHierarchy, "with its heading");
            Assert.AreEqual("Alice · You · Accepted · Progress 3", Value("QuestParticipants").text);
        }

        // ---------------------------------------------------------------- 3

        [TestCase("objective", "QuestObjective", "QuestName", "QuestGiver")]
        [TestCase("giver", "QuestGiver", "QuestObjective", "QuestReward")]
        [TestCase("reward", "QuestReward", "QuestGiver", "QuestParticipants")]
        public void AnEmptyFieldHidesItsHeading(string which, string emptied, string above, string below)
        {
            var row = Row(_first, "Held", "b", "o", "r");
            if (which == "objective") row.Objective = "";
            if (which == "giver") row.GiverLabel = "";
            if (which == "reward") row.Reward = "";
            row.Brief = row.Objective; // the brief repeats the objective: BRIEF is hidden in all three cases
            row.Participants = new[] { new WireQuestParticipant { Id = Alice, Accepted = true, Progress = 3 } };
            var roster = new[] { new WireQuestRosterEntry { Id = Alice, Label = "Alice" } };
            Deliver(new[] { row }, null, roster, Alice); Deliver(new[] { row }, null, roster, Alice);
            Open(); Select(_first);
            Assert.IsFalse(Field(emptied).gameObject.activeSelf, emptied + " is hidden");
            Assert.IsFalse(FieldLabel(emptied).gameObject.activeInHierarchy, emptied + " has no heading");
            Assert.IsFalse(Field("QuestBrief").gameObject.activeSelf, "the brief is hidden (rig)");
            foreach (var name in new[] { "QuestObjective", "QuestGiver", "QuestReward", "QuestParticipants" })
            {
                if (name == emptied) continue;
                Assert.IsTrue(Field(name).gameObject.activeInHierarchy, name + " is shown");
                Assert.IsTrue(FieldLabel(name).gameObject.activeInHierarchy, name + " has its heading");
            }
            Assert.That(Local(Field(above)).yMin - Local(Field(below)).yMax, Is.EqualTo(10f).Within(.5f), above + " to " + below + " across the emptied field");
        }

        // ---------------------------------------------------------------- 4

        [Test]
        public void TheNameHasNoHeadingAndLeadsTheDetails()
        {
            var full = Row(_first, "Echo Quest", "a long story", "o", "r");
            full.Participants = new[] { new WireQuestParticipant { Id = Alice, Accepted = true, Progress = 3 } };
            var roster = new[] { new WireQuestRosterEntry { Id = Alice, Label = "Alice" } };
            Deliver(new[] { full }, null, roster, Alice); Deliver(new[] { full }, null, roster, Alice);
            Open();
            var content = Req(Content.TrimEnd('/'));
            Assert.AreEqual("Select a quest", Value("QuestName").text, "nothing selected: the guidance");
            Assert.AreEqual(0, ActiveHeadings(content).Count, "nothing selected: no heading is active");

            Select(_first);
            var label = _canvas.transform.Find("QuestJournal/" + Content + "QuestName/FieldLabel");
            Assert.IsTrue(label == null || !label.gameObject.activeInHierarchy, "the name has no heading");
            Assert.IsTrue(Value("QuestName").gameObject.activeInHierarchy, "the name is shown");
            Assert.AreEqual("Echo Quest", Value("QuestName").text);
            var first = new List<TMP_Text>(content.GetComponentsInChildren<TMP_Text>(false));
            Assert.Greater(first.Count, 0, "rig: the details have active text");
            if (first.Count > 0) Assert.AreSame(Value("QuestName"), first[0], "the first active text of the details is the name's value");
            CollectionAssert.AreEqual(new[] { "OBJECTIVE", "GIVER", "BRIEF", "REWARD", "PARTICIPANTS" }, ActiveHeadings(content), "the headings, in order");
        }

        // ---------------------------------------------------------------- 5 (standing guard)

        [Test]
        public void TheFieldOrderAndTheStackAreUnchanged()
        {
            var full = Row(_first, "Echo Quest", "a long story", "o", "r");
            full.Participants = new[] { new WireQuestParticipant { Id = Alice, Accepted = true, Progress = 3 } };
            var roster = new[] { new WireQuestRosterEntry { Id = Alice, Label = "Alice" } };
            Deliver(new[] { full }, null, roster, Alice); Deliver(new[] { full }, null, roster, Alice);
            Open(); Select(_first);
            for (int i = 1; i < FieldNames.Length; i++)
                Assert.Less(Field(FieldNames[i - 1]).GetSiblingIndex(), Field(FieldNames[i]).GetSiblingIndex(), FieldNames[i - 1] + " stands above " + FieldNames[i]);
            foreach (float scale in new[] { 1f, 1.5f })
            {
                MythicUi.ApplyTextScale(_view.Root, scale); Rebuild();
                RectTransform previous = null; float heights = 0f; int count = 0;
                foreach (var name in FieldNames)
                {
                    var field = Field(name);
                    if (!field.gameObject.activeSelf) continue;
                    count++; heights += Local(field).height;
                    if (previous != null)
                        Assert.That(Local(previous).yMin - Local(field).yMax, Is.EqualTo(10f).Within(.5f), previous.name + " to " + name + " at text size " + scale);
                    previous = field;
                }
                Assert.Greater(count, 1, "rig: several fields are active");
                Assert.That(DetailContent().rect.height, Is.EqualTo(heights + 10f * (count - 1) + 8f).Within(1f), "content height at text size " + scale);
            }
        }

        // ---------------------------------------------------------------- 6

        [Test]
        public void ASettledRefreshAllocatesNothing()
        {
            var row = Row(_first, "Held", "b", "o", "");
            Deliver(new[] { row }); Deliver(new[] { row });
            Open(); Select(_first);
            Assert.IsFalse(Field("QuestReward").gameObject.activeSelf, "set-up: the empty reward is hidden");
            Assert.IsFalse(Field("QuestParticipants").gameObject.activeSelf, "set-up: the empty participants are hidden");
            Action refresh = _view.Refresh;
            refresh();
            AllocationMeter.AssertNone(refresh, 200, "settled journal refresh");
        }

        // ================================================================ helpers

        Transform Req(string path)
        {
            var found = _canvas.transform.Find("QuestJournal/" + path);
            Assert.IsNotNull(found, "no object at QuestJournal/" + path);
            return found;
        }

        void Open() => _view.SetVisible(true);
        RectTransform Field(string name) => (RectTransform)Req(Content + name);
        TMP_Text Value(string name) => Req(Content + name + "/Value").GetComponent<TMP_Text>();
        TMP_Text FieldLabel(string name) => Req(Content + name + "/FieldLabel").GetComponent<TMP_Text>();
        RectTransform DetailContent() => (RectTransform)typeof(QuestJournalView).GetField("_detailContent", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(_view);

        /// <summary>The texts of the headings that are active in the hierarchy, in the order of the details.</summary>
        static List<string> ActiveHeadings(Transform content)
        {
            var list = new List<string>();
            foreach (var text in content.GetComponentsInChildren<TMP_Text>(false))
                if (text.gameObject.name == "FieldLabel") list.Add(text.text);
            return list;
        }

        void Select(QuestHandle handle) { _view.SelectQuest(handle); Rebuild(); }

        void Rebuild()
        {
            Canvas.ForceUpdateCanvases();
            LayoutRebuilder.ForceRebuildLayoutImmediate(DetailContent());
            Canvas.ForceUpdateCanvases();
        }

        void Deliver(WireQuestRow[] rows, WireQuestAction[] actions = null, WireQuestRosterEntry[] roster = null, RosterId? owner = null)
        {
            _transport.Deliver(new ServerMsg.State { Tick = 1, Items = new StateItem[] { new QuestsState { Available = true, View = new WireQuestView
            {
                Rows = rows, Actions = actions ?? new WireQuestAction[0], Drops = new WireQuestDrop[0],
                Roster = roster ?? new WireQuestRosterEntry[0], OwnerRoster = owner,
            } } } });
            _view.Refresh(); Rebuild();
        }

        Rect Local(RectTransform rt)
        {
            var detail = (RectTransform)Req(Detail);
            var corners = new Vector3[4]; rt.GetWorldCorners(corners);
            var a = detail.InverseTransformPoint(corners[0]); var b = detail.InverseTransformPoint(corners[2]);
            return Rect.MinMaxRect(a.x - detail.rect.xMin, a.y - detail.rect.yMin, b.x - detail.rect.xMin, b.y - detail.rect.yMin);
        }

        static Session NewSession(TestTransport transport, byte id)
        {
            var session = new Session(transport, new SessionOptions { Url = "ws://quest-test/ws", Profile = "quest-test", Now = () => 1000 });
            session.Connect(); transport.Open();
            var match = new byte[16]; match[0] = id;
            transport.Deliver(new ServerMsg.Welcome { Protocol = Wire.ProtocolVersion, MatchId = match, Session = new byte[16],
                PlayerId = 1, TickRate = 20, Map = new MapBlob(2, 2, new byte[4]) });
            return session;
        }

        /// <summary>A held quest (accepted by the owner) with no participants and the giver "Wanderer &amp; Co.".</summary>
        static WireQuestRow Row(QuestHandle handle, string name, string brief, string objective, string reward)
        {
            return new WireQuestRow { Handle = handle, Def = "test", GiverX = 1, GiverY = 2, State = QuestRowState.Open,
                ExpiresAt = 12345.5, Participants = Array.Empty<WireQuestParticipant>(), DoneBy = null,
                CanAccept = false, OwnerAccepted = true, OwnerCompleted = false, OwnerReady = true, OwnerProgress = 2u,
                OwnerVisited = 0, Name = name, GiverLabel = "Wanderer & Co.", Brief = brief, Objective = objective, Reward = reward };
        }
    }

    /// <summary>P-UI-FLOWS-LEFTOVERS-1 oracle, CLOSE: the three window tabs (Quests, Traders, Notices) hold one CLOSE in one place, with
    /// one look and one text size, and its label fits its button at the largest text size on every canvas. No frame runs in
    /// EditMode, so a rule that follows the canvas must answer a change of its rectangle by a callback.</summary>
    public sealed class FlowCloseEditModeTests
    {
        GameObject _canvas, _host;
        QuestJournalView _journal;
        TraderDialogView _traders;
        OrderedNoticeFeed _feed;
        readonly int[] _closes = new int[3];

        void Build(int width, int height)
        {
            Array.Clear(_closes, 0, _closes.Length);
            _canvas = new GameObject("Flow close canvas", typeof(RectTransform), typeof(Canvas), typeof(GraphicRaycaster));
            _canvas.GetComponent<Canvas>().renderMode = RenderMode.WorldSpace;
            var rect = (RectTransform)_canvas.transform;
            rect.sizeDelta = new Vector2(width, height); rect.position = Vector3.zero; rect.localScale = Vector3.one;
            _host = new GameObject("Flow close host");
            _journal = _host.AddComponent<QuestJournalView>(); _journal.Initialize(_canvas.transform, () => _closes[0]++);
            _traders = _host.AddComponent<TraderDialogView>(); _traders.Initialize(_canvas.transform, () => _closes[1]++);
            _feed = _host.AddComponent<OrderedNoticeFeed>(); _feed.Initialize(_canvas.transform, () => _closes[2]++);
        }

        [TearDown]
        public void TearDown()
        {
            if (_host != null) UnityEngine.Object.DestroyImmediate(_host);
            if (_canvas != null) UnityEngine.Object.DestroyImmediate(_canvas);
        }

        static readonly string[] Paths = { "QuestJournal/CloseJournal", "TraderDialog/Close", "NoticeHistory/Close" };
        static readonly string[] Cards = { "QuestJournal", "TraderDialog", "NoticeHistory" };

        Button CloseButton(int i)
        {
            var found = _canvas.transform.Find(Paths[i]);
            Assert.IsNotNull(found, "no object at " + Paths[i]);
            return found.GetComponent<Button>();
        }

        TMP_Text CloseLabel(int i) => CloseButton(i).GetComponentInChildren<TMP_Text>(true);

        static Vector3[] Corners(RectTransform rt) { var c = new Vector3[4]; rt.GetWorldCorners(c); return c; }

        void OpenAll() { _journal.SetVisible(true); _traders.SetVisible(true); _feed.SetVisible(true); }

        // ---------------------------------------------------------------- 7

        [TestCase(1920, 1080)]
        [TestCase(1280, 800)]
        [TestCase(980, 2121)]
        public void TheThreeCloseButtonsStandInOnePlace(int width, int height)
        {
            Build(width, height);
            var reference = Corners((RectTransform)CloseButton(0).transform);
            for (int i = 0; i < 3; i++)
            {
                var rect = (RectTransform)CloseButton(i).transform;
                var corners = Corners(rect);
                Assert.That(corners[0].x, Is.EqualTo(reference[0].x).Within(.5f), Paths[i] + " left edge equals the first");
                Assert.That(corners[0].y, Is.EqualTo(reference[0].y).Within(.5f), Paths[i] + " bottom edge equals the first");
                Assert.That(corners[2].x, Is.EqualTo(reference[2].x).Within(.5f), Paths[i] + " right edge equals the first");
                Assert.That(corners[2].y, Is.EqualTo(reference[2].y).Within(.5f), Paths[i] + " top edge equals the first");
                Assert.That(rect.anchorMax.x, Is.EqualTo(FlowWindowClose.AnchorMax.x).Within(.0005f), Paths[i] + " anchorMax.x");
                Assert.That(rect.anchorMax.y, Is.EqualTo(FlowWindowClose.AnchorMax.y).Within(.0005f), Paths[i] + " anchorMax.y");
                var card = Corners((RectTransform)_canvas.transform.Find(Cards[i]));
                float cardWidth = card[2].x - card[0].x;
                Assert.That(corners[2].x, Is.EqualTo(card[0].x + .975f * cardWidth).Within(.5f), Paths[i] + " right edge is the card's x .975");
                Assert.That(corners[0].x, Is.LessThanOrEqualTo(card[0].x + .87f * cardWidth + .5f), Paths[i] + " left edge is at or left of the card's x .87");
            }
        }

        // ---------------------------------------------------------------- 8

        [Test]
        public void TheThreeCloseButtonsLookTheSame()
        {
            Build(1920, 1080);
            Color surface = UiTheme.Current.Surface;
            for (int i = 0; i < 3; i++)
                Assert.AreEqual(surface, CloseButton(i).colors.normalColor, Paths[i] + " has the quiet fill");
        }

        // ---------------------------------------------------------------- 9 (standing guard)

        [Test]
        public void TheCloseLabelsShareOneTextSize()
        {
            Build(1920, 1080);
            foreach (float scale in new[] { 1f, 1.5f })
            {
                MythicUi.ApplyTextScale(_canvas.transform, scale);
                for (int i = 1; i < 3; i++)
                    Assert.That(CloseLabel(i).fontSize, Is.EqualTo(CloseLabel(0).fontSize).Within(.001f), Paths[i] + " text size equals the journal's at text size " + scale);
            }
        }

        // ---------------------------------------------------------------- 10

        [TestCase(1920, 1080)]
        [TestCase(1280, 800)]
        [TestCase(980, 2121)]
        public void TheCloseLabelFitsItsButtonAtTheLargestTextSize(int width, int height)
        {
            Build(width, height);
            OpenAll();
            for (int i = 0; i < 3; i++)
                Assert.IsTrue(CloseLabel(i).gameObject.activeInHierarchy, Paths[i] + ": the label is shown, so it measures its true size");
            MythicUi.ApplyTextScale(_canvas.transform, 1.5f);
            Canvas.ForceUpdateCanvases();
            AssertFit(width + " x " + height);
            ((RectTransform)_canvas.transform).sizeDelta = new Vector2(980, 2121);
            Canvas.ForceUpdateCanvases();
            AssertFit(width + " x " + height + " then 980 x 2121");
        }

        void AssertFit(string at)
        {
            for (int i = 0; i < 3; i++)
            {
                var label = CloseLabel(i);
                bool auto = label.enableAutoSizing; float keep = label.fontSize;
                label.enableAutoSizing = false; label.fontSize = label.fontSizeMin;
                // A width bound of 0 would break the word letter by letter (measured 23.48, one capital): the line is measured in a box wide enough for it.
                float need = label.GetPreferredValues("CLOSE", 100000f, 0f).x;
                label.fontSize = keep; label.enableAutoSizing = auto;
                Assert.That(need, Is.LessThanOrEqualTo(label.rectTransform.rect.width + .01f),
                    Paths[i] + " needs " + need + " of " + label.rectTransform.rect.width + " at " + at);
            }
        }

        // ---------------------------------------------------------------- 11 (standing guard)

        [Test]
        public void EachCloseKeepsItsNameAndStillCloses()
        {
            Build(1920, 1080);
            var names = new[] { "CloseJournal", "Close", "Close" };
            Action[] open = { () => _journal.SetVisible(true), () => _traders.SetVisible(true), () => _feed.SetVisible(true) };
            Func<bool>[] visible = { () => _journal.IsVisible, () => _traders.IsVisible, () => _feed.IsVisible };
            for (int i = 0; i < 3; i++)
            {
                var button = CloseButton(i);
                Assert.AreEqual(names[i], button.name, "the name of the button of " + Cards[i]);
                open[i]();
                Assert.IsTrue(visible[i](), Cards[i] + " is open (rig)");
                button.onClick.Invoke();
                Assert.IsFalse(visible[i](), Cards[i] + " is closed by its CLOSE");
                Assert.AreEqual(1, _closes[i], Cards[i] + " reports one close");
            }
        }
    }

    /// <summary>P-UI-FLOWS-LEFTOVERS-1 oracle, the trader list: a list of offers taller than its viewport opens at its top, at every
    /// opening and for another trader, keeps its place across a rebuild while the window is open, and a barter list that grows past
    /// its viewport also stands at the top. Text size 1.5 on a 1920 x 1080 canvas unless said; the rig is that of
    /// <c>TraderRowsRealOrderEditModeTests</c> (a hidden window, a real session, a new view object for each delivery).</summary>
    public sealed class TraderListTopEditModeTests
    {
        static readonly FieldInfo ScaleField = typeof(ShowcaseSettings).GetField("_textScale", BindingFlags.Instance | BindingFlags.NonPublic);
        ShowcaseSettings _settings; GameObject _settingsGo; float _savedScale; bool _createdSettings; UiTheme _savedTheme;

        [SetUp]
        public void SetUp()
        {
            _savedTheme = UiTheme.Current;
            _settings = ShowcaseSettings.Current;
            if (_settings == null)
            {
                _settingsGo = new GameObject("trader list settings"); _createdSettings = true;
                _settings = _settingsGo.AddComponent<ShowcaseSettings>();
                typeof(ShowcaseSettings).GetMethod("Awake", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(_settings, null);
            }
            _savedScale = (float)ScaleField.GetValue(_settings);
        }

        [TearDown]
        public void TearDown()
        {
            if (_settings != null) ScaleField.SetValue(_settings, _savedScale);
            if (_createdSettings) UnityEngine.Object.DestroyImmediate(_settingsGo);
            UiTheme.Current = _savedTheme;
        }

        static readonly Vector2 Wide = new Vector2(1920f, 1080f);

        static ScrollRect Scroll(TraderDialogView view) => view.Root.Find("OfferPanel/ChoiceScroll").GetComponent<ScrollRect>();
        static float Position(TraderDialogView view) => Scroll(view).verticalNormalizedPosition;

        /// <summary>Set-up: the content is taller than the viewport, else a position of 0 or 1 means nothing.</summary>
        static void AssertTaller(TraderDialogView view, string at)
        {
            var scroll = Scroll(view);
            Assert.That(scroll.content.rect.height, Is.GreaterThan(scroll.viewport.rect.height + .5f),
                "set-up: the list is taller than its viewport " + at + " (" + scroll.content.rect.height + " against " + scroll.viewport.rect.height + ")");
        }

        static void AssertTop(TraderDialogView view, string at)
        {
            Assert.That(Position(view), Is.EqualTo(1f).Within(.01f), "the list stands at its top " + at);
        }

        static void Scrolled(TraderDialogView view)
        {
            Scroll(view).verticalNormalizedPosition = .4f;
            Assert.That(Position(view), Is.EqualTo(.4f).Within(.02f), "set-up: the list is scrolled to .4");
        }

        /// <summary>The standard stat trade with one offer's label changed: SameContent differs, so Refresh rebuilds; the merchants keep their names and kinds.</summary>
        static WireTradeView ChangedOffer()
        {
            var changed = TraderRowsRealOrderEditModeTests.Trade(true, 0);
            changed.Offers[0].Label = "−30% movement speed → +99.9 Soul Offence";
            return changed;
        }

        static WireTradeView TwoStatTraders()
        {
            var view = TraderRowsRealOrderEditModeTests.Trade(true, 0);
            view.Merchants[1].Kind = TradeKind.Stats; view.Merchants[1].Name = "Soul Peddler";
            return view;
        }

        // ---------------------------------------------------------------- 12

        [Test]
        public void AStatListFirstOpensAtTheTop()
        {
            using (var rig = new Rig(Wide, 1.5f))
            {
                rig.Deliver(true); rig.View.Refresh();
                rig.View.SetVisible(true);
                AssertTaller(rig.View, "at the first opening");
                AssertTop(rig.View, "at the first opening");
                rig.LateUpdate();
                AssertTop(rig.View, "after a frame");
                rig.SetScale(1.4f); rig.LateUpdate();
                AssertTaller(rig.View, "after a later reflow");
                AssertTop(rig.View, "after a later reflow");
            }
        }

        // ---------------------------------------------------------------- 13

        [Test]
        public void EveryOpeningStartsAtTheTop()
        {
            using (var rig = new Rig(Wide, 1.5f))
            {
                rig.Deliver(true); rig.View.Refresh();
                rig.View.SetVisible(true);
                AssertTaller(rig.View, "at the first opening");
                Scrolled(rig.View);
                rig.View.SetVisible(false);
                rig.Deliver(ChangedOffer()); rig.View.Refresh(); rig.LateUpdate();   // a content change rebuilds while closed: a moved merchant no longer does (P-UI-TRADER-STEADY-1)
                rig.View.SetVisible(true);
                AssertTop(rig.View, "when it opens after a rebuild while closed");
                Scrolled(rig.View);
                rig.View.SetVisible(false); rig.View.SetVisible(true);
                AssertTop(rig.View, "when it opens again with no delivery");
                Scrolled(rig.View);
                rig.View.SetVisible(true);
                Assert.That(Position(rig.View), Is.EqualTo(.4f).Within(.02f), "a tab click on the open window does not move the list");
            }
        }

        // ---------------------------------------------------------------- 14

        [Test]
        public void AnotherTradersListStartsAtTheTop()
        {
            using (var rig = new Rig(Wide, 1.5f))
            {
                rig.Deliver(TwoStatTraders()); rig.View.Refresh();
                rig.View.SetVisible(true);
                AssertTaller(rig.View, "for the first trader");
                Scrolled(rig.View);
                Assert.IsTrue(rig.View.SelectMerchant(1), "rig: the second trader is selected");
                AssertTaller(rig.View, "for the second trader");
                AssertTop(rig.View, "for another trader's list");
                Scrolled(rig.View);
                Assert.IsTrue(rig.View.SelectMerchant(1), "rig: the selected row is clicked again");
                Assert.That(Position(rig.View), Is.EqualTo(.4f).Within(.02f), "a click on the selected row is the same trader");
            }
        }

        // ---------------------------------------------------------------- 15

        [Test]
        public void ARebuildWhileOpenKeepsThePosition()
        {
            using (var rig = new Rig(Wide, 1.5f))
            {
                rig.Deliver(true); rig.View.Refresh();
                rig.View.SetVisible(true);
                AssertTaller(rig.View, "at the opening");
                Scrolled(rig.View);
                rig.Deliver(ChangedOffer());   // a content change, same merchants: a rebuild with the window open
                rig.View.Refresh();
                Assert.That(Position(rig.View), Is.EqualTo(.4f).Within(.02f), "after the same trader's offer changed (a rebuild), Refresh");
                rig.LateUpdate();
                Assert.That(Position(rig.View), Is.EqualTo(.4f).Within(.02f), "after the next frame");
            }
        }

        // ---------------------------------------------------------------- 16

        [Test]
        public void ABarterListThatGrowsPastTheViewportStandsAtTheTop()
        {
            using (var rig = new Rig(new Vector2(1920f, 1000f), 1.5f))
            {
                rig.Deliver(false); rig.View.Refresh();
                rig.View.SetVisible(true);
                var scroll = Scroll(rig.View);
                Assert.That(scroll.viewport.rect.height, Is.EqualTo(266.7f).Within(1f), "rig: the viewport is 266.7 high");
                Assert.That(scroll.content.rect.height, Is.LessThanOrEqualTo(scroll.viewport.rect.height + .5f), "set-up: three short rows are not taller than the viewport");
                var longer = TraderRowsRealOrderEditModeTests.Trade(false, 0);
                longer.BarterLabels = new[] { Long("weapon", "a finely balanced sword"), Long("potion", "a bow of the northern halls"), Long("coin", "a heavy axe") };
                rig.Deliver(longer); rig.View.Refresh(); rig.LateUpdate();
                AssertTaller(rig.View, "after the labels grew");
                AssertTop(rig.View, "after the list grew past its viewport");
            }
        }

        // ---------------------------------------------------------------- 16b (added: the first build of a barter list reads an empty content)

        [Test]
        public void ALongBarterListFirstOpensAtTheTop()
        {
            using (var rig = new Rig(new Vector2(1920f, 1000f), 1.5f))
            {
                var longer = TraderRowsRealOrderEditModeTests.Trade(false, 0);
                longer.BarterLabels = new[] { Long("weapon", "a finely balanced sword"), Long("potion", "a bow of the northern halls"), Long("coin", "a heavy axe") };
                rig.Deliver(longer); rig.View.Refresh();
                rig.View.SetVisible(true);
                AssertTaller(rig.View, "at the first opening");
                AssertTop(rig.View, "at the first opening");
            }
        }

        static string Long(string give, string get)
        {
            return "Give the " + give + " you carry to the traveller of the road and receive, in exchange for it, " + get +
                   " that was forged long ago in the halls of the north, kept dry through every winter, and carried here for you";
        }

        // ================================================================ the rig (as TraderRowsRealOrderEditModeTests', with a Deliver of a given view)

        sealed class Rig : IDisposable
        {
            public readonly GameObject CanvasGo, Host; public readonly TraderDialogView View;
            public readonly TraderRowsRealOrderEditModeTests.TestTransport T; public readonly Session S;
            readonly float _savedScale; int _tick = 1;
            static readonly MethodInfo Late = typeof(TraderDialogView).GetMethod("LateUpdate", BindingFlags.Instance | BindingFlags.NonPublic);

            public Rig(Vector2 size, float scale)
            {
                _savedScale = (float)ScaleField.GetValue(ShowcaseSettings.Current);
                ScaleField.SetValue(ShowcaseSettings.Current, scale);
                CanvasGo = new GameObject("Trader list canvas", typeof(RectTransform), typeof(Canvas), typeof(GraphicRaycaster));
                CanvasGo.GetComponent<Canvas>().renderMode = RenderMode.WorldSpace;
                ((RectTransform)CanvasGo.transform).sizeDelta = size;
                Host = new GameObject("Trader list host");
                View = Host.AddComponent<TraderDialogView>();
                View.Initialize(CanvasGo.transform);
                T = new TraderRowsRealOrderEditModeTests.TestTransport();
                S = new Session(T, new SessionOptions { Url = "ws://fixture/ws", Profile = "trader-list", Now = () => 1000, ReconnectBaseMs = 500 });
                S.Connect();
                var id = new byte[16]; id[0] = 1;
                T.Deliver(new ServerMsg.Welcome { Protocol = Wire.ProtocolVersion, MatchId = id, Session = new byte[16], PlayerId = 1, TickRate = 20, Map = new MapBlob(4, 4, new byte[16]) });
                T.Sent.Clear();
                View.Bind(S);
            }

            public void SetScale(float scale)
            {
                ScaleField.SetValue(ShowcaseSettings.Current, scale);
                MythicUi.ApplyTextScale(CanvasGo.transform, scale);
            }

            public void LateUpdate() { Late.Invoke(View, null); }

            /// <summary>Delivers the standard trade in a NEW view object each call, as every state frame does.</summary>
            public void Deliver(bool stat, double dx = 0) { Deliver(TraderRowsRealOrderEditModeTests.Trade(stat, dx)); }

            /// <summary>Delivers the given trade view in a new state message.</summary>
            public void Deliver(WireTradeView view)
            {
                T.Deliver(new ServerMsg.State { Tick = (uint)(++_tick), Items = new StateItem[] { new TradeState { Available = true, Value = view } } });
            }

            public void Dispose()
            {
                if (S != null) S.Leave();
                UnityEngine.Object.DestroyImmediate(Host); UnityEngine.Object.DestroyImmediate(CanvasGo);
                ScaleField.SetValue(ShowcaseSettings.Current, _savedScale);
            }
        }
    }

    /// <summary>P-UI-FLOWS-LEFTOVERS-1 oracle, the words after the end: the Quests and Traders tabs of a session that has ended say
    /// the banner's word, "Run over" (<c>PlayerWords.Word(SessionState.Ended)</c>), the word fits its line at text size 1.5, a settled
    /// refresh writes and allocates nothing, and a disconnect keeps its own words.</summary>
    public sealed class FlowEndWordsEditModeTests
    {
        sealed class Transport : ITransport
        {
            public event Action Opened;
            public event Action<byte[]> Received;
            public event Action<string> Closed;
            public void Connect(string url) { Opened?.Invoke(); }
            public void Send(byte[] frame) { }
            public void Close() { }
            public void Poll() { }
            public void Deliver(ServerMsg msg) { Received?.Invoke(msg.Encode()); }
            public void Drop() { Closed?.Invoke("fixture disconnect"); }
        }

        GameObject _canvas, _host;
        readonly List<Session> _sessions = new List<Session>();
        const string Ended = "Run over";

        [SetUp]
        public void SetUp()
        {
            _canvas = new GameObject("Flow end words canvas", typeof(RectTransform), typeof(Canvas), typeof(GraphicRaycaster));
            _canvas.GetComponent<Canvas>().renderMode = RenderMode.WorldSpace;
            var rect = (RectTransform)_canvas.transform;
            rect.sizeDelta = new Vector2(1920, 1080); rect.position = Vector3.zero; rect.localScale = Vector3.one;
            _host = new GameObject("Flow end words host");
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var session in _sessions) session.Leave();
            if (_host != null) UnityEngine.Object.DestroyImmediate(_host);
            if (_canvas != null) UnityEngine.Object.DestroyImmediate(_canvas);
        }

        Session NewSession(Transport transport, string profile)
        {
            var session = new Session(transport, new SessionOptions { Url = "ws://fixture/ws", Profile = profile, Now = () => 1000, ReconnectBaseMs = 500 });
            _sessions.Add(session);
            session.Connect();
            var id = new byte[16]; id[0] = 1;
            transport.Deliver(new ServerMsg.Welcome { Protocol = Wire.ProtocolVersion, MatchId = id, Session = new byte[16], PlayerId = 1, TickRate = 20, Map = new MapBlob(4, 4, new byte[16]) });
            return session;
        }

        /// <summary>A session that ended: a Welcome, a quest state, a trade state (one message each), then MatchEnded.</summary>
        Session EndedSession(string profile)
        {
            var transport = new Transport();
            var session = NewSession(transport, profile);
            var row = new WireQuestRow { Handle = new QuestHandle(71), Def = "test", GiverX = 1, GiverY = 2, State = QuestRowState.Open, ExpiresAt = 12345.5,
                Participants = Array.Empty<WireQuestParticipant>(), OwnerAccepted = true, OwnerReady = true, OwnerProgress = 2u,
                Name = "Held", GiverLabel = "Wanderer & Co.", Brief = "b", Objective = "o", Reward = "r" };
            transport.Deliver(new ServerMsg.State { Tick = 1, Items = new StateItem[] { new QuestsState { Available = true, View = new WireQuestView { Rows = new[] { row } } } } });
            transport.Deliver(new ServerMsg.State { Tick = 2, Items = new StateItem[] { new TradeState { Available = true, Value = TraderRowsRealOrderEditModeTests.Trade(false, 0) } } });
            transport.Deliver(new ServerMsg.Events { Tick = 3, List = new List<WireEvent> { new WireEvent { Kind = EventKind.MatchEnded } } });
            Assert.AreEqual(SessionState.Ended, session.State, "rig: the session has ended");
            return session;
        }

        static void AssertFits(TMP_Text label, string at)
        {
            Assert.IsTrue(label.gameObject.activeInHierarchy, at + ": the label is shown, so it measures its true size");
            bool auto = label.enableAutoSizing; float keep = label.fontSize;
            label.enableAutoSizing = false; label.fontSize = label.fontSizeMin;
            var need = label.GetPreferredValues(label.text, label.textWrappingMode != TextWrappingModes.NoWrap ? label.rectTransform.rect.width : 0f, 0f);
            label.fontSize = keep; label.enableAutoSizing = auto;
            Assert.That(need.y, Is.LessThanOrEqualTo(label.rectTransform.rect.height + .5f), at + ": the text fits its line (needs " + need.y + " of " + label.rectTransform.rect.height + ")");
        }

        TMP_Text JournalState() => _canvas.transform.Find("QuestJournal/JournalState").GetComponent<TMP_Text>();
        TMP_Text TraderStatus() => _canvas.transform.Find("TraderDialog/Status").GetComponent<TMP_Text>();

        QuestJournalView NewJournal() { var view = _host.AddComponent<QuestJournalView>(); view.Initialize(_canvas.transform); return view; }
        TraderDialogView NewTraders() { var view = _host.AddComponent<TraderDialogView>(); view.Initialize(_canvas.transform); return view; }

        // ---------------------------------------------------------------- 17

        [Test]
        public void TheQuestsTabSaysTheEndedWord()
        {
            Assert.AreEqual(Ended, Roguehalla.Net.Ui.PlayerWords.Word(SessionState.Ended), "rig: the banner's word");
            var view = NewJournal();
            view.Bind(EndedSession("flow-end-quests")); view.Refresh();
            Assert.IsFalse(view.IsVisible, "rig: the journal is hidden");
            Assert.AreEqual(Ended, JournalState().text, "the state text of an ended session");
            Action refresh = view.Refresh;
            refresh();
            AllocationMeter.AssertNone(refresh, 200, "settled Ended refresh");
            view.SetVisible(true);
            MythicUi.ApplyTextScale(view.Root, 1.5f); Canvas.ForceUpdateCanvases();
            AssertFits(JournalState(), "the Quests tab at text size 1.5");
        }

        // ---------------------------------------------------------------- 18

        [Test]
        public void TheTradersTabSaysTheEndedWord()
        {
            var view = NewTraders();
            view.Bind(EndedSession("flow-end-traders")); view.Refresh();
            Assert.IsFalse(view.IsVisible, "rig: the window is hidden");
            Assert.AreEqual(Ended, TraderStatus().text, "the status of an ended session");
            view.SetVisible(true);
            MythicUi.ApplyTextScale(view.Root, 1.5f); Canvas.ForceUpdateCanvases();
            AssertFits(TraderStatus(), "the Traders tab at text size 1.5");
        }

        // ---------------------------------------------------------------- 19 (standing guard)

        [Test]
        public void ADisconnectKeepsItsOwnWords()
        {
            var journal = NewJournal(); var traders = NewTraders();

            var dropped = new Transport(); var reconnecting = NewSession(dropped, "flow-end-reconnecting");
            dropped.Drop();
            Assert.AreEqual(SessionState.Reconnecting, reconnecting.State, "rig: reconnecting");
            journal.Bind(reconnecting); journal.Refresh(); traders.Bind(reconnecting); traders.Refresh();
            Assert.AreEqual("Reconnecting · quest actions paused", JournalState().text, "Quests, reconnecting");
            Assert.AreEqual("Connection interrupted · resuming", TraderStatus().text, "Traders, reconnecting");

            var detached = NewSession(new Transport(), "flow-end-detached");
            detached.Detach();
            Assert.AreEqual(SessionState.Detached, detached.State, "rig: detached");
            journal.Bind(detached); journal.Refresh(); traders.Bind(detached); traders.Refresh();
            Assert.AreEqual("Quest journal unavailable while disconnected.", JournalState().text, "Quests, detached");
            Assert.AreEqual("Disconnected", TraderStatus().text, "Traders, detached");
        }
    }
}
