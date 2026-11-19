using System.Collections.Generic;
using Roguehalla.Net.Protocol;

namespace Roguehalla.Net
{
    /// <summary>
    /// STUB (P-NET-DRAWAHEAD-1b replaces the body): draw-ahead of remote fighters (legacy packages/client/src/net/remoteDraw.ts).
    /// Presentation only. Today it is exactly <see cref="SnapshotBuffer.Sample"/>: no fighter is drawn past its newest sample.
    /// </summary>
    public sealed class RemoteDraw
    {
        /// <summary>A mob has no walk hint: one that moves is drawn at most this far past its newest sample (legacy MOB_EXTRAPOLATE_MS).</summary>
        public const double MobExtrapolateMs = 25;

        readonly SnapshotBuffer _snapshots;

        /// <summary>The walls to clip at (null: nothing is drawn ahead, as legacy without a map).</summary>
        public MapBlob Map;

        /// <summary>Server tick interval (ms), from the Welcome: converts a walk hint's ticks to time.</summary>
        public double TickMs = 50;

        public RemoteDraw(SnapshotBuffer snapshots) { _snapshots = snapshots; }

        /// <summary>
        /// Fills <paramref name="outActors"/> as <see cref="SnapshotBuffer.Sample"/> does at <paramref name="drawMs"/> (the
        /// clock's <see cref="RenderClock.DrawMs"/>, once per frame), with remote fighters' positions drawn ahead. False when empty.
        /// </summary>
        public bool Sample(double drawMs, List<InterpActor> outActors) { return _snapshots.Sample(drawMs, outActors); }
    }
}
