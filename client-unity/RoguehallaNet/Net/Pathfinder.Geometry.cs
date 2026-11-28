using System;
using Roguehalla.Net.Protocol;

namespace Roguehalla.Net
{
    /// <summary>The tile geometry of <see cref="Pathfinder"/>: snapping, clamping, disc and box clearance, and grid ray traversal (rh-sim geometry.rs and map.rs).</summary>
    public sealed partial class Pathfinder
    {
        // ----------------------------------------------------------------------------------------------------------------
        // Snapping, clamping, line of sight (geometry.rs / map.rs)

        static bool Finite(double v) { return !double.IsNaN(v) && !double.IsInfinity(v); }

        static long FloorI64(double v)
        {
            if (double.IsNaN(v)) return 0;
            if (v >= 9223372036854775808.0) return long.MaxValue;
            if (v <= -9223372036854775808.0) return long.MinValue;
            long t = (long)v;
            if ((double)t > v) t--;
            return t;
        }

        static long CeilI64(double v)
        {
            if (double.IsNaN(v)) return 0;
            if (v >= 9223372036854775808.0) return long.MaxValue;
            if (v <= -9223372036854775808.0) return long.MinValue;
            long t = (long)v;
            if ((double)t < v) t++;
            return t;
        }

        static long SatAdd(long a, long b)
        {
            if (b > 0 && a > long.MaxValue - b) return long.MaxValue;
            if (b < 0 && a < long.MinValue - b) return long.MinValue;
            return a + b;
        }

        static long SatSub(long a, long b)
        {
            if (b > 0 && a < long.MinValue + b) return long.MinValue;
            if (b < 0 && a > long.MaxValue + b) return long.MaxValue;
            return a - b;
        }

        // f64::max / f64::min: a NaN argument yields the other one.
        static double Max(double a, double b)
        {
            if (double.IsNaN(a)) return b;
            if (double.IsNaN(b)) return a;
            return a > b ? a : b;
        }

        static double Min(double a, double b)
        {
            if (double.IsNaN(a)) return b;
            if (double.IsNaN(b)) return a;
            return a < b ? a : b;
        }

        static long WorldToTile(double v) { return FloorI64(v / Tile); }

        static double TileToCenter(long t) { return (double)t * Tile + HalfTile; }

        bool IsFloor(long tx, long ty)
        {
            return tx >= 0 && ty >= 0 && tx < _w && ty < _h && _map.Tiles[(int)(ty * _w + tx)] == MapBlob.Floor;
        }

        bool Blocked(long tx, long ty) => Blocked(_map, tx, ty);

        static bool Blocked(MapBlob map, long tx, long ty)
        {
            if (tx < 0 || ty < 0 || tx >= map.W || ty >= map.H) return true;
            return (map.Tiles[(int)(ty * map.W + tx)] & MoveMask) != 0;
        }

        bool SnapToFloor(double x, double y, out long sx, out long sy)
        {
            long cx = WorldToTile(x);
            long cy = WorldToTile(y);
            for (long ring = 1; ring <= SnapRadius; ring++)
            {
                bool found = false;
                long bestX = 0, bestY = 0;
                double bestD = double.PositiveInfinity;
                for (long dy = -ring; dy <= ring; dy++)
                {
                    long rest = ring - Math.Abs(dy);
                    long dx0 = rest == 0 ? 0 : -rest;
                    long dx1 = rest == 0 ? 0 : rest;
                    for (long dx = dx0; dx <= dx1; dx = dx1)
                    {
                        long tx = SatAdd(cx, dx);
                        long ty = SatAdd(cy, dy);
                        if (IsFloor(tx, ty))
                        {
                            double ex = TileToCenter(tx) - x;
                            double ey = TileToCenter(ty) - y;
                            double d = ex * ex + ey * ey;
                            if (d < bestD)
                            {
                                bestD = d;
                                bestX = tx;
                                bestY = ty;
                                found = true;
                            }
                        }
                        if (dx == dx1) break;
                    }
                }
                if (found)
                {
                    sx = bestX;
                    sy = bestY;
                    return true;
                }
            }
            sx = 0;
            sy = 0;
            return false;
        }

