using System;
using Roguehalla.Net.Protocol;

namespace Roguehalla.Net
{
    /// <summary>The reconciliation of <see cref="LocalMotion"/>: comparing a server sample with the aligned model pose, snapping or smoothing, and expiring orders.</summary>
    public sealed partial class LocalMotion
    {
        // ----------------------------------------------------------------------------------------------------------------
        // Reconciliation

        void Reconcile(double stamp, double nowMs)
        {
            ExpireUnappliedOrders(nowMs);
            double at = stamp - _align;
            double refX, refY;
            ModelAt(at, out refX, out refY);
            // ex/ey: how far the model was from the server at the aligned time (model - server).
            double ex = refX - _simX;
            double ey = refY - _simY;
            double err = Math.Sqrt(ex * ex + ey * ey);
            if (_ordCount > 0 && at >= _ordAt[_ordStart])
            {
                // The model at `at` already follows an order the server has not applied: the sample only bounds the lead.
                double lead = PlayerSpeed * _speedMult * SpeedModifier(nowMs) * (at - _ordAt[_ordStart]) / 1000.0;
                if (err > SnapDistance + lead) SnapToSim();
                return;
            }
            ReconSamples++;
            ReconErrSum += err;
            if (err > ReconMaxErr) ReconMaxErr = err;
            if (err > ReconCorrectionU) ReconCorrections++;
            if (err > ReconBigU) ReconBig++;
            double drawEx = ex + _corrX;
            double drawEy = ey + _corrY;
            // Multiple accepted samples can arrive in one Poll with no intervening decay. Bound the composed correction,
            // not just each sample's model error, so smoothing cannot conceal an arbitrarily large divergence.
            if (err > SnapDistance || Math.Sqrt(drawEx * drawEx + drawEy * drawEy) > SnapDistance)
            {
                SnapToSim();
                return;
            }
            if (err > DivergeErr)
            {
                if (ConsecutiveOutOfToleranceSamples < int.MaxValue) ConsecutiveOutOfToleranceSamples++;
            }
            else
            {
                ConsecutiveOutOfToleranceSamples = 0;
            }
            // Move the model (and the poses it is compared from) by the aligned error and keep the drawn position where it was
            // by moving the offset the other way; the offset then fades (the sample is not a teleport, so it is not shown as one).
            _modelX -= ex;
            _modelY -= ey;
            ShiftHistory(-ex, -ey);
            _corrX += ex;
            _corrY += ey;
            AdvanceConsumedWaypoints();
        }

        /// <summary>A sample past <see cref="SnapDistance"/>: show the server's position now and plan the remaining walk from it.</summary>
        void SnapToSim()
        {
            _modelX = _simX;
            _modelY = _simY;
            _corrX = 0;
            _corrY = 0;
            ConsecutiveOutOfToleranceSamples = 0;
            _histCount = 0;
            if (Snaps < int.MaxValue) Snaps++;
            if (_pathIndex < _path.Length) PlanFrom(_simX, _simY);
            else
            {
                // The walk was already finished (or was never planned): there is nothing left to replan.
                ClearPath();
                _goalHeld = false;
            }
        }

        /// <summary>
        /// After the model is rebased onto the server, skip waypoints the corrected position has already passed, so the next
        /// step cannot walk backwards to a waypoint behind it. Only forward progress is inferred; a correction that moves the
        /// model backwards keeps the current leg and the next step simply rejoins it.
        /// </summary>
        void AdvanceConsumedWaypoints()
        {
            while (_pathIndex < _path.Length)
            {
                PathPoint wp = _path[_pathIndex];
                double ax = _pathIndex == 0 ? _walkFromX : _path[_pathIndex - 1].X;
                double ay = _pathIndex == 0 ? _walkFromY : _path[_pathIndex - 1].Y;
                double dx = wp.X - ax;
                double dy = wp.Y - ay;
                double px = _modelX - wp.X;
                double py = _modelY - wp.Y;
                if (dx * dx + dy * dy > ZeroSegmentSq && px * dx + py * dy < 0) return;
                _pathIndex++;
            }
        }

        void ExpireOrders(double nowMs)
        {
            if (!_pending) return;
            if (nowMs - _orderAtMs < OrderExpireMs) return;
            // Forgotten: the pending gate clears so the next sample reconciles; the walk itself keeps its path (stopping it
            // here would freeze the player if the server did apply the order and only the echo was lost).
            _pending = false;
            ClearNonMovementAcks();
            if (ExpiredOrders < int.MaxValue) ExpiredOrders++;
        }

        void MarkStepClock(double nowMs)
        {
            if (!_stepClock || nowMs > _lastStepMs)
            {
                _lastStepMs = nowMs;
                _stepClock = true;
            }
        }
    }
}
