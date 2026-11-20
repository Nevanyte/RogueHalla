using System.Collections.Generic;
using Roguehalla.Net.Protocol;

namespace Roguehalla.Client
{
    /// <summary>Formats the viewer's own native readout rows (State key 14) for the HUD. Presentation only: it never computes a cap.</summary>
    public static class NativeReadoutText
    {
        public readonly struct Line
        {
            public readonly byte Card, Kind;
            public readonly string Title, Text;
            /// <summary>value / max in 0..1, 0 when max is 0.</summary>
            public readonly float Fill;
            /// <summary>Marks rows: the number of pips (max) and how many are lit (value); 0 and 0 for other rows.</summary>
            public readonly int Pips, PipsOn;
            public Line(byte card, byte kind, string title, string text, float fill, int pips, int pipsOn)
            { Card = card; Kind = kind; Title = title; Text = text; Fill = fill; Pips = pips; PipsOn = pipsOn; }
        }

        public static Line[] Lines(IReadOnlyList<WireReadout> rows) { return new Line[0]; } // stub
    }
}
