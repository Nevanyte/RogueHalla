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
    /// P-UI-ABILITY-FEEDBACK-2: arming Frost's Wish aim (Quick cast off, the wall room) while the Wish cannot be cast says why, once per
    /// arming, in the feedback lane, with the same line and key as the sent cast (P-UI-ABILITY-FEEDBACK-1). The aim itself is unchanged:
    /// it stays armed, nothing is sent, no tile pops. The rig is the whole-HUD one of AbilityFeedbackEditModeTests with a Frost seat and
    /// the wall's terms in the Welcome; the aim is armed through the real <c>WishAim.Key</c> with the frame <c>MatchClient.MakeWishFrame</c>
    /// builds (the key itself is read by MatchClient.ReadInput, which the existing aim tests and UiInputRoutingEditModeTests cover).
    /// EditMode time does not advance: the lane's clock is MatchHud.NoticeDtOverride, and a rig Frame() is .1 s of lane time.
    /// </summary>
    public sealed class AimFeedbackEditModeTests
    {
        const BindingFlags All = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        static readonly Vector2 Window = new Vector2(1280, 800);
        const ushort Stun = 1, Silence = 4;

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
        double _serverMs;
        uint _tick;
        readonly List<WireCommand> _cues = new List<WireCommand>();

        [SetUp]
        public void SetUp() { UiMotion.ReducedOverride = false; MatchHud.NoticeDtOverride = .05f; }

        [TearDown]
        public void TearDown()
        {
            UiMotion.ReducedOverride = null;
            MatchHud.NoticeDtOverride = null;
            if (_fullRoot != null) UnityEngine.Object.DestroyImmediate(_fullRoot);
            if (_events != null) UnityEngine.Object.DestroyImmediate(_events);
            if (_priorEvents != null) EventSystem.current = _priorEvents;
            if (_savedTheme != null) UiTheme.Current = _savedTheme;
            _fullRoot = null; _events = null; _priorEvents = null; _savedTheme = null;
        }

        // ------------------------------------------------------------------ the rig

        /// <summary>The Welcome of a room whose Frost Wish is the wall: terms 23 to 29 as StormInkOwnerWishInputEditModeTests sets them.</summary>
        static void Hello(Transport transport)
        {
            double[] terms = { 1, 450, 64, 224, 4000, 24, 8 };
            var block = new WireHudTerm[terms.Length];
            for (int i = 0; i < terms.Length; i++) block[i] = new WireHudTerm { Code = (byte)((int)HudTerm.FrostWish + i), Value = terms[i] };
            transport.Deliver(new ServerMsg.Welcome { Protocol = Wire.ProtocolVersion, MatchId = new byte[16], Session = new byte[16],
                PlayerId = 1, TickRate = 20, ServerMs = 50, Map = new MapBlob(32, 32, new byte[1024]), Hud = new WireHudContent { Terms = block } });
        }

        Session NewSession(out Transport transport)
        {
            transport = new Transport();
            var session = new Session(transport, new SessionOptions { Url = "ws://aim/ws", Profile = "aim", Now = () => 1000 });
            session.CueSent += c => _cues.Add(c);
            return session;
        }

        /// <summary>The whole HUD on a fake transport at 1280x800, a Frost seat in the wall room, the first-run hint cleared (a sticky
        /// feedback line would be a second pill) and one snapshot (tick 1, 50 ms) delivered.</summary>
        void BuildRig()
        {
            _savedTheme = UiTheme.Current;
            UiTheme.Current = UiTheme.Placeholder();
            _priorEvents = EventSystem.current;
            _events = new GameObject("Aim feedback events", typeof(UiInputRoutingEventSystem));
            var system = _events.GetComponent<UiInputRoutingEventSystem>(); system.enabled = false; system.enabled = true;
            EventSystem.current = system;
            _fullRoot = new GameObject("Aim feedback fixture");
            _client = _fullRoot.AddComponent<MatchClient>(); _client.enabled = false;
            _cues.Clear();
            _session = NewSession(out _transport);
            Call(_client, "InitializeLayoutReview", _session); _hud = _client.Hud;
            _session.Connect(); _transport.Open();
            Hello(_transport);
            _tick = 1; _serverMs = 50;
            _transport.Deliver(new ServerMsg.Snapshot { Tick = _tick, ServerTimeOverrideMs = _serverMs, Phase = (byte)MatchPhase.Active, Me = Self(null, 0) });
            float factor = HudLayout.CanvasScalerFactor(Window.x, Window.y);
            ((HudStage)GetF(_hud, "_stageScaler")).Apply(Window.x, Window.y, factor);
            ((HudStage)GetF(_hud, "_chromeScaler")).Apply(Window.x, Window.y, factor);
            _hud.HudLocalMs = () => 5000;
            SetF(_hud, "_firstRunHint", false); SetF(_hud, "_feedback", "");
            Assert.That(_session.Me.Faction, Is.EqualTo(FactionId.Frost), "set-up: the local fighter is Frost");
            Assert.That(_session.Welcome.Hud.FrostWish, Is.EqualTo(FrostWishKind.Wall), "set-up: the room's Frost Wish is the wall");
            Assert.That(_session.Welcome.Hud.FrostWall.HasValue, Is.True, "set-up: the Welcome carries the wall's numbers");
        }

        static WireSelf Self(WireWishOwner wish, ushort effects) =>
            new WireSelf { Id = 1, Faction = (byte)FactionId.Frost, X = 64, Y = 64, Hp = 100, MaxHp = 100, Status = 1, Weapon = 0, ReadyInMs = 0, Wish = wish, Effects = effects };

        /// <summary>A snapshot with a tick and a server time above every earlier one, carrying the owner's Wish (null: none) and Effects.</summary>
        void Snap(double serverMs, WireWishOwner wish = null, ushort effects = 0) => SnapOn(_transport, ref _tick, ref _serverMs, serverMs, wish, effects);

        static void SnapOn(Transport transport, ref uint tick, ref double lastMs, double serverMs, WireWishOwner wish, ushort effects)
        {
            Assert.That(serverMs, Is.GreaterThan(lastMs), "set-up: server times rise");
            lastMs = serverMs; tick++;
            transport.Deliver(new ServerMsg.Snapshot { Tick = tick, ServerTimeOverrideMs = serverMs, Phase = (byte)MatchPhase.Active, Me = Self(wish, effects) });
        }

        /// <summary>A valid owner Wish (one charge slot); a cast when castStartedAt is not negative.</summary>
        static WireWishOwner Wish(byte charges, double readyAt, double rechargeMs, double castStartedAt = -1, double castLandsAt = 0) =>
            new WireWishOwner
            {
                Ability = 3, MaxCharges = 1, Charges = charges, ReadyAt = readyAt, RechargeMs = rechargeMs,
                Cast = castStartedAt < 0 ? null : new WireWishCast { Ability = 3, StartedAt = castStartedAt, LandsAt = castLandsAt },
            };

        /// <summary>One frame of the HUD's late update (the action row, which ticks the press pulses, runs in it). One Frame() is .1 s of lane time.</summary>
        void Frame()
        {
            MatchHud.NoticeDtOverride = .05f;
            Call(_hud, "LateUpdate"); Call(_hud, "TickPlayerFlows");
        }

        /// <summary>Frames worth this many seconds of lane time.</summary>
        void Lane(float seconds) { for (int i = 0, n = (int)Math.Round(seconds / .1f); i < n; i++) Frame(); }

        // ------------------------------------------------------------------ the aim, through the real WishAim

        /// <summary>The frame MatchClient builds for a Wish key: the real MakeWishFrame, a ground point under the cursor, the given Quick cast.</summary>
        WishAimFrame AimFrame(bool quickCast)
        {
            var frame = (WishAimFrame)Call(_client, "MakeWishFrame", 64f, 64f, false, true, 200f, 64f, null);
            Assert.That(frame.Wish, Is.EqualTo(FrostWishKind.Wall), "set-up: the frame's Wish kind is the wall");
            Assert.That(frame.Numbers.HasValue, Is.True, "set-up: the frame carries the wall's numbers");
            frame.QuickCast = quickCast;
            return frame;
        }

        /// <summary>The Wish key with Quick cast off (or on): returns the order number of a cast it collected, 0 for an arm or a put-away.</summary>
        byte Key(bool quickCast = false) => _client.WishAim.Key(_session, AimFrame(quickCast));

        /// <summary>Arms the aim and checks that it armed and sent nothing.</summary>
        void Arm()
        {
            Assert.That(_client.WishAim.Aiming, Is.False, "set-up: the aim is away before the key");
            Assert.That(Key(), Is.EqualTo((byte)0), "set-up: the arming key collects no cast");
            Assert.That(_client.WishAim.Aiming, Is.True, "set-up: the key armed the aim");
        }

        void PutAway()
        {
            Assert.That(Key(), Is.EqualTo((byte)0), "set-up: the second key collects no cast");
            Assert.That(_client.WishAim.Aiming, Is.False, "set-up: the second key put the aim away");
        }

        // ------------------------------------------------------------------ reading the HUD

        RectTransform Stage => (RectTransform)GetF(_hud, "_stage");
        HudPulses Pulses() => (HudPulses)GetF(_hud, "_pulses");
        AbilityTile WishTile => (AbilityTile)GetF(_hud, "_wishAbilityTile");

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

        List<string> Texts()
        {
            var texts = new List<string>();
            foreach (var pill in Pills())
            {
                var t = pill.Find("Text");
                Assert.That(t, Is.Not.Null, pill.name + " has a child 'Text'");
                texts.Add(t.GetComponent<TMP_Text>().text);
            }
            return texts;
        }

        void AssertPills(string what, params string[] lines) => Assert.That(Texts(), Is.EqualTo(new List<string>(lines)), what);

        /// <summary>What the Wish key would do now, from the facts the session holds (the function the HUD uses).</summary>
        string ReadingLine() => CastReadiness.Wish(_session.Me.Wish, _session.ServerTimeMs, _session.Me.Effects).Line;

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

        /// <summary>The Wish at 50 s, cooling with 3 s to go.</summary>
        void CoolingWish() { Snap(50000, Wish(0, 52100, 10000)); Frame(); }

        // ------------------------------------------------------------------ 1. arming while the Wish cools

        [Test]
        public void ArmingTheAimWhileTheWishCoolsSaysWhen()
        {
            BuildRig();
            CoolingWish();
            Assert.That(_client.WishAim.Aiming, Is.False, "set-up: the aim is not armed");
            AssertPills("set-up: the lane shows no cast line");
            Assert.That(ReadingLine(), Is.EqualTo("Ready in 3"), "set-up: the facts say the Wish is cooling for 3 s");
            Call(_hud, "ClearPulses");
            Arm();
            Frame();
            string line = ReadingLine();
            AssertPills("arming a cooling Wish shows its reason once", line);
            foreach (var pill in Pills())
                Assert.That(pill.Find("Tone").GetComponent<Image>().color, Is.EqualTo((Color)UiTheme.Current.Warning), "in the warning tone");
            Assert.That(_client.WishAim.Aiming, Is.True, "the aim stays armed, as today");
            _session.FlushInput();
            Assert.That(_cues.Count, Is.EqualTo(0), "nothing was sent: no cast left");
            Assert.That(Pulses().Count, Is.EqualTo(0), "no tile pop was started");
            Assert.That(WishTile.Root.localScale.x, Is.EqualTo(1f), "the Wish tile is at rest");
        }

        // ------------------------------------------------------------------ 2. each reason

        [TestCase("cooling", "Ready in 3")]
        [TestCase("casting", "Casting")]
        [TestCase("stunned", "Stunned")]
        [TestCase("silenced", "Silenced")]
        public void EachRefusalHasItsLine(string state, string expected)
        {
            BuildRig();
            switch (state)
            {
                case "cooling": Snap(50000, Wish(0, 52100, 10000)); break;
                case "casting": Snap(50000, Wish(1, 40000, 10000, 49500, 50500)); break;
                case "stunned": Snap(50000, Wish(1, 40000, 10000), Stun); break;
                default: Snap(50000, Wish(1, 40000, 10000), Silence); break;
            }
            Frame();
            Assert.That(ReadingLine(), Is.EqualTo(expected), "set-up: the facts read '" + expected + "'");
            Arm();
            Frame();
            AssertPills(state + ": arming shows the function's line", ReadingLine());
            AssertPills(state + ": and it is the line the reason has", expected);
            Assert.That(_client.WishAim.Aiming, Is.True, state + ": the aim stays armed");
        }

        // ------------------------------------------------------------------ 3. once per arming

        [Test]
        public void TheLineComesOncePerArming()
        {
            BuildRig();
            CoolingWish();
            Arm();
            Frame();
            AssertPills("the arming shows the line", "Ready in 3");
            Lane(2f);
            AssertPills("past the line's hold, with the aim still armed, it is gone");
            Assert.That(_client.WishAim.Aiming, Is.True, "set-up: the aim stayed armed");
            Lane(1f);
            AssertPills("and no new line came while the aim stayed armed");
            PutAway();
            Frame();
            AssertPills("putting the aim away shows nothing");
            Arm();
            Frame();
            AssertPills("arming again shows the line again", "Ready in 3");
        }

        [Test]
        public void ARefusalThatBeginsWhileTheAimIsArmedSaysNothing()
        {
            BuildRig();
            Snap(50000, Wish(1, 40000, 10000)); Frame();
            Arm(); Frame();
            AssertPills("set-up: arming a ready Wish says nothing");
            Snap(50100, Wish(0, 52100, 10000)); Frame();
            Assert.That(ReadingLine(), Is.EqualTo("Ready in 2"), "set-up: the Wish began to cool under the armed aim");
            Lane(.5f);
            AssertPills("the edge is the aim's: a reading that turns refused while armed is told by the click only");
        }

        // ------------------------------------------------------------------ 4. the click that follows

        [Test]
        public void TheClickAfterwardsShowsOnePill()
        {
            BuildRig();
            CoolingWish();
            Arm();
            Frame();
            AssertPills("set-up: the arming shows the line", "Ready in 3");
            byte seq = _client.WishAim.Click(_session, AimFrame(false), out bool consumed);
            Assert.That(consumed, Is.True, "set-up: the click belonged to the aim");
            Assert.That(seq, Is.Not.EqualTo((byte)0), "set-up: the click collected the cast, as today");
            _session.FlushInput();
            Frame();
            Assert.That(_cues.Count, Is.EqualTo(1), "the click still sends one command");
            Assert.That(_cues[0].Kind, Is.EqualTo(CommandKind.Cast), "a Cast");
            Assert.That((int)(_cues[0].Slot ?? 255), Is.EqualTo(0), "of slot 0");
            AssertPills("the line is restarted, never doubled", "Ready in 3");
            Assert.That(_client.WishAim.Aiming, Is.False, "the click put the aim away");
        }

        // ------------------------------------------------------------------ 5. standing guards: green before and after

        [Test]
        public void ArmingAReadyAimSaysNothing()
        {
            BuildRig();
            Snap(50000, Wish(1, 40000, 10000)); Frame();
            Assert.That(ReadingLine(), Is.Null, "set-up: the Wish can be cast");
            Arm();
            Lane(.5f);
            AssertPills("a ready Wish arms silently");
            Assert.That(_client.WishAim.Aiming, Is.True, "and the aim stays armed");
        }

        [Test]
        public void AnEmptyWishSaysNothing()
        {
            BuildRig();
            Snap(50000, null); Frame();
            Assert.That(CastReadiness.Wish(_session.Me.Wish, _session.ServerTimeMs, _session.Me.Effects).Empty, Is.True, "set-up: the Wish slot is empty");
            Arm();
            Lane(.5f);
            AssertPills("an empty Wish arms silently");
            Assert.That(_client.WishAim.Aiming, Is.True, "and the aim stays armed");
        }

        [Test]
        public void QuickCastOnSaysItOnceThroughTheSentCast()
        {
            BuildRig();
            CoolingWish();
            Assert.That(_client.WishAim.Aiming, Is.False, "set-up: the aim is away");
            byte seq = Key(true);
            Assert.That(seq, Is.Not.EqualTo((byte)0), "set-up: with Quick cast on the key collects the cast");
            Assert.That(_client.WishAim.Aiming, Is.False, "the aim is never armed");
            Frame();
            AssertPills("nothing is said before the cast leaves");
            _session.FlushInput();
            Frame();
            AssertPills("the sent cast says it once", "Ready in 3");
            Assert.That(_client.WishAim.Aiming, Is.False, "and the aim was never armed");
            Assert.That(_cues.Count, Is.EqualTo(1), "one cast was sent");
        }

        [Test]
        public void ARebindForgetsTheArm()
        {
            BuildRig();
            CoolingWish();
            Arm();
            Frame();
            Lane(2f);
            AssertPills("set-up: whatever the arming said has run out");
            RebindTo(Wish(1, 40000, 10000));
            Frame();
            Assert.That(_client.WishAim.Aiming, Is.True, "set-up: the aim is armed from the new session's first frame");
            AssertPills("a second session whose Wish is ready says nothing");
        }

        [Test]
        public void ARebindReadsTheFirstArmingOfTheNewSessionFresh()
        {
            BuildRig();
            CoolingWish();
            Arm();
            Frame();
            AssertPills("the first arming shows its line", "Ready in 3");
            Lane(2f);
            AssertPills("set-up: the line has run out");
            RebindTo(Wish(0, 52100, 10000));
            Assert.That(_client.WishAim.Aiming, Is.True, "set-up: the aim is armed from the new session's first frame");
            Frame();
            AssertPills("the new session's first armed frame is read fresh and says why", "Ready in 3");
        }

        [Test]
        public void AHudWithoutAClientTicksWithoutThrowing()
        {
            BuildRig();
            CoolingWish();
            Arm();
            SetF(_hud, "_client", null);
            Assert.DoesNotThrow(() => Frame(), "a HUD with no client does nothing");
            Assert.DoesNotThrow(() => Frame(), "and again");
            AssertPills("and says nothing");
        }

        /// <summary>Binds the HUD to a second session (a rebind: resume, spectate, training) whose Wish is the given one, in the same room.</summary>
        void RebindTo(WireWishOwner wish)
        {
            var second = NewSession(out var transport);
            _hud.Bind(second);
            second.Connect(); transport.Open();
            Hello(transport);
            uint tick = 1; double ms = 50;
            transport.Deliver(new ServerMsg.Snapshot { Tick = tick, ServerTimeOverrideMs = ms, Phase = (byte)MatchPhase.Active, Me = Self(null, 0) });
            SnapOn(transport, ref tick, ref ms, 50000, wish, 0);
            _session = second; _transport = transport; _tick = tick; _serverMs = ms;
            SetF(_hud, "_firstRunHint", false); SetF(_hud, "_feedback", "");
            Assert.That(_session.Me.Faction, Is.EqualTo(FactionId.Frost), "set-up: the second session's fighter is Frost");
            _hud.HudLocalMs = () => 5000;
        }
    }
}
