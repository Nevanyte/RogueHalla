using System;
using System.Collections.Generic;
using NUnit.Framework;
using Roguehalla.Net;
using Roguehalla.Net.Protocol;
using Roguehalla.Net.Ui;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;

namespace Roguehalla.Client.Tests
{
    /// <summary>
    /// P-UI-TABMAP-MARKS-1: the Tab map's whole marks layer (mesh, pooled labels, zone names) is drawn <see cref="MapMarkScale.Layer"/> times
    /// larger as one unit and still covers the map exactly; the minimap is untouched. The real TacticalMapController is laid out in the stage
    /// a 1280x720, 1920x1080, 1024x768 and 640x480 window give, with the stage scaled the way the window scales it (the pixel scale is what
    /// the layer scale reads); the clock is the controller's <see cref="TacticalMapController.SecondsClock"/> seam.
    /// </summary>
    public sealed class TabMapMarksEditModeTests
    {
        static readonly Vector2[] Wide = { new Vector2(1280, 720), new Vector2(1920, 1080), new Vector2(1024, 768) };
        static readonly Vector2[] All = { new Vector2(1280, 720), new Vector2(1920, 1080), new Vector2(1024, 768), new Vector2(640, 480) };
        static readonly Vector2[] Floor = { new Vector2(1280, 720), new Vector2(1024, 768), new Vector2(640, 480) };

        const int Tiles = 6;
        const float WorldSide = Tiles * 32f;

        GameObject _canvas, _stageObject, _clientObject, _controllerObject;
        Fix3Transport _transport;
        Session _session;
        TacticalMapController _controller;
        MatchClient _client;
        RectTransform _stage;
        UiDisplayPreferences _prefs;
        bool _priorToggle;
        Func<double> _priorClock;
        bool? _priorReduced;
        UiTheme _priorTheme;
        double _now = 100d;
        byte[] _regions;
        uint _tick = 2;

        [SetUp]
        public void SetUp()
        {
            _priorClock = TacticalMapController.SecondsClock;
            TacticalMapController.SecondsClock = () => _now;
            _priorReduced = UiMotion.ReducedOverride;
            _priorTheme = UiTheme.Current;
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
                UiTheme.Current = _priorTheme;
                WorldLabelVisibility.Hidden = false;
                if (_prefs != null) _prefs.SetMapToggle(_priorToggle);
            }
        }

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
                foreach (var go in new[] { _controllerObject, _clientObject, _stageObject, _canvas })
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

        /// <summary>Screen pixels per HUD unit of the window, as the HUD's stage has it.</summary>
        static float StageScale(Vector2 window) => HudLayout.Viewport(window.x, window.y).Scale;

        /// <summary>The pixels per HUD unit the stage really has (the canvas here has no scaler, so its factor is 1).</summary>
        float Px => _stage.lossyScale.x;

