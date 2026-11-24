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
    /// inside the map and overlaps no fixed mark and no block placed earlier. Elite camps are placed first, then the others, each in index order.
    /// Pure and allocation free; layer units, y up.
    /// </summary>
    public static class CampTextPlacement
    {
        public const int None = -1, Right = 0, Below = 1, Above = 2, Left = 3, Places = 4;
        public const float Margin = 2f, RightGap = 21.2f, AboveGap = 21f, LinePitch = 14.4f, Tolerance = .001f;

        static bool Good(float v) { return v > 0f && !float.IsNaN(v) && !float.IsInfinity(v); }

        /// <summary>The rect of a place for a block; all zero for a bad place.</summary>
        public static MapRect RectOf(int place, CampTextBlock block, float leftGap, float belowGap)
        {
            float h = block.LineH + (block.Lines > 1 ? block.Lines - 1 : 0) * LinePitch;
            float rightY = block.Y + 2f - block.LineH * .5f;
            switch (place)
            {
                case Right: return new MapRect(block.X + RightGap, rightY, block.W, h);
                case Below: return new MapRect(block.X - block.W * .5f, block.Y - belowGap - h, block.W, h);
                case Above: return new MapRect(block.X - block.W * .5f, block.Y + AboveGap, block.W, h);
                case Left: return new MapRect(block.X - leftGap - block.W, rightY, block.W, h);
                default: return new MapRect(0f, 0f, 0f, 0f);
            }
        }

        static float Overlap(MapRect a, MapRect b)
        {
            float w = Math.Min(a.Right, b.Right) - Math.Max(a.X, b.X), h = Math.Min(a.Bottom, b.Bottom) - Math.Max(a.Y, b.Y);
            return w > Tolerance && h > Tolerance ? w * h : 0f;
        }

        static bool Inside(MapRect r, MapRect b)
        {
            return r.X >= b.X + Margin - Tolerance && r.Y >= b.Y + Margin - Tolerance && r.Right <= b.Right - Margin + Tolerance && r.Bottom <= b.Bottom - Margin + Tolerance;
        }

        static float OutsideArea(MapRect r, MapRect b)
        {
            float w = Math.Min(r.Right, b.Right - Margin) - Math.Max(r.X, b.X + Margin), h = Math.Min(r.Bottom, b.Bottom - Margin) - Math.Max(r.Y, b.Y + Margin);
            float inner = w > 0f && h > 0f ? w * h : 0f;
            return r.W * r.H - inner;
        }

        /// <summary>Chooses a place per block, in map index order, into the caller's buffers (the least of the three lengths is written).</summary>
        public static void Place(IReadOnlyList<CampTextBlock> blocks, IReadOnlyList<MapRect> obstacles, MapRect bounds,
            float leftGap, float belowGap, int[] places, MapRect[] rects)
        {
            if (blocks == null || places == null || rects == null) return;
            int n = Math.Min(blocks.Count, Math.Min(places.Length, rects.Length));
            for (int i = 0; i < n; i++) { places[i] = None; rects[i] = new MapRect(0f, 0f, 0f, 0f); }
            bool boundsOk = Good(bounds.W) && Good(bounds.H) && !float.IsNaN(bounds.X) && !float.IsNaN(bounds.Y)
                && !float.IsInfinity(bounds.X) && !float.IsInfinity(bounds.Y);
            bool gapsOk = Good(leftGap) && Good(belowGap);
            for (int pass = 0; pass < 2; pass++)
                for (int i = 0; i < n; i++)
                {
                    var b = blocks[i];
                    if (b.Lines <= 0 || b.Elite != (pass == 0)) continue;
                    if (!gapsOk || !Good(b.W) || !Good(b.LineH))
                    {
                        places[i] = Right; rects[i] = new MapRect(b.X + RightGap, b.Y + 2f, 0f, 0f);
                        continue;
                    }
                    if (!boundsOk) { places[i] = Right; rects[i] = RectOf(Right, b, leftGap, belowGap); continue; }
                    int best = Right; float bestScore = float.MaxValue; bool bestInside = false;
                    for (int p = 0; p < Places; p++)
                    {
                        var r = RectOf(p, b, leftGap, belowGap);
                        bool inside = Inside(r, bounds);
                        float hit = 0f;
                        if (obstacles != null) for (int k = 0; k < obstacles.Count; k++) hit += Overlap(r, obstacles[k]);
                        for (int k = 0; k < n; k++) if (k != i && places[k] != None && rects[k].W > 0f) hit += Overlap(r, rects[k]);
                        if (inside && hit <= 0f) { best = p; bestInside = true; bestScore = 0f; break; }
                        float score = inside ? hit : OutsideArea(r, bounds);
                        bool better;
                        if (p == 0) better = true;
                        else if (inside != bestInside) better = inside;
                        else better = score < bestScore - Tolerance;
                        if (better) { best = p; bestScore = score; bestInside = inside; }
                    }
                    places[i] = best; rects[i] = RectOf(best, b, leftGap, belowGap);
                }
        }
    }
}
