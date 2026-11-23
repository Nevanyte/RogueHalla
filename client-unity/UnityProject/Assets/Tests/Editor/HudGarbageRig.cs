using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using NUnit.Framework;
using Roguehalla.Net;
using Roguehalla.Net.Protocol;
using Roguehalla.Net.Ui;
using UnityEngine;
using UnityEngine.EventSystems;

namespace Roguehalla.Client.Tests
{
    /// <summary>
    /// P-UI-HUD-GARBAGE-1: a whole <see cref="MatchHud"/> on a real MatchClient and Session over a fake transport (the rig of
    /// AbilityRowNearbyNoGarbageEditModeTests), with the v17 HUD block, one Fire player holding a Wand, one inventory frame and
    /// a build summary. Wire messages are encoded before a measured window and delivered through the session's own decoder, and the
    /// HUD's own LateUpdate runs after each delivery. The three clocks the HUD reads (SessionOptions.Now, MatchHud.HudLocalMs and
    /// TacticalMapController.SecondsClock) advance 50 ms a delivery; nothing here reads a wall clock. Dispose restores every static.
    /// </summary>
    public sealed class HudGarbageRig : IDisposable
    {
        public const BindingFlags All = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;
        /// <summary>One measured window: a multiple of every cycle in the feeds (64).</summary>
        public const int Calls = 320;
        /// <summary>Samples runs one warm window and up to three measured windows of <see cref="Calls"/>: 4 x Calls + 64 ticks.</summary>
        public const int Feeds = 4 * Calls + 64;

        public enum Kind { Stand, Damage, Ramp }

        sealed class Transport : ITransport
        {
            public event Action Opened;
            public event Action<byte[]> Received;
            public event Action<string> Closed;
            public void Connect(string url) { }
            public void Send(byte[] frame) { }
            public void Close() { Closed?.Invoke("closed"); }
            public void Poll() { }
            public void Open() { Opened?.Invoke(); }
            public void Raise(byte[] frame) { Received?.Invoke(frame); }
            public void Deliver(ServerMsg message) { Received?.Invoke(message.Encode()); }
        }

        /// <summary>Encoded ticks of one kind; Ramp also carries a State frame (build summary variant) a tick.</summary>
        public sealed class Feed
        {
            public byte[][] Snap, St;
            public double[] Ms;
            public int I;
        }

        readonly GameObject _fullRoot, _events;
        readonly EventSystem _priorEvents;
        readonly Transport _transport;
        readonly UiTheme _savedTheme;
        readonly Func<double> _priorClock;
        readonly Action _late;
        readonly WireBuildSummary _baseSummary;

        public readonly MatchClient Client;
        public readonly MatchHud Hud;
        public readonly Session Session;
        public readonly WireHudContent Block;
        public double NowMs = 80d;
        public uint Tick = 1;

        // ------------------------------------------------------------------ the fixture data (v17, as ChampionPanelNumbersEditModeTests)

        static byte[] Server(string name)
        {
            var d = new DirectoryInfo(Application.dataPath);
            while (d != null && !File.Exists(System.IO.Path.Combine(d.FullName, "protocol", "fixtures", "v17.json"))) d = d.Parent;
            Assert.That(d, Is.Not.Null, "protocol/fixtures/v17.json above " + Application.dataPath);
            var m = Regex.Match(File.ReadAllText(System.IO.Path.Combine(d.FullName, "protocol", "fixtures", "v17.json")),
                "\"name\": \"" + Regex.Escape(name) + "\", \"hex\": \"([0-9a-f]+)\"");
            if (!m.Success) throw new InvalidOperationException("no server vector " + name);
            string hex = m.Groups[1].Value;
            return Enumerable.Range(0, hex.Length / 2).Select(i => Convert.ToByte(hex.Substring(i * 2, 2), 16)).ToArray();
        }

        /// <summary>The Welcome HUD block of the v17 fixture; every call decodes a new object.</summary>
        public static WireHudContent FixtureHud() { return ((ServerMsg.Welcome)ServerMsg.Decode(Server("welcome"))).Hud; }
        /// <summary>The fixture's build summary with its champion panel (codes 8, 9, 23 and 24 among them).</summary>
        public static WireBuildSummary FixtureSummary() { return ((BuildSummaryState)((ServerMsg.State)ServerMsg.Decode(Server("state_build_summary_panel_tooltip"))).Items[0]).Value; }

        // ------------------------------------------------------------------ the rig

