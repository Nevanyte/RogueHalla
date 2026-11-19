using System.Collections.Generic;
using Roguehalla.Net.Protocol;
using UnityEngine;

namespace Roguehalla.Client
{
    /// <summary>Builds map-space current coordinates from the public wet cells once per ground material.</summary>
    internal sealed partial class StormBoundCurrent
    {
        const byte Lava = 1, Shallow = 2;
        const int Unvisited = -1;
        readonly int _width, _height;
        readonly byte[] _wet;
        readonly int[] _component, _shore, _distance, _parent, _station, _bestCross, _cross;
        readonly Color[] _field;
        readonly Vector2[] _tangent;
        readonly List<int> _cells = new List<int>(), _path = new List<int>();
        readonly List<Vector2> _centres = new List<Vector2>();
        readonly List<float> _lengths = new List<float>();
        readonly Queue<int> _queue = new Queue<int>();
        readonly SortedSet<(int cost, int cell)> _pending = new SortedSet<(int, int)>();

        StormBoundCurrent(int width, int height)
        {
            _width = width;
            _height = height;
            int count = width * height;
            _wet = new byte[count];
            _component = new int[count];
            _shore = new int[count];
            _distance = new int[count];
            _parent = new int[count];
            _station = new int[count];
            _bestCross = new int[count];
            _cross = new int[count];
            _field = new Color[count];
            _tangent = new Vector2[count];
            for (int i = 0; i < count; i++) _component[i] = Unvisited;
        }

        internal static Texture2D CreateTexture(MapBlob map, PlagueGround plague, byte[] effectiveTerrain)
        {
            var field = Bake(map, plague, effectiveTerrain);
            if (!System.Array.Exists(field, cell => cell.b > 0f)) return null;
            var half = new ushort[field.Length * 4];
            for (int i = 0; i < field.Length; i++)
            {
                var cell = field[i];
                half[4 * i] = Mathf.FloatToHalf(cell.r);
                half[4 * i + 1] = Mathf.FloatToHalf(cell.g);
                half[4 * i + 2] = Mathf.FloatToHalf(cell.b);
                half[4 * i + 3] = Mathf.FloatToHalf(cell.a);
            }
            var flow = new Texture2D(map.W, map.H, TextureFormat.RGBAHalf, false, true)
            {
                name = "Storm Ink current field",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear
            };
            flow.SetPixelData(half, 0);
            flow.Apply(false, true);
            return flow;
        }

        // R downstream arclength, G signed lateral distance, B shore depth, all in tiles; A flow activity.
        internal static Color[] Bake(MapBlob map, PlagueGround plague, byte[] effectiveTerrain)
        {
            var current = new StormBoundCurrent(map.W, map.H);
            int volcano = Unvisited;
            foreach (var anchor in map.Anchors)
                if (anchor.Kind == MapBlob.AnchorKindVolcano && anchor.Zone == (byte)RegionId.Fire)
                {
                    volcano = anchor.Centre.Y * map.W + anchor.Centre.X;
                    break;
                }

            for (int i = 0; i < current._wet.Length; i++)
            {
                byte terrain = effectiveTerrain == null ? (byte)TerrainKind.None : effectiveTerrain[i];
                byte collision = map.Tiles[i];
                if (terrain == (byte)TerrainKind.Lava && collision != MapBlob.Wall) current._wet[i] = Lava;
                else if (terrain == (byte)TerrainKind.None && collision == MapBlob.Floor &&
                    map.Regions != null && map.Regions[i] == (byte)RegionId.Plague &&
                    plague.TryGet(i % map.W, i / map.W, RegionId.Plague, out var surface) &&
                    surface == SurfaceKind.ShallowWater)
                    current._wet[i] = Shallow;
            }

            for (int i = 0; i < current._wet.Length; i++)
            {
                if (current._wet[i] == 0 || current._component[i] != Unvisited) continue;
                current.BuildComponent(i, current._wet[i] == Lava ? volcano : Unvisited);
            }
            current.AddDryGuards();
            return current._field;
        }