        void BuildController(Vector2 window, bool planning = false)
        {
            _canvas = new GameObject("marks canvas", typeof(RectTransform), typeof(Canvas), typeof(UnityEngine.UI.GraphicRaycaster));
            _stageObject = new GameObject("marks stage", typeof(RectTransform));
            _stage = (RectTransform)_stageObject.transform; _stage.SetParent(_canvas.transform, false);
            _stage.anchorMin = _stage.anchorMax = _stage.pivot = new Vector2(.5f, .5f); _stage.sizeDelta = StageUnits(window);
            float s = StageScale(window);
            _stage.localScale = new Vector3(s, s, 1f);
            _regions = new byte[Tiles * Tiles];
            for (int i = 0; i < _regions.Length; i++) _regions[i] = (byte)(i % 6 + 1);
            var map = new MapBlob(Tiles, Tiles, new byte[Tiles * Tiles], null, _regions)
                .WithPublic(new[] { new MapCamp(96, 96, 8, 1), new MapCamp(32, 32, 8, 3) }, null, null, null, null);
            _transport = new Fix3Transport();
            _session = Fix3Transport.Join(_transport, map,
                new WireSelf { Id = 1, Faction = 4, X = 48, Y = 48, Hp = 100, MaxHp = 100, Status = 1, Weapon = (byte)WeaponId.Wand });
            _clientObject = new GameObject("marks client");
            _client = _clientObject.AddComponent<MatchClient>(); _client.enabled = false; _client.Url = "ws://localhost/marks"; _client.Profile = "fixture";
            _controllerObject = new GameObject("marks controller");
            _controller = _controllerObject.AddComponent<TacticalMapController>();
            if (_prefs == null) { _prefs = new UiDisplayPreferences(); _priorToggle = _prefs.MapToggle; }
            _controller.Initialize(_client, _canvas.transform, _prefs, _stage);
            Assert.That(Px, Is.EqualTo(s).Within(.001f), "set-up: the stage has the window's pixel scale");
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

        /// <summary>A cleared camp (a clock), a camp with a reward (an amount), a chest elsewhere (seconds), a hunted rival and another one
        /// (the HUNT and ENEMY chips), then two frames so the paint throttle lets the controller draw them.</summary>
        void DeliverMarks()
        {
            _transport.Deliver(new ServerMsg.State { Tick = 3, Items = new StateItem[] { new CampsState { Rows = new[] {
                new WireCamp { MobType = 0, Elite = 0, Alive = 0, State = 2, ClearedAt = 1000, RespawnAt = 181000 },
                new WireCamp { MobType = 0, Elite = 0, Alive = 3, State = 0, Reward = new WireCampReward { Stat = 2, Amount = 12 } } } } } });
            _transport.Deliver(new ServerMsg.State { Tick = 4, Items = new StateItem[] { new RewardChestsState { Rows = new[] {
                new WireRewardChest { Handle = new RewardChestHandle(5), X = 160, Y = 48, ExpiresAt = 12300, CanChoose = true, Options = new WireRewardOption[0] } } } } });
            _transport.Deliver(new ServerMsg.Snapshot { Tick = 5, Phase = 1,
                Me = new WireSelf { Id = 1, Faction = (byte)FactionId.Sentenced, Status = 1, HuntTarget = 2, Hp = 100, MaxHp = 100, X = 48, Y = 48 },
                Actors = new[] { new WireActor { Id = 1, X = 48, Y = 48, HpPct = 100, Status = 1 }, new WireActor { Id = 2, X = 140, Y = 140, HpPct = 100, Status = 1 },
                    new WireActor { Id = 3, X = 140, Y = 100, HpPct = 100, Status = 1 } } });
            Frame(); Frame();
        }

        RectTransform Panel => (RectTransform)_canvas.transform.Find("TacticalMap");

        RectTransform Part(string path)
        {
            var found = Panel.Find(path);
            Assert.That(found, Is.Not.Null, "the panel has " + path);
            return (RectTransform)found;
        }

        TacticalMapSurface Large() => Part("MapFrame/PublicMap").GetComponent<TacticalMapSurface>();
        TacticalMapSurface Mini() => _stage.Find("Minimap/TerrainFrame").GetComponent<TacticalMapSurface>();
        TacticalMapMarks Marks() => Part("MapFrame/PublicMap/Delivered contacts and personal route").GetComponent<TacticalMapMarks>();
        RectTransform ZoneHost() => Part("MapFrame/PublicMap/Zone labels");

        MapScreenLayout LayoutNow(MapScreenMode mode)
        {
            var settings = ShowcaseSettings.Current;
            float ts = settings != null ? Mathf.Clamp(settings.TextScale, 1f, 1.5f) : 1f;
            return MapScreenLayout.Compute(_stage.sizeDelta.x, _stage.sizeDelta.y, mode, ts);
        }

        /// <summary>The layer scale the rule gives for the live layout and pixel scale.</summary>
        float Expected(MapScreenMode mode = MapScreenMode.Tab) => MapMarkScale.Layer(LayoutNow(mode).Map.W, Px);

        /// <summary>A world position of the map on the screen (Overlay canvas: the world position is the screen position), through the map's own corners.</summary>
        Vector2 WorldPoint(float wx, float wy)
        {
            var c = new Vector3[4]; Part("MapFrame/PublicMap").GetWorldCorners(c);
            return new Vector2(Mathf.Lerp(c[0].x, c[2].x, wx / WorldSide), Mathf.Lerp(c[2].y, c[0].y, wy / WorldSide));
        }

        static void AssertSameCorners(RectTransform actual, RectTransform expected, string what)
        {
            var a = new Vector3[4]; var e = new Vector3[4];
            actual.GetWorldCorners(a); expected.GetWorldCorners(e);
            for (int i = 0; i < 4; i++)
            {
                Assert.That(a[i].x, Is.EqualTo(e[i].x).Within(.05f), what + " corner " + i + " x");
                Assert.That(a[i].y, Is.EqualTo(e[i].y).Within(.05f), what + " corner " + i + " y");
            }
        }

        /// <summary>The marks layer covers the map and has the scale the rule gives (test 1's two assertions).</summary>
        void AssertLayerOnTheMap(string step)
        {
            var marks = Marks();
            float expected = Expected();
            Assert.That(expected, Is.GreaterThan(1.2f), step + ": set-up: the rule gives a layer larger than the minimap's marks");
            Assert.That(marks.transform.localScale.x, Is.EqualTo(expected).Within(.001f), step + ": scale x");
            Assert.That(marks.transform.localScale.y, Is.EqualTo(expected).Within(.001f), step + ": scale y");
            AssertSameCorners((RectTransform)marks.transform, Part("MapFrame/PublicMap"), step + ": the layer covers the map");
        }

        // ---- 1-2. the scale and the cover ---------------------------------------------------------------------------------------------

        [TestCaseSource(nameof(All))]
        public void TheMarksLayerIsScaledAndStillCoversTheMap(Vector2 window)
        {
            BuildController(window);
            AssertLayerOnTheMap("window " + window);
        }

        [TestCaseSource(nameof(Wide))]
        public void TheSurfaceReportsItsScale(Vector2 window)
        {
            BuildController(window);
            Assert.That(Expected(), Is.GreaterThan(1.2f), "set-up: the rule gives a layer larger than the minimap's marks");
            Assert.That(Large().MarkScale, Is.EqualTo(Expected()).Within(.001f), "the Tab map's surface");
            Assert.That(Mini().MarkScale, Is.EqualTo(1f), "the minimap's surface");
        }

        // ---- 3. the labels are at least ten pixels ---------------------------------------------------------------------------------------

        static readonly string[] LabelPrefixes = { "CampClock", "ChestSeconds", "CampBadgeText", "ContactLabel", "ZoneLabel" };

        [TestCaseSource(nameof(Floor))]
        public void MarkLabelsAreAtLeastTenPixels(Vector2 window)
        {
            BuildController(window);
            Marks().CampFactsInKey = false;   // P-UI-TABMAP-TEXT-2: this test measures the amount labels, which the key would take off the map
            DeliverMarks();
            var found = new Dictionary<string, List<TMP_Text>>();
            foreach (var prefix in LabelPrefixes) found[prefix] = new List<TMP_Text>();
            foreach (var root in new Transform[] { Marks().transform, ZoneHost() })
                foreach (var text in root.GetComponentsInChildren<TMP_Text>(false))
                {
                    string name = text.name == "Text" && text.transform.parent != null ? text.transform.parent.name : text.name;
                    foreach (var prefix in LabelPrefixes) if (name.StartsWith(prefix)) found[prefix].Add(text);
                }
            foreach (var prefix in LabelPrefixes)
            {
                Assert.That(found[prefix].Count, Is.GreaterThanOrEqualTo(1), "set-up: an active " + prefix + " label is drawn");
                foreach (var text in found[prefix])
                    Assert.That(text.fontSize * text.transform.lossyScale.y, Is.GreaterThanOrEqualTo(10f - .05f),
                        prefix + " '" + text.text + "' on screen at " + window + " (pixels)");
            }
        }

        // ---- 4. a mark stays on its world point ------------------------------------------------------------------------------------------

        [TestCaseSource(nameof(Wide))]
        public void AMarkStaysOnItsWorldPoint(Vector2 window)
        {
            BuildController(window);
            Assert.That(Large().MarkScale, Is.GreaterThan(1.2f), "set-up: the layer is larger than the minimap's marks");
            DeliverMarks();
            var marks = Marks();
            float k = marks.transform.localScale.x, px = Px;
            var ring = marks.SelfMarkerRect;
            Assert.That(ring.HasValue, Is.True, "set-up: the own position is drawn");
            Vector2 self = marks.transform.TransformPoint(ring.Value.center);
            var own = WorldPoint(48, 48);
            Assert.That(self.x, Is.EqualTo(own.x).Within(.5f * px), "own ring x");
            Assert.That(self.y, Is.EqualTo(own.y).Within(.5f * px), "own ring y");
            Assert.That(Large().ScreenWorld(own, null, out var world), Is.True, "the pointer maps to the map");
            Assert.That(world.x, Is.EqualTo(48f).Within(1f), "ScreenWorld x");
            Assert.That(world.y, Is.EqualTo(48f).Within(1f), "ScreenWorld y");
            var clock = marks.transform.Find("CampClock0");
            Assert.That(clock, Is.Not.Null, "set-up: the cleared camp's clock is drawn");
            Vector2 under = (Vector2)clock.position - new Vector2(0f, -16f * k * px);
            var camp = WorldPoint(96, 96);
            Assert.That(under.x, Is.EqualTo(camp.x).Within(.5f * px), "camp clock x");
            Assert.That(under.y, Is.EqualTo(camp.y).Within(.5f * px), "camp clock y");
        }

        // ---- 5-6. the zone names follow the layer -----------------------------------------------------------------------------------------

        static void AssertSameLayer(RectTransform host, RectTransform marks, string what)
        {
            Assert.That(host.localScale.x, Is.EqualTo(marks.localScale.x).Within(.001f), what + ": scale x");
            Assert.That(host.localScale.y, Is.EqualTo(marks.localScale.y).Within(.001f), what + ": scale y");
            Assert.That(host.anchorMin.x, Is.EqualTo(marks.anchorMin.x).Within(.0005f), what + ": anchorMin x");
            Assert.That(host.anchorMin.y, Is.EqualTo(marks.anchorMin.y).Within(.0005f), what + ": anchorMin y");
            Assert.That(host.anchorMax.x, Is.EqualTo(marks.anchorMax.x).Within(.0005f), what + ": anchorMax x");
            Assert.That(host.anchorMax.y, Is.EqualTo(marks.anchorMax.y).Within(.0005f), what + ": anchorMax y");
            AssertSameCorners(host, marks, what + ": the host covers what the layer covers");
        }

        [TestCaseSource(nameof(Wide))]
        public void ZoneNamesFollowTheLayer(Vector2 window)
        {
            BuildController(window);
            Assert.That(Large().MarkScale, Is.GreaterThan(1.2f), "set-up: the layer is larger than the minimap's marks");
            var marks = (RectTransform)Marks().transform; var host = ZoneHost();
            AssertSameLayer(host, marks, "Zone labels");
            MapZones.LabelTiles(_regions, Tiles, Tiles, out var found, out var tileX, out var tileY);
            float k = marks.localScale.x, px = Px; int seen = 0;
            foreach (var text in host.GetComponentsInChildren<TMP_Text>(false))
            {
                Assert.That(text.name, Does.StartWith("ZoneLabel"));
                int region = int.Parse(text.name.Substring("ZoneLabel".Length));
                Assert.That(found[region], Is.True, "set-up: zone " + region + " has a tile");
                var at = WorldPoint((float)tileX[region] * 32f, (float)tileY[region] * 32f);
                Assert.That(text.transform.position.x, Is.EqualTo(at.x).Within(.5f * px), text.name + " x");
                Assert.That(text.transform.position.y, Is.EqualTo(at.y - 24f * k * px).Within(.5f * px), text.name + " y: 24 layer units under the icon");
                seen++;
            }
            Assert.That(seen, Is.EqualTo(MapZones.Count), "set-up: every zone name is drawn");
        }

        [Test]
        public void TheZoneHostFollowsAHandScaledLayer()
        {
            BuildController(new Vector2(1280, 720));
            var marks = (RectTransform)Marks().transform; var host = ZoneHost();
            const float k = 1.25f;
            marks.anchorMin = new Vector2(.5f - .5f / k, .5f - .5f / k); marks.anchorMax = new Vector2(.5f + .5f / k, .5f + .5f / k);
            marks.offsetMin = marks.offsetMax = Vector2.zero; marks.pivot = new Vector2(.5f, .5f);
            marks.localScale = new Vector3(k, k, 1f);
            Marks().LayerChanged();
            AssertSameLayer(host, marks, "after a hand-scaled layer and LayerChanged");
            Assert.That(host.localScale.x, Is.EqualTo(k).Within(.001f), "the host has the hand-set scale, not the controller's");
        }

        // ---- 7. the minimap's layer ------------------------------------------------------------------------------------------------------

        static void AssertUnscaledLayer(TacticalMapSurface surface, string what)
        {
            var marks = (RectTransform)surface.Marks.transform;
            Assert.That(marks.anchorMin, Is.EqualTo(Vector2.zero), what + ": anchorMin");
            Assert.That(marks.anchorMax, Is.EqualTo(Vector2.one), what + ": anchorMax");
            Assert.That(marks.offsetMin, Is.EqualTo(Vector2.zero), what + ": offsetMin");
            Assert.That(marks.offsetMax, Is.EqualTo(Vector2.zero), what + ": offsetMax");
            Assert.That(marks.localScale, Is.EqualTo(Vector3.one), what + ": scale");
        }

        [TestCaseSource(nameof(Wide))]
        public void TheMinimapLayerIsUntouched(Vector2 window)
        {
            BuildController(window);
            Assert.That(Large().MarkScale, Is.GreaterThan(1.2f), "set-up: the Tab map's layer is scaled");
            _controller.SetOpen(false); Frame();
            AssertUnscaledLayer(Mini(), "the minimap with the Tab map closed");
            _controller.SetOpen(true); Frame();
            AssertUnscaledLayer(Mini(), "the minimap with the Tab map open");
            Assert.That(Large().MarkScale, Is.GreaterThan(1.2f), "the Tab map's layer is scaled again when reopened");
        }

        // ---- 8. a resize keeps the layer on the map -------------------------------------------------------------------------------------------

        [Test]
        public void AResizeKeepsTheLayerOnTheMap()
        {
            BuildController(new Vector2(1280, 720));
            AssertLayerOnTheMap("1280 x 720");
            Assert.That(Expected(), Is.EqualTo(1.300f).Within(.001f), "set-up: the first step's scale");
            // The 16:9 window 1138 x 640: the same 1422 x 800 stage, a smaller pixel scale.
            _stage.localScale = new Vector3(.8f, .8f, 1f);
            Frame();
            Assert.That(Expected(), Is.EqualTo(1.389f).Within(.001f), "set-up: the second step's scale (the pixel floor)");
            AssertLayerOnTheMap("1138 x 640");
            // The 4:3 window 1024 x 768: another stage size at the same scale.
            _stage.sizeDelta = new Vector2(1280f, 960f);
            Frame();
            Assert.That(Expected(), Is.EqualTo(1.400f).Within(.001f), "set-up: the third step's scale (the map is 700 wide)");
            AssertLayerOnTheMap("1024 x 768");
        }

        // ---- 9. the planning map --------------------------------------------------------------------------------------------------------

        [TestCaseSource(nameof(Wide))]
        public void ThePlanningMapUsesTheSameRule(Vector2 window)
        {
            BuildController(window, planning: true);
            float expected = Expected(MapScreenMode.Planning);
            Assert.That(expected, Is.GreaterThan(1.2f), "set-up: the rule gives the planning map a larger layer");
            Assert.That(Large().MarkScale, Is.EqualTo(expected).Within(.001f), "planning map's layer scale");
            Assert.That(Large().MarkScale, Is.GreaterThan(1.2f));
        }

        // ---- 10-11. the call -------------------------------------------------------------------------------------------------------------

        [Test]
        public void SettingTheSameScaleTwiceDoesNothing()
        {
            BuildController(new Vector2(1280, 720));
            Assert.That(Large().MarkScale, Is.GreaterThan(1.2f), "set-up: the layer is larger than the minimap's marks");
            var surface = Large(); var marks = Marks();
            int n = marks.LayerChanges;
            surface.SetMarkScale(surface.MarkScale);
            Assert.That(marks.LayerChanges, Is.EqualTo(n), "the same value is ignored");
            float other = surface.MarkScale + .1f;
            surface.SetMarkScale(other);
            Assert.That(marks.LayerChanges, Is.EqualTo(n + 1), "a new value changes the layer once");
            Assert.That(surface.MarkScale, Is.EqualTo(other).Within(.0001f));
            Assert.That(marks.transform.localScale.x, Is.EqualTo(other).Within(.0001f), "the marks object has the new scale");
            surface.SetMarkScale(.5f);
            Assert.That(surface.MarkScale, Is.EqualTo(1f), "a value under 1 is 1");
            Assert.That(marks.transform.localScale.x, Is.EqualTo(1f));
            var rect = (RectTransform)marks.transform;
            Assert.That(rect.anchorMin, Is.EqualTo(Vector2.zero));
            Assert.That(rect.anchorMax, Is.EqualTo(Vector2.one));
            Assert.That(marks.LayerChanges, Is.EqualTo(n + 2));
        }

        [Test]
        public void ASteadyScaleCallAllocatesNothing()
        {
            BuildController(new Vector2(1280, 720));
            Assert.That(Large().MarkScale, Is.GreaterThan(1.2f), "set-up: the layer is larger than the minimap's marks");
            var surface = Large(); float k = surface.MarkScale;
            Action action = () => surface.SetMarkScale(k);
            AllocationMeter.AssertNone(action, 200, "SetMarkScale with the value already set");
        }
    }
}
