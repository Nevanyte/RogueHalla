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

namespace Roguehalla.Client.Tests
{
    /// <summary>
    /// P-UI-RUN-END-1: nothing that cannot act stays on the results screen (the Quests launcher leaves with the result, the banner pill never
    /// stands over an ended run) and the end of a run reads "Run over" on every surface. The rig is the full rig of HudWordsEditModeTests (a
    /// whole MatchHud on a real MatchClient over a fake transport). Nothing here reads a clock: two fields are set by reflection,
    /// _nextMetricsAt (0 makes TickMetrics run on the next frame) and _pendingResultAt (a defeat's card waits 1.5 s: MaxValue holds it, 0 releases it).
    /// </summary>
    public sealed class RunEndEditModeTests
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
        uint _tick = 20;

        void BuildFullRig(bool welcome = true)
        {
            _savedTheme = UiTheme.Current;
            UiTheme.Current = UiTheme.Placeholder();
            _priorEvents = EventSystem.current;
            _events = new GameObject("Run end events", typeof(UiInputRoutingEventSystem));
            var system = _events.GetComponent<UiInputRoutingEventSystem>(); system.enabled = false; system.enabled = true;
            EventSystem.current = system;
            _fullRoot = new GameObject("Run end fixture");
            _client = _fullRoot.AddComponent<MatchClient>(); _client.enabled = false;
            _transport = new Transport();
            _session = new Session(_transport, new SessionOptions { Url = "ws://runend/ws", Profile = "runend", Now = () => 1000 });
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

        /// <summary>One frame of the HUD's late update plus the player-flow tick; each call is set dirty first.</summary>
        void Frame() { SetF(_hud, "_dirty", true); Call(_hud, "LateUpdate"); Call(_hud, "TickPlayerFlows"); }

        /// <summary>Seats Ash (1, the player) and Quill (2); the winner's name is read from the roster, so this goes before the event.</summary>
        void Roster()
        {
            Snap(10, 100);
            Items(10, new RosterState { Rows = new[] { new WireRosterRow { Id = 1, Name = "Ash" }, new WireRosterRow { Id = 2, Name = "Quill" } } });
        }

        void Event(WireEvent e) => _transport.Deliver(new ServerMsg.Events { Tick = ++_tick, List = new List<WireEvent> { e } });
        void Ended(uint? winner = 2, bool draw = false) => Event(new WireEvent { Kind = EventKind.MatchEnded, Winner = winner, Draw = draw });
        void Defeat() => Event(new WireEvent { Kind = EventKind.Result, Result = new WireResult { Outcome = 2, Place = 2, Players = 2, KilledBy = "Quill", DurationSec = 196 } });
        void Win() => Event(new WireEvent { Kind = EventKind.Result, Result = new WireResult { Outcome = 0, Place = 1, Players = 2, DurationSec = 196 } });

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

        /// <summary>Whether the named field of the HUD (a component or an object) is active itself.</summary>
        bool On(string field)
        {
            var o = GetF(_hud, field);
            Assert.That(o, Is.Not.Null, field + " is built");
            return o is Component c ? c.gameObject.activeSelf : ((GameObject)o).activeSelf;
        }
        bool CardUp => On("_results");
        string Strip => ((TMP_Text)GetF(_hud, "_phaseStrip")).text;
        TMP_Text ResultNote => (TMP_Text)GetF(_hud, "_resultNote");

        const string RunOverQuill = "Run over — Quill is victorious";

        // ------------------------------------------------------------------ the top of the screen after the end

        [Test]
        public void ADefeatHidesTheBannerAndShowsTheRunOverLine()
        {
            BuildFullRig();
            Roster();
            SetF(_hud, "_nextMetricsAt", 0d);
            Defeat(); Ended(2);
            Frame();
            SetF(_hud, "_pendingResultAt", float.MaxValue);
            Frame();
            Assert.That(_session.State, Is.EqualTo(SessionState.Ended), "set-up: the match has ended");
            Assert.That((bool)GetF(_hud, "_matchEnded"), Is.True, "set-up: the event was seen");
            Assert.That(CardUp, Is.False, "set-up: the defeat's card waits for its delay");
            Assert.That(On("_bannerPill"), Is.False, "no banner over a finished run (before the card)");
            Assert.That(On("_phasePill"), Is.True, "the run-over line stands on the phase pill (before the card)");
            Assert.That(Strip, Is.EqualTo(RunOverQuill), "the winner (before the card)");

            SetF(_hud, "_pendingResultAt", 0f);
            Frame();
            Assert.That(CardUp, Is.True, "set-up: the card is up");
            Assert.That(On("_bannerPill"), Is.False, "no banner over the card");
            Assert.That(On("_phasePill"), Is.True, "the line stays with the card");
            Assert.That(Strip, Is.EqualTo(RunOverQuill), "the winner with the card");
        }

        [Test]
        public void TheTopPillDoesNotFollowTheCardAcrossAFlow()
        {
            BuildFullRig();
            Roster();
            SetF(_hud, "_nextMetricsAt", 0d);
            Defeat(); Ended(2);
            Frame();
            SetF(_hud, "_pendingResultAt", 0f);
            Frame();
            Assert.That(_session.State, Is.EqualTo(SessionState.Ended), "set-up: ended");
            Assert.That(CardUp, Is.True, "set-up: the card is up");
            _hud.ShowPlayerFlow(2);
            Frame();
            Assert.That(CardUp, Is.False, "set-up: the notices page hides the card");
            Assert.That(_hud.ServerNotices.IsVisible, Is.True, "set-up: a flow is visible");
            Assert.That(On("_bannerPill"), Is.False, "no banner behind the flow");
            Assert.That(On("_phasePill"), Is.True, "the line does not follow the card away");
            Call(_hud, "ClosePlayerFlows");
            Frame();
            Assert.That(CardUp, Is.True, "set-up: closing the flow returns the card");
            Assert.That(On("_bannerPill"), Is.False, "no banner with the card back");
            Assert.That(On("_phasePill"), Is.True, "the line with the card back");
        }

        [TestCase(true, 0u)]
        [TestCase(false, 9u)]
        public void ADrawOrAnUnknownWinnerReadsBareRunOver(bool draw, uint winner)
        {
            BuildFullRig();
            Roster();
            Ended(draw ? (uint?)null : winner, draw);
            SetF(_hud, "_nextMetricsAt", 0d);
            Frame();
            Assert.That(_session.State, Is.EqualTo(SessionState.Ended), "set-up: ended");
            Assert.That(Strip, Is.EqualTo("Run over"), "no winner to name");
            Assert.That(On("_phasePill"), Is.True, "the line is shown");
            Assert.That(On("_bannerPill"), Is.False, "no banner over an ended run");
        }

        [Test]
        public void TheStripIsCurrentOnTheFrameTheEndArrives()
        {
            BuildFullRig();
            Snap(5, 80, (byte)MatchPhase.Lobby);
            SetF(_hud, "_nextMetricsAt", 0d);
            Frame();
            Assert.That(Strip, Is.EqualTo("FORMING MATCH"), "set-up: the lobby line");
            SetF(_hud, "_nextMetricsAt", double.MaxValue);
            Roster();
            Ended(2);
            Frame();
            Assert.That(_session.State, Is.EqualTo(SessionState.Ended), "set-up: ended");
            Assert.That(Strip, Is.EqualTo(RunOverQuill), "the strip is not up to a quarter second stale");
            Assert.That(On("_phasePill"), Is.True, "the line is shown");
        }

        [Test]
        public void AnEndWithTheEventAndNoResultShowsRunOverOnTheTop()
        {
            BuildFullRig();
            Roster();
            Ended(2);
            SetF(_hud, "_nextMetricsAt", 0d);
            Frame();
            Assert.That(_session.State, Is.EqualTo(SessionState.Ended), "set-up: ended");
            Assert.That(_session.LastResult, Is.Null, "set-up: no result was delivered");
            Assert.That(CardUp, Is.True, "set-up: the card is up");
            Assert.That(ResultNote.text, Is.EqualTo("Run over\nNo authoritative result received."), "the card's note");
            Assert.That(On("_bannerPill"), Is.False, "no banner over the card");
            Assert.That(On("_phasePill"), Is.True, "the line is shown");
            Assert.That(Strip, Is.EqualTo(RunOverQuill), "the winner");
            Assert.That(On("_journalButton"), Is.False, "no launcher under a card with no result");
        }

        [Test]
        public void AnEndWithoutTheEventShowsNoTopPill()
        {
            BuildFullRig();
            Frame();
            _session.Leave();
            SetF(_hud, "_nextMetricsAt", 0d);
            Frame();
            Assert.That(_session.State, Is.EqualTo(SessionState.Ended), "set-up: ended");
            Assert.That((bool)GetF(_hud, "_matchEnded"), Is.False, "set-up: no MatchEnded event was seen");
            Assert.That(ResultNote.text, Is.EqualTo("Run over\nNo authoritative result received."), "the card's note");
            Assert.That(Strip, Is.Empty, "no line without the event");
            Assert.That(On("_phasePill"), Is.False, "no phase pill without the event");
            Assert.That(On("_bannerPill"), Is.False, "no banner over an ended run");
        }

        [Test]
        public void GUARD_TheEndedTermKeepsAStaleLineOffTheTop()
        {
            BuildFullRig();
            Snap(5, 80, (byte)MatchPhase.Lobby);
            SetF(_hud, "_nextMetricsAt", 0d);
            Frame();
            Assert.That(Strip, Is.EqualTo("FORMING MATCH"), "set-up: the lobby line");
            Assert.That(On("_phasePill"), Is.True, "set-up: the pill shows it");
            SetF(_hud, "_nextMetricsAt", double.MaxValue);
            _session.Leave();
            Frame();
            Assert.That(_session.State, Is.EqualTo(SessionState.Ended), "set-up: ended");
            Assert.That((bool)GetF(_hud, "_matchEnded"), Is.False, "set-up: no MatchEnded event");
            Assert.That(Strip, Is.EqualTo("FORMING MATCH"), "set-up: the strip is stale");
            Assert.That(On("_phasePill"), Is.False, "a stale in-match line is not shown over an ended run");
        }

        // ------------------------------------------------------------------ the launcher

        [Test]
        public void TheLauncherLeavesAtTheDefeatResultAndStaysGone()
        {
            BuildFullRig();
            Frame();
            Assert.That(_session.State, Is.EqualTo(SessionState.InMatch), "set-up: running match");
            Assert.That(_session.LastResult, Is.Null, "set-up: no result yet");
            Assert.That(CardUp, Is.False, "set-up: no card");
            Assert.That(On("_journalButton"), Is.True, "GUARD: the launcher shows in a running match");

            Defeat();
            Frame();
            SetF(_hud, "_pendingResultAt", float.MaxValue);
            Frame();
            Assert.That(_session.State, Is.EqualTo(SessionState.InMatch), "set-up: the match goes on");
            Assert.That(_session.LastResult, Is.Not.Null, "set-up: the result is here");
            Assert.That(CardUp, Is.False, "set-up: the card waits for its delay");
            Assert.That(On("_journalButton"), Is.False, "the launcher is gone with the result, before the card");

            SetF(_hud, "_pendingResultAt", 0f);
            Frame();
            Assert.That(CardUp, Is.True, "set-up: the card is up");
            Assert.That(On("_journalButton"), Is.False, "the launcher stays gone under the card");
        }

        [Test]
        public void TheLauncherReturnsWhileSpectatingAndLeavesWithTheCardAgain()
        {
            BuildFullRig();
            Roster();
            Defeat();
            Frame();
            SetF(_hud, "_pendingResultAt", 0f);
            Frame();
            Assert.That(CardUp, Is.True, "set-up: the card is up");
            Assert.That(_session.State, Is.EqualTo(SessionState.InMatch), "set-up: the match goes on");
            var spectate = (UnityEngine.UI.Button)GetF(_hud, "_spectateButton");
            Assert.That(spectate.gameObject.activeSelf, Is.True, "set-up: Spectate is offered");
            spectate.onClick.Invoke();
            Frame();
            Assert.That(CardUp, Is.False, "set-up: spectating hides the card");
            Assert.That(On("_spectateBar"), Is.True, "set-up: the spectate bar is up");
            Assert.That(On("_journalButton"), Is.True, "GUARD: the launcher returns while spectating");
            Call(_hud, "StopSpectating");
            Frame();
            Assert.That(CardUp, Is.True, "set-up: the card is back");
            Assert.That(On("_spectateBar"), Is.False, "set-up: the bar is gone");
            Assert.That(On("_journalButton"), Is.False, "the launcher leaves with the card again");
        }

        [Test]
        public void JOpensTheNoticesAfterTheEndAndCloseReturnsToACardWithoutALauncher()
        {
            BuildFullRig();
            Roster();
            Win(); Ended(2);
            Frame();
            Assert.That(_session.State, Is.EqualTo(SessionState.Ended), "set-up: ended");
            Assert.That(_session.LastResult, Is.Not.Null, "set-up: a result");
            Assert.That(CardUp, Is.True, "set-up: the card is up at once");
            WithKeyboard(keyboard =>
            {
                InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.J)); InputSystem.Update();
                _hud.ReadUiInput();
                Assert.That(_hud.ServerNotices.IsVisible, Is.True, "GUARD: J opens the notices page after the end");
                Assert.That(_hud.QuestJournal.IsVisible, Is.False, "GUARD: not the quest journal");
                Assert.That(CardUp, Is.False, "GUARD: the card steps aside");
                InputSystem.QueueStateEvent(keyboard, new KeyboardState()); InputSystem.Update();
                InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.J)); InputSystem.Update();
                _hud.ReadUiInput();
                Assert.That(_hud.ServerNotices.IsVisible, Is.False, "GUARD: J again closes the notices");
                Assert.That(CardUp, Is.True, "GUARD: the card returns");
            });
            Frame();
            Assert.That(On("_journalButton"), Is.False, "no launcher with the card back");
        }

        // ------------------------------------------------------------------ the word

        [Test]
        public void TheEndedPhaseWordIsRunOver()
        {
            BuildFullRig();
            SetF(_hud, "_nextMetricsAt", 0d);
            Snap(5, 80, (byte)MatchPhase.Ended);
            Frame();
            Assert.That(_session.State, Is.EqualTo(SessionState.InMatch), "set-up: the state is still InMatch");
            Assert.That(((TMP_Text)GetF(_hud, "_banner")).text, Is.EqualTo("Run over"), "the banner's word");
            Assert.That(((TMP_Text)GetF(_hud, "_plateStatus")).text, Is.EqualTo("Run over"), "the plate's word");
            Assert.That(Strip, Is.EqualTo("Run over"), "GUARD: the strip");
            Assert.That(On("_phasePill"), Is.True, "GUARD: the phase pill");
            Assert.That(On("_bannerPill"), Is.False, "GUARD: no banner pill");
        }

        [Test]
        public void GUARD_TheOtherPhaseWordsAreUntouched()
        {
            BuildFullRig();
            var status = (TMP_Text)GetF(_hud, "_plateStatus");
            Snap(5, 80, (byte)MatchPhase.Active); Frame();
            Assert.That(status.text, Is.EqualTo("Match active"));
            Snap(6, 90, (byte)MatchPhase.Lobby); Frame();
            Assert.That(status.text, Is.EqualTo("Lobby — waiting for players"));
            Snap(7, 100, (byte)MatchPhase.Planning); Frame();
            Assert.That(status.text, Is.EqualTo("Planning — choose your spawn"));
        }

        // ------------------------------------------------------------------ what keeps its pill

        [Test]
        public void GUARD_ReconnectingStillShowsItsPill()
        {
            BuildFullRig();
            Frame();
            _transport.Drop();
            Frame();
            Assert.That(_session.State, Is.EqualTo(SessionState.Reconnecting), "set-up: the link dropped");
            Assert.That(On("_bannerPill"), Is.True);
            Assert.That(On("_phasePill"), Is.False);
        }

        [Test]
        public void GUARD_FailedStillShowsItsPill()
        {
            BuildFullRig(welcome: false);
            _transport.Drop();
            Frame();
            Assert.That(_session.State, Is.EqualTo(SessionState.Failed), "set-up: failed");
            Assert.That(On("_bannerPill"), Is.True);
        }

        [Test]
        public void GUARD_ARejectedJoinWithAResultStillHidesItsPill()
        {
            BuildFullRig();
            Defeat();
            _transport.Deliver(new ServerMsg.Reject { Code = RejectCode.Kicked, Reason = "closed" });
            Frame();
            Assert.That(_session.State, Is.EqualTo(SessionState.Rejected), "set-up: rejected");
            Assert.That(_session.LastResult, Is.Not.Null, "set-up: a result");
            Assert.That(On("_bannerPill"), Is.False);
        }

        [Test]
        public void GUARD_ARejectedJoinWithNoResultStillShowsItsPill()
        {
            BuildFullRig();
            _transport.Deliver(new ServerMsg.Reject { Code = RejectCode.Kicked, Reason = "closed" });
            Frame();
            Assert.That(_session.State, Is.EqualTo(SessionState.Rejected), "set-up: rejected");
            Assert.That(_session.LastResult, Is.Null, "set-up: no result");
            Assert.That(On("_bannerPill"), Is.True);
        }

        // Copied from PlayerFlowsEditModeTests: a real keyboard whose input settings and device are restored in a finally.
        static void WithKeyboard(Action<Keyboard> action)
        {
            var original = InputSystem.settings; var flags = original.hideFlags; var previous = Keyboard.current;
            var settings = ScriptableObject.Instantiate(original); Keyboard keyboard = null;
            try
            {
                settings.updateMode = InputSettings.UpdateMode.ProcessEventsInDynamicUpdate;
                settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
                settings.SetInternalFeatureFlag("RUN_PLAYER_UPDATES_IN_EDIT_MODE", true);
                if (flags == HideFlags.HideAndDontSave) original.hideFlags = HideFlags.DontSave;
                InputSystem.settings = settings; keyboard = InputSystem.AddDevice<Keyboard>(); keyboard.MakeCurrent();
                action(keyboard);
            }
            finally
            {
                if (keyboard != null && keyboard.added) InputSystem.RemoveDevice(keyboard);
                if (original != null) { InputSystem.settings = original; original.hideFlags = flags; }
                if (settings != null) UnityEngine.Object.DestroyImmediate(settings);
                if (previous != null && previous.added) previous.MakeCurrent();
            }
        }
    }
}
