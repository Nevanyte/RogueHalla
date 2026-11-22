using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Roguehalla.Net.Protocol;
using UnityEngine;

namespace Roguehalla.Client
{
    /// <summary>Geometry shared by the authored cypress, marsh ground and replacement wall casters.</summary>
    internal sealed class StormCypressGeometry
    {
        const int MinFootprint = 20, MinTrunkSpan = 7;
        internal const string TrunkResource = "StormInk/Plague/CypressTrunk";
        static readonly ConditionalWeakTable<MapBlob, StormCypressGeometry> Results =
            new ConditionalWeakTable<MapBlob, StormCypressGeometry>();
        static readonly StormCypressGeometry Ineligible = new StormCypressGeometry(null, null);

        readonly MapAnchor _anchor;
        readonly bool[] _wallMask;

        StormCypressGeometry(MapAnchor anchor, bool[] wallMask)
        {
            _anchor = anchor;
            _wallMask = wallMask;
        }

        internal bool Eligible => _anchor != null;
        internal bool Matches(MapAnchor anchor) => Eligible && ReferenceEquals(_anchor, anchor);

        // Public map planes are stable over an arena build. Do not retain a prior match or cache asset presence.
        internal static StormCypressGeometry For(MapBlob map) => Results.GetValue(map, Find);

        internal bool[] ReplacementWalls() =>
            Eligible && Resources.Load<Sprite>(TrunkResource) != null ? _wallMask : null;

        static StormCypressGeometry Find(MapBlob map)
        {
            foreach (var anchor in map.Anchors)
            {
                if (anchor.Zone != (byte)RegionId.Plague || anchor.Kind != MapBlob.AnchorKindBogTree) continue;
                var f = anchor.Footprint;
                if (f.W < MinFootprint || f.H < MinFootprint || f.X + f.W > map.W || f.Y + f.H > map.H) continue;
                var wall = new bool[f.W * f.H];
                for (int y = 0; y < f.H; y++)
                for (int x = 0; x < f.W; x++)
                    wall[y * f.W + x] = map.Tiles[(f.Y + y) * map.W + f.X + x] == MapBlob.Wall;

                if (!HasTrunk(wall, f.W, f.H)) continue;

                var skip = new bool[map.W * map.H];
                for (int y = 0; y < f.H; y++)
                for (int x = 0; x < f.W; x++)
                    if (wall[y * f.W + x]) skip[(f.Y + y) * map.W + f.X + x] = true;
                return new StormCypressGeometry(anchor, skip);
            }
            return Ineligible;
        }

        static bool HasTrunk(bool[] wall, int width, int height)
        {
            var seen = new bool[wall.Length];
            var stack = new Stack<int>();
            int largest = 0;
            bool eligible = false;
            for (int start = 0; start < wall.Length; start++)
            {
                if (!wall[start] || seen[start]) continue;
                seen[start] = true;
                stack.Push(start);
                int count = 0, minX = width, minY = height, maxX = -1, maxY = -1;
                while (stack.Count > 0)
                {
                    int at = stack.Pop(), x = at % width, y = at / width;
                    count++;
                    minX = Mathf.Min(minX, x); minY = Mathf.Min(minY, y);
                    maxX = Mathf.Max(maxX, x); maxY = Mathf.Max(maxY, y);
                    for (int dy = -1; dy <= 1; dy++)
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        int nx = x + dx, ny = y + dy;
                        if ((uint)nx >= (uint)width || (uint)ny >= (uint)height) continue;
                        int next = ny * width + nx;
                        if (!wall[next] || seen[next]) continue;
                        seen[next] = true;
                        stack.Push(next);
                    }
                }
                // The landmark chooses the first largest component in row-major order on ties.
                if (count <= largest) continue;
                largest = count;
                eligible = maxX - minX + 1 >= MinTrunkSpan && maxY - minY + 1 >= MinTrunkSpan;
            }
            return eligible;
        }
    }
}
