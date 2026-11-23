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
using UnityEngine.UI;

namespace Roguehalla.Client.Tests
{
    /// <summary>
    /// P-UI-CHARGE-TILES-1: a quest spell that holds a charge draws its Ready look (name, strong frame, no wedge, no number, the count as
    /// the badge) and a press of it pops; with no charge it cools and a press shows "Ready in n". The rig is the full one of
    /// AbilityFeedbackEditModeTests: a whole MatchHud on a real MatchClient over a fake transport, a press through the real path
    /// (Session.CollectSpellInput, then FlushInput). EditMode time does not advance: server time is the snapshot's, the HUD clock is
    /// MatchHud.HudLocalMs (fixed at 5000), so a pop never expires by itself and every pop assertion is a delta taken after ClearPulses.
    /// </summary>
    public sealed class ChargeTilesEditModeTests
    {
        const BindingFlags All = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        static readonly Vector2 Window = new Vector2(1280, 800);

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
        }

        GameObject _fullRoot, _events;
        EventSystem _priorEvents;
        MatchClient _client;
        MatchHud _hud;
        Session _session;
        Transport _transport;
        UiTheme _savedTheme;
        double _local, _serverMs;
        uint _tick;

        [SetUp]
        public void SetUp() { UiMotion.ReducedOverride = false; MatchHud.NoticeDtOverride = .05f; }

        [TearDown]
        public void TearDown()
        {
            UiMotion.ReducedOverride = null;
            MatchHud.NoticeDtOverride = null;
            Cleanup();
        }

        void Cleanup()
        {
            if (_fullRoot != null) UnityEngine.Object.DestroyImmediate(_fullRoot);
            if (_events != null) UnityEngine.Object.DestroyImmediate(_events);
            if (_priorEvents != null) EventSystem.current = _priorEvents;
            if (_savedTheme != null) UiTheme.Current = _savedTheme;
            _fullRoot = null; _events = null; _priorEvents = null; _savedTheme = null;
        }

        // ------------------------------------------------------------------ the rig

        static void Hello(Transport transport) =>
            transport.Deliver(new ServerMsg.Welcome { Protocol = Wire.ProtocolVersion, MatchId = new byte[16], Session = new byte[16],
                PlayerId = 1, TickRate = 20, ServerMs = 50, Map = new MapBlob(32, 32, new byte[1024]) });

        /// <summary>The whole HUD on a fake transport at 1280x800, the HUD's local clock at 5000 ms, the first-run hint cleared (it is a
        /// sticky feedback line and would be a second pill) and one snapshot (tick 1, 50 ms) delivered.</summary>
        void BuildRig()
        {
            _savedTheme = UiTheme.Current;
            UiTheme.Current = UiTheme.Placeholder();
            _priorEvents = EventSystem.current;
            _events = new GameObject("Charge tiles events", typeof(UiInputRoutingEventSystem));
            var system = _events.GetComponent<UiInputRoutingEventSystem>(); system.enabled = false; system.enabled = true;
            EventSystem.current = system;
            _fullRoot = new GameObject("Charge tiles fixture");
            _client = _fullRoot.AddComponent<MatchClient>(); _client.enabled = false;
            _transport = new Transport();
            _session = new Session(_transport, new SessionOptions { Url = "ws://charges/ws", Profile = "charges", Now = () => 1000 });
            Call(_client, "InitializeLayoutReview", _session); _hud = _client.Hud;
            _session.Connect(); _transport.Open();
            Hello(_transport);
            _tick = 1; _serverMs = 50;
            _transport.Deliver(new ServerMsg.Snapshot { Tick = _tick, ServerTimeOverrideMs = _serverMs, Phase = (byte)MatchPhase.Active, Me = Self() });
            float factor = HudLayout.CanvasScalerFactor(Window.x, Window.y);
            ((HudStage)GetF(_hud, "_stageScaler")).Apply(Window.x, Window.y, factor);
            ((HudStage)GetF(_hud, "_chromeScaler")).Apply(Window.x, Window.y, factor);
            _local = 5000;
            _hud.HudLocalMs = () => _local;
            SetF(_hud, "_firstRunHint", false); SetF(_hud, "_feedback", "");
        }

