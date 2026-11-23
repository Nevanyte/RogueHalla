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
    /// <summary>P-UI-JOURNAL-DETAILS-1 oracle: the journal's details read in one order (name, objective, giver, brief, reward,
    /// participants), the quest's own actions stand in a strip outside the scroll, Abandon stands in the footer and takes two
    /// presses, nothing selected shows one line, the scroll returns to the top with the selection, an indicator marks a pane with
    /// more, and a settled refresh writes no rect and allocates nothing. The canvas is 1920 x 1080 world units, so every rect is
    /// read in the units of the packet. The journal is built hidden, as a match builds it.</summary>
    public sealed class QuestJournalDetailsEditModeTests
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

        const string Detail = "QuestDetail";
        const string Content = "QuestDetail/QuestTextScroll/Viewport/Content/";
        const string Strip = "QuestDetail/QuestActions";
        const string StripContent = "QuestDetail/QuestActions/Viewport/Content";
        static readonly string[] FieldNames = { "QuestName", "QuestObjective", "QuestGiver", "QuestBrief", "QuestReward", "QuestParticipants" };

        GameObject _canvas, _host;
        QuestJournalView _view;
        Roguehalla.Net.Ui.QuestAcceptLatch _latch = new Roguehalla.Net.Ui.QuestAcceptLatch();
        Session _session;
        TestTransport _transport;
        readonly QuestHandle _first = new QuestHandle(71), _second = new QuestHandle(72);
        readonly string _long = new string('L', 255);

        [SetUp]
        public void SetUp()
        {
            _canvas = new GameObject("Quest journal details canvas", typeof(RectTransform), typeof(Canvas), typeof(GraphicRaycaster));
            _canvas.GetComponent<Canvas>().renderMode = RenderMode.WorldSpace;
            var canvasRect = (RectTransform)_canvas.transform;
            canvasRect.sizeDelta = new Vector2(1920, 1080); canvasRect.position = Vector3.zero; canvasRect.localScale = Vector3.one;
            _host = new GameObject("Quest journal details host");
            _view = _host.AddComponent<QuestJournalView>();
            _view.Initialize(_canvas.transform, null, null, _latch);
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
        public void RewardStandsUnderTheGiverWhateverTheActions()
        {
            Open();
            var held = Row(_first, "Held", "brief", "brief", "reward", false, true);
            var gaps = new List<float>();
            var sets = new[]
            {
                new WireQuestAction[0],
                new[] { Act(_first, 1, 0, "Abandon") },
                new[] { Act(_first, 6, 0, "Meditate"), Act(_first, 1, 0, "Abandon") },
            };
            foreach (var actions in sets)
            {
                Deliver(new[] { held }, actions); Select(_first);
                Assert.IsTrue(Value("QuestGiver").gameObject.activeInHierarchy, "rig: the giver is shown");
                Assert.IsTrue(Value("QuestReward").gameObject.activeInHierarchy, "rig: the reward is shown");
                float gap = Local(Field("QuestGiver")).yMin - Local(Field("QuestReward")).yMax;
                gaps.Add(gap);
                Assert.That(gap, Is.EqualTo(10f).Within(.5f), "giver to reward with " + actions.Length + " actions");
            }
            Assert.That(gaps[1], Is.EqualTo(gaps[0]).Within(.01f), "one action does not move the reward");
            Assert.That(gaps[2], Is.EqualTo(gaps[0]).Within(.01f), "two actions do not move the reward");

            var different = Row(_first, "Held", "a long story", "the objective", "reward", false, true);
            Deliver(new[] { different }, sets[2]); Select(_first);
            var content = Field("QuestReward").parent;
            Transform above = null;
            for (int i = Field("QuestReward").GetSiblingIndex() - 1; i >= 0 && above == null; i--)
                if (content.GetChild(i).gameObject.activeSelf) above = content.GetChild(i);
            Assert.IsNotNull(above, "an active sibling stands above the reward");
            Assert.AreEqual("QuestBrief", above.name, "the brief is the field above the reward");
            Assert.That(Local((RectTransform)above).yMin - Local(Field("QuestReward")).yMax, Is.EqualTo(10f).Within(.5f), "brief to reward");
        }

        // ---------------------------------------------------------------- 2

        [Test]
        public void NoActionIsInTheScrollOrOverlapsTheRest()
        {
            Open();
            Deliver(new[] { Row(_first, "Held", "b", "o", "r", false, true) },
                new[] { Act(_first, 1, 0, "Abandon"), Act(_first, 2, 0, "Carry A"), Act(_first, 2, 1, "Carry B") });
            Select(_first);
            var detail = (RectTransform)Req(Detail);
            var panel = new Rect(0f, 0f, detail.rect.width, detail.rect.height);
            var others = new[] { "QuestTextScroll", "QuestStatus", "TrackQuest", "AcceptQuest", "AcceptHint", "AbandonQuest" };
            int shown = 0;
            foreach (var button in detail.GetComponentsInChildren<Button>(false))
            {
                if (!button.name.StartsWith("Action") || !button.gameObject.activeInHierarchy) continue;
                shown++;
                var rect = Local((RectTransform)button.transform);
                bool inScroll = false;
                for (var p = button.transform.parent; p != null && p != detail; p = p.parent) if (p.name == "QuestTextScroll") inScroll = true;
                Assert.IsFalse(inScroll, button.name + " is not inside the details scroll");
                Assert.That(rect.xMin, Is.GreaterThanOrEqualTo(panel.xMin - .5f), button.name + " left");
                Assert.That(rect.xMax, Is.LessThanOrEqualTo(panel.xMax + .5f), button.name + " right");
                Assert.That(rect.yMin, Is.GreaterThanOrEqualTo(panel.yMin - .5f), button.name + " bottom");
                Assert.That(rect.yMax, Is.LessThanOrEqualTo(panel.yMax + .5f), button.name + " top");
                foreach (var other in others)
                    Assert.IsFalse(Meets(rect, Local((RectTransform)Req(Detail + "/" + other))), button.name + " meets " + other);
            }
            Assert.AreEqual(2, shown, "two own actions are shown as buttons named Action<i>");
            Assert.That(Local((RectTransform)Req(Strip)).yMin, Is.EqualTo(.20f * detail.rect.height).Within(.5f), "the strip's bottom is .20 of the panel");

            var abandon = Local((RectTransform)Req(Detail + "/AbandonQuest"));
            var accept = Local((RectTransform)Req(Detail + "/AcceptQuest"));
            Assert.That(abandon.xMin, Is.EqualTo(accept.xMin).Within(.5f), "AbandonQuest has the rect of AcceptQuest: left");
            Assert.That(abandon.xMax, Is.EqualTo(accept.xMax).Within(.5f), "right");
            Assert.That(abandon.yMin, Is.EqualTo(accept.yMin).Within(.5f), "bottom");
            Assert.That(abandon.yMax, Is.EqualTo(accept.yMax).Within(.5f), "top");
            foreach (var other in new[] { "QuestTextScroll", "TrackQuest", "QuestActions", "QuestStatus" })
                Assert.IsFalse(Meets(abandon, Local((RectTransform)Req(Detail + "/" + other))), "AbandonQuest meets " + other);
            // QuestStatus is a standing guard (green on the base for AcceptQuest): MythicUi.Text insets its rect 3 units, so the gap is .08 units.
        }

        // ---------------------------------------------------------------- 3

        [Test]
        public void AbandonStandsInTheFooterAndTheDeliveredTuplesAreSent()
        {
            Open();
            var drop = new WireQuestDrop { Index = 37, Reward = 20, Charges = 1, X = 64, Y = 64 };
            var picked = Act(_first, 9, 37, "Pick up WallBuster"); picked.Points = new[] { 64.0, 64.0 };
            Deliver(new[] { Row(_first, "Held", "b", "o", "r", false, true) },
                new[] { Act(_first, 1, 0, "Abandon"), Act(_first, 2, 0, "Carry A"), Act(_first, 2, 1, "Carry B"), Act(_first, 8, 0, "Choose"), picked },
                new[] { drop });
            Select(_first);
            var labels = ActiveStripLabels();
            Assert.AreEqual(2, labels.Count, "the strip holds the two Carry actions only");
            if (labels.Count == 2) { Assert.AreEqual("Carry A", labels[0]); Assert.AreEqual("Carry B", labels[1]); }
            var abandon = Req(Detail + "/AbandonQuest");
            Assert.IsTrue(abandon.gameObject.activeSelf, "Abandon is shown");
            Assert.AreSame(Req(Detail), abandon.parent, "Abandon is a child of QuestDetail");
            Assert.AreEqual("Abandon", ButtonLabel("AbandonQuest"));
            Assert.AreEqual(Req(Detail + "/TrackQuest").GetComponent<Button>().colors.normalColor, abandon.GetComponent<Button>().colors.normalColor, "same style as TrackQuest");
            Assert.IsFalse(Req(Detail + "/AcceptQuest").gameObject.activeSelf, "ACCEPT is hidden");
            Assert.IsFalse(Req(Detail + "/AcceptHint").gameObject.activeSelf, "the hint is hidden");

            StripButton(0).onClick.Invoke(); _session.FlushInput();
            AssertLast(2, 0, "Carry A");
            StripButton(1).onClick.Invoke(); _session.FlushInput();
            AssertLast(2, 1, "Carry B");
            int sent = Commands().Count;
            abandon.GetComponent<Button>().onClick.Invoke(); _session.FlushInput();
            Assert.AreEqual(sent, Commands().Count, "the first press of Abandon sends nothing");
            abandon.GetComponent<Button>().onClick.Invoke(); _session.FlushInput();
            AssertLast(1, 0, "Abandon, second press");

            Deliver(new[] { Row(_first, "Held", "b", "o", "r", false, true) }, new[] { Act(_first, 2, 0, "Carry A") }); Select(_first);
            Assert.IsFalse(Req(Detail + "/AbandonQuest").gameObject.activeSelf, "no Abandon delivered, none shown");
            Deliver(new[] { Row(_first, "Held", "b", "o", "r", false, true), Row(_second, "Other", "b", "o", "r", false, true) },
                new[] { Act(_second, 1, 0, "Abandon") });
            Select(_first);
            Assert.IsFalse(Req(Detail + "/AbandonQuest").gameObject.activeSelf, "Abandon of another quest is not shown for this one");
            Select(_second);
            Assert.IsTrue(Req(Detail + "/AbandonQuest").gameObject.activeSelf, "it is shown for its own quest");
        }

        // ---------------------------------------------------------------- 4

        [Test]
        public void TheStripHeightFollowsTheOwnActionsAndTheScrollFollowsTheStrip()
        {
            Open();
            int[] counts = { 0, 1, 2, 60, 0 };
            float[] height = { 0f, 44f, 93f, 93f, 0f };
            float[] bottom = { 0f, 50f, 99f, 99f, 0f };
            var detail = (RectTransform)Req(Detail);
            float h = detail.rect.height;
            Assert.That(h, Is.EqualTo(583.8f).Within(1f), "rig: QuestDetail is 583.8 units high");
            for (int step = 0; step < counts.Length; step++)
            {
                var actions = new List<WireQuestAction> { Act(_first, 1, 0, "Abandon") };
                for (int i = 0; i < counts[step]; i++)
                    actions.Add(counts[step] == 60 && i == 59 ? Act(_first, 5, 0, "Last action") : Act(_first, 3, (byte)i, "Own " + i));
                Deliver(new[] { Row(_first, "Held", "b", "o", "r", false, true) }, actions.ToArray()); Select(_first);
                string at = "with " + counts[step] + " own actions";
                var strip = (RectTransform)Req(Strip);
                Assert.That(strip.sizeDelta.y, Is.EqualTo(height[step]).Within(.5f), "strip height " + at);
                Assert.AreEqual(counts[step] > 0, strip.gameObject.activeSelf, "strip active " + at);
                Assert.IsTrue(Req(Detail + "/AbandonQuest").gameObject.activeSelf, "Abandon is shown " + at);
                var scroll = Local((RectTransform)Req(Detail + "/QuestTextScroll"));
                Assert.That(scroll.yMin, Is.EqualTo(.20f * h + bottom[step]).Within(.5f), "scroll bottom " + at);
                Assert.That(scroll.yMax, Is.EqualTo(.87f * h).Within(.5f), "scroll top " + at);
                Assert.That(Local((RectTransform)Req(Detail + "/DetailHeading")).yMin, Is.GreaterThanOrEqualTo(scroll.yMax - .01f), "the heading lies above the scroll " + at);
                AssertAnchors("QuestStatus", .025f, .115f, .975f, .20f);
                AssertAnchors("TrackQuest", .035f, .025f, .48f, .12f);
                AssertAnchors("AcceptQuest", .52f, .025f, .965f, .12f);
                AssertAnchors("AcceptHint", .52f, .025f, .965f, .12f);
                if (counts[step] == 60)
                {
                    var labels = ActiveStripLabels();
                    Assert.AreEqual(60, labels.Count, "all sixty actions are in the strip");
                    Assert.That(((RectTransform)Req(StripContent)).rect.height, Is.EqualTo(2935f).Within(1f), "the strip's content is 2935 high");
                    if (labels.Count > 0) Assert.AreEqual("Last action", labels[labels.Count - 1], "the last button is the last action");
                }
            }
        }

        // ---------------------------------------------------------------- 5

        [Test]
        public void NothingSelectedShowsOneLineOfGuidance()
        {
            Open();
            foreach (var name in new[] { "QuestName", "QuestObjective", "QuestGiver", "QuestReward", "QuestParticipants" })
                Assert.IsFalse(FieldLabel(name).gameObject.activeInHierarchy, name + " has no heading before a selection");
            foreach (var name in new[] { "QuestObjective", "QuestGiver", "QuestBrief", "QuestReward", "QuestParticipants" })
                Assert.IsFalse(Field(name).gameObject.activeInHierarchy, name + " is hidden before a selection");
            Assert.AreEqual("Select a quest", Value("QuestName").text);
            Assert.IsTrue(Value("QuestName").gameObject.activeInHierarchy, "the guidance is shown");
            Assert.IsFalse(Req(Detail + "/TrackQuest").GetComponent<Button>().interactable, "TRACK is not interactable");
            Assert.IsFalse(Req(Strip).gameObject.activeSelf, "no strip");
            Assert.IsFalse(Req(Detail + "/AbandonQuest").gameObject.activeSelf, "no Abandon");

            Deliver(new[] { Row(_first, "Held", "b", "o", "r", false, true) }, new[] { Act(_first, 1, 0, "Abandon") }); Select(_first);
            foreach (var name in new[] { "QuestObjective", "QuestGiver", "QuestReward" })
                Assert.IsTrue(FieldLabel(name).gameObject.activeInHierarchy, name + " has its heading after a selection");
            foreach (var name in new[] { "QuestName", "QuestParticipants" })
                Assert.IsFalse(FieldLabel(name).gameObject.activeInHierarchy, name + " has no heading: the name has none, and the row has no participants");
            Deliver(new[] { Row(_second, "Other", "b", "o", "r", false, true) });
            foreach (var name in new[] { "QuestName", "QuestObjective", "QuestGiver", "QuestReward", "QuestParticipants" })
                Assert.IsFalse(FieldLabel(name).gameObject.activeInHierarchy, name + " is hidden again when the row is gone");
            Assert.AreEqual("Select a quest", Value("QuestName").text);
        }

        // ---------------------------------------------------------------- 6

        [TestCase(1f)]
        [TestCase(1.5f)]
        public void NoHeadingIsTallerThanItsRowAndNothingOverlaps(float scale)
        {
            Open();
            Deliver(new[] { LongRow(_first) }, new[] { Act(_first, 1, 0, "Abandon"), Act(_first, 3, 0, "Own") });
            Select(_first);
            MythicUi.ApplyTextScale(_view.Root, scale); Rebuild();
            AssertHeadings("text scale " + scale);
            var viewport = ((ScrollRect)DetailScroll()).viewport;
            Assert.That(DetailContent().rect.height, Is.GreaterThan(viewport.rect.height), "the long text overflows the pane");
        }

        // ---------------------------------------------------------------- 6b

        [Test]
        public void TheHeadingsAreRightWhenTheJournalOpensAfterDeliveriesWhileHidden()
        {
            var actions = new[] { Act(_first, 1, 0, "Abandon"), Act(_first, 3, 0, "Own") };
            Deliver(new[] { LongRow(_first) }, actions); _view.Refresh(); _view.Refresh();
            Assert.IsFalse(_view.Root.gameObject.activeSelf, "the journal is still hidden");
            Open(); Select(_first);
            MythicUi.ApplyTextScale(_view.Root, 1.5f); Rebuild();
            AssertHeadings("after a hidden delivery");
            Assert.That(Local(Field("QuestGiver")).yMin - Local(Field("QuestReward")).yMax, Is.EqualTo(10f).Within(.5f), "giver to reward");
        }

        // ---------------------------------------------------------------- 7

        [Test]
        public void TheScrollReturnsToTheTopWhenTheSelectionChanges()
        {
            Open();
            var rows = new[] { LongRow(_first), LongRow(_second) };
            Deliver(rows); Select(_first);
            var scroll = DetailScroll();
            Assert.That(DetailContent().rect.height, Is.GreaterThan(scroll.viewport.rect.height), "rig: the pane scrolls");
            scroll.verticalNormalizedPosition = .2f;
            Assert.That(scroll.verticalNormalizedPosition, Is.EqualTo(.2f).Within(.02f), "the pane really scrolls");
            float y = DetailContent().anchoredPosition.y;
            Assert.Greater(y, 100f, "rig: the content moved");
            _view.Refresh(); _view.Refresh();
            Assert.That(DetailContent().anchoredPosition.y, Is.EqualTo(y).Within(.5f), "Refresh keeps the offset");
            Deliver(new[] { LongRow(_first), LongRow(_second) });
            Assert.That(DetailContent().anchoredPosition.y, Is.EqualTo(y).Within(.5f), "a delivery of the same handle keeps the offset");
            Select(_second);
            Assert.That(scroll.verticalNormalizedPosition, Is.EqualTo(1f).Within(.001f), "another quest starts at the top");
            Assert.That(DetailContent().anchoredPosition.y, Is.EqualTo(0f).Within(.5f), "offset 0");
            scroll.verticalNormalizedPosition = .3f;
            Assert.Greater(DetailContent().anchoredPosition.y, 100f, "rig: scrolled again");
            Deliver(new[] { LongRow(_first) });
            Assert.That(DetailContent().anchoredPosition.y, Is.EqualTo(0f).Within(.5f), "a vanished selection returns to the top");
        }

        // ---------------------------------------------------------------- 8

        [Test]
        public void TheIndicatorShowsOnlyWhileTheContentIsTaller()
        {
            Open();
            const string track = "QuestDetail/QuestTextScroll/ScrollIndicator";
            Deliver(new[] { Row(_first, "Short", "b", "o", "r", false, true) }); Select(_first);
            var scroll = DetailScroll();
            scroll.onValueChanged.Invoke(scroll.normalizedPosition);
            Assert.IsFalse(Req(track).gameObject.activeSelf, "a short row needs no indicator");

            Deliver(new[] { LongRow(_first) }); Select(_first);
            scroll.onValueChanged.Invoke(scroll.normalizedPosition);
            var trackRect = (RectTransform)Req(track);
            Assert.IsTrue(trackRect.gameObject.activeSelf, "a long row shows the indicator");
            var handle = (RectTransform)Req(track + "/Handle");
            float length = scroll.viewport.rect.height / DetailContent().rect.height;
            scroll.verticalNormalizedPosition = 1f; scroll.onValueChanged.Invoke(scroll.normalizedPosition);
            Assert.That(handle.anchorMax.y - handle.anchorMin.y, Is.EqualTo(length).Within(.005f), "handle length is viewport over content");
            Assert.That(handle.anchorMax.y, Is.EqualTo(1f).Within(.001f), "at the top the handle touches the top");
            scroll.verticalNormalizedPosition = 0f; scroll.onValueChanged.Invoke(scroll.normalizedPosition);
            Assert.That(handle.anchorMin.y, Is.EqualTo(0f).Within(.001f), "at the bottom the handle touches the bottom");
            scroll.verticalNormalizedPosition = .5f; scroll.onValueChanged.Invoke(scroll.normalizedPosition);
            Assert.That((handle.anchorMin.y + handle.anchorMax.y) * .5f, Is.EqualTo(.5f).Within(.005f), "in the middle the handle is centred");
            float low = handle.anchorMin.y;
            scroll.verticalNormalizedPosition = .9f; _view.Refresh();
            Assert.That(handle.anchorMin.y, Is.EqualTo(low).Within(.0001f), "Refresh does not move the handle: the scroll event does");
            Assert.AreEqual(0, trackRect.GetComponentsInChildren<Selectable>(true).Length, "no Scrollbar or Selectable");
            Assert.IsNull(trackRect.GetComponent<Scrollbar>(), "no Scrollbar");
            foreach (var graphic in trackRect.GetComponentsInChildren<Graphic>(true))
            {
                Assert.IsTrue(UiTheme.Current.Owns(graphic.color), graphic.name + " uses a theme colour");
                Assert.IsFalse(graphic.raycastTarget, graphic.name + " takes no raycast");
            }

            const string stripTrack = Strip + "/ScrollIndicator";
            var stripScroll = Req(Strip).GetComponent<ScrollRect>();
            Assert.IsNotNull(stripScroll, "the strip frame has a ScrollRect");
            var own = new List<WireQuestAction> { Act(_first, 1, 0, "Abandon"), Act(_first, 3, 0, "A"), Act(_first, 3, 1, "B") };
            Deliver(new[] { LongRow(_first) }, own.ToArray()); Select(_first);
            if (stripScroll != null) stripScroll.onValueChanged.Invoke(stripScroll.normalizedPosition);
            Assert.IsFalse(Req(stripTrack).gameObject.activeSelf, "two own actions fit the strip");
            own.Add(Act(_first, 3, 2, "C"));
            Deliver(new[] { LongRow(_first) }, own.ToArray()); Select(_first);
            if (stripScroll != null) stripScroll.onValueChanged.Invoke(stripScroll.normalizedPosition);
            Assert.IsTrue(Req(stripTrack).gameObject.activeSelf, "three own actions overflow the strip");
            var stripHandle = (RectTransform)Req(stripTrack + "/Handle");
            if (stripScroll != null)
            {
                float stripLength = stripScroll.viewport.rect.height / ((RectTransform)Req(StripContent)).rect.height;
                Assert.That(stripLength, Is.EqualTo(93f / 142f).Within(.005f), "rig: 93 of 142");
                Assert.That(stripHandle.anchorMax.y - stripHandle.anchorMin.y, Is.EqualTo(stripLength).Within(.005f), "strip handle length");
            }
            var many = new List<WireQuestAction> { Act(_first, 1, 0, "Abandon") };
            for (int i = 0; i < 60; i++) many.Add(Act(_first, 3, (byte)i, "Own " + i));
            Deliver(new[] { LongRow(_first) }, many.ToArray()); Select(_first);
            if (stripScroll != null) stripScroll.onValueChanged.Invoke(stripScroll.normalizedPosition);
            Assert.IsTrue(Req(stripTrack).gameObject.activeSelf, "sixty own actions overflow the strip");
        }

        // ---------------------------------------------------------------- 9

        [Test]
        public void AnUnchangedRefreshWritesNoRectAndAllocatesNothing()
        {
            Open();
            Deliver(new[] { LongRow(_first) }, new[] { Act(_first, 1, 0, "Abandon"), Act(_first, 3, 0, "Own A"), Act(_first, 3, 1, "Own B") });
            Select(_first);
            var detailScroll = DetailScroll(); detailScroll.onValueChanged.Invoke(detailScroll.normalizedPosition);
            var strip = (RectTransform)Req(Strip);
            var text = (RectTransform)Req(Detail + "/QuestTextScroll");
            var handle = (RectTransform)Req("QuestDetail/QuestTextScroll/ScrollIndicator/Handle");
            strip.sizeDelta = new Vector2(strip.sizeDelta.x, 7f);
            text.offsetMin = new Vector2(text.offsetMin.x, 3f);
            handle.anchorMin = new Vector2(handle.anchorMin.x, .25f);
            _view.Refresh(); _view.Refresh(); _view.Refresh();
            Assert.That(strip.sizeDelta.y, Is.EqualTo(7f), "the strip height is not rewritten");
            Assert.That(text.offsetMin.y, Is.EqualTo(3f), "the pane's bottom is not rewritten");
            Assert.That(handle.anchorMin.y, Is.EqualTo(.25f), "the handle is not rewritten by Refresh");

            Action refresh = _view.Refresh;
            refresh();
            AllocationMeter.AssertNone(refresh, 200, "settled journal refresh");
            Req(Detail + "/AbandonQuest").GetComponent<Button>().onClick.Invoke();
            Assert.AreEqual("Confirm abandon", ButtonLabel("AbandonQuest"), "rig: Abandon is armed");
            AllocationMeter.AssertNone(refresh, 200, "settled journal refresh with Abandon armed");
        }

        // ---------------------------------------------------------------- 10

        [Test]
        public void ADeliveryThatChangesNothingShownWritesNoRectAndKeepsTheStripScroll()
        {
            Open();
            Action<int, int> deliver = (a, b) =>
            {
                var list = new List<WireQuestAction>(OwnFor(_first, a)); list.AddRange(OwnFor(_second, b));
                Deliver(new[] { Row(_first, "One", "b", "o", "r", false, true), Row(_second, "Two", "b", "o", "r", false, true) }, list.ToArray());
            };
            deliver(60, 60); Select(_first);
            var stripScroll = Req(Strip).GetComponent<ScrollRect>();
            Assert.IsNotNull(stripScroll, "the strip frame has a ScrollRect");
            var strip = (RectTransform)Req(Strip);
            var text = (RectTransform)Req(Detail + "/QuestTextScroll");
            var stripContent = (RectTransform)Req(StripContent);
            stripScroll.verticalNormalizedPosition = .4f;
            Assert.That(stripScroll.verticalNormalizedPosition, Is.EqualTo(.4f).Within(.02f), "the strip scrolls");
            float y = stripContent.anchoredPosition.y;
            Assert.Greater(y, 100f, "rig: the strip moved");
            Action sentinels = () =>
            {
                strip.sizeDelta = new Vector2(strip.sizeDelta.x, 7f);
                text.offsetMin = new Vector2(text.offsetMin.x, 3f);
            };
            sentinels();

            deliver(60, 60);
            Assert.That(stripContent.anchoredPosition.y, Is.EqualTo(y).Within(.5f), "(a) an identical list keeps the strip offset");
            Assert.That(strip.sizeDelta.y, Is.EqualTo(7f), "(a) strip height");
            Assert.That(text.offsetMin.y, Is.EqualTo(3f), "(a) pane bottom");

            deliver(3, 60); deliver(60, 60);
            Assert.That(strip.sizeDelta.y, Is.EqualTo(7f), "(b) strip height after 3 then 60 (rows 2 and 2)");
            Assert.That(text.offsetMin.y, Is.EqualTo(3f), "(b) pane bottom");

            stripScroll.verticalNormalizedPosition = .4f;
            Assert.Greater(stripContent.anchoredPosition.y, 100f, "(c) rig: the strip is scrolled again");
            Select(_second);
            Assert.That(stripContent.anchoredPosition.y, Is.EqualTo(0f).Within(.5f), "(c) another quest returns the strip to the top");

            deliver(60, 1);
            Assert.That(strip.sizeDelta.y, Is.EqualTo(44f).Within(.5f), "(d) one row is 44 high");
            Assert.That(text.offsetMin.y, Is.EqualTo(50f).Within(.5f), "(d) the pane's bottom moved by 50");
            sentinels();
            deliver(60, 1);
            Assert.That(strip.sizeDelta.y, Is.EqualTo(7f), "(d) the same single action in a new view writes nothing: strip");
            Assert.That(text.offsetMin.y, Is.EqualTo(3f), "(d) pane bottom");
        }

        // ---------------------------------------------------------------- 11

        [Test]
        public void ADoubleClickOnAcceptNeverAbandons()
        {
            Open();
            Deliver(new[] { Row(_first, "Offer", "b", "o", "r", true) }); Select(_first);
            Req(Detail + "/AcceptQuest").GetComponent<Button>().onClick.Invoke();
            Deliver(new[] { Row(_first, "Offer", "b", "o", "r", false, true) }, new[] { Act(_first, 1, 0, "Abandon") });
            Assert.IsFalse(Req(Detail + "/AcceptQuest").gameObject.activeSelf, "ACCEPT gives way");
            var abandon = Req(Detail + "/AbandonQuest");
            Assert.IsTrue(abandon.gameObject.activeSelf, "Abandon takes its place");
            var a = Local((RectTransform)abandon); var b = Local((RectTransform)Req(Detail + "/AcceptQuest"));
            Assert.That(a.xMin, Is.EqualTo(b.xMin).Within(.5f), "same rect: left");
            Assert.That(a.yMin, Is.EqualTo(b.yMin).Within(.5f), "same rect: bottom");
            Assert.That(a.xMax, Is.EqualTo(b.xMax).Within(.5f), "same rect: right");
            Assert.That(a.yMax, Is.EqualTo(b.yMax).Within(.5f), "same rect: top");
            Assert.AreEqual("Abandon", ButtonLabel("AbandonQuest"));

            for (int click = 2; click <= 4; click++)
            {
                abandon.GetComponent<Button>().onClick.Invoke(); _session.FlushInput();
                int accepts = 0, interacts = 0;
                foreach (var c in Commands()) { if (c.Kind == CommandKind.QuestAccept) accepts++; if (c.Kind == CommandKind.QuestInteract) interacts++; }
                Assert.AreEqual(1, accepts, "one QuestAccept");
                Assert.AreEqual(0, interacts, "click " + click + " of a rapid series sends no Abandon");
                Assert.AreEqual("Abandon", ButtonLabel("AbandonQuest"), "click " + click + " arms nothing");
            }
            _latch.Tick(Roguehalla.Net.Ui.QuestAcceptLatch.HoldSeconds);
            abandon.GetComponent<Button>().onClick.Invoke(); _session.FlushInput();
            Assert.AreEqual(0, Interacts(), "after the hold the first press only arms");
            Assert.AreEqual("Confirm abandon", ButtonLabel("AbandonQuest"));
            abandon.GetComponent<Button>().onClick.Invoke(); _session.FlushInput();
            AssertLast(1, 0, "the second press after the hold abandons");
            Assert.AreEqual("Abandon", ButtonLabel("AbandonQuest"));
        }

        // ---------------------------------------------------------------- 12

        [Test]
        public void AbandonIsArmedUntilSomethingChanges()
        {
            Open();
            Func<bool, bool, WireQuestAction[]> actions = (one, two) =>
            {
                var list = new List<WireQuestAction>();
                if (one) list.Add(Act(_first, 1, 0, "Abandon"));
                if (two) list.Add(Act(_second, 1, 0, "Abandon"));
                return list.ToArray();
            };
            Action<bool, bool> deliver = (one, two) =>
                Deliver(new[] { Row(_first, "One", "b", "o", "r", false, true), Row(_second, "Two", "b", "o", "r", false, true) }, actions(one, two));
            deliver(true, true); Select(_first);
            Action click = () => Req(Detail + "/AbandonQuest").GetComponent<Button>().onClick.Invoke();
            click();
            Assert.AreEqual("Confirm abandon", ButtonLabel("AbandonQuest"), "armed by one press");
            Select(_second);
            Assert.AreEqual("Abandon", ButtonLabel("AbandonQuest"), "another quest is unarmed");
            Select(_first);
            Assert.AreEqual("Abandon", ButtonLabel("AbandonQuest"), "back on the first it is unarmed");
            _session.FlushInput();
            Assert.AreEqual(0, Interacts(), "nothing was sent");

            click(); Assert.AreEqual("Confirm abandon", ButtonLabel("AbandonQuest"), "armed again");
            _view.SetVisible(false); Open();
            Assert.AreEqual("Abandon", ButtonLabel("AbandonQuest"), "closing disarms");
            click(); _session.FlushInput();
            Assert.AreEqual(0, Interacts(), "one click after reopening arms and sends nothing");
            Assert.AreEqual("Confirm abandon", ButtonLabel("AbandonQuest"), "armed");

            deliver(true, true);
            Assert.AreEqual("Confirm abandon", ButtonLabel("AbandonQuest"), "an identical list in a new view keeps the arm");
            click(); _session.FlushInput();
            Assert.AreEqual(1, Interacts(), "exactly one QuestInteract");
            AssertLast(1, 0, "Abandon of the first quest", _first);
            Assert.AreEqual("Abandon", ButtonLabel("AbandonQuest"), "disarmed after sending");

            click(); Assert.AreEqual("Confirm abandon", ButtonLabel("AbandonQuest"), "armed once more");
            deliver(false, true);
            Assert.IsFalse(Req(Detail + "/AbandonQuest").gameObject.activeSelf, "the server withdrew Abandon");
            deliver(true, true);
            Assert.AreEqual("Abandon", ButtonLabel("AbandonQuest"), "when it returns it is unarmed");
            click(); _session.FlushInput();
            Assert.AreEqual(1, Interacts(), "one click sends nothing");
        }

        // ---------------------------------------------------------------- 13

        [TestCase(1f)]
        [TestCase(1.5f)]
        public void TheArmedTextFitsTheButton(float scale)
        {
            Open();
            Deliver(new[] { Row(_first, "Held", "b", "o", "r", false, true) }, new[] { Act(_first, 1, 0, "Abandon") }); Select(_first);
            MythicUi.ApplyTextScale(_view.Root, scale);
            Req(Detail + "/AbandonQuest").GetComponent<Button>().onClick.Invoke();
            Rebuild();
            var label = Req(Detail + "/AbandonQuest/Label").GetComponent<TMP_Text>();
            Assert.IsTrue(label.gameObject.activeInHierarchy, "the label is shown, so it measures its true size");
            Assert.AreEqual("Confirm abandon", label.text);
            var need = Needs(label, "Confirm abandon", label.fontSizeMin);
            Assert.That(need.y, Is.LessThanOrEqualTo(label.rectTransform.rect.height + .5f), "the armed text fits the button at text scale " + scale);
        }

        // ================================================================ helpers

        static Vector2 Needs(TMP_Text label, string text, float size)
        {
            bool auto = label.enableAutoSizing; float keep = label.fontSize;
            label.enableAutoSizing = false; label.fontSize = size;
            var need = label.GetPreferredValues(text, label.textWrappingMode != TextWrappingModes.NoWrap ? label.rectTransform.rect.width : 0f, 0f);
            label.fontSize = keep; label.enableAutoSizing = auto;
            return need;
        }

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
        string ButtonLabel(string name) => Req(Detail + "/" + name + "/Label").GetComponent<TMP_Text>().text;
        RectTransform DetailContent() => (RectTransform)typeof(QuestJournalView).GetField("_detailContent", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(_view);
        ScrollRect DetailScroll() => (ScrollRect)typeof(QuestJournalView).GetField("_detailScroll", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(_view);

        void Select(QuestHandle handle) { _view.SelectQuest(handle); Rebuild(); }

        void Rebuild()
        {
            Canvas.ForceUpdateCanvases();
            LayoutRebuilder.ForceRebuildLayoutImmediate(DetailContent());
            var strip = _canvas.transform.Find("QuestJournal/" + StripContent) as RectTransform;
            if (strip != null) LayoutRebuilder.ForceRebuildLayoutImmediate(strip);
            Canvas.ForceUpdateCanvases();
        }

        void Deliver(WireQuestRow[] rows, WireQuestAction[] actions = null, WireQuestDrop[] drops = null)
        {
            _transport.Deliver(new ServerMsg.State { Tick = 1, Items = new StateItem[] { new QuestsState { Available = true, View = new WireQuestView
            {
                Rows = rows, Actions = actions ?? new WireQuestAction[0], Drops = drops ?? new WireQuestDrop[0],
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

        static bool Meets(Rect a, Rect b) =>
            Mathf.Min(a.xMax, b.xMax) - Mathf.Max(a.xMin, b.xMin) > .01f && Mathf.Min(a.yMax, b.yMax) - Mathf.Max(a.yMin, b.yMin) > .01f;

        void AssertAnchors(string name, float x0, float y0, float x1, float y1)
        {
            var rt = (RectTransform)Req(Detail + "/" + name);
            Assert.That(rt.anchorMin.x, Is.EqualTo(x0).Within(.0005f), name + " anchorMin.x");
            Assert.That(rt.anchorMin.y, Is.EqualTo(y0).Within(.0005f), name + " anchorMin.y");
            Assert.That(rt.anchorMax.x, Is.EqualTo(x1).Within(.0005f), name + " anchorMax.x");
            Assert.That(rt.anchorMax.y, Is.EqualTo(y1).Within(.0005f), name + " anchorMax.y");
        }

        void AssertHeadings(string at)
        {
            RectTransform previous = null;
            foreach (var name in FieldNames)
            {
                var field = Field(name);
                if (!field.gameObject.activeSelf) continue;
                Assert.IsTrue(Value(name).gameObject.activeInHierarchy, name + " is shown, so it measures its true size");
                if (previous != null)
                    Assert.That(Local(previous).yMin, Is.GreaterThanOrEqualTo(Local(field).yMax - .01f), previous.name + " overlaps " + name + " " + at);
                previous = field;
                if (name == "QuestName") continue; // the name has no heading (P-UI-FLOWS-LEFTOVERS-1)
                var label = FieldLabel(name);
                Assert.IsTrue(label.gameObject.activeInHierarchy, name + " heading is shown");
                float need = label.GetPreferredValues(label.text, 0f, 0f).y;
                Assert.That(need, Is.LessThanOrEqualTo(label.rectTransform.rect.height + .5f), name + " heading needs " + need + " of " + label.rectTransform.rect.height + " " + at);
            }
        }

        List<string> ActiveStripLabels()
        {
            var labels = new List<string>();
            var content = Req(StripContent);
            foreach (Transform child in content)
                if (child.gameObject.activeSelf && child.GetComponent<Button>() != null)
                    labels.Add(child.GetComponentInChildren<TMP_Text>(true).text);
            return labels;
        }

        Button StripButton(int index)
        {
            var content = Req(StripContent);
            int seen = 0;
            foreach (Transform child in content)
                if (child.gameObject.activeSelf && child.GetComponent<Button>() != null && seen++ == index) return child.GetComponent<Button>();
            Assert.Fail("no active strip button " + index);
            return null;
        }

        List<WireCommand> Commands()
        {
            var all = new List<WireCommand>();
            foreach (byte[] frame in _transport.Sent)
                if (ClientMsg.Decode(frame) is ClientMsg.Input input) all.AddRange(input.Commands);
            return all;
        }

        int Interacts()
        {
            int n = 0;
            foreach (var c in Commands()) if (c.Kind == CommandKind.QuestInteract) n++;
            return n;
        }

        WireCommand LastCommand()
        {
            var all = Commands();
            Assert.Greater(all.Count, 0, "a command was sent");
            return all.Count > 0 ? all[all.Count - 1] : null;
        }

        void AssertLast(byte action, byte target, string what, QuestHandle? quest = null)
        {
            var last = LastCommand();
            Assert.IsNotNull(last, what);
            if (last == null) return;
            if (quest.HasValue) Assert.AreEqual(quest.Value, last.QuestRef, what + ": quest");
            Assert.AreEqual(CommandKind.QuestInteract, last.Kind, what + ": kind");
            Assert.AreEqual(action, last.QuestAction, what + ": action");
            Assert.AreEqual(target, last.QuestTarget, what + ": target");
        }

        List<WireQuestAction> OwnFor(QuestHandle quest, int n)
        {
            var list = new List<WireQuestAction> { Act(quest, 1, 0, "Abandon") };
            for (int i = 0; i < n; i++)
                list.Add(n == 60 && i == 59 ? Act(quest, 5, 0, "Last action") : Act(quest, 3, (byte)i, "Own " + i));
            return list;
        }

        static WireQuestAction Act(QuestHandle quest, byte action, byte target, string label) =>
            new WireQuestAction { Quest = quest, Action = action, Target = target, Label = label, Points = new[] { 1.0, 2.0 } };

        WireQuestRow LongRow(QuestHandle handle)
        {
            var row = Row(handle, _long, _long, _long, _long, false, true);
            row.GiverLabel = _long;
            return row;
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

        static WireQuestRow Row(QuestHandle handle, string name, string brief, string objective, string reward, bool canAccept, bool accepted = false)
        {
            return new WireQuestRow { Handle = handle, Def = "test", GiverX = 1, GiverY = 2, State = accepted ? QuestRowState.Open : QuestRowState.Preview,
                ExpiresAt = 12345.5, Participants = Array.Empty<WireQuestParticipant>(), DoneBy = null,
                CanAccept = canAccept, OwnerAccepted = accepted, OwnerCompleted = false, OwnerReady = accepted, OwnerProgress = accepted ? 2u : 0u,
                OwnerVisited = 0, Name = name, GiverLabel = "Wanderer & Co.", Brief = brief, Objective = objective, Reward = reward };
        }
    }
}
