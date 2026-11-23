using System;
using System.Reflection;
using NUnit.Framework;
using Roguehalla.Net;
using Roguehalla.Net.Protocol;
using TMPro;
using UnityEngine;

namespace Roguehalla.Client.Tests
{
    /// <summary>
    /// P-UI-VITALS-EASE-1: the plate's health and barrier fills ease toward the delivered fraction and lost health lingers in a ghost
    /// behind the health fill. Group A drives OwnerVitalFills on bare rects with explicit dt; group B drives a MatchHud through
    /// RefreshOwnerPresentation, TickOwnerVitals and ResetTransientPresentation. Time is the dt argument, never a clock.
    /// </summary>
    public sealed class OwnerVitalEaseEditModeTests
    {
        const float Dt = .05f;
        GameObject _root, _canvas, _owner;
        RectTransform _health, _barrier, _ghost;
        MatchHud _hud;
        TMP_Text _vitals;
        Session _session;
        Fix3Transport _transport;
        uint _tick;
        bool? _savedReduced;
        UiTheme _savedTheme;

        [SetUp]
        public void SetUp()
        {
            _savedReduced = UiMotion.ReducedOverride;
            UiMotion.ReducedOverride = false;
            _savedTheme = UiTheme.Current;
        }

        [TearDown]
        public void TearDown()
        {
            if (_session != null) _session.Leave();
            _session = null;
            if (_root != null) UnityEngine.Object.DestroyImmediate(_root);
            if (_canvas != null) UnityEngine.Object.DestroyImmediate(_canvas);
            if (_owner != null) UnityEngine.Object.DestroyImmediate(_owner);
            _root = _canvas = _owner = null;
            UiMotion.ReducedOverride = _savedReduced;
            UiTheme.Current = _savedTheme;
        }

        // ---- rig helpers ----
        RectTransform NewRect(string name)
        {
            var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            rect.SetParent(_root.transform, false);
            return rect;
        }

        OwnerVitalFills BareFills(bool withGhost = true)
        {
            if (_root == null) _root = new GameObject("vital ease root", typeof(RectTransform));
            _health = NewRect("Health"); _barrier = NewRect("Barrier"); _ghost = withGhost ? NewRect("Ghost") : null;
            return new OwnerVitalFills(_health, _barrier, _ghost);
        }

        static SelfView Me(float hp, float barrier = 0f, ActorStatus status = ActorStatus.Alive, uint id = 0)
        {
            return new SelfView { Id = id, Hp = hp, MaxHp = 100f, Barrier = barrier, Status = status };
        }

        void Ticks(OwnerVitalFills fills, int n) { for (int i = 0; i < n; i++) fills.Tick(Dt, false); }
        void AssertAnchor(float expected, float actual, string what) { Assert.That(actual, Is.EqualTo(expected).Within(.001f), what); }

        // ---- group A: OwnerVitalFills on bare rects ----
        [Test]
        public void TheFirstDeliverySnapsAndWrites()
        {
            var fills = BareFills();
            fills.Deliver(Me(60f, 25f), false);
            AssertAnchor(.60f, _health.anchorMax.x, "health anchorMax");
            AssertAnchor(.60f, _barrier.anchorMin.x, "barrier anchorMin");
            AssertAnchor(.85f, _barrier.anchorMax.x, "barrier anchorMax");
            AssertAnchor(.60f, _ghost.anchorMax.x, "ghost anchorMax");
            Assert.That(fills.Ready, Is.True);
        }

        [Test]
        public void DamageEasesAndTheGhostLingers()
        {
            var fills = BareFills();
            fills.Deliver(Me(100f), false);
            fills.Deliver(Me(70f), false);
            AssertAnchor(1f, _health.anchorMax.x, "a delivery moves nothing");
            AssertAnchor(1f, _ghost.anchorMax.x, "a delivery leaves the ghost");
            float[] ghost = { 1f, 1f, 1f, 1f, 1f, .9f, .8f, .7f };
            for (int i = 0; i < 8; i++)
            {
                fills.Tick(Dt, false);
                AssertAnchor(.70f, _health.anchorMax.x, "health at tick " + (i + 1));
                AssertAnchor(ghost[i], _ghost.anchorMax.x, "ghost at tick " + (i + 1));
            }
            fills.Deliver(Me(100f), false);
            fills.Tick(Dt, false);
            AssertAnchor(.85f, _health.anchorMax.x, "heal tick 1");
            AssertAnchor(.85f, _ghost.anchorMax.x, "the ghost follows a heal");
            fills.Tick(Dt, false);
            AssertAnchor(1f, _health.anchorMax.x, "heal tick 2");
            AssertAnchor(1f, _ghost.anchorMax.x, "ghost after heal tick 2");
        }

