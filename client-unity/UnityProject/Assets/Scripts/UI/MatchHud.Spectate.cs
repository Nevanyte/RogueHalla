using TMPro;
using UnityEngine;
using Roguehalla.Net;

namespace Roguehalla.Client
{
    /// <summary>
    /// HUD part for watching the rest of the match after a final death: the results card's Spectate button and the spectate
    /// bar (who is watched, Next player, Results, Return to lobby). What a spectator sees is the server's (rh-sim
    /// <c>Sim::spectate_eye</c>); this only asks for a player and follows the one delivered.
    /// </summary>
    public sealed partial class MatchHud
    {
        const float SpectateBarBottom = 28f, SpectateBarWidth = 600f, SpectateBarHeight = 56f;
        RectTransform _spectateBar;
        TMP_Text _spectateName;
        UnityEngine.UI.Button _spectateButton;
        uint _spectateShownWatching = uint.MaxValue;

        void CreateSpectateBar()
        {
            var theme = UiTheme.Current;
            _spectateBar = HudUi.Box(_stage, "SpectateBar", HudAnchor.BottomCenter, 0, SpectateBarBottom, SpectateBarWidth, SpectateBarHeight);
            HudUi.Surface(_spectateBar, UiTheme.With(theme.Surface, .94f), theme.CornerRadiusLarge, theme.BorderStrong, true, true);
            _spectateName = LaunchUi.Text(_spectateBar, "Watching", 16f, 8f, 216f, 40f, "", 14f, theme.TextPrimary, TextAlignmentOptions.MidlineLeft, true, UiFontRole.Body, true);
            _spectateName.richText = false;   // player names are typed by players
            var next = LaunchUi.Button(_spectateBar, "NextPlayer", 240f, 8f, 112f, 40f, LaunchUi.Look.Primary, SpectateNext);
            LaunchUi.Text(next.transform, "Label", 0, 0, 112f, 40f, "NEXT PLAYER", 12.5f, theme.OutlineDark, TextAlignmentOptions.Center, true);
            var results = LaunchUi.Button(_spectateBar, "ShowResults", 360f, 8f, 104f, 40f, LaunchUi.Look.Raised, StopSpectating);
            LaunchUi.Text(results.transform, "Label", 0, 0, 104f, 40f, "RESULTS", 12.5f, theme.TextPrimary, TextAlignmentOptions.Center, true);
            var lobby = LaunchUi.Button(_spectateBar, "ReturnToLobby", 472f, 8f, 112f, 40f, LaunchUi.Look.Raised, () => _client.Leave());
            LaunchUi.Text(lobby.transform, "Label", 0, 0, 112f, 40f, "LEAVE MATCH", 12.5f, theme.TextPrimary, TextAlignmentOptions.Center, true);
            _spectateBar.gameObject.SetActive(false);
        }

        Spectator ActiveSpectator => _client != null && _session != null && ReferenceEquals(_client.Session, _session) ? _client.Spectator : null;

        /// <summary>True while this HUD's session is watching someone (the death line and results card stay hidden).</summary>
        bool Spectating { get { var sp = ActiveSpectator; return sp != null && sp.Active; } }

        /// <summary>A final death in a match that is still being played, outside practice: Spectate is offered.</summary>
        bool CanSpectate =>
            _session != null && _session.LastResult != null && _session.LastResult.Outcome == 2 &&
            _session.State == SessionState.InMatch && _session.Phase == MatchPhase.Active &&
            !(_client != null && _client.ConfirmedPractice) && ActiveSpectator != null;

        void StartSpectating()
        {
            var sp = ActiveSpectator;
            if (sp == null || !CanSpectate) return;
            // The killer first when the roster names them; the server moves on to the next standing player if they have fallen.
            uint first = Spectator.RosterIdOf(_session.Roster, _session.LastResult.KilledBy, _session.PlayerId);
            sp.Start(_session, first);
            _results.gameObject.SetActive(false);
            _spectateShownWatching = uint.MaxValue;
            RefreshWorldHudVisibility();
        }

        void SpectateNext() { var sp = ActiveSpectator; if (sp != null) sp.Next(_session); }

        /// <summary>Back to the results card (its Spectate resumes watching).</summary>
        void StopSpectating()
        {
            var sp = ActiveSpectator;
            if (sp != null) sp.Stop();
            if (_session != null && _session.LastResult != null) _results.gameObject.SetActive(true);
            RefreshWorldHudVisibility();
        }

        /// <summary>Per frame: the bar follows the spectator; the match ending (or the session leaving play) returns to the results.</summary>
        void TickSpectate()
        {
            if (_spectateBar == null) return;
            var sp = ActiveSpectator;
            if (sp != null && sp.Active && (_session.State != SessionState.InMatch || _session.Phase == MatchPhase.Ended))
                StopSpectating();
            bool on = sp != null && sp.Active;
            if (_spectateBar.gameObject.activeSelf != on) _spectateBar.gameObject.SetActive(on);
            if (_spectateButton != null)
            {
                bool offer = CanSpectate;
                if (_spectateButton.gameObject.activeSelf != offer) LayoutResultsButtons(offer);
            }
            if (!on) { _spectateShownWatching = uint.MaxValue; return; }
            if (sp.Watching == _spectateShownWatching) return;
            _spectateShownWatching = sp.Watching;
            string name = sp.Watching != 0 ? _session.RosterName(sp.Watching) : null;
            _spectateName.text = sp.Watching == 0 ? "Finding a player to watch…" :
                "SPECTATING\n" + (string.IsNullOrEmpty(name) ? "Player " + sp.Watching : name);
        }
    }
}
