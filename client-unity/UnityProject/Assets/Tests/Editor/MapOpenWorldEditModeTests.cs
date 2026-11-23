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

namespace Roguehalla.Client.Tests
{
    /// <summary>
    /// P-UI-MAP-OPEN-WORLD-1: while <see cref="WorldLabelVisibility.Hidden"/> is true (the Tab map or the planning screen is open) no overhead UI of
    /// any actor is drawn (bars, the overhead cluster, names) and the "Camp patience outlines" root is hidden with the other world roots; the
    /// actors and their footprints stay, and everything is back on the first frame after the flag is false. The presenter tests drive a real
    /// <see cref="SpriteActorPresenter"/> (the rig of NamePackFix4); the HUD test uses the real MatchHud (the rig of TabMapSheet).
    /// </summary>
    public sealed class MapOpenWorldEditModeTests
    {
        sealed class RigTransport : ITransport
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

        // The sprite objects the overhead layer and the bar draw, and the text objects, by name (see the packet).
        static readonly string[] OverheadNames =
        {
            "Health and barrier track", "Confirmed health", "Confirmed barrier", "Health and barrier track outline", "Bounty crown", "Wish icon",
            "Secondary weapon backer", "Mark pip 1", "Stat mark 1", "Elite star", "Elite ring outer", "Hover ring", "Sentenced hunt ring",
            "Attack lock ring", "Rooted ring", "Zoned ring", "Status stun", "Mark brand ring", "Teleport immunity ring", "Barrier bubble",
            "Signature cue plate", "Fire Roam wash", "Name", "Bounty number", "Signature cue",
        };

        GameObject _arena; SpriteActorPresenter _presenter; UiTheme _savedTheme; bool _savedOutline; float _now = 100f;
        GameObject _arenaView, _rigRoot, _events; EventSystem _priorEvents; MatchClient _client; MatchHud _hud; Session _session; RigTransport _rigTransport;
        Func<double> _priorClock; double _clock = 100d; bool? _priorReduced;

        [SetUp]
        public void SetUp()
        {
            _savedTheme = UiTheme.Current; UiTheme.Current = UiTheme.Placeholder();
            _savedOutline = SpriteOutline.Enabled; SpriteOutline.Enabled = true;
            ShowcaseSprites.Initialize();
            _now = 100f; SpriteActorPresenter.FadeClock = () => _now;
            _priorClock = TacticalMapController.SecondsClock; TacticalMapController.SecondsClock = () => _clock;
            _priorReduced = UiMotion.ReducedOverride;
            WorldLabelVisibility.Hidden = false;
            _arena = new GameObject("map open world arena");
            _presenter = new SpriteActorPresenter(_arena.transform, null);
            _presenter.Delivered = new HashSet<uint>();
        }

        [TearDown]
        public void TearDown()
        {
            try
            {
                if (_session != null) _session.Leave();
                if (_client != null) _client.Arena = null;
            }
            finally
            {
                WorldLabelVisibility.Hidden = false;
                SpriteOutline.Enabled = _savedOutline;
                SpriteActorPresenter.FadeClock = null;
                TacticalMapController.SecondsClock = _priorClock;
                UiMotion.ReducedOverride = _priorReduced;
                foreach (var go in new[] { _arena, _arenaView, _rigRoot, _events }) if (go != null) UnityEngine.Object.DestroyImmediate(go);
                if (_priorEvents != null) EventSystem.current = _priorEvents;
                UiTheme.Current = _savedTheme;
                _session = null; _client = null; _hud = null;
            }
        }

        // ---- the presenter rig ----------------------------------------------------------------------------------------------------------

