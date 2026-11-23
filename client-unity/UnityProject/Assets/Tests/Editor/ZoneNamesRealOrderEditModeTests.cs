using System;
using System.Collections.Generic;
using NUnit.Framework;
using Roguehalla.Net;
using Roguehalla.Net.Protocol;
using Roguehalla.Net.Ui;
using TMPro;
using UnityEngine;
using MapRect = Roguehalla.Net.Ui.MapRect;

namespace Roguehalla.Client.Tests
{
    /// <summary>
    /// P-UI-ZONE-NAMES-1-fix: a zone name is measured only when its label is awake. The fixture of <see cref="ZoneNamesEditModeTests"/>
    /// opens the Tab map before its first frame, so its labels are created awake; in a real match the map and the marks are delivered
    /// while the panel is closed and the first refresh runs before anything opens. Here the orders are told apart: <b>real</b> (built
    /// closed, marks delivered closed, opened later), <b>control</b> (opened before the first frame) and <b>planning</b> (the panel is
    /// shown by the first Tick, after SetMap). A text label never active in the hierarchy measures at a tenth of its size, so every
    /// width assertion first asserts the label is active in the hierarchy. The packet (docs/backlog/packets/P-UI-ZONE-NAMES-1-fix.md)
    /// has the rules and the numbers.
    /// </summary>
    public sealed class ZoneNamesRealOrderEditModeTests
    {
        static readonly Vector2 Window = new Vector2(1280, 720);
        const int Tiles = 6;
        const float WorldSide = Tiles * 32f;
        const int Fire = MapZones.Fire, Haunted = MapZones.Sentenced, Plague = MapZones.Plague, Frost = MapZones.Frost, Holy = MapZones.Holy, Coliseum = MapZones.Coliseum;

        /// <summary>A camp placed beside a zone's icon: its zone, its layer offset from that icon (right, down) at k = 1, its reward and the text that reward draws.</summary>
        struct PicCamp { public int Region; public float Right, Down; public byte Stat; public ushort Amount; public string Text; }

        // 20 right of the icon, 24 down: clear of the tenth-size box a mis-measured name has, on the true box of the name.
        static readonly PicCamp[] EdgeCamps =
        {
            new PicCamp { Region = Haunted, Right = 20, Down = 24, Stat = 0, Amount = 10000, Text = "+3" },
            new PicCamp { Region = Holy, Right = 20, Down = 24, Stat = 2, Amount = 100, Text = "+3%" },
            new PicCamp { Region = Frost, Right = 20, Down = 24, Stat = 0, Amount = 15000, Text = "+4.5" },
            new PicCamp { Region = Plague, Right = 20, Down = 24, Stat = 2, Amount = 100, Text = "+3%" },
        };

        static PicCamp[] FiftyCamps()
        {
            var list = new List<PicCamp>(EdgeCamps);
            for (int i = 0; list.Count < 50; i++)
                list.Add(new PicCamp { Region = 1 + i % 5, Right = -250 + 50 * (i % 11), Down = 100 + 45 * (i / 11), Stat = (byte)(i % 3), Amount = (ushort)(i % 2 == 0 ? 10000 : 100), Text = "" });
            return list.ToArray();
        }

        static readonly Vector2[] NoGivers = new Vector2[0];

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
        uint _tick = 10;
        float _layer1;
        MapCamp[] _mapCamps;
        PicCamp[] _campRows = EdgeCamps;
        WireSelf _wireSelf;

        [SetUp]
        public void SetUp()
        {
            _priorClock = TacticalMapController.SecondsClock;
            TacticalMapController.SecondsClock = () => _now;
            _priorReduced = UiMotion.ReducedOverride;
            _priorTheme = UiTheme.Current;
            WorldLabelVisibility.Hidden = false;
            _campRows = EdgeCamps;
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
                _controller = null; _session = null; _controllerObject = _clientObject = _stageObject = _canvas = null;
            }
        }

        // ---- fixtures ----------------------------------------------------------------------------------------------------------------

        static Vector2 StageUnits(Vector2 window)
        {
            float scale = Mathf.Clamp(Mathf.Min(window.x / 1280f, window.y / 800f), .8f, 1.5f);
            return new Vector2(window.x / scale, window.y / scale);
        }