        [Test]
        public void AHealKeepsARunningGhost()
        {
            var fills = BareFills();
            fills.Deliver(Me(100f), false);
            fills.Deliver(Me(70f), false);
            Ticks(fills, 2);
            fills.Deliver(Me(71f), false);
            fills.Tick(Dt, false);
            AssertAnchor(.71f, fills.HealthShown, "HealthShown after the heal");
            AssertAnchor(1f, _ghost.anchorMax.x, "the ghost rect is not wiped by the heal");
            Ticks(fills, 3);
            AssertAnchor(.90f, _ghost.anchorMax.x, "the ghost falls after its hold");
        }

        [Test]
        public void TheBarrierEasesItsWidth()
        {
            var fills = BareFills();
            fills.Deliver(Me(60f), false);
            fills.Deliver(Me(60f, 25f), false);
            fills.Tick(Dt, false);
            AssertAnchor(.75f, _barrier.anchorMax.x, "barrier tick 1");
            fills.Tick(Dt, false);
            AssertAnchor(.85f, _barrier.anchorMax.x, "barrier tick 2");
            fills.Deliver(Me(60f), false);
            fills.Tick(Dt, false);
            AssertAnchor(.70f, _barrier.anchorMax.x, "the barrier falls at the rise rate");
            fills.Tick(Dt, false);
            AssertAnchor(.60f, _barrier.anchorMax.x, "barrier gone");
        }

        [Test]
        public void GainingBarrierAtFullHealthShowsNoGhostAndNoDip()
        {
            var fills = BareFills();
            fills.Deliver(Me(100f), false);
            fills.Deliver(Me(100f, 50f), false);
            AssertAnchor(1f, fills.HealthShown, "HealthShown right after the delivery");
            AssertAnchor(1f, _health.anchorMax.x, "health rect right after the delivery");
            float[] health = { .85f, .70f, 2f / 3f, 2f / 3f, 2f / 3f, 2f / 3f };
            for (int i = 0; i < 6; i++)
            {
                fills.Tick(Dt, false);
                AssertAnchor(health[i], fills.HealthShown, "HealthShown at tick " + (i + 1));
                Assert.That(fills.HealthGhost, Is.EqualTo(fills.HealthShown).Within(1e-5f), "ghost equals shown at tick " + (i + 1));
                Assert.That(fills.HealthShown + fills.BarrierShown, Is.GreaterThanOrEqualTo(.99f), "the bar's right edge does not retreat at tick " + (i + 1));
                Assert.That(_barrier.anchorMax.x, Is.LessThanOrEqualTo(1.0001f), "barrier anchorMax within the bar at tick " + (i + 1));
                if (i == 0) AssertAnchor(1f, _barrier.anchorMax.x, "barrier anchorMax at tick 1");
                if (i == 0) AssertAnchor(.85f, fills.HealthGhost, "HealthGhost at tick 1");
            }
            AssertAnchor(2f / 3f, fills.HealthShown, "HealthShown after tick 6");
            AssertAnchor(1f, _barrier.anchorMax.x, "barrier anchorMax after tick 6");
        }

        [Test]
        public void ANewLifeSnaps()
        {
            var fills = BareFills();
            fills.Deliver(Me(90f), false);
            fills.Deliver(Me(50f, 0f, ActorStatus.Dead), false);
            AssertAnchor(.90f, _health.anchorMax.x, "a hit while dying eases");
            fills.Deliver(Me(70f), false);
            AssertAnchor(.70f, _health.anchorMax.x, "alive after dead snaps");
            AssertAnchor(.70f, _ghost.anchorMax.x, "alive after dead snaps the ghost");
            fills.Deliver(Me(80f, 0f, ActorStatus.Alive, 1u), false);
            AssertAnchor(.80f, _health.anchorMax.x, "a different Id snaps (a heal)");
            fills.Deliver(Me(60f, 0f, ActorStatus.Alive, 2u), false);
            AssertAnchor(.60f, _health.anchorMax.x, "a different Id snaps (a fall)");
            AssertAnchor(.60f, _ghost.anchorMax.x, "a different Id snaps the ghost");
        }

