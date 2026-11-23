using UnityEngine;

namespace Roguehalla.Client
{
    /// <summary>The zone names of the Tab map and where each stands clear of the marks (P-UI-ZONE-NAMES-1). STUB: reads only.</summary>
    public sealed partial class TacticalMapMarks
    {
        /// <summary>How many placement passes have run.</summary>
        public int ZoneLabelPlacements => 0;
        /// <summary>The spot the name of <paramref name="region"/> is on now; -1: no name.</summary>
        public int ZoneLabelSpot(int region) { return -1; }
        /// <summary>The rect of that spot in layer units; null: no name.</summary>
        public Rect? ZoneLabelRect(int region) { return null; }
        /// <summary>The dark plate behind icon and name in layer units; null: no name.</summary>
        public Rect? ZonePlateRect(int region) { return null; }
    }
}
