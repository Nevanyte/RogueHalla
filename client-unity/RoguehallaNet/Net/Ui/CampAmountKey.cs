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
    /// and the map can stop printing it beside every camp. STUB: every member returns a default until the packet is implemented.
    /// </summary>
    public sealed class CampAmountKey
    {
        public const int Kinds = 9;
        public static readonly string[] Words = new string[0];
        public static readonly string[] IconKeys = new string[0];

        public static int KindOf(string iconKey) { return -1; }

        public int Version { get { return 0; } }
        public int Count { get { return 0; } }
        public bool Has(int kind) { return false; }
        public bool IsFixed(int kind) { return false; }
        public string CellText(int kind) { return ""; }
        public CampKeyCell Cell(int kind) { return default(CampKeyCell); }
        public int SlotOf(int kind) { return -1; }
        public int KindAt(int slot) { return -1; }
        public void Apply(List<CampGlyphRow> camps, bool strip) { }
        public void Forget() { }
    }

    /// <summary>The numbers of the key's camp grid and of the rail below it, one source for the controller and the tests (HUD units, y down).</summary>
    public static class CampKeyLayout
    {
        public const int Rows = 0;
        public const float RowH = 0f, KeyTop = 0f, KeyH = 0f, ViewY = 0f, ViewHead = 0f, ListFoot = 0f, MinList = 0f, MinWidth = 0f, MinHeight = 0f;

        public static float ListHeight(float leftHeight) { return 0f; }
        public static MapRect Cell(int slot, float width) { return default(MapRect); }
        public static bool InKey(bool tab, float leftWidth, float leftHeight) { return false; }
    }
}
