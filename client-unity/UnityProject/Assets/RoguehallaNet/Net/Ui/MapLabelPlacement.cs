using System;
using System.Collections.Generic;

namespace Roguehalla.Net.Ui
{
    /// <summary>Where a zone's name goes on the map: six fixed spots around its icon, first free one wins.</summary>
    public static class MapLabelPlacement
    {
        public const int Spots = 6;
        public const float IconHalf = 13f, Gap = 4f, Drop = 24f, SideDrop = 20f, EdgeSlab = 1000f;

        static bool Finite(float v) { return !float.IsNaN(v) && !float.IsInfinity(v); }

        static bool Valid(MapRect r) { return Finite(r.X) && Finite(r.Y) && Finite(r.W) && Finite(r.H) && r.W > 0f && r.H > 0f; }

        /// <summary>The rect of a spot (layer units, y up) around the icon at (iconX, iconY) for a name of w x h; empty for a bad spot or size.</summary>
        public static MapRect Spot(int spot, float iconX, float iconY, float w, float h)
        {
            if (spot < 0 || spot >= Spots || !Finite(w) || !Finite(h) || w <= 0f || h <= 0f || !Finite(iconX) || !Finite(iconY)) return new MapRect(0f, 0f, 0f, 0f);
            float hw = w * .5f, hh = h * .5f;
            float drop = Math.Max(Drop, IconHalf + Gap + hh);
            float side = IconHalf + Gap + hw;
            float dx, dy;
            switch (spot)
            {
                case 0: dx = 0f; dy = -drop; break;
                case 1: dx = 0f; dy = drop; break;
                case 2: dx = side; dy = 0f; break;
                case 3: dx = -side; dy = 0f; break;
                case 4: dx = side; dy = -SideDrop; break;
                default: dx = -side; dy = -SideDrop; break;
            }
            return new MapRect(iconX + dx - hw, iconY + dy - hh, w, h);
        }

        static float Overlap(MapRect a, MapRect b)
        {
            float w = Math.Min(a.Right, b.Right) - Math.Max(a.X, b.X);
            float h = Math.Min(a.Bottom, b.Bottom) - Math.Max(a.Y, b.Y);
            return w > 0f && h > 0f ? w * h : 0f;
        }

        static float TotalOverlap(MapRect spot, IReadOnlyList<MapRect> marks)
        {
            float sum = 0f;
            for (int i = 0; i < marks.Count; i++)
            {
                var m = marks[i];
                if (Valid(m)) sum += Overlap(spot, m);
            }
            return sum;
        }

        /// <summary>First free spot wins; a free current spot is kept; with none free the least overlap, keeping current unless strictly bettered. -1: no valid spot.</summary>
        public static int Choose(IReadOnlyList<MapRect> spots, IReadOnlyList<MapRect> marks, int current)
        {
            int n = spots.Count;
            bool haveCurrent = current >= 0 && current < n && Valid(spots[current]);
            float currentOverlap = 0f;
            if (haveCurrent)
            {
                currentOverlap = TotalOverlap(spots[current], marks);
                if (currentOverlap <= 0f) return current;
            }
            int best = -1; float bestOverlap = 0f;
            for (int i = 0; i < n; i++)
            {
                if (!Valid(spots[i])) continue;
                float o = TotalOverlap(spots[i], marks);
                if (o <= 0f) return i;
                if (best < 0 || o < bestOverlap) { best = i; bestOverlap = o; }
            }
            if (best < 0) return -1;
            if (haveCurrent && currentOverlap <= bestOverlap) return current;
            return best;
        }

        /// <summary>Adds a w x h box centred on (cx, cy); nothing for a bad size.</summary>
        public static void AddBox(List<MapRect> into, float cx, float cy, float w, float h)
        {
            if (!Finite(cx) || !Finite(cy) || !Finite(w) || !Finite(h) || w <= 0f || h <= 0f) return;
            into.Add(new MapRect(cx - w * .5f, cy - h * .5f, w, h));
        }

        /// <summary>Adds a w x h text box about cy; align -1: left edge at x, 0: centred on x, 1: right edge at x.</summary>
        public static void AddText(List<MapRect> into, float x, float cy, float w, float h, int align)
        {
            if (!Finite(x) || !Finite(cy) || !Finite(w) || !Finite(h) || w <= 0f || h <= 0f) return;
            float left = align < 0 ? x : align == 0 ? x - w * .5f : x - w;
            into.Add(new MapRect(left, cy - h * .5f, w, h));
        }

        /// <summary>Adds four slabs just outside the layer rect, so a spot off the map is overlapped.</summary>
        public static void AddEdges(List<MapRect> into, float xMin, float yMin, float width, float height)
        {
            if (!Finite(xMin) || !Finite(yMin) || !Finite(width) || !Finite(height) || width <= 0f || height <= 0f) return;
            float e = EdgeSlab;
            into.Add(new MapRect(xMin - e, yMin - e, e, height + 2f * e));
            into.Add(new MapRect(xMin + width, yMin - e, e, height + 2f * e));
            into.Add(new MapRect(xMin - e, yMin - e, width + 2f * e, e));
            into.Add(new MapRect(xMin - e, yMin + height, width + 2f * e, e));
        }
    }
}