        /// <summary>The fixture's build, with the one difference that matters: the map is opened here only when <paramref name="open"/>, and nothing ticks.</summary>
        void BuildController(bool open, MapCamp[] camps)
        {
            _mapCamps = camps ?? new[] { new MapCamp(96, 96, 8, 1), new MapCamp(32, 32, 8, 3) };
            _canvas = new GameObject("zone names canvas", typeof(RectTransform), typeof(Canvas), typeof(UnityEngine.UI.GraphicRaycaster));
            _stageObject = new GameObject("zone names stage", typeof(RectTransform));
            _stage = (RectTransform)_stageObject.transform; _stage.SetParent(_canvas.transform, false);
            _stage.anchorMin = _stage.anchorMax = _stage.pivot = new Vector2(.5f, .5f); _stage.sizeDelta = StageUnits(Window);
            float s = HudLayout.Viewport(Window.x, Window.y).Scale;
            _stage.localScale = new Vector3(s, s, 1f);
            _regions = new byte[Tiles * Tiles];
            for (int i = 0; i < _regions.Length; i++) _regions[i] = (byte)(i % 6 + 1);
            var map = new MapBlob(Tiles, Tiles, new byte[Tiles * Tiles], null, _regions).WithPublic(_mapCamps, null, null, null, null);
            _wireSelf = new WireSelf { Id = 1, Faction = 4, X = 48, Y = 48, Hp = 100, MaxHp = 100, Status = 1, Weapon = (byte)WeaponId.Wand };
            _transport = new Fix3Transport();
            _session = Fix3Transport.Join(_transport, map, _wireSelf);
            _clientObject = new GameObject("zone names client");
            _client = _clientObject.AddComponent<MatchClient>(); _client.enabled = false; _client.Url = "ws://localhost/zone-names"; _client.Profile = "fixture";
            _controllerObject = new GameObject("zone names controller");
            _controller = _controllerObject.AddComponent<TacticalMapController>();
            if (_prefs == null) { _prefs = new UiDisplayPreferences(); _priorToggle = _prefs.MapToggle; }
            _controller.Initialize(_client, _canvas.transform, _prefs, _stage);
            Marks().CampFactsInKey = false;   // P-UI-TABMAP-TEXT-2: the zone names are placed around the camps' own texts, as these tests were written
            _controller.SetToggleMode(false);
            _controller.Bind(_session);
            if (open) _controller.SetOpen(true);
        }

        /// <summary>The fixture's planning branch: the plan is delivered (only the planning order calls this).</summary>
        void DeliverPlan()
        {
            _transport.Deliver(new ServerMsg.Snapshot { Tick = 2, ServerTimeOverrideMs = 100, Phase = (byte)MatchPhase.Planning,
                Me = new WireSelf { Id = 1, X = 48, Y = 48, Hp = 100, MaxHp = 100, Status = 1, Weapon = 0 } });
            _transport.Deliver(new ServerMsg.State { Tick = 2, Items = new StateItem[] { new PlanState { Value = new WirePlan {
                EndsAt = 10000, Spawn0 = 5, Spawn1 = 8, Pick = 1, Boon = 0, Boons = new[] { "First boon", "Second boon" },
                Option0 = new WirePlanOption { Index = 5, X = 64, Y = 64, Zone = 1 },
                Option1 = new WirePlanOption { Index = 8, X = 96, Y = 96, Zone = 2 } } } } });
        }

        /// <summary>Places the camps of <see cref="_campRows"/> from the layer width of this layout at k = 1 (found by a first build in the open order).</summary>
        void FindCamps(PicCamp[] rows)
        {
            _campRows = rows;
            BuildController(true, null); Frame();
            _layer1 = Marks().rectTransform.rect.width * Large().MarkScale;
            Reset();
            var list = new MapCamp[rows.Length];
            for (int i = 0; i < list.Length; i++)
            {
                var c = rows[i]; var w = Icon1(c.Region) + new Vector2(c.Right, c.Down) * (WorldSide / _layer1);
                list[i] = new MapCamp((ushort)Mathf.Clamp(Mathf.RoundToInt(w.x), 4, 188), (ushort)Mathf.Clamp(Mathf.RoundToInt(w.y), 4, 188), 8, (byte)c.Region);
            }
            _mapCamps = list;
        }

        static Vector2 Icon1(int region) => new Vector2((region - .5f) * 32f, 80f);

