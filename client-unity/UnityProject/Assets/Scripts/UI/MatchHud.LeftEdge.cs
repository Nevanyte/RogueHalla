using System.Collections.Generic;
using Roguehalla.Net.Ui;
using TMPro;
using UnityEngine;

namespace Roguehalla.Client
{
    /// <summary>
    /// The two folds of the left edge (P-UI-LEFT-EDGE-1): the plate's GAINED table folds into its header line and the champion
    /// combat panel folds into its head row. Open by default; a click stores the choice on the device, nothing else does.
    /// </summary>
    public sealed partial class MatchHud
    {
        const float GainedHeaderHeight = 14f, ChampionFoldedHeight = 28f;
        bool _gainsFolded, _championFolded;
        RectTransform _gainsHit, _championBody;
        TMP_Text _gainsHeader, _championGlyph;
        HudIconHint _gainsHint;
        bool _gainsHitStale = true;
        float _gainsHitSize = -1f;
        int _headerVersion = -1;   // _gainCellsVersion at the last header write; -1 = never written or invalidated
        bool _headerFolded;

        public bool GainsFolded => _gainsFolded;
        public bool ChampionFolded => _championFolded;

        /// <summary>Sets both folds without storing them (the tests, and the one read of the stored values).</summary>
        void ApplyFolds(bool gainsFolded, bool championFolded)
        {
            FoldGains(gainsFolded, false);
            FoldChampion(championFolded, false);
        }

        float GainedBodyHeight() { return _gainsFolded ? 0f : 5 * BuildRowStep; }

        /// <summary>The GAINED caption: a plain band holding the text, and a small click box at its left, as wide as the widest header.</summary>
        void CreateGainsHeader(UiTheme theme)
        {
            var band = HudUi.At(_buildStrip, "GainedLabel", 0, 0, PlateInner, GainedHeaderHeight);
            _gainsHit = HudUi.At(band, "HitBox", 0, 0, 0, GainedHeaderHeight);
            var image = HudUi.Surface(_gainsHit, Color.white, theme.CornerRadiusSmall, null, true);
            image.canvasRenderer.cullTransparentMesh = false;
            var button = _gainsHit.gameObject.AddComponent<UnityEngine.UI.Button>();
            button.targetGraphic = image;
            HudUi.ApplyColors(button, UiTheme.With(theme.SurfaceHover, 0f));
            button.onClick.AddListener(() => FoldGains(!_gainsFolded, true));
            _gainsHint = HudIconHint.Attach(button, _root, LeftEdgeText.GainedHint(null, false));
            _gainsHeader = HudUi.Label(band, "Label", LeftEdgeText.GainedHeader(null, false), theme.SizeTiny, theme.TextSecondary);
            _gainsHeader.characterSpacing = 3f;
        }

        /// <summary>The champion head row as a control, and the body that holds the three rows.</summary>
        void CreateChampionHeader()
        {
            var theme = UiTheme.Current;
            var toggle = HudUi.Box(_championHud, "HeadToggle", HudAnchor.TopLeft, 0, 0, 0, 26);
            toggle.anchorMin = new Vector2(0, 1); toggle.anchorMax = new Vector2(1, 1); toggle.pivot = new Vector2(.5f, 1);
            toggle.sizeDelta = new Vector2(-2f, 26f); toggle.anchoredPosition = new Vector2(0, -1f);
            var image = HudUi.Surface(toggle, Color.white, 6f, null, true);
            image.canvasRenderer.cullTransparentMesh = false;
            var button = toggle.gameObject.AddComponent<UnityEngine.UI.Button>();
            button.targetGraphic = image;
            HudUi.ApplyColors(button, UiTheme.With(theme.SurfaceHover, 0f));
            button.onClick.AddListener(() => FoldChampion(!_championFolded, true));
            var glyph = HudUi.Box(toggle, "FoldGlyph", HudAnchor.TopRight, 7, 6, 14, 14);
            _championGlyph = HudUi.Label(glyph, "Label", LeftEdgeText.FoldGlyph(false), theme.SizeTiny, theme.TextSecondary, TextAlignmentOptions.Center);
            _championBody = HudUi.Box(_championHud, "ChampionBody", HudAnchor.TopLeft, 0, 0, 0, 0);
            _championBody.anchorMin = Vector2.zero; _championBody.anchorMax = Vector2.one;
            _championBody.offsetMin = _championBody.offsetMax = Vector2.zero;
        }

