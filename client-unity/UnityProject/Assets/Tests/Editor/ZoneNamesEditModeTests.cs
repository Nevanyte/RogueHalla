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
    /// P-UI-ZONE-NAMES-1: on the Tab map and the planning map a zone's name stands at the first of six spots around its icon that
    /// overlaps no mark of that zone (camp plates and texts, givers, wards, chests, delivered points, the map's edge), keeps a free
    /// spot, and costs nothing in a steady frame. The real TacticalMapController is laid out at 1280 x 720 with a 6 x 6 tile map (zone
    /// r's icon is at world ((r - .5) * 32, 80)); the clock is its <see cref="TacticalMapController.SecondsClock"/> seam. Every mark
    /// rect the tests compare with is built here from the layer rect, never read from the implementation.
    /// The packet (docs/backlog/packets/P-UI-ZONE-NAMES-1.md) has the rules and the picture's numbers.
    /// </summary>
    public sealed class ZoneNamesEditModeTests
    {
        static readonly Vector2 Window = new Vector2(1280, 720);
        const int Tiles = 6;
        const float WorldSide = Tiles * 32f;
        const int Haunted = MapZones.Sentenced, Holy = MapZones.Holy, Coliseum = MapZones.Coliseum;

        /// <summary>A camp of the picture: the zone whose icon it is read from, its layer offset from that icon (right, down), and its reward.</summary>
        struct PicCamp { public int Region; public float Right, Down; public byte Stat; public ushort Amount; public string Text; }

        // The picture 20-tabmap-held.png, read off with 2 units either way, taken as layer units at k = 1.
        static readonly PicCamp[] PictureCamps =
        {
            new PicCamp { Region = Haunted, Right = 16, Down = 28, Stat = 0, Amount = 10000, Text = "+3" },
            new PicCamp { Region = Haunted, Right = -54, Down = 11, Stat = 2, Amount = 100, Text = "+3%" },
            new PicCamp { Region = Holy, Right = 11, Down = 19, Stat = 0, Amount = 15000, Text = "+4.5" },
            new PicCamp { Region = Holy, Right = -34, Down = 20, Stat = 2, Amount = 100, Text = "+3%" },
        };
        static readonly Vector2[] PictureGivers = { new Vector2(-16, 30), new Vector2(-4, 23) };

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

        void BuildController(bool planning = false, MapCamp[] camps = null)
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
            _transport = new Fix3Transport();
            _session = Fix3Transport.Join(_transport, map,
                new WireSelf { Id = 1, Faction = 4, X = 48, Y = 48, Hp = 100, MaxHp = 100, Status = 1, Weapon = (byte)WeaponId.Wand });
            _clientObject = new GameObject("zone names client");
            _client = _clientObject.AddComponent<MatchClient>(); _client.enabled = false; _client.Url = "ws://localhost/zone-names"; _client.Profile = "fixture";
            _controllerObject = new GameObject("zone names controller");
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
            Assert.That(planning ? _controller.IsPlanning : _controller.IsOpen, Is.True, "set-up: the map is on screen");
            // The width the layer has at k = 1 (the layer is its map width divided by k), to turn the picture's layer offsets into world points.
            _layer1 = Marks().rectTransform.rect.width * Large().MarkScale;
            Assert.That(_layer1, Is.GreaterThan(300f), "set-up: the map is wide");
        }

        /// <summary>The picture's four camps as map camps, placed from the layer width of this layout at k = 1 (found by a first build).</summary>
        MapCamp[] PictureMapCamps(bool planning)
        {
            BuildController(planning);
            float w1 = _layer1;
            Reset();
            var list = new MapCamp[PictureCamps.Length];
            for (int i = 0; i < list.Length; i++)
            {
                var c = PictureCamps[i]; var w = Icon1(c.Region) + new Vector2(c.Right, c.Down) * (WorldSide / w1);
                list[i] = new MapCamp((ushort)Mathf.RoundToInt(w.x), (ushort)Mathf.RoundToInt(w.y), 8, (byte)c.Region);
            }
            _layer1 = w1;
            return list;
        }

        static Vector2 Icon1(int region) => new Vector2((region - .5f) * 32f, 80f);

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

        TacticalMapSurface Large() => Part("MapFrame/PublicMap").GetComponent<TacticalMapSurface>();
        TacticalMapMarks Marks() => Part("MapFrame/PublicMap/Delivered contacts and personal route").GetComponent<TacticalMapMarks>();
        RectTransform ZoneHost() => Part("MapFrame/PublicMap/Zone labels");
        TMP_Text NameLabel(int region) => ZoneHost().Find("ZoneLabel" + region).GetComponent<TMP_Text>();

        // ---- deliveries --------------------------------------------------------------------------------------------------------------

        void DeliverCamps()
        {
            var rows = new WireCamp[PictureCamps.Length];
            for (int i = 0; i < rows.Length; i++)
                rows[i] = new WireCamp { MobType = 0, Elite = 0, Alive = 3, State = 0, Reward = new WireCampReward { Stat = PictureCamps[i].Stat, Amount = PictureCamps[i].Amount } };
            _transport.Deliver(new ServerMsg.State { Tick = _tick++, Items = new StateItem[] { new CampsState { Rows = rows } } });
        }

        /// <summary>The givers as server rows, each with its own handle; (x, y) are world points.</summary>
        void DeliverGivers(params Vector2[] world)
        {
            var rows = new WireQuestRow[world.Length];
            for (int i = 0; i < rows.Length; i++)
                rows[i] = new WireQuestRow { Handle = new QuestHandle((uint)(71 + i)), Def = "fixture", GiverX = world[i].x, GiverY = world[i].y,
                    State = QuestRowState.Preview, ExpiresAt = 10000, CanAccept = true, Name = "giver " + i, GiverLabel = "giver " + i,
                    Brief = "native brief", Objective = "native objective", Reward = "native reward" };
            _transport.Deliver(new ServerMsg.State { Tick = _tick++, Items = new StateItem[] { new QuestsState { Available = true, View = new WireQuestView { Rows = rows } } } });
        }

        Vector2[] PictureGiverWorld(float extraDown = 0f)
        {
            var list = new Vector2[PictureGivers.Length];
            for (int i = 0; i < list.Length; i++)
                list[i] = Icon1(Haunted) + new Vector2(PictureGivers[i].x, PictureGivers[i].y + (i == 0 ? extraDown : 0f)) * (WorldSide / _layer1);
            return list;
        }

        /// <summary>The whole picture: four camps and two givers delivered, painted, then the layer scale set by hand to <paramref name="k"/>.</summary>
        void BuildPicture(float k, bool planning = false)
        {
            var camps = PictureMapCamps(planning);
            BuildController(planning, camps);
            DeliverCamps(); DeliverGivers(PictureGiverWorld());
            Frame(); Frame();
            Assert.That(Marks().CampMarkerCount, Is.EqualTo(4), "set-up: the four camps are delivered");
            Assert.That(Marks().QuestGiverCount, Is.EqualTo(2), "set-up: the two givers are delivered");
            Large().SetMarkScale(k);
            Assert.That(Large().MarkScale, Is.EqualTo(k).Within(.0001f), "set-up: the layer scale is " + k);
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

        Vector2 Icon(int region)
        {
            MapZones.LabelTiles(_regions, Tiles, Tiles, out var found, out var tileX, out var tileY);
            return LayerOf(new Vector2((float)tileX[region] * 32f, (float)tileY[region] * 32f));
        }

        static MapRect Of(Rect r) => new MapRect(r.x, r.y, r.width, r.height);
        static MapRect Square(Vector2 centre, float side) => new MapRect(centre.x - side / 2f, centre.y - side / 2f, side, side);
        static bool Hits(MapRect a, List<MapRect> marks) { foreach (var m in marks) if (a.Overlaps(m)) return true; return false; }
        static int HitCount(MapRect a, List<MapRect> marks) { int n = 0; foreach (var m in marks) if (a.Overlaps(m)) n++; return n; }

        /// <summary>The size of a pooled camp text of the marks object, from its own label (the packet's rule 1.9).</summary>
        Vector2 TextSize(string text)
        {
            foreach (var label in Marks().transform.GetComponentsInChildren<TMP_Text>(false))
                if (label.name.StartsWith("CampBadgeText") && label.text == text) return new Vector2(label.preferredWidth, label.preferredHeight);
            Assert.That(false, Is.True, "set-up: a CampBadgeText label shows '" + text + "'");
            return Vector2.zero;
        }

        /// <summary>Every mark of the picture as the tests see it: a giver 19.6 square, a camp plate 20.4 square, its amount text to the right.</summary>
        List<MapRect> PictureMarks(bool givers = true, bool camps = true)
        {
            var list = new List<MapRect>();
            if (givers) foreach (var world in PictureGiverWorld()) list.Add(Square(LayerOf(world), 19.6f));
            if (camps)
                foreach (var camp in _mapCamps)
                {
                    var at = LayerOf(new Vector2(camp.X, camp.Y));
                    list.Add(Square(at, 20.4f));
                }
            if (camps)
                for (int i = 0; i < _mapCamps.Length; i++)
                {
                    var at = LayerOf(new Vector2(_mapCamps[i].X, _mapCamps[i].Y)); var size = TextSize(PictureCamps[i].Text);
                    list.Add(new MapRect(at.x + 21.2f, at.y + 2f - size.y / 2f, size.x, size.y));
                }
            return list;
        }

        /// <summary>A name's spot rect for the label's own size, as the pure type gives it (from the icon).</summary>
        MapRect Spot(int spot, int region)
        {
            var label = NameLabel(region); var icon = Icon(region);
            return MapLabelPlacement.Spot(spot, icon.x, icon.y, label.preferredWidth, label.preferredHeight);
        }

        Rect NameRect(int region) => Marks().ZoneLabelRect(region) ?? default(Rect);

        Vector2 LabelPosition(int region) => NameLabel(region).rectTransform.anchoredPosition;

        void AssertNamesClear(List<MapRect> marks, string step)
        {
            var layer = LayerRect();
            for (int region = 1; region <= MapZones.Count; region++)
            {
                var found = Marks().ZoneLabelRect(region);
                Assert.That(found.HasValue, Is.True, step + ": zone " + region + " has a name rect");
                var rect = found ?? default(Rect);
                Assert.That(Hits(Of(rect), marks), Is.False, step + ": " + MapZones.Name(region) + "'s name overlaps no mark");
                Assert.That(rect.xMin >= layer.xMin - .01f && rect.xMax <= layer.xMax + .01f && rect.yMin >= layer.yMin - .01f && rect.yMax <= layer.yMax + .01f, Is.True, step + ": " + MapZones.Name(region) + "'s name is inside the layer rect");
                Assert.That(LabelPosition(region).x, Is.EqualTo(rect.center.x).Within(.01f), step + ": " + MapZones.Name(region) + " label x is the rect's centre");
                Assert.That(LabelPosition(region).y, Is.EqualTo(rect.center.y).Within(.01f), step + ": " + MapZones.Name(region) + " label y is the rect's centre");
            }
        }

        // ---- 1. the picture ----------------------------------------------------------------------------------------------------------

        [Test]
        public void ThePicturesNamesAreClearOfTheirMarks()
        {
            BuildPicture(1f);
            var marks = PictureMarks();
            Assert.That(marks.Count, Is.EqualTo(10), "set-up: two givers, four camp plates and four amount texts");
            foreach (int region in new[] { Haunted, Holy })
                Assert.That(HitCount(Spot(0, region), marks), Is.GreaterThanOrEqualTo(2), "set-up: spot 0 of " + MapZones.Name(region) + " overlaps at least two marks at k = 1");
            foreach (int region in new[] { Haunted, Holy })
                Assert.That(Marks().ZoneLabelSpot(region), Is.GreaterThan(0), MapZones.Name(region) + " leaves spot 0 at k = 1");
            AssertNamesClear(marks, "k = 1");
            Large().SetMarkScale(1.4f);
            var closer = PictureMarks();
            foreach (int region in new[] { Haunted, Holy })
                Assert.That(HitCount(Spot(0, region), closer), Is.GreaterThanOrEqualTo(2), "set-up: spot 0 of " + MapZones.Name(region) + " overlaps at least two marks at k = 1.4");
            foreach (int region in new[] { Haunted, Holy })
                Assert.That(Marks().ZoneLabelSpot(region), Is.GreaterThan(0), MapZones.Name(region) + " leaves spot 0 at k = 1.4, with no Present or Frame");
            AssertNamesClear(closer, "k = 1.4");
        }

        // ---- 2. nothing near it ------------------------------------------------------------------------------------------------------

        void AssertEveryNameIsToday(string step)
        {
            var layer = LayerRect();
            for (int region = 1; region <= MapZones.Count; region++)
            {
                Assert.That(Marks().ZoneLabelSpot(region), Is.EqualTo(0), step + ": " + MapZones.Name(region) + " is on spot 0");
                var icon = Icon(region);
                Assert.That(LabelPosition(region).x, Is.EqualTo(icon.x).Within(.01f), step + ": " + MapZones.Name(region) + " x");
                Assert.That(LabelPosition(region).y, Is.EqualTo(icon.y - 24f).Within(.01f), step + ": " + MapZones.Name(region) + " y, 24 layer units under the icon");
                var plate = Marks().ZonePlateRect(region);
                Assert.That(plate.HasValue, Is.True, step + ": " + MapZones.Name(region) + " has a plate");
                var p = plate ?? default(Rect);
                Assert.That(p.width, Is.EqualTo(104f).Within(.01f), step + ": plate width");
                Assert.That(p.height, Is.EqualTo(56f).Within(.01f), step + ": plate height");
                Assert.That(p.center.x, Is.EqualTo(icon.x).Within(.01f), step + ": plate centre x");
                Assert.That(p.center.y, Is.EqualTo(icon.y - 12f).Within(.01f), step + ": plate centre y");
            }
        }

        [Test]
        public void ANameWithNothingNearItIsWhereItIsToday()
        {
            BuildController();
            Assert.That(Large().MarkScale, Is.EqualTo(1.3f).Within(.001f), "set-up: the layer scale of 1280 x 720");
            AssertEveryNameIsToday("k = 1.3");
            Large().SetMarkScale(1.4f);
            AssertEveryNameIsToday("k = 1.4");
        }

        // ---- 3. no hopping -----------------------------------------------------------------------------------------------------------

        [Test]
        public void ANameKeepsItsSpotWhileItIsFree()
        {
            BuildController();
            DeliverGivers(PictureGiverWorld());
            Frame(); Frame();
            Large().SetMarkScale(1f);
            var marks = Large().Marks;
            Assert.That(marks.QuestGiverCount, Is.EqualTo(2), "set-up: the two givers are delivered");
            int spot = marks.ZoneLabelSpot(Haunted);
            Assert.That(spot, Is.Not.EqualTo(0), "set-up: HAUNTED leaves spot 0 for the two givers");
            Assert.That(spot, Is.GreaterThanOrEqualTo(1), "set-up: HAUNTED has a spot");
            var where = LabelPosition(Haunted);
            DeliverGivers(PictureGiverWorld(3f));
            marks.Present(_session, null);
            Assert.That(marks.QuestGiverCount, Is.EqualTo(2), "set-up: the moved giver is delivered");
            Assert.That(marks.ZoneLabelSpot(Haunted), Is.EqualTo(spot), "giver A 3 units lower: the spot is kept");
            Assert.That(LabelPosition(Haunted), Is.EqualTo(where), "giver A 3 units lower: the label has not moved at all");
            DeliverGivers();
            marks.Present(_session, null);
            Assert.That(marks.QuestGiverCount, Is.EqualTo(0), "set-up: no giver is delivered");
            Assert.That(marks.ZoneLabelSpot(Haunted), Is.EqualTo(spot), "no giver: no hop back to spot 0");
            Assert.That(LabelPosition(Haunted), Is.EqualTo(where), "no giver: the label has not moved at all");
            var on = Marks().ZoneLabelRect(Haunted);
            Assert.That(on.HasValue, Is.True, "HAUNTED has a name rect");
            var centre = (on ?? default(Rect)).center;
            DeliverGivers(WorldOf(centre));
            marks.Present(_session, null);
            Assert.That(marks.QuestGiverCount, Is.EqualTo(1), "set-up: the giver on the name is delivered");
            Assert.That(marks.ZoneLabelSpot(Haunted), Is.Not.EqualTo(spot), "a giver on the spot's centre: the name moves");
            Assert.That(Hits(Of(NameRect(Haunted)), new List<MapRect> { Square(centre, 19.6f) }), Is.False, "the new spot overlaps the giver no more");
        }

        // ---- 4. the box is the text, the plate follows -------------------------------------------------------------------------------

        [Test]
        public void TheBoxesAreTheTextAndThePlateFollows()
        {
            BuildPicture(1f);
            for (int region = 1; region <= MapZones.Count; region++)
            {
                var label = NameLabel(region); var icon = Icon(region);
                var rect = NameRect(region);
                Assert.That(Marks().ZoneLabelRect(region).HasValue, Is.True, MapZones.Name(region) + " has a name rect");
                Assert.That(rect.width, Is.EqualTo(label.preferredWidth).Within(.01f), MapZones.Name(region) + " box width is the text's");
                Assert.That(rect.height, Is.EqualTo(label.preferredHeight).Within(.01f), MapZones.Name(region) + " box height is the text's");
                var plate = Marks().ZonePlateRect(region);
                Assert.That(plate.HasValue, Is.True, MapZones.Name(region) + " has a plate");
                var p = plate ?? default(Rect);
                Vector2 d = rect.center - icon;
                Assert.That(p.width, Is.EqualTo(104f + Mathf.Abs(d.x)).Within(.01f), MapZones.Name(region) + " plate width");
                Assert.That(p.height, Is.EqualTo(32f + Mathf.Abs(d.y)).Within(.01f), MapZones.Name(region) + " plate height");
                Assert.That(p.center.x, Is.EqualTo(icon.x + d.x / 2f).Within(.01f), MapZones.Name(region) + " plate centre x");
                Assert.That(p.center.y, Is.EqualTo(icon.y + d.y / 2f).Within(.01f), MapZones.Name(region) + " plate centre y");
                Assert.That(p.xMin <= rect.xMin + .01f && p.xMax >= rect.xMax - .01f && p.yMin <= rect.yMin + .01f && p.yMax >= rect.yMax - .01f, Is.True, MapZones.Name(region) + " plate holds the name");
                Assert.That(p.xMin <= icon.x - 13f + .01f && p.xMax >= icon.x + 13f - .01f && p.yMin <= icon.y - 13f + .01f && p.yMax >= icon.y + 13f - .01f, Is.True, MapZones.Name(region) + " plate holds the 26 x 26 icon box");
            }
        }

        // ---- 5. a steady frame -------------------------------------------------------------------------------------------------------

        [Test]
        public void AQuietFrameAllocatesNothingAndPlacesNothing()
        {
            BuildPicture(1f);
            var marks = Large().Marks;
            Assert.That(marks.ZoneLabelPlacements, Is.GreaterThan(0), "set-up: placement passes have run");
            marks.LayerChanged(); marks.LayerChanged();
            Action pass = () => marks.LayerChanged();
            AllocationMeter.AssertNone(pass, 200, "a forced placement pass (LayerChanged)");
            marks.Present(_session, null);
            int n = marks.ZoneLabelPlacements;
            marks.Present(_session, null); marks.Present(_session, null);
            Assert.That(marks.ZoneLabelPlacements, Is.EqualTo(n), "two Present calls with the same marks place nothing");
            var moved = PictureGiverWorld(6f);
            DeliverGivers(moved);
            marks.Present(_session, null);
            Assert.That(marks.ZoneLabelPlacements, Is.EqualTo(n + 1), "a giver that moved makes one pass");
            marks.Present(_session, null);
            Assert.That(marks.ZoneLabelPlacements, Is.EqualTo(n + 1), "and the next Present with the same marks places nothing");
        }

        // ---- 6. the planning map -----------------------------------------------------------------------------------------------------

        [Test]
        public void ThePlanningMapAvoidsToo()
        {
            var camps = PictureMapCamps(true);
            BuildController(true, camps);
            DeliverGivers(PictureGiverWorld());
            Frame(); Frame();
            Large().SetMarkScale(1f);
            var marks = Large().Marks;
            Assert.That(marks.QuestGiverCount, Is.EqualTo(2), "set-up: the planning map draws both givers");
            var givers = new List<MapRect>();
            foreach (var world in PictureGiverWorld()) givers.Add(Square(LayerOf(world), 19.6f));
            Assert.That(HitCount(Spot(0, Haunted), givers), Is.EqualTo(2), "set-up: spot 0 of HAUNTED overlaps both givers");
            for (int region = 1; region <= MapZones.Count; region++)
            {
                Assert.That(NameLabel(region).gameObject.activeInHierarchy, Is.True, "set-up: the label of " + MapZones.Name(region) + " is active in the hierarchy");
                Assert.That(Marks().ZoneLabelRect(region).HasValue, Is.True, MapZones.Name(region) + " has a name rect");
                Assert.That(NameRect(region).width, Is.EqualTo(NameLabel(region).preferredWidth).Within(.01f), MapZones.Name(region) + " kept width is the label's");
                Assert.That(NameRect(region).height, Is.EqualTo(NameLabel(region).preferredHeight).Within(.01f), MapZones.Name(region) + " kept height is the label's");
            }
            Assert.That(Marks().ZoneLabelRect(Haunted).HasValue, Is.True, "HAUNTED has a name rect on the planning map");
            Assert.That(Hits(Of(NameRect(Haunted)), givers), Is.False, "the planning map's HAUNTED name misses both givers");
        }

        // ---- 7. a rect that comes back -----------------------------------------------------------------------------------------------

        [Test]
        public void ANarrowedAndRestoredRectBringsTheSpotBack()
        {
            BuildController();
            var marks = Large().Marks;
            for (int region = 1; region <= MapZones.Count; region++)
                Assert.That(marks.ZoneLabelSpot(region), Is.EqualTo(0), "set-up: no mark, so " + MapZones.Name(region) + " is on spot 0");
            float w = NameLabel(Coliseum).preferredWidth;
            var rect = marks.rectTransform;
            var before = rect.offsetMax;
            float width = rect.rect.width;
            rect.offsetMax = before + new Vector2(5f * w - width, 0f);
            Canvas.ForceUpdateCanvases();
            Assert.That(rect.rect.width, Is.EqualTo(5f * w).Within(.05f), "set-up: the layer is 5 names wide");
            var edges = new List<MapRect>();
            var r = LayerRect();
            MapLabelPlacement.AddEdges(edges, r.xMin, r.yMin, r.width, r.height);
            Assert.That(Hits(Spot(0, Coliseum), edges), Is.True, "set-up: spot 0 of COLISEUM overlaps the map's edge now");
            Assert.That(marks.ZoneLabelSpot(Coliseum), Is.Not.EqualTo(0), "COLISEUM leaves spot 0 when the layer narrows");
            rect.offsetMax = before;
            Canvas.ForceUpdateCanvases();
            Assert.That(rect.rect.width, Is.EqualTo(width).Within(.05f), "set-up: the layer is back");
            Assert.That(marks.ZoneLabelSpot(Coliseum), Is.EqualTo(0), "restored: COLISEUM is on spot 0 again, the rect pass stored nothing");
        }

        // ---- 8. a bigger text scale --------------------------------------------------------------------------------------------------

        [Test]
        public void TextScaleOnePointFiveKeepsNamesOffTheirIcons()
        {
            BuildController();
            var marks = Marks();
            var one = new float[MapZones.Count + 1];
            for (int region = 1; region <= MapZones.Count; region++)
            {
                Assert.That(marks.ZoneLabelRect(region).HasValue, Is.True, "set-up: " + MapZones.Name(region) + " has a name rect at scale 1");
                one[region] = NameRect(region).width;
            }
            try
            {
                MythicUi.ApplyTextScale(Panel, 1.5f);
                marks.LayerChanged();
                for (int region = 1; region <= MapZones.Count; region++)
                {
                    var rect = NameRect(region); var label = NameLabel(region); var icon = Icon(region);
                    Assert.That(rect.width, Is.GreaterThanOrEqualTo(1.2f * one[region]), MapZones.Name(region) + " is at least 1.2 times wider at 1.5");
                    Assert.That(rect.width, Is.EqualTo(label.preferredWidth).Within(.01f), MapZones.Name(region) + " box is the text's");
                    var grown = new MapRect(icon.x - 13f - 4f + .01f, icon.y - 13f - 4f + .01f, 26f + 8f - .02f, 26f + 8f - .02f);
                    Assert.That(grown.Overlaps(Of(rect)), Is.False, MapZones.Name(region) + " is 4 or more clear of its 26 x 26 icon box");
                }
            }
            finally { MythicUi.ApplyTextScale(Panel, 1f); }
        }
    }
}
