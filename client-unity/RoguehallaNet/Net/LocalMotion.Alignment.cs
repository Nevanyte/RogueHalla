using System;
using Roguehalla.Net.Protocol;

namespace Roguehalla.Net
{
    /// <summary>The delay alignment of <see cref="LocalMotion"/>: sample stamps, unapplied orders, and the pose history that server samples are compared with.</summary>
    public sealed partial class LocalMotion
    {
        // ----------------------------------------------------------------------------------------------------------------
        // Delay alignment

        /// <summary>
        /// The local time a sample stands for: the previous sample's time plus the source clock's advance, never later than its
        /// arrival. Arrival jitter therefore does not read as walk error; without a source time (or after a cut) it is the arrival.
        /// </summary>
        double SampleStamp(double nowMs, double? serverMs)
        {
            double stamp = nowMs;
            bool usable = serverMs.HasValue && !double.IsNaN(serverMs.Value) && !double.IsInfinity(serverMs.Value);
            // A replacement snapshot of the same tick stands for the same instant: keep the anchor.
            if (usable && _hasStamp && serverMs.Value == _stampServerMs) return _stampMs;
            if (usable && _hasStamp && serverMs.Value > _stampServerMs)
            {
                double projected = _stampMs + (serverMs.Value - _stampServerMs);
                // The anchor only ever moves earlier, so a server that fell behind wall time (dropped steps) would leave
                // every later stamp early. Bound how far a stamp may trail its arrival; past that, re-anchor.
                if (projected < nowMs && nowMs - projected <= MaxStampLagMs) stamp = projected;
            }
            _hasStamp = usable;
            _stampMs = stamp;
            _stampServerMs = usable ? serverMs.Value : 0;
            return stamp;
        }

        void NoteOrder(byte seq, double nowMs, bool timing, WireCommand command)
        {
            if (_ordCount == OrderRingSize) { _ordStart = (_ordStart + 1) % OrderRingSize; _ordCount--; }
            int i = (_ordStart + _ordCount) % OrderRingSize;
            _ordSeq[i] = seq;
            _ordAt[i] = nowMs;
            _ordTiming[i] = timing;
            _ordCommand[i] = command;
            _ordFrame[i] = 0;
            _ordSent[i] = false;
            _ordCount++;
        }

        /// <summary>
        /// An echo of order <paramref name="seq"/> covers it and every earlier order (the server applies them in order). The first
        /// echo of a timing order measures the delay samples are aligned with.
        /// </summary>
        void NoteEcho(byte seq, double stamp, uint? ackInput)
        {
            if (seq == 0) return;
            for (int n = _ordCount - 1; n >= 0; n--)
            {
                int i = (_ordStart + n) % OrderRingSize;
                if (_ordSeq[i] != seq) continue;
                // The reused byte alone is not an input receipt. Do not change alignment or
                // retire ANY covered order until this exact command was sent and its frame
                // was acknowledged. Direct method callers without AckInput retain that API.
                if (ackInput.HasValue && (!_ordSent[i] ||
                    unchecked((int)(ackInput.Value - _ordFrame[i])) < 0)) return;
                if (_ordTiming[i])
                    _align = Math.Max(DelayMinMs, Math.Min(DelayMaxMs, stamp - StepMs - _ordAt[i]));
                _ordStart = (i + 1) % OrderRingSize;
                _ordCount -= n + 1;
                return;
            }
        }

        void RecordPose()
        {
            int i;
            if (_histCount == HistorySize) { i = _histStart; _histStart = (_histStart + 1) % HistorySize; }
            else i = (_histStart + _histCount++) % HistorySize;
            _histT[i] = _lastStepMs;
            _histX[i] = _modelX;
            _histY[i] = _modelY;
        }

        /// <summary>Where the model was at local time <paramref name="t"/>: from the pose history, or carried on along the path.</summary>
        void ModelAt(double t, out double x, out double y)
        {
            x = _modelX;
            y = _modelY;
            if (t >= _lastStepMs)
            {
                // A negative delay (a click just before a tick on a fast link) asks slightly ahead of the model.
                if (_alive && !_predictionSuspended) WalkAhead(Math.Min(t - _lastStepMs, MaxStepMs) / 1000.0, ref x, ref y);
                return;
            }
            double bt = _lastStepMs, bx = _modelX, by = _modelY;
            for (int n = _histCount - 1; n >= 0; n--)
            {
                int i = (_histStart + n) % HistorySize;
                if (_histT[i] <= t)
                {
                    double k = bt > _histT[i] ? (t - _histT[i]) / (bt - _histT[i]) : 1.0;
                    x = _histX[i] + (bx - _histX[i]) * k;
                    y = _histY[i] + (by - _histY[i]) * k;
                    return;
                }
                bt = _histT[i]; bx = _histX[i]; by = _histY[i];
            }
            x = bx; // older than the history: its oldest pose
            y = by;
        }

        /// <summary>The <see cref="Walk"/> arithmetic on copies: how far the path carries the model in <paramref name="seconds"/>.</summary>
        void WalkAhead(double seconds, ref double x, ref double y)
        {
            double budget = PlayerSpeed * _speedMult * SpeedModifier(_lastStepMs) * MovingSeconds(_lastStepMs, _lastStepMs + seconds * 1000.0, seconds);
            for (int i = _pathIndex; budget > 0.0 && i < _path.Length; i++)
            {
                double dx = _path[i].X - x, dy = _path[i].Y - y;
                double dist = Math.Sqrt(dx * dx + dy * dy);
                if (dist <= budget) { x = _path[i].X; y = _path[i].Y; budget -= dist; }
                else { x += dx / dist * budget; y += dy / dist * budget; budget = 0.0; }
            }
        }

        void ShiftHistory(double dx, double dy)
        {
            for (int n = 0; n < _histCount; n++)
            {
                int i = (_histStart + n) % HistorySize;
                _histX[i] += dx;
                _histY[i] += dy;
            }
        }

        void ExpireUnappliedOrders(double nowMs)
        {
            while (_ordCount > 0 && nowMs - _ordAt[_ordStart] >= OrderExpireMs)
            {
                _ordStart = (_ordStart + 1) % OrderRingSize;
                _ordCount--;
            }
        }
    }
}
