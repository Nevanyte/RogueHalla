using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using Roguehalla.Net;
using Roguehalla.Net.Protocol;
using Roguehalla.Net.Ui;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using MapRect = Roguehalla.Net.Ui.MapRect;   // Net.Protocol has a MapRect too: the map screen's is meant here

namespace Roguehalla.Client.Tests
{
    /// <summary>
    /// P-UI-TABMAP-SHEET-1: the Tab map is one sheet with a header (heading, the HUD's phase line, the hint), the live rects equal
    /// <see cref="MapScreenLayout"/>, one method decides the panel, and while the map is open only what belongs shows around it.
    /// The real TacticalMapController is laid out in the stage a 1280x720, 1920x1080, 1024x768 and 640x480 window give; the clock is the
    /// controller's <see cref="TacticalMapController.SecondsClock"/> seam. The HUD tests use the PlayerFlows rig (a real MatchHud).
    /// </summary>
    public sealed class TabMapSheetEditModeTests
    {
        sealed class RigTransport : ITransport
        {
            public event Action Opened;
            public event Action<byte[]> Received;
            public event Action<string> Closed;
            public void Connect(string url) { }
            public void Send(byte[] frame) { }
            public void Close() { Closed?.Invoke("closed"); }
            public void Poll() { }
            public void Open() { Opened?.Invoke(); }
            public void Deliver(ServerMsg message) { Received?.Invoke(message.Encode()); }
        }

        static readonly Vector2[] WideWindows = { new Vector2(1280, 720), new Vector2(1920, 1080), new Vector2(1024, 768) };
        static readonly Vector2 StackedWindow = new Vector2(640, 480);

        GameObject _canvas, _stageObject, _clientObject, _controllerObject, _arenaObject, _rigRoot, _events;
        Fix3Transport _transport;
        RigTransport _rigTransport;
        Session _session;
        TacticalMapController _controller;
        MatchClient _client;
        MatchHud _hud;
        RectTransform _stage;
        EventSystem _priorEvents;
        UiDisplayPreferences _prefs;
        bool _priorToggle;
        Func<double> _priorClock;
        bool? _priorReduced;
        double _now = 100d;

        [SetUp]
        public void SetUp()
        {
            _priorClock = TacticalMapController.SecondsClock;
            TacticalMapController.SecondsClock = () => _now;
            _priorReduced = UiMotion.ReducedOverride;
            WorldLabelVisibility.Hidden = false;
        }

        [TearDown]
        public void TearDown()
        {
            try { Reset(); }
            finally
            {
                TacticalMapController.SecondsClock = _priorClock;
                UiMotion.ReducedOverride = _priorReduced;
                WorldLabelVisibility.Hidden = false;
                if (_prefs != null) _prefs.SetMapToggle(_priorToggle);
                if (_priorEvents != null) EventSystem.current = _priorEvents;
            }
        }

        /// <summary>Takes the built fixture down (a test that builds a second window calls this between them).</summary>
        void Reset()
        {
            try
            {
                if (_controller != null) { _controller.SetToggleMode(false); _controller.Bind(null); }
                if (_session != null) _session.Leave();
            }
            finally
            {
                WorldLabelVisibility.Hidden = false;
                foreach (var go in new[] { _controllerObject, _clientObject, _arenaObject, _stageObject, _canvas, _rigRoot, _events })
                    if (go != null) UnityEngine.Object.DestroyImmediate(go);
                _controller = null; _session = null;
            }
        }

        // ---- fixtures ----------------------------------------------------------------------------------------------------------------

        static Vector2 StageUnits(Vector2 window)
        {
            float scale = Mathf.Clamp(Mathf.Min(window.x / 1280f, window.y / 800f), .8f, 1.5f);
            return new Vector2(window.x / scale, window.y / scale);
        }

