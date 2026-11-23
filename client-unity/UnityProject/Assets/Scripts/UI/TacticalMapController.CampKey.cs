using Roguehalla.Net.Ui;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using MapRect = Roguehalla.Net.Ui.MapRect;   // Net.Protocol has a MapRect too: the map screen's is meant here

namespace Roguehalla.Client
{
    /// <summary>
    /// The Tab map key's camp grid (P-UI-TABMAP-TEXT-2): nine kind cells (glyph and "+3% Damage") that pack from slot 0 in kind order, filled from
    /// what the large marks' CampAmountKey learned, so the map says each camp buff amount once. Written on a change only; nothing allocates in a steady frame.
    /// </summary>
    public sealed partial class TacticalMapController
    {
        readonly RectTransform[] _ckCells = new RectTransform[CampAmountKey.Kinds];
        readonly Image[] _ckIcons = new Image[CampAmountKey.Kinds];
        readonly TMP_Text[] _ckTexts = new TMP_Text[CampAmountKey.Kinds];
        readonly MythicTextScale[] _ckScales = new MythicTextScale[CampAmountKey.Kinds];
        int _ckVersion = -1, _ckFlag = -1;
        bool _ckOn;
        float _ckWidth = -1f;

        /// <summary>Creates the nine hidden kind cells under the key (between the heading row and the elite row).</summary>
        void BuildCampCells(UiTheme theme)
        {
            for (int k = 0; k < CampAmountKey.Kinds; k++)
            {
                var cell = HudUi.At(_keyRoot, "KindCell" + k, 0f, CampKeyLayout.Cell(0, 250f).Y, 125f, CampKeyLayout.RowH);
                var icon = HudUi.At(cell, "Icon", 8f, 2f, 14f, 14f);
                var img = icon.gameObject.AddComponent<Image>();
                img.sprite = CampRespawnLabels.Art(CampAmountKey.IconKeys[k]); img.preserveAspect = true; img.raycastTarget = false;
                var label = HudUi.Label(cell, "Text", "", 11f, theme.TextPrimary, TextAlignmentOptions.MidlineLeft);
                var lr = (RectTransform)label.transform;
                lr.offsetMin = new Vector2(26f, lr.offsetMin.y); lr.offsetMax = new Vector2(-2f, lr.offsetMax.y);
                _ckCells[k] = cell; _ckIcons[k] = img; _ckTexts[k] = label; _ckScales[k] = label.GetComponent<MythicTextScale>();
                cell.gameObject.SetActive(false);
            }
        }

        /// <summary>Puts each seen kind's cell at its packed slot for a key of this width, and refits its label (the box changed).</summary>
        void PlaceCampGrid(float width)
        {
            var key = _largeMarks.CampKey;
            for (int k = 0; k < CampAmountKey.Kinds; k++)
            {
                int slot = _ckOn ? key.SlotOf(k) : -1;
                var cell = _ckCells[k];
                if (slot < 0) { if (cell.gameObject.activeSelf) cell.gameObject.SetActive(false); continue; }
                var r = CampKeyLayout.Cell(slot, width);
                HudUi.Place(cell, HudAnchor.TopLeft, r.X, r.Y, r.W, r.H);
                if (!cell.gameObject.activeSelf) cell.gameObject.SetActive(true);
                var c = key.Cell(k);
                _ckIcons[k].color = new Color32((byte)(c.IconRgb >> 16), (byte)(c.IconRgb >> 8), (byte)c.IconRgb, 255);
                if (!string.Equals(_ckTexts[k].text, c.Text, System.StringComparison.Ordinal)) _ckTexts[k].text = c.Text;
                if (_ckScales[k] != null) _ckScales[k].Refit();
            }
        }

        /// <summary>Brings the cells in line with the learned kinds, the key's on flag and its width; returns at once when none changed.</summary>
        void ApplyCampKey()
        {
            if (_keyRoot == null || _largeMarks == null) return;
            bool on = _keyRoot.gameObject.activeSelf;
            float width = _keyRoot.sizeDelta.x;
            int version = _largeMarks.CampKey.Version;
            if (version == _ckVersion && on == _ckOn && width == _ckWidth) return;
            _ckVersion = version; _ckOn = on; _ckWidth = width;
            PlaceCampGrid(width);
        }
    }
}
