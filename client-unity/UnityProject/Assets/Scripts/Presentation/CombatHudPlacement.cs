using System.Collections.Generic;
using UnityEngine;

namespace Roguehalla.Client
{
    /// <summary>Places world-space HUD rectangles around already drawn presentation geometry.</summary>
    internal static class CombatHudPlacement
    {
        const float Clearance = .06f;

        public static Rect FromBounds(Bounds bounds) =>
            Rect.MinMaxRect(bounds.min.x, bounds.min.y, bounds.max.x, bounds.max.y);

        public static Rect Lift(Rect placed, List<Rect> obstacles)
        {
            // Every lift passes the upper edge of at least one obstacle. That
            // obstacle cannot be met again, so the number of obstacles is the bound.
            for (int pass = 0; pass < obstacles.Count; pass++)
            {
                float y = placed.y;
                for (int i = 0; i < obstacles.Count; i++)
                {
                    Rect blocked = obstacles[i];
                    if (placed.xMin < blocked.xMax + Clearance && placed.xMax > blocked.xMin - Clearance &&
                        placed.yMin < blocked.yMax + Clearance && placed.yMax > blocked.yMin - Clearance)
                        y = Mathf.Max(y, blocked.yMax + Clearance);
                }
                if (y <= placed.y) return placed;
                placed.y = y;
            }
            return placed;
        }

        public static Rect Damage(Vector2 origin, float width, float height, List<Rect> obstacles)
        {
            const float side = .58f;
            float step = Mathf.Max(.55f, width + Clearance);
            var best = default(Rect);
            float cost = float.PositiveInfinity;
            // Two lanes to either side provide a nearby alternative to lifting
            // every successive number into a single column above the fighter.
            for (int lane = 0; lane < 4; lane++)
            {
                int distance = lane / 2;
                float dx = side + distance * step;
                float x = (lane & 1) == 0 ? origin.x + dx : origin.x - dx - width;
                Rect candidate = Lift(new Rect(x, origin.y, width, height), obstacles);
                float move = candidate.y - origin.y + distance * .50f;
                if (move >= cost) continue;
                best = candidate;
                cost = move;
                if (cost == 0f) break;
            }
            return best;
        }
    }
}
