using System.Collections.Generic;

namespace Roguehalla.Net.Ui
{
    /// <summary>One cell of the Tab map key's camp grid: the kind, its map glyph key and colour, and the text it shows ("+3% Damage" or "Damage · varies").</summary>
    public struct CampKeyCell
    {
        public int Kind;
        public string IconKey;
        public uint IconRgb;
        public string Text;
    }

    /// <summary>
    /// P-UI-TABMAP-TEXT-2: learns each camp buff kind's per-kill amount from the delivered camp rows, so the Tab map key can say it once
    /// and the map can stop printing it beside every camp. The first amount of a kind is its amount; a later different one makes the kind
    /// "varies" for good (until Forget). Apply allocates nothing; strings are composed only when something is learned.
    /// </summary>
    public sealed class CampAmountKey
    {
        public const int Kinds = 9;
        public static readonly string[] Words = { "Offense", "Soul Offence", "Damage", "Attack speed", "Life steal", "Max health", "Defense", "Soul Defense", "Move speed" };
        public static readonly string[] IconKeys = { CampBadges.IconKey(0), CampBadges.IconKey(1), CampBadges.IconKey(2), CampBadges.IconKey(3), CampBadges.IconKey(4), CampBadges.IconKey(5), CampBadges.IconKey(6), CampBadges.IconKey(7), CampBadges.IconKey(8) };

        readonly bool[] _seen = new bool[Kinds];
        readonly bool[] _varies = new bool[Kinds];
        readonly string[] _amount = new string[Kinds];
        readonly string[] _text = new string[Kinds];
        int _version, _count;

        public static int KindOf(string iconKey)
        {
            if (iconKey == null) return -1;
            for (int k = 0; k < Kinds; k++) if (string.Equals(IconKeys[k], iconKey, System.StringComparison.Ordinal)) return k;
            return -1;
        }

        public int Version { get { return _version; } }
        public int Count { get { return _count; } }
        public bool Has(int kind) { return kind >= 0 && kind < Kinds && _seen[kind]; }
        public bool IsFixed(int kind) { return Has(kind) && !_varies[kind]; }
        public string CellText(int kind) { return Has(kind) ? _text[kind] : ""; }

        public CampKeyCell Cell(int kind)
        {
            if (kind < 0 || kind >= Kinds) return default(CampKeyCell);
            return new CampKeyCell { Kind = kind, IconKey = IconKeys[kind], IconRgb = CampBadges.IconRgb((byte)kind), Text = CellText(kind) };
        }

        public int SlotOf(int kind)
        {
            if (!Has(kind)) return -1;
            int n = 0;
            for (int k = 0; k < kind; k++) if (_seen[k]) n++;
            return n;
        }

        public int KindAt(int slot)
        {
            if (slot < 0) return -1;
            for (int k = 0; k < Kinds; k++) if (_seen[k] && slot-- == 0) return k;
            return -1;
        }

        public void Apply(List<CampGlyphRow> camps, bool strip)
        {
            if (camps == null) return;
            for (int i = 0; i < camps.Count; i++)
            {
                var c = camps[i];
                int k = KindOf(c.IconKey);
                if (k < 0 || c.AmountText == null) continue;
                if (!_seen[k])
                {
                    _seen[k] = true; _amount[k] = c.AmountText; _text[k] = c.AmountText + " " + Words[k]; _count++; _version++;
                }
                else if (!_varies[k] && !string.Equals(_amount[k], c.AmountText, System.StringComparison.Ordinal))
                {
                    _varies[k] = true; _text[k] = Words[k] + " \u00b7 varies"; _version++;
                }
            }
            if (!strip) return;
            for (int i = 0; i < camps.Count; i++)
            {
                var c = camps[i];
                int k = KindOf(c.IconKey);
                bool change = c.EliteText != null;
                c.EliteText = null;
                if (k >= 0 && _seen[k] && !_varies[k] && c.AmountText != null) { c.AmountText = null; change = true; }
                if (change) camps[i] = c;
            }
        }

        public void Forget()
        {
            for (int k = 0; k < Kinds; k++) { _seen[k] = false; _varies[k] = false; _amount[k] = null; _text[k] = null; }
            _count = 0; _version++;
        }
    }

    /// <summary>The numbers of the key's camp grid and of the rail below it, one source for the controller and the tests (HUD units, y down).</summary>
    public static class CampKeyLayout
    {
        public const int Rows = 17;
        public const float RowH = 19f, KeyTop = 34f, KeyH = 332f, ViewY = 366f, ViewHead = 22f, ListFoot = 112f, MinList = 110f, MinWidth = 240f, MinHeight = 610f;
        const float GridY = 76f;

        public static float ListHeight(float leftHeight) { return leftHeight - ViewY - 134f; }

        public static MapRect Cell(int slot, float width)
        {
            if (slot < 0 || slot >= Kinds) return default(MapRect);
            return new MapRect((slot % 2) * (float)System.Math.Floor(width / 2f), GridY + (slot / 2) * RowH, (float)System.Math.Floor(width / 2f), RowH);
        }

        const int Kinds = CampAmountKey.Kinds;

        public static bool InKey(bool tab, float leftWidth, float leftHeight) { return tab && leftWidth >= MinWidth && leftHeight >= MinHeight; }
    }
}