        [Test]
        public void ABigHitEasesABigHealSnapsAndReducedMotionSnaps()
        {
            var fills = BareFills();
            fills.Deliver(Me(100f), false);
            fills.Deliver(Me(30f), false);
            fills.Tick(Dt, false);
            AssertAnchor(.70f, _health.anchorMax.x, "a big hit eases");
            AssertAnchor(1f, _ghost.anchorMax.x, "a big hit keeps its ghost");

            var heal = BareFills();
            heal.Deliver(Me(20f), false);
            heal.Deliver(Me(95f), false);
            AssertAnchor(.95f, _health.anchorMax.x, "a rise over .6 snaps the health");
            AssertAnchor(.95f, _ghost.anchorMax.x, "and the ghost");

            var reduced = BareFills();
            reduced.Deliver(Me(100f), false);
            reduced.Deliver(Me(70f), true);
            AssertAnchor(.70f, _health.anchorMax.x, "reduced motion snaps the health");
            AssertAnchor(.70f, _ghost.anchorMax.x, "reduced motion shows no ghost");
        }

        [Test]
        public void ClearZeroesAndForgets()
        {
            var fills = BareFills();
            fills.Deliver(Me(60f, 25f), false);
            Assert.That(fills.Ready, Is.True);
            fills.Clear();
            Assert.That(_health.anchorMax.x, Is.Zero, "health cleared");
            Assert.That(_barrier.anchorMax.x, Is.Zero, "barrier cleared");
            Assert.That(_ghost.anchorMax.x, Is.Zero, "ghost cleared");
            Assert.That(fills.Ready, Is.False);
            fills.Tick(1f, false);
            Assert.That(_health.anchorMax.x, Is.Zero, "a tick after Clear changes nothing");
            Assert.That(_barrier.anchorMax.x, Is.Zero, "a tick after Clear changes nothing (barrier)");
            fills.Deliver(Me(40f), false);
            AssertAnchor(.40f, _health.anchorMax.x, "the next delivery snaps");
            AssertAnchor(.40f, _ghost.anchorMax.x, "the next delivery snaps the ghost");
        }

        [Test]
        public void ASettledFrameAllocatesNothingAndWritesNoRect()
        {
            var fills = BareFills();
            var me = Me(70f);
            fills.Deliver(Me(100f), false);
            fills.Deliver(me, false);
            fills.Tick(1f, false);
            AssertAnchor(.70f, fills.HealthShown, "HealthShown after a long tick");
            AssertAnchor(.70f, _health.anchorMax.x, "health rect after a long tick");
            Action frame = () => { fills.Deliver(me, false); fills.Tick(.016f, false); };
            frame();
            AllocationMeter.AssertNone(frame, 200, "a settled owner vitals frame");
            _health.anchorMax = new Vector2(.33f, 1f);
            frame(); frame(); frame();
            AssertAnchor(.33f, _health.anchorMax.x, "a steady frame writes no rect");

            var bare = BareFills(false);
            var other = Me(50f, 10f);
            Assert.DoesNotThrow(() => { bare.Deliver(other, false); bare.Tick(Dt, false); }, "a null ghost is allowed");
            AssertAnchor(50f / 100f, _health.anchorMax.x, "the null-ghost fills still write the health");
            Assert.DoesNotThrow(() => bare.Deliver(null, false), "a null owner clears");
            Assert.That(_health.anchorMax.x, Is.Zero, "a null owner zeroes the health rect");
        }

