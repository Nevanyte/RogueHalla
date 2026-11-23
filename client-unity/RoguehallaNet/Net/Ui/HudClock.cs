namespace Roguehalla.Net.Ui
{
    /// <summary>Server time for HUD countdowns, advanced between snapshots so sweeps and bars move every frame.</summary>
    public sealed class HudClock
    {
        public const double MaxAheadMs = 150;
        bool _ready;
        double _server, _arrival, _last;

        public bool Ready { get { return _ready; } }

        public void Reset() { _ready = false; _server = _arrival = _last = 0; }

        /// <summary>Call when the newest snapshot's server time is seen to change.</summary>
        public void OnServerTime(double serverMs, double localMs)
        {
            if (_ready)
            {
                if (serverMs == _server) return;
                if (serverMs < _server) Reset();
            }
            _ready = true; _server = serverMs; _arrival = localMs;
        }

        /// <summary>Server ms to draw countdowns at; 0 when not Ready.</summary>
        public double Now(double localMs)
        {
            if (!_ready) return 0;
            double ahead = localMs - _arrival;
            if (ahead < 0) ahead = 0; else if (ahead > MaxAheadMs) ahead = MaxAheadMs;
            double v = _server + ahead;
            if (v < _last) v = _last;
            _last = v;
            return v;
        }
    }
}