        PathPoint ClampClear(PathPoint p, long gx, long gy, Footprint foot)
        {
            if (FootClear(p.X, p.Y, foot)) return p;
            // x by the footprint's east-west half-extent, y by its north-south one (both the radius for a disc).
            double rx = foot.Hx, ry = foot.Hy;
            double x0 = (double)gx * Tile;
            double y0 = (double)gy * Tile;
            double x = p.X;
            double y = p.Y;
            if (Blocked(gx - 1, gy)) x = Max(x, x0 + rx);
            if (Blocked(gx + 1, gy)) x = Min(x, x0 + Tile - rx);
            if (Blocked(gx, gy - 1)) y = Max(y, y0 + ry);
            if (Blocked(gx, gy + 1)) y = Min(y, y0 + Tile - ry);
            var clamped = new PathPoint(x, y);
            if (FootClear(clamped.X, clamped.Y, foot)) return clamped;
            return new PathPoint(TileToCenter(gx), TileToCenter(gy));
        }

        /// <summary><c>Footprint::clear</c>: the legacy disc test, or the foot box test.</summary>
        bool FootClear(double x, double y, Footprint foot)
        {
            return foot.IsBox ? BoxClear(x, y, foot.Hx, foot.Hy) : CircleClear(x, y, foot.Hx);
        }

        /// <summary>
        /// Does the box [x-hx, x+hx] x [y-hy, y+hy] overlap no wall or LOW tile? (box_clear, MASK_MOVE.) A tile counts only when it
        /// overlaps the box's open interior, so touching a wall face is clear; out of bounds is blocked.
        /// </summary>
        bool BoxClear(double px, double py, double hx, double hy)
        {
            if (!Finite(px + py + hx + hy)) return false;
            if (hx <= 0.0 || hy <= 0.0) return !Blocked(FloorI64(px / Tile), FloorI64(py / Tile));
            long i0 = FloorI64((px - hx) / Tile);
            long i1 = CeilI64((px + hx) / Tile) - 1;
            long j0 = FloorI64((py - hy) / Tile);
            long j1 = CeilI64((py + hy) / Tile) - 1;
            for (long j = j0; j <= j1; j++)
                for (long i = i0; i <= i1; i++)
                    if (Blocked(i, j)) return false;
            return true;
        }

        /// <summary>Does a disc of radius at (cx, cy) overlap no wall or LOW tile? (circle_clear, MASK_MOVE)</summary>
        bool CircleClear(double cx, double cy, double radius)
        {
            if (!Finite(cx + cy + radius)) return false;
            long tx = FloorI64(cx / Tile);
            long ty = FloorI64(cy / Tile);
            if (radius <= 0.0) return !Blocked(tx, ty);
            long reach = Math.Max(1L, CeilI64(radius / Tile));
            // A 3x3 block of floor tiles has no blocked tile in reach 1.
            if (reach == 1 && IsFloor(tx, ty) && NavAt(tx, ty) == 0xFF) return true;
            double r2 = radius * radius;
            long j0 = SatSub(ty, reach), j1 = SatAdd(ty, reach);
            long i0 = SatSub(tx, reach), i1 = SatAdd(tx, reach);
            for (long j = j0; ; j++)
            {
                for (long i = i0; ; i++)
                {
                    if (Blocked(i, j))
                    {
                        double nx = Min(Max(cx, (double)i * Tile), (double)SatAdd(i, 1) * Tile);
                        double ny = Min(Max(cy, (double)j * Tile), (double)SatAdd(j, 1) * Tile);
                        double ddx = cx - nx;
                        double ddy = cy - ny;
                        if (ddx * ddx + ddy * ddy < r2) return false;
                    }
                    if (i == i1) break;
                }
                if (j == j1) break;
            }
            return true;
        }

        byte NavAt(long tx, long ty) { return _nav[(int)(ty * _w + tx)]; }

        /// <summary>The ray part of <c>Footprint::segment_clear</c> (both end footprints known clear).</summary>
        bool SegmentRaysClear(PathPoint a, PathPoint b, Footprint foot)
        {
            return foot.IsBox ? SegmentBoxRaysClear(a, b, foot.Hx, foot.Hy) : SegmentRaysClear(a, b, foot.Hx);
        }

