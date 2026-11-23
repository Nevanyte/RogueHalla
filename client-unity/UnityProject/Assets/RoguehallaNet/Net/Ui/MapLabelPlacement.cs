using System.Collections.Generic;

namespace Roguehalla.Net.Ui
{
    /// <summary>Where a zone's name goes on the map: six fixed spots around its icon, first free one wins.</summary>
    public static class MapLabelPlacement
    {
        public const int Spots = 6;
        public const float IconHalf = 13f, Gap = 4f, Drop = 24f, SideDrop = 20f, EdgeSlab = 1000f;

        /// <summary>STUB (P-UI-ZONE-NAMES-1): a zero rect.</summary>
        public static MapRect Spot(int spot, float iconX, float iconY, float w, float h) { return new MapRect(0f, 0f, 0f, 0f); }

        /// <summary>STUB (P-UI-ZONE-NAMES-1): always -1.</summary>
        public static int Choose(IReadOnlyList<MapRect> spots, IReadOnlyList<MapRect> marks, int current) { return -1; }

        /// <summary>STUB (P-UI-ZONE-NAMES-1): adds nothing.</summary>
        public static void AddBox(List<MapRect> into, float cx, float cy, float w, float h) { }

        /// <summary>STUB (P-UI-ZONE-NAMES-1): adds nothing.</summary>
        public static void AddText(List<MapRect> into, float x, float cy, float w, float h, int align) { }

        /// <summary>STUB (P-UI-ZONE-NAMES-1): adds nothing.</summary>
        public static void AddEdges(List<MapRect> into, float xMin, float yMin, float width, float height) { }
    }
}
