using System.Collections.Generic;
using UnityEngine;

namespace Roguehalla.Client
{
    /// <summary>Projects connected liquid reaches away from their chosen source.</summary>
    internal sealed partial class StormBoundCurrent
    {
        // A single painted centreline can run upstream on one arm of a fork. Start each
        // liquid reach at the same source and keep the coordinates of its closest wet spine.
        void ProjectOutlets(int component, int vent)
        {
            int source = component;
            Vector2 origin = Position(vent);
            float nearest = float.PositiveInfinity;
            foreach (int cell in _cells)
            {
                float distance = (Position(cell) - origin).sqrMagnitude;
                if (distance > nearest || (distance == nearest && cell >= source)) continue;
                source = cell;
                nearest = distance;
            }

            Farthest(component, source);
            var tips = new List<(int distance, int cell)>();
            foreach (int cell in _cells)
            {
                int distance = _distance[cell];
                bool end = true;
                for (int d = 0; d < 4; d++)
                {
                    int next = Neighbour(cell, d);
                    if (next != Unvisited && _component[next] == component && _distance[next] > distance)
                    { end = false; break; }
                }
                if (end) tips.Add((distance, cell));
            }
            tips.Sort((a, b) => a.distance != b.distance ? b.distance.CompareTo(a.distance) : a.cell.CompareTo(b.cell));
            foreach (var tip in tips)
            {
                // Wide ends have neighbouring maxima on opposite banks. A full channel width
                // between a new tip and the nearest painted line distinguishes another outlet.
                if (_bestCross[tip.cell] <= Mathf.Max(3, _shore[tip.cell] + 2)) continue;
                CentrePath(component, source, tip.cell);
                Project(component);
            }
        }
    }
}