        // Cardinal adjacency cannot cut a dry corner or transfer into a different fluid.
        int Neighbour(int i, int direction)
        {
            switch (direction)
            {
                case 0: return i % _width > 0 ? i - 1 : Unvisited;
                case 1: return i % _width + 1 < _width ? i + 1 : Unvisited;
                case 2: return i >= _width ? i - _width : Unvisited;
                default: return i / _width + 1 < _height ? i + _width : Unvisited;
            }
        }

        Vector2 Position(int i) => new Vector2(i % _width, i / _width);

        void BuildComponent(int first, int upstream)
        {
            _cells.Clear();
            _component[first] = first;
            _queue.Enqueue(first);
            while (_queue.Count > 0)
            {
                int i = _queue.Dequeue();
                _cells.Add(i);
                bool bank = false;
                for (int d = 0; d < 4; d++)
                {
                    int n = Neighbour(i, d);
                    if (n == Unvisited || _wet[n] != _wet[first]) { bank = true; continue; }
                    if (_component[n] != Unvisited) continue;
                    _component[n] = first;
                    _queue.Enqueue(n);
                }
                _shore[i] = bank ? 1 : 0;
            }

            foreach (int i in _cells) if (_shore[i] == 1) _queue.Enqueue(i);
            while (_queue.Count > 0)
            {
                int i = _queue.Dequeue();
                for (int d = 0; d < 4; d++)
                {
                    int n = Neighbour(i, d);
                    if (n == Unvisited || _component[n] != first || _shore[n] != 0) continue;
                    _shore[n] = _shore[i] + 1;
                    _queue.Enqueue(n);
                }
            }

            foreach (int i in _cells) _bestCross[i] = int.MaxValue;
            if (upstream != Unvisited)
            {
                ProjectOutlets(first, upstream);
                return;
            }
            // Unanchored water has no delivered source. Pick a stable direction for each connected run.
            int a = Farthest(first, first), b = Farthest(first, a);
            if (b < a) (a, b) = (b, a);
            CentrePath(first, a, b);
            Project(first);
        }

        int Farthest(int component, int start)
        {
            foreach (int i in _cells) _distance[i] = Unvisited;
            _distance[start] = 0;
            _queue.Enqueue(start);
            int farthest = start;
            while (_queue.Count > 0)
            {
                int i = _queue.Dequeue();
                if (_distance[i] > _distance[farthest] ||
                    (_distance[i] == _distance[farthest] && i < farthest)) farthest = i;
                for (int d = 0; d < 4; d++)
                {
                    int n = Neighbour(i, d);
                    if (n == Unvisited || _component[n] != component || _distance[n] != Unvisited) continue;
                    _distance[n] = _distance[i] + 1;
                    _queue.Enqueue(n);
                }
            }
            return farthest;
        }

        void CentrePath(int component, int start, int end)
        {
            foreach (int i in _cells) _distance[i] = int.MaxValue;
            _distance[start] = 0;
            _parent[start] = Unvisited;
            _pending.Add((0, start));
            while (_pending.Count > 0)
            {
                var next = _pending.Min;
                _pending.Remove(next);
                int i = next.cell;
                if (i == end) { _pending.Clear(); break; }
                for (int d = 0; d < 4; d++)
                {
                    int n = Neighbour(i, d);
                    if (n == Unvisited || _component[n] != component) continue;
                    // Prefer deeper cells around a bend without allowing any path onto a bank.
                    int cost = next.cost + 10 + 12 / Mathf.Min(_shore[i], _shore[n]);
                    if (cost >= _distance[n]) continue;
                    _pending.Remove((_distance[n], n));
                    _distance[n] = cost;
                    _parent[n] = i;
                    _pending.Add((cost, n));
                }
            }
            _path.Clear();
            for (int i = end; i != Unvisited; i = _parent[i]) _path.Add(i);
            _path.Reverse();
            _centres.Clear();
            _lengths.Clear();
            for (int s = 0; s < _path.Count; s++)
            {
                // Round off the cardinal stair-steps of a diagonal stream. Project every wet centre
                // into this same smooth frame so the two sides of the brush keep their width.
                int lo = Mathf.Max(0, s - 1), hi = Mathf.Min(s + 1, _path.Count - 1);
                Vector2 point = Position(_path[s]);
                if (s != lo && s != hi)
                    point = (Position(_path[lo]) + 2f * point + Position(_path[hi])) * .25f;
                _centres.Add(point);
                _lengths.Add(s == 0 ? 0f : _lengths[lo] + Vector2.Distance(point, _centres[lo]));
            }
        }