        public HudGarbageRig(WeaponId weapon = WeaponId.Wand)
        {
            Weapon = weapon;
            _savedTheme = UiTheme.Current;
            UiTheme.Current = UiTheme.Placeholder();
            _priorClock = TacticalMapController.SecondsClock;
            TacticalMapController.SecondsClock = () => NowMs / 1000d;
            _priorEvents = EventSystem.current;
            _events = new GameObject("Garbage events", typeof(UiInputRoutingEventSystem));
            var system = _events.GetComponent<UiInputRoutingEventSystem>(); system.enabled = false; system.enabled = true;
            EventSystem.current = system;
            _baseSummary = FixtureSummary(); Block = FixtureHud();
            foreach (byte code in new byte[] { 8, 9, 23, 24 })
                Assert.That(_baseSummary.Panel.Any(p => p.Code == code), Is.True, "set-up: the fixture panel has code " + code);

            _fullRoot = new GameObject("Garbage fixture");
            Client = _fullRoot.AddComponent<MatchClient>(); Client.enabled = false;
            _transport = new Transport();
            Session = new Session(_transport, new SessionOptions { Url = "ws://garbage/ws", Profile = "garbage", Now = () => NowMs });
            Call(Client, "InitializeLayoutReview", Session); Hud = Client.Hud;
            Hud.HudLocalMs = () => NowMs;
            Session.Connect(); _transport.Open();
            _transport.Deliver(new ServerMsg.Welcome { Protocol = Wire.ProtocolVersion, MatchId = new byte[16], Session = new byte[16],
                PlayerId = 1, TickRate = 20, ServerMs = 50, Hud = Block, Map = new MapBlob(32, 32, new byte[1024]) });
            Tick = 1; NowMs = 80d;
            _transport.Deliver(Snapshot(1, Kind.Stand));
            _transport.Deliver(new ServerMsg.State { Tick = 1, Items = new StateItem[] {
                new InventoryState { Value = new WireInventory { ActiveSlot = 0, Slot0 = (byte)weapon, PotionSmall = 2, PotionLarge = 1 } },
                new BuildSummaryState { Value = Variant(0) } } });
            float factor = HudLayout.CanvasScalerFactor(1280, 800);
            ((HudStage)GetF(Hud, "_stageScaler")).Apply(1280, 800, factor);
            ((HudStage)GetF(Hud, "_chromeScaler")).Apply(1280, 800, factor);
            SetF(Hud, "_nextMetricsAt", double.MaxValue);
            _late = Make<Action>(Hud, "LateUpdate");
            Frame(); Frame();
        }

        public WeaponId Weapon { get; private set; }

        public void Dispose()
        {
            if (_fullRoot != null) UnityEngine.Object.DestroyImmediate(_fullRoot);
            if (_events != null) UnityEngine.Object.DestroyImmediate(_events);
            if (_priorEvents != null) EventSystem.current = _priorEvents;
            if (_savedTheme != null) UiTheme.Current = _savedTheme;
            if (_priorClock != null) TacticalMapController.SecondsClock = _priorClock;
        }

        // ------------------------------------------------------------------ messages

        /// <summary>The roam ramp as the server sends it: charge 2.5 units a tick (panel code 8, 1e-4 units), the speed bonus 0.0005 a
        /// charge unit (code 23 and the Move speed cell, Gains[8]), armed 0 (code 9). Arrays are fresh; MobRows is the fixture's.</summary>
        public WireBuildSummary Variant(int k)
        {
            var v = new WireBuildSummary { Gains = (int[])_baseSummary.Gains.Clone(), MobRows = (WireMobBonusRow[])_baseSummary.MobRows.Clone(),
                Panel = (WirePanelValue[])_baseSummary.Panel.Clone() };
            int speed = k * 125 / 10;
            for (int i = 0; i < v.Panel.Length; i++)
            {
                if (v.Panel[i].Code == 8) v.Panel[i].Value = k * 25000;
                else if (v.Panel[i].Code == 9) v.Panel[i].Value = 0;
                else if (v.Panel[i].Code == 23) v.Panel[i].Value = speed;
            }
            v.Gains[8] = speed;
            return v;
        }

        /// <summary>A snapshot of one Fire player (id 1) and 24 other actors; Damage lowers the hp 7 a tick over a cycle of 64.</summary>
        public ServerMsg.Snapshot Snapshot(uint tick, Kind kind)
        {
            int k = (int)(tick % 320);
            var me = new WireSelf { Id = 1, Faction = 0, X = 300, Y = 300, Facing = 0f, Hp = 950, MaxHp = 950, Status = 1, Weapon = (byte)Weapon,
                Wish = new WireWishOwner { Ability = 3, ReadyAt = 0, RechargeMs = 8000, MaxCharges = 1, Charges = 1 } };
            if (kind == Kind.Damage) me.Hp = 950 - 7 * (k % 64);
            var actors = new WireActor[24];
            for (int i = 0; i < actors.Length; i++)
            {
                bool player = i < 8;
                actors[i] = new WireActor { Id = (uint)(2 + i), X = player ? 900 + i * 40 : 100 + i * 20, Y = 100 + (i * 37) % 300, Faction = (byte)(i % 5), HpPct = 100, Status = 1,
                    Weapon = player ? (byte)(i % 6) : (byte)0, Kind = player ? (byte)0 : (byte)ActorKind.RegionalMob };
            }
            return new ServerMsg.Snapshot { Tick = tick, ServerTimeOverrideMs = tick * 50d, Phase = (byte)MatchPhase.Active, Me = me, Actors = actors,
                OwnerCombat = new WireOwnerCombat { AsMilli = 1000, WindupMilli = 1000, SpeedMilli = 1000 } };
        }

