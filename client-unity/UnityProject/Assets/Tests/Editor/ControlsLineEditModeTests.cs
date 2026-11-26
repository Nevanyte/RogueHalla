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
    /// P-UI-CONTROLS-LINE-1, layout half: the permanent line of keys under the action row is not built, and nothing under the row
    /// reaches into the 14 HUD units above the stage bottom. The real legacy chrome and combat HUD at three window sizes.
    /// </summary>
    public sealed class ControlsLineLayoutEditModeTests
    {
        GameObject _canvas, _hudOwner, _settingsOwner;
        MatchHud _hud;
        RectTransform _stage, _chrome;
        TacticalMapController _map;
        ModernSettingsView _settings;
        HelpGuideView _help;
        UiDisplayPreferences _prefs;
        UiTheme _savedTheme;

        [SetUp]
        public void SetUp()
        {
            _savedTheme = UiTheme.Current;
            Build(UiTheme.Placeholder());
        }

        void Build(UiTheme theme)
        {
            UiTheme.Current = theme;
            _canvas = new GameObject("Controls line layout canvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            _canvas.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
            _hudOwner = new GameObject("Controls line HUD owner");
            _hud = _hudOwner.AddComponent<MatchHud>();
            Set("_root", _canvas.GetComponent<RectTransform>());
            Invoke("EnsureStage");
            Invoke("CreateTopAndToastPills");
            Invoke("CreateLegacyChrome");
            Invoke("CreateCombatHud");
            _stage = (RectTransform)Get("_stage");
            _chrome = (RectTransform)Get("_chromeStage");
            _prefs = new UiDisplayPreferences();
            Set("_uiPrefs", _prefs);
            var rewards = (RectTransform)new GameObject("Rewards", typeof(RectTransform)).transform;
            rewards.SetParent(_stage, false);
            Set("_rewards", rewards);

            _map = _hudOwner.AddComponent<TacticalMapController>();
            _map.Initialize(null, _canvas.transform, _prefs, _stage);
            Set("_map", _map);

            _settingsOwner = new GameObject("Controls line settings owner");
            var showcase = _settingsOwner.AddComponent<ShowcaseSettings>();
            typeof(ShowcaseSettings).GetField("_textScale", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(showcase, 1f);
            _settings = _settingsOwner.AddComponent<ModernSettingsView>();
            _settings.Initialize(_chrome, showcase, _map, _prefs, null, () => { });
            Set("_modernSettings", _settings);
            _help = _settingsOwner.AddComponent<HelpGuideView>();
            _help.Initialize(_chrome, null);
            Set("_helpGuide", _help);
            Set("_stage", _stage);
            var launcher = HudUi.Button(_stage, "Journal", "Quests · J", HudAnchor.BottomLeft, 12f, 12f, 84f, 22f, null);
            Set("_journalButton", launcher);
        }

        [TearDown]
        public void TearDown()
        {
            if (_canvas != null) UnityEngine.Object.DestroyImmediate(_canvas);
            if (_hudOwner != null) UnityEngine.Object.DestroyImmediate(_hudOwner);
            if (_settingsOwner != null) UnityEngine.Object.DestroyImmediate(_settingsOwner);
            UiTheme.Current = _savedTheme;
        }

        static readonly Vector2[] Windows = { new Vector2(1280, 800), new Vector2(1920, 1080), new Vector2(2560, 1440) };

        void Layout(Vector2 window)
        {
            float factor = HudLayout.CanvasScalerFactor(window.x, window.y);
            ((HudStage)Get("_stageScaler")).Apply(window.x, window.y, factor);
            ((HudStage)Get("_chromeScaler")).Apply(window.x, window.y, factor);
            _settings.SetVisible(true);
            _settings.ApplyViewport(HudLayout.Viewport(window.x, window.y));
        }

        /// <summary>Insets (left, top, right, bottom) and size of a rect inside the stage, in HUD units.</summary>
        static Rect4 Measure(RectTransform stage, RectTransform r)
        {
            var corners = new Vector3[4]; r.GetWorldCorners(corners);
            Vector3 min = stage.InverseTransformPoint(corners[0]), max = stage.InverseTransformPoint(corners[2]);
            float w = stage.rect.width, h = stage.rect.height;
            return new Rect4 { Left = min.x + w / 2f, Right = w / 2f - max.x, Top = h / 2f - max.y, Bottom = min.y + h / 2f, Width = max.x - min.x, Height = max.y - min.y };
        }

        struct Rect4 { public float Left, Top, Right, Bottom, Width, Height; }

        RectTransform Child(Transform root, string path) { var t = root.Find(path); Assert.That(t, Is.Not.Null, path); return (RectTransform)t; }

        static string Path(Component c)
        {
            string path = c.name;
            for (var t = c.transform.parent; t != null; t = t.parent) path = t.name + "/" + path;
            return path;
        }

        object Get(string name) => typeof(MatchHud).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(_hud);
        void Set(string name, object value) => typeof(MatchHud).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(_hud, value);
        void Invoke(string name) => typeof(MatchHud).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(_hud, null);

        [TestCaseSource(nameof(Windows))]
        public void TheControlsLineIsGone(Vector2 window)
        {
            Layout(window);
            var bar = Child(_stage, "PlayerHUD/CombatActions");
            Assert.That(Measure(_stage, bar).Bottom, Is.EqualTo(14f).Within(.01f), "set-up: the bar sits 14 from the bottom");
            var consumables = Measure(_stage, Child(bar, "Consumables"));
            Assert.That(consumables.Bottom, Is.EqualTo(14f).Within(.01f), "set-up: Consumables bottom");
            Assert.That(consumables.Height, Is.EqualTo(28f).Within(.01f), "set-up: Consumables height");

            Assert.That(bar.Find("ControlsLine"), Is.Null, "no ControlsLine child of the bar");
            var named = new List<string>();
            foreach (var root in new[] { _stage, _chrome })
                foreach (var t in root.GetComponentsInChildren<Transform>(true))
                    if (t.name == "ControlsLine") named.Add(Path(t));
            Assert.That(named.Count, Is.EqualTo(0), "no Transform named ControlsLine under the stage or the chrome: " + string.Join("; ", named));

            var texts = new List<string>();
            foreach (var text in bar.GetComponentsInChildren<TMP_Text>(true))
                foreach (string banned in new[] { "WASD", "Click attack", "Shift/Q" })
                    if (text.text != null && text.text.Contains(banned)) { texts.Add(Path(text) + " = " + text.text); break; }
            Assert.That(texts.Count, Is.EqualTo(0), "no label under the bar repeats the keys: " + string.Join("; ", texts));
        }

        [TestCaseSource(nameof(Windows))]
        public void NothingUnderTheRowCrossesTheConsumablesBottom(Vector2 window)
        {
            Layout(window);
            var bar = Child(_stage, "PlayerHUD/CombatActions");
            var rects = bar.GetComponentsInChildren<RectTransform>(true).Where(r => r != bar).ToArray();
            Assert.That(rects.Length, Is.GreaterThan(20), "set-up: the walk finds the rects of the row (an empty walk cannot pass)");
            var offenders = new List<string>();
            foreach (var r in rects)
            {
                float bottom = Measure(_stage, r).Bottom;
                if (bottom < HudRegions.ConsumablesBottom - .01f) offenders.Add(Path(r) + " bottom " + bottom.ToString("0.##"));
            }
            Assert.That(offenders.Count, Is.EqualTo(0), "rects under the bar below " + HudRegions.ConsumablesBottom + " units: " + string.Join("; ", offenders));
        }
    }

    /// <summary>
    /// P-UI-CONTROLS-LINE-1, hint half: the first-run hint names moving and attacking and the help key, and fits its pill in the
    /// feedback lane at text scales 1 and 1.5. A real MatchClient on a fake transport; frames are the HUD's LateUpdate; time moves
    /// only through <see cref="MatchHud.NoticeDtOverride"/>.
    /// </summary>
    public sealed class ControlsLineHintEditModeTests
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

        const string Hint = "WASD or right-click move · Left-click attack · H help";

        GameObject _root, _events;
        EventSystem _priorEvents;
        MatchClient _client;
        MatchHud _hud;
        Session _session;
        Transport _transport;
        bool? _priorReduced;
        ShowcaseSettings _settings;

        [SetUp]
        public void SetUp()
        {
            _priorEvents = EventSystem.current;
            _priorReduced = UiMotion.ReducedOverride;
            UiMotion.ReducedOverride = false;
            MatchHud.NoticeDtOverride = .05f;
            _events = new GameObject("Controls line events", typeof(UiInputRoutingEventSystem));
            var system = _events.GetComponent<UiInputRoutingEventSystem>(); system.enabled = false; system.enabled = true;
            EventSystem.current = system;
            _root = new GameObject("Controls line fixture");
            _client = _root.AddComponent<MatchClient>(); _client.enabled = false;
            _transport = new Transport();
            _session = new Session(_transport, new SessionOptions { Url = "ws://controls/ws", Profile = "controls", Now = () => 1000 });
            typeof(MatchClient).GetMethod("InitializeLayoutReview", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).Invoke(_client, new object[] { _session });
            _hud = _client.Hud;
            _session.Connect(); _transport.Open();
            _transport.Deliver(new ServerMsg.Welcome { Protocol = Wire.ProtocolVersion, MatchId = new byte[16], Session = new byte[16],
                PlayerId = 1, TickRate = 20, ServerMs = 50, Map = new MapBlob(32, 32, new byte[1024]) });
            _transport.Deliver(new ServerMsg.Snapshot { Tick = 1, ServerTimeOverrideMs = 50, Phase = 1,
                Me = new WireSelf { Id = 1, X = 64, Y = 64, Hp = 100, MaxHp = 100, Status = 1, Weapon = 0 } });
            // The hint is the subject: it is not cleared here.
            Assert.That((bool)Get("_firstRunHint"), Is.True, "rig: Bind armed the first-run hint");
            Assert.That((string)Get("_feedback"), Is.EqualTo(HintConst()), "rig: the hint is the feedback line");
            _settings = (ShowcaseSettings)Get("_showcaseSettings");
            Assert.That(_settings, Is.Not.Null, "rig: the HUD built its settings component");
            if (ShowcaseSettings.Current == null)
                typeof(ShowcaseSettings).GetMethod("Awake", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(_settings, null);
            Assert.That(ShowcaseSettings.Current, Is.SameAs(_settings), "rig: the current settings are the HUD's (another is a leftover of another class)");
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
        void Set(string field, object value) => typeof(MatchHud).GetField(field, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(_hud, value);
        RectTransform Stage => (RectTransform)Get("_stage");
        float StageW => Stage.rect.width;
        float StageH => Stage.rect.height;

        static string HintConst() =>
            (string)typeof(MatchHud).GetField("FirstRunHint", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic).GetRawConstantValue();

        void Layout(Vector2 window)
        {
            float factor = HudLayout.CanvasScalerFactor(window.x, window.y);
            ((HudStage)Get("_stageScaler")).Apply(window.x, window.y, factor);
            ((HudStage)Get("_chromeScaler")).Apply(window.x, window.y, factor);
        }

        /// <summary>The text scale changes the way the running client applies it: the setting is written, Changed is raised (no PlayerPrefs), then frames run.</summary>
        void SetScale(float scale)
        {
            typeof(ShowcaseSettings).GetField("_textScale", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(_settings, scale);
            var changed = (Action)typeof(ShowcaseSettings).GetField("Changed", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(_settings);
            changed?.Invoke();
            Frames(3);
        }

        /// <summary>One player-loop frame of the HUD: dirty flag set, LateUpdate run, the notice clock advanced by dt.</summary>
        void Frame(float dt = .05f)
        {
            MatchHud.NoticeDtOverride = dt;
            Set("_dirty", true);
            typeof(MatchHud).GetMethod("LateUpdate", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(_hud, null);
        }

        void Frames(int count, float dt = .05f) { for (int i = 0; i < count; i++) Frame(dt); }

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

        Rect4 Measure(RectTransform r)
        {
            var corners = new Vector3[4]; r.GetWorldCorners(corners);
            Vector3 min = Stage.InverseTransformPoint(corners[0]), max = Stage.InverseTransformPoint(corners[2]);
            float w = Stage.rect.width, h = Stage.rect.height;
            return new Rect4 { Left = min.x + w / 2f, Right = w / 2f - max.x, Top = h / 2f - max.y, Bottom = min.y + h / 2f, Width = max.x - min.x, Height = max.y - min.y };
        }

        void AssertAt(RectTransform pill, HudRegion region, string what)
        {
            var want = region.On(StageW, StageH); var got = Measure(pill);
            Assert.That(got.Left, Is.EqualTo(want.Left).Within(.5f), what + " left");
            Assert.That(got.Top, Is.EqualTo(want.Top).Within(.5f), what + " top");
            Assert.That(got.Width, Is.EqualTo(want.Width).Within(.5f), what + " width");
            Assert.That(got.Height, Is.EqualTo(want.Height).Within(.5f), what + " height");
        }

        HudRegion FeedbackAt(RectTransform pill, int slot) => HudRegions.FeedbackPill(slot, Measure(pill).Width);

        // ---- the tests ----------------------------------------------------------------------------------------------------------

        [Test]
        public void TheFirstRunHintNamesMoveAndAttack()
        {
            Frames(2);
            var pills = Active("FeedbackLane");
            Assert.That(pills.Count, Is.EqualTo(1), "one active pill in the feedback lane");
            var pill = pills[0];
            string text = TextOf(pill).text;
            Assert.That(text, Is.EqualTo(Hint), "the hint names moving, attacking and the help key");
            Assert.That(HintConst(), Is.EqualTo(Hint), "the constant FirstRunHint");
            foreach (string banned in new[] { "potion", "Wish", "Shift", "Space" })
                Assert.That(text.Contains(banned), Is.False, "the row's key caps are not repeated: " + banned);
            AssertAt(pill, FeedbackAt(pill, 0), "feedback pill 0");
        }

        [TestCase(1f, 13f)]
        [TestCase(1.5f, 15f)]
        public void TheHintFitsItsPillAtScaleOneAndOnePointFive(float scale, float leastSize)
        {
            Frames(2);
            SetScale(scale);
            var pills = Active("FeedbackLane");
            Assert.That(pills.Count, Is.EqualTo(1), "one active pill in the feedback lane");
            var pill = pills[0];
            var label = TextOf(pill);
            Assert.That(label.text, Is.EqualTo(HintConst()), "the pill shows the constant");
            Assert.That(label.fontSize, Is.GreaterThanOrEqualTo(leastSize - .01f), "the hint keeps a readable size at scale " + scale);
            float preferred = label.GetPreferredValues(label.text, 0f, 0f).x;
            Assert.That(preferred, Is.LessThanOrEqualTo(label.rectTransform.rect.width * MythicTextScale.WidthShare + .01f), "the text fits 88 percent of its label width");
            Assert.That(Measure(pill).Width, Is.LessThanOrEqualTo(HudRegions.LaneWidth(StageW) + .5f), "the pill is inside the lane");
            Assert.That(label.overflowMode, Is.Not.EqualTo(TextOverflowModes.Ellipsis).And.Not.EqualTo(TextOverflowModes.Truncate), "no ellipsis or truncation");
        }
    }
}