        void Project(int component)
        {
            foreach (int i in _cells) _station[i] = Unvisited;
            for (int s = 0; s < _path.Count; s++)
            {
                _station[_path[s]] = s;
                _cross[_path[s]] = 0;
                _queue.Enqueue(_path[s]);
            }
            // Propagate from the spine through wet cells, never across an island or bank.
            while (_queue.Count > 0)
            {
                int i = _queue.Dequeue();
                int s = _station[i];
                for (int d = 0; d < 4; d++)
                {
                    int n = Neighbour(i, d);
                    if (n == Unvisited || _component[n] != component || _station[n] != Unvisited) continue;
                    _station[n] = s;
                    _cross[n] = _cross[i] + 1;
                    _queue.Enqueue(n);
                }
                if (_cross[i] >= _bestCross[i]) continue;
                _bestCross[i] = _cross[i];
                Vector2 axis = _centres[Mathf.Min(s + 2, _path.Count - 1)] - _centres[Mathf.Max(s - 2, 0)];
                Vector2 offset = Position(i) - _centres[s];
                Vector2 tangent = axis.normalized;
                _tangent[i] = tangent;
                float lateral = tangent.x * offset.y - tangent.y * offset.x;
                float longitudinal = Mathf.Clamp(_lengths[s] + Vector2.Dot(tangent, offset), 0, _lengths[_lengths.Count - 1]);
                float width = _shore[_path[s]];
                float tip = Mathf.Clamp01(Mathf.Min(s, _path.Count - s - 1) / 3f);
                float channel = 1f - Mathf.SmoothStep(0, 1, Mathf.InverseLerp(1.5f, 3.5f, width));
                float nearSpine = 1f - Mathf.SmoothStep(0, 1, Mathf.InverseLerp(width + .5f, width + 2f, offset.magnitude));
                float activity = tip * channel * nearSpine * Mathf.Clamp01((_path.Count - 4f) / 4f);
                _field[i] = new Color(longitudinal, lateral, _shore[i] - .5f, activity);
            }
        }

        void AddDryGuards()
        {
            for (int i = 0; i < _wet.Length; i++)
            {
                if (_wet[i] != 0) continue;
                // Bilinear samples on a one-tile stream straddle both banks. Continue its local coordinate
                // grid across the half-texel apron, leaving every dry texel at zero depth and speed.
                int x = i % _width, y = i / _width;
                int component = Unvisited;
                bool shared = false;
                Vector2 projected = Vector2.zero;
                float weights = 0;
                for (int dy = -1; dy <= 1; dy++)
                for (int dx = -1; dx <= 1; dx++)
                {
                    int nx = x + dx, ny = y + dy;
                    if (nx < 0 || nx >= _width || ny < 0 || ny >= _height) continue;
                    int n = ny * _width + nx;
                    if (_wet[n] == 0) continue;
                    if (component != Unvisited && _component[n] != component) shared = true;
                    component = _component[n];
                    Vector2 tangent = _tangent[n];
                    var delta = new Vector2(-dx, -dy);
                    float weight = 1f / delta.sqrMagnitude;
                    float station = _field[n].r + Vector2.Dot(tangent, delta);
                    float side = _field[n].g + tangent.x * delta.y - tangent.y * delta.x;
                    projected += new Vector2(station, side) * weight;
                    weights += weight;
                }
                if (!shared && weights > 0)
                    _field[i] = new Color(projected.x / weights, projected.y / weights, 0, 0);
            }
        }
    }
}
