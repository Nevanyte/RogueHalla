using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using Roguehalla.Net;
using Roguehalla.Net.Protocol;
using Roguehalla.Net.Ui;
using TMPro;
using UnityEngine;

namespace Roguehalla.Client.Tests
{
    /// <summary>
    /// P-UI-TABMAP-TEXT-2-fix: the Tab map key's "MAP KEY" heading hides with the key (so "IN VIEW" stands alone when the key is hidden), no two
    /// labels of the left column overprint, and the key's fixed camp cells share one text size (the smallest of the sizes their own fits give),
    /// decided again when a kind is learned, the width changes, the key is shown or the text size changes. The rig is the real order of
    /// <see cref="CampKeyEditModeTests"/> (built closed, rows delivered closed, three frames, then opened). A label never active in the hierarchy
    /// measures a tenth of its size, so every width assertion first asserts the label is active. Label rectangles are not used for overprint:
    /// the heading labels' rectangles are stretched and grown (HudUi.Label), so the rendered text bounds are compared. The packet
    /// (docs/backlog/packets/P-UI-TABMAP-TEXT-2-fix.md) has the rules.
    /// </summary>
    public sealed class CampKeyOneSizeEditModeTests
    {
        Vector2 Window = new Vector2(1280, 800);
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

        // ---- fixtures (copied from CampKeyEditModeTests) -----------------------------------------------------------------------------

        static FieldInfo ScaleField() => typeof(ShowcaseSettings).GetField("_textScale", BindingFlags.Instance | BindingFlags.NonPublic);

