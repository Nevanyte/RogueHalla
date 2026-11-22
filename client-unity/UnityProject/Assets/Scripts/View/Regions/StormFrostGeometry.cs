using System.Runtime.CompilerServices;
using Roguehalla.Net.Protocol;
using Roguehalla.Net.Ui;
using MapRect = Roguehalla.Net.Protocol.MapRect;
using UnityEngine;

namespace Roguehalla.Client
{
    /// <summary>Eligibility and replacement mask shared by the painted Frost cave roof and its wall caster.</summary>
    internal sealed class StormFrostGeometry
    {
        const int MinFootprint = 12;
        internal const string RoofResource = "StormInk/Frost/IceRoof";
        static readonly ConditionalWeakTable<MapBlob, StormFrostGeometry> Results =
            new ConditionalWeakTable<MapBlob, StormFrostGeometry>();
        static readonly StormFrostGeometry Ineligible = new StormFrostGeometry(null, null);

        readonly MapAnchor _anchor;
        readonly bool[] _wallMask;

        StormFrostGeometry(MapAnchor anchor, bool[] wallMask)
        {
            _anchor = anchor;
            _wallMask = wallMask;
        }

        internal bool Eligible => _anchor != null;
        internal bool Matches(MapAnchor anchor) => Eligible && ReferenceEquals(_anchor, anchor);
        internal bool[] ReplacementWalls() => Eligible && HasReadableRoof() ? _wallMask : null;
        internal static StormFrostGeometry For(MapBlob map) => Results.GetValue(map, Find);

        static StormFrostGeometry Find(MapBlob map)
        {
            if (map == null || map.Anchors == null || map.Tiles == null) return Ineligible;
            foreach (var anchor in map.Anchors)
            {
                if (anchor.Zone != (byte)RegionId.Frost || anchor.Kind != MapBlob.AnchorKindCave) continue;
                var f = anchor.Footprint;
                if (f.W < MinFootprint || f.H < MinFootprint || f.X + f.W > map.W || f.Y + f.H > map.H || anchor.Entrance.Length != 3) continue;
                var interior = LandmarkInterior.Find(map.Tiles, map.W, map.H, f, anchor.Entrance);
                if (LandmarkInterior.Count(interior) == 0 || !HasEdgeArch(map, anchor, f)) continue;

                var mask = new bool[map.W * map.H];
                for (int y = 0; y < f.H; y++)
                for (int x = 0; x < f.W; x++)
                {
                    int i = (f.Y + y) * map.W + f.X + x;
                    mask[i] = map.Tiles[i] == MapBlob.Wall;
                }
                return new StormFrostGeometry(anchor, mask);
            }
            return Ineligible;
        }

        static bool HasReadableRoof()
        {
            var sprite = Resources.Load<Texture2D>(RoofResource);
            return sprite != null && sprite.isReadable;
        }

        static bool HasEdgeArch(MapBlob map, MapAnchor anchor, MapRect f)
        {
            var first = anchor.Entrance[0];
            int minX = first.X, maxX = first.X, minY = first.Y, maxY = first.Y;
            foreach (var tile in anchor.Entrance)
            {
                if (tile.X >= map.W || tile.Y >= map.H || map.At(tile.X, tile.Y) != MapBlob.Floor) return false;
                minX = Mathf.Min(minX, tile.X); maxX = Mathf.Max(maxX, tile.X);
                minY = Mathf.Min(minY, tile.Y); maxY = Mathf.Max(maxY, tile.Y);
            }
            bool onX = minX == maxX && maxY - minY == 2 && (minX == f.X || minX == f.X + f.W - 1);
            bool onY = minY == maxY && maxX - minX == 2 && (minY == f.Y || minY == f.Y + f.H - 1);
            return onX || onY;
        }
    }
}