        /// <summary>One frame: the clock moves a second (past the paint throttle), the controller ticks, the canvas lays out.</summary>
        void Frame()
        {
            _now += 1d;
            _controller.Tick();
            Canvas.ForceUpdateCanvases();
        }

        void Frames(int n) { for (int i = 0; i < n; i++) Frame(); }

        RectTransform Panel => (RectTransform)_canvas.transform.Find("TacticalMap");

        RectTransform Part(string path)
        {
            var found = Panel.Find(path);
            Assert.That(found, Is.Not.Null, "the panel has " + path);
            return (RectTransform)found;
        }

        TacticalMapSurface Large() => Part("MapFrame/PublicMap").GetComponent<TacticalMapSurface>();
        TacticalMapMarks Marks() => Part("MapFrame/PublicMap/Delivered contacts and personal route").GetComponent<TacticalMapMarks>();
        RectTransform ZoneHost() => Part("MapFrame/PublicMap/Zone labels");
        TMP_Text NameLabel(int region) => ZoneHost().Find("ZoneLabel" + region).GetComponent<TMP_Text>();

        void DeliverCamps()
        {
            var rows = new WireCamp[_campRows.Length];
            for (int i = 0; i < rows.Length; i++)
                rows[i] = new WireCamp { MobType = 0, Elite = 0, Alive = 3, State = 0, Reward = new WireCampReward { Stat = _campRows[i].Stat, Amount = _campRows[i].Amount } };
            _transport.Deliver(new ServerMsg.State { Tick = _tick++, Items = new StateItem[] { new CampsState { Rows = rows } } });
        }

        // ---- the orders --------------------------------------------------------------------------------------------------------------

        /// <summary>The first closed frame of a real match: each label exists, is on, and is not active in the hierarchy; the panel is not on.</summary>
        void AssertRealPremises()
        {
            Assert.That(Panel.gameObject.activeSelf, Is.False, "premise: the Tab panel is closed");
            Assert.That(Marks().ZoneLabelPlacements, Is.GreaterThan(0), "premise: a placement pass has run");
            for (int r = 1; r <= MapZones.Count; r++)
            {
                Assert.That(NameLabel(r).gameObject.activeSelf, Is.True, "premise: the label of " + MapZones.Name(r) + " is on");
                Assert.That(NameLabel(r).gameObject.activeInHierarchy, Is.False, "premise: the label of " + MapZones.Name(r) + " is not active in the hierarchy");
            }
        }

        /// <summary>Real order (E2): built closed, one frame, scale 1, camps delivered closed, four frames closed.</summary>
        void RealUntilOpen(Action afterFirstClosedFrame = null)
        {
            BuildController(false, _mapCamps); Frame();
            AssertRealPremises();
            afterFirstClosedFrame?.Invoke();
            Large().SetMarkScale(1f); DeliverCamps(); Frames(4);
        }

        void Open() { _controller.SetOpen(true); Frames(4); }

        void Real()
        {
            RealUntilOpen();
            Open();
            Assert.That(_controller.IsOpen, Is.True, "the map is open");
        }

        /// <summary>Control order (E1): opened before the first frame, then the camps, then scale 1.</summary>
        void Control()
        {
            BuildController(true, _mapCamps); Frame();
            DeliverCamps(); Frames(2);
            Large().SetMarkScale(1f);
        }

        /// <summary>Planning order (G1): the plan, one frame (SetMap, then the panel shows), the premises, the camps.</summary>
        void Planning()
        {
            BuildController(false, _mapCamps); DeliverPlan(); Frame();
            Assert.That(_controller.IsPlanning, Is.True, "premise: planning");
            Assert.That(Panel.gameObject.activeSelf, Is.True, "premise: the panel is shown by the first Tick");
            Assert.That(Marks().ZoneLabelPlacements, Is.GreaterThan(0), "premise: a placement pass has run");
            for (int r = 1; r <= MapZones.Count; r++)
                Assert.That(NameLabel(r).gameObject.activeInHierarchy, Is.True, "premise: the label of " + MapZones.Name(r) + " is active in the hierarchy");
            DeliverCamps(); Frames(2);
        }

        // ---- geometry in layer units (the marks object's space, y up) ----------------------------------------------------------------

        Rect LayerRect() => Marks().rectTransform.rect;