        static WireSelf Self() =>
            new WireSelf { Id = 1, X = 64, Y = 64, Hp = 100, MaxHp = 100, Status = 1, Weapon = 0, ReadyInMs = 0, Wish = null, Effects = 0 };

        /// <summary>A snapshot with a tick and a server time above every earlier one.</summary>
        void Snap(double serverMs)
        {
            Assert.That(serverMs, Is.GreaterThan(_serverMs), "set-up: server times rise");
            _serverMs = serverMs; _tick++;
            _transport.Deliver(new ServerMsg.Snapshot { Tick = _tick, ServerTimeOverrideMs = serverMs, Phase = (byte)MatchPhase.Active, Me = Self() });
        }

        /// <summary>A State message on the newest snapshot's tick.</summary>
        void Items(params StateItem[] items) => _transport.Deliver(new ServerMsg.State { Tick = _tick, Items = items });

        static QuestsState Spells(params WireQuestSpell[] spells) => new QuestsState { Available = true, View = new WireQuestView { Spells = spells } };

        /// <summary>A Lantern Ward (reward 15) in a slot with its charges and the time the next one arrives.</summary>
        static WireQuestSpell Lantern(int slot, int charges, double readyAt) =>
            new WireQuestSpell { Slot = (byte)slot, Reward = 15, Charges = (byte)charges, ReadyAt = readyAt };

        /// <summary>One frame of the HUD's late update (the quest refresh runs in it, and once more through the player-flow tick).</summary>
        void Frame()
        {
            Call(_hud, "LateUpdate"); Call(_hud, "TickPlayerFlows");
        }

        /// <summary>A press through the real path: collected by the session as a key would be, sent by FlushInput.</summary>
        void Press(int slot)
        {
            byte seq = _session.CollectSpellInput((byte)slot, false, true, 64f, 64f);
            Assert.That(seq, Is.Not.EqualTo((byte)0), "set-up: the session collected the cast of slot " + slot);
            _session.FlushInput();
        }

        /// <summary>Clears the HUD's pops, presses the key, runs a frame, and returns how many pops that press started.</summary>
        int PopsOfPress(int slot)
        {
            Call(_hud, "ClearPulses");
            Assert.That(Pulses().Count, Is.EqualTo(0), "set-up: the pops were cleared");
            Press(slot); Frame();
            return Pulses().Count;
        }

        // ------------------------------------------------------------------ reading the HUD

        RectTransform Stage => (RectTransform)GetF(_hud, "_stage");
        HudPulses Pulses() => (HudPulses)GetF(_hud, "_pulses");
        AbilityTile Tile(int slot) => slot == 0 ? (AbilityTile)GetF(_hud, "_wishAbilityTile") : ((AbilityTile[])GetF(_hud, "_questTiles"))[slot - 1];

        List<string> Pills()
        {
            var list = new List<string>();
            var lane = Stage.Find("FeedbackLane");
            Assert.That(lane, Is.Not.Null, "FeedbackLane exists under the HUD stage");
            foreach (Transform child in lane)
            {
                if (!child.name.StartsWith("Toast") || !child.gameObject.activeInHierarchy) continue;
                var text = child.Find("Text");
                Assert.That(text, Is.Not.Null, child.name + " has a child 'Text'");
                list.Add(text.GetComponent<TMP_Text>().text);
            }
            return list;
        }

        enum Face { Empty, Ready, Cooling, Cast }

        /// <summary>What the tile shows: Empty (not interactable), Cooling (the wedge), a cast bar, else Ready.</summary>
        static Face FaceOf(AbilityTile tile)
        {
            if (!tile.Button.interactable) return Face.Empty;
            if (tile.Sweep.enabled) return Face.Cooling;
            if (tile.CastBar.enabled) return Face.Cast;
            return Face.Ready;
        }

