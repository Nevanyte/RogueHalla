using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using Roguehalla.Net;
using Roguehalla.Net.Protocol;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.UI;

namespace Roguehalla.Client.Tests
{
    /// <summary>
    /// P-UI-TABMAP-FLOW-1: the Tab map steers with a held right button, nothing re-follows the camera while it is open, a closed
    /// surface is not presented, the map opens already painted, and the own marker follows the drawn position every frame.
    /// The real TacticalMapController on a recording transport, a real mouse for the held button and the controller's clock seam
    /// (<see cref="TacticalMapController.SecondsClock"/>) so the gesture cadence and the paint throttle are driven by the test.
    /// </summary>
    public sealed class TabMapFlowEditModeTests
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
        static readonly Vector2 A = new Vector2(100f, 100f), B = new Vector2(300f, 120f), C = new Vector2(400f, 400f);

        GameObject _clientObject, _mapObject, _canvasObject, _arenaObject, _cameraObject;
        MatchClient _client;
        TacticalMapController _map;
        TacticalMapSurface _mini, _large;
        Session _session;
        RecordingTransport _transport;
        Keyboard _keyboard;
        Mouse _mouse;
        Func<double> _priorClock;
        bool _priorToggle;
        double _now = 100d;
        uint _tick = 1;
        WireActor[] _actors = new WireActor[0];