        /// <summary>One frame of the three actors (positions in legacy px, 200 apart so no name merges): P the local fighter, E an elite, L a plain mob.</summary>
        void Frame(bool p = true, bool e = true, bool l = true)
        {
            _presenter.Begin();
            if (p)
            {
                _presenter.Present(1, 640f, 640f, 0, 70f, FactionId.Holy, ActorStatus.Alive, WeaponId.Fists, ActorKind.Player, null,
                    (ushort)(Indicators.Stunned | Indicators.Rooted | Indicators.Zoned), OverheadExtras.TaxedBit, true, 40f, 4);
                _presenter.PresentMarks(1, new OverheadIndicators.Marks { Name = "s1", Tier = 2, Top1 = 1, Top2 = 2, Brand = 50, Immune = true, Roam = true });
                _presenter.PresentIndicators(1, 40, true, WeaponId.Bow, false, -1, false, false, 1, 0);
            }
            if (e)
            {
                _presenter.Present(2, 840f, 640f, 0, 60f, null, ActorStatus.Alive, null, ActorKind.RegionalMob, MobTypeId.Gaoler, 0, 0, false, 0, 0, "grunt");
                _presenter.PresentIndicators(2, 0, false, WeaponId.Fists, false, -1, true);
            }
            if (l)
            {
                _presenter.Present(3, 1040f, 640f, 0, 40f, null, ActorStatus.Alive, null, ActorKind.RegionalMob, MobTypeId.PlagueRat, 0, 0, false, 0, 0, "grunt");
                _presenter.PresentIndicators(3, 0, false, WeaponId.Fists, true, -1, false, true);
            }
            _presenter.End();
        }

        /// <summary>Lets the vision fade of a new actor settle (the pattern of ActorOutlineEditModeTests).</summary>
        void Settle() { _now += 1f; _presenter.StepFades(); }

        List<Transform> Roots()
        {
            var list = new List<Transform>();
            for (int i = 0; i < _arena.transform.childCount; i++) if (_arena.transform.GetChild(i).name == "Observed actor") list.Add(_arena.transform.GetChild(i));
            return list;
        }

        static Component Part(Transform root, string name)
        {
            foreach (var r in root.GetComponentsInChildren<SpriteRenderer>(true)) if (r.gameObject.name == name) return r;
            foreach (var t in root.GetComponentsInChildren<TextMeshPro>(true)) if (t.gameObject.name == name) return t;
            return null;
        }

        /// <summary>True for a sprite that is enabled and active in the hierarchy, and for a text that is active in the hierarchy.</summary>
        static bool Drawn(Transform root, string name)
        {
            var part = Part(root, name);
            if (part == null) return false;
            var sprite = part as SpriteRenderer;
            return sprite != null ? sprite.enabled && sprite.gameObject.activeInHierarchy : part.gameObject.activeInHierarchy;
        }

        bool DrawnOnAny(string name) { foreach (var root in Roots()) if (Drawn(root, name)) return true; return false; }

        List<string> DrawnNames() { var list = new List<string>(); foreach (string n in OverheadNames) if (DrawnOnAny(n)) list.Add(n); return list; }

        // ---- 1 ---------------------------------------------------------------------------------------------------------------------------

        [Test]
        public void NoOverheadObjectIsDrawnWhileTheMapIsOpen()
        {
            Frame(); Settle();
            var missing = new List<string>();
            foreach (string n in OverheadNames) if (!DrawnOnAny(n)) missing.Add(n);
            Assert.That(missing, Is.Empty, "set-up: every overhead object is drawn on some actor while the map is closed");
            Assert.That(_presenter.NameShown(1) && _presenter.NameShown(2), Is.True, "set-up: the fighter and the elite show names");

            WorldLabelVisibility.Hidden = true;
            Frame();
            Assert.That(DrawnNames(), Is.Empty, "drawn while the map is open");
            Assert.That(_presenter.NameShown(1) || _presenter.NameShown(2) || _presenter.NameShown(3), Is.False, "no name is shown");
        }

        // ---- 2 ---------------------------------------------------------------------------------------------------------------------------

        sealed class Record
        {
            public bool ActiveSelf, Enabled, HasRenderer; public Color Color; public Vector3 Position, Scale; public string Text;
        }

