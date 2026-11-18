using System;
using Roguehalla.Net.Protocol;

namespace Roguehalla.Net
{
    /// <summary>The walk model of <see cref="LocalMotion"/>: advancing the predicted position along its path and decaying the correction offset.</summary>
    public sealed partial class LocalMotion
    {
        // ----------------------------------------------------------------------------------------------------------------
        // Walk model

        void Walk(double seconds, double nowMs)
        {
            // Mirrors rh-sim systems/movement.rs advance_along_path: a budget spent waypoint by waypoint, landing exactly on a
            // waypoint when the budget covers it. All arithmetic is double, as on the server.
            double budget = PlayerSpeed * _speedMult * SpeedModifier(nowMs) * MovingSeconds(nowMs - seconds * 1000.0, nowMs, seconds);
            while (budget > 0.0 && _pathIndex < _path.Length)
            {
                PathPoint target = _path[_pathIndex];
                double dx = target.X - _modelX;
                double dy = target.Y - _modelY;
                double dist = Math.Sqrt(dx * dx + dy * dy);
                if (dist <= budget)
                {
                    _modelX = target.X;
                    _modelY = target.Y;
                    budget -= dist;
                    _pathIndex++;
                }
                else
                {
                    _modelX += dx / dist * budget;
                    _modelY += dy / dist * budget;
                    budget = 0.0;
                }
            }
        }

        /// <summary>
        /// The own footprint the walk plans with (ADR0024): the foot box of the self archetype from the Welcome table, or the 12 u
        /// disc (the default) when the server sent no table or the legacy row. It applies to paths planned from now on.
        /// </summary>
        public Footprint Foot { get { return _foot; } set { _foot = value; } }

        void PlanFrom(double fromX, double fromY)
        {
            _walkFromX = fromX;
            _walkFromY = fromY;
            _path = _walkMap == null ? NoPath : _pathfinder.FindPath(_walkMap, fromX, fromY, _goalX, _goalY, _foot);
            _pathIndex = 0;
        }

        void ClearPath()
        {
            _path = NoPath;
            _pathIndex = 0;
        }

        void Decay(double dtMs)
        {
            double mag = Math.Sqrt(_corrX * _corrX + _corrY * _corrY);
            if (mag > 0)
            {
                // Exponential, but never faster than the correction speed cap (legacy decay): a walking fighter is slowed or
                // hurried by a correction, never turned round.
                double cap = Initialized && _alive && !_predictionSuspended && _pathIndex < _path.Length
                    ? Math.Min(CorrectSpeedMovingMax, CorrectSpeedMoving + 2.0 * Math.Max(0.0, mag - 20.0))
                    : CorrectSpeedRestMax;
                double dec = Math.Min(mag * (1.0 - Math.Exp(-dtMs / VisualTauMs)), cap * dtMs / 1000.0);
                double k = (mag - dec) / mag;
                _corrX *= k;
                _corrY *= k;
            }
            if (Math.Abs(_corrX) < 1e-9) _corrX = 0;
            if (Math.Abs(_corrY) < 1e-9) _corrY = 0;
        }

        static double WalkMultiplier(double? speedMilli)
        {
            // rh-sim decodes a missing or nonfinite mirror as 1.0 and floors a positive tick multiplier at MOVE_MULT_FLOOR.
            if (!speedMilli.HasValue || double.IsNaN(speedMilli.Value) || double.IsInfinity(speedMilli.Value) || speedMilli.Value < 0)
                return 1.0;
            double m = speedMilli.Value / 1000.0;
            return m > 0 && m < MoveMultFloor ? MoveMultFloor : m;
        }
    }
}
