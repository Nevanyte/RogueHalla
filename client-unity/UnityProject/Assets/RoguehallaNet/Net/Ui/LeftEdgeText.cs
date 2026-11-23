using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Roguehalla.Net.Ui
{
    /// <summary>Pure text of the two left-edge folds.</summary>
    public static class LeftEdgeText
    {
        public const string GainedTitle = "GAINED", OpenGlyph = "▲", FoldedGlyph = "▼";
        const string Heading = "Gained from boosts, cards and farmed mobs";

        static IReadOnlyList<string> _forms;

        public static string FoldGlyph(bool folded) { return folded ? FoldedGlyph : OpenGlyph; }

        /// <summary>The cells that are not Dim (raw != 0); null reads 0.</summary>
        public static int GainedCount(IReadOnlyList<BuildCell> cells)
        {
            int count = 0;
            if (cells == null) return 0;
            for (int i = 0; i < cells.Count; i++) if (!cells[i].Dim) count++;
            return count;
        }

        public static string GainedHeader(IReadOnlyList<BuildCell> cells, bool folded)
        {
            if (!folded) return GainedTitle + " " + OpenGlyph;
            return GainedTitle + " · " + GainedCount(cells).ToString(CultureInfo.InvariantCulture) + " " + FoldedGlyph;
        }

        public static string GainedHint(IReadOnlyList<BuildCell> cells, bool folded)
        {
            var sb = new StringBuilder(Heading);
            if (folded)
            {
                bool any = false;
                if (cells != null)
                    for (int i = 0; i < cells.Count; i++)
                        if (!cells[i].Dim) { sb.Append('\n').Append(cells[i].Label).Append(' ').Append(cells[i].Value); any = true; }
                if (!any) sb.Append("\nNothing gained yet");
            }
            sb.Append(folded ? "\nClick to show the table" : "\nClick to fold the table");
            return sb.ToString();
        }

        /// <summary>The open form, then the folded form for 0 to 9 gains: the widest texts the header can show.</summary>
        public static IReadOnlyList<string> GainedHeaderForms()
        {
            if (_forms != null) return _forms;
            var forms = new List<string> { GainedHeader(null, false) };
            for (int n = 0; n <= 9; n++)
                forms.Add(GainedTitle + " · " + n.ToString(CultureInfo.InvariantCulture) + " " + FoldedGlyph);
            _forms = forms;
            return forms;
        }
    }
}