        /// <summary>The name box is shown when the box is active and its label is not empty.</summary>
        static bool NameShown(AbilityTile tile) => Child(tile.Root, "Name").gameObject.activeSelf && tile.NameLabel.text.Length != 0;

        static Color FrameOf(AbilityTile tile) => Child(tile.Root, "Frame").GetComponent<Image>().color;

        static RectTransform Child(Transform root, string path)
        {
            var t = root.Find(path);
            Assert.That(t, Is.Not.Null, "no '" + path + "' under " + root.name);
            return (RectTransform)t;
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

        /// <summary>The Ready look of a held (or full) charge with this badge: nothing of a cooldown is drawn.</summary>
        void AssertReadyLook(string at, int slot, string badge)
        {
            var tile = Tile(slot);
            Assert.That(NameShown(tile), Is.True, at + ": the name box is shown");
            Assert.That(tile.NameLabel.text, Is.EqualTo("Lantern Ward"), at + ": with the spell's name");
            Assert.That(FrameOf(tile), Is.EqualTo((Color)UiTheme.Current.BorderStrong), at + ": the strong frame");
            Assert.That(tile.Sweep.enabled, Is.False, at + ": no wedge");
            Assert.That(tile.SecondsLabel.text, Is.Empty, at + ": no number");
            Assert.That(tile.BadgeLabel.text, Is.EqualTo(badge), at + ": the badge is the count");
            Assert.That(tile.Button.interactable, Is.True, at + ": the button is interactable");
            Assert.That(FaceOf(tile), Is.EqualTo(Face.Ready), at + ": the tile reads Ready");
        }

        // ------------------------------------------------------------------ 1. a held charge

        [Test]
        public void AHeldChargeDrawsTheReadyLook()
        {
            BuildRig();
            Snap(2000);
            Items(Spells(Lantern(0, 1, 62000)));
            Frame();
            Assert.That(_session.Collections.Quests.Spells.Count, Is.EqualTo(1), "set-up: the spell was delivered");
            Assert.That(_session.ServerTimeMs, Is.EqualTo(2000d), "set-up: the server time is the delivered one");
            AssertReadyLook("one charge, the next 60 s away", 1, "1");
        }

        // ------------------------------------------------------------------ 2. the real cycle of a Lantern Ward

        [Test]
        public void ARealLanternWardCycle()
        {
            BuildRig();
            Snap(1000); Items(Spells(Lantern(0, 2, 0))); Frame();
            Assert.That(_session.Collections.Quests.Spells.Count, Is.EqualTo(1), "set-up: the spell was delivered");
            AssertReadyLook("step 1 (2, 0)", 1, "2");
            Assert.That(PopsOfPress(1), Is.EqualTo(1), "step 1: a press pops");
            Assert.That(Pills(), Is.Empty, "step 1: and says nothing");

            Snap(2000); Items(Spells(Lantern(0, 1, 62000))); Frame();
            AssertReadyLook("step 2 (1, 62000)", 1, "1");
            Assert.That(PopsOfPress(1), Is.EqualTo(1), "step 2: a press pops, the server casts it");
            Assert.That(Pills(), Is.Empty, "step 2: and says nothing");

            Snap(5000); Items(Spells(Lantern(0, 0, 62000))); Frame();
            var tile = Tile(1);
            Assert.That(NameShown(tile), Is.False, "step 3 (0, 62000): the name is hidden");
            Assert.That(FrameOf(tile), Is.EqualTo((Color)UiTheme.Current.Border), "step 3: the cooling frame");
            Assert.That(tile.Sweep.enabled, Is.True, "step 3: the wedge is on");
            Assert.That(tile.SecondsLabel.text, Is.EqualTo("57"), "step 3: 57 s left");
            Assert.That(tile.Sweep.fillAmount, Is.InRange(.90f, 1f), "step 3: the wedge starts at its true share of the 60 s, not at a fresh full");
            Assert.That(tile.BadgeLabel.text, Is.EqualTo("0"), "step 3: no charge left");
            Assert.That(PopsOfPress(1), Is.EqualTo(0), "step 3: a press does not pop");
            Assert.That(Pills(), Is.EqualTo(new List<string> { "Ready in 57" }), "step 3: it says when");

            Snap(62000); Items(Spells(Lantern(0, 1, 122000))); Frame();
            AssertReadyLook("step 4 (1, 122000)", 1, "1");

            Snap(122000); Items(Spells(Lantern(0, 2, 122000))); Frame();
            AssertReadyLook("step 5 (2, 122000)", 1, "2");
        }

        // ------------------------------------------------------------------ 3. a second charge arrives

        // The lines after the set-up are a guard behind a red set-up line: they are green on the base.
        [Test]
        public void ASecondChargeArrivingChangesTheBadgeOnly()
        {
            BuildRig();
            Snap(2000); Items(Spells(Lantern(0, 1, 62000))); Frame();
            var tile = Tile(1);
            Assert.That(NameShown(tile), Is.True, "set-up: the held charge shows its name");
            Assert.That(FaceOf(tile), Is.EqualTo(Face.Ready), "set-up: the tile reads Ready");
            Assert.That(Pulses().Count, Is.EqualTo(0), "set-up: nothing has popped");
            var frame = FrameOf(tile);

            Snap(62000); Items(Spells(Lantern(0, 2, 62000)));
            for (int i = 0; i < 3; i++)
            {
                Frame();
                Assert.That(Pulses().Count, Is.EqualTo(0), "frame " + i + ": a second charge pops nothing (no press was made)");
                Assert.That(FrameOf(tile), Is.EqualTo(frame), "frame " + i + ": the frame is unchanged");
                Assert.That(NameShown(tile), Is.True, "frame " + i + ": the name is shown");
                Assert.That(tile.BadgeLabel.text, Is.EqualTo("2"), "frame " + i + ": the badge is 2");
            }
        }

        // ------------------------------------------------------------------ 4. the hint keeps the time

        // Standing guard: green on the base; the hint must not lose the time of the next charge.
        [Test]
        public void TheHintKeepsTheTimeOfTheNextCharge()
        {
            BuildRig();
            Snap(2000); Items(Spells(Lantern(0, 1, 62000))); Frame();
            Assert.That(Tile(1).Hint.Text, Is.EqualTo("SHIFT · Lantern Ward\n1 charge · 60s"), "a held charge, 60 s to the next");
            Snap(20000); Frame();
            Assert.That(Tile(1).Hint.Text, Is.EqualTo("SHIFT · Lantern Ward\n1 charge · 42s"), "the time runs down with no new delivery");
            Snap(62000); Items(Spells(Lantern(0, 2, 0))); Frame();
            Assert.That(Tile(1).Hint.Text, Is.EqualTo("SHIFT · Lantern Ward\n2 charges · Ready"), "full stock");
        }

        // ------------------------------------------------------------------ 5. no garbage

        [Test]
        public void AHeldChargeFrameAllocatesNothing()
        {
            BuildRig();
            Snap(2000); Items(Spells(Lantern(0, 1, 62000))); Frame();
            Assert.That(NameShown(Tile(1)), Is.True, "set-up: the held charge shows its name");
            var refresh = (Action)Delegate.CreateDelegate(typeof(Action), _hud, typeof(MatchHud).GetMethod("RefreshQuestRewardHud", All));
            var tick = (Action)Delegate.CreateDelegate(typeof(Action), _hud, typeof(MatchHud).GetMethod("TickActionRow", All));
            for (int i = 0; i < 5; i++) { refresh(); tick(); }
            AllocationMeter.AssertNone(refresh, 200, "a steady quest refresh with a held charge");
            AllocationMeter.AssertNone(tick, 200, "a steady action-row tick with a held charge");
        }
    }
}