        [SetUp]
        public void SetUp()
        {
            _keyboard = InputSystem.AddDevice<Keyboard>();
            _mouse = InputSystem.AddDevice<Mouse>();
            _priorClock = TacticalMapController.SecondsClock;
            TacticalMapController.SecondsClock = () => _now;
            WorldLabelVisibility.Hidden = false;

            _transport = new RecordingTransport();
            _session = new Session(_transport, new SessionOptions { Url = "ws://localhost/tabmap-flow", Profile = "fixture", Now = () => 1000 });
            _session.Connect();
            _transport.Open();
            _transport.Deliver(new ServerMsg.Welcome
            {
                Protocol = Wire.ProtocolVersion, MatchId = new byte[16], Session = new byte[16], PlayerId = 1,
                TickRate = 20, Map = new MapBlob(MapTiles, MapTiles, new byte[MapTiles * MapTiles])
            });
            Snapshot();

            _clientObject = new GameObject("Tab map flow client");
            _client = _clientObject.AddComponent<MatchClient>();
            _client.enabled = false;
            _client.Url = "ws://localhost/tabmap-flow"; _client.Profile = "fixture";
            typeof(MatchClient).GetField("<Session>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(_client, _session);
            _arenaObject = new GameObject("Tab map flow arena");
            _arenaObject.transform.SetParent(_clientObject.transform, false);
            _client.Arena = _arenaObject.AddComponent<ArenaView>();
            _cameraObject = new GameObject("Tab map flow camera", typeof(Camera));
            _cameraObject.transform.SetParent(_clientObject.transform, false);
            _client.Cam = _cameraObject.GetComponent<Camera>();

            _canvasObject = new GameObject("Tab map flow canvas", typeof(RectTransform), typeof(Canvas), typeof(GraphicRaycaster));
            _canvasObject.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
            ((RectTransform)_canvasObject.transform).sizeDelta = new Vector2(1000, 800);
            _mapObject = new GameObject("Tab map flow controller");
            _map = _mapObject.AddComponent<TacticalMapController>();
            _map.Initialize(_client, _canvasObject.transform);
            _priorToggle = _map.ToggleMode;
            _map.SetToggleMode(false);
            _map.Bind(_session);
            _mini = _canvasObject.transform.Find("Minimap/TerrainFrame").GetComponent<TacticalMapSurface>();
            _large = _canvasObject.transform.Find("TacticalMap/MapFrame/PublicMap").GetComponent<TacticalMapSurface>();
            Assert.That(_session.State, Is.EqualTo(SessionState.InMatch), "set-up");
            Assert.That(_session.Motion.Initialized, Is.True, "set-up");
            Frame(); Frame();
            Assert.That(_mini.gameObject.activeInHierarchy, Is.True, "set-up: the minimap is on screen");
            _transport.Take();
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
        public void HoldingRightOnTheBigMapSteers()
        {
            _map.SetOpen(true);
            Frame(); Frame();
            SetMouse(Mouse.current.position.ReadValue(), MouseButton.Right);
            _large.Pressed(A, PointerEventData.InputButton.Right);
            var press = TakeMoves();
            Assert.That(press.Count, Is.EqualTo(1), "the right press sends one move order");
            AssertAt(press[0], A, "the press");

            Frame(50); Frame(50);
            _large.Dragged(B, PointerEventData.InputButton.Right);
            var during = new List<WireCommand>();
            for (int i = 0; i < 4; i++) { Frame(50); during.AddRange(TakeMoves()); }
            Assert.That(during.Exists(m => Near(m, B)), Is.True,
                "holding right and dragging keeps sending moves toward the cursor (a point near B was sent during the drag; sent: " + Describe(during) + ")");

            _large.Dragged(C, PointerEventData.InputButton.Right);
            _large.PointerReleased(C, PointerEventData.InputButton.Right);
            SetMouse(Mouse.current.position.ReadValue());
            Frame();
            var final = TakeMoves();
            Assert.That(final.Count, Is.GreaterThan(0), "releasing sends the final point");
            AssertAt(final[final.Count - 1], C, "the release");
        }

        [Test]
        public void AMinimapGestureIsReleasedOnceWhenTheMinimapHides()
        {
            SetMouse(Mouse.current.position.ReadValue(), MouseButton.Right);
            _mini.Pressed(A, PointerEventData.InputButton.Right);
            Assert.That(TakeMoves().Count, Is.EqualTo(1), "set-up: the minimap press sends a move");
            Frame(50);
            _mini.Dragged(B, PointerEventData.InputButton.Right);
            var onMini = new List<WireCommand>();
            for (int i = 0; i < 4; i++) { Frame(50); onMini.AddRange(TakeMoves()); }
            Assert.That(onMini.Exists(m => Near(m, B)), Is.True, "set-up: the minimap steers while it is on screen");

            _map.SetOpen(true);
            Frame();
            TakeMoves();
            _mini.Dragged(C, PointerEventData.InputButton.Right);
            for (int i = 0; i < 5; i++) Frame(50);
            Assert.That(TakeMoves(), Is.Empty, "the minimap's gesture ended when the minimap hid; it does not resume");

            _large.Pressed(A, PointerEventData.InputButton.Right);
            Assert.That(TakeMoves().Count, Is.EqualTo(1), "a right press on the big map sends a move");
            Frame(50);
            _large.Dragged(B, PointerEventData.InputButton.Right);
            var onLarge = new List<WireCommand>();
            for (int i = 0; i < 4; i++) { Frame(50); onLarge.AddRange(TakeMoves()); }
            Assert.That(onLarge.Exists(m => Near(m, B)),
                Is.True, "the big map's gesture is not touched by later frames (sent: " + Describe(onLarge) + ")");

            _map.SetOpen(false);
            Frame();
            TakeMoves();
            _large.Dragged(C, PointerEventData.InputButton.Right);
            _mini.Dragged(C, PointerEventData.InputButton.Right);
            for (int i = 0; i < 5; i++) Frame(50);
            Assert.That(TakeMoves(), Is.Empty, "closing the big map ends its gesture, and the minimap's stays ended");
        }

        [Test]
        public void TheOpenMapDoesNotMoveTheCamera()
        {
            _map.SetOpen(true);
            Frame(); Frame();
            Assert.That(_map.IsOpen, Is.True);
            Assert.That(WorldLabelVisibility.Hidden, Is.True, "set-up: an open map hides the world names");
            var elsewhere = new Vector3(500f, 500f, -30f);
            _client.Cam.transform.position = elsewhere;
            for (int i = 0; i < 10; i++) Frame();
            Assert.That(_client.Cam.transform.position, Is.EqualTo(elsewhere), "with the map open and no peek the controller never moves the camera");
        }

        [Test]
        public void EndingAMinimapPeekRestoresTheCameraOnce()
        {
            var peekAt = new Vector2(300f, 300f);
            // A left-hold peek ended by releasing the button.
            SetMouse(Mouse.current.position.ReadValue(), MouseButton.Left);
            _mini.Pressed(peekAt, PointerEventData.InputButton.Left);
            Assert.That(_map.IsPeeking, Is.True, "set-up: the left press begins a peek");
            _client.Arena.Follow(_client.Cam, 110, 100);
            var peekCamera = _client.Cam.transform.position;
            Frame();
            Assert.That(_client.Cam.transform.position, Is.EqualTo(peekCamera), "a peek in progress keeps the peeked camera");
            SetMouse(Mouse.current.position.ReadValue());
            _mini.PointerReleased(peekAt, PointerEventData.InputButton.Left);
            var owner = OwnerCamera();
            Assert.That(peekCamera, Is.Not.EqualTo(owner), "set-up: the peek moved the camera");
            Assert.That(_client.Cam.transform.position, Is.EqualTo(owner), "ending the peek returns the camera to the player");
            var elsewhere = new Vector3(500f, 500f, -30f);
            _client.Cam.transform.position = elsewhere;
            for (int i = 0; i < 10; i++) Frame();
            Assert.That(_client.Cam.transform.position, Is.EqualTo(elsewhere), "later frames do not move the camera again");

            // A peek ended by opening the Tab map.
            SetMouse(Mouse.current.position.ReadValue(), MouseButton.Left);
            _mini.Pressed(peekAt, PointerEventData.InputButton.Left);
            Assert.That(_map.IsPeeking, Is.True);
            _client.Arena.Follow(_client.Cam, 110, 100);
            _map.SetOpen(true);
            Assert.That(_map.IsPeeking, Is.False, "opening the Tab map ends the peek");
            Assert.That(_client.Cam.transform.position, Is.EqualTo(owner), "and returns the camera to the player");
            _client.Cam.transform.position = elsewhere;
            for (int i = 0; i < 10; i++) Frame();
            Assert.That(_client.Cam.transform.position, Is.EqualTo(elsewhere), "the open map does not re-follow the player every frame afterwards");
        }

        [Test]
        public void AClosedMapIsNotPresented()
        {
            for (int i = 0; i < 4; i++) Frame();
            int large0 = _map.LargePresentCount, mini0 = _map.MinimapPresentCount;
            for (int i = 0; i < 60; i++) Frame();
            Assert.That(_map.LargePresentCount, Is.EqualTo(large0), "sixty frames with the map closed do not present the big surface");
            Assert.That(_map.MinimapPresentCount, Is.GreaterThan(mini0), "the minimap on screen keeps its paint");
            StringAssert.Contains("stops", RouteText(), "the route plan keeps working while the map is closed");

            _map.SetOpen(true);
            for (int i = 0; i < 4; i++) Frame();
            int large1 = _map.LargePresentCount, mini1 = _map.MinimapPresentCount;
            for (int i = 0; i < 60; i++) Frame();
            Assert.That(_map.MinimapPresentCount, Is.EqualTo(mini1), "with the map open the hidden minimap is not presented");
            Assert.That(_map.LargePresentCount, Is.GreaterThan(large1), "the open map keeps its paint");

            _map.SetOpen(false);
            for (int i = 0; i < 4; i++) Frame();
            _transport.Deliver(new ServerMsg.State { Tick = ++_tick, Items = new StateItem[] { new PlanState { Value = new WirePlan { EndsAt = 99999, Boons = new string[0] } } } });
            for (int i = 0; i < 4; i++) Frame();
            Assert.That(_map.IsPlanning, Is.True, "set-up: the planning screen");
            Assert.That(_map.IsOpen, Is.True, "set-up: the planning screen shows the map panel");
            int large2 = _map.LargePresentCount, mini2 = _map.MinimapPresentCount;
            for (int i = 0; i < 60; i++) Frame();
            Assert.That(_map.LargePresentCount, Is.GreaterThan(large2), "the planning screen keeps presenting its map");
            Assert.That(_map.MinimapPresentCount, Is.EqualTo(mini2), "and the minimap stays idle behind it");
        }

        [Test]
        public void TheMapIsPaintedOnTheFrameItOpens()
        {
            _map.SetOpen(true);
            Frame(); Frame();
            StringAssert.Contains("release Tab", HintText(), "set-up: the hold-mode hint of the first opening");
            _map.SetOpen(false);
            Frame(); Frame();
            _map.SetToggleMode(true);   // an input of the hint changes while the map is closed
            Frame();
            _actors = new[] { new WireActor { Id = 3, X = 80, Y = 80, Faction = 2, HpPct = 100, Status = 1 } };
            Frame(17);     // a new contact is delivered while closed; this frame consumes the paint
            Frame(5, false);
            Assert.That(_map.IsOpen, Is.False, "set-up: still closed");

            _map.SetOpen(true);
            Frame(5, false);   // the opening frame: less than a paint interval after the last paint, no new snapshot
            Assert.That(_map.IsOpen, Is.True);
            Assert.That(LargeMarks().ContactCount, Is.EqualTo(1), "the new contact is on the map on the frame it opens");
            StringAssert.Contains("Plague · Player", ContactsText(), "the contacts rail shows the new contact on the first frame");
            StringAssert.DoesNotContain("No other contacts", ContactsText());
            Assert.That(TitleText(), Is.Not.Empty, "the title is written");
            StringAssert.Contains("Tab closes", HintText(), "the hint is the current text on the first frame, not the previous opening's");
            StringAssert.DoesNotContain("release Tab", HintText());
        }

        [Test]
        public void TheOwnMarkerFollowsTheDrawnPositionEveryFrame()
        {
            var motion = _session.Motion;
            _map.SetOpen(true);
            Frame(40); Frame(40);
            AssertMarkerFollowsDrawn(LargeMarks(), motion, 300f, 200f, 1, "the Tab map's own marker");
            AssertMarkerFollowsDrawn(LargeMarks(), motion, 120f, 330f, 2, "the Tab map's own marker, again the next frame");

            _map.SetOpen(false);
            Frame(40); Frame(40);
            AssertMarkerFollowsDrawn(MiniMarks(), motion, 260f, 90f, 3, "the minimap's own marker");
        }

        [Test]
        public void ReleaseTransientEndsAHeldBigMapGesture()
        {
            _map.SetOpen(true);
            Frame(); Frame();
            SetMouse(Mouse.current.position.ReadValue(), MouseButton.Right);
            _large.Pressed(A, PointerEventData.InputButton.Right);
            TakeMoves();
            Frame(50);
            _large.Dragged(B, PointerEventData.InputButton.Right);
            var before = new List<WireCommand>();
            for (int i = 0; i < 4; i++) { Frame(50); before.AddRange(TakeMoves()); }
            Assert.That(before.Exists(m => Near(m, B)), Is.True, "set-up: the held gesture is alive on the big map (sent: " + Describe(before) + ")");

            _map.ReleaseTransient();
            Assert.That(WorldLabelVisibility.Hidden, Is.False, "ReleaseTransient shows the world names again");
            Assert.That(_map.IsOpen, Is.False);
            _large.Dragged(C, PointerEventData.InputButton.Right);
            for (int i = 0; i < 5; i++) Frame(50);
            Assert.That(TakeMoves(), Is.Empty, "ReleaseTransient ended the held gesture: nothing more is sent");
        }

        // ---- helpers -----------------------------------------------------------------------------------------------------------

        /// <summary>One player-loop frame: time advances, a snapshot arrives (unless told not to), the HUD reads input, the map ticks, input flushes.</summary>
        void Frame(double dtMs = 16.7, bool snapshot = true)
        {
            _now += dtMs / 1000d;
            if (snapshot) Snapshot();
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

        Vector3 OwnerCamera() => ArenaView.WorldPoint(_session.Motion.DrawnX, _session.Motion.DrawnY) + Vector3.up * 1.5f + Vector3.back * 30;

        static bool Near(WireCommand command, Vector2 point) => Mathf.Abs(command.X - point.x) < .01f && Mathf.Abs(command.Y - point.y) < .01f;

        static void AssertAt(WireCommand command, Vector2 point, string what)
        {
            Assert.That(command.Kind, Is.EqualTo(CommandKind.Move), what);
            Assert.That(command.X, Is.EqualTo(point.x).Within(.01f), what + " x");
            Assert.That(command.Y, Is.EqualTo(point.y).Within(.01f), what + " y");
        }

        static string Describe(List<WireCommand> moves)
        {
            var parts = new List<string>();
            foreach (var m in moves) parts.Add("(" + m.X + "," + m.Y + ")");
            return parts.Count == 0 ? "nothing" : string.Join(" ", parts);
        }

        /// <summary>Moves the presented position by a displacement (a hard cut, so the drawn point lands on it), runs a frame with no new
        /// snapshot, less than a paint interval after the last paint, and checks the own marker sits on the drawn point.</summary>
        void AssertMarkerFollowsDrawn(TacticalMapMarks marks, LocalMotion motion, float x, float y, int displaceSeq, string what)
        {
            var before = marks.SelfMarkerRect;
            Assert.That(before.HasValue, Is.True, what + ": has a position");
            motion.OnSnapshot(new WireSelf { Id = 1, X = x, Y = y, Hp = 100, MaxHp = 100, Status = 1, DisplaceSeq = (byte)displaceSeq }, 1000);
            Assert.That(motion.DrawnX, Is.EqualTo(x).Within(.01f), "set-up: the drawn position moved");
            Frame(5, false);
            var rect = marks.rectTransform.rect;
            Assert.That(rect.width, Is.GreaterThan(1f), "set-up: the surface has a size");
            var expected = new Vector2(rect.xMin + motion.DrawnX / (MapTiles * 32f) * rect.width, rect.yMax - motion.DrawnY / (MapTiles * 32f) * rect.height);
            var now = marks.SelfMarkerRect;
            Assert.That(now.HasValue, Is.True);
            Assert.That(now.Value.center.x, Is.EqualTo(expected.x).Within(.5f), what + " follows the drawn x");
            Assert.That(now.Value.center.y, Is.EqualTo(expected.y).Within(.5f), what + " follows the drawn y");
        }

        TacticalMapMarks LargeMarks() => _large.Marks;
        TacticalMapMarks MiniMarks() => _mini.Marks;
        string RouteText() => _canvasObject.transform.Find("TacticalMap/RouteSummary").GetComponent<TMP_Text>().text;
        string HintText() => _canvasObject.transform.Find("TacticalMap/MapTitle/MapHint").GetComponent<TMP_Text>().text;
        string TitleText() => _canvasObject.transform.Find("TacticalMap/MapTitle/MapHeading").GetComponent<TMP_Text>().text;
        string ContactsText() => _canvasObject.transform.Find("TacticalMap/TacticalInformation/ContactsPage/DeliveredContacts/Viewport/Content").GetComponent<TMP_Text>().text;
    }
}
