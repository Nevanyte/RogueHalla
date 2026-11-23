using System.Linq;
using NUnit.Framework;
using Roguehalla.Net.Protocol;
using Roguehalla.Net.Ui;

namespace Roguehalla.Net.Tests
{
    /// <summary>P-UI-LEFT-EDGE-1: the pure text of the two left-edge folds. Counting is by the cell's Dim flag (raw != 0); the
    /// header and the hint say what the folded table hides. The glyphs are the triangles TerrainChips draws.</summary>
    public sealed class LeftEdgeTextTests
    {
        static WireBuildSummary Summary(params int[] gains)
        {
            var s = new WireBuildSummary();
            for (int i = 0; i < gains.Length; i++) s.Gains[i] = gains[i];
            return s;
        }

        static readonly int[] Eight = { 240000, 0, 1900, 1250, 325, 1500000, -25000, 15000, 500 };

        [Test]
        public void TheCountIsTheCellsThatAreNotZero()
        {
            Assert.AreEqual(0, LeftEdgeText.GainedCount(null), "null");
            Assert.AreEqual(0, LeftEdgeText.GainedCount(BuildHudText.Cells(null)), "no summary: no cells");
            Assert.AreEqual(0, LeftEdgeText.GainedCount(BuildHudText.Cells(Summary())), "an empty summary");
            Assert.AreEqual(8, LeftEdgeText.GainedCount(BuildHudText.Cells(Summary(Eight))), "eight of nine");
            Assert.AreEqual(1, LeftEdgeText.GainedCount(BuildHudText.Cells(Summary(0, 0, 0, 0, 0, 0, 0, 0, 500))), "the last cell alone");
            Assert.AreEqual(1, LeftEdgeText.GainedCount(BuildHudText.Cells(Summary(100))), "a raw 100 reads +0 and still counts");
        }

        [Test]
        public void TheHeaderNamesTheCountAndPointsDownWhenFoldedAndUpWhenOpen()
        {
            Assert.AreEqual("▲", LeftEdgeText.OpenGlyph);
            Assert.AreEqual("▼", LeftEdgeText.FoldedGlyph);
            Assert.AreEqual("GAINED", LeftEdgeText.GainedTitle);
            Assert.AreEqual("▲", LeftEdgeText.FoldGlyph(false));
            Assert.AreEqual("▼", LeftEdgeText.FoldGlyph(true));
            Assert.AreEqual("GAINED · 2 ▼", LeftEdgeText.GainedHeader(BuildHudText.Cells(Summary(240000, 0, 1900)), true));
            Assert.AreEqual("GAINED · 0 ▼", LeftEdgeText.GainedHeader(BuildHudText.Cells(Summary()), true), "all zeros");
            Assert.AreEqual("GAINED · 0 ▼", LeftEdgeText.GainedHeader(null, true), "null");
            Assert.AreEqual("GAINED ▲", LeftEdgeText.GainedHeader(null, false), "open, null");
            Assert.AreEqual("GAINED ▲", LeftEdgeText.GainedHeader(BuildHudText.Cells(Summary()), false), "open, zeros");
            Assert.AreEqual("GAINED ▲", LeftEdgeText.GainedHeader(BuildHudText.Cells(Summary(Eight)), false), "open, eight");
        }

        [Test]
        public void TheHintListsWhatIsHiddenWhileFolded()
        {
            var lines = LeftEdgeText.GainedHint(BuildHudText.Cells(Summary(Eight)), true).Split('\n');
            Assert.AreEqual("Gained from boosts, cards and farmed mobs", lines[0]);
            Assert.AreEqual("Offense +24", lines[1]);
            Assert.AreEqual("Damage gained +19%", lines[2]);
            Assert.AreEqual(8 + 2, lines.Length, "the heading, eight cells, the click line");
            Assert.IsFalse(lines.Any(l => l.StartsWith("SO")), "a zero cell is not listed");
            Assert.AreEqual("Click to show the table", lines[lines.Length - 1]);

            var zeros = LeftEdgeText.GainedHint(BuildHudText.Cells(Summary()), true).Split('\n');
            Assert.AreEqual(new[] { "Gained from boosts, cards and farmed mobs", "Nothing gained yet", "Click to show the table" }, zeros);

            var open = LeftEdgeText.GainedHint(BuildHudText.Cells(Summary(Eight)), false).Split('\n');
            Assert.AreEqual(new[] { "Gained from boosts, cards and farmed mobs", "Click to fold the table" }, open);
        }

        [Test]
        public void TheHeaderFormsAreTheOpenOneAndTheTenFoldedOnes()
        {
            var forms = LeftEdgeText.GainedHeaderForms();
            Assert.AreEqual(11, forms.Count);
            Assert.AreEqual(LeftEdgeText.GainedHeader(null, false), forms[0]);
            Assert.AreEqual("GAINED ▲", forms[0]);
            for (int n = 0; n <= 9; n++)
            {
                var ones = Enumerable.Repeat(1, n).ToArray();
                string want = LeftEdgeText.GainedHeader(BuildHudText.Cells(Summary(ones)), true);
                Assert.AreEqual(want, forms[n + 1], "form " + n);
                Assert.AreEqual("GAINED · " + n + " ▼", forms[n + 1], "text of form " + n);
            }
        }
    }
}