        Vector2 LayerOf(Vector2 world)
        {
            var r = LayerRect();
            return new Vector2(r.xMin + world.x / WorldSide * r.width, r.yMax - world.y / WorldSide * r.height);
        }

        static MapRect Square(Vector2 centre, float side) => new MapRect(centre.x - side / 2f, centre.y - side / 2f, side, side);

        Vector2 TextSize(string text)
        {
            foreach (var label in Marks().transform.GetComponentsInChildren<TMP_Text>(false))
                if (label.name.StartsWith("CampBadgeText") && label.text == text) return new Vector2(label.preferredWidth, label.preferredHeight);
            Assert.That(false, Is.True, "set-up: a CampBadgeText label shows '" + text + "'");
            return Vector2.zero;
        }

        /// <summary>The marks of the four edge camps as the tests see them: a plate 20.4 square at the camp, its amount text to the right.</summary>
        List<MapRect> EdgeMarks()
        {
            var list = new List<MapRect>();
            foreach (var camp in _mapCamps) list.Add(Square(LayerOf(new Vector2(camp.X, camp.Y)), 20.4f));
            for (int i = 0; i < _mapCamps.Length; i++)
            {
                var at = LayerOf(new Vector2(_mapCamps[i].X, _mapCamps[i].Y)); var size = TextSize(_campRows[i].Text);
                list.Add(new MapRect(at.x + 21.2f, at.y + 2f - size.y / 2f, size.x, size.y));
            }
            return list;
        }

        /// <summary>The label's true box: its anchored position as centre, its preferred size.</summary>
        MapRect DrawnRect(int region)
        {
            var label = NameLabel(region); var at = label.rectTransform.anchoredPosition;
            float w = label.preferredWidth, h = label.preferredHeight;
            return new MapRect(at.x - w / 2f, at.y - h / 2f, w, h);
        }

        Rect Kept(int region)
        {
            var rect = Marks().ZoneLabelRect(region);
            Assert.That(rect.HasValue, Is.True, MapZones.Name(region) + " has a name rect");
            return rect ?? default(Rect);
        }

        // ---- assertions --------------------------------------------------------------------------------------------------------------

        void AssertKeptSizesAreTrue(string step)
        {
            for (int r = 1; r <= MapZones.Count; r++)
            {
                Assert.That(NameLabel(r).gameObject.activeInHierarchy, Is.True, step + ": the label of " + MapZones.Name(r) + " is active in the hierarchy");
                var rect = Kept(r);
                Assert.That(rect.width, Is.EqualTo(NameLabel(r).preferredWidth).Within(.01f), step + ": " + MapZones.Name(r) + " kept width is the label's");
                Assert.That(rect.height, Is.EqualTo(NameLabel(r).preferredHeight).Within(.01f), step + ": " + MapZones.Name(r) + " kept height is the label's");
            }
        }

        void AssertFourLeaveSpot0(string step)
        {
            foreach (int r in new[] { Haunted, Plague, Frost, Holy })
                Assert.That(Marks().ZoneLabelSpot(r), Is.GreaterThan(0), step + ": " + MapZones.Name(r) + " leaves spot 0");
        }

        void AssertEdgeSpots(string step)
        {
            AssertFourLeaveSpot0(step);
            foreach (int r in new[] { Fire, Coliseum })
                Assert.That(Marks().ZoneLabelSpot(r), Is.EqualTo(0), step + ": " + MapZones.Name(r) + " stays on spot 0");
        }

        void AssertClearOfEdgeMarks(string step)
        {
            var marks = EdgeMarks();
            Assert.That(marks.Count, Is.EqualTo(8), step + ": four camp plates and four amount texts");
            for (int r = 1; r <= MapZones.Count; r++)
            {
                var drawn = DrawnRect(r);
                foreach (var m in marks)
                    Assert.That(drawn.Overlaps(m), Is.False, step + ": the drawn name " + MapZones.Name(r) + " overlaps no camp mark");
            }
        }

        void AssertTrueAndClear(string step)
        {
            AssertKeptSizesAreTrue(step);
            AssertEdgeSpots(step);
            AssertClearOfEdgeMarks(step);
        }

        // ---- 1. a real match --------------------------------------------------------------------------------------------------------

