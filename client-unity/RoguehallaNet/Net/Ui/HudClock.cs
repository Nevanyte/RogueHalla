namespace Roguehalla.Net.Ui
{
    /// <summary>Server time for HUD countdowns, advanced between snapshots so sweeps and bars move every frame.</summary>
    public sealed class HudClock
    {
        public const double MaxAheadMs = 150;
        public bool Ready { get { return false; } }
        public void Reset() { }
        public void OnServerTime(double serverMs, double localMs) { }
        public double Now(double localMs) { return 0; }
    }
}
