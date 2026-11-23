namespace Roguehalla.Net.Ui
{
    /// <summary>The shown fraction of a bar and the ghost of lost value behind it, eased toward a target.</summary>
    public struct BarEase
    {
        public const float FallPerSecond = 6f, RisePerSecond = 3f, GhostHoldSeconds = .25f, GhostFallPerSecond = 2f,
            SnapJump = .6f;
        public float Shown, Ghost, GhostHold;
        public bool Has { get; private set; }
        public float Target { get; private set; }
        public void Snap(float target) { }
        public void Step(float target, float dt, bool reduced) { Step(target, dt, reduced, true); }
        public void Step(float target, float dt, bool reduced, bool ghost) { }
        public void Clear() { }
    }
}
