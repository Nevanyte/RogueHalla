using System;
using Roguehalla.Net.Protocol;

namespace Roguehalla.Net
{
    /// <summary>A waypoint in world units (x right, y down), as <c>rh_sim::math::Vec2</c> on the server.</summary>
    public readonly struct PathPoint
    {
        public readonly double X;
        public readonly double Y;

        public PathPoint(double x, double y) { X = x; Y = y; }
        public override string ToString() { return "(" + X + ", " + Y + ")"; }
    }

    /// <summary>
    /// Exact C# port of the Rust authoritative pathfinder (<c>crates/rh-sim/src/pathfind.rs</c> <c>find_path</c>, itself an
    /// exact port of legacy <c>packages/shared/src/pathfind.ts</c>): A* over floor tiles with string pulling.
    /// <para>
    /// Everything that changes a waypoint is kept operation for operation: the binary heap with its sift order and the
    /// non-transitive <c>before</c> comparator (PATH_TIE_EPS 1e-9 ties fall through to g and then node index), the neighbour
    /// order d = 0..8, the octile heuristic with sqrt(2), the tile snap rings (SNAP_RADIUS 4), the start-centre slack 0.5,
    /// the goal clamp, the pull window 96 and the string pull's linear scan down from the far end of the window (clearness
    /// is not monotonic in j). All math is <c>double</c>; nothing is converted to <c>float</c> here.
    /// </para>
    /// <para>
    /// The map is the client's decoded <see cref="MapBlob"/> (the client never generates maps); the movement mask is
    /// MASK_MOVE (0xFF), so walls <em>and</em> LOW cover block. Derived navigation tables are built once per map instance.
    /// The instance holds scratch for one query at a time and is not thread-safe (the library is single-threaded by design).
    /// </para>
    /// </summary>
    public sealed partial class Pathfinder
    {
        /// <summary>Player body radius (rh-sim <c>PLAYER_RADIUS</c>).</summary>
        public const double PlayerRadius = 12.0;

        const double Tile = 32.0;                 // rh-sim TILE
        const double HalfTile = 16.0;             // TILE / 2
        const double Sqrt2 = 1.4142135623730951;  // core::f64::consts::SQRT_2
        const double PathTieEps = 1e-9;           // PATH_TIE_EPS
        const double StartCenterSlack = 0.5;      // START_CENTER_SLACK
        const int SnapRadius = 4;                 // SNAP_RADIUS
        const int PullWindow = 96;                // PULL_WINDOW
        const int MoveMask = 0xff;                // MASK_MOVE: walls and LOW block

        // Candidate disc memo (pathfind.rs UNKNOWN/CLEAR/BLOCKED).
        const byte DiscUnknown = 0, DiscClear = 1, DiscBlocked = 2;

        static readonly int[] StepDx = { 1, -1, 0, 0, 1, 1, -1, -1 };
        static readonly int[] StepDy = { 0, 0, 1, -1, 1, -1, 1, -1 };

        struct Node
        {
            public double G;
            public int Parent;
            public uint Stamp;
        }

        struct HeapItem
        {
            public int Node;
            public double F;
            public double G;
        }

        // ---- map identity and derived tables (built once per MapBlob instance) ----
        MapBlob _map;
        int _w, _h;
        byte[] _nav;
        int[] _comp;

        // ---- A* scratch (the generation stamp replaces a full clear, as in Rust) ----
        Node[] _nodes;
        uint _base;
        int _n;
        HeapItem[] _heap;
        int _heapCount;
        long _searches;

        // ---- path scratch ----
        int[] _tilePath = new int[16];
        int _tilePathLen;
        double[] _candX = new double[16];
        double[] _candY = new double[16];
        int _candLen;
        byte[] _candClear = new byte[16];
        PathPoint[] _out = new PathPoint[16];
        int _outLen;

        /// <summary>Searches run (diagnostics, mirrors <c>Pathfinder::searches</c>).</summary>
        public long Searches { get { return _searches; } }

