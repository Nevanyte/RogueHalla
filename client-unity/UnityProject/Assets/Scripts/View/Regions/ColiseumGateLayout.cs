using System.Collections.Generic;
using Roguehalla.Net.Protocol;
using UnityEngine;

namespace Roguehalla.Client
{
    /// <summary>Presentation-only geometry derived from the delivered Coliseum gate rows.</summary>
    public static class ColiseumGateLayout
    {
        public struct Gate
        {
            public byte Side;
            public readonly List<MapTile> Tiles;
            public Gate(byte side, List<MapTile> tiles) { Side = side; Tiles = tiles; }
            public bool RunsAlongScreen => Side == 0 || Side == 2;
        }

        public static bool IsArtReady()
        {
            return RegionPropArt.TryGet("col.gate.pier", out var art) && art != null && art.Sprite != null &&
                   art.UsesAuthoredTexture;
        }

        /// <summary>Returns only contiguous, in-bounds, declared-side gate runs. No guessed gates are created.</summary>
        public static List<Gate> DeliveredGates(MapBlob map)
        {
            var result = new List<Gate>();
            if (map == null || map.Coliseum == null || map.Coliseum.Gates == null || map.Tiles == null ||
                map.Tiles.Length != map.W * map.H) return result;
            foreach (var source in map.Coliseum.Gates)
            {
                if (source == null || source.Tiles == null || source.Tiles.Length == 0 || source.Side > 3) continue;
                var col = map.Coliseum;
                if (col.W == 0 || col.H == 0 || (long)col.X + col.W > map.W || (long)col.Y + col.H > map.H) continue;
                var tiles = new List<MapTile>(source.Tiles.Length);
                bool valid = true;
                int fixedCoord = -1;
                foreach (var t in source.Tiles)
                {
                    int fixedAxis = source.Side == 0 || source.Side == 2 ? t.Y : t.X;
                    int expectedFixed = source.Side == 0 ? col.Y : source.Side == 2 ? col.Y + col.H - 1 :
                        source.Side == 1 ? col.X + col.W - 1 : col.X;
                    int alongMin = source.Side == 0 || source.Side == 2 ? col.X : col.Y;
                    int alongMax = alongMin + (source.Side == 0 || source.Side == 2 ? col.W : col.H);
                    if (t.X >= map.W || t.Y >= map.H || map.Tiles[t.Y * map.W + t.X] != MapBlob.Floor ||
                        fixedAxis != expectedFixed ||
                        (source.Side == 0 || source.Side == 2 ? t.X : t.Y) < alongMin ||
                        (source.Side == 0 || source.Side == 2 ? t.X : t.Y) >= alongMax ||
                        (fixedCoord >= 0 && fixedAxis != fixedCoord))
                    { valid = false; break; }
                    fixedCoord = fixedAxis; tiles.Add(t);
                }
                if (!valid || tiles.Count == 0) continue;
                tiles.Sort((a, b) => (source.Side == 0 || source.Side == 2 ? a.X.CompareTo(b.X) : a.Y.CompareTo(b.Y)));
                for (int i = 1; i < tiles.Count; i++)
                {
                    int previous = source.Side == 0 || source.Side == 2 ? tiles[i - 1].X : tiles[i - 1].Y;
                    int current = source.Side == 0 || source.Side == 2 ? tiles[i].X : tiles[i].Y;
                    if (current != previous + 1) { valid = false; break; }
                }
                if (!valid) continue;
                if (!HasSolidEndpoint(map, source.Side, tiles[0], -1) ||
                    !HasSolidEndpoint(map, source.Side, tiles[tiles.Count - 1], 1)) continue;
                result.Add(new Gate(source.Side, tiles));
            }
            return result;
        }

        public static List<Vector2Int> PierCells(MapBlob map, bool artReady)
        {
            var result = new List<Vector2Int>();
            if (!artReady) return result;
            foreach (var gate in DeliveredGates(map))
            {
                var first = gate.Tiles[0]; var last = gate.Tiles[gate.Tiles.Count - 1];
                Vector2Int before, after;
                if (gate.Side == 0 || gate.Side == 2)
                {
                    before = new Vector2Int(first.X - 1, first.Y); after = new Vector2Int(last.X + 1, last.Y);
                }
                else
                {
                    before = new Vector2Int(first.X, first.Y - 1); after = new Vector2Int(last.X, last.Y + 1);
                }
                result.Add(before); result.Add(after);
            }
            return result;
        }

        static bool HasSolidEndpoint(MapBlob map, byte side, MapTile tile, int direction)
        {
            int x = tile.X, y = tile.Y;
            if (side == 0 || side == 2) x += direction;
            else y += direction;
            return IsSolid(map, x, y);
        }

        public static bool IsSolid(MapBlob map, int x, int y)
        {
            if (map == null || x < 0 || y < 0 || x >= map.W || y >= map.H || map.Tiles == null) return false;
            int i = y * map.W + x;
            if (map.Tiles[i] != MapBlob.Wall) return false;
            var terrain = map.Terrain;
            if (terrain == null) return true;
            if (terrain.Length != map.W * map.H) return false;
            return terrain[i] != (byte)TerrainKind.Lava && terrain[i] != (byte)TerrainKind.Crevasse;
        }
    }
}
