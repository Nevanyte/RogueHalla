using System;
using System.Collections.Generic;
using System.Reflection;
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
    /// P-UI-TABMAP-TEXT-2: the Tab map says each camp bonus once, in the map key. A fixed kind's amount and every "Aug" leave the map and
    /// stand in a key cell (the cells of the kinds seen pack from slot 0); a kind with two amounts keeps every text of its kind; nothing
    /// that ticks moves a cell or a text; a bind forgets the latch; the key is whole on the three canvases at both text sizes; a window
    /// too short or narrow for the key keeps the old texts; planning keeps them; a steady frame allocates nothing; the key's rows stand
    /// where <see cref="CampKeyLayout"/> puts them. The fixture is the real order of <see cref="CampTextEditModeTests"/> (built closed,
    /// rows delivered closed, three frames, then opened). A text label never active in the hierarchy measures a tenth of its size, so a
    /// width assertion first asserts the label is active. The packet (docs/backlog/packets/P-UI-TABMAP-TEXT-2.md) has the rules and numbers.
    /// </summary>
    public sealed class CampKeyEditModeTests
    {
        Vector2 Window = new Vector2(1280, 720);
        const int Tiles = 6;

        GameObject _canvas, _stageObject, _clientObject, _controllerObject, _settingsObject;
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
        ShowcaseSettings _settings;
        float _priorScale;
        double _now = 100d;
        byte[] _regions;
        uint _tick = 10;
        MapCamp[] _mapCamps;
        WireSelf _wireSelf;

        // the quanta of the shipped table, by kind (the same as CampAmountKeyTests)
        static readonly ushort[] Quanta = { 10000, 10000, 100, 100, 25, 100, 12500, 15000, 50 };

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
                if (_settings != null) ScaleField().SetValue(_settings, _priorScale);
                if (_settingsObject != null) UnityEngine.Object.DestroyImmediate(_settingsObject);
                _settings = null; _settingsObject = null;
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

        static FieldInfo ScaleField() => typeof(ShowcaseSettings).GetField("_textScale", BindingFlags.Instance | BindingFlags.NonPublic);

        /// <summary>The text size setting for the next build (the labels BuildKey builds and TextScale() read it): set before BuildController.</summary>
        void UseTextScale(float scale)
        {
            if (_settings == null)
            {
                _settings = ShowcaseSettings.Current;
                if (_settings == null)
                {
                    _settingsObject = new GameObject("key text scale settings");
                    _settings = _settingsObject.AddComponent<ShowcaseSettings>();
                    typeof(ShowcaseSettings).GetMethod("Awake", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(_settings, null);
                }
                _priorScale = (float)ScaleField().GetValue(_settings);
            }
            ScaleField().SetValue(_settings, scale);
        }

        float Ts() { var s = ShowcaseSettings.Current; return s != null ? Mathf.Clamp(s.TextScale, 1f, 1.5f) : 1f; }

        static Vector2 StageUnits(Vector2 window)
        {
            float scale = Mathf.Clamp(Mathf.Min(window.x / 1280f, window.y / 800f), .8f, 1.5f);
            return new Vector2(window.x / scale, window.y / scale);
        }

        void BuildController(bool open, MapCamp[] camps)
        {
            _mapCamps = camps;
            _canvas = new GameObject("camp key canvas", typeof(RectTransform), typeof(Canvas), typeof(UnityEngine.UI.GraphicRaycaster));
            _stageObject = new GameObject("camp key stage", typeof(RectTransform));
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
            _clientObject = new GameObject("camp key client");
            _client = _clientObject.AddComponent<MatchClient>(); _client.enabled = false; _client.Url = "ws://localhost/camp-key"; _client.Profile = "fixture";
            _controllerObject = new GameObject("camp key controller");
            _controller = _controllerObject.AddComponent<TacticalMapController>();
            if (_prefs == null) { _prefs = new UiDisplayPreferences(); _priorToggle = _prefs.MapToggle; }
            _controller.Initialize(_client, _canvas.transform, _prefs, _stage);
            _controller.SetToggleMode(false);
            _controller.Bind(_session);
            if (open) _controller.SetOpen(true);
        }

        void DeliverPlan()
        {
            _transport.Deliver(new ServerMsg.Snapshot { Tick = 2, ServerTimeOverrideMs = 100, Phase = (byte)MatchPhase.Planning,
                Me = new WireSelf { Id = 1, X = 48, Y = 48, Hp = 100, MaxHp = 100, Status = 1, Weapon = 0 } });
            _transport.Deliver(new ServerMsg.State { Tick = 2, Items = new StateItem[] { new PlanState { Value = new WirePlan {
                EndsAt = 10000, Spawn0 = 5, Spawn1 = 8, Pick = 1, Boon = 0, Boons = new[] { "First boon", "Second boon" },
                Option0 = new WirePlanOption { Index = 5, X = 64, Y = 64, Zone = 1 },
                Option1 = new WirePlanOption { Index = 8, X = 96, Y = 96, Zone = 2 } } } } });
        }

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

        /// <summary>A second match on the same camps: a new session and a bind (SetMap(null) runs inside Bind), the old one left.</summary>
        void RebindKeepingCamps()
        {
            var old = _session;
            var map2 = new MapBlob(Tiles, Tiles, new byte[Tiles * Tiles], null, _regions).WithPublic(_mapCamps, null, null, null, null);
            _transport = new Fix3Transport();
            _session = Fix3Transport.Join(_transport, map2, _wireSelf);
            _controller.Bind(_session);
            old.Leave();
        }

        // ---- deliveries --------------------------------------------------------------------------------------------------------------

        /// <summary>A camp row with a reward (the wire refuses an amount of 0); a cleared camp (state 2) is not alive.</summary>
        static WireCamp Row(byte stat, ushort amount, bool elite = false, byte state = 0, double respawnAt = 0, WireWard? ward = null)
        {
            return new WireCamp { MobType = 0, Elite = (byte)(elite ? 1 : 0), Alive = (byte)(state == 2 ? 0 : 3), State = state, ClearedAt = state == 2 ? 1000 : 0, RespawnAt = respawnAt, Ward = ward,
                Reward = new WireCampReward { Stat = stat, Amount = amount } };
        }

        void DeliverRows(params WireCamp[] rows)
        {
            _transport.Deliver(new ServerMsg.State { Tick = _tick++, Items = new StateItem[] { new CampsState { Rows = rows } } });
        }

        /// <summary>The real order: built closed, one frame, the rows delivered closed, three frames closed (nothing is placed).</summary>
        void RealUntilOpen(MapCamp[] camps, params WireCamp[] rows)
        {
            BuildController(false, camps); Frame();
            DeliverRows(rows); Frames(3);
        }

        void Open(int frames = 3) { _controller.SetOpen(true); Frames(frames); }

        void Close() { _controller.SetOpen(false); Frame(); }

        static MapCamp[] Camps(params Vector2[] world)
        {
            var list = new MapCamp[world.Length];
            for (int i = 0; i < list.Length; i++) list[i] = new MapCamp((ushort)world[i].x, (ushort)world[i].y, 8, 1);
            return list;
        }

        static MapCamp[] FourCamps() => Camps(new Vector2(184, 96), new Vector2(184, 40), new Vector2(96, 4), new Vector2(96, 100));

        /// <summary>Test 1's rows: damage (+3%), defense elite (+3.75), defense (+3.75), offense (+3).</summary>
        static WireCamp[] FourRows() => new[] { Row(2, 100), Row(6, 12500, true), Row(6, 12500), Row(0, 10000) };

        static MapCamp[] NineCamps()
        {
            var list = new MapCamp[9];
            for (int k = 0; k < 9; k++) list[k] = new MapCamp((ushort)(30 + k % 3 * 66), (ushort)(30 + k / 3 * 66), 8, 1);
            return list;
        }

        /// <summary>One camp per kind with the shipped quanta, the one of kind 8 elite.</summary>
        static WireCamp[] NineRows()
        {
            var rows = new WireCamp[9];
            for (int k = 0; k < 9; k++) rows[k] = Row((byte)k, Quanta[k], k == 8);
            return rows;
        }

        // ---- the map's texts ---------------------------------------------------------------------------------------------------------

        IEnumerable<TMP_Text> ActiveBadgeLabels()
        {
            foreach (var label in Marks().transform.GetComponentsInChildren<TMP_Text>(true))
                if (label.name.StartsWith("CampBadgeText") && label.gameObject.activeInHierarchy) yield return label;
        }

        int ActiveBadgeCount() { int n = 0; foreach (var label in ActiveBadgeLabels()) n++; return n; }

        bool Drawn(string text) { foreach (var label in ActiveBadgeLabels()) if (label.text == text) return true; return false; }

        // ---- the key -----------------------------------------------------------------------------------------------------------------

        RectTransform KeyRoot() => Part("TacticalInformation/ContactsPage/Key");

        float KeyW() => KeyRoot().sizeDelta.x;

        RectTransform CellRect(int kind) => KeyRoot().Find("KindCell" + kind) as RectTransform;

        bool CellActive(int kind) { var c = CellRect(kind); return c != null && c.gameObject.activeInHierarchy; }

        TMP_Text CellLabel(int kind)
        {
            var c = CellRect(kind); var t = c != null ? c.Find("Text") : null;
            return t != null ? t.GetComponent<TMP_Text>() : null;
        }

        /// <summary>The text of a cell; null when the cell (or its label) does not exist.</summary>
        string CellText(int kind) { var l = CellLabel(kind); return l != null ? l.text : null; }

        /// <summary>Asserts the cell is active, reads its text and stands at the layout's cell of its slot (the rect is its anchored position and size).</summary>
        void AssertCell(int kind, int slot, string text, string step)
        {
            var c = CellRect(kind);
            Assert.That(c, Is.Not.Null, step + ": KindCell" + kind + " exists");
            Assert.That(CellActive(kind), Is.True, step + ": KindCell" + kind + " is active in the hierarchy");
            Assert.That(CellText(kind), Is.EqualTo(text), step + ": KindCell" + kind + " text");
            if (c == null) return;
            var want = CampKeyLayout.Cell(slot, KeyW());
            var at = new Rect(c.anchoredPosition.x, -c.anchoredPosition.y, c.sizeDelta.x, c.sizeDelta.y);
            Assert.That(new[] { at.x, at.y, at.width, at.height }, Is.EqualTo(new[] { want.X, want.Y, want.W, want.H }).Within(.01f), step + ": KindCell" + kind + " stands at slot " + slot);
            Assert.That(want.W, Is.GreaterThan(0f), step + ": the layout has a cell at slot " + slot);
        }

        void AssertCellInactive(int kind, string step)
        {
            Assert.That(CellRect(kind), Is.Not.Null, step + ": KindCell" + kind + " exists");
            Assert.That(CellActive(kind), Is.False, step + ": KindCell" + kind + " is inactive");
        }

        string[] CellTexts()
        {
            var texts = new string[9];
            for (int k = 0; k < 9; k++) texts[k] = CellActive(k) ? CellText(k) : null;
            return texts;
        }

        // ---- 1. a fixed kind leaves the map -------------------------------------------------------------------------------------------

        [Test]
        public void AFixedKindLeavesTheMapAndStandsInTheKey()
        {
            RealUntilOpen(FourCamps(), FourRows());
            Open();
            Assert.That(_controller.IsOpen, Is.True, "premise: the map is open");
            Assert.That(Marks().CampMarkerCount, Is.EqualTo(4), "premise: four camps are delivered");
            Assert.That(Marks().CampFactsInKey, Is.True, "the key says the amounts");
            Assert.That(ActiveBadgeCount(), Is.EqualTo(0), "no camp text is drawn");
            for (int i = 0; i < 4; i++) Assert.That(Marks().CampTextRect(i).HasValue, Is.False, "camp " + i + " has no text block");
            Assert.That(Marks().CampTextPlacements, Is.EqualTo(0), "no placement pass ran");
            Assert.That(Marks().EliteMarkersDrawn, Is.EqualTo(1), "the elite's gold diamond stays");
            AssertCell(0, 0, "+3 Offense", "slot 0");
            AssertCell(2, 1, "+3% Damage", "slot 1");
            AssertCell(6, 2, "+3.75 Defense", "slot 2");
            foreach (int k in new[] { 1, 3, 4, 5, 7, 8 }) AssertCellInactive(k, "kind not seen");
            var icon = CellRect(0) != null ? CellRect(0).Find("Icon") : null;
            Assert.That(icon, Is.Not.Null, "KindCell0 has an Icon");
            if (icon != null)
            {
                var image = icon.GetComponent<UnityEngine.UI.Image>();
                Assert.That(image != null && image.sprite == CampRespawnLabels.Art(CampBadges.IconKey(0)), Is.True, "the Icon shows the map's glyph of the kind");
                uint rgb = CampBadges.IconRgb(0);
                var want = new Color32((byte)(rgb >> 16 & 255), (byte)(rgb >> 8 & 255), (byte)(rgb & 255), 255);
                Color32 have = image != null ? (Color32)image.color : default(Color32);
                Assert.That(new[] { (int)have.r, have.g, have.b }, Is.EqualTo(new[] { (int)want.r, want.g, want.b }).Within(2), "the Icon is tinted with the kind's colour");
            }
        }

        // ---- 2. a kind with two amounts keeps every text ------------------------------------------------------------------------------

        [Test]
        public void ADifferingAmountKeepsEveryTextOfItsKind()
        {
            RealUntilOpen(FourCamps(), new[] { Row(2, 25), Row(2, 50), Row(0, 10000), Row(0, 10000) });
            Open();
            Assert.That(Marks().CampFactsInKey, Is.True, "the key is on");
            AssertCell(0, 0, "+3 Offense", "slot 0");
            AssertCell(2, 1, "Damage · varies", "slot 1");
            Assert.That(Marks().CampTextRect(0).HasValue, Is.True, "camp 0 has a block");
            Assert.That(Marks().CampTextRect(1).HasValue, Is.True, "camp 1 has a block");
            Assert.That(Marks().CampTextRect(2).HasValue, Is.False, "camp 2 (a fixed kind) has none");
            Assert.That(Marks().CampTextRect(3).HasValue, Is.False, "camp 3 (a fixed kind) has none");
            Assert.That(Drawn("+0.75%"), Is.True, "camp 0's amount is drawn");
            Assert.That(Drawn("+1.5%"), Is.True, "camp 1's amount is drawn");
            Assert.That(Drawn("+3"), Is.False, "the offense amount is not");
            Assert.That(ActiveBadgeCount(), Is.EqualTo(2), "two texts in all");
        }

        // ---- 3. a clear, a clock and a ward move nothing -----------------------------------------------------------------------------

        struct State { public int Version, Placements; public string[] Cells; }

        State Record() => new State { Version = Marks().CampKey.Version, Placements = Marks().CampTextPlacements, Cells = CellTexts() };

        void AssertSame(State first, string step)
        {
            var now = Record();
            Assert.That(now.Version, Is.EqualTo(first.Version), step + ": the key's version");
            Assert.That(now.Placements, Is.EqualTo(first.Placements), step + ": the placement passes");
            Assert.That(now.Cells, Is.EqualTo(first.Cells), step + ": the cells' texts");
        }

        [Test]
        public void ClearingACampAndItsClockMoveNothing()
        {
            RealUntilOpen(FourCamps(), FourRows());
            Open();
            Assert.That(Marks().CampKey.Version, Is.GreaterThan(0), "premise: the key has learned the kinds");
            var first = Record();
            Assert.That(first.Cells[0], Is.EqualTo("+3 Offense"), "premise: the cells are filled");

            DeliverRows(Row(2, 100, false, 2, 180050), Row(6, 12500, true), Row(6, 12500), Row(0, 10000)); Frames(2);
            var clock = Marks().transform.Find("CampClock0");
            Assert.That(clock != null && !string.IsNullOrEmpty(clock.GetComponent<TMP_Text>().text), Is.True, "premise: camp 0's clock has a text");
            AssertSame(first, "camp 0 cleared");

            DeliverRows(Row(2, 100, false, 2, 180050), Row(6, 12500, true), Row(6, 12500), Row(0, 10000, false, 0, 0, new WireWard { Yours = true, ExpiresAt = 90050 })); Frames(2);
            Assert.That(Marks().WardMarkCount, Is.EqualTo(1), "premise: camp 3 has a ward");
            AssertSame(first, "a ward on camp 3");

            DeliverRows(FourRows()); Frames(2);
            Assert.That(Marks().WardMarkCount, Is.EqualTo(0), "premise: the ward is gone");
            AssertSame(first, "both gone");
        }

        // ---- 4. a late differing amount: one pass, and the latch -----------------------------------------------------------------------

        [Test]
        public void ALateDifferingAmountMakesOnePassAndStays()
        {
            var camps = Camps(new Vector2(96, 96), new Vector2(30, 40), new Vector2(150, 150));
            RealUntilOpen(camps, Row(2, 100), Row(2, 100), Row(0, 10000));
            Open();
            Assert.That(Marks().CampFactsInKey, Is.True, "premise: the key is on");
            Assert.That(ActiveBadgeCount(), Is.EqualTo(0), "no text at first");
            int passes = Marks().CampTextPlacements;
            Assert.That(passes, Is.EqualTo(0), "premise: no pass ran");

            DeliverRows(Row(2, 100), Row(2, 100), Row(2, 50)); Frames(2);
            Assert.That(Marks().CampTextPlacements, Is.EqualTo(passes + 1), "one pass for the kind that now varies");
            for (int i = 0; i < 3; i++) Assert.That(Marks().CampTextRect(i).HasValue, Is.True, "camp " + i + " has a block");
            Assert.That(ActiveBadgeCount(), Is.EqualTo(3), "all three texts are drawn");
            Frames(2);
            Assert.That(Marks().CampTextPlacements, Is.EqualTo(passes + 1), "no more while nothing changes");

            DeliverRows(Row(2, 100), Row(2, 100), Row(2, 100)); Frames(2);
            Assert.That(Marks().CampTextPlacements, Is.EqualTo(passes + 2), "exactly one more pass: camp 2's word changed");
            for (int i = 0; i < 3; i++) Assert.That(Marks().CampTextRect(i).HasValue, Is.True, "camp " + i + " still has a block: the kind stays not fixed");
            Assert.That(ActiveBadgeCount(), Is.EqualTo(3), "all three texts are still drawn");
            Assert.That(Marks().CampKey.IsFixed(2), Is.False, "the latch holds");
        }

        // ---- 5. a bind forgets the latch ----------------------------------------------------------------------------------------------

        [Test]
        public void ABindForgetsTheLatch()
        {
            var camps = Camps(new Vector2(96, 96), new Vector2(30, 40), new Vector2(150, 150));
            RealUntilOpen(camps, Row(2, 100), Row(2, 100), Row(0, 10000));
            Open();
            DeliverRows(Row(2, 100), Row(2, 100), Row(2, 50)); Frames(2);
            Assert.That(Marks().CampKey.IsFixed(2), Is.False, "premise: damage varies");
            Assert.That(ActiveBadgeCount(), Is.EqualTo(3), "premise: its texts are drawn");
            Close();
            RebindKeepingCamps();
            DeliverRows(Row(2, 100), Row(2, 100), Row(0, 10000)); Frames(3);
            Open();
            Assert.That(Marks().CampMarkerCount, Is.EqualTo(3), "premise: the second match's camps are delivered");
            Assert.That(Marks().CampFactsInKey, Is.True, "premise: the key is on");
            Assert.That(ActiveBadgeCount(), Is.EqualTo(0), "no text: damage is fixed again");
            AssertCell(2, 1, "+3% Damage", "after the bind");
        }

        // ---- 6. the key is whole on the three canvases ---------------------------------------------------------------------------------

        static readonly string[] NineTexts =
        {
            "+3 Offense", "+3 Soul Offence", "+3% Damage", "+3% Attack speed", "+0.75% Life steal", "+3% Max health", "+3.75 Defense", "+4.5 Soul Defense", "+1.5% Move speed",
        };

        static readonly Vector2[] Canvases = { new Vector2(1920, 1080), new Vector2(1280, 800), new Vector2(980, 2121) };
        static readonly Vector2[] Stages = { new Vector2(1422, 800), new Vector2(1280, 800), new Vector2(1225, 2651) };

        [Test]
        public void TheKeyIsWholeOnTheThreeCanvasesAtTextSizeOneAndOnePointFive()
        {
            for (int c = 0; c < Canvases.Length; c++)
                foreach (float ts in new[] { 1f, 1.5f })
                {
                    Window = Canvases[c]; UseTextScale(ts);
                    RealUntilOpen(NineCamps(), NineRows());
                    Open();
                    string step = Canvases[c].x + "x" + Canvases[c].y + " text " + ts;
                    Assert.That(Ts(), Is.EqualTo(ts), step + ": premise: the text size is set");
                    Assert.That(Mathf.Abs(_stage.sizeDelta.x - Stages[c].x) < 1f && Mathf.Abs(_stage.sizeDelta.y - Stages[c].y) < 1f, Is.True, step + ": premise: the stage is " + Stages[c]);
                    var layout = MapScreenLayout.Compute(_stage.sizeDelta.x, _stage.sizeDelta.y, MapScreenMode.Tab, ts);
                    Assert.That(Marks().CampFactsInKey, Is.True, step + ": the key is on");
                    Assert.That(KeyRoot().gameObject.activeInHierarchy, Is.True, step + ": the key root is active");
                    float w = KeyW();
                    Assert.That(w, Is.EqualTo(layout.Left.W).Within(.01f), step + ": the key is as wide as the rail");
                    float floor = ts == 1f ? 9f : 10.5f;
                    var boxes = new Rect[9];
                    for (int k = 0; k < 9; k++)
                    {
                        AssertCell(k, k, NineTexts[k], step + " kind " + k);
                        var label = CellLabel(k);
                        Assert.That(label != null && label.gameObject.activeInHierarchy, Is.True, step + ": kind " + k + " label is active in the hierarchy");
                        if (label != null)
                        {
                            Assert.That(label.preferredWidth, Is.LessThanOrEqualTo(label.rectTransform.rect.width + .01f), step + ": kind " + k + " text fits its box");
                            Assert.That(label.fontSize, Is.GreaterThanOrEqualTo(floor), step + ": kind " + k + " is at least " + floor);
                        }
                        var cell = CellRect(k);
                        if (cell != null) boxes[k] = new Rect(cell.anchoredPosition.x, -cell.anchoredPosition.y, cell.sizeDelta.x, cell.sizeDelta.y);
                    }
                    for (int slot = 0; slot + 1 < 9; slot += 2)
                        Assert.That(boxes[slot].xMax, Is.LessThanOrEqualTo(boxes[slot + 1].xMin + .01f), step + ": the cells of row " + slot / 2 + " do not overlap");
                    var key = KeyRoot();
                    for (int k = 0; k < 9; k++)
                        Assert.That(new MapRect(boxes[k].x, boxes[k].y, boxes[k].width, boxes[k].height).Within(w, key.sizeDelta.y), Is.True, step + ": kind " + k + " lies inside the key");
                    Assert.That(key.sizeDelta.y, Is.EqualTo(332f), step + ": the key's height");
                    Assert.That(CampKeyLayout.KeyTop + key.sizeDelta.y, Is.LessThanOrEqualTo(layout.Left.H), step + ": the key lies inside the rail");

                    RectTransform elite = RowWithText("Elite camp: augment chest");
                    Assert.That(elite, Is.Not.Null, step + ": the elite row exists");
                    var eliteLabel = elite != null ? elite.Find("Text").GetComponent<TMP_Text>() : null;
                    Assert.That(eliteLabel != null && eliteLabel.gameObject.activeInHierarchy, Is.True, step + ": the elite label is active");
                    if (eliteLabel != null) Assert.That(eliteLabel.preferredWidth, Is.LessThanOrEqualTo(eliteLabel.rectTransform.rect.width + .01f), step + ": the elite row's label fits");

                    var corners = new Vector3[4]; var head = new Vector3[4];
                    var last = (RectTransform)key.GetChild(key.childCount - 1);
                    last.GetWorldCorners(corners); ((RectTransform)Part("TacticalInformation/ContactsPage/InViewHeading")).GetWorldCorners(head);
                    Assert.That(head[1].y, Is.LessThanOrEqualTo(corners[0].y + .01f), step + ": the IN VIEW heading is at or below the last key row");
                    var list = Part("TacticalInformation/ContactsPage/DeliveredContacts");
                    Assert.That(list.rect.height, Is.GreaterThanOrEqualTo(110f), step + ": the list is at least 110 high");
                    Assert.That(list.rect.height, Is.EqualTo(CampKeyLayout.ListHeight(layout.Left.H)).Within(.01f), step + ": the list is the layout's height");
                    Reset();
                }

            // the not-fixed cell of the longest words: the shipped table cannot produce it, so it is held to a lower floor (the packet records its size)
            foreach (float ts in new[] { 1f, 1.5f })
            {
                Window = new Vector2(1280, 800); UseTextScale(ts);
                RealUntilOpen(Camps(new Vector2(60, 60), new Vector2(120, 120)), Row(7, 15000), Row(7, 10000));
                Open();
                string step = "varies, text " + ts;
                AssertCell(7, 0, "Soul Defense · varies", step);
                var label = CellLabel(7);
                Assert.That(label != null && label.gameObject.activeInHierarchy, Is.True, step + ": the label is active in the hierarchy");
                if (label != null)
                {
                    Assert.That(label.preferredWidth, Is.LessThanOrEqualTo(label.rectTransform.rect.width + .01f), step + ": the text fits its box");
                    Assert.That(label.fontSize, Is.GreaterThanOrEqualTo(6f), step + ": at least the lane's smallest legible size");
                }
                Reset();
            }
        }

        RectTransform RowWithText(string text)
        {
            var key = KeyRoot();
            for (int i = 0; i < key.childCount; i++)
            {
                var t = key.GetChild(i).Find("Text");
                if (t != null && t.GetComponent<TMP_Text>() != null && t.GetComponent<TMP_Text>().text == text) return (RectTransform)key.GetChild(i);
            }
            return null;
        }

        // ---- 7. a compact window keeps the texts ---------------------------------------------------------------------------------------

        [Test]
        public void ACompactWindowKeepsTheTexts()
        {
            foreach (var pair in new[] { (new Vector2(1000, 480), 474f), (new Vector2(1000, 560), 574f) })
            {
                Window = pair.Item1;
                RealUntilOpen(FourCamps(), FourRows());
                Open();
                var stage = _stage.sizeDelta;
                float leftH = MapScreenLayout.Compute(stage.x, stage.y, MapScreenMode.Tab, 1f).Left.H;
                string step = Window.x + "x" + Window.y;
                Assert.That(leftH, Is.EqualTo(pair.Item2).Within(.01f), step + ": premise: the left column's height");
                Assert.That(leftH, Is.LessThan(610f).And.GreaterThanOrEqualTo(430f), step + ": premise: under the key's minimum, and the old rule showed the key");
                Assert.That(KeyRoot().gameObject.activeInHierarchy, Is.False, step + ": the key is hidden");
                Assert.That(Marks().CampFactsInKey, Is.False, step + ": the map keeps its texts");
                Assert.That(Drawn("+3%"), Is.True, step + ": damage amount drawn");
                Assert.That(Drawn("+3.75"), Is.True, step + ": defense amount drawn");
                Assert.That(Drawn("+3"), Is.True, step + ": offense amount drawn");
                Assert.That(Drawn("Aug"), Is.True, step + ": the elite tag drawn");
                for (int i = 0; i < 4; i++) Assert.That(Marks().CampTextRect(i).HasValue, Is.True, step + ": camp " + i + " has a block");
                Reset();
            }

            Window = new Vector2(1000, 600);
            RealUntilOpen(FourCamps(), FourRows());
            Open();
            var stage2 = _stage.sizeDelta;
            float h2 = MapScreenLayout.Compute(stage2.x, stage2.y, MapScreenMode.Tab, 1f).Left.H;
            Assert.That(h2, Is.EqualTo(624f).Within(.01f), "1000x600: premise: the left column's height");
            Assert.That(KeyRoot().gameObject.activeInHierarchy, Is.True, "1000x600: the key is shown");
            Assert.That(Marks().CampFactsInKey, Is.True, "1000x600: the map drops its texts");
            Assert.That(ActiveBadgeCount(), Is.EqualTo(0), "1000x600: no text on the map");
            Assert.That(Part("TacticalInformation/ContactsPage/DeliveredContacts").rect.height, Is.EqualTo(124f).Within(.01f), "1000x600: the list is 124 high");
        }

        // ---- 8. planning keeps the texts ------------------------------------------------------------------------------------------------

        [Test]
        public void PlanningKeepsTheTexts()
        {
            BuildController(false, FourCamps()); DeliverPlan(); Frame();
            Assert.That(_controller.IsPlanning, Is.True, "premise: planning");
            DeliverRows(FourRows()); Frames(2);
            for (int k = 0; k < 9; k++) Assert.That(CellRect(k), Is.Not.Null, "KindCell" + k + " exists");
            for (int k = 0; k < 9; k++) Assert.That(CellActive(k), Is.False, "KindCell" + k + " is not active in planning");
            Assert.That(Marks().CampFactsInKey, Is.False, "the map keeps its texts");
            for (int i = 0; i < 4; i++) Assert.That(Marks().CampTextRect(i).HasValue, Is.True, "camp " + i + " has a block");
            Assert.That(Drawn("Aug"), Is.True, "the elite tag is drawn");
            Assert.That(Drawn("+3.75"), Is.True, "a fixed kind's amount is drawn");
        }

        // ---- 9. a steady frame allocates nothing -----------------------------------------------------------------------------------------

        [Test]
        public void ASteadyFrameAllocatesNothing()
        {
            RealUntilOpen(FourCamps(), FourRows());
            Open();
            Assert.That(Marks().CampKey.Version, Is.GreaterThan(0), "premise: the key has learned the kinds");
            var marks = Marks();
            Action present = () => marks.Present(_session, null);
            Action layer = () => marks.LayerChanged();
            var method = typeof(TacticalMapController).GetMethod("ApplyCampKey", BindingFlags.Instance | BindingFlags.NonPublic, null, Type.EmptyTypes, null);
            Assert.That(method, Is.Not.Null, "the controller has an instance method ApplyCampKey()");
            Action apply = method != null ? (Action)Delegate.CreateDelegate(typeof(Action), _controller, method) : null;
            AllocationMeter.AssertNone(present, 200, "a steady Present");
            AllocationMeter.AssertNone(layer, 200, "a forced camp-text pass (LayerChanged)");
            if (apply != null) AllocationMeter.AssertNone(apply, 200, "a steady ApplyCampKey");
        }

        // ---- 10. a late kind shifts the cells after it once ---------------------------------------------------------------------------------

        [Test]
        public void ALateKindShiftsTheCellsAfterItOnce()
        {
            var camps = Camps(new Vector2(96, 96), new Vector2(30, 40), new Vector2(150, 150));
            RealUntilOpen(camps, Row(0, 10000), Row(6, 12500), Row(6, 12500));
            Open();
            AssertCell(0, 0, "+3 Offense", "before");
            AssertCell(6, 1, "+3.75 Defense", "before");
            int version = Marks().CampKey.Version;
            Assert.That(version, Is.GreaterThan(0), "premise: the key has learned");

            DeliverRows(Row(0, 10000), Row(6, 12500), Row(2, 100)); Frames(2);
            AssertCell(0, 0, "+3 Offense", "after the late kind");
            AssertCell(2, 1, "+3% Damage", "after the late kind");
            AssertCell(6, 2, "+3.75 Defense", "after the late kind");
            Assert.That(Marks().CampKey.Version, Is.EqualTo(version + 1), "one change");

            DeliverRows(Row(0, 10000), Row(6, 12500), Row(2, 100)); Frames(2);
            AssertCell(0, 0, "+3 Offense", "the same rows again");
            AssertCell(2, 1, "+3% Damage", "the same rows again");
            AssertCell(6, 2, "+3.75 Defense", "the same rows again");
            Assert.That(Marks().CampKey.Version, Is.EqualTo(version + 1), "the version stays");
        }

        // ---- 11. the key's rows stand where the layout puts them ----------------------------------------------------------------------------

        static float RowY(Transform row) => row != null ? -((RectTransform)row).anchoredPosition.y : -1f;
        static Transform ChildAt(Transform parent, int i) => i < parent.childCount ? parent.GetChild(i) : null;
        static RectTransform TextRectOf(Transform row) => row != null ? row.Find("Text") as RectTransform : null;
        static string TextOf(Transform row) { var t = TextRectOf(row); return t != null && t.GetComponent<TMP_Text>() != null ? t.GetComponent<TMP_Text>().text : null; }

        [Test]
        public void TheKeyRowsStandWhereTheLayoutPutsThem()
        {
            Window = new Vector2(1280, 800);
            RealUntilOpen(FourCamps(), FourRows());
            Open();
            var key = KeyRoot();
            var layout = MapScreenLayout.Compute(_stage.sizeDelta.x, _stage.sizeDelta.y, MapScreenMode.Tab, 1f);
            Assert.That(key.sizeDelta.x, Is.EqualTo(layout.Left.W).Within(.01f), "the key is as wide as the rail");
            Assert.That(key.sizeDelta.y, Is.EqualTo(332f).Within(.01f), "the key is 332 high");
            Assert.That(key.childCount, Is.EqualTo(21), "twelve rows and nine cells");
            Assert.That(new[] { RowY(ChildAt(key, 0)), RowY(ChildAt(key, 1)), RowY(ChildAt(key, 2)) }, Is.EqualTo(new[] { 0f, 19f, 38f }).Within(.01f), "the first three rows");
            var heading = ChildAt(key, 3);
            Assert.That(RowY(heading), Is.EqualTo(57f).Within(.01f), "the heading's row");
            Assert.That(TextOf(heading), Is.EqualTo("Camp buff, each kill"), "the heading's words");
            Assert.That(heading != null && heading.Find("Icon") == null, Is.True, "the heading has no icon");
            Assert.That(TextRectOf(heading) != null ? TextRectOf(heading).offsetMin.x : -1f, Is.EqualTo(8f).Within(.01f), "the heading's text starts at 8");
            for (int k = 0; k < 9; k++) Assert.That(ChildAt(key, 4 + k) != null ? ChildAt(key, 4 + k).name : null, Is.EqualTo("KindCell" + k), "the cells follow in kind order (" + k + ")");
            var elite = ChildAt(key, 13);
            Assert.That(RowY(elite), Is.EqualTo(171f).Within(.01f), "the elite row");
            Assert.That(TextOf(elite), Is.EqualTo("Elite camp: augment chest"), "the elite row's words");
            Assert.That(elite != null && elite.Find("Icon") != null, Is.True, "the elite row keeps its diamond");
            Assert.That(TextRectOf(elite) != null ? TextRectOf(elite).offsetMin.x : -1f, Is.EqualTo(34f).Within(.01f), "the elite text starts at 34");
            var last = ChildAt(key, 20);
            Assert.That(TextOf(last), Is.EqualTo("Walk target (right-click)"), "the last row");
            Assert.That(RowY(last), Is.EqualTo(304f).Within(.01f), "the last row's place");
            var view = (RectTransform)Part("TacticalInformation/ContactsPage/InViewHeading");
            Assert.That(view.anchoredPosition.y, Is.EqualTo(-366f).Within(.01f), "IN VIEW's heading");
        }
    }
}