        Dictionary<string, Record> Capture()
        {
            var all = new Dictionary<string, Record>();
            foreach (var t in _arena.GetComponentsInChildren<Transform>(true))
            {
                string path = t.name; for (var p = t.parent; p != null && p != _arena.transform.parent; p = p.parent) path = p.name + "[" + p.GetSiblingIndex() + "]/" + path;
                var rec = new Record { ActiveSelf = t.gameObject.activeSelf, Position = t.localPosition, Scale = t.localScale };
                var sprite = t.GetComponent<SpriteRenderer>(); var text = t.GetComponent<TextMeshPro>();
                if (sprite != null) { rec.HasRenderer = true; rec.Enabled = sprite.enabled; rec.Color = sprite.color; }
                if (text != null) { rec.HasRenderer = true; rec.Enabled = text.enabled; rec.Color = text.color; rec.Text = text.text; }
                all[path + "@" + t.GetSiblingIndex()] = rec;
            }
            return all;
        }

        [Test]
        public void EverythingIsBackOnTheFirstFrameAfterTheMapCloses()
        {
            Frame(); Settle(); Frame();
            var before = Capture();

            WorldLabelVisibility.Hidden = true;
            Frame();
            Assert.That(DrawnOnAny("Confirmed health"), Is.False, "set-up: the bar is not drawn while the map is open");

            WorldLabelVisibility.Hidden = false;
            Frame();
            var after = Capture();
            Assert.That(after.Count, Is.EqualTo(before.Count), "the same objects exist");
            var wrong = new List<string>();
            foreach (var pair in before)
            {
                if (!after.TryGetValue(pair.Key, out var now)) { wrong.Add(pair.Key + " is gone"); continue; }
                var was = pair.Value;
                if (now.ActiveSelf != was.ActiveSelf) wrong.Add(pair.Key + " activeSelf " + now.ActiveSelf + " expected " + was.ActiveSelf);
                if (now.HasRenderer && now.Enabled != was.Enabled) wrong.Add(pair.Key + " enabled " + now.Enabled + " expected " + was.Enabled);
                if (now.HasRenderer && !Same(now.Color, was.Color)) wrong.Add(pair.Key + " color " + now.Color + " expected " + was.Color);
                if ((now.Position - was.Position).magnitude > 1e-4f) wrong.Add(pair.Key + " position " + now.Position + " expected " + was.Position);
                if ((now.Scale - was.Scale).magnitude > 1e-4f) wrong.Add(pair.Key + " scale " + now.Scale + " expected " + was.Scale);
                if (now.Text != was.Text) wrong.Add(pair.Key + " text \"" + now.Text + "\" expected \"" + was.Text + "\"");
            }
            Assert.That(wrong, Is.Empty, "differences against the frame before the map opened");

            var p1 = Roots()[0];
            var health = (SpriteRenderer)Part(p1, "Confirmed health");
            Assert.That(health.enabled, Is.True, "P's bar is drawn again");
            Assert.That(health.transform.localScale.x, Is.EqualTo(.468f).Within(.001f), "hp 70 of max(100, 70 + 40) at 28 legacy px, rounded to half a pixel");
            Assert.That(health.color, Is.EqualTo((Color)UiTheme.Current.Success), "above half health is the success colour");
            Assert.That(((TextMeshPro)Part(p1, "Name")).text, Is.EqualTo("s1"));
            Assert.That(((TextMeshPro)Part(p1, "Bounty number")).text, Is.EqualTo("40"));
        }

        static bool Same(Color a, Color b) { return Mathf.Abs(a.r - b.r) < 1e-4f && Mathf.Abs(a.g - b.g) < 1e-4f && Mathf.Abs(a.b - b.b) < 1e-4f && Mathf.Abs(a.a - b.a) < 1e-4f; }

        // ---- 3 (a guard: green before and after) ----------------------------------------------------------------------------------------

        [Test]
        public void TheBodyAndItsFootprintStayDrawnWhileTheMapIsOpen()
        {
            Frame(); Settle();
            WorldLabelVisibility.Hidden = true;
            Frame();
            var roots = Roots();
            Assert.That(roots.Count, Is.EqualTo(3), "three actors");
            Assert.That(_presenter.ActiveCount, Is.EqualTo(3));
            foreach (var root in roots)
            {
                Assert.That(root.gameObject.activeInHierarchy, Is.True, "the actor's root stays active");
                foreach (string n in new[] { "Authored character", "Faction footprint", "Contact shadow" })
                {
                    var part = (SpriteRenderer)Part(root, n);
                    Assert.That(part != null && part.enabled, Is.True, n + " stays drawn");
                }
            }
        }

