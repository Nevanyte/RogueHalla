using UnityEngine;

namespace Roguehalla.Client
{
    /// <summary>Answers the world rectangle of the local fighter's overhead cluster (false when there is none). Stub (P-UI-WORLD-LABELS-1).</summary>
    public delegate bool LocalClusterSource(out Rect rect);

    /// <summary>The local fighter's overhead cluster as a rectangle, for the world labels that keep clear of it. Stub (P-UI-WORLD-LABELS-1).</summary>
    public sealed partial class SpriteActorPresenter
    {
        /// <summary>Stub: always false.</summary>
        public bool TryLocalClusterRect(out Rect rect) { rect = default; return false; }
    }
}
