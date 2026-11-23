using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using Roguehalla.Net;
using Roguehalla.Net.Protocol;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;

namespace Roguehalla.Client.Tests
{
    /// <summary>
    /// P-UI-PLAYER-WORDS-2, HUD words: the pickup prompt and tip name the prop, the build tip names the boosts, and the banner, plate status
    /// and result note speak in words for the connection state. The rig is the full rig of PromptPairNarrowEditModeTests (a whole MatchHud on
    /// a real MatchClient over a fake transport). Nothing here reads a clock and no pickup cue is sent.
    /// </summary>
    public sealed class HudWordsEditModeTests
    {
        const BindingFlags All = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        sealed class Transport : ITransport
        {
            public event Action Opened;
            public event Action<byte[]> Received;
            public event Action<string> Closed;
            public void Connect(string url) { }
            public void Send(byte[] frame) { }
            public void Close() => Closed?.Invoke("closed");
            public void Poll() { }
            public void Open() => Opened?.Invoke();
            public void Deliver(ServerMsg message) => Received?.Invoke(message.Encode());
            public void Drop() => Closed?.Invoke("fixture disconnect");
        }

        GameObject _fullRoot, _events;
        EventSystem _priorEvents;
        MatchClient _client;
        MatchHud _hud;
        Session _session;
        Transport _transport;
        UiTheme _savedTheme;

        /// <summary>The full rig. With welcome false it stops after the transport opens: no Welcome, the session is in HelloSent.</summary>
        void BuildFullRig(bool welcome = true)
        {
            _savedTheme = UiTheme.Current;
            UiTheme.Current = UiTheme.Placeholder();
            _priorEvents = EventSystem.current;
            _events = new GameObject("Hud words events", typeof(UiInputRoutingEventSystem));
            var system = _events.GetComponent<UiInputRoutingEventSystem>(); system.enabled = false; system.enabled = true;
            EventSystem.current = system;
            _fullRoot = new GameObject("Hud words fixture");
            _client = _fullRoot.AddComponent<MatchClient>(); _client.enabled = false;
            _transport = new Transport();
            _session = new Session(_transport, new SessionOptions { Url = "ws://words/ws", Profile = "words", Now = () => 1000 });
            Call(_client, "InitializeLayoutReview", _session); _hud = _client.Hud;
            _session.Connect(); _transport.Open();
            if (!welcome) return;
            _transport.Deliver(new ServerMsg.Welcome { Protocol = Wire.ProtocolVersion, MatchId = new byte[16], Session = new byte[16],
                PlayerId = 1, TickRate = 20, ServerMs = 50, Map = new MapBlob(32, 32, new byte[1024]) });
            Snap(1, 50);
        }

        void Snap(uint tick, double serverMs, byte phase = (byte)MatchPhase.Active) =>
            _transport.Deliver(new ServerMsg.Snapshot { Tick = tick, ServerTimeOverrideMs = serverMs, Phase = phase,
                Me = new WireSelf { Id = 1, X = 64, Y = 64, Hp = 100, MaxHp = 100, Status = 1, Weapon = 0 } });

        void Items(uint tick, params StateItem[] items) => _transport.Deliver(new ServerMsg.State { Tick = tick, Items = items });

        /// <summary>One frame of the HUD's late update; the first call of a session is dirty (Bind sets it), later ones are set dirty here.</summary>
        void Frame() { SetF(_hud, "_dirty", true); Call(_hud, "LateUpdate"); Call(_hud, "TickPlayerFlows"); }

        [TearDown]
        public void TearDown()
        {
            if (_fullRoot != null) UnityEngine.Object.DestroyImmediate(_fullRoot);
            if (_events != null) UnityEngine.Object.DestroyImmediate(_events);
            if (_priorEvents != null) EventSystem.current = _priorEvents;
            if (_savedTheme != null) UiTheme.Current = _savedTheme;
        }

        static object GetF(object o, string name)
        {
            var f = o.GetType().GetField(name, All);
            Assert.That(f, Is.Not.Null, "missing field " + name);
            return f.GetValue(o);
        }
        static void SetF(object o, string name, object value)
        {
            var f = o.GetType().GetField(name, All);
            Assert.That(f, Is.Not.Null, "missing field " + name);
            f.SetValue(o, value);
        }
        static object Call(object o, string name, params object[] args)
        {
            var m = o.GetType().GetMethod(name, All);
            Assert.That(m, Is.Not.Null, "missing method " + name);
            return m.Invoke(o, args.Length == 0 ? null : args);
        }

        TMP_Text Banner => (TMP_Text)GetF(_hud, "_banner");
        TMP_Text PlateStatus => (TMP_Text)GetF(_hud, "_plateStatus");
        TMP_Text ResultNote => (TMP_Text)GetF(_hud, "_resultNote");

        // ------------------------------------------------------------------ the pickup prompt and tip