        // ---- 4 ---------------------------------------------------------------------------------------------------------------------------

        [Test]
        public void AnActorBornOrPooledWhileTheMapIsOpenIsHiddenAndComesBack()
        {
            WorldLabelVisibility.Hidden = true;
            Frame(true, false, false); Settle();
            Assert.That(DrawnNames(), Is.Empty, "a fighter born while the map is open");
            Frame(false, false, false);
            Assert.That(_presenter.ActiveCount, Is.EqualTo(0), "set-up: the fighter's art went to the pool");
            Frame(false, true, false); Settle();
            Assert.That(_presenter.ActiveCount, Is.EqualTo(1), "set-up: the elite is presented");
            Assert.That(DrawnNames(), Is.Empty, "an elite that took the pooled art while the map is open");

            WorldLabelVisibility.Hidden = false;
            Frame(false, true, false);
            var root = Roots().Find(r => r.gameObject.activeInHierarchy);
            Assert.That(root, Is.Not.Null, "the elite's root");
            foreach (string n in new[] { "Confirmed health", "Elite star", "Elite ring outer" }) Assert.That(Drawn(root, n), Is.True, n + " is back");
            Assert.That(Drawn(root, "Name"), Is.True, "the name is back");
            Assert.That(((TextMeshPro)Part(root, "Name")).text, Is.EqualTo("Gaoler"));
        }

        // ---- 5 ---------------------------------------------------------------------------------------------------------------------------