        void FoldGains(bool folded, bool store)
        {
            _gainsFolded = folded;
            if (store) UiDisplayPreferences.StoreGainsFolded(folded);
            PlaceGainsParts();
            SetPlateMode(_plateActive);
            FollowPlate();
            if (!folded) RefitUnder(_buildCells);
        }

        void FoldChampion(bool folded, bool store)
        {
            _championFolded = folded;
            if (store) UiDisplayPreferences.StoreChampionFolded(folded);
            if (_championHud == null) return;
            _championBody.gameObject.SetActive(!folded);
            _championGlyph.text = LeftEdgeText.FoldGlyph(folded);
            _championHud.sizeDelta = new Vector2(_championHud.sizeDelta.x, folded ? ChampionFoldedHeight : ChampionHeight);
            SetPlateMode(_plateActive);
            FollowPlate();
            if (!folded) RefitUnder(new[] { _championBody });
        }

        /// <summary>A label measured while inactive may be stale after a text-size change: fit again once, on unfolding.</summary>
        static void RefitUnder(RectTransform[] parents)
        {
            for (int i = 0; i < parents.Length; i++)
                if (parents[i] != null)
                    foreach (var fit in parents[i].GetComponentsInChildren<MythicTextScale>(true)) fit.Refit();
        }

        /// <summary>The strip, its cells, the extras line and its body, from the fold state.</summary>
        void PlaceGainsParts()
        {
            if (_buildStrip == null) return;
            float body = GainedBodyHeight(), y = 18 + BuildTop + body + 3;
            HudUi.Place(_buildStrip, HudAnchor.TopLeft, 0, 18, PlateInner, BuildTop + body);
            for (int i = 0; i < _buildCells.Length; i++) if (_buildCells[i] != null) _buildCells[i].gameObject.SetActive(!_gainsFolded);
            HudUi.Place((RectTransform)_extrasToggle.transform, HudAnchor.TopLeft, 0, y, PlateInner, 14);
            HudUi.Place(_extrasBody, HudAnchor.TopLeft, 0, y + 16, PlateInner, 62);
        }

        /// <summary>The header text and hint; writes only when the cells or the fold changed.</summary>
        void RefreshGainsHeader()
        {
            if (_gainsHeader == null) return;
            if (_gainsHitStale) SizeGainsHit();
            // An open header shows neither count nor hint, so a changed cell matters only while the strip is folded.
            if (_headerVersion >= 0 && _headerFolded == _gainsFolded && (!_gainsFolded || _headerVersion == _gainCellsVersion)) return;
            _headerVersion = _gainCellsVersion; _headerFolded = _gainsFolded;
            _gainsHeader.text = LeftEdgeText.GainedHeader(_gainCells, _gainsFolded);
            _gainsHint.Text = LeftEdgeText.GainedHint(_gainCells, _gainsFolded);
            SizeGainsHit();
        }

        /// <summary>Sizes the click box to the widest header at the current text size. Measures only a label that is active in the hierarchy.</summary>
        void SizeGainsHit()
        {
            if (_gainsHeader == null || _gainsHit == null) return;
            if (!_gainsHeader.gameObject.activeInHierarchy) { _gainsHitStale = true; return; }
            float size = _gainsHeader.fontSize;
            if (!_gainsHitStale && Mathf.Approximately(size, _gainsHitSize)) return;
            float widest = 0f;
            var forms = LeftEdgeText.GainedHeaderForms();
            for (int i = 0; i < forms.Count; i++) widest = Mathf.Max(widest, _gainsHeader.GetPreferredValues(forms[i]).x);
            float width = Mathf.Ceil(widest) + _gainsHeader.margin.x + _gainsHeader.margin.z;
            _gainsHitStale = false; _gainsHitSize = size;
            if (!Mathf.Approximately(_gainsHit.sizeDelta.x, width)) HudUi.Place(_gainsHit, HudAnchor.TopLeft, 0, 0, width, GainedHeaderHeight);
        }
    }
}