        /// <summary>
        /// segment_box_rays_clear: the centre ray, then the two silhouette-corner rays. For motion with dx*dy >= 0 the corners are
        /// (+hx, -hy) and (-hx, +hy), otherwise (+hx, +hy) and (-hx, -hy).
        /// </summary>
        bool SegmentBoxRaysClear(PathPoint a, PathPoint b, double hx, double hy)
        {
            if (RayBlocked(a.X, a.Y, b.X, b.Y)) return false;
            double dx = b.X - a.X;
            double dy = b.Y - a.Y;
            if ((dx == 0.0 && dy == 0.0) || hx <= 0.0 || hy <= 0.0) return true;
            double ox, oy;
            if (dx * dy >= 0.0) { ox = hx; oy = -hy; } else { ox = hx; oy = hy; }
            return !RayBlocked(a.X + ox, a.Y + oy, b.X + ox, b.Y + oy)
                && !RayBlocked(a.X - ox, a.Y - oy, b.X - ox, b.Y - oy);
        }

        /// <summary>The ray part of segment_clear: the centre ray and the side rays (both end discs known clear).</summary>
        bool SegmentRaysClear(PathPoint a, PathPoint b, double radius)
        {
            if (RayBlocked(a.X, a.Y, b.X, b.Y)) return false;
            double dx = b.X - a.X;
            double dy = b.Y - a.Y;
            double len = Math.Sqrt(dx * dx + dy * dy);
            if (len == 0.0 || radius <= 0.0) return true;
            double nx = -dy / len;
            double ny = dx / len;
            long rays = CeilI64(radius / Tile);
            for (long k = 1; k <= rays; k++)
            {
                double off = (radius * (double)k) / (double)rays;
                double ox = nx * off;
                double oy = ny * off;
                if (RayBlocked(a.X + ox, a.Y + oy, b.X + ox, b.Y + oy)) return false;
                if (RayBlocked(a.X - ox, a.Y - oy, b.X - ox, b.Y - oy)) return false;
            }
            return true;
        }

        bool RayBlocked(double x0, double y0, double x1, double y1) => MovementRayBlocked(_map, x0, y0, x1, y1);

        /// <summary>Grid traversal verdict (ray_blocked / ray_blocked_checked): same decisions as rh-sim geometry.rs.</summary>
        public static bool MovementRayBlocked(MapBlob map, double x0, double y0, double x1, double y1)
        {
            if (map == null || !Finite(x0 + y0 + x1 + y1)) return true;
            long tx = FloorI64(x0 / Tile);
            long ty = FloorI64(y0 / Tile);
            if (Blocked(map, tx, ty)) return true;
            double dx = x1 - x0;
            double dy = y1 - y0;
            if (dx * dx + dy * dy == 0.0) return false;
            long stepX = dx > 0.0 ? 1 : dx < 0.0 ? -1 : 0;
            long stepY = dy > 0.0 ? 1 : dy < 0.0 ? -1 : 0;
            double tMaxX = stepX == 0 ? double.PositiveInfinity : (Edge(tx, stepX) - x0) / dx;
            double tMaxY = stepY == 0 ? double.PositiveInfinity : (Edge(ty, stepY) - y0) / dy;
            for (; ; )
            {
                double t = tMaxX < tMaxY ? tMaxX : tMaxY;
                if (t > 1.0) return false;
                bool crossX = tMaxX <= t;
                bool crossY = tMaxY <= t;
                if (crossX && crossY && (Blocked(map, tx + stepX, ty) || Blocked(map, tx, ty + stepY))) return true;
                if (crossX)
                {
                    tx += stepX;
                    tMaxX = (Edge(tx, stepX) - x0) / dx;
                }
                if (crossY)
                {
                    ty += stepY;
                    tMaxY = (Edge(ty, stepY) - y0) / dy;
                }
                if (Blocked(map, tx, ty)) return true;
            }
        }

        static double Edge(long t, long step)
        {
            return (double)(t + (step > 0 ? 1 : 0)) * Tile;
        }
    }
}
