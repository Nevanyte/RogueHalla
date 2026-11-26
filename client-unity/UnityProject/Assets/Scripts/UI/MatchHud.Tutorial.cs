using System;
using Roguehalla.Net;
using Roguehalla.Net.Protocol;
using Roguehalla.Net.Ui;

namespace Roguehalla.Client
{
    /// <summary>
    /// Tutorial wiring (P-TUTORIAL-3). Presentation only: <see cref="TutorialFlow"/> advances from accepted server snapshots through
    /// <see cref="TutorialObserver"/>; this sends no input. Progress saves are fire-and-forget and never block play.
    /// </summary>
    public sealed partial class MatchHud
    {
        // The Wish key the client binds (MatchClient: R).
        const string TutorialWishKey = "R";
        readonly TutorialFlow _tutFlow = new TutorialFlow();
        TutorialView _tutView;
        TutorialObserver _tutObserver;
        Session _tutObserverSession;
        bool _tutArmed;
        string _tutDismissedFor;
        bool _tutDoneDismissed;

        void InitializeTutorial()
        {
            _tutView = gameObject.AddComponent<TutorialView>();
            _tutView.Initialize(_stage, _chromeStage, StartTutorialFromWelcome, SkipTutorialFromWelcome, SkipTutorial, CloseTutorialDone);
            _modernSettings.ReplayTutorialRequested += ReplayTutorial;
        }

        AccountFullProfile TutorialProfile { get { return _client?.AccountFlow?.Snapshot?.FullProfile; } }

        // First real runs (open alpha): a one-line controls reminder in the bottom toast, shown from the first accepted snapshot
        // and for the first 20 seconds of play, never in Training (the tutorial card covers it). Presentation only.
        internal const string FirstRunHint = "WASD or right-click move · Left-click attack · H help";
        bool _firstRunHint;
        float _firstRunHintPlayed;

        void ArmFirstRunHint()
        {
            var profile = TutorialProfile;
            _firstRunHint = FirstRunHintRules.Owed(profile?.Stats?.Runs);
            _firstRunHintPlayed = 0f;
            if (_firstRunHint) _feedback = FirstRunHint;
        }

        bool FirstRunHintLaneVisible => WorldHudVisible && _hud.gameObject.activeInHierarchy;

        void EndFirstRunHint()
        {
            _firstRunHint = false;
            if (_feedback == FirstRunHint) { _feedback = ""; SetStickyNotice(""); }
        }

        // Per frame (from TickTutorial): counts seconds of play, ends the hint, and puts the line back after a reset.
        void TickFirstRunHint()
        {
            if (!_firstRunHint || _session == null || _client != null && _client.LaunchPending) return;
            var verdict = FirstRunHintRules.Judge(
                practice: _client != null && _client.ConfirmedPractice,
                matchOver: _session.LastResult != null || _session.State == SessionState.Ended,
                inMatch: _session.State == SessionState.InMatch,
                phase: _session.Phase,
                laneVisible: FirstRunHintLaneVisible);
            if (verdict == FirstRunHintVerdict.End) { EndFirstRunHint(); return; }
            if (verdict == FirstRunHintVerdict.Count)
            {
                _firstRunHintPlayed = FirstRunHintRules.Advance(_firstRunHintPlayed, NoticeDtOverride ?? UnityEngine.Time.unscaledDeltaTime);
                if (FirstRunHintRules.Spent(_firstRunHintPlayed)) { EndFirstRunHint(); return; }
            }
            if (string.IsNullOrEmpty(_feedback)) _feedback = FirstRunHint;
            if (_feedback == FirstRunHint) SetStickyNotice(_feedback);
        }

        bool TutorialMenuVisible =>
            _menu != null && _menu.gameObject.activeSelf && !_signIn.gameObject.activeSelf && !_settings.gameObject.activeSelf &&
            !_modernSettings.IsVisible && !AnyHubPanelVisible && !ResumeChooserVisible;

        void StartTutorialFromWelcome()
        {
            var profile = TutorialProfile;
            _tutDismissedFor = profile?.Name;
            if (TutorialMarkerRules.WelcomeAction(profile?.Tutorial, TutorialFlow.Version) == "Resume") _tutFlow.ResumeAt(profile.Tutorial.Step);
            else _tutFlow.Restart();
            if (TutorialMarkerRules.ShouldArm(StartTraining())) BeginTutorialRun();
        }

        void SkipTutorialFromWelcome()
        {
            _tutDismissedFor = TutorialProfile?.Name;
            _tutFlow.Skip(); _tutArmed = false; SaveTutorial("skipped");
        }

