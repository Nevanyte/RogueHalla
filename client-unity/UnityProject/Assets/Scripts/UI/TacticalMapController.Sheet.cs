using Roguehalla.Net.Ui;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using MapRect = Roguehalla.Net.Ui.MapRect;   // Net.Protocol has a MapRect too: the map screen's is meant here

namespace Roguehalla.Client
{
    /// <summary>
    /// The Tab map's sheet and header (P-UI-TABMAP-SHEET-1): the backing panel behind the wide Tab layout, the header's heading, phase line
    /// and hint with the room rule, the rails' look on the sheet, the one method that shows the panel, and the large map's rect from the layout.
    /// </summary>
    public sealed partial class TacticalMapController
    {
        const float HeaderGap = 16f, CloseInset = 92f;
        RectTransform _sheetRect, _publicMap;
        TMP_Text _phaseLabel;
        string _phaseLine = "";
        int _railsOnSheet = -1;
        // What the header was last laid out for: compared field by field so a steady call allocates nothing.
        bool _headerShown, _headerToggle;
        float _headerWidth;
        string _headerPhase, _headerTitle;
        const string HintToggle = "Right-click to walk · Tab closes", HintHold = "Right-click to walk · release Tab to close";

        void BuildSheet(UiTheme theme)
        {
            _sheetRect = HudUi.Box(_panel, "MapSheet", HudAnchor.TopLeft, 0, 0, 100, 100);
            HudUi.Surface(_sheetRect, UiTheme.With(theme.Surface, .97f), theme.CornerRadiusLarge, theme.BorderStrong, false, false);
        }

        void BuildPhaseLabel(UiTheme theme)
        {
            _phaseLabel = HudUi.Label(_titleRect, "MapPhase", "", 13f, theme.TextPrimary, TextAlignmentOptions.Center);
            // Both lines follow the text-size setting but are never shrunk to their box: the room rule measures their real widths.
            FollowSettingUnshrunk(_phaseLabel); FollowSettingUnshrunk(_hint);
        }

        void FollowSettingUnshrunk(TMP_Text label)
        {
            var scale = label.GetComponent<MythicTextScale>();
            if (scale == null) return;
            scale.FitBox = false;
            scale.Apply(TextScale());
        }

        /// <summary>The HUD's phase line for the map header (null or empty shows nothing); written only when it changes.</summary>
        public void SetPhaseLine(string text)
        {
            text = text ?? "";
            if (text == _phaseLine) return;
            _phaseLine = text;
            if (_phaseLabel != null) _phaseLabel.text = text;
            PresentHeader();
        }

        /// <summary>The one place that shows or hides the panel, and the only writer of <see cref="WorldLabelVisibility.Hidden"/>.</summary>
        void ShowPanel(bool wanted)
        {
            if (_panel != null) _panel.gameObject.SetActive(wanted);
            WorldLabelVisibility.Hidden = _panel != null && _panel.gameObject.activeSelf;
        }

        void ApplySheet(MapScreenLayout L, bool onSheet)
        {
            _sheetRect.gameObject.SetActive(onSheet);
            if (onSheet) Put(_sheetRect, L.Sheet);
            Put(_publicMap, new MapRect(MapScreenLayout.FramePad, MapScreenLayout.FramePad, L.Map.W, L.Map.H));
            int look = onSheet ? 1 : 0;
            if (look != _railsOnSheet)
            {
                _railsOnSheet = look;
                var theme = UiTheme.Current;
                foreach (var rail in new[] { _contactRail, _counterRail })
                {
                    if (onSheet) HudUi.Surface(rail, theme.SurfaceRaised, theme.CornerRadius, null, true, false);
                    else HudUi.Surface(rail, theme.Surface, theme.CornerRadiusLarge, theme.Border, true, true);
                }
            }
            _headerShown = false;
            if (onSheet || _layoutMode == MapScreenMode.Tab) PresentHeader();
        }

        /// <summary>Hint text, Close button and what of the header fits. Decided here and not in the cached layout: the toggle setting changes at any time.</summary>
        void PresentHeader()
        {
            if (_hint == null || _layoutMode != MapScreenMode.Tab) return;
            bool toggle = ToggleMode;
            string hint = toggle ? HintToggle : HintHold;
            float width = _layout.Header.W;
            string title = _title.text;
            if (_headerShown && toggle == _headerToggle && width == _headerWidth && _phaseLine == _headerPhase && title == _headerTitle) return;
            _headerShown = true; _headerToggle = toggle; _headerWidth = width; _headerPhase = _phaseLine; _headerTitle = title;
            _hint.text = hint;
            _close.gameObject.SetActive(toggle);
            float inset = toggle ? CloseInset : 0f;
            var hr = (RectTransform)_hint.transform;
            hr.offsetMax = new Vector2(-inset, hr.offsetMax.y);
            _title.GetComponent<MythicTextScale>()?.Refit();   // the heading box was resized by the layout
            float wh = Preferred(_title), wp = Preferred(_phaseLabel), wt = Preferred(_hint) + inset;
            bool phaseHere = _phaseLine.Length > 0;
            float side = width / 2f - wp / 2f - HeaderGap;
            bool phaseFits = phaseHere && side >= wh;
            _phaseLabel.gameObject.SetActive(phaseFits);
            _hint.gameObject.SetActive(phaseFits ? side >= wt : wh + HeaderGap + wt <= width);
        }

        static float Preferred(TMP_Text label) { return label.GetPreferredValues(label.text, 0f, 0f).x; }
    }
}
