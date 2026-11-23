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
    /// P-UI-ABILITY-FEEDBACK-1: a cast key that the delivered facts say cannot work shows one line in the feedback lane, in legacy's
    /// words (abilitySlot.ts castDenial), and plays no success pop; a key that can work pops as before. The rig is the full one of
    /// AbilityMotionEditModeTests (a whole MatchHud on a real MatchClient over a fake transport) with the lane readers of
    /// ToastLanesEditModeTests. A press goes through the real path (Session.CollectSpellInput, then FlushInput, which raises CueSent).
    /// EditMode time does not advance: the tiles' clock is MatchHud.HudLocalMs and the lanes' is MatchHud.NoticeDtOverride (.05, and a
    /// rig Frame() ticks the notices twice, so one Frame() is .1 s of lane time).
    /// </summary>
    public sealed class AbilityFeedbackEditModeTests
    {
        const BindingFlags All = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        static readonly Vector2 Window = new Vector2(1280, 800);
        const ushort Stun = 1, Root = 2, Silence = 4, CastingBit = 4096;

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
        readonly List<WireCommand> _cues = new List<WireCommand>();

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

        /// <summary>The whole HUD on a fake transport at 1280x800, the HUD's local clock at 5000 ms under the test's control, the first-run
        /// hint cleared (it is a sticky feedback line and would be a second pill) and one snapshot (tick 1, 50 ms) delivered.</summary>
        void BuildRig()
        {
            _savedTheme = UiTheme.Current;
            UiTheme.Current = UiTheme.Placeholder();
            _priorEvents = EventSystem.current;
            _events = new GameObject("Ability feedback events", typeof(UiInputRoutingEventSystem));
            var system = _events.GetComponent<UiInputRoutingEventSystem>(); system.enabled = false; system.enabled = true;
            EventSystem.current = system;
            _fullRoot = new GameObject("Ability feedback fixture");
            _client = _fullRoot.AddComponent<MatchClient>(); _client.enabled = false;
            _transport = new Transport();
            _session = new Session(_transport, new SessionOptions { Url = "ws://feedback/ws", Profile = "feedback", Now = () => 1000 });
            _cues.Clear();
            _session.CueSent += c => _cues.Add(c);
            Call(_client, "InitializeLayoutReview", _session); _hud = _client.Hud;
            _session.Connect(); _transport.Open();
            Hello(_transport);
            _tick = 1; _serverMs = 50;
            _transport.Deliver(new ServerMsg.Snapshot { Tick = _tick, ServerTimeOverrideMs = _serverMs, Phase = (byte)MatchPhase.Active, Me = Self(null, 0) });
            float factor = HudLayout.CanvasScalerFactor(Window.x, Window.y);
            ((HudStage)GetF(_hud, "_stageScaler")).Apply(Window.x, Window.y, factor);
            ((HudStage)GetF(_hud, "_chromeScaler")).Apply(Window.x, Window.y, factor);
            _local = 5000;
            _hud.HudLocalMs = () => _local;
            SetF(_hud, "_firstRunHint", false); SetF(_hud, "_feedback", "");
        }

        static WireSelf Self(WireWishOwner wish, ushort effects) =>
            new WireSelf { Id = 1, X = 64, Y = 64, Hp = 100, MaxHp = 100, Status = 1, Weapon = 0, ReadyInMs = 0, Wish = wish, Effects = effects };

        /// <summary>A snapshot with a tick and a server time above every earlier one, carrying the owner's Wish (null: none) and Effects.</summary>
        void Snap(double serverMs, WireWishOwner wish = null, ushort effects = 0)
        {
            Assert.That(serverMs, Is.GreaterThan(_serverMs), "set-up: server times rise");
            _serverMs = serverMs; _tick++;
            _transport.Deliver(new ServerMsg.Snapshot { Tick = _tick, ServerTimeOverrideMs = serverMs, Phase = (byte)MatchPhase.Active, Me = Self(wish, effects) });
        }

        /// <summary>A valid owner Wish (Blaze, one charge slot); a cast when castStartedAt is not negative.</summary>
        static WireWishOwner Wish(byte charges, double readyAt, double rechargeMs, double castStartedAt = -1, double castLandsAt = 0) =>
            new WireWishOwner
            {
                Ability = 3, MaxCharges = 1, Charges = charges, ReadyAt = readyAt, RechargeMs = rechargeMs,
                Cast = castStartedAt < 0 ? null : new WireWishCast { Ability = 3, StartedAt = castStartedAt, LandsAt = castLandsAt },
            };

        /// <summary>A State message on the newest snapshot's tick, as the existing tests deliver them.</summary>
        void Items(params StateItem[] items) => _transport.Deliver(new ServerMsg.State { Tick = _tick, Items = items });

        static QuestsState Spells(params WireQuestSpell[] spells) => new QuestsState { Available = true, View = new WireQuestView { Spells = spells } };

        static WireQuestSpell Spell(int index, byte charges, double readyAt) => new WireQuestSpell { Slot = (byte)index, Reward = 3, Charges = charges, ReadyAt = readyAt };

        void Events(params WireEvent[] list) =>
            _transport.Deliver(new ServerMsg.Events { Tick = ++_tick, List = new List<WireEvent>(list) });

        /// <summary>One frame of the HUD's late update (the quest refresh runs in it, and once more through the player-flow tick). The notice
        /// clock advances by dt twice: one Frame() is 2 x dt of lane time.</summary>
        void Frame(float dt = .05f)
        {
            MatchHud.NoticeDtOverride = dt;
            Call(_hud, "LateUpdate"); Call(_hud, "TickPlayerFlows");
        }

        /// <summary>Frames worth this many seconds of lane time (a Frame() is .1 s).</summary>
        void Lane(float seconds) { for (int i = 0, n = (int)Math.Round(seconds / .1f); i < n; i++) Frame(); }

        /// <summary>A press through the real path: collected by the session as a key would be, sent by FlushInput, which raises CueSent.</summary>
        void Press(int slot)
        {
            byte seq = _session.CollectSpellInput((byte)slot, false, true, 64f, 64f);
            Assert.That(seq, Is.Not.EqualTo((byte)0), "set-up: the session collected the cast of slot " + slot);
            _session.FlushInput();
        }

        // ------------------------------------------------------------------ reading the HUD

        RectTransform Stage => (RectTransform)GetF(_hud, "_stage");
        HudPulses Pulses() => (HudPulses)GetF(_hud, "_pulses");
        ToastLaneModel Model => (ToastLaneModel)GetF(_hud, "_feedbackModel");
        AbilityTile Tile(int slot) => slot == 0 ? (AbilityTile)GetF(_hud, "_wishAbilityTile") : ((AbilityTile[])GetF(_hud, "_questTiles"))[slot - 1];
        RectTransform TileRoot(int slot) => Tile(slot).Root;

        /// <summary>The pills of the feedback lane that are active in the hierarchy.</summary>
        List<RectTransform> Pills()
        {
            var list = new List<RectTransform>();
            var lane = Stage.Find("FeedbackLane");
            Assert.That(lane, Is.Not.Null, "FeedbackLane exists under the HUD stage");
            foreach (Transform child in lane)
                if (child.name.StartsWith("Toast") && child.gameObject.activeInHierarchy) list.Add((RectTransform)child);
            return list;
        }

        static string TextOf(RectTransform pill)
        {
            var t = pill.Find("Text");
            Assert.That(t, Is.Not.Null, pill.name + " has a child 'Text'");
            return t.GetComponent<TMP_Text>().text;
        }

        List<string> Texts()
        {
            var texts = new List<string>();
            foreach (var pill in Pills()) texts.Add(TextOf(pill));
            return texts;
        }

        /// <summary>The pills read exactly these lines, in order.</summary>
        void AssertPills(string what, params string[] lines) => Assert.That(Texts(), Is.EqualTo(new List<string>(lines)), what);

        static float AlphaOf(RectTransform pill) => pill.GetComponent<CanvasGroup>().alpha;

        enum Face { Empty, Ready, Cooling, Cast }

        /// <summary>What the tile shows: Empty (not interactable), Cooling (the wedge), a cast bar, else Ready.</summary>
        static Face FaceOf(AbilityTile tile)
        {
            if (!tile.Button.interactable) return Face.Empty;
            if (tile.Sweep.enabled) return Face.Cooling;
            if (tile.CastBar.enabled) return Face.Cast;
            return Face.Ready;
        }

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

        // ------------------------------------------------------------------ 1, 2. a denied press

        [Test]
        public void ADeniedPressSaysWhyAndPopsNothing()
        {
            BuildRig();
            Snap(50000, Wish(0, 52100, 10000)); Frame();
            Assert.That(FaceOf(Tile(0)), Is.EqualTo(Face.Cooling), "set-up: the Wish tile is cooling");
            int pulses = Pulses().Count;
            Press(0); Frame();
            AssertPills("the denied press shows one pill", "Ready in 3");
            foreach (var pill in Pills())
                Assert.That(Child(pill, "Tone").GetComponent<Image>().color, Is.EqualTo((Color)UiTheme.Current.Warning), "its tone strip is the theme's warning colour");
            Assert.That(Pulses().Count, Is.EqualTo(pulses), "no pop was started");
            double start = _local;
            for (int ms = 10; ms <= 160; ms += 10)
            {
                _local = start + ms; Call(_hud, "TickActionRow");
                Assert.That(TileRoot(0).localScale.x, Is.EqualTo(1f), "the Wish tile stays at scale 1 at +" + ms + " ms");
            }
            foreach (var label in TileRoot(0).GetComponentsInChildren<TMP_Text>(true))
                Assert.That(label.text, Is.Not.EqualTo("Ready in 3"), "no label on the tile reads the line (" + label.name + ")");
            Assert.That(_cues.Count, Is.EqualTo(1), "the press was still sent: CueSent saw one command");
            Assert.That(_cues[0].Kind, Is.EqualTo(CommandKind.Cast), "a Cast");
            Assert.That((int)(_cues[0].Slot ?? 255), Is.EqualTo(0), "of slot 0");
        }

        [Test]
        public void ASecondPressReplacesTheLine()
        {
            BuildRig();
            Snap(50000, Wish(0, 52100, 10000));
            Items(Spells(Spell(0, 0, 56000)));
            Frame();
            Assert.That(FaceOf(Tile(0)), Is.EqualTo(Face.Cooling), "set-up: the Wish tile is cooling");
            Assert.That(FaceOf(Tile(1)), Is.EqualTo(Face.Cooling), "set-up: the Shift tile is cooling");
            Press(0); Frame();
            AssertPills("the first press", "Ready in 3");
            Snap(51200, Wish(0, 52100, 10000)); Frame();
            Press(0); Frame();
            AssertPills("the second press replaces the line in place", "Ready in 1");
            Press(1); Frame();
            AssertPills("a press of another key replaces it too", "Ready in 5");
        }

        // ------------------------------------------------------------------ 3, 4, 6. a press that goes through, an empty slot

        [Test]
        public void AReadyPressPopsAndSpeaksNothing()
        {
            BuildRig();
            Snap(50000, Wish(1, 40000, 10000));
            Items(Spells(Spell(0, 1, 0), Spell(1, 1, 0)));
            Frame();
            for (int slot = 0; slot <= 2; slot++)
            {
                Assert.That(FaceOf(Tile(slot)), Is.EqualTo(Face.Ready), "set-up: tile " + slot + " is ready");
                int before = Pulses().Count;
                Press(slot); Frame();
                Assert.That(Pulses().Count, Is.EqualTo(before + 1), "slot " + slot + ": the press pops its tile");
                AssertPills("slot " + slot + ": nothing is said");
            }
        }

        [Test]
        public void AnEmptySlotNeitherPopsNorSpeaks()
        {
            BuildRig(); Frame();
            for (int slot = 0; slot <= 2; slot++)
            {
                Assert.That(FaceOf(Tile(slot)), Is.EqualTo(Face.Empty), "set-up: tile " + slot + " is empty");
                int before = Pulses().Count;
                Press(slot); Frame();
                Assert.That(Pulses().Count, Is.EqualTo(before), "slot " + slot + ": an empty slot does not pop");
                AssertPills("slot " + slot + ": and says nothing");
            }
            Assert.That(_cues.Count, Is.EqualTo(3), "all three presses were still sent");
        }

        [Test]
        public void ARootDoesNotStopACast()
        {
            BuildRig();
            Snap(50000, Wish(1, 40000, 10000), Root);
            Items(Spells(Spell(0, 1, 0)));
            Frame();
            Assert.That(_session.Me.Effects, Is.EqualTo(Root), "set-up: the root reached the session");
            foreach (int slot in new[] { 0, 1 })
            {
                int before = Pulses().Count;
                Press(slot); Frame();
                Assert.That(Pulses().Count, Is.EqualTo(before + 1), "slot " + slot + ": a rooted press pops");
                AssertPills("slot " + slot + ": and says nothing");
            }
        }

        // ------------------------------------------------------------------ 5. the line is the function's, for every phase the tile shows

        struct Case
        {
            public string Name;
            public int Slot;
            public WireWishOwner Wish;
            public WireQuestSpell? Spell;
            public ushort Effects;
            public Face Tile;
            public int Seconds;
            public string Line;
            public bool Pops;
        }

        static List<Case> Cases()
        {
            var cases = new List<Case>();
            var ready = Wish(1, 40000, 10000);
            void R(string name, WireWishOwner wish, ushort effects, Face tile, int seconds, string line, bool pops) =>
                cases.Add(new Case { Name = "R " + name, Slot = 0, Wish = wish, Effects = effects, Tile = tile, Seconds = seconds, Line = line, Pops = pops });
            R("empty", null, 0, Face.Empty, 0, null, false);
            R("ready", ready, 0, Face.Ready, 0, null, true);
            R("cooling 3 s", Wish(0, 52100, 10000), 0, Face.Cooling, 3, "Ready in 3", false);
            R("cooling 299 s", Wish(0, 348100, 300000), 0, Face.Cooling, 299, "Ready in 4:59", false);
            R("effects bit 4096", ready, CastingBit, Face.Ready, 0, "Casting", false);
            R("cast bar", Wish(1, 40000, 10000, 49500, 50500), 0, Face.Cast, 0, "Casting", false);
            R("stunned", ready, Stun, Face.Ready, 0, "Stunned", false);
            R("silenced", ready, Silence, Face.Ready, 0, "Silenced", false);
            foreach (int slot in new[] { 1, 2 })
            {
                string key = slot == 1 ? "Shift" : "Q";
                void S(string name, WireQuestSpell? spell, ushort effects, Face tile, int seconds, string line, bool pops) =>
                    cases.Add(new Case { Name = key + " " + name, Slot = slot, Spell = spell, Effects = effects, Tile = tile, Seconds = seconds, Line = line, Pops = pops });
                int i = slot - 1;
                S("empty", null, 0, Face.Empty, 0, null, false);
                S("ready", Spell(i, 1, 0), 0, Face.Ready, 0, null, true);
                S("cooling 3 s", Spell(i, 0, 52100), 0, Face.Cooling, 3, "Ready in 3", false);
                S("cooling 299 s", Spell(i, 0, 348100), 0, Face.Cooling, 299, "Ready in 4:59", false);
                S("effects bit 4096", Spell(i, 1, 0), CastingBit, Face.Ready, 0, "Casting", false);
                S("charge in hand, next recharging", Spell(i, 1, 52100), 0, Face.Ready, 0, null, true);
                S("stunned", Spell(i, 1, 0), Stun, Face.Ready, 0, null, true);
                S("silenced", Spell(i, 1, 0), Silence, Face.Ready, 0, null, true);
            }
            return cases;
        }

        [Test]
        public void TheLineIsTheOneTheFunctionGivesForEveryPhaseTheTileShows()
        {
            var cases = Cases();
            Assert.That(cases.Count, Is.EqualTo(24), "set-up: eight facts for each of R, Shift and Q");
            foreach (var c in cases)
            {
                Cleanup();
                BuildRig();
                Snap(50000, c.Wish, c.Effects);
                if (c.Slot > 0) Items(Spells(c.Spell.HasValue ? new[] { c.Spell.Value } : new WireQuestSpell[0]));
                Frame();
                var tile = Tile(c.Slot);
                Assert.That(FaceOf(tile), Is.EqualTo(c.Tile), c.Name + ": set-up: the tile shows " + c.Tile);
                if (c.Tile == Face.Cooling) Assert.That(int.Parse(tile.SecondsLabel.text), Is.EqualTo(c.Seconds), c.Name + ": set-up: the countdown");

                // What the pure function says from the facts the session holds.
                CastReading reading;
                double now = _session.ServerTimeMs;
                if (c.Slot == 0) reading = CastReadiness.Wish(_session.Me.Wish, now, _session.Me.Effects);
                else if (c.Spell.HasValue) reading = CastReadiness.Spell(true, c.Spell.Value.Charges, c.Spell.Value.ReadyAt, now, _session.Me.Effects);
                else reading = CastReadiness.Spell(false, 0, 0d, now, _session.Me.Effects);
                Assert.That(reading.Line, Is.EqualTo(c.Line), c.Name + ": CastReadiness fed the delivered facts gives the line");
                Assert.That(reading.Pops, Is.EqualTo(c.Pops), c.Name + ": and says whether the press pops");

                int before = Pulses().Count;
                Press(c.Slot); Frame();
                if (c.Line == null) AssertPills(c.Name + ": no pill");
                else AssertPills(c.Name + ": the pill is the line", c.Line);
                Assert.That(Pulses().Count, Is.EqualTo(before + (c.Pops ? 1 : 0)), c.Name + (c.Pops ? ": the press pops" : ": the press does not pop"));
            }
        }

        /// <summary>P-UI-CHARGE-TILES-1: the tile and the key say the same thing in every case of the matrix. Reads only what the tile
        /// draws (FaceOf), what the pure reading says from the delivered facts, and what the press did; never a case's expected Tile.</summary>
        [Test]
        public void TheTileAndTheKeyAgreeInEveryCase()
        {
            var cases = Cases();
            Assert.That(cases.Count, Is.EqualTo(24), "set-up: eight facts for each of R, Shift and Q");
            int cooling = 0, popped = 0;
            foreach (var c in cases)
            {
                Cleanup();
                BuildRig();
                Snap(50000, c.Wish, c.Effects);
                if (c.Slot > 0) Items(Spells(c.Spell.HasValue ? new[] { c.Spell.Value } : new WireQuestSpell[0]));
                Frame();
                var face = FaceOf(Tile(c.Slot));
                CastReading reading;
                double now = _session.ServerTimeMs;
                if (c.Slot == 0) reading = CastReadiness.Wish(_session.Me.Wish, now, _session.Me.Effects);
                else if (c.Spell.HasValue) reading = CastReadiness.Spell(true, c.Spell.Value.Charges, c.Spell.Value.ReadyAt, now, _session.Me.Effects);
                else reading = CastReadiness.Spell(false, 0, 0d, now, _session.Me.Effects);
                int before = Pulses().Count;
                Press(c.Slot); Frame();
                bool pops = Pulses().Count > before;
                var texts = Texts();
                bool saysReady = reading.Line != null && reading.Line.StartsWith("Ready in ");
                if (face == Face.Cooling) { cooling++; Assert.That(saysReady, Is.True, c.Name + ": a cooling tile's line starts with 'Ready in ' (line: " + (reading.Line ?? "none") + ")"); }
                if (face == Face.Cooling) Assert.That(pops, Is.False, c.Name + ": a press of a cooling tile does not pop");
                if (saysReady) Assert.That(face, Is.EqualTo(Face.Cooling), c.Name + ": a line 'Ready in ...' means a cooling tile");
                if (saysReady) Assert.That(texts, Is.EqualTo(new List<string> { reading.Line }), c.Name + ": and the press shows that line");
                if (pops) { popped++; Assert.That(face, Is.EqualTo(Face.Ready), c.Name + ": a press that pops is on a ready tile"); }
                if (face == Face.Empty) Assert.That(pops, Is.False, c.Name + ": an empty tile does not pop");
                if (face == Face.Empty) Assert.That(texts, Is.Empty, c.Name + ": and shows no line");
                if (face == Face.Cast) Assert.That(reading.Line, Is.EqualTo("Casting"), c.Name + ": a cast-bar tile reads 'Casting'");
            }
            Assert.That(cooling, Is.GreaterThanOrEqualTo(6), "set-up: the matrix has cooling tiles (R, Shift, Q at 3 s and 299 s)");
            Assert.That(popped, Is.GreaterThanOrEqualTo(6), "set-up: the matrix has presses that pop");
        }

        // ------------------------------------------------------------------ 7, 8, 9. reduced motion, a rebind, no garbage

        [Test]
        public void ReducedMotionStillSpeaks()
        {
            UiMotion.ReducedOverride = true;
            BuildRig();
            Snap(50000, Wish(0, 52100, 10000)); Frame();
            Press(0); Frame();
            AssertPills("the line shows under reduced motion", "Ready in 3");
            Assert.That(Pulses().Count, Is.EqualTo(0), "and nothing pops");
        }

        [Test]
        public void ARebindForgetsTheLine()
        {
            BuildRig();
            Snap(50000, Wish(0, 52100, 10000)); Frame();
            Press(0); Frame();
            AssertPills("set-up: the line shows", "Ready in 3");
            _hud.ShowMenu();
            Assert.That(Model.Count, Is.EqualTo(0), "a rebind leaves no line in the lane's model");
            Assert.That(Model.Waiting, Is.EqualTo(0), "and none waiting");
        }

        [Test]
        public void ASteadyFrameWithTheLineAllocatesNothing()
        {
            BuildRig();
            Snap(50000, Wish(0, 52100, 10000)); Frame();
            Press(0); Frame();
            AssertPills("set-up: the line shows", "Ready in 3");
            var tickNotices = (Action)Delegate.CreateDelegate(typeof(Action), _hud, typeof(MatchHud).GetMethod("TickNotices", All));
            var tickRow = (Action)Delegate.CreateDelegate(typeof(Action), _hud, typeof(MatchHud).GetMethod("TickActionRow", All));
            MatchHud.NoticeDtOverride = .001f;
            AllocationMeter.AssertNone(tickNotices, 200, "a notice tick with the cast line showing");
            AllocationMeter.AssertNone(tickRow, 200, "an action-row tick with the cast line showing");
        }

        // ------------------------------------------------------------------ 10. one second, and never late

        [Test]
        public void TheLineLastsOneSecondAndNeverShowsLate()
        {
            BuildRig();
            Snap(50000, Wish(0, 52100, 10000)); Frame();
            Press(0);
            Lane(.9f);
            AssertPills("at .9 s the line shows", "Ready in 3");
            foreach (var pill in Pills()) Assert.That(AlphaOf(pill), Is.EqualTo(1f).Within(.01f), "fully opaque");
            Lane(.8f);
            AssertPills("at 1.7 s it is gone");

            Events(new WireEvent { Kind = EventKind.PickupRefused, PickupReason = 3 }, new WireEvent { Kind = EventKind.CannonNotice, CannonCode = 1 });
            for (int i = 0; i < 30 && Pills().Count < 2; i++) Frame();
            AssertPills("set-up: the lane is full", "Can't pick that up", "Cannon sealed");
            Press(0); Frame();
            Assert.That(Texts(), Does.Not.Contain("Ready in 3"), "a line that cannot show at once is not shown");
            Assert.That(Model.Waiting, Is.EqualTo(0), "and is not queued");
            Lane(3f);
            Assert.That(Texts(), Does.Not.Contain("Ready in 3"), "and does not appear when the lane has room again");
            Assert.That(Model.Waiting, Is.EqualTo(0), "nothing waited");
        }

        // ------------------------------------------------------------------ 11. the Wish is read from the newest snapshot

        [Test]
        public void ThePressReadsTheNewestSnapshot()
        {
            BuildRig();
            Snap(50000, Wish(1, 40000, 10000)); Frame();
            Assert.That(FaceOf(Tile(0)), Is.EqualTo(Face.Ready), "set-up: the Wish tile is ready");
            Snap(50050, Wish(0, 52100, 10000));
            int pulses = Pulses().Count;
            Press(0);
            Assert.That(FaceOf(Tile(0)), Is.EqualTo(Face.Ready), "the tile has not refreshed yet: it is one snapshot behind");
            Assert.That(Pulses().Count, Is.EqualTo(pulses), "the press did not pop");
            Assert.That(Model.Count, Is.EqualTo(1), "the model holds one line before any frame");
            Assert.That(Model.Count > 0 ? Model.Line(0).Text : "(no line)", Is.EqualTo("Ready in 3"), "read from the newest snapshot");
            Frame();
            Assert.That(FaceOf(Tile(0)), Is.EqualTo(Face.Cooling), "the frame refreshes the tile to cooling");
            AssertPills("and one pill reads the line", "Ready in 3");
        }

        // ------------------------------------------------------------------ 12. a press that goes through ends the line

        [Test]
        public void APressThatGoesThroughEndsTheLine()
        {
            BuildRig();
            Snap(50000, null);
            Items(Spells(Spell(0, 0, 52100)));
            Frame();
            Press(1);
            Lane(.2f);
            AssertPills("the first press shows its line", "Ready in 3");
            Snap(52200, null);
            int before = Pulses().Count;
            Press(1);
            Assert.That(Pulses().Count, Is.EqualTo(before + 1), "the cooldown ended: the press pops");
            Lane(.5f);
            AssertPills("and the line it left behind is gone");
        }
    }
}
