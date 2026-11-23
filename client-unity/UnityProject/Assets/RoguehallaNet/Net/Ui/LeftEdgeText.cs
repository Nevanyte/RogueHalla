using System.Collections.Generic;

namespace Roguehalla.Net.Ui
{
    /// <summary>Pure text of the two left-edge folds.</summary>
    public static class LeftEdgeText
    {
        public const string GainedTitle = "GAINED", OpenGlyph = "▲", FoldedGlyph = "▼";

        // STUB (P-UI-LEFT-EDGE-1): the implementer replaces every body.
        public static string FoldGlyph(bool folded) { return ""; }
        public static int GainedCount(IReadOnlyList<BuildCell> cells) { return 0; }
        public static string GainedHeader(IReadOnlyList<BuildCell> cells, bool folded) { return ""; }
        public static string GainedHint(IReadOnlyList<BuildCell> cells, bool folded) { return ""; }
        public static IReadOnlyList<string> GainedHeaderForms() { return new string[0]; }
    }
}
