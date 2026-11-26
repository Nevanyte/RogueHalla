using System.Collections.Generic;
using Roguehalla.Net.Ui;
using TMPro;
using UnityEngine;
using MapRect = Roguehalla.Net.Ui.MapRect;

namespace Roguehalla.Client
{
    /// <summary>
    /// The place of each camp's amount and elite text on the Tab map (P-UI-TABMAP-TEXT-1): one block per camp, placed by
    /// CampTextPlacement clear of the map's edge and of the fixed marks, and kept until the camps, the size or the text size change.
    /// </summary>
    public sealed partial class TacticalMapMarks
    {
        struct CampSig { public double X, Y; public string Amount, Elite; public bool IsElite, HasIcon; }

        readonly List<CampTextBlock> _ctBlocks = new List<CampTextBlock>(64);
        readonly List<MapRect> _ctObstacles = new List<MapRect>(256);
        readonly List<CampSig> _ctSig = new List<CampSig>(64);
        int[] _ctPlaces = new int[0];
        MapRect[] _ctRects = new MapRect[0];
        int _ctCount, _ctPasses;
        bool _ctSigValid;
        float _ctSigW, _ctSigH, _ctSigK, _ctSigR;
        TMP_Text _ctMeasure;

        /// <summary>What the Tab map key learns of the camp buff amounts (P-UI-TABMAP-TEXT-2).</summary>
        public CampAmountKey CampKey { get; } = new CampAmountKey();

        /// <summary>True while the Tab map key shows each kind's amount, so the map drops the texts the key says (P-UI-TABMAP-TEXT-2; stub: inert).</summary>
        public bool CampFactsInKey { get; set; }

        /// <summary>How many camp-text placement passes have run.</summary>
        public int CampTextPlacements { get { return _ctPasses; } }

        /// <summary>The place (CampTextPlacement.Right to Left) of the camp row's text block; -1: no block.</summary>
        public int CampTextPlace(int campRow) { return campRow >= 0 && campRow < _ctCount ? _ctPlaces[campRow] : CampTextPlacement.None; }

        /// <summary>The rect of the camp row's text block in layer units (x, y the bottom-left corner); null: no block.</summary>
        public Rect? CampTextRect(int campRow)
        {
            if (CampTextPlace(campRow) == CampTextPlacement.None) return null;
            var r = _ctRects[campRow];
            return new Rect(r.X, r.Y, r.W, r.H);
        }

        /// <summary>Forgets every block and the signature (a null map: a new match).</summary>
        void ClearCampTexts() { _ctCount = 0; _ctSig.Clear(); _ctSigValid = false; }

        static int TextLines(CampGlyphRow c) { return (c.AmountText != null ? 1 : 0) + (c.EliteText != null ? 1 : 0); }

        bool CampSigSame(float r, Rect layer, float k)
        {
            if (!_ctSigValid || _ctSig.Count != _camps.Count || _ctSigW != layer.width || _ctSigH != layer.height || _ctSigK != k || _ctSigR != r) return false;
            for (int i = 0; i < _camps.Count; i++)
            {
                var c = _camps[i]; var s = _ctSig[i];
                if (s.X != c.WorldX || s.Y != c.WorldY || s.IsElite != c.Elite || s.HasIcon != (c.IconKey != null)
                    || !string.Equals(s.Amount, c.AmountText, System.StringComparison.Ordinal) || !string.Equals(s.Elite, c.EliteText, System.StringComparison.Ordinal)) return false;
            }
            return true;
        }