        /// <summary>
        /// Waypoints (excluding the start point) that walk a disc of <see cref="PlayerRadius"/> from the start to the goal;
        /// empty when the goal is unreachable. A goal inside a wall snaps to the nearest floor tile within four rings; the
        /// last waypoint is the exact goal when the disc fits there.
        /// </summary>
        public PathPoint[] FindPath(MapBlob map, double fromX, double fromY, double toX, double toY)
        {
            return FindPath(map, fromX, fromY, toX, toY, Footprint.Disc(PlayerRadius));
        }

        /// <summary>As <see cref="FindPath(MapBlob,double,double,double,double)"/> with an explicit body radius (a disc).</summary>
        public PathPoint[] FindPath(MapBlob map, double fromX, double fromY, double toX, double toY, double radius)
        {
            return FindPath(map, fromX, fromY, toX, toY, Footprint.Disc(radius));
        }

        /// <summary>As the radius form with an explicit footprint (a disc, or an archetype's foot box, ADR0024).</summary>
        public PathPoint[] FindPath(MapBlob map, double fromX, double fromY, double toX, double toY, Footprint foot)
        {
            Plan(map, fromX, fromY, toX, toY, foot);
            var result = new PathPoint[_outLen];
            Array.Copy(_out, result, _outLen);
            return result;
        }

        /// <summary>
        /// Allocation-free form: writes the waypoints into <paramref name="destination"/> and returns their count (0 when
        /// unreachable). The destination must hold at least <c>map.W * map.H</c> entries to be safe for any query.
        /// </summary>
        public int FindPath(MapBlob map, double fromX, double fromY, double toX, double toY, double radius, PathPoint[] destination)
        {
            return FindPath(map, fromX, fromY, toX, toY, Footprint.Disc(radius), destination);
        }

        /// <summary>The allocation-free form with an explicit footprint.</summary>
        public int FindPath(MapBlob map, double fromX, double fromY, double toX, double toY, Footprint foot, PathPoint[] destination)
        {
            if (destination == null) throw new ArgumentNullException(nameof(destination));
            Plan(map, fromX, fromY, toX, toY, foot);
            if (_outLen > destination.Length)
                throw new ArgumentException("destination holds " + destination.Length + " waypoints, the path needs " + _outLen, nameof(destination));
            Array.Copy(_out, destination, _outLen);
            return _outLen;
        }

        /// <summary>
        /// Can a disc of <paramref name="radius"/> slide straight from a to b? The mirror of rh-sim <c>segment_clear</c> (MASK_MOVE):
        /// both end discs clear, the centre ray and the side rays clear. Read-only; used to choose which destination to send.
        /// </summary>
        public bool SegmentClear(MapBlob map, double ax, double ay, double bx, double by, double radius)
        {
            return SegmentClear(map, ax, ay, bx, by, Footprint.Disc(radius));
        }

        /// <summary>
        /// <see cref="SegmentClear(MapBlob,double,double,double,double,double)"/> for a footprint: the mirror of
        /// <c>Footprint::segment_clear</c> (a disc is the same evaluation as above; a box is <c>segment_box_clear</c>).
        /// </summary>
        public bool SegmentClear(MapBlob map, double ax, double ay, double bx, double by, Footprint foot)
        {
            Prepare(map);
            if (!FootClear(ax, ay, foot) || !FootClear(bx, by, foot)) return false;
            return SegmentRaysClear(new PathPoint(ax, ay), new PathPoint(bx, by), foot);
        }

        /// <summary>Does the footprint at (x, y) overlap no wall or LOW tile? Read-only (<c>Footprint::clear</c>, MASK_MOVE).</summary>
        public bool Clear(MapBlob map, double x, double y, Footprint foot)
        {
            Prepare(map);
            return FootClear(x, y, foot);
        }

        // ----------------------------------------------------------------------------------------------------------------
        // Planning (find_path_into)

