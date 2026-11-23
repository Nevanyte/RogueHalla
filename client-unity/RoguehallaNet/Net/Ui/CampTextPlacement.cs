using System;
using System.Collections.Generic;

namespace Roguehalla.Net.Ui
{
    /// <summary>One camp's text on the Tab map: the camp point, the widest line and one line's height (layer units, y up), how many lines.</summary>
    public struct CampTextBlock
    {
        public float X, Y, W, LineH;
        public int Lines;
        public bool Elite;
    }

    /// <summary>
    /// Where a camp's text block goes on the Tab map (P-UI-TABMAP-TEXT-1): Right, Below, Above or Left of the camp, the first place that lies
    /// inside the map and overlaps no fixed mark and no block placed earlier. STUB: the oracle's signatures, no behaviour yet.
    /// </summary>
    public static class CampTextPlacement
    {
        public const int None = -1, Right = 0, Below = 1, Above = 2, Left = 3, Places = 4;
        public const float Margin = 2f, RightGap = 21.2f, AboveGap = 21f, LinePitch = 14.4f, Tolerance = .001f;

        /// <summary>The rect of a place for a block; all zero for a bad place. STUB.</summary>
        public static MapRect RectOf(int place, CampTextBlock block, float leftGap, float belowGap)
        {
            return new MapRect(0f, 0f, 0f, 0f);
        }

        /// <summary>Chooses a place per block, in map index order, into the caller's buffers. STUB: every entry None with a zero rect.</summary>
        public static void Place(IReadOnlyList<CampTextBlock> blocks, IReadOnlyList<MapRect> obstacles, MapRect bounds,
            float leftGap, float belowGap, int[] places, MapRect[] rects)
        {
            if (blocks == null) return;
            for (int i = 0; i < blocks.Count; i++)
            {
                if (places != null && i < places.Length) places[i] = None;
                if (rects != null && i < rects.Length) rects[i] = new MapRect(0f, 0f, 0f, 0f);
            }
        }
    }
}
