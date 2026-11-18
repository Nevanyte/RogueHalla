using System;
using Roguehalla.Net.Protocol;

namespace Roguehalla.Net
{
    /// <summary>The A* search of <see cref="Pathfinder"/>: the binary heap, its tie-breaking comparator and the octile heuristic.</summary>
    public sealed partial class Pathfinder
    {
        // ----------------------------------------------------------------------------------------------------------------
        // A* (astar) and its heap

        /// <summary>f1/g1/n1 sorts before f2/g2/n2: f within PATH_TIE_EPS, then larger g, then smaller node index.</summary>
        static bool Before(double f1, double g1, int n1, double f2, double g2, int n2)
        {
            if (Math.Abs(f1 - f2) > PathTieEps) return f1 < f2;
            if (g1 != g2) return g1 > g2;
            return n1 < n2;
        }

        static double Octile(long x, long y, long gx, long gy)
        {
            double dx = Math.Abs((double)(x - gx));
            double dy = Math.Abs((double)(y - gy));
            return dx + dy + (Sqrt2 - 2.0) * Math.Min(dx, dy);
        }

        void Reset(int n)
        {
            if (_n != n || _base >= uint.MaxValue - 2)
            {
                _n = n;
                _nodes = new Node[n];
                _base = 0;
                if (_heap == null) _heap = new HeapItem[1024];
            }
            // Every stamp is now below base: g = infinity, open.
            _base += 2;
            _heapCount = 0;
        }

        void Push(int node, double f, double g)
        {
            if (_heapCount == _heap.Length) Array.Resize(ref _heap, _heap.Length * 2);
            _heap[_heapCount++] = new HeapItem { Node = node, F = f, G = g };
            int i = _heapCount - 1;
            while (i > 0)
            {
                int p = (i - 1) >> 1;
                if (!Before(f, g, node, _heap[p].F, _heap[p].G, _heap[p].Node)) break;
                _heap[i] = _heap[p];
                i = p;
            }
            _heap[i] = new HeapItem { Node = node, F = f, G = g };
        }

        int Pop()
        {
            int top = _heap[0].Node;
            HeapItem lastItem = _heap[--_heapCount];
            int last = _heapCount;
            if (last > 0)
            {
                int node = lastItem.Node;
                double f = lastItem.F, g = lastItem.G;
                int i = 0;
                while (true)
                {
                    int c = 2 * i + 1;
                    if (c >= last) break;
                    if (c + 1 < last
                        && Before(_heap[c + 1].F, _heap[c + 1].G, _heap[c + 1].Node, _heap[c].F, _heap[c].G, _heap[c].Node))
                    {
                        c++;
                    }
                    if (!Before(_heap[c].F, _heap[c].G, _heap[c].Node, f, g, node)) break;
                    _heap[i] = _heap[c];
                    i = c;
                }
                _heap[i] = lastItem;
            }
            return top;
        }

        bool AStar(int start, int goal)
        {
            int w = _w;
            Reset(w * _h);
            _searches++;
            uint baseStamp = _base;
            uint closed = baseStamp + 1;
            int gx = goal % w;
            int gy = goal / w;
            _nodes[start] = new Node { G = 0.0, Parent = -1, Stamp = baseStamp };
            Push(start, Octile(start % w, start / w, gx, gy), 0.0);
            while (_heapCount > 0)
            {
                int node = Pop();
                if (_nodes[node].Stamp == closed) continue;
                _nodes[node].Stamp = closed;
                if (node == goal)
                {
                    _tilePathLen = 0;
                    int n = node;
                    while (n != -1)
                    {
                        AddTile(n);
                        n = _nodes[n].Parent;
                    }
                    Array.Reverse(_tilePath, 0, _tilePathLen);
                    return true;
                }
                int x = node % w;
                int y = node / w;
                double g = _nodes[node].G;
                byte steps = _nav[node];
                for (int d = 0; d < 8; d++)
                {
                    if ((steps & (1 << d)) == 0) continue;
                    int nx = x + StepDx[d];
                    int ny = y + StepDy[d];
                    int ni = ny * w + nx;
                    Node nn = _nodes[ni];
                    if (nn.Stamp == closed) continue;
                    double ng = g + (d >= 4 ? Sqrt2 : 1.0);
                    double oldG = nn.Stamp == baseStamp ? nn.G : double.PositiveInfinity;
                    if (ng < oldG)
                    {
                        _nodes[ni] = new Node { G = ng, Parent = node, Stamp = baseStamp };
                        Push(ni, ng + Octile(nx, ny, gx, gy), ng);
                    }
                }
            }
            return false;
        }
    }
}