        // ---- group B: a MatchHud ----
        void BuildHudBare()
        {
            _root = new GameObject("HUD test root");
            _hud = _root.AddComponent<MatchHud>();
            _vitals = new GameObject("Vitals", typeof(RectTransform), typeof(TextMeshProUGUI)).GetComponent<TextMeshProUGUI>();
            _vitals.transform.SetParent(_root.transform, false);
            _health = NewRect("Health fill"); _barrier = NewRect("Barrier fill"); _ghost = NewRect("Health ghost");
            SetField("_vitals", _vitals);
            SetField("_healthBarFill", _health); SetField("_barrierBarFill", _barrier); SetField("_healthGhostFill", _ghost);
        }

        [Test]
        public void ARefreshDeliversAndATickMoves()
        {
            BuildHudBare();
            Invoke("RefreshOwnerPresentation", Me(100f), 5000d);
            Invoke("RefreshOwnerPresentation", Me(70f), 5050d);
            AssertAnchor(1f, _health.anchorMax.x, "the delivery moves nothing");
            StringAssert.Contains("HP 70", _vitals.text);
            Invoke("TickOwnerVitals", Dt);
            AssertAnchor(.70f, _health.anchorMax.x, "a tick moves the bar");
            AssertAnchor(1f, _ghost.anchorMax.x, "the ghost lingers");

            Invoke("ClearOwnerPresentation", "Awaiting server");
            UiMotion.ReducedOverride = true;
            Invoke("RefreshOwnerPresentation", Me(100f), 6000d);
            Invoke("RefreshOwnerPresentation", Me(70f), 6050d);
            AssertAnchor(.70f, _health.anchorMax.x, "reduced motion reads the delivered value with no tick");
            AssertAnchor(.70f, _ghost.anchorMax.x, "reduced motion shows no ghost");
        }

        void Snapshot(float hp)
        {
            _tick++;
            _transport.Deliver(new ServerMsg.Snapshot
            {
                Tick = _tick, ServerTimeOverrideMs = 1000 + _tick * 50, Phase = (byte)MatchPhase.Active,
                Me = new WireSelf { Id = 1, Faction = (byte)FactionId.Holy, X = 64, Y = 64, Hp = hp, MaxHp = 100, Status = 1, Weapon = 2 },
                Actors = new WireActor[0],
            });
        }

        [Test]
        public void AMidEaseTimelineResetSnaps()
        {
            UiTheme.Current = UiTheme.Placeholder();
            _canvas = new GameObject("vital ease canvas", typeof(RectTransform), typeof(Canvas));
            _owner = new GameObject("vital ease hud owner");
            _hud = _owner.AddComponent<MatchHud>();
            SetField("_root", _canvas.GetComponent<RectTransform>());
            Invoke("EnsureStage"); Invoke("CreateTopAndToastPills"); Invoke("CreateLegacyChrome"); Invoke("CreateCombatHud");
            _transport = new Fix3Transport();
            _session = Fix3Transport.Join(_transport, new MapBlob(4, 4, new byte[16]), null);
            SetField("_session", _session);
            _tick = 1;
            _health = NewRectUnder(_canvas, "Health fill"); _barrier = NewRectUnder(_canvas, "Barrier fill"); _ghost = NewRectUnder(_canvas, "Health ghost");
            SetField("_healthBarFill", _health); SetField("_barrierBarFill", _barrier); SetField("_healthGhostFill", _ghost);

            Snapshot(100f);
            Invoke("RefreshOwnerPresentation", _session.Me, _session.ServerTimeMs);
            Snapshot(70f);
            Invoke("RefreshOwnerPresentation", _session.Me, _session.ServerTimeMs);
            Invoke("TickOwnerVitals", .025f);
            AssertAnchor(.85f, _health.anchorMax.x, "mid-ease, so the premise holds");
            _hud.ResetTransientPresentation();
            AssertAnchor(.70f, _health.anchorMax.x, "a timeline reset snaps the health");
            AssertAnchor(.70f, _ghost.anchorMax.x, "a timeline reset snaps the ghost");
        }

        static RectTransform NewRectUnder(GameObject parent, string name)
        {
            var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            rect.SetParent(parent.transform, false);
            return rect;
        }

        object GetField(string name) => typeof(MatchHud).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(_hud);
        void SetField(string name, object value) => typeof(MatchHud).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(_hud, value);
        void Invoke(string name, params object[] args) => typeof(MatchHud).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public).Invoke(_hud, args.Length == 0 ? null : args);
    }
}
