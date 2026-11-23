using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Roguehalla.Net;
using Roguehalla.Net.Protocol;
using Roguehalla.Net.Ui;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Roguehalla.Client.Tests
{
    /// <summary>
    /// P-UI-TOAST-LANES-1: server notices are small toasts in two lanes (event: top centre under the timer; feedback: bottom centre
    /// above the ability row) and nothing is drawn in the middle of the screen. A real MatchClient on a fake transport; frames are
    /// the HUD's LateUpdate; time moves only through <see cref="MatchHud.NoticeDtOverride"/>.
    /// </summary>
    public sealed class ToastLanesEditModeTests
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

        struct Rect4 { public float Left, Top, Right, Bottom, Width, Height; }

        GameObject _root, _events;
        EventSystem _priorEvents;
        MatchClient _client;
        MatchHud _hud;
        Session _session;
        Transport _transport;
        uint _tick = 2;
        bool? _priorReduced;
        UiTheme _priorTheme;

        [SetUp]
        public void SetUp()
        {
            _priorEvents = EventSystem.current;
            _priorReduced = UiMotion.ReducedOverride;
            UiMotion.ReducedOverride = false;
            MatchHud.NoticeDtOverride = .05f;
            _events = new GameObject("Toast lanes events", typeof(UiInputRoutingEventSystem));
            var system = _events.GetComponent<UiInputRoutingEventSystem>(); system.enabled = false; system.enabled = true;
            EventSystem.current = system;
            _root = new GameObject("Toast lanes fixture");
            _client = _root.AddComponent<MatchClient>(); _client.enabled = false;
            _transport = new Transport();
            _session = new Session(_transport, new SessionOptions { Url = "ws://toasts/ws", Profile = "toasts", Now = () => 1000 });
            typeof(MatchClient).GetMethod("InitializeLayoutReview", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).Invoke(_client, new object[] { _session });
            _hud = _client.Hud;
            _session.Connect(); _transport.Open();
            _transport.Deliver(new ServerMsg.Welcome { Protocol = Wire.ProtocolVersion, MatchId = new byte[16], Session = new byte[16],
                PlayerId = 1, TickRate = 20, ServerMs = 50, Map = new MapBlob(32, 32, new byte[1024]) });
            _transport.Deliver(new ServerMsg.Snapshot { Tick = 1, ServerTimeOverrideMs = 50, Phase = 1,
                Me = new WireSelf { Id = 1, X = 64, Y = 64, Hp = 100, MaxHp = 100, Status = 1, Weapon = 0 } });
            ClearFirstRunHint();
            Layout(new Vector2(1422, 800));
        }

        [TearDown]
        public void TearDown()
        {
            MatchHud.NoticeDtOverride = null;
            UiMotion.ReducedOverride = _priorReduced;
            if (_root != null) UnityEngine.Object.DestroyImmediate(_root);
            if (_events != null) UnityEngine.Object.DestroyImmediate(_events);
            if (_priorEvents != null) EventSystem.current = _priorEvents;
        }

        // ---- helpers ------------------------------------------------------------------------------------------------------------

        object Get(string field) => typeof(MatchHud).GetField(field, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(_hud);
        /// <summary>Bind arms the first-run hint (no account flow in this rig, so it is armed and its expiry needs Time.unscaledTime, which does not move in EditMode): clear it so the feedback lane starts empty.</summary>
        void ClearFirstRunHint() { Set("_firstRunHint", false); Set("_feedback", ""); }
        void Set(string field, object value) => typeof(MatchHud).GetField(field, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(_hud, value);
        RectTransform Stage => (RectTransform)Get("_stage");
        float StageW => Stage.rect.width;
        float StageH => Stage.rect.height;

        void Layout(Vector2 window)
        {
            float factor = HudLayout.CanvasScalerFactor(window.x, window.y);
            ((HudStage)Get("_stageScaler")).Apply(window.x, window.y, factor);
            ((HudStage)Get("_chromeScaler")).Apply(window.x, window.y, factor);
        }

        /// <summary>One player-loop frame of the HUD: dirty flag set, LateUpdate run, the notice clock advanced by dt.</summary>
        void Frame(float dt = .05f)
        {
            MatchHud.NoticeDtOverride = dt;
            Set("_dirty", true);
            typeof(MatchHud).GetMethod("LateUpdate", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(_hud, null);
        }

        void Frames(int count, float dt = .05f) { for (int i = 0; i < count; i++) Frame(dt); }
        void Seconds(float seconds, float dt = .05f) => Frames((int)Math.Round(seconds / dt), dt);

        void Deliver(params WireEvent[] items) =>
            _transport.Deliver(new ServerMsg.Events { Tick = _tick++, List = new List<WireEvent>(items) });

        static WireEvent Toast(string text) => new WireEvent { Kind = EventKind.Toast, ToastText = text };
        static WireEvent Refused() => new WireEvent { Kind = EventKind.PickupRefused, PickupReason = 3 };

        RectTransform Lane(string name)
        {
            var t = Stage.Find(name);
            Assert.That(t, Is.Not.Null, name + " exists under the HUD stage");
            return (RectTransform)t;
        }

        List<RectTransform> Active(string lane)
        {
            var list = new List<RectTransform>();
            foreach (Transform child in Lane(lane))
                if (child.name.StartsWith("Toast") && child.gameObject.activeInHierarchy) list.Add((RectTransform)child);
            return list;
        }

        static TMP_Text TextOf(RectTransform pill)
        {
            var t = pill.Find("Text");
            Assert.That(t, Is.Not.Null, pill.name + " has a child 'Text'");
            return t.GetComponent<TMP_Text>();
        }

        RectTransform ActiveWithText(string lane, string text) =>
            Active(lane).FirstOrDefault(p => TextOf(p).text == text);

        Rect4 Measure(RectTransform r)
        {
            var corners = new Vector3[4]; r.GetWorldCorners(corners);
            Vector3 min = Stage.InverseTransformPoint(corners[0]), max = Stage.InverseTransformPoint(corners[2]);
            float w = Stage.rect.width, h = Stage.rect.height;
            return new Rect4 { Left = min.x + w / 2f, Right = w / 2f - max.x, Top = h / 2f - max.y, Bottom = min.y + h / 2f, Width = max.x - min.x, Height = max.y - min.y };
        }

        /// <summary>The bottom edge of the active top-centre pill (banner or timer), 0 when neither shows.</summary>
        float TopCentreBottom()
        {
            var banner = (RectTransform)Get("_bannerPill"); var phase = (RectTransform)Get("_phasePill");
            if (banner != null && banner.gameObject.activeSelf) return 12f + banner.sizeDelta.y;
            if (phase != null && phase.gameObject.activeSelf) return 12f + phase.sizeDelta.y;
            return 0f;
        }

        void AssertAt(RectTransform pill, HudRegion region, string what)
        {
            var want = region.On(StageW, StageH); var got = Measure(pill);
            Assert.That(got.Left, Is.EqualTo(want.Left).Within(.5f), what + " left");
            Assert.That(got.Top, Is.EqualTo(want.Top).Within(.5f), what + " top");
            Assert.That(got.Width, Is.EqualTo(want.Width).Within(.5f), what + " width");
            Assert.That(got.Height, Is.EqualTo(want.Height).Within(.5f), what + " height");
        }

        HudRegion EventAt(RectTransform pill, int slot) => HudRegions.EventPill(slot, Measure(pill).Width, TopCentreBottom());
        HudRegion FeedbackAt(RectTransform pill, int slot) => HudRegions.FeedbackPill(slot, Measure(pill).Width);

        static bool Overlap(Rect4 r, HudStageRect z, float stageW, float stageH) =>
            r.Left < z.Right && z.Left < stageW - r.Right && r.Top < z.Bottom && z.Top < stageH - r.Bottom;

        // ---- the tests ----------------------------------------------------------------------------------------------------------

        [Test]
        public void AQuestAcceptedToastIsOneSmallLineUnderTheTimer()
        {
            Deliver(Toast("Quest accepted: Letters"));
            Frames(2);
            var pills = Active("EventLane");
            Assert.AreEqual(1, pills.Count, "one active pill in the event lane");
            Assert.AreEqual("Quest accepted: Letters", TextOf(pills[0]).text);
            AssertAt(pills[0], EventAt(pills[0], 0), "event pill 0");
            Assert.That(Measure(pills[0]).Height, Is.EqualTo(24f).Within(.5f));
            var zone = HudRegions.CombatZone(StageW, StageH);
            Assert.LessOrEqual(Measure(pills[0]).Top + Measure(pills[0]).Height, zone.Top + .01f, "entirely above the combat zone");
            Assert.AreEqual(0, Active("FeedbackLane").Count);
        }

        [Test]
        public void NothingAppearsInTheMiddleOfTheScreen()
        {
            Deliver(Toast("Quest accepted: Letters"), Refused());
            Frames(2);
            Assert.AreEqual(1, Active("EventLane").Count, "set-up: an event pill shows");
            Assert.AreEqual(1, Active("FeedbackLane").Count, "set-up: a feedback pill shows");
            var zone = HudRegions.CombatZone(StageW, StageH);
            foreach (var laneName in new[] { "EventLane", "FeedbackLane" })
                foreach (var g in Lane(laneName).GetComponentsInChildren<Graphic>(false))
                    if (g.gameObject.activeInHierarchy)
                        Assert.IsFalse(Overlap(Measure(g.rectTransform), zone, StageW, StageH), laneName + "/" + g.name + " is in the combat zone");
            foreach (var t in _hud.GetComponentsInChildren<Transform>(true))
                Assert.IsFalse(t.name == "RecentServerNotices" || t.name == "ToastStack", t.name + " no longer exists");
            foreach (var t in _root.GetComponentsInChildren<Transform>(true))
                Assert.IsFalse(t.name == "RecentServerNotices" || t.name == "ToastStack", t.name + " no longer exists anywhere");
        }

        [Test]
        public void ToastsNeverTakeThePointer()
        {
            Deliver(Toast("Quest accepted: Letters"), Refused());
            Frames(2);
            Assert.AreEqual(1, Active("EventLane").Count, "set-up: an event pill shows");
            Assert.AreEqual(1, Active("FeedbackLane").Count, "set-up: a feedback pill shows");
            foreach (var laneName in new[] { "EventLane", "FeedbackLane" })
            {
                foreach (var g in Lane(laneName).GetComponentsInChildren<Graphic>(true))
                    Assert.IsFalse(g.raycastTarget, laneName + "/" + g.name + " takes the pointer");
                foreach (var c in Lane(laneName).GetComponentsInChildren<CanvasGroup>(true))
                    Assert.IsFalse(c.blocksRaycasts, laneName + "/" + c.name + " blocks raycasts");
            }
        }

        [Test]
        public void ARefusedPickupAnswersAboveTheAbilityRow()
        {
            Deliver(Refused());
            Frames(2);
            var pills = Active("FeedbackLane");
            Assert.AreEqual(1, pills.Count);
            Assert.AreEqual("Can't pick that up", TextOf(pills[0]).text);
            AssertAt(pills[0], FeedbackAt(pills[0], 0), "feedback pill 0");
            Assert.AreEqual(0, Active("EventLane").Count);
            CollectionAssert.Contains(_hud.ServerNotices.Entries.Select(x => x.Text).ToArray(), "Pickup refused by server (3)", "the Notices page keeps the reason code");
        }

        [Test]
        public void TheSameToastTwiceShowsOneLineTimesTwo()
        {
            Deliver(Toast("Slow down"));
            Frames(2);
            Deliver(Toast("Slow down"));
            Frames(2);
            var pills = Active("EventLane");
            Assert.AreEqual(1, pills.Count);
            Assert.AreEqual("Slow down x2", TextOf(pills[0]).text);
            Assert.AreEqual(2, _hud.ServerNotices.Entries.Count, "the history keeps both");
        }

        [Test]
        public void KillStackGrantsCountUpInOneLine()
        {
            string[] texts = { "Ember Imp: +1 kill stack · +1 Offense total", "Ember Imp: +2 kill stacks · +2 Offense total", "Ember Imp: +3 kill stacks · +3 Offense total" };
            foreach (var text in texts) { Deliver(Toast(text)); Frame(); }
            Frames(2);
            var pills = Active("EventLane");
            Assert.AreEqual(1, pills.Count, "one line counts up");
            Assert.AreEqual(texts[2], TextOf(pills[0]).text);
            StringAssert.DoesNotContain(" x", TextOf(pills[0]).text);
        }

        [Test]
        public void ToastsWaitWhileTheTabMapIsOpen()
        {
            Assert.IsNotNull(_hud.TacticalMap, "set-up: the HUD has a Tab map");
            _hud.TacticalMap.SetOpen(true);
            Assert.IsTrue(_hud.TacticalMap.IsOpen, "set-up: the map is open");
            Deliver(Toast("Slow down"));
            Seconds(10f, 1f);
            Assert.AreEqual(0, Active("EventLane").Count, "nothing drawn while the map is open");
            _hud.TacticalMap.SetOpen(false);
            Seconds(.3f);
            var pills = Active("EventLane");
            Assert.AreEqual(1, pills.Count, "the toast appears when the map closes");
            Assert.AreEqual("Slow down", TextOf(pills[0]).text);
            Seconds(1.9f);
            Assert.AreEqual(1, Active("EventLane").Count, "and lives its full hold (2.2 s)");
            Assert.GreaterOrEqual(Active("EventLane")[0].GetComponent<CanvasGroup>().alpha, .99f);
            Seconds(.5f);
            Assert.AreEqual(0, Active("EventLane").Count, "then fades out");
        }

        [Test]
        public void TheFirstRunHintIsAStickyFeedbackLine()
        {
            const string hint = "Press WASD to move";
            Set("_feedback", hint);
            Frames(2);
            var pills = Active("FeedbackLane");
            Assert.AreEqual(1, pills.Count);
            Assert.AreEqual(hint, TextOf(pills[0]).text);
            AssertAt(pills[0], FeedbackAt(pills[0], 0), "the hint in slot 0");
            Seconds(10f, 1f);
            Assert.IsNotNull(ActiveWithText("FeedbackLane", hint), "stays for longer than any hold");
            Deliver(Refused());
            Frames(3);
            Assert.AreEqual(2, Active("FeedbackLane").Count);
            AssertAt(ActiveWithText("FeedbackLane", hint), FeedbackAt(ActiveWithText("FeedbackLane", hint), 0), "the hint still in slot 0");
            var refusal = ActiveWithText("FeedbackLane", "Can't pick that up");
            Assert.IsNotNull(refusal);
            AssertAt(refusal, FeedbackAt(refusal, 1), "the refusal above it");
            Set("_feedback", "");
            Seconds(.6f);
            Assert.IsNull(ActiveWithText("FeedbackLane", hint), "clearing the hint removes the line");
        }

        [Test]
        public void TheHistoryKeepsTheNewestTwoHundred()
        {
            var batch = new List<WireEvent>();
            for (int i = 1; i <= 250; i++) batch.Add(Toast("t" + i));
            Deliver(batch.ToArray());
            Assert.AreEqual(200, _hud.ServerNotices.Entries.Count);
            Assert.AreEqual("t51", _hud.ServerNotices.Entries[0].Text);
            Assert.AreEqual("t250", _hud.ServerNotices.Entries[199].Text);
            _hud.ShowPlayerFlow(2);
            string text = _hud.ServerNotices.Root.Find("History/Viewport/Content").GetComponent<TMP_Text>().text;
            StringAssert.StartsWith("51 · t51", text);
            StringAssert.EndsWith("250 · t250", text);
        }

        [Test]
        public void ASteadyFrameWithToastsAllocatesNothing()
        {
            Deliver(Toast("Slow down"), Refused());
            Frames(4);
            Assert.AreEqual(1, Active("EventLane").Count, "set-up: an event pill shows");
            Assert.AreEqual(1, Active("FeedbackLane").Count, "set-up: a feedback pill shows");
            var tick = (Action)Delegate.CreateDelegate(typeof(Action), _hud, typeof(MatchHud).GetMethod("TickNotices", BindingFlags.Instance | BindingFlags.NonPublic));
            MatchHud.NoticeDtOverride = .001f;
            for (int i = 0; i < 10; i++) tick();                       // warm up
            AllocationMeter.AssertNone(tick, 200, "a steady notice tick");
            Assert.AreEqual(1, Active("EventLane").Count, "the pill is still there");
        }

        [Test]
        public void ALongToastShrinksAndStaysInItsLane()
        {
            string text = "Quest accepted: " + string.Concat(Enumerable.Repeat("wolfpack ", 14));
            Assert.GreaterOrEqual(text.Length, 120);
            text = text.Substring(0, 120);
            Deliver(Toast(text));
            Frames(3);
            var pills = Active("EventLane");
            Assert.AreEqual(1, pills.Count);
            var label = TextOf(pills[0]);
            Assert.AreEqual(NoticeRouter.MaxChars, label.text.Length);
            Assert.IsTrue(label.text.EndsWith("…"));
            Assert.AreEqual(NoticeRouter.Short(text), label.text);
            Assert.That(Measure(pills[0]).Width, Is.EqualTo(HudRegions.LaneWidth(StageW)).Within(.5f));
            Assert.GreaterOrEqual(label.fontSize, 9f - .01f, "the fit stops at 9");
            Assert.LessOrEqual(label.GetPreferredValues(label.text, 0f, 0f).x, label.rectTransform.rect.width + .5f, "the text fits its box");
            foreach (var laneName in new[] { "EventLane", "FeedbackLane" })
                foreach (var t in Lane(laneName).GetComponentsInChildren<TMP_Text>(true))
                    Assert.IsFalse(t.overflowMode == TextOverflowModes.Ellipsis || t.overflowMode == TextOverflowModes.Truncate, t.name + " cuts its text");
        }

        [Test]
        public void AnEventPillClearsThePlateOnANarrowStage()
        {
            string text = "Quest accepted: " + string.Join(" ", Enumerable.Repeat("wolfpack", 5));
            Assert.AreEqual(60, text.Length);
            Layout(new Vector2(700, 600));
            Deliver(Toast(text));
            Frames(3);
            var pills = Active("EventLane");
            Assert.AreEqual(1, pills.Count);
            var pill = Measure(pills[0]);
            float plateRight = 12f + HudLayout.PlateWidth(StageW);
            Assert.GreaterOrEqual(pill.Left, plateRight + 8f - .5f, "the pill keeps 8 units from the plate's right edge");
            var plate = Measure((RectTransform)Get("_vitalsPanel"));
            bool overlap = pill.Left < StageW - plate.Right && plate.Left < StageW - pill.Right && pill.Top < plate.Top + plate.Height && plate.Top < pill.Top + pill.Height;
            Assert.IsFalse(overlap, "the pill does not intersect the plate");
            Assert.LessOrEqual(pill.Right, StageW, "and stays on the stage");

            Layout(new Vector2(1280, 800));
            Frames(3);
            pill = Measure(Active("EventLane")[0]);
            Assert.That(pill.Left, Is.EqualTo(pill.Right).Within(.5f), "centred on a wide stage");
        }

        [Test]
        public void ANewSessionStartsWithEmptyLanes()
        {
            Deliver(Toast("Slow down"), Refused());
            Frames(2);
            Assert.AreEqual(1, Active("EventLane").Count, "set-up: an event pill shows");
            Assert.AreEqual(1, Active("FeedbackLane").Count, "set-up: a feedback pill shows");
            var otherTransport = new Transport();
            var other = new Session(otherTransport, new SessionOptions { Url = "ws://toasts/ws", Profile = "other", Now = () => 1000 });
            other.Connect(); otherTransport.Open();
            otherTransport.Deliver(new ServerMsg.Welcome { Protocol = Wire.ProtocolVersion, MatchId = new byte[16], Session = new byte[16],
                PlayerId = 2, TickRate = 20, Map = new MapBlob(32, 32, new byte[1024]) });
            try
            {
                _hud.Bind(other);
                ClearFirstRunHint();
                Frames(2);
                Assert.AreEqual(0, Active("EventLane").Count);
                Assert.AreEqual(0, Active("FeedbackLane").Count);
                Assert.AreEqual(0, _hud.ServerNotices.Entries.Count);
            }
            finally { _hud.Bind(_session); other.Leave(); }
        }
    }
}
