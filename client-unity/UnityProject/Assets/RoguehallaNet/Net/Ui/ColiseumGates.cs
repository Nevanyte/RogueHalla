// P-COLISEUM-GATES-1: the coliseum gate geometry the client reads from the Welcome map (MapBlob.Coliseum.Gates). Engine-free.
// ClosedMap mirrors rh-sim TileMap::closed_gates (crates/rh-sim/src/map.rs:168-194; legacy shared/src/map.ts:318-336
// withGates): the server's body map while the gates are locked. ClosedTiles is the list the closed-gate drawing reads
// (legacy MapView.drawGates / setGatesLocked, MapView.ts:572-618). Presentation and prediction only: the server decides.
using System.Collections.Generic;
using Roguehalla.Net.Protocol;

namespace Roguehalla.Net.Ui
{
    /// <summary>The coliseum gate tiles of the Welcome map: the closed-gate tile list for the drawing and the locked body map
    /// for the walk mirror.</summary>
    public static class ColiseumGates
    {
        /// <summary>Clears <paramref name="into"/>, then, when <paramref name="gatesLocked"/>, adds every gate's tiles, gate by
        /// gate, in delivered order. Nothing when open, when <paramref name="map"/> is null or has no coliseum.</summary>
        public static void ClosedTiles(MapBlob map, bool gatesLocked, List<MapTile> into)
        {
            into.Clear();
            if (!gatesLocked || map == null || map.Coliseum == null || map.Coliseum.Gates == null) return;
            var gates = map.Coliseum.Gates;
            for (int g = 0; g < gates.Length; g++)
            {
                var tiles = gates[g].Tiles;
                if (tiles == null) continue;
                for (int i = 0; i < tiles.Length; i++) into.Add(tiles[i]);
            }
        }

        /// <summary>rh-sim <c>closed_gates</c>: a new map with <paramref name="map"/>'s collision classes and every in-bounds gate
        /// tile WALL, or null when <paramref name="map"/> is null or has no gate tile (the server then keeps the open map).
        /// <paramref name="map"/> itself is never edited. The copy carries the collision layer only (what the walk mirror reads).</summary>
        public static MapBlob ClosedMap(MapBlob map)
        {
            if (map == null || map.Coliseum == null || map.Coliseum.Gates == null) return null;
            int w = map.W, h = map.H;
            byte[] tiles = null;
            var gates = map.Coliseum.Gates;
            for (int g = 0; g < gates.Length; g++)
            {
                var cells = gates[g].Tiles;
                if (cells == null) continue;
                for (int i = 0; i < cells.Length; i++)
                {
                    int x = cells[i].X, y = cells[i].Y;
                    if (x >= w || y >= h) continue;
                    if (tiles == null) tiles = (byte[])map.Tiles.Clone();
                    tiles[y * w + x] = MapBlob.Wall;
                }
            }
            return tiles == null ? null : new MapBlob(map.W, map.H, tiles);
        }
    }
}
