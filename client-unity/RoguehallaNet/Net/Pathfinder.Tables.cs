using System;
using Roguehalla.Net.Protocol;

namespace Roguehalla.Net
{
    /// <summary>The per-map tables and scratch growth of <see cref="Pathfinder"/>: navigation tables built once per map instance, and the buffers queries reuse.</summary>
    public sealed partial class Pathfinder
    {
        // ----------------------------------------------------------------------------------------------------------------
        // Derived map tables

        /// <summary>Call when a caller mutates collision tiles in place; the next query rebuilds navigation tables.</summary>
        public void InvalidateMap() { _map = null; }

        void Prepare(MapBlob map)
        {
            if (map == null) throw new ArgumentNullException(nameof(map));
            if (ReferenceEquals(map, _map)) return;
            _map = map;
            _w = map.W;
            _h = map.H;
            BuildTables();
            // Force the next Reset to allocate the node array for this map size.
            _n = 0;
            _base = 0;
            _heapCount = 0;
            _tilePathLen = 0;
            _candLen = 0;
            _outLen = 0;
        }

        void BuildTables()
        {
            int n = _w * _h;
            byte[] tiles = _map.Tiles;
            _comp = new int[n];
            for (int i = 0; i < n; i++) _comp[i] = -1;
            var queue = new int[n];
            int id = 0;
            for (int s = 0; s < n; s++)
            {
                if (tiles[s] != MapBlob.Floor || _comp[s] >= 0) continue;
                int head = 0, tail = 0;
                queue[tail++] = s;
                _comp[s] = id;
                while (head < tail)
                {
                    int i = queue[head++];
                    int x = i % _w;
                    if (x > 0) Visit(i - 1, id, tiles, queue, ref tail);
                    if (x < _w - 1) Visit(i + 1, id, tiles, queue, ref tail);
                    if (i >= _w) Visit(i - _w, id, tiles, queue, ref tail);
                    if (i < n - _w) Visit(i + _w, id, tiles, queue, ref tail);
                }
                id++;
            }

            _nav = new byte[n];
            for (int y = 0; y < _h; y++)
            {
                for (int x = 0; x < _w; x++)
                {
                    int e = FloorBit(x + 1, y);
                    int wv = FloorBit(x - 1, y);
                    int s = FloorBit(x, y + 1);
                    int no = FloorBit(x, y - 1);
                    _nav[y * _w + x] = (byte)(e
                        | wv << 1
                        | s << 2
                        | no << 3
                        | ((FloorBit(x + 1, y + 1) & e & s) << 4)
                        | ((FloorBit(x + 1, y - 1) & e & no) << 5)
                        | ((FloorBit(x - 1, y + 1) & wv & s) << 6)
                        | ((FloorBit(x - 1, y - 1) & wv & no) << 7));
                }
            }
        }

        void Visit(int j, int id, byte[] tiles, int[] queue, ref int tail)
        {
            if (tiles[j] == MapBlob.Floor && _comp[j] < 0)
            {
                _comp[j] = id;
                queue[tail++] = j;
            }
        }

        int FloorBit(int x, int y)
        {
            return x >= 0 && y >= 0 && x < _w && y < _h && _map.Tiles[y * _w + x] == MapBlob.Floor ? 1 : 0;
        }

        // ----------------------------------------------------------------------------------------------------------------
        // Scratch growth

        void AddTile(int tile)
        {
            if (_tilePathLen == _tilePath.Length) Array.Resize(ref _tilePath, _tilePath.Length * 2);
            _tilePath[_tilePathLen++] = tile;
        }

        void AddCand(double x, double y)
        {
            if (_candLen == _candX.Length)
            {
                Array.Resize(ref _candX, _candX.Length * 2);
                Array.Resize(ref _candY, _candY.Length * 2);
            }
            _candX[_candLen] = x;
            _candY[_candLen] = y;
            _candLen++;
        }

        PathPoint CandAt(int j) { return new PathPoint(_candX[j], _candY[j]); }

        void EnsureCandClear()
        {
            if (_candClear.Length < _candLen) _candClear = new byte[Math.Max(_candLen, _candClear.Length * 2)];
        }

        void AddOut(PathPoint p)
        {
            if (_outLen == _out.Length) Array.Resize(ref _out, _out.Length * 2);
            _out[_outLen++] = p;
        }
    }
}