        void BuildController(Vector2 window, bool planning = false)
        {
            _canvas = new GameObject("sheet canvas", typeof(RectTransform), typeof(Canvas), typeof(GraphicRaycaster));
            _stageObject = new GameObject("sheet stage", typeof(RectTransform));
            _stage = (RectTransform)_stageObject.transform; _stage.SetParent(_canvas.transform, false);
            _stage.anchorMin = _stage.anchorMax = _stage.pivot = new Vector2(.5f, .5f); _stage.sizeDelta = StageUnits(window);
            _transport = new Fix3Transport();
            _session = Fix3Transport.Join(_transport, new MapBlob(4, 4, new byte[16]),
                new WireSelf { Id = 1, Faction = 4, X = 48, Y = 48, Hp = 100, MaxHp = 100, Status = 1, Weapon = (byte)WeaponId.Wand });
            _clientObject = new GameObject("sheet client");
            _client = _clientObject.AddComponent<MatchClient>(); _client.enabled = false; _client.Url = "ws://localhost/sheet"; _client.Profile = "fixture";
            _controllerObject = new GameObject("sheet controller");
            _controller = _controllerObject.AddComponent<TacticalMapController>();
            if (_prefs == null) { _prefs = new UiDisplayPreferences(); _priorToggle = _prefs.MapToggle; }
            _controller.Initialize(_client, _canvas.transform, _prefs, _stage);
            _controller.SetToggleMode(false);
            _controller.Bind(_session);
            if (planning)
            {
                _transport.Deliver(new ServerMsg.Snapshot { Tick = 2, ServerTimeOverrideMs = 100, Phase = (byte)MatchPhase.Planning,
                    Me = new WireSelf { Id = 1, X = 48, Y = 48, Hp = 100, MaxHp = 100, Status = 1, Weapon = 0 } });
                _transport.Deliver(new ServerMsg.State { Tick = 2, Items = new StateItem[] { new PlanState { Value = new WirePlan {
                    EndsAt = 10000, Spawn0 = 5, Spawn1 = 8, Pick = 1, Boon = 0, Boons = new[] { "First boon", "Second boon" },
                    Option0 = new WirePlanOption { Index = 5, X = 64, Y = 64, Zone = 1 },
                    Option1 = new WirePlanOption { Index = 8, X = 96, Y = 96, Zone = 2 } } } } });
            }
            else _controller.SetOpen(true);
            Frame();
            if (planning) Assert.That(_controller.IsPlanning, Is.True, "set-up: the planning screen");
            else Assert.That(_controller.IsOpen, Is.True, "set-up: the Tab map is open");
        }

        /// <summary>One frame: the clock moves a second (past the paint throttle), the controller ticks, the canvas lays out.</summary>
        void Frame()
        {
            _now += 1d;
            _controller.Tick();
            Canvas.ForceUpdateCanvases();
        }

        RectTransform Panel => (RectTransform)_canvas.transform.Find("TacticalMap");

        RectTransform Part(string path)
        {
            var found = Panel.Find(path);
            Assert.That(found, Is.Not.Null, "the panel has " + path);
            return (RectTransform)found;
        }

        /// <summary>A rect in HUD units from the panel's top-left corner, as <see cref="MapScreenLayout"/> states it.</summary>
        MapRect Measure(RectTransform rect)
        {
            var corners = new Vector3[4]; rect.GetWorldCorners(corners);
            Vector3 min = Panel.InverseTransformPoint(corners[0]), max = Panel.InverseTransformPoint(corners[2]);
            var space = Panel.rect;
            return new MapRect(min.x - space.xMin, space.yMax - max.y, max.x - min.x, max.y - min.y);
        }

        static void AssertSame(MapRect expected, MapRect actual, string what)
        {
            Assert.That(actual.X, Is.EqualTo(expected.X).Within(.01f), what + " x");
            Assert.That(actual.Y, Is.EqualTo(expected.Y).Within(.01f), what + " y");
            Assert.That(actual.W, Is.EqualTo(expected.W).Within(.01f), what + " width");
            Assert.That(actual.H, Is.EqualTo(expected.H).Within(.01f), what + " height");
        }

        MapScreenLayout LayoutNow(MapScreenMode mode)
        {
            var settings = ShowcaseSettings.Current;
            float ts = settings != null ? Mathf.Clamp(settings.TextScale, 1f, 1.5f) : 1f;
            return MapScreenLayout.Compute(_stage.sizeDelta.x, _stage.sizeDelta.y, mode, ts);
        }

        static float Wide(TMP_Text label) => label.GetPreferredValues(label.text, 0f, 0f).x;

