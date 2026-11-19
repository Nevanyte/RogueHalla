using System;
using Roguehalla.Net.Protocol;

namespace Roguehalla.Net
{
    /// <summary>The standing-wall part of <see cref="LocalMotion"/>: the walk map with walls LOW, and the re-plan when a wall rises on the remaining walk.</summary>
    public sealed partial class LocalMotion
    {
        /// <summary>
        /// P-FROST-WALL-PREDICT-1: the standing walls the client holds (<c>Collections.Fields</c>, rows of kind
        /// <see cref="AreaKind.Wall"/>; other kinds ignored). Their tiles are LOW on <see cref="WalkMap"/>, as the server's
        /// <c>barriers::body_map</c> (<c>map.with_low</c>: a FLOOR tile becomes LOW). When a wall was added and the remaining walk
        /// runs into the new map (rh-sim <c>barriers::walk_blocked</c>: each remaining leg from the model position, a 12 u disc),
        /// the walk is re-planned from the model position to its last waypoint with the own <see cref="Foot"/>, as
        /// <c>raise_wall</c> (<c>effects.rs:350-371</c>) does in the landing tick; an unreachable goal stops it. A wall that
        /// falls re-plans nothing. Called after every State message (before that tick's snapshot); the same wall set again,
        /// in any array, changes nothing and allocates nothing. Suspended prediction keeps the walls for when it resumes.
        /// </summary>
        public void SetWalls(System.Collections.Generic.IReadOnlyList<AreaView> fields)
        {
            int count = 0;
            bool same = true, added = false;
            int n = fields == null ? 0 : fields.Count;
            for (int i = 0; i < n; i++)
            {
                AreaView f = fields[i];
                if (f.Kind != AreaKind.Wall || f.Tiles == null) continue;
                if (count >= _wallCount || !_wallKeys[count].Equals(f.Key)
                    || !SameWallCells(_wallTiles[count], f.Tiles)) same = false;
                if (!added && !HoldsWall(f.Key)) added = true;
                count++;
            }
            if (same && count == _wallCount) return;
            if (count > _wallKeys.Length)
            {
                int size = Math.Max(count, _wallKeys.Length * 2);
                _wallKeys = new AreaKey[size];
                _wallTiles = new MapTile[size][];
            }
            count = 0;
            for (int i = 0; i < n; i++)
            {
                AreaView f = fields[i];
                if (f.Kind != AreaKind.Wall || f.Tiles == null) continue;
                _wallKeys[count] = f.Key;
                _wallTiles[count] = f.Tiles;
                count++;
            }
            for (int i = count; i < _wallCount; i++) _wallTiles[i] = null;
            _wallCount = count;
            RebuildWalkMap();
            if (added && Initialized && _alive && !_predictionSuspended && WalkBlocked())
            {
                PathPoint goal = _path[_path.Length - 1];
                _goalX = goal.X;
                _goalY = goal.Y;
                PlanFrom(_modelX, _modelY);
            }
        }

        /// <summary>
        /// P-FROST-WALL-PREDICT-1: the map the walk mirror plans and steers on (the base map with every standing wall's tiles
        /// LOW; the base map object itself when no wall stands). Keyboard steering reads it.
        /// </summary>
        public MapBlob WalkMap { get { return _walkMap; } }

        static bool SameWallCells(MapTile[] held, MapTile[] cells)
        {
            if (ReferenceEquals(held, cells)) return true;
            if (held.Length != cells.Length) return false;
            for (int i = 0; i < cells.Length; i++)
                if (held[i].X != cells[i].X || held[i].Y != cells[i].Y) return false;
            return true;
        }

        bool HoldsWall(AreaKey key)
        {
            for (int i = 0; i < _wallCount; i++) if (_wallKeys[i].Equals(key)) return true;
            return false;
        }

        /// <summary>The C# <c>TileMap::with_low</c> over the base map; allocates a new map only when a wall stands.</summary>
        void RebuildWalkMap()
        {
            if (_map == null || _wallCount == 0) { _walkMap = _map; return; }
            byte[] tiles = (byte[])_map.Tiles.Clone();
            int w = _map.W, h = _map.H;
            for (int i = 0; i < _wallCount; i++)
            {
                MapTile[] cells = _wallTiles[i];
                for (int j = 0; j < cells.Length; j++)
                {
                    int x = cells[j].X, y = cells[j].Y;
                    if (x >= w || y >= h) continue;
                    int at = y * w + x;
                    if (tiles[at] == MapBlob.Floor) tiles[at] = MapBlob.Low;
                }
            }
            _walkMap = new MapBlob(_map.W, _map.H, tiles);
        }

        /// <summary>rh-sim <c>barriers::walk_blocked</c>: does the remaining walk from the model position cross the walk map?</summary>
        bool WalkBlocked()
        {
            if (_walkMap == null || _pathIndex >= _path.Length) return false;
            double fx = _modelX, fy = _modelY;
            for (int i = _pathIndex; i < _path.Length; i++)
            {
                if (!_pathfinder.SegmentClear(_walkMap, fx, fy, _path[i].X, _path[i].Y, WallReplanRadius)) return true;
                fx = _path[i].X;
                fy = _path[i].Y;
            }
            return false;
        }

        /// <summary>rh-sim <c>PLAYER_RADIUS</c>: <c>walk_blocked</c> tests every fighter's legs with this disc, whatever its foot.</summary>
        const double WallReplanRadius = 12.0;
    }
}
