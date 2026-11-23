using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using Roguehalla.Net;
using Roguehalla.Net.Protocol;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace Roguehalla.Client.Tests
{
    /// <summary>
    /// P-UI-TABMAP-FLOW-1-fix: a hidden minimap is not painted (P-UI-TABMAP-FLOW-1), so it must be current on the very call that shows it
    /// again, not one Tick later. In the HUD's frame order the Tab map closes, TickFeatures ticks the map, and only then
    /// RefreshWorldHudVisibility un-suppresses the minimap: no Tick follows. The real TacticalMapController on a recording transport.
    /// </summary>
    public sealed class MinimapReturnEditModeTests
    {
        sealed class RecordingTransport : ITransport
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

        const int MapTiles = 16;

        GameObject _clientObject, _mapObject, _canvasObject, _arenaObject, _cameraObject;
        MatchClient _client;
        TacticalMapController _map;
        TacticalMapSurface _mini;
        Session _session;
        RecordingTransport _transport;
        Keyboard _keyboard;
        Mouse _mouse;
        Func<double> _priorClock;
        bool _priorToggle;
        double _now = 100d;
        uint _tick = 1;
        WireActor[] _actors = new WireActor[0];

        static WireActor Contact() => new WireActor { Id = 3, X = 80, Y = 80, Faction = 2, HpPct = 100, Status = 1 };

        [SetUp]
        public void SetUp()
        {
            _now = 100d; _tick = 1; _actors = new WireActor[0];   // NUnit shares one fixture instance between tests
            _keyboard = InputSystem.AddDevice<Keyboard>();
            _mouse = InputSystem.AddDevice<Mouse>();
            _priorClock = TacticalMapController.SecondsClock;
            TacticalMapController.SecondsClock = () => _now;
            WorldLabelVisibility.Hidden = false;

            _transport = new RecordingTransport();
            _session = new Session(_transport, new SessionOptions { Url = "ws://localhost/minimap-return", Profile = "fixture", Now = () => 1000 });
            _session.Connect();
            _transport.Open();
            _transport.Deliver(new ServerMsg.Welcome
            {
                Protocol = Wire.ProtocolVersion, MatchId = new byte[16], Session = new byte[16], PlayerId = 1,
                TickRate = 20, Map = new MapBlob(MapTiles, MapTiles, new byte[MapTiles * MapTiles])
            });
            Snapshot();

            _clientObject = new GameObject("Minimap return client");
            _client = _clientObject.AddComponent<MatchClient>();
            _client.enabled = false;
            _client.Url = "ws://localhost/minimap-return"; _client.Profile = "fixture";
            typeof(MatchClient).GetField("<Session>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(_client, _session);
            _arenaObject = new GameObject("Minimap return arena");
            _arenaObject.transform.SetParent(_clientObject.transform, false);
            _client.Arena = _arenaObject.AddComponent<ArenaView>();
            _cameraObject = new GameObject("Minimap return camera", typeof(Camera));
            _cameraObject.transform.SetParent(_clientObject.transform, false);
            _client.Cam = _cameraObject.GetComponent<Camera>();

            _canvasObject = new GameObject("Minimap return canvas", typeof(RectTransform), typeof(Canvas), typeof(GraphicRaycaster));
            _canvasObject.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
            ((RectTransform)_canvasObject.transform).sizeDelta = new Vector2(1000, 800);
            _mapObject = new GameObject("Minimap return controller");
            _map = _mapObject.AddComponent<TacticalMapController>();
            _map.Initialize(_client, _canvasObject.transform);
            _priorToggle = _map.ToggleMode;
            _map.SetToggleMode(false);
            _map.Bind(_session);
            _mini = _canvasObject.transform.Find("Minimap/TerrainFrame").GetComponent<TacticalMapSurface>();
            Assert.That(_session.State, Is.EqualTo(SessionState.InMatch), "set-up");
            Assert.That(_session.Motion.Initialized, Is.True, "set-up");
            Frame(); Frame();
            Assert.That(_mini.gameObject.activeInHierarchy, Is.True, "set-up: the minimap is on screen");
        }

        [TearDown]
        public void TearDown()
        {
            try
            {
                if (_map != null) { _map.SetToggleMode(_priorToggle); _map.Bind(null); }
                if (_session != null) _session.Leave();
            }
            finally
            {
                TacticalMapController.SecondsClock = _priorClock;
                WorldLabelVisibility.Hidden = false;
                if (_keyboard != null && _keyboard.added) InputSystem.RemoveDevice(_keyboard);
                if (_mouse != null && _mouse.added) InputSystem.RemoveDevice(_mouse);
                if (_mapObject != null) UnityEngine.Object.DestroyImmediate(_mapObject);
                if (_canvasObject != null) UnityEngine.Object.DestroyImmediate(_canvasObject);
                if (_clientObject != null) UnityEngine.Object.DestroyImmediate(_clientObject);
            }
        }

        // ---- the tests ---------------------------------------------------------------------------------------------------------

        [Test]
        public void TheMinimapIsCurrentOnTheCallThatShowsIt()
        {
            _map.SetMinimapSuppressed(true);
            _actors = new[] { Contact() };
            Snapshot(); _map.Tick();
            Assert.That(MiniMarks().ContactCount, Is.EqualTo(0), "set-up: the hidden minimap was not painted with the new contact");
            Assert.That(_map.MinimapRoot.gameObject.activeSelf, Is.False, "set-up: the minimap is hidden");

            _map.SetMinimapSuppressed(false);   // no Tick after it: this is RefreshWorldHudVisibility after TickFeatures
            Assert.That(MiniMarks().ContactCount, Is.EqualTo(1), "the minimap shows the contact on the call that shows it, not one Tick later");
        }

        [Test]
        public void TheSameHoldsWhenTheTabMapCloses()
        {
            _map.SetOpen(true); _map.Tick();
            Assert.That(_map.IsOpen, Is.True, "set-up: the Tab map is open");
            _map.SetMinimapSuppressed(true);   // RefreshWorldHudVisibility, after that Tick
            int atSuppress = _map.MinimapPresentCount;
            _actors = new[] { Contact() };
            Snapshot(); _map.Tick();   // the next frame's start
            _map.SetOpen(false);       // the key closes the map; the minimap is still suppressed
            Assert.That(_map.IsOpen, Is.False, "set-up: the Tab map is closed");
            Assert.That(_map.MinimapRoot.gameObject.activeSelf, Is.False, "the minimap stays hidden while suppressed");
            _map.Tick();               // TickFeatures runs the map first
            Assert.That(_map.MinimapRoot.gameObject.activeSelf, Is.False, "still hidden after the map's Tick");
            Assert.That(_map.MinimapPresentCount, Is.EqualTo(atSuppress), "a hidden minimap is not presented");

            _map.SetMinimapSuppressed(false);   // RefreshWorldHudVisibility; no Tick after it
            Assert.That(_map.MinimapRoot.gameObject.activeSelf, Is.True, "the minimap is back");
            Assert.That(MiniMarks().ContactCount, Is.EqualTo(1), "and it is current on that frame");
            Assert.That(_map.MinimapPresentCount, Is.EqualTo(atSuppress + 1), "presented exactly once by the call that shows it");
        }

        [Test]
        public void TheOwnMarkerIsPlacedToo()
        {
            var motion = _session.Motion;
            _map.SetMinimapSuppressed(true);
            motion.OnSnapshot(new WireSelf { Id = 1, X = 260, Y = 90, Hp = 100, MaxHp = 100, Status = 1, DisplaceSeq = 1 }, 1000);
            Assert.That(motion.DrawnX, Is.EqualTo(260f).Within(.01f), "set-up: the drawn position moved");
            _map.Tick();
            _map.SetMinimapSuppressed(false);   // no Tick after it
            var rect = _mini.Marks.rectTransform.rect;
            Assert.That(rect.width, Is.GreaterThan(1f), "set-up: the surface has a size");
            var expected = new Vector2(rect.xMin + motion.DrawnX / (MapTiles * 32f) * rect.width, rect.yMax - motion.DrawnY / (MapTiles * 32f) * rect.height);
            var marker = MiniMarks().SelfMarkerRect;
            Assert.That(marker.HasValue, Is.True, "the minimap has an own marker");
            Assert.That(marker.Value.center.x, Is.EqualTo(expected.x).Within(.5f), "the own marker is on the drawn x as the minimap returns");
            Assert.That(marker.Value.center.y, Is.EqualTo(expected.y).Within(.5f), "the own marker is on the drawn y as the minimap returns");
        }

        [Test]
        public void AHiddenMinimapIsStillNotPresented()
        {
            _map.SetMinimapSuppressed(true);
            int c0 = _map.MinimapPresentCount;
            for (int i = 0; i < 10; i++) Frame();
            Assert.That(_map.MinimapPresentCount, Is.EqualTo(c0), "ten frames with the minimap hidden do not present it");
            _map.SetMinimapSuppressed(false);
            Assert.That(_map.MinimapPresentCount, Is.EqualTo(c0 + 1), "the call that shows it presents once");
            Frame();
            Assert.That(_map.MinimapPresentCount, Is.EqualTo(c0 + 2), "the next frame repaints once more");
            _map.SetMinimapSuppressed(false);
            Assert.That(_map.MinimapPresentCount, Is.EqualTo(c0 + 2), "showing it twice in a row paints once");
        }

        [Test]
        public void ShowingWithoutAMapDoesNothing()
        {
            int c0 = _map.MinimapPresentCount;   // the SetUp frames painted the minimap already
            // (a) nothing bound
            _map.Bind(null);
            Assert.DoesNotThrow(() => { _map.SetMinimapSuppressed(true); _map.SetMinimapSuppressed(false); _map.Tick(); }, "no session");
            Assert.That(_map.MinimapPresentCount, Is.EqualTo(c0), "nothing is presented without a session");
            Assert.That(_map.MinimapRoot.gameObject.activeSelf, Is.False, "no session, no minimap");

            // (b) a session that is open but has had no Welcome: no map
            var transport = new RecordingTransport();
            var bare = new Session(transport, new SessionOptions { Url = "ws://localhost/minimap-return-bare", Profile = "fixture", Now = () => 1000 });
            try
            {
                bare.Connect();
                transport.Open();
                Assert.That(bare.Map, Is.Null, "set-up: no map without a Welcome");
                Assert.That(bare.State, Is.Not.EqualTo(SessionState.InMatch), "set-up: not in a match");
                _map.Bind(bare);
                Assert.DoesNotThrow(() => { _map.SetMinimapSuppressed(true); _map.SetMinimapSuppressed(false); _map.Tick(); }, "a session without a map");
                Assert.That(_map.MinimapPresentCount, Is.EqualTo(c0), "nothing is presented without a map");
                Assert.That(_map.MinimapRoot.gameObject.activeSelf, Is.False, "no map, no minimap");
            }
            finally { bare.Leave(); }
        }

        // ---- helpers -----------------------------------------------------------------------------------------------------------

        /// <summary>One player-loop frame: time advances, a snapshot arrives, the HUD reads input, the map ticks, input flushes.</summary>
        void Frame(double dtMs = 16.7)
        {
            _now += dtMs / 1000d;
            Snapshot();
            _map.ReadUiInput(false, false);
            _map.Tick();
            _session.FlushInput();
        }

        void Snapshot()
        {
            _tick++;
            _transport.Deliver(new ServerMsg.Snapshot
            {
                Tick = _tick, Phase = 1, Actors = _actors,
                Me = new WireSelf { Id = 1, X = 48, Y = 48, Hp = 100, MaxHp = 100, Status = 1 }
            });
        }

        TacticalMapMarks MiniMarks() => _mini.Marks;
    }
}