        // ---- 1. the live rects equal the layout --------------------------------------------------------------------------------------

        [TestCaseSource(nameof(WideWindows))]
        public void TheLiveRectsEqualTheLayout(Vector2 window)
        {
            BuildController(window);
            var l = LayoutNow(MapScreenMode.Tab);
            Assert.That(l.Stacked, Is.False, "set-up: a wide window");
            AssertSame(l.Sheet, Measure(Part("MapSheet")), "MapSheet");
            AssertSame(l.Header, Measure(Part("MapTitle")), "MapTitle");
            AssertSame(l.Frame, Measure(Part("MapFrame")), "MapFrame");
            AssertSame(l.Legend, Measure(Part("ZoneLegend")), "ZoneLegend");
            AssertSame(l.Left, Measure(Part("TacticalInformation/ContactsPage")), "ContactsPage");
            AssertSame(l.Right, Measure(Part("TacticalInformation/WeaponsPage")), "WeaponsPage");
            var map = Part("MapFrame/PublicMap");
            AssertSame(l.Map, Measure(map), "PublicMap after the map was delivered");
            Assert.That(map.GetComponent<TacticalMapSurface>().Map, Is.Not.Null, "set-up: a map was delivered to the large surface");
            Assert.That(map.GetComponent<AspectRatioFitter>(), Is.Null, "the layout owns the large map's rect: no aspect fitter");
            var mini = _canvas.transform.Find("Minimap/TerrainFrame");
            Assert.That(mini.GetComponent<AspectRatioFitter>(), Is.Not.Null, "the minimap surface keeps its own fitter");
        }

        // ---- 2. the sheet is behind everything and only on the wide Tab layout ---------------------------------------------------------

        [TestCaseSource(nameof(WideWindows))]
        public void TheSheetIsBehindEverythingAndOnlyOnTheWideTabLayout(Vector2 window)
        {
            BuildController(window);
            Assert.That(Panel.GetChild(0).name, Is.EqualTo("Scrim"));
            Assert.That(Panel.GetChild(1).name, Is.EqualTo("MapSheet"), "the sheet is the panel's second child, behind everything else");
            Assert.That(Panel.GetChild(1).gameObject.activeSelf, Is.True, "shown with the Tab map on a wide window");
        }

        [Test]
        public void TheSheetIsHiddenOnTheStackedLayoutAndOnThePlanningScreen()
        {
            BuildController(StackedWindow);
            Assert.That(LayoutNow(MapScreenMode.Tab).Stacked, Is.True, "set-up: 640x480 stacks");
            Assert.That(Panel.GetChild(1).name, Is.EqualTo("MapSheet"), "the sheet exists as the second child");
            Assert.That(Panel.GetChild(1).gameObject.activeSelf, Is.False, "not drawn on the stacked layout");
            Reset();
            BuildController(new Vector2(1920, 1080), planning: true);
            Assert.That(Panel.GetChild(1).name, Is.EqualTo("MapSheet"));
            Assert.That(Panel.GetChild(1).gameObject.activeSelf, Is.False, "not drawn on the planning screen");
        }

        // ---- 3. the header ----------------------------------------------------------------------------------------------------------------

        [Test]
        public void TheHeaderCarriesTitlePhaseAndHint()
        {
            BuildController(new Vector2(1280, 720));
            var hint = Part("MapTitle/MapHint").GetComponent<TMP_Text>();
            var close = Part("MapTitle/CloseMap");
            Assert.That(hint.text, Is.EqualTo("Right-click to walk · release Tab to close"), "hold mode hint");
            Assert.That(close.gameObject.activeSelf, Is.False, "releasing Tab is the way out in hold mode: no Close button");

            _controller.SetToggleMode(true);
            _controller.SetOpen(true);
            Frame();
            Assert.That(_controller.IsOpen, Is.True, "set-up: reopened");
            Assert.That(hint.text, Is.EqualTo("Right-click to walk · Tab closes"), "toggle mode hint, decided without a relayout");
            Assert.That(close.gameObject.activeSelf, Is.True, "toggle mode shows the Close button");

            var phase = Part("MapTitle/MapPhase").GetComponent<TMP_Text>();
            _controller.SetPhaseLine("Coliseum · sudden death 0:42");
            Frame();
            Assert.That(phase.text, Is.EqualTo("Coliseum · sudden death 0:42"));
            Assert.That(phase.gameObject.activeInHierarchy, Is.True, "a short line fits a 1156 wide header");
            _controller.SetPhaseLine(null);
            Frame();
            Assert.That(phase.text, Is.Empty, "a null line shows nothing");
        }

