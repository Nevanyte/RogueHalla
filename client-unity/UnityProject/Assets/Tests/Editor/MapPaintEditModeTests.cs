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
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.UI;

namespace Roguehalla.Client.Tests
{
    /// <summary>
    /// P-UI-MAP-PAINT-1, the paint half: the map's surfaces are painted on news (a snapshot, a delivery, a changed layout, an input that
    /// changes what is drawn) and on a quarter-second heartbeat, once per news however many handlers marked it, and the Tab-held texts are
    /// rebuilt only when what they show changed. The real <see cref="TacticalMapController"/> on a recording transport as
    /// <see cref="TabMapFlowEditModeTests"/> builds it (its Welcome map here carries one camp and a wall at tile 0); the clock is the
    /// controller's <see cref="TacticalMapController.SecondsClock"/> seam, so no test reads time. The packet
    /// (docs/backlog/packets/P-UI-MAP-PAINT-1.md) has the rules and the numbers.
    /// </summary>
    public sealed class MapPaintEditModeTests
    {
        sealed class RecordingTransport : ITransport
        {
            public event Action Opened;
            public event Action<byte[]> Received;
            public event Action<string> Closed;
            public readonly List<byte[]> Sent = new List<byte[]>();
            public void Connect(string url) { }
            public void Send(byte[] frame) { Sent.Add(frame); }
            public void Close() { Closed?.Invoke("closed"); }
            public void Poll() { }
            public void Open() { Opened?.Invoke(); }
            public void Deliver(ServerMsg message) { Received?.Invoke(message.Encode()); }
            public List<WireCommand> Take()
            {
                var commands = new List<WireCommand>();
                foreach (var frame in Sent)
                    if (ClientMsg.Decode(frame) is ClientMsg.Input input) commands.AddRange(input.Commands);
                Sent.Clear();
                return commands;
            }
        }

        const int MapTiles = 16;
        static readonly Vector2 A = new Vector2(100f, 100f), B = new Vector2(300f, 120f);
        static readonly MapCamp[] Camps = { new MapCamp(256, 256, 8, 1) };
        const string Fallback = "Quest directions arrive with the next server update.";

        GameObject _clientObject, _mapObject, _canvasObject, _arenaObject, _cameraObject;
        MatchClient _client;
        TacticalMapController _map;
        TacticalMapSurface _mini, _large;
        Session _session;
        RecordingTransport _transport;
        Keyboard _keyboard;
        Mouse _mouse;
        Func<double> _priorClock;
        bool _priorToggle, _priorShowRoute;
        double _now;
        uint _tick;
        byte _weapon;
        WireActor[] _actors = new WireActor[0];

        [SetUp]
        public void SetUp()
        {
            _priorClock = TacticalMapController.SecondsClock;
            Build(true);
        }

        [TearDown]
        public void TearDown()
        {
            try { Dispose(); }
            finally
            {
                TacticalMapController.SecondsClock = _priorClock;
                WorldLabelVisibility.Hidden = false;
            }
        }

        ServerMsg.Welcome Welcome()
        {
            var tiles = new byte[MapTiles * MapTiles];
            tiles[0] = MapBlob.Wall;
            return new ServerMsg.Welcome
            {
                Protocol = Wire.ProtocolVersion, MatchId = new byte[16], Session = new byte[16], PlayerId = 1,
                TickRate = 20, Map = new MapBlob(MapTiles, MapTiles, tiles).WithPublic(Camps, null, null, null, null)
            };
        }