        void SkipTutorial()
        {
            _tutFlow.Skip(); _tutArmed = false; SaveTutorial("skipped");
        }

        /// <summary>Settings "Replay tutorial": start over and launch Training (or continue in the current practice session).</summary>
        void ReplayTutorial()
        {
            _tutFlow.Restart(); _tutDoneDismissed = false;
            bool launched = TutorialMenuVisible ? StartTraining() : _client != null && _client.ConfirmedPractice;
            if (TutorialMarkerRules.ShouldArm(launched)) BeginTutorialRun();
        }

        // Close only hides the Done card: never Skip, never save "skipped" (the stored "done" must survive).
        void CloseTutorialDone() { _tutDoneDismissed = true; }

        void BeginTutorialRun()
        {
            _tutArmed = true; _tutDoneDismissed = false;
            if (_tutFlow.Start()) SaveTutorial("step");
        }

        // Fire and forget: a failed or unavailable save is only a result code.
        void SaveTutorial(string state)
        {
            var api = _client?.AccountOwner?.Api;
            if (api == null) return;
            try { var ignored = api.SaveTutorialAsync(TutorialFlow.Version, _tutFlow.Index, state); }
            catch (Exception) { }
        }

        void DropTutorialObserver()
        {
            if (_tutObserver != null) _tutObserver.Dispose();
            _tutObserver = null; _tutObserverSession = null;
        }

        // Same gate as RefreshTrainingConsole: only the real accepted-snapshot subscriber of a confirmed practice session counts.
        void RefreshTutorial(Session session, ServerMsg.Snapshot accepted)
        {
            if (_tutView == null) return;
            bool practice = isActiveAndEnabled && _trainingWindowFocused && _client != null && session != null &&
                ReferenceEquals(_session, session) && ReferenceEquals(_client.Session, session) && ReferenceEquals(session.Latest, accepted) &&
                _client.ConfirmedPractice && session.State == SessionState.InMatch && session.LastResult == null && !TrainingHigherPanelVisible;
            if (!practice || !_tutArmed || !_tutFlow.Running) { if (!practice) DropTutorialObserver(); return; }
            if (!ReferenceEquals(session, _tutObserverSession)) { DropTutorialObserver(); _tutObserver = new TutorialObserver(session); _tutObserverSession = session; }
            if (_tutObserver.TryBuild(out var observation) && _tutFlow.Evaluate(observation))
            {
                if (_tutFlow.Done) { _tutArmed = false; SaveTutorial("done"); }
                else SaveTutorial("step");
            }
        }

        void TickTutorial()
        {
            TickFirstRunHint();
            if (_tutView == null) return;
            var profile = TutorialProfile;
            bool inMatch = _session != null && _session.State == SessionState.InMatch;
            bool welcome = profile != null && !_tutArmed && _tutDismissedFor != profile.Name &&
                TutorialMarkerRules.WelcomeVisible(profile.TutorialDue, TutorialMenuVisible, false, inMatch);
            if (welcome != _tutView.WelcomeShown)
            {
                string action = welcome ? TutorialMarkerRules.WelcomeAction(profile.Tutorial, TutorialFlow.Version) : "Start";
                _tutView.SetWelcome(welcome, action == "Resume" ? "Resume" : "Start",
                    (action == "Resume" ? "Pick up the tutorial where you left off." : "A short guided walk through Training: move, grab a weapon, hit a dummy, swap and cast your Wish.") +
                    "\nNothing in Training is recorded. Press H anytime for controls; replay it from Settings.");
            }

            bool practice = _client != null && _client.ConfirmedPractice;
            if (!practice) _tutDoneDismissed = true; // practice ended: the Done card is gone until a restart
            bool card = TutorialMarkerRules.CardVisible(practice, _trainingWindowFocused,
                TrainingHigherPanelVisible, _tutArmed && _tutFlow.Running);
            bool doneCard = TutorialMarkerRules.DoneCardVisible(practice, _trainingWindowFocused,
                TrainingHigherPanelVisible, _tutFlow.Done, _tutDoneDismissed);
            bool shown = card || doneCard;
            _tutView.SetCard(shown, shown ? TutorialMarkerRules.StepLabel(_tutFlow.Index, TutorialFlow.StepCount - 2, _tutFlow.Copy, TutorialWishKey) : null, doneCard);
            if (card) _tutView.ShowMarker(_client.Cam, _tutFlow.Target);
        }
    }
}
