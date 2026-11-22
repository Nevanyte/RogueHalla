using System;

namespace Roguehalla.Net
{
    /// <summary>
    /// Maps the local clock to the server time that remote actors are drawn at ("render time"), a delay behind the newest data.
    /// The delay follows snapshot arrival jitter between 55 ms (one 50 ms tick plus 5 ms of margin) and 150 ms. Render time never runs backwards: when more delay is
    /// needed it slows to 80% speed; when less, it catches up at 1% for a gap of 20 ms or less, ramping smoothly to
    /// <see cref="MaxCatchUpShare"/> (4%) at 80 ms, so a spike's extra delay is gone in seconds, not ten; it only jumps forward (after a 250 ms gap).
    /// With <see cref="DrawAhead"/> on, the clock runs the time remote fighters are drawn at (<see cref="DrawMs"/>) with the floor,
    /// start and target delay lowered by <see cref="AheadMs"/> (the ceiling stays), and <see cref="RenderMs"/> is that minus
    /// <see cref="LeadMs"/>: the render time the clock without draw-ahead keeps, stamped on attacks and casts.
    /// </summary>
    public sealed class RenderClock
    {
        public const double MinDelayMs = 55, MaxDelayMs = 150, StartDelayMs = 100, SnapMs = 250;
        /// <summary>How much arrival history the jitter percentile covers, whatever the tick (80 samples at 20 Hz, 160 at 40 Hz).</summary>
        public const double WindowMs = 4000;
        const double SlowShare = 0.2, CatchUpShare = 0.01, RampFromMs = 20, RampSpanMs = 60;

        double[] _d = new double[80];
        double[] _sorted = new double[80];
        int _n, _head;
        double _minD, _targetDelay = StartDelayMs, _lastLocal, _render, _lead;
        bool _init;
        bool _sourceReady;
        double _lastSourceMs;

        /// <summary>Server tick interval; the delay must cover one interval plus the arrival jitter to have a complete segment to draw.</summary>
        public double TickMs = 50;

        /// <summary>The delay floor, <see cref="TickMs"/> + 5 (55 at 50 ms, 30 at 25 ms); <see cref="MinDelayMs"/> is the 50 ms value.</summary>
        public double MinDelay { get { return TickMs + 5; } }

        /// <summary>Catch-up speed-up reached for gaps of 80 ms or more (0.01 reproduces the old constant 1%).</summary>
        public double MaxCatchUpShare = 0.04;

        public bool Ready { get { return _init; } }
        /// <summary>The delay the draw time slews to (with <see cref="DrawAhead"/> on, lowered by <see cref="AheadMs"/>).</summary>
        public double TargetDelayMs { get { return _init ? _targetDelay : StartDelayMs - AheadMs; } }
        /// <summary>The delay of the draw time actually in force right now (lags TargetDelayMs while it slews).</summary>
        public double DelayMs { get { return _init ? (_lastLocal - _minD) - _render : StartDelayMs - AheadMs; } }
        /// <summary>
        /// The render time stamped on attacks and casts: <see cref="DrawMs"/> minus <see cref="LeadMs"/>, which is what the clock
        /// without draw-ahead gives (so the server's rewind is unchanged). Never runs backwards. Without draw-ahead it is DrawMs.
        /// </summary>
        public double RenderMs { get { return _render - _lead; } }

        /// <summary>
        /// Draw-ahead (P-NET-DRAWAHEAD-1b; legacy shared/src/interp.ts RenderClock <c>aheadMs</c>). Off on a bare clock; the
        /// session turns it on. On: the delay floor, start and target drop by <see cref="AheadMs"/>, remote fighters are drawn
        /// at <see cref="DrawMs"/>, and <see cref="RenderMs"/> stays the time stamped on attacks and casts (render time minus
        /// <see cref="LeadMs"/>), so the server's rewind is unchanged.
        /// </summary>
        public bool DrawAhead;