        // ---- 4. the header drops what does not fit -------------------------------------------------------------------------------------

        [Test]
        public void TheHeaderDropsWhatDoesNotFit()
        {
            BuildController(StackedWindow);
            var layout = LayoutNow(MapScreenMode.Tab);
            Assert.That(layout.Title.W, Is.EqualTo(392f).Within(.01f), "set-up: the 640x480 header is 392 wide");
            const string longLine = "Waiting for players 12/16 — run starts in 1:42";
            _controller.SetToggleMode(true);
            _controller.SetOpen(true);
            _controller.SetPhaseLine(longLine);
            Frame();
            AssertHeaderFits(layout.Title.W, "toggle mode, long phase line", expectPhase: false);

            _controller.SetPhaseLine(null);
            Frame();
            AssertHeaderFits(layout.Title.W, "toggle mode, no phase line", expectHint: false);

            _controller.SetToggleMode(false);
            _controller.SetOpen(true);
            _controller.SetPhaseLine(longLine);
            Frame();
            AssertHeaderFits(layout.Title.W, "hold mode, long phase line", expectPhase: false);

            Reset();
            BuildController(new Vector2(1280, 720));
            _controller.SetToggleMode(true);
            _controller.SetOpen(true);
            _controller.SetPhaseLine("Coliseum · sudden death 0:42");
            Frame();
            Assert.That(Part("MapTitle/MapHeading").gameObject.activeInHierarchy, Is.True, "the heading shows");
            Assert.That(Part("MapTitle/MapPhase").gameObject.activeInHierarchy, Is.True, "at 1280x720 the phase line shows");
            Assert.That(Part("MapTitle/MapHint").gameObject.activeInHierarchy, Is.True, "at 1280x720 the hint shows");
            AssertHeaderFits(LayoutNow(MapScreenMode.Tab).Title.W, "1280x720 all three");
        }

        /// <summary>The rule of the packet, with the labels' own preferred widths: what shows clears its neighbours by 16, what is hidden would not.</summary>
        void AssertHeaderFits(float width, string what, bool? expectPhase = null, bool? expectHint = null)
        {
            var heading = Part("MapTitle/MapHeading").GetComponent<TMP_Text>();
            var phase = Part("MapTitle/MapPhase").GetComponent<TMP_Text>();
            var hint = Part("MapTitle/MapHint").GetComponent<TMP_Text>();
            var close = Part("MapTitle/CloseMap");
            float inset = close.gameObject.activeSelf ? 92f : 0f;
            Assert.That(-((RectTransform)hint.transform).offsetMax.x, Is.EqualTo(inset).Within(.01f), what + ": the hint's right inset is 92 with the Close button, else 0");
            float wh = Wide(heading), wp = Wide(phase), wt = Wide(hint) + inset;
            bool phaseHere = !string.IsNullOrEmpty(phase.text);
            bool phaseFits = phaseHere && width / 2f - wp / 2f - 16f >= wh;
            bool phaseShown = phase.gameObject.activeSelf && phaseHere;
            Assert.That(phaseShown, Is.EqualTo(phaseFits), what + ": the phase line shows exactly when it clears the heading (Wh " + wh + ", Wp " + wp + ")");
            bool hintFits = phaseFits ? width / 2f - wp / 2f - 16f >= wt : wh + 16f + wt <= width;
            Assert.That(hint.gameObject.activeSelf, Is.EqualTo(hintFits), what + ": the hint shows exactly when it clears what is left of it (Wt " + wt + ")");
            if (expectPhase.HasValue) Assert.That(phaseShown, Is.EqualTo(expectPhase.Value), what + ": the long phase line does not fit this header");
            if (expectHint.HasValue) Assert.That(hint.gameObject.activeSelf, Is.EqualTo(expectHint.Value), what + ": the toggle hint plus its inset does not fit beside the heading");
        }

