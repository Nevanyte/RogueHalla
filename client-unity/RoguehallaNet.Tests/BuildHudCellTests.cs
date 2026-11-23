using System;
using NUnit.Framework;
using Roguehalla.Net.Protocol;
using Roguehalla.Net.Ui;

namespace Roguehalla.Net.Tests
{
    /// <summary>P-UI-HUD-GARBAGE-1: <see cref="BuildHudText.Cell"/> is the one cell of <see cref="BuildHudText.Cells"/>, so the HUD can
    /// rewrite a GAINED cell for its own raw gain without building the other eight.</summary>
    public sealed class BuildHudCellTests
    {
        static readonly int[] Raws = { 0, 1, -1, 499, 500, 501, -500, 1250, 240000, 1500000 };

        [Test]
        public void CellEqualsCellsForEveryIndexAndEdgeRaw()
        {
            foreach (int raw in Raws)
            {
                var s = new WireBuildSummary();
                for (int i = 0; i < s.Gains.Length; i++) s.Gains[i] = raw;
                var cells = BuildHudText.Cells(s);
                for (int i = 0; i < 9; i++)
                {
                    var cell = BuildHudText.Cell(i, raw);
                    string what = "index " + i + " raw " + raw;
                    Assert.That(cell.Label, Is.EqualTo(cells[i].Label), "label, " + what);
                    Assert.That(cell.Value, Is.EqualTo(cells[i].Value), "value, " + what);
                    Assert.That(cell.Dim, Is.EqualTo(cells[i].Dim), "dim, " + what);
                }
            }
        }

        [Test]
        public void CellRefusesAnIndexOutsideTheNine()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => BuildHudText.Cell(-1, 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => BuildHudText.Cell(9, 0));
        }
    }
}
