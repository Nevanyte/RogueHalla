using UnityEngine;

namespace Roguehalla.Client
{
    /// <summary>Resolves the anatomical hand positions of Fire’s painted bow actions.</summary>
    public static partial class StormInkActors
    {
        static bool TryFireBowHand(Sprite body, bool left, out Vector2 uv)
        {
            var art = StormInkFireBowAuthoring.Load();
            if (art != null && art.TryHand(body, left, out uv)) return true;
            uv = default;
            return false;
        }
    }
}