        void Plan(MapBlob map, double fromX, double fromY, double toX, double toY, Footprint foot)
        {
            _outLen = 0;
            Prepare(map);
            // Each coordinate on its own: opposite extremes cancel in a sum.
            if (!(Finite(fromX) && Finite(fromY) && Finite(toX) && Finite(toY))) return;

            int w = _w;
            long sx = WorldToTile(fromX);
            long sy = WorldToTile(fromY);
            bool startInWall = !IsFloor(sx, sy);
            if (startInWall)
            {
                if (!SnapToFloor(fromX, fromY, out sx, out sy)) return;
            }
            long gx = WorldToTile(toX);
            long gy = WorldToTile(toY);
            var target = new PathPoint(toX, toY);
            bool goalInWall = !IsFloor(gx, gy);
            if (goalInWall)
            {
                if (!SnapToFloor(toX, toY, out gx, out gy)) return;
                target = new PathPoint(TileToCenter(gx), TileToCenter(gy));
            }
            if (!goalInWall) target = ClampClear(target, gx, gy, foot);

            // The end discs of from and target are pure functions of the map: evaluated at most once per query.
            bool? fromClear = null;
            bool? targetClear = null;
            if (!startInWall)
            {
                // segment_clear(from, target): the disc at from, the disc at target, then the rays.
                bool a = FootClear(fromX, fromY, foot);
                fromClear = a;
                if (a)
                {
                    bool b = FootClear(target.X, target.Y, foot);
                    targetClear = b;
                    if (b && SegmentRaysClear(new PathPoint(fromX, fromY), target, foot))
                    {
                        AddOut(target);
                        return;
                    }
                }
            }

            int si = (int)(sy * w + sx);
            int gi = (int)(gy * w + gx);
            if (_comp[si] != _comp[gi]) return;
            if (!AStar(si, gi)) return;

            // Candidates: the start tile centre when the start is in a wall or off-centre, the interior tiles, the goal
            // centre, and the clamped goal when it is not exactly that centre.
            _candLen = 0;
            if (_tilePathLen > 1
                && (startInWall
                    || Math.Abs(fromX - TileToCenter(sx)) > StartCenterSlack
                    || Math.Abs(fromY - TileToCenter(sy)) > StartCenterSlack))
            {
                AddCand(TileToCenter(_tilePath[0] % w), TileToCenter(_tilePath[0] / w));
            }
            if (_tilePathLen > 2)
            {
                for (int k = 1; k < _tilePathLen - 1; k++)
                    AddCand(TileToCenter(_tilePath[k] % w), TileToCenter(_tilePath[k] / w));
            }
            int goalTile = _tilePath[_tilePathLen - 1];
            double goalCx = TileToCenter(goalTile % w);
            double goalCy = TileToCenter(goalTile / w);
            AddCand(goalCx, goalCy);
            if (target.X != goalCx || target.Y != goalCy) AddCand(target.X, target.Y);

            // The last candidate equals target (it is target, or a tile centre numerically equal to it): its disc verdict is
            // known if the direct test evaluated it.
            EnsureCandClear();
            Array.Clear(_candClear, 0, _candLen);
            if (targetClear.HasValue) _candClear[_candLen - 1] = targetClear.Value ? DiscClear : DiscBlocked;

            // String pulling: from each anchor, the farthest j in the window whose segment is clear, scanning down from the
            // far end exactly as legacy segmentClear(anchor, cand[j]) does, with the end discs read from the memo.
            var anchor = new PathPoint(fromX, fromY);
            int anchorJ = -1; // -1: the anchor is the start point; otherwise the candidate index.
            int i = 0;
            while (i < _candLen)
            {
                int j = Math.Min(_candLen - 1, i + PullWindow);
                while (j > i)
                {
                    bool anchorClear;
                    if (anchorJ >= 0)
                    {
                        anchorClear = Disc(anchorJ, foot);
                    }
                    else
                    {
                        if (!fromClear.HasValue) fromClear = FootClear(fromX, fromY, foot);
                        anchorClear = fromClear.Value;
                    }
                    if (anchorClear
                        && Disc(j, foot)
                        && SegmentRaysClear(anchor, CandAt(j), foot))
                    {
                        break;
                    }
                    j--;
                }
                var waypoint = CandAt(j);
                AddOut(waypoint);
                anchor = waypoint;
                anchorJ = j;
                i = j + 1;
            }
        }

        bool Disc(int j, Footprint foot)
        {
            if (_candClear[j] == DiscUnknown)
                _candClear[j] = FootClear(_candX[j], _candY[j], foot) ? DiscClear : DiscBlocked;
            return _candClear[j] == DiscClear;
        }
    }
}