        // ---- 8. the rails ----------------------------------------------------------------------------------------------------------------

        static void AssertLook(RectTransform rail, bool unframed, string what)
        {
            var theme = UiTheme.Current;
            var image = rail.GetComponent<Image>();
            Assert.That(image, Is.Not.Null, what + " has a surface");
            Color32 fill = image.color, expected = unframed ? theme.SurfaceRaised : theme.Surface;
            Assert.That(fill.r == expected.r && fill.g == expected.g && fill.b == expected.b, Is.True, what + " fill " + fill + " should be " + expected);
            var frame = rail.Find("Frame");
            if (unframed) Assert.That(frame, Is.Null, what + " draws no border on the sheet");
            else
            {
                Assert.That(frame, Is.Not.Null, what + " keeps its border");
                Color32 ring = frame.GetComponent<Image>().color, border = theme.Border;
                Assert.That(ring.r == border.r && ring.g == border.g && ring.b == border.b, Is.True, what + " border colour");
            }
        }

        [Test]
        public void TheRailsAreUnframedOnlyOnTheSheet()
        {
            BuildController(new Vector2(1920, 1080));
            AssertLook(Part("TacticalInformation/ContactsPage"), true, "ContactsPage on the sheet");
            AssertLook(Part("TacticalInformation/WeaponsPage"), true, "WeaponsPage on the sheet");
            Assert.That(Part("MapFrame").Find("Frame"), Is.Not.Null, "MapFrame keeps its border");
            Reset();
            BuildController(StackedWindow);
            AssertLook(Part("TacticalInformation/ContactsPage"), false, "stacked ContactsPage");
            AssertLook(Part("TacticalInformation/WeaponsPage"), false, "stacked WeaponsPage");
            Assert.That(Part("MapFrame").Find("Frame"), Is.Not.Null, "stacked MapFrame keeps its border");
            Reset();
            BuildController(new Vector2(1920, 1080), planning: true);
            AssertLook(Part("TacticalInformation/ContactsPage"), false, "planning ContactsPage");
            AssertLook(Part("TacticalInformation/WeaponsPage"), false, "planning WeaponsPage");
        }

        [Test]
        public void TheDimIsDarkerOnTheTabMapAndPlanningKeepsItsOwn()
        {
            BuildController(new Vector2(1280, 720));
            Assert.That(Part("Scrim").GetComponent<Image>().color.a, Is.EqualTo(.86f).Within(.005f), "Tab dim");
            Reset();
            BuildController(new Vector2(1280, 720), planning: true);
            Assert.That(Part("Scrim").GetComponent<Image>().color.a, Is.EqualTo(.84f).Within(.005f), "planning dim");
        }

        // ---- the HUD rig -------------------------------------------------------------------------------------------------------------------

        void BuildHud()
        {
            if (_priorEvents == null) _priorEvents = EventSystem.current;
            _events = new GameObject("Sheet events", typeof(UiInputRoutingEventSystem));
            var system = _events.GetComponent<UiInputRoutingEventSystem>(); system.enabled = false; system.enabled = true;
            EventSystem.current = system;
            _rigRoot = new GameObject("Sheet HUD fixture");
            _client = _rigRoot.AddComponent<MatchClient>(); _client.enabled = false;
            _rigTransport = new RigTransport();
            _session = new Session(_rigTransport, new SessionOptions { Url = "ws://sheet/ws", Profile = "sheet", Now = () => 1000 });
            typeof(MatchClient).GetMethod("InitializeLayoutReview", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public).Invoke(_client, new object[] { _session });
            _hud = _client.Hud;
            _controller = null;
            _session.Connect(); _rigTransport.Open();
            _rigTransport.Deliver(new ServerMsg.Welcome { Protocol = Wire.ProtocolVersion, MatchId = new byte[16], Session = new byte[16], PlayerId = 1,
                TickRate = 20, ServerMs = 50, Map = new MapBlob(32, 32, new byte[1024]) });
        }

        void Snapshot(MatchPhase phase) =>
            _rigTransport.Deliver(new ServerMsg.Snapshot { Tick = 1, ServerTimeOverrideMs = 50, Phase = (byte)phase,
                Me = new WireSelf { Id = 1, X = 64, Y = 64, Hp = 100, MaxHp = 100, Status = 1, Weapon = 0 } });

