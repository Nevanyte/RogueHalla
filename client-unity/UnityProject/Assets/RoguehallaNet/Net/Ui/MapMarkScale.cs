namespace Roguehalla.Net.Ui
{
    /// <summary>
    /// How much larger than its base size the marks layer of the Tab map is drawn (P-UI-TABMAP-MARKS-1). STUB: always 1.
    /// </summary>
    public static class MapMarkScale
    {
        /// <summary>A map this wide (HUD units) or less draws marks at base size.</summary>
        public const float ReferenceSide = 480f;
        /// <summary>The most a wide map grows them.</summary>
        public const float MaxGrowth = 1.4f;
        /// <summary>The pooled mark labels' font size, in the layer's units.</summary>
        public const float LabelUnits = 9f;
        /// <summary>The smallest a mark label may be on screen, in pixels.</summary>
        public const float MinLabelPx = 10f;
        /// <summary>A guard: never larger than this, whatever the inputs.</summary>
        public const float Max = 1.6f;

        /// <summary>The layer scale for a map `mapSide` HUD units wide at `stageScale` screen pixels per HUD unit.</summary>
        public static float Layer(float mapSide, float stageScale) { return 1f; }
    }
}