        void PlaceCampTexts(bool force)
        {
            if (!Large || _map == null || !gameObject.activeInHierarchy) return;
            bool any = false;
            for (int i = 0; i < _camps.Count && !any; i++) any = TextLines(_camps[i]) > 0;
            if (!any) { ClearCampTexts(); return; }
            if (_ctMeasure == null)
            {
                _ctMeasure = HudUi.Label(transform, "CampTextMeasure", "", 9f, UiTheme.Current.TextPrimary, TextAlignmentOptions.Center, false, UiFontRole.Display);
                _ctMeasure.raycastTarget = false;
                var scale = _ctMeasure.GetComponent<MythicTextScale>(); scale.FitBox = false; scale.Refit();
            }
            var layer = rectTransform.rect; float r = _ctMeasure.fontSize, k = transform.localScale.x;
            if (!force && CampSigSame(r, layer, k)) return;

            var digits = Measure(_ctMeasure, "888");
            bool measured = digits.x > 0f && digits.y > 0f;
            float leftGap = CampTextPlacement.RightGap + digits.x + 2f, belowGap = 16f + digits.y * .5f + 2f;
            _ctBlocks.Clear(); _ctObstacles.Clear();
            const float elite = 11.2f;
            for (int i = 0; i < _camps.Count; i++)
            {
                var c = _camps[i];
                var p = At(new Vector2((float)c.WorldX, (float)c.WorldY));
                float plate = c.IconKey != null ? 20.4f : 11.2f;
                _ctObstacles.Add(new MapRect(p.x - plate * .5f, p.y - plate * .5f, plate, plate));
                if (c.Elite) _ctObstacles.Add(new MapRect(p.x - elite * .5f, p.y + 13.5f - elite * .5f, elite, elite));
                _ctObstacles.Add(new MapRect(p.x - 13.2f - 6.8f, p.y + 2f - 6.8f, 13.6f, 13.6f));
                _ctObstacles.Add(new MapRect(p.x - 21.2f - digits.x, p.y + 2f - digits.y * .5f, digits.x, digits.y));
                _ctObstacles.Add(new MapRect(p.x - digits.x * .5f, p.y - 16f - digits.y * .5f, digits.x, digits.y));
                _ctObstacles.Add(new MapRect(p.x - 15.6f, p.y - 15f - 7.2f, 31.2f, 14.4f));
                var block = new CampTextBlock { X = p.x, Y = p.y, Lines = TextLines(c), Elite = c.Elite };
                if (block.Lines > 0)
                {
                    float w = 0f, h = 0f;
                    if (c.AmountText != null) { var s = Measure(_ctMeasure, c.AmountText); w = Mathf.Max(w, s.x); h = Mathf.Max(h, s.y); if (s.x <= 0f || s.y <= 0f) measured = false; }
                    if (c.EliteText != null) { var s = Measure(_ctMeasure, c.EliteText); w = Mathf.Max(w, s.x); h = Mathf.Max(h, s.y); if (s.x <= 0f || s.y <= 0f) measured = false; }
                    block.W = w; block.LineH = h;
                }
                _ctBlocks.Add(block);
            }
            for (int region = 1; region <= MapZones.Count; region++)
            {
                if (!_zoneFound[region]) continue;
                var z = ZoneAt(region);
                _ctObstacles.Add(new MapRect(z.x - 13f, z.y - 13f, 26f, 26f));
            }
            if (!measured)
                for (int i = 0; i < _ctBlocks.Count; i++) { var b = _ctBlocks[i]; b.W = 0f; _ctBlocks[i] = b; }
            if (_ctPlaces.Length < _ctBlocks.Count) { _ctPlaces = new int[_ctBlocks.Count * 2]; _ctRects = new MapRect[_ctBlocks.Count * 2]; }
            CampTextPlacement.Place(_ctBlocks, _ctObstacles, new MapRect(layer.xMin, layer.yMin, layer.width, layer.height), leftGap, belowGap, _ctPlaces, _ctRects);
            _ctCount = _ctBlocks.Count;
            _ctPasses++;

            _ctSig.Clear();
            for (int i = 0; i < _camps.Count; i++)
            {
                var c = _camps[i];
                _ctSig.Add(new CampSig { X = c.WorldX, Y = c.WorldY, Amount = c.AmountText, Elite = c.EliteText, IsElite = c.Elite, HasIcon = c.IconKey != null });
            }
            _ctSigW = layer.width; _ctSigH = layer.height; _ctSigK = k; _ctSigR = r;
            _ctSigValid = measured;
        }

        /// <summary>Draws one camp's lines inside its placed block: the amount below, the tag above (a row without a block draws nothing).</summary>
        void ShowCampTexts(ref int used, int row, CampGlyphRow camp)
        {
            if (row >= _ctCount || _ctPlaces[row] == CampTextPlacement.None) return;
            var rect = _ctRects[row]; int place = _ctPlaces[row];
            int align = place == CampTextPlacement.Right ? -1 : place == CampTextPlacement.Left ? 1 : 0;
            float x = align < 0 ? rect.X : align > 0 ? rect.Right : rect.X + rect.W * .5f;
            float lineH = Mathf.Max(0f, rect.H - (TextLines(camp) - 1) * CampTextPlacement.LinePitch);
            float y = rect.Y + lineH * .5f;
            int line = 0;
            if (camp.AmountText != null) ShowBadgeText(ref used, new Vector2(x, y + line++ * CampTextPlacement.LinePitch), camp.AmountText, Rgb(camp.IconRgb), align);
            if (camp.EliteText != null) ShowBadgeText(ref used, new Vector2(x, y + line * CampTextPlacement.LinePitch), camp.EliteText, ChestTone, align);
        }
    }
}
