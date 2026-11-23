using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using Roguehalla.Net;
using Roguehalla.Net.Protocol;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace Roguehalla.Client.Tests
{
    /// <summary>
    /// P-UI-PLAYER-WORDS-1: the Tab map's IN VIEW list reads in the game's words ("Haunted · creatures x3", never the code name
    /// "RegionalMob"), its group rows keep a fixed order whatever actor ids enter or leave, and an unchanged snapshot rebuilds nothing.
    /// The real TacticalMapController on a fake transport, as <see cref="TabMapFlowEditModeTests"/> builds it; the clock is the
    /// controller's <see cref="TacticalMapController.SecondsClock"/> seam.
    /// </summary>
    public sealed class ContactRailWordsEditModeTests
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
        Session _session;
        RecordingTransport _transport;
        Keyboard _keyboard;
        Mouse _mouse;
        Func<double> _priorClock;
        bool _priorToggle;
        double _now = 100d;
        uint _tick = 1;
        WireActor[] _actors = new WireActor[0];

        const byte Haunted = 1, Plague = 2;

        static WireActor Actor(uint id, byte faction, ActorKind kind) =>
            new WireActor { Id = id, X = 80, Y = 80, Faction = faction, HpPct = 100, Status = 1, Kind = (byte)kind };

        static WireActor Actor(uint id, byte faction, byte kind) =>
            new WireActor { Id = id, X = 80, Y = 80, Faction = faction, HpPct = 100, Status = 1, Kind = kind };

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
            _session = new Session(_transport, new SessionOptions { Url = "ws://localhost/contact-words", Profile = "fixture", Now = () => 1000 });
            _session.Connect();
            _transport.Open();
            _transport.Deliver(new ServerMsg.Welcome
            {
                Protocol = Wire.ProtocolVersion, MatchId = new byte[16], Session = new byte[16], PlayerId = 1,
                TickRate = 20, Map = new MapBlob(MapTiles, MapTiles, new byte[MapTiles * MapTiles])
            });
            Snapshot();

            _clientObject = new GameObject("Contact words client");
            _client = _clientObject.AddComponent<MatchClient>();
            _client.enabled = false;
            _client.Url = "ws://localhost/contact-words"; _client.Profile = "fixture";
            typeof(MatchClient).GetField("<Session>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(_client, _session);
            _arenaObject = new GameObject("Contact words arena");
            _arenaObject.transform.SetParent(_clientObject.transform, false);
            _client.Arena = _arenaObject.AddComponent<ArenaView>();
            _cameraObject = new GameObject("Contact words camera", typeof(Camera));
            _cameraObject.transform.SetParent(_clientObject.transform, false);
            _client.Cam = _cameraObject.GetComponent<Camera>();

            _canvasObject = new GameObject("Contact words canvas", typeof(RectTransform), typeof(Canvas), typeof(GraphicRaycaster));
            _canvasObject.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
            ((RectTransform)_canvasObject.transform).sizeDelta = new Vector2(1000, 800);
            _mapObject = new GameObject("Contact words controller");
            _map = _mapObject.AddComponent<TacticalMapController>();
            _map.Initialize(_client, _canvasObject.transform);
            _priorToggle = _map.ToggleMode;
            _map.SetToggleMode(false);
            _map.Bind(_session);
            Assert.That(_session.State, Is.EqualTo(SessionState.InMatch), "set-up");
            Assert.That(_session.Motion.Initialized, Is.True, "set-up");
            Frame(); Frame();
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

        [Test]
        public void CreaturesReadAsCreaturesNotAsTheCodeName()
        {
            CreaturesSetUp();
            Assert.That(ContactsText(), Is.EqualTo("Haunted · creatures x3\nPlague · creatures x2"));
            StringAssert.DoesNotContain("RegionalMob", ContactsText());
        }

        [Test]
        public void EveryKindGetsItsOwnWordsInAFixedOrder()
        {
            var giver = Actor(5, 15, ActorKind.QuestGiver);
            giver.Weapon = 15;   // the wire's absent identity; the encoder refuses a giver without it
            _actors = new[]
            {
                Actor(2, Haunted, ActorKind.RegionalMob), Actor(3, Plague, ActorKind.Player), Actor(4, Haunted, ActorKind.Zombie),
                giver, Actor(6, Haunted, ActorKind.RegionalMob), Actor(7, Haunted, 5), Actor(8, Haunted, 6), Actor(9, Plague, ActorKind.RegionalMob)
            };
            Frame(); Frame();
            _map.SetOpen(true);
            Frame();
            Assert.That(_map.IsOpen, Is.True, "set-up: the Tab map is open");
            Assert.That(ContactsText(), Is.EqualTo(
                "Plague · Player\nQuest giver x1\nHaunted · creatures x2\nHaunted · zombie x1\nHaunted · contacts x2\nPlague · creature x1"));
        }

        [Test]
        public void AnUnchangedSnapshotAllocatesNothing()
        {
            CreaturesSetUp();
            var method = typeof(TacticalMapController).GetMethod("PresentTactics", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(method, Is.Not.Null, "set-up: the controller has a PresentTactics method");
            var present = (Action)Delegate.CreateDelegate(typeof(Action), _map, method);
            Assert.That(ContactsText(), Is.EqualTo("Haunted · creatures x3\nPlague · creatures x2"));
            for (int i = 0; i < 5; i++) present();   // warm up
            AllocationMeter.AssertNone(present, 200, "PresentTactics with no new snapshot");
            Assert.That(ContactsText(), Is.EqualTo("Haunted · creatures x3\nPlague · creatures x2"), "the text is unchanged");
        }

        [Test]
        public void ARowKeepsItsPlaceWhenActorIdsChange()
        {
            Frame(); Frame();
            _map.SetOpen(true);
            Frame();
            Assert.That(_map.IsOpen, Is.True, "set-up: the Tab map is open");
            _actors = new[] { Actor(3, Plague, ActorKind.RegionalMob), Actor(4, Haunted, ActorKind.RegionalMob), Actor(5, Haunted, ActorKind.RegionalMob) };
            Frame();
            var first = ContactsText();
            Assert.That(first, Is.EqualTo("Haunted · creatures x2\nPlague · creature x1"));
            _actors = new[] { Actor(2, Plague, ActorKind.RegionalMob), Actor(5, Haunted, ActorKind.RegionalMob), Actor(6, Haunted, ActorKind.RegionalMob) };
            Frame();
            Assert.That(ContactsText(), Is.EqualTo("Haunted · creatures x2\nPlague · creature x1"));
            Assert.That(ContactsText(), Is.EqualTo(first), "the same text after the ids changed");
        }

        // ---- helpers -----------------------------------------------------------------------------------------------------------

        /// <summary>One player-loop frame: time advances, a snapshot arrives, the map ticks, input flushes.</summary>
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

        string ContactsText() => _canvasObject.transform.Find("TacticalMap/TacticalInformation/ContactsPage/DeliveredContacts/Viewport/Content").GetComponent<TMP_Text>().text;
    }
}
