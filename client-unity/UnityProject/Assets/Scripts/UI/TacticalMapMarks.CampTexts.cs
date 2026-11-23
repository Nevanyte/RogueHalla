using UnityEngine;

namespace Roguehalla.Client
{
    /// <summary>
    /// The place of each camp's amount and elite text on the Tab map (P-UI-TABMAP-TEXT-1): one block per camp, placed by
    /// CampTextPlacement clear of the map's edge and of the fixed marks, and kept until the camps, the size or the text size change.
    /// STUB: no block is ever placed.
    /// </summary>
    public sealed partial class TacticalMapMarks
    {
        /// <summary>How many camp-text placement passes have run.</summary>
        public int CampTextPlacements { get { return 0; } }

        /// <summary>The place (CampTextPlacement.Right to Left) of the camp row's text block; -1: no block.</summary>
        public int CampTextPlace(int campRow) { return -1; }

        /// <summary>The rect of the camp row's text block in layer units (x, y the bottom-left corner); null: no block.</summary>
        public Rect? CampTextRect(int campRow) { return null; }
    }
}