        /// <summary>Encodes <see cref="Feeds"/> ticks, strictly rising, with every message ready before any window.</summary>
        public Feed BuildFeed(Kind kind)
        {
            var f = new Feed { Snap = new byte[Feeds][], St = kind == Kind.Ramp ? new byte[Feeds][] : null, Ms = new double[Feeds] };
            uint t0 = Tick + 1;
            for (int j = 0; j < Feeds; j++)
            {
                uint tick = t0 + (uint)j;
                f.Snap[j] = Snapshot(tick, kind).Encode();
                f.Ms[j] = tick * 50d + 30d;
                if (f.St != null)
                    f.St[j] = new ServerMsg.State { Tick = tick, Items = new StateItem[] { new BuildSummaryState { Value = Variant((int)(tick % 64)) } } }.Encode();
            }
            Tick = t0 + (uint)Feeds;
            return f;
        }

        /// <summary>The delivery of one tick: the clock moves, the snapshot (and in a Ramp feed the State frame) goes through the decoder and handlers.</summary>
        public Action Deliver(Feed f)
        {
            return () => { int i = f.I++; NowMs = f.Ms[i]; _transport.Raise(f.Snap[i]); if (f.St != null) _transport.Raise(f.St[i]); };
        }

        /// <summary>Only the State frame of a Ramp tick (the HUD bound to the session, no frame run).</summary>
        public Action DeliverStateOnly(Feed f)
        {
            return () => { int i = f.I++; NowMs = f.Ms[i]; _transport.Raise(f.St[i]); };
        }

        /// <summary>The HUD's own LateUpdate, as a delegate built once.</summary>
        public Action Late { get { return _late; } }
        public void Frame() { _late(); }

        /// <summary>A prebuilt feed's ticks, one window of deliveries and frames: settles the HUD on a pattern.</summary>
        public void Settle(Kind kind)
        {
            var d = Deliver(BuildFeed(kind));
            for (int i = 0; i < Calls; i++) { d(); _late(); }
        }

        /// <summary>Delivers one snapshot now (the owner as <see cref="Snapshot"/> makes it, then <paramref name="edit"/>); no frame runs.</summary>
        public void Snap(Action<WireSelf> edit = null, byte phase = (byte)MatchPhase.Active)
        {
            Tick++; NowMs += 50d;
            var s = Snapshot(Tick, Kind.Stand); s.Phase = phase;
            edit?.Invoke(s.Me);
            _transport.Deliver(s);
        }

        /// <summary>Delivers one State frame (items in ascending key order); the HUD's collections handler runs at once.</summary>
        public void State(params StateItem[] items)
        {
            Tick++; NowMs += 50d;
            _transport.Deliver(new ServerMsg.State { Tick = Tick, Items = items });
        }

        // ------------------------------------------------------------------ reflection helpers

        public static object GetF(object o, string name)
        {
            var f = o.GetType().GetField(name, All);
            Assert.That(f, Is.Not.Null, "missing field " + name);
            return f.GetValue(o);
        }

        public static void SetF(object o, string name, object value)
        {
            var f = o.GetType().GetField(name, All);
            Assert.That(f, Is.Not.Null, "missing field " + name);
            f.SetValue(o, value);
        }

        public static object Call(object o, string name, params object[] args)
        {
            var m = o.GetType().GetMethod(name, All);
            Assert.That(m, Is.Not.Null, "missing method " + name);
            return m.Invoke(o, args.Length == 0 ? null : args);
        }

        /// <summary>A delegate on a (private) method, found by name and the delegate's own parameter types.</summary>
        public static TDel Make<TDel>(object target, string name) where TDel : Delegate
        {
            var types = typeof(TDel).GetMethod("Invoke").GetParameters().Select(p => p.ParameterType).ToArray();
            var m = target.GetType().GetMethod(name, All, null, types, null);
            Assert.That(m, Is.Not.Null, "missing method " + name);
            return (TDel)Delegate.CreateDelegate(typeof(TDel), m.IsStatic ? null : target, m);
        }
    }
}
