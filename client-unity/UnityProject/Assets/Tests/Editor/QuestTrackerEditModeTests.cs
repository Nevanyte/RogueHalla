using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using Roguehalla.Client;
using Roguehalla.Net;
using Roguehalla.Net.Protocol;
using Roguehalla.Net.Ui;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Roguehalla.Client.Tests
{
    /// <summary>P-UI-QUEST-TRACKER-1 oracle: the quest tracker card under the plate chain. A whole HUD on a real MatchClient (the
    /// PlayerFlowsEditModeTests rig); the fade tests and the space tests drive the view's Refresh directly with an explicit dt.</summary>
    public sealed class QuestTrackerEditModeTests
    {
        sealed class Transport : ITransport
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

        static readonly Vector2[] Windows = { new Vector2(1280, 800), new Vector2(1920, 1080), new Vector2(2560, 1440) };
        const string LongName = "The Long Road To The Northern Watchtowers";
        const string LongObjective = "Carry the sealed letters to each of the three watchtowers before the horn sounds";

        GameObject _root, _events;
        EventSystem _priorEvents;
        MatchClient _client;
        MatchHud _hud;
        Session _session;
        Transport _transport;
        bool? _priorReduced;
        uint _tick = 2;
        double _serverMs = 50;
        readonly List<GameObject> _extra = new List<GameObject>();

        [SetUp]
        public void SetUp()
        {
            _priorReduced = UiMotion.ReducedOverride; UiMotion.ReducedOverride = true;
            _priorEvents = EventSystem.current;
            _events = new GameObject("Tracker events", typeof(UiInputRoutingEventSystem));
            var system = _events.GetComponent<UiInputRoutingEventSystem>(); system.enabled = false; system.enabled = true;
            EventSystem.current = system;
            _root = new GameObject("Tracker fixture");
            _client = _root.AddComponent<MatchClient>(); _client.enabled = false;
            _transport = new Transport();
            _session = new Session(_transport, new SessionOptions { Url = "ws://tracker/ws", Profile = "tracker", Now = () => 1000 });
            Invoke(_client, "InitializeLayoutReview", _session); _hud = _client.Hud;
            _session.Connect(); _transport.Open(); Welcome(_transport);
            Snapshot(MatchPhase.Active, 50);
            Invoke(_hud, "SetPlateMode", true);
            Layout(Windows[0]);
        }

        [TearDown]
        public void TearDown()
        {
            UiMotion.ReducedOverride = _priorReduced;
            foreach (var go in _extra) if (go != null) UnityEngine.Object.DestroyImmediate(go);
            if (_root != null) UnityEngine.Object.DestroyImmediate(_root);
            if (_events != null) UnityEngine.Object.DestroyImmediate(_events);
            if (_priorEvents != null) EventSystem.current = _priorEvents;
        }

        static void Welcome(Transport t) => t.Deliver(new ServerMsg.Welcome { Protocol = Wire.ProtocolVersion, MatchId = new byte[16],
            Session = new byte[16], PlayerId = 1, TickRate = 20, ServerMs = 50, Map = new MapBlob(32, 32, new byte[1024]) });

        void Snapshot(MatchPhase phase, double serverMs)
        {
            _serverMs = serverMs;
            _transport.Deliver(new ServerMsg.Snapshot { Tick = _tick++, ServerTimeOverrideMs = serverMs, Phase = (byte)phase,
                Me = new WireSelf { Id = 1, X = 64, Y = 64, Hp = 100, MaxHp = 100, Status = 1, Weapon = 0 } });
        }

        static WireQuestRow Quest(uint handle, double expiresAt, bool ready = false, string def = "hold_the_fort", uint progress = 12,
            string name = "Hold the Fort", string objective = "Stay inside the ring", bool accepted = true)
        {
            return new WireQuestRow
            {
                Handle = new QuestHandle(handle), Def = def, GiverX = 64, GiverY = 80, State = QuestRowState.Open, ExpiresAt = expiresAt,
                Name = name, GiverLabel = "Giver", Brief = "brief", Objective = objective, Reward = "reward",
                OwnerAccepted = accepted, OwnerReady = ready, OwnerProgress = progress,
                Participants = new[] { new WireQuestParticipant { Id = new RosterId(7), Accepted = accepted } },
            };
        }

        static QuestsState Quests(params WireQuestRow[] rows) => new QuestsState { Available = true, View = new WireQuestView {
            OwnerRoster = new RosterId(7), Roster = new[] { new WireQuestRosterEntry { Id = new RosterId(7), Label = "owner" } },
            Rows = rows, Objectives = new WireQuestObjective[0] } };

        void Deliver(params WireQuestRow[] rows) => _transport.Deliver(new ServerMsg.State { Tick = _tick++, Items = new StateItem[] { Quests(rows) } });

        // One HUD frame as TickFeatures runs it: the flows (the tracker), then the terrain row, then the readout strip.
        void Frame() { Invoke(_hud, "TickPlayerFlows"); Invoke(_hud, "TickTerrain"); Invoke(_hud, "RefreshReadouts"); }
        void Frames() { Frame(); Frame(); }

        // The rig is what it claims to be: a session in an Active match with the world HUD showing and no flow page open.
        void AssertRig()
        {
            Assert.AreEqual(MatchPhase.Active, _session.Phase, "rig: phase");
            Assert.IsTrue((bool)typeof(MatchHud).GetProperty("WorldHudVisible", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(_hud), "rig: the world HUD shows");
            Assert.IsFalse(_hud.QuestJournal.IsVisible, "rig: no journal");
        }

        RectTransform Tracker() { return (RectTransform)HudTestPaths.Find(_hud.transform, "MatchUI/HudStage/PlayerHUD/QuestTracker"); }
        QuestTrackerView TrackerView() { return (QuestTrackerView)Field(_hud, "_questTracker"); }
        RectTransform Stage() { return (RectTransform)HudTestPaths.Find(_hud.transform, "MatchUI/HudStage"); }

        TMP_Text Label(string box, int row = 0)
        {
            var root = Tracker(); Assert.IsNotNull(root, "the tracker root exists");
            var t = root.Find(box == "Title" || box == "More" ? "Header/" + box + "/Label" : "Row" + row + "/" + box + "/Label");
            Assert.IsNotNull(t, "label " + box + " of row " + row);
            return t.GetComponent<TMP_Text>();
        }

        void Layout(Vector2 window)
        {
            float factor = HudLayout.CanvasScalerFactor(window.x, window.y);
            ((HudStage)Field(_hud, "_stageScaler")).Apply(window.x, window.y, factor);
            ((HudStage)Field(_hud, "_chromeScaler")).Apply(window.x, window.y, factor);
        }

        struct Rect4 { public float Left, Top, Width, Height; public float Bottom => Top + Height; }

        static Rect4 Measure(RectTransform stage, RectTransform r)
        {
            var corners = new Vector3[4]; r.GetWorldCorners(corners);
            Vector3 min = stage.InverseTransformPoint(corners[0]), max = stage.InverseTransformPoint(corners[2]);
            float w = stage.rect.width, h = stage.rect.height;
            return new Rect4 { Left = min.x + w / 2f, Top = h / 2f - max.y, Width = max.x - min.x, Height = max.y - min.y };
        }

        // The bottom of the live chain: the plate, then the terrain row when it shows.
        float ChainBottom()
        {
            var stage = Stage();
            float bottom = Measure(stage, (RectTransform)Field(_hud, "_vitalsPanel")).Bottom;
            var terrain = (RectTransform)Field(_hud, "_terrainRow");
            if (terrain != null && terrain.gameObject.activeSelf) bottom = Math.Max(bottom, Measure(stage, terrain).Bottom);
            return bottom;
        }

        [TestCaseSource(nameof(Windows))]
        public void TheTrackerDocksUnderThePlateChain(Vector2 window)
        {
            Layout(window);
            Deliver(Quest(1, _serverMs + 134000));
            Frames();
            AssertRig();
            var stage = Stage();
            var want = HudRegions.TrackerDock(ChainBottom() + HudRegions.TrackerGap, 1).On(stage.rect.width, stage.rect.height);
            var root = Tracker();
            Assert.IsNotNull(root, "PlayerHUD/QuestTracker exists");
            Assert.IsTrue(root.gameObject.activeSelf, "the tracker shows for one accepted quest");
            var got = Measure(stage, root);
            Assert.AreEqual(want.Left, got.Left, .6f, "left");
            Assert.AreEqual(want.Top, got.Top, .6f, "top: the plate chain bottom plus the 8 gap");
            Assert.AreEqual(want.Width, got.Width, .6f, "width");
            Assert.AreEqual(want.Height, got.Height, .6f, "height: header plus one row");
        }

        [Test]
        public void ItFollowsThePlateWhenTheExtrasOpen()
        {
            Deliver(Quest(1, _serverMs + 134000));
            Frames();
            var stage = Stage(); var root = Tracker();
            Assert.IsNotNull(root); Assert.IsTrue(root.gameObject.activeSelf, "shows before the extras open");
            float topBefore = Measure(stage, root).Top, heightBefore = Measure(stage, root).Height, plateBefore = Measure(stage, (RectTransform)Field(_hud, "_vitalsPanel")).Bottom;
            Invoke(_hud, "ToggleExtras");
            Frames();
            float grew = Measure(stage, (RectTransform)Field(_hud, "_vitalsPanel")).Bottom - plateBefore;
            Assert.Greater(grew, 10f, "the plate grew when the extras opened");
            Assert.AreEqual(topBefore + grew, Measure(stage, root).Top, .6f, "the tracker's top moved by the plate's growth");
            Assert.AreEqual(heightBefore, Measure(stage, root).Height, .6f, "same rows, same height");
        }

        [Test]
        public void ARowShowsNameObjectiveClockAndProgress()
        {
            Deliver(Quest(1, _serverMs + 134000, progress: 12));
            Frames();
            Assert.AreEqual("Hold the Fort", Label("Name").text);
            Assert.AreEqual("Stay inside the ring", Label("Objective").text);
            Assert.AreEqual("2:14", Label("Clock").text);
            Assert.AreEqual("12 / 45 s", Label("Progress").text);
            Assert.AreEqual("QUESTS", Label("Title").text);
            Assert.AreEqual("J · log", Label("More").text);
        }

        [Test]
        public void TheClockRunsWithTheServerTime()
        {
            Deliver(Quest(1, _serverMs + 134000));
            Frames();
            string name = Label("Name").text, objective = Label("Objective").text, progress = Label("Progress").text;
            Assert.AreEqual("2:14", Label("Clock").text);
            Snapshot(MatchPhase.Active, _serverMs + 10000);   // the same quest view, ten server seconds later
            Frames();
            Assert.AreEqual("2:04", Label("Clock").text, "the clock follows the delivered server time");
            Assert.AreEqual(name, Label("Name").text); Assert.AreEqual(objective, Label("Objective").text); Assert.AreEqual(progress, Label("Progress").text);
            Assert.AreEqual("Hold the Fort", name);
        }

        [Test]
        public void UrgentAndReadyUseTheirTones()
        {
            var theme = UiTheme.Current;
            Deliver(Quest(1, _serverMs + 31000));
            Frames();
            Tone(theme.TextSecondary, Label("Clock").color, "31 s is calm");
            Deliver(Quest(1, _serverMs + 30000));
            Frames();
            Assert.AreEqual("0:30", Label("Clock").text);
            Tone(theme.Warning, Label("Clock").color, "30 s is urgent");
            Deliver(Quest(1, _serverMs + 10000));
            Frames();
            Assert.AreEqual("0:10", Label("Clock").text);
            Tone(theme.Danger, Label("Clock").color, "10 s is critical");
            Tone(theme.TextPrimary, Label("Name").color, "an unready name is primary");
            Deliver(Quest(1, _serverMs + 100000, ready: true));
            Frames();
            Assert.AreEqual("Turn in", Label("Progress").text);
            Tone(theme.Success, Label("Progress").color);
            Tone(theme.Success, Label("Name").color, "a ready name is success");
        }

        [Test]
        public void MoreQuestsThanRowsAreCounted()
        {
            Deliver(Quest(1, _serverMs + 10000), Quest(2, _serverMs + 20000), Quest(3, _serverMs + 30000), Quest(4, _serverMs + 40000), Quest(5, _serverMs + 50000));
            Frames();
            Assert.AreEqual(3, TrackerView().Rows, "three rows of five running quests");
            Assert.AreEqual("+2 more · J", Label("More").text);
            Assert.AreEqual(18f + 3 * 38f, Measure(Stage(), Tracker()).Height, .6f, "header plus three rows");
        }

        [Test]
        public void ItNeverTakesThePointer()
        {
            var root = Tracker();
            Assert.IsNotNull(root, "built with the HUD");
            Assert.IsNotNull(root.Find("Header/Title"), "the header is built from the start");
            Assert.IsNotNull(root.Find("Row2/Progress"), "three rows are built from the start");
            AssertNoPointer(root);
            Deliver(Quest(1, _serverMs + 90000), Quest(2, _serverMs + 20000));
            Frames();
            AssertNoPointer(root);
        }

        static void Tone(Color32 want, Color got, string message = null)
        {
            Color32 g = got;
            Assert.IsTrue(want.r == g.r && want.g == g.g && want.b == g.b && want.a == g.a, (message ?? "tone") + ": want " + want + " got " + g);
        }

        static void AssertNoPointer(RectTransform root)
        {
            var graphics = root.GetComponentsInChildren<Graphic>(true);
            Assert.Greater(graphics.Length, 0, "the card has a surface and labels");
            foreach (var g in graphics) Assert.IsFalse(g.raycastTarget, g.name + " is a raycast target");
            foreach (var group in root.GetComponentsInChildren<CanvasGroup>(true)) Assert.IsFalse(group.blocksRaycasts, group.name + " blocks raycasts");
        }

        [Test]
        public void ItLeavesAtOnceWithTheWorldHud()
        {
            Deliver(Quest(1, _serverMs + 134000));
            Frames();
            AssertRig();
            var root = Tracker();
            Assert.IsNotNull(root); Assert.IsTrue(root.gameObject.activeSelf); Assert.AreEqual(1, TrackerView().Rows);

            _hud.TacticalMap.SetOpen(true); _hud.TacticalMap.Tick();
            Frame();
            Assert.IsFalse(root.gameObject.activeSelf, "the map is open"); Assert.AreEqual(0, TrackerView().Rows);
            _hud.TacticalMap.SetOpen(false); _hud.TacticalMap.Tick();
            Frames();
            Assert.IsTrue(root.gameObject.activeSelf, "back with the map closed"); Assert.AreEqual(1, TrackerView().Rows);

            _hud.ShowPlayerFlow(0);
            Assert.IsTrue(_hud.QuestJournal.IsVisible, "rig: the journal opened");
            Frame();
            Assert.IsFalse(root.gameObject.activeSelf, "the journal is open"); Assert.AreEqual(0, TrackerView().Rows);
            Invoke(_hud, "ClosePlayerFlows");
            Frames();
            Assert.IsTrue(root.gameObject.activeSelf, "back with the journal closed");

            Snapshot(MatchPhase.Ended, _serverMs + 50);
            Frame();
            Assert.AreEqual(MatchPhase.Ended, _session.Phase);
            Assert.IsFalse(root.gameObject.activeSelf, "the phase is not Active"); Assert.AreEqual(0, TrackerView().Rows);
        }

        // The view driven directly: its own root under a stage child, an explicit dt, no HUD tick.
        QuestTrackerView DirectView()
        {
            var host = new GameObject("tracker host", typeof(RectTransform)); _extra.Add(host);
            host.transform.SetParent(Stage(), false);
            return new QuestTrackerView(host.transform);
        }

        [Test]
        public void ItFadesOutWhenTheLastQuestLeavesAndKeepsItsFace()
        {
            UiMotion.ReducedOverride = false;
            Deliver(Quest(1, _serverMs + 134000));
            var view = DirectView();
            view.Refresh(_session, true, 166f, 344f, .05f);
            view.Refresh(_session, true, 166f, 344f, .5f);
            Assert.AreEqual(1, view.Rows);
            var root = view.Root;
            Assert.IsTrue(root.gameObject.activeSelf);
            float height = root.rect.height;
            var name = root.Find("Row0/Name/Label").GetComponent<TMP_Text>(); var clock = root.Find("Row0/Clock/Label").GetComponent<TMP_Text>();
            Assert.AreEqual("Hold the Fort", name.text); Assert.AreEqual("2:14", clock.text);
            Assert.Greater(height, 50f, "header plus a row");

            Deliver();   // no quest any more
            view.Refresh(_session, true, 166f, 344f, .05f);
            Assert.AreEqual(0, view.Rows, "Rows reports 0 at once");
            Assert.IsTrue(root.gameObject.activeSelf, "the card is still fading out");
            Assert.AreEqual(height, root.rect.height, .01f, "the rect it last drew");
            Assert.AreEqual("Hold the Fort", name.text, "the face it last drew"); Assert.AreEqual("2:14", clock.text);
            view.Refresh(_session, true, 166f, 344f, .5f);
            Assert.IsFalse(root.gameObject.activeSelf, "gone after the out time");
            Assert.AreEqual(0, view.Rows);
        }

        [Test]
        public void ItFadesIn()
        {
            UiMotion.ReducedOverride = false;
            Deliver(Quest(1, _serverMs + 134000));
            var view = DirectView();
            view.Refresh(_session, true, 166f, 344f, .02f);
            Assert.IsTrue(view.Root.gameObject.activeSelf, "active from the first refresh with a quest");
            var group = view.Root.GetComponent<CanvasGroup>();
            Assert.IsNotNull(group); Assert.Less(group.alpha, 1f, "fading in");
            Assert.IsFalse(group.blocksRaycasts);
            view.Refresh(_session, true, 166f, 344f, .5f);
            Assert.AreEqual(1f, group.alpha, .001f, "fully in after the in time");
        }

        [Test]
        public void ANewSessionStartsEmpty()
        {
            Deliver(Quest(1, _serverMs + 134000));
            var view = DirectView();
            view.Refresh(_session, true, 166f, 344f, .05f);
            Assert.AreEqual(1, view.Rows); Assert.IsTrue(view.Root.gameObject.activeSelf);
            var before = new List<string>();
            foreach (var t in view.Root.GetComponentsInChildren<TMP_Text>(true)) if (!string.IsNullOrEmpty(t.text)) before.Add(t.text);
            Assert.Contains("Hold the Fort", before);

            var other = new Transport();
            var second = new Session(other, new SessionOptions { Url = "ws://tracker2/ws", Profile = "tracker2", Now = () => 1000 });
            second.Connect(); other.Open(); Welcome(other);
            other.Deliver(new ServerMsg.Snapshot { Tick = 2, ServerTimeOverrideMs = 50, Phase = (byte)MatchPhase.Active,
                Me = new WireSelf { Id = 1, X = 64, Y = 64, Hp = 100, MaxHp = 100, Status = 1, Weapon = 0 } });
            view.Refresh(second, true, 166f, 344f, .05f);
            Assert.IsFalse(view.Root.gameObject.activeSelf, "the new session has no quest");
            Assert.AreEqual(0, view.Rows);
            foreach (var t in view.Root.GetComponentsInChildren<TMP_Text>(true))
                foreach (string old in new[] { "Hold the Fort", "Stay inside the ring", "2:14", "12 / 45 s" })
                    Assert.AreNotEqual(old, t.text, t.name + " still holds a text of the old session");
        }

        [Test]
        public void FewerRowsWhenTheSpaceIsShort()
        {
            Deliver(Quest(1, _serverMs + 10000), Quest(2, _serverMs + 20000), Quest(3, _serverMs + 30000));
            var view = DirectView();
            view.Refresh(_session, true, 232f, 344f, .05f);
            Assert.AreEqual(2, view.Rows, "two rows fit between 232 and 344");
            Assert.IsTrue(view.Root.gameObject.activeSelf);
            Assert.AreEqual("+1 more · J", view.Root.Find("Header/More/Label").GetComponent<TMP_Text>().text);
            Assert.AreEqual(18f + 2 * 38f, view.Root.rect.height, .01f);
            view.Refresh(_session, true, 467f, 344f, .05f);
            Assert.AreEqual(0, view.Rows, "no room for one row");
            Assert.IsFalse(view.Root.gameObject.activeSelf, "the card is hidden");
            view.Refresh(_session, true, 467f, 618f, .05f);
            Assert.AreEqual(3, view.Rows);
            Assert.IsTrue(view.Root.gameObject.activeSelf);
            Assert.AreEqual(18f + 3 * 38f, view.Root.rect.height, .01f);
        }

        [Test]
        public void TheReadoutStripSetsTheFloor()
        {
            var stage = Stage();
            Deliver(Quest(1, _serverMs + 134000), Quest(2, _serverMs + 20000), Quest(3, _serverMs + 30000));
            Frames();
            float champTop = Measure(stage, (RectTransform)Field(_hud, "_championHud")).Top;
            Assert.AreEqual(champTop - 8f, (float)Invoke(_hud, "TrackerFloor"), .6f, "without readouts the floor is the champion panel's top minus 8");

            _transport.Deliver(new ServerMsg.State { Tick = _tick++, Items = new StateItem[] { new ReadoutsState { Rows = new[] {
                new WireReadout { Card = 80, Kind = 0, Value = 2, Max = 5 } } } } });
            Invoke(_hud, "ToggleExtras");
            Frames();
            var strip = (TMP_Text)Field(_hud, "_readoutStrip");
            Assert.IsNotNull(strip, "the readout strip was built"); Assert.IsFalse(string.IsNullOrEmpty(strip.text), "the strip has text");
            float stripTop = Measure(stage, strip.rectTransform).Top;
            Assert.AreEqual(stripTop - 8f, (float)Invoke(_hud, "TrackerFloor"), .6f, "with readouts the floor is the strip's top minus 8");
            var root = Tracker();
            if (root.gameObject.activeSelf)
                Assert.LessOrEqual(Measure(stage, root).Bottom, stripTop - 8f + .6f, "the card stays 8 above the strip");
            int fit = Math.Min(HudRegions.TrackerMaxRows, QuestTracker.RowsThatFit(ChainBottom() + HudRegions.TrackerGap, stripTop - 8f));
            Assert.GreaterOrEqual(fit, 1, "rig: at least one row fits above the strip");
            Assert.Less(fit, 3, "rig: the open extras leave fewer than three rows above the strip");
            Assert.AreEqual(fit, TrackerView().Rows, "as many rows as fit above the strip");
            Assert.IsTrue(root.gameObject.activeSelf);
        }

        [Test]
        public void ASteadyFrameAllocatesNothing()
        {
            Deliver(Quest(1, _serverMs + 134000), Quest(2, _serverMs + 40000));
            var view = DirectView();
            for (int i = 0; i < 6; i++) view.Refresh(_session, true, 166f, 344f, .016f);   // warm up
            Assert.AreEqual(2, view.Rows, "the card is up and drawing two rows");
            Assert.AreEqual("0:40", view.Root.Find("Row0/Clock/Label").GetComponent<TMP_Text>().text);
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 200; i++) view.Refresh(_session, true, 166f, 344f, .016f);
            long grown = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.LessOrEqual(grown, 0, "200 steady frames allocated " + grown + " bytes");
        }

        [Test]
        public void LongTextsAreShortenedAndFit()
        {
            Deliver(Quest(1, _serverMs + 134000, name: LongName, objective: LongObjective));
            var view = DirectView();
            view.Refresh(_session, true, 166f, 344f, .05f);
            Assert.AreEqual(1, view.Rows);
            var name = view.Root.Find("Row0/Name/Label").GetComponent<TMP_Text>();
            var objective = view.Root.Find("Row0/Objective/Label").GetComponent<TMP_Text>();
            Assert.AreEqual(26, name.text.Length); StringAssert.EndsWith("…", name.text); Assert.IsFalse(name.text.EndsWith(" …"));
            Assert.AreEqual(33, objective.text.Length, "the cut falls on a space, which is dropped before the ellipsis"); StringAssert.EndsWith("…", objective.text); Assert.IsFalse(objective.text.EndsWith(" …"));
            Assert.AreEqual(LongName.Substring(0, 25).TrimEnd() + "…", name.text);
            Assert.AreEqual(LongObjective.Substring(0, 33).TrimEnd() + "…", objective.text);

            var labels = view.Root.GetComponentsInChildren<TMP_Text>(true);
            Assert.AreEqual(2 + 3 * 4, labels.Length, "two header labels and four per row");
            foreach (float scale in new[] { 1f, 1.5f })
                foreach (var label in labels)
                {
                    var fit = label.GetComponent<MythicTextScale>();
                    Assert.IsNotNull(fit, label.transform.parent.name + " carries MythicTextScale");
                    fit.Apply(scale); fit.Refit();
                    Assert.AreNotEqual(TextOverflowModes.Ellipsis, label.overflowMode, label.transform.parent.name);
                    Assert.AreNotEqual(TextOverflowModes.Truncate, label.overflowMode, label.transform.parent.name);
                    if (!label.gameObject.activeInHierarchy || string.IsNullOrEmpty(label.text)) continue;
                    float box = ((RectTransform)label.transform.parent).rect.width;
                    Assert.LessOrEqual(label.GetPreferredValues(label.text, 0f, 0f).x, box + .5f, label.transform.parent.name + " at scale " + scale + " is wider than its box");
                    Assert.GreaterOrEqual(label.fontSize, 8f - .01f, label.transform.parent.name + " at scale " + scale + " shrank under 8");
                }
        }

        static object Field(object target, string name) => target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).GetValue(target);
        static object Invoke(object target, string method, params object[] args) => target.GetType()
            .GetMethod(method, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).Invoke(target, args);
    }
}