        /// <summary>How far past a fighter's newest sample the draw may run (ms): one tick while <see cref="DrawAhead"/> is on.</summary>
        public double AheadMs { get { return DrawAhead ? TickMs : 0; } }

        /// <summary>
        /// How far <see cref="DrawMs"/> runs ahead of <see cref="RenderMs"/> now: min(<see cref="AheadMs"/>, max(0,
        /// <see cref="MaxDelayMs"/> - <see cref="DelayMs"/>)) as of the last update (legacy client remoteDraw.ts <c>attackTime</c>), less
        /// whatever keeps the stamp from running backwards. 0 without draw-ahead.
        /// </summary>
        public double LeadMs { get { return _lead; } }

        /// <summary>The server time remote fighters, projectiles and fx are drawn at (equal to <see cref="RenderMs"/> without draw-ahead).</summary>
        public double DrawMs { get { return _render; } }

        public void Reset() { _n = 0; _head = 0; _init = false; _targetDelay = StartDelayMs; _lead = 0; _sourceReady = false; }

        public void OnSnapshot(double localMs, double serverMs)
        {
            // A new source-clock epoch is explicit authoritative data, not network jitter. Normal default-clock
            // frames retain the existing monotonic/slewing behavior; a cut must realign deadlines immediately.
            if (_sourceReady && serverMs <= _lastSourceMs) Reset();
            _lastSourceMs = serverMs;
            _sourceReady = true;
            int window = Math.Max(8, (int)Math.Round(WindowMs / TickMs));
            if (_d.Length != window)
            {
                // Only when the tick changed (first snapshot after Reset sets TickMs): never per snapshot in steady play.
                _d = new double[window];
                _sorted = new double[window];
                _n = 0; _head = 0;
            }
            _d[_head] = localMs - serverMs;
            _head = (_head + 1) % window;
            if (_n < window) _n++;
            Array.Copy(_d, _sorted, _n);
            Array.Sort(_sorted, 0, _n);
            _minD = _sorted[0];
            // 98th percentile of how much later than the fastest arrival a snapshot came.
            double excess = _sorted[Math.Min(_n - 1, (int)Math.Ceiling(_n * 0.98) - 1)] - _minD;
            double ahead = AheadMs;
            _targetDelay = Math.Min(MaxDelayMs, Math.Max(MinDelay - ahead, TickMs + excess - ahead));
            if (!_init)
            {
                _init = true;
                _lastLocal = localMs;
                _render = serverMs - (StartDelayMs - ahead);
                _lead = WantedLead();
            }
        }

        /// <summary>The lead the stamp wants under the draw time at the current delay (0 without draw-ahead).</summary>
        double WantedLead()
        {
            double ahead = AheadMs;
            return ahead > 0 ? Math.Min(ahead, Math.Max(0, MaxDelayMs - DelayMs)) : 0;
        }

        /// <summary>Advances render time to local time <paramref name="localMs"/> (call once per frame) and returns it in server ms.</summary>
        public double Update(double localMs)
        {
            if (!_init) return 0;
            double dt = Math.Max(0, localMs - _lastLocal);
            _lastLocal = localMs;
            double wanted = localMs - _minD - _targetDelay;
            double diff = wanted - (_render + dt);
            double adv;
            if (diff < 0) adv = dt + Math.Max(diff, -SlowShare * dt);
            else if (diff >= SnapMs) adv = dt + diff;
            else
            {
                double ramp = Math.Min(1, Math.Max(0, (diff - RampFromMs) / RampSpanMs));
                double share = CatchUpShare + ramp * Math.Max(0, MaxCatchUpShare - CatchUpShare);
                adv = dt + Math.Min(diff, share * dt);
            }
            double stamp = _render - _lead;
            _render += adv;
            // The stamp is the draw time less the lead, and never runs backwards (a lead that grows faster than the draw time
            // advances, near the ceiling, is held back): 0 <= lead <= AheadMs always.
            _lead = Math.Max(0, Math.Min(WantedLead(), _render - stamp));
            return _render;
        }
    }
}