        /// <summary>The text size setting for the next build (the labels BuildKey builds and TextScale() read it): set before BuildController.</summary>
        void UseTextScale(float scale)
        {
            if (_settings == null)
            {
                _settings = ShowcaseSettings.Current;
                if (_settings == null)
                {
                    _settingsObject = new GameObject("key size text scale settings");
                    _settings = _settingsObject.AddComponent<ShowcaseSettings>();
                    typeof(ShowcaseSettings).GetMethod("Awake", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(_settings, null);
                }
                _priorScale = (float)ScaleField().GetValue(_settings);
            }
            ScaleField().SetValue(_settings, scale);
        }

        /// <summary>The text size changes the way the product does it: the setting, then every label of the canvas is applied (MatchHud.ApplyTextScale), then frames.</summary>
        void ChangeTextScale(float scale, int frames = 3)
        {
            ScaleField().SetValue(_settings, scale);
            MythicUi.ApplyTextScale(_canvas.transform, scale);
            Frames(frames);
        }

        static Vector2 StageUnits(Vector2 window)
        {
            float scale = Mathf.Clamp(Mathf.Min(window.x / 1280f, window.y / 800f), .8f, 1.5f);
            return new Vector2(window.x / scale, window.y / scale);
        }

        void BuildController(bool open, MapCamp[] camps)
        {
            _mapCamps = camps;
            _canvas = new GameObject("key size canvas", typeof(RectTransform), typeof(Canvas), typeof(UnityEngine.UI.GraphicRaycaster));
            _stageObject = new GameObject("key size stage", typeof(RectTransform));
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
            _clientObject = new GameObject("key size client");
            _client = _clientObject.AddComponent<MatchClient>(); _client.enabled = false; _client.Url = "ws://localhost/key-size"; _client.Profile = "fixture";
            _controllerObject = new GameObject("key size controller");
            _controller = _controllerObject.AddComponent<TacticalMapController>();
            if (_prefs == null) { _prefs = new UiDisplayPreferences(); _priorToggle = _prefs.MapToggle; }
            _controller.Initialize(_client, _canvas.transform, _prefs, _stage);
            _controller.SetToggleMode(false);
            _controller.Bind(_session);
            if (open) _controller.SetOpen(true);
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

        TacticalMapMarks Marks() => Part("MapFrame/PublicMap/Delivered contacts and personal route").GetComponent<TacticalMapMarks>();

        static WireCamp Row(byte stat, ushort amount, bool elite = false)
        {
            return new WireCamp { MobType = 0, Elite = (byte)(elite ? 1 : 0), Alive = 3, State = 0, ClearedAt = 0, RespawnAt = 0, Ward = null,
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

        static WireCamp[] FourRows() => new[] { Row(2, 100), Row(6, 12500, true), Row(6, 12500), Row(0, 10000) };

        static MapCamp[] NineCamps()
        {
            var list = new MapCamp[9];
            for (int k = 0; k < 9; k++) list[k] = new MapCamp((ushort)(30 + k % 3 * 66), (ushort)(30 + k / 3 * 66), 8, 1);
            return list;
        }

        /// <summary>Nine camps and a tenth, so one kind can have two camps.</summary>
        static MapCamp[] TenCamps()
        {
            var list = new MapCamp[10];
            var nine = NineCamps();
            for (int k = 0; k < 9; k++) list[k] = nine[k];
            list[9] = new MapCamp(184, 40, 8, 1);
            return list;
        }

        /// <summary>One camp per kind with the shipped quanta, the one of kind 8 elite.</summary>
        static WireCamp[] NineRows()
        {
            var rows = new WireCamp[9];
            for (int k = 0; k < 9; k++) rows[k] = Row((byte)k, Quanta[k], k == 8);
            return rows;
        }

        // ---- the key -----------------------------------------------------------------------------------------------------------------

        RectTransform KeyRoot() => Part("TacticalInformation/ContactsPage/Key");
        RectTransform ContactsPage() => Part("TacticalInformation/ContactsPage");

        RectTransform CellRect(int kind) => KeyRoot().Find("KindCell" + kind) as RectTransform;

        bool CellActive(int kind) { var c = CellRect(kind); return c != null && c.gameObject.activeInHierarchy; }

        TMP_Text CellLabel(int kind)
        {
            var c = CellRect(kind); var t = c != null ? c.Find("Text") : null;
            return t != null ? t.GetComponent<TMP_Text>() : null;
        }

        /// <summary>The visible cells whose kind has a fixed amount (a cell of a kind not seen is not active).</summary>
        List<int> FixedCells()
        {
            var list = new List<int>();
            for (int k = 0; k < 9; k++) if (CellActive(k) && Marks().CampKey.IsFixed(k)) list.Add(k);
            return list;
        }

        /// <summary>The same measure as MythicTextScale.Fits (private there): the text at this size needs at most .88 of the box width and the box height.</summary>
        static bool Fits(TMP_Text label, float size)
        {
            var box = label.rectTransform.rect;
            bool wraps = label.textWrappingMode != TextWrappingModes.NoWrap;
            float keep = label.fontSize; bool auto = label.enableAutoSizing;
            label.enableAutoSizing = false; label.fontSize = size;
            var need = label.GetPreferredValues(label.text, wraps ? box.width : 0f, 0f);
            label.fontSize = keep; label.enableAutoSizing = auto;
            return need.x <= box.width * MythicTextScale.WidthShare - .005f && need.y <= box.height + .01f;
        }

        /// <summary>Asserts every fixed visible cell has one font size and returns it (-1 when there is no fixed cell).</summary>
        float AssertOneSize(string step)
        {
            var cells = FixedCells();
            Assert.That(cells.Count, Is.GreaterThan(0), step + ": premise: a fixed cell is visible");
            if (cells.Count == 0) return -1f;
            float first = CellLabel(cells[0]).fontSize;
            foreach (int k in cells)
                Assert.That(CellLabel(k).fontSize, Is.EqualTo(first).Within(.001f), step + ": kind " + k + " has the shared size");
            return first;
        }

        float[] CellSizes()
        {
            var sizes = new float[9];
            for (int k = 0; k < 9; k++) sizes[k] = CellActive(k) ? CellLabel(k).fontSize : -1f;
            return sizes;
        }

        // ---- the left column's labels --------------------------------------------------------------------------------------------------

        /// <summary>The rendered text of a label in world space (its text bounds); the label's rectangle is not used (the heading labels' rectangles are stretched).</summary>
        static Rect TextWorldRect(TMP_Text label)
        {
            label.ForceMeshUpdate();
            var b = label.textBounds;
            var t = label.transform;
            var a = t.TransformPoint(b.min); var c = t.TransformPoint(b.max);
            return Rect.MinMaxRect(Mathf.Min(a.x, c.x), Mathf.Min(a.y, c.y), Mathf.Max(a.x, c.x), Mathf.Max(a.y, c.y));
        }

        List<TMP_Text> LeftColumnLabels()
        {
            var list = new List<TMP_Text>();
            var page = ContactsPage();
            for (int i = 0; i < page.childCount; i++)
            {
                var label = page.GetChild(i).GetComponent<TMP_Text>();
                if (label != null && label.gameObject.activeInHierarchy && !string.IsNullOrEmpty(label.text)) list.Add(label);
            }
            foreach (var label in KeyRoot().GetComponentsInChildren<TMP_Text>(false))
                if (label.gameObject.activeInHierarchy && !string.IsNullOrEmpty(label.text)) list.Add(label);
            return list;
        }

        static string Named(TMP_Text label) => label.transform.parent.name + "/" + label.name + " \"" + label.text + "\"";

        /// <summary>
        /// The worst overlap (in HUD units, the smaller of the two axes) of every pair of labels of the left column, with the two names. A pair
        /// with a label under the key counts only when <paramref name="keyPairs"/> is set: at text size 1.5 the key's own rows overprint by about 5
        /// units on the base ("Reward chest" over "Route stop") and the MAP KEY heading touches the first row by about 3.7 (measured by the oracle
        /// author on the base, 1000x600 and 1920x1080), which this packet does not cause and cannot fix inside its scope.
        /// </summary>
        string WorstOverprint(bool keyPairs, out float worst)
        {
            var key = KeyRoot();
            var labels = LeftColumnLabels();
            var rects = new Rect[labels.Count];
            float unit = Panel.lossyScale.x;
            for (int i = 0; i < labels.Count; i++) rects[i] = TextWorldRect(labels[i]);
            worst = 0f; string who = "";
            for (int i = 0; i < labels.Count; i++)
                for (int j = i + 1; j < labels.Count; j++)
                {
                    if (!keyPairs && (labels[i].transform.IsChildOf(key) || labels[j].transform.IsChildOf(key))) continue;
                    float ox = (Mathf.Min(rects[i].xMax, rects[j].xMax) - Mathf.Max(rects[i].xMin, rects[j].xMin)) / unit;
                    float oy = (Mathf.Min(rects[i].yMax, rects[j].yMax) - Mathf.Max(rects[i].yMin, rects[j].yMin)) / unit;
                    float o = Mathf.Min(ox, oy);
                    if (ox > .5f && oy > .5f && o > worst) { worst = o; who = Named(labels[i]) + " and " + Named(labels[j]); }
                }
            return who;
        }

        // ---- 1. the heading follows the key --------------------------------------------------------------------------------------------

        [Test]
        public void TheKeyHeadingHidesWithTheKey()
        {
            foreach (var window in new[] { new Vector2(1000, 480), new Vector2(1000, 560) })
            {
                Window = window;
                RealUntilOpen(FourCamps(), FourRows());
                Open();
                string step = window.x + "x" + window.y;
                Assert.That(KeyRoot().gameObject.activeInHierarchy, Is.False, step + ": premise: the key is hidden");
                Assert.That(Part("TacticalInformation/ContactsPage/KeyHeading").gameObject.activeInHierarchy, Is.False, step + ": the MAP KEY heading is hidden with the key");
                Assert.That(Part("TacticalInformation/ContactsPage/InViewHeading").gameObject.activeInHierarchy, Is.True, step + ": IN VIEW stays");
                Reset();
            }

            foreach (var window in new[] { new Vector2(1000, 600), new Vector2(1920, 1080) })
            {
                Window = window;
                RealUntilOpen(FourCamps(), FourRows());
                Open();
                string step = window.x + "x" + window.y;
                Assert.That(KeyRoot().gameObject.activeInHierarchy, Is.True, step + ": premise: the key is shown");
                var heading = Part("TacticalInformation/ContactsPage/KeyHeading");
                Assert.That(heading.gameObject.activeInHierarchy, Is.True, step + ": the MAP KEY heading is shown with the key");
                var text = heading.GetComponent<TMP_Text>();
                var keyCorners = new Vector3[4];
                KeyRoot().GetWorldCorners(keyCorners);
                float unit = Panel.lossyScale.x;
                Assert.That(text.text, Is.EqualTo("MAP KEY"), step + ": the heading's words");
                Assert.That((TextWorldRect(text).yMin - keyCorners[1].y) / unit, Is.GreaterThanOrEqualTo(-.01f), step + ": the heading's text lies above the key's first row");
                Reset();
            }
        }

        [Test]
        public void TheHeadingFollowsAResizeFromCompactToFullAndBack()
        {
            // the layout pass runs again on a resize: the switch lives where the key is switched, not in the build
            Window = new Vector2(1000, 560);
            RealUntilOpen(FourCamps(), FourRows());
            Open();
            Assert.That(KeyRoot().gameObject.activeInHierarchy, Is.False, "premise: compact: the key is hidden");
            Assert.That(Part("TacticalInformation/ContactsPage/KeyHeading").gameObject.activeInHierarchy, Is.False, "compact: the heading is hidden");
            _stage.sizeDelta = new Vector2(1422f, 800f);
            Frames(3);
            Assert.That(KeyRoot().gameObject.activeInHierarchy, Is.True, "premise: full: the key is shown");
            Assert.That(Part("TacticalInformation/ContactsPage/KeyHeading").gameObject.activeInHierarchy, Is.True, "full: the heading is shown");
            _stage.sizeDelta = StageUnits(new Vector2(1000, 560));
            Frames(3);
            Assert.That(KeyRoot().gameObject.activeInHierarchy, Is.False, "premise: compact again: the key is hidden");
            Assert.That(Part("TacticalInformation/ContactsPage/KeyHeading").gameObject.activeInHierarchy, Is.False, "compact again: the heading is hidden");
        }

        // ---- 2. no two labels of the left column overprint -------------------------------------------------------------------------------

        [Test]
        public void NoTwoLabelsOfTheLeftColumnOverprint()
        {
            foreach (var window in new[] { new Vector2(1000, 480), new Vector2(1000, 560), new Vector2(1000, 600), new Vector2(1920, 1080) })
                foreach (float ts in new[] { 1f, 1.5f })
                {
                    Window = window; UseTextScale(ts);
                    RealUntilOpen(FourCamps(), FourRows());
                    Open();
                    string step = window.x + "x" + window.y + " text " + ts;
                    // a shown key gives the column its heading, twelve rows and IN VIEW; a hidden key (rule 1) leaves IN VIEW alone
                    int expected = KeyRoot().gameObject.activeInHierarchy ? 2 : 1;
                    Assert.That(LeftColumnLabels().Count, Is.GreaterThanOrEqualTo(expected), step + ": premise: the left column has labels");
                    float worst;
                    string who = WorstOverprint(ts == 1f, out worst);
                    Assert.That(worst, Is.EqualTo(0f), step + ": no two labels overprint (worst: " + who + " by " + worst + " units)");
                    Reset();
                }
        }

        // ---- 3. the fixed cells share one size -------------------------------------------------------------------------------------------

        static readonly Vector2[] Canvases = { new Vector2(1920, 1080), new Vector2(1280, 800), new Vector2(980, 2121) };

        [Test]
        public void TheFixedCellsShareOneTextSize()
        {
            for (int c = 0; c < Canvases.Length; c++)
                foreach (float ts in new[] { 1f, 1.5f })
                {
                    Window = Canvases[c]; UseTextScale(ts);
                    RealUntilOpen(NineCamps(), NineRows());
                    Open();
                    string step = Canvases[c].x + "x" + Canvases[c].y + " text " + ts;
                    Assert.That(KeyRoot().gameObject.activeInHierarchy, Is.True, step + ": premise: the key is shown");
                    Assert.That(FixedCells().Count, Is.EqualTo(9), step + ": premise: nine fixed cells are visible");
                    float shared = AssertOneSize(step);
                    float floor = ts == 1f ? 9f : 10.5f;
                    Assert.That(shared, Is.GreaterThanOrEqualTo(floor), step + ": the shared size is at least " + floor);
                    bool someTooBig = false;
                    foreach (int k in FixedCells())
                    {
                        var label = CellLabel(k);
                        Assert.That(label.gameObject.activeInHierarchy, Is.True, step + ": kind " + k + " label is active in the hierarchy");
                        Assert.That(Fits(label, shared), Is.True, step + ": kind " + k + " fits its box at the shared size " + shared);
                        if (!Fits(label, shared + .3f)) someTooBig = true;
                    }
                    Assert.That(someTooBig, Is.True, step + ": at the shared size plus .3 some cell no longer fits (the size is not smaller than needed)");
                    Reset();
                }
        }

        // ---- 4. a text size change keeps one size ----------------------------------------------------------------------------------------

        [Test]
        public void ATextSizeChangeKeepsOneSize()
        {
            Window = new Vector2(1280, 800); UseTextScale(1f);
            RealUntilOpen(NineCamps(), NineRows());
            Open();
            float one = AssertOneSize("text 1");
            ChangeTextScale(1.5f);
            float big = AssertOneSize("text 1.5 with the map open");
            Assert.That(big, Is.GreaterThan(one + .01f), "the shared size grows with the text size");
            foreach (int k in FixedCells()) Assert.That(Fits(CellLabel(k), big), Is.True, "text 1.5 open: kind " + k + " fits its box");

            // the same change while the map is closed, then open
            Close();
            ChangeTextScale(1f);
            Open();
            float back = AssertOneSize("text 1 changed while closed, then opened");
            Assert.That(back, Is.EqualTo(one).Within(.01f), "the shared size is the one of text 1 again");
            Close();
            ChangeTextScale(1.5f);
            Open();
            float again = AssertOneSize("text 1.5 changed while closed, then opened");
            Assert.That(again, Is.EqualTo(big).Within(.01f), "the shared size is the one of text 1.5 again");
        }

        // ---- 5. a late kind takes and gives the shared size ------------------------------------------------------------------------------

        [Test]
        public void ALateKindTakesAndGivesTheSharedSize()
        {
            Window = new Vector2(1280, 800); UseTextScale(1f);
            var eight = new WireCamp[9];
            for (int k = 0; k < 8; k++) eight[k] = Row((byte)k, Quanta[k]);
            eight[8] = Row(0, Quanta[0]);   // a ninth camp of a kind already seen: the map has nine camps, so the rows count
            RealUntilOpen(NineCamps(), eight);
            Open();
            Assert.That(FixedCells().Count, Is.EqualTo(8), "premise: eight cells are visible, none of Move speed");
            Assert.That(CellActive(8), Is.False, "premise: the Move speed cell is hidden");
            float before = AssertOneSize("eight kinds");

            var nine = new WireCamp[9];
            for (int k = 0; k < 8; k++) nine[k] = Row((byte)k, Quanta[k]);
            nine[8] = Row(8, Quanta[8]);
            DeliverRows(nine); Frames(3);
            Assert.That(FixedCells().Count, Is.EqualTo(9), "premise: the ninth kind is learned");
            float after = AssertOneSize("nine kinds");
            Assert.That(after, Is.LessThanOrEqualTo(before + .001f), "the late cell never raises the shared size");
            foreach (int k in FixedCells()) Assert.That(Fits(CellLabel(k), after), Is.True, "kind " + k + " fits its box at the shared size");
        }

        // ---- 6. a varying kind does not move the others ---------------------------------------------------------------------------------

        [Test]
        public void AVaryingKindDoesNotMoveTheOthers()
        {
            foreach (float ts in new[] { 1f, 1.5f })
            {
                Window = new Vector2(1280, 800); UseTextScale(ts);
                var rows = new WireCamp[10];
                var nine = NineRows();
                for (int k = 0; k < 9; k++) rows[k] = nine[k];
                rows[2] = Row(2, 100); rows[9] = Row(2, 50);   // Damage: two camps, two amounts
                RealUntilOpen(TenCamps(), rows);
                Open();
                string step = "text " + ts;
                Assert.That(Marks().CampKey.IsFixed(2), Is.False, step + ": premise: Damage varies");
                Assert.That(CellActive(2), Is.True, step + ": premise: the varying cell is visible");
                var cells = FixedCells();
                Assert.That(cells.Count, Is.EqualTo(8), step + ": premise: eight fixed cells");
                float shared = AssertOneSize(step);
                bool someTooBig = false;
                foreach (int k in cells)
                {
                    var label = CellLabel(k);
                    Assert.That(label.gameObject.activeInHierarchy, Is.True, step + ": kind " + k + " label is active in the hierarchy");
                    Assert.That(Fits(label, shared), Is.True, step + ": kind " + k + " fits its box at the shared size");
                    if (!Fits(label, shared + .3f)) someTooBig = true;
                }
                Assert.That(someTooBig, Is.True, step + ": at the shared size plus .3 one of the eight no longer fits (the varying cell did not pull the size down)");
                var varying = CellLabel(2);
                Assert.That(varying.text, Is.EqualTo("Damage · varies"), step + ": the varying cell's words");
                Assert.That(varying.fontSize, Is.LessThanOrEqualTo(shared + .001f), step + ": the varying cell is not larger than the shared size");
                Assert.That(varying.fontSize, Is.GreaterThanOrEqualTo(6f), step + ": the varying cell is at least 6");
                Assert.That(Fits(varying, varying.fontSize), Is.True, step + ": the varying cell fits its box at its own size");
                Reset();
            }
        }

        // ---- 7. a steady frame writes and allocates nothing ------------------------------------------------------------------------------

        [Test]
        public void ASteadyFrameWritesNothing()
        {
            Window = new Vector2(1280, 800); UseTextScale(1f);
            RealUntilOpen(NineCamps(), NineRows());
            Open();
            AssertOneSize("settled");
            var first = CellSizes();
            Frames(10);
            Assert.That(CellSizes(), Is.EqualTo(first), "ten more frames change no cell's size");
            var method = typeof(TacticalMapController).GetMethod("ApplyCampKey", BindingFlags.Instance | BindingFlags.NonPublic, null, Type.EmptyTypes, null);
            Assert.That(method, Is.Not.Null, "the controller has an instance method ApplyCampKey()");
            if (method != null)
            {
                var apply = (Action)Delegate.CreateDelegate(typeof(Action), _controller, method);
                AllocationMeter.AssertNone(apply, 200, "a steady ApplyCampKey");
            }
            Assert.That(CellSizes(), Is.EqualTo(first), "the steady calls change no cell's size");
        }
    }
}
