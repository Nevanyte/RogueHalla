using System;
using System.Text;
using Roguehalla.Net;
using Roguehalla.Net.Protocol;
using Roguehalla.Net.Ui;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;

namespace Roguehalla.Client
{
    /// <summary>HUD part for the top-centre banner and phase pills and the toast pill, and the phase pill placement.</summary>
    public sealed partial class MatchHud
    {
        internal const float BannerTop = 12f;

        void CreateTopAndToastPills()
        {
            var theme = UiTheme.Current;
            _bannerPill = HudUi.Box(_stage, "ConnectionBanner", HudAnchor.TopCenter, 0, BannerTop, 380, 28);
            HudUi.Surface(_bannerPill, Tone(theme.Surface, HudUi.PlateAlpha), 14f, theme.BorderStrong, false, true);
            _banner = HudUi.Label(_bannerPill, "Connection", "Choose a faction and join", theme.SizeBody, theme.TextPrimary, TextAlignmentOptions.Center, true);
            _banner.rectTransform.offsetMin = new Vector2(12, 0); _banner.rectTransform.offsetMax = new Vector2(-12, 0);

            _phasePill = HudUi.Box(_stage, "PhaseTimerPill", HudAnchor.TopCenter, 0, BannerTop, 220, 26);
            HudUi.Surface(_phasePill, Tone(theme.Surface, HudUi.PlateAlpha), theme.CornerRadius, theme.Border, false, true);
            _phaseStrip = HudUi.Label(_phasePill, "PhaseTimer", "", theme.SizeBody, theme.TextPrimary, TextAlignmentOptions.Center, true);
            _phasePill.gameObject.SetActive(false);

            CreateToastLanes();
        }

        /// <summary>Keeps the pills in step with their text: toasts only exist while they have text; the banner yields to nothing.</summary>
        void SyncTopCentre()
        {
            if (_bannerPill == null) return;
            // Plain phase captions live on the plate (legacy hud-status); the pill is for connection state only.
            bool inMatch = _session != null && _session.State == SessionState.InMatch;
            bool showBanner = !_hud.gameObject.activeSelf ? false : !inMatch || (_client != null && _client.LaunchPending);
            if (_session != null && (_session.State == SessionState.Reconnecting || _session.State == SessionState.Rejected || _session.State == SessionState.Failed)) showBanner = true;
            // While the launch screen is up its own status line carries connection progress and failures.
            if (_menu != null && _menu.gameObject.activeSelf) showBanner = false;
            // A match that ended with a result: the results card is the truth, not a stale join status (P-UNITY-HUD-VISUAL-FIX-4).
            // LastResult belongs to one Session (a new join builds a new one, MatchClient), so this never hides a later join's notice; Failed and Reconnecting still show.
            if (_session != null && _session.State == SessionState.Rejected && _session.LastResult != null) showBanner = false;
            if (_bannerPill.gameObject.activeSelf != showBanner) _bannerPill.gameObject.SetActive(showBanner);
            bool phase = !showBanner && _hud.gameObject.activeSelf && _phaseStrip != null && !string.IsNullOrEmpty(_phaseStrip.text);
            if (_phasePill.gameObject.activeSelf != phase) _phasePill.gameObject.SetActive(phase);
            _topCentreBottom = showBanner ? BannerTop + _bannerPill.sizeDelta.y : phase ? BannerTop + _phasePill.sizeDelta.y : 0f;
        }

        /// <summary>Writes the top-centre phase line and its tone (P-UNITY-HUD-VISUAL-FIX-1 test seam: the tone must reach the label's colour, never tags in its text).</summary>
        public void ApplyPhaseStrip(string text, Roguehalla.Net.Ui.Indicators.ArenaLineTone tone)
        {
            // Colour through the label, never tags: HudUi.Label is plain text (richText = false). Legacy --warn and .hud-arena.sudden (style.css:44,320).
            _phaseStrip.color = tone == Roguehalla.Net.Ui.Indicators.ArenaLineTone.Danger ? (Color)new Color32(0xff, 0x8a, 0x80, 255)
                : tone == Roguehalla.Net.Ui.Indicators.ArenaLineTone.Warn ? (Color)new Color32(0xff, 0xcc, 0x80, 255) : (Color)UiTheme.Current.TextPrimary;
            // Legacy's line is as wide as its text (hud.ts updateArena): size the pill to the measured text, only when the text changed.
            bool changed = _phaseStrip.text != text;
            _phaseStrip.text = text;
            if (changed && _phasePill != null)
            {
                float w = _phaseStrip.GetPreferredValues(text, 0f, 0f).x;
                _phasePill.sizeDelta = new Vector2(Mathf.Max(120f, Mathf.Ceil(w) + 24f), _phasePill.sizeDelta.y);
                PlacePhasePill();
            }
        }

        /// <summary>
        /// The phase pill is centred at the top; in a narrow window the (now 294 wide) plate would sit under its left end, so the pill moves right
        /// to keep a small gap from the plate's right edge, as far as the window's right edge allows. Wider windows are untouched.
        /// </summary>
        void PlacePhasePill()
        {
            if (_phasePill == null || _viewport.UiWidth <= 0f) return;
            float width = _phasePill.sizeDelta.x, uiWidth = _viewport.UiWidth;
            float left = uiWidth * .5f - width * .5f, minLeft = PlateLeft + HudLayout.PlateWidth(uiWidth) + 8f;
            float shift = Mathf.Clamp(minLeft - left, 0f, Mathf.Max(0f, uiWidth * .5f - width * .5f - 8f));
            HudUi.Place(_phasePill, HudAnchor.TopCenter, shift, BannerTop, width, _phasePill.sizeDelta.y);
        }
    }
}
