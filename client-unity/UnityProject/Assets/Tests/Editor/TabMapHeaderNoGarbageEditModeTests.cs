using System;
using System.Reflection;
using NUnit.Framework;
using Roguehalla.Net;
using Roguehalla.Net.Protocol;
using Roguehalla.Net.Ui;
using TMPro;
using UnityEngine;
using MapRect = Roguehalla.Net.Ui.MapRect;   // Net.Protocol has a MapRect too: the map screen's is meant here

namespace Roguehalla.Client.Tests
{
    /// <summary>
    /// P-UI-TABMAP-SHEET-1-fix: the open Tab map's header (<c>TacticalMapController.PresentHeader</c>, called on every paint) allocates nothing
    /// while nothing it depends on changes, and a change of the phase line, the toggle setting or the layout still reaches it. The controller is
    /// built as <see cref="TabMapSheetEditModeTests"/> builds it; the clock is the controller's <see cref="TacticalMapController.SecondsClock"/> seam.
    /// </summary>
    public sealed class TabMapHeaderNoGarbageEditModeTests
    {
        const string LongLine = "Waiting for players 12/16 — run starts in 1:42";

        GameObject _canvas, _stageObject, _clientObject, _controllerObject;
        Fix3Transport _transport;
        Session _session;
        TacticalMapController _controller;
        MatchClient _client;
        RectTransform _stage;
        UiDisplayPreferences _prefs;
        bool _priorToggle;
        Func<double> _priorClock;
        double _now = 100d;

        [SetUp]
        public void SetUp()
        {
            _priorClock = TacticalMapController.SecondsClock;
            TacticalMapController.SecondsClock = () => _now;
            WorldLabelVisibility.Hidden = false;
        }

        [TearDown]
        public void TearDown()
        {
            try
            {
                if (_controller != null) { _controller.SetToggleMode(false); _controller.Bind(null); }
                if (_session != null) _session.Leave();
            }
            finally
            {
                foreach (var go in new[] { _controllerObject, _clientObject, _stageObject, _canvas })
                    if (go != null) UnityEngine.Object.DestroyImmediate(go);
                _controller = null; _session = null;
                TacticalMapController.SecondsClock = _priorClock;
                WorldLabelVisibility.Hidden = false;
                if (_prefs != null) _prefs.SetMapToggle(_priorToggle);
            }
        }

        static Vector2 StageUnits(Vector2 window)
        {
            float scale = Mathf.Clamp(Mathf.Min(window.x / 1280f, window.y / 800f), .8f, 1.5f);
            return new Vector2(window.x / scale, window.y / scale);
        }

        void BuildController(Vector2 window)
        {
            _canvas = new GameObject("header canvas", typeof(RectTransform), typeof(Canvas), typeof(UnityEngine.UI.GraphicRaycaster));
            _stageObject = new GameObject("header stage", typeof(RectTransform));
            _stage = (RectTransform)_stageObject.transform; _stage.SetParent(_canvas.transform, false);
            _stage.anchorMin = _stage.anchorMax = _stage.pivot = new Vector2(.5f, .5f); _stage.sizeDelta = StageUnits(window);
            _transport = new Fix3Transport();
            _session = Fix3Transport.Join(_transport, new MapBlob(4, 4, new byte[16]),
                new WireSelf { Id = 1, Faction = 4, X = 48, Y = 48, Hp = 100, MaxHp = 100, Status = 1, Weapon = (byte)WeaponId.Wand });
            _clientObject = new GameObject("header client");
            _client = _clientObject.AddComponent<MatchClient>(); _client.enabled = false; _client.Url = "ws://localhost/header"; _client.Profile = "fixture";
            _controllerObject = new GameObject("header controller");
            _controller = _controllerObject.AddComponent<TacticalMapController>();
            _prefs = new UiDisplayPreferences(); _priorToggle = _prefs.MapToggle;
            _controller.Initialize(_client, _canvas.transform, _prefs, _stage);
            _controller.SetToggleMode(false);
            _controller.Bind(_session);
            _controller.SetOpen(true);
            Frame();
            Assert.That(_controller.IsOpen, Is.True, "set-up: the Tab map is open");
        }

        /// <summary>One frame: the clock moves a second (past the paint throttle), the controller ticks, the canvas lays out.</summary>
        void Frame()
        {
            _now += 1d;
            _controller.Tick();
            UnityEngine.Canvas.ForceUpdateCanvases();
        }