        T Field<T>(object owner, string name) => (T)owner.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(owner);
        void Call(object owner, string name) => owner.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(owner, null);

        // ---- 5. the HUD hands its phase line to the map ------------------------------------------------------------------------------------

        [Test]
        public void TheHudHandsItsPhaseLineToTheMap()
        {
            BuildHud();
            Snapshot(MatchPhase.Lobby);   // no arena delivered: the line is "FORMING MATCH"
            typeof(MatchHud).GetField("_nextMetricsAt", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(_hud, 0d);
            Call(_hud, "TickMetrics");
            var pill = Field<TMP_Text>(_hud, "_phaseStrip");
            Assert.That(pill.text, Is.Not.Empty, "set-up: the phase pill shows a line");
            var phase = HudTestPaths.Find(_hud.transform, "MatchUI/TacticalMap").Find("MapTitle/MapPhase");
            Assert.That(phase, Is.Not.Null, "the map header has a phase label");
            Assert.That(phase.GetComponent<TMP_Text>().text, Is.EqualTo(pill.text), "the map shows the line the pill shows");
        }

        // ---- 6. only the chrome survives the map -------------------------------------------------------------------------------------------

        [Test]
        public void OnlyTheChromeSurvivesTheMap()
        {
            BuildHud();
            Snapshot(MatchPhase.Active);
            var veiled = new Dictionary<string, Transform>
            {
                { "phase pill", Field<RectTransform>(_hud, "_phasePill") },
                { "journal launcher", Field<UnityEngine.UI.Button>(_hud, "_journalButton").transform },
                { "edge threats", Field<EdgeThreatView>(_hud, "_edgeThreats").Root },
            };
            var chrome = new List<Transform> { Field<RectTransform>(_hud, "_chromeStage"), Field<RectTransform>(_hud, "_metricsPill") };
            foreach (var button in Field<UnityEngine.UI.Button[]>(_hud, "_headerActions")) if (button != null) chrome.Add(button.transform);
            var chromeActive = new List<bool>();
            foreach (var t in chrome) chromeActive.Add(t.gameObject.activeSelf);

            _hud.TacticalMap.SetOpen(true);
            Assert.That(_hud.TacticalMap.IsOpen, Is.True, "set-up: the map is open");
            Call(_hud, "RefreshWorldHudVisibility");
            for (int i = 0; i < chrome.Count; i++)
            {
                Assert.That(chrome[i].gameObject.activeSelf, Is.EqualTo(chromeActive[i]), chrome[i].name + " keeps its own state");
                foreach (var group in chrome[i].GetComponentsInParent<CanvasGroup>(true)) Assert.That(group.alpha, Is.EqualTo(1f), chrome[i].name + " is not veiled");
            }
            foreach (var pair in veiled)
            {
                var group = pair.Value.GetComponent<CanvasGroup>();
                Assert.That(group, Is.Not.Null, pair.Key + " is veiled by a CanvasGroup");
                Assert.That(group.alpha, Is.EqualTo(0f), pair.Key + " alpha while the map is open");
                Assert.That(group.blocksRaycasts, Is.False, pair.Key + " blocks no raycast while the map is open");
                Assert.That(group.interactable, Is.False, pair.Key + " is not interactable while the map is open");
            }
            _hud.TacticalMap.SetOpen(false);
            Call(_hud, "RefreshWorldHudVisibility");
            foreach (var pair in veiled)
            {
                var group = pair.Value.GetComponent<CanvasGroup>();
                Assert.That(group, Is.Not.Null, pair.Key + " keeps its group");
                Assert.That(group.alpha, Is.EqualTo(1f), pair.Key + " alpha after the map closed");
                Assert.That(group.blocksRaycasts, Is.True, pair.Key + " blocks raycasts again");
                Assert.That(group.interactable, Is.True, pair.Key + " is interactable again");
            }
        }

        // ---- 7. world marks ---------------------------------------------------------------------------------------------------------------------

        [Test]
        public void WorldMarksAreHiddenWhileTheMapIsOpen()
        {
            _arenaObject = new GameObject("sheet arena");
            var arena = _arenaObject.AddComponent<ArenaView>();
            if (_arenaObject.transform.Find("World markers") == null)
                typeof(ArenaView).GetMethod("Awake", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(arena, null);
            var shown = new List<SpriteRenderer>();
            foreach (string name in new[] { "World markers", "World indicators", "Quest carrier flags" })
            {
                var root = _arenaObject.transform.Find(name);
                Assert.That(root, Is.Not.Null, "the arena has the root " + name);
                var mark = new GameObject(name + " test mark", typeof(SpriteRenderer)); mark.transform.SetParent(root, false);
                shown.Add(mark.GetComponent<SpriteRenderer>());
            }
            var off = new GameObject("a mark that was not showing", typeof(SpriteRenderer));
            off.transform.SetParent(_arenaObject.transform.Find("World markers"), false); off.GetComponent<SpriteRenderer>().enabled = false;
            foreach (var r in shown) Assert.That(r.enabled && r.gameObject.activeInHierarchy, Is.True, "set-up: " + r.name + " is showing");

            arena.SetWorldMarksVisible(false);
            foreach (var r in shown) Assert.That(r.gameObject.activeInHierarchy, Is.False, r.name + " is hidden while the map is open");
            foreach (string name in new[] { "World markers", "World indicators", "Quest carrier flags" })
                Assert.That(_arenaObject.transform.Find(name).gameObject.activeSelf, Is.False, name + " root is hidden");

            arena.SetWorldMarksVisible(true);
            foreach (var r in shown) Assert.That(r.enabled && r.gameObject.activeInHierarchy, Is.True, r.name + " is back");
            Assert.That(off.GetComponent<SpriteRenderer>().enabled, Is.False, "a mark that was not showing stays off");
        }

        // ---- 9. planning keeps blocking the map under the journal (green before and after: a guard) --------------------------------

        [Test]
        public void PlanningStillBlocksTheMapUnderTheJournal()
        {
            BuildHud();
            Snapshot(MatchPhase.Active);
            var handle = new QuestHandle(71);
            var quests = new QuestsState { Available = true, View = new WireQuestView {
                Rows = new[] { new WireQuestRow { Handle = handle, Def = "fixture", GiverX = 64, GiverY = 80, State = QuestRowState.Preview, ExpiresAt = 10000,
                    CanAccept = false, Name = "Planning quest", GiverLabel = "same", Brief = "brief", Objective = "objective", Reward = "reward" } },
                Objectives = new[] { new WireQuestObjective { Quest = handle, X = 200, Y = 220 } } } };
            _rigTransport.Deliver(new ServerMsg.Snapshot { Tick = 2, ServerTimeOverrideMs = 100, Phase = (byte)MatchPhase.Planning,
                Me = new WireSelf { Id = 1, X = 64, Y = 64, Hp = 100, MaxHp = 100, Status = 1, Weapon = 0 } });
            _rigTransport.Deliver(new ServerMsg.State { Tick = 2, Items = new StateItem[] { new PlanState { Value = new WirePlan {
                EndsAt = 10000, Spawn0 = 5, Spawn1 = 8, Pick = 1, Boon = 0, Boons = new[] { "First boon", "Second boon" },
                Option0 = new WirePlanOption { Index = 5, X = 64, Y = 64, Zone = 1 },
                Option1 = new WirePlanOption { Index = 8, X = 96, Y = 96, Zone = 2 } } }, quests } });
            _hud.TacticalMap.Tick();
            Assert.That(_hud.TacticalMap.IsPlanning, Is.True, "set-up: the planning screen");

            _hud.ShowPlayerFlow(0);
            var map = HudTestPaths.Find(_hud.transform, "MatchUI/TacticalMap");
            var group = map.GetComponent<CanvasGroup>();
            Assert.That(group, Is.Not.Null, "the journal blocks the map through a group on the panel");
            Assert.That(group.blocksRaycasts, Is.False, "the map takes no pointer under the journal");
            Assert.That(group.interactable, Is.False);
            _hud.QuestJournal.Root.Find("CloseJournal").GetComponent<UnityEngine.UI.Button>().onClick.Invoke();
            Assert.That(group.blocksRaycasts, Is.True, "the map takes the pointer again when the journal closes");
            Assert.That(group.interactable, Is.True);
        }
    }
}
