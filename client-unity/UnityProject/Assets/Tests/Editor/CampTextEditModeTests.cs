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
    /// P-UI-TABMAP-TEXT-1: on the Tab map a camp's amount and "Aug" text is one block placed Right, Below, Above or Left of its camp, inside
    /// the layer and on no plate or other fixed mark, and it never hops: a rival, a clock, a ward, a giver, a chest, a closed and opened
    /// map or a replaced terrain map move no text. The fixture is the real order of <see cref="ZoneNamesRealOrderEditModeTests"/> (the map
    /// and the camps are delivered while the panel is closed, windows are built hidden, the first refresh runs before anything opens),
    /// with the tests' own deliveries. A text label never active in the hierarchy measures a tenth of its size, so every width assertion
    /// first asserts the label is active. Every mark rect is built here from the layer rect. The packet
    /// (docs/backlog/packets/P-UI-TABMAP-TEXT-1.md) has the rules and the numbers.
    /// </summary>
    public sealed class CampTextEditModeTests
    {
        static readonly Vector2 Window = new Vector2(1280, 720);
        const int Tiles = 6;
        const float WorldSide = Tiles * 32f;
        const int Haunted = MapZones.Sentenced, Holy = MapZones.Holy;

        /// <summary>A camp placed beside a zone's icon (the picture of ZoneNamesEditModeTests): its zone, its layer offset from that icon (right, down) at k = 1, its reward.</summary>
        struct PicCamp { public int Region; public float Right, Down; public byte Stat; public ushort Amount; }

        static readonly PicCamp[] PictureCamps =
        {
            new PicCamp { Region = Haunted, Right = 16, Down = 28, Stat = 0, Amount = 10000 },
            new PicCamp { Region = Haunted, Right = -54, Down = 11, Stat = 2, Amount = 100 },
            new PicCamp { Region = Holy, Right = 11, Down = 19, Stat = 0, Amount = 15000 },
            new PicCamp { Region = Holy, Right = -34, Down = 20, Stat = 2, Amount = 100 },
        };

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
        WireSelf _wireSelf;

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
                _controller = null; _session = null; _controllerObject = _clientObject = _stageObject = _canvas = null;
            }
        }

        // ---- fixtures ----------------------------------------------------------------------------------------------------------------

        static Vector2 StageUnits(Vector2 window)
        {
            float scale = Mathf.Clamp(Mathf.Min(window.x / 1280f, window.y / 800f), .8f, 1.5f);
            return new Vector2(window.x / scale, window.y / scale);
        }

        /// <summary>The fixture of the real order: the map is opened here only when <paramref name="open"/>, and nothing ticks. <paramref name="wallAtZero"/> sets tile 0 to a wall.</summary>
        void BuildController(bool open, MapCamp[] camps, bool wallAtZero = false)
        {
            _mapCamps = camps ?? new[] { new MapCamp(96, 96, 8, 1), new MapCamp(32, 32, 8, 3) };
            _canvas = new GameObject("camp text canvas", typeof(RectTransform), typeof(Canvas), typeof(UnityEngine.UI.GraphicRaycaster));
            _stageObject = new GameObject("camp text stage", typeof(RectTransform));
            _stage = (RectTransform)_stageObject.transform; _stage.SetParent(_canvas.transform, false);
            _stage.anchorMin = _stage.anchorMax = _stage.pivot = new Vector2(.5f, .5f); _stage.sizeDelta = StageUnits(Window);
            float s = HudLayout.Viewport(Window.x, Window.y).Scale;
            _stage.localScale = new Vector3(s, s, 1f);
            _regions = new byte[Tiles * Tiles];
            for (int i = 0; i < _regions.Length; i++) _regions[i] = (byte)(i % 6 + 1);
            var tiles = new byte[Tiles * Tiles];
            if (wallAtZero) tiles[0] = MapBlob.Wall;
            var map = new MapBlob(Tiles, Tiles, tiles, null, _regions).WithPublic(_mapCamps, null, null, null, null);
            _wireSelf = new WireSelf { Id = 1, Faction = 4, X = 48, Y = 48, Hp = 100, MaxHp = 100, Status = 1, Weapon = (byte)WeaponId.Wand };
            _transport = new Fix3Transport();
            _session = Fix3Transport.Join(_transport, map, _wireSelf);
            _clientObject = new GameObject("camp text client");
            _client = _clientObject.AddComponent<MatchClient>(); _client.enabled = false; _client.Url = "ws://localhost/camp-text"; _client.Profile = "fixture";
            _controllerObject = new GameObject("camp text controller");
            _controller = _controllerObject.AddComponent<TacticalMapController>();
            if (_prefs == null) { _prefs = new UiDisplayPreferences(); _priorToggle = _prefs.MapToggle; }
            _controller.Initialize(_client, _canvas.transform, _prefs, _stage);
            _controller.SetToggleMode(false);
            _controller.Bind(_session);
            if (open) _controller.SetOpen(true);
        }

        /// <summary>The planning branch: the plan is delivered (only the planning order calls this).</summary>
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

        void Rebind()
        {
            var old = _session;
            var map2 = new MapBlob(Tiles, Tiles, new byte[Tiles * Tiles], null, _regions);
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

        /// <summary>The camps' rows, index-aligned to the map's camps.</summary>
        void DeliverRows(params WireCamp[] rows)
        {
            _transport.Deliver(new ServerMsg.State { Tick = _tick++, Items = new StateItem[] { new CampsState { Rows = rows } } });
        }

        /// <summary>The snapshot of a frame: the own seat and the given actors (rivals).</summary>
        void Snap(params WireActor[] actors)
        {
            _transport.Deliver(new ServerMsg.Snapshot { Tick = _tick++, ServerTimeOverrideMs = 50, Phase = 1, Me = _wireSelf, Actors = actors });
        }

        /// <summary>The whole quest view in ONE quests state (a quests state replaces the whole view): the givers' rows and the drops. Null drops: none.</summary>
        void DeliverQuests(WireQuestDrop[] drops, params Vector2[] givers)
        {
            var rows = new WireQuestRow[givers.Length];
            for (int i = 0; i < rows.Length; i++)
                rows[i] = new WireQuestRow { Handle = new QuestHandle((uint)(71 + i)), Def = "fixture", GiverX = givers[i].x, GiverY = givers[i].y,
                    State = QuestRowState.Preview, ExpiresAt = 10000, CanAccept = true, Name = "giver " + i, GiverLabel = "giver " + i,
                    Brief = "native brief", Objective = "native objective", Reward = "native reward" };
            var view = new WireQuestView { Rows = rows };
            if (drops != null) view.Drops = drops;
            _transport.Deliver(new ServerMsg.State { Tick = _tick++, Items = new StateItem[] { new QuestsState { Available = true, View = view } } });
        }

        void DeliverChest(Vector2 world)
        {
            _transport.Deliver(new ServerMsg.State { Tick = _tick++, Items = new StateItem[] { new RewardChestsState { Rows = new[] {
                new WireRewardChest { Handle = new RewardChestHandle(5), X = (ushort)Mathf.RoundToInt(world.x), Y = (ushort)Mathf.RoundToInt(world.y), ExpiresAt = 12300,
                    CanChoose = true, Options = new WireRewardOption[0] } } } } });
        }

        void DeliverNoChest()
        {
            _transport.Deliver(new ServerMsg.State { Tick = _tick++, Items = new StateItem[] { new RewardChestsState { Rows = new WireRewardChest[0] } } });
        }

        // ---- the orders --------------------------------------------------------------------------------------------------------------

        /// <summary>The open order: built open, one frame, the rows, two frames, scale 1.</summary>
        void OpenOrder(MapCamp[] camps, params WireCamp[] rows)
        {
            BuildController(true, camps); Frame();
            DeliverRows(rows); Frames(2);
            Large().SetMarkScale(1f);
        }

        /// <summary>The real order: built closed, one frame, the rows delivered closed, three frames closed (nothing is placed), then opened.</summary>
        void RealUntilOpen(MapCamp[] camps, params WireCamp[] rows)
        {
            BuildController(false, camps); Frame();
            DeliverRows(rows); Frames(3);
        }

        void Open(int frames = 3) { _controller.SetOpen(true); Frames(frames); }

        void Close() { _controller.SetOpen(false); Frame(); }

        /// <summary>The picture of ZoneNamesEditModeTests: four camps beside two zone icons, found as ZoneNamesRealOrderEditModeTests.FindCamps finds its own.</summary>
        MapCamp[] FindPictureCamps()
        {
            BuildController(true, null); Frame();
            _layer1 = LayerRect().width * Large().MarkScale;
            Reset();
            var list = new MapCamp[PictureCamps.Length];
            for (int i = 0; i < list.Length; i++)
            {
                var c = PictureCamps[i]; var w = Icon1(c.Region) + new Vector2(c.Right, c.Down) * (WorldSide / _layer1);
                list[i] = new MapCamp((ushort)Mathf.Clamp(Mathf.RoundToInt(w.x), 4, 188), (ushort)Mathf.Clamp(Mathf.RoundToInt(w.y), 4, 188), 8, (byte)c.Region);
            }
            return list;
        }

        static Vector2 Icon1(int region) => new Vector2((region - .5f) * 32f, 80f);

        static WireCamp[] PictureRows()
        {
            var rows = new WireCamp[PictureCamps.Length];
            for (int i = 0; i < rows.Length; i++) rows[i] = Row(PictureCamps[i].Stat, PictureCamps[i].Amount);
            return rows;
        }

        MapCamp[] PictureTexts(out WireCamp[] rows)
        {
            var camps = FindPictureCamps();
            rows = PictureRows();
            return camps;
        }

        MapCamp[] Camps(params Vector2[] world)
        {
            var list = new MapCamp[world.Length];
            for (int i = 0; i < list.Length; i++) list[i] = new MapCamp((ushort)world[i].x, (ushort)world[i].y, 8, 1);
            return list;
        }

        // ---- geometry in layer units (the marks object's space, y up) ----------------------------------------------------------------

        Rect LayerRect() => Marks().rectTransform.rect;

        Vector2 LayerOf(Vector2 world)
        {
            var r = LayerRect();
            return new Vector2(r.xMin + world.x / WorldSide * r.width, r.yMax - world.y / WorldSide * r.height);
        }

        Vector2 WorldOf(Vector2 layer)
        {
            var r = LayerRect();
            return new Vector2((layer.x - r.xMin) / r.width * WorldSide, (r.yMax - layer.y) / r.height * WorldSide);
        }

        static MapRect Of(Rect r) => new MapRect(r.x, r.y, r.width, r.height);
        static MapRect Square(Vector2 centre, float side) => new MapRect(centre.x - side / 2f, centre.y - side / 2f, side, side);

        /// <summary>The extents of the overlap are both above .001: touching is free.</summary>
        static bool Meets(MapRect a, MapRect b)
        {
            return Math.Min(a.Right, b.Right) - Math.Max(a.X, b.X) > .001f && Math.Min(a.Bottom, b.Bottom) - Math.Max(a.Y, b.Y) > .001f;
        }

        /// <summary>The block of a camp row; asserts it exists (the zero rect when it does not, so the lines after it still run).</summary>
        Rect Block(int i)
        {
            var r = Marks().CampTextRect(i);
            Assert.That(r.HasValue, Is.True, "camp " + i + " has a text block");
            return r ?? default(Rect);
        }

        void AssertPlaced(int campsWithText, string step)
        {
            Assert.That(_controller.IsOpen, Is.True, step + ": the map is open");
            Assert.That(Marks().CampTextPlacements, Is.GreaterThan(0), step + ": a camp-text pass has run");
            for (int i = 0; i < campsWithText; i++) Assert.That(Marks().CampTextRect(i).HasValue, Is.True, step + ": camp " + i + " has a text block");
        }

        void AssertInsideLayer(Rect r, string what)
        {
            var layer = LayerRect();
            Assert.That(r.xMin, Is.GreaterThanOrEqualTo(layer.xMin + 1.99f), what + ": left edge inside the layer");
            Assert.That(r.yMin, Is.GreaterThanOrEqualTo(layer.yMin + 1.99f), what + ": bottom edge inside the layer");
            Assert.That(r.xMax, Is.LessThanOrEqualTo(layer.xMax - 1.99f), what + ": right edge inside the layer");
            Assert.That(r.yMax, Is.LessThanOrEqualTo(layer.yMax - 1.99f), what + ": top edge inside the layer");
        }

        // ---- the drawn labels ---------------------------------------------------------------------------------------------------------

        static Rect LabelRect(TMP_Text label)
        {
            var at = label.rectTransform.anchoredPosition; var pivot = label.rectTransform.pivot;
            float w = label.preferredWidth, h = label.preferredHeight;
            return new Rect(at.x - pivot.x * w, at.y - .5f * h, w, h);
        }

        IEnumerable<TMP_Text> ActiveBadgeLabels()
        {
            foreach (var label in Marks().transform.GetComponentsInChildren<TMP_Text>(true))
                if (label.name.StartsWith("CampBadgeText") && label.gameObject.activeInHierarchy) yield return label;
        }

        static bool InsideBlock(Rect label, Rect block)
        {
            return label.xMin >= block.xMin - .01f && label.yMin >= block.yMin - .01f && label.xMax <= block.xMax + .01f && label.yMax <= block.yMax + .01f;
        }

        /// <summary>The active label with that text whose rect lies inside the block (two "Aug" are told apart by their block); null: none.</summary>
        TMP_Text LabelIn(string text, Rect block)
        {
            foreach (var label in ActiveBadgeLabels())
                if (label.text == text && InsideBlock(LabelRect(label), block)) return label;
            return null;
        }

        /// <summary>The label showing a text, wherever it stands; null: none is active.</summary>
        TMP_Text LabelWith(string text)
        {
            foreach (var label in ActiveBadgeLabels()) if (label.text == text) return label;
            return null;
        }

        Vector2? PositionOf(string text)
        {
            var label = LabelWith(text);
            if (label == null) return null;
            return label.rectTransform.anchoredPosition;
        }

        /// <summary>The label of the text in its block: asserts it is drawn inside the block and returns its rect (the empty rect when it is not).</summary>
        Rect DrawnIn(string text, Rect block, string what)
        {
            var label = LabelIn(text, block);
            Assert.That(label, Is.Not.Null, what + ": '" + text + "' is drawn inside its block");
            return label != null ? LabelRect(label) : default(Rect);
        }

        int BadgeChildren()
        {
            int n = 0;
            foreach (var label in Marks().transform.GetComponentsInChildren<TMP_Text>(true)) if (label.name.StartsWith("CampBadgeText")) n++;
            return n;
        }

        TMP_Text MeasureLabel()
        {
            foreach (var label in Marks().transform.GetComponentsInChildren<TMP_Text>(true)) if (label.name == "CampTextMeasure") return label;
            return null;
        }

        /// <summary>The width of a text at a font size, from a probe label of the same role that is active in the hierarchy.</summary>
        float WidthAt(string text, float size)
        {
            var probe = HudUi.Label(Panel, "ProbeLabel", "", size, Color.white, TextAlignmentOptions.Center, false, UiFontRole.Display);
            try
            {
                Assert.That(probe.gameObject.activeInHierarchy, Is.True, "the probe label is active in the hierarchy");
                probe.fontSize = size;
                return probe.GetPreferredValues(text, 0f, 0f).x;
            }
            finally { UnityEngine.Object.DestroyImmediate(probe.gameObject); }
        }

        void AssertBlockWidths(string[][] linesOfCamp, float size, string step)
        {
            Assert.That(MeasureLabel(), Is.Not.Null, step + ": the measuring label exists");
            Assert.That(MeasureLabel() != null && MeasureLabel().gameObject.activeInHierarchy, Is.True, step + ": the measuring label is active in the hierarchy");
            Assert.That(MeasureLabel() != null ? MeasureLabel().fontSize : 0f, Is.EqualTo(size).Within(.001f), step + ": the reference size is " + size);
            for (int i = 0; i < linesOfCamp.Length; i++)
            {
                var block = Block(i);
                float widest = 0f;
                foreach (var line in linesOfCamp[i]) widest = Mathf.Max(widest, WidthAt(line, size));
                Assert.That(block.width, Is.EqualTo(widest).Within(.01f), step + ": camp " + i + " block is as wide as its widest line at " + size);
                AssertInsideLayer(block, step + ": camp " + i + " block");
            }
        }

        /// <summary>The state of every text: the pass counter, each block's place and rect, each text's drawn position.</summary>
        struct Frozen
        {
            public int Passes; public int[] Places; public Rect?[] Rects; public Vector2?[] Drawn; public int Zones; public int[] Spots; public Rect?[] Names;
        }

        Frozen Freeze(int camps, string[] texts, bool names = false)
        {
            var f = new Frozen { Passes = Marks().CampTextPlacements, Places = new int[camps], Rects = new Rect?[camps], Drawn = new Vector2?[texts.Length] };
            for (int i = 0; i < camps; i++) { f.Places[i] = Marks().CampTextPlace(i); f.Rects[i] = Marks().CampTextRect(i); }
            for (int i = 0; i < texts.Length; i++) f.Drawn[i] = PositionOf(texts[i]);
            if (names)
            {
                f.Zones = Marks().ZoneLabelPlacements; f.Spots = new int[MapZones.Count + 1]; f.Names = new Rect?[MapZones.Count + 1];
                for (int r = 1; r <= MapZones.Count; r++) { f.Spots[r] = Marks().ZoneLabelSpot(r); f.Names[r] = Marks().ZoneLabelRect(r); }
            }
            return f;
        }

        static void AssertSameRect(Rect? now, Rect? was, string what)
        {
            Assert.That(now.HasValue, Is.EqualTo(was.HasValue), what + ": has a value as before");
            if (!now.HasValue || !was.HasValue) return;
            Assert.That(now.Value == was.Value, Is.True, what + ": " + now.Value + " is " + was.Value);
        }

        void AssertFrozen(Frozen was, Frozen now, string[] texts, string step, string hidden = null)
        {
            Assert.That(now.Passes, Is.EqualTo(was.Passes), step + ": no pass ran");
            for (int i = 0; i < was.Places.Length; i++)
            {
                Assert.That(now.Places[i], Is.EqualTo(was.Places[i]), step + ": camp " + i + " place");
                AssertSameRect(now.Rects[i], was.Rects[i], step + ": camp " + i + " block");
            }
            for (int i = 0; i < texts.Length; i++)
            {
                if (texts[i] == hidden) continue;
                Assert.That(now.Drawn[i].HasValue, Is.EqualTo(was.Drawn[i].HasValue), step + ": '" + texts[i] + "' is drawn as before");
                if (now.Drawn[i].HasValue && was.Drawn[i].HasValue)
                    Assert.That(now.Drawn[i].Value == was.Drawn[i].Value, Is.True, step + ": '" + texts[i] + "' stands where it stood (" + now.Drawn[i].Value + " against " + was.Drawn[i].Value + ")");
            }
            if (was.Spots == null) return;
            for (int r = 1; r <= MapZones.Count; r++)
            {
                Assert.That(now.Spots[r], Is.EqualTo(was.Spots[r]), step + ": " + MapZones.Name(r) + " name spot");
                AssertSameRect(now.Names[r], was.Names[r], step + ": " + MapZones.Name(r) + " name");
            }
        }

        // ---- 1. edges ---------------------------------------------------------------------------------------------------------------

        [Test]
        public void TextsAtTheRightAndTopEdgesStayInsideTheMap()
        {
            var camps = Camps(new Vector2(184, 96), new Vector2(184, 40), new Vector2(96, 4), new Vector2(96, 100));
            OpenOrder(camps, Row(2, 25), Row(2, 50, true), Row(2, 75, true), Row(2, 100));
            var texts = new[] { new[] { "+0.75%" }, new[] { "+1.5%", "Aug" }, new[] { "+2.25%", "Aug" }, new[] { "+3%" } };
            foreach (float k in new[] { 1f, 1.4f })
            {
                if (k != 1f) Large().SetMarkScale(k);
                string step = "k " + k;
                AssertPlaced(4, step);
                for (int i = 0; i < 4; i++)
                {
                    var block = Block(i);
                    AssertInsideLayer(block, step + ": camp " + i + " block");
                    foreach (var text in texts[i])
                    {
                        var drawn = DrawnIn(text, block, step + ": camp " + i);
                        AssertInsideLayer(drawn, step + ": camp " + i + " '" + text + "'");
                    }
                }
            }
        }

        // ---- 2. neighbours ----------------------------------------------------------------------------------------------------------

        [Test]
        public void NeighbouringTextsTouchNeitherEachOtherNorEachOthersPlates()
        {
            var camps = Camps(new Vector2(80, 96), new Vector2(92, 96));
            OpenOrder(camps, Row(2, 25), Row(2, 100));
            AssertPlaced(2, "two near camps");
            var blocks = new[] { Block(0), Block(1) };
            var drawn = new[] { DrawnIn("+0.75%", blocks[0], "camp 0"), DrawnIn("+3%", blocks[1], "camp 1") };
            var plates = new[] { Square(LayerOf(new Vector2(80, 96)), 20.4f), Square(LayerOf(new Vector2(92, 96)), 20.4f) };
            Assert.That(Meets(Of(blocks[0]), Of(blocks[1])), Is.False, "the two blocks overlap nowhere");
            Assert.That(Meets(Of(drawn[0]), Of(drawn[1])), Is.False, "the two drawn texts overlap nowhere");
            for (int i = 0; i < 2; i++)
                for (int p = 0; p < 2; p++)
                {
                    Assert.That(Meets(Of(blocks[i]), plates[p]), Is.False, "block " + i + " meets no plate (" + p + ")");
                    Assert.That(Meets(Of(drawn[i]), plates[p]), Is.False, "drawn text " + i + " meets no plate (" + p + ")");
                }
        }

        // ---- 3. a rival -------------------------------------------------------------------------------------------------------------

        [Test]
        public void ARivalHidesOneTextAndMovesNoOther()
        {
            var camps = Camps(new Vector2(96, 96), new Vector2(96, 99), new Vector2(30, 150));
            OpenOrder(camps, Row(2, 25), Row(2, 50), Row(2, 100));
            AssertPlaced(3, "three camps");
            var texts = new[] { "+0.75%", "+1.5%", "+3%" };
            Assert.That(Marks().CampTextPlace(0), Is.EqualTo(CampTextPlacement.Right), "premise: A is Right");
            Assert.That(Marks().CampTextPlace(1), Is.Not.EqualTo(CampTextPlacement.Right), "premise: B is not Right");
            var atB = LayerOf(new Vector2(96, 99));
            var bRight = new MapRect(atB.x + 21.2f, atB.y + 2f - Block(1).height / 2f, Block(1).width, Block(1).height);
            Assert.That(Meets(bRight, Of(Block(0))), Is.True, "premise: B's Right place overlaps A's block");
            var before = Freeze(3, texts);
            int drawnBefore = Marks().CampMarkersDrawn;
            Assert.That(drawnBefore, Is.EqualTo(3), "premise: three camps are drawn");
            Assert.That(PositionOf("+0.75%").HasValue, Is.True, "premise: A's text is drawn");

            Snap(new WireActor { Id = 2, X = 96, Y = 94, HpPct = 100, Status = 1 });
            Frame();
            var rival = LayerOf(new Vector2(96, 94));
            Assert.That((LayerOf(new Vector2(96, 96)) - rival).magnitude, Is.LessThan(8f), "premise: the rival is under 8 layer units from A");
            Assert.That((LayerOf(new Vector2(96, 99)) - rival).magnitude, Is.GreaterThan(8f), "premise: the rival is over 8 layer units from B");
            Assert.That(Marks().ContactCount, Is.EqualTo(1), "the rival is delivered");
            Assert.That(Marks().CampMarkersDrawn, Is.EqualTo(drawnBefore - 1), "A's marker is dropped under the rival");
            Assert.That(LabelWith("+0.75%"), Is.Null, "no label shows A's text while the rival covers A");
            AssertFrozen(before, Freeze(3, texts), texts, "rival on A", "+0.75%");
            Assert.That(Marks().CampTextRect(0).HasValue, Is.True, "A's block stays while its text is not drawn");

            Snap();
            Frame();
            Assert.That(Marks().ContactCount, Is.EqualTo(0), "the rival is gone");
            Assert.That(LabelWith("+0.75%"), Is.Not.Null, "A's label is back");
            AssertFrozen(before, Freeze(3, texts), texts, "rival removed");
        }

        // ---- 4. nothing that ticks --------------------------------------------------------------------------------------------------

        [Test]
        public void NothingThatTicksReplacesAText()
        {
            var camps = Camps(new Vector2(96, 96), new Vector2(30, 40), new Vector2(150, 150));
            OpenOrder(camps, Row(2, 25), Row(2, 50, true), Row(2, 100));
            AssertPlaced(3, "three camps");
            var texts = new[] { "+0.75%", "+1.5%", "Aug", "+3%" };
            var first = Freeze(3, texts);
            for (int i = 0; i < texts.Length; i++) Assert.That(first.Drawn[i].HasValue, Is.True, "premise: '" + texts[i] + "' is drawn");

            Frames(10);
            AssertFrozen(first, Freeze(3, texts), texts, "ten frames");

            DeliverRows(Row(2, 25, false, 2, 180050), Row(2, 50, true), Row(2, 100)); Frames(2);
            var clock = Marks().transform.Find("CampClock0");
            Assert.That(clock != null && !string.IsNullOrEmpty(clock.GetComponent<TMP_Text>().text), Is.True, "premise: A's clock label has a text");
            AssertFrozen(first, Freeze(3, texts), texts, "A cleared");

            DeliverRows(Row(2, 25, false, 2, 180050), Row(2, 50, true), Row(2, 100, false, 0, 0, new WireWard { Yours = true, ExpiresAt = 90050 })); Frames(2);
            Assert.That(Marks().WardMarkCount, Is.EqualTo(1), "premise: C has a ward");
            AssertFrozen(first, Freeze(3, texts), texts, "ward on C");

            var onA = WorldOf(Block(0).center);
            DeliverQuests(new[] { new WireQuestDrop { Index = 37, Reward = 20, Charges = 1, X = onA.x, Y = onA.y } }, onA); Frames(2);
            Assert.That(Marks().QuestGiverCount, Is.EqualTo(1), "premise: the giver is on the map");
            DeliverChest(onA); Frames(2);
            Assert.That(Marks().ChestBadgeCount, Is.EqualTo(1), "premise: the chest is on the map");
            AssertFrozen(first, Freeze(3, texts), texts, "giver, quest drop and chest on A's block");

            DeliverQuests(null); DeliverNoChest(); Frames(2);
            Assert.That(Marks().QuestGiverCount, Is.EqualTo(0), "premise: the giver is gone");
            Assert.That(Marks().ChestBadgeCount, Is.EqualTo(0), "premise: the chest is gone");
            AssertFrozen(first, Freeze(3, texts), texts, "giver, quest drop and chest removed");

            int passes = Marks().CampTextPlacements;
            DeliverRows(Row(2, 25, false, 2, 180050), Row(2, 50, true), Row(2, 75, false, 0, 0, new WireWard { Yours = true, ExpiresAt = 90050 })); Frames(2);
            Assert.That(Marks().CampTextPlacements, Is.EqualTo(passes + 1), "one reward word changed: exactly one pass");
            Frames(2);
            Assert.That(Marks().CampTextPlacements, Is.EqualTo(passes + 1), "and no more while nothing changes");

            passes = Marks().CampTextPlacements;
            try
            {
                MythicUi.ApplyTextScale(Panel, 1.5f);
                Marks().Present(_session, null);
                Assert.That(Marks().CampTextPlacements, Is.EqualTo(passes + 1), "a new text size: exactly one pass");
                Marks().Present(_session, null);
                Assert.That(Marks().CampTextPlacements, Is.EqualTo(passes + 1), "a second Present at that size: no pass");
            }
            finally { MythicUi.ApplyTextScale(Panel, 1f); }
        }

        // ---- 5. no garbage ----------------------------------------------------------------------------------------------------------

        [Test]
        public void ASteadyPresentAndAForcedPassAllocateNothing()
        {
            var camps = Camps(new Vector2(184, 96), new Vector2(184, 40), new Vector2(96, 4), new Vector2(96, 100));
            OpenOrder(camps, Row(2, 25), Row(2, 50, true), Row(2, 75, true), Row(2, 100));
            AssertPlaced(4, "four camps");
            var marks = Marks();
            Action layer = () => marks.LayerChanged();
            Action present = () => marks.Present(_session, null);
            AllocationMeter.AssertNone(layer, 200, "a forced camp-text pass (LayerChanged)");
            // unless the base's steady Present already allocates on this rig for a cause outside this packet: then only the pass is asserted
            if (!AllocationMeter.Allocates(present, 200)) AllocationMeter.AssertNone(present, 200, "a steady Present");
        }

        // ---- 6. the zone names ------------------------------------------------------------------------------------------------------

        [Test]
        public void TheZoneNamesSeeThePlacedBlocks()
        {
            var camps = PictureTexts(out var rows);
            BuildController(true, camps);
            DeliverRows(rows); Frames(2);
            foreach (float k in new[] { 1f, 1.4f })
            {
                Large().SetMarkScale(k);
                string step = "k " + k;
                AssertPlaced(4, step);
                var blocks = new List<MapRect>();
                for (int i = 0; i < 4; i++) blocks.Add(Of(Block(i)));
                for (int r = 1; r <= MapZones.Count; r++)
                {
                    var name = Marks().ZoneLabelRect(r);
                    Assert.That(name.HasValue, Is.True, step + ": " + MapZones.Name(r) + " has a name rect");
                    if (!name.HasValue) continue;
                    for (int i = 0; i < blocks.Count; i++)
                        Assert.That(Meets(Of(name.Value), blocks[i]), Is.False, step + ": the name " + MapZones.Name(r) + " meets no camp's block (" + i + ")");
                }
            }
        }

        // ---- 7. standing guard ------------------------------------------------------------------------------------------------------

        [Test]
        public void ACampWithNoTextCreatesNoLabel()
        {
            var camps = Camps(new Vector2(96, 96), new Vector2(30, 150));
            OpenOrder(camps, new WireCamp { Alive = 3 }, new WireCamp { Alive = 3 });
            Assert.That(_controller.IsOpen, Is.True, "the map is open");
            Assert.That(Marks().CampMarkerCount, Is.EqualTo(2), "premise: both camps are delivered");
            Marks().Present(_session, null);
            Marks().LayerChanged();
            Assert.That(BadgeChildren(), Is.EqualTo(0), "no CampBadgeText label for a camp with no text");
            Assert.That(MeasureLabel(), Is.Null, "no CampTextMeasure label for a camp with no text");
        }

        // ---- 8. the real order ------------------------------------------------------------------------------------------------------

        static readonly string[][] FourCampLines = { new[] { "+0.75%" }, new[] { "+1.5%", "Aug" }, new[] { "+2.25%", "Aug" }, new[] { "+3%" } };

        MapCamp[] FourCamps() => Camps(new Vector2(184, 96), new Vector2(184, 40), new Vector2(96, 4), new Vector2(96, 100));
        WireCamp[] FourRows() => new[] { Row(2, 25), Row(2, 50, true), Row(2, 75, true), Row(2, 100) };

        [Test]
        public void TheCampTextsOfARealMatchAreMeasuredWhenTheMapOpens()
        {
            RealUntilOpen(FourCamps(), FourRows());
            Assert.That(Panel.gameObject.activeSelf, Is.False, "premise: the panel is closed");
            Assert.That(Marks().CampTextPlacements, Is.EqualTo(0), "premise: nothing is placed while closed");
            Assert.That(BadgeChildren(), Is.EqualTo(0), "premise: no CampBadgeText label yet");
            Assert.That(MeasureLabel(), Is.Null, "premise: no CampTextMeasure label yet");

            // a row with text and no block: a Present on the closed marks draws nothing and creates nothing
            Assert.DoesNotThrow(() => Marks().Present(_session, null), "a Present on the closed marks");
            Assert.That(Marks().CampMarkerCount, Is.EqualTo(4), "closed Present: four camp rows");
            for (int i = 0; i < 4; i++) Assert.That(Marks().CampTextRect(i).HasValue, Is.False, "closed Present: camp " + i + " has no block");
            Assert.That(BadgeChildren(), Is.EqualTo(0), "closed Present: no CampBadgeText label is created");
            Assert.That(Marks().CampTextPlacements, Is.EqualTo(0), "closed Present: no pass");

            Open();
            AssertPlaced(4, "opened");
            AssertBlockWidths(FourCampLines, 9f, "opened at the controller's own scale");
            // the controller opens the layer at about 1.3 (480 units across); the phase B premise below needs the layer at 1 (624 across)
            Large().SetMarkScale(1f); Frame();
            AssertPlaced(4, "opened at scale 1");
            AssertBlockWidths(FourCampLines, 9f, "opened at 9");
            var rect9 = new Rect[4]; var place9 = new int[4];
            for (int i = 0; i < 4; i++) { rect9[i] = Block(i); place9[i] = Marks().CampTextPlace(i); }
            Assert.That(place9[0], Is.Not.EqualTo(CampTextPlacement.Right), "premise: the camp at (184, 96) is not Right at 9: its Right leaves the map");

            // phase B: a bigger text size while closed
            try
            {
                Close();
                int passes = Marks().CampTextPlacements;
                MythicUi.ApplyTextScale(Panel, 1.5f);
                Marks().LayerChanged();
                Assert.That(Marks().CampTextPlacements, Is.EqualTo(passes), "closed at 1.5: no pass");
                for (int i = 0; i < 4; i++) AssertSameRect(Marks().CampTextRect(i), rect9[i], "closed at 1.5: camp " + i);
                Assert.That(MeasureLabel() != null && !MeasureLabel().gameObject.activeInHierarchy, Is.True, "closed: the measuring label is not active in the hierarchy");
                Open();
                Large().SetMarkScale(1f); Frame();
                Assert.That(Marks().CampTextPlacements, Is.GreaterThan(passes), "opened at 1.5: a pass ran");
                AssertBlockWidths(FourCampLines, 13.5f, "opened at 13.5");
                Assert.That(Marks().CampTextPlace(0), Is.EqualTo(CampTextPlacement.Below), "at 13.5 the camp at (184, 96) is Below: the gap follows the clock reserve");
                var rectB = new Rect[4]; var placeB = new int[4];
                for (int i = 0; i < 4; i++) { rectB[i] = Block(i); placeB[i] = Marks().CampTextPlace(i); }
                int passesB = Marks().CampTextPlacements;

                // phase C: closed, a forced call, three frames, opened: nothing moved
                Close();
                Marks().LayerChanged(); Frames(3);
                Open(2);
                Assert.That(Marks().CampTextPlacements, Is.EqualTo(passesB), "phase C: no pass");
                for (int i = 0; i < 4; i++)
                {
                    Assert.That(Marks().CampTextPlace(i), Is.EqualTo(placeB[i]), "phase C: camp " + i + " place");
                    AssertSameRect(Marks().CampTextRect(i), rectB[i], "phase C: camp " + i);
                }
            }
            finally { MythicUi.ApplyTextScale(Panel, 1f); }
        }

        // ---- 9. planning ------------------------------------------------------------------------------------------------------------

        [Test]
        public void ThePlanningOrderPlacesWhenThePanelShows()
        {
            BuildController(false, FourCamps()); DeliverPlan(); Frame();
            Assert.That(_controller.IsPlanning, Is.True, "premise: planning");
            Assert.That(Panel.gameObject.activeSelf, Is.True, "premise: the panel is shown by the first Tick");
            DeliverRows(FourRows()); Frames(2);
            Assert.That(Marks().gameObject.activeInHierarchy, Is.True, "the marks object is active in the hierarchy");
            Assert.That(Marks().CampTextPlacements, Is.GreaterThan(0), "a pass has run");
            for (int i = 0; i < 4; i++) Assert.That(Marks().CampTextRect(i).HasValue, Is.True, "camp " + i + " has a block");
            AssertBlockWidths(FourCampLines, 9f, "planning");
        }

        // ---- 10. a rebind -----------------------------------------------------------------------------------------------------------

        [Test]
        public void ARebindForgetsTheBlocks()
        {
            RealUntilOpen(FourCamps(), FourRows());
            Open();
            AssertPlaced(4, "first match");
            int passes = Marks().CampTextPlacements;
            for (int i = 0; i < 4; i++) Assert.That(Marks().CampTextRect(i).HasValue, Is.True, "first match: camp " + i + " has a block");
            Close();
            Rebind();
            Frames(4);
            Open();
            Assert.That(Marks().CampMarkerCount, Is.EqualTo(0), "second match: no camps");
            for (int i = 0; i < 4; i++)
            {
                Assert.That(Marks().CampTextRect(i).HasValue, Is.False, "second match: camp " + i + " has no block");
                Assert.That(Marks().CampTextPlace(i), Is.EqualTo(CampTextPlacement.None), "second match: camp " + i + " has no place");
            }
            int active = 0;
            foreach (var label in ActiveBadgeLabels()) active++;
            Assert.That(active, Is.EqualTo(0), "second match: no CampBadgeText label is active");
            Assert.That(Marks().CampTextPlacements, Is.EqualTo(passes), "second match: no pass ran");
        }

        // ---- 11. destroyed terrain --------------------------------------------------------------------------------------------------

        [Test]
        public void ATerrainChangeInTheMiddleOfAMatchMovesNoText()
        {
            var camps = PictureTexts(out var rows);
            foreach (var camp in camps) Assert.That(camp.X < 32 && camp.Y < 32, Is.False, "set-up: no camp stands on tile (0, 0)");
            BuildController(true, camps, true);
            DeliverRows(rows); Frames(2);
            Large().SetMarkScale(1f);
            AssertPlaced(4, "before the terrain changes");
            var texts = new[] { "+3", "+3%", "+4.5" };
            var before = Freeze(4, texts, true);
            Assert.That(before.Zones, Is.GreaterThan(0), "set-up: names have been placed");

            var oldMap = _session.TerrainMap;
            _transport.Deliver(new ServerMsg.State { Tick = _tick++, Items = new StateItem[] { new DestroyedTerrainState { Cells = new uint[] { 0 } } } });
            Frame();
            Assert.That(ReferenceEquals(_session.TerrainMap, oldMap), Is.False, "premise: the terrain map is a new object");
            Assert.That(ReferenceEquals(_session.TerrainMap, _session.Map), Is.False, "premise: it is not the Welcome map either");
            Assert.That(Marks().ZoneLabelPlacements, Is.GreaterThan(before.Zones), "premise: SetMap's forced zone pass ran");
            AssertFrozen(before, Freeze(4, texts, true), texts, "terrain replaced");
            Frames(3);
            AssertFrozen(before, Freeze(4, texts, true), texts, "three frames after");
        }
    }
}