        /// <summary>The rig of TabMapFlowEditModeTests. <paramref name="welcomeFirst"/> false leaves the Welcome undelivered (the real order of test 15).</summary>
        void Build(bool welcomeFirst)
        {
            _now = 100d; _tick = 1; _weapon = 0; _actors = new WireActor[0];   // NUnit shares one fixture instance between tests
            _keyboard = InputSystem.AddDevice<Keyboard>();
            _mouse = InputSystem.AddDevice<Mouse>();
            TacticalMapController.SecondsClock = () => _now;
            WorldLabelVisibility.Hidden = false;

            _transport = new RecordingTransport();
            _session = new Session(_transport, new SessionOptions { Url = "ws://localhost/map-paint", Profile = "fixture", Now = () => 1000 });
            _session.Connect();
            _transport.Open();
            if (welcomeFirst) { _transport.Deliver(Welcome()); Snapshot(); }

            _clientObject = new GameObject("Map paint client");
            _client = _clientObject.AddComponent<MatchClient>();
            _client.enabled = false;
            _client.Url = "ws://localhost/map-paint"; _client.Profile = "fixture";
            typeof(MatchClient).GetField("<Session>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(_client, _session);
            _arenaObject = new GameObject("Map paint arena");
            _arenaObject.transform.SetParent(_clientObject.transform, false);
            _client.Arena = _arenaObject.AddComponent<ArenaView>();
            _cameraObject = new GameObject("Map paint camera", typeof(Camera));
            _cameraObject.transform.SetParent(_clientObject.transform, false);
            _client.Cam = _cameraObject.GetComponent<Camera>();

            _canvasObject = new GameObject("Map paint canvas", typeof(RectTransform), typeof(Canvas), typeof(GraphicRaycaster));
            _canvasObject.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
            ((RectTransform)_canvasObject.transform).sizeDelta = new Vector2(1000, 800);
            _mapObject = new GameObject("Map paint controller");
            _map = _mapObject.AddComponent<TacticalMapController>();
            _map.Initialize(_client, _canvasObject.transform);
            _priorToggle = _map.ToggleMode; _priorShowRoute = _map.ShowRoute;
            _map.SetToggleMode(false);
            _map.Bind(_session);
            _mini = _canvasObject.transform.Find("Minimap/TerrainFrame").GetComponent<TacticalMapSurface>();
            _large = _canvasObject.transform.Find("TacticalMap/MapFrame/PublicMap").GetComponent<TacticalMapSurface>();
            if (!welcomeFirst) return;
            Assert.That(_session.State, Is.EqualTo(SessionState.InMatch), "set-up");
            Assert.That(_session.Motion.Initialized, Is.True, "set-up");
            Frame(); Frame();
            Assert.That(_mini.gameObject.activeInHierarchy, Is.True, "set-up: the minimap is on screen");
            _transport.Take();
        }

        void Dispose()
        {
            try
            {
                if (_map != null) { _map.SetToggleMode(_priorToggle); _map.SetShowRoute(_priorShowRoute); _map.Bind(null); }
                if (_session != null) _session.Leave();
            }
            finally
            {
                WorldLabelVisibility.Hidden = false;
                if (_keyboard != null && _keyboard.added) InputSystem.RemoveDevice(_keyboard);
                if (_mouse != null && _mouse.added) InputSystem.RemoveDevice(_mouse);
                if (_mapObject != null) UnityEngine.Object.DestroyImmediate(_mapObject);
                if (_canvasObject != null) UnityEngine.Object.DestroyImmediate(_canvasObject);
                if (_clientObject != null) UnityEngine.Object.DestroyImmediate(_clientObject);
                _mapObject = _canvasObject = _clientObject = null; _map = null; _session = null; _keyboard = null; _mouse = null;
            }
        }

        // ---- the tests: how often it paints -------------------------------------------------------------------------------------------

        /// <summary>Red: a snapshot is news and paints once; nothing else paints between snapshots 50 ms apart (the base also paints on a 33 ms timer).</summary>
        [TestCase(false)]
        [TestCase(true)]
        public void ASnapshotEveryFiftyMsPaintsOncePerSnapshot(bool open)
        {
            if (open) _map.SetOpen(true);
            Settle();
            int visible0 = Visible(open), other0 = Visible(!open);
            for (int i = 0; i < 200; i++) Frame(10, i % 5 == 0);
            Assert.That(Visible(open) - visible0, Is.EqualTo(40), "forty snapshots, forty paints of the visible surface");
            Assert.That(Visible(!open) - other0, Is.EqualTo(0), "the surface that is not on screen is not painted");
        }

        /// <summary>Red: with nothing delivered the map paints on its heartbeat, four times a second.</summary>
        [TestCase(false)]
        [TestCase(true)]
        public void WithNoDeliveryItPaintsFourTimesASecond(bool open)
        {
            if (open) _map.SetOpen(true);
            Settle();
            int visible0 = Visible(open);
            for (int i = 0; i < 200; i++) Frame(20, false);
            Assert.That(Visible(open) - visible0, Is.InRange(12, 24), "four seconds of 20 ms frames: one paint per 13 frames (16 expected)");
        }

        /// <summary>Red: the heartbeat is a quarter of a second.</summary>
        [TestCase(false)]
        [TestCase(true)]
        public void TheHeartbeatIsAQuarterSecond(bool open)
        {
            if (open) _map.SetOpen(true);
            Settle();
            int c0 = Visible(open);
            Frame(240, false);
            Assert.That(Visible(open), Is.EqualTo(c0), "240 ms after a paint: nothing is due");
            Frame(20, false);
            Assert.That(Visible(open), Is.EqualTo(c0 + 1), "260 ms after a paint: one paint");
        }

        // ---- news ------------------------------------------------------------------------------------------------------------------

        static TradeState OneMerchant(uint _ = 0) => new TradeState { Available = true, Value = new WireTradeView { Merchants = new[] {
            new WireMerchant { Kind = TradeKind.Barter, X = 100, Y = 100, Name = "same" } } } };

        /// <summary>Red: the collections handler presents at once; the Tick after it must not present the same news again.</summary>
        [TestCase(false)]
        [TestCase(true)]
        public void ACollectionsEventPaintsOnce(bool open)
        {
            if (open) _map.SetOpen(true);
            Settle();
            int c0 = Visible(open);
            _transport.Deliver(new ServerMsg.State { Tick = ++_tick, Items = new StateItem[] { OneMerchant() } });
            Assert.That((open ? _large : _mini).Marks.MerchantCount, Is.EqualTo(1), "the delivered merchant is on the visible surface at once");
            Frame(5, false);
            Assert.That(Visible(open), Is.EqualTo(c0 + 1), "one present for the delivery, none from the Tick after it");
        }

        /// <summary>Red: a session state change presents at once and the Tick after it does not present it again.</summary>
        [Test]
        public void AStateEventPaintsOnce()
        {
            _actors = new[] { new WireActor { Id = 3, X = 80, Y = 80, Faction = 2, HpPct = 100, Status = 1 } };
            Frame(); Frame();
            Settle();
            Assert.That(_mini.Marks.ContactCount, Is.EqualTo(1), "premise: the contact is on the minimap");
            int c0 = _map.MinimapPresentCount;
            _transport.Close();
            Assert.That(_mini.Marks.ContactCount, Is.EqualTo(0), "the state change clears the visible surface at once");
            _now += .005;
            _map.Tick();
            Assert.That(_map.MinimapPresentCount, Is.EqualTo(c0 + 1), "one present for the state change, none from the Tick after it");
        }

        /// <summary>Red: tracking a quest presents at once (the objective shows) and the Tick after it does not present again.</summary>
        [TestCase(false)]
        [TestCase(true)]
        public void ATrackedQuestPaintsOnce(bool open)
        {
            var handle = new QuestHandle(71);
            var state = QuestState(handle);
            state.View.Rows[0].State = QuestRowState.Open;
            state.View.Rows[0].OwnerAccepted = true;
            _transport.Deliver(new ServerMsg.State { Tick = ++_tick, Items = new StateItem[] { state } });
            if (open) _map.SetOpen(true);
            Settle();
            var marks = (open ? _large : _mini).Marks;
            Assert.That(marks.QuestObjectiveCount, Is.EqualTo(1), "an accepted quest objective is shown before it is tracked");
            int c0 = Visible(open);
            _map.SetTrackedQuest(handle);
            Assert.That(marks.QuestObjectiveCount, Is.EqualTo(1), "tracking emphasizes the objective without dropping other navigation marks");
            Frame(5, false);
            Assert.That(Visible(open), Is.EqualTo(c0 + 1), "one present for the tracking, none from the Tick after it");
        }

        [Test]
        public void MultipleAcceptedQuestObjectivesStayVisibleOnBothMapSurfaces()
        {
            var first = new QuestHandle(71);
            var second = new QuestHandle(72);
            var state = QuestState(first);
            state.View.Rows[0].State = QuestRowState.Open;
            state.View.Rows[0].OwnerAccepted = true;
            var another = new WireQuestRow { Handle = second, Def = "fixture_two", GiverX = 280, GiverY = 180,
                State = QuestRowState.Open, OwnerAccepted = true, Name = "Second quest", GiverLabel = "Another giver",
                Brief = "", Objective = "Find the second mark", Reward = "" };
            state.View.Rows = new[] { state.View.Rows[0], another };
            state.View.Objectives = new[]
            {
                new WireQuestObjective { Quest = first, X = 200, Y = 220 },
                new WireQuestObjective { Quest = second, X = 360, Y = 420 },
            };
            _transport.Deliver(new ServerMsg.State { Tick = ++_tick, Items = new StateItem[] { state } });
            Settle();
            Assert.That(_mini.Marks.QuestObjectiveCount, Is.EqualTo(2), "the minimap keeps both accepted quest destinations visible");
            _map.SetOpen(true);
            Settle();
            Assert.That(_large.Marks.QuestObjectiveCount, Is.EqualTo(2), "the full map keeps both accepted quest destinations visible");
            var directions = (TMP_Text)typeof(TacticalMapController).GetField("_questsText", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(_map);
            StringAssert.Contains("NEXT · native objective", directions.text);
            StringAssert.Contains("NEXT · Find the second mark", directions.text);
            StringAssert.Contains("MAP · 1 quest mark", directions.text);
        }

        /// <summary>Red: a new minimap size moves every pooled label, which only a present places: the next Tick paints.</summary>
        [Test]
        public void ANewMinimapSizePaintsOnTheNextTick()
        {
            Settle();
            Assert.That(_map.MinimapRoot.sizeDelta.x, Is.EqualTo(270f).Within(.01f), "premise: the size Initialize gave");
            int c0 = _map.MinimapPresentCount;
            _map.ApplyViewport(HudLayout.Viewport(1280, 800), 1.25f);
            Assert.That(_map.MinimapRoot.sizeDelta.x, Is.EqualTo(338f).Within(.01f), "premise: the new size");
            Frame(5, false);
            Assert.That(_map.MinimapPresentCount, Is.EqualTo(c0 + 1), "the minimap is painted on the next Tick");
        }

        /// <summary>Red: a changed stage size re-lays the open map out, and a layout change is news.</summary>
        [Test]
        public void AChangedStagePaintsOnTheNextTick()
        {
            Dispose();
            var rig = new StageRig();
            try
            {
                rig.Build();
                rig.Settle();
                int c0 = rig.Controller.LargePresentCount;
                rig.Stage.sizeDelta = StageRig.StageUnits(new Vector2(640, 480));
                rig.Frame(5);
                Assert.That(rig.Controller.LargePresentCount, Is.EqualTo(c0 + 1), "the open map is painted on the next Tick");
            }
            finally { rig.Dispose(); }
        }

        /// <summary>Red: a refused pick changes the planning feedback, which the next Tick must show.</summary>
        [Test]
        public void ARefusedPickShowsItsFeedbackOnTheNextTick()
        {
            DeliverPlan();
            Settle();
            Assert.That(_map.IsPlanning, Is.True, "premise: the planning screen");
            Assert.That(Status(), Is.Not.EqualTo("That spawn is already confirmed."), "premise: no feedback yet");
            Click("PlanningDecisions/Spawn1");
            Frame(5, false);
            Assert.That(Status(), Is.EqualTo("That spawn is already confirmed."), "the refused spawn pick is explained on the next Tick");
            Settle();
            Click("PlanningDecisions/Boon0");
            Frame(5, false);
            Assert.That(Status(), Is.EqualTo("That boon is already confirmed."), "the refused boon pick is explained on the next Tick");
        }

        /// <summary>Guard (green on the base): a route stop is news.</summary>
        [Test]
        public void ARouteStopPaintsOnTheNextTick()
        {
            _map.SetOpen(true);
            Settle();
            int c0 = _map.LargePresentCount;
            _large.Pressed(A, PointerEventData.InputButton.Left);
            Frame(5, false);
            Assert.That(_map.LargePresentCount, Is.EqualTo(c0 + 1), "the open map is painted on the next Tick");
            StringAssert.StartsWith("1/12 stops", RouteText(), "the route summary is replanned on the next Tick, not up to a quarter second later");
        }

        /// <summary>Guard: showing or hiding the route is news.</summary>
        [Test]
        public void ShowingTheRoutePaintsOnTheNextTick()
        {
            Settle();
            int c0 = _map.MinimapPresentCount;
            _map.SetShowRoute(!_map.ShowRoute);
            Frame(5, false);
            Assert.That(_map.MinimapPresentCount, Is.EqualTo(c0 + 1), "the minimap is painted on the next Tick");
        }

        /// <summary>Guard: opening the map paints it on the next Tick, once.</summary>
        [Test]
        public void OpeningTheMapPaintsOnTheNextTick()
        {
            Settle();
            int c0 = _map.LargePresentCount;
            _map.SetOpen(true);
            Frame(5, false);
            Assert.That(_map.IsOpen, Is.True, "premise: open");
            Assert.That(_map.LargePresentCount, Is.EqualTo(c0 + 1), "painted once on the frame it opens");
        }

        // ---- things that change with the server time, and the gesture ---------------------------------------------------------------

        /// <summary>Guard: a camp clock is a function of the newest snapshot's server time.</summary>
        [Test]
        public void ClockTextsFollowTheServerTime()
        {
            _transport.Deliver(new ServerMsg.State { Tick = ++_tick, Items = new StateItem[] { new CampsState { Rows = new[] {
                new WireCamp { MobType = 0, Elite = 0, Alive = 0, State = 2, ClearedAt = 1000, RespawnAt = 181000,
                    Reward = new WireCampReward { Stat = 2, Amount = 100 } } } } } });
            Snapshot(100000);
            Frame(5, false);
            Assert.That(ClockText(), Is.EqualTo("81"), "181 s minus 100 s");
            Snapshot(101000);
            Frame(5, false);
            Assert.That(ClockText(), Is.EqualTo("80"), "the next snapshot's time");
            for (int i = 0; i < 10; i++) Frame(100, false);
            Assert.That(ClockText(), Is.EqualTo("80"), "it stays while nothing is delivered");
        }

        /// <summary>Red: a held gesture's repeat is offered on every frame, painted or not.</summary>
        [Test]
        public void AGestureTicksOnAFrameWithoutAPaint()
        {
            SetMouse(Mouse.current.position.ReadValue(), MouseButton.Right);
            _mini.Pressed(A, PointerEventData.InputButton.Right);
            Assert.That(TakeMoves().Count, Is.EqualTo(1), "set-up: the minimap press sends a move");
            _mini.Dragged(B, PointerEventData.InputButton.Right);
            Frame(95);
            Assert.That(TakeMoves(), Is.Empty, "95 ms after the press: under the gesture's 100 ms interval, nothing is sent");
            int c0 = _map.MinimapPresentCount;
            Frame(10, false);
            var sent = TakeMoves();
            Assert.That(sent.Exists(m => Near(m, B)), Is.True, "105 ms after the press: the move to B is sent on a frame that paints nothing (sent: " + Describe(sent) + ")");
            Assert.That(_map.MinimapPresentCount, Is.EqualTo(c0), "and that frame did not paint");
        }

        // ---- the Tab-held texts ---------------------------------------------------------------------------------------------------------

        static WireActor Actor(uint id, byte faction, ActorKind kind) =>
            new WireActor { Id = id, X = 80, Y = 80, Faction = faction, HpPct = 100, Status = 1, Kind = (byte)kind };

        const byte Haunted = 1, Plague = 2;

        void CreaturesSetUp()
        {
            _actors = new[]
            {
                Actor(3, Haunted, ActorKind.RegionalMob), Actor(4, Haunted, ActorKind.RegionalMob), Actor(5, Haunted, ActorKind.RegionalMob),
                Actor(6, Plague, ActorKind.RegionalMob), Actor(7, Plague, ActorKind.RegionalMob)
            };
            Frame(); Frame();
            _map.SetOpen(true);
            Frame();
            Assert.That(_map.IsOpen, Is.True, "set-up: the Tab map is open");
        }

        /// <summary>Red: a new snapshot with the same contacts rebuilds nothing and allocates nothing (the base allocates 28 times a call).</summary>
        [Test]
        public void UnchangedContactsAllocateNothingOnANewSnapshot()
        {
            CreaturesSetUp();
            var method = typeof(TacticalMapController).GetMethod("PresentTactics", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(method, Is.Not.Null, "set-up: the controller has a PresentTactics method");
            Assert.That(method.GetParameters().Length, Is.EqualTo(0), "PresentTactics takes no parameters");
            var present = (Action)Delegate.CreateDelegate(typeof(Action), _map, method);
            var field = typeof(TacticalMapController).GetField("_lastContactsSnapshot", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null, "set-up: the controller has a _lastContactsSnapshot field");
            object minusOne = -1;   // boxed once: the write below allocates nothing of its own
            Action write = () => field.SetValue(_map, minusOne);
            Action call = () => { field.SetValue(_map, minusOne); present(); };
            Assert.That(ContactsText(), Is.EqualTo("Haunted · creatures x3\nPlague · creatures x2"), "set-up: the contacts");
            for (int i = 0; i < 5; i++) call();   // warm up
            AllocationMeter.AssertNone(write, 200, "the reflection write alone (a control: it must not allocate)");
            AllocationMeter.AssertNone(call, 200, "PresentTactics after a new snapshot with the same contacts");
            Assert.That(ContactsText(), Is.EqualTo("Haunted · creatures x3\nPlague · creatures x2"), "the text is unchanged");
        }

        /// <summary>Guard (green on the base): every kind of change to the contacts reaches the line on the tick it arrives.</summary>
        [Test]
        public void AChangedContactChangesTheLineOnThatTick()
        {
            _map.SetOpen(true);
            Frame();
            Assert.That(_map.IsOpen, Is.True, "set-up: the Tab map is open");
            _actors = new[] { Actor(3, Haunted, ActorKind.RegionalMob), Actor(4, Haunted, ActorKind.RegionalMob) };
            Frame();
            Assert.That(ContactsText(), Is.EqualTo("Haunted · creatures x2"), "two creatures");
            _actors = new[] { Actor(3, Haunted, ActorKind.RegionalMob), Actor(4, Plague, ActorKind.RegionalMob) };
            Frame();
            Assert.That(ContactsText(), Is.EqualTo("Haunted · creature x1\nPlague · creature x1"), "a faction changed");
            _actors = new[] { Actor(3, Haunted, ActorKind.RegionalMob), Actor(4, Plague, ActorKind.Zombie) };
            Frame();
            Assert.That(ContactsText(), Is.EqualTo("Haunted · creature x1\nPlague · zombie x1"), "a kind changed");
            _actors = new[] { Actor(3, Haunted, ActorKind.RegionalMob), Actor(4, Plague, ActorKind.Zombie), Actor(5, Haunted, ActorKind.RegionalMob) };
            Frame();
            Assert.That(ContactsText(), Is.EqualTo("Haunted · creatures x2\nPlague · zombie x1"), "a count changed");
            _actors = new[] { Actor(3, Haunted, ActorKind.RegionalMob), Actor(4, Plague, ActorKind.Zombie), Actor(5, Haunted, ActorKind.RegionalMob), Actor(6, Plague, ActorKind.Player) };
            Frame();
            Assert.That(ContactsText(), Is.EqualTo("Plague · Player\nHaunted · creatures x2\nPlague · zombie x1"), "a player arrived");
            _actors = new[] { Actor(3, Haunted, ActorKind.RegionalMob), Actor(4, Plague, ActorKind.Zombie), Actor(5, Haunted, ActorKind.RegionalMob), Actor(6, Plague, ActorKind.Player), Actor(7, Haunted, ActorKind.Player) };
            Frame();
            Assert.That(ContactsText(), Is.EqualTo("Plague · Player\nHaunted · Player\nHaunted · creatures x2\nPlague · zombie x1"), "a second player");
            _actors = new[] { Actor(3, Haunted, ActorKind.RegionalMob), Actor(4, Plague, ActorKind.Zombie), Actor(5, Haunted, ActorKind.RegionalMob), Actor(6, Haunted, ActorKind.Player), Actor(7, Plague, ActorKind.Player) };
            Frame();
            Assert.That(ContactsText(), Is.EqualTo("Haunted · Player\nPlague · Player\nHaunted · creatures x2\nPlague · zombie x1"), "two players of different factions swapped: same counts, other line");
        }

        /// <summary>Guard: a new Bind starts the contacts text from nothing, with or without a contact.</summary>
        [Test]
        public void ABoundMapRebuildsTheContactsText()
        {
            _actors = new[] { Actor(3, Haunted, ActorKind.RegionalMob) };
            Frame();
            _map.SetOpen(true);
            Frame();
            Assert.That(ContactsText(), Is.EqualTo("Haunted · creature x1"), "set-up");
            _map.Bind(null); _map.Bind(_session);
            _map.SetOpen(true);
            Frame();
            Assert.That(_map.IsOpen, Is.True, "premise: the Tab map is open again");
            Assert.That(ContactsText(), Is.EqualTo("Haunted · creature x1"), "a rebind rebuilds the line it cleared");

            _actors = new WireActor[0];
            Frame();
            Assert.That(ContactsText(), Is.EqualTo("No other contacts in view."), "set-up: no contact");
            _map.Bind(null); _map.Bind(_session);
            _map.SetOpen(true);
            Frame();
            Assert.That(_map.IsOpen, Is.True, "premise: the Tab map is open again");
            Assert.That(ContactsText(), Is.EqualTo("No other contacts in view."), "a rebind with no contact reads the empty line, not a blank one");
        }

        /// <summary>Guard: a change of the quest rows or the traders reaches the quests text.</summary>
        [Test]
        public void AQuestRowChangeChangesTheQuestsText()
        {
            _map.SetOpen(true);
            Frame();
            Assert.That(_map.IsOpen, Is.True, "set-up: the Tab map is open");
            Assert.That(QuestsText(), Is.EqualTo(Fallback), "no quest and no trader");
            var handle = new QuestHandle(71);
            var offered = QuestState(handle); offered.View.Rows[0].State = QuestRowState.Open;
            _transport.Deliver(new ServerMsg.State { Tick = ++_tick, Items = new StateItem[] { offered } });
            Frame(5, false);
            Assert.That(QuestsText(), Is.EqualTo("same\nACCEPT · Visit same"), "a quest giver");
            var renamed = QuestState(handle); renamed.View.Rows[0].State = QuestRowState.Open; renamed.View.Rows[0].Name = "Changed";
            _transport.Deliver(new ServerMsg.State { Tick = ++_tick, Items = new StateItem[] { renamed } });
            Frame(5, false);
            Assert.That(QuestsText(), Is.EqualTo("Changed\nACCEPT · Visit same"), "the quest's name changed");
            _transport.Deliver(new ServerMsg.State { Tick = ++_tick, Items = new StateItem[] { renamed, OneMerchant() } });
            Frame(5, false);
            Assert.That(QuestsText(), Is.EqualTo("Changed\nACCEPT · Visit same\n\nsame · trader"), "a trader arrived");
            _transport.Deliver(new ServerMsg.State { Tick = ++_tick, Items = new StateItem[] {
                new QuestsState { Available = true, View = new WireQuestView() }, new TradeState { Available = true, Value = new WireTradeView() } } });
            Frame(5, false);
            Assert.That(QuestsText(), Is.EqualTo("No open quests delivered."), "both emptied");
        }

        /// <summary>Guard (named): the first opening after a bind, with nothing to list, reads the fallback line, not an empty text.</summary>
        [Test]
        public void TheFirstOpeningReadsTheFallbackQuestLine()
        {
            Frame(); Frame();
            _map.SetOpen(true);
            Frame(5, false);
            Assert.That(_map.IsOpen, Is.True, "premise: open");
            Assert.That(QuestsText(), Is.EqualTo(Fallback), "the first opening");
        }

        /// <summary>Guard: the own weapon reaches the counter guide.</summary>
        [Test]
        public void AWeaponChangeReachesTheGuide()
        {
            _map.SetOpen(true);
            Frame();
            Assert.That(_map.IsOpen, Is.True, "set-up: the Tab map is open");
            Assert.That(GuideWeapon(), Is.EqualTo("sword"), "the sword (weapon 0)");
            _weapon = (byte)WeaponId.Bow;
            Frame();
            Assert.That(GuideWeapon(), Is.EqualTo("bow"), "a new snapshot with a bow");
            _weapon = (byte)WeaponId.Fists;
            Frame();
            Assert.That(GuideWeapon(), Is.EqualTo("fists"), "fists");
            _weapon = (byte)WeaponId.Wand;
            Frame();
            Assert.That(GuideWeapon(), Is.EqualTo("wand"), "a wand");
        }

        /// <summary>Guard (named): a replaced terrain map reaches the visible surface, painted again with the new map.</summary>
        [TestCase(false)]
        [TestCase(true)]
        public void ADestroyedTerrainReachesTheVisibleSurface(bool open)
        {
            if (open) _map.SetOpen(true);
            Settle();
            int c0 = Visible(open);
            _transport.Deliver(new ServerMsg.State { Tick = ++_tick, Items = new StateItem[] { new DestroyedTerrainState { Cells = new uint[] { 0 } } } });
            Assert.That(ReferenceEquals(_session.TerrainMap, _session.Map), Is.False, "premise: the session drew a new terrain map");
            Frame(5, false);
            Assert.That((open ? _large : _mini).Map, Is.SameAs(_session.TerrainMap), "the visible surface holds the new map");
            Assert.That(Visible(open), Is.EqualTo(c0 + 2), "the handler's present with the old map, then the Tick's after the map compare marked news");
        }

        /// <summary>Guard (the real order): the controller binds before the Welcome; the minimap's first show has no map on its surface yet,
        /// so that present is not fresh and the next Tick paints the camp.</summary>
        [Test]
        public void TheMinimapShowsItsCampsAfterTheFirstTick()
        {
            Dispose();
            Build(false);
            _transport.Deliver(Welcome());
            Snapshot();
            _transport.Deliver(new ServerMsg.State { Tick = ++_tick, Items = new StateItem[] { new CampsState { Rows = new[] {
                new WireCamp { MobType = 0, Elite = 0, Alive = 3, State = 0, Reward = new WireCampReward { Stat = 2, Amount = 100 } } } } } });
            Assert.That(_session.State, Is.EqualTo(SessionState.InMatch), "premise: in the match");
            _now += .017;
            _map.ReadUiInput(false, false);
            Assert.That(_mini.gameObject.activeInHierarchy, Is.True, "premise: the minimap shows");
            _map.Tick();
            Assert.That(_mini.Marks.CampMarkerCount, Is.EqualTo(1), "the minimap shows its camp after the first Tick");
        }

        // ---- helpers ---------------------------------------------------------------------------------------------------------------------

        int Visible(bool open) => open ? _map.LargePresentCount : _map.MinimapPresentCount;

        /// <summary>A paint on both code paths (the heartbeat and the timer), then no paint due within 5 ms.</summary>
        void Settle() { Frame(300, false); Frame(5, false); }

        /// <summary>One player-loop frame: time advances, a snapshot arrives (unless told not to), the HUD reads input, the map ticks, input flushes.</summary>
        void Frame(double dtMs = 16.7, bool snapshot = true)
        {
            _now += dtMs / 1000d;
            if (snapshot) Snapshot();
            _map.ReadUiInput(false, false);
            _map.Tick();
            _session.FlushInput();
        }

        void Snapshot(double? serverMs = null)
        {
            _tick++;
            _transport.Deliver(new ServerMsg.Snapshot
            {
                Tick = _tick, Phase = 1, Actors = _actors, ServerTimeOverrideMs = serverMs,
                Me = new WireSelf { Id = 1, X = 48, Y = 48, Hp = 100, MaxHp = 100, Status = 1, Weapon = _weapon }
            });
        }

        /// <summary>The planning screen: the plan alone (R1's frames deliver the rising ticks); the server already confirmed spawn 1 and boon 0.</summary>
        void DeliverPlan()
        {
            _transport.Deliver(new ServerMsg.State { Tick = ++_tick, Items = new StateItem[] { new PlanState { Value = new WirePlan {
                EndsAt = 9999999, Spawn0 = 5, Spawn1 = 8, Pick = 1, Boon = 0, Boons = new[] { "First boon", "Second boon" },
                Option0 = new WirePlanOption { Index = 5, X = 64, Y = 64, Zone = 1 },
                Option1 = new WirePlanOption { Index = 8, X = 96, Y = 96, Zone = 2 } } } } });
        }

        static QuestsState QuestState(QuestHandle handle) => new QuestsState { Available = true, View = new WireQuestView {
            Rows = new[] { new WireQuestRow { Handle = handle, Def = "fixture", GiverX = 64, GiverY = 80,
                State = QuestRowState.Preview, ExpiresAt = 10000, CanAccept = true, Name = "same", GiverLabel = "same",
                Brief = "native brief", Objective = "native objective", Reward = "native reward" } },
            Objectives = new[] { new WireQuestObjective { Quest = handle, X = 200, Y = 220 } } } };

        void Click(string path)
        {
            var found = _canvasObject.transform.Find("TacticalMap/" + path);
            Assert.That(found, Is.Not.Null, "the panel has " + path);
            found.GetComponent<UnityEngine.UI.Button>().onClick.Invoke();
        }

        List<WireCommand> TakeMoves()
        {
            _session.FlushInput();
            return _transport.Take().FindAll(c => c.Kind == CommandKind.Move);
        }

        void SetMouse(Vector2 position, params MouseButton[] buttons)
        {
            _mouse.MakeCurrent();
            var state = new MouseState { position = position };
            foreach (var button in buttons) state = state.WithButton(button);
            InputSystem.QueueStateEvent(_mouse, state);
            InputSystem.Update();
        }

        static bool Near(WireCommand command, Vector2 point) => Mathf.Abs(command.X - point.x) < .01f && Mathf.Abs(command.Y - point.y) < .01f;

        static string Describe(List<WireCommand> moves)
        {
            var parts = new List<string>();
            foreach (var m in moves) parts.Add("(" + m.X + "," + m.Y + ")");
            return parts.Count == 0 ? "nothing" : string.Join(" ", parts);
        }

        string GuideWeapon()
        {
            var field = typeof(TacticalMapController).GetField("_counterGuide", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null, "the controller has a _counterGuide field");
            return ((WeaponCounterGuide)field.GetValue(_map)).ActiveWeapon;
        }

        string ClockText()
        {
            var clock = _mini.Marks.transform.Find("CampClock0");
            Assert.That(clock, Is.Not.Null, "the minimap has a CampClock0 label");
            return clock.GetComponent<TMP_Text>().text;
        }

        string Status() => _canvasObject.transform.Find("TacticalMap/MapStatus").GetComponent<TMP_Text>().text;
        string RouteText() => _canvasObject.transform.Find("TacticalMap/RouteSummary").GetComponent<TMP_Text>().text;
        string ContactsText() => _canvasObject.transform.Find("TacticalMap/TacticalInformation/ContactsPage/DeliveredContacts/Viewport/Content").GetComponent<TMP_Text>().text;
        string QuestsText() => _canvasObject.transform.Find("TacticalMap/TacticalInformation/WeaponsPage/QuestList/Viewport/Content").GetComponent<TMP_Text>().text;

        /// <summary>The rig of TabMapHeaderNoGarbageEditModeTests (a stage-sized panel, a session with one seat, no snapshots after the first),
        /// with a frame of a chosen length; the clock is the controller's seam, which the fixture's SetUp installed.</summary>
        sealed class StageRig
        {
            GameObject _canvas, _stageObject, _clientObject, _controllerObject;
            Session _session;
            bool _priorToggle;
            UiDisplayPreferences _prefs;
            double _now = 100d;
            Func<double> _priorClock;
            public TacticalMapController Controller;
            public RectTransform Stage;

            public static Vector2 StageUnits(Vector2 window)
            {
                float scale = Mathf.Clamp(Mathf.Min(window.x / 1280f, window.y / 800f), .8f, 1.5f);
                return new Vector2(window.x / scale, window.y / scale);
            }

            public void Build()
            {
                _priorClock = TacticalMapController.SecondsClock;
                TacticalMapController.SecondsClock = () => _now;
                WorldLabelVisibility.Hidden = false;
                _canvas = new GameObject("paint canvas", typeof(RectTransform), typeof(Canvas), typeof(UnityEngine.UI.GraphicRaycaster));
                _stageObject = new GameObject("paint stage", typeof(RectTransform));
                Stage = (RectTransform)_stageObject.transform; Stage.SetParent(_canvas.transform, false);
                Stage.anchorMin = Stage.anchorMax = Stage.pivot = new Vector2(.5f, .5f); Stage.sizeDelta = StageUnits(new Vector2(1280, 720));
                var transport = new Fix3Transport();
                _session = Fix3Transport.Join(transport, new MapBlob(4, 4, new byte[16]),
                    new WireSelf { Id = 1, Faction = 4, X = 48, Y = 48, Hp = 100, MaxHp = 100, Status = 1, Weapon = (byte)WeaponId.Wand });
                _clientObject = new GameObject("paint client");
                var client = _clientObject.AddComponent<MatchClient>(); client.enabled = false; client.Url = "ws://localhost/paint"; client.Profile = "fixture";
                _controllerObject = new GameObject("paint controller");
                Controller = _controllerObject.AddComponent<TacticalMapController>();
                _prefs = new UiDisplayPreferences(); _priorToggle = _prefs.MapToggle;
                Controller.Initialize(client, _canvas.transform, _prefs, Stage);
                Controller.SetToggleMode(false);
                Controller.Bind(_session);
                Controller.SetOpen(true);
                Frame(1000);
                Assert.That(Controller.IsOpen, Is.True, "set-up: the Tab map is open");
            }

            public void Frame(double dtMs)
            {
                _now += dtMs / 1000d;
                Controller.Tick();
                Canvas.ForceUpdateCanvases();
            }

            public void Settle() { Frame(300); Frame(5); }

            public void Dispose()
            {
                try
                {
                    if (Controller != null) { Controller.SetToggleMode(false); Controller.Bind(null); }
                    if (_session != null) _session.Leave();
                }
                finally
                {
                    foreach (var go in new[] { _controllerObject, _clientObject, _stageObject, _canvas })
                        if (go != null) UnityEngine.Object.DestroyImmediate(go);
                    TacticalMapController.SecondsClock = _priorClock;
                    WorldLabelVisibility.Hidden = false;
                    if (_prefs != null) _prefs.SetMapToggle(_priorToggle);
                }
            }
        }
    }
}