        [Test]
        public void TheNamesOfARealMatchAreMeasuredWhenTheMapOpens()
        {
            FindCamps(EdgeCamps);
            RealUntilOpen();
            for (int r = 1; r <= MapZones.Count; r++)
            {
                Assert.That(Kept(r).width, Is.EqualTo(0f), "closed: " + MapZones.Name(r) + " is not measured yet");
                Assert.That(Marks().ZoneLabelSpot(r), Is.EqualTo(0), "closed: " + MapZones.Name(r) + " is on spot 0");
            }
            _controller.SetOpen(true); Frames(4);
            Assert.That(_controller.IsOpen, Is.True, "the map is open");
            Assert.That(Marks().CampMarkerCount, Is.EqualTo(4), "the four camps are delivered");
            AssertTrueAndClear("opened");
        }

        // ---- 2. the planning map ----------------------------------------------------------------------------------------------------

        [Test]
        public void ThePlanningOrderMeasuresWhenThePanelShows()
        {
            FindCamps(EdgeCamps);
            Planning();
            Assert.That(Marks().CampMarkerCount, Is.EqualTo(4), "the four camps are delivered");
            AssertTrueAndClear("planning");
        }

        // ---- 3. close and open again ------------------------------------------------------------------------------------------------

        [Test]
        public void CloseAndOpenAgainChangesNothing()
        {
            FindCamps(EdgeCamps);
            Real();
            AssertEdgeSpots("settled open");
            var spot = new int[MapZones.Count + 1]; var rect = new Rect[MapZones.Count + 1];
            for (int r = 1; r <= MapZones.Count; r++) { spot[r] = Marks().ZoneLabelSpot(r); rect[r] = Kept(r); }
            _controller.SetOpen(false); Frames(3);
            Marks().LayerChanged();
            AssertSame(spot, rect, "closed, after a forced pass");
            int n = Marks().ZoneLabelPlacements;
            Frames(3);
            AssertSame(spot, rect, "closed, three more frames");
            Assert.That(Marks().ZoneLabelPlacements, Is.EqualTo(n), "closed: no pass ran in three frames");
            _controller.SetOpen(true); Frames(2);
            AssertSame(spot, rect, "opened again");
            Assert.That(Marks().ZoneLabelPlacements, Is.EqualTo(n), "opened again: no pass ran, so none moved a name");
        }

        void AssertSame(int[] spot, Rect[] rect, string step)
        {
            for (int r = 1; r <= MapZones.Count; r++)
            {
                Assert.That(Marks().ZoneLabelSpot(r), Is.EqualTo(spot[r]), step + ": " + MapZones.Name(r) + " spot");
                var now = Kept(r);
                Assert.That(now.width, Is.GreaterThan(0f), step + ": " + MapZones.Name(r) + " rect width is not 0");
                Assert.That(now.x, Is.EqualTo(rect[r].x).Within(.001f), step + ": " + MapZones.Name(r) + " rect x");
                Assert.That(now.y, Is.EqualTo(rect[r].y).Within(.001f), step + ": " + MapZones.Name(r) + " rect y");
                Assert.That(now.width, Is.EqualTo(rect[r].width).Within(.001f), step + ": " + MapZones.Name(r) + " rect width");
                Assert.That(now.height, Is.EqualTo(rect[r].height).Within(.001f), step + ": " + MapZones.Name(r) + " rect height");
            }
        }

        // ---- 4. a bigger text size ---------------------------------------------------------------------------------------------------

        [Test]
        public void ATextSizeChangedWhileClosedIsMeasuredWhenOpened()
        {
            FindCamps(EdgeCamps);
            try
            {
                RealUntilOpen(() =>
                {
                    float before = NameLabel(1).fontSize;
                    MythicUi.ApplyTextScale(Panel, 1.5f); Marks().LayerChanged();
                    Assert.That(NameLabel(1).fontSize, Is.GreaterThan(10f), "premise: the zone names are at the 1.5 text size");
                    Assert.That(NameLabel(1).fontSize, Is.GreaterThan(before), "premise: the font grew");
                });
                Open();
                Assert.That(_controller.IsOpen, Is.True, "the map is open");
                AssertKeptSizesAreTrue("text 1.5");
                AssertFourLeaveSpot0("text 1.5");   // the 1.5 names are wide: COLISEUM may leave spot 0 too, so it is not pinned
            }
            finally { MythicUi.ApplyTextScale(Panel, 1f); }
        }