        ArenaView BuildArena(out List<SpriteRenderer> marks, out SpriteRenderer offMark)
        {
            _arenaView = new GameObject("map open world arena view");
            var arena = _arenaView.AddComponent<ArenaView>();
            if (_arenaView.transform.Find("World markers") == null)
                typeof(ArenaView).GetMethod("Awake", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(arena, null);
            marks = new List<SpriteRenderer>();
            foreach (string name in new[] { "World markers", "Camp patience outlines", "World indicators", "Quest carrier flags" })
            {
                var root = _arenaView.transform.Find(name);
                Assert.That(root, Is.Not.Null, "the arena has the root " + name);
                var mark = new GameObject(name + " test mark", typeof(SpriteRenderer)); mark.transform.SetParent(root, false);
                marks.Add(mark.GetComponent<SpriteRenderer>());
            }
            var off = new GameObject("a mark that was not showing", typeof(SpriteRenderer));
            off.transform.SetParent(_arenaView.transform.Find("World markers"), false); off.GetComponent<SpriteRenderer>().enabled = false;
            offMark = off.GetComponent<SpriteRenderer>();
            return arena;
        }

        [Test]
        public void TheOutlinesRootIsHiddenWithTheWorldMarks()
        {
            var arena = BuildArena(out var marks, out var off);
            foreach (var r in marks) Assert.That(r.enabled && r.gameObject.activeInHierarchy, Is.True, "set-up: " + r.name + " is showing");

            arena.SetWorldMarksVisible(false);
            Assert.That(_arenaView.transform.Find("Camp patience outlines").gameObject.activeSelf, Is.False, "the outlines root is hidden");
            foreach (var r in marks) Assert.That(r.gameObject.activeInHierarchy, Is.False, r.name + " is hidden while the map is open");
            foreach (string name in new[] { "World markers", "World indicators", "Quest carrier flags" })
                Assert.That(_arenaView.transform.Find(name).gameObject.activeSelf, Is.False, name + " root is hidden");

            arena.SetWorldMarksVisible(true);
            Assert.That(_arenaView.transform.Find("Camp patience outlines").gameObject.activeSelf, Is.True, "the outlines root is back");
            foreach (var r in marks) Assert.That(r.enabled && r.gameObject.activeInHierarchy, Is.True, r.name + " is back");
            Assert.That(off.enabled, Is.False, "a mark that was not showing stays off");
        }

        // ---- 6 ---------------------------------------------------------------------------------------------------------------------------

        void BuildHud()
        {
            _priorEvents = EventSystem.current;
            _events = new GameObject("Map open events", typeof(UiInputRoutingEventSystem));
            var system = _events.GetComponent<UiInputRoutingEventSystem>(); system.enabled = false; system.enabled = true;
            EventSystem.current = system;
            _rigRoot = new GameObject("Map open HUD fixture");
            _client = _rigRoot.AddComponent<MatchClient>(); _client.enabled = false;
            _rigTransport = new RigTransport();
            _session = new Session(_rigTransport, new SessionOptions { Url = "ws://map-open/ws", Profile = "map-open", Now = () => 1000 });
            typeof(MatchClient).GetMethod("InitializeLayoutReview", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public).Invoke(_client, new object[] { _session });
            _hud = _client.Hud;
            _session.Connect(); _rigTransport.Open();
            _rigTransport.Deliver(new ServerMsg.Welcome { Protocol = Wire.ProtocolVersion, MatchId = new byte[16], Session = new byte[16], PlayerId = 1,
                TickRate = 20, ServerMs = 50, Map = new MapBlob(32, 32, new byte[1024]) });
        }

        void Snapshot(MatchPhase phase) =>
            _rigTransport.Deliver(new ServerMsg.Snapshot { Tick = 1, ServerTimeOverrideMs = 50, Phase = (byte)phase,
                Me = new WireSelf { Id = 1, X = 64, Y = 64, Hp = 100, MaxHp = 100, Status = 1, Weapon = 0 } });

        static void Call(object owner, string name) => owner.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(owner, null);

        [Test]
        public void TheOpenEdgeHidesAndRestoresTheOutlinesRootThroughTheHud()
        {
            BuildHud();
            var arena = BuildArena(out _, out _);
            _client.Arena = arena;
            Snapshot(MatchPhase.Active);
            var outlines = _arenaView.transform.Find("Camp patience outlines").gameObject;
            Assert.That(outlines.activeSelf, Is.True, "set-up: the outlines root shows");

            _hud.TacticalMap.SetOpen(true);
            Call(_hud, "RefreshWorldHudVisibility");
            Assert.That(WorldLabelVisibility.Hidden, Is.True, "the open map sets the flag");
            Assert.That(outlines.activeSelf, Is.False, "the open edge hides the outlines root");

            _hud.TacticalMap.SetOpen(false);
            Call(_hud, "RefreshWorldHudVisibility");
            Assert.That(WorldLabelVisibility.Hidden, Is.False, "the closed map clears the flag");
            Assert.That(outlines.activeSelf, Is.True, "the close edge restores the outlines root");
        }

        // ---- 7 ---------------------------------------------------------------------------------------------------------------------------

        OverheadIndicators.Facts _facts;

        [Test]
        public void AHiddenClusterFrameAllocatesNothing()
        {
            var go = new GameObject("map open actor");
            try
            {
                var ind = new OverheadIndicators(go.transform);
                _facts = new OverheadIndicators.Facts { Order = 2000, HeadY = 1.68f, BarY = 1.8f, HpPct = 100f, Bounty = 40, Effects = Indicators.Stunned,
                    M = new OverheadIndicators.Marks { Name = "s1" } };
                ind.Update(in _facts);
                Assert.That(go.transform.Find("Bounty crown").GetComponent<SpriteRenderer>().enabled, Is.True, "set-up: the crown shows while the map is closed");

                WorldLabelVisibility.Hidden = true;
                ind.Update(in _facts);
                Assert.That(go.transform.Find("Bounty crown").GetComponent<SpriteRenderer>().enabled, Is.False, "the crown is not drawn while the map is open");
                Assert.That(ind.NameActive, Is.False, "the name is not drawn while the map is open");

                System.Action frame = () => ind.Update(in _facts);
                AllocationMeter.AssertNone(frame, 200, "a steady hidden overhead frame");
            }
            finally { UnityEngine.Object.DestroyImmediate(go); }
        }
    }
}