        [TestCase(PropKind.Chest, "Chest")]
        [TestCase(PropKind.Body, "Body")]
        [TestCase(PropKind.Corpse, "Corpse")]
        [TestCase(PropKind.WindEmblem, "Wind emblem")]
        [TestCase(PropKind.Cannon, "Travel cannon")]
        [TestCase(PropKind.Ground, "Axe")]
        public void ThePickupPromptNamesTheProp(PropKind kind, string word)
        {
            BuildFullRig();
            var row = new WireProp { Handle = new PickupHandle(5), X = 300, Y = 300, Kind = kind };
            if (kind == PropKind.Ground) row.GroundWeapon = (byte)WeaponId.Axe;
            if (kind == PropKind.Body) row.BodyLabel = "Ash";
            if (kind == PropKind.Corpse) row.CorpseLabel = "Rat";
            Snap(10, 100);
            Items(10, new PropsState { Rows = new[] { row }, Focus = new WireFocus { Handle = new PickupHandle(5) } });
            Frame();
            Assert.That(_session.Collections.Focus.HasValue, Is.True, "set-up: the prop is in reach");
            Assert.That(((TMP_Text)GetF(_hud, "_pickupText")).text, Is.EqualTo("F  take " + word));
            var hint = ((HudIconHint)GetF(_hud, "_pickupHint")).Text;
            Assert.That(hint, Does.EndWith(word));
            Assert.That(hint, Does.StartWith("F · Pick up "));
        }

        // ------------------------------------------------------------------ the build tip

        [Test]
        public void TheBuildTipNamesTheBoosts()
        {
            BuildFullRig();
            var boosts = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 };
            Snap(10, 100);
            Items(10, new BuildState { Value = new WireBuild { Cards = new byte[80], Boosts = boosts } });
            Frame();
            var tip = ((HudIconHint)GetF(_hud, "_buildHint")).Text;
            Assert.That(tip, Does.StartWith("B · Full build details\nBUILD  "), "set-up: the tip is delivered");
            foreach (var entry in new[] { "Offense +1", "Soul Offence +2", "Defense +3", "Soul Defense +4", "Max health +5", "Attack speed +6", "Move speed +7", "Lifesteal +8" })
                Assert.That(tip, Does.Contain(entry));
            foreach (var code in new[] { "MaxHp", "AttackSpeed", "MoveSpeed", "Ad+", "Ap+", "Armor+", "Shield+" })
                Assert.That(tip, Does.Not.Contain(code), "the code name " + code);
        }

        // ------------------------------------------------------------------ the banner

        [Test]
        public void TheBannerSpeaksInWords()
        {
            BuildFullRig(welcome: false);
            Assert.That(_session.State, Is.EqualTo(SessionState.HelloSent), "set-up: opened, no Welcome yet");
            Frame();
            Assert.That(Banner.text, Is.EqualTo("Connecting…"), "banner while connecting");
            Assert.That(PlateStatus.text, Is.EqualTo("Connecting…"), "plate status while connecting");

            _transport.Deliver(new ServerMsg.Welcome { Protocol = Wire.ProtocolVersion, MatchId = new byte[16], Session = new byte[16],
                PlayerId = 1, TickRate = 20, ServerMs = 50, Map = new MapBlob(32, 32, new byte[1024]) });
            Snap(1, 50);
            Assert.That(_session.State, Is.EqualTo(SessionState.InMatch), "set-up: in the match");

            _transport.Deliver(new ServerMsg.Events { Tick = 2, List = new List<WireEvent> { new WireEvent { Kind = EventKind.MatchEnded } } });
            Assert.That(_session.State, Is.EqualTo(SessionState.Ended), "set-up: the match has ended");
            Frame();
            Assert.That(Banner.text, Is.EqualTo("The match has ended."), "banner after the match");
            Assert.That(PlateStatus.text, Is.EqualTo("The match has ended."), "plate status after the match");
            Assert.That(ResultNote.text, Is.EqualTo("The match has ended.\nNo authoritative result received."), "the result note");

            _session.Detach();
            Frame();
            Assert.That(_session.State, Is.EqualTo(SessionState.Detached), "set-up: detached");
            Assert.That(Banner.text, Is.EqualTo("Disconnected"), "banner after detach");
            Assert.That(PlateStatus.text, Is.EqualTo("Disconnected"), "plate status after detach");
        }

        [Test]
        public void GUARD_ReconnectingKeepsItsBanner()
        {
            BuildFullRig();
            Frame();
            Assert.That(_session.State, Is.EqualTo(SessionState.InMatch), "set-up: in the match");
            _transport.Drop();
            Assert.That(_session.State, Is.EqualTo(SessionState.Reconnecting), "set-up: the link dropped");
            Frame();
            Assert.That(Banner.text, Does.StartWith("Reconnecting… ("));
        }
    }
}