        [Test]
        public void ANameKeepsNoStaleSizeAtAnOldFontWhileClosed()
        {
            FindCamps(EdgeCamps);
            try
            {
                Real();
                _controller.SetOpen(false); Frame();
                float before = NameLabel(1).fontSize;
                MythicUi.ApplyTextScale(Panel, 1.5f); Marks().LayerChanged();
                Assert.That(NameLabel(1).fontSize, Is.GreaterThan(before), "premise: the font grew while closed");
                for (int r = 1; r <= MapZones.Count; r++)
                    Assert.That(Kept(r).width, Is.EqualTo(0f), MapZones.Name(r) + ": closed at a new font, no size is kept");
                _controller.SetOpen(true); Frames(4);
                AssertKeptSizesAreTrue("reopened at 1.5");
            }
            finally { MythicUi.ApplyTextScale(Panel, 1f); }
        }

        // ---- 5. fifty camps ---------------------------------------------------------------------------------------------------------

        [Test]
        public void FiftyCampsInTheRealOrderGiveTheSpotsOfTheControlOrder()
        {
            FindCamps(FiftyCamps());
            var camps = _mapCamps; var rows = _campRows;
            Control();
            Assert.That(Marks().CampMarkerCount, Is.EqualTo(50), "premise: fifty camps in the control order");
            var spot = new int[MapZones.Count + 1]; int off = 0;
            for (int r = 1; r <= MapZones.Count; r++) { spot[r] = Marks().ZoneLabelSpot(r); if (spot[r] > 0) off++; }
            Assert.That(off, Is.GreaterThanOrEqualTo(4), "premise: at least four names leave spot 0 in the control order");
            Reset();
            _mapCamps = camps; _campRows = rows;
            Real();
            Assert.That(Marks().CampMarkerCount, Is.EqualTo(50), "fifty camps in the real order");
            Large().SetMarkScale(1f); Frame();
            for (int r = 1; r <= MapZones.Count; r++)
                Assert.That(Marks().ZoneLabelSpot(r), Is.EqualTo(spot[r]), MapZones.Name(r) + ": the real order gives the control's spot");
        }

        // ---- 6. standing guard: today's result --------------------------------------------------------------------------------------

        [Test]
        public void AMapOpenedBeforeTheFirstFrameGivesTodaysResult()
        {
            FindCamps(EdgeCamps);
            Control();
            AssertTrueAndClear("control order");
        }

        // ---- 7. a rebind ------------------------------------------------------------------------------------------------------------

        void Rebind()
        {
            var old = _session;
            var map2 = new MapBlob(Tiles, Tiles, new byte[Tiles * Tiles], null, _regions);
            _transport = new Fix3Transport();
            _session = Fix3Transport.Join(_transport, map2, _wireSelf);
            _controller.Bind(_session);
            old.Leave();
        }

        [Test]
        public void ARebindForgetsTheLastMatchsCamps()
        {
            FindCamps(EdgeCamps);
            Real();
            AssertEdgeSpots("first match");
            Assert.That(Marks().CampMarkerCount, Is.EqualTo(4), "first match: four camps");
            _controller.SetOpen(false); Frame();
            Rebind();
            Frame();
            AssertRealPremises();
            Assert.That(Marks().CampMarkerCount, Is.EqualTo(0), "second match: the last match's camps are gone");
            Frames(3);
            for (int r = 1; r <= MapZones.Count; r++)
                Assert.That(Marks().ZoneLabelSpot(r), Is.EqualTo(0), "second match, closed: " + MapZones.Name(r) + " is on spot 0");
            _controller.SetOpen(true); Frames(4);
            Assert.That(_controller.IsOpen, Is.True, "the map is open");
            Assert.That(Marks().CampMarkerCount, Is.EqualTo(0), "second match, open: no camps");
            for (int r = 1; r <= MapZones.Count; r++)
            {
                Assert.That(NameLabel(r).gameObject.activeInHierarchy, Is.True, "second match, open: the label of " + MapZones.Name(r) + " is active in the hierarchy");
                Assert.That(Marks().ZoneLabelSpot(r), Is.EqualTo(0), "second match, open: " + MapZones.Name(r) + " is on spot 0");
            }
            AssertKeptSizesAreTrue("second match, open");
        }
    }
}
