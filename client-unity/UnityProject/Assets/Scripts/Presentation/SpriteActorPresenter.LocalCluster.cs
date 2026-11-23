using System.Collections.Generic;
using UnityEngine;

namespace Roguehalla.Client
{
    /// <summary>Answers the world rectangle of the local fighter's overhead cluster (false when there is none).</summary>
    public delegate bool LocalClusterSource(out Rect rect);

    /// <summary>The local fighter's overhead cluster as a rectangle, for the world labels that keep clear of it.</summary>
    public sealed partial class SpriteActorPresenter
    {
        readonly List<Rect> _clusterParts = new List<Rect>();

        /// <summary>The union of the local fighter's bar track and overhead rectangles as last presented (valid between End and the next Begin). The body status glyphs are not part of it. False when nothing is drawn.</summary>
        public bool TryLocalClusterRect(out Rect rect)
        {
            rect = default;
            if (WorldLabelVisibility.Hidden) return false;
            foreach (var art in _active.Values)
            {
                if (art.Stamp != _stamp || !art.IndFacts.Local || art.IndFacts.Mob || art.Dead || art.Ind == null || !art.Root.activeInHierarchy) continue;
                _clusterParts.Clear();
                if (art.HpBack != null && art.HpBack.enabled) _clusterParts.Add(CombatHudPlacement.FromBounds(art.HpBack.bounds));
                art.Ind.AddOverheadObstacles(_clusterParts);
                if (_clusterParts.Count == 0) return false;
                float x0 = float.MaxValue, y0 = float.MaxValue, x1 = float.MinValue, y1 = float.MinValue;
                for (int i = 0; i < _clusterParts.Count; i++)
                {
                    var r = _clusterParts[i];
                    if (r.xMin < x0) x0 = r.xMin; if (r.yMin < y0) y0 = r.yMin;
                    if (r.xMax > x1) x1 = r.xMax; if (r.yMax > y1) y1 = r.yMax;
                }
                rect = Rect.MinMaxRect(x0, y0, x1, y1);
                return true;
            }
            return false;
        }
    }
}