        Transform Panel => _canvas.transform.Find("TacticalMap");
        TMP_Text Text(string path) { var t = Panel.Find(path); Assert.That(t, Is.Not.Null, "the panel has " + path); return t.GetComponent<TMP_Text>(); }

        Action HeaderAction()
        {
            var method = typeof(TacticalMapController).GetMethod("PresentHeader", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(method, Is.Not.Null, "the controller has a PresentHeader method");
            Assert.That(method.GetParameters().Length, Is.EqualTo(0), "PresentHeader takes no parameters");
            return (Action)Delegate.CreateDelegate(typeof(Action), _controller, method);
        }

        [Test]
        public void AnUnchangedHeaderAllocatesNothing()
        {
            BuildController(new Vector2(1280, 720));
            _controller.SetPhaseLine("Coliseum · sudden death 0:42");
            Frame();
            Assert.That(_controller.IsOpen, Is.True, "set-up: the map is open");
            Assert.That(Text("MapTitle/MapHint").text, Is.Not.Empty, "set-up: the hint has text");
            Assert.That(Text("MapTitle/MapPhase").text, Is.EqualTo("Coliseum · sudden death 0:42"), "set-up: a phase line is set");
            var present = HeaderAction();
            for (int i = 0; i < 5; i++) present();   // warm up

            // GC.GetAllocatedBytesForCurrentThread reads 0 in this editor, so the meter is AllocationMeter (P-UI-ALLOC-METER-1):
            // it counts GC.Alloc samples and checks itself against a known allocating and a known clean control on every call.
            AllocationMeter.AssertNone(present, 200, "PresentHeader with nothing changed");
        }

        /// <summary>Green on the base too: it guards the fix against a cache that sticks when an input changes.</summary>
        [Test]
        public void AChangeStillReachesTheHeader()
        {
            BuildController(new Vector2(1280, 720));
            var present = HeaderAction();
            for (int i = 0; i < 50; i++) present();

            var phase = Text("MapTitle/MapPhase");
            var hint = Text("MapTitle/MapHint");
            var close = Panel.Find("MapTitle/CloseMap");
            Assert.That(hint.text, Is.EqualTo("Right-click to walk · release Tab to close"), "hold mode hint");
            Assert.That(close.gameObject.activeSelf, Is.False, "hold mode: no Close button");

            _controller.SetPhaseLine("Coliseum · sudden death 0:42");
            for (int i = 0; i < 50; i++) present();
            Assert.That(phase.text, Is.EqualTo("Coliseum · sudden death 0:42"), "the new phase line is written");
            Assert.That(phase.gameObject.activeSelf, Is.True, "and a short line shows on a wide header");
            _controller.SetPhaseLine("Coliseum · sudden death 0:41");
            for (int i = 0; i < 50; i++) present();
            Assert.That(phase.text, Is.EqualTo("Coliseum · sudden death 0:41"), "a changed phase line replaces it");

            _controller.SetToggleMode(true);
            _controller.SetOpen(true);
            Frame();
            for (int i = 0; i < 50; i++) present();
            Assert.That(hint.text, Is.EqualTo("Right-click to walk · Tab closes"), "toggle mode changes the hint");
            Assert.That(close.gameObject.activeSelf, Is.True, "toggle mode shows CloseMap");
            Assert.That(-((RectTransform)hint.transform).offsetMax.x, Is.EqualTo(92f).Within(.01f), "and insets the hint for it");

            _controller.SetPhaseLine(LongLine);
            for (int i = 0; i < 50; i++) present();
            Assert.That(phase.gameObject.activeSelf, Is.True, "set-up: the long line fits the 1156 wide header");

            // the window shrinks to 640x480: the header is 392 wide and the long line no longer clears the heading
            _stage.sizeDelta = StageUnits(new Vector2(640, 480));
            Frame();
            for (int i = 0; i < 50; i++) present();
            var layout = MapScreenLayout.Compute(_stage.sizeDelta.x, _stage.sizeDelta.y, MapScreenMode.Tab, 1f);
            Assert.That(layout.Header.W, Is.LessThan(500f), "set-up: the header is narrow now");
            Assert.That(phase.gameObject.activeSelf, Is.False, "a layout at another window re-evaluates what fits: the long line is dropped");
        }
    }
}
